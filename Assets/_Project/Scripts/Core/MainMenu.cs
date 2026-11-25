using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 主選單（MainMenu 場景）：水墨風格的標題畫面（構圖參考 Hades II：標題、選項在左，主角在右）。
///   - 左邊：「Miguel's Revenge」標題（「Miguel」被三道朱紅的爪痕抓破、「Revenge」被一刀斜斜砍開）、副標「神使之復仇」，
///     下面三個選項：開始遊戲、選項、離開遊戲
///   - 右邊：主角舉刀（第三批人物照的去背圖），背後一團潑墨、一輪朱紅色的日，和一尊巨大的道士水墨法相（拿巨筆的道士，巨筆劃過紅日）
///   - 背景：舊宣紙上淡淡的遊戲裡的古畫（祠堂的海上鬥龍、百足現世、與民同醉）和弔玄清的詩，墨點慢慢飄
///   - 進場：墨從中間暈開 → 日、道士法相、主角浮現 → 標題浮現 → 一爪抓破 Miguel → 一刀砍開 Revenge（標題震一下）→ 選項依序出現；
///     潑墨和法相一直慢慢呼吸、主角輕輕起伏
///   - 選到的選項後面刷上一筆朱紅（從左往右刷開），滑鼠移上去、方向鍵 / W S、手把十字鍵或左搖桿選，Enter / 空白鍵 / 手把 A 確定
///   - 開始遊戲：主畫面上跳出「選擇存檔」的面板（皮紙上三塊破布 = 三個存檔欄，SaveSystem），每一欄寫著存在哪裡、玩了多久、
///     總共倒下幾次、上次存檔是因為什麼（「和村長說完話」「打倒了雙尾虎」）、什麼時候存的；空的寫「空白／開始新的旅程」。
///     選一欄（滑鼠點、左右鍵 / 十字鍵 + Enter / A）→ 墨潑滿整個畫面，接著進入遊戲（GameManager.StartSlot：空的 = 開新遊戲，有存檔 = 接著玩）。
///     選到 / 移上去的那塊染成朱紅、被風吹，右上角有「清除」（按兩次才刪；鍵盤 Delete / 手把 Y）；Esc / 手把 B / 「返回」關掉面板
///   - 選項：和遊戲中暫停選單的「設定」同一個畫面（ControlsSettingsMenu）
/// 圖都在 Resources/MainMenu（Art/MainMenu/make_menu_art.py 產生）；畫面全部用程式碼產生，場景裡不用事先擺東西。
/// 在編輯器裡直接從漁村（JapaneseVillage）按 Play 時，會先跳到這個主畫面（選單 GeminiGrowUp/主畫面/直接測試漁村 可以關掉）。
/// </summary>
public class MainMenu : MonoBehaviour
{
    public Color backgroundColor = new Color(0.88f, 0.83f, 0.72f);

    const string Art = "MainMenu/";
    /// <summary>編輯器：直接從漁村按 Play 時不跳到主畫面（GeminiGrowUp/主畫面/直接測試漁村）</summary>
    public const string SkipVillageRedirectKey = "MainMenu.SkipVillageRedirect";
    /// <summary>編輯器工具用：下一次進 Play 不跳到主畫面（只算一次，例如拍漁村地圖空拍圖時要留在漁村）</summary>
    public const string SkipRedirectOnceKey = "MainMenu.SkipRedirectOnce";

    Canvas canvas;
    RectTransform root;
    float t0;
    bool leaving;
    float leaveAt;
    int selected;
    float strokeFill;
    int lastSelected = -1;

    // 圖層
    RawImage paper, inkWash, inkWash2, sun, taoist, hero, title, titleCut1, titleCut2, subtitle, cover;
    Image claws, slash;
    // 標題四件組顯示的高度（make_menu_art.py 的 TITLE_DISPLAY_HEIGHT）：字跟以前一樣大，上下多留空間給爪痕、刀痕
    const float TitleHeight = 270f;
    // 版面（1920x1080、左上原點的像素）
    const float TitleY = 250f, SubtitleY = 355f, ItemY0 = 560f, ItemStep = 120f;
    const float ClawAt = 2.1f, SlashAt = 2.55f;
    readonly List<(RawImage img, Vector2 pos, float alpha, float phase)> paintings = new List<(RawImage, Vector2, float, float)>();
    readonly List<Particle> particles = new List<Particle>();
    Item[] items;

    class Item
    {
        public RawImage label;
        public Image stroke;
        public RectTransform rect;
        public Vector2 home;
        public System.Action action;
    }

    class Particle
    {
        public RawImage img;
        public Vector2 pos, vel;
        public float life, age, size;
    }

    // ---------------- 自動建立 / 從漁村跳過來 ----------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // 遊戲一開始就在漁村（編輯器裡直接從 JapaneseVillage 按 Play）：先到主畫面，按「開始遊戲」才進村子
        var first = SceneManager.GetActiveScene();
        string menuScene = GameManager.Instance != null ? GameManager.Instance.mainMenuScene : "MainMenu";
        string village = GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.StoryStartScene) ? GameManager.Instance.StoryStartScene : "JapaneseVillage";
        bool skip = false;
#if UNITY_EDITOR
        skip = PlayerPrefs.GetInt(SkipVillageRedirectKey, 0) == 1;
        if (PlayerPrefs.GetInt(SkipRedirectOnceKey, 0) == 1)
        {
            skip = true;
            PlayerPrefs.DeleteKey(SkipRedirectOnceKey);
            PlayerPrefs.Save();
        }
#endif
        if (!skip && first.name == village && Application.CanStreamedLevelBeLoaded(menuScene))
        {
            SceneManager.LoadScene(menuScene);
            return;
        }
        OnSceneLoaded(first, LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string menuScene = GameManager.Instance != null ? GameManager.Instance.mainMenuScene : "MainMenu";
        if (scene.name != menuScene || FindAnyObjectByType<MainMenu>() != null) return;
        new GameObject("MainMenu").AddComponent<MainMenu>();
    }

    void Start()
    {
        Time.timeScale = 1f;
        UIKit.ShowCursor();
        EnsureCamera();
        Build();
        t0 = Time.unscaledTime;
        if (GameManager.Instance != null) GameManager.Instance.PlaySceneMusic();
    }

    void EnsureCamera()
    {
        if (Camera.main != null) return;
        var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        go.tag = "MainCamera";
        var cam = go.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
    }

    // ---------------- 畫面 ----------------

    static Texture2D Tex(string name)
    {
        var t = Resources.Load<Texture2D>(Art + name);
        if (t == null) Debug.LogWarning($"[MainMenu] 找不到 Resources/{Art}{name}（執行 python Art/MainMenu/make_menu_art.py）");
        return t;
    }

    /// <summary>一個圖層：中心在 (cx, cy)（1920x1080、左上原點的像素），高 height（寬照圖的比例）</summary>
    RawImage Layer(string name, string tex, float cx, float cy, float height, float alpha = 1f)
    {
        var t = Tex(tex);
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(root, false);
        var img = go.GetComponent<RawImage>();
        img.texture = t;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, alpha);
        var rt = img.rectTransform;
        float w = t != null ? height * t.width / t.height : height;
        rt.sizeDelta = new Vector2(w, height);
        rt.anchoredPosition = Px(cx, cy);
        return img;
    }

    static Vector2 Px(float x, float y) => new Vector2(x - 960f, 540f - y);

    void Build()
    {
        canvas = UIKit.CreateCanvas("MainMenuCanvas", 10, transform);
        root = (RectTransform)canvas.transform;

        // 紙：鋪滿整個畫面
        paper = Layer("Paper", "Paper", 960f, 540f, 1080f);
        paper.rectTransform.anchorMin = Vector2.zero;
        paper.rectTransform.anchorMax = Vector2.one;
        paper.rectTransform.sizeDelta = Vector2.zero;
        paper.rectTransform.anchoredPosition = Vector2.zero;
        if (paper.texture == null) paper.color = backgroundColor;

        // 古畫（跟 make_menu_art.py 的 LAYOUT_PAINTINGS 一樣）
        foreach (var (tex, cx, cy, h, a) in new[]
        {
            ("Paint_SeaDragon", 1640f, 190f, 460f, 0.42f),
            ("Paint_Centipede", 640f, 900f, 480f, 0.24f),
            ("Paint_Poem", 190f, 470f, 820f, 0.06f),
            ("Paint_Feast", 1080f, 960f, 300f, 0.18f),
        })
        {
            var img = Layer(tex, tex, cx, cy, h, 0f);
            paintings.Add((img, Px(cx, cy), a, Random.value * 10f));
        }

        // 主角背後：兩層潑墨（一層慢慢轉，看起來墨還在流動）、朱紅的日、主角
        inkWash2 = Layer("InkWash2", "InkWash", 1380f, 580f, 1150f, 0f);
        inkWash = Layer("InkWash", "InkWash", 1390f, 560f, 1250f, 0f);
        sun = Layer("RedSun", "RedSun", 1420f, 300f, 430f, 0f);
        // 道士的水墨法相（跟 make_menu_art.py 的 TAOIST_LAYOUT 一樣）：身體在主角正後方，巨筆往右上劃過紅日
        taoist = Layer("TaoistInk", "TaoistInk", 1560f, 690f, 1350f, 0f);
        hero = Layer("Hero", "Hero", 1400f, 640f, 1250f, 0f);

        // 墨點粒子
        var dot = Tex("InkDot");
        for (int i = 0; i < 28; i++)
        {
            var go = new GameObject("InkMote", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(root, false);
            var img = go.GetComponent<RawImage>();
            img.texture = dot;
            img.raycastTarget = false;
            var p = new Particle { img = img };
            Respawn(p, true);
            particles.Add(p);
        }

        // 左邊：標題（乾淨的 → 被抓破的 → 再被砍開的，三張疊在同一個位置換）、爪痕、刀痕、副標
        title = Layer("Title", "Title", 540f, TitleY, TitleHeight, 0f);
        titleCut1 = Layer("TitleCut1", "TitleCut1", 540f, TitleY, TitleHeight, 0f);
        titleCut2 = Layer("TitleCut2", "TitleCut2", 540f, TitleY, TitleHeight, 0f);
        claws = Filled("TitleClaws", Image.FillMethod.Vertical, (int)Image.OriginVertical.Top);       // 從上往下一爪抓下去
        slash = Filled("TitleSlash", Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left); // 從左往右一刀砍過去
        subtitle = Layer("Subtitle", "Subtitle", 470f, SubtitleY, 64f, 0f);

        // 三個選項：後面一筆朱紅（選到才刷開）
        var strokeTex = Tex("BrushStroke");
        Sprite strokeSprite = strokeTex != null ? Sprite.Create(strokeTex, new Rect(0, 0, strokeTex.width, strokeTex.height), new Vector2(0.5f, 0.5f)) : null;
        string[] names = { "Menu_Start", "Menu_Options", "Menu_Quit" };
        System.Action[] actions = { OnStart, OnOptions, OnQuit };
        items = new Item[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            float y = ItemY0 + i * ItemStep;
            var strokeGo = new GameObject("Stroke_" + names[i], typeof(RectTransform), typeof(Image));
            strokeGo.transform.SetParent(root, false);
            var stroke = strokeGo.GetComponent<Image>();
            stroke.sprite = strokeSprite;
            stroke.type = Image.Type.Filled;
            stroke.fillMethod = Image.FillMethod.Horizontal;
            stroke.fillOrigin = (int)Image.OriginHorizontal.Left;
            stroke.fillAmount = 0f;
            stroke.raycastTarget = false;
            stroke.color = new Color(1f, 1f, 1f, 0.9f);
            stroke.rectTransform.sizeDelta = new Vector2(560f, 105f);
            stroke.rectTransform.anchoredPosition = Px(380f, y);
            var label = Layer(names[i], names[i], 330f, y, 92f, 0f);
            items[i] = new Item { label = label, stroke = stroke, rect = label.rectTransform, home = Px(330f, y), action = actions[i] };
        }

        BuildSlotPanel();

        // 開始遊戲時潑滿整個畫面的墨（平常看不到）
        cover = Layer("InkCover", "InkWash", 960f, 540f, 1400f, 0f);
        cover.color = new Color(0f, 0f, 0f, 0f);
    }

    /// <summary>跟標題一樣大、疊在同一個位置的 Filled 圖（爪痕、刀痕：fillAmount 0 → 1 就是抓 / 砍的動作）</summary>
    Image Filled(string tex, Image.FillMethod method, int origin)
    {
        var t = Tex(tex);
        var go = new GameObject(tex, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);
        var img = go.GetComponent<Image>();
        if (t != null) img.sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
        img.type = Image.Type.Filled;
        img.fillMethod = method;
        img.fillOrigin = origin;
        img.fillAmount = 0f;
        img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(t != null ? TitleHeight * t.width / t.height : TitleHeight, TitleHeight);
        img.rectTransform.anchoredPosition = Px(540f, TitleY);
        return img;
    }

    void Respawn(Particle p, bool anywhere)
    {
        // 從主角背後那團墨附近冒出來，慢慢往上、往左飄
        p.pos = Px(Random.Range(1050f, 1800f), anywhere ? Random.Range(150f, 1000f) : Random.Range(700f, 1050f));
        p.vel = new Vector2(Random.Range(-22f, 6f), Random.Range(10f, 34f));
        p.life = Random.Range(5f, 11f);
        p.age = anywhere ? Random.Range(0f, p.life) : 0f;
        p.size = Random.Range(5f, 16f);
        p.img.rectTransform.sizeDelta = new Vector2(p.size, p.size);
    }

    // ---------------- 每幀 ----------------

    static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
    static float Window(float t, float a, float b) => Ease((t - a) / Mathf.Max(0.01f, b - a));
    /// <summary>在 at 那一刻打中：1 → 0（0.18 秒內衰減），用來讓標題震一下</summary>
    static float Hit(float t, float at) => t < at ? 0f : Mathf.Clamp01(1f - (t - at) / 0.18f);

    void Update()
    {
        if (root == null) return;
        float t = Time.unscaledTime - t0, dt = Time.unscaledDeltaTime;

        // 古畫：慢慢浮現、輕輕飄
        foreach (var (img, pos, a, ph) in paintings)
        {
            img.color = new Color(1f, 1f, 1f, a * Window(t, 0f, 1.6f));
            img.rectTransform.anchoredPosition = pos + new Vector2(Mathf.Sin(t * 0.07f + ph) * 12f, Mathf.Cos(t * 0.05f + ph) * 8f);
        }

        // 潑墨：從中間暈開，之後一直慢慢呼吸、轉
        float ink = Window(t, 0.1f, 1.5f);
        float breathe = 1f + 0.025f * Mathf.Sin(t * 0.6f);
        inkWash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.45f, 1f, ink) * breathe;
        inkWash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.15f) * 2f);
        inkWash.color = new Color(1f, 1f, 1f, 0.92f * ink);
        inkWash2.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.05f, ink) * (2f - breathe);
        inkWash2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 160f + t * 1.5f);
        inkWash2.color = new Color(1f, 1f, 1f, 0.28f * ink);

        float sunA = Window(t, 0.5f, 1.5f);
        sun.color = new Color(1f, 1f, 1f, 0.95f * sunA);
        sun.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, sunA);

        // 道士法相：淡淡浮現，之後一直很慢地呼吸（濃淡）、微微往上飄
        float taoA = Window(t, 0.4f, 2.0f);
        taoist.color = new Color(1f, 1f, 1f, 0.7f * taoA * (0.92f + 0.08f * Mathf.Sin(t * 0.45f)));
        taoist.rectTransform.anchoredPosition = Px(1560f, 690f + (1f - taoA) * 40f + Mathf.Sin(t * 0.3f) * 6f);
        taoist.rectTransform.localScale = Vector3.one * (1f + 0.012f * Mathf.Sin(t * 0.45f));

        float heroA = Window(t, 0.7f, 1.7f);
        hero.color = new Color(1f, 1f, 1f, heroA);
        hero.rectTransform.anchoredPosition = Px(1400f + (1f - heroA) * 90f, 640f + Mathf.Sin(t * 0.9f) * 5f);

        // 標題：先浮現乾淨的字；一爪抓下去時換成被抓破的、一刀砍過去時換成被砍開的，每一下標題震一下
        float titleA = Window(t, 1.0f, 2.0f);
        claws.fillAmount = Mathf.Clamp01((t - ClawAt) / 0.12f);
        slash.fillAmount = Mathf.Clamp01((t - SlashAt) / 0.1f);
        int stage = t >= SlashAt + 0.05f ? 2 : t >= ClawAt + 0.06f ? 1 : 0;
        title.color = new Color(1f, 1f, 1f, stage == 0 ? titleA : 0f);
        titleCut1.color = new Color(1f, 1f, 1f, stage == 1 ? 1f : 0f);
        titleCut2.color = new Color(1f, 1f, 1f, stage == 2 ? 1f : 0f);
        float hit = Mathf.Max(Hit(t, ClawAt + 0.06f), Hit(t, SlashAt + 0.05f));
        Vector2 jolt = new Vector2(Mathf.Sin(t * 95f), Mathf.Cos(t * 83f)) * 6f * hit;
        Vector2 titlePos = Px(540f - (1f - titleA) * 60f, TitleY) + jolt;
        foreach (var g in new Graphic[] { title, titleCut1, titleCut2, claws, slash }) g.rectTransform.anchoredPosition = titlePos;
        if (t >= ClawAt && t - dt < ClawAt) GameAudio.Play("SFX/RPG/drawKnife3", Vector3.zero, 0.8f, 0.8f, 0f, 0f);
        if (t >= SlashAt && t - dt < SlashAt) GameAudio.Play("SFX/RPG/drawKnife1", Vector3.zero, 0.9f, 1.3f, 0f, 0f);
        subtitle.color = new Color(1f, 1f, 1f, Window(t, 1.4f, 2.3f));

        UpdateParticles(dt, Window(t, 0.6f, 2f));
        UpdateItems(t, dt);
        UpdatePanel(dt);
        HandleInput(t);

        // 開始遊戲：墨潑滿畫面
        if (leaving)
        {
            float k = Ease((Time.unscaledTime - leaveAt) / 0.7f);
            cover.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.2f, 3.2f, k);
            cover.rectTransform.localRotation = Quaternion.Euler(0f, 0f, k * 25f);
            cover.color = new Color(0f, 0f, 0f, Mathf.Clamp01(k * 1.6f));
        }
    }

    void UpdateParticles(float dt, float fade)
    {
        foreach (var p in particles)
        {
            p.age += dt;
            if (p.age >= p.life) Respawn(p, false);
            p.pos += p.vel * dt;
            p.img.rectTransform.anchoredPosition = p.pos;
            float u = p.age / p.life;
            p.img.color = new Color(1f, 1f, 1f, Mathf.Sin(u * Mathf.PI) * 0.55f * fade);
        }
    }

    void UpdateItems(float t, float dt)
    {
        if (lastSelected != selected) { strokeFill = 0f; lastSelected = selected; }
        strokeFill = Mathf.MoveTowards(strokeFill, 1f, dt / 0.22f);
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            float appear = Window(t, 1.6f + i * 0.15f, 2.3f + i * 0.15f);
            bool on = i == selected;
            float shift = on ? 18f : 0f;
            it.rect.anchoredPosition = Vector2.Lerp(it.rect.anchoredPosition, it.home + new Vector2(shift - (1f - appear) * 70f, 0f), 1f - Mathf.Exp(-dt * 14f));
            it.rect.localScale = Vector3.one * Mathf.Lerp(it.rect.localScale.x, on ? 1.06f : 1f, 1f - Mathf.Exp(-dt * 14f));
            it.label.color = new Color(1f, 1f, 1f, appear * (on ? 1f : 0.72f));
            it.stroke.fillAmount = on && appear > 0.9f ? Ease(strokeFill) : Mathf.MoveTowards(it.stroke.fillAmount, 0f, dt * 6f);
        }
    }

    // ---------------- 選擇存檔（按「開始遊戲」後跳出來的面板） ----------------

    sealed class SaveSlot
    {
        public RectTransform rect;
        public Image cloth;
        public ClothFlutter flutter;
        public Text name, place, time, defeats, lastEvent, savedAt, erase;
        public Vector2 home;
        public SaveData data;
    }

    RectTransform panel, backRect;
    CanvasGroup panelGroup;
    Image backCloth;
    ClothFlutter backFlutter;
    SaveSlot[] slots;
    /// <summary>選擇存檔的面板開著（開著的時候主選單的選項不吃輸入）</summary>
    bool panelOpen, backHover;
    float panelShow, panelOpenedAt, armedUntil = -1f;
    int focusSlot, hoverSlot = -1, armedSlot = -1, chosenSlot = -1;

    const float CardW = 380f, CardH = 400f, CardGap = 40f;
    static readonly Color SlotInk = new Color(0.17f, 0.11f, 0.065f);
    static readonly Color SlotCinnabar = new Color(0.6f, 0.14f, 0.08f);

    Text SlotText(string name, Transform parent, string text, int size, Vector2 pos, Vector2 box, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var t = UIKit.Label(name, parent, text, size, SlotInk, pos, box, anchor, style);
        var f = MenuSkin.Font;
        if (f != null) t.font = f;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var sh = t.GetComponent<Shadow>();
        if (sh != null) { sh.effectColor = new Color(1f, 0.94f, 0.8f, 0.25f); sh.effectDistance = new Vector2(1f, -1f); }
        return t;
    }

    /// <summary>「選擇存檔」面板：暗下來的主畫面上一張破皮紙，標題刷一筆朱紅，三塊破布是三個存檔欄（平常藏著）</summary>
    void BuildSlotPanel()
    {
        panel = UIKit.Rect("SlotPanel", root, Vector2.zero, Vector2.one);
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        panelGroup.alpha = 0f;
        panelGroup.blocksRaycasts = false;
        panelGroup.interactable = false;
        bool skin = MenuSkin.Available;
        UIKit.Panel("Dim", panel, skin ? MenuSkin.Dim : new Color(0.06f, 0.04f, 0.025f, 0.6f)).raycastTarget = false;
        if (skin) MenuSkin.WideParchment(panel, Vector2.zero, 1420f);
        else UIKit.Box("Parchment", panel, Vector2.zero, new Vector2(1420f, 840f), new Color(0.9f, 0.85f, 0.74f, 0.96f)).raycastTarget = false;
        MenuSkin.TitleStroke(panel, new Vector2(0f, 300f), new Vector2(400f, 96f));
        SlotText("Title", panel, "選擇存檔", 64, new Vector2(0f, 302f), new Vector2(600f, 90f), TextAnchor.MiddleCenter, FontStyle.Bold);
        var hint = SlotText("Hint", panel, "滑鼠點一下、或 ← → 選好按 Enter / 手把 A 進入遊戲　　Delete / 手把 Y 清除存檔", 22,
                            new Vector2(0f, -262f), new Vector2(1300f, 32f), TextAnchor.MiddleCenter);
        hint.color = new Color(SlotInk.r, SlotInk.g, SlotInk.b, 0.7f);

        var clothSprite = MenuSkin.Cloth;
        Image Cloth(RectTransform rt)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            if (clothSprite != null) { img.sprite = clothSprite; img.type = Image.Type.Sliced; }
            return img;
        }

        slots = new SaveSlot[SaveSystem.SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            var home = new Vector2((i - (slots.Length - 1) * 0.5f) * (CardW + CardGap), 10f);
            var rt = UIKit.Rect("SaveSlot_" + (i + 1), panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), home, new Vector2(CardW, CardH));
            var img = Cloth(rt);
            var flutter = rt.gameObject.AddComponent<ClothFlutter>();
            flutter.amplitude = 6f;
            var s = new SaveSlot
            {
                rect = rt, cloth = img, flutter = flutter, home = home,
                name = SlotText("Name", rt, GameManager.SlotName(i), 40, new Vector2(0f, 150f), new Vector2(CardW - 40f, 50f), TextAnchor.MiddleCenter, FontStyle.Bold),
                erase = SlotText("Erase", rt, "清除", 20, new Vector2(CardW * 0.5f - 80f, 176f), new Vector2(130f, 28f), TextAnchor.MiddleRight),
                place = SlotText("Place", rt, "", 34, new Vector2(0f, 88f), new Vector2(CardW - 40f, 46f), TextAnchor.MiddleCenter, FontStyle.Bold),
                time = SlotText("Time", rt, "", 24, new Vector2(0f, 38f), new Vector2(CardW - 40f, 34f), TextAnchor.MiddleCenter),
                defeats = SlotText("Defeats", rt, "", 24, new Vector2(0f, 2f), new Vector2(CardW - 40f, 34f), TextAnchor.MiddleCenter),
                lastEvent = SlotText("LastEvent", rt, "", 22, new Vector2(0f, -72f), new Vector2(CardW - 56f, 70f), TextAnchor.MiddleCenter),
                savedAt = SlotText("SavedAt", rt, "", 20, new Vector2(0f, -148f), new Vector2(CardW - 40f, 30f), TextAnchor.MiddleCenter),
            };
            s.lastEvent.horizontalOverflow = HorizontalWrapMode.Wrap;   // 原因長的話折成兩行（字不跟著布飄）
            slots[i] = s;
        }

        // 返回
        backRect = UIKit.Rect("Back", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(220f, 62f));
        backCloth = Cloth(backRect);
        backFlutter = backRect.gameObject.AddComponent<ClothFlutter>();
        SlotText("Label", backRect, "返　回", 28, Vector2.zero, new Vector2(200f, 50f), TextAnchor.MiddleCenter);
        panel.gameObject.SetActive(false);
    }

    /// <summary>重新讀三個欄位的存檔，更新上面的字</summary>
    void RefreshSlots()
    {
        var gm = GameManager.Instance;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            s.data = SaveSystem.Peek(i);
            if (s.data == null)
            {
                s.place.text = "空　白";
                s.time.text = "開始新的旅程";
                s.defeats.text = "";
                s.lastEvent.text = "";
                s.savedAt.text = "";
                continue;
            }
            var level = gm != null ? gm.FindLevel(s.data.sceneName) : null;
            string place = level != null ? level.title : s.data.sceneName;
            int cut = place.LastIndexOf('　');   // 「第二關　維京村莊」→「維京村莊」
            if (cut >= 0 && cut < place.Length - 1) place = place.Substring(cut + 1);
            s.place.text = place;
            s.time.text = $"遊玩 {FormatTime(s.data.playTime)}";
            int defeats = s.data.story != null ? s.data.story.defeats : 0;
            s.defeats.text = defeats > 0 ? $"總共倒下 {defeats} 次" : "還沒倒下過";
            s.lastEvent.text = $"上次存檔：{(string.IsNullOrEmpty(s.data.lastEvent) ? "—" : s.data.lastEvent)}";
            s.savedAt.text = s.data.savedAt;
        }
    }

    static string FormatTime(float seconds)
    {
        int t = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    /// <summary>按「開始遊戲」：跳出選擇存檔的面板，游標先停在最近存過的那一欄</summary>
    void OpenPanel()
    {
        RefreshSlots();
        focusSlot = 0;
        string latest = null;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].data != null && (latest == null || string.CompareOrdinal(slots[i].data.savedAt, latest) > 0)) { latest = slots[i].data.savedAt; focusSlot = i; }
        armedSlot = -1;
        hoverSlot = -1;
        panelOpen = true;
        panelOpenedAt = Time.unscaledTime;
        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();
        cover.transform.SetAsLastSibling();
        GameAudio.Play("SFX/RPG/bookOpen", Vector3.zero, 0.8f, 1f, 0f, 0f);
    }

    void ClosePanel()
    {
        panelOpen = false;
        armedSlot = -1;
        GameAudio.Play("SFX/RPG/bookClose", Vector3.zero, 0.7f, 1.1f, 0f, 0f);
    }

    void FocusSlot(int i)
    {
        if (i == focusSlot) return;
        focusSlot = i;
        GameAudio.Play("SFX/RPG/bookFlip1", Vector3.zero, 0.45f, 1.25f, 0.05f, 0f);
    }

    /// <summary>選了一欄：墨潑滿畫面，進入遊戲（空的 = 開新遊戲，有存檔 = 接著玩）</summary>
    void EnterSlot(int i)
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.IsLoading || leaving) return;
        chosenSlot = i;
        focusSlot = i;
        leaving = true;
        leaveAt = Time.unscaledTime;
        GameAudio.Play("SFX/RPG/metalClick", Vector3.zero, 1f, 0.9f, 0f, 0f);
        cover.transform.SetAsLastSibling();
        Invoke(nameof(BeginGame), 0.55f);
    }

    /// <summary>清除存檔：按一次提示「再按一次清除」（3 秒內），再按一次才真的刪掉</summary>
    void Erase(int i)
    {
        if (slots[i].data == null) return;
        if (armedSlot == i && Time.unscaledTime < armedUntil)
        {
            SaveSystem.DeleteSlot(i);
            armedSlot = -1;
            RefreshSlots();
            GameAudio.Play("SFX/RPG/cloth3", Vector3.zero, 0.9f, 0.8f, 0f, 0f);
            return;
        }
        armedSlot = i;
        armedUntil = Time.unscaledTime + 3f;
        GameAudio.Play("SFX/RPG/creak2", Vector3.zero, 0.6f, 1.3f, 0f, 0f);
    }

    void UpdatePanel(float dt)
    {
        if (panel == null) return;
        panelShow = Mathf.MoveTowards(panelShow, panelOpen ? 1f : 0f, dt * (panelOpen ? 5f : 7f));
        if (panelShow <= 0f) { if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false); return; }
        float e = Ease(panelShow);
        panelGroup.alpha = e;
        panel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
        float now = Time.unscaledTime;
        if (armedSlot >= 0 && now >= armedUntil) armedSlot = -1;
        bool skin = slots[0].cloth.sprite != null;
        Color Ink(float a) => new Color(SlotInk.r, SlotInk.g, SlotInk.b, a);

        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            bool focus = i == focusSlot || i == hoverSlot;
            s.rect.anchoredPosition = s.home;   // 選到的那塊只有布被風吹、染成朱紅，字不動
            s.cloth.color = skin ? (focus ? MenuSkin.ClothChosenHover : MenuSkin.ClothNormal)
                                 : (focus ? new Color(0.6f, 0.14f, 0.08f, 0.35f) : new Color(0.17f, 0.11f, 0.065f, 0.12f));
            s.flutter.forced = focus;
            s.name.color = Ink(1f);
            s.place.color = Ink(s.data != null ? 1f : 0.75f);
            s.time.color = Ink(0.85f);
            s.defeats.color = s.data != null && s.data.story != null && s.data.story.defeats > 0 ? new Color(SlotCinnabar.r, SlotCinnabar.g, SlotCinnabar.b, 0.9f) : Ink(0.7f);
            s.lastEvent.color = Ink(0.85f);
            s.savedAt.color = Ink(0.6f);
            bool armed = armedSlot == i;
            s.erase.text = armed ? "再按一次清除" : "清除";
            s.erase.color = s.data != null && (focus || armed) ? (armed ? new Color(SlotCinnabar.r, SlotCinnabar.g, SlotCinnabar.b, 1f) : Ink(0.6f)) : new Color(0f, 0f, 0f, 0f);
        }
        backCloth.color = skin ? (backHover ? MenuSkin.ClothHover : MenuSkin.ClothNormal) : new Color(0.17f, 0.11f, 0.065f, backHover ? 0.22f : 0.12f);
        backFlutter.forced = backHover;
    }

    // ---------------- 操作 ----------------

    void HandleInput(float t)
    {
        if (leaving || ControlsSettingsMenu.IsOpen || ControlsSettingsMenu.ClosedThisFrame || t < 1.6f) return;
        if (panelOpen) { HandlePanelInput(); return; }
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        int move = 0;
        if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) move = 1;
        if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) move = -1;
        if (pad != null)
        {
            if (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame) move = 1;
            if (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame) move = -1;
        }
        if (move != 0) Select((selected + move + items.Length) % items.Length);

        // 滑鼠：移到哪個選項上就選它，點一下確定
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 mp = mouse.position.ReadValue();
            for (int i = 0; i < items.Length; i++)
            {
                var hit = items[i].stroke.rectTransform; // 用那一筆的範圍（比字大），比較好點
                if (!RectTransformUtility.RectangleContainsScreenPoint(hit, mp, null)) continue;
                if (mouse.delta.ReadValue().sqrMagnitude > 0.01f && selected != i) Select(i);
                if (mouse.leftButton.wasPressedThisFrame) { Select(i); Confirm(); return; }
            }
        }
        bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                       || PadInput.ConfirmPressed;
        if (confirm) Confirm();
    }

    /// <summary>選擇存檔的面板：左右換欄、Enter / A 進入、Delete / Y 清除、Esc / B 返回；滑鼠移上去選、點一下進入</summary>
    void HandlePanelInput()
    {
        if (Time.unscaledTime - panelOpenedAt < 0.2f) return;   // 打開面板的那一下不要直接選到
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        int move = 0;
        if (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) move = 1;
        if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) move = -1;
        if (pad != null)
        {
            if (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame) move = 1;
            if (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame) move = -1;
        }
        bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                       || PadInput.ConfirmPressed;
        bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
        bool erase = (kb != null && (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonNorth.wasPressedThisFrame);

        var mouse = Mouse.current;
        hoverSlot = -1;
        backHover = false;
        if (mouse != null)
        {
            Vector2 mp = mouse.position.ReadValue();
            bool moved = mouse.delta.ReadValue().sqrMagnitude > 0.01f;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(slots[i].rect, mp, null)) continue;
                hoverSlot = i;
                if (moved) FocusSlot(i);
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    var er = slots[i].erase;
                    if (slots[i].data != null && er.color.a > 0.05f && RectTransformUtility.RectangleContainsScreenPoint(er.rectTransform, mp, null)) Erase(i);
                    else EnterSlot(i);
                    return;
                }
            }
            backHover = RectTransformUtility.RectangleContainsScreenPoint(backRect, mp, null);
            if (backHover && mouse.leftButton.wasPressedThisFrame) { ClosePanel(); return; }
        }
        if (back) { ClosePanel(); return; }
        if (move != 0) FocusSlot((focusSlot + move + slots.Length) % slots.Length);
        if (erase) { Erase(focusSlot); return; }
        if (confirm) EnterSlot(focusSlot);
    }

    void Select(int i)
    {
        if (i == selected) return;
        selected = i;
        GameAudio.Play("SFX/RPG/bookFlip1", Vector3.zero, 0.5f, 1.15f, 0.05f, 0f);
    }

    void Confirm() => items[selected].action?.Invoke();

    void OnStart()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.IsLoading || leaving || panelOpen) return;
        OpenPanel();
    }

    void BeginGame()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        // 下方的進度條（偶爾會倒退），跑完才進去：空的存檔 = 漁村（開新遊戲），有存檔 = 存檔那一關
        StoryLoadingScreen.Begin(gm, gm.SlotTargetScene(chosenSlot));
        gm.StartSlot(chosenSlot);
    }

    /// <summary>選項：操作方式、鏡頭靈敏度、自訂按鍵（和遊戲中暫停選單的設定是同一個畫面、同一份設定）</summary>
    void OnOptions()
    {
        if (ControlsSettingsMenu.IsOpen || canvas == null) return;
        GameAudio.Play("SFX/RPG/metalClick", Vector3.zero, 0.8f, 1.1f, 0f, 0f);
        // 主畫面留著當背景（選項畫在皮紙上、蓋在主畫面上面）；主畫面的選單在選項開著時不吃輸入（UpdateItems）
        ControlsSettingsMenu.Open(transform, null);
    }

    void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
