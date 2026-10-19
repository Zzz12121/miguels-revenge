using UnityEngine;

/// <summary>
/// 過關的大標題：畫面中央一條暗色橫帶，金色大字淡入、慢慢放大，停幾秒後淡出（類似魂系遊戲擊敗 Boss 的畫面）。
/// 同時播放勝利音效。不用事先放進場景，呼叫一次就會自己出現、播完自己消失：
///
///   VictoryBanner.Show("LEVEL COMPLETE", "");                  // 打倒 Boss：大標題（第二個字串是下面的小字，可以留空）
///   VictoryBanner.ShowSmall("COMPLETE", "房間已清空");          // 清完一間房間：比較小、比較快、位置偏上，不擋畫面
///   VictoryBanner.SlowMotion(0.25f, 0.9f);                     // 擊殺瞬間的慢動作（可以不用）
///
/// 全部用「真實時間」計時，所以慢動作或暫停都不會影響標題的節奏。
/// </summary>
public class VictoryBanner : MonoBehaviour
{
    static VictoryBanner current;
    static Texture2D bandTexture, lineTexture;

    string title, subtitle;
    Color color;
    AudioClip sound;
    float volume, delay;
    float fadeIn = 1.1f, hold = 3.6f, fadeOut = 1.6f;
    float size = 1f;          // 整體大小（1 = Boss 的大標題）
    float centerY = 0.42f;    // 標題中心在畫面高度的哪裡（0 = 最上面）
    float flash = 0.3f;       // 出現瞬間整個畫面閃一下的強度（0 = 不閃）
    float age;
    bool started;
    GUIStyle titleStyle, subStyle;
    int layoutW, layoutH, titleFontSize;   // 依照目前的畫面大小算好的字級和標題寬度（畫面大小變了才重算）
    float titleWidth;

    /// <summary>
    /// 顯示過關標題。delay = 幾秒後才出現；sound = 出現時播的音效（可以是 null）。
    /// 上一個標題還沒播完就再呼叫，會直接換成新的。
    /// </summary>
    /// <summary>馬上收掉正在顯示的標題（打開全螢幕選單時用，避免暗色橫帶蓋住選單）</summary>
    public static void HideCurrent()
    {
        if (current != null) Destroy(current.gameObject);
        current = null;
    }

    public static VictoryBanner Show(string title, string subtitle, Color color, float delay = 0f,
                                     AudioClip sound = null, float volume = 1f)
    {
        if (current != null) Destroy(current.gameObject);
        var go = new GameObject("VictoryBanner");
        current = go.AddComponent<VictoryBanner>();
        current.title = Spaced(title);
        current.subtitle = Spaced(subtitle);
        current.color = color;
        current.delay = Mathf.Max(delay, 0f);
        current.sound = sound;
        current.volume = volume;
        return current;
    }

    public static VictoryBanner Show(string title, string subtitle) =>
        Show(title, subtitle, new Color(1f, 0.82f, 0.42f));

    /// <summary>
    /// 小一號的版本（清完一間房間用）：字大約是大標題的一半、位置在畫面上方三分之一、不閃畫面，總共兩秒多就消失。
    /// 大標題正在顯示的時候不會蓋掉它。
    /// </summary>
    public static VictoryBanner ShowSmall(string title, string subtitle, Color color, AudioClip sound = null, float volume = 1f)
    {
        if (current != null && current.size >= 1f) return null;
        var banner = Show(title, subtitle, color, 0f, sound, volume);
        banner.size = 0.5f;
        banner.centerY = 0.3f;
        banner.flash = 0f;
        banner.fadeIn = 0.45f;
        banner.hold = 1.5f;
        banner.fadeOut = 0.7f;
        return banner;
    }

    public static VictoryBanner ShowSmall(string title, string subtitle) =>
        ShowSmall(title, subtitle, new Color(1f, 0.82f, 0.42f));

    /// <summary>字跟字之間插入空格，拉開字距（大標題看起來比較有份量）</summary>
    static string Spaced(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            if (i > 0) sb.Append(text[i] == ' ' ? "  " : " ");
            if (text[i] != ' ') sb.Append(text[i]);
        }
        return sb.ToString();
    }

    void Update()
    {
        if (slowOnly) { UpdateSlowMotion(); return; }

        age += Time.unscaledDeltaTime;
        if (!started && age >= delay)
        {
            started = true;
            age = delay;
            if (sound != null)
            {
                Vector3 at = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                GameAudio.PlayAt(sound, at, volume, 1f, 0f); // spatial 0 = 不管在哪裡都一樣大聲
            }
        }
        if (started && age - delay > fadeIn + hold + fadeOut) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (current == this) current = null;
        // 慢動作還沒結束就被清掉（例如切換場景）：把時間還原
        if (slowOurs) Time.timeScale = 1f;
    }

    void OnGUI()
    {
        if (slowOnly || !started || Event.current.type != EventType.Repaint) return;
        float t = age - delay;
        float total = fadeIn + hold + fadeOut;
        // 整體透明度：淡入 → 停留 → 淡出
        float alpha = t < fadeIn ? Mathf.SmoothStep(0f, 1f, t / fadeIn)
                    : t < fadeIn + hold ? 1f
                    : Mathf.SmoothStep(1f, 0f, (t - fadeIn - hold) / fadeOut);
        if (alpha <= 0f) return;

        EnsureAssets();
        GUI.depth = -1000; // 畫在其他 HUD 的上面
        float w = Screen.width, h = Screen.height;
        float cy = h * centerY;
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;

        // 一開始整個畫面閃一下
        if (flash > 0f && t < 0.5f)
        {
            GUI.color = new Color(color.r, color.g, color.b, flash * (1f - t / 0.5f));
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
        }

        // 暗色橫帶（上下邊緣漸層淡出）
        float bandH = h * 0.3f * size;
        GUI.color = new Color(0f, 0f, 0f, 0.82f * alpha);
        GUI.DrawTexture(new Rect(0f, cy - bandH * 0.5f, w, bandH), bandTexture);

        // 字級：標題太長的話自動縮小，不會超出畫面
        if (layoutW != Screen.width || layoutH != Screen.height) MeasureTitle(w, h);

        // 標題上下的細線：從中間往兩邊拉開，至少比標題寬一點
        float lineFull = Mathf.Min(Mathf.Max(w * 0.62f * Mathf.Lerp(0.6f, 1f, size), titleWidth * 1.1f), w * 0.96f);
        float lineW = lineFull * EaseOut(Mathf.Clamp01(t / (fadeIn * 1.3f)));
        float lineGap = h * 0.085f * size;
        float lineH = Mathf.Max(2f, h * 0.0028f);
        GUI.color = new Color(color.r, color.g, color.b, 0.9f * alpha);
        GUI.DrawTexture(new Rect((w - lineW) * 0.5f, cy - lineGap - lineH, lineW, lineH), lineTexture);
        GUI.DrawTexture(new Rect((w - lineW) * 0.5f, cy + lineGap, lineW, lineH), lineTexture);

        // 大標題：一開始比較大，縮到定位後繼續很慢地放大
        float scale = Mathf.Lerp(1.22f, 1f, EaseOut(Mathf.Clamp01(t / fadeIn))) + 0.05f * Mathf.Clamp01(t / total);
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(w * 0.5f, cy));
        titleStyle.fontSize = titleFontSize;
        var titleRect = new Rect(0f, cy - h * 0.1f, w, h * 0.2f);
        float glow = Mathf.Max(1.5f, h * 0.004f * size);
        // 光暈：同一行字往八個方向各偏一點、很淡地疊上去
        GUI.color = new Color(color.r, color.g * 0.75f, color.b * 0.4f, 0.11f * alpha);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            var r = titleRect;
            r.x += Mathf.Cos(a) * glow * 2f;
            r.y += Mathf.Sin(a) * glow * 2f;
            GUI.Label(r, title, titleStyle);
        }
        // 陰影
        GUI.color = new Color(0f, 0f, 0f, 0.75f * alpha);
        var shadow = titleRect;
        shadow.x += glow;
        shadow.y += glow;
        GUI.Label(shadow, title, titleStyle);
        // 本體
        GUI.color = new Color(color.r, color.g, color.b, alpha);
        GUI.Label(titleRect, title, titleStyle);
        GUI.matrix = oldMatrix;

        // 副標題：晚一點出現
        if (subtitle.Length > 0)
        {
            float subAlpha = alpha * Mathf.SmoothStep(0f, 1f, (t - fadeIn * 0.6f) / (fadeIn * 0.7f));
            subStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(h * 0.026f * Mathf.Lerp(0.55f, 1f, size)));
            var subRect = new Rect(0f, cy + lineGap + h * 0.012f * size, w, h * 0.05f * size);
            GUI.color = new Color(0f, 0f, 0f, 0.7f * subAlpha);
            GUI.Label(new Rect(subRect.x + 1.5f, subRect.y + 1.5f, subRect.width, subRect.height), subtitle, subStyle);
            GUI.color = new Color(0.92f, 0.88f, 0.8f, 0.9f * subAlpha);
            GUI.Label(subRect, subtitle, subStyle);
        }

        GUI.color = oldColor;
    }

    static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);

    /// <summary>算出標題的字級和寬度。標題比畫面的 86% 還寬就把字縮小到剛好</summary>
    void MeasureTitle(float w, float h)
    {
        layoutW = Screen.width;
        layoutH = Screen.height;
        titleFontSize = Mathf.Max(12, Mathf.RoundToInt(h * 0.1f * size));
        titleStyle.fontSize = titleFontSize;
        var content = new GUIContent(title);
        titleWidth = titleStyle.CalcSize(content).x;
        float limit = w * 0.86f;
        if (titleWidth > limit && titleWidth > 0f)
        {
            titleFontSize = Mathf.Max(12, Mathf.FloorToInt(titleFontSize * limit / titleWidth));
            titleStyle.fontSize = titleFontSize;
            titleWidth = titleStyle.CalcSize(content).x;
        }
    }

    void EnsureAssets()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow,
            };
            titleStyle.normal.textColor = Color.white; // 實際顏色用 GUI.color 控制
            subStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Normal };
        }
        if (bandTexture == null)
        {
            // 直的漸層：中間不透明、上下邊緣透明
            bandTexture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 64; y++)
            {
                float d = Mathf.Abs((y + 0.5f) / 64f * 2f - 1f);         // 0 = 中間，1 = 邊緣
                bandTexture.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.45f, 1f, d))));
            }
            bandTexture.Apply();
        }
        if (lineTexture == null)
        {
            // 橫的漸層：中間亮、兩端淡出
            lineTexture = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < 64; x++)
            {
                float d = Mathf.Abs((x + 0.5f) / 64f * 2f - 1f);
                lineTexture.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - d * d));
            }
            lineTexture.Apply();
        }
    }

    // =====================================================================
    //  慢動作（同一個元件的另一種用法：只控制時間，不畫東西）
    // =====================================================================

    bool slowOnly, slowOurs;
    float slowScale, slowSeconds, slowLastSet = -1f;

    /// <summary>
    /// 短暫的慢動作：時間先變成 scale 倍，再平滑回到正常，總共 seconds 秒（真實時間）。
    /// 如果這時候已經有別的東西在控制時間（暫停、其他技能的慢動作），就不插手。
    /// </summary>
    public static void SlowMotion(float scale, float seconds)
    {
        if (seconds <= 0f || scale >= 1f) return;
        if (Mathf.Abs(Time.timeScale - 1f) > 0.01f) return;
        var runner = new GameObject("VictorySlowMotion").AddComponent<VictoryBanner>();
        runner.slowOnly = true;
        runner.slowScale = Mathf.Clamp(scale, 0.05f, 1f);
        runner.slowSeconds = seconds;
    }

    void UpdateSlowMotion()
    {
        // 別的程式中途改了時間流速（例如暫停）→ 交給它，不再插手
        if (slowLastSet >= 0f && Mathf.Abs(Time.timeScale - slowLastSet) > 0.001f)
        {
            slowOurs = false;
            Destroy(gameObject);
            return;
        }

        age += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(age / slowSeconds);
        // 前 55% 維持最慢，之後平滑回到正常
        slowLastSet = Mathf.Lerp(slowScale, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, k)));
        Time.timeScale = slowLastSet;
        slowOurs = true;
        if (k >= 1f)
        {
            Time.timeScale = 1f;
            slowOurs = false;
            Destroy(gameObject);
        }
    }
}
