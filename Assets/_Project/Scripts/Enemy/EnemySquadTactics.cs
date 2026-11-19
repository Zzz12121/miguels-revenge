using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 骷髏士兵的圍攻戰術：附近有兩隻以上在追同一個玩家時，自動分工——
///   1. 包圍：大家平均分散站在玩家四周的一圈上（不會全部擠在正面）
///   2. 接力劈砍：一次只有一隻衝上去砍一刀，砍下去的那一刻，下一隻（圈上順時針的下一個）已經在旁邊準備，
///      隔 chainGap 秒接著從另一個方向砍下來；砍到的那幾下玩家會「定在原地防禦」（GuardBrace）
///   3. 收尾：一輪的最後一刀是重擊，把玩家打飛（KnockedFlying）；之後大家退回圈上喘口氣，再開始下一輪
/// 只剩一隻時自動回到 EnemyAI 原本的單挑行為。
/// 站位會避開沒有地面、被牆擋住的地方：在窄橋上自動排成前後兩排輪流上，不會擠到扶手外面卡牆。
/// 等待時面向玩家橫著走（播橫移動畫）；一輪結束休息時，偶爾有一隻吼一聲挑釁。
///
/// 用法：掛在有 EnemyAI 的小怪上（Enemy_Grunt / 骷髏騎士）。不掛就跟以前一模一樣。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyAI))]
public class EnemySquadTactics : MonoBehaviour
{
    [Header("包圍")]
    [Tooltip("離玩家這麼近的同伴才會一起圍攻 (m)")]
    public float engageRadius = 12f;
    [Tooltip("等待出手時站在離玩家多遠的圈上 (m)")]
    public float ringRadius = 3.8f;
    [Tooltip("下一個要出手的站得比較近 (m)，一輪到就能馬上補刀")]
    public float onDeckRadius = 2.9f;
    [Tooltip("走去圈上站位時的速度比例")]
    [Range(0.2f, 1f)] public float circleSpeed = 0.6f;

    [Header("接力劈砍")]
    [Tooltip("上一刀砍下去之後，隔多久下一隻接著砍下去 (秒)。比玩家的防禦僵直長一點，玩家才有機會翻滾逃出去")]
    public float chainGap = 0.55f;
    [Tooltip("一輪最多接幾刀（不會超過正在圍攻的數量 + 1）")]
    [Range(2, 6)] public int maxChain = 4;
    [Tooltip("一輪結束後大家休息多久才開始下一輪 (秒，隨機範圍)")]
    public Vector2 roundRest = new Vector2(1.6f, 2.6f);
    [Tooltip("接力時每一刀打中玩家的受擊姿勢")]
    public PlayerHitReaction chainReaction = PlayerHitReaction.GuardBrace;
    [Tooltip("最後一刀把玩家打飛")]
    public bool finisherKnocksFlying = true;
    [Tooltip("最後一刀的擊退倍率")]
    public float finisherKnockbackScale = 1.5f;
    [Tooltip("輪到出手卻超過這麼久還砍不到（被擋住、追不上），就換下一隻 (秒)")]
    public float approachTimeout = 2f;
    [Tooltip("一輪結束休息時，有一隻吼一聲挑釁的機率")]
    [Range(0f, 1f)] public float tauntChance = 0.6f;

    EnemyAI ai;
    EnemyCombat combat;
    EnemyHealth health;
    ThirdPersonController controller;

    float slotAngle;          // 在圈上的位置（繞玩家的角度，度）
    Vector3 slotPos;          // 實際要站的位置（已避開懸空、牆後面）
    bool hasSlot;
    float tauntAt = float.PositiveInfinity, tauntEnd = float.NegativeInfinity; // 挑釁：什麼時候吼、站到什麼時候
    bool tauntPlayed;
    bool settled;             // 已經站到位置（離開 0.75m 以上才再走，站著時不會一直微調抖動）
    bool lockedRotation;      // 繞圈時是我把 lockRotation 打開的（離開圍攻要還原）
    int lastRingFrame = -1;
    float swingStartedAt = float.NegativeInfinity;
    bool swinging;            // 已經開始這一刀了

    // ---------------- 整個小隊共用（場景裡只有一個玩家） ----------------
    static readonly List<EnemySquadTactics> All = new List<EnemySquadTactics>();
    static readonly List<EnemySquadTactics> Active = new List<EnemySquadTactics>();
    static int directedFrame = -1;
    static EnemySquadTactics attacker, onDeck, lastAttacker;
    static float attackerSince, earliestSwing, restUntil, nextSlotUpdate;
    static int chainCount, chainLength;
    static Transform squadTarget;

    /// <summary>正在圍攻的數量（2 以上才是圍攻模式）</summary>
    public static int SquadSize => Active.Count;
    public bool IsAttacker => attacker == this;

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        combat = GetComponent<EnemyCombat>();
        health = GetComponent<EnemyHealth>();
        controller = GetComponent<ThirdPersonController>();
    }

    void OnEnable() => All.Add(this);

    void OnDisable()
    {
        All.Remove(this);
        Active.Remove(this);
        if (attacker == this) attacker = null;
        if (onDeck == this) onDeck = null;
        if (lastAttacker == this) lastAttacker = null;
        if (lockedRotation && controller != null) { controller.lockRotation = false; lockedRotation = false; }
    }

    void Update()
    {
        Direct();
        // 這一幀沒有在繞圈（輪到出手、被打、離開圍攻）：把轉向還給移動系統
        if (lockedRotation && lastRingFrame != Time.frameCount && (combat == null || !combat.IsAttacking))
        {
            controller.lockRotation = false;
            lockedRotation = false;
        }
    }

    // =====================================================================
    //  指揮：誰在圍攻、誰站哪裡、輪到誰砍
    // =====================================================================

    static void Direct()
    {
        if (directedFrame == Time.frameCount) return;
        directedFrame = Time.frameCount;

        // 1. 誰在圍攻：正在追同一個目標、而且離得夠近
        Active.Clear();
        squadTarget = null;
        foreach (var m in All)
        {
            if (!m.Eligible()) continue;
            if (squadTarget == null) squadTarget = m.ai.Target;
            if (m.ai.Target == squadTarget) Active.Add(m);
        }
        if (Active.Count < 2)
        {
            attacker = onDeck = null;
            chainCount = chainLength = 0;
            return;
        }

        Vector3 center = squadTarget.position;
        // 2. 依照繞著玩家的角度排序（順時針），平均分配圈上的位置；保留大家現在的大概方位，不會整群繞圈跑
        Active.Sort((a, b) => a.AngleAround(center).CompareTo(b.AngleAround(center)));
        if (Time.time >= nextSlotUpdate)
        {
            nextSlotUpdate = Time.time + 0.3f;
            float step = 360f / Active.Count;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < Active.Count; i++)
            {
                float a = (Active[i].AngleAround(center) - i * step) * Mathf.Deg2Rad;
                sum += new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            float baseAngle = Mathf.Atan2(sum.y, sum.x) * Mathf.Rad2Deg;
            for (int i = 0; i < Active.Count; i++) Active[i].slotAngle = baseAngle + i * step;
            AssignValidSlots(center);
        }

        // 3. 接力：上一隻砍下去的那一刻就把棒子交給下一隻
        if (attacker != null && !Active.Contains(attacker)) attacker = null;
        if (onDeck != null && !Active.Contains(onDeck)) onDeck = null;
        if (attacker != null)
        {
            bool struck = attacker.swinging && Time.time >= attacker.swingStartedAt + attacker.StrikeDelay;
            bool interrupted = attacker.swinging && !attacker.combat.IsAttacking && !struck; // 被打斷
            bool timedOut = !attacker.swinging && Time.time - attackerSince > attacker.approachTimeout;
            if (struck)
            {
                chainCount++;
                earliestSwing = attacker.swingStartedAt + attacker.StrikeDelay + attacker.chainGap;
                lastAttacker = attacker;
                attacker.swinging = false;
                attacker = null;
            }
            else if (interrupted || timedOut)
            {
                lastAttacker = attacker;
                attacker.swinging = false;
                attacker = null;
                earliestSwing = Time.time + 0.2f;
            }
        }

        var leader = Active[0];
        if (attacker == null)
        {
            if (chainLength > 0 && chainCount >= chainLength)
            {
                // 一輪結束：休息
                restUntil = Time.time + Random.Range(leader.roundRest.x, leader.roundRest.y);
                chainCount = chainLength = 0;
                onDeck = null;
                // 休息的空檔：隨便一隻（不是剛砍完的那隻）吼一聲挑釁
                if (Random.value < leader.tauntChance)
                {
                    var t = Active[Random.Range(0, Active.Count)];
                    if (t != lastAttacker || Active.Count == 1)
                    {
                        t.tauntAt = Time.time + 0.3f;   // 先停下來再吼
                        t.tauntEnd = t.tauntAt + 1.1f;
                        t.tauntPlayed = false;
                    }
                }
            }
            else if (Time.time >= restUntil && TargetAttackable())
            {
                if (chainLength == 0)
                {
                    chainLength = Mathf.Min(leader.maxChain, Active.Count + 1);
                    chainCount = 0;
                    earliestSwing = Time.time;
                }
                attacker = onDeck != null && onDeck != lastAttacker ? onDeck : NextAfter(lastAttacker, center);
                attackerSince = Time.time;
                attacker.swinging = false;
                onDeck = NextAfter(attacker, center);
                if (onDeck == attacker) onDeck = null;
            }
        }
    }

    // ---------------- 站位：避開懸空 / 牆後 ----------------

    static readonly List<Vector3> taken = new List<Vector3>();
    static readonly RaycastHit[] probeHits = new RaycastHit[12];
    static readonly Collider[] probeCols = new Collider[12];
    static readonly float[] AngleOffsets = { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };

    /// <summary>
    /// 每隻從自己理想的角度開始，往兩側找第一個「站得住、看得到玩家、沒被其他同伴佔走」的位置；
    /// 圈上都不行就往外一圈找（窄橋上會排成一列）。
    /// </summary>
    static void AssignValidSlots(Vector3 center)
    {
        taken.Clear();
        var ph = squadTarget.GetComponent<PlayerHealth>();
        float feetY = ph != null ? ph.FeetY : center.y - 1f;
        foreach (var m in Active)
        {
            bool found = false;
            for (int ring = 0; ring < 4 && !found; ring++)
            {
                float radius = m.ringRadius + ring * 1.8f;
                foreach (float off in AngleOffsets)
                {
                    float rad = (m.slotAngle + off) * Mathf.Deg2Rad;
                    Vector3 p = center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius;
                    if (!SlotFree(p) || !SlotValid(p, center, feetY, out Vector3 ground)) continue;
                    m.slotPos = ground;
                    m.slotAngle += off;
                    m.hasSlot = true;
                    taken.Add(ground);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                // 都找不到：原地等（不要硬擠）
                m.slotPos = m.transform.position;
                m.hasSlot = true;
                taken.Add(m.slotPos);
            }
        }
    }

    static bool SlotFree(Vector3 p)
    {
        foreach (var t in taken)
            if ((Flat(t) - Flat(p)).sqrMagnitude < 1.5f * 1.5f) return false;
        return true;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>腳下有地面、身體的空間沒有障礙、從玩家那邊看得到（中間沒有牆）</summary>
    static bool SlotValid(Vector3 p, Vector3 center, float feetY, out Vector3 ground)
    {
        ground = p;
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, feetY + 1.4f, p.z), Vector3.down, probeHits, 3.2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var h = probeHits[i];
            if (h.collider is CharacterController || h.normal.y < 0.6f) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (float.IsNegativeInfinity(best) || Mathf.Abs(best - feetY) > 1.3f) return false;
        ground = new Vector3(p.x, best, p.z);

        int c = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * 0.7f, ground + Vector3.up * 1.6f, 0.45f, probeCols, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < c; i++)
            if (!(probeCols[i] is CharacterController)) return false;

        Vector3 a = new Vector3(center.x, feetY + 1f, center.z), b = ground + Vector3.up * 1f;
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len > 0.01f)
        {
            n = Physics.RaycastNonAlloc(a, d / len, probeHits, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!(probeHits[i].collider is CharacterController)) return false;
        }
        return true;
    }

    /// <summary>圈上順時針的下一隻；previous 不在名單裡就挑離玩家最近的那隻</summary>
    static EnemySquadTactics NextAfter(EnemySquadTactics previous, Vector3 center)
    {
        int i = previous != null ? Active.IndexOf(previous) : -1;
        if (i >= 0) return Active[(i + 1) % Active.Count];
        EnemySquadTactics best = Active[0];
        float bestD = float.PositiveInfinity;
        foreach (var m in Active)
        {
            float d = FlatDistance(m.transform.position, center);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    /// <summary>玩家被打飛躺在地上、或已經死掉：不要追打</summary>
    static bool TargetAttackable()
    {
        if (squadTarget == null) return false;
        var ph = squadTarget.GetComponent<PlayerHealth>();
        return ph == null || !ph.IsDead && !ph.IsKnockedDown;
    }

    bool Eligible()
    {
        if (!isActiveAndEnabled || ai == null || health == null || health.IsDead) return false;
        if (ai.CurrentState != EnemyAI.State.Chase || ai.Target == null) return false;
        return FlatDistance(transform.position, ai.Target.position) <= engageRadius;
    }

    float AngleAround(Vector3 center)
    {
        Vector3 d = transform.position - center;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    /// <summary>從開始這一刀到真的砍下去要多久</summary>
    float StrikeDelay => combat.WindupFor(0) + combat.SwingFor(0) * 0.5f;

    // =====================================================================
    //  每隻自己的動作（EnemyAI.UpdateChase 呼叫；回傳 false = 不在圍攻，照原本的方式打）
    // =====================================================================

    public bool Drive(Vector3 toTarget, float dist)
    {
        Direct();
        if (Active.Count < 2 || !Active.Contains(this)) return false;

        Vector3 toN = dist > 0.001f ? toTarget / dist : transform.forward;
        if (attacker == this) return DriveAttacker(toTarget, toN, dist);

        // 等待出手：走到圈上自己的位置，一直面向玩家（橫著走、退著走）
        Vector3 slot = hasSlot ? slotPos : transform.position;
        if (onDeck == this)
        {
            // 下一個出手的站近一點（沿著自己站位的方向往玩家靠），那個位置不行就站原本的位置
            Vector3 dirOut = Flat(slot - squadTarget.position);
            if (dirOut.sqrMagnitude > 0.01f)
            {
                Vector3 near = squadTarget.position + dirOut.normalized * onDeckRadius;
                var ph = squadTarget.GetComponent<PlayerHealth>();
                if (SlotValid(near, squadTarget.position, ph != null ? ph.FeetY : squadTarget.position.y - 1f, out Vector3 g)) slot = g;
            }
        }
        Vector3 diff = slot - transform.position;
        diff.y = 0f;
        float off = diff.magnitude;

        lastRingFrame = Time.frameCount;
        bool far = off > 3.5f;
        // 遠的時候正常轉身跑過去；靠近站位之後鎖住轉向、面向玩家橫移
        if (!far && !combat.IsAttacking)
        {
            if (!controller.lockRotation) { controller.lockRotation = true; lockedRotation = true; }
            ai.TurnTowards(toTarget, 300f);
        }
        else if (lockedRotation)
        {
            controller.lockRotation = false;
            lockedRotation = false;
        }

        bool taunting = Time.time < tauntEnd;
        settled = settled ? off < 0.75f : off < 0.3f;
        if (!settled && !taunting)
        {
            Vector3 dir = diff / off;
            // 太靠近玩家就先往外退一點，不要從玩家身上穿過去
            if (dist < ringRadius * 0.6f) dir += -toN * 0.8f;
            dir += ai.Separation();
            float speed = far ? 1f : Mathf.Lerp(circleSpeed * 0.6f, circleSpeed, Mathf.Clamp01(off / 2f)); // 快到位置時放慢，不會衝過頭來回修正
            controller.SetAIInput(dir.normalized * speed, off > ai.sprintDistance);
        }
        else
        {
            controller.SetAIInput(Vector3.zero);
            if (taunting && !tauntPlayed && Time.time >= tauntAt)
            {
                tauntPlayed = true;
                var visual = GetComponent<EnemyVisual>();
                if (visual != null) visual.PlayTaunt(tauntEnd - Time.time);
            }
        }
        return true;
    }

    bool DriveAttacker(Vector3 toTarget, Vector3 toN, float dist)
    {
        if (lockedRotation) { controller.lockRotation = false; lockedRotation = false; }
        float range = ai.attackRange;
        if (dist > range * 0.85f)
        {
            Vector3 dir = toN + ai.Separation() * 0.5f;
            controller.SetAIInput(dir.normalized, dist > ai.sprintDistance);
        }
        else
        {
            controller.SetAIInput(Vector3.zero);
            ai.TurnTowards(toTarget, 540f);
        }

        // 預先抬手：讓「砍下去」剛好落在 earliestSwing（上一刀 + chainGap）
        bool timeOk = Time.time + StrikeDelay >= earliestSwing;
        if (!swinging && timeOk && dist <= range && Vector3.Angle(transform.forward, toTarget) < 60f && TargetAttackable())
        {
            bool finisher = chainLength > 0 && chainCount >= chainLength - 1;
            PlayerHitReaction reaction = finisher && finisherKnocksFlying ? PlayerHitReaction.KnockedFlying : chainReaction;
            if (combat.StartCombo(1, reaction, finisher ? finisherKnockbackScale : 0.6f))
            {
                swinging = true;
                swingStartedAt = Time.time;
            }
        }
        return true;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || squadTarget == null || !Active.Contains(this)) return;
        Vector3 slot = hasSlot ? slotPos : transform.position;
        Gizmos.color = attacker == this ? Color.red : onDeck == this ? Color.yellow : Color.cyan;
        Gizmos.DrawWireSphere(slot, 0.35f);
        Gizmos.DrawLine(transform.position, slot);
    }
}
