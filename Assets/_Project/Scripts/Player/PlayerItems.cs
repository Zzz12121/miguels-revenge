using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家的消耗品（在商人那裡用草藥買）：
///   - 治癒藥水：按 Q 喝，花 1.5 秒喝完（期間移動變慢、不能做其他動作），喝完回復 30% 最大生命
///   - 火焰彈　：按 G 往鏡頭方向丟出，爆炸後讓範圍內的敵人燃燒
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
    public Key bombKey = Key.G;
    [Tooltip("治癒藥水回復最大生命的比例")]
    public float potionHealRatio = 0.3f;
    [Tooltip("喝藥水要花多久 (秒)")]
    public float drinkTime = 1.5f;
    [Tooltip("喝藥水時的移動速度倍率")]
    [Range(0f, 1f)] public float drinkMoveMultiplier = 0.35f;
    [Tooltip("火焰彈往前丟的速度 / 往上的速度")]
    public float throwSpeed = 16f, throwUp = 5f;
    [Tooltip("兩次使用之間的冷卻 (秒)")]
    public float useCooldown = 0.6f;

    /// <summary>正在喝藥水</summary>
    public bool IsDrinking { get; private set; }
    float drinkStart;
    Coroutine drinkRoutine;

    PlayerHealth health;
    ThirdPersonController controller;
    PlayerCombat combat;
    float nextUseTime;
    float messageTime = float.NegativeInfinity;
    string message;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        controller = GetComponent<ThirdPersonController>();
        combat = GetComponent<PlayerCombat>();
    }

    void OnEnable()
    {
        if (health != null)
        {
            health.OnRespawned += RefillPotions;
            health.OnDied += CancelDrink;
        }
    }

    void OnDisable()
    {
        if (health != null)
        {
            health.OnRespawned -= RefillPotions;
            health.OnDied -= CancelDrink;
        }
        CancelDrink();
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

    /// <summary>現在能不能使用道具（死亡、對話、選單、暫停、喝藥中、翻滾中都不行）</summary>
    bool CanUse()
    {
        if (IsDrinking || (health != null && health.IsDead)) return false;
        if (controller != null && (!controller.inputEnabled || controller.ActionsLocked || controller.IsRolling)) return false;
        if (combat != null && (combat.IsAttacking || combat.IsShieldBashing)) return false;
        if (Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked) return false;
        return !GameUI.OverlayOpen;
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
        drinkRoutine = StartCoroutine(Drink());
    }

    /// <summary>喝藥水：drinkTime 秒內移動變慢、不能做其他動作，喝完才回血</summary>
    IEnumerator Drink()
    {
        IsDrinking = true;
        drinkStart = Time.time;
        if (controller != null)
        {
            controller.LockActions();
            controller.ApplyTemporarySpeedMultiplier(drinkMoveMultiplier, drinkTime);
        }
        GameAudio.Play("SFX/RPG/beltHandle1", transform.position, 0.7f, 1.2f, 0.05f, 0.3f);

        yield return new WaitForSeconds(drinkTime);

        float amount = health.maxHealth * potionHealRatio;
        health.Heal(amount);
        CombatVFX.Heal(transform);
        GameAudio.Play("SFX/RPG/bookPlace1", transform.position, 0.8f, 1.4f, 0.05f, 0.3f);
        Say($"回復 {amount:0} 點生命");
        EndDrink();
    }

    void EndDrink()
    {
        if (!IsDrinking) return;
        IsDrinking = false;
        drinkRoutine = null;
        nextUseTime = Time.time + useCooldown;
        if (controller != null) controller.UnlockActions();
    }

    /// <summary>死掉時中斷（藥水已經喝掉了，重生時會補回來）</summary>
    void CancelDrink()
    {
        if (drinkRoutine != null) StopCoroutine(drinkRoutine);
        EndDrink();
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

    // ---------- 右下角：身上的消耗品；喝藥水時畫面下方顯示進度 ----------
    void OnGUI()
    {
        if (GameUI.OverlayOpen) return;

        const float w = 220f, h = 40f, gap = 8f, margin = 24f;
        float x = Screen.width - w - margin;
        float y = Screen.height - margin - h;
        int potions = Count(HealingPotion), potionMax = Count(HealingPotionMax), bombs = Count(FireBombItem);
        if (bombs > 0)
        {
            Slot(new Rect(x, y, w, h), bombKey, "火焰彈", $"×{bombs}", FireBomb.FireColor, true);
            y -= h + gap;
        }
        // 喝光了也繼續顯示（0 / 3），提醒重生後會補回來
        if (potions > 0 || potionMax > 0)
        {
            string count = potionMax > 0 ? $"{potions}/{potionMax}" : $"×{potions}";
            Slot(new Rect(x, y, w, h), potionKey, "治癒藥水", count, GameUI.Good, potions > 0);
            y -= h + gap;
        }

        float since = Time.time - messageTime;
        if (since < 1.5f)
            GameUI.Shadowed(new Rect(Screen.width - 420f - margin, y, 420f, h), message, GameUI.SmallRight,
                            new Color(1f, 1f, 1f, 1f - since / 1.5f));

        if (IsDrinking)
        {
            float t = (Time.time - drinkStart) / drinkTime;
            var bar = new Rect((Screen.width - 260f) * 0.5f, Screen.height * 0.64f, 260f, 10f);
            GameUI.Shadowed(new Rect(bar.x, bar.y - 28f, bar.width, 24f), "飲用治癒藥水中……", GameUI.Center, GameUI.Good);
            GameUI.ProgressBar(bar, t, GameUI.Good);
        }
    }

    void Slot(Rect r, Key key, string label, string count, Color color, bool available)
    {
        GameUI.Panel(r, color);
        float a = available ? 1f : 0.45f;
        GameUI.Key(new Rect(r.x + 10f, r.y + 8f, 24f, 24f), key.ToString(), a);
        GUI.color = new Color(color.r, color.g, color.b, a);
        GUI.Label(new Rect(r.x + 44f, r.y, r.width - 52f, r.height), label, GameUI.Small);
        GUI.Label(new Rect(r.x, r.y, r.width - 12f, r.height), count, GameUI.SmallRight);
        GUI.color = Color.white;
    }
}
