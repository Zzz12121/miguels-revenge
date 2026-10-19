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
            return gm != null && gm.IsOverlayOpen;
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
    public static string KeyName => InteractKey.ToString();

    static InteractPrompt host;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => host = null; // 沒有重新載入腳本時，上一次 Play 的物件已經不在了
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
        var kb = Keyboard.current;
        if (kb == null || !kb[InteractKey].wasPressedThisFrame || GameUI.OverlayOpen) return false;
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
        float keyW = focusedEnabled ? 30f : 0f;
        float w = size.x + keyW + 40f, h = 44f;
        var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.72f, w, h);

        GameUI.Panel(r, focusedEnabled ? GameUI.Accent : GameUI.MutedColor);
        float x = r.x + 16f;
        if (focusedEnabled)
        {
            GameUI.Key(new Rect(x, r.y + 9f, 26f, 26f), KeyName);
            x += keyW + 4f;
        }
        GUI.color = focusedEnabled ? GameUI.TextColor : GameUI.MutedColor;
        GUI.Label(new Rect(x, r.y, size.x + 8f, h), focusedText, style);
        GUI.color = Color.white;
    }
}
