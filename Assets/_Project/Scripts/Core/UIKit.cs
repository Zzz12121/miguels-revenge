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

    /// <summary>
    /// 漁村風格（GameManager 在漁村主線開暫停選單時打開，關掉選單時關回來）：
    /// 字變成墨色（原本淺色的字自動換成墨色）、陰影變成淡淡的紙色，按鈕是紙上的淡墨框、選到 / 滑鼠移上去變朱紅；
    /// 背景用 HangingScroll 畫一幅掛軸（跟對話框同一套牛皮紙、裱布、木軸）
    /// </summary>
    public static bool Washi;

    static readonly Color WashiInk = new Color(0.17f, 0.11f, 0.065f);
    static readonly Color WashiCinnabar = new Color(0.6f, 0.14f, 0.08f);

    /// <summary>切換鈕（設定選單的分頁）沒選 / 選了的底色</summary>
    public static Color TabOff => Washi ? new Color(WashiInk.r, WashiInk.g, WashiInk.b, 0.08f) : new Color(0.16f, 0.2f, 0.28f, 0.92f);
    public static Color TabOn => Washi ? new Color(WashiCinnabar.r, WashiCinnabar.g, WashiCinnabar.b, 0.45f) : new Color(0.85f, 0.62f, 0.2f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Washi = false;

    /// <summary>
    /// 一幅直的掛軸（漁村風格的選單背景）：中間是牛皮紙，上下兩段深紅褐色的裱布、外面一圈裱布的邊，上下各一根木軸、兩端軸頭。
    /// center / size = 紙加裱布的範圍（Canvas 1920x1080 座標）
    /// </summary>
    public static RectTransform HangingScroll(Transform parent, Vector2 center, Vector2 size)
    {
        var rootRt = Rect("HangingScroll", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), center, size);
        RawImage Raw(string n, Transform p, Texture tex, Vector2 pos, Vector2 sz, Rect uv, Color c)
        {
            var rt = Rect(n, p, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, sz);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.uvRect = uv;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }
        var silk = GameUI.ScrollSilk;
        float w = size.x, h = size.y, band = Mathf.Min(70f, h * 0.09f), edge = 14f;
        // 影子、裱布（整幅）、紙
        Raw("Shadow", rootRt, Texture2D.whiteTexture, new Vector2(8f, -10f), size, new Rect(0, 0, 1, 1), new Color(0f, 0f, 0f, 0.45f));
        Raw("Silk", rootRt, silk, Vector2.zero, size, new Rect(0f, 0f, w / 32f, h / 32f), Color.white);
        var paperSize = new Vector2(w - edge * 2f, h - band * 2f);
        Raw("Paper", rootRt, GameUI.KraftPaper, Vector2.zero, paperSize, new Rect(0f, 0f, 1f, 1f), Color.white);
        // 紙和裱布交界的金線
        var gold = new Color(1f, 0.8f, 0.4f, 0.55f);
        foreach (float y in new[] { paperSize.y * 0.5f, -paperSize.y * 0.5f })
            Raw("Gold", rootRt, Texture2D.whiteTexture, new Vector2(0f, y), new Vector2(paperSize.x, 2f), new Rect(0, 0, 1, 1), gold);
        // 上下的木軸、軸頭
        foreach (float y in new[] { h * 0.5f + 6f, -h * 0.5f - 6f })
        {
            Raw("RodShadow", rootRt, GameUI.ScrollRod, new Vector2(3f, y - 4f), new Vector2(w + 50f, 26f), new Rect(0, 0, 1, 1), new Color(0f, 0f, 0f, 0.35f));
            Raw("Rod", rootRt, GameUI.ScrollRod, new Vector2(0f, y), new Vector2(w + 50f, 26f), new Rect(0, 0, 1, 1), Color.white);
            foreach (float x in new[] { -(w + 50f) * 0.5f - 12f, (w + 50f) * 0.5f + 12f })
                Raw("Knob", rootRt, GameUI.ScrollKnob, new Vector2(x, y), new Vector2(40f, 40f), new Rect(0, 0, 1, 1), Color.white);
        }
        return rootRt;
    }

    /// <summary>朱紅的方印（漁村風格的選單標題旁邊）：紅底、白字</summary>
    public static void Seal(Transform parent, string text, Vector2 pos, float size)
    {
        var box = Box("Seal", parent, pos, new Vector2(size, size), new Color(WashiCinnabar.r, WashiCinnabar.g, WashiCinnabar.b, 0.9f));
        box.raycastTarget = false;
        box.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -6f);
        bool was = Washi;
        Washi = false; // 印裡的字是白的，不要變墨色
        var t = Label("SealText", box.transform, text, Mathf.RoundToInt(size * 0.6f), new Color(1f, 0.93f, 0.85f), Vector2.zero, new Vector2(size, size),
                      TextAnchor.MiddleCenter, FontStyle.Bold);
        Washi = was;
        var sh = t.GetComponent<Shadow>();
        if (sh != null) sh.enabled = false;
    }

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
        if (Washi)
        {
            // 淺色的字寫在紙上看不到：換成墨色（保留透明度）；陰影變成淡淡的紙色（像墨滲進紙裡）
            float lum = 0.3f * color.r + 0.59f * color.g + 0.11f * color.b;
            if (lum > 0.55f) t.color = new Color(WashiInk.r, WashiInk.g, WashiInk.b, color.a);
            shadow.effectColor = new Color(1f, 0.93f, 0.8f, 0.25f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }
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
        if (Washi)
        {
            // 紙上的淡墨框；選到（手把 / 方向鍵）或滑鼠移上去變朱紅
            colors.normalColor = new Color(WashiInk.r, WashiInk.g, WashiInk.b, 0.07f);
            colors.highlightedColor = new Color(WashiCinnabar.r, WashiCinnabar.g, WashiCinnabar.b, 0.28f);
            colors.selectedColor = new Color(WashiCinnabar.r, WashiCinnabar.g, WashiCinnabar.b, 0.28f);
            colors.pressedColor = new Color(WashiCinnabar.r, WashiCinnabar.g, WashiCinnabar.b, 0.5f);
        }
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.targetGraphic = bg;
        if (onClick != null) button.onClick.AddListener(onClick);

        var outline = bg.gameObject.AddComponent<Outline>();
        outline.effectColor = Washi ? new Color(WashiInk.r, WashiInk.g, WashiInk.b, 0.45f) : new Color(1f, 0.82f, 0.42f, 0.7f);
        outline.effectDistance = Washi ? new Vector2(1.5f, -1.5f) : new Vector2(2f, -2f);

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
