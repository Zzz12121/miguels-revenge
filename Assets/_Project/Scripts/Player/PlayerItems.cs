using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家的消耗品（在商人那裡用草藥買）：
///   - 治癒藥水：按 Q 喝，回復 15% 最大生命
///   - 火焰彈　：按 F 往鏡頭方向丟出，爆炸後讓範圍內的敵人燃燒
/// 數量存在 PlayerInventory（跟草藥一樣），所以會跟著換關卡 / 存檔。
/// 治癒藥水像原素瓶：死亡重生後補回「買過的總數」，喝掉的會回來；火焰彈用掉就沒了。
/// 身上有消耗品時，畫面右下角顯示數量和按鍵。PlayerHealth 會自動加上這個元件。
/// </summary>
public class PlayerItems : MonoBehaviour
{
    public const string HealingPotion = "HealingPotion";
    /// <summary>買過幾瓶治癒藥水（重生時補到這個數量）</summary>
    public const string HealingPotionMax = "HealingPotionMax";
    public const string FireBombItem = "FireBomb";

    public Key potionKey = Key.Q;
    public Key bombKey = Key.F;
    [Tooltip("治癒藥水回復最大生命的比例")]
    public float potionHealRatio = 0.15f;
    [Tooltip("火焰彈往前丟的速度 / 往上的速度")]
    public float throwSpeed = 16f, throwUp = 5f;
    [Tooltip("兩次使用之間的冷卻 (秒)")]
    public float useCooldown = 0.6f;

    PlayerHealth health;
    ThirdPersonController controller;
    float nextUseTime;
    float messageTime = float.NegativeInfinity;
    string message;
    GUIStyle boxStyle, keyStyle;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        controller = GetComponent<ThirdPersonController>();
    }

    void OnEnable()
    {
        if (health != null) health.OnRespawned += RefillPotions;
    }

    void OnDisable()
    {
        if (health != null) health.OnRespawned -= RefillPotions;
    }

    /// <summary>重生時：治癒藥水補回買過的總數</summary>
    void RefillPotions()
    {
        int max = Count(HealingPotionMax), now = Count(HealingPotion);
        if (max <= now) return;
        Inventory.AddResource(HealingPotion, max - now);
        Say($"治癒藥水已補滿（{max}）");
    }

    PlayerInventory Inventory => GetComponent<PlayerInventory>();

    public int Count(string item)
    {
        var inv = Inventory;
        return inv != null ? inv.GetResource(item) : 0;
    }

    public void Add(string item, int amount)
    {
        var inv = Inventory;
        if (inv == null) inv = gameObject.AddComponent<PlayerInventory>();
        inv.AddResource(item, amount);
        if (item == HealingPotion) inv.AddResource(HealingPotionMax, amount);
    }

    /// <summary>現在能不能使用道具（死亡、對話、選單、暫停中都不行）</summary>
    bool CanUse()
    {
        if (health != null && health.IsDead) return false;
        if (controller != null && !controller.inputEnabled) return false;
        if (Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked) return false;
        var gm = GameManager.Instance;
        return gm == null || !gm.IsOverlayOpen;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || Time.time < nextUseTime || !CanUse()) return;
        if (kb[potionKey].wasPressedThisFrame) DrinkPotion();
        else if (kb[bombKey].wasPressedThisFrame) ThrowBomb();
    }

    void DrinkPotion()
    {
        if (Count(HealingPotion) <= 0) { Say("沒有治癒藥水"); return; }
        if (health.Health >= health.maxHealth) { Say("血量已滿"); return; }
        Inventory.ConsumeResource(HealingPotion, 1);
        nextUseTime = Time.time + useCooldown;
        float amount = health.maxHealth * potionHealRatio;
        health.Heal(amount);
        CombatVFX.Heal(transform);
        GameAudio.Play("SFX/RPG/bookPlace1", transform.position, 0.8f, 1.4f, 0.05f, 0.3f);
        Say($"回復 {amount:0} 點生命");
    }

    void ThrowBomb()
    {
        if (Count(FireBombItem) <= 0) { Say("沒有火焰彈"); return; }
        Inventory.ConsumeResource(FireBombItem, 1);
        nextUseTime = Time.time + useCooldown;

        // 往鏡頭看的方向丟（水平方向），帶一點往上的弧度
        Vector3 dir = transform.forward;
        var cam = Camera.main;
        if (cam != null)
        {
            dir = cam.transform.forward;
            dir.y = Mathf.Max(dir.y, -0.2f);
        }
        Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
        if (controller != null) controller.FaceDirection(flat);
        Vector3 origin = transform.position + Vector3.up * 1.2f + flat * 0.6f;
        FireBomb.Throw(origin, dir.normalized * throwSpeed + Vector3.up * throwUp, transform);
        GameAudio.Play("SFX/RPG/cloth2", origin, 0.9f, 1.2f, 0.08f, 0.5f);
    }

    void Say(string text)
    {
        message = text;
        messageTime = Time.time;
    }

    // ---------- 右下角：身上的消耗品 ----------
    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.IsOverlayOpen) return;
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        const float w = 210f, h = 40f, gap = 8f, margin = 24f;
        float x = Screen.width - w - margin;
        float y = Screen.height - margin - h;
        int potions = Count(HealingPotion), potionMax = Count(HealingPotionMax), bombs = Count(FireBombItem);
        if (bombs > 0)
        {
            Slot(x, y, w, h, bombKey, "火焰彈", $"×{bombs}", FireBomb.FireColor);
            y -= h + gap;
        }
        // 喝光了也繼續顯示（0 / 3），提醒重生後會補回來
        if (potions > 0 || potionMax > 0)
        {
            var c = potions > 0 ? new Color(0.4f, 1f, 0.5f) : new Color(0.55f, 0.65f, 0.55f);
            Slot(x, y, w, h, potionKey, "治癒藥水", potionMax > 0 ? $"{potions}/{potionMax}" : $"×{potions}", c);
            y -= h + gap;
        }

        float since = Time.time - messageTime;
        if (since < 1.5f)
        {
            GUI.color = new Color(1f, 1f, 1f, 1f - since / 1.5f);
            var right = new GUIStyle(boxStyle) { alignment = TextAnchor.MiddleRight, fontSize = 16 };
            GUI.Label(new Rect(Screen.width - 400f - margin, y, 400f, h), message, right);
        }
        GUI.color = Color.white;
    }

    void Slot(float x, float y, float w, float h, Key key, string label, string count, Color color)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, 4f, h), Texture2D.whiteTexture);

        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(new Rect(x + 12f, y + 7f, 26f, 26f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 12f, y + 7f, 26f, 26f), key.ToString(), keyStyle);

        GUI.color = color;
        GUI.Label(new Rect(x + 48f, y, w - 56f, h), $"{label}  {count}", boxStyle);
    }
}
