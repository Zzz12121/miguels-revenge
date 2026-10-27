using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人稱角色移動（類似原神 / 艾爾登法環的自由移動）
/// - WASD / 方向鍵：相對鏡頭方向移動，角色自動轉向移動方向
/// - 左 Shift：衝刺
/// - 空白鍵：跳躍（含 Coyote Time 與跳躍緩衝，手感較好）
/// 需要搭配 CharacterController。
///
/// 小怪也用這個元件移動（勾選 aiControlled），由 EnemyAI 呼叫 SetAIInput 給方向，
/// 所以小怪的走路、衝刺、跳躍、重力手感都跟玩家完全一樣。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("移動")]
    [Tooltip("走路速度 (m/s)")]
    public float walkSpeed = 4.5f;
    [Tooltip("按住左 Shift 的衝刺速度 (m/s)")]
    public float sprintSpeed = 8f;
    [Tooltip("加速 / 減速度，越大越靈敏 (m/s²)")]
    public float acceleration = 40f;
    [Tooltip("在空中時的操控比例 (0 = 空中完全無法改變方向, 1 = 跟地面一樣)")]
    [Range(0f, 1f)] public float airControl = 0.5f;
    [Tooltip("轉向平滑時間，越小轉身越快")]
    public float rotationSmoothTime = 0.08f;

    [Header("跳躍與重力")]
    [Tooltip("跳躍高度 (m)")]
    public float jumpHeight = 1.6f;
    [Tooltip("重力，比真實世界 (-9.81) 大一點跳躍比較不飄")]
    public float gravity = -25f;
    [Tooltip("離開平台後仍可起跳的寬限時間 (秒)")]
    public float coyoteTime = 0.12f;
    [Tooltip("落地前提早按跳躍仍會生效的時間 (秒)")]
    public float jumpBufferTime = 0.12f;
    [Tooltip("連續斜坡 / 下坡：腳下離地這個距離內就直接貼回地面，不會一直變成騰空、一頓一頓的 (m)")]
    public float groundSnapDistance = 0.6f;
    // 動作鎖定：對話、喝藥水時不能跳、翻滾、攻擊、用技能（可以多個來源同時鎖，全部解開才恢復）
    int actionLocks;
    float suppressJumpUntil = float.NegativeInfinity;
    /// <summary>不能跳躍 / 翻滾 / 攻擊 / 使用道具與技能</summary>
    public bool ActionsLocked => actionLocks > 0;
    public void LockActions() => actionLocks++;
    public void UnlockActions() => actionLocks = Mathf.Max(0, actionLocks - 1);
    /// <summary>接下來 seconds 秒內按空白鍵不會跳（關掉對話框的那一下不要順便跳起來）</summary>
    public void SuppressJump(float seconds) => suppressJumpUntil = Time.unscaledTime + seconds;

    PlayerStamina stamina;
    /// <summary>玩家的精力條（PlayerHealth 會自動加上；小怪沒有）</summary>
    PlayerStamina Stamina => stamina != null ? stamina : stamina = GetComponent<PlayerStamina>();

    [Tooltip("二段跳（能力植物「一步登天」會打開）：在空中可以再跳一次")]
    public bool doubleJumpEnabled = false;
    [Tooltip("二段跳的高度 (m)")]
    public float doubleJumpHeight = 1.8f;
    bool airJumpUsed;
    /// <summary>二段跳的瞬間通知外面（特效 / 動畫用）</summary>
    public event System.Action OnDoubleJump;

    [Header("狀態")]
    [Tooltip("關閉時角色不吃玩家輸入（死亡、過場時使用），但仍受重力影響")]
    public bool inputEnabled = true;
    [Tooltip("移動速度倍率（攻擊時會暫時降低）")]
    public float speedMultiplier = 1f;
    [Tooltip("鎖定面向，不會自動轉向移動方向（攻擊時使用）")]
    public bool lockRotation = false;

    [Header("翻滾閃避（V；靶場預設 C，可按 K 設定）")]
    [Min(0.2f)] public float rollDuration = 0.48f;
    [Min(0.5f)] public float rollDistance = 5.2f;
    [Min(0f)] public float rollCooldown = 0.18f;
    public bool IsRolling { get; private set; }
    public float RollNormalizedTime => IsRolling ? Mathf.Clamp01((Time.unscaledTime - rollStartedAt) / Mathf.Max(.01f, rollDuration)) : 0f;
    public bool IsControlImmune => IsRolling;
    public event System.Action OnRollStarted;
    public event System.Action OnRollEnded;

    [Header("參考")]
    [Tooltip("用來決定「前方」的鏡頭，留空會自動抓 Main Camera")]
    public Transform cameraTransform;

    [Header("推石頭")]
    [Tooltip("頂著可以推的岩石（ArenaRock）走時會把它推動，自己的速度會變慢。小怪（AI 控制）不會推")]
    public bool canPushRocks = true;

    float pushFactor = 1f;
    float pushUntil = float.NegativeInfinity;
    /// <summary>正在推石頭（之後做推的動畫時可以讀這個值）</summary>
    public bool IsPushing => Time.time < pushUntil;

    [Header("AI 控制（小怪用）")]
    [Tooltip("勾選後不讀鍵盤，改由程式（EnemyAI）呼叫 SetAIInput 控制")]
    public bool aiControlled = false;

    Vector3 aiMove;
    bool aiSprint;
    bool aiJump;

    readonly Dictionary<int, float> temporarySpeedMultipliers = new Dictionary<int, float>();
    int nextTemporarySpeedEffectId;
    int temporarySpeedEffectGeneration;
    float temporarySpeedMultiplier = 1f;
    // 加速效果（能力植物：翻滾後加速、惡魔型態）：和減速分開算，同時有好幾個時取最快的
    readonly Dictionary<int, float> temporarySpeedBoosts = new Dictionary<int, float>();
    float temporarySpeedBoost = 1f;

    CharacterController controller;
    Vector3 horizontalVelocity;

    [Header("腳步聲")]
    [Tooltip("腳步聲（不填 = Kenney RPG Audio 的 footstep00~09）")]
    public AudioClip[] footstepSounds;
    [Range(0f, 1f)] public float footstepVolume = 0.35f;
    [Tooltip("AI 控制（小怪）時腳步聲的音量倍率。小怪數量多，要比玩家小聲很多才不會吵")]
    [Range(0f, 1f)] public float aiFootstepScale = 0.2f;
    [Tooltip("走幾公尺響一次")]
    public float stepDistance = 1.9f;
    float stepTravel;
    bool wasGrounded = true;
    float fallSpeed;

    static readonly string[] DefaultSteps =
    {
        "SFX/RPG/footstep00", "SFX/RPG/footstep01", "SFX/RPG/footstep02", "SFX/RPG/footstep03", "SFX/RPG/footstep04",
        "SFX/RPG/footstep05", "SFX/RPG/footstep06", "SFX/RPG/footstep07", "SFX/RPG/footstep08", "SFX/RPG/footstep09",
    };

    void Footstep(float volume)
    {
        // 小怪的腳步聲小一點、低一點，不然一群小怪會很吵
        float v = volume * (aiControlled ? aiFootstepScale : 1f);
        float pitch = (aiControlled ? 0.85f : 1f) * Random.Range(0.9f, 1.1f);
        GameAudio.PlayAt(GameAudio.Pick(footstepSounds, DefaultSteps), transform.position, v, pitch, 1f, 25f);
    }

    void UpdateFootsteps(float dt)
    {
        bool grounded = controller.isGrounded;
        if (grounded)
        {
            if (!wasGrounded && fallSpeed < -6f) Footstep(footstepVolume * 1.4f); // 落地
            stepTravel += horizontalVelocity.magnitude * dt;
            if (stepTravel >= stepDistance)
            {
                stepTravel = 0f;
                Footstep(footstepVolume);
            }
        }
        else fallSpeed = verticalVelocity;
        wasGrounded = grounded;
    }
    float verticalVelocity;
    float rotationVelocity;
    float lastGroundedTime = float.NegativeInfinity;
    float lastJumpPressedTime = float.NegativeInfinity;
    Vector3 rollDirection;
    float rollStartedAt;
    bool rollCommitted;
    bool rollPending;
    float rollTravel;
    float nextRollTime;

    // 之後做動畫 / UI 時可以讀這些值
    public bool IsGrounded => controller != null && controller.isGrounded && verticalVelocity <= 0f;
    public float CurrentSpeed => horizontalVelocity.magnitude;
    public float VerticalVelocity => verticalVelocity;

    // 受擊僵直：這段時間內不吃移動 / 跳躍輸入（擊退照樣有效）
    float stunUntil = float.NegativeInfinity;
    /// <summary>正在受擊僵直</summary>
    public bool IsStunned => Time.time < stunUntil;
    /// <summary>僵直 seconds 秒（玩家受傷時使用）</summary>
    public void Stun(float seconds) => stunUntil = Mathf.Max(stunUntil, Time.time + seconds);

    /// <summary>給角色一個瞬間的推力（受擊擊退用）</summary>
    public void AddImpulse(Vector3 impulse)
    {
        if (IsControlImmune) return;
        horizontalVelocity += new Vector3(impulse.x, 0f, impulse.z);
        if (impulse.y > 0f) verticalVelocity = Mathf.Max(verticalVelocity, impulse.y);
    }

    /// <summary>
    /// 暫時套用額外移動倍率。效果由玩家自己計時，來源物件即使被銷毀也能正常解除。
    /// 多個效果重疊時採用最慢的倍率，且不會覆蓋攻擊使用的 speedMultiplier。
    /// </summary>
    public void ApplyTemporarySpeedMultiplier(float multiplier, float duration)
    {
        if (duration <= 0f || IsControlImmune) return;
        StartCoroutine(TemporarySpeedEffectRoutine(Mathf.Clamp01(multiplier), duration));
    }

    // A source-owned lease can be cancelled without clearing teammates' other buffs.
    public int AddSpeedMultiplierLease(float multiplier)
    {
        if(IsControlImmune)return 0;
        int id=++nextTemporarySpeedEffectId;
        temporarySpeedMultipliers[id]=Mathf.Clamp01(multiplier);
        RecalculateTemporarySpeedMultiplier();return id;
    }
    public void RemoveSpeedMultiplierLease(int id)
    {temporarySpeedMultipliers.Remove(id);RecalculateTemporarySpeedMultiplier();}

    IEnumerator TemporarySpeedEffectRoutine(float multiplier, float duration)
    {
        int id = ++nextTemporarySpeedEffectId;
        int generation = temporarySpeedEffectGeneration;
        temporarySpeedMultipliers[id] = multiplier;
        RecalculateTemporarySpeedMultiplier();

        yield return new WaitForSeconds(duration);

        if (generation != temporarySpeedEffectGeneration) yield break;
        temporarySpeedMultipliers.Remove(id);
        RecalculateTemporarySpeedMultiplier();
    }

    /// <summary>
    /// 暫時加速（multiplier > 1，例如 1.4 = 快 40%）。跟 ApplyTemporarySpeedMultiplier 的減速分開計算、互相相乘，
    /// 好幾個加速重疊時取最快的。翻滾中也可以套用（翻滾結束後才會用到）。
    /// </summary>
    public void ApplyTemporarySpeedBoost(float multiplier, float duration)
    {
        if (duration <= 0f || multiplier <= 1f) return;
        StartCoroutine(TemporarySpeedBoostRoutine(multiplier, duration));
    }

    IEnumerator TemporarySpeedBoostRoutine(float multiplier, float duration)
    {
        int id = ++nextTemporarySpeedEffectId;
        int generation = temporarySpeedEffectGeneration;
        temporarySpeedBoosts[id] = multiplier;
        RecalculateTemporarySpeedMultiplier();

        yield return new WaitForSeconds(duration);

        if (generation != temporarySpeedEffectGeneration) yield break;
        temporarySpeedBoosts.Remove(id);
        RecalculateTemporarySpeedMultiplier();
    }

    void RecalculateTemporarySpeedMultiplier()
    {
        temporarySpeedBoost = 1f;
        foreach (float boost in temporarySpeedBoosts.Values)
            temporarySpeedBoost = Mathf.Max(temporarySpeedBoost, boost);
        temporarySpeedMultiplier = 1f;
        foreach (float multiplier in temporarySpeedMultipliers.Values)
            temporarySpeedMultiplier = Mathf.Min(temporarySpeedMultiplier, multiplier);
    }

    public void ClearTemporarySpeedEffects()
    {
        temporarySpeedEffectGeneration++;
        temporarySpeedMultipliers.Clear();
        temporarySpeedMultiplier = 1f;
        temporarySpeedBoosts.Clear();
        temporarySpeedBoost = 1f;
    }

    void OnDisable()
    {
        EndRoll();
        ClearTemporarySpeedEffects();
    }

    void EndRoll()
    {
        if (!IsRolling && !rollPending) return;
        bool committed=rollCommitted;rollCommitted=false;
        IsRolling = false;rollPending=false;
        horizontalVelocity = Vector3.zero;
        if(committed)OnRollEnded?.Invoke();
    }

    /// <summary>立刻轉向某個方向（攻擊時面向鏡頭前方）</summary>
    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(direction);
        rotationVelocity = 0f;
    }

    /// <summary>AI 用：給一個世界座標的移動方向（長度 0~1）、要不要衝刺、要不要跳</summary>
    public void SetAIInput(Vector3 worldMove, bool sprint = false, bool jump = false)
    {
        worldMove.y = 0f;
        aiMove = Vector3.ClampMagnitude(worldMove, 1f);
        aiSprint = sprint;
        if (jump) aiJump = true;
    }

    /// <summary>清除所有速度（重生 / 傳送時使用）</summary>
    public void ResetMotion()
    {
        EndRoll();
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        ballistic = false;
        ClearTemporarySpeedEffects();
    }

    /// <summary>
    /// 只停止目前的水平移動，不影響跳躍、重力或其他暫時速度效果。
    /// 給舉盾等需要立刻站穩、但不該重設整個移動狀態的動作用。
    /// </summary>
    public void StopHorizontalMotion()
    {
        horizontalVelocity = Vector3.zero;
    }

    bool ballistic;
    /// <summary>正在拋物線飛行中（Launch 之後、落地之前）</summary>
    public bool IsBallistic => ballistic;

    /// <summary>
    /// 以指定的速度起跳（拋物線）。飛行中不受移動輸入影響，落地後恢復正常。
    /// 小怪跳過缺口 / 撲向玩家時使用。
    /// </summary>
    public void Launch(Vector3 velocity)
    {
        if (IsControlImmune) return;
        horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        verticalVelocity = velocity.y;
        ballistic = true;
        lastGroundedTime = float.NegativeInfinity;
        lastJumpPressedTime = float.NegativeInfinity;
    }

    /// <summary>
    /// 算出「跳到 target」需要的起跳速度。apex = 拋物線最高點比起跳點高多少 (m)。
    /// </summary>
    public Vector3 LaunchVelocityTo(Vector3 target, float apex)
    {
        float g = -gravity;
        Vector3 flat = target - transform.position;
        float rise = flat.y;               // 落點比起跳點高多少
        flat.y = 0f;
        apex = Mathf.Max(apex, rise + 0.5f, 0.5f);
        float vy = Mathf.Sqrt(2f * g * apex);
        float time = vy / g + Mathf.Sqrt(2f * (apex - rise) / g);
        return flat / Mathf.Max(time, 0.05f) + Vector3.up * vy;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        if (RangeInputSettings.IsRange(this) && RangeInputSettings.InputBlocked) return;
        if (controller == null || !controller.enabled) return;
        float dt = Time.deltaTime;
        Vector3 moveDir;
        bool sprint, jumpPressed, rollPressed = false;

        if (aiControlled)
        {
            // ---- 1a. AI：直接用世界座標的方向 ----
            bool canAct = inputEnabled;
            moveDir = canAct ? aiMove : Vector3.zero;
            sprint = canAct && aiSprint;
            jumpPressed = canAct && aiJump;
            aiJump = false;
        }
        else
        {
            // ---- 1b. 玩家：以鏡頭方向換算移動方向 ----
            Vector2 input = ReadMoveInput(out sprint, out jumpPressed, out rollPressed);
            Vector3 camForward = Vector3.forward, camRight = Vector3.right;
            if (cameraTransform != null)
            {
                camForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                camRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            }
            moveDir = camForward * input.y + camRight * input.x; // 長度 <= 1
            if (IsStunned) { moveDir = Vector3.zero; sprint = false; jumpPressed = false; rollPressed = false; }
        }

        // 動作鎖定中（對話、喝藥水）或剛結束對話：這一下空白鍵不算跳躍
        if (ActionsLocked || Time.unscaledTime < suppressJumpUntil) jumpPressed = false;
        if (rollPressed) TryStartRoll(moveDir);
        if (IsRolling || rollPending)
        {
            UpdateRoll(dt);
            return;
        }

        // 精力：用完就不能衝刺；衝刺中扣、在地上沒衝刺時（走路或站著）回復（小怪沒有精力）
        var playerStamina = aiControlled ? null : Stamina;
        if (playerStamina != null)
        {
            if (!playerStamina.CanSprint) sprint = false;
            bool moving = moveDir.sqrMagnitude > 0.01f;
            playerStamina.Tick(dt, !controller.isGrounded, sprint && moving);
        }
        if (jumpPressed) lastJumpPressedTime = Time.time;

        // ---- 2. 水平速度（有加減速）----
        float targetSpeed = (sprint ? sprintSpeed : walkSpeed) * speedMultiplier * temporarySpeedMultiplier * temporarySpeedBoost
                          * (IsPushing ? pushFactor : 1f);
        Vector3 targetVelocity = moveDir * targetSpeed;
        float accel = controller.isGrounded ? acceleration : acceleration * airControl;
        if (ballistic)
        {
            // 拋物線飛行中：水平速度不變，落地（或撞到東西停下來）才恢復控制
            // 上升中 verticalVelocity > 0，所以起跳那一幀「還在地上」不會誤判成落地
            if (controller.isGrounded && verticalVelocity <= 0f) ballistic = false;
        }
        else horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, accel * dt);

        // ---- 3. 角色轉向移動方向 ----
        if (moveDir.sqrMagnitude > 0.001f && !lockRotation)
        {
            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle,
                                                ref rotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        // ---- 4. 跳躍與重力 ----
        if (controller.isGrounded)
        {
            lastGroundedTime = Time.time;
            if (verticalVelocity < 0f) verticalVelocity = -2f; // 輕壓地面，下坡才不會一直判定騰空
        }

        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        bool wantsJump = Time.time - lastJumpPressedTime <= jumpBufferTime;
        // 精力不夠跳：這次按鍵作廢（也不會變成二段跳）
        if (wantsJump && (canJump || doubleJumpEnabled && !aiControlled && !airJumpUsed && !ballistic && !controller.isGrounded)
            && playerStamina != null && !playerStamina.TrySpendJump())
        {
            lastJumpPressedTime = float.NegativeInfinity;
            wantsJump = false;
        }
        if (canJump && wantsJump)
        {
            GetComponent<PlayerCombat>()?.CancelTransientActions();
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            lastJumpPressedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity; // 防止連跳
        }
        else if (wantsJump && doubleJumpEnabled && !aiControlled && !airJumpUsed && !ballistic && !controller.isGrounded)
        {
            // 二段跳：空中再按一次跳躍（每次離地只能一次）
            verticalVelocity = Mathf.Sqrt(doubleJumpHeight * -2f * gravity);
            lastJumpPressedTime = float.NegativeInfinity;
            airJumpUsed = true;
            CombatVFX.Puff(transform.position + Vector3.down * 0.6f, Vector3.down, new Color(0.9f, 0.85f, 0.7f, 0.6f));
            GameAudio.Play("SFX/RPG/cloth3", transform.position, 0.7f, 1.3f, 0.08f, 0.3f);
            OnDoubleJump?.Invoke();
        }
        if (controller.isGrounded && verticalVelocity <= 0f) airJumpUsed = false;

        verticalVelocity += gravity * dt;

        // ---- 5. 實際移動 ----
        bool wasGrounded = controller.isGrounded;
        // Step-up is a grounded walking aid, not an airborne landing. At sprint speed it can
        // snap a rising capsule onto an obstacle and prematurely replace Jump with Run.
        float savedStepOffset = controller.stepOffset;
        CollisionFlags flags;
        try
        {
            if (verticalVelocity > 0f || !controller.isGrounded) controller.stepOffset = 0f;
            flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
        }
        finally { controller.stepOffset = savedStepOffset; }

        // 撞到天花板時停止上升
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;

        // 下坡 / 坡度起伏時腳下突然離地：沒有在跳、也不是被打飛的話，直接貼回地面
        if (wasGrounded && !controller.isGrounded && verticalVelocity <= 0f && !ballistic)
            SnapToGround();

        UpdateFootsteps(dt);
    }

    readonly RaycastHit[] snapHits = new RaycastHit[8];

    /// <summary>往下找地面（groundSnapDistance 以內、坡度不超過 slopeLimit），找到就把角色往下貼過去</summary>
    void SnapToGround()
    {
        float scale = transform.lossyScale.y;
        float radius = controller.radius * scale * 0.9f;
        Vector3 origin = transform.TransformPoint(controller.center);
        // 投射球的底部碰到「角色腳底那一層」時走過的距離
        float toBottom = controller.height * 0.5f * scale - radius;
        int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, snapHits, toBottom + groundSnapDistance,
                                           ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 normal = Vector3.up;
        for (int i = 0; i < n; i++)
        {
            var h = snapHits[i];
            if (h.collider == null || h.distance <= 0f || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best) { best = h.distance; normal = h.normal; }
        }
        if (best == float.MaxValue || Vector3.Angle(normal, Vector3.up) > controller.slopeLimit + 5f) return;
        float drop = best - toBottom;
        if (drop > 0f) controller.Move(Vector3.down * (drop + controller.skinWidth));
    }

    /// <summary>
    /// CharacterController 移動時撞到東西就會呼叫。
    /// 撞到木桶 / 木箱 / 掉在地上的火把（Knockable）：把它往前頂開、踢飛。
    /// 撞到可以推的岩石、而且是朝著它走：把岩石往前推一小段，並讓自己接下來一小段時間變慢（岩石越重越慢）。
    /// </summary>
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody hitBody = hit.collider.attachedRigidbody;
        if (hitBody == null) return;     // 地板、牆壁沒有 Rigidbody，馬上就跳過了

        // 木桶、木箱、掉在地上的火把：玩家和小怪用走的都能把它頂開 / 踢飛（不然翻倒的木桶會把路堵住）
        if (hitBody.TryGetComponent(out Knockable prop))
        {
            // 重的東西會把推的人拖慢（跟推岩石一樣的機制）
            float slow = prop.Shove(transform.position, horizontalVelocity, hit.normal, sprintSpeed);
            if (slow < 1f)
            {
                pushFactor = slow;
                pushUntil = Time.time + 0.15f;
            }
            return;
        }

        if (hit.normal.y > 0.6f) return; // 踩在上面，不是從旁邊推
        if (aiControlled || !canPushRocks || ballistic) return;
        var rock = ArenaRock.From(hit.collider);
        if (rock == null || !rock.pushable) return;
        if (hit.point.y > rock.transform.position.y + rock.height * 0.6f) return; // 站在石頭的肩膀上，不算推

        Vector3 move = horizontalVelocity;
        float speed = move.magnitude;
        if (speed < 0.2f) return;

        Vector3 toRock = rock.transform.position - transform.position;
        toRock.y = 0f;
        if (toRock.sqrMagnitude < 0.0001f) return;
        toRock.Normalize();
        if (Vector3.Dot(move / speed, toRock) < 0.35f) return; // 只是從旁邊擦過去

        // 岩石每一幀往前挪「比玩家這一幀想走的距離少一點」，玩家下一幀跟上去時一定還頂得到它，
        // 才不會推一下、空一幀、再推一下地卡頓
        float pushSpeed = Mathf.Min(speed, sprintSpeed * rock.pushSpeedFactor);
        if (rock.Push(toRock, pushSpeed * Time.deltaTime * 0.85f, controller))
        {
            pushFactor = rock.pushSpeedFactor;
            pushUntil = Time.time + 0.15f;
        }
    }

    void TryStartRoll(Vector3 requestedDirection)
    {
        if (aiControlled || !isActiveAndEnabled || !inputEnabled || ActionsLocked || IsStunned || ballistic || IsRolling || rollPending ||
            !controller || !controller.enabled || !controller.isGrounded || Time.timeScale<=0 || GameUI.OverlayOpen ||
            (GetComponent<PlayerHealth>()?.IsDead??false) || Time.unscaledTime < nextRollTime) return;
        // Affordability first; payment is committed only after the controller really moves.
        if (Stamina != null && !Stamina.CanAffordRoll){Stamina.TrySpendRoll();return;}
        requestedDirection.y = 0f;
        rollDirection = requestedDirection.sqrMagnitude > .001f ? requestedDirection.normalized : transform.forward;
        FaceDirection(rollDirection);
        horizontalVelocity = Vector3.zero;
        verticalVelocity = -2f;
        ballistic = false;
        GetComponent<PlayerCombat>()?.CancelTransientActions();
        rollStartedAt = Time.unscaledTime;
        rollCommitted=false;rollTravel=0;
        rollPending = true;
    }

    void UpdateRoll(float dt)
    {
        if(dt<=0)return;
        if(!inputEnabled || ActionsLocked || IsStunned){EndRoll();return;}
        float u = Mathf.Clamp01((Time.unscaledTime - rollStartedAt) / Mathf.Max(.01f, rollDuration));
        // 半正弦速度：起步與收尾不突兀，積分後仍約等於設定距離。
        float speed = rollDistance / Mathf.Max(.01f, rollDuration) * (Mathf.PI * .5f) * Mathf.Sin(Mathf.Clamp01(u+dt/rollDuration*.5f) * Mathf.PI);
        Vector3 before=transform.position;
        controller.Move((rollDirection * speed + Vector3.down * 2f) * dt);
        if(!rollCommitted)
        {
            rollTravel+=Mathf.Max(0,Vector3.Dot(transform.position-before,rollDirection));
            if(rollTravel>=Mathf.Clamp(rollDistance*.01f,.005f,.05f))
            {
                if(Stamina!=null && !Stamina.TrySpendRoll()){EndRoll();return;}
                rollCommitted=true;rollPending=false;IsRolling=true;nextRollTime=rollStartedAt+rollDuration+rollCooldown;
                GetComponent<PlayerHealth>()?.GrantDodgeInvulnerability(Mathf.Max(0,rollDuration*(1-u))+.08f);
                OnRollStarted?.Invoke();
            }
            else if(u>.08f){EndRoll();return;}
        }
        if (u < 1f) return;
        EndRoll();
    }

    Vector2 ReadMoveInput(out bool sprint, out bool jumpPressed, out bool rollPressed)
    {
        sprint = false;
        jumpPressed = false;
        rollPressed = false;
        if (!inputEnabled || (RangeInputSettings.IsRange(this) && RangeInputSettings.InputBlocked)) return Vector2.zero;

        Vector2 input = Vector2.zero;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;

            sprint = kb[RangeInputSettings.Binding(this,RangeInputSettings.Action.Sprint)].isPressed;
            jumpPressed = kb[RangeInputSettings.Binding(this,RangeInputSettings.Action.Jump)].wasPressedThisFrame;
            rollPressed = kb[RangeInputSettings.Binding(this,RangeInputSettings.Action.Roll)].wasPressedThisFrame;
        }
#if ENABLE_LEGACY_INPUT_MANAGER
        else
        {
            input.x = Input.GetAxisRaw("Horizontal");
            input.y = Input.GetAxisRaw("Vertical");
            sprint = Input.GetKey(KeyCode.LeftShift);
            jumpPressed = Input.GetKeyDown(KeyCode.Space);
            rollPressed = Input.GetKeyDown(RangeInputSettings.IsRange(this) ? KeyCode.C : KeyCode.V);
        }
#endif
        return Vector2.ClampMagnitude(input, 1f); // 斜走不會比較快
    }
}
