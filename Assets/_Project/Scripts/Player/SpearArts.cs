using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 長槍的兩個組合技（拿長槍時才有；跟大刀的「跳劈」「盾滑」一樣是一般招式：不需要、也不消耗技能條，有冷卻）：
///
///   破陣突刺：按住衝刺 + 攻擊（PS4：按住 R2 按 □；鍵盤：按住 Shift 按左鍵）
///     蹲低、槍往後收（蓄勢）→ 朝前方的敵人直線爆衝（最遠 9.5 m），槍放平往前推，
///     一路貫穿：路上每個敵人都被刺中一次（傷害 baseDamage × chargeMultiplier），衝過頭一點再煞住
///
///   飛身貫地：跳起來（或按住衝刺跳）之後按攻擊（PS4：△ 再按 □；鍵盤：Space 再左鍵）
///     飛到敵人頭上 → 雙手把槍舉到胸口、槍尖朝下（停一下蓄勢）→ 整個人往下把槍插進敵人
///     （傷害 baseDamage × plungeMultiplier）→ 地面震波，旁邊的敵人也被震到（shockDamage）
///     拿長槍時它取代大刀的「跳劈」（LeapCleaveStrike：同一組按鍵，拿長槍時先關掉）
///   一個武器兩招：拿長槍時大刀的「盾滑」（ShieldSlideStrike，翻滾 攻擊 翻滾）也關掉，換回大刀再打開
///
/// 整段無敵；附近（突刺：前方）沒有敵人時不會發動（照常攻擊）。空中側閃剛結束時按攻擊交給幻影分身突進。
/// 傷害會乘上鐵匠鋪升級的倍率（WeaponForge.DamageScale）。
/// 不用在場景裡放東西：每個場景自動掛到玩家身上（跟 LeapCleaveStrike 一樣）。
/// 動作：手和槍交給 PlayerLoadout 的雙手持槍 IK（PlayerLoadout.ArtPose：往後收 / 往前推 / 舉高、槍指的方向），
///      身體用 Run / Jump / Fall / Land 動畫當底，往下刺時借用跳劈的程序化姿勢（LeapCleavePose：腰腹、腿）。
/// </summary>
[DefaultExecutionOrder(-310)]   // 比 PlayerCombat、LeapCleaveStrike 早：□ 在同一幀先被這裡接走
public sealed class SpearArts : MonoBehaviour
{
    [Header("按鍵判定")]
    [Tooltip("按了跳躍之後，多久內按攻擊算「飛身貫地」（人還沒離地也算）")]
    public float comboWindow = .5f;
    [Tooltip("冷卻時間（秒）：招式整段做完之後才開始算")]
    public float chargeCooldown = 1.5f, plungeCooldown = 1.8f;

    [Header("找目標")]
    public float chargeRadius = 10f;
    [Tooltip("突刺只找前方這個角度內的敵人（鏡頭的前方）")]
    public float chargeAngle = 75f;
    public float plungeRadius = 10f;

    [Header("破陣突刺")]
    public float windupSeconds = .16f;
    public float dashSeconds = .26f;
    public float skidSeconds = .24f;
    [Tooltip("衝過敵人多遠才停")]
    public float overshoot = 2.6f;
    public float maxDash = 9.5f;

    [Header("飛身貫地")]
    public float riseSeconds = .34f;
    public float hoverSeconds = .16f;
    public float plungeSeconds = .12f;
    public float stuckSeconds = .16f;
    public float recoverSeconds = .3f;
    [Tooltip("飛到敵人頭頂上方多高")]
    public float apexAboveHead = 1.3f;

    [Header("傷害")]
    public float baseDamage = 40f;
    public float chargeMultiplier = 1.8f;
    public float plungeMultiplier = 2.4f;
    public float shockRadius = 2.8f;
    public float shockDamage = 20f;

    /// <summary>現在拿的是長槍（這兩招可以用）</summary>
    public bool Active => PlayerLoadout.Current == WeaponLoadout.Spear && combat != null && !combat.bowMode;
    public bool Playing { get; private set; }
    /// <summary>冷卻剩幾秒（招式進行中 = 整段冷卻）。給 MoveCooldownHud 畫圓形倒數</summary>
    public float ChargeRemaining => Playing && playingCharge ? chargeCooldown : Mathf.Max(0f, nextCharge - Time.unscaledTime);
    public float PlungeRemaining => Playing && !playingCharge ? plungeCooldown : Mathf.Max(0f, nextPlunge - Time.unscaledTime);

    ThirdPersonController motor;
    PlayerCombat combat;
    PlayerHealth health;
    CharacterController body;
    PlayerVisualAnimator visualDriver;
    PlayerWeaponPresenter weapon;
    LeapCleaveStrike leap;
    ShieldSlideStrike slide;
    Animator animator;
    LeapCleavePose pose;
    Transform model;
    Vector3 modelPos;
    float nextCharge, nextPlunge, groundOffset;
    float jumpAt = float.NegativeInfinity;
    bool bodyWasEnabled, driverWasEnabled, playingCharge, leapDisabledByUs, slideDisabledByUs;

    static bool hooked;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { hooked = false; PlayerLoadout.ArtPose = default; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => Attach(); }
        Attach();
    }

    static void Attach()
    {
        foreach (var c in FindObjectsByType<PlayerCombat>(FindObjectsInactive.Exclude))
            if (c.GetComponent<PlayerHealth>() != null && c.GetComponent<SpearArts>() == null)
                c.gameObject.AddComponent<SpearArts>();
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
        UpdateSwordMoves();
        if (Playing) return;
        if (Time.timeScale <= 0f || GameUI.OverlayOpen || RangeInputSettings.InputBlocked || motor == null) { jumpAt = float.NegativeInfinity; return; }
        if (!Active) return;
        float now = Time.unscaledTime;
        if (InputBindings.Pressed(GameAction.Jump)) jumpAt = now;
        if (!InputBindings.Pressed(GameAction.Attack)) return;

        bool air = !motor.IsGrounded || now - jumpAt <= comboWindow;
        if (air)
        {
            // 空中側閃中 / 剛側閃完：交給幻影分身突進
            if (motor.IsAirDashing || (Time.time - motor.LastAirDashEndTime <= .25f && !motor.IsGrounded)) return;
            if (now < nextPlunge || !CanStart()) return;
            jumpAt = float.NegativeInfinity;
            if (FindTarget(plungeRadius, 180f, out var t, out var d)) StartCoroutine(Plunge(t, d));
        }
        else if (InputBindings.Held(GameAction.Sprint))
        {
            if (now < nextCharge || !CanStart()) return;
            if (FindTarget(chargeRadius, chargeAngle, out var t, out var d)) StartCoroutine(Charge(t, d));
        }
    }

    /// <summary>
    /// 一個武器兩招：拿長槍時把大刀的跳劈（同一組按鍵換成飛身貫地）和盾滑關掉，換回別的武器再打開（只打開自己關掉的）
    /// </summary>
    void UpdateSwordMoves()
    {
        if (leap == null) leap = GetComponent<LeapCleaveStrike>();
        if (slide == null) slide = GetComponent<ShieldSlideStrike>();
        bool spear = Active;
        if (leap != null)
        {
            if (spear && leap.enabled && !leap.Playing) { leap.enabled = false; leapDisabledByUs = true; }
            else if (!spear && leapDisabledByUs) { leap.enabled = true; leapDisabledByUs = false; }
        }
        if (slide != null)
        {
            if (spear && slide.enabled && !slide.Playing) { slide.enabled = false; slideDisabledByUs = true; }
            else if (!spear && slideDisabledByUs) { slide.enabled = true; slideDisabledByUs = false; }
        }
    }

    bool CanStart()
    {
        if (health == null || health.IsDead || combat == null) return false;
        if (!motor.inputEnabled || motor.IsStunned || combat.bowMode || SkillAimCancellation.IsAiming) return false;
        if (slide != null && slide.Playing) return false;
        if (leap != null && leap.Playing) return false;
        foreach (var skill in FindObjectsByType<VoidRendSkillDemo>(FindObjectsInactive.Exclude))
            if (skill.IsPlaying) return false;
        return true;
    }

    /// <summary>附近活著的敵人：越近、越在鏡頭（或角色）前方的越優先；maxAngle 以外的不算</summary>
    bool FindTarget(float radius, float maxAngle, out Transform target, out IDamageable damageable)
    {
        target = null; damageable = null;
        Vector3 me = transform.position;
        Vector3 look = motor.cameraTransform != null ? motor.cameraTransform.forward : transform.forward;
        look = Vector3.ProjectOnPlane(look, Vector3.up).normalized;
        float best = float.MaxValue;
        foreach (var c in Physics.OverlapSphere(me + Vector3.up, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
            var d = c.GetComponentInParent<IDamageable>();
            if (d == null || d.IsDead || !(d is Component owner)) continue;
            if (!CalligraphyWorldIsolation.Allows(owner.transform)) continue;
            Vector3 to = Vector3.ProjectOnPlane(owner.transform.position - me, Vector3.up);
            if (Mathf.Abs(owner.transform.position.y - me.y) > 4f) continue;
            float angle = to.sqrMagnitude > .01f ? Vector3.Angle(look, to) : 0f;
            if (angle > maxAngle) continue;
            float score = to.magnitude * (1f + angle / 90f);
            if (score < best) { best = score; target = owner.transform; damageable = d; }
        }
        return target != null;
    }

    // ---------------- 共用：開始 / 結束 ----------------

    void Begin(bool charge)
    {
        Playing = true;
        playingCharge = charge;
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
        pose = null;
        if (!charge && animator != null && animator.isHuman)
        {
            pose = animator.GetComponent<LeapCleavePose>();
            if (pose == null) pose = animator.gameObject.AddComponent<LeapCleavePose>();
            pose.Init(animator, transform);
        }
        weapon = GetComponentInChildren<PlayerWeaponPresenter>(true);
    }

    void Cleanup()
    {
        if (!Playing) return;
        Playing = false;
        float now = Time.unscaledTime;
        if (playingCharge) nextCharge = now + chargeCooldown; else nextPlunge = now + plungeCooldown;   // 冷卻從招式結束才開始算
        PlayerLoadout.ArtPose = default;
        if (pose != null) { pose.arch = 0f; pose.strike = 0f; }
        if (model != null) model.localPosition = modelPos;
        if (health != null) health.SetSkillInvulnerability(this, false);
        if (body != null) body.enabled = bodyWasEnabled || body.enabled;
        if (visualDriver != null) visualDriver.enabled = driverWasEnabled;
        if (animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime("Idle", .15f, 0, 0f);
        if (motor != null) { motor.ResetMotion(); motor.UnlockActions(); }
    }

    void OnDisable() { StopAllCoroutines(); Cleanup(); }

    static void Art(float weight, float pull, float extend, float raise, Vector3 dir) =>
        PlayerLoadout.ArtPose = new PlayerLoadout.SpearArtPose { weight = weight, pull = pull, extend = extend, raise = raise, dir = dir };

    Transform Tip => weapon != null && weapon.BladeTip != null ? weapon.BladeTip
        : animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : transform;

    float Damage(float multiplier) => baseDamage * multiplier * WeaponForge.DamageScale;

    // ---------------- 破陣突刺 ----------------

    IEnumerator Charge(Transform target, IDamageable damageable)
    {
        Begin(true);
        var skins = GetComponentsInChildren<SkinnedMeshRenderer>();
        try
        {
            Vector3 dir = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
            if (dir.sqrMagnitude < .01f) dir = transform.forward;
            dir.Normalize();
            Vector3 level = (dir - Vector3.up * .03f).normalized;

            // ── 1. 蓄勢：蹲低、槍往後收 ──
            Play("Idle", .05f);
            GameAudio.Play("SFX/RPG/clothBelt2", transform.position, .8f, .8f, .05f, 1f, 20f);
            for (float t = 0f; t < windupSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / windupSeconds);
                FaceDir(dir);
                Art(Mathf.SmoothStep(0f, 1f, u * 1.6f), 1f, 0f, 0f, level);
                if (model != null) model.localPosition = modelPos + Vector3.down * (.14f * Mathf.SmoothStep(0f, 1f, u));
                yield return null;
            }

            // ── 2. 爆衝：直線衝過去，路上的敵人一個一個刺穿 ──
            Play("Run", .04f);
            GameAudio.Play("SFX/Gen/swing_whoosh_2", transform.position, 1f, 1.15f, .05f, 1f, 25f);
            ThirdPersonCamera.ShakeMain(.15f, .2f);
            Vector3 start = transform.position;
            float flat = Vector3.ProjectOnPlane(target.position - start, Vector3.up).magnitude;
            Vector3 end = start + dir * Mathf.Clamp(flat + overshoot, 4f, maxDash);
            var pierced = new HashSet<IDamageable>();
            if (Tip != null) CombatVFX.Trail(Tip, new Color(.95f, .9f, .75f), .14f);
            float nextFx = 0f;
            Vector3 prev = transform.position;
            for (float t = 0f; t < dashSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / dashSeconds);
                MoveOnGround(Vector3.Lerp(start, end, 1f - (1f - u) * (1f - u) * (1f - u)));
                FaceDir(dir);
                Art(1f, 0f, Mathf.SmoothStep(0f, 1f, u * 3f), 0f, level);
                if (model != null) model.localPosition = modelPos + Vector3.down * (.14f * (1f - u));
                Pierce(prev, transform.position, dir, pierced);
                prev = transform.position;
                if (t >= nextFx)
                {
                    nextFx = t + .04f;
                    CombatVFX.Puff(transform.position - Vector3.up * groundOffset + Vector3.up * .05f, -dir, new Color(.78f, .72f, .62f, .5f));
                    if (skins.Length > 0) AfterimageGhost.Spawn(skins, new Color(1f, .85f, .6f, .38f), .22f);
                }
                PadRumble.Pulse(.35f, .25f, .05f);
                yield return null;
            }
            Pierce(prev, transform.position, dir, pierced);

            // ── 3. 煞住：往前滑一小段，槍慢慢收回來 ──
            Vector3 skidFrom = transform.position;
            Vector3 skidTo = skidFrom + dir * .7f;
            for (float t = 0f; t < skidSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / skidSeconds);
                MoveOnGround(Vector3.Lerp(skidFrom, skidTo, 1f - (1f - u) * (1f - u)));
                Art(1f - u * u, 0f, 1f - Mathf.SmoothStep(0f, 1f, u), 0f, level);
                if (t < skidSeconds * .5f)
                    CombatVFX.Puff(transform.position - Vector3.up * groundOffset + Vector3.up * .05f, -dir, new Color(.78f, .72f, .62f, .4f));
                yield return null;
            }
        }
        finally { Cleanup(); }
    }

    /// <summary>從 a 衝到 b 的這一段（加上槍往前伸的長度）碰到的敵人：每個只刺一次</summary>
    void Pierce(Vector3 a, Vector3 b, Vector3 dir, HashSet<IDamageable> done)
    {
        Vector3 lift = Vector3.up * (1f - groundOffset);   // 從腳底往上 1 m（腰的高度）
        Vector3 p0 = a + lift, p1 = b + dir * 1.7f + lift;
        foreach (var c in Physics.OverlapCapsule(p0, p1, .95f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
            var d = c.GetComponentInParent<IDamageable>();
            if (d == null || d.IsDead || !(d is Component owner) || !CalligraphyWorldIsolation.Allows(owner.transform)) continue;
            if (!done.Add(d)) continue;
            Vector3 point = c.ClosestPoint(b + Vector3.up * .3f);
            float dealt = Damage(chargeMultiplier);
            // 一般招式：沒有強敵的技能傷害上限；打中算普攻，會累積技能條
            d.TakeDamage(dealt, point);
            combat.ReportSkillHit(dealt, d, point, true, countsAsSkill: false);
            CombatVFX.Impact(point, new Color(1f, .8f, .45f));
            CombatVFX.Slash(point, dir, new Color(1f, .95f, .85f));
            GameAudio.Play("SFX/RPG/knifeSlice", point, 1f, .9f + .1f * done.Count, .05f, 1f, 35f);
            PadRumble.Pulse(.8f, .6f, .12f);
        }
    }

    // ---------------- 飛身貫地 ----------------

    IEnumerator Plunge(Transform target, IDamageable damageable)
    {
        Begin(false);
        try
        {
            Vector3 dir = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
            if (dir.sqrMagnitude < .01f) dir = transform.forward;
            dir.Normalize();
            Vector3 down = (-Vector3.up * .92f + dir * .38f).normalized;
            float enemyRadius = Radius(target, out float enemyTop);
            float ground = GroundY(transform.position, out float gy) ? gy : transform.position.y - groundOffset;
            bool grounded = transform.position.y - ground < groundOffset + .3f;

            // ── 1. 飛到敵人頭上：槍慢慢舉到胸口、槍尖轉朝下 ──
            Play("Jump", .05f);
            if (grounded)
            {
                GameAudio.Play("SFX/Gen/jump_whoosh_1", transform.position, .9f, .95f, .05f, 1f, 25f);
                CombatVFX.Ring(transform.position - Vector3.up * groundOffset + Vector3.up * .05f, new Color(.85f, .9f, 1f), 1.1f);
            }
            float rise = grounded ? riseSeconds : riseSeconds * .65f;
            Vector3 from = transform.position;
            float apexY = Mathf.Max(Mathf.Max(enemyTop, target.position.y + 1.6f) + apexAboveHead + groundOffset, from.y + .4f);
            for (float t = 0f; t < rise; t += Time.deltaTime)
            {
                if (TargetGone(target, damageable)) yield break;
                float u = Mathf.Clamp01(t / rise);
                Vector3 apex = target.position - dir * .3f;
                Vector3 p = Vector3.Lerp(from, apex, 1f - (1f - u) * (1f - u));
                p.y = Mathf.Lerp(from.y, apexY, Mathf.Sin(u * Mathf.PI * .5f));
                transform.position = p;
                FaceTo(target.position);
                float k = Mathf.SmoothStep(0f, 1f, u);
                Art(k, .5f * k, 0f, k, down);
                if (pose != null) { pose.arch = .5f * k; pose.strike = 0f; }
                yield return null;
            }

            // ── 2. 蓄勢：停在頭頂，槍再往上提 ──
            Play("Fall", .1f);
            Vector3 hover = transform.position;
            Transform tip = Tip;
            for (float t = 0f; t < hoverSeconds; t += Time.deltaTime)
            {
                if (TargetGone(target, damageable)) yield break;
                float u = Mathf.Clamp01(t / hoverSeconds);
                transform.position = hover + Vector3.up * (.15f * Mathf.Sin(u * Mathf.PI * .5f));
                FaceTo(target.position);
                Art(1f, Mathf.Lerp(.5f, 1f, u), 0f, 1f, down);
                if (pose != null) pose.arch = Mathf.Lerp(.5f, .8f, u);
                if (tip != null) CombatVFX.Puff(tip.position, Random.onUnitSphere, new Color(1f, .85f, .55f, .7f), true);
                PadRumble.Pulse(.2f + .4f * u, .1f + .3f * u, .06f);
                yield return null;
            }

            // ── 3. 往下插：整個人砸下去，槍插進敵人 ──
            if (tip != null) CombatVFX.Trail(tip, new Color(1f, .92f, .75f), .2f);
            GameAudio.Play("SFX/Gen/swing_whoosh_2", transform.position, 1f, .8f, .05f, 1f, 25f);
            Vector3 strikeFrom = transform.position;
            Vector3 landing = target.position - dir * (enemyRadius * .5f + .45f);
            landing.y = LandingGround(landing, target, ground) + groundOffset;
            bool hit = false;
            for (float t = 0f; t < plungeSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / plungeSeconds);
                transform.position = Vector3.Lerp(strikeFrom, landing, u * u);
                Art(1f, 1f - u, Mathf.SmoothStep(0f, 1f, u), 1f - .35f * u, down);
                if (pose != null) { pose.strike = Mathf.SmoothStep(0f, 1f, u); pose.arch = .8f * (1f - pose.strike); }
                if (!hit && u >= .5f) { hit = true; Impale(target, damageable); }
                yield return null;
            }
            transform.position = landing;
            if (!hit) Impale(target, damageable);

            // ── 4. 落地震波 ──
            Vector3 groundPoint = landing - Vector3.up * groundOffset;
            ShockwaveFx.Spawn(groundPoint + Vector3.up * .1f, shockRadius, .32f, .32f);
            CombatVFX.Ring(groundPoint + Vector3.up * .05f, new Color(1f, .9f, .7f), shockRadius);
            for (int i = 0; i < 8; i++)
            {
                var o = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                CombatVFX.Puff(groundPoint + o * .5f + Vector3.up * .05f, o + Vector3.up * .6f, new Color(.75f, .7f, .6f, .6f));
            }
            ThirdPersonCamera.ShakeMain(.4f, .3f);
            PadRumble.Pulse(1f, .8f, .3f);
            GameAudio.Play("SFX/Gen/hit_heavy_1", groundPoint, 1f, .8f, .05f, 1f, 35f);
            ShockNearby(groundPoint, damageable);

            // ── 5. 槍插在地上停一下，再拔起來站好 ──
            Play("Land", .05f);
            for (float t = 0f; t < stuckSeconds; t += Time.deltaTime)
            {
                Art(1f, 0f, 1f, .65f, down);
                if (model != null) model.localPosition = modelPos + Vector3.down * .3f;
                yield return null;
            }
            GameAudio.Play("SFX/RPG/clothBelt2", transform.position, .7f, 1.1f, .05f, 1f, 20f);
            for (float t = 0f; t < recoverSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / recoverSeconds);
                float k = 1f - Mathf.SmoothStep(0f, 1f, u);
                Art(k, 0f, k, .65f * k, down);
                if (pose != null) pose.strike = k;
                if (model != null) model.localPosition = modelPos + Vector3.down * (.3f * k * k);
                yield return null;
            }
        }
        finally { Cleanup(); }
    }

    void Impale(Transform target, IDamageable damageable)
    {
        if (TargetGone(target, damageable)) return;
        Vector3 point = target.position + Vector3.up * 1.1f;
        float dealt = Damage(plungeMultiplier);
        damageable.TakeDamage(dealt, point);
        combat.ReportSkillHit(dealt, damageable, point, true, countsAsSkill: false);
        CombatVFX.Slash(point + Vector3.up * .5f, Vector3.down, new Color(1f, .95f, .85f));
        CombatVFX.Impact(point, new Color(1f, .8f, .4f));
        GameAudio.Play("SFX/RPG/knifeSlice", point, 1f, .75f, .05f, 1f, 35f);
    }

    void ShockNearby(Vector3 center, IDamageable main)
    {
        var done = new HashSet<IDamageable> { main };
        foreach (var c in Physics.OverlapSphere(center + Vector3.up, shockRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
            var d = c.GetComponentInParent<IDamageable>();
            if (d == null || d.IsDead || !done.Add(d) || !(d is Component owner) || !CalligraphyWorldIsolation.Allows(owner.transform)) continue;
            Vector3 p = c.ClosestPoint(center + Vector3.up);
            float dealt = shockDamage * WeaponForge.DamageScale;
            d.TakeDamage(dealt, p);
            combat.ReportSkillHit(dealt, d, p, false, countsAsSkill: false);
        }
    }

    // ---------------- 小工具（跟 LeapCleaveStrike 一樣的做法） ----------------

    void Play(string state, float fade)
    {
        if (animator == null) return;
        int hash = Animator.StringToHash(state);
        if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
    }

    /// <summary>沿著地面移動：撞到牆停下來（敵人不算牆，可以穿過去），坡太陡當成牆</summary>
    void MoveOnGround(Vector3 position)
    {
        Vector3 from = transform.position;
        Vector3 step = position - from; step.y = 0f;
        float len = step.magnitude;
        if (len > .001f)
        {
            float wall = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(from + Vector3.up * .3f, .35f, step / len, len, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<IDamageable>() != null || hit.collider is CharacterController) continue;
                if (hit.distance > 0f && hit.distance < wall) wall = hit.distance;
            }
            if (wall < float.MaxValue) position = from + step / len * Mathf.Max(0f, wall - .05f);
        }
        position.y = from.y;
        if (GroundY(position, out float g))
        {
            float dy = g + groundOffset - from.y;
            if (dy > .45f) { position.x = from.x; position.z = from.z; }
            else if (dy > -1.2f) position.y = g + groundOffset;
        }
        transform.position = position;
    }

    float LandingGround(Vector3 landing, Transform target, float fallback)
    {
        float feet = target.position.y;
        var cc = target.GetComponentInParent<CharacterController>();
        if (cc != null) feet = cc.bounds.min.y;
        float from = Mathf.Max(feet, fallback) + 1.5f;
        float y = float.NegativeInfinity, best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(new Vector3(landing.x, from, landing.z), Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform) || h.transform.IsChildOf(target) || h.collider is CharacterController) continue;
            if (h.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        if (SkeletonCentipedeBossAI.TerrainY(landing, out float terrain)) y = Mathf.Max(y, terrain);
        return float.IsNegativeInfinity(y) ? fallback : y;
    }

    bool GroundY(Vector3 at, out float y)
    {
        y = 0f;
        float best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(at + Vector3.up * 1.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform) || h.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        return best < float.MaxValue;
    }

    void FaceTo(Vector3 point) => FaceDir(point - transform.position);

    void FaceDir(Vector3 d)
    {
        d.y = 0f;
        if (d.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(d.normalized);
    }

    static bool TargetGone(Transform target, IDamageable d) => target == null || d == null || !(d is Component c) || c == null || d.IsDead;

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
