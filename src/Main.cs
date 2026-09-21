using System;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(CasinoLedger.Main), "Casino Ledger", "0.5.0", "holyfurries")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace CasinoLedger;

public sealed class Main : MelonMod
{
    private static MelonPreferences_Entry<bool>? hud_enabled;
    private static MelonPreferences_Entry<bool>? day_summary_enabled;
    private static MelonPreferences_Entry<float>? hud_margin_right;
    private static MelonPreferences_Entry<float>? hud_margin_top;
    private static MelonPreferences_Entry<float>? hud_scale;
    private static MelonPreferences_Entry<KeyCode>? hud_toggle_key;
    private static bool running;
    private static bool ui_failed;
    private static int day_number;
    private float next_hud_refresh_seconds;
    internal static bool hud_visible
    {
        get => hud_enabled!.Value;
        set
        {
            hud_enabled!.Value = value;
            MelonPreferences.Save();
        }
    }
    internal static bool ready => running && LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded;

    public override void OnInitializeMelon()
    {
        MelonPreferences_Category preferences = MelonPreferences.CreateCategory("CasinoLedger");
        hud_enabled = preferences.CreateEntry("hud_enabled", true, "Show each player's total casino profit in this save");
        day_summary_enabled = preferences.CreateEntry("day_summary_enabled", true, "Show casino stats after the sleep summary");
        hud_margin_right = preferences.CreateEntry("hud_margin_right", 13f, "HUD distance from the right screen edge (1920x1080 units)");
        hud_margin_top = preferences.CreateEntry("hud_margin_top", 420f, "HUD distance from the top screen edge (1920x1080 units)");
        hud_scale = preferences.CreateEntry("hud_scale", 1f, "HUD size multiplier, 0.5 to 3");
        hud_toggle_key = preferences.CreateEntry("hud_toggle_key", KeyCode.F7, "Key that shows or hides the HUD in game; None turns the key off");
        CasinoStats.subscriber_failed = error => LoggerInstance.Warning($"A round_settled subscriber threw: {error}");
        CasinoStats.round_settled += note_day_number;
        Hooks.install(HarmonyInstance);
        register_settings();
    }

    private static void register_settings()
    {
        ModSettings.Settings.toggle("Casino Ledger", "Show standings", hud_enabled!);
        ModSettings.Settings.slider("Casino Ledger", "Standings size", hud_scale!, minimum: 0.5f, maximum: 3f);
        ModSettings.Settings.toggle("Casino Ledger", "Day-end stats screen", day_summary_enabled!);
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
            DaySummary.update();
            if (hud_toggle_key!.Value != KeyCode.None && Input.GetKeyDown(hud_toggle_key.Value))
            {
                hud_visible = !hud_visible;
                next_hud_refresh_seconds = 0;
            }
            if (Time.unscaledTime < next_hud_refresh_seconds) return;
            next_hud_refresh_seconds = Time.unscaledTime + 0.5f;
            Hud.refresh(visible: hud_enabled!.Value && !DaySummary.showing, new Vector2(hud_margin_right!.Value, hud_margin_top!.Value),
                hud_scale!.Value);
        }
        catch (Exception error) { disable_ui(error); }
    }

    internal static void end_day()
    {
        Ledger ledger = CasinoStats.ledger;
        try
        {
            if (ledger.day_round_count > 0 && day_summary_enabled!.Value && !ui_failed) DaySummary.show(ledger, day_number);
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
