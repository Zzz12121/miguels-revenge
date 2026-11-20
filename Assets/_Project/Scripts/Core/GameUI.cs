using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 共用的 OnGUI 外觀：顏色、字型大小、面板、按鍵框。所有關卡 UI（互動提示、對話框、選單、HUD）都用這裡的樣式，
/// 看起來才會一致。只能在 OnGUI 裡呼叫（GUIStyle 需要 GUI.skin）。
/// </summary>
public static class GameUI
{
    public static readonly Color Accent = new Color(1f, 0.8f, 0.4f);        // 金色：標題、框線
    public static readonly Color PanelColor = new Color(0.04f, 0.035f, 0.03f, 0.82f);
    public static readonly Color TextColor = new Color(0.96f, 0.94f, 0.9f);
    public static readonly Color MutedColor = new Color(0.75f, 0.72f, 0.66f);
    public static readonly Color Good = new Color(0.55f, 1f, 0.6f);
    public static readonly Color Bad = new Color(1f, 0.5f, 0.45f);
    public static readonly Color Danger = new Color(1f, 0.25f, 0.2f);

    static GUIStyle title, body, small, keyCap, button, rightSmall, center;

    /// <summary>
    /// 專案關掉了「進入 Play 時重新載入腳本」（Enter Play Mode Options），static 變數會留到下一次 Play。
    /// 上一次 Play 建立的 GUIStyle 指向已經不存在的字型 / 按鈕圖，第二次 Play 開始文字和按鈕就會變成看不見（只剩黑底）。
    /// 每次進 Play 都清掉，讓它們重新建立。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        title = body = small = keyCap = button = rightSmall = center = null;
    }

    /// <summary>大標題（選單標題）</summary>
    public static GUIStyle Title => title ?? (title = new GUIStyle(GUI.skin.label)
        { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
    /// <summary>內文（對話、提示）</summary>
    public static GUIStyle Body => body ?? (body = new GUIStyle(GUI.skin.label)
        { fontSize = 20, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } });
    /// <summary>小字（HUD、說明）</summary>
    public static GUIStyle Small => small ?? (small = new GUIStyle(GUI.skin.label)
        { fontSize = 16, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } });
    public static GUIStyle SmallRight => rightSmall ?? (rightSmall = new GUIStyle(Small) { alignment = TextAnchor.MiddleRight });
    public static GUIStyle Center => center ?? (center = new GUIStyle(GUI.skin.label)
        { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
    /// <summary>按鍵框裡的字（F、Q…）</summary>
    public static GUIStyle KeyCap => keyCap ?? (keyCap = new GUIStyle(GUI.skin.label)
        { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } });
    /// <summary>選項按鈕</summary>
    public static GUIStyle Button => button ?? (button = new GUIStyle(GUI.skin.button)
        { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(14, 10, 6, 6) });

    /// <summary>深色半透明面板，上緣一條金線</summary>
    public static void Panel(Rect r, Color? line = null)
    {
        var prev = GUI.color;
        GUI.color = PanelColor;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = line ?? Accent;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // ---------------------------------------------------------------- 卷軸（對話框）

    static Texture2D scrollPaper, scrollBrocade, scrollRod, scrollKnob, scrollShade, scrollShadeV, scrollPainting;

    /// <summary>
    /// 對話框的背景：一幅卷軸的上半截，下半截在畫面外（紙一直延伸到畫面最下面）。
    /// 最上面一根橫的木軸、兩端圓圓的軸頭，下面一條織錦的裱布（天）夾兩道金線，紙的左右兩邊也是窄窄的裱布；
    /// 紙是一幅畫在舊絹上的水墨山水（遠山、霧、近山、松樹、月亮、幾隻飛鳥、金箔；整體偏暗），
    /// 字的那一帶再壓暗一點，白色、金色的字都看得清楚。
    /// r = 原本對話框的範圍（字照原本的位置排）。
    /// </summary>
    public static void ScrollPanel(Rect r)
    {
        EnsureScrollTextures();
        var prev = GUI.color;
        const float side = 9f, band = 13f, rodH = 13f, knob = 21f;
        float bottom = Screen.height + 2f;
        var paper = new Rect(r.x, r.y, r.width, bottom - r.y);

        // 左右的裱布
        GUI.color = new Color(1f, 1f, 1f, 0.97f);
        foreach (float x in new[] { paper.x - side, paper.xMax })
            GUI.DrawTextureWithTexCoords(new Rect(x, paper.y, side, paper.height), scrollBrocade, new Rect(0f, 0f, side / 24f, paper.height / 24f));
        // 紙：舊絹上的水墨山水（整張畫撐滿、裁掉多的部分）
        GUI.color = new Color(1f, 1f, 1f, 0.97f);
        GUI.DrawTexture(paper, scrollPainting, ScaleMode.ScaleAndCrop);
        // 字的那一帶（上面）壓暗一點，字才看得清楚
        GUI.color = new Color(0f, 0f, 0f, 0.34f);
        GUI.DrawTexture(new Rect(paper.x, paper.y, paper.width, Mathf.Min(150f, paper.height)), scrollShadeV);
        // 紙靠左右邊緣、上緣暗一點（微微捲起來的弧度、裱布的陰影）
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(new Rect(paper.x, paper.y, 26f, paper.height), scrollShade);
        GUI.DrawTextureWithTexCoords(new Rect(paper.xMax - 26f, paper.y, 26f, paper.height), scrollShade, new Rect(1f, 0f, -1f, 1f));
        GUI.color = new Color(0f, 0f, 0f, 0.3f);
        GUI.DrawTexture(new Rect(paper.x, paper.y, paper.width, 14f), scrollShadeV);
        // 金色的細線：紙和裱布的交界
        GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
        GUI.DrawTexture(new Rect(paper.x - 1f, paper.y, 1f, paper.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(paper.xMax, paper.y, 1f, paper.height), Texture2D.whiteTexture);

        // 上面的裱布（天）：織錦 + 上下兩道金線
        var top = new Rect(r.x - side, r.y - band, r.width + side * 2f, band);
        GUI.color = Color.white;
        GUI.DrawTextureWithTexCoords(top, scrollBrocade, new Rect(0f, 0f, top.width / 24f, top.height / 24f));
        GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
        GUI.DrawTexture(new Rect(top.x, top.yMax - 1.5f, top.width, 1.5f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(top.x, top.y + 2f, top.width, 1f), Texture2D.whiteTexture);

        // 木軸：比紙寬一點、圓柱的明暗；兩端的軸頭
        var rod = new Rect(top.x - 12f, top.y - rodH + 4f, top.width + 24f, rodH);
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(new Rect(rod.x + 2f, rod.y + 3f, rod.width, rod.height), scrollRod); // 影子
        GUI.color = Color.white;
        GUI.DrawTexture(rod, scrollRod);
        foreach (float x in new[] { rod.x - knob * 0.6f, rod.xMax - knob * 0.4f })
            GUI.DrawTexture(new Rect(x, rod.center.y - knob * 0.5f, knob, knob), scrollKnob);
        GUI.color = prev;
    }

    static void EnsureScrollTextures()
    {
        if (scrollPaper != null && scrollBrocade != null && scrollRod != null && scrollKnob != null && scrollShade != null && scrollShadeV != null && scrollPainting != null) return;
        scrollPainting = PaintLandscape(640, 192);
        // 紙：灰階的纖維紋路（橫的細絲 + 一點點斑點），乘上 GUI.color 的顏色
        scrollPaper = NewTex(256, 128, TextureWrapMode.Repeat);
        var rnd = new System.Random(11);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 256; x++)
            {
                float fiber = Mathf.PerlinNoise(x * 0.012f, y * 0.35f) * 0.6f + Mathf.PerlinNoise(x * 0.05f + 7f, y * 0.9f) * 0.25f;
                float speck = rnd.NextDouble() < 0.004 ? 0.25f : 0f;
                float v = Mathf.Clamp01(0.78f + fiber * 0.32f - speck);
                scrollPaper.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        scrollPaper.Apply();
        // 織錦：深紅底、金色的小菱形花紋（24 px 一格）
        scrollBrocade = NewTex(24, 24, TextureWrapMode.Repeat);
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                int dx = Mathf.Abs(x - 12), dy = Mathf.Abs(y - 12);
                bool diamond = dx + dy == 8 || dx + dy == 9;
                bool dot = dx + dy <= 1;
                Color c = new Color(0.34f, 0.08f, 0.07f, 1f);
                if (diamond) c = new Color(0.62f, 0.45f, 0.2f, 1f);
                else if (dot) c = new Color(0.7f, 0.52f, 0.25f, 1f);
                else c *= 0.9f + 0.1f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
                c.a = 1f;
                scrollBrocade.SetPixel(x, y, c);
            }
        scrollBrocade.Apply();
        // 木軸：上下方向的圓柱明暗（中間偏上最亮）
        scrollRod = NewTex(4, 16, TextureWrapMode.Clamp);
        for (int y = 0; y < 16; y++)
        {
            float v = y / 15f; // 0 = 下, 1 = 上
            float light = Mathf.Sin(Mathf.Clamp01(v * 1.05f) * Mathf.PI);
            float hi = Mathf.Exp(-Mathf.Pow((v - 0.68f) / 0.1f, 2f));
            Color c = Color.Lerp(new Color(0.16f, 0.09f, 0.05f), new Color(0.42f, 0.26f, 0.14f), light) + new Color(0.18f, 0.14f, 0.1f) * hi;
            c.a = 1f;
            for (int x = 0; x < 4; x++) scrollRod.SetPixel(x, y, c);
        }
        scrollRod.Apply();
        // 軸頭：圓的、有一點立體感（左上亮、右下暗），邊緣柔和
        scrollKnob = NewTex(32, 32, TextureWrapMode.Clamp);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                Vector2 d = new Vector2(x - 15.5f, y - 15.5f) / 15.5f;
                float r = d.magnitude;
                float a = Mathf.Clamp01((1f - r) * 12f);
                float shade = Mathf.Clamp01(0.55f + 0.45f * Vector2.Dot(-d, new Vector2(0.6f, -0.8f)) * (1f - r * 0.3f));
                Color c = Color.Lerp(new Color(0.18f, 0.1f, 0.06f), new Color(0.55f, 0.36f, 0.18f), shade);
                if (r > 0.78f) c = Color.Lerp(c, new Color(0.75f, 0.58f, 0.3f), Mathf.Clamp01((r - 0.78f) * 8f) * (1f - Mathf.Clamp01((r - 0.9f) * 10f))); // 金色的環
                c.a = a;
                scrollKnob.SetPixel(x, 31 - y, c);
            }
        scrollKnob.Apply();
        // 陰影：從左（不透明）到右（透明）的漸層
        scrollShade = NewTex(32, 1, TextureWrapMode.Clamp);
        for (int x = 0; x < 32; x++) scrollShade.SetPixel(x, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - x / 31f, 2f)));
        scrollShade.Apply();
        // 上緣的陰影：上（不透明）到下（透明）
        scrollShadeV = NewTex(1, 32, TextureWrapMode.Clamp);
        for (int y = 0; y < 32; y++) scrollShadeV.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(y / 31f, 2f)));
        scrollShadeV.Apply();
    }

    /// <summary>
    /// 一幅原創的水墨山水（程式畫的，深色的舊絹底）：
    ///   天空是舊絹的顏色（越上面越亮一點、細細的纖維），右上角一輪淡淡的月亮和幾隻飛鳥；
    ///   遠山一層淡墨、山腳化進一條橫的霧；中景的山濃一點，有乾筆的直紋；左下角近處的山坡最黑，上面幾棵松樹；
    ///   零星的金箔。v = 0 是下面
    /// </summary>
    static Texture2D PaintLandscape(int w, int h)
    {
        var tex = NewTex(w, h, TextureWrapMode.Clamp);
        var px = new Color[w * h];
        float aspect = (float)w / h;
        float Fbm(float x, float seed)
        {
            float a = 0.5f, f = 1f, sum = 0f, norm = 0f;
            for (int o = 0; o < 4; o++) { sum += a * Mathf.PerlinNoise(x * f, seed + o * 17.3f); norm += a; a *= 0.5f; f *= 2.1f; }
            return sum / norm;
        }
        // 松樹：在近山的山坡上（u 位置、高度）
        var pines = new[] { (0.05f, 0.2f), (0.11f, 0.26f), (0.17f, 0.17f), (0.24f, 0.13f) };
        // 飛鳥：右上角、月亮旁邊（u, v, 大小）
        var birds = new[] { (0.66f, 0.8f, 0.012f), (0.69f, 0.84f, 0.009f), (0.715f, 0.79f, 0.01f), (0.62f, 0.86f, 0.008f) };
        var rnd = new System.Random(5);
        Color silkLo = new Color(0.2f, 0.152f, 0.11f), silkHi = new Color(0.31f, 0.245f, 0.175f);
        Color far = new Color(0.2f, 0.165f, 0.13f), mist = new Color(0.33f, 0.28f, 0.21f), mid = new Color(0.1f, 0.08f, 0.065f), near = new Color(0.055f, 0.042f, 0.034f);
        Color moonCol = new Color(0.56f, 0.5f, 0.38f), gold = new Color(0.62f, 0.48f, 0.24f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1), v = (float)y / (h - 1);
                float fiber = 0.92f + 0.09f * Mathf.PerlinNoise(x * 0.012f, y * 0.4f) + 0.05f * Mathf.PerlinNoise(x * 0.09f + 5f, y * 0.9f);
                Color c = Color.Lerp(silkLo, silkHi, Mathf.SmoothStep(0f, 1f, v)) * fiber;

                // 月亮（右上）：淡淡的光暈 + 月面
                float md = new Vector2((u - 0.8f) * aspect, v - 0.74f).magnitude;
                c = Color.Lerp(c, moonCol, Mathf.Clamp01(1f - md / 0.32f) * 0.12f);
                if (md < 0.1f) c = Color.Lerp(c, moonCol * (0.95f + 0.08f * Mathf.PerlinNoise(u * 60f, v * 60f)), 0.75f * Mathf.Clamp01((0.1f - md) * 60f));

                // 遠山：淡墨的山稜，山腳慢慢化掉
                float rf = 0.5f + 0.22f * (Fbm(u * 2.6f + 1.3f, 3.1f) - 0.5f) * 2f;
                if (v < rf) c = Color.Lerp(c, far, 0.6f * Mathf.SmoothStep(rf - 0.3f, rf - 0.01f, v));
                // 橫的一條霧
                c = Color.Lerp(c, mist, 0.42f * Mathf.Exp(-Mathf.Pow((v - 0.32f) / 0.07f, 2f)) * (0.7f + 0.3f * Mathf.PerlinNoise(u * 6f, 1.7f)));

                // 中景的山：右邊高、乾筆的直紋，山腳也化進霧裡
                float rm = 0.27f + 0.3f * Fbm(u * 1.9f + 7f, 8.4f) * Mathf.SmoothStep(0.15f, 0.75f, u);
                if (v < rm)
                {
                    float brush = 0.65f + 0.35f * Mathf.PerlinNoise(u * 55f, v * 5f);
                    float k = Mathf.SmoothStep(rm - 0.24f, rm - 0.01f, v) * brush;
                    c = Color.Lerp(c, mid, 0.85f * k);
                }

                // 左下角近處的山坡（最黑）
                float rn = 0.24f + 0.08f * Fbm(u * 3.5f + 3f, 1.1f) - 0.42f * u;
                if (v < rn) c = Color.Lerp(c, near, 0.9f * Mathf.Clamp01((rn - v) * 40f));

                // 松樹：直的樹幹 + 一層層往上變小的橫向樹冠
                foreach (var (pu, ph) in pines)
                {
                    float baseV = 0.24f + 0.08f * Fbm(pu * 3.5f + 3f, 1.1f) - 0.42f * pu - 0.01f;
                    float du = (u - pu) * aspect;
                    if (v > baseV && v < baseV + ph && Mathf.Abs(du) < 0.006f) c = Color.Lerp(c, near, 0.9f);
                    // 松枝：一層層扁扁寬寬、往上越短，邊緣不規則（像一團團的松針）
                    for (int tier = 0; tier < 5; tier++)
                    {
                        float tv = baseV + ph * (0.3f + tier * 0.16f);
                        float tw = 0.075f * (1f - tier * 0.16f) * (0.8f + 0.4f * Mathf.PerlinNoise(pu * 9f, tier));
                        float shift = (tier % 2 == 0 ? 1f : -1f) * 0.012f;                        // 左右交錯
                        float ragged = 0.75f + 0.5f * Mathf.PerlinNoise(u * 90f, tier * 3.1f);   // 不規則的邊
                        float e = Mathf.Pow((du - shift) / (tw * ragged), 2f) + Mathf.Pow((v - tv) / 0.02f, 2f);
                        if (e < 1f) c = Color.Lerp(c, near, 0.92f * Mathf.Clamp01((1f - e) * 4f) * (0.75f + 0.25f * Mathf.PerlinNoise(u * 120f, v * 40f)));
                    }
                }

                // 飛鳥：兩筆往上翹的短線（「く」倒過來）
                foreach (var (bu, bv, bs) in birds)
                {
                    float du = (u - bu) * aspect, dv = v - bv;
                    float wing = Mathf.Abs(du);
                    if (wing < bs && Mathf.Abs(dv - wing * 0.6f) < 0.0045f) c = Color.Lerp(c, mid, 0.85f);
                }

                // 零星的金箔
                if (rnd.NextDouble() < 0.0004) c = Color.Lerp(c, gold, 0.4f);
                c.a = 1f;
                px[y * w + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D NewTex(int w, int h, TextureWrapMode wrap) =>
        new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };

    /// <summary>
    /// 選項卡片（取代 GUI.Button）：背景、滑鼠移上去的高亮、文字顏色全部自己畫，點擊用透明按鈕判斷。
    /// 不用 GUI.skin.button 的外觀：它的背景圖有時候還沒載入，第一次打開選單時按鈕會變成透明、字變暗。
    /// 回傳 true = 這一下被點到了。enabled = false 時畫成半透明（仍然可以點，由呼叫的人決定要不要理）。
    /// </summary>
    public static bool Option(Rect r, bool enabled = true)
    {
        var prev = GUI.color;
        bool hover = r.Contains(Event.current.mousePosition);
        float a = enabled ? 1f : 0.5f;
        GUI.color = new Color(1f, 1f, 1f, (hover ? 0.16f : 0.07f) * a);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        // 外框：平常淡淡的，滑鼠移上去變金色
        GUI.color = hover ? new Color(Accent.r, Accent.g, Accent.b, a) : new Color(1f, 1f, 1f, 0.18f * a);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
        GUI.color = prev;
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    /// <summary>按鍵框：金底黑字的小方塊</summary>
    public static void Key(Rect r, string key, float alpha = 1f)
    {
        var prev = GUI.color;
        GUI.color = new Color(Accent.r, Accent.g, Accent.b, alpha);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(r, key, KeyCap);
        GUI.color = prev;
    }

    /// <summary>有陰影的文字（直接畫在場景上，沒有面板時用）</summary>
    public static void Shadowed(Rect r, string text, GUIStyle style, Color color)
    {
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f * color.a);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        GUI.color = color;
        GUI.Label(r, text, style);
        GUI.color = prev;
    }

    /// <summary>進度條（喝藥水等）</summary>
    public static void ProgressBar(Rect r, float t, Color fill)
    {
        var prev = GUI.color;
        GUI.color = PanelColor;
        GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    /// <summary>畫面中間偏上的大訊息（檢查點、能力提升…）</summary>
    public static void Banner(string text, Color color, float alpha = 1f)
    {
        var style = new GUIStyle(Title) { fontSize = 28 };
        Shadowed(new Rect(0f, Screen.height * 0.32f, Screen.width, 44f), text, style, new Color(color.r, color.g, color.b, alpha));
    }

    /// <summary>有沒有任何全螢幕選單（暫停 / Game Over）開著</summary>
    public static bool OverlayOpen
    {
        get
        {
            var gm = GameManager.Instance;
            return RangeInputSettings.InputBlocked || (gm != null && gm.IsOverlayOpen);
        }
    }
}

/// <summary>
/// 統一的互動提示「[E] 撿起能力植物」。所有可以互動的東西（能力植物、草藥、營火、商人）都用這個：
///   - 在 Update 裡玩家在範圍內就呼叫 Offer(this, 距離, 文字)
///   - 只有最近的那一個會顯示提示，也只有它的 Pressed(this) 會是 true，
///     所以同一下 E 不會同時觸發兩個東西（例如草藥長在營火旁邊）
/// 提示固定畫在畫面下方中央。
/// </summary>
public class InteractPrompt : MonoBehaviour
{
    /// <summary>第一關所有互動的按鍵</summary>
    public const Key InteractKey = Key.E;
    /// <summary>互動鍵在畫面上顯示的名稱（照設定裡的按鍵；用手把時顯示手把按鍵）</summary>
    public static string KeyName => InputBindings.DisplayName(GameAction.Interact);

    static InteractPrompt host;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { host = null; blockers.Clear(); player = null; } // 沒有重新載入腳本時，上一次 Play 的物件已經不在了

    // 演出、動畫進行中（過場鏡頭、劇情對話、玩家被鎖住不能操作的時候）：不顯示任何互動提示，按 E 也不會觸發
    static readonly System.Collections.Generic.HashSet<object> blockers = new System.Collections.Generic.HashSet<object>();
    static ThirdPersonController player;

    /// <summary>開始演出：owner 結束前（Unblock）所有互動提示都不出現</summary>
    public static void Block(object owner) { if (owner != null) blockers.Add(owner); }
    public static void Unblock(object owner) { if (owner != null) blockers.Remove(owner); }

    /// <summary>現在不能互動：有演出擋著、劇情對話中，或玩家被演出鎖住（不能移動）</summary>
    public static bool Blocked
    {
        get
        {
            blockers.RemoveWhere(o => o == null || (o is Object uo && uo == null));
            if (blockers.Count > 0 || StoryDialogue.Active) return true;
            if (player == null) player = FindAnyObjectByType<ThirdPersonController>();
            return player != null && player.isActiveAndEnabled && !player.inputEnabled;
        }
    }
    // 這一幀收集中的候選 / 上一幀選出來的
    object pendingOwner, focusedOwner;
    float pendingDistance;
    string pendingText, focusedText;
    bool pendingEnabled, focusedEnabled;
    int pendingFrame = -1;

    static InteractPrompt Host
    {
        get
        {
            if (host == null)
            {
                host = FindAnyObjectByType<InteractPrompt>();
                if (host == null) host = new GameObject("InteractPrompt").AddComponent<InteractPrompt>();
            }
            return host;
        }
    }

    /// <summary>
    /// 玩家在範圍內時每幀呼叫。distance 用來決定多個互動物件重疊時顯示誰。
    /// enabled = false 時仍顯示提示（灰色），但按 E 不會有反應（例如「植物沒有反應」）
    /// </summary>
    public static void Offer(object owner, float distance, string text, bool enabled = true)
    {
        if (Blocked) return;
        var h = Host;
        if (h.pendingFrame != Time.frameCount || distance < h.pendingDistance)
        {
            h.pendingFrame = Time.frameCount;
            h.pendingOwner = owner;
            h.pendingDistance = distance;
            h.pendingText = text;
            h.pendingEnabled = enabled;
        }
    }

    /// <summary>這一幀 owner 是不是被選中的互動對象，而且剛按下 E</summary>
    public static bool Pressed(object owner)
    {
        if (!InputBindings.Pressed(GameAction.Interact) || GameUI.OverlayOpen || Blocked) return false; // 預設 E / 手把 Y
        var h = Host;
        return h.focusedOwner == owner && h.focusedEnabled;
    }

    void LateUpdate()
    {
        // 這一幀收集到的最近候選，成為下一幀的焦點
        bool fresh = pendingFrame == Time.frameCount;
        focusedOwner = fresh ? pendingOwner : null;
        focusedText = fresh ? pendingText : null;
        focusedEnabled = fresh && pendingEnabled;
    }

    void OnGUI()
    {
        if (focusedOwner == null || string.IsNullOrEmpty(focusedText) || GameUI.OverlayOpen) return;
        var style = new GUIStyle(GameUI.Center) { fontSize = 19, alignment = TextAnchor.MiddleLeft };
        var size = style.CalcSize(new GUIContent(focusedText));
        string keyName = KeyName;
        float keyBoxW = Mathf.Max(26f, GameUI.Small.CalcSize(new GUIContent(keyName)).x + 12f);
        float keyW = focusedEnabled ? keyBoxW + 4f : 0f;
        float w = size.x + keyW + 40f, h = 44f;
        var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.72f, w, h);

        GameUI.Panel(r, focusedEnabled ? GameUI.Accent : GameUI.MutedColor);
        float x = r.x + 16f;
        if (focusedEnabled)
        {
            GameUI.Key(new Rect(x, r.y + 9f, keyBoxW, 26f), keyName);
            x += keyW + 4f;
        }
        GUI.color = focusedEnabled ? GameUI.TextColor : GameUI.MutedColor;
        GUI.Label(new Rect(x, r.y, size.x + 8f, h), focusedText, style);
        GUI.color = Color.white;
    }
}
