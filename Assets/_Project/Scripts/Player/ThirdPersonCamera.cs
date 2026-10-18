using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人稱環繞鏡頭（從角色斜後方看），用 Cinemachine 實作：
///   - Main Camera 上有 CinemachineBrain，畫面由 CinemachineCamera 決定
///   - CinemachineCamera 用 Third Person Follow 跟著「鏡頭注視點」（CameraTarget），
///     並開啟 Avoid Obstacles：鏡頭被牆擋住時自動往前拉，不會穿牆
///   - 這個腳本負責讀滑鼠 / 滾輪，轉動注視點、調整距離，以及畫面震動
///
/// 操作：
/// - 滑鼠移動：旋轉視角
/// - 滑鼠滾輪：拉近 / 拉遠
/// - Esc：放開滑鼠游標；在遊戲畫面點左鍵：重新鎖定
///
/// 掛在 Main Camera 上，並把 Target 設為玩家。Cinemachine 的物件沒有事先擺好的話，遊戲開始時會自動建立。
/// </summary>
[DefaultExecutionOrder(-50)] // 要比 CinemachineBrain（LateUpdate）先更新注視點
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

    [Header("防穿牆（Cinemachine Avoid Obstacles）")]
    public LayerMask collisionMask = ~(1 << 2); // 不含 Ignore Raycast
    [Range(0f, 1f)] public float collisionRadius = 0.25f;
    [Tooltip("被牆擋住後，鏡頭退回原距離所花的時間（越大越慢）")]
    [Range(0f, 10f)] public float returnDamping = 0.4f;
    [Tooltip("注視點至少離天花板 / 牆多遠 (m)。注視點在角色頭頂上方，在矮門洞或隧道裡跳起來時，" +
             "它不會跟著頂進天花板裡（頂進去的話鏡頭就會穿牆）。0 = 關閉這個保護")]
    public float pivotClearance = 0.6f;

    [Header("瞄準（在船上拉弓時，按住右鍵）")]
    [Tooltip("瞄準時鏡頭離注視點多遠")]
    public float aimDistance = 2.2f;
    [Tooltip("瞄準時往右偏多少（越肩視角）")]
    public float aimShoulder = 0.7f;
    [Tooltip("瞄準時的注視點（相對 aimTarget 的位置；玩家的位置在身體中間，所以 0.7 大約是肩膀高度）")]
    public Vector3 aimOffset = new Vector3(0f, 0.7f, 0f);
    [Tooltip("瞄準時的視角（越小放大越多）")]
    public float aimFov = 32f;
    [Tooltip("進入 / 離開瞄準的速度")]
    public float aimBlendSpeed = 7f;
    [Tooltip("瞄準時的滑鼠靈敏度倍率")]
    public float aimSensitivityScale = 0.55f;

    [Header("Cinemachine（留空會自動建立）")]
    public CinemachineCamera cinemachineCamera;
    [Tooltip("Cinemachine 跟隨的注視點，這個腳本每幀設定它的位置和角度")]
    public Transform cameraTarget;

    [HideInInspector] public float yaw, pitch;
    Vector3 smoothedPivot, pivotVelocity;
    readonly RaycastHit[] hits = new RaycastHit[16];
    CharacterController targetBody;
    Transform targetBodyOwner;
    float shakeAmplitude, shakeDuration, shakeTimer;
    CinemachineThirdPersonFollow follow;
    bool aiming;
    Transform aimTarget;
    float aimBlend, baseFov = 60f;

    /// <summary>目前是不是在瞄準（0 = 一般鏡頭，1 = 完全進入瞄準）</summary>
    public float AimBlend => aimBlend;

    /// <summary>開始 / 結束瞄準：鏡頭拉近到 who 的肩膀後面並放大畫面</summary>
    public void SetAiming(bool on, Transform who)
    {
        aiming = on;
        if (who != null) aimTarget = who;
    }

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

    void Awake()
    {
        EnsureCinemachine();
    }

    void Start()
    {
        if (target != null)
        {
            yaw = target.eulerAngles.y;
            smoothedPivot = target.position + targetOffset;
        }
        pitch = startPitch;
        LockCursor(true);
        SnapToTarget();
    }

    /// <summary>確保 Main Camera 有 CinemachineBrain，並找到（或建立）CinemachineCamera 和注視點</summary>
    void EnsureCinemachine()
    {
        var brain = GetComponent<CinemachineBrain>();
        if (brain == null) brain = gameObject.AddComponent<CinemachineBrain>();
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

        Transform rig = transform.parent != null ? transform.parent : transform;
        if (cinemachineCamera == null) cinemachineCamera = rig.GetComponentInChildren<CinemachineCamera>(true);
        if (cinemachineCamera == null)
        {
            var go = new GameObject("CM PlayerFollowCamera");
            go.transform.SetParent(rig, false);
            cinemachineCamera = go.AddComponent<CinemachineCamera>();
        }
        if (cameraTarget == null)
        {
            var found = rig.Find("CM CameraTarget");
            cameraTarget = found != null ? found : new GameObject("CM CameraTarget").transform;
            if (found == null) cameraTarget.SetParent(rig, false);
        }

        // 鏡頭的視角、遠近裁切跟原本的 Camera 一樣
        var cam = GetComponent<Camera>();
        if (cam != null)
        {
            baseFov = cam.fieldOfView;
            cinemachineCamera.Lens.FieldOfView = cam.fieldOfView;
            cinemachineCamera.Lens.NearClipPlane = cam.nearClipPlane;
            cinemachineCamera.Lens.FarClipPlane = cam.farClipPlane;
        }
        cinemachineCamera.Follow = cameraTarget;

        follow = cinemachineCamera.GetComponent<CinemachineThirdPersonFollow>();
        if (follow == null) follow = cinemachineCamera.gameObject.AddComponent<CinemachineThirdPersonFollow>();
        ConfigureFollow();
    }

    void ConfigureFollow()
    {
        if (follow == null) return;
        follow.Damping = Vector3.zero;         // 平滑交給注視點的 SmoothDamp，鏡頭本身不再延遲
        follow.ShoulderOffset = Vector3.zero;  // 注視點已經在肩膀 / 頭的高度
        follow.VerticalArmLength = 0f;
        follow.CameraSide = 0.5f;
        follow.CameraDistance = distance;
        var avoid = follow.AvoidObstacles;
        avoid.Enabled = true;
        avoid.CollisionFilter = collisionMask;
        avoid.IgnoreTag = "Player";
        avoid.CameraRadius = collisionRadius;
        avoid.DampingIntoCollision = 0f;       // 被擋住時立刻拉近
        avoid.DampingFromCollision = returnDamping;
        follow.AvoidObstacles = avoid;
    }

    void LateUpdate() // 在角色 Update 移動完之後才更新鏡頭，避免抖動
    {
        if (target == null || cameraTarget == null) return;
        HandleInput();

        // 注視點（平滑跟隨）
        Vector3 pivot = target.position + targetOffset;
        smoothedPivot = followSmoothTime > 0f
            ? Vector3.SmoothDamp(smoothedPivot, pivot, ref pivotVelocity, followSmoothTime)
            : pivot;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 lookPoint = ClampPivot(smoothedPivot + rotation * Vector3.right * shoulderOffset);

        // 瞄準：注視點移到瞄準對象的肩膀、鏡頭拉近、畫面放大（平滑過渡）
        aimBlend = Mathf.MoveTowards(aimBlend, aiming && aimTarget != null ? 1f : 0f, aimBlendSpeed * Time.deltaTime);
        float dist = distance;
        if (aimBlend > 0f && aimTarget != null)
        {
            float k = Mathf.SmoothStep(0f, 1f, aimBlend);
            Vector3 aimPoint = aimTarget.position + aimOffset + rotation * Vector3.right * aimShoulder;
            lookPoint = Vector3.Lerp(lookPoint, aimPoint, k);
            dist = Mathf.Lerp(distance, aimDistance, k);
        }
        if (cinemachineCamera != null)
            cinemachineCamera.Lens.FieldOfView = Mathf.Lerp(baseFov, aimFov, Mathf.SmoothStep(0f, 1f, aimBlend));

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            lookPoint += Random.insideUnitSphere * shakeAmplitude * Mathf.Clamp01(shakeTimer / shakeDuration);
        }

        // Cinemachine 的 Third Person Follow 會把鏡頭放在注視點正後方 distance 公尺（有牆就往前拉）
        cameraTarget.SetPositionAndRotation(lookPoint, rotation);
        if (follow != null) follow.CameraDistance = dist;
    }

    /// <summary>
    /// 注視點在角色頭頂上方，本身可能跑進天花板或牆裡（例如在矮門洞底下跳起來）。
    /// 一旦它在牆裡，Cinemachine 從注視點往後的防穿牆檢查就偵測不到那面牆，鏡頭會直接穿過去。
    /// 所以先從角色身體的中心（一定在空的地方）往注視點掃一次：路上有東西，就把注視點壓低到離它 pivotClearance 的位置。
    /// </summary>
    Vector3 ClampPivot(Vector3 lookPoint)
    {
        if (pivotClearance <= 0f) return lookPoint;

        if (targetBodyOwner != target)
        {
            targetBodyOwner = target;
            targetBody = target.GetComponent<CharacterController>();
        }
        Vector3 origin = targetBody != null ? target.TransformPoint(targetBody.center) : target.position;
        Vector3 to = lookPoint - origin;
        float dist = to.magnitude;
        if (dist < 0.01f) return lookPoint;
        Vector3 dir = to / dist;

        float nearest = float.MaxValue;
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, dir, hits, dist + pivotClearance,
                                               collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (hits[i].transform.IsChildOf(target)) continue;
            if (hits[i].collider is CharacterController) continue;
            if (hits[i].distance <= 0f) continue;
            if (IsLooseProp(hits[i])) continue;
            nearest = Mathf.Min(nearest, hits[i].distance);
        }
        if (nearest == float.MaxValue) return lookPoint;
        return origin + dir * Mathf.Min(dist, Mathf.Max(nearest - pivotClearance, 0f));
    }

    /// <summary>
    /// 正在飛 / 正在滾的擺設（被打飛的木桶、掉下來的火把）。它們飛過角色和注視點中間時不算障礙物，
    /// 不然注視點會瞬間被壓低，畫面看起來像頓了一下。
    /// </summary>
    static bool IsLooseProp(RaycastHit hit)
    {
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic || !body.TryGetComponent(out Knockable _)) return false;
        return body.linearVelocity.sqrMagnitude > 0.09f || body.angularVelocity.sqrMagnitude > 0.25f;
    }

    void HandleInput()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);

#if ENABLE_LEGACY_INPUT_MANAGER
        if (kb == null && Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
        if (mouse == null && Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);
#endif

        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (mouse != null)
        {
            // 旋轉（滑鼠 delta 已經是每幀位移，不需要乘 deltaTime）
            Vector2 delta = mouse.delta.ReadValue();
            float sens = mouseSensitivity * Mathf.Lerp(1f, aimSensitivityScale, aimBlend); // 瞄準時比較好微調
            yaw += delta.x * sens;
            pitch += delta.y * sens * (invertY ? 1f : -1f);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            // 縮放
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !aiming)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }
#if ENABLE_LEGACY_INPUT_MANAGER
        else
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 15f;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * 15f;
            yaw += mouseX;
            pitch += mouseY * (invertY ? 1f : -1f);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }
#endif
    }

    /// <summary>
    /// 立即將鏡頭瞬移至目標身後（切換跟隨目標或傳送時呼叫，防止平滑插值穿幫或卡在障礙物）
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null || cameraTarget == null) return;
        smoothedPivot = target.position + targetOffset;
        pivotVelocity = Vector3.zero;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        cameraTarget.SetPositionAndRotation(smoothedPivot + rotation * Vector3.right * shoulderOffset, rotation);
        if (follow != null) follow.CameraDistance = distance;
        if (cinemachineCamera != null) cinemachineCamera.PreviousStateIsValid = false; // 下一幀直接跳過去，不做平滑
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
