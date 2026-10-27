using System.Collections;
using UnityEngine;

/// <summary>
/// 關卡之間的轉場畫面（第一關 → 第二關、第二關 → 第三關）：一按下傳送門 / 船抵達終點就直接蓋上來，不會先黑一下。
/// 畫面從「原本關卡」的意境慢慢變成「下一關」的意境：
///   沙漠：風吹的沙粒、層層沙丘、暖橘色
///   維京海域：起伏的海浪、浪花、海藍色
///   地下城：石磚牆、往上飄的火星、暗紫色
/// 中間是旋轉的異次元漩渦 + 往外衝的光線（穿越的感覺），顏色也從原本關卡漸變成下一關；下方是目的地名稱和載入進度。
/// 全部用 OnGUI 畫，不需要任何素材；用 unscaled time，載入中也會動。
/// 由 GameManager.LoadRoutine 控制：Begin → 每幀設定 Progress → End（淡出到黑，再由 GameManager 淡入新關卡）。
/// </summary>
public class LevelTransition : MonoBehaviour
{
    public float fadeTime = 0.3f;
    [Tooltip("從原本關卡的意境變成下一關的時間 (s)")]
    public float morphTime = 2.2f;

    /// <summary>載入進度 0～1</summary>
    public float Progress { get; set; }
    public bool Showing => alpha > 0f;
    /// <summary>已經完全蓋住畫面（底下可以換成黑幕、開始切場景）</summary>
    public bool FullyShown => alpha >= 1f;

    string title, route, subtitle;
    int fromLevel, toLevel;
    float alpha;
    float startTime;
    Coroutine fading;

    static Texture2D ringTex, glowTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ringTex = glowTex = null;

    /// <summary>每一關的意境：主色、背景色</summary>
    static (Color accent, Color bg) Theme(int levelIndex)
    {
        switch (levelIndex)
        {
            case 0: return (new Color(1f, 0.68f, 0.3f), new Color(0.2f, 0.1f, 0.04f));     // 沙漠
            case 1: return (new Color(0.35f, 0.75f, 1f), new Color(0.02f, 0.07f, 0.16f));  // 維京海域
            case 2: return (new Color(0.72f, 0.4f, 1f), new Color(0.05f, 0.03f, 0.07f));   // 地下城
            default: return (new Color(1f, 0.8f, 0.4f), new Color(0.02f, 0.02f, 0.04f));
        }
    }

    /// <summary>每一段旅程的一句話（index = 目的地關卡）</summary>
    static string Line(int toLevel)
    {
        switch (toLevel)
        {
            case 1: return "沙粒化作海風……遠方傳來維京人的號角聲";
            case 2: return "海潮聲漸漸遠去……冰冷的石壁在黑暗中等待";
            default: return "穿越異次元……";
        }
    }

    public void Begin(string fromTitle, string destinationTitle, int fromIndex, int toIndex)
    {
        title = destinationTitle;
        route = string.IsNullOrEmpty(fromTitle) ? "" : $"{fromTitle}　›　{destinationTitle}";
        subtitle = Line(toIndex);
        fromLevel = fromIndex;
        toLevel = toIndex;
        Progress = 0f;
        startTime = Time.unscaledTime;
        if (fading != null) StopCoroutine(fading);
        fading = StartCoroutine(FadeTo(1f));
    }

    /// <summary>淡出轉場畫面（底下 GameManager 的黑幕還在，接著由它淡入新關卡）</summary>
    public IEnumerator End()
    {
        if (fading != null) StopCoroutine(fading);
        yield return FadeTo(0f);
    }

    IEnumerator FadeTo(float target)
    {
        float from = alpha, t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            alpha = Mathf.Lerp(from, target, t / fadeTime);
            yield return null;
        }
        alpha = target;
    }

    // ---------------- 畫面 ----------------

    static void EnsureTextures()
    {
        if (ringTex == null) ringTex = MakeRadial(256, r => Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2f)));
        if (glowTex == null) glowTex = MakeRadial(128, r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
    }

    static Texture2D MakeRadial(int size, System.Func<float, float> alphaAt)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(alphaAt(Mathf.Sqrt(dx * dx + dy * dy)));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    [System.NonSerialized] GUIStyle titleStyle, lineStyle, routeStyle;

    void OnGUI()
    {
        if (alpha <= 0f) return;
        GUI.depth = -1001; // 蓋在 GameManager 的黑幕上面
        EnsureTextures();
        float t = Time.unscaledTime - startTime;
        float w = Screen.width, h = Screen.height;
        // 原本關卡 → 下一關的混合比例
        float m = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.25f) / morphTime));
        var (fromAccent, fromBg) = Theme(fromLevel);
        var (toAccent, toBg) = Theme(toLevel);
        Color theme = Color.Lerp(fromAccent, toAccent, m);

        // 背景：兩關的底色漸變
        Color bg = Color.Lerp(fromBg, toBg, m);
        GUI.color = new Color(bg.r, bg.g, bg.b, alpha);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

        // 兩關的意境：原本的慢慢消失、下一關的慢慢浮現
        DrawScenery(fromLevel, (1f - m) * alpha, t, w, h);
        DrawScenery(toLevel, m * alpha, t, w, h);

        var center = new Vector2(w * 0.5f, h * 0.42f);
        float R = Mathf.Min(w, h) * 0.36f;
        Matrix4x4 baseMatrix = GUI.matrix;
        GUI.color = new Color(theme.r, theme.g, theme.b, 0.3f * alpha);
        GUI.DrawTexture(new Rect(center.x - R * 1.6f, center.y - R * 1.6f, R * 3.2f, R * 3.2f), glowTex);

        // 往外衝的光線（穿越的感覺）：固定的亂數種子，每條光線自己循環
        for (int i = 0; i < 80; i++)
        {
            float seed = i * 12.9898f;
            float angle = Hash(seed) * 360f;
            float speed = 0.35f + Hash(seed * 1.7f) * 0.6f;
            float phase = Frac(t * speed + Hash(seed * 3.1f));
            float d = phase * phase * R * 2.2f;                // 越外面越快
            float len = 6f + phase * phase * R * 0.5f;
            float a = Mathf.Clamp01(phase * 3f) * (1f - phase) * alpha;
            GUIUtility.RotateAroundPivot(angle, center);
            GUI.color = new Color(Mathf.Lerp(1f, theme.r, 0.5f), Mathf.Lerp(1f, theme.g, 0.5f), Mathf.Lerp(1f, theme.b, 0.5f), a);
            GUI.DrawTexture(new Rect(center.x + d, center.y - 1f, len, 2f), Texture2D.whiteTexture);
            GUI.matrix = baseMatrix;
        }

        // 漩渦：幾圈不同大小、不同方向旋轉、會脈動的光環；內圈先變成下一關的顏色
        for (int i = 0; i < 7; i++)
        {
            float k = i / 6f;
            float size = R * Mathf.Lerp(0.25f, 1.15f, k) * (1f + 0.04f * Mathf.Sin(t * 3f + i));
            float spin = (i % 2 == 0 ? 1f : -1f) * (40f + i * 18f) * t;
            float ringMix = Mathf.Clamp01(m * 1.6f - k * 0.6f);
            Color rc = Color.Lerp(fromAccent, toAccent, ringMix);
            GUIUtility.RotateAroundPivot(spin, center);
            // 光環壓扁一點、斜一點，看起來像立體的隧道
            GUIUtility.ScaleAroundPivot(new Vector2(1f, 0.82f), center);
            GUI.color = new Color(rc.r, rc.g, rc.b, (0.25f + 0.5f * (1f - k)) * alpha);
            GUI.DrawTexture(new Rect(center.x - size, center.y - size, size * 2f, size * 2f), ringTex);
            GUI.matrix = baseMatrix;
        }
        // 中央的亮點
        float core = R * (0.28f + 0.03f * Mathf.Sin(t * 5f));
        GUI.color = new Color(1f, 1f, 1f, 0.85f * alpha);
        GUI.DrawTexture(new Rect(center.x - core, center.y - core, core * 2f, core * 2f), glowTex);

        // 文字：路線、目的地和一句話
        titleStyle ??= new GUIStyle(GameUI.Title) { fontSize = 46, alignment = TextAnchor.MiddleCenter };
        lineStyle ??= new GUIStyle(GameUI.Center) { fontSize = 20 };
        routeStyle ??= new GUIStyle(GameUI.Center) { fontSize = 16 };
        float textA = Mathf.Clamp01((t - 0.3f) / 0.6f) * alpha;
        float ty = h * 0.74f;
        if (!string.IsNullOrEmpty(route))
            GameUI.Shadowed(new Rect(0, ty - 30f, w, 26f), route, routeStyle, new Color(1f, 1f, 1f, textA * 0.7f));
        GameUI.Shadowed(new Rect(0, ty, w, 60f), title, titleStyle, new Color(1f, 1f, 1f, textA));
        float lineA = Mathf.Clamp01((t - 0.9f) / 0.8f) * alpha;
        GameUI.Shadowed(new Rect(0, ty + 58f, w, 30f), subtitle, lineStyle,
                        new Color(Mathf.Lerp(1f, theme.r, 0.4f), Mathf.Lerp(1f, theme.g, 0.4f), Mathf.Lerp(1f, theme.b, 0.4f), lineA * 0.9f));

        // 載入進度
        float barW = Mathf.Min(520f, w * 0.5f);
        var bar = new Rect((w - barW) * 0.5f, ty + 104f, barW, 6f);
        GUI.color = new Color(1f, 1f, 1f, 0.15f * alpha);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(theme.r, theme.g, theme.b, alpha);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(Progress), bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // ---------------- 各關的意境 ----------------

    static void DrawScenery(int level, float a, float t, float w, float h)
    {
        if (a <= 0.01f) return;
        switch (level)
        {
            case 0: DrawDesert(a, t, w, h); break;
            case 1: DrawSea(a, t, w, h); break;
            case 2: DrawDungeon(a, t, w, h); break;
        }
    }

    /// <summary>沙漠：遠近三層沙丘 + 被風吹過畫面的沙粒</summary>
    static void DrawDesert(float a, float t, float w, float h)
    {
        for (int layer = 0; layer < 3; layer++)
        {
            float baseY = h * (0.66f + layer * 0.1f);
            Color c = Color.Lerp(new Color(0.55f, 0.3f, 0.12f), new Color(0.3f, 0.14f, 0.05f), layer / 2f);
            GUI.color = new Color(c.r, c.g, c.b, a * 0.9f);
            const float step = 8f;
            for (float x = 0f; x < w; x += step)
            {
                float y = baseY + Mathf.Sin(x * 0.004f + layer * 1.7f + t * 0.05f) * h * 0.05f
                                + Mathf.Sin(x * 0.011f + layer * 0.9f) * h * 0.015f;
                GUI.DrawTexture(new Rect(x, y, step + 1f, h - y), Texture2D.whiteTexture);
            }
        }
        for (int i = 0; i < 140; i++)
        {
            float seed = i * 7.31f;
            float speed = 0.12f + Hash(seed) * 0.25f;
            float x = Frac(Hash(seed * 2.3f) + t * speed) * (w + 60f) - 30f;
            float y = Hash(seed * 4.1f) * h + Mathf.Sin(t * 2f + seed) * 12f;
            float s = 1.5f + Hash(seed * 5.7f) * 3f;
            GUI.color = new Color(1f, 0.82f, 0.55f, a * (0.3f + Hash(seed * 6.9f) * 0.5f));
            GUI.DrawTexture(new Rect(x, y, s * 2.5f, s), Texture2D.whiteTexture); // 拉長一點：被風吹的感覺
        }
    }

    /// <summary>維京海域：四層起伏的海浪 + 浪頭的白沫 + 往上飄的水花</summary>
    static void DrawSea(float a, float t, float w, float h)
    {
        for (int layer = 0; layer < 4; layer++)
        {
            float baseY = h * (0.62f + layer * 0.085f);
            Color c = Color.Lerp(new Color(0.08f, 0.25f, 0.45f), new Color(0.02f, 0.08f, 0.2f), layer / 3f);
            const float step = 6f;
            for (float x = 0f; x < w; x += step)
            {
                float y = baseY + Mathf.Sin(x * 0.012f + t * (1.1f + layer * 0.35f) + layer * 2f) * 12f
                                + Mathf.Sin(x * 0.031f - t * 1.8f + layer) * 5f;
                GUI.color = new Color(c.r, c.g, c.b, a * 0.92f);
                GUI.DrawTexture(new Rect(x, y, step + 1f, h - y), Texture2D.whiteTexture);
                GUI.color = new Color(0.85f, 0.95f, 1f, a * (0.25f + 0.2f * Mathf.Sin(x * 0.05f + t * 3f)));
                GUI.DrawTexture(new Rect(x, y, step + 1f, 2f), Texture2D.whiteTexture);
            }
        }
        for (int i = 0; i < 70; i++)
        {
            float seed = i * 9.17f;
            float phase = Frac(t * (0.25f + Hash(seed) * 0.35f) + Hash(seed * 1.9f));
            float x = Hash(seed * 3.3f) * w + Mathf.Sin(t + seed) * 10f;
            float y = h * 0.75f - phase * h * 0.5f;
            float s = 2f + Hash(seed * 4.4f) * 3f;
            GUI.color = new Color(0.8f, 0.93f, 1f, a * (1f - phase) * 0.7f);
            GUI.DrawTexture(new Rect(x, y, s, s), Texture2D.whiteTexture);
        }
    }

    /// <summary>地下城：石磚牆的縫線 + 四周的暗角 + 往上飄的火星</summary>
    static void DrawDungeon(float a, float t, float w, float h)
    {
        const float rowH = 42f, brickW = 96f;
        int rows = Mathf.CeilToInt(h / rowH);
        for (int r = 0; r <= rows; r++)
        {
            float y = r * rowH;
            GUI.color = new Color(0.32f, 0.25f, 0.38f, a * 0.45f);
            GUI.DrawTexture(new Rect(0f, y, w, 2f), Texture2D.whiteTexture);
            float offset = r % 2 == 0 ? 0f : brickW * 0.5f;
            for (float x = offset; x < w; x += brickW)
                GUI.DrawTexture(new Rect(x, y, 2f, rowH), Texture2D.whiteTexture);
        }
        // 暗角：上下壓暗
        GUI.color = new Color(0f, 0f, 0f, a * 0.5f);
        GUI.DrawTexture(new Rect(0f, 0f, w, h * 0.12f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, h * 0.88f, w, h * 0.12f), Texture2D.whiteTexture);
        for (int i = 0; i < 60; i++)
        {
            float seed = i * 5.93f;
            float phase = Frac(t * (0.12f + Hash(seed) * 0.2f) + Hash(seed * 2.7f));
            float x = Hash(seed * 3.9f) * w + Mathf.Sin(t * 1.5f + seed) * 18f;
            float y = h * 1.05f - phase * h * 1.1f;
            float s = 2f + Hash(seed * 6.1f) * 2.5f;
            float flicker = 0.6f + 0.4f * Mathf.Sin(t * 12f + seed * 3f);
            GUI.color = new Color(1f, 0.55f + Hash(seed) * 0.3f, 0.2f, a * flicker * Mathf.Sin(phase * Mathf.PI));
            GUI.DrawTexture(new Rect(x, y, s, s), Texture2D.whiteTexture);
        }
    }

    static float Frac(float x) => x - Mathf.Floor(x);
    static float Hash(float seed) => Frac(Mathf.Sin(seed) * 43758.55f);
}
