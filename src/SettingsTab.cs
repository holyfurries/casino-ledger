using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppScheduleOne.UI.Settings;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CasinoLedger;

/// Adds a "Casino" tab to the game's settings screen by cloning its last tab and that tab's first toggle row.
internal static class SettingsTab
{
    private const string tab_name = "CasinoLedgerTab";
    private const int category_count_max = 16;

    public static void install(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SettingsScreen), nameof(SettingsScreen.Awake)),
            postfix: new HarmonyMethod(typeof(SettingsTab), nameof(add_tab)));
    }

    private static void add_tab(SettingsScreen __instance)
    {
        try { build(__instance); }
        catch (Exception error) { MelonLogger.Warning($"Casino Ledger: Settings tab unavailable, use MelonPreferences.cfg instead: {error}"); }
    }

    private static void build(SettingsScreen screen)
    {
        Il2CppReferenceArray<SettingsScreen.SettingsCategory> categories = screen.Categories;
        if (categories == null || categories.Length == 0 || categories.Length >= category_count_max) throw new InvalidOperationException("Unexpected settings categories.");
        SettingsScreen.SettingsCategory template = categories[categories.Length - 1];
        if (template.Toggle == null || template.Panel == null) throw new InvalidOperationException("Settings template tab is incomplete.");
        if (template.Toggle.transform.parent.Find(tab_name) != null) return;

        GameObject panel = UnityEngine.Object.Instantiate(template.Panel, template.Panel.transform.parent);
        panel.name = "CasinoLedger";
        panel.SetActive(false);
        SettingsToggle? template_row = panel.GetComponentInChildren<SettingsToggle>(true);
        if (template_row == null) throw new InvalidOperationException("Settings template has no toggle row.");
        Transform row = template_row.transform;
        UIToggle? toggle = row.GetComponent<UIToggle>();
        if (toggle == null) throw new InvalidOperationException("Settings toggle row has no UIToggle.");
        UnityEngine.Object.DestroyImmediate(template_row);
        Transform rows = row.parent;
        for (int i = 0; i < rows.childCount; i++)
        {
            if (rows.GetChild(i) != row) rows.GetChild(i).gameObject.SetActive(false);
        }
        TextMeshProUGUI[] labels = row.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI label in labels)
        {
            if (label.name.StartsWith("Option Name", StringComparison.Ordinal)) label.text = "Standings panel";
            else if (label.name.StartsWith("Label", StringComparison.Ordinal)) label.gameObject.SetActive(false);
        }
        toggle.OnChanged.AddListener(new Action<bool>(visible => Main.hud_visible = visible));

        Toggle tab = UnityEngine.Object.Instantiate(template.Toggle.gameObject, template.Toggle.transform.parent).GetComponent<Toggle>();
        tab.name = tab_name;
        tab.SetIsOnWithoutNotify(false);
        TextMeshProUGUI? tab_label = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tab_label != null) tab_label.text = "Casino";

        int index = categories.Length;
        var extended = new Il2CppReferenceArray<SettingsScreen.SettingsCategory>(index + 1);
        for (int i = 0; i < index; i++) extended[i] = categories[i];
        extended[index] = new SettingsScreen.SettingsCategory { Toggle = tab, Panel = panel };
        screen.Categories = extended;
        tab.onValueChanged.AddListener(new Action<bool>(selected =>
        {
            if (!selected) return;
            screen.ShowCategory(index);
            toggle.SetStateWithoutNotify(Main.hud_visible);
        }));
    }
}
