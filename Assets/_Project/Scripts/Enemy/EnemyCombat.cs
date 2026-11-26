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

    // ---------------- 招式 ----------------

    /// <summary>骷髏士兵的四種攻擊招式（舉盾格擋在 EnemyShieldBlock）</summary>
    public enum Move { DiagonalSlash, Thrust, Overhead, Sweep }

    [System.Serializable]
    public class MoveSpec
    {
        public Move move;
        [Tooltip("顯示用的名稱")]
        public string label;
        [Tooltip("被選中的機率權重（0 = 不用這招）")]
        [Range(0f, 5f)] public float weight = 1f;
        [Tooltip("傷害倍率（乘在 comboDamage 上）")]
        public float damageScale = 1f;
        [Tooltip("抬手 / 揮的判定時間 / 收招硬直 (秒)")]
        public float windup = 0.35f, swing = 0.16f, recovery = 0.35f;
        [Tooltip("判定球離身體多遠、半徑多大 (m)")]
        public float reach = 1.1f, radius = 1.1f;
        [Tooltip("出招時往前踏多猛")]
        public float lunge = 4f;
        [Tooltip("擊退倍率")]
        public float knockbackScale = 1f;
        [Tooltip("目標比這個距離遠時，這招的機率 ×3（突刺用：玩家稍微拉開距離就刺過去）(m)；0 = 不管距離")]
        public float favoredBeyond = 0f;
    }

    [Header("招式（骷髏士兵：斜砍 / 突刺 / 直砍 / 橫掃）")]
    [Tooltip("勾選後每一刀從下面的招式隨機挑一招（判定範圍、節奏、傷害、動畫都不同）；不勾 = 原本的三段連擊")]
    public bool useMoveSet = false;
    public MoveSpec[] moves = DefaultMoves();

    /// <summary>這一刀用的招式（useMoveSet 關閉時是 null）</summary>
    public MoveSpec CurrentMove { get; private set; }

    public static MoveSpec[] DefaultMoves() => new[]
    {
        // 斜砍：右上往左下，標準的一刀
        new MoveSpec { move = Move.DiagonalSlash, label = "斜砍", weight = 1.2f, damageScale = 1f,
                       windup = 0.35f, swing = 0.16f, recovery = 0.35f, reach = 1.1f, radius = 1.1f, lunge = 4f },
        // 突刺：抬手稍久、往前衝，打得遠但很窄
        new MoveSpec { move = Move.Thrust, label = "突刺", weight = 1f, damageScale = 0.9f,
                       windup = 0.42f, swing = 0.12f, recovery = 0.45f, reach = 1.9f, radius = 0.65f, lunge = 8f,
                       knockbackScale = 0.8f, favoredBeyond = 2f },
        // 直砍：高舉過頭重重劈下，慢、痛、擊退大
        new MoveSpec { move = Move.Overhead, label = "直砍", weight = 0.8f, damageScale = 1.45f,
                       windup = 0.55f, swing = 0.14f, recovery = 0.6f, reach = 1.25f, radius = 0.95f, lunge = 3f,
                       knockbackScale = 1.4f },
        // 橫掃：貼身大範圍水平掃一圈，傷害低
        new MoveSpec { move = Move.Sweep, label = "橫掃", weight = 1f, damageScale = 0.8f,
                       windup = 0.4f, swing = 0.22f, recovery = 0.4f, reach = 0.8f, radius = 1.7f, lunge = 2f,
                       knockbackScale = 1.1f },
    };

    [Header("出招轉身（流暢度）")]
    [Tooltip("每一刀開始時最多瞬間轉幾度（剩下的在抬手期間慢慢轉過去，不會一下子啪地轉向）")]
    public float maxSnapAngle = 35f;
    [Tooltip("抬手期間追著目標轉身的速度 (度/秒)；砍下去之後方向就固定")]
    public float windupTurnSpeed = 300f;

    float swingStartedAt = float.NegativeInfinity, swingWindup;

    float[] baseDamage;
    float baseReach, baseRadius, baseLunge;
    Move lastMove;
    bool hasLastMove;

    protected override void Awake()
    {
        base.Awake();
        baseDamage = (float[])comboDamage.Clone();
        baseReach = hitForwardOffset;
        baseRadius = hitRadius;
        baseLunge = lungeForce;
    }

    /// <summary>挑這一刀的招式並套用它的判定範圍、節奏、傷害（在 MeleeCombat 讀這些數值之前呼叫）</summary>
    void ApplyMove(int index)
    {
        if (!useMoveSet || moves == null || moves.Length == 0)
        {
            CurrentMove = null;
            return;
        }
        float dist = target != null ? Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up).magnitude : 0f;
        float total = 0f;
        foreach (var m in moves) total += MoveWeight(m, dist);
        MoveSpec pick = moves[0];
        if (total > 0f)
        {
            float r = Random.value * total;
            foreach (var m in moves)
            {
                r -= MoveWeight(m, dist);
                if (r <= 0f) { pick = m; break; }
            }
        }
        CurrentMove = pick;
        lastMove = pick.move;
        hasLastMove = true;

        int n = comboDamage.Length;
        if (stepWindup == null || stepWindup.Length != n) { stepWindup = new float[n]; stepSwing = new float[n]; stepRecovery = new float[n]; }
        int i = Mathf.Clamp(index, 0, n - 1);
        stepWindup[i] = pick.windup;
        stepSwing[i] = pick.swing;
        stepRecovery[i] = pick.recovery;
        comboDamage[i] = baseDamage[Mathf.Clamp(i, 0, baseDamage.Length - 1)] * pick.damageScale;
        hitForwardOffset = pick.reach > 0f ? pick.reach : baseReach;
        hitRadius = pick.radius > 0f ? pick.radius : baseRadius;
        lungeForce = pick.lunge >= 0f ? pick.lunge : baseLunge;
    }

    float MoveWeight(MoveSpec m, float dist)
    {
        float w = Mathf.Max(0f, m.weight);
        if (m.favoredBeyond > 0f && dist > m.favoredBeyond) w *= 3f;
        if (hasLastMove && m.move == lastMove) w *= 0.35f; // 不太會連續出同一招
        return w;
    }

    int hitsLeft;
    // 這一輪連擊打中玩家時用的受擊姿勢與擊退倍率（EnemySquadTactics 的接力劈砍會指定）
    PlayerHitReaction comboReaction = PlayerHitReaction.Auto;
    float comboKnockScale = 1f;

    protected override float PitchScale => 0.85f; // 小怪的揮劍聲比較低沉
    protected override bool AllowAirAttacks => false; // 小怪不做空中攻擊

    /// <summary>開始一輪連擊，打 hits 下（1 ~ 連擊段數）。已經在攻擊中就回傳 false</summary>
    public bool StartCombo(int hits) => StartCombo(hits, PlayerHitReaction.Auto, 1f);

    /// <summary>開始一輪連擊，並指定打中玩家時的受擊姿勢（例如防禦姿勢 / 打飛）與擊退倍率</summary>
    public bool StartCombo(int hits, PlayerHitReaction reaction, float knockbackScale)
    {
        if (IsAttacking) return false;
        hitsLeft = Mathf.Clamp(hits, 1, comboDamage.Length);
        comboReaction = reaction;
        comboKnockScale = knockbackScale;
        return BeginAttack();
    }

    protected override void OnSwingStart(int index)
    {
        hitsLeft--;
        ApplyMove(index);
        swingStartedAt = Time.time;
        swingWindup = WindupFor(index);
        if (attackVfx) StartCoroutine(SlashVfxAfterWindup(WindupFor(index)));
    }

    /// <summary>抬手結束、真的砍下去的那一刻才噴火花</summary>
    System.Collections.IEnumerator SlashVfxAfterWindup(float windup)
    {
        yield return new WaitForSeconds(windup);
        Vector3 fwd = transform.forward;
        CombatVFX.Slash(transform.position + Vector3.up * 1.1f + fwd * 0.6f, fwd, attackVfxColor);
    }

    protected override bool WantsNextHit() => hitsLeft > 0;

    protected override Vector3 GetAimDirection()
    {
        Vector3 want = FlatToTarget();
        if (want.sqrMagnitude < 0.0001f) return transform.forward;
        // 只瞬間轉一小段，避免出招那一幀整個人啪地轉過去
        float angle = Vector3.SignedAngle(transform.forward, want, Vector3.up);
        return Quaternion.AngleAxis(Mathf.Clamp(angle, -maxSnapAngle, maxSnapAngle), Vector3.up) * transform.forward;
    }

    Vector3 FlatToTarget()
    {
        if (target == null) return transform.forward;
        Vector3 d = target.position - transform.position;
        d.y = 0f;
        return d;
    }

    void LateUpdate()
    {
        // 抬手期間持續轉向目標（砍下去之後方向固定，玩家才閃得掉）
        if (!IsAttacking || target == null || Time.time > swingStartedAt + swingWindup) return;
        Vector3 want = FlatToTarget();
        if (want.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(want),
                                                      windupTurnSpeed * Time.deltaTime);
    }

    protected override bool TryDamage(Collider c, float damage, Vector3 hitPoint, HashSet<object> alreadyHit)
    {
        // 玩家的幻影分身：打一下就消散
        var decoy = c.GetComponentInParent<PhantomDecoy>();
        if (decoy != null)
        {
            if (!decoy.IsLure || alreadyHit.Contains(decoy)) return false;
            alreadyHit.Add(decoy);
            decoy.Break();
            if (attackVfx) CombatVFX.Impact(hitPoint, attackVfxColor);
            return true;
        }

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
        Vector3 push = dir.normalized * knockback * comboKnockScale * (CurrentMove != null ? CurrentMove.knockbackScale : 1f)
                     + Vector3.up * (comboReaction == PlayerHitReaction.KnockedFlying ? 7f : 3f);
        if (!player.TakeDamage(damage, push, false, "被打倒了", true, comboReaction)) return false;
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
