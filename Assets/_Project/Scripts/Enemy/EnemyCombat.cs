using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 小怪的攻擊：和玩家一樣的三段揮劍（共用 MeleeCombat），由 EnemyAI 決定一輪要打幾下。
/// 只會打到玩家，不會打到其他小怪（揮劍時掃到場景裡的木桶、火把會把它們打翻）。
/// 預設的抬手時間比玩家長，讓玩家看得出來、來得及閃。
/// </summary>
public class EnemyCombat : MeleeCombat
{
    [Header("小怪")]
    [Tooltip("打中玩家時的擊退力道")]
    public float knockback = 6f;

    [Header("攻擊特效")]
    [Tooltip("揮劍時往前噴火花、打中玩家時炸開火花")]
    public bool attackVfx = true;
    [Tooltip("火花顏色（骷髏預設淡綠色，聖騎士由安裝選單設成橘紅色）")]
    public Color attackVfxColor = new Color(0.55f, 1f, 0.6f);

    /// <summary>攻擊目標（EnemyAI 會自動設定成玩家）</summary>
    [HideInInspector] public Transform target;

    int hitsLeft;

    protected override float PitchScale => 0.85f; // 小怪的揮劍聲比較低沉
    protected override bool AllowAirAttacks => false; // 小怪不做空中攻擊

    /// <summary>開始一輪連擊，打 hits 下（1 ~ 連擊段數）。已經在攻擊中就回傳 false</summary>
    public bool StartCombo(int hits)
    {
        if (IsAttacking) return false;
        hitsLeft = Mathf.Clamp(hits, 1, comboDamage.Length);
        return BeginAttack();
    }

    protected override void OnSwingStart(int index)
    {
        hitsLeft--;
        if (attackVfx) StartCoroutine(SlashVfxAfterWindup());
    }

    /// <summary>抬手結束、真的砍下去的那一刻才噴火花</summary>
    System.Collections.IEnumerator SlashVfxAfterWindup()
    {
        yield return new WaitForSeconds(windupTime);
        Vector3 fwd = transform.forward;
        CombatVFX.Slash(transform.position + Vector3.up * 1.1f + fwd * 0.6f, fwd, attackVfxColor);
    }

    protected override bool WantsNextHit() => hitsLeft > 0;

    protected override Vector3 GetAimDirection() =>
        target != null ? target.position - transform.position : transform.forward;

    protected override bool TryDamage(Collider c, float damage, Vector3 hitPoint, HashSet<object> alreadyHit)
    {
        // 場景裡的木桶、火把：小怪不會特地去打，但揮劍時掃到一樣會打翻
        var prop = c.GetComponentInParent<Knockable>();
        if (prop != null)
        {
            if (prop.IsDead || alreadyHit.Contains(prop)) return false;
            alreadyHit.Add(prop);
            prop.Knock(transform.position, damage);
            return true;
        }

        var player = c.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead || alreadyHit.Contains(player)) return false;

        alreadyHit.Add(player);
        Vector3 dir = player.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        player.TakeDamage(damage, dir.normalized * knockback + Vector3.up * 3f);
        if (attackVfx) CombatVFX.Impact(hitPoint, attackVfxColor);
        return true;
    }

    // 在編輯器手動加這個元件時，套用小怪用的預設值（比玩家慢、比玩家弱）
    void Reset()
    {
        comboDamage = new[] { 8f, 8f, 14f };
        windupTime = 0.35f;
        swingTime = 0.16f;
        recoveryTime = 0.35f;
        finisherRecoveryTime = 0.7f;
        hitForwardOffset = 1.1f;
        hitRadius = 1.1f;
        attackMoveMultiplier = 0.1f;
        lungeForce = 4f;
        weaponColor = new Color(0.35f, 0.33f, 0.3f);
    }
}
