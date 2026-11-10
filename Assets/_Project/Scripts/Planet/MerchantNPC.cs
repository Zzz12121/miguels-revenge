using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>講完台詞後要玩家回答的問題（任務村民用）。onPick 收到選了第幾個（從 0 開始），回傳接著要講的台詞，null = 直接結束對話</summary>
public sealed class DialogueChoice
{
    public string question;
    public string[] options;
    public System.Func<int, string[]> onPick;
}

/// <summary>
/// 商人：玩家走近時顯示「[E] 與流浪商人對話」，按下後開始引導式對話（畫面下方的對話框，文字逐字出現），
/// 每按一次空白鍵繼續下一句（文字還沒跑完時先直接顯示整句）。
/// 講完之後直接在對話框裡購買：
///   1. 治癒藥水：回復 30% 血量，草藥 ×4（之後按 Q 使用）
///   2. 精力藥水：喝下後精力全滿，草藥 ×3（之後按 G 使用）
///   3. 離開（或按 E）
/// 用數字鍵或滑鼠點選。第一次對話是完整的新手引導，之後再來只講幾句招呼。
/// 對話期間角色不能移動、跳躍、攻擊；關掉對話框的那一下空白鍵也不會跳起來。
/// </summary>
public class MerchantNPC : MonoBehaviour
{
    [Header("互動設定（按鍵統一用 InteractPrompt.InteractKey = E）")]
    [Tooltip("玩家離商人多近才可以對話 (公尺)")]
    public float interactRange = 3.0f;
    [Tooltip("舊的商店 UI Panel（已改成在對話中購買，這裡只會在開始時把它關掉）")]
    public GameObject shopCanvasUI;

    [Tooltip("對話框右邊半身像要拍的模型（留空 = 拍商人自己的模型）")]
    public GameObject portraitPrefab;

    [Header("對話行為（村民用，預設值 = 原本的商人）")]
    [Tooltip("講完之後打開商店；關掉就只是一般對話的村民")]
    public bool sellsGoods = true;
    [Tooltip("開始對話時轉身面向玩家（例如釣魚中的漁夫可以關掉）")]
    public bool facePlayerOnTalk = true;

    [Header("商品")]
    public string resourceName = "Herb";
    public string resourceDisplayName = "草藥";
    public int potionCost = 4;
    public int staminaPotionCost = 3;

    [Header("對話")]
    public string speakerName = "流浪商人";
    [Tooltip("第一次對話：新手引導")]
    [TextArea(2, 4)]
    public string[] guideLines =
    {
        "喲，旅人！在這片沙漠還能遇到活人，真是稀奇。",
        "這顆星球被「時間吞噬者」佔據了。想離開這裡，就得打倒牠。",
        "四處長著的草藥別放過——靠近按 E 就能採集。拿來找我，我用草藥跟你換好東西。",
        "附近有個蜘蛛巢穴。小心那些卵，靠近就會孵化；蜘蛛被打痛了會鑽進沙裡，看到腳下冒紅光就快閃開！",
        "巢裡長著一株奇特的能力植物。你在那裡選擇的道路……跑速、力量、還是血量，會決定你之後的成長方向。",
        "營火靠近按 E 可以回復 40% 的血量。不過每座營火只能用一次，死了也不會再燒起來，省著點用。",
        "跟著地上流動的沙子走，就能找到時間吞噬者的廣場。入口立著一塊古老的石碑，碰過它之後，倒下時會在石碑旁重新站起來。",
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

    const Key ContinueKey = Key.Space;

    enum Mode { Idle, Talking, Shopping, Choosing }

    /// <summary>有設定的話，每次開始對話時用它決定台詞（取代 guideLines / repeatLines，任務進度用）</summary>
    [System.NonSerialized] public System.Func<string[]> linesProvider;
    /// <summary>有設定的話，每次開始對話時問它：講完台詞後要不要問玩家問題（回傳 null = 不問）</summary>
    [System.NonSerialized] public System.Func<DialogueChoice> choiceProvider;
    DialogueChoice choice;
    bool pointerUnlocked;

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
    // 開場劇情之後：商人頭上顯示指引標記，直到玩家第一次跟他說話
    bool guiding;
    // 按鍵離開商店時等下一幀才解除動作鎖：同一顆鍵（例如 3）這一幀不會再觸發技能
    bool leaveNextFrame;

    /// <summary>正在對話或購物中（例如會走路的村民要停下來）</summary>
    public bool InConversation => mode != Mode.Idle;

    /// <summary>開始在商人頭上顯示指引標記（第一次對話後消失）</summary>
    public void StartGuide() => guiding = !hasTalked;

    /// <summary>不管有沒有講過話都顯示指引標記，下一次對話時消失（任務進度用）</summary>
    public void ShowGuideMarker() => guiding = true;

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
        if (portraitPrefab != null) DialoguePortraits.UsePrefab(transform, portraitPrefab);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || player == null || GameUI.OverlayOpen) return; // 暫停選單開著時不處理

        if (leaveNextFrame)
        {
            leaveNextFrame = false;
            EndAll();
            return;
        }

        if (mode != Mode.Idle && playerHealth != null && playerHealth.IsDead)
        {
            EndAll();
            return;
        }

        switch (mode)
        {
            case Mode.Idle:
                if (playerHealth != null && playerHealth.IsDead) break;
                float dist = Vector3.Distance(transform.position, player.position);
                if (dist > interactRange) break;
                InteractPrompt.Offer(this, dist, $"與{speakerName}對話");
                if (InteractPrompt.Pressed(this)) StartTalk();
                break;

            case Mode.Talking:
                if (kb[ContinueKey].wasPressedThisFrame || PadInput.ConfirmPressed) Advance();
                break;

            case Mode.Shopping:
                int pad = PadInput.NumberPressed; // 手把十字鍵 ← ↑ → = 1 2 3，B / Y 離開
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame || pad == 1) Buy(PlayerItems.HealingPotion);
                else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame || pad == 2) Buy(PlayerItems.StaminaPotion);
                else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame || pad == 3
                         || InputBindings.Pressed(GameAction.Interact) || PadInput.CancelPressed) leaveNextFrame = true;
                break;

            case Mode.Choosing:
                int padChoice = PadInput.NumberPressed;
                for (int i = 0; i < choice.options.Length; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame || padChoice == i + 1)
                    {
                        Pick(i);
                        return;
                    }
                // 互動鍵 / 手把 B = 選最後一個（「先不用」這類的選項）
                if (InputBindings.Pressed(GameAction.Interact) || PadInput.CancelPressed) Pick(choice.options.Length - 1);
                break;
        }
    }

    void StartTalk()
    {
        inventory = player.GetComponent<PlayerInventory>();
        items = player.GetComponent<PlayerItems>();
        lines = linesProvider != null ? linesProvider() : hasTalked || guideLines.Length == 0 ? repeatLines : guideLines;
        choice = choiceProvider != null ? choiceProvider() : null;
        hasTalked = true;
        guiding = false;
        lineIndex = 0;
        lineStartTime = Time.unscaledTime;
        mode = Mode.Talking;
        SetPlayerInput(false);
        if (facePlayerOnTalk) FacePlayer();
        GameAudio.Play("SFX/RPG/bookOpen", transform.position, 0.7f, 1.1f, 0f, 0.3f);
        if (lines == null || lines.Length == 0) FinishLines();
    }

    /// <summary>台詞講完：商人打開商店，村民直接結束對話</summary>
    void FinishLines()
    {
        if (choice != null) OpenChoice();
        else if (sellsGoods) OpenShop();
        else EndAll();
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
        if (lineIndex >= lines.Length) FinishLines();
    }

    int VisibleChars() => Mathf.FloorToInt((Time.unscaledTime - lineStartTime) * charsPerSecond);

    void OpenShop()
    {
        mode = Mode.Shopping;
        shopLine = "想要什麼？";
        lineStartTime = Time.unscaledTime;
        UnlockPointer();
    }

    void OpenChoice()
    {
        mode = Mode.Choosing;
        lineStartTime = Time.unscaledTime;
        UnlockPointer();
    }

    /// <summary>選了第 index 個選項：講回覆的台詞，沒有台詞就結束對話</summary>
    void Pick(int index)
    {
        var picked = choice;
        choice = null;
        string[] reply = picked.onPick?.Invoke(index);
        RestorePointer();
        GameAudio.Play("SFX/RPG/bookFlip1", transform.position, 0.5f, 1f, 0.1f, 0.3f);
        if (reply == null || reply.Length == 0)
        {
            leaveNextFrame = true; // 等下一幀才解除動作鎖：同一顆鍵這一幀不會再觸發技能
            return;
        }
        lines = reply;
        lineIndex = 0;
        lineStartTime = Time.unscaledTime;
        mode = Mode.Talking;
    }

    /// <summary>鏡頭在滑鼠沒鎖定時點左鍵會重新鎖定，要點選項時先關掉，選項才點得到</summary>
    void UnlockPointer()
    {
        cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null) cam.enabled = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        pointerUnlocked = true;
    }

    void RestorePointer()
    {
        if (!pointerUnlocked) return;
        pointerUnlocked = false;
        if (cam != null) cam.enabled = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    int Herbs => inventory != null ? inventory.GetResource(resourceName) : 0;

    void Buy(string item)
    {
        bool potion = item == PlayerItems.HealingPotion;
        int cost = potion ? potionCost : staminaPotionCost;
        string itemName = potion ? "治癒藥水" : "精力藥水";
        if (inventory == null || items == null || !inventory.ConsumeResource(resourceName, cost))
        {
            shopLine = $"{resourceDisplayName}不夠喔，{itemName}要 {cost} 株{resourceDisplayName}。";
            GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.6f, 0.7f, 0f, 0.3f);
        }
        else
        {
            items.Add(item, 1);
            shopLine = potion
                ? $"成交！治癒藥水一瓶。受傷時按 {items.potionKey} 喝下去，喝的時候會走得比較慢。倒下之後藥水會補回來。"
                : $"成交！精力藥水一瓶。跑不動的時候按 {items.staminaPotionKey} 喝下去，精力馬上全滿。";
            GameAudio.Play("SFX/RPG/handleCoins", transform.position, 0.9f, 1f, 0.05f, 0.3f);
        }
        lineStartTime = Time.unscaledTime;
    }

    void EndAll()
    {
        if (mode == Mode.Shopping) GameAudio.Play("SFX/RPG/bookClose", transform.position, 0.6f, 1.1f, 0f, 0.3f);
        RestorePointer();
        bool wasTalking = mode != Mode.Idle;
        mode = Mode.Idle;
        choice = null;
        if (wasTalking) SetPlayerInput(true);
    }

    /// <summary>對話中：不能移動、跳躍、翻滾、攻擊；結束時這一下空白鍵也不會跳</summary>
    void SetPlayerInput(bool on)
    {
        if (controller == null) return;
        if (on)
        {
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
            if (playerHealth == null || !playerHealth.IsDead) controller.inputEnabled = true;
        }
        else
        {
            controller.LockActions();
            controller.inputEnabled = false;
        }
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

    // ---------------- UI ----------------

    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if (GameUI.OverlayOpen) return;
        // 對話框蓋在畫面下方的操作說明（-1100）上面，選項才不會被擋住；劇情對話（-1200）仍在最上層
        GUI.depth = mode == Mode.Idle ? 0 : -1150;
        if (guiding && mode == Mode.Idle && !StoryDialogue.Active) DrawGuideMarker();
        switch (mode)
        {
            case Mode.Talking:
                if (lines != null && lineIndex < lines.Length)
                {
                    bool last = lineIndex == lines.Length - 1;
                    var box = DrawBox(170f, lines[lineIndex]);
                    bool done = VisibleChars() >= lines[lineIndex].Length;
                    float a = done ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f) : 0.4f;
                    Hint(box, $"{lineIndex + 1}/{lines.Length}", "Space", !last ? "繼續" : choice != null ? "回答" : sellsGoods ? "看看商品" : "結束", a);
                }
                break;

            case Mode.Shopping:
                DrawShop();
                break;

            case Mode.Choosing:
                if (choice != null) DrawChoice();
                break;
        }
        GUI.color = Color.white;
    }

    /// <summary>對話框：名字 + 逐字出現的文字，回傳框的位置</summary>
    Rect DrawBox(float h, string line)
    {
        float w = Mathf.Min(900f, Screen.width - 80f);
        var box = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 40f, w, h);
        // 左邊玩家、右邊商人的半身像（商人在說話）
        StoryDialogue.DrawPortraits(box, player, transform, false, true);
        GameUI.Panel(box);
        GUI.color = GameUI.Accent;
        GUI.Label(new Rect(box.x + 24f, box.y + 12f, box.width - 48f, 30f), speakerName, new GUIStyle(GameUI.Body) { fontStyle = FontStyle.Bold });

        int n = Mathf.Clamp(VisibleChars(), 0, line.Length);
        GUI.color = GameUI.TextColor;
        GUI.Label(new Rect(box.x + 24f, box.y + 46f, box.width - 48f, 60f), line.Substring(0, n), GameUI.Body);
        GUI.color = Color.white;
        return box;
    }

    /// <summary>框右下角的操作提示：「1/9　[Space] 繼續」</summary>
    void Hint(Rect box, string left, string key, string action, float alpha)
    {
        float y = box.yMax - 34f;
        var right = GameUI.SmallRight;
        float actionW = right.CalcSize(new GUIContent(action)).x;
        float keyW = 58f;
        float x = box.xMax - 24f - actionW;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(x, y, actionW, 24f), action, right);
        GameUI.Key(new Rect(x - keyW - 6f, y, keyW, 24f), key, alpha);
        GUI.color = new Color(GameUI.MutedColor.r, GameUI.MutedColor.g, GameUI.MutedColor.b, alpha);
        GUI.Label(new Rect(x - keyW - 120f, y, 100f, 24f), left, right);
        GUI.color = Color.white;
    }

    void DrawChoice()
    {
        const float optionH = 42f, gap = 8f, top = 92f;
        var box = DrawBox(top + choice.options.Length * (optionH + gap) + 8f, choice.question);
        float oy = box.y + top;
        int clicked = -1;
        for (int i = 0; i < choice.options.Length; i++)
        {
            var r = new Rect(box.x + 24f, oy, box.width - 48f, optionH);
            if (GameUI.Option(r)) clicked = i;
            GameUI.Key(new Rect(r.x + 10f, r.y + 9f, 24f, 24f), (i + 1).ToString());
            GUI.color = GameUI.TextColor;
            GUI.Label(new Rect(r.x + 44f, r.y, r.width - 52f, r.height), choice.options[i], OptionText);
            GUI.color = Color.white;
            oy += optionH + gap;
        }
        if (clicked >= 0) Pick(clicked);
    }

    void DrawShop()
    {
        var box = DrawBox(290f, shopLine);

        // 右上角：身上的草藥
        GUI.color = GameUI.Good;
        GUI.Label(new Rect(box.x, box.y + 12f, box.width - 24f, 30f), $"{resourceDisplayName}：{Herbs}",
                  new GUIStyle(GameUI.SmallRight) { fontSize = 18, fontStyle = FontStyle.Bold });

        float ox = box.x + 24f, ow = box.width - 48f, oh = 42f;
        float oy = box.y + 104f;
        if (Option(new Rect(ox, oy, ow, oh), "1", "治癒藥水　回復 30% 血量",
                   potionCost, items != null ? items.Count(PlayerItems.HealingPotion) : 0))
            Buy(PlayerItems.HealingPotion);
        oy += oh + 8f;
        if (Option(new Rect(ox, oy, ow, oh), "2", "精力藥水　喝下後精力全滿",
                   staminaPotionCost, items != null ? items.Count(PlayerItems.StaminaPotion) : 0))
            Buy(PlayerItems.StaminaPotion);
        oy += oh + 8f;
        GUI.color = Color.white;
        var leave = new Rect(ox, oy, ow, oh);
        bool leaveClicked = GameUI.Option(leave);
        GameUI.Key(new Rect(leave.x + 10f, leave.y + 9f, 24f, 24f), "3");
        GUI.color = GameUI.TextColor;
        GUI.Label(new Rect(leave.x + 44f, leave.y, leave.width - 52f, leave.height), $"離開（或按 {InteractPrompt.KeyName}）", OptionText);
        GUI.color = Color.white;
        if (leaveClicked) EndAll();
    }

    static GUIStyle OptionText => new GUIStyle(GameUI.Small) { fontSize = 18 };

    [System.NonSerialized] GUIStyle markerStyle;

    /// <summary>商人頭上跳動的「!」和距離；商人在畫面外時貼在畫面邊緣指出方向</summary>
    void DrawGuideMarker()
    {
        var cam = Camera.main;
        if (cam == null || player == null) return;
        Vector3 head = transform.position + Vector3.up * 2.6f;
        Vector3 sp = cam.WorldToScreenPoint(head);
        bool behind = sp.z < 0f;
        if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; } // 在背後：上下左右顛倒
        float x = sp.x, y = Screen.height - sp.y;
        const float m = 48f;
        bool offscreen = behind || x < m || x > Screen.width - m || y < m || y > Screen.height - m;
        if (offscreen)
        {
            // 從畫面中央往商人的方向，貼到畫面邊緣
            Vector2 c = new Vector2(Screen.width, Screen.height) * 0.5f;
            Vector2 d = new Vector2(x, y) - c;
            if (d.sqrMagnitude < 1f) d = Vector2.up;
            float k = Mathf.Min((c.x - m) / Mathf.Max(0.01f, Mathf.Abs(d.x)), (c.y - m) / Mathf.Max(0.01f, Mathf.Abs(d.y)));
            x = c.x + d.x * k;
            y = c.y + d.y * k;
        }
        float bob = Mathf.Sin(Time.unscaledTime * 4f) * 6f;
        markerStyle ??= new GUIStyle(GameUI.Title) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
        GameUI.Shadowed(new Rect(x - 30f, y - 40f + bob, 60f, 50f), "!", markerStyle, PlanetStory.MerchantColor);
        float dist = Vector3.Distance(player.position, transform.position);
        GameUI.Shadowed(new Rect(x - 60f, y + 10f + bob, 120f, 22f), $"{dist:0} m", GameUI.Center, PlanetStory.MerchantColor);
    }

    bool Option(Rect r, string key, string text, int cost, int owned)
    {
        bool afford = Herbs >= cost;
        bool clicked = GameUI.Option(r, afford);
        GameUI.Key(new Rect(r.x + 10f, r.y + 9f, 24f, 24f), key, afford ? 1f : 0.55f);
        GUI.color = afford ? GameUI.TextColor : GameUI.MutedColor;
        GUI.Label(new Rect(r.x + 44f, r.y, r.width - 52f, r.height), text, OptionText);
        GUI.color = afford ? GameUI.Good : GameUI.Bad;
        GUI.Label(new Rect(r.x, r.y, r.width - 14f, r.height), $"持有 {owned}　{resourceDisplayName} ×{cost}",
                  new GUIStyle(GameUI.SmallRight) { fontStyle = FontStyle.Bold });
        return clicked;
    }
}
