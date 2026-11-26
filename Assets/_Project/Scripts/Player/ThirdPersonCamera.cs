using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人稱環繞鏡頭（從角色斜後方看），用 Cinemachine 實作：
///   - Main Camera 上有 CinemachineBrain，畫面由 CinemachineCamera 決定
///   - CinemachineCamera 用 Third Person Follow 跟著「鏡頭注視點」（CameraTarget），
///     並開啟 Avoid Obstacles：鏡頭被牆擋住時自動往前拉，不會穿牆
///   - 這個腳本負責讀滑鼠 / 滾輪，轉動注視點、調整距離，以及畫面震動
///   - keepPlayerVisible（ThirdPersonCamera.FullView.cs）開著時改用自己的防擋：小東西隱藏、大東西把鏡頭抬高，
///     鏡頭不會被拉到角色身上，永遠看得到整個角色
///
/// 操作：
/// - 滑鼠移動：旋轉視角
/// - 滑鼠滾輪：拉近 / 拉遠
/// - Esc：放開滑鼠游標；在遊戲畫面點左鍵：重新鎖定
///
/// 掛在 Main Camera 上，並把 Target 設為玩家。Cinemachine 的物件沒有事先擺好的話，遊戲開始時會自動建立。
/// </summary>
[DefaultExecutionOrder(-50)] // 要比 CinemachineBrain（LateUpdate）先更新注視點
public partial class ThirdPersonCamera : MonoBehaviour
{
    [Header("取景（原神 / 崩鐵風格）")]
    [Tooltip("開著：鏡頭低一點、平一點（注視點在胸口附近），拉近時看得到上半身；往上看時鏡頭往角色靠近、不碰到地面，角色留在畫面裡。\n關掉 = 原本的取景（注視點固定在 Target Offset）")]
    public bool lowAngleFraming = true;
    [Tooltip("注視點離「腳底」的高度：拉到最近時 / 拉到最遠時（公尺）。注視點在畫面正中間：拉近時對準腰，看得到整個人")]
    public float framingPivotNear = 1.1f, framingPivotFar = 1.4f;
    [Tooltip("一開始的俯角（度）：越小越平、越像原神")]
    [Range(-10f, 40f)] public float framingStartPitch = 5f;
    [Tooltip("往上看到底（Min Pitch）時，鏡頭距離縮成原本的多少")]
    [Range(.2f, 1f)] public float lookUpDistanceScale = .55f;
    [Tooltip("鏡頭至少離角色腳底的地面多高（公尺）。要比 Collision Radius 大，不然鏡頭一碰到地面就會被推到角色身上")]
    [Range(0f, 1.5f)] public float minCameraHeight = .5f;
    [SerializeField, HideInInspector] int framingVersion;
    const int FramingVersion = 3;

    /// <summary>場景裡存的是舊版的取景數值時，換成這一版的預設值（只做一次）</summary>
    void UpgradeFraming()
    {
        if (framingVersion >= FramingVersion) return;
        framingVersion = FramingVersion;
        lowAngleFraming = true;
        framingPivotNear = 1.1f; framingPivotFar = 1.4f;
        framingStartPitch = 5f; lookUpDistanceScale = .55f; minCameraHeight = .5f;
    }

    void OnValidate() => UpgradeFraming();

    [Header("跟隨目標")]
    public Transform target;
    [Tooltip("鏡頭注視點相對角色腳底的位置（大約肩膀 / 頭的高度）")]
    public Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);
    [Tooltip("往右偏移，做出越肩視角；0 = 置中（原神風格）")]
    public float shoulderOffset = 0f;
    [Tooltip("跟隨平滑時間，0 = 完全不延遲")]
    public float followSmoothTime = 0.05f;
    [Tooltip("翻滾時鏡頭跟得慢一點（秒）：角色一下子衝出去時畫面不會跟著猛拉，滾完再慢慢回到 followSmoothTime")]
    public float rollFollowSmoothTime = 0.14f;

    [Header("距離 / 縮放")]
    public float distance = 8f;
    public float minDistance = 2f;
    public float maxDistance = 13f;
    [Tooltip("滾輪每一格縮放的距離")]
    public float zoomStep = 0.6f;

    [Header("滑鼠控制")]
    [Tooltip("每移動 1 像素旋轉幾度")]
    public float mouseSensitivity = 0.12f;
    [Tooltip("遊戲手把右搖桿推到底時，鏡頭每秒轉幾度")]
    public float padLookSpeed = 180f;
    public bool invertY = false;
    [Tooltip("最低可往上看的角度（負值 = 從下往上看）")]
    public float minPitch = -30f;
    [Tooltip("最高可往下俯視的角度")]
    public float maxPitch = 70f;
    [Tooltip("開始時鏡頭的俯角")]
    public float startPitch = 15f;
    [Tooltip("技能瞄準時，游標距離畫面左右邊緣多少像素開始轉鏡頭")]
    [Min(8f)] public float edgePanMargin = 52f;
    [Tooltip("技能瞄準時靠近畫面邊緣的水平轉向速度（度／秒）")]
    [Min(10f)] public float edgePanSpeed = 92f;

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

    [Header("攀爬時")]
    [Tooltip("攀爬時鏡頭距離 = 一般距離 × 這個倍數（越小越近）")]
    [Range(.3f, 1f)] public float climbDistanceScale = .7f;
    [Tooltip("攀爬時注視點比平常低多少公尺（看向角色的身體，而不是頭頂上方）")]
    public float climbPivotDrop = .35f;

    [Header("Cinemachine（留空會自動建立）")]
    public CinemachineCamera cinemachineCamera;
    [Tooltip("Cinemachine 跟隨的注視點，這個腳本每幀設定它的位置和角度")]
    public Transform cameraTarget;

    [HideInInspector] public float yaw, pitch;
    ThirdPersonController targetMotor;
    float currentFollowSmooth;
    Vector3 smoothedPivot, pivotVelocity;
    readonly RaycastHit[] hits = new RaycastHit[16];
    CharacterController targetBody;
    Transform targetBodyOwner;
    CombatCameraImpulse impactImpulse;
    [Header("命中鏡頭回饋")]
    [Range(0f, 1f)] public float cameraShakeIntensity = .7f;
    [Tooltip("保留手動環繞方向，攻擊與跳躍不會自行轉到背後")]
    public bool manualOrbitPriority = true;
    public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    CinemachineThirdPersonFollow follow;
    bool aiming;
    Transform aimTarget;
    float aimBlend, baseFov = 60f;
    // 攀爬：角色的根節點會先放到「爬完站好的位置」，身體才慢慢爬上去，
    // 所以攀爬時注視點改跟著身體（髖部）走，不然鏡頭會停在平台上方很高的地方
    Animator targetAnimator;
    Transform targetAnimatorOwner, targetHips;
    Vector3 hipsLocal, climbShift;
    float climbBlend;

    /// <summary>目前是不是在瞄準（0 = 一般鏡頭，1 = 完全進入瞄準）</summary>
    public float AimBlend => aimBlend;

    /// <summary>開始 / 結束瞄準：鏡頭拉近到 who 的肩膀後面並放大畫面</summary>
    public void SetAiming(bool on, Transform who)
    {
        aiming = on;
        if (who != null) aimTarget = who;
    }

    /// <summary>
    /// 游標解鎖的指定技可呼叫此方法：靠近畫面左右邊緣時仍能環繞角色，
    /// 讓玩家不必取消瞄準就能搜尋更大的範圍。
    /// </summary>
    public void PanFromScreenEdge(Vector2 pointerPosition, float unscaledDeltaTime)
    {
        float margin = Mathf.Clamp(edgePanMargin, 8f, Screen.width * .35f);
        float direction = pointerPosition.x <= margin ? -1f : pointerPosition.x >= Screen.width - margin ? 1f : 0f;
        if (Mathf.Abs(direction) < .01f) return;
        float edgeDepth = direction < 0f
            ? 1f - Mathf.Clamp01(pointerPosition.x / margin)
            : 1f - Mathf.Clamp01((Screen.width - pointerPosition.x) / margin);
        yaw += direction * edgePanSpeed * Mathf.Lerp(.35f, 1f, edgeDepth) * unscaledDeltaTime;
        ApplyUnlockedAimRotation();
    }

    void ApplyUnlockedAimRotation()
    {
        if (target == null || cameraTarget == null) return;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + targetOffset;
        cameraTarget.SetPositionAndRotation(pivot + rotation * Vector3.right * shoulderOffset, rotation);
        if (cinemachineCamera != null) cinemachineCamera.PreviousStateIsValid = false;
    }

    /// <summary>畫面震動（Boss 拍地、怒吼時使用）</summary>
    public void Shake(float amplitude, float duration)
    {
        Impact(target ? transform.position - target.position : -transform.forward, amplitude, duration);
    }

    [Min(1f)] public float impactFeedbackRange = 24f;
    public static float MainShakeIntensity => Camera.main && Camera.main.TryGetComponent<ThirdPersonCamera>(out var orbit)
        ? Mathf.Clamp01(orbit.cameraShakeIntensity) : 1f;

    public void ImpactAt(Vector3 point, float amplitude, float duration)
    {
        Vector3 direction = target ? point - target.position : point - transform.position;
        float attenuation = 1f - Mathf.Clamp01((direction.magnitude - 2f) / Mathf.Max(1f, impactFeedbackRange));
        Impact(direction, amplitude * attenuation * attenuation, duration);
    }

    public static void ImpactAtMain(Vector3 point, float amplitude, float duration)
    {
        if (Camera.main && Camera.main.TryGetComponent<ThirdPersonCamera>(out var orbit))
            orbit.ImpactAt(point, amplitude, duration);
    }

    public void Impact(Vector3 direction, float amplitude, float duration)
    {
        if (impactImpulse) impactImpulse.Play(direction, amplitude, duration);
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
        UpgradeFraming();
        pitch = lowAngleFraming ? framingStartPitch : startPitch;
        LockCursor(true);
        SnapToTarget();
    }

    /// <summary>確保 Main Camera 有 CinemachineBrain，並找到（或建立）CinemachineCamera 和注視點</summary>
    void EnsureCinemachine()
    {
        var brain = GetComponent<CinemachineBrain>();
        if (brain == null) brain = gameObject.AddComponent<CinemachineBrain>();
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
        brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
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
        impactImpulse = cinemachineCamera.GetComponent<CombatCameraImpulse>();
        if (!impactImpulse) impactImpulse = cinemachineCamera.gameObject.AddComponent<CombatCameraImpulse>();
    }

    void ConfigureFollow()
    {
        if (follow == null) return;
        follow.Damping = Vector3.zero;         // 平滑交給注視點的 SmoothDamp，鏡頭本身不再延遲
        follow.ShoulderOffset = Vector3.zero;  // 注視點已經在胸口 / 肩膀 / 頭的高度
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

    void Update()
    {
        float beforeYaw = yaw, beforePitch = pitch;
        HandleInput();
        NoteManualLook(beforeYaw, beforePitch);
        HandleLockInput();
    }

    void LateUpdate() // 在角色 Update 移動完之後才更新鏡頭，避免抖動
    {
        if (target == null || cameraTarget == null) return;
        UpdateLockOn(Time.unscaledDeltaTime);
        if (impactImpulse) impactImpulse.Intensity = cameraShakeIntensity;

        // 注視點（平滑跟隨）
        Vector3 pivot = target.position + FramedOffset() + ClimbPivotShift() + LockFramingOffset;
        if (targetMotor == null && target != null) targetMotor = target.GetComponent<ThirdPersonController>();
        AutoBehind();   // 進攻 / 翻滾 / 跳躍時轉到背後（ThirdPersonCamera.AutoBehind.cs）
        bool rolling = targetMotor != null && targetMotor.IsRolling;
        currentFollowSmooth = Mathf.MoveTowards(currentFollowSmooth <= 0f ? followSmoothTime : currentFollowSmooth,
            rolling ? Mathf.Max(followSmoothTime, rollFollowSmoothTime) : followSmoothTime, Time.deltaTime * (rolling ? 2f : .4f));
        smoothedPivot = currentFollowSmooth > 0f
            ? Vector3.SmoothDamp(smoothedPivot, pivot, ref pivotVelocity, currentFollowSmooth)
            : pivot;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 lookPoint = ClampPivot(smoothedPivot + rotation * Vector3.right * shoulderOffset);

        // 瞄準：注視點移到瞄準對象的肩膀、鏡頭拉近、畫面放大（平滑過渡）
        aimBlend = Mathf.MoveTowards(aimBlend, aiming && aimTarget != null ? 1f : 0f, aimBlendSpeed * Time.deltaTime);
        float dist = Mathf.Max(distance, LockFramingDistance) * Mathf.Lerp(1f, climbDistanceScale, Mathf.SmoothStep(0f, 1f, climbBlend));
        if (lowAngleFraming)
        {
            // 往上看：鏡頭拉近（原神：仰角越大鏡頭越貼近角色，角色留在畫面下方、背景是天空）
            float lookUp = Mathf.InverseLerp(0f, Mathf.Min(-1f, minPitch), pitch);
            dist *= Mathf.Lerp(1f, lookUpDistanceScale, Mathf.SmoothStep(0f, 1f, lookUp));
            // 鏡頭高度 = 注視點 - 往後拉的距離 × sin(仰角)：太低就再拉近，不讓鏡頭碰到地面（碰到的話防穿牆會把鏡頭推到角色身上）
            if (pitch < 0f)
            {
                float drop = Mathf.Sin(-pitch * Mathf.Deg2Rad);
                float room = lookPoint.y - (target.position.y + FeetOffset() + minCameraHeight);
                if (drop > .01f) dist = Mathf.Min(dist, Mathf.Max(.6f, room / drop));
            }
        }
        if (aimBlend > 0f && aimTarget != null)
        {
            float k = Mathf.SmoothStep(0f, 1f, aimBlend);
            Vector3 aimPoint = aimTarget.position + aimOffset + rotation * Vector3.right * aimShoulder;
            lookPoint = Vector3.Lerp(lookPoint, aimPoint, k);
            dist = Mathf.Lerp(distance, aimDistance, k);
        }
        if (cinemachineCamera != null)
            cinemachineCamera.Lens.FieldOfView = Mathf.Lerp(baseFov, aimFov, Mathf.SmoothStep(0f, 1f, aimBlend));

        // Cinemachine 的 Third Person Follow 會把鏡頭放在注視點正後方 distance 公尺（有牆就往前拉）
        bool fullView = keepPlayerVisible && follow != null && !CalligraphyPaintingWorld.BlocksTether;
        if (fullView) KeepPlayerVisible(lookPoint, ref rotation, ref dist);   // 會調整角度（抬高）和距離
        else { autoPitch = 0f; RestoreOccluders(true); }
        cameraTarget.SetPositionAndRotation(lookPoint, rotation);
        if (follow != null)
        {
            var avoid=follow.AvoidObstacles;
            bool painting=CalligraphyPaintingWorld.BlocksTether;
            avoid.Enabled=!painting && !fullView;follow.AvoidObstacles=avoid;
            if(painting)
                foreach(var hit in Physics.SphereCastAll(lookPoint,collisionRadius,-(rotation*Vector3.forward),dist,collisionMask,QueryTriggerInteraction.Ignore))
                    if(!hit.transform.IsChildOf(target) && CalligraphyWorldIsolation.Allows(hit.transform) && hit.distance>0)
                        dist=Mathf.Min(dist,Mathf.Max(.3f,hit.distance-.1f));
            follow.CameraDistance = dist;
        }
    }

    /// <summary>注視點相對腳底的位置：原神風格時高度跟著遠近變（拉近 = 胸口，拉遠 = 肩膀），否則就是 Target Offset</summary>
    Vector3 FramedOffset()
    {
        if (!lowAngleFraming) return targetOffset;
        float zoom = Mathf.InverseLerp(minDistance, maxDistance, distance);
        return new Vector3(targetOffset.x, FeetOffset() + Mathf.Lerp(framingPivotNear, framingPivotFar, zoom), targetOffset.z);
    }

    /// <summary>腳底相對角色根物件的高度：玩家的根物件在 CharacterController 膠囊的中心（腳底上方 1 公尺），不是在腳底</summary>
    float FeetOffset()
    {
        if (targetBodyOwner != target)
        {
            targetBodyOwner = target;
            targetBody = target != null ? target.GetComponent<CharacterController>() : null;
        }
        if (targetBody == null) return 0f;
        float scale = target.lossyScale.y;
        return (targetBody.center.y - targetBody.height * .5f) * scale;
    }

    /// <summary>攀爬時注視點要額外移動多少：身體離「平常的位置」多遠，就跟著移多遠（再稍微壓低）</summary>
    Vector3 ClimbPivotShift()
    {
        if (targetAnimatorOwner != target)
        {
            targetAnimatorOwner = target;
            targetAnimator = target.GetComponentInChildren<Animator>();
            targetHips = targetAnimator != null && targetAnimator.isHuman ? targetAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (targetHips != null) hipsLocal = target.InverseTransformPoint(targetHips.position);
        }
        bool climbing = targetHips != null && CalligraphyPlatformClimb.IsClimbing(target);
        climbBlend = Mathf.MoveTowards(climbBlend, climbing ? 1f : 0f, Time.deltaTime * 3f);
        if (targetHips == null) return Vector3.zero;
        if (climbing)
            climbShift = targetHips.position - target.TransformPoint(hipsLocal);
        else
        {
            hipsLocal = target.InverseTransformPoint(targetHips.position); // 記住平常的髖部位置
            climbShift = Vector3.Lerp(climbShift, Vector3.zero, 1f - Mathf.Exp(-8f * Time.deltaTime)); // 爬完平順地回到平常的跟隨
        }
        return climbShift + Vector3.down * (climbPivotDrop * Mathf.SmoothStep(0f, 1f, climbBlend));
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
        origin += climbShift; // 攀爬時從身體真正的位置檢查，不是從平台上方的根節點
        Vector3 to = lookPoint - origin;
        float dist = to.magnitude;
        if (dist < 0.01f) return lookPoint;
        Vector3 dir = to / dist;

        float nearest = float.MaxValue;
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, dir, hits, dist + pivotClearance,
                                               collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if(!CalligraphyWorldIsolation.Allows(hits[i].transform))continue;
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
        pointerUiOwners.RemoveWhere(owner=>!owner);
        if(pointerUiOwners.Count>0)return;
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame && !SkillAimCancellation.EscapeConsumedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);

#if ENABLE_LEGACY_INPUT_MANAGER
        if (kb == null && Input.GetKeyDown(KeyCode.Escape) && !SkillAimCancellation.EscapeConsumedThisFrame) LockCursor(false);
        if (mouse == null && Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);
#endif

        // 用手把操作時自動鎖定游標（技能瞄準中不鎖，右搖桿要拿去移動準心）
        if (Cursor.lockState != CursorLockMode.Locked && PadInput.AnyPressed && !SkillAimCancellation.IsAiming && !GameUI.OverlayOpen)
            LockCursor(true);

        if (Cursor.lockState != CursorLockMode.Locked || GameUI.OverlayOpen || Time.timeScale <= 0f) return;

        // 右搖桿轉鏡頭（每秒轉的角度；瞄準時一樣比較好微調）
        Vector2 look = PadInput.Look;
        if (look.sqrMagnitude > 0f)
        {
            float padSens = padLookSpeed * InputBindings.LookSensitivity * Mathf.Lerp(1f, aimSensitivityScale, aimBlend) * Time.unscaledDeltaTime;
            yaw += look.x * padSens;
            pitch += look.y * padSens * (invertY != InputBindings.InvertY ? 1f : -1f) * .7f;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        if (mouse != null && InputBindings.KeyboardEnabled) // 設定選「只用手把」時滑鼠不轉鏡頭
        {
            // 旋轉（滑鼠 delta 已經是每幀位移，不需要乘 deltaTime）；靈敏度 / 上下反轉可以在暫停選單「設定」裡調
            Vector2 delta = mouse.delta.ReadValue();
            float sens = mouseSensitivity * InputBindings.LookSensitivity * Mathf.Lerp(1f, aimSensitivityScale, aimBlend); // 瞄準時比較好微調
            yaw += delta.x * sens;
            pitch += delta.y * sens * (invertY != InputBindings.InvertY ? 1f : -1f);
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
    public void WarpWithTarget(Vector3 delta)
    {
        resolvedViewDistance = -1f;
        viewDistanceVelocity = 0f;
        smoothedPivot+=delta;
        if(cameraTarget)cameraTarget.position+=delta;
        transform.position+=delta;
        if(cinemachineCamera && cameraTarget)cinemachineCamera.OnTargetObjectWarped(cameraTarget,delta);
    }

    public void SnapToTarget()
    {
        if (target == null || cameraTarget == null) return;
        resolvedViewDistance = -1f;
        viewDistanceVelocity = 0f;
        autoPitch = autoPitchVelocity = 0f;
        smoothedPivot = target.position + FramedOffset() + ClimbPivotShift() + LockFramingOffset;
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

    readonly System.Collections.Generic.HashSet<UnityEngine.Object> pointerUiOwners=new();
    /// <summary>Temporary UI ownership prevents click-to-relock from swallowing a visible menu button.</summary>
    public void SetPointerUiOwner(UnityEngine.Object owner,bool active)
    {
        if(active && owner)pointerUiOwners.Add(owner);
        else pointerUiOwners.Remove(owner);
    }
}
