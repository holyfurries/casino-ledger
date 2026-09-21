using System;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Casino;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace CasinoLedger;

internal static class Hooks
{
    private const float spin_settle_seconds_max = 8f;
    private const float spin_repeat_seconds = 1f;
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
    private static bool tracking => Main.ready && !failed && ledger_path.Length > 0;

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
        ledger_path = "";
        CasinoStats.ledger.clear();
    }

    public static void update(float now_seconds)
    {
        if (failed) return;
        if (ledger_path.Length == 0)
        {
            try { load(); }
            catch (Exception error) { pause(error); }
            return;
        }
        for (int i = 0; i < spins.Length; i++)
        {
            if (spins[i].machine != IntPtr.Zero && now_seconds >= spins[i].deadline_seconds) spin_settle(i);
        }
        Sync.flush(now_seconds);
    }

    private static void card_round_begin(CasinoGameController __instance, NetworkObject __0)
    {
        if (!tracking || Player.Local == null || __0 == null || __0 != Player.Local.NetworkObject) return;
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
        if (!tracking || !MoneyManager.InstanceExists || Player.Local == null) return;
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
            settle_local(new Round(CasinoStats.player_key(Player.Local.PlayerCode), Ledger.safe_name(Player.Local.PlayerName), game, __state.stake, returned));
        }
        catch (Exception error) { pause(error); }
        return __exception;
    }

    private static void remote_round(Round round)
    {
        if (!tracking || Player.Local == null || round.player_key == CasinoStats.player_key(Player.Local.PlayerCode)) return;
        try { settle(round); }
        catch (ArgumentOutOfRangeException error) { MelonLogger.Warning($"Casino Ledger: Ignored an out of range remote round: {error.Message}"); }
        catch (Exception error) { pause(error); }
    }

    private static void spin_begin(SlotMachine __instance, NetworkConnection __0, Il2CppStructArray<SlotMachine.ESymbol> __1, int __2)
    {
        if (!tracking || __0 == null || __1 == null) return;
        try
        {
            Player spinner = Player.Local;
            if (spinner == null || spinner.Owner == null || spinner.Owner.ClientId != __0.ClientId) return;
            int free = -1;
            for (int i = 0; i < spins.Length; i++)
            {
                if (spins[i].machine == __instance.Pointer)
                {
                    float held_seconds = spin_settle_seconds_max - (spins[i].deadline_seconds - Time.unscaledTime);
                    if (held_seconds < spin_repeat_seconds) return;
                    spin_settle(i);
                }
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
        try { settle_local(round); }
        catch (Exception error) { pause(error); }
    }

    private static void day_closed()
    {
        if (!tracking) return;
        try
        {
            for (int i = 0; i < spins.Length; i++)
            {
                if (spins[i].machine != IntPtr.Zero) spin_settle(i);
            }
            Main.end_day();
            Sync.send_totals();
        }
        catch (Exception error) { pause(error); }
    }

    private static void settle_local(Round round)
    {
        settle(round);
        Sync.send_round(round);
    }

    private static void settle(Round round)
    {
        CasinoStats.record(round);
        save();
        MelonLogger.Msg($"Casino Ledger: {round.player_name} {round.game} stake={round.stake:F2} returned={round.returned:F2} day_net={CasinoStats.ledger.day(round.player_key).net:F2}");
    }

    private static void load()
    {
        if (!GameManager.InstanceExists) return;
        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "CasinoLedger");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, CasinoStats.world_key(GameManager.Instance.seed, GameManager.Instance.OrganisationName) + ".txt");
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
        Sync.round_received = remote_round;
        Sync.totals_changed = save;
        Sync.connect();
        MelonLogger.Msg($"Casino Ledger: Tracking this save in {Path.GetFileName(path)}.");
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
