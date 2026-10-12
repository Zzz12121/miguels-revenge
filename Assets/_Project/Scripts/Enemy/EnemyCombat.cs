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

    /// <summary>攻擊目標（EnemyAI 會自動設定成玩家）</summary>
    [HideInInspector] public Transform target;

    int hitsLeft;

    protected override float PitchScale => 0.85f; // 小怪的揮劍聲比較低沉

    /// <summary>開始一輪連擊，打 hits 下（1 ~ 連擊段數）。已經在攻擊中就回傳 false</summary>
    public bool StartCombo(int hits)
    {
        if (IsAttacking) return false;
        hitsLeft = Mathf.Clamp(hits, 1, comboDamage.Length);
        return BeginAttack();
    }

    protected override void OnSwingStart(int index) => hitsLeft--;

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
