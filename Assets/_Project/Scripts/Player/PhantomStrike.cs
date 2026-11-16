using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 幻影分身突進（跳起來 → 翻滾鍵側閃 → 側閃中按左鍵）：
///   1. 自動鎖定前方最近的敵人 E。本尊沿弧線衝到 E 的一側、分身（鏡像）同時衝到另一側，兩邊同時橫砍一刀。
///   2. 砍完之後分身留在原地當誘餌（PhantomDecoy），吸引附近小怪的仇恨，被打一下或時間到就消散。
///   3. 分身還在的時候再按左鍵 = 剪刀斬：本尊和分身同時朝敵人衝過去、交叉斬過，然後分身消散。
/// 分身用鏡像畫出來，所以本尊的動畫就是分身的動畫。
/// 動畫（Art/PlayerCatWarrior/phantom_pose.py 在 Blender 手擺，Animator 的 PhantomDash / PhantomSlash / PhantomScissor）：
///   衝過去 = 上身前壓、左膝往前頂的突進姿勢；橫砍 = 左腳踏出去煞車、轉腰由右往左砍、收招站起；剪刀斬 = 壓低從左腰拔刀砍過去、殘心。
/// 節奏：短蓄勢 → 爆衝 → 衝到一半就開始揮刀、到位那一刻剛好砍中後急停（打中算重擊，頓幀比較久）。
/// 本尊和分身衝刺時都會留下殘影（本尊：PlayerVisualAnimator；分身：PhantomDecoy）。
/// PlayerCombat 會自動加上這個元件；冷卻之後做平衡時再加。
/// </summary>
[DisallowMultipleComponent]
public sealed class PhantomStrike : MonoBehaviour
{
    [Header("幻影分身突進（空中側閃時按左鍵）")]
    public bool phantomEnabled = true;
    [Tooltip("側閃結束後多久內 (秒) 按左鍵也算")]
    [Min(0f)] public float dashWindow = .25f;
    [Tooltip("自動鎖定敵人的範圍 (m)")]
    [Min(1f)] public float searchRadius = 12f;
    [Tooltip("自動鎖定的角度：鏡頭前方左右各幾度")]
    [Range(10f, 180f)] public float searchAngle = 75f;
    [Tooltip("本尊和分身衝到敵人兩側的角度（0 = 正前方、90 = 正側面）")]
    [Range(20f, 85f)] public float flankAngle = 65f;
    [Tooltip("衝到離敵人（表面）多遠停下來砍 (m)")]
    [Min(.3f)] public float strikeDistance = 1.3f;
    [Tooltip("突進的時間 (秒)")]
    [Min(.08f)] public float dashTime = .28f;
    [Tooltip("衝出去之前的短蓄勢 (秒)：身體先壓成突進的姿勢再爆出去")]
    [Range(0f, .2f)] public float dashWindup = .06f;
    [Tooltip("到位之前多久 (秒) 就開始揮刀：到位那一刻剛好砍中（突進斬是抬手 0.06 秒 + 揮 0.21 秒，砍中在中間）")]
    [Range(0f, .3f)] public float strikeLead = .165f;
    [Tooltip("到位時還剩多少速度（0 = 慢慢停下來、1 = 全速衝到位然後急停）")]
    [Range(0f, 1f)] public float arriveSpeed = .7f;
    [Tooltip("突進最遠多遠 (m)；敵人更遠就衝到一半")]
    [Min(1f)] public float maxDashDistance = 14f;
    [Tooltip("弧線往外鼓多少 (m)")]
    [Min(0f)] public float arcBulge = 1.4f;
    [Tooltip("本尊這一刀的傷害 = 第一段傷害 × 這個倍率")]
    public float strikeDamageScale = 1.4f;
    [Tooltip("分身這一刀的傷害倍率")]
    public float cloneDamageScale = .6f;

    [Header("分身（誘餌）")]
    [Tooltip("分身留在場上多久 (秒)")]
    [Min(.2f)] public float decoyLifetime = 3f;
    [Tooltip("勾選：砍完之後分身繼續當本尊的鏡像（跟著走、跟著揮刀）；不勾：定格在原地")]
    public bool decoyMirrorsPlayer = true;
    [Tooltip("分身跟著本尊揮刀時的傷害倍率（× 第一段傷害）")]
    public float mirrorAttackDamageScale = .6f;
    [Tooltip("分身吸引小怪仇恨的範圍 (m)；Boss 不吃這招")]
    [Min(1f)] public float decoyLureRadius = 12f;
    public Color ghostColor = new Color(.5f, .72f, 1f, .5f);

    [Header("剪刀斬（分身還在時再按左鍵）")]
    [Min(.08f)] public float scissorDashTime = .24f;
    [Tooltip("衝幾秒到交叉點（= 砍中的那一刻；突進斬是抬手 0.06 秒 + 揮 0.21 秒，砍中在中間）。之後是滑行煞車")]
    [Range(.08f, .3f)] public float scissorStrikeAt = .165f;
    [Tooltip("交叉之後再往前衝多遠 (m)")]
    [Min(0f)] public float scissorOvershoot = 1.8f;
    public float scissorDamageScale = 2f;
    [Tooltip("分身在剪刀斬的傷害倍率")]
    public float scissorCloneDamageScale = 1.2f;

    /// <summary>正在突進 / 揮刀中（PlayerCombat 這段時間不接受其他攻擊）</summary>
    public bool Busy { get; private set; }

    ThirdPersonController controller;
    PlayerCombat combat;
    PlayerHealth health;
    CharacterController capsule;
    PhantomDecoy decoy;
    Transform lastTarget;
    readonly Collider[] overlap = new Collider[48];

    void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        capsule = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        if (combat != null) combat.OnStrike += MirrorStrike;
    }

    /// <summary>分身當鏡像時：本尊每一刀判定傷害的那一刻，分身在鏡射過去的位置也砍一下</summary>
    void MirrorStrike(Vector3 center, float radius)
    {
        if (Busy || decoy == null || !decoy.IsMirrorLure) return; // 突進 / 剪刀斬的那兩刀自己會算分身的傷害
        combat.StrikeArea(decoy.MirrorPoint(center), radius, BaseDamage() * mirrorAttackDamageScale, new HashSet<object>());
    }

    void OnDisable()
    {
        if (combat != null) combat.OnStrike -= MirrorStrike;
        StopAllCoroutines();
        if (Busy && controller != null) controller.EndScriptedMove(Vector3.zero);
        Busy = false;
        if (decoy != null) decoy.Dissolve();
    }

    // =====================================================================

    /// <summary>空中側閃中（或剛結束）按左鍵：開始分身突進。條件不符回傳 false（照一般攻擊處理）</summary>
    public bool TryStartFromDash()
    {
        if (!phantomEnabled || Busy || controller == null || combat == null || combat.bowMode) return false;
        bool inWindow = controller.IsAirDashing || (Time.time - controller.LastAirDashEndTime <= dashWindow && !controller.IsGrounded);
        if (!inWindow) return false;
        if (health != null && health.IsDead) return false;
        StartCoroutine(SplitRoutine(FindTarget(transform.position, CameraForward(), searchRadius, searchAngle)));
        return true;
    }

    /// <summary>分身還在（誘敵中）時按左鍵：剪刀斬</summary>
    public bool TryScissor()
    {
        if (!phantomEnabled || Busy || decoy == null || !decoy.IsLure || controller == null || combat == null || combat.bowMode) return false;
        if (health != null && health.IsDead) return false;
        Vector3 mid = (transform.position + decoy.transform.position) * .5f;
        Transform target = lastTarget != null && IsAlive(lastTarget) && Flat(lastTarget.position - mid).magnitude <= searchRadius
            ? lastTarget : FindTarget(mid, Vector3.zero, searchRadius, 180f);
        StartCoroutine(ScissorRoutine(target));
        return true;
    }

    // ---------------- 分身突進 ----------------

    IEnumerator SplitRoutine(Transform target)
    {
        Busy = true;
        combat.CancelTransientActions();
        controller.BeginScriptedMove();
        if (decoy != null) { decoy.Dissolve(); decoy = null; }
        lastTarget = target;

        Vector3 start = transform.position;
        Vector3 enemy = target != null ? TargetCenter(target) : start + CameraForward() * 6f;
        float enemyRadius = target != null ? TargetRadius(target) : 0f;
        Vector3 approach = Flat(enemy - start);
        approach = approach.sqrMagnitude > .01f ? approach.normalized : CameraForward();
        // 鏡射平面：通過敵人、沿著「從玩家到敵人」的方向（本尊在一側，分身在另一側）
        Vector3 normal = Vector3.Cross(Vector3.up, approach).normalized;
        float side = Vector3.Dot(start - enemy, normal) >= 0f ? 1f : -1f; // 本尊走自己原本比較靠近的那一側
        Vector3 offset = Quaternion.AngleAxis(-flankAngle * side, Vector3.up) * (-approach) * (enemyRadius + strikeDistance);
        if (Vector3.Dot(offset, normal) * side < 0f) offset = Quaternion.AngleAxis(flankAngle * side, Vector3.up) * (-approach) * (enemyRadius + strikeDistance);
        Vector3 end = enemy + offset;
        end.y = GroundRootHeight(end, enemy.y);
        Vector3 travel = end - start;
        if (travel.magnitude > maxDashDistance) end = start + travel.normalized * maxDashDistance;
        Vector3 control = (start + end) * .5f + normal * (side * arcBulge);

        health?.GrantDodgeInvulnerability(dashTime + .45f);
        decoy = PhantomDecoy.Create(transform, enemy, normal, ghostColor);
        GameAudio.Play("SFX/RPG/cloth3", transform.position, .8f, 1.7f, .05f, .3f);
        CombatVFX.Puff(transform.position, -approach, new Color(ghostColor.r, ghostColor.g, ghostColor.b, .7f), true);

        // 短蓄勢：原地先壓成突進的姿勢（PlayerVisualAnimator 看到 ScriptedMoveDirection 就會切過去）、面向敵人
        Vector3 firstDir = Flat(control - start);
        controller.ScriptedMoveDirection = firstDir.sqrMagnitude > .01f ? firstDir.normalized : approach;
        for (float w = 0f; w < dashWindup; w += Time.deltaTime)
        {
            controller.FaceDirection(approach);
            yield return null;
        }

        // 沿弧線爆衝過去：一開始最快、到位時還帶著速度（arriveSpeed），到位那一刻砍中、急停。
        // 衝到剩 strikeLead 秒時就開始揮刀，不是到位之後才砍。
        var cloneHits = new HashSet<object>();
        float cloneDamage = BaseDamage() * cloneDamageScale;
        bool slashed = false;
        float t = 0f;
        while (t < dashTime)
        {
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / dashTime);
            float u = Mathf.Lerp(1f - (1f - x) * (1f - x), x, arriveSpeed); // 混「一開始最快、最後停住」和「等速」→ 到位時還剩 arriveSpeed 的速度
            Vector3 want = Bezier(start, control, end, u);
            Vector3 delta = want - transform.position;
            controller.ScriptedMoveDirection = Flat(delta).normalized;
            controller.ScriptedMove(delta);
            Vector3 face = Flat(enemy - transform.position);
            if (face.sqrMagnitude > .01f) controller.FaceDirection(face);
            if (!slashed && dashTime - t <= strikeLead)
            {
                slashed = true;
                combat.BeginDashSlash(.3f, face, strikeDamageScale, 0);
            }
            if (slashed && decoy != null)
                combat.StrikeArea(decoy.MirrorPoint(transform.position + transform.forward * .9f), combat.hitRadius, cloneDamage, cloneHits);
            yield return null;
        }
        controller.ScriptedMoveDirection = Vector3.zero;

        // 到位：把刀砍完（兩邊同時）、收招
        if (!slashed) combat.BeginDashSlash(.3f, Flat(enemy - transform.position), strikeDamageScale, 0);
        while (combat.IsDashSlash)
        {
            if (decoy != null) combat.StrikeArea(decoy.MirrorPoint(transform.position + transform.forward * .9f), combat.hitRadius, cloneDamage, cloneHits);
            yield return null;
        }

        controller.EndScriptedMove(Vector3.zero);
        if (decoy != null)
        {
            if (decoyMirrorsPlayer) decoy.BecomeMirrorLure(decoyLifetime, decoyLureRadius);
            else decoy.BecomeLure(decoyLifetime, decoyLureRadius);
        }
        Busy = false;
    }

    // ---------------- 剪刀斬 ----------------

    IEnumerator ScissorRoutine(Transform target)
    {
        Busy = true;
        combat.CancelTransientActions();
        controller.BeginScriptedMove();
        lastTarget = target;

        Vector3 start = transform.position;
        Vector3 clone = decoy.transform.position;
        Vector3 across = Flat(clone - start);
        if (across.sqrMagnitude < .04f) across = transform.right;
        Vector3 normal = across.normalized;
        Vector3 mid = (start + clone) * .5f;
        // 交叉點：敵人投影到「本尊和分身的中垂面」上（兩邊會在這裡交叉）
        Vector3 cross = target != null ? TargetCenter(target) : mid + CameraForward() * 3f;
        cross -= normal * Vector3.Dot(cross - mid, normal);
        Vector3 dir = Flat(cross - start);
        dir = dir.sqrMagnitude > .01f ? dir.normalized : CameraForward();
        Vector3 end = cross + dir * scissorOvershoot;
        end.y = GroundRootHeight(end, start.y);

        decoy.SetMirror(mid, normal); // 分身 = 本尊對中垂面的鏡像 → 一起衝、在交叉點擦身而過
        health?.GrantDodgeInvulnerability(scissorDashTime + .4f);
        GameAudio.Play("SFX/RPG/cloth3", transform.position, .9f, 1.5f, .05f, .3f);

        // 拔刀斬的節奏：全速衝到交叉點（剛好是砍中的那一刻），砍過去之後帶著速度滑行、慢慢煞住（殘心）
        combat.BeginDashSlash(scissorDashTime + .06f, dir, scissorDamageScale, 1);
        var cloneHits = new HashSet<object>();
        float cloneDamage = BaseDamage() * scissorCloneDamageScale;
        float reach = Mathf.Max(.05f, scissorStrikeAt);                    // 幾秒衝到交叉點
        float toCross = Flat(cross - start).magnitude, after = Flat(end - cross).magnitude;
        // 滑行的時間：讓滑行一開始的速度 = 衝過來的速度（速度連續，不會頓一下）
        float skid = Mathf.Clamp(2f * after * reach / Mathf.Max(.1f, toCross), .12f, .32f);
        Vector3 crossAt = new Vector3(cross.x, Mathf.Lerp(start.y, end.y, toCross / Mathf.Max(.01f, toCross + after)), cross.z);
        bool shook = false;
        float t = 0f;
        while (t < reach + skid)
        {
            t += Time.deltaTime;
            Vector3 want;
            if (t < reach) want = Vector3.Lerp(start, crossAt, t / reach);
            else
            {
                float v = Mathf.Clamp01((t - reach) / skid);
                want = Vector3.Lerp(crossAt, end, 1f - (1f - v) * (1f - v));
                if (!shook) { shook = true; ThirdPersonCamera.ShakeMain(.16f, .15f); }
            }
            Vector3 delta = want - transform.position;
            controller.ScriptedMoveDirection = t < reach + skid * .6f ? dir : Vector3.zero;
            controller.ScriptedMove(delta);
            if (decoy != null) combat.StrikeArea(decoy.MirrorPoint(transform.position + dir * .6f), combat.hitRadius * 1.1f, cloneDamage, cloneHits);
            yield return null;
        }
        controller.ScriptedMoveDirection = Vector3.zero;
        while (combat.IsDashSlash) yield return null;

        if (decoy != null) { decoy.Dissolve(); decoy = null; }
        controller.EndScriptedMove(Vector3.zero); // 已經滑行煞住了，不再帶著速度衝出去
        Busy = false;
    }

    // ---------------- 工具 ----------------

    float BaseDamage() => combat.comboDamage != null && combat.comboDamage.Length > 0 ? combat.comboDamage[0] * combat.damageMultiplier : 10f;

    Vector3 CameraForward()
    {
        var cam = Camera.main;
        Vector3 f = cam != null ? Flat(cam.transform.forward) : Flat(transform.forward);
        return f.sqrMagnitude > .001f ? f.normalized : Vector3.forward;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u)
    {
        float v = 1f - u;
        return a * (v * v) + b * (2f * v * u) + c * (u * u);
    }

    /// <summary>前方 angle 度內、radius 以內最近的敵人（forward = 0 → 不限方向）</summary>
    Transform FindTarget(Vector3 from, Vector3 forward, float radius, float angle)
    {
        int n = Physics.OverlapSphereNonAlloc(from, radius, overlap, ~0, QueryTriggerInteraction.Collide);
        Transform best = null; float bestScore = float.MaxValue;
        float cos = Mathf.Cos(angle * Mathf.Deg2Rad);
        for (int i = 0; i < n; i++)
        {
            var c = overlap[i];
            if (c == null || c.transform.IsChildOf(transform) || c.GetComponentInParent<PhantomDecoy>() != null) continue;
            var dmg = c.GetComponentInParent<IDamageable>();
            if (dmg == null || dmg.IsDead || dmg is Knockable) continue;
            if (!CalligraphyWorldIsolation.Allows(c.transform)) continue;
            Transform t = ((Component)dmg).transform;
            Vector3 to = Flat(t.position - from);
            float dist = to.magnitude;
            if (forward.sqrMagnitude > .001f && dist > .5f && Vector3.Dot(to / dist, forward) < cos) continue;
            // 越近、越靠近正前方越優先
            float score = dist * (forward.sqrMagnitude > .001f && dist > .5f ? 1.5f - Vector3.Dot(to / dist, forward) * .5f : 1f);
            if (score < bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    static bool IsAlive(Transform t)
    {
        var d = t.GetComponentInParent<IDamageable>();
        return d != null && !d.IsDead;
    }

    static Vector3 TargetCenter(Transform t)
    {
        var col = t.GetComponentInChildren<Collider>();
        return col != null ? col.bounds.center : t.position;
    }

    static float TargetRadius(Transform t)
    {
        var col = t.GetComponentInChildren<Collider>();
        if (col == null) return .4f;
        var e = col.bounds.extents;
        return Mathf.Clamp(Mathf.Max(e.x, e.z), .3f, 3f);
    }

    /// <summary>at 正下方的地面上，玩家根節點（膠囊中心）應該在的高度；找不到地面就用 fallback</summary>
    float GroundRootHeight(Vector3 at, float fallback)
    {
        float rootOffset = capsule != null ? transform.position.y - capsule.bounds.min.y : 1f;
        var hits = Physics.RaycastAll(new Vector3(at.x, Mathf.Max(at.y, transform.position.y) + 4f, at.z), Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (h.normal.y < .5f) continue;
            if (h.point.y > best && h.point.y <= Mathf.Max(at.y, transform.position.y) + 1.5f) best = h.point.y;
        }
        return best > float.NegativeInfinity ? best + rootOffset + .02f : fallback;
    }
}
