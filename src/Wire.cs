using System;
using System.Globalization;

namespace CasinoLedger;

internal enum WireKind { None, Round, Hello, Total }

/// Text messages sent over the Steam lobby chat. Every peer reports its own rounds, batched; the host answers a hello with
/// one total message per ledger row so late joiners and drifted peers converge on the host's numbers.
internal static class Wire
{
    public const string prefix = "CasinoLedger|2|";
    public const int length_max = 2048;
    public const int rounds_per_message_max = 12;
    public const string hello = prefix + "hello";

    public static string rounds(ReadOnlySpan<Round> batch)
    {
        if (batch.Length == 0 || batch.Length > rounds_per_message_max) throw new ArgumentOutOfRangeException(nameof(batch));
        var parts = new string[batch.Length];
        for (int i = 0; i < batch.Length; i++)
        {
            Round round = batch[i];
            parts[i] = string.Join('|', round.player_key, ((int)round.game).ToString(CultureInfo.InvariantCulture),
                round.stake.ToString("R", CultureInfo.InvariantCulture), round.returned.ToString("R", CultureInfo.InvariantCulture),
                Ledger.safe_name(round.player_name));
        }
        string message = prefix + "round|" + string.Join(';', parts);
        if (message.Length > length_max) throw new InvalidOperationException("Round batch exceeds the message limit.");
        return message;
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

    public static int parse_rounds(string message, Span<Round> target)
    {
        string[] parts = message[(prefix.Length + "round|".Length)..].Split(';');
        if (parts.Length > rounds_per_message_max || parts.Length > target.Length) throw new FormatException("Round message has too many rounds.");
        for (int i = 0; i < parts.Length; i++)
        {
            string[] fields = parts[i].Split('|');
            if (fields.Length != 5) throw new FormatException("Round message has the wrong shape.");
            if (fields[0].Length != 64) throw new FormatException("Round message has a bad player key.");
            int game = int.Parse(fields[1], CultureInfo.InvariantCulture);
            if (game < 0 || game >= Ledger.game_count) throw new FormatException("Round message names an unknown game.");
            double stake = double.Parse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture);
            double returned = double.Parse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture);
            target[i] = new Round(fields[0], Ledger.safe_name(fields[4]), (CasinoGame)game, stake, returned);
        }
        return parts.Length;
    }

    public static string parse_total(string message)
    {
        string row = message[(prefix.Length + "total|".Length)..];
        if (row.Length == 0 || row.Contains('\n')) throw new FormatException("Total message has the wrong shape.");
        return row;
    }
}
