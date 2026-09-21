using System;
using Il2CppFishNet;
using Il2CppScheduleOne.Networking;
using MelonLoader;

namespace CasinoLedger;

/// Carries Wire messages over the game's Steam lobby chat, which reaches every player in the session.
internal static class Sync
{
    private const float flush_interval_seconds = 1f;
    private static readonly Round[] outbox = new Round[64];
    private static readonly Round[] inbox = new Round[Wire.rounds_per_message_max];
    private static int outbox_count;
    private static float next_flush_seconds;
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
        if (service_in_lobby() == null) return;
        if (outbox_count == outbox.Length) flush_batch();
        outbox[outbox_count++] = round;
    }

    public static void flush(float now_seconds)
    {
        if (outbox_count == 0 || now_seconds < next_flush_seconds) return;
        next_flush_seconds = now_seconds + flush_interval_seconds;
        while (outbox_count > 0) flush_batch();
    }

    private static void flush_batch()
    {
        int batch_count = Math.Min(outbox_count, Wire.rounds_per_message_max);
        string message = Wire.rounds(outbox.AsSpan(0, batch_count));
        Array.Copy(outbox, batch_count, outbox, 0, outbox_count - batch_count);
        outbox_count -= batch_count;
        if (service_in_lobby() != null) Lobby.Instance.SendLobbyMessage(message);
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
                    int round_count = Wire.parse_rounds(message, inbox);
                    for (int i = 0; i < round_count; i++) round_received?.Invoke(inbox[i]);
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
