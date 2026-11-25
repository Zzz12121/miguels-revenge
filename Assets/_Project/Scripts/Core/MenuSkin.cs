using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 選單的主畫面風格（選項、遊戲中的 Esc 暫停選單、Game Over / 通關畫面共用）：
///   撕得破破爛爛的皮紙底、每個按鈕是一塊破布（選到 / 滑鼠移上去時像被微風吹，ClothFlutter）、
///   墨色的字（英文 Bookman Old Style、中文標楷體，跟主畫面一樣）、標題底下刷一筆朱紅。
/// 圖在 Resources/MainMenu（Art/MainMenu/make_settings_art.py 產生）；載不到的話 Available = false，各選單退回原本的樣子。
/// </summary>
public static class MenuSkin
{
    public static readonly Color Ink = new Color(0.17f, 0.11f, 0.065f);
    public static readonly Color Cinnabar = new Color(0.6f, 0.14f, 0.08f);
    public static readonly Color ClothNormal = new Color(0.97f, 0.94f, 0.88f, 1f);
    public static readonly Color ClothHover = new Color(1f, 0.93f, 0.84f, 1f);
    public static readonly Color ClothChosen = new Color(0.93f, 0.55f, 0.45f, 1f);      // 選了的那個（操作方式、分頁）：染成朱紅的布
    public static readonly Color ClothChosenHover = new Color(0.97f, 0.62f, 0.5f, 1f);
    public static readonly Color ClothPressed = new Color(0.82f, 0.6f, 0.48f, 1f);
    /// <summary>皮紙後面那層暗色（看得到後面的遊戲 / 主畫面）</summary>
    public static readonly Color Dim = new Color(0.06f, 0.04f, 0.025f, 0.6f);

    // make_settings_art.py 的數字
    const float WidePad = 40f;                                               // Settings_Parchment 外圍留給影子的 px
    static readonly Vector4 ClothBorder = new Vector4(56f, 40f, 56f, 40f);   // CLOTH_BORDER（左、下、右、上）
    const float TallBorder = 150f;                                           // PAUSE_BORDER

    static Texture2D wideTex, brushTex, loadingCloth, paperTex;
    static Sprite clothSprite, tallSprite;
    static Font font;
    static bool tried, fontTried;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        wideTex = brushTex = loadingCloth = paperTex = null;
        clothSprite = tallSprite = null;
        font = null;
        tried = fontTried = false;
    }

    /// <summary>圖都載得到（皮紙、破布）</summary>
    public static bool Available
    {
        get
        {
            if (clothSprite != null && wideTex != null) return true;
            if (tried && clothSprite == null) return false;
            tried = true;
            wideTex = Resources.Load<Texture2D>("MainMenu/Settings_Parchment");
            brushTex = Resources.Load<Texture2D>("MainMenu/BrushStroke");
            var cloth = Resources.Load<Texture2D>("MainMenu/Settings_Cloth");
            var tall = Resources.Load<Texture2D>("MainMenu/Pause_Parchment");
            if (wideTex == null || cloth == null) return false;
            // 九宮格：四邊的毛邊不會被拉長；pixelsPerUnit 200 = 邊在畫面上縮成一半（按鈕只有 40~70 高）
            clothSprite = Sprite.Create(cloth, new Rect(0, 0, cloth.width, cloth.height), new Vector2(0.5f, 0.5f), 200f, 0,
                                        SpriteMeshType.FullRect, ClothBorder);
            clothSprite.name = "Settings_Cloth";
            if (tall != null)
            {
                tallSprite = Sprite.Create(tall, new Rect(0, 0, tall.width, tall.height), new Vector2(0.5f, 0.5f), 100f, 0,
                                           SpriteMeshType.FullRect, Vector4.one * TallBorder);
                tallSprite.name = "Pause_Parchment";
            }
            return true;
        }
    }

    /// <summary>按鈕用的破布（九宮格）；圖載不到時是 null</summary>
    public static Sprite Cloth => Available ? clothSprite : null;

    /// <summary>進度條用的破布（一長條，不切九宮格）</summary>
    public static Texture2D LoadingCloth => loadingCloth != null ? loadingCloth : loadingCloth = Resources.Load<Texture2D>("MainMenu/Loading_Cloth");
    /// <summary>主畫面的舊宣紙</summary>
    public static Texture2D Paper => paperTex != null ? paperTex : paperTex = Resources.Load<Texture2D>("MainMenu/Paper");
    /// <summary>主畫面選到選項時後面那一筆朱紅（乾筆橫刷）</summary>
    public static Texture2D BrushStroke { get { _ = Available; return brushTex; } }

    /// <summary>
    /// 主畫面的字：英文、數字用 Bookman Old Style（「Miguel's Revenge」標題的字），中文用標楷體（「開始遊戲」「選項」那些字），
    /// 前一個沒有的字自動用下一個。用系統裝的字型（Windows 內建 / Office），沒有中文字型的話回傳 null，維持原本的字
    /// </summary>
    public static Font Font
    {
        get
        {
            if (font != null || fontTried) return font;
            fontTried = true;
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            var names = new List<string>();
            foreach (var n in new[] { "Bookman Old Style", "DFKai-SB", "標楷體", "BiauKai", "KaiTi" })
                if (installed.Contains(n)) names.Add(n);
            // 一定要有中文字型（只有 Bookman 的話中文會變成方塊）
            if (names.Count > 0 && !(names.Count == 1 && names[0] == "Bookman Old Style")) font = Font.CreateDynamicFontFromOSFont(names.ToArray(), 32);
            return font;
        }
    }

    static RawImage Raw(Transform root, string name, Texture tex, Vector2 pos, Vector2 size, Color color)
    {
        var rt = UIKit.Rect(name, root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = tex;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>橫的大皮紙（選項畫面）：width = 紙本身在畫面上多寬（不含影子），高照圖的比例</summary>
    public static void WideParchment(Transform root, Vector2 center, float width)
    {
        if (!Available) return;
        float k = width / (wideTex.width - WidePad * 2f);
        Raw(root, "Parchment", wideTex, center, new Vector2(wideTex.width * k, wideTex.height * k), Color.white);
    }

    /// <summary>直的皮紙（暫停選單）：九宮格，高度跟著按鈕數量變，四邊的毛邊不會被拉長</summary>
    public static void TallParchment(Transform root, Vector2 center, Vector2 size)
    {
        if (!Available) return;
        if (tallSprite == null) { WideParchment(root, center, size.x); return; }
        var rt = UIKit.Rect("Parchment", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), center, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = tallSprite;
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
    }

    /// <summary>標題底下那一筆朱紅（要在標題的字之前加，才會在字後面）</summary>
    public static void TitleStroke(Transform root, Vector2 center, Vector2 size)
    {
        if (BrushStroke != null) Raw(root, "TitleStroke", brushTex, center, size, new Color(1f, 1f, 1f, 0.8f));
    }

    /// <summary>
    /// 套用到 root 底下所有東西：按鈕換成破布（選到時被風吹）、字換成墨色（名字是 Status 的用朱紅、Hint 淡一點）、
    /// 陰影換成淡淡的紙色、字體換成主畫面的字
    /// </summary>
    public static void Apply(Transform root)
    {
        if (!Available) return;
        foreach (var b in root.GetComponentsInChildren<Button>(true))
        {
            var img = b.GetComponent<Image>();
            if (img == null || b.GetComponent<ClothFlutter>() != null) continue;
            img.sprite = clothSprite;
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
            img.color = Color.white;
            var outline = b.GetComponent<Outline>();
            if (outline != null) { outline.enabled = false; Object.Destroy(outline); }
            var c = b.colors;
            c.normalColor = ClothNormal;
            c.highlightedColor = c.selectedColor = ClothHover;
            c.pressedColor = ClothPressed;
            c.disabledColor = new Color(0.8f, 0.78f, 0.74f, 0.6f);
            c.colorMultiplier = 1f;
            b.colors = c;
            b.gameObject.AddComponent<ClothFlutter>();   // 只有布被風吹，字不動
        }
        var f = Font;
        foreach (var t in root.GetComponentsInChildren<Text>(true))
        {
            if (f != null) t.font = f;
            if (t.name == "SealText") continue;   // 朱印裡的白字
            t.color = t.name == "Status" ? Cinnabar : t.name == "Hint" ? new Color(Ink.r, Ink.g, Ink.b, 0.7f) : new Color(Ink.r, Ink.g, Ink.b, t.color.a);
            var sh = t.GetComponent<Shadow>();
            if (sh != null && !(sh is Outline)) { sh.effectColor = new Color(1f, 0.94f, 0.8f, 0.25f); sh.effectDistance = new Vector2(1f, -1f); }
        }
    }

    /// <summary>切換鈕（選了 / 沒選）：選了的是染成朱紅的布，被選到時還是紅的</summary>
    public static void Tint(Button b, bool on)
    {
        var c = b.colors;
        c.normalColor = on ? ClothChosen : ClothNormal;
        c.highlightedColor = c.selectedColor = on ? ClothChosenHover : ClothHover;
        b.colors = c;
    }
}
