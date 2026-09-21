using System;
using System.Security.Cryptography;
using System.Text;

namespace CasinoLedger;

public readonly record struct RoundReport(Round round, Totals lifetime, Totals day);

/// Entry point for other mods. Subscribe to round_settled or query totals by player_key.
/// Totals cover the loaded save only. Everything runs on the Unity main thread. Remote players' totals are what this peer has observed.
public static class CasinoStats
{
    internal static readonly Ledger ledger = new();

    public static event Action<RoundReport>? round_settled;
    internal static Action<Exception>? subscriber_failed;

    public static string player_key(string player_code)
    {
        if (string.IsNullOrEmpty(player_code) || player_code.Length > 128) throw new ArgumentOutOfRangeException(nameof(player_code));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(player_code)));
    }

    internal static string world_key(int seed, string? organisation_name)
    {
        string organisation = organisation_name ?? "";
        if (organisation.Length > 128) throw new ArgumentOutOfRangeException(nameof(organisation_name));
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}|{organisation}")))[..16];
        if (key.Length != 16) throw new InvalidOperationException("World key has the wrong length.");
        return key;
    }

    public static Totals lifetime(string player_key, CasinoGame? game = null) => ledger.lifetime(player_key, game);
    public static Totals day(string player_key, CasinoGame? game = null) => ledger.day(player_key, game);
    public static string format_money(double value, bool signed) => Ledger.format_money(value, signed);

    internal static void record(Round round)
    {
        ledger.settle(round);
        var report = new RoundReport(round, ledger.lifetime(round.player_key), ledger.day(round.player_key));
        if (round_settled == null) return;
        foreach (Delegate subscriber in round_settled.GetInvocationList())
        {
            try { ((Action<RoundReport>)subscriber)(report); }
            catch (Exception error) { subscriber_failed?.Invoke(error); }
        }
    }
}
