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

    [Header("狀態")]
    [Tooltip("關閉時角色不吃玩家輸入（死亡、過場時使用），但仍受重力影響")]
    public bool inputEnabled = true;
    [Tooltip("移動速度倍率（攻擊時會暫時降低）")]
    public float speedMultiplier = 1f;
    [Tooltip("鎖定面向，不會自動轉向移動方向（攻擊時使用）")]
    public bool lockRotation = false;

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

    // 之後做動畫 / UI 時可以讀這些值
    public bool IsGrounded => controller != null && controller.isGrounded;
    public float CurrentSpeed => horizontalVelocity.magnitude;
    public float VerticalVelocity => verticalVelocity;

    /// <summary>給角色一個瞬間的推力（受擊擊退用）</summary>
    public void AddImpulse(Vector3 impulse)
    {
        horizontalVelocity += new Vector3(impulse.x, 0f, impulse.z);
        if (impulse.y > 0f) verticalVelocity = Mathf.Max(verticalVelocity, impulse.y);
    }

    /// <summary>
    /// 暫時套用額外移動倍率。效果由玩家自己計時，來源物件即使被銷毀也能正常解除。
    /// 多個效果重疊時採用最慢的倍率，且不會覆蓋攻擊使用的 speedMultiplier。
    /// </summary>
    public void ApplyTemporarySpeedMultiplier(float multiplier, float duration)
    {
        if (duration <= 0f) return;
        StartCoroutine(TemporarySpeedEffectRoutine(Mathf.Clamp01(multiplier), duration));
    }

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

    void RecalculateTemporarySpeedMultiplier()
    {
        temporarySpeedMultiplier = 1f;
        foreach (float multiplier in temporarySpeedMultipliers.Values)
            temporarySpeedMultiplier = Mathf.Min(temporarySpeedMultiplier, multiplier);
    }

    public void ClearTemporarySpeedEffects()
    {
        temporarySpeedEffectGeneration++;
        temporarySpeedMultipliers.Clear();
        temporarySpeedMultiplier = 1f;
    }

    void OnDisable()
    {
        ClearTemporarySpeedEffects();
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
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        ballistic = false;
        ClearTemporarySpeedEffects();
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
        if (controller == null || !controller.enabled) return;
        float dt = Time.deltaTime;
        Vector3 moveDir;
        bool sprint, jumpPressed;

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
            Vector2 input = ReadMoveInput(out sprint, out jumpPressed);
            Vector3 camForward = Vector3.forward, camRight = Vector3.right;
            if (cameraTransform != null)
            {
                camForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                camRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            }
            moveDir = camForward * input.y + camRight * input.x; // 長度 <= 1
        }
        if (jumpPressed) lastJumpPressedTime = Time.time;

        // ---- 2. 水平速度（有加減速）----
        float targetSpeed = (sprint ? sprintSpeed : walkSpeed) * speedMultiplier * temporarySpeedMultiplier
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
        if (canJump && wantsJump)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            lastJumpPressedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity; // 防止連跳
        }

        verticalVelocity += gravity * dt;

        // ---- 5. 實際移動 ----
        CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);

        // 撞到天花板時停止上升
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;

        UpdateFootsteps(dt);
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

    Vector2 ReadMoveInput(out bool sprint, out bool jumpPressed)
    {
        sprint = false;
        jumpPressed = false;
        if (!inputEnabled) return Vector2.zero;

        Vector2 input = Vector2.zero;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;

            sprint = kb.leftShiftKey.isPressed;
            jumpPressed = kb.spaceKey.wasPressedThisFrame;
        }
#if ENABLE_LEGACY_INPUT_MANAGER
        else
        {
            input.x = Input.GetAxisRaw("Horizontal");
            input.y = Input.GetAxisRaw("Vertical");
            sprint = Input.GetKey(KeyCode.LeftShift);
            jumpPressed = Input.GetKeyDown(KeyCode.Space);
        }
#endif
        return Vector2.ClampMagnitude(input, 1f); // 斜走不會比較快
    }
}
