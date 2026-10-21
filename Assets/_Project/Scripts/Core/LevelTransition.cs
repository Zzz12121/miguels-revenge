using System.Collections;
using UnityEngine;

/// <summary>
/// 關卡之間的轉場畫面（第一關 → 第二關、第二關 → 第三關）：載入下一關時不再是一片黑，
/// 而是旋轉的異次元漩渦 + 往外衝的光線（穿越的感覺）、目的地名稱和載入進度。
/// 全部用 OnGUI 畫，不需要任何素材；用 unscaled time，載入中也會動。
/// 由 GameManager.LoadRoutine 控制：Begin → 每幀設定 Progress → End（淡出到黑，再由 GameManager 淡入新關卡）。
/// </summary>
public class LevelTransition : MonoBehaviour
{
    public float fadeTime = 0.45f;

    /// <summary>載入進度 0～1</summary>
    public float Progress { get; set; }
    public bool Showing => alpha > 0f;

    string title, subtitle;
    Color theme = Color.white;
    float alpha;
    float startTime;
    Coroutine fading;

    static Texture2D ringTex, glowTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ringTex = glowTex = null;

    /// <summary>每一關的轉場配色和一句話（index = 目的地關卡）</summary>
    static (Color color, string line) Theme(int levelIndex)
    {
        switch (levelIndex)
        {
            case 1: return (new Color(0.35f, 0.75f, 1f), "沙粒化作海風……遠方傳來維京人的號角聲");
            case 2: return (new Color(0.75f, 0.35f, 1f), "海潮聲漸漸遠去……冰冷的石壁在黑暗中等待");
            default: return (new Color(1f, 0.8f, 0.4f), "穿越異次元……");
        }
    }

    public void Begin(string destinationTitle, int levelIndex)
    {
        title = destinationTitle;
        (theme, subtitle) = Theme(levelIndex);
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

    [System.NonSerialized] GUIStyle titleStyle, lineStyle;

    void OnGUI()
    {
        if (alpha <= 0f) return;
        GUI.depth = -1001; // 蓋在 GameManager 的黑幕上面
        EnsureTextures();
        float t = Time.unscaledTime - startTime;
        float w = Screen.width, h = Screen.height;
        var center = new Vector2(w * 0.5f, h * 0.44f);
        float R = Mathf.Min(w, h) * 0.42f;
        Matrix4x4 baseMatrix = GUI.matrix;

        // 背景：黑底 + 中央一團主題色的光
        GUI.color = new Color(0.02f, 0.02f, 0.04f, alpha);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = new Color(theme.r, theme.g, theme.b, 0.35f * alpha);
        GUI.DrawTexture(new Rect(center.x - R * 1.6f, center.y - R * 1.6f, R * 3.2f, R * 3.2f), glowTex);

        // 往外衝的光線（穿越的感覺）：固定的亂數種子，每條光線自己循環
        for (int i = 0; i < 90; i++)
        {
            float seed = i * 12.9898f;
            float angle = Frac(Mathf.Sin(seed) * 43758.55f) * 360f;
            float speed = 0.35f + Frac(Mathf.Sin(seed * 1.7f) * 24634.63f) * 0.6f;
            float phase = Frac(t * speed + Frac(Mathf.Sin(seed * 3.1f) * 1931.1f));
            float d = phase * phase * R * 2.2f;                // 越外面越快
            float len = 6f + phase * phase * R * 0.5f;
            float a = Mathf.Clamp01(phase * 3f) * (1f - phase) * alpha;
            GUIUtility.RotateAroundPivot(angle, center);
            GUI.color = new Color(Mathf.Lerp(1f, theme.r, 0.5f), Mathf.Lerp(1f, theme.g, 0.5f), Mathf.Lerp(1f, theme.b, 0.5f), a);
            GUI.DrawTexture(new Rect(center.x + d, center.y - 1f, len, 2f), Texture2D.whiteTexture);
            GUI.matrix = baseMatrix;
        }

        // 漩渦：幾圈不同大小、不同方向旋轉、會脈動的光環
        for (int i = 0; i < 7; i++)
        {
            float k = i / 6f;
            float size = R * Mathf.Lerp(0.25f, 1.15f, k) * (1f + 0.04f * Mathf.Sin(t * 3f + i));
            float spin = (i % 2 == 0 ? 1f : -1f) * (40f + i * 18f) * t;
            GUIUtility.RotateAroundPivot(spin, center);
            // 光環壓扁一點、斜一點，看起來像立體的隧道
            GUIUtility.ScaleAroundPivot(new Vector2(1f, 0.82f), center);
            GUI.color = new Color(theme.r, theme.g, theme.b, (0.25f + 0.5f * (1f - k)) * alpha);
            GUI.DrawTexture(new Rect(center.x - size, center.y - size, size * 2f, size * 2f), ringTex);
            GUI.matrix = baseMatrix;
        }
        // 中央的亮點
        float core = R * (0.28f + 0.03f * Mathf.Sin(t * 5f));
        GUI.color = new Color(1f, 1f, 1f, 0.85f * alpha);
        GUI.DrawTexture(new Rect(center.x - core, center.y - core, core * 2f, core * 2f), glowTex);

        // 文字：目的地和一句話
        titleStyle ??= new GUIStyle(GameUI.Title) { fontSize = 46, alignment = TextAnchor.MiddleCenter };
        lineStyle ??= new GUIStyle(GameUI.Center) { fontSize = 20 };
        float textA = Mathf.Clamp01((t - 0.4f) / 0.8f) * alpha;
        GameUI.Shadowed(new Rect(0, h * 0.76f, w, 60f), title, titleStyle, new Color(1f, 1f, 1f, textA));
        GameUI.Shadowed(new Rect(0, h * 0.76f + 58f, w, 30f), subtitle, lineStyle,
                        new Color(Mathf.Lerp(1f, theme.r, 0.4f), Mathf.Lerp(1f, theme.g, 0.4f), Mathf.Lerp(1f, theme.b, 0.4f), textA * 0.9f));

        // 載入進度
        float barW = Mathf.Min(520f, w * 0.5f);
        var bar = new Rect((w - barW) * 0.5f, h * 0.76f + 104f, barW, 6f);
        GUI.color = new Color(1f, 1f, 1f, 0.15f * alpha);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(theme.r, theme.g, theme.b, alpha);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(Progress), bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static float Frac(float x) => x - Mathf.Floor(x);
}
