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
    [SerializeField] Vector2 idleChangeInterval = new Vector2(5.5f, 8.5f);

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
    Transform leftUpperLeg;
    Transform rightUpperLeg;

    void Awake()
    {
        ResolveReferences();
        if (health != null) previousHealth = health.Health;
        CacheHumanoidBones();
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
    }

    void Update()
    {
        if (animator == null) return;

        float normalizedSpeed = 0f;
        bool grounded = true;
        if (controller != null)
        {
            float referenceSpeed = Mathf.Max(0.01f, controller.sprintSpeed);
            normalizedSpeed = controller.CurrentSpeed / referenceSpeed;
            grounded = controller.IsGrounded;
            animator.SetFloat(SpeedHash, normalizedSpeed, 0.1f, Time.deltaTime);
            animator.SetBool(GroundedHash, grounded);
            animator.SetFloat(VerticalSpeedHash, controller.VerticalVelocity);
        }

        ForceLocomotionWhenMoving(normalizedSpeed, grounded);
        UpdateSprintJumpFeedback(grounded);
        UpdateIdleCycle(normalizedSpeed, grounded);

        if (health != null)
        {
            if (!health.IsDead && health.Health < previousHealth) PlayHitReaction();
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
        // 刀攻擊只輪替 slash / chop；Attack3 保留給未來的踢擊系統。
        animator.ResetTrigger(Attack1Hash);
        animator.ResetTrigger(Attack2Hash);
        // wait / look_around 沒有接 Attack Trigger 的 Animator 轉場，直接指定狀態
        // 才能保證休息中按左鍵也立刻出刀；連擊仍沿用 PlayerCombat 的同一套流程。
        string attackState = (comboIndex & 1) == 0 ? "Attack_1" : "Attack_2";
        animator.CrossFadeInFixedTime(attackState, 0.055f, 0, 0f);
        if (weaponPresenter != null) weaponPresenter.DrawForSeconds(0.95f);
        ScheduleNextIdle(2f);
    }

    void LateUpdate()
    {
        if (animator == null || !animator.isHuman) return;
        if (hips == null || chest == null) CacheHumanoidBones();

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
        leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
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
