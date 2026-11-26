using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 技能「跳躍劈擊」：按住衝刺鍵時，依序按 跳躍 → 攻擊（PS4：按住 R2，按 △ 再按 □；鍵盤：按住 Shift，Space 再左鍵）
///   1. 衝刺：往最近的敵人衝過去（殘影、塵土）
///   2. 起跳：拋物線跳到敵人頭上
///   3. 蓄力：在敵人頭頂停一下（世界稍微變慢）——腰腹往後伸展成弓形、劍往後拉到腦後、雙腳也往後收，
///            手上聚光、手把震動越來越強
///   4. 下劈：整個人往前收、雙手往下全力砍，直直砸到敵人面前的地上（傷害 baseDamage × cleaveMultiplier）
///   5. 落地：地面震波，旁邊的敵人也會被震到（shockDamage），蹲低緩衝再站起來
/// 整段無敵；附近沒有敵人時不會發動（照常跳 / 攻擊）。
/// 屬於一般招式：不需要、也不消耗技能條（SkillGauge）；打中跟普攻一樣會累積技能條，對強敵也沒有技能傷害上限。
///
/// 不用在場景裡放東西：每個場景自動掛到玩家身上（跟 ShieldSlideStrike 一樣）。
/// 動作：沒有現成的動畫。用「Jump / Fall 動畫當底」+ 程序化改骨頭（LeapCleavePose，在所有動畫之後套用），
///      直接把脊椎、手臂、腿轉到想要的方向，任何人形骨架都能用。
/// </summary>
[DefaultExecutionOrder(-300)]   // 要比 PlayerCombat 早：□ 在同一幀先被這裡接走，才不會又普攻一次
public sealed class LeapCleaveStrike : MonoBehaviour
{
    [Header("按鍵判定")]
    [Tooltip("按了跳躍之後，多久內按攻擊算數")]
    public float comboWindow = .5f;
    [Tooltip("冷卻時間（秒）：招式整段做完之後才開始算，冷卻中再按不會發動")]
    public float cooldown = 1.5f;

    [Header("找目標")]
    public float searchRadius = 11f;

    [Header("時間")]
    public float dashSeconds = .26f;
    public float leapSeconds = .36f;
    public float chargeSeconds = .38f;
    public float strikeSeconds = .13f;
    public float recoverSeconds = .32f;
    [Tooltip("蓄力時世界變慢多少（1 = 不變慢）")]
    [Range(.2f, 1f)] public float chargeTimeScale = .45f;

    [Header("位置")]
    [Tooltip("跳到敵人頭頂上方多高（從敵人頭頂算）")]
    public float apexAboveHead = 1.4f;
    [Tooltip("起跳點離敵人多遠")]
    public float takeoffDistance = 2.4f;

    [Header("傷害")]
    public float baseDamage = 40f;
    public float cleaveMultiplier = 2.5f;
    public float shockRadius = 3.2f;
    public float shockDamage = 25f;

    public bool Playing { get; private set; }
    /// <summary>冷卻剩幾秒（招式進行中 = 還沒開始算，回傳整段 cooldown）。給 MoveCooldownHud 畫圓形倒數</summary>
    public float CooldownRemaining => Playing ? cooldown : Mathf.Max(0f, nextUse - Time.unscaledTime);

    ThirdPersonController motor;
    PlayerCombat combat;
    PlayerHealth health;
    CharacterController body;
    PlayerVisualAnimator visualDriver;
    Animator animator;
    LeapCleavePose pose;
    float nextUse, groundOffset;

    // 判定：按住衝刺時按了跳躍的時間
    float jumpAt = float.NegativeInfinity;

    // 要還原的東西
    Transform model;
    PlayerWeaponPresenter weapon;
    Vector3 modelPos;
    bool bodyWasEnabled, driverWasEnabled, slowed;
    float savedTimeScale = 1f;

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
            if (c.GetComponent<PlayerHealth>() != null && c.GetComponent<LeapCleaveStrike>() == null)
                c.gameObject.AddComponent<LeapCleaveStrike>();
    }

    void Awake()
    {
        motor = GetComponent<ThirdPersonController>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        body = GetComponent<CharacterController>();
        visualDriver = GetComponent<PlayerVisualAnimator>();
    }

    // ---------------- 按鍵判定 ----------------

    void Update()
    {
        if (Playing) return;
        if (Time.timeScale <= 0f || GameUI.OverlayOpen || RangeInputSettings.InputBlocked) { jumpAt = float.NegativeInfinity; return; }
        bool sprintHeld = InputBindings.Held(GameAction.Sprint);
        if (sprintHeld && InputBindings.Pressed(GameAction.Jump)) jumpAt = Time.unscaledTime;
        if (sprintHeld && InputBindings.Pressed(GameAction.Attack) && Time.unscaledTime - jumpAt <= comboWindow)
        {
            jumpAt = float.NegativeInfinity;
            // 先看附近有沒有敵人（沒有的話就是普通的按鍵，不提示技能條沒滿），再看技能條
            // 一般招式：不需要技能條
            if (FindTarget(out var target, out var damageable) && CanStart()) StartCoroutine(Play(target, damageable));
        }
    }

    bool CanStart()
    {
        if (Time.unscaledTime < nextUse || motor == null || combat == null || health == null || health.IsDead) return false;
        if (!motor.inputEnabled || motor.IsStunned || combat.bowMode || SkillAimCancellation.IsAiming) return false;
        var slide = GetComponent<ShieldSlideStrike>();
        if (slide != null && slide.Playing) return false;
        foreach (var skill in FindObjectsByType<VoidRendSkillDemo>(FindObjectsInactive.Exclude))
            if (skill.IsPlaying) return false;
        return true;
    }

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
            if (Mathf.Abs(owner.transform.position.y - me.y) > 4f) continue;
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

        motor.LockActions();
        motor.ResetMotion();
        combat.CancelTransientActions();
        health.SetSkillInvulnerability(this, true);
        bodyWasEnabled = body != null && body.enabled;
        groundOffset = GroundY(transform.position, out float g0) && transform.position.y - g0 < 1.4f ? transform.position.y - g0 : 1.04f;
        if (body != null) body.enabled = false;
        if (visualDriver == null) visualDriver = GetComponent<PlayerVisualAnimator>();
        driverWasEnabled = visualDriver != null && visualDriver.enabled;
        if (visualDriver != null) visualDriver.enabled = false;
        animator = visualDriver != null ? visualDriver.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
        model = animator != null ? animator.transform : null;
        modelPos = model != null ? model.localPosition : Vector3.zero;
        if (animator != null && animator.isHuman)
        {
            pose = animator.GetComponent<LeapCleavePose>();
            if (pose == null) pose = animator.gameObject.AddComponent<LeapCleavePose>();
            pose.Init(animator, transform);
        }
        var skins = GetComponentsInChildren<SkinnedMeshRenderer>();
        // 平常不打架時刀是收起來的：技能期間拔出來
        weapon = GetComponentInChildren<PlayerWeaponPresenter>(true);
        if (weapon != null) weapon.BeginSkillDraw();
        Transform rightHand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : transform;

        try
        {
            Vector3 enemy = target.position;
            float enemyRadius = Radius(target, out float enemyTop);
            Vector3 dir = Vector3.ProjectOnPlane(enemy - transform.position, Vector3.up);
            if (dir.sqrMagnitude < .01f) dir = transform.forward;
            dir.Normalize();

            // ── 1. 衝刺（已經夠近就不用衝）──
            float ground = GroundY(transform.position, out float gy) ? gy : transform.position.y - groundOffset;
            Vector3 start = transform.position;
            Vector3 takeoff = enemy - dir * Mathf.Max(takeoffDistance, enemyRadius + 1.2f);
            float dashLen = Vector3.ProjectOnPlane(takeoff - start, Vector3.up).magnitude;
            bool grounded = transform.position.y - ground < groundOffset + .3f;
            if (grounded && dashLen > .6f && Vector3.Dot(takeoff - start, dir) > 0f)
            {
                Play("Run", .05f);
                float seconds = Mathf.Lerp(dashSeconds * .6f, dashSeconds, Mathf.Clamp01(dashLen / 8f));
                float nextFx = 0f;
                for (float t = 0f; t < seconds; t += Time.deltaTime)
                {
                    if (TargetGone(target, damageable)) yield break;
                    float u = Mathf.Clamp01(t / seconds);
                    MoveOnGround(Vector3.Lerp(start, takeoff, u));
                    FaceTo(target.position);
                    if (t >= nextFx)
                    {
                        nextFx = t + .05f;
                        CombatVFX.Puff(transform.position - Vector3.up * groundOffset + Vector3.up * .05f, -dir, new Color(.78f, .72f, .62f, .5f));
                        if (skins.Length > 0) AfterimageGhost.Spawn(skins, new Color(.75f, .9f, 1f, .4f), .22f);
                    }
                    yield return null;
                }
            }

            // ── 2. 起跳：拋物線到敵人頭上 ──
            Play("Jump", .05f);
            GameAudio.Play("SFX/Gen/jump_whoosh_1", transform.position, .9f, .9f, .05f, 1f, 25f);
            CombatVFX.Ring(transform.position - Vector3.up * groundOffset + Vector3.up * .05f, new Color(.85f, .9f, 1f), 1.2f);
            Vector3 leapFrom = transform.position;
            float apexY = Mathf.Max(enemyTop, target.position.y + 1.6f) + apexAboveHead + groundOffset;
            for (float t = 0f; t < leapSeconds; t += Time.deltaTime)
            {
                if (TargetGone(target, damageable)) yield break;
                float u = Mathf.Clamp01(t / leapSeconds);
                Vector3 apex = target.position - dir * .35f;   // 敵人頭上偏我這一側一點點，下劈時刀落在他身上
                Vector3 p = Vector3.Lerp(leapFrom, apex, 1f - (1f - u) * (1f - u));
                p.y = Mathf.Lerp(leapFrom.y, apexY, Mathf.Sin(u * Mathf.PI * .5f));
                transform.position = p;
                FaceTo(target.position);
                if (pose != null) { pose.arch = Mathf.SmoothStep(0f, .6f, Mathf.Clamp01((u - .35f) / .65f)); pose.strike = 0f; }
                yield return null;
            }

            // ── 3. 蓄力：停在頭頂、身體拉成弓形 ──
            if (chargeTimeScale < .999f && Time.timeScale > .99f) { savedTimeScale = CombatHitStop.CaptureTimeScale(); Time.timeScale = chargeTimeScale; slowed = true; }
            Play("Fall", .1f);
            Vector3 hover = transform.position;
            float nextSpark = 0f;
            for (float t = 0f; t < chargeSeconds; t += Time.unscaledDeltaTime)
            {
                if (TargetGone(target, damageable)) yield break;
                float u = Mathf.Clamp01(t / chargeSeconds);
                transform.position = hover + Vector3.up * (.18f * Mathf.Sin(u * Mathf.PI * .5f));
                FaceTo(target.position);
                if (pose != null) pose.arch = Mathf.Lerp(.6f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / .45f)));
                if (t >= nextSpark && rightHand != null)
                {
                    nextSpark = t + .05f;
                    CombatVFX.Puff(rightHand.position, Random.onUnitSphere, Color.Lerp(new Color(.6f, .85f, 1f, .6f), new Color(1f, .95f, .8f, .9f), u), true);
                }
                PadRumble.Pulse(.15f + .45f * u, .1f + .3f * u, .08f);
                yield return null;
            }
            if (slowed) { Time.timeScale = savedTimeScale; slowed = false; }

            // ── 4. 下劈：全力往下砍，砸到敵人面前 ──
            if (rightHand != null) CombatVFX.Trail(rightHand, new Color(.85f, .95f, 1f), .3f);
            GameAudio.Play("SFX/Gen/swing_whoosh_2", transform.position, 1f, .75f, .05f, 1f, 25f);
            Vector3 strikeFrom = transform.position;
            Vector3 landing = target.position - dir * (enemyRadius + .55f);
            landing.y = LandingGround(landing, target, ground) + groundOffset;
            bool hit = false;
            for (float t = 0f; t < strikeSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / strikeSeconds);
                float e = u * u;   // 越砍越快
                transform.position = Vector3.Lerp(strikeFrom, landing, e);
                if (pose != null) { pose.strike = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / .55f)); pose.arch = 1f - pose.strike; }
                if (!hit && u >= .45f) { hit = true; Cleave(target, damageable); }
                yield return null;
            }
            transform.position = landing;
            if (!hit) Cleave(target, damageable);

            // ── 5. 落地震波 ──
            Vector3 groundPoint = landing - Vector3.up * groundOffset;
            ShockwaveFx.Spawn(groundPoint + Vector3.up * .1f, shockRadius, .35f, .35f);
            CombatVFX.Ring(groundPoint + Vector3.up * .05f, new Color(.9f, .95f, 1f), shockRadius);
            for (int i = 0; i < 6; i++)
                CombatVFX.Puff(groundPoint + Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * .6f + Vector3.up * .05f,
                               Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward, new Color(.75f, .7f, .6f, .6f));
            ThirdPersonCamera.ShakeMain(.45f, .35f);
            PadRumble.Pulse(1f, .8f, .35f);
            GameAudio.Play("SFX/Gen/hit_heavy_1", groundPoint, 1f, .7f, .05f, 1f, 35f);
            ShockNearby(groundPoint, damageable);

            // 蹲低緩衝再站起來
            Play("Land", .05f);
            for (float t = 0f; t < recoverSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / recoverSeconds);
                if (pose != null) pose.strike = 1f - Mathf.SmoothStep(0f, 1f, u);
                if (model != null) model.localPosition = modelPos + Vector3.down * (.28f * (1f - u) * (1f - u));
                yield return null;
            }
        }
        finally { Cleanup(); }
    }

    void Cleave(Transform target, IDamageable damageable)
    {
        if (TargetGone(target, damageable)) return;
        Vector3 point = target.position + Vector3.up * 1.2f;
        // 一般招式：沒有強敵的技能傷害上限；打中算普攻，會累積技能條
        float dealt = baseDamage * cleaveMultiplier;
        damageable.TakeDamage(dealt, point);
        combat.ReportSkillHit(dealt, damageable, point, true, countsAsSkill: false);
        CombatVFX.Slash(point + Vector3.up * .6f, Vector3.down, new Color(.9f, .97f, 1f));
        CombatVFX.Impact(point, new Color(1f, .8f, .4f));
        GameAudio.Play("SFX/RPG/knifeSlice", point, 1f, .7f, .05f, 1f, 35f);
    }

    void ShockNearby(Vector3 center, IDamageable main)
    {
        var done = new System.Collections.Generic.HashSet<IDamageable> { main };
        foreach (var c in Physics.OverlapSphere(center + Vector3.up, shockRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
            var d = c.GetComponentInParent<IDamageable>();
            if (d == null || d.IsDead || !done.Add(d) || !(d is Component owner)) continue;
            Vector3 p = c.ClosestPoint(center + Vector3.up);
            d.TakeDamage(shockDamage, p);
            combat.ReportSkillHit(shockDamage, d, p, false, countsAsSkill: false);
        }
    }

    void Cleanup()
    {
        if (!Playing) return;
        Playing = false;
        nextUse = Time.unscaledTime + cooldown;   // 冷卻從招式結束才開始算
        if (slowed) { Time.timeScale = savedTimeScale; slowed = false; }
        if (pose != null) { pose.arch = 0f; pose.strike = 0f; }
        if (model != null) model.localPosition = modelPos;
        if (health != null) health.SetSkillInvulnerability(this, false);
        if (weapon != null) weapon.EndSkillDraw(false);   // 慢慢收刀（不要一落地刀就瞬間消失）
        // 落在原本的地面高度（中途被打斷、還在空中時：交給重力掉下來）
        if (body != null) body.enabled = bodyWasEnabled || body.enabled;
        if (visualDriver != null) visualDriver.enabled = driverWasEnabled;
        if (animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime("Idle", .15f, 0, 0f);
        if (motor != null) { motor.ResetMotion(); motor.UnlockActions(); }
    }

    void OnDisable() { StopAllCoroutines(); Cleanup(); }

    // ---------------- 小工具 ----------------

    void Play(string state, float fade)
    {
        if (animator == null) return;
        int hash = Animator.StringToHash(state);
        if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
    }

    void MoveOnGround(Vector3 position)
    {
        Vector3 from = transform.position;
        Vector3 step = position - from; step.y = 0f;
        float len = step.magnitude;
        if (len > .001f && Physics.SphereCast(from + Vector3.up * .3f, .35f, step / len, out var hit, len, ~0, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<IDamageable>() == null)
            position = from + step / len * Mathf.Max(0f, hit.distance - .05f);
        // 地面要從「現在」的高度往下找（用起跳前的高度找，爬坡時射線會從坡裡面出發、找不到地面，人會鑽進山坡）
        position.y = from.y;
        if (GroundY(position, out float g))
        {
            float dy = g + groundOffset - from.y;
            if (dy > .45f) { position.x = from.x; position.z = from.z; }   // 坡太陡 / 太高：當成牆，停在原地
            else if (dy > -1.2f) position.y = g + groundOffset;
        }
        transform.position = position;
    }

    /// <summary>
    /// 落地點的地面高度。要從敵人腳邊的高度往下找（不能從跳躍最高點往下找：會先打到敵人頭上的東西，落地後站在敵人頭上），
    /// 敵人自己的碰撞、角色的膠囊都不算地面；在地形上的話地面至少是地形高度（敵人站在坡上時也不會落進坡裡）。
    /// </summary>
    float LandingGround(Vector3 landing, Transform target, float fallback)
    {
        float feet = target.position.y;
        var cc = target.GetComponentInParent<CharacterController>();
        if (cc != null) feet = cc.bounds.min.y;
        float from = Mathf.Max(feet, fallback) + 1.5f;
        float y = float.NegativeInfinity;
        Transform root = target;   // 只排除這個敵人自己（不要用 .root：敵人可能都放在同一個群組底下）
        var hits = Physics.RaycastAll(new Vector3(landing.x, from, landing.z), Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(transform) || h.transform.IsChildOf(root) || h.collider is CharacterController) continue;
            if (h.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        if (SkeletonCentipedeBossAI.TerrainY(landing, out float terrain)) y = Mathf.Max(y, terrain);
        return float.IsNegativeInfinity(y) ? fallback : y;
    }

    bool GroundY(Vector3 at, out float y)
    {
        y = 0f;
        var hits = Physics.RaycastAll(at + Vector3.up * 1.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
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

    /// <summary>敵人的水平半徑，順便回傳頭頂高度</summary>
    static float Radius(Transform target, out float top)
    {
        bool has = false; var b = new Bounds(target.position, Vector3.zero);
        foreach (var c in target.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
        }
        top = has ? b.max.y : target.position.y + 1.8f;
        return has ? Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z), .3f, 2f) : .5f;
    }
}

/// <summary>
/// 跳躍劈擊的程序化姿勢（掛在角色的 Animator 上，所有動畫算完之後才套用）：
///   arch（0~1）蓄力：腰腹往後伸展成弓形、雙手把劍拉到腦後、大腿往後、小腿往上收
///   strike（0~1）下劈：上半身往前收、雙手往前下方砍、膝蓋往前提
/// 做法：每根骨頭「現在指的方向」轉到「想要的方向」（世界座標、以角色的前 / 右 / 上為基準），不管骨架怎麼綁都適用。
/// </summary>
[DefaultExecutionOrder(31500)]
public sealed class LeapCleavePose : MonoBehaviour
{
    [Range(0f, 1f)] public float arch;
    [Range(0f, 1f)] public float strike;

    Transform root;
    Transform spine, chest, upperChest, neck;
    Transform rUpperArm, rLowerArm, rHand, lUpperArm, lLowerArm, lHand;
    Transform rUpperLeg, rLowerLeg, rFoot, lUpperLeg, lLowerLeg, lFoot;
    PlayerWeaponPresenter weapon;

    public void Init(Animator a, Transform characterRoot)
    {
        root = characterRoot;
        spine = a.GetBoneTransform(HumanBodyBones.Spine);
        chest = a.GetBoneTransform(HumanBodyBones.Chest);
        upperChest = a.GetBoneTransform(HumanBodyBones.UpperChest);
        neck = a.GetBoneTransform(HumanBodyBones.Neck);
        rUpperArm = a.GetBoneTransform(HumanBodyBones.RightUpperArm); rLowerArm = a.GetBoneTransform(HumanBodyBones.RightLowerArm); rHand = a.GetBoneTransform(HumanBodyBones.RightHand);
        lUpperArm = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); lLowerArm = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); lHand = a.GetBoneTransform(HumanBodyBones.LeftHand);
        rUpperLeg = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); rLowerLeg = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); rFoot = a.GetBoneTransform(HumanBodyBones.RightFoot);
        lUpperLeg = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); lLowerLeg = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); lFoot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
        weapon = characterRoot.GetComponentInChildren<PlayerWeaponPresenter>(true);
    }

    void LateUpdate()
    {
        if (root == null || (arch <= .001f && strike <= .001f)) return;
        Vector3 F = root.forward, R = root.right, U = Vector3.up;

        // 脊椎：蓄力往後仰（每節 -15 度，三節加起來大約 45 度），下劈往前收（每節 +14 度）
        float bend = -15f * arch + 14f * strike;
        Bend(spine, R, bend); Bend(chest, R, bend); Bend(upperChest, R, bend * .7f); Bend(neck, R, -8f * arch + 6f * strike);

        // 手臂：蓄力 → 舉過頭、往後拉（雙手在腦後握劍）；下劈 → 往前下方砍到底
        Aim(rUpperArm, rLowerArm, U * .8f - F * .6f + R * .15f, F * .9f - U * .45f + R * .1f);
        Aim(rLowerArm, rHand, U * .15f - F * 1f - R * .2f, F * .8f - U * .6f);
        Aim(lUpperArm, lLowerArm, U * .8f - F * .55f - R * .15f, F * .9f - U * .45f - R * .1f);
        Aim(lLowerArm, lHand, U * .15f - F * 1f + R * .25f, F * .8f - U * .6f + R * .15f);

        // 腿：蓄力 → 大腿往後、小腿往上收（腳往後）；下劈 → 膝蓋往前提、小腿往下
        Aim(rUpperLeg, rLowerLeg, -U * .75f - F * .65f, F * .6f - U * .8f);
        Aim(rLowerLeg, rFoot, -F * .7f + U * .55f, -U);
        Aim(lUpperLeg, lLowerLeg, -U * .8f - F * .55f, F * .5f - U * .85f);
        Aim(lLowerLeg, lFoot, -F * .65f + U * .5f, -U);

        // 劍：蓄力 → 劍尖往下垂在背後（劍往後拉）；下劈 → 劍尖往前下方砍出去
        // PlayerWeaponPresenter（31000）先擺好握把，這裡（31500）再把整把劍轉到要的方向
        if (weapon != null && weapon.BladePivot != null && weapon.BladeTip != null)
        {
            Transform pivot = weapon.BladePivot;
            Vector3 now = weapon.BladeTip.position - pivot.position;
            if (now.sqrMagnitude > 1e-6f)
            {
                Vector3 want = Vector3.Slerp(now.normalized, (-F * .55f - U * .85f).normalized, arch);
                want = Vector3.Slerp(want, (F * .75f - U * .65f).normalized, strike);
                pivot.rotation = Quaternion.FromToRotation(now.normalized, want) * pivot.rotation;
            }
        }
    }

    void Bend(Transform bone, Vector3 axis, float degrees)
    {
        if (bone == null || Mathf.Abs(degrees) < .01f) return;
        bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    /// <summary>把 bone 轉到「指向 child 的方向」= 蓄力方向 / 下劈方向（依 arch / strike 混合）</summary>
    void Aim(Transform bone, Transform child, Vector3 archDir, Vector3 strikeDir)
    {
        if (bone == null || child == null) return;
        Vector3 now = child.position - bone.position;
        if (now.sqrMagnitude < 1e-6f) return;
        Vector3 want = Vector3.Slerp(now.normalized, archDir.normalized, arch);
        want = Vector3.Slerp(want, strikeDir.normalized, strike);
        bone.rotation = Quaternion.FromToRotation(now.normalized, want) * bone.rotation;
    }
}
