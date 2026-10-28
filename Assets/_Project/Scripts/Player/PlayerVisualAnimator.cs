using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 將既有玩家控制器狀態轉成純視覺 Animator 參數。
/// 不參與移動、碰撞、傷害或技能邏輯。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // Read grounded/vertical velocity after the movement Update.
public sealed class PlayerVisualAnimator : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] ThirdPersonController controller;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerWeaponPresenter weaponPresenter;
    PlayerShieldBowAnimator shieldBow; // 盾牌格擋 / 盾擊 / 拉弓的動畫（自動加上）
    PlayerPaladinShield paladinShield; // 原本聖騎士的盾牌模型（PlayerRig 上有掛才有）
    PlayerArcherBow archerBow;         // 敵艦弓箭手的弓（PlayerShieldBowAnimator 會自動加上）
    [SerializeField] Vector2 idleChangeInterval = new Vector2(5.5f, 8.5f);
    [Header("弓與盾（共用角色視覺）")]
    [SerializeField] Color bowWoodColor = new Color(0.32f, 0.14f, 0.055f, 1f);
    [SerializeField] Color shieldColor = new Color(0.12f, 0.24f, 0.38f, 1f);

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int GroundedHash = Animator.StringToHash("Grounded");
    static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
    // 落地緩衝動畫（Land）的播放速度：輕輕落地播快一點、從高處摔下來播慢一點（完整地蹲下緩衝）
    static readonly int LandRateHash = Animator.StringToHash("LandRate");
    static readonly int Attack1Hash = Animator.StringToHash("Attack1");
    static readonly int Attack2Hash = Animator.StringToHash("Attack2");
    static readonly int DieHash = Animator.StringToHash("Die");
    static readonly int RespawnHash = Animator.StringToHash("Respawn");
    static readonly string[] IdleStates = { "Idle", "wait", "look_around" };
    static readonly string[] HitStates = { "Hit", "hit_to_body_02", "hit_to_head", "hit_to_side", "hit_to_stomach" };

    float previousHealth;
    float nextIdleChange;
    int idleIndex;
    int hitIndex;
    bool previousGrounded = true;
    float pounceStartedAt = float.NegativeInfinity;
    float landingVelocity;
    Transform hips;
    Transform leftToes, rightToes; // 有腳趾骨就用「腳 → 腳趾」當腳尖方向，最準
    Transform chest;
    Transform rightUpperArm;
    Transform rightForearm;
    Transform rightHand;
    Transform leftUpperArm;
    Transform leftForearm;
    Transform leftHand;
    Transform leftUpperLeg;
    Transform leftLowerLeg;
    Transform leftFoot;
    Transform rightUpperLeg;
    Transform rightLowerLeg;
    Transform rightFoot;
    Transform head;
    Transform visualRoot;
    Vector3 visualRootRestPosition;
    Quaternion visualRootRestRotation;
    float slashStartedAt = float.NegativeInfinity;
    // 袈裟斬的節奏 (秒)：抬手 → 斬下 → 收刀。改這三個數字就能調揮刀快慢
    const float SlashPrepare = .2f, SlashCut = .34f, SlashRecover = .24f;
    const float SlashTotal = SlashPrepare + SlashCut + SlashRecover; // 不含揮到底停住的 hold、也不含額外蓄力
    // 最後一刀的蓄力（抬手）時間 (秒)，比一般刀的 SlashPrepare 久
    const float FinisherPrepare = .5f;
    // 接招容錯：一刀收完之後還有多久 (秒) 可以按下一段（揮刀中途先按也會記住）
    const float ComboGrace = .45f;
    // 刀鋒在模型的哪一面：揮刀時刀鋒朝後就改成 false / true 對調
    const bool FlipBladeEdge = true;
    // 每一刀揮完之後到能接下一刀的硬直 (秒)；最後一段（第三刀）比較長
    const float SlashRecovery = .36f, FinisherRecovery = .56f;
    // 揮刀時身體扭轉的角度：臀部 / 胸口（胸口會再疊在臀部上）
    const float SlashHipTwist = 52f, SlashChestTwist = 46f;
    // 揮刀時前腳往前踏、後腳往後撤多少 (m)
    // 後腳只稍微撤一點、踩穩不動；前腳往前跨大步
    const float SlashStanceForward = .52f, SlashStanceBack = .08f;
    // 重心往前腳移多少 (m)：臀部往前壓，前腳膝蓋自然彎下去（微蹲），後腳伸直踩穩
    const float SlashWeightShift = .28f, SlashWeightSide = .05f; // 往前 / 往前腳那一側
    // 揮刀時整體再多蹲多少 (m)；腿伸不到地面時會自動再降
    const float SlashBaseDrop = .06f;
    // 跳劈時刀路再往斜的方向轉幾度（從角色背後看順時針）
    const float PlungeTilt = 24f;
    float plungeBlend;
    bool plungeSlash;
    // 雙手持刀時左手離右手多遠（往劍柄尾端）(m)
    const float SlashGripGap = .11f;
    // 翻滾：蹲下前撲結束、滾完一圈的時間點（0~1）、往下壓多少 (m)、前撲時先前傾幾度
    const float RollDiveEnd = .16f, RollSpinEnd = .74f, RollDrop = .36f, RollLean = 28f;
    int slashSide;

    /// <summary>
    /// 三段連擊各自的刀路（座標以右肩為原點、角色的 右 / 上 / 前 為軸，單位 m）：
    ///   第一刀 右下 → 左上 斜斬、第二刀 左下 → 右上 拉回來、第三刀 蓄力後由上往下（稍微斜）劈下。
    /// twist = 身體扭轉（+1 = 轉向右邊、-1 = 轉向左邊）；pitch = 上身前壓（正）/ 後仰（負）的角度
    /// </summary>
    struct SlashPath
    {
        public Vector3 handFrom, handTo, bladeFrom, bladeTo, pole;
        public float twistFrom, twistTo, pitchFrom, pitchTo, arc;
        public float crouch, lift; // 蹲多低、抬手時先往上提多高 (m)；有 lift 的話是抬手時站高、劈下時才蹲下去
        public Vector3 offFrom, offTo; // 單手持刀時沒拿刀的左手：以左肩為原點，往外伸出去平衡（twoHanded 時不用）
        public float hold;             // 揮到底之後停住多久（頓一下）(秒)
        public bool edgeFollowsSwing;  // 刀鋒朝揮的方向（劍會轉過直立的那一刀要打開，刀鋒才不會亂轉）
        public Vector3 windBack, windBlade; // 抬手時手再往後拉多少（加在 handFrom 上）、劍往哪裡指（拉到最後面）
        public bool leftFootForward;   // 弓步時左腳在前（不然右腳在前）
        public bool twoHanded;         // 雙手持刀：左手握在右手下面
        public float prepareTime;      // 抬手（蓄力）時間 (秒)；0 = 用 SlashPrepare
        public float edgeFlat;         // 刀鋒放橫多少：0 = 刀鋒朝上、1 = 刀鋒完全朝揮的方向
        public float Prepare => prepareTime > 0f ? prepareTime : SlashPrepare;
        public float cutTime;          // 揮的時間 (秒)；0 = 用 SlashCut
        public bool airborne;          // 空中的刀：不蹲、不踩腳
        public float Cut => cutTime > 0f ? cutTime : SlashCut;
        public float Total => Prepare + Cut + SlashRecover + hold;
    }

    /// <summary>空中單手輕砍：由上往下一刀，左手往外伸出去平衡（時間會同步給 MeleeCombat 的 airSlash*）</summary>
    static readonly SlashPath AirSlash = new SlashPath
    {
        handFrom = new Vector3(.32f, .45f, .1f), handTo = new Vector3(-.22f, -.32f, .4f),
        bladeFrom = new Vector3(.5f, .9f, -.1f), bladeTo = new Vector3(-.45f, -.55f, .75f),
        pole = new Vector3(1f, -.3f, -.2f),
        twistFrom = .2f, twistTo = -.2f, pitchFrom = -6f, pitchTo = 16f, arc = .2f, crouch = 0f, lift = 0f,
        offFrom = new Vector3(-.4f, -.05f, .1f), offTo = new Vector3(-.55f, -.15f, -.25f), hold = .03f,
        edgeFollowsSwing = true,
        windBack = new Vector3(0f, .08f, -.15f), windBlade = new Vector3(.05f, .6f, -.8f),
        prepareTime = .12f, cutTime = .2f, airborne = true,
    };

    static readonly SlashPath[] SlashPaths =
    {
        new SlashPath // 右下 → 左上
        {
            handFrom = new Vector3(.42f, -.2f, .22f), handTo = new Vector3(-.78f, .14f, .3f),
            bladeFrom = new Vector3(.85f, -.3f, .55f), bladeTo = new Vector3(-.9f, .35f, .45f),
            pole = new Vector3(.4f, -1f, -.2f),
            twistFrom = .6f, twistTo = -1f, pitchFrom = 0f, pitchTo = 8f, arc = .22f, crouch = .12f, lift = 0f,
            offFrom = new Vector3(-.12f, -.2f, .28f), offTo = new Vector3(-.55f, -.08f, -.22f), hold = .05f,
            windBack = new Vector3(.12f, -.06f, -.4f), windBlade = new Vector3(.55f, -.25f, -.85f),
            leftFootForward = true, edgeFlat = .55f,
            twoHanded = true,
        },
        new SlashPath // 左下 → 右上
        {
            handFrom = new Vector3(-.72f, -.16f, .3f), handTo = new Vector3(.45f, .16f, .24f),
            bladeFrom = new Vector3(-.9f, -.3f, .45f), bladeTo = new Vector3(.85f, .35f, .55f),
            pole = new Vector3(.4f, -1f, -.2f),
            twistFrom = -.6f, twistTo = 1f, pitchFrom = 4f, pitchTo = 6f, arc = .22f, crouch = .12f, lift = 0f,
            offFrom = new Vector3(-.5f, -.1f, -.1f), offTo = new Vector3(-.42f, .02f, .42f), hold = .05f,
            windBack = new Vector3(-.08f, -.06f, -.38f), windBlade = new Vector3(-.55f, -.25f, -.85f),
            edgeFlat = .55f,
            twoHanded = true,
        },
        new SlashPath // 由上往下，從右上斜劈到左下
        {
            handFrom = new Vector3(.22f, .5f, .12f), handTo = new Vector3(-.32f, -.52f, .36f),
            bladeFrom = new Vector3(.3f, 1f, -.05f), bladeTo = new Vector3(-.35f, -.75f, .6f),
            pole = new Vector3(1f, -.4f, -.2f),
            twistFrom = .4f, twistTo = -.4f, pitchFrom = -10f, pitchTo = 28f, arc = .3f, crouch = .24f, lift = .05f,
            offFrom = new Vector3(-.35f, .1f, .18f), offTo = new Vector3(-.58f, -.25f, -.22f), hold = .22f,
            edgeFollowsSwing = true,
            windBack = new Vector3(-.05f, .1f, -.3f), windBlade = new Vector3(.15f, .55f, -.95f),
            twoHanded = true, prepareTime = FinisherPrepare, edgeFlat = .45f,
        },
    };
    bool wasRolling;
    float animatorSpeedBeforeRoll = 1f;
    BoneRestPose[] rollPose;
    Transform bowProp;
    Transform shieldProp;

    struct BoneRestPose
    {
        public Transform bone;
        public Vector3 localPosition;
        public Quaternion localRotation;
    }

    void Awake()
    {
        ResolveReferences();
        if (combat != null && GetComponent<CalligraphyPlayerBladeTrail>() == null)
            gameObject.AddComponent<CalligraphyPlayerBladeTrail>();
        shieldBow = GetComponent<PlayerShieldBowAnimator>();
        if (shieldBow == null) shieldBow = gameObject.AddComponent<PlayerShieldBowAnimator>();
        paladinShield = GetComponent<PlayerPaladinShield>();
        if (health != null) previousHealth = health.Health;
        CacheHumanoidBones();
        CacheVisualRoot();
        ConfigureShortSlashTiming();
        ScheduleNextIdle();
    }

    void ResolveReferences()
    {
        if (controller == null) controller = GetComponent<ThirdPersonController>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (weaponPresenter == null) weaponPresenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
    }

    void OnEnable()
    {
        // 關閉 Domain / Scene Reload 的快速 Play Mode，或執行中才補掛 PlayerCombat 時，
        // Awake 可能保留舊的空參照；每次啟用重新抓取，確保揮刀事件一定會接上。
        ResolveReferences();
        ConfigureShortSlashTiming();
        if (combat != null) combat.OnSwing += HandleSwing;
        if (health != null)
        {
            health.OnDied += HandleDeath;
            health.OnRespawned += HandleRespawn;
        }
    }

    void OnDisable()
    {
        if (combat != null) combat.OnSwing -= HandleSwing;
        if (health != null)
        {
            health.OnDied -= HandleDeath;
            health.OnRespawned -= HandleRespawn;
        }
        RestoreRollVisualState();
        if (bowProp != null) bowProp.gameObject.SetActive(false);
        if (shieldProp != null) shieldProp.gameObject.SetActive(false);
    }

    void Update()
    {
        // Wheel skills deliberately disable the Animator while owning the complete pose.
        // Do not crossfade or add locomotion offsets on top of that frozen procedural pose.
        if (animator == null || !animator.enabled) return;
        var bossControl = GetComponent<CalligraphyPlayerControl>();
        if (bossControl != null && bossControl.Active)
        {
            if (health != null) previousHealth = health.Health;
            animator.SetFloat(SpeedHash, 0);
            animator.SetBool(GroundedHash, true);
            animator.SetFloat(VerticalSpeedHash, 0);
            return;
        }

        // 倒下的期間什麼都不要切：被打飛時的移動 / 落下 / 翻滾畫面都會把「倒下」動畫蓋掉
        if (health != null && health.IsDead)
        {
            previousHealth = health.Health;
            return;
        }

        // 攀爬中：Climb 動畫的進度由 CalligraphyPlatformClimb 控制，這裡不要切任何動畫
        if (CalligraphyPlatformClimb.IsClimbing(health != null ? health.transform : transform))
        {
            if (health != null) previousHealth = health.Health;
            landingVelocity = 0f;
            previousGrounded = true;
            return;
        }

        UpdateRollAnimationState();

        float normalizedSpeed = 0f;
        bool grounded = true;
        if (controller != null)
        {
            float referenceSpeed = Mathf.Max(0.01f, controller.sprintSpeed);
            // 站在船上時移動腳本被停用：當成站在地上，不要播往下掉
            bool standingOnBoat = !controller.isActiveAndEnabled;
            normalizedSpeed = standingOnBoat ? 0f : controller.CurrentSpeed / referenceSpeed;
            grounded = standingOnBoat || controller.IsGrounded;
            animator.SetFloat(SpeedHash, normalizedSpeed, 0.1f, Time.deltaTime);
            animator.SetBool(GroundedHash, grounded);
            animator.SetFloat(VerticalSpeedHash, controller.VerticalVelocity);
        }

        // 盾牌 / 拉弓動畫播放中、或站在船上（移動腳本停用）：不要切去待機 / 移動 / 跳躍動畫
        bool overriding = shieldBow != null && shieldBow.Overriding;
        bool onBoat = controller != null && !controller.isActiveAndEnabled;
        if (!overriding && !onBoat) ForceLocomotionWhenMoving(normalizedSpeed, grounded);
        UpdateSprintJumpFeedback(grounded);
        if (!overriding) UpdateIdleCycle(normalizedSpeed, grounded);

        if (health != null)
        {
            // 拉弓瞄準時被打不打斷（和以前的聖騎士一樣）
            bool aimingBow = combat != null && combat.bowMode && overriding;
            if (!health.IsDead && health.Health < previousHealth && !aimingBow) PlayHitReaction();
            previousHealth = health.Health;
        }
    }

    void ForceLocomotionWhenMoving(float speed, bool grounded)
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        bool extendedIdle = current.IsName("Idle") || current.IsName("wait") || current.IsName("look_around");
        bool alreadyLeavingIdle = false;
        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            alreadyLeavingIdle = next.IsName("Walk") || next.IsName("Run") ||
                                 next.IsName("Jump") || next.IsName("Fall");
        }

        // wait / look_around 原本只能等動畫播完才回 Idle。玩家一移動時直接中斷，
        // 避免控制器與腳步音效都在跑，模型卻維持休息姿勢而在地上滑行。
        // CrossFade 進行中的 current 仍會暫時回報來源狀態；若此時每幀重送，
        // Walk / Run 就會永遠被重播第 0 幀，看起來像只有起步動作。
        if (grounded && speed > 0.035f && extendedIdle && !alreadyLeavingIdle)
        {
            string locomotion = speed >= 0.68f ? "Run" : "Walk";
            animator.CrossFadeInFixedTime(locomotion, 0.055f, 0, 0f);
            ScheduleNextIdle(1f);
            return;
        }

        if (!grounded && extendedIdle && !alreadyLeavingIdle)
        {
            string airborne = controller != null && controller.VerticalVelocity > 0.05f ? "Jump" : "Fall";
            animator.CrossFadeInFixedTime(airborne, 0.15f, 0, 0f);
            ScheduleNextIdle(1f);
        }
    }

    void UpdateSprintJumpFeedback(bool grounded)
    {
        if (controller == null) return;

        bool sprinting = controller.CurrentSpeed > controller.walkSpeed * 1.05f;
        bool jumpPressed = Keyboard.current != null && !RangeInputSettings.InputBlocked &&
            Keyboard.current[RangeInputSettings.Binding(controller,RangeInputSettings.Action.Jump)].wasPressedThisFrame;
        if (grounded && sprinting && jumpPressed)
        {
            pounceStartedAt = Time.unscaledTime;
            ThirdPersonCamera.ShakeMain(0.075f, 0.16f);
        }

        if (!grounded) landingVelocity = Mathf.Min(landingVelocity, controller.VerticalVelocity);
        if (!previousGrounded && grounded)
        {
            // 落得越快，緩衝動畫播得越慢、越完整（Animator 的 Land 狀態用這個參數當速度）
            float impact = -landingVelocity;
            float rate = impact < 5f ? 2.2f : impact < 9f ? 1.5f : impact < 14f ? 1.15f : 0.9f;
            if (HasParameter(LandRateHash)) animator.SetFloat(LandRateHash, rate);
            if (landingVelocity < -2.5f)
                ThirdPersonCamera.ShakeMain(Mathf.Clamp(-landingVelocity * 0.018f, 0.06f, 0.16f), 0.15f);
            landingVelocity = 0f;
        }
        previousGrounded = grounded;
    }

    void UpdateIdleCycle(float speed, bool grounded)
    {
        if (Time.time - slashStartedAt < CurrentSlash.Total + .3f || (controller != null && controller.IsRolling)) return;
        if (!grounded || speed > 0.035f || Time.unscaledTime < nextIdleChange) return;
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (current.IsName("Attack_1") || current.IsName("Attack_2") || current.IsName("Attack_3") ||
            current.IsName("Death") || current.IsName("Jump") || current.IsName("Fall") ||
            current.IsName("Land") || current.IsName("Climb") || animator.IsInTransition(0))
        {
            ScheduleNextIdle(1f);
            return;
        }

        idleIndex = (idleIndex + 1) % IdleStates.Length;
        animator.CrossFadeInFixedTime(IdleStates[idleIndex], 0.22f, 0, 0f);
        ScheduleNextIdle();
    }

    void ScheduleNextIdle(float minimum = 0f)
    {
        float low = Mathf.Max(minimum, idleChangeInterval.x);
        float high = Mathf.Max(low, idleChangeInterval.y);
        nextIdleChange = Time.unscaledTime + Random.Range(low, high);
    }

    void HandleSwing(int comboIndex) => PlaySwordAttack(comboIndex);

    public void PlaySwordAttack(int comboIndex = 0)
    {
        if (animator == null) return;
        // 自製短版袈裟斬：回到穩定站姿後用骨架疊加約 0.42 秒（SlashTotal）的斜切，
        // 不再把原本有長前搖／後搖的 slash、chop 動畫整段加速播完。
        animator.ResetTrigger(Attack1Hash);
        animator.ResetTrigger(Attack2Hash);
        // 空中輕砍：保留跳躍 / 下落動畫，只在上面疊揮刀
        bool air = combat != null && combat.IsAirSlash;
        if (!air) animator.CrossFadeInFixedTime("Idle", 0.025f, 0, 0f);
        slashSide = air ? -1 : comboIndex;
        slashStartedAt = Time.time;
        plungeSlash = false;
        plungeBlend = 0f;
        if (weaponPresenter != null) weaponPresenter.DrawForSeconds(CurrentSlash.Total + 0.6f);
        ScheduleNextIdle(.7f);
    }

    bool? hasLandRate;
    /// <summary>Animator 裡有沒有 LandRate 參數（還沒執行安裝選單的舊 Controller 沒有，不要每次落地都噴警告）</summary>
    bool HasParameter(int hash)
    {
        if (hasLandRate == null)
        {
            hasLandRate = false;
            foreach (var p in animator.parameters) if (p.nameHash == hash) hasLandRate = true;
        }
        return hasLandRate.Value;
    }

    void LateUpdate()
    {
        if (animator == null || !animator.enabled || !animator.isHuman) return;
        if (CalligraphyPlatformClimb.IsClimbing(health != null ? health.transform : transform)) return;
        if (GetComponent<CalligraphyPlayerControl>()?.Active ?? false) return;
        if (hips == null || chest == null) CacheHumanoidBones();

        if (controller != null && controller.IsRolling)
        {
            ApplyRollVisual();
            return;
        }

        GroundFeet();
        ApplyShortKesaSlash();
        UpdateBowAndShieldVisuals();

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        bool attackOne = state.IsName("Attack_1");
        bool attackTwo = state.IsName("Attack_2");
        if ((attackOne || attackTwo) && chest != null)
        {
            float phase = Mathf.Clamp01(state.normalizedTime);
            float weight = Mathf.Sin(Mathf.Clamp01(phase / 0.82f) * Mathf.PI);
            float side = attackOne ? 1f : -1f;
            chest.localRotation *= Quaternion.Euler(-5f * weight, 17f * side * weight, 7f * side * weight);
            if (rightUpperArm != null)
                rightUpperArm.localRotation *= Quaternion.Euler(-11f * weight, 0f, 12f * side * weight);
        }

        float pounceAge = Time.unscaledTime - pounceStartedAt;
        if (pounceAge < 0f || pounceAge > 0.62f || controller == null) return;

        if (pounceAge < 0.11f)
        {
            float crouch = Mathf.Sin(pounceAge / 0.11f * Mathf.PI) * 0.13f;
            if (hips != null) hips.localPosition += Vector3.down * crouch;
            if (chest != null) chest.localRotation *= Quaternion.Euler(11f, 0f, 0f);
            if (leftUpperLeg != null) leftUpperLeg.localRotation *= Quaternion.Euler(-9f, 0f, 0f);
            if (rightUpperLeg != null) rightUpperLeg.localRotation *= Quaternion.Euler(-9f, 0f, 0f);
        }
        else if (!controller.IsGrounded)
        {
            float airWeight = Mathf.Sin(Mathf.Clamp01((pounceAge - 0.11f) / 0.51f) * Mathf.PI);
            if (chest != null) chest.localRotation *= Quaternion.Euler(20f * airWeight, 0f, 0f);
            if (hips != null) hips.localRotation *= Quaternion.Euler(8f * airWeight, 0f, 0f);
        }
    }

    void CacheHumanoidBones()
    {
        if (animator == null || !animator.isHuman) return;
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    void CacheVisualRoot()
    {
        if (animator == null) return;
        visualRoot = animator.transform;
        visualRootRestPosition = visualRoot.localPosition;
        visualRootRestRotation = visualRoot.localRotation;
    }

    void ConfigureShortSlashTiming()
    {
        if (combat == null) return;
        int count = combat.comboDamage != null ? combat.comboDamage.Length : 3;
        combat.stepWindup = new float[count];
        combat.stepSwing = new float[count];
        combat.stepRecovery = new float[count];
        for (int i = 0; i < count; i++)
        {
            combat.stepWindup[i] = SlashPaths[i % SlashPaths.Length].Prepare;
            combat.stepSwing[i] = SlashPaths[i % SlashPaths.Length].Cut;
            combat.stepRecovery[i] = i == count - 1 ? FinisherRecovery : SlashRecovery;
        }
        combat.chainAfter = 1f; // 接受單次緩衝，但完整收招後才能接下一刀。
        combat.comboGraceTime = ComboGrace;
        combat.lungeForce = 0f; // 揮刀時人不往前滑，靠前腳跨步表現往前砍
        combat.airSlashWindup = AirSlash.Prepare;
        combat.airSlashSwing = AirSlash.Cut;
    }

    void UpdateRollAnimationState()
    {
        bool rolling = controller != null && controller.IsRolling;
        if (rolling && !wasRolling)
        {
            slashStartedAt = float.NegativeInfinity;
            animatorSpeedBeforeRoll = animator.speed;
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
            animator.speed = 0f;
            CacheVisualRoot();
            CaptureRollPose();
        }
        else if (!rolling && wasRolling)
        {
            RestoreRollVisualState();
            animator.CrossFadeInFixedTime("Idle", .05f, 0, 0f);
        }
        wasRolling = rolling;
    }

    void RestoreRollVisualState()
    {
        if (!wasRolling && (animator == null || animator.speed > .001f)) return;
        RestoreRollPose();
        if (animator != null) animator.speed = Mathf.Max(.01f, animatorSpeedBeforeRoll);
        if (visualRoot != null)
        {
            visualRoot.localPosition = visualRootRestPosition;
            visualRoot.localRotation = visualRootRestRotation;
        }
        wasRolling = false;
    }

    void UpdateBowAndShieldVisuals()
    {
        if (combat == null || animator == null || !animator.isHuman) return;
        // 弓和盾牌改用：敵艦弓箭手的弓（PlayerArcherBow）、聖騎士的盾牌（PlayerPaladinShield）。
        // 這裡內建的弓 / 盾和手臂姿勢只在那兩個元件沒準備好時才用
        if (archerBow == null) archerBow = GetComponent<PlayerArcherBow>();
        bool showBow = combat.bowMode && (archerBow == null || !archerBow.Ready);
        bool showShield = combat.ShieldUp && (paladinShield == null || !paladinShield.Ready);

        if (showBow && bowProp == null) bowProp = BuildBowProp();
        if (showShield && shieldProp == null) shieldProp = BuildShieldProp();
        if (bowProp != null) bowProp.gameObject.SetActive(showBow);
        if (shieldProp != null) shieldProp.gameObject.SetActive(showShield);

        if (showBow && bowProp != null)
        {
            if (combat.IsAimingBow) ApplyBowPose();
            Vector3 position = leftHand != null
                ? leftHand.position
                : transform.position + transform.up * 1.15f - transform.right * .24f;
            bowProp.SetPositionAndRotation(position, Quaternion.LookRotation(transform.forward, transform.up));
        }

        if (showShield && shieldProp != null)
        {
            ApplyShieldPose();
            Vector3 position = leftHand != null
                ? leftHand.position + transform.forward * .08f
                : transform.position + transform.up * 1.05f + transform.forward * .35f;
            shieldProp.SetPositionAndRotation(position, Quaternion.LookRotation(transform.forward, transform.up));
        }
    }

    void ApplyBowPose()
    {
        Vector3 origin = chest != null ? chest.position : transform.position + transform.up * 1.2f;
        Vector3 leftGoal = origin + transform.forward * .56f - transform.right * .22f + transform.up * .04f;
        Vector3 rightGoal = origin + transform.forward * .17f + transform.right * .15f + transform.up * .03f;
        SolveLimbWorld(leftUpperArm, leftForearm, leftHand, leftGoal, -transform.right);
        SolveLimbWorld(rightUpperArm, rightForearm, rightHand, rightGoal, transform.right);
    }

    void ApplyShieldPose()
    {
        Vector3 origin = chest != null ? chest.position : transform.position + transform.up * 1.15f;
        Vector3 leftGoal = origin + transform.forward * .34f - transform.right * .23f - transform.up * .08f;
        SolveLimbWorld(leftUpperArm, leftForearm, leftHand, leftGoal, -transform.right);
    }

    Transform BuildBowProp()
    {
        Transform root = new GameObject("PlayerBow_Visual").transform;
        root.SetParent(transform, false);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        LineRenderer limb = new GameObject("BowLimb").AddComponent<LineRenderer>();
        limb.transform.SetParent(root, false);
        limb.useWorldSpace = false;
        limb.sharedMaterial = new Material(shader) { color = bowWoodColor };
        limb.widthMultiplier = .045f;
        limb.numCornerVertices = 3;
        limb.positionCount = 15;
        for (int i = 0; i < limb.positionCount; i++)
        {
            float t = i / (limb.positionCount - 1f) * 2f - 1f;
            limb.SetPosition(i, new Vector3(0f, t * .72f, (1f - t * t) * .22f));
        }

        LineRenderer bowString = new GameObject("BowString").AddComponent<LineRenderer>();
        bowString.transform.SetParent(root, false);
        bowString.useWorldSpace = false;
        bowString.sharedMaterial = new Material(shader) { color = new Color(.9f, .95f, 1f, 1f) };
        bowString.widthMultiplier = .009f;
        bowString.positionCount = 2;
        bowString.SetPosition(0, new Vector3(0f, -.72f, 0f));
        bowString.SetPosition(1, new Vector3(0f, .72f, 0f));
        return root;
    }

    Transform BuildShieldProp()
    {
        Transform root = new GameObject("PlayerShield_Visual").transform;
        root.SetParent(transform, false);
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "ShieldBody";
        body.transform.SetParent(root, false);
        body.transform.localScale = new Vector3(.78f, .98f, .13f);
        Collider generatedCollider = body.GetComponent<Collider>();
        if (generatedCollider != null) Destroy(generatedCollider);
        Renderer renderer = body.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material shieldMaterial = new Material(shader) { color = shieldColor };
        shieldMaterial.SetFloat("_Metallic", .55f);
        shieldMaterial.SetFloat("_Smoothness", .35f);
        renderer.sharedMaterial = shieldMaterial;

        GameObject boss = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        boss.name = "ShieldBoss";
        boss.transform.SetParent(root, false);
        boss.transform.localPosition = new Vector3(0f, 0f, .1f);
        boss.transform.localScale = new Vector3(.24f, .24f, .12f);
        generatedCollider = boss.GetComponent<Collider>();
        if (generatedCollider != null) Destroy(generatedCollider);
        Material bossMaterial = new Material(shader) { color = new Color(.7f, .76f, .82f, 1f) };
        bossMaterial.SetFloat("_Metallic", .8f);
        bossMaterial.SetFloat("_Smoothness", .55f);
        boss.GetComponent<Renderer>().sharedMaterial = bossMaterial;
        return root;
    }

    void ApplyRollVisual()
    {
        if (visualRoot == null || controller == null) return;
        float u = controller.RollNormalizedTime;
        Transform actor = controller.transform;
        // 三段：0 ~ RollDiveEnd 蹲下前撲、RollDiveEnd ~ RollSpinEnd 貼地滾一圈、之後從蹲姿站起來
        float curlIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / RollDiveEnd));
        float curlOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - RollSpinEnd) / (1f - RollSpinEnd)));
        float curl = curlIn * curlOut;
        float spinT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - RollDiveEnd * .5f) / (RollSpinEnd - RollDiveEnd * .5f)));
        // 蹲低：比身體縮起來晚一點放開，起身時先蹲著、再站直
        float lowOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - RollSpinEnd - .04f) / (1f - RollSpinEnd - .04f)));
        float low = curlIn * lowOut;

        // Animator 暫停在站姿後，每幀先還原骨架，避免程序姿勢逐幀累加而扭曲。
        RestoreRollPose();

        // 縮成球之後，球心在胸腹附近（.82m）；整個人往下壓 RollDrop，球的底部才會貼著地面滾。
        // 前撲時先往前傾一點，再接著滾，不會一開始就從直立整個翻過去。
        float angle = RollLean * curlIn * (1f - spinT) + 360f * spinT;
        Quaternion spin = Quaternion.Euler(angle, 0f, 0f);
        Vector3 localBallCenter = Vector3.up * .82f;
        visualRoot.localRotation = visualRootRestRotation * spin;
        visualRoot.localPosition = visualRootRestPosition
            + visualRootRestRotation * localBallCenter
            - visualRoot.localRotation * localBallCenter
            - Vector3.up * (RollDrop * low);

        Quaternion restWorldRotation = visualRoot.parent != null
            ? visualRoot.parent.rotation * visualRootRestRotation
            : visualRootRestRotation;
        Quaternion rollDelta = visualRoot.rotation * Quaternion.Inverse(restWorldRotation);
        Vector3 rollRight = rollDelta * actor.right;
        Vector3 rollForward = rollDelta * actor.forward;
        Vector3 rollUp = rollDelta * actor.up;

        // 縮下巴、彎腰，雙膝收向胸口；手掌再抱向小腿，形成清楚的球狀輪廓。
        RotateBoneWorld(chest, rollRight, 48f * curl);
        RotateBoneWorld(head, rollRight, 30f * curl);

        // 起身：還沒站直前腳踩在地上、膝蓋彎著（蹲姿），不會一滾完就直挺挺站著
        float rise = (1f - curlOut) * low;
        if (rise > .001f && hips != null)
        {
            RotateBoneWorld(chest, actor.right, 22f * rise);
            Vector3 ground = actor.position + actor.up * .02f;
            Vector3 lf = ground + actor.forward * .12f - actor.right * .14f;
            Vector3 rf = ground - actor.forward * .18f + actor.right * .14f;
            if (leftFoot != null) SolveLimbWorld(leftUpperLeg, leftLowerLeg, leftFoot, Vector3.Lerp(leftFoot.position, lf, rise), actor.forward);
            if (rightFoot != null) SolveLimbWorld(rightUpperLeg, rightLowerLeg, rightFoot, Vector3.Lerp(rightFoot.position, rf, rise), actor.forward);
        }

        if (hips != null)
        {
            Vector3 leftFootGoal = hips.position + rollForward * .39f + rollUp * .10f - rollRight * .16f;
            Vector3 rightFootGoal = hips.position + rollForward * .39f + rollUp * .10f + rollRight * .16f;
            SolveLimbWorld(leftUpperLeg, leftLowerLeg, leftFoot,
                Vector3.Lerp(leftFoot != null ? leftFoot.position : leftFootGoal, leftFootGoal, curl), rollForward);
            SolveLimbWorld(rightUpperLeg, rightLowerLeg, rightFoot,
                Vector3.Lerp(rightFoot != null ? rightFoot.position : rightFootGoal, rightFootGoal, curl), rollForward);

            Vector3 leftHandGoal = leftLowerLeg != null
                ? leftLowerLeg.position + rollForward * .04f - rollRight * .05f
                : hips.position + rollForward * .36f + rollUp * .32f - rollRight * .18f;
            Vector3 rightHandGoal = rightLowerLeg != null
                ? rightLowerLeg.position + rollForward * .04f + rollRight * .05f
                : hips.position + rollForward * .36f + rollUp * .32f + rollRight * .18f;
            SolveLimbWorld(leftUpperArm, leftForearm, leftHand,
                Vector3.Lerp(leftHand != null ? leftHand.position : leftHandGoal, leftHandGoal, curl), -rollRight + rollForward * .2f);
            SolveLimbWorld(rightUpperArm, rightForearm, rightHand,
                Vector3.Lerp(rightHand != null ? rightHand.position : rightHandGoal, rightHandGoal, curl), rollRight + rollForward * .2f);
        }
    }

    void CaptureRollPose()
    {
        Transform[] bones =
        {
            hips, chest, head,
            leftUpperArm, leftForearm, leftHand,
            rightUpperArm, rightForearm, rightHand,
            leftUpperLeg, leftLowerLeg, leftFoot,
            rightUpperLeg, rightLowerLeg, rightFoot
        };
        rollPose = new BoneRestPose[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            rollPose[i] = new BoneRestPose
            {
                bone = bone,
                localPosition = bone != null ? bone.localPosition : Vector3.zero,
                localRotation = bone != null ? bone.localRotation : Quaternion.identity
            };
        }
    }

    void RestoreRollPose()
    {
        if (rollPose == null) return;
        for (int i = 0; i < rollPose.Length; i++)
        {
            BoneRestPose pose = rollPose[i];
            if (pose.bone == null) continue;
            pose.bone.localPosition = pose.localPosition;
            pose.bone.localRotation = pose.localRotation;
        }
    }

    /// <summary>
    /// 把腳尖轉到朝角色正前方（只繞垂直軸轉，腳底仍貼地）。
    /// 腳骨的哪一軸是腳尖不一定，這裡挑水平方向最接近前方的那一軸當腳尖
    /// </summary>
    /// <summary>髖部要再往下降多少，這隻腳（膝蓋留一點彎）才踩得到 goal；踩得到就回傳 0</summary>
    static float LegReachDrop(Transform upper, Transform lower, Transform foot, Vector3 goal, Vector3 up)
    {
        if (upper == null || lower == null || foot == null) return 0f;
        float reach = (Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position)) * .96f;
        Vector3 d = goal - upper.position;
        float below = -Vector3.Dot(d, up);                        // 腳在髖關節下方多遠
        float side = Vector3.ProjectOnPlane(d, up).magnitude;     // 水平離多遠
        if (side >= reach) return 0f;                             // 水平就已經伸不到：降也沒用
        float maxBelow = Mathf.Sqrt(reach * reach - side * side);
        return Mathf.Max(0f, below - maxBelow);
    }

    static void PointToesForward(Transform foot, Transform toes, Transform actor, float weight)
    {
        if (foot == null || weight <= 0f) return;
        Vector3 up = actor.up;
        Vector3 forward = Vector3.ProjectOnPlane(actor.forward, up).normalized;
        if (toes != null)
        {
            Vector3 dir = Vector3.ProjectOnPlane(toes.position - foot.position, up);
            if (dir.sqrMagnitude > .0001f)
            {
                foot.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(dir, forward, up) * weight, up) * foot.rotation;
                return;
            }
        }
        Vector3 toe = Vector3.zero;
        float best = -1f;
        Vector3[] axes = { foot.forward, -foot.forward, foot.up, -foot.up, foot.right, -foot.right };
        foreach (Vector3 a in axes)
        {
            Vector3 flat = Vector3.ProjectOnPlane(a, up);
            if (flat.sqrMagnitude < .09f) continue; // 幾乎垂直的軸不是腳尖
            float d = Vector3.Dot(flat.normalized, forward);
            if (d > best) { best = d; toe = flat.normalized; }
        }
        if (best < 0f) return;
        float angle = Vector3.SignedAngle(toe, forward, up);
        foot.rotation = Quaternion.AngleAxis(angle * weight, up) * foot.rotation;
    }

    static void RotateBoneWorld(Transform bone, Vector3 axis, float degrees)
    {
        if (bone != null && axis.sqrMagnitude > .0001f)
            bone.rotation = Quaternion.AngleAxis(degrees, axis.normalized) * bone.rotation;
    }

    static void SolveLimbWorld(Transform upper, Transform lower, Transform end, Vector3 goal, Vector3 poleDirection)
    {
        if (upper == null || lower == null || end == null) return;
        Vector3 root = upper.position;
        Vector3 toGoal = goal - root;
        if (toGoal.sqrMagnitude < .0001f) return;

        float upperLength = Vector3.Distance(root, lower.position);
        float lowerLength = Vector3.Distance(lower.position, end.position);
        float distance = Mathf.Clamp(toGoal.magnitude, .01f, upperLength + lowerLength - .001f);
        Vector3 direction = toGoal.normalized;
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float bendAmount = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(poleDirection, direction).normalized;
        if (bend.sqrMagnitude < .0001f) bend = Vector3.ProjectOnPlane(Vector3.up, direction).normalized;
        Vector3 joint = root + direction * along + bend * bendAmount;

        upper.rotation = Quaternion.FromToRotation(lower.position - root, joint - root) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, goal - lower.position) * lower.rotation;
    }

    SlashPath CurrentSlash => slashSide < 0 ? AirSlash : SlashPaths[slashSide % SlashPaths.Length];

    // ---------------- 腳貼地 ----------------
    // 角色控制器的膠囊底部是圓的、還有 skin width，模型在平地上就浮了約 0.15 m，站在斜坡或沙丘上更明顯。
    // 每隻腳往下打射線找地面：髖部往下降到兩隻腳都踩得到地面，高的那隻腳用 IK 彎膝蓋踩上去。
    // 動畫裡抬起來的腳（走路、跑步）會保留抬起的高度，只修正「踩在地上的那一層」
    const float FootGroundMaxDrop = .45f;   // 髖部最多往下降多少 (m)
    const float FootGroundMaxRaise = .15f;  // 模型陷進地面時最多往上提多少 (m)
    const float FootToeClearance = .03f;    // 腳趾骨離鞋底多高 (m)
    const float FootGroundSpeed = 2.5f;     // 髖部升降的速度 (m/s)，避免一顛一顛
    const float FootIkFullSpeed = .3f, FootIkOffSpeed = 1.6f; // 移動速度 (m/s) 低於前者：腳完全用 IK 踩地；高於後者：完全照動畫
    const float FootGroundRunSpeed = .6f;   // 跑步時髖部升降的速度 (m/s)：慢一點，濾掉每一步的起伏
    const float FootSoleRecover = .12f;     // 記住的腳底高度每秒往上回升多少 (m)
    float soleHeight = float.NaN;
    float groundDrop;
    readonly RaycastHit[] groundHits = new RaycastHit[8];

    void GroundFeet()
    {
        if (controller == null || hips == null || leftFoot == null || rightFoot == null) return;
        Transform actor = controller.transform;
        if (!controller.IsGrounded || controller.IsRolling)
        {
            groundDrop = 0f;
            return;
        }
        Vector3 up = actor.up;
        // 腳底的位置：有腳趾骨就用腳趾骨（比較接近鞋底），沒有就用腳踝
        Vector3 leftSole = leftToes != null ? leftToes.position : leftFoot.position;
        Vector3 rightSole = rightToes != null ? rightToes.position : rightFoot.position;
        float leftHeight = Vector3.Dot(leftSole - actor.position, up);
        float rightHeight = Vector3.Dot(rightSole - actor.position, up);
        float planted = Mathf.Min(leftHeight, rightHeight);
        // 這隻腳在動畫裡抬起多高（比較低的那隻腳當作踩在地上）
        float leftLift = leftHeight - planted, rightLift = rightHeight - planted;

        bool hitLeft = GroundBelow(leftSole, actor, out float leftGround);
        bool hitRight = GroundBelow(rightSole, actor, out float rightGround);
        // 浮多高 = 踩地那一層的腳底 - 地面（正 = 浮在半空、負 = 陷進地面）
        float leftGap = hitLeft ? leftHeight - leftLift - FootToeClearance - leftGround : 0f;
        float rightGap = hitRight ? rightHeight - rightLift - FootToeClearance - rightGround : 0f;
        if (!hitLeft) leftGround = rightGround;
        if (!hitRight) rightGround = leftGround;
        if (!hitLeft) leftGap = rightGap;
        if (!hitRight) rightGap = leftGap;
        if (!hitLeft && !hitRight) { groundDrop = Mathf.MoveTowards(groundDrop, 0f, Time.deltaTime * FootGroundSpeed); return; }
        leftGap = Mathf.Clamp(leftGap, -FootGroundMaxRaise, FootGroundMaxDrop);
        rightGap = Mathf.Clamp(rightGap, -FootGroundMaxRaise, FootGroundMaxDrop);

        // 站著（或慢走）才用 IK 讓兩隻腳各自踩到高低不同的地面；跑起來就只把整個身體往下移，
        // 腳的動作完全照跑步動畫，膝蓋才不會被 IK 扳得左右亂彎
        float speed = controller.CurrentSpeed;
        float plantWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FootIkFullSpeed, FootIkOffSpeed, speed));
        // 跑步時兩隻腳常常都在空中：記住最近「踩到地那一刻」腳底的高度（往下立刻更新、往上慢慢回升），
        // 用它來算身體要降多少，身體才不會跟著每一步一跳一跳
        if (Time.deltaTime > 0f)
            soleHeight = float.IsNaN(soleHeight) ? planted : Mathf.Min(planted, soleHeight + Time.deltaTime * FootSoleRecover);
        float runGap = float.IsNaN(soleHeight) ? (leftGap + rightGap) * .5f
            : Mathf.Clamp(soleHeight - FootToeClearance - (leftGround + rightGround) * .5f, -FootGroundMaxRaise, FootGroundMaxDrop);
        // 站著：髖部降到比較低的地面也踩得到（斜坡的下坡腳）
        float target = Mathf.Lerp(runGap, Mathf.Max(leftGap, rightGap), plantWeight);
        groundDrop = Mathf.MoveTowards(groundDrop, target, Time.deltaTime * Mathf.Lerp(FootGroundRunSpeed, FootGroundSpeed, plantWeight));

        // 髖部往下移 = 整個身體（連腳）一起往下移
        Vector3 leftAnim = leftFoot.position, rightAnim = rightFoot.position;
        Quaternion leftRot = leftFoot.rotation, rightRot = rightFoot.rotation;
        hips.position -= up * groundDrop;
        if (plantWeight <= .001f) return;
        // 每隻腳再各自補「它底下的地面」和「身體降下去的量」差多少
        Vector3 leftGoal = Vector3.Lerp(leftFoot.position, leftAnim - up * leftGap, plantWeight);
        Vector3 rightGoal = Vector3.Lerp(rightFoot.position, rightAnim - up * rightGap, plantWeight);
        SolveLimbWorld(leftUpperLeg, leftLowerLeg, leftFoot, leftGoal, actor.forward - actor.right * .2f);
        SolveLimbWorld(rightUpperLeg, rightLowerLeg, rightFoot, rightGoal, actor.forward + actor.right * .2f);
        leftFoot.rotation = leftRot;
        rightFoot.rotation = rightRot;
    }

    /// <summary>腳正下方地面的高度（相對角色位置，沿角色上方量）</summary>
    bool GroundBelow(Vector3 sole, Transform actor, out float height)
    {
        height = 0f;
        Vector3 up = actor.up;
        int count = Physics.RaycastNonAlloc(sole + up * .8f, -up, groundHits, 2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var h = groundHits[i];
            if (h.collider.transform == actor || h.collider.transform.IsChildOf(actor)) continue;
            if (h.distance < best) { best = h.distance; height = Vector3.Dot(h.point - actor.position, up); }
        }
        return best < float.MaxValue;
    }

    void ApplyShortKesaSlash()
    {
        float age = Time.time - slashStartedAt;
        var path = CurrentSlash;
        if (age < 0f || age > path.Total) return;
        // 抬手（蓄力）→ 揮 → 揮到底停住 hold 秒（頓一下，最後一刀最久）→ 收刀
        float prep = path.Prepare;
        // 跳劈：揮到一半停住（把時間往後推），一路停到落地才把後半刀砍完
        if (combat != null && combat.IsPlunging)
        {
            float freezeAt = prep + path.Cut * .5f;
            if (age > freezeAt)
            {
                slashStartedAt += age - freezeAt;
                age = freezeAt;
            }
            if (weaponPresenter != null) weaponPresenter.DrawForSeconds(path.Total + .6f);
        }
        float prepare = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / prep));
        float cut = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - prep) / path.Cut));
        float recover = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - prep - path.Cut - path.hold) / SlashRecover));
        // 人在空中（空中輕砍 / 跳劈還沒落地）：不蹲、不把腳踩回地面
        bool airborne = path.airborne || (controller != null && !controller.IsGrounded);
        float weight = prepare * recover;
        Transform actor = controller != null ? controller.transform : transform;
        // 身體旋轉：抬手時腰和胸往起刀的那一側扭（蓄力），揮的時候一路扭到收刀那一側（甩身），收刀時回正。
        // 臀部轉一部分、胸口再多轉一部分，上半身合起來約 ±60°；揮到一半身體往前壓（挑斬是先壓低再後仰）
        float twist = Mathf.Lerp(path.twistFrom, path.twistTo, cut) * weight;

        // 腳踩住地面：先記下腳的位置，臀部轉動 / 蹲下之後再用 IK 把腳放回原位（膝蓋自然彎曲，腳不會在地上滑）
        Vector3 leftFootAt = leftFoot != null ? leftFoot.position : Vector3.zero;
        Vector3 rightFootAt = rightFoot != null ? rightFoot.position : Vector3.zero;
        Quaternion leftFootRot = leftFoot != null ? leftFoot.rotation : Quaternion.identity;
        Quaternion rightFootRot = rightFoot != null ? rightFoot.rotation : Quaternion.identity;
        // 頭的方向固定：身體怎麼扭、前壓，頭都一直看著前方
        Quaternion headRot = head != null ? head.rotation : Quaternion.identity;

        // 蹲低：左右橫斬整刀都蹲著（收刀才站起來）；劈斬抬手時先站高（墊腳），劈下去時一路蹲到底
        float crouch = path.crouch * weight * (path.lift > 0f ? cut : 1f);
        float lift = path.lift * prepare * (1f - cut);
        // 重心移到前腳：抬手時前腳先伸出去，身體和髖部只跟一點；揮下去時才整個壓到前腳上（往前、也往前腳那一側）
        float shift = weight * Mathf.Lerp(.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(cut * 1.4f)));
        Vector3 frontSide = path.leftFootForward ? -actor.right : actor.right;
        if (hips != null && !airborne)
            hips.position += actor.up * (lift - crouch)
                + actor.forward * (SlashWeightShift * shift) + frontSide * (SlashWeightSide * shift);

        // 髖部帶頭：比胸口早一點轉過去（右 → 左 / 左 → 右），胸口再跟上
        float hipCut = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(cut * 1.3f));
        float hipTwist = Mathf.Lerp(path.twistFrom, path.twistTo, hipCut) * weight;
        RotateBoneWorld(hips, actor.up, SlashHipTwist * hipTwist);
        RotateBoneWorld(chest, actor.up, SlashChestTwist * twist);
        float pitch = Mathf.Lerp(path.pitchFrom, path.pitchTo, cut) + 10f * Mathf.Sin(cut * Mathf.PI);
        // 前壓要繞「扭過去之後」的胸口左右軸，不然身體扭 90° 時前壓會變成往側邊倒
        Vector3 pitchAxis = Quaternion.AngleAxis(SlashHipTwist * hipTwist + SlashChestTwist * twist, actor.up) * actor.right;
        RotateBoneWorld(chest, pitchAxis, pitch * weight);
        if (head != null) head.rotation = headRot;

        // 跳劈：整個刀路平面往斜的方向轉（蓄力中跳起來才慢慢轉過去，不會突然跳一下）
        if (combat != null && combat.IsPlunging) plungeSlash = true;
        plungeBlend = Mathf.MoveTowards(plungeBlend, plungeSlash ? 1f : 0f, Time.deltaTime * 5f);
        Quaternion tilt = Quaternion.AngleAxis(-PlungeTilt * plungeBlend, Vector3.forward);
        Vector3 Local(Vector3 v)
        {
            v = tilt * v;
            return actor.right * v.x + actor.up * v.y + actor.forward * v.z;
        }

        // 揮刀時兩腳一前一後（前腳往前踏、後腳往後撤），蹲得比較穩
        float leftStep = path.leftFootForward ? SlashStanceForward : -SlashStanceBack;
        float rightStep = path.leftFootForward ? -SlashStanceBack : SlashStanceForward;
        leftFootAt += actor.forward * (leftStep * weight);
        rightFootAt += actor.forward * (rightStep * weight);
        // 整體壓低：先多蹲一點，腳跨太開、腿伸到底還碰不到地面時，再把髖部降到兩隻腳都踩得到為止
        if (hips != null && !airborne)
        {
            float drop = SlashBaseDrop * weight;
            hips.position -= actor.up * drop;
            float needed = Mathf.Max(
                LegReachDrop(leftUpperLeg, leftLowerLeg, leftFoot, leftFootAt, actor.up),
                LegReachDrop(rightUpperLeg, rightLowerLeg, rightFoot, rightFootAt, actor.up));
            if (needed > 0f) hips.position -= actor.up * needed;
        }
        if (leftFoot != null && !airborne)
        {
            SolveLimbWorld(leftUpperLeg, leftLowerLeg, leftFoot, leftFootAt, actor.forward - actor.right * .2f);
            leftFoot.rotation = leftFootRot;
            if (path.leftFootForward) PointToesForward(leftFoot, leftToes, actor, Mathf.Clamp01(weight * 1.5f));
        }
        if (rightFoot != null && !airborne)
        {
            SolveLimbWorld(rightUpperLeg, rightLowerLeg, rightFoot, rightFootAt, actor.forward + actor.right * .2f);
            rightFoot.rotation = rightFootRot;
            if (!path.leftFootForward) PointToesForward(rightFoot, rightToes, actor, Mathf.Clamp01(weight * 1.5f));
        }

        // 匯入骨架的 local 軸向和 Unity 人形骨架不一定一致。改用角色世界前方作為斬擊平面，
        // 可避免同一組 Euler 角在這個貓戰士模型上變成朝背後揮刀。
        if (rightUpperArm != null)
        {
            Vector3 shoulder = rightUpperArm.position;
            // 拉刀：抬手時把刀再往後拉（windBack），揮的前半段才拉回來砍出去
            float wind = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(cut / .45f));
            // 刀路是一段往前鼓出去的弧線（arc），劍尖畫出半圓
            Vector3 slashGoal = shoulder + Local(Vector3.Lerp(path.handFrom + path.windBack * wind, path.handTo, cut))
                + actor.forward * (Mathf.Sin(cut * Mathf.PI) * path.arc);
            Vector3 goal = Vector3.Lerp(rightHand.position, slashGoal, weight);
            // Preserve the animation's wrist orientation while solving arm positions.
            // Otherwise both IK rotations accumulate into the hand and roll the blade.
            Quaternion wrist = rightHand.rotation;
            SolveArmWorld(rightUpperArm, rightForearm, rightHand, goal, Local(path.pole));
            rightHand.rotation = wrist;
            if (weaponPresenter != null)
            {
                Vector3 blade = Local(Vector3.Slerp(Vector3.Slerp(path.bladeFrom, path.bladeTo, cut), path.windBlade, wind));
                Vector3 edge = default;
                if (path.edgeFollowsSwing || path.edgeFlat > 0f)
                {
                    // 刀鋒朝劍尖移動的方向；edgeFlat 再決定要從「朝上」往這個方向放橫多少
                    Vector3 axis = Vector3.Cross(Local(path.bladeFrom), Local(path.bladeTo));
                    Vector3 swingEdge = axis.sqrMagnitude > .0001f ? Vector3.Cross(axis.normalized, blade) : actor.up;
                    Vector3 baseEdge = path.edgeFollowsSwing ? swingEdge : actor.up;
                    Vector3 flatEdge = Vector3.Cross(blade, actor.up);
                    if (Vector3.Dot(flatEdge, Local(path.bladeTo - path.bladeFrom)) < 0f) flatEdge = -flatEdge; // 放橫時刀鋒朝揮的那一側
                    edge = flatEdge.sqrMagnitude > .0001f
                        ? Vector3.Slerp(baseEdge.normalized, flatEdge.normalized, path.edgeFlat)
                        : baseEdge;
                    // 這把刀模型的刀鋒在劍身「上方」的反面，整個翻過來
                    if (FlipBladeEdge) edge = -edge;
                }
                weaponPresenter.AimSlashBlade(blade, edge);
            }

            if (leftUpperArm != null)
            {
                // 單手刀：沒拿刀的左手跟著揮刀往外伸出去平衡（往刀的反方向甩）；雙手刀：左手握在右手下面（劍柄尾端）
                Vector3 bladeDir = weaponPresenter != null && weaponPresenter.BladeTip != null && weaponPresenter.BladePivot != null
                    ? (weaponPresenter.BladeTip.position - weaponPresenter.BladePivot.position).normalized
                    : actor.forward;
                Vector3 supportGoal = path.twoHanded
                    ? rightHand.position - bladeDir * SlashGripGap
                    : leftUpperArm.position + Local(Vector3.Lerp(path.offFrom, path.offTo, cut));
                supportGoal = Vector3.Lerp(leftHand.position, supportGoal, weight);
                SolveArmWorld(leftUpperArm, leftForearm, leftHand, supportGoal, -actor.right - actor.up * .5f);
            }
        }
    }

    static void SolveArmWorld(Transform upper, Transform forearm, Transform hand, Vector3 goal, Vector3 poleDirection)
    {
        if (upper == null || forearm == null || hand == null) return;
        Vector3 root = upper.position;
        Vector3 toGoal = goal - root;
        if (toGoal.sqrMagnitude < .0001f) return;

        float upperLength = Vector3.Distance(root, forearm.position);
        float lowerLength = Vector3.Distance(forearm.position, hand.position);
        float distance = Mathf.Clamp(toGoal.magnitude, .01f, upperLength + lowerLength - .001f);
        Vector3 direction = toGoal.normalized;
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float bendAmount = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(poleDirection, direction).normalized;
        if (bend.sqrMagnitude < .0001f) bend = Vector3.ProjectOnPlane(Vector3.up, direction).normalized;
        Vector3 elbow = root + direction * along + bend * bendAmount;

        upper.rotation = Quaternion.FromToRotation(forearm.position - root, elbow - root) * upper.rotation;
        forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, goal - forearm.position) * forearm.rotation;
    }

    /// <summary>
    /// 播一個一次性的動作（喝藥水、二段跳…）。state = Animator 裡的狀態名稱，播完會自己回到 Idle；
    /// 找不到這個狀態就不播。hold = 這段時間內不要被待機小動作打斷
    /// </summary>
    public void PlayGesture(string state, float hold = 1.2f)
    {
        if (animator == null || health != null && health.IsDead) return;
        int hash = Animator.StringToHash(state);
        if (!animator.HasState(0, hash)) return;
        animator.CrossFadeInFixedTime(hash, 0.08f, 0, 0f);
        ScheduleNextIdle(hold);
    }

    public void PlayHitReaction()
    {
        if (animator == null) return;
        string state = HitStates[hitIndex++ % HitStates.Length];
        animator.CrossFadeInFixedTime(state, 0.07f, 0, 0f);
        ScheduleNextIdle(1.5f);
    }

    static readonly int DeathStateHash = Animator.StringToHash("Death");

    void HandleDeath()
    {
        if (animator != null)
        {
            // 翻滾中死掉：翻滾時 Animator 是暫停的（speed = 0），要先恢復，不然倒下動畫不會動
            RestoreRollVisualState();
            // 直接切到倒下動畫（不靠 Trigger 等轉換線），同一幀其他腳本切的動畫也蓋不掉它；
            // 找不到 Death 狀態時才退回用 Trigger
            if (animator.HasState(0, DeathStateHash))
                animator.CrossFadeInFixedTime(DeathStateHash, 0.1f, 0, 0f);
            else
                animator.SetTrigger(DieHash);
        }
        if (weaponPresenter != null) weaponPresenter.SheatheNow();
    }

    void HandleRespawn()
    {
        if (animator == null) return;
        animator.ResetTrigger(DieHash);
        animator.SetTrigger(RespawnHash);
        if (weaponPresenter != null) weaponPresenter.SheatheNow();
        if (health != null) previousHealth = health.Health;
        idleIndex = 0;
        ScheduleNextIdle();
    }

    /// <summary>播放已匯入但尚未由玩法自動觸發的狀態。</summary>
    public void PlayAnimation(string stateName, float transitionSeconds = 0.1f)
    {
        if (animator != null && !string.IsNullOrWhiteSpace(stateName))
            animator.CrossFadeInFixedTime(stateName, Mathf.Max(0f, transitionSeconds));
    }
}
