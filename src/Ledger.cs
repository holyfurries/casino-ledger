using System;
using System.Globalization;
using System.Text;

namespace CasinoLedger;

public enum CasinoGame : byte { Blackjack, RideTheBus, Slots }

public readonly record struct Round(string player_key, string player_name, CasinoGame game, double stake, double returned)
{
    public double net => Math.Round(returned - stake, 2);
}

public readonly record struct Totals(int round_count, int win_count, int loss_count, double wagered, double returned,
    double win_largest, double loss_largest)
{
    public double net => Math.Round(returned - wagered, 2);
    public int push_count => round_count - win_count - loss_count;

    public Totals add(Totals other) => new(round_count + other.round_count, win_count + other.win_count,
        loss_count + other.loss_count, wagered + other.wagered, returned + other.returned,
        Math.Max(win_largest, other.win_largest), Math.Max(loss_largest, other.loss_largest));
}

public readonly record struct SeriesPoint(int day_round_index, double day_net);

public sealed class Ledger
{
    public const int player_count_max = 32;
    public const int series_point_count_max = 64;
    public const int game_count = 3;
    public const double stake_max = 1_000_000;
    public const double returned_max = 100_000_000;
    public const double total_max = 1_000_000_000_000;
    private const string header = "casino-ledger 1";

    private sealed class PlayerRecord
    {
        public string key = "";
        public string name = "";
        public readonly Totals[] lifetime = new Totals[game_count];
        public readonly Totals[] day = new Totals[game_count];
        public readonly SeriesPoint[] series = new SeriesPoint[series_point_count_max];
        public int series_point_count;
    }

    private readonly PlayerRecord[] players = new PlayerRecord[player_count_max];
    private int player_count;
    public int day_round_count { get; private set; }

    public Ledger()
    {
        for (int i = 0; i < players.Length; i++) players[i] = new PlayerRecord();
    }

    public void settle(Round round)
    {
        if (string.IsNullOrEmpty(round.player_key) || round.player_key.Length > 64) throw new ArgumentOutOfRangeException(nameof(round), "player_key");
        if ((int)round.game >= game_count) throw new ArgumentOutOfRangeException(nameof(round), "game");
        if (!double.IsFinite(round.stake) || round.stake <= 0 || round.stake > stake_max) throw new ArgumentOutOfRangeException(nameof(round), "stake");
        if (!double.IsFinite(round.returned) || round.returned < 0 || round.returned > returned_max) throw new ArgumentOutOfRangeException(nameof(round), "returned");
        PlayerRecord player = find(round.player_key) ?? claim(round.player_key);
        double net = round.net;
        var change = new Totals(1, net > 0 ? 1 : 0, net < 0 ? 1 : 0, round.stake, round.returned, Math.Max(net, 0), Math.Max(-net, 0));
        Totals lifetime = player.lifetime[(int)round.game].add(change);
        if (lifetime.wagered > total_max || lifetime.returned > total_max) throw new OverflowException("Casino totals exceed the tracked maximum.");
        player.lifetime[(int)round.game] = lifetime;
        player.day[(int)round.game] = player.day[(int)round.game].add(change);
        player.name = round.player_name ?? "";
        day_round_count++;
        if (player.series_point_count == series_point_count_max)
        {
            for (int i = 0; i < series_point_count_max / 2; i++) player.series[i] = player.series[i * 2 + 1];
            player.series_point_count = series_point_count_max / 2;
        }
        player.series[player.series_point_count++] = new SeriesPoint(day_round_count, sum(player.day).net);
        assert(player.series_point_count <= series_point_count_max, "series bounded");
        assert(sum(player.lifetime).round_count >= sum(player.day).round_count, "day is a subset of lifetime");
    }

    public void start_day()
    {
        for (int i = 0; i < player_count; i++)
        {
            Array.Clear(players[i].day, 0, game_count);
            players[i].series_point_count = 0;
        }
        day_round_count = 0;
    }

    public void clear()
    {
        player_count = 0;
        day_round_count = 0;
    }

    public Totals lifetime(string player_key, CasinoGame? game = null) => pick(find(player_key)?.lifetime, game);
    public Totals day(string player_key, CasinoGame? game = null) => pick(find(player_key)?.day, game);
    public string name(string player_key) => find(player_key)?.name ?? "";

    public int day_series(string player_key, Span<SeriesPoint> target)
    {
        PlayerRecord? player = find(player_key);
        if (player == null) return 0;
        int count = Math.Min(player.series_point_count, target.Length);
        player.series.AsSpan(0, count).CopyTo(target);
        return count;
    }

    public int day_player_keys(Span<string> target)
    {
        int count = 0;
        for (int i = 0; i < player_count && count < target.Length; i++)
        {
            if (players[i].series_point_count > 0) target[count++] = players[i].key;
        }
        return count;
    }

    public string serialize()
    {
        var text = new StringBuilder(header).Append('\n');
        var rows = new string[player_count_max * game_count];
        int row_count = lifetime_rows(rows);
        for (int i = 0; i < row_count; i++) text.Append(rows[i]).Append('\n');
        return text.ToString();
    }

    public int lifetime_rows(Span<string> target)
    {
        int count = 0;
        for (int i = 0; i < player_count; i++)
        {
            for (int game = 0; game < game_count && count < target.Length; game++)
            {
                Totals totals = players[i].lifetime[game];
                if (totals.round_count == 0 && totals.wagered == 0 && totals.returned == 0) continue;
                target[count++] = string.Join(' ', players[i].key, game.ToString(CultureInfo.InvariantCulture),
                    totals.round_count.ToString(CultureInfo.InvariantCulture), totals.win_count.ToString(CultureInfo.InvariantCulture),
                    totals.loss_count.ToString(CultureInfo.InvariantCulture), number(totals.wagered), number(totals.returned),
                    number(totals.win_largest), number(totals.loss_largest));
            }
        }
        return count;
    }

    public void load(string text)
    {
        if (text == null || text.Length > 64 * 1024) throw new FormatException("Casino ledger exceeds size limit.");
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != header) throw new FormatException("Unknown casino ledger header.");
        if (lines.Length > 1 + player_count_max * game_count) throw new FormatException("Casino ledger has too many rows.");
        clear();
        for (int i = 1; i < lines.Length; i++) replace_lifetime_row(lines[i]);
    }

    /// Returns false when the row is older than the rounds already counted today, which would break day <= lifetime.
    public bool replace_lifetime_row(string row)
    {
        string[] fields = row.Split(' ');
        if (fields.Length != 9) throw new FormatException("Casino ledger row has the wrong field count.");
        int game = int.Parse(fields[1], CultureInfo.InvariantCulture);
        var totals = new Totals(int.Parse(fields[2], CultureInfo.InvariantCulture), int.Parse(fields[3], CultureInfo.InvariantCulture),
            int.Parse(fields[4], CultureInfo.InvariantCulture), parse(fields[5]), parse(fields[6]), parse(fields[7]), parse(fields[8]));
        if (game < 0 || game >= game_count) throw new FormatException("Casino ledger row names an unknown game.");
        if (totals.round_count < 0 || totals.win_count < 0 || totals.loss_count < 0 || totals.push_count < 0) throw new FormatException("Casino ledger row has impossible counts.");
        if (fields[0].Length == 0 || fields[0].Length > 64) throw new FormatException("Casino ledger row has a bad player key.");
        PlayerRecord player = find(fields[0]) ?? claim(fields[0]);
        if (totals.round_count < player.day[game].round_count) return false;
        player.lifetime[game] = totals;
        return true;
    }

    public static string format_money(double value, bool signed)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        double magnitude = Math.Abs(value);
        string sign = !signed ? (value < 0 ? "-" : "") : value < -0.005 ? "-" : "+";
        (double divisor, string suffix)[] units = { (1, ""), (1_000, "K"), (1_000_000, "M"), (1_000_000_000, "B") };
        foreach ((double divisor, string suffix) in units)
        {
            double scaled = Math.Round(magnitude / divisor, divisor == 1 ? 0 : 2);
            if (scaled >= 1000 && suffix != "B") continue;
            return $"{sign}${scaled.ToString("0.##", CultureInfo.InvariantCulture)}{suffix}";
        }
        throw new InvalidOperationException("Unreachable: the last unit accepts every magnitude.");
    }

    public static string safe_name(string? name)
    {
        const int length_max = 24;
        var text = new StringBuilder(length_max);
        foreach (char character in name ?? "")
        {
            if (text.Length == length_max) break;
            bool allowed = char.IsLetterOrDigit(character) || character == ' ' || character == '_' || character == '-' || character == '.';
            if (allowed) text.Append(character);
        }
        string result = text.ToString().Trim();
        return result.Length == 0 ? "Player" : result;
    }

    private static Totals pick(Totals[]? totals, CasinoGame? game)
    {
        if (totals == null) return default;
        if (game == null) return sum(totals);
        if ((int)game.Value >= game_count) throw new ArgumentOutOfRangeException(nameof(game));
        return totals[(int)game.Value];
    }

    private static Totals sum(Totals[] totals)
    {
        Totals result = default;
        foreach (Totals each in totals) result = result.add(each);
        return result;
    }

    private PlayerRecord? find(string player_key)
    {
        for (int i = 0; i < player_count; i++)
        {
            if (players[i].key == player_key) return players[i];
        }
        return null;
    }

    private PlayerRecord claim(string player_key)
    {
        PlayerRecord player;
        if (player_count < player_count_max)
        {
            player = players[player_count++];
        }
        else
        {
            player = players[0];
            foreach (PlayerRecord candidate in players)
            {
                bool idle = candidate.series_point_count == 0;
                bool smaller = sum(candidate.lifetime).round_count < sum(player.lifetime).round_count;
                if (idle && (smaller || player.series_point_count > 0)) player = candidate;
            }
            if (player.series_point_count > 0) throw new InvalidOperationException("Every tracked casino player is active today.");
        }
        player.key = player_key;
        player.name = "";
        Array.Clear(player.lifetime, 0, game_count);
        Array.Clear(player.day, 0, game_count);
        player.series_point_count = 0;
        return player;
    }

    private static string number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static double parse(string field)
    {
        double value = double.Parse(field, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(value) || value < 0 || value > total_max) throw new FormatException("Casino ledger amount out of range.");
        return value;
    }

    private static void assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Casino Ledger assertion failed: {message}");
    }
}
