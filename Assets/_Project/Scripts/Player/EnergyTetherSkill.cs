using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 雙極能量牽引：中鍵進入五秒緩速瞄準、左鍵鎖定，滾輪上拉自己、下拉目標。
/// 第一次左鍵鎖定；第二次可在畫面任意位置按住拖甩，放開投擲。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnergyTetherSkill : MonoBehaviour
{
    [Header("瞄準")]
    [Range(.01f, .2f)] public float aimingTimeScale = .05f;
    [Min(1f)] public float aimingDuration = 5f;
    [Min(5f)] public float maximumRange = 90f;
    [Min(1f)] public float emptySpaceDistance = 30f;
    public LayerMask aimMask = ~0;

    [Header("牽引")]
    [Min(1f)] public float playerPullSpeed = 24f;
    [Min(1f)] public float objectPullSpeed = 19f;
    [Min(.1f)] public float objectHoverDistance = 2.4f;
    [Min(0f)] public float skillCooldown = .35f;
    [Header("滑鼠拖甩")]
    [Min(5f)] public float minimumThrowSpeed = 9f;
    [Min(5f)] public float maximumThrowSpeed = 32f;
    [Min(100f)] public float mouseSpeedForMaximumForce = 1800f;
    Vector2 dragStart, dragLast, dragVelocity;
    Vector2 dragDisplacement;
    float dragPeakSpeed;
    Vector3 dragCameraRight, dragCameraUp;
    bool dragging;
    string feedback;
    float feedbackUntil;
    TetherThrownMotion activeMotion;
    bool pullingPlayerControls, playerInputBeforePull;

    [Header("視覺")]
    [Range(5, 32)] public int beamSegments = 18;
    [Min(.01f)] public float beamWidth = .085f;
    [Min(0f)] public float beamJitter = .18f;
    [Min(.05f)] public float reticleRadius = .28f;

    struct AimPoint
    {
        public Collider collider;
        public Vector3 localPoint;
        public Vector3 localNormal;
        public Vector3 fallbackPoint;
        public bool hitSomething;

        public Vector3 Point => collider != null ? collider.transform.TransformPoint(localPoint) : fallbackPoint;
        public Vector3 Normal => collider != null ? collider.transform.TransformDirection(localNormal).normalized : Vector3.up;
    }

    ThirdPersonController movement;
    CharacterController body;
    PlayerHealth health;
    Camera aimCamera;
    ThirdPersonCamera orbitCamera;
    Animator animator;
    Transform leftHand, rightHand;
    LineRenderer reticle;
    readonly LineRenderer[] beams = new LineRenderer[4];
    Material lineMaterial;

    AimPoint hovered;
    AimPoint locked;
    bool aiming;
    bool selectionLocked;
    bool executing;
    bool aimingThrowDestination;
    float aimingStartedAt;
    float nextUseTime;
    float savedTimeScale = 1f;
    float savedFixedDelta = .02f;
    bool savedInputEnabled;
    CursorLockMode savedCursorLock;
    bool savedCursorVisible;

    Transform heldChunk;
    Rigidbody heldChunkBody;
    TetherInteractable heldChunkMarker;

    public bool IsAiming => aiming;
    public bool IsExecuting => executing;

    void Awake()
    {
        movement = GetComponent<ThirdPersonController>();
        body = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        ResolveCameraAndHands();
        CreateVisuals();
    }

    void ResolveCameraAndHands()
    {
        if (Camera.main != null)
        {
            aimCamera = Camera.main;
            orbitCamera = aimCamera.GetComponent<ThirdPersonCamera>();
        }
        animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
    }

    void Update()
    {
        if (Time.timeScale <= 0f) return;
        if (aimCamera == null) ResolveCameraAndHands();
        Mouse mouse = Mouse.current;
        if (mouse == null || aimCamera == null) return;

        if (!aiming && !executing && heldChunk == null && Cursor.lockState == CursorLockMode.Locked &&
            mouse.middleButton.wasPressedThisFrame && Time.unscaledTime >= nextUseTime && (health == null || !health.IsDead) &&
            (movement == null || !movement.ActionsLocked))
        {
            BeginAiming(false);
            return;
        }

        if (heldChunk != null) UpdateHeldChunkPosition();
        if (!aiming) return;

        if (orbitCamera != null && !dragging)
            orbitCamera.PanFromScreenEdge(mouse.position.ReadValue(), Time.unscaledDeltaTime);

        hovered = FindAimPoint(mouse.position.ReadValue());
        if (selectionLocked && !ValidPoint(locked)) { selectionLocked=false; dragging=false; }
        UpdateReticle(selectionLocked ? locked : hovered, selectionLocked || ValidPoint(hovered));

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SkillAimCancellation.CancelForEscape();
            return;
        }
        if (!dragging && Time.unscaledTime - aimingStartedAt >= aimingDuration)
        {
            CancelAiming();
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (!selectionLocked)
            {
                if (!ValidPoint(hovered)) { Notify("請瞄準實際物件，不能鎖定空氣"); return; }
                locked = hovered;
                selectionLocked = true;
                aimingStartedAt = Time.unscaledTime;
                // First click only selects. Its hold/release must never count as a throw.
                dragging = false;
                dragVelocity = Vector2.zero;
                UpdateReticle(locked, true);
                return;
            }
            if (aimingThrowDestination)
            {
                if (!ValidPoint(hovered)) return;
                Vector3 destination = hovered.Point;
                EndAiming(false);
                ThrowHeldChunk(destination);
                return;
            }
            if (IsStaticAnchor(locked)) { Notify("無法拖動此目標"); return; }
            dragging=true; dragStart=dragLast=mouse.position.ReadValue(); dragVelocity=Vector2.zero;
            dragDisplacement=Vector2.zero;
            dragPeakSpeed=0f;
            dragCameraRight=aimCamera.transform.right;
            dragCameraUp=aimCamera.transform.up;
        }

        if (dragging && (mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame))
        {
            Vector2 current=mouse.position.ReadValue();
            Vector2 delta=mouse.delta.ReadValue();
            // Raw motion still works at the screen edge; position is a fallback.
            if (delta.sqrMagnitude<.001f) delta=current-dragLast;
            dragDisplacement+=delta;
            Vector2 speed=delta/Mathf.Max(.001f,Time.unscaledDeltaTime);
            dragVelocity=Vector2.Lerp(dragVelocity,speed,1-Mathf.Exp(-18*Time.unscaledDeltaTime));
            dragPeakSpeed=Mathf.Max(dragPeakSpeed,dragVelocity.magnitude);
            dragLast=current;
        }
        if (dragging && mouse.leftButton.wasReleasedThisFrame)
        {
            dragging=false;
            aimingStartedAt=Time.unscaledTime;
            Vector2 gesture=dragDisplacement;
            if (gesture.magnitude>=8f)
            {
                if (IsStaticAnchor(locked)) { Notify("無法拖動此目標"); return; }
                AimPoint thrownPoint=locked;
                float force=Mathf.Lerp(minimumThrowSpeed,maximumThrowSpeed,Mathf.Clamp01(dragPeakSpeed/mouseSpeedForMaximumForce));
                // Screen-relative swipe only: never re-raycast a destination or require
                // the pointer to remain over the locked target. Gravity handles landing.
                Vector3 direction=(dragCameraRight*gesture.x + dragCameraUp*gesture.y).normalized;
                EndAiming(false);
                StartCoroutine(ThrowTarget(thrownPoint,direction*force));
                return;
            }
            Notify("按住左鍵移動滑鼠，再放開甩出");
        }

        if (!selectionLocked) return;
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < .01f) return;
        AimPoint chosen = locked;
        if (scroll < 0 && IsStaticAnchor(chosen)) { Notify("無法拖動此目標"); return; }
        bool pullSelf = scroll > 0f;
        EndAiming(false);
        StartCoroutine(pullSelf ? PullPlayer(chosen) : PullTarget(chosen));
    }

    void BeginAiming(bool throwDestination)
    {
        if (aiming || executing || Time.timeScale <= 0f || !SkillAimCancellation.Register(CancelAiming)) return;
        aiming = true;
        aimingThrowDestination = throwDestination;
        selectionLocked = false;
        dragging=false;
        aimingStartedAt = Time.unscaledTime;
        savedTimeScale = Time.timeScale;
        savedFixedDelta = Time.fixedDeltaTime;
        savedCursorLock = Cursor.lockState;
        savedCursorVisible = Cursor.visible;
        savedInputEnabled = movement == null || movement.inputEnabled;
        if (movement != null)
        {
            movement.inputEnabled = false;
            movement.ResetMotion();
        }
        Time.timeScale = aimingTimeScale;
        Time.fixedDeltaTime = Mathf.Max(.0001f, savedFixedDelta * aimingTimeScale / Mathf.Max(.0001f, savedTimeScale));
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void EndAiming(bool restoreHeldChunk)
    {
        if (!aiming) return;
        aiming = false;
        SkillAimCancellation.Release(CancelAiming);
        aimingThrowDestination = false;
        selectionLocked = false;
        Time.timeScale = savedTimeScale;
        dragging=false;
        Time.fixedDeltaTime = savedFixedDelta;
        Cursor.lockState = savedCursorLock;
        Cursor.visible = savedCursorVisible;
        if (movement != null) movement.inputEnabled = savedInputEnabled;
        UpdateReticle(default, false);
        if (restoreHeldChunk) ReleaseHeldChunk(false, Vector3.zero);
    }

    void CancelAiming()
    {
        bool release = aimingThrowDestination && heldChunk != null;
        EndAiming(release);
    }

    AimPoint FindAimPoint(Vector2 pointer)
    {
        Ray ray = aimCamera.ScreenPointToRay(pointer);
        RaycastHit[] hits = Physics.RaycastAll(ray, maximumRange, aimMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (heldChunk != null && hit.transform.IsChildOf(heldChunk)) continue;
            // Ignore interaction volumes, VFX triggers and empty fallback aim zones.
            if (hit.collider.isTrigger && !(hit.collider.name == "SkillAimZone" && hit.collider.GetComponentInParent<IDamageable>() != null)) continue;
            return new AimPoint
            {
                collider = hit.collider,
                localPoint = hit.collider.transform.InverseTransformPoint(hit.point),
                localNormal = hit.collider.transform.InverseTransformDirection(hit.normal),
                fallbackPoint = hit.point,
                hitSomething = true
            };
        }
        return new AimPoint
        {
            fallbackPoint = ray.GetPoint(Mathf.Min(emptySpaceDistance, maximumRange)),
            localNormal = -ray.direction,
            hitSomething = false
        };
    }

    bool ValidPoint(AimPoint p) => p.hitSomething && p.collider != null && p.collider.enabled &&
        p.collider.gameObject.activeInHierarchy && Vector3.Distance(transform.position,p.Point)<=maximumRange;
    void Notify(string message) { feedback=message; feedbackUntil=Time.unscaledTime+2f; }

    IEnumerator ThrowTarget(AimPoint point, Vector3 velocity)
    {
        Transform root=ResolveTargetRoot(point);
        if (!root || !ValidPoint(point)) yield break;
        executing=true;
        activeMotion=root.GetComponent<TetherThrownMotion>() ?? root.gameObject.AddComponent<TetherThrownMotion>();
        activeMotion.Hold();
        // Brief tension build; do not teleport the target to the destination.
        for(float t=0;t<.1f;t+=Time.deltaTime) { if (!root) break; UpdateBeams(point.Point); yield return null; }
        if (activeMotion) activeMotion.Release(velocity,true);
        activeMotion=null; SetBeams(false); executing=false;
        nextUseTime=Time.unscaledTime+skillCooldown;
    }

    bool IsStaticAnchor(AimPoint point)
    {
        if (point.collider == null) return true;
        TetherInteractable marker = point.collider.GetComponentInParent<TetherInteractable>();
        if (marker != null)
        {
            if (marker.mode == TetherInteractable.TetherMode.StaticAnchor) return true;
            if (marker.mode == TetherInteractable.TetherMode.Pullable || marker.mode == TetherInteractable.TetherMode.FloatingTerrainChunk) return false;
        }
        if (point.collider.attachedRigidbody != null && !point.collider.attachedRigidbody.isKinematic) return false;
        if (point.collider.GetComponentInParent<IDamageable>() != null) return false;
        return point.collider.GetComponentInParent<ThirdPersonController>() == null;
    }

    IEnumerator PullPlayer(AimPoint targetPoint)
    {
        if (!ValidPoint(targetPoint)) yield break;
        executing = true;
        nextUseTime = Time.unscaledTime + skillCooldown;
        bool previousInput = movement == null || movement.inputEnabled;
        playerInputBeforePull=previousInput; pullingPlayerControls=true;
        if (movement != null) { movement.inputEnabled = false; movement.ResetMotion(); }
        Vector3 start = transform.position;
        Vector3 normal = targetPoint.Normal.sqrMagnitude > .001f ? targetPoint.Normal : Vector3.up;
        float stop = 1.05f;
        if (targetPoint.collider != null)
        {
            TetherInteractable marker = targetPoint.collider.GetComponentInParent<TetherInteractable>();
            if (marker != null) stop = marker.playerStopDistance;
        }
        Vector3 destination = targetPoint.Point + normal * stop;
        float distance = Vector3.Distance(start, destination);
        float duration = Mathf.Clamp(distance / playerPullSpeed, .16f, 1.35f);
        SetBeams(true);
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            Vector3 desired = Vector3.Lerp(start, destination, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * Mathf.Min(1.4f, distance * .08f));
            MovePlayer(desired - transform.position);
            FaceFlat(destination - transform.position);
            UpdateBeams(targetPoint.Point);
            yield return null;
        }
        SetBeams(false);
        if (movement != null) movement.inputEnabled = previousInput;
        pullingPlayerControls=false;
        executing = false;
    }

    IEnumerator PullTarget(AimPoint targetPoint)
    {
        Transform targetRoot = ResolveTargetRoot(targetPoint);
        if (targetRoot == null || !ValidPoint(targetPoint)) yield break;
        executing = true;
        nextUseTime = Time.unscaledTime + skillCooldown;
        Rigidbody rb = targetRoot.GetComponent<Rigidbody>();
        TetherInteractable marker = targetRoot.GetComponent<TetherInteractable>() ?? targetRoot.GetComponentInChildren<TetherInteractable>();
        activeMotion=targetRoot.GetComponent<TetherThrownMotion>() ?? targetRoot.gameObject.AddComponent<TetherThrownMotion>();
        activeMotion.Hold();

        Vector3 start = targetRoot.position;
        Vector3 destination = transform.position + transform.forward * objectHoverDistance + Vector3.up * 1.15f;
        float duration = Mathf.Clamp(Vector3.Distance(start, destination) / objectPullSpeed, .18f, 1.25f);
        SetBeams(true);
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            if (!targetRoot) { executing=false; SetBeams(false); yield break; }
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            targetRoot.position = Vector3.Lerp(start, destination, u);
            UpdateBeams(targetRoot.position);
            yield return null;
        }
        SetBeams(false);

        // Pulling completes this cast, including floating chunks. Throwing is chosen
        // with the second left-button drag while the initial target remains locked.
        if (activeMotion) activeMotion.Release(Vector3.zero,false);
        activeMotion=null;
        executing = false;
    }

    Transform ResolveTargetRoot(AimPoint point)
    {
        if (point.collider == null) return null;
        TetherInteractable marker = point.collider.GetComponentInParent<TetherInteractable>();
        if (marker != null) return marker.transform;
        if (point.collider.attachedRigidbody != null) return point.collider.attachedRigidbody.transform;
        IDamageable damageable = point.collider.GetComponentInParent<IDamageable>();
        if (damageable is Component owner) return owner.transform;
        ThirdPersonController controller = point.collider.GetComponentInParent<ThirdPersonController>();
        return controller != null ? controller.transform : null;
    }

    void ThrowHeldChunk(Vector3 destination)
    {
        if (heldChunk == null) return;
        Vector3 origin = heldChunk.position;
        Vector3 direction = (destination - origin).normalized;
        float speed = heldChunkMarker != null ? heldChunkMarker.throwSpeed : 24f;
        ReleaseHeldChunk(true, direction * speed);
        ThirdPersonCamera.ShakeMain(.18f, .18f);
        nextUseTime = Time.unscaledTime + skillCooldown;
    }

    void ReleaseHeldChunk(bool throwIt, Vector3 velocity)
    {
        if (activeMotion) { activeMotion.Release(throwIt ? velocity : Vector3.zero,throwIt); activeMotion=null; }
        else
        if (heldChunkBody != null)
        {
            heldChunkBody.isKinematic = !throwIt;
            heldChunkBody.useGravity = throwIt;
            if (throwIt) heldChunkBody.linearVelocity = velocity;
        }
        heldChunk = null;
        heldChunkBody = null;
        heldChunkMarker = null;
    }

    void UpdateHeldChunkPosition()
    {
        if (heldChunk == null) return;
        Vector3 hover = transform.position + transform.forward * objectHoverDistance + Vector3.up * 1.15f;
        heldChunk.position = Vector3.Lerp(heldChunk.position, hover, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        heldChunk.Rotate(Vector3.up, 38f * Time.unscaledDeltaTime, Space.World);
    }

    void MovePlayer(Vector3 delta)
    {
        if (body != null && body.enabled) body.Move(delta);
        else transform.position += delta;
    }

    void FaceFlat(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    void CreateVisuals()
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) lineMaterial = new Material(shader) { name = "Runtime_BipolarTether" };

        GameObject ring = new GameObject("EnergyTether_Reticle");
        ring.transform.SetParent(transform, false);
        reticle = ring.AddComponent<LineRenderer>();
        ConfigureLine(reticle, .04f, true);
        reticle.positionCount = 48;
        reticle.startColor = reticle.endColor = new Color(1f, .05f, .04f, .95f);
        reticle.enabled = false;

        for (int i = 0; i < beams.Length; i++)
        {
            GameObject beam = new GameObject("EnergyTether_Beam_" + i);
            beam.transform.SetParent(transform, false);
            beams[i] = beam.AddComponent<LineRenderer>();
            ConfigureLine(beams[i], i >= 2 ? beamWidth * .5f : beamWidth * 1.2f, false);
            beams[i].positionCount = Mathf.Max(5, beamSegments);
            beams[i].colorGradient = CreateBeamGradient(i % 2 == 0);
            beams[i].widthCurve = new AnimationCurve(new Keyframe(0,.25f),new Keyframe(.12f,1f),new Keyframe(.85f,.7f),new Keyframe(1,.12f));
            if(i>=2) beams[i].startColor = beams[i].endColor = new Color(1f, .96f, 1f, 1f);
            beams[i].enabled = false;
            if (i < 2) beam.AddComponent<LightningFilamentBundle>().Initialize(beams[i], 22, .19f);
        }
    }

    void ConfigureLine(LineRenderer line, float width, bool loop)
    {
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = width;
        line.numCornerVertices = 3;
        line.numCapVertices = 3;
        line.sharedMaterial = lineMaterial;
    }

    static Gradient CreateBeamGradient(bool redFirst)
    {
        Gradient gradient = new Gradient();
        Color red = new Color(1f, .035f, .04f, 1f);
        Color blue = new Color(.02f, .35f, 1f, 1f);
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(redFirst ? red : blue, 0f),
                new GradientColorKey(Color.white, .48f),
                new GradientColorKey(redFirst ? blue : red, 1f)
            },
            new[] { new GradientAlphaKey(.95f, 0f), new GradientAlphaKey(1f, .5f), new GradientAlphaKey(.85f, 1f) });
        return gradient;
    }

    void UpdateReticle(AimPoint point, bool visible)
    {
        if (reticle == null) return;
        reticle.enabled = visible;
        if (!visible) { SetBeams(false); return; }
        Vector3 normal = point.Normal.sqrMagnitude > .001f ? point.Normal.normalized : Vector3.up;
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < .001f) tangent = Vector3.Cross(normal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
        Vector3 center = point.Point + normal * .025f;
        float pulse = reticleRadius * (1f + Mathf.Sin(Time.unscaledTime * 8f) * .08f);
        for (int i = 0; i < reticle.positionCount; i++)
        {
            float angle = i / (float)reticle.positionCount * Mathf.PI * 2f;
            reticle.SetPosition(i, center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * pulse);
        }
        Color color = selectionLocked ? new Color(1f, .8f, .12f, 1f) : new Color(1f, .04f, .035f, .95f);
        reticle.startColor = reticle.endColor = color;
        if (selectionLocked) UpdateBeams(point.Point);
        else SetBeams(false);
    }

    void SetBeams(bool enabled)
    {
        foreach (LineRenderer beam in beams) if (beam != null) beam.enabled = enabled;
    }

    void UpdateBeams(Vector3 targetPoint)
    {
        SetBeams(true);
        Vector3 fallbackLeft = transform.position + transform.up * 1.25f - transform.right * .25f;
        Vector3 fallbackRight = transform.position + transform.up * 1.25f + transform.right * .25f;
        UpdateBeamPair(0, leftHand != null ? leftHand.position : fallbackLeft, targetPoint, -1f);
        UpdateBeamPair(1, rightHand != null ? rightHand.position : fallbackRight, targetPoint, 1f);
    }

    void UpdateBeamPair(int side, Vector3 start, Vector3 end, float phase)
    {
        Vector3 direction = end - start;
        Vector3 sideAxis = Vector3.Cross(direction.normalized, aimCamera != null ? aimCamera.transform.forward : Vector3.forward);
        if (sideAxis.sqrMagnitude < .001f) sideAxis = transform.right;
        sideAxis.Normalize();
        Vector3 upAxis = Vector3.Cross(direction.normalized, sideAxis).normalized;
        int count = Mathf.Max(5, beamSegments);
        for (int layer = 0; layer < 2; layer++)
        {
            LineRenderer line = beams[side + layer * 2];
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float u = i / (float)(count - 1);
                float envelope = Mathf.Sin(u * Mathf.PI);
                // Hold branching contours briefly rather than boiling every frame.
                float tick = Mathf.Floor(Time.unscaledTime * 22f);
                float noiseA = Mathf.PerlinNoise(i * .73f + side * 7f, tick*.37f) * 2f - 1f;
                float noiseB = Mathf.PerlinNoise(i * 1.17f + 14f, tick*.41f + side * 5f) * 2f - 1f;
                // Core and colored envelope share the same path so they read as one bolt.
                float amount = beamJitter * .65f;
                Vector3 point = Vector3.Lerp(start, end, u) + (sideAxis * noiseA + upAxis * noiseB) * amount * envelope;
                line.SetPosition(i, point);
            }
        }
    }

    void OnGUI()
    {
        if (Time.unscaledTime<feedbackUntil)
            GUI.Box(new Rect(Screen.width*.5f-180,Screen.height*.45f,360,40),feedback);
        if (!aiming) return;
        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        string text = aimingThrowDestination
            ? "浮空地塊：左鍵指定投擲位置　Esc 取消"
            : selectionLocked
                ? "已鎖定：任意位置按住左鍵甩動後放開　滾輪↑ 拉自己／↓ 拉目標"
                : "第一次左鍵只鎖定目標　游標移到左右邊緣可轉視角";
        GUI.Box(new Rect(Screen.width * .5f - 270f, Screen.height - 92f, 540f, 42f), text, style);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (aiming) EndAiming(true);
        if (activeMotion) activeMotion.Release(Vector3.zero,false);
        activeMotion=null; executing=false; dragging=false;
        if (pullingPlayerControls && movement) movement.inputEnabled=playerInputBeforePull;
        pullingPlayerControls=false;
        SetBeams(false);
        UpdateReticle(default, false);
    }

    void OnDestroy()
    {
        if (lineMaterial != null) Destroy(lineMaterial);
    }
}
