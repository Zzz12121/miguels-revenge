using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 石碑上的碑文：玩家走近按 E，畫面中間展開一張卷軸顯示文字，再按 E / 空白鍵 / 滑鼠左鍵收起。
/// 閱讀時遊戲暫停、玩家不能動（跟劇情對話一樣）。
/// 跟 Checkpoint 掛在同一個物件上時，閱讀也會把這裡設成重生點。
/// </summary>
public class SteleInscription : MonoBehaviour
{
    [Tooltip("卷軸標題")]
    public string title = "古老的石碑";
    [Tooltip("卷軸內文（可以換行）")]
    [TextArea(4, 12)] public string text;
    [Tooltip("走到多近可以按 E 閱讀 (m)")]
    public float readRadius = 3.2f;
    public string promptText = "閱讀石碑";
    [Tooltip("卷軸用的字型（留空 = 用電腦裡的楷書：標楷體 / 楷體，都沒有就用預設字型）")]
    public Font font;
    [Tooltip("卷軸背景的紙張圖（留空 = 程式自動產生一張邊緣燒焦、撕破的舊羊皮紙）")]
    public Texture2D paperTexture;

    PlayerHealth playerHealth;
    ThirdPersonController controller;
    Checkpoint checkpoint;
    bool open;
    int openFrame;
    float openTime, prevTimeScale = 1f;

    const float UnrollTime = 0.35f;

    public static bool AnyOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { AnyOpen = false; osFont = null; oldPaper = null; }

    static Font osFont;
    static readonly string[] KaiFonts = { "DFKai-SB", "標楷體", "BiauKai", "KaiTi", "楷体", "STKaiti", "华文楷体" };

    /// <summary>電腦裡的楷書字型（不打包進遊戲，執行時向作業系統借用）</summary>
    static Font OSKaiFont()
    {
        if (osFont != null) return osFont;
        var installed = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames());
        foreach (var n in KaiFonts)
            if (installed.Contains(n)) return osFont = Font.CreateDynamicFontFromOSFont(n, BodySize);
        return null;
    }

    void Start()
    {
        playerHealth = PlayerHealth.Find();
        if (playerHealth != null) controller = playerHealth.GetComponent<ThirdPersonController>();
        checkpoint = GetComponent<Checkpoint>();
    }

    void Update()
    {
        if (open) { UpdateOpen(); return; }
        if (playerHealth == null || playerHealth.IsDead || StoryDialogue.Active || AnyOpen) return;

        Vector3 d = playerHealth.transform.position - transform.position;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist > readRadius) return;
        InteractPrompt.Offer(this, dist, promptText);
        if (InteractPrompt.Pressed(this)) Open();
    }

    void Open()
    {
        open = AnyOpen = true;
        openFrame = Time.frameCount;
        openTime = Time.unscaledTime;
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        if (controller != null)
        {
            controller.LockActions();
            controller.inputEnabled = false;
        }
        if (checkpoint != null) checkpoint.Activate();
        if (paperTexture == null) OldPaper(); // 第一次打開時先產生紙張，畫的時候才不會卡一下
        GameAudio.Play("SFX/RPG/bookOpen", Vector3.zero, 0.7f, 0.9f, 0f, 0f);
    }

    void UpdateOpen()
    {
        if (GameUI.OverlayOpen) return; // 暫停選單開著時不動
        Time.timeScale = 0f;             // 暫停選單關掉時會把時間恢復，這裡再停一次
        if (Time.frameCount == openFrame || Time.unscaledTime - openTime < UnrollTime) return;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        bool close = (kb != null && (kb[InteractPrompt.InteractKey].wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                  || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        if (close) Close();
    }

    void Close()
    {
        if (!open) return;
        open = AnyOpen = false;
        Time.timeScale = prevTimeScale;
        if (controller != null)
        {
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
            if (playerHealth == null || !playerHealth.IsDead) controller.inputEnabled = true;
        }
        GameAudio.Play("SFX/RPG/bookClose", Vector3.zero, 0.6f, 1f, 0f, 0f);
    }

    void OnDisable() => Close();

    // ---------------- 卷軸 ----------------

    static readonly Color Ink = new Color(0.22f, 0.13f, 0.08f);

    static Texture2D oldPaper;

    /// <summary>
    /// 程式產生的舊羊皮紙：米黃底色加上紙纖維的斑駁、四邊不規則撕破，撕口一圈燒焦成深褐色。
    /// </summary>
    static Texture2D OldPaper()
    {
        if (oldPaper != null) return oldPaper;
        const int W = 400, H = 500;
        var px = new Color32[W * H];
        var seed = new Vector2(Random.Range(0f, 100f), Random.Range(0f, 100f));
        var paper = new Color(0.87f, 0.73f, 0.5f);
        var burn = new Color(0.55f, 0.33f, 0.13f);
        var char_ = new Color(0.25f, 0.13f, 0.05f);

        float Noise(float x, float y, float f) => Mathf.PerlinNoise(seed.x + x * f, seed.y + y * f);

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                // 離最近的邊多遠（像素），邊緣用雜訊推進推出 = 撕破的形狀
                float d = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                float tear = 10f + Noise(u, v, 9f) * 22f + Noise(u, v, 38f) * 9f;
                float e = d - tear;
                if (e < 0f) { px[y * W + x] = new Color32(0, 0, 0, 0); continue; }

                // 紙纖維的斑駁 + 細顆粒
                float blot = Noise(u, v, 4f) * 0.6f + Noise(u, v, 13f) * 0.3f + Noise(u, v, 60f) * 0.1f;
                float grain = Noise(u, v, 160f) - 0.5f;
                var c = paper * (0.86f + blot * 0.22f + grain * 0.06f);
                // 四周微微變暗
                float vig = Mathf.Clamp01(1f - Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * 4f);
                c = Color.Lerp(c, burn, vig * vig * 0.25f);
                // 撕口一圈燒焦：越靠邊越深
                float b = 1f - Mathf.Clamp01(e / (10f + Noise(u, v, 20f) * 14f));
                c = Color.Lerp(c, burn, b * 0.85f);
                c = Color.Lerp(c, char_, Mathf.Pow(b, 3f) * 0.9f);
                c.a = Mathf.Clamp01(e / 1.5f);
                px[y * W + x] = c;
            }

        oldPaper = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "OldPaper" };
        oldPaper.SetPixels32(px);
        oldPaper.Apply(false, true);
        return oldPaper;
    }

    const int TitleSize = 34, BodySize = 26;
    const float CharWidth = 1.5f, LineHeight = 1.65f, BlankLine = 0.7f; // 內文一格多寬、一行多高、空行多高（× 字級）

    /// <summary>內文的總高度（× 字級）</summary>
    static float BodyLines(string[] lines)
    {
        float h = 0f;
        foreach (var l in lines) h += l.Length == 0 ? BlankLine : LineHeight;
        return h;
    }

    static GUIStyle Style(Font f, int size, TextAnchor align, bool wrap = false)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = align, wordWrap = wrap, richText = false };
        if (f != null) s.font = f;
        s.normal.textColor = Color.white;
        return s;
    }

    void OnGUI()
    {
        if (!open || GameUI.OverlayOpen) return;
        GUI.depth = -1150; // 蓋在 HUD、技能操作說明（-1100）上面，劇情對話（-1200）下面
        float k = Mathf.Clamp01((Time.unscaledTime - openTime) / UnrollTime);
        k = 1f - (1f - k) * (1f - k);

        var lines = (text ?? "").Replace("\r", "").Split('\n');
        // 畫面太矮時整張卷軸（字）跟著縮小，內容才放得下
        float ui = Mathf.Clamp((Screen.height - 110f) / (BodyLines(lines) * BodySize + 200f), 0.5f, 1f);
        int titleSize = Mathf.RoundToInt(TitleSize * ui), bodySize = Mathf.RoundToInt(BodySize * ui);

        var f = font != null ? font : OSKaiFont();
        // 動態字型的字圖集在畫到一半時重建，會讓部分字用到舊的貼圖位置、大小不一；先把要用的字都放進圖集
        if (f != null && Event.current.type == EventType.Layout)
        {
            f.RequestCharactersInTexture(title, titleSize);
            f.RequestCharactersInTexture(text, bodySize);
        }
        var titleStyle = Style(f, titleSize, TextAnchor.MiddleCenter);
        var charStyle = Style(f, bodySize, TextAnchor.MiddleCenter);
        var hintStyle = Style(null, Mathf.RoundToInt(15 * Mathf.Max(ui, 0.8f)), TextAnchor.MiddleRight);

        // 暗掉背景
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.45f * k);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        float w = Mathf.Min(620f * ui, Screen.width - 60f);
        float bodyH = BodyLines(lines) * bodySize;
        float fullH = bodyH + 200f * ui;
        float h = fullH;
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.47f;
        var paper = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);

        // 紙張：從稍微小一點放大、淡入；圖比內容區大一圈，撕破的邊才不會切到字
        float grow = Mathf.Lerp(0.88f, 1f, k);
        float pw = (w + 80f * ui) * grow, ph = (h + 70f * ui) * grow;
        GUI.color = new Color(1f, 1f, 1f, k);
        GUI.DrawTexture(new Rect(cx - pw * 0.5f, cy - ph * 0.5f, pw, ph), paperTexture != null ? paperTexture : OldPaper(), ScaleMode.StretchToFill, true);

        // 文字在展開後才淡入
        float a = Mathf.Clamp01((k - 0.7f) / 0.3f);
        if (a > 0f)
        {
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, a);
            GUI.Label(new Rect(paper.x, paper.y + 26f * ui, w, 48f * ui), title, titleStyle);
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, a * 0.5f);
            GUI.DrawTexture(new Rect(cx - w * 0.3f, paper.y + 82f * ui, w * 0.6f, 2f), Texture2D.whiteTexture);

            // 內文一個字一格、每格一樣寬（字距比較寬），每一行置中
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, a);
            float cell = bodySize * CharWidth, y = paper.y + 104f * ui;
            foreach (var line in lines)
            {
                if (line.Length == 0) { y += bodySize * BlankLine; continue; }
                float x = cx - line.Length * cell * 0.5f;
                for (int i = 0; i < line.Length; i++)
                    GUI.Label(new Rect(x + i * cell, y, cell, bodySize * LineHeight), line[i].ToString(), charStyle);
                y += bodySize * LineHeight;
            }

            // 收起提示寫在紙的右下角（放在紙外面會跟畫面下方的操作說明疊在一起）
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, a * 0.6f);
            GUI.Label(new Rect(paper.x, paper.yMax - 38f * ui, w - 26f, 26f), $"[{InteractPrompt.KeyName}] 收起卷軸", hintStyle);
        }
        GUI.color = prev;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.8f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, readRadius);
    }
}
