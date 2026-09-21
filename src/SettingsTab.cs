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

/// Adds a switch to a shared "holyfurries" tab on the game's settings screen. The first holyfurries mod to
/// run clones the game's last tab to create it; later ones add their row to the same panel.
internal static class SettingsTab
{
    private const string tab_name = "HolyfurriesTab";
    private const string tab_label = "holyfurries";
    private const int category_count_max = 16;
    private const int row_count_max = 8;
    private const float row_spacing = 60f;

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
        if (categories == null || categories.Length == 0 || categories.Length > category_count_max) throw new InvalidOperationException("Unexpected settings categories.");
        SettingsToggle? template_row = null;
        GameObject? panel = null;
        for (int i = 0; i < categories.Length; i++)
        {
            if (categories[i].Toggle == null || categories[i].Panel == null) throw new InvalidOperationException("Settings category is incomplete.");
            if (categories[i].Toggle.name == tab_name) panel = categories[i].Panel;
            else template_row = categories[i].Panel.GetComponentInChildren<SettingsToggle>(true) ?? template_row;
        }
        if (template_row == null) throw new InvalidOperationException("Settings screen has no toggle row to copy.");
        panel ??= add_category(screen, categories);

        Transform rows = panel.GetComponentInChildren<UIPanel>(true).transform;
        int row_index = 0;
        for (int i = 0; i < rows.childCount; i++)
        {
            if (rows.GetChild(i).gameObject.activeSelf) row_index++;
        }
        if (row_index >= row_count_max) throw new InvalidOperationException("The holyfurries settings tab is full.");
        GameObject row = UnityEngine.Object.Instantiate(template_row.gameObject, rows);
        row.name = "CasinoLedgerStandings";
        UnityEngine.Object.DestroyImmediate(row.GetComponent<SettingsToggle>());
        UIToggle toggle = row.GetComponent<UIToggle>() ?? throw new InvalidOperationException("Settings toggle row has no UIToggle.");
        RectTransform rect = row.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -10f - row_index * row_spacing);
        foreach (TextMeshProUGUI label in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.name.StartsWith("Option Name", StringComparison.Ordinal)) label.text = "Casino standings";
            else if (label.name.StartsWith("Label", StringComparison.Ordinal)) label.gameObject.SetActive(false);
        }
        row.SetActive(true);
        toggle.SetStateWithoutNotify(Main.hud_visible);
        toggle.OnChanged.AddListener(new Action<bool>(visible => Main.hud_visible = visible));
    }

    private static GameObject add_category(SettingsScreen screen, Il2CppReferenceArray<SettingsScreen.SettingsCategory> categories)
    {
        SettingsScreen.SettingsCategory template = categories[categories.Length - 1];
        GameObject panel = UnityEngine.Object.Instantiate(template.Panel, template.Panel.transform.parent);
        panel.name = "Holyfurries";
        panel.SetActive(false);
        Transform rows = panel.GetComponentInChildren<UIPanel>(true).transform;
        for (int i = 0; i < rows.childCount; i++) rows.GetChild(i).gameObject.SetActive(false);

        Toggle tab = UnityEngine.Object.Instantiate(template.Toggle.gameObject, template.Toggle.transform.parent).GetComponent<Toggle>();
        tab.name = tab_name;
        tab.SetIsOnWithoutNotify(false);
        TextMeshProUGUI? label = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = 6f;
            label.enableAutoSizing = true;
            label.text = tab_label;
        }

        int index = categories.Length;
        var extended = new Il2CppReferenceArray<SettingsScreen.SettingsCategory>(index + 1);
        for (int i = 0; i < index; i++) extended[i] = categories[i];
        extended[index] = new SettingsScreen.SettingsCategory { Toggle = tab, Panel = panel };
        screen.Categories = extended;
        tab.onValueChanged.AddListener(new Action<bool>(selected => { if (selected) screen.ShowCategory(index); }));
        return panel;
    }
}
