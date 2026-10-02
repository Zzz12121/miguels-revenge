using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人稱環繞鏡頭（從角色斜後方看）
/// - 滑鼠移動：旋轉視角
/// - 滑鼠滾輪：拉近 / 拉遠
/// - Esc：放開滑鼠游標；在遊戲畫面點左鍵：重新鎖定
/// - 鏡頭碰到牆壁時會自動往前拉，不會穿牆
/// 掛在 Main Camera 上，並把 Target 設為玩家。
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("跟隨目標")]
    public Transform target;
    [Tooltip("鏡頭注視點相對角色腳底的位置（大約肩膀 / 頭的高度）")]
    public Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);
    [Tooltip("往右偏移，做出越肩視角；0 = 置中（原神風格）")]
    public float shoulderOffset = 0f;
    [Tooltip("跟隨平滑時間，0 = 完全不延遲")]
    public float followSmoothTime = 0.05f;

    [Header("距離 / 縮放")]
    public float distance = 5f;
    public float minDistance = 2f;
    public float maxDistance = 9f;
    [Tooltip("滾輪每一格縮放的距離")]
    public float zoomStep = 0.6f;

    [Header("滑鼠控制")]
    [Tooltip("每移動 1 像素旋轉幾度")]
    public float mouseSensitivity = 0.12f;
    public bool invertY = false;
    [Tooltip("最低可往上看的角度（負值 = 從下往上看）")]
    public float minPitch = -30f;
    [Tooltip("最高可往下俯視的角度")]
    public float maxPitch = 70f;
    [Tooltip("開始時鏡頭的俯角")]
    public float startPitch = 15f;

    [Header("防穿牆")]
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.25f;
    [Tooltip("被牆擋住後，鏡頭拉回原距離的速度")]
    public float returnSpeed = 6f;

    float yaw, pitch;
    float currentDistance;
    Vector3 smoothedPivot, pivotVelocity;
    readonly RaycastHit[] hits = new RaycastHit[16];
    float shakeAmplitude, shakeDuration, shakeTimer;

    /// <summary>畫面震動（Boss 拍地、怒吼時使用）</summary>
    public void Shake(float amplitude, float duration)
    {
        if (amplitude < shakeAmplitude * (shakeDuration > 0f ? shakeTimer / shakeDuration : 0f)) return;
        shakeAmplitude = amplitude;
        shakeDuration = shakeTimer = Mathf.Max(duration, 0.01f);
    }

    /// <summary>讓 Main Camera 震動，找不到就忽略</summary>
    public static void ShakeMain(float amplitude, float duration)
    {
        if (Camera.main != null && Camera.main.TryGetComponent(out ThirdPersonCamera cam))
            cam.Shake(amplitude, duration);
    }

    void Start()
    {
        if (target != null)
        {
            yaw = target.eulerAngles.y;
            smoothedPivot = target.position + targetOffset;
        }
        pitch = startPitch;
        currentDistance = distance;
        LockCursor(true);
    }

    void LateUpdate() // 在角色 Update 移動完之後才更新鏡頭，避免抖動
    {
        if (target == null) return;
        HandleInput();

        // 注視點（平滑跟隨）
        Vector3 pivot = target.position + targetOffset;
        smoothedPivot = followSmoothTime > 0f
            ? Vector3.SmoothDamp(smoothedPivot, pivot, ref pivotVelocity, followSmoothTime)
            : pivot;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 lookPoint = smoothedPivot + rotation * Vector3.right * shoulderOffset;
        Vector3 backDir = rotation * Vector3.back;

        // 防穿牆：從注視點往鏡頭方向做球形射線
        float allowed = distance;
        int count = Physics.SphereCastNonAlloc(lookPoint, collisionRadius, backDir, hits,
                                               distance, collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (hits[i].transform.IsChildOf(target)) continue; // 忽略玩家自己
            if (hits[i].collider is CharacterController) continue; // 小怪擋在中間不拉近鏡頭
            if (hits[i].distance <= 0f) continue;              // 起點就重疊的忽略
            allowed = Mathf.Min(allowed, Mathf.Max(hits[i].distance - 0.05f, 0.3f));
        }

        // 被擋住時立刻拉近，沒被擋住時慢慢退回
        currentDistance = allowed < currentDistance
            ? allowed
            : Mathf.Lerp(currentDistance, allowed, 1f - Mathf.Exp(-returnSpeed * Time.deltaTime));

        Vector3 shake = Vector3.zero;
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            shake = Random.insideUnitSphere * shakeAmplitude * Mathf.Clamp01(shakeTimer / shakeDuration);
        }

        transform.SetPositionAndRotation(lookPoint + backDir * currentDistance + shake, rotation);
    }

    void HandleInput()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);

        if (mouse == null || Cursor.lockState != CursorLockMode.Locked) return;

        // 旋轉（滑鼠 delta 已經是每幀位移，不需要乘 deltaTime）
        Vector2 delta = mouse.delta.ReadValue();
        yaw += delta.x * mouseSensitivity;
        pitch += delta.y * mouseSensitivity * (invertY ? 1f : -1f);
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // 縮放
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
