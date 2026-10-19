using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 將既有玩家控制器狀態轉成純視覺 Animator 參數。
/// 不參與移動、碰撞、傷害或技能邏輯。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerVisualAnimator : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] ThirdPersonController controller;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerWeaponPresenter weaponPresenter;
    PlayerShieldBowAnimator shieldBow; // 盾牌格擋 / 盾擊 / 拉弓的動畫（自動加上）
    [SerializeField] Vector2 idleChangeInterval = new Vector2(5.5f, 8.5f);
    [Header("弓與盾（共用角色視覺）")]
    [SerializeField] Color bowWoodColor = new Color(0.32f, 0.14f, 0.055f, 1f);
    [SerializeField] Color shieldColor = new Color(0.12f, 0.24f, 0.38f, 1f);

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int GroundedHash = Animator.StringToHash("Grounded");
    static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
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
    int slashSide;
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
        shieldBow = GetComponent<PlayerShieldBowAnimator>();
        if (shieldBow == null) shieldBow = gameObject.AddComponent<PlayerShieldBowAnimator>();
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
        if (animator == null) return;

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
            animator.CrossFadeInFixedTime(airborne, 0.055f, 0, 0f);
            ScheduleNextIdle(1f);
        }
    }

    void UpdateSprintJumpFeedback(bool grounded)
    {
        if (controller == null) return;

        bool sprinting = controller.CurrentSpeed > controller.walkSpeed * 1.05f;
        bool jumpPressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (grounded && sprinting && jumpPressed)
        {
            pounceStartedAt = Time.unscaledTime;
            ThirdPersonCamera.ShakeMain(0.075f, 0.16f);
        }

        if (!grounded) landingVelocity = Mathf.Min(landingVelocity, controller.VerticalVelocity);
        if (!previousGrounded && grounded)
        {
            if (landingVelocity < -2.5f)
                ThirdPersonCamera.ShakeMain(Mathf.Clamp(-landingVelocity * 0.018f, 0.06f, 0.16f), 0.15f);
            landingVelocity = 0f;
        }
        previousGrounded = grounded;
    }

    void UpdateIdleCycle(float speed, bool grounded)
    {
        if (Time.unscaledTime - slashStartedAt < .3f || (controller != null && controller.IsRolling)) return;
        if (!grounded || speed > 0.035f || Time.unscaledTime < nextIdleChange) return;
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (current.IsName("Attack_1") || current.IsName("Attack_2") || current.IsName("Attack_3") ||
            current.IsName("Death") || current.IsName("Jump") || current.IsName("Fall"))
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
        // 自製短版袈裟斬：回到穩定站姿後用骨架疊加約 0.26 秒的核心斜切，
        // 不再把原本有長前搖／後搖的 slash、chop 動畫整段加速播完。
        animator.ResetTrigger(Attack1Hash);
        animator.ResetTrigger(Attack2Hash);
        animator.CrossFadeInFixedTime("Idle", 0.025f, 0, 0f);
        slashSide = comboIndex;
        slashStartedAt = Time.unscaledTime;
        if (weaponPresenter != null) weaponPresenter.DrawForSeconds(0.52f);
        ScheduleNextIdle(.7f);
    }

    void LateUpdate()
    {
        if (animator == null || !animator.isHuman) return;
        if (hips == null || chest == null) CacheHumanoidBones();

        if (controller != null && controller.IsRolling)
        {
            ApplyRollVisual();
            return;
        }

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
            combat.stepWindup[i] = .035f;
            combat.stepSwing[i] = .12f;
            combat.stepRecovery[i] = i == count - 1 ? .18f : .11f;
        }
        combat.chainAfter = .58f;
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
        bool showBow = combat.bowMode;
        bool showShield = combat.ShieldUp; // 盾擊或按住右鍵格擋時拿出盾牌

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
        float eased = Mathf.SmoothStep(0f, 1f, u);
        float curlIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / .16f));
        float curlOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - .78f) / .22f));
        float curl = curlIn * curlOut;

        // Animator 暫停在站姿後，每幀先還原骨架，避免程序姿勢逐幀累加而扭曲。
        RestoreRollPose();

        // 以胸腹附近作為旋轉中心，而不是讓直立角色繞腳底翻一整圈。
        Quaternion spin = Quaternion.Euler(360f * eased, 0f, 0f);
        Vector3 localBallCenter = Vector3.up * .82f;
        visualRoot.localRotation = visualRootRestRotation * spin;
        visualRoot.localPosition = visualRootRestPosition
            + visualRootRestRotation * localBallCenter
            - visualRoot.localRotation * localBallCenter
            + Vector3.up * (.05f * Mathf.Sin(u * Mathf.PI));

        Transform actor = controller.transform;
        Quaternion restWorldRotation = visualRoot.parent != null
            ? visualRoot.parent.rotation * visualRootRestRotation
            : visualRootRestRotation;
        Quaternion rollDelta = visualRoot.rotation * Quaternion.Inverse(restWorldRotation);
        Vector3 rollRight = rollDelta * actor.right;
        Vector3 rollForward = rollDelta * actor.forward;
        Vector3 rollUp = rollDelta * actor.up;

        // 縮下巴、彎腰，雙膝收向胸口；手掌再抱向小腿，形成清楚的球狀輪廓。
        RotateBoneWorld(chest, rollRight, 38f * curl);
        RotateBoneWorld(head, rollRight, 24f * curl);

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

    void ApplyShortKesaSlash()
    {
        float age = Time.unscaledTime - slashStartedAt;
        if (age < 0f || age > .26f) return;
        float side = (slashSide & 1) == 0 ? 1f : -1f;
        float cut = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - .025f) / .105f));
        float recover = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - .14f) / .12f));
        float weight = Mathf.Min(cut, recover);
        Transform actor = controller != null ? controller.transform : transform;
        if (chest != null)
            chest.localRotation *= Quaternion.Euler(-7f * weight, 36f * side * (cut - .28f) * weight, 12f * side * weight);

        // 匯入骨架的 local 軸向和 Unity 人形骨架不一定一致。改用角色世界前方作為斬擊平面，
        // 可避免同一組 Euler 角在這個貓戰士模型上變成朝背後揮刀。
        if (rightUpperArm != null)
        {
            Vector3 shoulder = rightUpperArm.position;
            Vector3 high = shoulder + actor.forward * .58f + actor.up * .34f + actor.right * (.42f * side);
            Vector3 low = shoulder + actor.forward * .78f - actor.up * .48f - actor.right * (.40f * side);
            Vector3 neutral = shoulder + actor.forward * .28f - actor.up * .10f + actor.right * (.18f * side);
            Vector3 slashGoal = Vector3.Lerp(high, low, cut);
            Vector3 goal = Vector3.Lerp(neutral, slashGoal, weight);
            SolveArmWorld(rightUpperArm, rightForearm, rightHand, goal, actor.right * side + actor.up * .15f);

            if (leftUpperArm != null)
            {
                Vector3 supportGoal = goal - actor.right * (.14f * side) - actor.forward * .06f;
                SolveArmWorld(leftUpperArm, leftForearm, leftHand, supportGoal, -actor.right * side + actor.up * .1f);
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

    public void PlayHitReaction()
    {
        if (animator == null) return;
        string state = HitStates[hitIndex++ % HitStates.Length];
        animator.CrossFadeInFixedTime(state, 0.07f, 0, 0f);
        ScheduleNextIdle(1.5f);
    }

    void HandleDeath()
    {
        if (animator != null) animator.SetTrigger(DieHash);
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
