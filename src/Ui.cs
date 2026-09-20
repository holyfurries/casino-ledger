using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CasinoLedger;

internal static class Ui
{
    public static readonly Color profit = new(0.24f, 0.93f, 0.38f, 1f);
    public static readonly Color loss = new(1f, 0.23f, 0.36f, 1f);
    public static readonly Color surface = new(0.13f, 0.13f, 0.13f, 0.9f);
    public static readonly Color outline = new(1f, 1f, 1f, 0.8f);
    private const int corner_radius_pixels = 10;
    private static Sprite? rounded_sprite;

    public static Color money_color(double value) => value < -0.005 ? loss : profit;

    public static GameObject canvas(string name, int sorting_order)
    {
        var root = new GameObject(name);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sorting_order;
        var scaler = root.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        return root;
    }

    public static RectTransform panel(Transform parent, string name, Color color)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        Image image = root.AddComponent<Image>();
        image.sprite = rounded();
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    public static RectTransform outlined_panel(Transform parent, string name)
    {
        RectTransform border = panel(parent, name, outline);
        RectTransform fill = panel(border, "Fill", new Color(surface.r, surface.g, surface.b, 1f));
        stretch(fill, inset: 2f);
        return border;
    }

    public static TextMeshProUGUI text(Transform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        TextMeshProUGUI label = root.AddComponent<TextMeshProUGUI>();
        if (NotificationsManager.InstanceExists && NotificationsManager.Instance.NotificationPrefab != null)
        {
            TMP_FontAsset? font = NotificationsManager.Instance.NotificationPrefab.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
            if (font != null) label.font = font;
        }
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        label.color = Color.white;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    public static void place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public static void stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static Sprite rounded()
    {
        if (rounded_sprite != null) return rounded_sprite;
        const int size = corner_radius_pixels * 2 + 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float corner_x = Mathf.Clamp(x + 0.5f, corner_radius_pixels, size - corner_radius_pixels);
                float corner_y = Mathf.Clamp(y + 0.5f, corner_radius_pixels, size - corner_radius_pixels);
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(corner_x, corner_y));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(corner_radius_pixels - distance + 0.5f)));
            }
        }
        texture.Apply();
        float border = corner_radius_pixels;
        rounded_sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        rounded_sprite.hideFlags = HideFlags.HideAndDontSave;
        return rounded_sprite;
    }
}
