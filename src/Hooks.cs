using System;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Casino;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace CasinoLedger;

internal static class Hooks
{
    private const string round_key_prefix = "CasinoLedger|";
    private const float spin_settle_seconds_max = 8f;
    private sealed class Table
    {
        public IntPtr pointer;
        public bool armed;
    }
    private struct Spin
    {
        public IntPtr machine;
        public Round round;
        public float deadline_seconds;
    }
    private readonly record struct Settlement(Table? table, CasinoGameController? game, float stake, float cash_before);
    private static readonly Table[] tables = new Table[32];
    private static readonly Spin[] spins = new Spin[32];
    private static string ledger_path = "";
    private static bool failed;

    public static void install(HarmonyLib.Harmony harmony)
    {
        for (int i = 0; i < tables.Length; i++) tables[i] = new Table();
        foreach (Type type in new[] { typeof(BlackjackGameController), typeof(RTBGameController) })
        {
            harmony.Patch(AccessTools.Method(type, "RpcLogic___AddPlayerToCurrentRound_3323014238"),
                postfix: new HarmonyMethod(typeof(Hooks), nameof(card_round_begin)));
            harmony.Patch(AccessTools.Method(type, "RemoveLocalPlayerFromGame"),
                prefix: new HarmonyMethod(typeof(Hooks), nameof(card_round_before_settlement)),
                finalizer: new HarmonyMethod(typeof(Hooks), nameof(card_round_after_settlement)));
        }
        harmony.Patch(AccessTools.Method(typeof(CasinoGamePlayers), "RpcLogic___ReceivePlayerFloat_2317689966"),
            postfix: new HarmonyMethod(typeof(Hooks), nameof(card_round_receive)));
        harmony.Patch(AccessTools.Method(typeof(SlotMachine), "RpcLogic___StartSpin_2659526290"),
            postfix: new HarmonyMethod(typeof(Hooks), nameof(spin_begin)));
        harmony.Patch(AccessTools.Method(typeof(SlotMachine), "DisplayOutcome"),
            postfix: new HarmonyMethod(typeof(Hooks), nameof(spin_outcome_displayed)));
        harmony.Patch(AccessTools.Method(typeof(DailySummary), "Close"),
            postfix: new HarmonyMethod(typeof(Hooks), nameof(day_closed)));
    }

    public static void reset()
    {
        foreach (Table table in tables) { table.pointer = IntPtr.Zero; table.armed = false; }
        Array.Clear(spins, 0, spins.Length);
        failed = false;
        try { load(); }
        catch (Exception error) { pause(error); }
    }

    public static void update(float now_seconds)
    {
        if (failed) return;
        for (int i = 0; i < spins.Length; i++)
        {
            if (spins[i].machine != IntPtr.Zero && now_seconds >= spins[i].deadline_seconds) spin_settle(i);
        }
    }

    private static void card_round_begin(CasinoGameController __instance, NetworkObject __0)
    {
        if (!Main.ready || failed || Player.Local == null || __0 == null || __0 != Player.Local.NetworkObject) return;
        try
        {
            for (int i = 0; i < tables.Length; i++)
            {
                if (tables[i].pointer != __instance.Pointer && tables[i].pointer != IntPtr.Zero) continue;
                tables[i].pointer = __instance.Pointer;
                tables[i].armed = true;
                return;
            }
        }
        catch (Exception error) { pause(error); }
    }

    private static void card_round_before_settlement(CasinoGameController __instance, out Settlement __state)
    {
        __state = default;
        if (!Main.ready || failed || !MoneyManager.InstanceExists || Player.Local == null) return;
        try
        {
            foreach (Table table in tables)
            {
                if (table.pointer != __instance.Pointer || !table.armed) continue;
                table.armed = false;
                __state = new Settlement(table, __instance, __instance.LocalPlayerBet, MoneyManager.Instance.cashBalance);
                return;
            }
        }
        catch (Exception error) { pause(error); }
    }

    private static Exception? card_round_after_settlement(Exception? __exception, Settlement __state)
    {
        if (__exception != null || __state.table == null || __state.game == null || Player.Local == null) return __exception;
        try
        {
            CasinoGame game = __state.game.TryCast<BlackjackGameController>() != null ? CasinoGame.Blackjack : CasinoGame.RideTheBus;
            float returned = Math.Max(MoneyManager.Instance.cashBalance - __state.cash_before, 0f);
            settle(new Round(CasinoStats.player_key(Player.Local.PlayerCode), Ledger.safe_name(Player.Local.PlayerName), game, __state.stake, returned));
            string key = round_key_prefix + ((int)game).ToString(CultureInfo.InvariantCulture) + "|" + __state.stake.ToString("R", CultureInfo.InvariantCulture);
            __state.game.Players.SendPlayerFloat(Player.Local.NetworkObject, key, returned);
        }
        catch (Exception error) { pause(error); }
        return __exception;
    }

    private static void card_round_receive(NetworkConnection __0, NetworkObject __1, string __2, float __3)
    {
        if (!Main.ready || failed || __1 == null || __2 == null || !__2.StartsWith(round_key_prefix, StringComparison.Ordinal)) return;
        try
        {
            Player player = __1.GetComponent<Player>();
            if (player == null || player == Player.Local) return;
            string[] fields = __2.Split('|');
            if (fields.Length != 3 || fields[1].Length != 1 || fields[2].Length > 32) return;
            int game = fields[1][0] - '0';
            if (game != (int)CasinoGame.Blackjack && game != (int)CasinoGame.RideTheBus) return;
            if (!float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float stake)) return;
            settle(new Round(CasinoStats.player_key(player.PlayerCode), Ledger.safe_name(player.PlayerName), (CasinoGame)game, stake, __3));
        }
        catch (ArgumentOutOfRangeException error) { MelonLogger.Warning($"Casino Ledger: Ignored a malformed remote round: {error.Message}"); }
        catch (Exception error) { pause(error); }
    }

    private static void spin_begin(SlotMachine __instance, NetworkConnection __0, Il2CppStructArray<SlotMachine.ESymbol> __1, int __2)
    {
        if (!Main.ready || failed || __0 == null || __1 == null) return;
        try
        {
            Player? spinner = null;
            int count = Math.Min(Player.PlayerList.Count, Ledger.player_count_max);
            for (int i = 0; i < count; i++)
            {
                Player candidate = Player.PlayerList[i];
                if (candidate != null && candidate.Owner != null && candidate.Owner.ClientId == __0.ClientId) spinner = candidate;
            }
            if (spinner == null) return;
            int free = -1;
            for (int i = 0; i < spins.Length; i++)
            {
                if (spins[i].machine == __instance.Pointer) spin_settle(i);
                if (free < 0 && spins[i].machine == IntPtr.Zero) free = i;
            }
            if (free < 0) throw new InvalidOperationException("More concurrent slot spins than tracked machines.");
            int returned = __instance.GetWinAmount(__instance.EvaluateOutcome(__1), __2);
            spins[free] = new Spin
            {
                machine = __instance.Pointer,
                round = new Round(CasinoStats.player_key(spinner.PlayerCode), Ledger.safe_name(spinner.PlayerName), CasinoGame.Slots, __2, returned),
                deadline_seconds = Time.unscaledTime + spin_settle_seconds_max,
            };
        }
        catch (Exception error) { pause(error); }
    }

    private static void spin_outcome_displayed(SlotMachine __instance)
    {
        if (failed) return;
        for (int i = 0; i < spins.Length; i++)
        {
            if (spins[i].machine == __instance.Pointer) spin_settle(i);
        }
    }

    private static void spin_settle(int index)
    {
        Round round = spins[index].round;
        spins[index] = default;
        try { settle(round); }
        catch (Exception error) { pause(error); }
    }

    private static void day_closed()
    {
        if (!Main.ready || failed) return;
        try
        {
            for (int i = 0; i < spins.Length; i++)
            {
                if (spins[i].machine != IntPtr.Zero) spin_settle(i);
            }
            Main.end_day();
        }
        catch (Exception error) { pause(error); }
    }

    private static void settle(Round round)
    {
        CasinoStats.record(round);
        save();
        MelonLogger.Msg($"Casino Ledger: {round.player_name} {round.game} stake={round.stake:F2} returned={round.returned:F2} day_net={CasinoStats.ledger.day(round.player_key).net:F2}");
    }

    private static void load()
    {
        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "CasinoLedger");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ledger.txt");
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("Casino ledger exceeds size limit.");
            CasinoStats.ledger.load(File.ReadAllText(path));
        }
        else
        {
            CasinoStats.ledger.clear();
        }
        ledger_path = path;
        import_death_notices_totals();
    }

    private static void import_death_notices_totals()
    {
        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "DeathNoticesCasino");
        if (!Directory.Exists(directory)) return;
        string[] files = Directory.GetFiles(directory, "*.txt");
        int file_count = Math.Min(files.Length, Ledger.player_count_max);
        for (int i = 0; i < file_count; i++)
        {
            string player_key = Path.GetFileNameWithoutExtension(files[i]);
            if (player_key.Length != 64 || new FileInfo(files[i]).Length > 128) continue;
            double net = double.Parse(File.ReadAllText(files[i]), NumberStyles.Float, CultureInfo.InvariantCulture);
            CasinoStats.ledger.import_net(player_key, CasinoGame.Blackjack, net);
            save();
            File.Move(files[i], files[i] + ".imported", true);
            MelonLogger.Msg($"Casino Ledger: Imported a Death Notices card total of {net:F2}.");
        }
    }

    private static void save()
    {
        string temporary = ledger_path + ".tmp";
        File.WriteAllText(temporary, CasinoStats.ledger.serialize());
        File.Move(temporary, ledger_path, true);
    }

    private static void pause(Exception error)
    {
        failed = true;
        MelonLogger.Warning($"Casino Ledger: Tracking paused for this scene: {error.Message}");
    }
}
