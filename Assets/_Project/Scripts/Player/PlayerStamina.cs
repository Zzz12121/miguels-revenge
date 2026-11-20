using UnityEngine;

/// <summary>
/// 玩家的精力條（0 ~ 100%）：
///   - 衝刺：每秒扣 5%（有在移動才扣）；精力用完就不能衝刺
///   - 翻滾：一次扣 12%；跳躍（含二段跳）：一次扣 10%；精力不夠就做不出來，精力條會閃一下
///   - 拉索：一次 25% ~ 75%（照距離，越遠越多；EnergyTetherSkill 呼叫 TrySpendRatio）
///   - 沒在衝刺時（走路或站著，人在地上）每秒回復 20%
///   - 額外精力（茶屋的甜點，AddBonus）：精力條後面多出一截金色的，花精力時先扣它、不會回復；倒下就沒了。
///     存在 static 裡，換關卡還留著（不寫進存檔）
/// 由 ThirdPersonController 呼叫 Tick / TrySpend；死亡重生時補滿。PlayerHealth 會自動加上這個元件。
/// UI 畫在血條正下方（米白色）。
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    [Tooltip("精力上限")]
    public float maxStamina = 100f;
    [Tooltip("衝刺時每秒消耗（佔上限的比例）")]
    public float sprintCostPerSecond = 0.05f;
    [Tooltip("翻滾一次消耗（佔上限的比例）")]
    public float rollCost = 0.12f;
    [Tooltip("跳躍一次消耗（佔上限的比例）")]
    public float jumpCost = 0.1f;
    [Tooltip("沒在衝刺時（走路或站著）每秒回復（佔上限的比例）")]
    public float idleRegenPerSecond = 0.2f;

    public float Stamina { get; private set; }
    public float Stamina01 => maxStamina > 0f ? Stamina / maxStamina : 0f;
    public bool CanSprint => Stamina > 0f || bonus > 0f;

    // 額外精力（佔上限的比例）：玩家只有一個，換場景後新的 PlayerStamina 接著用
    static float bonus;
    /// <summary>額外精力（佔上限的比例，0 = 沒有）</summary>
    public float Bonus01 => bonus;
    public static float BonusRatio => bonus;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => bonus = 0f;

    /// <summary>加上額外精力（ratio = 佔上限的比例，不會疊超過 ratio）：回傳 false = 已經有這麼多了</summary>
    public static bool AddBonus(float ratio)
    {
        if (bonus + 0.001f >= ratio) return false;
        bonus = ratio;
        return true;
    }

    PlayerHealth health;
    float deniedTime = float.NegativeInfinity;

    void Awake()
    {
        Stamina = maxStamina;
        health = GetComponent<PlayerHealth>();
    }

    void OnEnable() { if (health != null) { health.OnRespawned += Refill; health.OnDied += LoseBonus; } }
    void OnDisable() { if (health != null) { health.OnRespawned -= Refill; health.OnDied -= LoseBonus; } }

    static void LoseBonus() => bonus = 0f;

    public void Refill() => Stamina = maxStamina;

    /// <summary>每幀由移動控制器呼叫：衝刺中扣精力，沒衝刺而且在地上時回復（airborne = 在空中，不回復）</summary>
    public void Tick(float dt, bool airborne, bool sprinting)
    {
        if (sprinting) Spend(sprintCostPerSecond * maxStamina * dt);
        else if (!airborne) Stamina = Mathf.Min(maxStamina, Stamina + idleRegenPerSecond * maxStamina * dt);
    }

    public bool CanAffordRoll => Stamina + bonus * maxStamina + .001f >= rollCost * maxStamina;
    public bool TrySpendRoll() => TrySpend(rollCost);
    public bool TrySpendJump() => TrySpend(jumpCost);
    /// <summary>扣掉 ratio（佔上限的比例）的精力；不夠就不扣、精力條閃一下，回傳 false（拉索用）</summary>
    public bool TrySpendRatio(float ratio) => TrySpend(ratio);
    public bool CanAfford(float ratio) => Stamina + bonus * maxStamina + .001f >= ratio * maxStamina;
    /// <summary>精力條閃一下紅（精力不夠）</summary>
    public void FlashDenied() => deniedTime = Time.time;

    bool TrySpend(float ratio)
    {
        float cost = ratio * maxStamina;
        if (Stamina + bonus * maxStamina + 0.001f < cost)
        {
            deniedTime = Time.time;
            return false;
        }
        Spend(cost);
        return true;
    }

    /// <summary>扣精力：先扣額外精力，不夠再扣原本的</summary>
    void Spend(float amount)
    {
        if (bonus > 0f && maxStamina > 0f)
        {
            float fromBonus = Mathf.Min(bonus * maxStamina, amount);
            bonus = Mathf.Max(0f, bonus - fromBonus / maxStamina);
            if (bonus < 0.0005f) bonus = 0f;
            amount -= fromBonus;
        }
        Stamina = Mathf.Max(0f, Stamina - amount);
    }

    // ---------- 血條下方的精力條 ----------
    static readonly Color BaseColor = new Color(0.96f, 0.92f, 0.82f);   // 米白
    static readonly Color LowColor = new Color(1f, 0.55f, 0.35f);

    void OnGUI()
    {
        if (GameUI.OverlayOpen || (health != null && health.IsDead)) return;
        if (PlayerHealth.SimpleBarHidden) return; // VitalsHud 畫日式的精力條

        const float w = 260f, h = 10f, x = 20f, y = 44f;
        float denied = 1f - Mathf.Clamp01((Time.time - deniedTime) / 0.5f);
        bool low = Stamina01 < 0.2f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        // 精力不夠時整條閃一下紅
        if (denied > 0f)
        {
            GUI.color = new Color(1f, 0.2f, 0.15f, 0.5f * denied * Mathf.Abs(Mathf.Sin(Time.time * 25f)));
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        }
        GUI.color = low ? Color.Lerp(LowColor, BaseColor, 0.3f) : BaseColor;
        GUI.DrawTexture(new Rect(x, y, w * Stamina01, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
