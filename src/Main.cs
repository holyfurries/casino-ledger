using System;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(CasinoLedger.Main), "Casino Ledger", "0.1.0", "holyfurries")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace CasinoLedger;

public sealed class Main : MelonMod
{
    private static MelonPreferences_Entry<bool>? hud_enabled;
    private static MelonPreferences_Entry<bool>? day_summary_enabled;
    private static MelonPreferences_Entry<float>? hud_offset_x;
    private static MelonPreferences_Entry<float>? hud_offset_y;
    private static bool running;
    private static bool ui_failed;
    private static int day_number;
    private float next_hud_refresh_seconds;
    internal static bool ready => running && LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded;

    public override void OnInitializeMelon()
    {
        MelonPreferences_Category preferences = MelonPreferences.CreateCategory("CasinoLedger");
        hud_enabled = preferences.CreateEntry("hud_enabled", true, "Show each player's casino profit for the day");
        day_summary_enabled = preferences.CreateEntry("day_summary_enabled", true, "Show casino stats after the sleep summary");
        hud_offset_x = preferences.CreateEntry("hud_offset_x", 32f, "HUD distance from the right screen edge (1920x1080 units)");
        hud_offset_y = preferences.CreateEntry("hud_offset_y", 420f, "HUD distance from the top screen edge (1920x1080 units)");
        CasinoStats.subscriber_failed = error => LoggerInstance.Warning($"A round_settled subscriber threw: {error}");
        CasinoStats.round_settled += note_day_number;
        Hooks.install(HarmonyInstance);
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (sceneName != "Main") return;
        clear();
        running = true;
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        if (sceneName != "Main") return;
        running = false;
        clear();
    }

    public override void OnUpdate()
    {
        if (!ready) return;
        Hooks.update(Time.unscaledTime);
        if (ui_failed) return;
        try
        {
            DaySummary.update(Time.unscaledTime);
            if (Time.unscaledTime < next_hud_refresh_seconds) return;
            next_hud_refresh_seconds = Time.unscaledTime + 0.5f;
            Hud.refresh(visible: hud_enabled!.Value && !DaySummary.showing, new Vector2(hud_offset_x!.Value, hud_offset_y!.Value));
        }
        catch (Exception error) { disable_ui(error); }
    }

    internal static void end_day()
    {
        Ledger ledger = CasinoStats.ledger;
        try
        {
            if (ledger.day_round_count > 0 && day_summary_enabled!.Value && !ui_failed) DaySummary.show(ledger, day_number, Time.unscaledTime);
        }
        catch (Exception error) { disable_ui(error); }
        ledger.start_day();
        day_number = 0;
    }

    private static void note_day_number(RoundReport report)
    {
        if (day_number == 0 && TimeManager.InstanceExists) day_number = TimeManager.Instance.ElapsedDays + 1;
    }

    private void clear()
    {
        Hooks.reset();
        Hud.reset();
        DaySummary.reset();
        ui_failed = false;
        day_number = 0;
        next_hud_refresh_seconds = 0;
    }

    private static void disable_ui(Exception error)
    {
        ui_failed = true;
        Hud.reset();
        DaySummary.reset();
        MelonLogger.Error($"Casino Ledger: Overlay disabled for this scene, tracking continues: {error}");
    }
}
