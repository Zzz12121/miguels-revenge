using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 能力植物：走近按 E 撿起 → 遊戲暫停，三選一升級：
///   1. 跑速  ：走路 / 衝刺速度 ×(1 + speedBonus)
///   2. 攻擊力 ：PlayerCombat.damageMultiplier + attackBonus（近戰和遠程都吃）
///   3. 最大生命：PlayerHealth.maxHealth + healthBonus，並補上同樣的血量
/// 可以用滑鼠點，也可以按 1 / 2 / 3。選完植物就消失。
/// 第一株選的項目會記成玩家的「能力路線」（PlayerPerks.UpgradePath）。
///
/// 勾選 Advanced（第二株，放在 Boss 房火堆旁）：從第一株選的路線的 6 個進階能力中隨機抽 3 個讓玩家選，
/// 內容見 PlayerPerks.OptionsFor。還沒選過第一株的話不能撿。
/// </summary>
public class UpgradePlant : MonoBehaviour
{
    [Header("互動（按鍵統一用 InteractPrompt.InteractKey = E）")]
    [Tooltip("離植物多近可以撿 (m)")]
    public float interactRange = 3f;

    [Header("升級數值")]
    [Tooltip("跑速提升比例（0.15 = +15%）")]
    public float speedBonus = 0.15f;
    [Tooltip("攻擊倍率增加（0.25 = 傷害 +25%）")]
    public float attackBonus = 0.25f;
    [Tooltip("最大生命增加")]
    public float healthBonus = 30f;

    [Header("進階植物（第二株）")]
    [Tooltip("勾選 = 依照第一株植物選的路線，給三選一的進階能力")]
    public bool advanced;
    [Tooltip("這個物件本身沒有模型時，複製這個物件的外觀（例如蜘蛛房的第一株植物）")]
    public GameObject copyVisualFrom;
    [Tooltip("直接用這個 Prefab / 模型當外觀（優先於 copyVisualFrom；被複製的植物撿走後會被刪掉，用 Prefab 比較保險）")]
    public GameObject visualPrefab;
    [Tooltip("visualPrefab 的世界縮放")]
    public float visualScale = 10f;

    Transform player;
    ThirdPersonController controller;
    PlayerCombat combat;
    PlayerHealth health;
    ThirdPersonCamera cam;
    bool menuOpen;
    float prevTimeScale = 1f;
    // NonSerialized：GUIStyle 可以被序列化，不加的話 Unity 會塞一個空白的預設樣式進來（字小、灰色、靠左），
    // 下面的 null 檢查就不會建立正確的樣式
    [System.NonSerialized] GUIStyle optionStyle;

    PlayerPerks perks;
    // 進階植物：打開選單時抽出來的 3 個選項
    PlayerPerks.Option[] offered;

    void Start()
    {
        InitPlayerReferences();
        if ((visualPrefab != null || copyVisualFrom != null) && GetComponentInChildren<Renderer>() == null) CopyVisual();
    }

    /// <summary>複製另一株植物的模型當外觀（只留模型，不帶腳本和碰撞）</summary>
    void CopyVisual()
    {
        var source = visualPrefab != null ? visualPrefab : copyVisualFrom;
        var src = source.transform;
        var copy = Instantiate(source, transform.position, src.rotation, transform);
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (var col in copy.GetComponentsInChildren<Collider>(true)) Destroy(col);
        Vector3 ls = transform.lossyScale, want = visualPrefab != null ? src.localScale * visualScale : src.lossyScale;
        copy.transform.localScale = new Vector3(want.x / ls.x, want.y / ls.y, want.z / ls.z);
        copy.SetActive(true);
        copy.name = "Visual";
    }

    void InitPlayerReferences()
    {
        // 直接找 PlayerHealth：場景裡 PlayerRig 根物件也有 Player 標籤，但它不會跟著角色移動，
        // 用它（或 transform.root）算距離會永遠撿不到
        health = PlayerHealth.Find();
        if (health != null)
        {
            player = health.transform;
            controller = health.GetComponent<ThirdPersonController>();
            combat = health.GetComponent<PlayerCombat>();
            perks = PlayerPerks.For(health.gameObject);
        }
    }

    /// <summary>玩家離植物的水平距離；找不到玩家或玩家死掉時回傳無限大</summary>
    float PlayerDistance()
    {
        if (player == null)
        {
            InitPlayerReferences();
            if (player == null) return float.PositiveInfinity;
        }

        if (health != null && health.IsDead) return float.PositiveInfinity;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (!menuOpen)
        {
            float dist = PlayerDistance();
            if (dist > interactRange) return;
            bool canOpen = CanOpen();
            InteractPrompt.Offer(this, dist,
                canOpen ? $"撿起{(advanced ? "進階" : "")}能力植物" : "植物沒有反應……先到蜘蛛巢穴取得第一株能力植物", canOpen);
            if (InteractPrompt.Pressed(this)) OpenMenu();
            return;
        }

        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Choose(0);
        else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) Choose(1);
        else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) Choose(2);
    }

    /// <summary>進階植物要先選過第一株</summary>
    bool CanOpen() => !advanced || (perks != null && perks.UpgradePath != PlayerPerks.Path.None);

    void OpenMenu()
    {
        if (advanced && perks == null) return;
        if (advanced) offered = perks.RollOptions(perks.UpgradePath, 3);
        VictoryBanner.HideCurrent(); // 剛撿草藥之類的標題還在的話先收掉
        menuOpen = true;
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (controller != null) controller.inputEnabled = false;
        
        cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null) cam.enabled = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.9f, 1.2f, 0f, 0.3f);
    }

    void Choose(int option)
    {
        if (!menuOpen) return;
        string result = "";

        // 確保引用存在，若不存在先重新抓取一次
        if (controller == null || combat == null || health == null)
        {
            InitPlayerReferences();
        }

        if (advanced)
        {
            if (offered == null || option >= offered.Length) return;
            result = perks.Grant(offered[option].perk);
        }
        else
        {
            ApplyBasic(option, ref result);
            RecordPath(option, result);
        }

        menuOpen = false;
        Time.timeScale = prevTimeScale;
        if (controller != null) controller.inputEnabled = true;
        if (cam != null) cam.enabled = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        GameAudio.Play("SFX/RPG/handleCoins", transform.position, 1f, 1f);
        VictoryBanner.ShowSmall(advanced ? "進階能力" : "能力提升", result, new Color(0.5f, 1f, 0.6f));
        Destroy(gameObject);
    }

    /// <summary>第一株：記下玩家選的路線（第二株植物依照這個給選項）</summary>
    void RecordPath(int option, string result)
    {
        if (perks == null || perks.UpgradePath != PlayerPerks.Path.None) return;
        perks.BasicUpgrade = result;
        perks.UpgradePath = option == 0 ? PlayerPerks.Path.Speed
                          : option == 1 ? PlayerPerks.Path.Power
                          : PlayerPerks.Path.Health;
    }

    void ApplyBasic(int option, ref string result)
    {
        switch (option)
        {
            case 0:
                if (controller != null)
                {
                    controller.walkSpeed *= 1f + speedBonus;
                    controller.sprintSpeed *= 1f + speedBonus;
                    result = $"跑速 +{speedBonus * 100f:0}%";
                }
                else
                {
                    result = "跑速提升失敗 (找不到控制器)";
                }
                break;
            case 1:
                if (combat != null)
                {
                    combat.damageMultiplier += attackBonus;
                    result = $"攻擊力 +{attackBonus * 100f:0}%";
                }
                else
                {
                    result = "攻擊力提升失敗 (找不到戰鬥腳本)";
                }
                break;
            default:
                if (health != null)
                {
                    health.IncreaseMaxHealth(healthBonus);
                    result = $"最大生命 +{healthBonus:0}";
                }
                else
                {
                    result = "生命提升失敗 (找不到血量腳本)";
                }
                break;
        }
    }

    void OnGUI()
    {
        if (!menuOpen) return; // 互動提示由 InteractPrompt 統一畫
        if (advanced && perks == null) return;
        // 畫在所有 HUD 和過關標題（VictoryBanner 是 -1000）的上面，不會被別的東西蓋住
        GUI.depth = -1500;

        if (optionStyle == null)
            optionStyle = new GUIStyle(GameUI.Body) { fontSize = 18, alignment = TextAnchor.UpperCenter };

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // 標題 + 三個選項放在同一個面板裡
        const float w = 240f, gap = 24f;
        float h = advanced ? 220f : 170f;
        float panelW = w * 3f + gap * 2f + 60f, panelH = h + 150f;
        var panel = new Rect((Screen.width - panelW) * 0.5f, (Screen.height - panelH) * 0.5f, panelW, panelH);
        GameUI.Panel(panel);
        GUI.color = GameUI.Accent;
        GUI.Label(new Rect(panel.x, panel.y + 18f, panel.width, 44f), advanced ? "選擇一項進階能力" : "選擇一項能力升級", GameUI.Title);
        if (advanced)
        {
            GUI.color = GameUI.MutedColor;
            GUI.Label(new Rect(panel.x, panel.y + 58f, panel.width, 24f), "（從 6 種進階能力中隨機出現 3 種）", GameUI.Center);
        }
        GUI.color = Color.white;

        // 每個選項：名稱（大字）+ 說明
        string[] titles, details;
        if (advanced)
        {
            var opts = offered ?? new PlayerPerks.Option[0];
            titles = new string[opts.Length];
            details = new string[opts.Length];
            for (int i = 0; i < opts.Length; i++) { titles[i] = opts[i].title; details[i] = opts[i].detail; }
        }
        else
        {
            titles = new[] { "跑速", "攻擊力", "最大生命" };
            details = new[] { $"+{speedBonus * 100f:0}%", $"+{attackBonus * 100f:0}%", $"+{healthBonus:0}" };
        }
        var nameStyle = new GUIStyle(GameUI.Title) { fontSize = 24 };
        float x0 = panel.x + 30f, y0 = panel.y + (advanced ? 92f : 80f);
        int chosen = -1;
        for (int i = 0; i < titles.Length; i++)
        {
            var r = new Rect(x0 + i * (w + gap), y0, w, h);
            if (GameUI.Option(r)) chosen = i;
            GameUI.Key(new Rect(r.x + (w - 26f) * 0.5f, r.y + 12f, 26f, 26f), (i + 1).ToString());
            GUI.color = GameUI.Accent;
            GUI.Label(new Rect(r.x, r.y + 48f, r.width, 36f), titles[i], nameStyle);
            GUI.color = GameUI.TextColor;
            GUI.Label(new Rect(r.x + 10f, r.y + 94f, r.width - 20f, r.height - 100f), details[i], optionStyle);
            GUI.color = Color.white;
        }
        // 畫完才處理選擇（Choose 會刪掉這個物件）
        if (chosen >= 0) Choose(chosen);

        GUI.color = GameUI.MutedColor;
        GUI.Label(new Rect(panel.x, panel.yMax - 46f, panel.width, 30f), "點擊或按 1 / 2 / 3 選擇", GameUI.Center);
        GUI.color = Color.white;
    }

    void OnDisable()
    {
        if (!menuOpen) return;
        menuOpen = false;
        Time.timeScale = prevTimeScale;
        if (controller != null) controller.inputEnabled = true;
        if (cam != null) cam.enabled = true;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
