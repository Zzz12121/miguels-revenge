using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 漁村的地圖（巡邏的村丁給的）：一張全村的空拍圖，加上每一區（漁港、市街、村長宅院、竹林石階、山頂祠堂）放大的空拍圖，
/// 圖上標出重要的地方和玩家現在的位置。
///   - 空拍圖是預先拍好的（編輯器選單 GeminiGrowUp/拍漁村地圖空拍圖，VillageMapCapture），存在 Resources/VillageMap：
///     Map_All.png、Map_&lt;區域&gt;.png，和記錄每張圖拍的範圍、地點位置的 VillageMap.json
///   - 拿到地圖 = 背包裡 VillageMap = 1（跟著換關卡、存檔）
///   - 在漁村按「村莊地圖」鍵（預設 N）打開：左邊是現在看的那張圖，右邊是區域清單（點一下、數字鍵 1~6、手把十字鍵切換），
///     Esc / 再按一次 N / 手把 B 關掉。打開時遊戲暫停。
///   - 有地圖的時候，畫面左下角（操作說明上面）擺著一張摺過的小紙地圖（程式畫的羊皮紙：摺痕、墨線畫的海岸、道路、房子和後山），
///     上面寫著打開的按鍵
/// </summary>
public static class VillageMap
{
    public const string ItemKey = "VillageMap";
    public const string SceneName = "JapaneseVillage";
    public static bool Owned => VillageWallet.Get(ItemKey) > 0;

    [System.Serializable]
    public class Region
    {
        public string id, name, desc, image;
        public float x0, z0, x1, z1; // 這張圖拍的世界範圍（x 往右、z 往上）
    }

    [System.Serializable]
    public class Poi
    {
        public string label, region;
        public float x, z;
    }

    [System.Serializable]
    public class Data
    {
        public Region overall;
        public Region[] regions;
        public Poi[] pois;
    }

    static Data data;
    static bool loaded;
    static readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { data = null; loaded = false; images.Clear(); }

    /// <summary>地圖資料（Resources/VillageMap/VillageMap.json）；還沒拍空拍圖時是 null</summary>
    public static Data Load()
    {
        if (loaded) return data;
        loaded = true;
        var json = Resources.Load<TextAsset>("VillageMap/VillageMap");
        if (json != null) data = JsonUtility.FromJson<Data>(json.text);
        return data;
    }

    public static Texture2D Image(Region r)
    {
        if (r == null || string.IsNullOrEmpty(r.image)) return null;
        if (!images.TryGetValue(r.image, out var tex) || tex == null)
        {
            tex = Resources.Load<Texture2D>("VillageMap/" + r.image);
            images[r.image] = tex;
        }
        return tex;
    }

    /// <summary>給玩家地圖（村丁）：回傳 true = 剛拿到</summary>
    public static bool Give()
    {
        if (Owned) return false;
        VillageWallet.Set(ItemKey, 1);
        ItemToast.Show("漁村地圖", 0, $"按【{InputBindings.DisplayName(GameAction.VillageMap)}】打開");
        GameAudio.Play("SFX/RPG/bookOpen", Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.7f, 1f, 0.05f, 0.3f);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        OnLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneName || Object.FindAnyObjectByType<VillageMapScreen>() != null) return;
        new GameObject("VillageMapScreen").AddComponent<VillageMapScreen>();
    }
}

/// <summary>巡邏的村丁（Walker_Watchman）：玩家還沒有地圖的話，講完台詞問要不要收下漁村的地圖</summary>
public class VillageMapGiver : MonoBehaviour
{
    MerchantNPC npc;
    bool talked;

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        if (npc == null) { Destroy(this); return; }
        npc.linesProvider = Lines;
        npc.choiceProvider = Choice;
    }

    string[] Lines()
    {
        var lines = new List<string>(!talked || npc.repeatLines == null || npc.repeatLines.Length == 0 ? npc.guideLines : npc.repeatLines);
        talked = true;
        if (!VillageMap.Owned)
            lines.Add("對了，你是外地來的吧？村子不大，可是巷子彎來彎去的。我這裡有一張全村的地圖，是從後山頂上畫下來的。");
        return lines.ToArray();
    }

    DialogueChoice Choice()
    {
        if (VillageMap.Owned) return null;
        return new DialogueChoice
        {
            question = "要收下漁村的地圖嗎？",
            options = new[] { "收下，謝謝", "不用了" },
            onPick = i =>
            {
                if (i != 0) return new[] { "好吧，迷路了再來找我。我每天都在主街上巡。" };
                VillageMap.Give();
                string key = InputBindings.DisplayName(GameAction.VillageMap);
                return new[]
                {
                    "拿去吧。漁港、市街、村長的宅院、後山的竹林和山頂的祠堂都畫在上面了。",
                    $"想看的時候按【{key}】打開。竹林裡那頭老虎的窩我也標了，沒事別靠近。",
                };
            },
        };
    }
}

/// <summary>地圖畫面（漁村場景自動建立）：VillageMap 的說明</summary>
public class VillageMapScreen : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    int current = -1; // -1 = 全村，0.. = regions
    float savedTimeScale = 1f;
    CursorLockMode savedLock;
    bool savedVisible, savedInput;
    ThirdPersonController controller;
    Transform player;
    float noticeUntil;
    string notice;
    GUIStyle labelStyle, titleStyle, descStyle, itemStyle;
    static Texture2D dot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsOpen = false;

    void Start()
    {
        // 村丁：給地圖
        var guard = GameObject.Find("Walker_Watchman");
        if (guard != null && guard.GetComponent<VillageMapGiver>() == null && guard.GetComponent<MerchantNPC>() != null)
            guard.AddComponent<VillageMapGiver>();
    }

    bool closeRequested;

    void OnDisable() { if (IsOpen) Close(); }

    void LateUpdate()
    {
        if (!closeRequested) return;
        closeRequested = false;
        if (IsOpen) Close();
    }

    void Update()
    {
        if (IsOpen)
        {
            var data = VillageMap.Load();
            int count = data != null && data.regions != null ? data.regions.Length : 0;
            var kb = Keyboard.current;
            if (InputBindings.Pressed(GameAction.VillageMap) || (kb != null && kb.escapeKey.wasPressedThisFrame) || PadInput.CancelPressed)
            {
                if (current >= 0 && !InputBindings.Pressed(GameAction.VillageMap)) current = -1; // 先回到全村
                else closeRequested = true; // LateUpdate 才關：這一幀的 Esc 不會讓 GameManager 接著打開暫停選單（時間還停著）
                return;
            }
            if (kb != null)
            {
                if (kb.digit0Key.wasPressedThisFrame || kb.backquoteKey.wasPressedThisFrame) current = -1;
                for (int i = 0; i < Mathf.Min(count, 9); i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) current = i;
            }
            var pad = Gamepad.current;
            if (pad != null && InputBindings.PadEnabled)
            {
                if (pad.dpad.down.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame) current = current + 1 >= count ? -1 : current + 1;
                if (pad.dpad.up.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame) current = current - 1 < -1 ? count - 1 : current - 1;
            }
            return;
        }

        if (!InputBindings.Pressed(GameAction.VillageMap) || GameUI.OverlayOpen || StoryDialogue.Active || Time.timeScale <= 0f) return;
        if (!VillageMap.Owned) { Notify("還沒有地圖（巡邏的村丁那裡拿得到）"); return; }
        if (VillageMap.Load() == null) { Notify("地圖還沒畫好（Resources/VillageMap 沒有空拍圖）"); return; }
        Open();
    }

    void Notify(string text) { notice = text; noticeUntil = Time.unscaledTime + 2f; }

    void Open()
    {
        var hp = PlayerHealth.Find();
        player = hp != null ? hp.transform : null;
        controller = hp != null ? hp.GetComponent<ThirdPersonController>() : null;
        IsOpen = true;
        current = -1;
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        savedLock = Cursor.lockState;
        savedVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (controller != null)
        {
            savedInput = controller.inputEnabled;
            controller.inputEnabled = false;
            controller.LockActions();
        }
        InteractPrompt.Block(this);
        GameAudio.Play("SFX/RPG/bookOpen", Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.6f, 1f, 0.05f, 0.3f);
    }

    void Close()
    {
        IsOpen = false;
        Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
        Cursor.lockState = savedLock;
        Cursor.visible = savedVisible;
        if (controller != null)
        {
            controller.inputEnabled = savedInput;
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
        }
        InteractPrompt.Unblock(this);
        GameAudio.Play("SFX/RPG/bookClose", Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.6f, 1f, 0.05f, 0.3f);
    }

    void OnGUI()
    {
        if (!IsOpen)
        {
            if (Time.unscaledTime < noticeUntil)
                GameUI.Shadowed(new Rect(0f, Screen.height * 0.7f, Screen.width, 30f), notice, GameUI.Center, Color.white);
            DrawPaperIcon();
            return;
        }
        var data = VillageMap.Load();
        if (data == null) return;
        GUI.depth = -1300;
        Styles();
        float sw = Screen.width, sh = Screen.height, s = sh / 1080f;

        // 背景：整個畫面暗下來
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // 右邊：區域清單
        float listW = 330f * s, pad = 28f * s;
        var list = new Rect(sw - listW - pad, pad, listW, sh - pad * 2f);
        GameUI.Panel(list, new Color(0.85f, 0.7f, 0.45f));
        float y = list.y + 18f * s;
        GUI.Label(new Rect(list.x + 18f * s, y, list.width - 36f * s, 40f * s), "漁村地圖", titleStyle);
        y += 52f * s;
        int count = data.regions != null ? data.regions.Length : 0;
        for (int i = -1; i < count; i++)
        {
            var r = i < 0 ? data.overall : data.regions[i];
            var row = new Rect(list.x + 12f * s, y, list.width - 24f * s, 44f * s);
            bool on = i == current;
            if (on) { GUI.color = new Color(1f, 0.85f, 0.5f, 0.28f); GUI.DrawTexture(row, Texture2D.whiteTexture); GUI.color = Color.white; }
            string num = i < 0 ? "0" : (i + 1).ToString();
            GUI.Label(row, $"  {num}　{r.name}", itemStyle);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)) { current = i; Event.current.Use(); }
            y += 48f * s;
        }
        var cur = current < 0 ? data.overall : data.regions[current];
        y += 10f * s;
        if (!string.IsNullOrEmpty(cur.desc))
            GUI.Label(new Rect(list.x + 18f * s, y, list.width - 36f * s, list.yMax - y - 70f * s), cur.desc, descStyle);
        string closeKey = InputBindings.DisplayName(GameAction.VillageMap);
        GUI.Label(new Rect(list.x + 18f * s, list.yMax - 60f * s, list.width - 36f * s, 50f * s),
            current < 0 ? $"【{closeKey}】/ Esc 關閉　數字鍵切換區域" : $"Esc 回全村　【{closeKey}】關閉", descStyle);

        // 左邊：空拍圖（照圖的比例放大到剩下的空間），地點和玩家
        var area = new Rect(pad, pad, list.x - pad * 2f, sh - pad * 2f);
        var tex = VillageMap.Image(cur);
        float aspect = Mathf.Max(0.01f, (cur.x1 - cur.x0) / Mathf.Max(0.01f, cur.z1 - cur.z0));
        float w = area.width, h = w / aspect;
        if (h > area.height) { h = area.height; w = h * aspect; }
        var img = new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);
        GameUI.Panel(new Rect(img.x - 6f * s, img.y - 6f * s, img.width + 12f * s, img.height + 12f * s), new Color(0.85f, 0.7f, 0.45f));
        if (tex != null) GUI.DrawTexture(img, tex, ScaleMode.StretchToFill);
        else GUI.Label(img, "（這一區還沒有空拍圖）", GameUI.Center);
        GUI.Label(new Rect(img.x + 14f * s, img.y + 10f * s, img.width, 40f * s), cur.name, titleStyle);

        dot ??= MakeDot();
        if (data.pois != null)
            foreach (var p in data.pois)
            {
                if (!ToScreen(cur, img, p.x, p.z, out var pt)) continue;
                float d = 14f * s;
                GUI.color = new Color(1f, 0.82f, 0.35f);
                GUI.DrawTexture(new Rect(pt.x - d * 0.5f, pt.y - d * 0.5f, d, d), dot);
                GUI.color = Color.white;
                var size = labelStyle.CalcSize(new GUIContent(p.label));
                var lr = new Rect(pt.x + d * 0.6f, pt.y - size.y * 0.5f, size.x, size.y);
                if (lr.xMax > img.xMax) lr.x = pt.x - d * 0.6f - size.x; // 太靠右：字寫在點的左邊
                GameUI.Shadowed(lr, p.label, labelStyle, Color.white);
            }
        // 玩家：紅色的點 + 面向
        if (player != null && ToScreen(cur, img, player.position.x, player.position.z, out var pp))
        {
            float d = 18f * s;
            Vector3 f = player.forward;
            Vector2 dir = new Vector2(f.x, -f.z).normalized;
            GUI.color = new Color(1f, 0.25f, 0.2f);
            for (int k = 1; k <= 3; k++)
                GUI.DrawTexture(new Rect(pp.x + dir.x * d * 0.45f * k - d * 0.18f, pp.y + dir.y * d * 0.45f * k - d * 0.18f, d * 0.36f, d * 0.36f), dot);
            GUI.DrawTexture(new Rect(pp.x - d * 0.5f, pp.y - d * 0.5f, d, d), dot);
            GUI.color = Color.white;
            GameUI.Shadowed(new Rect(pp.x + d * 0.7f, pp.y - 26f * s, 120f * s, 24f * s), "你在這裡", labelStyle, new Color(1f, 0.6f, 0.55f));
        }
    }

    // ---------------- 左下角的紙地圖 ----------------

    static Texture2D paper;
    GUIStyle iconLabelStyle;

    /// <summary>左下角（操作說明「F1」那一格上面）一張稍微斜放的摺紙地圖，旁邊寫打開的按鍵；劇情、選單、轉場時不畫</summary>
    void DrawPaperIcon()
    {
        if (!VillageMap.Owned || GameUI.OverlayOpen || StoryDialogue.Active || PaintingSlideshow.Active || Time.timeScale <= 0f) return;
        if (Event.current.type != EventType.Repaint) return;
        if (paper == null) paper = MakePaper();
        float s = Screen.height / 1080f;
        float w = 132f * s, h = w * paper.height / paper.width;
        // 操作說明收起來時在 Screen.height - 40（展開時從 - 91 開始）：紙放在它上面
        var r = new Rect(20f * s, Screen.height - 100f - h, w, h);
        var saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(-4f, r.center);
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(new Rect(r.x + 4f * s, r.y + 5f * s, r.width, r.height), paper); // 影子
        GUI.color = Color.white;
        GUI.DrawTexture(r, paper);
        GUI.matrix = saved;
        // 按鍵：紙的右下角一個小方塊，旁邊「地圖」
        if (iconLabelStyle == null) iconLabelStyle = new GUIStyle(GameUI.Small) { alignment = TextAnchor.MiddleLeft };
        iconLabelStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(17f * s));
        string key = InputBindings.DisplayName(GameAction.VillageMap);
        float kw = Mathf.Max(26f * s, iconLabelStyle.CalcSize(new GUIContent(key)).x + 12f * s);
        var kr = new Rect(r.xMax - kw * 0.6f, r.yMax - 20f * s, kw, 26f * s);
        GameUI.Key(kr, key);
        GameUI.Shadowed(new Rect(kr.xMax + 6f * s, kr.y, 80f * s, kr.height), "地圖", iconLabelStyle, new Color(0.96f, 0.9f, 0.78f));
    }

    /// <summary>
    /// 摺過的羊皮紙地圖（256 × 184）：米黃色的紙、邊緣不規則又比較暗（舊紙）、兩道直的和一道橫的摺痕（一邊亮一邊暗），
    /// 上面用墨線畫：下面一條彎彎的海岸線和海浪、中間幾條路和小房子、右邊村長宅院、上面一座山和石階、竹林、一個紅色的記號
    /// </summary>
    static Texture2D MakePaper()
    {
        const int W = 256, H = 184;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Runtime_PaperMap" };
        var px = new Color[W * H];
        Color paperC = new Color(0.9f, 0.83f, 0.66f), edgeC = new Color(0.62f, 0.48f, 0.3f), ink = new Color(0.25f, 0.18f, 0.12f), sea = new Color(0.3f, 0.42f, 0.5f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1), v = y / (float)(H - 1);
                // 邊緣：不規則地往內缺一點（撕過、磨過的紙）
                float edge = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                float rag = 3f + 3.5f * Mathf.PerlinNoise(x * 0.09f + y * 0.11f, (x + y) * 0.05f + 7f);
                float a = Mathf.Clamp01((edge - rag) * 0.8f);
                float n = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.25f + 30f, y * 0.25f) * 0.4f;
                Color c = Color.Lerp(paperC * 0.92f, paperC * 1.04f, n);
                c = Color.Lerp(c, edgeC, Mathf.Clamp01(1f - (edge - rag) / 18f) * 0.55f); // 邊緣比較暗（舊）
                // 摺痕：u = 1/3、2/3 直的，v = 1/2 橫的；摺痕一邊暗、一邊亮
                for (int fi = 1; fi <= 2; fi++)
                {
                    float d = (u - fi / 3f) * W;
                    if (Mathf.Abs(d) < 3f) c *= d < 0f ? 0.9f + 0.03f * Mathf.Abs(d) : 1.04f;
                }
                float dv = (v - 0.5f) * H;
                if (Mathf.Abs(dv) < 3f) c *= dv < 0f ? 0.92f + 0.025f * Mathf.Abs(dv) : 1.03f;
                // 海：下面的海岸線以下，淡淡的藍和一道道浪
                float coast = 0.22f + 0.05f * Mathf.Sin(u * 7f) + 0.025f * Mathf.Sin(u * 17f + 1f);
                if (v < coast)
                {
                    c = Color.Lerp(c, sea, 0.28f);
                    if (Mathf.Abs(Mathf.Repeat((v + 0.02f * Mathf.Sin(u * 40f)) * 26f, 1f) - 0.5f) < 0.07f && u > 0.08f && u < 0.92f) c = Color.Lerp(c, sea, 0.45f);
                }
                if (Mathf.Abs(v - coast) * H < 1.3f) c = Color.Lerp(c, ink, 0.85f);
                c.a = a;
                px[y * W + x] = c;
            }

        void Dot(float cx, float cy, float rad, Color col, float strength)
        {
            for (int y = Mathf.FloorToInt(cy - rad - 1); y <= cy + rad + 1; y++)
                for (int x = Mathf.FloorToInt(cx - rad - 1); x <= cx + rad + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float k = Mathf.Clamp01(rad + 0.5f - d) * strength;
                    if (k <= 0f) continue;
                    var o = px[y * W + x];
                    var m = Color.Lerp(o, col, k);
                    m.a = o.a;
                    px[y * W + x] = m;
                }
        }

        void Line(Vector2 a, Vector2 b, float width, Color col, float strength, bool dashed = false)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(len * 2f));
            for (int i = 0; i <= steps; i++)
            {
                float f = i / (float)steps;
                if (dashed && Mathf.Repeat(f * len / 6f, 1f) > 0.55f) continue;
                Dot(Mathf.Lerp(a.x, b.x, f), Mathf.Lerp(a.y, b.y, f), width * 0.5f, col, strength);
            }
        }

        // 路：海岸道路、主街（直的）、三條東西向的街
        Line(new Vector2(20, 52), new Vector2(236, 56), 2.2f, ink, 0.7f);
        Line(new Vector2(124, 56), new Vector2(128, 130), 2.4f, ink, 0.75f);
        foreach (float yy in new[] { 78f, 98f, 118f }) Line(new Vector2(40, yy), new Vector2(214, yy + 2f), 1.6f, ink, 0.55f);
        // 房子：每排幾個小方塊
        foreach (float yy in new[] { 70f, 88f, 108f })
            for (int k = 0; k < 6; k++)
            {
                float xx = 48f + k * 24f + (k > 2 ? 10f : 0f);
                for (int dx = 0; dx < 8; dx++) Line(new Vector2(xx + dx, yy), new Vector2(xx + dx, yy + 5f), 1f, ink, 0.35f);
            }
        // 村長宅院：右邊一個框
        Line(new Vector2(204, 70), new Vector2(232, 70), 1.4f, ink, 0.7f);
        Line(new Vector2(232, 70), new Vector2(232, 112), 1.4f, ink, 0.7f);
        Line(new Vector2(232, 112), new Vector2(204, 112), 1.4f, ink, 0.7f);
        Line(new Vector2(204, 112), new Vector2(204, 70), 1.4f, ink, 0.7f);
        // 後山：上面一座山（兩條斜線、兩道山稜），石階是虛線
        Line(new Vector2(70, 150), new Vector2(128, 176), 1.8f, ink, 0.75f);
        Line(new Vector2(128, 176), new Vector2(190, 148), 1.8f, ink, 0.75f);
        Line(new Vector2(100, 158), new Vector2(118, 168), 1.2f, ink, 0.45f);
        Line(new Vector2(150, 166), new Vector2(166, 158), 1.2f, ink, 0.45f);
        Line(new Vector2(128, 130), new Vector2(128, 168), 1.6f, ink, 0.7f, true);
        // 棧橋：海裡一條短線
        Line(new Vector2(140, 52), new Vector2(141, 26), 2.4f, ink, 0.7f);
        // 竹林：左上幾個小點
        for (int k = 0; k < 14; k++) Dot(48f + (k * 37 % 50), 132f + (k * 23 % 18), 1.3f, new Color(0.25f, 0.35f, 0.2f), 0.7f);
        // 紅色的記號（祠堂）
        Dot(128, 171, 3.2f, new Color(0.72f, 0.12f, 0.08f), 0.9f);
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static bool ToScreen(VillageMap.Region r, Rect img, float x, float z, out Vector2 pt)
    {
        float u = Mathf.InverseLerp(r.x0, r.x1, x), v = Mathf.InverseLerp(r.z0, r.z1, z);
        pt = new Vector2(img.x + u * img.width, img.y + (1f - v) * img.height);
        return x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1;
    }

    void Styles()
    {
        float s = Screen.height / 1080f;
        int fs(float v) => Mathf.Max(10, Mathf.RoundToInt(v * s));
        if (labelStyle != null && labelStyle.fontSize == fs(17)) return;
        labelStyle = new GUIStyle(GameUI.Small) { fontSize = fs(17), alignment = TextAnchor.MiddleLeft, wordWrap = false };
        titleStyle = new GUIStyle(GameUI.Title) { fontSize = fs(28), alignment = TextAnchor.UpperLeft };
        descStyle = new GUIStyle(GameUI.Small) { fontSize = fs(17), wordWrap = true, alignment = TextAnchor.UpperLeft };
        itemStyle = new GUIStyle(GameUI.Small) { fontSize = fs(20), alignment = TextAnchor.MiddleLeft };
    }

    static Texture2D MakeDot()
    {
        const int N = 32;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = new Vector2(x + 0.5f - N * 0.5f, y + 0.5f - N * 0.5f).magnitude / (N * 0.5f);
                float a = Mathf.Clamp01((1f - d) * N * 0.5f);
                // 外圈一道深色的邊，看得清楚
                Color c = d > 0.72f ? new Color(0.1f, 0.07f, 0.04f, a) : new Color(1f, 1f, 1f, a);
                t.SetPixel(x, y, c);
            }
        t.Apply();
        return t;
    }
}
