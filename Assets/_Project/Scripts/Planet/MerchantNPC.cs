using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 商人：玩家走近時顯示「按 E 對話」，按下後開始引導式對話（畫面下方的對話框，文字逐字出現），
/// 每按一次空白鍵繼續下一句（文字還沒跑完時先直接顯示整句）。
/// 講完之後直接在對話框裡購買（不再開商店頁面）：
///   1. 治癒藥水：回復 15% 血量，草藥 ×4（之後按 Q 使用）
///   2. 火焰彈　：投擲後爆炸並讓敵人燃燒，草藥 ×6（之後按 F 投擲）
///   3. 離開
/// 用數字鍵或滑鼠點選。第一次對話是完整的新手引導，之後再來只講幾句招呼。
/// 對話期間角色不能移動，空白鍵不會跳躍。
/// </summary>
public class MerchantNPC : MonoBehaviour
{
    [Header("互動設定")]
    [Tooltip("玩家離商人多近才可以按 E 對話 (公尺)")]
    public float interactRange = 3.0f;
    [Tooltip("舊的商店 UI Panel（已改成在對話中購買，這裡只會在開始時把它關掉）")]
    public GameObject shopCanvasUI;
    public Key interactKey = Key.E;
    public Key continueKey = Key.Space;

    [Header("商品")]
    public string resourceName = "Herb";
    public string resourceDisplayName = "草藥";
    public int potionCost = 4;
    public int fireBombCost = 6;

    [Header("對話")]
    public string speakerName = "流浪商人";
    [Tooltip("第一次對話：新手引導")]
    [TextArea(2, 4)]
    public string[] guideLines =
    {
        "喲，旅人！在這片沙漠還能遇到活人，真是稀奇。",
        "這顆星球被「時間吞噬者」佔據了。想離開這裡，就得打倒牠。",
        "四處長著的草藥別放過——靠近按 E 就能採集。拿來找我，我用草藥跟你換好東西。",
        "附近有個蜘蛛巢穴，巢裡長著一株奇特的能力植物。你在那裡選擇的道路……跑速、力量、還是血量，會決定你之後的成長方向。",
        "營火靠近按 E 可以回復 20% 的血量。不過每座營火只能用一次，死了也不會再燒起來，省著點用。",
        "時間吞噬者就在廣場深處。入口立著一塊古老的石碑，碰過它之後，倒下時會在石碑旁重新站起來。",
        "對了，Boss 廣場的營火旁長著第二株能力植物。它會呼應你在蜘蛛巢穴的選擇，給你更強的力量。",
        "好啦，看看我的貨吧！",
    };
    [Tooltip("之後再來對話")]
    [TextArea(2, 4)]
    public string[] repeatLines =
    {
        "又見面了，旅人。草藥帶夠了嗎？",
    };
    [Tooltip("每秒顯示幾個字")]
    public float charsPerSecond = 40f;

    enum Mode { Idle, Talking, Shopping }

    Transform player;
    PlayerHealth playerHealth;
    PlayerInventory inventory;
    PlayerItems items;
    ThirdPersonController controller;
    ThirdPersonCamera cam;
    Mode mode = Mode.Idle;
    bool hasTalked;
    string[] lines;
    int lineIndex;
    float lineStartTime;
    string shopLine;
    GUIStyle nameStyle, textStyle, hintStyle, promptStyle, optionStyle;

    void Start()
    {
        playerHealth = PlayerHealth.Find();
        if (playerHealth != null)
        {
            player = playerHealth.transform;
            controller = playerHealth.GetComponent<ThirdPersonController>();
        }
        else
        {
            Debug.LogError("[MerchantNPC] 場景裡找不到玩家（PlayerHealth）！");
        }

        if (shopCanvasUI != null) shopCanvasUI.SetActive(false);
    }

    bool PlayerInRange()
    {
        if (player == null || (playerHealth != null && playerHealth.IsDead)) return false;
        return Vector3.Distance(transform.position, player.position) <= interactRange;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || player == null) return;
        var gm = GameManager.Instance;
        if (gm != null && gm.IsOverlayOpen) return; // 暫停選單開著時不處理

        if (mode != Mode.Idle && playerHealth != null && playerHealth.IsDead)
        {
            EndAll();
            return;
        }

        switch (mode)
        {
            case Mode.Idle:
                if (PlayerInRange() && kb[interactKey].wasPressedThisFrame) StartTalk();
                break;

            case Mode.Talking:
                if (kb[continueKey].wasPressedThisFrame) Advance();
                break;

            case Mode.Shopping:
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Buy(PlayerItems.HealingPotion);
                else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) Buy(PlayerItems.FireBombItem);
                else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame
                         || kb[interactKey].wasPressedThisFrame) EndAll();
                break;
        }
    }

    void StartTalk()
    {
        inventory = player.GetComponent<PlayerInventory>();
        items = player.GetComponent<PlayerItems>();
        lines = hasTalked || guideLines.Length == 0 ? repeatLines : guideLines;
        hasTalked = true;
        lineIndex = 0;
        lineStartTime = Time.unscaledTime;
        mode = Mode.Talking;
        SetPlayerInput(false);
        FacePlayer();
        GameAudio.Play("SFX/RPG/bookOpen", transform.position, 0.7f, 1.1f, 0f, 0.3f);
        if (lines == null || lines.Length == 0) OpenShop();
    }

    void Advance()
    {
        // 文字還在逐字出現：先整句顯示
        if (VisibleChars() < lines[lineIndex].Length)
        {
            lineStartTime = -9999f;
            return;
        }
        lineIndex++;
        lineStartTime = Time.unscaledTime;
        GameAudio.Play("SFX/RPG/bookFlip1", transform.position, 0.5f, 1f, 0.1f, 0.3f);
        if (lineIndex >= lines.Length) OpenShop();
    }

    int VisibleChars() => Mathf.FloorToInt((Time.unscaledTime - lineStartTime) * charsPerSecond);

    void OpenShop()
    {
        mode = Mode.Shopping;
        shopLine = "想要什麼？";
        lineStartTime = Time.unscaledTime;
        // 鏡頭在滑鼠沒鎖定時點左鍵會重新鎖定，購物時先關掉，選項才點得到
        cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null) cam.enabled = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    int Herbs => inventory != null ? inventory.GetResource(resourceName) : 0;

    void Buy(string item)
    {
        int cost = item == PlayerItems.HealingPotion ? potionCost : fireBombCost;
        string itemName = item == PlayerItems.HealingPotion ? "治癒藥水" : "火焰彈";
        if (inventory == null || items == null || !inventory.ConsumeResource(resourceName, cost))
        {
            shopLine = $"{resourceDisplayName}不夠喔，{itemName}要 {cost} 株{resourceDisplayName}。";
            GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.6f, 0.7f, 0f, 0.3f);
        }
        else
        {
            items.Add(item, 1);
            shopLine = item == PlayerItems.HealingPotion
                ? $"成交！治癒藥水一瓶。受傷時按 {items.potionKey} 喝下去。"
                : $"成交！火焰彈一顆。按 {items.bombKey} 往前方丟出去，小心別炸到自己。";
            GameAudio.Play("SFX/RPG/handleCoins", transform.position, 0.9f, 1f, 0.05f, 0.3f);
        }
        lineStartTime = Time.unscaledTime;
    }

    void EndAll()
    {
        if (mode == Mode.Shopping)
        {
            if (cam != null) cam.enabled = true;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            GameAudio.Play("SFX/RPG/bookClose", transform.position, 0.6f, 1.1f, 0f, 0.3f);
        }
        mode = Mode.Idle;
        SetPlayerInput(true);
    }

    void SetPlayerInput(bool on)
    {
        if (controller == null) return;
        if (on && playerHealth != null && playerHealth.IsDead) return;
        controller.inputEnabled = on;
    }

    void FacePlayer()
    {
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
    }

    void OnDisable()
    {
        if (mode != Mode.Idle) EndAll();
    }

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.IsOverlayOpen) return;

        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight };
            promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            optionStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = true,
                                                          padding = new RectOffset(14, 10, 6, 6) };
        }

        switch (mode)
        {
            case Mode.Idle:
                if (!PlayerInRange()) return;
                GUI.color = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 40f), $"按 {interactKey} 與{speakerName}對話", promptStyle);
                break;

            case Mode.Talking:
                if (lines != null && lineIndex < lines.Length)
                {
                    bool last = lineIndex == lines.Length - 1;
                    DrawBox(170f, lines[lineIndex], out Rect box);
                    bool done = VisibleChars() >= lines[lineIndex].Length;
                    GUI.color = new Color(1f, 1f, 1f, done ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f) : 0.4f);
                    GUI.Label(new Rect(box.x, box.yMax - 34f, box.width - 24f, 26f),
                              $"{lineIndex + 1}/{lines.Length}　▼ 按 空白鍵 {(last ? "看看商品" : "繼續")}", hintStyle);
                }
                break;

            case Mode.Shopping:
                DrawShop();
                break;
        }
        GUI.color = Color.white;
    }

    /// <summary>畫對話框（名字 + 逐字出現的文字），回傳框的位置</summary>
    void DrawBox(float h, string line, out Rect box)
    {
        float w = Mathf.Min(900f, Screen.width - 80f);
        box = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 40f, w, h);

        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.8f, 0.4f);
        GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
        GUI.Label(new Rect(box.x + 24f, box.y + 12f, box.width - 48f, 30f), speakerName, nameStyle);

        int n = Mathf.Clamp(VisibleChars(), 0, line.Length);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 24f, box.y + 48f, box.width - 48f, 60f), line.Substring(0, n), textStyle);
    }

    void DrawShop()
    {
        DrawBox(290f, shopLine, out Rect box);

        // 右上角：身上的草藥
        GUI.color = new Color(0.6f, 1f, 0.6f);
        var countStyle = new GUIStyle(hintStyle) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(box.x, box.y + 12f, box.width - 24f, 30f), $"{resourceDisplayName}：{Herbs}", countStyle);

        float ox = box.x + 24f, ow = box.width - 48f, oh = 42f;
        float oy = box.y + 104f;
        if (Option(new Rect(ox, oy, ow, oh), "1.  治癒藥水　回復 15% 血量",
                   potionCost, items != null ? items.Count(PlayerItems.HealingPotion) : 0))
            Buy(PlayerItems.HealingPotion);
        oy += oh + 8f;
        if (Option(new Rect(ox, oy, ow, oh), "2.  火焰彈　　投擲後爆炸並燃燒敵人",
                   fireBombCost, items != null ? items.Count(PlayerItems.FireBombItem) : 0))
            Buy(PlayerItems.FireBombItem);
        oy += oh + 8f;
        GUI.color = Color.white;
        if (GUI.Button(new Rect(ox, oy, ow, oh), $"3.  離開（或按 {interactKey}）", optionStyle)) EndAll();
    }

    bool Option(Rect r, string text, int cost, int owned)
    {
        bool afford = Herbs >= cost;
        GUI.color = afford ? Color.white : new Color(1f, 1f, 1f, 0.55f);
        bool clicked = GUI.Button(r, text, optionStyle);
        var right = new GUIStyle(hintStyle) { fontSize = 17, fontStyle = FontStyle.Bold };
        GUI.color = afford ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.45f);
        GUI.Label(new Rect(r.x, r.y, r.width - 14f, r.height), $"持有 {owned}　{resourceDisplayName} ×{cost}", right);
        return clicked;
    }
}
