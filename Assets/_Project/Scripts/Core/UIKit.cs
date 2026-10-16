using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 用程式碼建立 uGUI（Canvas / Text / Button）的小工具，主選單、暫停、Game Over、通關畫面共用。
/// 不用做 Prefab，也不用在場景裡事先擺 Canvas、EventSystem。
/// </summary>
public static class UIKit
{
    static Font font;

    public static Font DefaultFont
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    /// <summary>確保場景裡有 EventSystem（按鈕要有它才點得到）。專案只開新版 Input System，所以用 InputSystemUIInputModule</summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
        var module = go.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions(); // 滑鼠點擊、方向鍵 / Enter 選按鈕
        // 不跨場景保留：有些關卡自己就有 EventSystem，留著會變成兩個
    }

    /// <summary>建立一個 Screen Space Overlay 的 Canvas（以 1920x1080 為基準自動縮放）</summary>
    public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
    {
        EnsureEventSystem();
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (parent != null) go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    /// <summary>
    /// 建立一個 RectTransform。anchorMin / anchorMax 用 0~1 的比例指定位置，
    /// size 是額外的像素大小（anchor 兩點相同時就是元件大小）。
    /// </summary>
    public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                     Vector2 anchoredPos = default, Vector2 size = default)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>填滿父物件的色塊（背景、遮罩）</summary>
    public static Image Panel(string name, Transform parent, Color color)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static Image Box(string name, Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
    {
        var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static Text Label(string name, Transform parent, string text, int fontSize, Color color,
                             Vector2 anchoredPos, Vector2 size, TextAnchor alignment = TextAnchor.MiddleCenter,
                             FontStyle style = FontStyle.Normal)
    {
        var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPos, size);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = DefaultFont;
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    public static Button Button(string name, Transform parent, string text, Vector2 anchoredPos, Vector2 size,
                                UnityEngine.Events.UnityAction onClick, int fontSize = 34)
    {
        var bg = Box(name, parent, anchoredPos, size, Color.white);
        var button = bg.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = new Color(0.16f, 0.2f, 0.28f, 0.92f);
        colors.highlightedColor = new Color(0.32f, 0.42f, 0.6f, 1f);
        colors.pressedColor = new Color(0.55f, 0.45f, 0.2f, 1f);
        colors.selectedColor = new Color(0.32f, 0.42f, 0.6f, 1f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.targetGraphic = bg;
        if (onClick != null) button.onClick.AddListener(onClick);

        var outline = bg.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.82f, 0.42f, 0.7f);
        outline.effectDistance = new Vector2(2f, -2f);

        var label = Label("Text", bg.transform, text, fontSize, Color.white, Vector2.zero, size, TextAnchor.MiddleCenter,
                          FontStyle.Bold);
        label.verticalOverflow = VerticalWrapMode.Truncate;
        return button;
    }

    public static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
