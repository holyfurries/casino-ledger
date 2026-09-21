using System;
using System.Globalization;

namespace CasinoLedger;

internal enum WireKind { None, Round, Hello, Total }

/// Text messages sent over the Steam lobby chat. Every peer reports its own rounds; the host answers a hello with
/// one total message per ledger row so late joiners and drifted peers converge on the host's numbers.
internal static class Wire
{
    public const string prefix = "CasinoLedger|2|";
    public const int length_max = 512;
    public const string hello = prefix + "hello";

    public static string round(Round round)
    {
        return prefix + string.Join('|', "round", round.player_key, ((int)round.game).ToString(CultureInfo.InvariantCulture),
            round.stake.ToString("R", CultureInfo.InvariantCulture), round.returned.ToString("R", CultureInfo.InvariantCulture),
            Ledger.safe_name(round.player_name));
    }

    public static string total(string ledger_row) => prefix + "total|" + ledger_row;

    public static WireKind kind(string? message)
    {
        if (message == null || message.Length > length_max || !message.StartsWith(prefix, StringComparison.Ordinal)) return WireKind.None;
        string body = message[prefix.Length..];
        if (body == "hello") return WireKind.Hello;
        if (body.StartsWith("round|", StringComparison.Ordinal)) return WireKind.Round;
        if (body.StartsWith("total|", StringComparison.Ordinal)) return WireKind.Total;
        return WireKind.None;
    }

    public static Round parse_round(string message)
    {
        string[] fields = message[prefix.Length..].Split('|');
        if (fields.Length != 6 || fields[0] != "round") throw new FormatException("Round message has the wrong shape.");
        if (fields[1].Length != 64) throw new FormatException("Round message has a bad player key.");
        int game = int.Parse(fields[2], CultureInfo.InvariantCulture);
        if (game < 0 || game >= Ledger.game_count) throw new FormatException("Round message names an unknown game.");
        double stake = double.Parse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture);
        double returned = double.Parse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture);
        return new Round(fields[1], Ledger.safe_name(fields[5]), (CasinoGame)game, stake, returned);
    }

    public static string parse_total(string message)
    {
        string row = message[(prefix.Length + "total|".Length)..];
        if (row.Length == 0 || row.Contains('\n')) throw new FormatException("Total message has the wrong shape.");
        return row;
    }
}
