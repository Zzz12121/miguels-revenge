using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class VoidRendSkillDemo
{
    enum PreviewSkill { None, VoidRend, CatPounce, CrescentDash, SkyfallCross }

    [Header("Target selection and practice movement")]
    [Range(0.01f, 0.15f)] public float targetingTimeScale = 0.035f;
    [Min(1f)] public float targetingTimeout = 5f;
    [Min(1f)] public float previewMoveSpeed = 4.2f;
    [Min(90f)] public float previewTurnSpeed = 720f;
    public bool followCasterOutsideSkills = true;

    [Header("Shared skill combat")]
    [Min(0f)] public float targetSkillDamage = 500f;
    [Min(0f)] public float voidRendDamage = 1400f;
    [Min(0f)] public float voidRendDashDamage = 100f;

    struct TargetSelection
    {
        public VoidRendTargetable marker;
        public Collider collider;
        public Vector3 localPoint;
        public Vector3 localNormal;

        public bool IsValid => marker != null;
        public Vector3 WorldPoint => collider != null ? collider.transform.TransformPoint(localPoint) : marker != null ? marker.AimPosition : Vector3.zero;
        public Vector3 WorldNormal => collider != null ? collider.transform.TransformDirection(localNormal).normalized : Vector3.up;
    }

    PreviewSkill pendingSkill;
    VoidRendTargetable hoveredTarget;
    TargetSelection hoveredSelection;
    TargetSelection lockedSelection;
    LineRenderer targetingReticle;
    float targetingStarted;
    float timeScaleBeforeTargeting = 1f;
    Vector3 cameraFollowOffset;
    Vector3 cameraLookOffset;
    PlayerWeaponPresenter weaponPresenter;
    ThirdPersonController practiceController;
    CharacterController practiceBody;
    ThirdPersonCamera practiceOrbitCamera;
    PlayerVisualAnimator practiceAnimatorDriver;
    bool practiceControlsSuspended;

    bool IsTargeting => pendingSkill != PreviewSkill.None;

    void InitializeTargetingAndMovement()
    {
        if (caster == null) return;
        weaponPresenter = caster.GetComponentInChildren<PlayerWeaponPresenter>(true);
        if (casterAnimator == null) casterAnimator = caster.GetComponentInChildren<Animator>(true);
        practiceController = caster.GetComponent<ThirdPersonController>();
        practiceBody = caster.GetComponent<CharacterController>();
        practiceAnimatorDriver = caster.GetComponent<PlayerVisualAnimator>();
        if (skillCamera != null)
        {
            cameraFollowOffset = skillCamera.transform.position - caster.position;
            cameraLookOffset = Vector3.up * 1.25f;
            practiceOrbitCamera = skillCamera.GetComponent<ThirdPersonCamera>();
            if (practiceOrbitCamera != null)
            {
                practiceOrbitCamera.target = caster;
                practiceOrbitCamera.targetOffset = Vector3.up * 1.35f;
                practiceOrbitCamera.distance = Mathf.Clamp(cameraFollowOffset.magnitude, 4.5f, 7f);
                practiceOrbitCamera.SnapToTarget();
            }
        }
        if (practiceController != null && skillCamera != null)
            practiceController.cameraTransform = skillCamera.transform;
        CreateTargetingReticle();
    }

    void UpdatePreviewInputAndTargeting()
    {
        if (Keyboard.current == null) return;

        if (IsTargeting)
        {
            UpdateTargetSelection();
            return;
        }

        if (!playing)
        {
            if (Keyboard.current.zKey.wasPressedThisFrame) BeginTargeting(PreviewSkill.VoidRend);
            else if (Keyboard.current.cKey.wasPressedThisFrame) BeginTargeting(PreviewSkill.CatPounce);
            else if (Keyboard.current.qKey.wasPressedThisFrame) BeginTargeting(PreviewSkill.CrescentDash);
            else if (Keyboard.current.eKey.wasPressedThisFrame) BeginTargeting(PreviewSkill.SkyfallCross);
            else UpdatePracticeMovement();
        }
    }

    void UpdatePracticeMovement()
    {
        if (caster == null || skillCamera == null) return;
        // 靶場有正式關卡同款的 ThirdPersonController 時，移動、重力和空白跳躍都交給它。
        if (practiceController != null) return;

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 forward = Vector3.ProjectOnPlane(skillCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(skillCamera.transform.right, Vector3.up).normalized;
        Vector3 move = forward * input.y + right * input.x;
        if (move.sqrMagnitude > 0.001f)
        {
            caster.position += move.normalized * (previewMoveSpeed * Time.deltaTime);
            caster.rotation = Quaternion.RotateTowards(caster.rotation, Quaternion.LookRotation(move), previewTurnSpeed * Time.deltaTime);
        }

        if (casterAnimator != null)
        {
            casterAnimator.SetBool("Grounded", true);
            casterAnimator.SetFloat("VerticalSpeed", 0f);
            casterAnimator.SetFloat("Speed", input.magnitude * 0.82f, 0.09f, Time.deltaTime);
        }
    }

    void UpdatePracticeCamera()
    {
        if (!followCasterOutsideSkills || playing || IsTargeting || skillCamera == null || caster == null) return;
        // 有 ThirdPersonCamera 時由滑鼠環繞鏡頭；下面只保留舊場景的固定鏡頭 fallback。
        if (practiceOrbitCamera != null && practiceOrbitCamera.enabled) return;
        Vector3 desired = caster.position + cameraFollowOffset;
        skillCamera.transform.position = Vector3.Lerp(skillCamera.transform.position, desired, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
        Quaternion look = Quaternion.LookRotation(caster.position + cameraLookOffset - skillCamera.transform.position, Vector3.up);
        skillCamera.transform.rotation = Quaternion.Slerp(skillCamera.transform.rotation, look, 1f - Mathf.Exp(-7f * Time.unscaledDeltaTime));
    }

    void BeginTargeting(PreviewSkill skill)
    {
        if (playing || skill == PreviewSkill.None) return;
        pendingSkill = skill;
        targetingStarted = Time.unscaledTime;
        timeScaleBeforeTargeting = Time.timeScale;
        Time.timeScale = targetingTimeScale;
        Time.fixedDeltaTime = fixedDeltaStart * targetingTimeScale;
        SetTargetingControls(true);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        hoveredSelection = FindTargetUnderPointer();
        hoveredTarget = hoveredSelection.marker;
        UpdateTargetingReticle(hoveredSelection);
        Stage = "Targeting " + skill;
    }

    void UpdateTargetSelection()
    {
        hoveredSelection = FindTargetUnderPointer();
        hoveredTarget = hoveredSelection.marker;
        UpdateTargetingReticle(hoveredSelection);
        if (Keyboard.current.escapeKey.wasPressedThisFrame || Time.unscaledTime - targetingStarted >= targetingTimeout)
        {
            CancelTargeting();
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && hoveredTarget != null)
        {
            PreviewSkill selectedSkill = pendingSkill;
            Transform selectedTarget = hoveredTarget.transform;
            lockedSelection = hoveredSelection;
            RestoreTargetingTime();
            pendingSkill = PreviewSkill.None;
            UpdateTargetingReticle(default);
            target = selectedTarget;
            PrepareSkillOrigin();
            SuspendPracticeControlsForSkill();

            switch (selectedSkill)
            {
                case PreviewSkill.VoidRend: StartCoroutine(PlaySequence()); break;
                case PreviewSkill.CatPounce: StartCoroutine(PlayCatPounce()); break;
                case PreviewSkill.CrescentDash: StartCoroutine(PlayCrescentDash()); break;
                case PreviewSkill.SkyfallCross: StartCoroutine(PlaySkyfallCross()); break;
            }
        }
    }

    TargetSelection FindTargetUnderPointer()
    {
        if (skillCamera == null || Mouse.current == null) return default;
        Vector2 pointer = Mouse.current.position.ReadValue();
        Ray ray = skillCamera.ScreenPointToRay(pointer);
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (caster != null && hit.transform.IsChildOf(caster)) continue;
            VoidRendTargetable marker = hit.transform.GetComponentInParent<VoidRendTargetable>();
            if (marker == null)
            {
                // 正式關卡的既有敵人已經用 IDamageable，無須逐隻手動加靶場標記。
                // 第一次被滑鼠指到時補上輕量標記，之後即可共用鎖定框與受擊動畫。
                IDamageable damageable = hit.transform.GetComponentInParent<IDamageable>();
                if (damageable != null && !damageable.IsDead && damageable is Component owner)
                    marker = owner.GetComponent<VoidRendTargetable>() ?? owner.gameObject.AddComponent<VoidRendTargetable>();
            }
            if (marker != null)
            {
                return new TargetSelection
                {
                    marker = marker,
                    collider = hit.collider,
                    localPoint = hit.collider.transform.InverseTransformPoint(hit.point),
                    localNormal = hit.collider.transform.InverseTransformDirection(hit.normal).normalized
                };
            }
        }

        // 小型或沒有完整碰撞器的互動物件，仍可用畫面上的最近標記選取。
        VoidRendTargetable best = null;
        float bestPixels = 72f;
        foreach (VoidRendTargetable marker in FindObjectsByType<VoidRendTargetable>(FindObjectsInactive.Exclude))
        {
            IDamageable damageable = marker.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsDead) continue;
            Vector3 screen = skillCamera.WorldToScreenPoint(marker.AimPosition);
            if (screen.z <= 0f) continue;
            float pixels = Vector2.Distance(pointer, new Vector2(screen.x, screen.y));
            if (pixels < bestPixels) { bestPixels = pixels; best = marker; }
        }
        return new TargetSelection { marker = best };
    }

    void CreateTargetingReticle()
    {
        if (targetingReticle != null) return;
        GameObject go = new GameObject("VoidRend_TargetReticle");
        go.transform.SetParent(effectsRoot != null ? effectsRoot : transform, false);
        targetingReticle = go.AddComponent<LineRenderer>();
        targetingReticle.useWorldSpace = true;
        targetingReticle.loop = true;
        targetingReticle.positionCount = 48;
        targetingReticle.widthMultiplier = .035f;
        targetingReticle.numCornerVertices = 3;
        targetingReticle.sharedMaterial = slashMaterial;
        targetingReticle.startColor = new Color(1f, .04f, .025f, .98f);
        targetingReticle.endColor = targetingReticle.startColor;
        targetingReticle.enabled = false;
    }

    void UpdateTargetingReticle(TargetSelection selection)
    {
        if (targetingReticle == null) CreateTargetingReticle();
        if (targetingReticle == null) return;
        targetingReticle.enabled = IsTargeting && selection.IsValid;
        if (!targetingReticle.enabled) return;

        Vector3 normal = selection.WorldNormal;
        if (normal.sqrMagnitude < .001f) normal = skillCamera != null ? -skillCamera.transform.forward : Vector3.up;
        normal.Normalize();
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < .001f) tangent = Vector3.Cross(normal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
        Vector3 center = selection.WorldPoint + normal * .018f;
        const float radius = .24f;
        for (int i = 0; i < targetingReticle.positionCount; i++)
        {
            float angle = i / (float)targetingReticle.positionCount * Mathf.PI * 2f;
            targetingReticle.SetPosition(i, center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius);
        }
    }

    void CancelTargeting()
    {
        pendingSkill = PreviewSkill.None;
        hoveredTarget = null;
        hoveredSelection = default;
        UpdateTargetingReticle(default);
        RestoreTargetingTime();
        SetTargetingControls(false);
        Stage = "Ready";
    }

    void RestoreTargetingTime()
    {
        Time.timeScale = timeScaleBeforeTargeting;
        Time.fixedDeltaTime = fixedDeltaStart;
    }

    void SetTargetingControls(bool targeting)
    {
        if (practiceController != null)
            practiceController.inputEnabled = !targeting;
        if (practiceOrbitCamera != null)
        {
            practiceOrbitCamera.enabled = !targeting;
            if (!targeting)
            {
                practiceOrbitCamera.target = caster;
                practiceOrbitCamera.SnapToTarget();
            }
        }
        if (!targeting)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    void SuspendPracticeControlsForSkill()
    {
        if (practiceControlsSuspended) return;
        practiceControlsSuspended = true;
        if (practiceController != null)
        {
            practiceController.inputEnabled = false;
            practiceController.ResetMotion();
            practiceController.enabled = false;
        }
        if (practiceBody != null) practiceBody.enabled = false;
        if (practiceAnimatorDriver != null) practiceAnimatorDriver.enabled = false;
        if (practiceOrbitCamera != null) practiceOrbitCamera.enabled = false;
    }

    void RestorePracticeControlsAfterSkill()
    {
        practiceControlsSuspended = false;
        if (practiceBody != null) practiceBody.enabled = true;
        if (practiceController != null)
        {
            practiceController.enabled = true;
            practiceController.inputEnabled = true;
            practiceController.ResetMotion();
        }
        if (practiceAnimatorDriver != null) practiceAnimatorDriver.enabled = true;
        if (practiceOrbitCamera != null)
        {
            practiceOrbitCamera.target = caster;
            practiceOrbitCamera.enabled = true;
            practiceOrbitCamera.SnapToTarget();
        }
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void PrepareSkillOrigin()
    {
        if (caster == null) return;
        casterStartPosition = caster.position;
        casterStartRotation = caster.rotation;
        if (skillCamera != null)
        {
            cameraStartPosition = skillCamera.transform.position;
            cameraStartRotation = skillCamera.transform.rotation;
            cameraStartFov = skillCamera.fieldOfView;
            cameraFollowOffset = cameraStartPosition - casterStartPosition;
        }
    }

    IEnumerator PlayCatPounce()
    {
        SuspendPracticeControlsForSkill();
        playing = true;
        cinematicCamera = false;
        Vector3 origin = casterStartPosition;
        Quaternion originRotation = casterStartRotation;
        SetStage("CatPounceCharge");
        if (casterAnimator != null) casterAnimator.CrossFadeInFixedTime("Jump", 0.08f, 0, 0f);
        SpawnRing(origin + Vector3.up * 0.08f, 0.2f, 1.45f, 0.07f, 0.55f, cyan);
        yield return Wait(0.22f);

        if (weaponPresenter) weaponPresenter.BeginSkillDraw();
        SetStage("CatPounceLeap");
        Vector3 leapStart = caster.position;
        for (float t = 0f; t < 0.42f; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / 0.42f);
            Vector3 liveAim = TargetAimPosition();
            Vector3 flatDirection = Vector3.ProjectOnPlane(liveAim - leapStart, Vector3.up).normalized;
            Vector3 destination = liveAim - flatDirection * 1.05f;
            destination.y = origin.y;
            caster.position = Vector3.Lerp(leapStart, destination, Mathf.SmoothStep(0f, 1f, u)) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 1.25f);
            FaceCurrentTarget();
            yield return null;
        }

        SetStage("CatPounceDoubleCut");
        PlaySwordAnimation(0);
        EmitSkillCut(-1f);
        HitCurrentTarget();
        yield return Wait(0.42f);
        PlaySwordAnimation(1);
        EmitSkillCut(1f);
        HitCurrentTarget();
        yield return Wait(0.42f);

        SetStage("CatPounceReturn");
        Vector3 returnStart = caster.position;
        if (casterAnimator != null) casterAnimator.CrossFadeInFixedTime("Jump", 0.08f, 0, 0f);
        for (float t = 0f; t < 0.46f; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / 0.46f);
            caster.position = Vector3.Lerp(returnStart, origin, Mathf.SmoothStep(0f, 1f, u)) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 1.05f);
            Vector3 direction = origin - caster.position; direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f) caster.rotation = Quaternion.LookRotation(direction);
            yield return null;
        }
        FinishStandaloneSkill(origin, originRotation);
    }

    IEnumerator PlayCrescentDash()
    {
        SuspendPracticeControlsForSkill();
        playing = true;
        Vector3 origin = casterStartPosition;
        Quaternion originRotation = casterStartRotation;
        if (weaponPresenter) weaponPresenter.BeginSkillDraw();
        SetStage("CrescentDash");
        Vector3 start = caster.position;
        PlaySwordAnimation(0);
        for (float t = 0f; t < 0.36f; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / 0.36f);
            Vector3 aim = TargetAimPosition();
            Vector3 direction = Vector3.ProjectOnPlane(aim - start, Vector3.up).normalized;
            Vector3 destination = aim + direction * 1.2f; destination.y = origin.y;
            caster.position = Vector3.Lerp(start, destination, Mathf.SmoothStep(0f, 1f, u));
            FaceCurrentTarget();
            yield return null;
        }
        FracturedCut(start + Vector3.up, caster.position + Vector3.up, 0.09f, 1.4f, true);
        SpawnRing(TargetAimPosition(), 0.15f, 2.5f, 0.09f, 0.7f, pale);
        HitCurrentTarget();
        yield return Wait(0.5f);
        FinishStandaloneSkill(caster.position, caster.rotation, false);
    }

    IEnumerator PlaySkyfallCross()
    {
        SuspendPracticeControlsForSkill();
        playing = true;
        Vector3 origin = casterStartPosition;
        Quaternion originRotation = casterStartRotation;
        if (weaponPresenter) weaponPresenter.BeginSkillDraw();
        SetStage("SkyfallRise");
        if (casterAnimator != null) casterAnimator.CrossFadeInFixedTime("Jump", 0.06f, 0, 0f);
        Vector3 start = caster.position;
        Vector3 above = TargetAimPosition() + Vector3.up * 4.2f;
        for (float t = 0f; t < 0.42f; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / 0.42f);
            above = TargetAimPosition() + Vector3.up * 4.2f;
            caster.position = Vector3.Lerp(start, above, Mathf.SmoothStep(0f, 1f, u));
            FaceCurrentTarget();
            yield return null;
        }
        SetStage("SkyfallCrossCut");
        PlaySwordAnimation(1);
        Vector3 impact = TargetAimPosition();
        FracturedCut(impact + new Vector3(-2f, 2f, 0f), impact + new Vector3(2f, -1.2f, 0f), 0.12f, 1.6f, true);
        FracturedCut(impact + new Vector3(2f, 2f, 0f), impact + new Vector3(-2f, -1.2f, 0f), 0.12f, 1.6f, true);
        SpawnParticles(impact, 72, 1.1f, 10f, 0.025f, 0.11f, cyan);
        PunchCamera(0.65f, 7f);
        HitCurrentTarget();
        yield return Wait(0.3f);
        caster.position = origin;
        FinishStandaloneSkill(origin, originRotation);
    }

    Vector3 TargetAimPosition()
    {
        if (target == null) return caster.position + caster.forward * 2f;
        if (lockedSelection.IsValid && lockedSelection.marker != null && lockedSelection.marker.transform == target)
            return lockedSelection.WorldPoint;
        VoidRendTargetable marker = target.GetComponent<VoidRendTargetable>();
        return marker != null ? marker.AimPosition : target.position + Vector3.up;
    }

    void FaceCurrentTarget()
    {
        Vector3 direction = TargetAimPosition() - caster.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f) caster.rotation = Quaternion.LookRotation(direction.normalized);
    }

    void PlaySwordAnimation(int side)
    {
        // 短版程序化袈裟斬：只有抬刀、斜切、極短收勢，不再把長動畫整段加速播放。
        proceduralSlashSide = side;
        proceduralSlashStarted = Time.unscaledTime;
    }

    void EmitSkillCut(float side)
    {
        Vector3 center = TargetAimPosition();
        Vector3 horizontal = skillCamera != null ? skillCamera.transform.right : caster.right;
        Vector3 vertical = Vector3.up * (1.25f * side);
        FracturedCut(center - horizontal * 1.7f - vertical, center + horizontal * 1.7f + vertical, 0.085f, 1.25f, true);
        SpawnParticles(center, 24, 0.75f, 6f, 0.018f, 0.07f, pale);
        PunchCamera(0.28f, 2f);
    }

    void HitCurrentTarget()
    {
        if (target == null) return;
        target.GetComponent<VoidRendTargetable>()?.PlayHitReaction();
        DamageCurrentTarget(targetSkillDamage);
    }

    void DamageCurrentTarget(float amount)
    {
        if (target == null || amount <= 0f) return;
        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable == null || damageable.IsDead) return;
        damageable.TakeDamage(amount, TargetAimPosition());
    }

    void FinishStandaloneSkill(Vector3 position, Quaternion rotation, bool sheathe = true)
    {
        caster.position = position;
        caster.rotation = rotation;
        if (casterAnimator != null) casterAnimator.CrossFadeInFixedTime("Idle", 0.16f, 0, 0f);
        if (weaponPresenter) weaponPresenter.EndSkillDraw(sheathe);
        playing = false;
        cinematicCamera = false;
        Stage = "Ready";
        CompletedRuns++;
        RestorePracticeControlsAfterSkill();
    }

    void DrawTargetingUI()
    {
        GUI.depth = -1100;
        GUIStyle controls = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            normal = { textColor = new Color(0.78f, 0.94f, 1f, 0.94f) }
        };
        GUI.Box(new Rect(18f, Screen.height - 91f, 610f, 67f), GUIContent.none);
        GUI.Label(new Rect(31f, Screen.height - 83f, 590f, 24f), "WASD 移動　空白：跳躍　V：翻滾　滑鼠：視角　Z：虛空連斬", controls);
        GUI.Label(new Rect(31f, Screen.height - 56f, 590f, 24f), "C：貓躍雙斬　Q：月弧突進　E：空中十字斬（技能需滑鼠選取）", controls);

        if (!IsTargeting) return;
        GUIStyle title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 25,
            fontStyle = FontStyle.Bold,
            normal = { textColor = pale }
        };
        GUIStyle hint = new GUIStyle(title) { fontSize = 16, fontStyle = FontStyle.Normal };
        Rect panel = new Rect(Screen.width * 0.5f - 260f, 24f, 520f, 72f);
        GUI.Box(panel, GUIContent.none);
        GUI.Label(new Rect(panel.x, panel.y + 7f, panel.width, 30f), "時間凝結：用滑鼠選擇目標", title);
        GUI.Label(new Rect(panel.x, panel.y + 39f, panel.width, 24f), "左鍵確認｜Esc 取消｜5 秒後自動取消", hint);

        if (hoveredTarget != null && skillCamera != null)
        {
            Vector3 screen = skillCamera.WorldToScreenPoint(hoveredSelection.IsValid ? hoveredSelection.WorldPoint : hoveredTarget.AimPosition);
            float y = Screen.height - screen.y;
            GUI.color = new Color(1f, .08f, .04f, 1f);
            GUI.Box(new Rect(screen.x - 28f, y - 28f, 56f, 56f), "◎");
            GUI.color = Color.white;
        }
    }
}
