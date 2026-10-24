using UnityEngine;

/// <summary>
/// 玩家的精力條（0 ~ 100%）：
///   - 衝刺：每秒扣 5%（有在移動才扣）；精力用完就不能衝刺
///   - 翻滾：一次扣 20%；跳躍（含二段跳）：一次扣 10%；精力不夠就做不出來，精力條會閃一下
///   - 沒在衝刺時（走路或站著，人在地上）每秒回復 20%
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
    public float rollCost = 0.2f;
    [Tooltip("跳躍一次消耗（佔上限的比例）")]
    public float jumpCost = 0.1f;
    [Tooltip("沒在衝刺時（走路或站著）每秒回復（佔上限的比例）")]
    public float idleRegenPerSecond = 0.2f;

    public float Stamina { get; private set; }
    public float Stamina01 => maxStamina > 0f ? Stamina / maxStamina : 0f;
    public bool CanSprint => Stamina > 0f;

    PlayerHealth health;
    float deniedTime = float.NegativeInfinity;

    void Awake()
    {
        Stamina = maxStamina;
        health = GetComponent<PlayerHealth>();
    }

    void OnEnable() { if (health != null) health.OnRespawned += Refill; }
    void OnDisable() { if (health != null) health.OnRespawned -= Refill; }

    public void Refill() => Stamina = maxStamina;

    /// <summary>每幀由移動控制器呼叫：衝刺中扣精力，沒衝刺而且在地上時回復（airborne = 在空中，不回復）</summary>
    public void Tick(float dt, bool airborne, bool sprinting)
    {
        if (sprinting) Stamina = Mathf.Max(0f, Stamina - sprintCostPerSecond * maxStamina * dt);
        else if (!airborne) Stamina = Mathf.Min(maxStamina, Stamina + idleRegenPerSecond * maxStamina * dt);
    }

    public bool CanAffordRoll => Stamina + .001f >= rollCost * maxStamina;
    public bool TrySpendRoll() => TrySpend(rollCost);
    public bool TrySpendJump() => TrySpend(jumpCost);

    bool TrySpend(float ratio)
    {
        float cost = ratio * maxStamina;
        if (Stamina + 0.001f < cost)
        {
            deniedTime = Time.time;
            return false;
        }
        Stamina = Mathf.Max(0f, Stamina - cost);
        return true;
    }

    // ---------- 血條下方的精力條 ----------
    static readonly Color BaseColor = new Color(0.96f, 0.92f, 0.82f);   // 米白
    static readonly Color LowColor = new Color(1f, 0.55f, 0.35f);

    void OnGUI()
    {
        if (GameUI.OverlayOpen || (health != null && health.IsDead)) return;

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
