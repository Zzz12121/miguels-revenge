using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人稱角色移動（類似原神 / 艾爾登法環的自由移動）
/// - WASD / 方向鍵：相對鏡頭方向移動，角色自動轉向移動方向
/// - 左 Shift：衝刺
/// - 空白鍵：跳躍（含 Coyote Time 與跳躍緩衝，手感較好）
/// 需要搭配 CharacterController。
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

    CharacterController controller;
    Vector3 horizontalVelocity;
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

    /// <summary>立刻轉向某個方向（攻擊時面向鏡頭前方）</summary>
    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(direction);
        rotationVelocity = 0f;
    }

    /// <summary>清除所有速度（重生 / 傳送時使用）</summary>
    public void ResetMotion()
    {
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector2 input = ReadMoveInput(out bool sprint, out bool jumpPressed);
        if (jumpPressed) lastJumpPressedTime = Time.time;

        // ---- 1. 以鏡頭方向換算移動方向 ----
        Vector3 camForward = Vector3.forward, camRight = Vector3.right;
        if (cameraTransform != null)
        {
            camForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            camRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        Vector3 moveDir = camForward * input.y + camRight * input.x; // 長度 <= 1

        // ---- 2. 水平速度（有加減速）----
        float targetSpeed = (sprint ? sprintSpeed : walkSpeed) * speedMultiplier;
        Vector3 targetVelocity = moveDir * targetSpeed;
        float accel = controller.isGrounded ? acceleration : acceleration * airControl;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, accel * dt);

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
    }

    Vector2 ReadMoveInput(out bool sprint, out bool jumpPressed)
    {
        sprint = false;
        jumpPressed = false;
        var kb = Keyboard.current;
        if (kb == null || !inputEnabled) return Vector2.zero;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;

        sprint = kb.leftShiftKey.isPressed;
        jumpPressed = kb.spaceKey.wasPressedThisFrame;
        return Vector2.ClampMagnitude(input, 1f); // 斜走不會比較快
    }
}
