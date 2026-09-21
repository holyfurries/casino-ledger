using System;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CasinoLedger;

internal static class DaySummary
{
    private const int line_count_max = 8;
    private const string ui_element_name = "CasinoLedgerDaySummary";
    private const float input_delay_seconds = 0.4f;
    private const float content_width = 1120f;
    private const float panel_height = 330f;
    private const float chart_width = 640f;
    private const float chart_label_width = 210f;
    private const float chart_padding = 22f;
    private const float label_gap = 28f;
    private const float layout_scale = 0.75f;
    private const float layout_center_y = 468f;
    private static readonly Color[] line_colors =
    {
        new(0.91f, 0.45f, 0.78f), new(0.95f, 0.60f, 0.18f), new(0.35f, 0.75f, 0.98f), new(0.58f, 0.90f, 0.35f),
        new(0.98f, 0.85f, 0.25f), new(0.70f, 0.55f, 0.98f), new(0.98f, 0.45f, 0.40f), new(0.45f, 0.92f, 0.82f),
    };
    private static readonly string[] game_names = { "Blackjack", "Ride the Bus", "Slots" };
    private static GameObject? canvas_object;
    private static float input_open_seconds;

    public static bool showing => canvas_object != null;

    public static void reset()
    {
        if (canvas_object == null) return;
        UnityEngine.Object.Destroy(canvas_object);
        canvas_object = null;
        if (!PlayerSingleton<PlayerCamera>.InstanceExists) return;
        PlayerCamera camera = PlayerSingleton<PlayerCamera>.Instance;
        camera.RemoveActiveUIElement(ui_element_name);
        if (camera.activeUIElements.Count == 0) camera.LockMouse();
    }

    public static void update(float now_seconds)
    {
        if (canvas_object == null) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (now_seconds < input_open_seconds) return;
        if (GameInput.GetButtonDown(GameInput.ButtonCode.Submit) || Input.GetKeyDown(KeyCode.Escape)) reset();
    }

    public static void show(Ledger ledger, int day_number, float now_seconds)
    {
        if (ledger.day_round_count <= 0) throw new ArgumentOutOfRangeException(nameof(ledger), "No rounds to summarize.");
        reset();
        var keys = new string[line_count_max];
        int key_count = ledger.day_player_keys(keys);
        Totals group = default;
        for (int i = 0; i < key_count; i++) group = group.add(ledger.day(keys[i]));

        canvas_object = Ui.canvas("CasinoLedgerDaySummary", 29500);
        canvas_object.AddComponent<GraphicRaycaster>();
        input_open_seconds = now_seconds + input_delay_seconds;
        if (PlayerSingleton<PlayerCamera>.InstanceExists)
        {
            PlayerSingleton<PlayerCamera>.Instance.AddActiveUIElement(ui_element_name);
            PlayerSingleton<PlayerCamera>.Instance.FreeMouse();
        }
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(canvas_object.transform, false);
        Image backdrop_image = backdrop.AddComponent<Image>();
        backdrop_image.color = Color.black;
        backdrop_image.raycastTarget = false;
        Ui.stretch(backdrop_image.rectTransform, inset: 0f);
        var top_center = new Vector2(0.5f, 1f);
        var layout_object = new GameObject("Layout");
        layout_object.transform.SetParent(canvas_object.transform, false);
        RectTransform layout = layout_object.AddComponent<RectTransform>();
        Ui.place(layout, new Vector2(0.5f, 0.5f), top_center, new Vector2(0f, layout_center_y * layout_scale), new Vector2(content_width, 0f));
        layout.localScale = new Vector3(layout_scale, layout_scale, 1f);

        TextMeshProUGUI title = Ui.text(layout, "Title", 36f, TextAlignmentOptions.Center);
        title.text = $"Casino stats for day {day_number}";
        Ui.place(title.rectTransform, top_center, top_center, new Vector2(0f, -130f), new Vector2(content_width, 80f));

        RectTransform strip = Ui.outlined_panel(layout, "Totals");
        Ui.place(strip, top_center, top_center, new Vector2(0f, -250f), new Vector2(content_width, 64f));
        strip_cell(strip, 0, "Wagered", Ledger.format_money(group.wagered, signed: false), Color.white);
        strip_cell(strip, 1, "Returned", Ledger.format_money(group.returned, signed: false), Color.white);
        strip_cell(strip, 2, "Profit", Ledger.format_money(group.net, signed: true), Ui.money_color(group.net));

        RectTransform chart = Ui.outlined_panel(layout, "Chart");
        Ui.place(chart, top_center, new Vector2(0f, 1f), new Vector2(-content_width / 2f, -340f), new Vector2(chart_width, panel_height));
        draw_chart(chart, ledger, keys, key_count);

        RectTransform breakdown = Ui.outlined_panel(layout, "Breakdown");
        float breakdown_width = content_width - chart_width - 26f;
        Ui.place(breakdown, top_center, new Vector2(1f, 1f), new Vector2(content_width / 2f, -340f), new Vector2(breakdown_width, panel_height));
        draw_breakdown(breakdown, breakdown_width, ledger, keys, key_count, group);

        RectTransform button = Ui.panel(layout, "Continue", Ui.loss);
        Ui.place(button, top_center, top_center, new Vector2(0f, -730f), new Vector2(380f, 76f));
        TextMeshProUGUI button_label = Ui.text(button, "Label", 28f, TextAlignmentOptions.Center);
        button_label.text = "Continue";
        Image button_image = button.GetComponent<Image>();
        button_image.raycastTarget = true;
        Button continue_button = button.gameObject.AddComponent<Button>();
        continue_button.targetGraphic = button_image;
        continue_button.onClick.AddListener(new Action(reset));
        Ui.stretch(button_label.rectTransform, inset: 0f);
    }

    private static void strip_cell(RectTransform strip, int index, string label, string value, Color value_color)
    {
        float cell_width = content_width / 3f;
        var top_left = new Vector2(0f, 1f);
        TextMeshProUGUI name = Ui.text(strip, label, 26f, TextAlignmentOptions.Left);
        name.text = label;
        Ui.place(name.rectTransform, top_left, top_left, new Vector2(index * cell_width + 24f, 0f), new Vector2(cell_width - 48f, 64f));
        TextMeshProUGUI amount = Ui.text(strip, label + "Value", 26f, TextAlignmentOptions.Right);
        amount.text = value;
        amount.color = value_color;
        Ui.place(amount.rectTransform, top_left, top_left, new Vector2(index * cell_width + 24f, 0f), new Vector2(cell_width - 48f, 64f));
    }

    private static void draw_breakdown(RectTransform parent, float width, Ledger ledger, string[] keys, int key_count, Totals group)
    {
        const float row_height = 46f;
        var top_left = new Vector2(0f, 1f);
        int row = 0;
        void add_row(string label, string value, Color color)
        {
            TextMeshProUGUI name = Ui.text(parent, "Label", 22f, TextAlignmentOptions.Left);
            name.text = label;
            Ui.place(name.rectTransform, top_left, top_left, new Vector2(24f, -(14f + row * row_height)), new Vector2(width - 48f, row_height));
            TextMeshProUGUI amount = Ui.text(parent, "Value", 24f, TextAlignmentOptions.Right);
            amount.text = value;
            amount.color = color;
            Ui.place(amount.rectTransform, top_left, top_left, new Vector2(24f, -(14f + row * row_height)), new Vector2(width - 48f, row_height));
            row++;
        }

        string win_name = "";
        string loss_name = "";
        double win_largest = 0;
        double loss_largest = 0;
        for (int game = 0; game < Ledger.game_count; game++)
        {
            Totals totals = default;
            for (int i = 0; i < key_count; i++) totals = totals.add(ledger.day(keys[i], (CasinoGame)game));
            if (totals.round_count > 0) add_row(game_names[game], Ledger.format_money(totals.net, signed: true), Ui.money_color(totals.net));
        }
        for (int i = 0; i < key_count; i++)
        {
            Totals totals = ledger.day(keys[i]);
            if (totals.win_largest > win_largest) { win_largest = totals.win_largest; win_name = ledger.name(keys[i]); }
            if (totals.loss_largest > loss_largest) { loss_largest = totals.loss_largest; loss_name = ledger.name(keys[i]); }
        }
        if (win_largest > 0) add_row($"Best round ({win_name})", Ledger.format_money(win_largest, signed: true), Ui.profit);
        if (loss_largest > 0) add_row($"Worst round ({loss_name})", Ledger.format_money(-loss_largest, signed: true), Ui.loss);

        RectTransform divider = Ui.panel(parent, "Divider", new Color(1f, 1f, 1f, 0.35f));
        Ui.place(divider, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(2f, 70f), new Vector2(width - 4f, 2f));
        TextMeshProUGUI rounds = Ui.text(parent, "Rounds", 28f, TextAlignmentOptions.Left);
        rounds.text = "Rounds";
        Ui.place(rounds.rectTransform, Vector2.zero, Vector2.zero, new Vector2(24f, 8f), new Vector2(width - 48f, 56f));
        TextMeshProUGUI round_count = Ui.text(parent, "RoundCount", 28f, TextAlignmentOptions.Right);
        round_count.text = $"{group.round_count} ({group.win_count}W {group.loss_count}L)";
        Ui.place(round_count.rectTransform, Vector2.zero, Vector2.zero, new Vector2(24f, 8f), new Vector2(width - 48f, 56f));
    }

    private static void draw_chart(RectTransform parent, Ledger ledger, string[] keys, int key_count)
    {
        float plot_width = chart_width - chart_label_width - chart_padding * 2f;
        float plot_height = panel_height - chart_padding * 2f;
        var points = new SeriesPoint[Ledger.series_point_count_max];
        double net_min = 0;
        double net_max = 0;
        for (int i = 0; i < key_count; i++)
        {
            int count = ledger.day_series(keys[i], points);
            for (int j = 0; j < count; j++)
            {
                net_min = Math.Min(net_min, points[j].day_net);
                net_max = Math.Max(net_max, points[j].day_net);
            }
        }
        double net_span = Math.Max(net_max - net_min, 1);
        Vector2 plot(int day_round_index, double day_net) => new(
            chart_padding + plot_width * day_round_index / ledger.day_round_count,
            chart_padding + plot_height * (float)((day_net - net_min) / net_span));

        RectTransform baseline = Ui.panel(parent, "Baseline", new Color(1f, 1f, 1f, 0.25f));
        Ui.place(baseline, Vector2.zero, new Vector2(0f, 0.5f), plot(0, 0), new Vector2(plot_width, 2f));

        var ends = new (int key_index, Vector2 end, float label_y)[line_count_max];
        for (int i = 0; i < key_count; i++)
        {
            int count = ledger.day_series(keys[i], points);
            Vector2 previous = plot(0, 0);
            for (int j = 0; j < count; j++)
            {
                Vector2 next = plot(points[j].day_round_index, points[j].day_net);
                Vector2 delta = next - previous;
                RectTransform segment = Ui.panel(parent, "Segment", line_colors[i]);
                Ui.place(segment, Vector2.zero, new Vector2(0f, 0.5f), previous, new Vector2(delta.magnitude + 2f, 5f));
                segment.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                previous = next;
            }
            Vector2 end = new(chart_padding + plot_width, previous.y);
            RectTransform tail = Ui.panel(parent, "Tail", line_colors[i]);
            Ui.place(tail, Vector2.zero, new Vector2(0f, 0.5f), previous, new Vector2(end.x - previous.x + 2f, 5f));
            ends[i] = (i, end, end.y);
        }

        Array.Sort(ends, 0, key_count, System.Collections.Generic.Comparer<(int key_index, Vector2 end, float label_y)>.Create(
            (left, right) => right.end.y.CompareTo(left.end.y)));
        for (int i = 0; i < key_count; i++)
        {
            if (i > 0) ends[i].label_y = Math.Min(ends[i].label_y, ends[i - 1].label_y - label_gap);
        }
        float label_y_floor = chart_padding;
        for (int i = key_count - 1; i >= 0; i--)
        {
            ends[i].label_y = Math.Max(ends[i].label_y, label_y_floor);
            label_y_floor = ends[i].label_y + label_gap;
        }
        for (int i = 0; i < key_count; i++)
        {
            string key = keys[ends[i].key_index];
            double day_net = ledger.day(key).net;
            RectTransform dot = Ui.panel(parent, "Dot", line_colors[ends[i].key_index]);
            Ui.place(dot, Vector2.zero, new Vector2(0.5f, 0.5f), ends[i].end, new Vector2(16f, 16f));
            TextMeshProUGUI label = Ui.text(parent, "Label", 19f, TextAlignmentOptions.Left);
            string amount_color = ColorUtility.ToHtmlStringRGB(Ui.money_color(day_net));
            label.text = $"{ledger.name(key)} <color=#{amount_color}>{Ledger.format_money(day_net, signed: true)}</color>";
            Ui.place(label.rectTransform, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(ends[i].end.x + 14f, ends[i].label_y),
                new Vector2(chart_label_width - 8f, label_gap));
        }
    }
}
