using System;
using Il2CppFishNet;
using Il2CppScheduleOne.Networking;
using MelonLoader;

namespace CasinoLedger;

/// Carries Wire messages over the game's Steam lobby chat, which reaches every player in the session.
internal static class Sync
{
    private static IntPtr subscribed_service;
    private static Il2CppSystem.Action<string>? listener;
    public static Action<Round>? round_received;
    public static Action? totals_changed;

    public static void connect()
    {
        SteamLobbyService? service = service_in_lobby();
        if (service == null) return;
        if (service.Pointer != subscribed_service)
        {
            listener ??= new Action<string>(receive);
            service.add_OnLobbyMessage(listener);
            subscribed_service = service.Pointer;
        }
        if (!InstanceFinder.IsServer) Lobby.Instance.SendLobbyMessage(Wire.hello);
    }

    public static void send_round(Round round)
    {
        if (service_in_lobby() != null) Lobby.Instance.SendLobbyMessage(Wire.round(round));
    }

    public static void send_totals()
    {
        if (!InstanceFinder.IsServer || service_in_lobby() == null) return;
        var rows = new string[Ledger.player_count_max * Ledger.game_count];
        int row_count = CasinoStats.ledger.lifetime_rows(rows);
        for (int i = 0; i < row_count; i++) Lobby.Instance.SendLobbyMessage(Wire.total(rows[i]));
    }

    private static SteamLobbyService? service_in_lobby()
    {
        if (!Lobby.InstanceExists || !Lobby.Instance.IsInLobby || Lobby.Instance._lobbyService == null) return null;
        return Lobby.Instance._lobbyService.TryCast<SteamLobbyService>();
    }

    private static void receive(string message)
    {
        try
        {
            switch (Wire.kind(message))
            {
                case WireKind.Round:
                    round_received?.Invoke(Wire.parse_round(message));
                    break;
                case WireKind.Hello:
                    send_totals();
                    break;
                case WireKind.Total:
                    if (InstanceFinder.IsServer) break;
                    if (CasinoStats.ledger.replace_lifetime_row(Wire.parse_total(message))) totals_changed?.Invoke();
                    break;
            }
        }
        catch (Exception error) when (error is FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            MelonLogger.Warning($"Casino Ledger: Ignored a malformed lobby message: {error.Message}");
        }
    }
}
