using System;
using System.Collections.Generic;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using UnityEngine;

namespace CasinoLedger;

internal static class Hud
{
    private const int row_count_max = 8;
    private const float panel_width = 300f;
    private const float row_height = 37f;
    private const float padding = 14f;
    private const float amount_width = 112f;
    private readonly record struct Standing(string name, double day_net);
    private static readonly Comparer<Standing> by_day_net_descending = Comparer<Standing>.Create(
        (left, right) => right.day_net.CompareTo(left.day_net));
    private static readonly Standing[] standings = new Standing[row_count_max];
    private static readonly TextMeshProUGUI?[] amounts = new TextMeshProUGUI?[row_count_max];
    private static readonly TextMeshProUGUI?[] names = new TextMeshProUGUI?[row_count_max];
    private static GameObject? canvas_object;
    private static RectTransform? panel;

    public static void reset()
    {
        if (canvas_object != null) UnityEngine.Object.Destroy(canvas_object);
        canvas_object = null;
        panel = null;
        Array.Clear(amounts, 0, amounts.Length);
        Array.Clear(names, 0, names.Length);
    }

    public static void refresh(bool visible, Vector2 offset)
    {
        bool game_hud_visible = HUD.InstanceExists && HUD.Instance.canvas != null && HUD.Instance.canvas.enabled;
        int standing_count = visible && game_hud_visible ? collect() : 0;
        if (standing_count == 0)
        {
            if (canvas_object != null) canvas_object.SetActive(false);
            return;
        }
        if (canvas_object == null) build();
        if (panel == null) throw new InvalidOperationException("Casino HUD panel missing after build.");
        canvas_object!.SetActive(true);
        Ui.place(panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-offset.x, -offset.y),
            new Vector2(panel_width, standing_count * row_height + padding * 2f));
        for (int i = 0; i < row_count_max; i++)
        {
            TextMeshProUGUI amount = amounts[i]!;
            TextMeshProUGUI name = names[i]!;
            amount.gameObject.SetActive(i < standing_count);
            name.gameObject.SetActive(i < standing_count);
            if (i >= standing_count) continue;
            amount.text = Ledger.format_money(standings[i].day_net, signed: true);
            amount.color = Ui.money_color(standings[i].day_net);
            name.text = standings[i].name;
        }
    }

    private static int collect()
    {
        if (CasinoStats.ledger.day_round_count == 0) return 0;
        int standing_count = 0;
        int player_count = Math.Min(Player.PlayerList.Count, Ledger.player_count_max);
        for (int i = 0; i < player_count && standing_count < row_count_max; i++)
        {
            Player player = Player.PlayerList[i];
            if (player == null || string.IsNullOrEmpty(player.PlayerCode)) continue;
            double day_net = CasinoStats.ledger.day(CasinoStats.player_key(player.PlayerCode)).net;
            standings[standing_count++] = new Standing(Ledger.safe_name(player.PlayerName), day_net);
        }
        Array.Sort(standings, 0, standing_count, by_day_net_descending);
        return standing_count;
    }

    private static void build()
    {
        canvas_object = Ui.canvas("CasinoLedgerHud", 29000);
        panel = Ui.outlined_panel(canvas_object.transform, "Standings");
        for (int i = 0; i < row_count_max; i++)
        {
            float row_top = -(padding + i * row_height);
            TextMeshProUGUI amount = Ui.text(panel, "Amount", 22f, TextAlignmentOptions.Left);
            Ui.place(amount.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(padding + 8f, row_top),
                new Vector2(amount_width, row_height));
            TextMeshProUGUI name = Ui.text(panel, "Name", 20f, TextAlignmentOptions.Right);
            Ui.place(name.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-(padding + 8f), row_top),
                new Vector2(panel_width - amount_width - padding * 2f - 24f, row_height));
            amounts[i] = amount;
            names[i] = name;
        }
    }
}
