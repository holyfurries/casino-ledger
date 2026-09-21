using System.Collections.Generic;
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
    private const float outline_pixels = 2f;
    private static Sprite? rounded_sprite;
    private static readonly Dictionary<float, Sprite> outlined_sprites = new();

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

    public static RectTransform outlined_panel(Transform parent, string name, float fill_alpha = 1f, float corner_radius = corner_radius_pixels)
    {
        if (!(fill_alpha >= 0f && fill_alpha <= 1f)) throw new System.ArgumentOutOfRangeException(nameof(fill_alpha));
        if (!(corner_radius >= 1f && corner_radius <= 64f)) throw new System.ArgumentOutOfRangeException(nameof(corner_radius));
        RectTransform rect = panel(parent, name, Color.white);
        Image image = rect.GetComponent<Image>();
        image.sprite = outlined(fill_alpha);
        image.pixelsPerUnitMultiplier = corner_radius_pixels / corner_radius;
        return rect;
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
        rounded_sprite ??= sliced(coverage => new Color(1f, 1f, 1f, coverage.outer));
        return rounded_sprite;
    }

    private static Sprite outlined(float fill_alpha)
    {
        if (outlined_sprites.TryGetValue(fill_alpha, out Sprite? cached) && cached != null) return cached;
        var fill = new Color(surface.r, surface.g, surface.b, fill_alpha);
        Sprite sprite = sliced(coverage =>
        {
            Color color = Color.Lerp(outline, fill, coverage.inner);
            color.a *= coverage.outer;
            return color;
        });
        outlined_sprites[fill_alpha] = sprite;
        return sprite;
    }

    private static Sprite sliced(System.Func<(float outer, float inner), Color> shade)
    {
        const int size = corner_radius_pixels * 2 + 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float corner_x = Mathf.Clamp(x + 0.5f, corner_radius_pixels, size - corner_radius_pixels);
                float corner_y = Mathf.Clamp(y + 0.5f, corner_radius_pixels, size - corner_radius_pixels);
                float depth = corner_radius_pixels - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(corner_x, corner_y));
                texture.SetPixel(x, y, shade((Mathf.Clamp01(depth + 0.5f), Mathf.Clamp01(depth - outline_pixels + 0.5f))));
            }
        }
        texture.Apply();
        float border = corner_radius_pixels;
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
