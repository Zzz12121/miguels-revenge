using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 連擊技「滑剷繞背斬」：快速按 翻滾 → 攻擊 → 翻滾（PS4：× □ ×；鍵盤：V 左鍵 V）
///   1. 滑剷：身體壓低後仰、盾牌舉在身前，往最近的敵人滑過去（地上揚起塵土、手把持續低震）
///   2. 繞背：貼著敵人身邊畫半圈繞到背後，邊起身邊面向敵人（殘影）
///   3. 背刺：從背後砍一刀（傷害 × backstabMultiplier），打擊回饋跟普攻一樣（頓幀、震動、音效）
/// 整段無敵；附近沒有敵人時不會發動（第三下照常翻滾）。
/// 屬於一般招式：不需要、也不消耗技能條（SkillGauge）；背刺打中跟普攻一樣會累積技能條，對強敵也沒有技能傷害上限。
///
/// 不用在場景裡放東西：跟 PlayerLoadout 一樣，每個場景自動掛到玩家身上。
/// 動作：沒有現成的滑剷動畫，用「舉盾 BlockIdle + 程序化壓低後仰」組成；砍擊用 PhantomSlash（沒有就用 Attack_3 / Slash3）。
/// </summary>
[DefaultExecutionOrder(-300)]   // 要比 ThirdPersonController 早：第三下 × 在同一幀先被這裡接走，才不會又翻滾一次
public sealed class ShieldSlideStrike : MonoBehaviour
{
    [Header("連擊判定")]
    [Tooltip("每一下之間最多隔幾秒")]
    public float comboWindow = .65f;
    [Tooltip("冷卻時間（秒）：招式整段做完之後才開始算，冷卻中再按不會發動")]
    public float cooldown = 1.5f;

    [Header("找目標")]
    public float searchRadius = 9f;

    [Header("時間")]
    public float slideSeconds = .42f;
    public float orbitSeconds = .34f;
    public float strikeSeconds = .42f;

    [Header("滑剷姿勢")]
    [Tooltip("滑剷時模型壓低多少（公尺）")]
    public float slideDrop = .45f;
    [Tooltip("滑剷時上半身往後仰幾度")]
    public float slideLean = 22f;

    [Header("傷害")]
    public float baseDamage = 30f;
    public float backstabMultiplier = 2f;

    public bool Playing { get; private set; }
    /// <summary>冷卻剩幾秒（招式進行中 = 還沒開始算，回傳整段 cooldown）。給 MoveCooldownHud 畫圓形倒數</summary>
    public float CooldownRemaining => Playing ? cooldown : Mathf.Max(0f, nextUse - Time.unscaledTime);

    ThirdPersonController motor;
    PlayerCombat combat;
    PlayerHealth health;
    CharacterController body;
    PlayerVisualAnimator visualDriver;
    Animator animator;
    float nextUse;
    float groundOffset;   // 玩家的 transform 離地面多高（技能中貼地用）

    // 技能中暫時改掉、結束時要還原的東西
    Transform model;
    PlayerWeaponPresenter weapon;
    Vector3 modelPos;
    Quaternion modelRot;
    bool bodyWasEnabled, driverWasEnabled;

    // 連擊進度：0 = 還沒開始、1 = 按了翻滾、2 = 翻滾 → 攻擊
    int step;
    float stepAt;

    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => hooked = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => Attach(); }
        Attach();
    }

    static void Attach()
    {
        foreach (var c in FindObjectsByType<PlayerCombat>(FindObjectsInactive.Exclude))
            if (c.GetComponent<PlayerHealth>() != null && c.GetComponent<ShieldSlideStrike>() == null)
                c.gameObject.AddComponent<ShieldSlideStrike>();
    }

    void Awake()
    {
        motor = GetComponent<ThirdPersonController>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        body = GetComponent<CharacterController>();
        visualDriver = GetComponent<PlayerVisualAnimator>();
    }

    // ---------------- 連擊判定 ----------------

    void Update()
    {
        if (Playing) return;
        if (Time.timeScale <= 0f || GameUI.OverlayOpen || RangeInputSettings.InputBlocked) { step = 0; return; }
        if (step > 0 && Time.unscaledTime - stepAt > comboWindow) step = 0;

        bool roll = InputBindings.Pressed(GameAction.Roll);
        bool attack = InputBindings.Pressed(GameAction.Attack);
        if (roll && step == 2)
        {
            step = 0;
            // 一般招式：不需要技能條
            if (FindTarget(out var target, out var damageable) && CanStart()) StartCoroutine(Play(target, damageable));
            return;
        }
        if (attack && step == 1) { step = 2; stepAt = Time.unscaledTime; return; }
        if (roll) { step = 1; stepAt = Time.unscaledTime; }
    }

    bool CanStart()
    {
        if (Time.unscaledTime < nextUse || motor == null || combat == null || health == null || health.IsDead) return false;
        if (!motor.inputEnabled || motor.IsStunned || combat.bowMode || SkillAimCancellation.IsAiming) return false;
        foreach (var skill in FindObjectsByType<VoidRendSkillDemo>(FindObjectsInactive.Exclude))
            if (skill.IsPlaying) return false;
        return true;
    }

    /// <summary>附近活著的敵人：越近、越在鏡頭（或角色）前方的越優先</summary>
    bool FindTarget(out Transform target, out IDamageable damageable)
    {
        target = null; damageable = null;
        Vector3 me = transform.position;
        Vector3 look = motor.cameraTransform != null ? motor.cameraTransform.forward : transform.forward;
        look = Vector3.ProjectOnPlane(look, Vector3.up).normalized;
        float best = float.MaxValue;
        foreach (var c in Physics.OverlapSphere(me + Vector3.up, searchRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
            var d = c.GetComponentInParent<IDamageable>();
            if (d == null || d.IsDead || !(d is Component owner)) continue;
            if (!CalligraphyWorldIsolation.Allows(owner.transform)) continue;
            Vector3 to = Vector3.ProjectOnPlane(owner.transform.position - me, Vector3.up);
            if (Mathf.Abs(owner.transform.position.y - me.y) > 3f) continue;   // 太高 / 太低的不追
            float angle = to.sqrMagnitude > .01f ? Vector3.Angle(look, to) : 0f;
            float score = to.magnitude * (1f + angle / 90f);
            if (score < best) { best = score; target = owner.transform; damageable = d; }
        }
        return target != null;
    }

    // ---------------- 技能本體 ----------------

    IEnumerator Play(Transform target, IDamageable damageable)
    {
        Playing = true;
        nextUse = Time.unscaledTime + cooldown;

        // 接手：取消翻滾 / 攻擊、鎖住其他動作、整段無敵、盾牌舉起來
        motor.LockActions();
        motor.ResetMotion();
        combat.CancelTransientActions();
        combat.SkillShield = true;
        health.SetSkillInvulnerability(this, true);
        bodyWasEnabled = body != null && body.enabled;
        groundOffset = GroundY(transform.position, out float g) ? transform.position.y - g
                     : body != null ? transform.position.y - body.bounds.min.y : 1f;   // 找不到地面：用身體膠囊的腳底
        if (body != null) body.enabled = false;
        if (visualDriver == null) visualDriver = GetComponent<PlayerVisualAnimator>();
        driverWasEnabled = visualDriver != null && visualDriver.enabled;
        if (visualDriver != null) visualDriver.enabled = false;
        animator = visualDriver != null ? visualDriver.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
        model = animator != null ? animator.transform : null;
        modelPos = model != null ? model.localPosition : Vector3.zero;
        modelRot = model != null ? model.localRotation : Quaternion.identity;
        var skins = GetComponentsInChildren<SkinnedMeshRenderer>();
        // 平常不打架時刀是收起來的：技能期間拔出來
        weapon = GetComponentInChildren<PlayerWeaponPresenter>(true);
        if (weapon != null) weapon.BeginSkillDraw();

        try
        {
            Vector3 enemyPos = Flat(target.position, transform.position.y);
            float enemyRadius = Radius(target);
            Vector3 start = transform.position;
            Vector3 toEnemy = enemyPos - start; toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude < .01f) toEnemy = transform.forward;
            Vector3 dir = toEnemy.normalized;
            // 從鏡頭比較靠近的那一側繞（右手邊 = +1）
            float side = motor.cameraTransform != null && Vector3.Dot(motor.cameraTransform.right, dir) < 0f ? -1f : 1f;
            Vector3 sideVec = Vector3.Cross(Vector3.up, dir) * side;
            float ringRadius = enemyRadius + .85f;
            // 滑到敵人斜前方（偏向要繞過去的那一側）
            Vector3 slideEnd = enemyPos - dir * ringRadius * .55f + sideVec * ringRadius * .8f;

            // ── 1. 滑剷 ──
            Play("BlockIdle", .06f);
            FaceTo(enemyPos);
            float nextDust = 0f;
            for (float t = 0f; t < slideSeconds; t += Time.deltaTime)
            {
                if (TargetGone(target, damageable)) yield break;
                float u = Mathf.Clamp01(t / slideSeconds);
                float e = 1f - (1f - u) * (1f - u) * (1f - u);   // 一開始爆衝，後面減速
                MoveTo(Vector3.Lerp(start, slideEnd, e));
                FaceTo(enemyPos);
                float pose = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / .2f));   // 一下就壓低
                SetModelPose(model, modelPos, modelRot, pose);
                if (t >= nextDust)
                {
                    nextDust = t + .045f;
                    CombatVFX.Puff(transform.position + Vector3.up * .08f, -dir, new Color(.78f, .72f, .62f, .55f));
                }
                PadRumble.Pulse(.35f, .15f, .08f);
                yield return null;
            }

            // ── 2. 繞到背後 ──
            Vector3 enemyBack = -Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            if (enemyBack.sqrMagnitude < .01f) enemyBack = dir;
            Vector3 fromVec = (Flat(transform.position, enemyPos.y) - enemyPos); fromVec.y = 0f;
            float a0 = Mathf.Atan2(fromVec.x, fromVec.z);
            float a1 = Mathf.Atan2(enemyBack.x, enemyBack.z);
            // 照選好的那一側繞（角度往同一個方向走，最多繞 300 度）
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            if (Mathf.Sign(delta) != -side && Mathf.Abs(delta) > .2f) delta += -side * Mathf.PI * 2f;
            delta = Mathf.Clamp(delta, -Mathf.PI * 1.67f, Mathf.PI * 1.67f);
            float r0 = fromVec.magnitude;
            float nextGhost = 0f;
            for (float t = 0f; t < orbitSeconds; t += Time.deltaTime)
            {
                if (TargetGone(target, damageable)) yield break;
                float u = Mathf.Clamp01(t / orbitSeconds);
                float e = Mathf.SmoothStep(0f, 1f, u);
                enemyPos = Flat(target.position, transform.position.y);   // 敵人在動就跟著
                float a = a0 + delta * e;
                float r = Mathf.Lerp(r0, ringRadius, e);
                MoveTo(enemyPos + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r);
                FaceTo(enemyPos);
                SetModelPose(model, modelPos, modelRot, 1f - e);   // 邊繞邊起身
                if (t >= nextGhost && skins.Length > 0)
                {
                    nextGhost = t + .06f;
                    AfterimageGhost.Spawn(skins, new Color(.75f, .9f, 1f, .45f), .25f);
                }
                yield return null;
            }
            SetModelPose(model, modelPos, modelRot, 0f);
            combat.SkillShield = false;

            // ── 3. 背刺 ──
            enemyPos = Flat(target.position, transform.position.y);
            FaceTo(enemyPos);
            Play(animator != null && animator.HasState(0, Animator.StringToHash("PhantomSlash")) ? "PhantomSlash"
                 : animator != null && animator.HasState(0, Animator.StringToHash("Attack_3")) ? "Attack_3" : "Slash3", .05f);
            bool hit = false;
            for (float t = 0f; t < strikeSeconds; t += Time.deltaTime)
            {
                if (!hit && t >= strikeSeconds * .3f)
                {
                    hit = true;
                    if (!TargetGone(target, damageable))
                    {
                        Vector3 point = target.position + Vector3.up * 1.1f;
                        // 一般招式：沒有強敵的技能傷害上限；打中算普攻，會累積技能條
                        float dealt = baseDamage * backstabMultiplier;
                        damageable.TakeDamage(dealt, point);
                        combat.ReportSkillHit(dealt, damageable, point, true, countsAsSkill: false);   // 頓幀、震動、音效、戰鬥紀錄跟普攻一樣
                        PadRumble.Pulse(.9f, .7f, .25f);
                        CombatVFX.Slash(transform.position + Vector3.up * 1.1f, transform.forward, new Color(.85f, .95f, 1f));
                        CombatVFX.Impact(point, new Color(1f, .75f, .35f));
                        ThirdPersonCamera.ShakeMain(.22f, .18f);
                        GameAudio.Play("SFX/RPG/knifeSlice", point, 1f, .8f, .05f, 1f, 35f);
                    }
                }
                yield return null;
            }
        }
        finally { Cleanup(); }
    }

    /// <summary>還原技能接手的所有東西（正常結束、目標中途死掉、元件被關掉都會走這裡；重複呼叫沒關係）</summary>
    void Cleanup()
    {
        if (!Playing) return;
        Playing = false;
        nextUse = Time.unscaledTime + cooldown;   // 冷卻從招式結束才開始算
        if (model != null) { model.localPosition = modelPos; model.localRotation = modelRot; }
        if (combat != null) combat.SkillShield = false;
        if (health != null) health.SetSkillInvulnerability(this, false);
        if (weapon != null) weapon.EndSkillDraw(false);   // 慢慢收刀（不要一落地刀就瞬間消失）
        if (body != null) body.enabled = bodyWasEnabled || body.enabled;
        if (visualDriver != null) visualDriver.enabled = driverWasEnabled;
        if (animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime("Idle", .15f, 0, 0f);
        if (motor != null) { motor.ResetMotion(); motor.UnlockActions(); }
    }

    void OnDisable()
    {
        // 協程被停掉時 finally 不一定會跑：這裡自己還原
        StopAllCoroutines();
        Cleanup();
    }

    // ---------------- 小工具 ----------------

    void Play(string state, float fade)
    {
        if (animator == null) return;
        int hash = Animator.StringToHash(state);
        if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
    }

    /// <summary>滑剷姿勢：模型壓低、整個往後仰（amount 0 = 站直、1 = 完全滑剷）</summary>
    void SetModelPose(Transform model, Vector3 basePos, Quaternion baseRot, float amount)
    {
        if (model == null) return;
        model.localPosition = basePos + Vector3.down * slideDrop * amount;
        // 以腰部（大約 0.9 公尺高）為中心往後仰，看起來是腳在前、身體在後
        Quaternion lean = Quaternion.AngleAxis(-slideLean * amount, Vector3.right);
        model.localRotation = baseRot * lean;
        model.localPosition += (Vector3.up * .9f - lean * (Vector3.up * .9f)) * amount;
    }

    void MoveTo(Vector3 position)
    {
        // 擋到牆就停在牆前面（不穿牆）
        Vector3 from = transform.position;
        Vector3 step = position - from; step.y = 0f;
        float len = step.magnitude;
        if (len > .001f && Physics.SphereCast(from + Vector3.up * .9f, .35f, step / len, out var hit, len, ~0, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<IDamageable>() == null)
            position = from + step / len * Mathf.Max(0f, hit.distance - .05f);
        // 貼地（保持原本離地的高度）：最多往上 0.45 公尺（不會滑上掩體、石頭）、往下 1.2 公尺；超過就維持原本的高度
        // 地面要從「現在」的高度往下找：用招式起點的高度找的話，爬坡超過 1.5 公尺射線就從坡裡面出發、找不到地面，人會鑽進山坡
        position.y = from.y;
        if (GroundY(position, out float g))
        {
            float dy = g + groundOffset - from.y;
            if (dy > .45f) { position.x = from.x; position.z = from.z; }   // 坡太陡 / 太高：當成牆，停在原地（不鑽進地形）
            else if (dy > -1.2f) position.y = g + groundOffset;
        }
        transform.position = position;
    }

    bool GroundY(Vector3 at, out float y)
    {
        y = 0f;
        var hits = Physics.RaycastAll(at + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(transform) || h.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        return best < float.MaxValue;
    }

    void FaceTo(Vector3 point)
    {
        Vector3 d = point - transform.position; d.y = 0f;
        if (d.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(d.normalized);
    }

    static bool TargetGone(Transform target, IDamageable d) => target == null || d == null || !(d is Component c) || c == null || d.IsDead;

    static Vector3 Flat(Vector3 p, float y) { p.y = y; return p; }

    static float Radius(Transform target)
    {
        bool has = false; var b = new Bounds(target.position, Vector3.zero);
        foreach (var c in target.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
        }
        return has ? Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z), .3f, 2f) : .5f;
    }
}
