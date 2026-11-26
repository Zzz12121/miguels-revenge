using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed partial class VoidRendSkillDemo
{
    readonly List<VoidRendTransientLine> heldDashLines = new();
    void ReleaseDashLines(float seconds)
    {
        foreach (var line in heldDashLines) if (line != null) line.Release(seconds);
        heldDashLines.Clear();
    }
    [Header("Finisher choreography")]
    [Min(1)] public float effectRangeMultiplier = 4f;
    [Min(0.1f)] public float chargeOrbitSeconds = 3.5f;
    public float launchHeight = 4.5f;
    [Min(1f)] public float landingDistanceBehind = 3.2f;
    [Min(8f)] public float recoveryCameraDistance = 11.5f;
    [Header("Skill 1 / 轉刀鏡頭（以刀為中心、跟刀旋轉相反的方向環繞）")]
    [Tooltip("鏡頭繞著刀轉的速度（度 / 秒）。刀是順時針轉（從上往下看），鏡頭逆時針")]
    public float spinOrbitDegreesPerSecond = 75f;
    [Tooltip("鏡頭離刀多遠（公尺）")]
    [Range(1f, 8f)] public float spinOrbitRadius = 2.6f;
    [Tooltip("鏡頭比刀低多少（負的 = 從下往上拍，看得到旋轉的刀面，角色在畫面下方）")]
    public float spinOrbitHeight = -.7f;
    [Range(25f, 75f)] public float spinOrbitFov = 50f;
    [Tooltip("轉刀進行到幾成之後開始拉回原本的遠景（接下一段出拳的鏡頭）")]
    [Range(.3f, 1f)] public float spinOrbitPullOutFrom = .75f;
    [Header("Skill 1 / 挑空（衝到敵人身邊 → 上撈拔刀斬把敵人挑到空中 → 慢動作）")]
    [Tooltip("刀撈上來碰到敵人之後，敵人多快飛到 60% 高度（秒）")]
    [Range(.08f, .6f)] public float launchRiseSeconds = .22f;
    [Tooltip("慢動作：敵人四肢無力在空中翻滾的時間（真實秒數）")]
    [Range(.3f, 3f)] public float airSlowSeconds = 1.3f;
    [Tooltip("慢動作的時間倍率（0.25 = 四分之一速度）")]
    [Range(.05f, 1f)] public float airSlowTimeScale = .25f;
    [Tooltip("敵人在空中翻滾的總角度（度）")]
    public float airTumbleDegrees = 160f;
    [Header("Skill 1 / 收招鏡頭（砍完 → 推近主角 → 後面爆炸 → 平順回到玩家鏡頭）")]
    [Tooltip("砍完之後停在離目標中心多遠（公尺）：遠一點比較看得清楚角色，鏡頭也不容易被怪物擋住")]
    [Min(1f)] public float finisherLandingDistance = 4.5f;
    [Tooltip("鏡頭在角色正前方再往旁邊偏幾度：正的 = 爆炸出現在角色的左邊（配合角色放在畫面右下角）")]
    [Range(-60f, 60f)] public float finisherCameraSideAngle = 25f;
    [Tooltip("鏡頭推近：從多遠推到多近（公尺）。近一點角色在畫面上比較大")]
    [Min(.8f)] public float finisherPushStartDistance = 6.5f, finisherPushCloseDistance = 2f;
    [Tooltip("鏡頭角度轉好之後，花幾秒推到最近")]
    [Range(.3f, 4f)] public float finisherPushSeconds = 1.6f;
    [Tooltip("鏡頭比角色胸口高多少（負的 = 稍微從下往上拍，後面的爆炸比較進得了畫面）")]
    public float finisherCameraHeight = -.25f;
    [Range(20f, 70f)] public float finisherCameraFov = 45f;
    [Tooltip("收招鏡頭裡角色放在畫面哪裡（0～1，左下是 0,0）：預設右下角，鏡頭往左上轉，後面的爆炸佔大部分畫面")]
    public Vector2 finisherCharacterScreenPos = new Vector2(.68f, .33f);
    [Tooltip("爆炸結束後，鏡頭花幾秒平順地繞回玩家鏡頭")]
    [Range(.3f, 3f)] public float finisherCameraReturnSeconds = 1.4f;
    public Material smokeMaterial;
    public Material refractionMaterial;
    public UniversalRenderPipelineAsset previewPipeline;
    [Tooltip("只供獨立展示場景使用。正式 PlayerRig 不應在執行時替換整個遊戲的 URP 設定。")]
    public bool overrideRenderPipelineForPreview;
    [Tooltip("Explicit opt-in: movement/AI components to suspend during this preview.")]
    public Behaviour[] targetControllers = new Behaviour[0];
    public UnityEvent onTargetLocked = new UnityEvent();
    public UnityEvent onTargetReleased = new UnityEvent();
    public UnityEvent onFinisherImpact = new UnityEvent();
    public string Stage { get; private set; } = "Ready";
    public float Elapsed { get; private set; }
    public bool IsPlaying => playing;
    public bool TargetLocked => targetLocked;
    public int CompletedRuns { get; private set; }

    Vector3 victimHome, victimHeld, arenaCenter, cameraFocus;
    Vector3 victimLanding, previewVictimStart;
    bool completedLanding;
    float tumbleAtFall;
    Quaternion victimRotation;
    float cameraYaw = 160, cameraRadius = 8, cameraHeight = 2.2f, cinematicFov = 48, cameraRoll;
    // 鏡頭對準 cameraFocus 之後再往左 / 往上轉幾度（讓對準的東西落在畫面的某個角落，不是正中間）
    Vector2 cameraFrameAngles;
    // 固定機位（開場鏡頭 A）：開著的時候不用上面的環繞參數
    bool fixedShot;
    Vector3 fixedShotPosition;
    Quaternion fixedShotRotation;
    float fixedShotFov = 60f;
    float charge, distortion, sequenceStart, stageTime;
    float finisherPausedDuration, finisherPauseStarted = -1f;
    float FinisherClock => Time.unscaledTime - finisherPausedDuration
        - (finisherPauseStarted >= 0f ? Time.unscaledTime - finisherPauseStarted : 0f);

    void UpdateFinisherPauseClock()
    {
        if (playing && Time.timeScale <= 0f)
        {
            if (finisherPauseStarted < 0f) finisherPauseStarted = Time.unscaledTime;
        }
        else if (finisherPauseStarted >= 0f)
        {
            finisherPausedDuration += Time.unscaledTime - finisherPauseStarted;
            finisherPauseStarted = -1f;
        }
    }
    bool targetLocked, cinematicCamera, voidRendCinematicActive;
    RenderPipelineAsset previousPipeline;
    bool pipelineChanged;
    readonly Dictionary<Transform, Quaternion> poseRest = new Dictionary<Transform, Quaternion>();
    readonly Dictionary<Transform, HumanoidBonePose> humanoidUprightPose = new Dictionary<Transform, HumanoidBonePose>();
    Transform leftUpper, leftFore, leftHand, weapon, sheath;
    Transform finisherWeaponTip;
    Vector3 weaponRestPosition;
    Quaternion weaponRestRotation;
    Vector3 weaponBladeLocalDirection = Vector3.forward;
    Animator casterAnimator;
    bool animatorWasEnabled;
    bool animatorRootMotionWasEnabled;
    Vector3 animatorRootRestPosition;
    Quaternion animatorRootRestRotation;
    bool useLegacyProceduralPose = true;
    float priorTimeScale;
    ParticleSystem chargeSmoke;
    Transform pelvis, leftThigh, leftCalf, leftFoot, rightThigh, rightCalf, rightFoot;
    Vector3 pelvisRest;
    float proceduralSlashStarted = float.NegativeInfinity;
    int proceduralSlashSide;

    struct HumanoidBonePose
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
    }

    void InitializeFinisher()
    {
        if (target != null) { victimHome = target.position; previewVictimStart = target.position; victimRotation = target.rotation; }
        if (caster != null)
        {
            leftUpper = FindDeepChild(caster, "Bip001 L UpperArm");
            leftFore = FindDeepChild(caster, "Bip001 L Forearm");
            leftHand = FindDeepChild(caster, "Bip001 L Hand");
            weapon = FindDeepChild(caster, "VoidRend_KatanaSocket");
            sheath = FindDeepChild(caster, "VoidRend_Sheath");
            pelvis = FindDeepChild(caster, "Bip001 Pelvis");
            if(pelvis)pelvisRest=pelvis.localPosition;
            leftThigh=FindDeepChild(caster,"Bip001 L Thigh");leftCalf=FindDeepChild(caster,"Bip001 L Calf");leftFoot=FindDeepChild(caster,"Bip001 L Foot");
            rightThigh=FindDeepChild(caster,"Bip001 R Thigh");rightCalf=FindDeepChild(caster,"Bip001 R Calf");rightFoot=FindDeepChild(caster,"Bip001 R Foot");

            // Humanoid fallback：讓技能能直接套用到玩家的新角色骨架。
            casterAnimator = caster.GetComponentInChildren<Animator>();
            if (casterAnimator != null && casterAnimator.isHuman)
            {
                // Humanoid 角色交給匯入動畫控制；舊版程序化骨骼姿勢只適用 Bocchi 骨架。
                useLegacyProceduralPose = false;
                if (leftUpper == null) leftUpper = casterAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                if (leftFore == null) leftFore = casterAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                if (leftHand == null) leftHand = casterAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (pelvis == null) pelvis = casterAnimator.GetBoneTransform(HumanBodyBones.Hips);
                if (leftThigh == null) leftThigh = casterAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                if (leftCalf == null) leftCalf = casterAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                if (leftFoot == null) leftFoot = casterAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (rightThigh == null) rightThigh = casterAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                if (rightCalf == null) rightCalf = casterAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                if (rightFoot == null) rightFoot = casterAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            if (casterAnimator != null)
            {
                animatorRootRestPosition = casterAnimator.transform.localPosition;
                animatorRootRestRotation = casterAnimator.transform.localRotation;
            }
            if (weapon == null) weapon = FindDeepChild(caster, "WeaponPivot") ?? FindDeepChild(caster, "PlayerMagicSaber");
            if (weapon != null)
            {
                finisherWeaponTip = FindDeepChild(weapon, "WeaponTip");
                if (finisherWeaponTip != null)
                {
                    Vector3 localTip = weapon.InverseTransformPoint(finisherWeaponTip.position);
                    if (localTip.sqrMagnitude > 0.0001f) weaponBladeLocalDirection = localTip.normalized;
                }
            }
            foreach (Transform bone in caster.GetComponentsInChildren<Transform>())
                if (bone.name.StartsWith("Bip001") || (casterAnimator != null && bone.IsChildOf(casterAnimator.transform))) poseRest[bone] = bone.localRotation;
            if (weapon) { weaponRestPosition = weapon.localPosition; weaponRestRotation = weapon.localRotation; }
        }
        if (overrideRenderPipelineForPreview && previewPipeline != null)
        {
            previousPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = previewPipeline;
            pipelineChanged = true;
        }
        SetDistortion(0);
    }

    void LockVictim()
    {
        Transform body = VictimBody(target);   // 老頭變身後是書法分身，不是留在原地的根物件
        victimHome = body.position;
        victimRotation = body.rotation;
        victimHeld = victimHome;
        HoldVictim(target);
        targetLocked = true;
        onTargetLocked.Invoke();
    }

    bool ShouldSuspendVictimBehaviour(Behaviour controller)
    {
        if (controller == null || controller == this || controller is VoidRendTargetable) return false;
        if (controller is Animator || controller is EnemyAI || controller is EnemyCombat || controller is ThirdPersonController) return true;
        string type = controller.GetType().Name;
        if (type.Contains("Health") || type.Contains("Targetable") || type.Contains("Visual")) return false;
        return type.Contains("AI") || type.Contains("Attack") || type.Contains("Combat") || type.Contains("Controller");
    }

    void ReleaseVictim(bool restorePosition = true)
    {
        if (!targetLocked) return;
        ReleaseLiftedVictims(restorePosition);
        targetLocked = false;
        onTargetReleased.Invoke();
    }

    void SetStage(string stage) { Stage = stage; stageTime = FinisherClock; if (!IsHorseHoldStage()) cameraFrameAngles = Vector2.zero; }

    /// <summary>要把對準點放在畫面 screen（0～1）的位置，鏡頭要往左、往上轉幾度</summary>
    Vector2 FrameAnglesFor(Vector2 screen, float fov)
    {
        float tanV = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
        float aspect = skillCamera != null ? skillCamera.aspect : 16f / 9f;
        return new Vector2(Mathf.Atan((2f * screen.x - 1f) * tanV * aspect) * Mathf.Rad2Deg,
                           Mathf.Atan((1f - 2f * screen.y) * tanV) * Mathf.Rad2Deg);
    }
    void SetDistortion(float amount)
    {
        distortion = amount;
        Shader.SetGlobalFloat("_SpatialStrength", amount);
        Shader.SetGlobalFloat("_SpatialClock", FinisherClock);
        if (refractionMaterial) { refractionMaterial.SetFloat("_Strength", amount); refractionMaterial.SetFloat("_Clock", FinisherClock); }
    }

    /// <summary>目標的水平半徑（取碰撞體，沒有就取 Renderer 外框），用來決定衝刺停在多遠。</summary>
    float VictimRadius()
    {
        if (target == null) return .5f;
        Transform body = VictimBody(target);
        bool has = false; var b = new Bounds(body.position, Vector3.zero);
        foreach (var c in body.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
        }
        if (!has)
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
        if (!has) return .5f;
        // 目標中心到外框邊緣的水平距離（取較小的那一邊，避免長條形的武器把距離撐太大）
        return Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z), .35f, 2.2f);
    }

    float CasterRadius() => practiceBody != null ? practiceBody.radius : .45f;

    IEnumerator PlaySequence()
    {
        SkillDamageCap.BeginCast(this);   // 這次施放對強敵的傷害重新計算（一次最多 30%）
        SuspendPracticeControlsForSkill();
        playing = true; priorTimeScale = CombatHitStop.CaptureTimeScale(); sequenceStart = FinisherClock;
        SetWindupInvulnerability(true);
        ResetPreviewState();
        // Reset the isolated demo only when the next replay actually begins.
        if(completedLanding && loopPreview) { target.position=previewVictimStart; }
        completedLanding=false;
        LockVictim();
        if (casterAnimator)
        {
            CaptureDrawEntryPose();
            animatorWasEnabled = casterAnimator.enabled;
            animatorRootMotionWasEnabled = casterAnimator.applyRootMotion;
            casterAnimator.applyRootMotion = false;
            if (useLegacyProceduralPose) casterAnimator.enabled = false;
            else
            {
                // 先取樣一幀真正的站姿，再關掉 Animator。Z 技能期間由程序姿勢接手，
                // 匯入動畫就不會在後段把骨盆翻倒或讓腿誤播跑步。
                casterAnimator.enabled = true;
                CaptureSkillClipPoses();   // Tripo 的轉刀 / 低馬步姿勢先取樣存起來（之後 Animator 會關掉）
                casterAnimator.Play("Idle", 0, 0f);
                casterAnimator.Update(0f);
                CaptureHumanoidUprightPose();
                casterAnimator.enabled = false;
            }
        }
        if (weaponPresenter) weaponPresenter.BeginSkillDraw();
        cinematicCamera = true;
        voidRendCinematicActive = true;
        AcquireCinematicCamera();
        ResetSkillCameraSafety();
        arenaCenter = new Vector3(victimHome.x, casterStartPosition.y, victimHome.z);
        cameraFocus = arenaCenter + Vector3.up * 1.5f;
        cameraYaw = 155; cameraRadius = 8; cameraHeight = 2.2f; cinematicFov = 48;
        float normalCameraRadius = Mathf.Clamp(Vector3.ProjectOnPlane(cameraFollowOffset, Vector3.up).magnitude, 4.5f, 10f);
        float normalCameraHeight = Mathf.Clamp(cameraFollowOffset.y - cameraLookOffset.y, .8f, 4f);
        float spinCameraStartRadius = Mathf.Max(7.2f, normalCameraRadius);
        float spinCameraEndRadius = spinCameraStartRadius + 3.4f;

        // Blend from the actual locomotion frame into the left-waist draw pose.
        Vector3 approachStart = caster.position;
        // 衝刺停在目標身前，不衝進目標的身體裡（舊版直接停在目標中心，蓄力時會和老頭等大型目標穿模）。
        // 空中連斬仍以目標為中心（arenaCenter），只有地面上的衝刺 / 擊飛 / 蓄力站在 groundStop。
        Vector3 toVictim = Vector3.ProjectOnPlane(arenaCenter - approachStart, Vector3.up);
        // 衝到敵人腳邊就停（不衝過頭）：敵人身體邊緣 + 自己的半徑 + 一點點空隙
        float standOff = VictimRadius() * .8f + CasterRadius() + approachStopGap;
        Vector3 groundStop = toVictim.magnitude > standOff
            ? arenaCenter - toVictim.normalized * standOff
            : approachStart;
        groundStop.y = arenaCenter.y;
        // 不穿模：衝刺路上有牆 / 建築就停在它前面；停下的地方貼齊地面（敵人站在不同高度時不會浮空或陷進地裡）
        {
            Vector3 dash = groundStop - approachStart; float len = Vector3.ProjectOnPlane(dash, Vector3.up).magnitude;
            if (len > .1f && SkillPathBlocked(approachStart, groundStop))
            {
                float lo = 0f, hi = 1f;
                for (int k = 0; k < 8; k++) { float mid = (lo + hi) * .5f; if (SkillPathBlocked(approachStart, Vector3.Lerp(approachStart, groundStop, mid))) hi = mid; else lo = mid; }
                groundStop = Vector3.Lerp(approachStart, groundStop, lo);
            }
            if (SkillGroundBelow(groundStop, 1.5f, out Vector3 stopGround) && Mathf.Abs(RootOnGround(stopGround).y - groundStop.y) < 2.5f) groundStop.y = RootOnGround(stopGround).y;
        }
        Vector3 approachDirection = Vector3.ProjectOnPlane(groundStop - approachStart, Vector3.up);
        float approachDistance = approachDirection.magnitude;
        // 起手面向目標（就算已經站得夠近、不需要衝刺）
        Quaternion drawFacing = toVictim.sqrMagnitude > .0025f
            ? Quaternion.LookRotation(toVictim.normalized, Vector3.up) : caster.rotation;
        Quaternion entryFacing = caster.rotation;
        // 鏡頭 A：角色左後方、低機位的固定特寫，角色大、目標小（近大遠小）；蹲低時慢慢推近，爆衝時鏡頭不動，看角色一路衝遠
        Vector3 openForward = drawFacing * Vector3.forward, openRight = drawFacing * Vector3.right;
        Vector3 openCamera = approachStart + openRight * openingCameraGroundLevel.x + Vector3.up * openingCameraGroundLevel.y + openForward * openingCameraGroundLevel.z;
        openCamera = KeepCameraOutOfWalls(approachStart + Vector3.up * .4f, openCamera);
        Vector3 openLook = Vector3.Lerp(approachStart + Vector3.up * .8f, TargetAimPosition(), openingLookTowardTarget);
        fixedShot = true;
        fixedShotFov = openingCameraFov;
        fixedShotPosition = openCamera;
        fixedShotRotation = Quaternion.LookRotation(openLook - openCamera, Vector3.up);
        SetStage("DrawPrepare");
        float crouchSeconds = Mathf.Max(drawBlendSeconds, openingCrouchSeconds);
        for (float t = 0; t < crouchSeconds; t += Time.unscaledDeltaTime)
        {
            caster.rotation = Quaternion.Slerp(entryFacing, drawFacing, Mathf.SmoothStep(0, 1, t / Mathf.Max(.01f, drawBlendSeconds)));
            cameraFocus = caster.position + Vector3.up * 1.35f;
            float push = Mathf.SmoothStep(0, 1, t / crouchSeconds) * openingCameraPushIn;
            fixedShotPosition = openCamera + (openLook - openCamera).normalized * push;
            yield return null;
        }
        caster.rotation = drawFacing;
        SetStage("ApproachRun");
        nextDrawGhostTime = 0f;
        SpawnRing(approachStart + Vector3.up * .06f, .15f, 1.6f, .06f, .45f, pale);   // 蹬地的衝擊
        PunchCamera(.22f, 5f);
        if (approachDistance > .05f)
        {
            float approachDuration = Mathf.Clamp(approachDistance / Mathf.Max(1f, drawDashSpeed), openingDashSeconds.x, Mathf.Max(openingDashSeconds.x, openingDashSeconds.y));
            for (float t = 0; t < approachDuration; t += Time.unscaledDeltaTime)
            {
                // 一開始爆發（很快加速），快到目標時不減速：直接衝到目標身前
                float u = Mathf.Clamp01(t / approachDuration);
                caster.position = Vector3.Lerp(approachStart, groundStop, 1f - (1f - u) * (1f - u) * (1f - .6f * u));
                cameraFocus = caster.position + Vector3.up * 1.35f;
                yield return null;
            }
        }
        caster.position = groundStop;
        // 鏡頭 2：衝到目標後直接切到角色正面偏右的特寫（刀和表情），轉刀時再慢慢拉遠
        fixedShot = false;
        float heroYaw = Mathf.Atan2(openForward.x, openForward.z) * Mathf.Rad2Deg;   // 角色面向的角度（鏡頭 yaw 用同一個算法）
        float spinYawStart = heroYaw + spinCloseUpAngle;
        cameraYaw = spinYawStart; cameraRadius = spinCloseUpDistance; cameraHeight = -.12f; cameraRoll = 0f;
        cameraFocus = caster.position + Vector3.up * spinCloseUpFocusHeight;
        cinematicFov = spinCloseUpFov;
        // ── 中段：上撈的拔刀斬把敵人挑到空中 → 慢動作：敵人四肢無力、在空中翻滾 ──
        // Launch 姿勢（ApplyDrawDashPose）：刀從左腰後方一路往前上方撈到頭頂，0.11 秒左右經過正前方 = 碰到敵人
        SetStage("Launch");
        Vector3 launchSide = Vector3.Cross(Vector3.up, openForward);
        victimTumbleAxis = launchSide.sqrMagnitude > .01f ? launchSide.normalized : Vector3.right;   // 往後翻
        victimTumbleDegrees = 0f;
        yield return Wait(.11f);
        LaunchNearbyVictims();
        BeginVictimLimp(1f);
        Vector3 launchHit = victimHome + Vector3.up * 1.1f;
        SpawnArc(victimHome + Vector3.up * .9f, Quaternion.LookRotation(openForward, Vector3.up) * Quaternion.Euler(0f, 0f, 90f), 2.4f, -100f, 100f, .16f, .5f, pale);
        SpawnSlashPair(launchHit, (openForward + Vector3.up * 1.6f).normalized, 4.5f, .09f, .4f, pale);
        SpawnParticles(launchHit, 40, .8f, 9f, .02f, .07f, cyan);
        SpawnRing(victimHome + Vector3.up * .06f, .2f, 2.2f, .07f, .45f, pale);
        PunchCamera(.7f, 6); Flash(cyan, .16f);
        KickVictimLimbs(Vector3.up, 8f);

        // 快速飛起：launchRiseSeconds 秒到 60% 高度；鏡頭從特寫轉到角色後方的低角度（往上拍敵人）
        float riseHeight = launchHeight * .6f;
        float slowYaw = heroYaw + 180f - 30f;
        float camYaw0 = cameraYaw, camR0 = cameraRadius, camH0 = cameraHeight, camFov0 = cinematicFov;
        Vector3 camFocus0 = cameraFocus;
        for (float t = 0; t < launchRiseSeconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / launchRiseSeconds);
            victimHeld = victimHome + Vector3.up * Mathf.Lerp(.08f, riseHeight, 1f - Mathf.Pow(1f - u, 3f));
            victimTumbleDegrees = Mathf.Lerp(0f, 35f, u);
            float cu = Mathf.SmoothStep(0f, 1f, u);
            cameraYaw = Mathf.LerpAngle(camYaw0, slowYaw, cu); cameraRadius = Mathf.Lerp(camR0, 4.2f, cu);
            cameraHeight = Mathf.Lerp(camH0, -.5f, cu); cinematicFov = Mathf.Lerp(camFov0, 48f, cu);
            cameraFocus = Vector3.Lerp(camFocus0, Vector3.Lerp(caster.position + Vector3.up * 1.3f, victimHeld + Vector3.up, .6f), cu);
            yield return null;
        }

        DiagnoseLift();
        // 慢動作：整個世界變慢，敵人慢慢升到最高點、翻滾、四肢無力甩動；鏡頭慢慢繞一點
        Time.timeScale = airSlowTimeScale;
        Time.fixedDeltaTime = fixedDeltaStart * airSlowTimeScale;
        SetVictimLimpSlow(airSlowTimeScale);
        float nextAirSpark = 0f;
        for (float t = 0; t < airSlowSeconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / airSlowSeconds);
            victimHeld = victimHome + Vector3.up * Mathf.Lerp(riseHeight, launchHeight, 1f - (1f - u) * (1f - u));
            victimTumbleDegrees = Mathf.Lerp(35f, airTumbleDegrees, u);
            cameraYaw = slowYaw + 25f * u; cameraRadius = Mathf.Lerp(4.2f, 5.4f, u);
            cameraHeight = Mathf.Lerp(-.5f, .2f, u); cinematicFov = Mathf.Lerp(48f, 56f, u); cameraRoll = 0f;
            cameraFocus = Vector3.Lerp(caster.position + Vector3.up * 1.3f, victimHeld + Vector3.up, .6f);
            if (t >= nextAirSpark)
            {
                nextAirSpark = t + .12f;
                SpawnParticles(victimHeld + Vector3.up + Random.insideUnitSphere * .4f, 3, .9f, .35f, .008f, .02f, cyan);
            }
            SetDistortion(.12f * u);
            yield return null;
        }
        Time.timeScale = priorTimeScale;
        Time.fixedDeltaTime = fixedDeltaStart;
        SetVictimLimpSlow(1f);
        Vector3 center = victimHeld;
        Flash(pale, .3f); PunchCamera(.45f, 6); SetDistortion(.45f);
        if (!useLegacyProceduralPose) PlaySwordAnimation(1);
        SetWindupInvulnerability(false);
        yield return Wait(.12f);
        SetStage("DashLattice");
        cameraFocus = center - Vector3.up*1.2f;
        Vector3 last = caster.position;
        int cuts = 22;
        for (int i=0; i<cuts; i++)
        {
            if (!useLegacyProceduralPose && i % 3 == 0) PlaySwordAnimation(i / 3);
            float angle = (i * 137.508f + 24) * Mathf.Deg2Rad;
            float radius = Mathf.Min(teleportRadius * effectRangeMultiplier, latticeMaxRadius) * (i%3 == 0 ? 1f : .72f);
            Vector3 desired = arenaCenter + new Vector3(Mathf.Cos(angle)*radius, 2 + (i%5)*1.05f, Mathf.Sin(angle)*radius);
            // 不穿模：落點卡進牆 / 地形，或衝過去的路被擋，就往目標靠近、左右偏一點；都不行就原地
            Vector3 next = SafeLatticePoint(last, desired, victimHeld);
            KickVictimLimbs(next - last, 5f);
            victimTumbleDegrees += 7f;
            CreateAfterimage(); poseSide = (i%2 == 0 ? 1 : -1);
            // Geometry is emitted from the exact swept character path, with a advancing head.
            const float dashDuration = .15f;
            Vector3 previous = last + Vector3.up*.9f;
            var growing=SpawnRibbon(new[]{previous,previous},slashMaterial,.09f,2.1f,pale);
            var halo=SpawnRibbon(new[]{previous,previous},slashMaterial,.16f,2.1f,new Color(.15f,.65f,1,.22f));
            for(float t=0;t<dashDuration;t+=Time.unscaledDeltaTime)
            {
                float u = Mathf.Clamp01(t/dashDuration);
                caster.position = Vector3.Lerp(last, next, u);
                Vector3 head = caster.position + Vector3.up*.9f;
                if (growing) growing.SetPosition(1,head);
                if (halo) halo.SetPosition(1,head);
                Vector3 dashFacing = next - last;
                dashFacing.y = 0f;
                if (dashFacing.sqrMagnitude > .0001f)
                    caster.rotation = Quaternion.LookRotation(dashFacing.normalized, Vector3.up);
                cameraYaw = Mathf.Lerp(0, 300, (i+u)/cuts);
                cameraRadius = Mathf.Lerp(7.2f, 9f, Mathf.Sin((i+u)/cuts*Mathf.PI));
                cameraHeight = 2.2f + Mathf.Sin(i*.4f)*.7f;
                cameraRoll = Mathf.Sin((i+u)*1.2f)*7;
                cinematicFov = 64;
                SetDistortion(.55f + .3f*(1-u));
                yield return null;
            }
            caster.position = next;
            if (growing) growing.SetPosition(1,next+Vector3.up*.9f);
            if (halo) halo.SetPosition(1,next+Vector3.up*.9f);
            FracturedCut(last+Vector3.up*.9f,next+Vector3.up*.9f,.065f,8.4f,true);
            SpawnSpatialLattice(center, i, 5, 2.2f);
            DamageDashArea(last + Vector3.up * .9f, next + Vector3.up * .9f);
            if (i%3==0)
            {
                CreateSmoke(Vector3.Lerp(last,next,.5f),false,2.3f,.8f);
                SpawnParticles(center, 18, 1.2f, 7, .025f,.08f,pale);
                PunchCamera(.3f, 2); Flash(pale,.08f);
            }
            last=next;
            yield return Wait(.04f);
        }
        SetStage("SheatheOrbit");
        CreateAfterimage();
        Vector3 recoveryFallStart = caster.position;
        Vector3 recoveryForward = Vector3.ProjectOnPlane(victimRotation * Vector3.forward, Vector3.up).normalized;
        if (recoveryForward.sqrMagnitude < .001f) recoveryForward = Vector3.forward;
        // 收招的跪地（居合低伏）要在地面 / 屋頂上：不停在空中，直接落到找到的地面上
        bool holdInAir=false;
        // 收招位置：前方找一個站得下、下面有地面 / 屋頂、飛過去不會撞牆的地方（不穿模、之後不會落進地裡）
        Vector3 recoveryGround, recoveryHold;
        if (FindRecoverySpot(recoveryFallStart, recoveryForward, holdInAir ? finisherAirHoldHeight : 0f, out recoveryGround, out recoveryHold, out Vector3 safeFacing))
            recoveryForward = safeFacing;
        else
        {
            // 找不到：留在最後一刀的位置正下方的地面上空
            // 找不到：落在最後一刀位置正下方的地面上（腳踩地面）
            if (!SkillGroundBelow(recoveryFallStart, 1f, out recoveryGround)) recoveryGround = FeetOf(new Vector3(recoveryFallStart.x, casterStartPosition.y, recoveryFallStart.z));
            recoveryHold = RootOnGround(recoveryGround);
        }
        Quaternion recoveryRotation = Quaternion.LookRotation(recoveryForward, Vector3.up);
        Quaternion recoveryStartRotation = caster.rotation;
        float frontCameraYaw = Mathf.Atan2(recoveryForward.x, recoveryForward.z) * Mathf.Rad2Deg;
        float yawStart=cameraYaw;
        float radiusStart=cameraRadius, heightStart=cameraHeight, rollStart=cameraRoll;
        Vector3 focusStart=cameraFocus, heldStart=victimHeld;
        // 目標留在切割與爆炸中心，爆炸後才垂直落回原地。
        victimLanding=victimHome;
        // 砍完不落地：飛到目標前方的空中停住（馬步），等技能結束、鏡頭切回玩家後才往下掉
        // 收招鏡頭：先繞到角色正前方（偏一側，目標和爆炸在角色後面），然後一路慢慢推近到爆炸結束
        float shotYaw=frontCameraYaw+finisherCameraSideAngle;
        // 鏡頭先花 1.3 秒繞到角度（角色移到畫面右下角），角度對了才開始推近，推到最近之後停住
        const float pushFrom=1.3f;
        float PushRadius(float T)=>Mathf.Lerp(finisherPushStartDistance,finisherPushCloseDistance,
            Mathf.SmoothStep(0,1,Mathf.Clamp01((T-pushFrom)/Mathf.Max(.05f,finisherPushSeconds))));
        Vector3 ShotFocus()=>caster.position+Vector3.up*.35f;
        for(float t=0;t<2.4f;t+=Time.unscaledDeltaTime)
        {
            float travel=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/1.75f));
            // 落到地面上跪地：最後貼在地面（不加上下晃動，不然會陷進地裡或浮起來）
            caster.position=Vector3.Lerp(recoveryFallStart,recoveryHold,travel)
                +Vector3.up*(holdInAir?Mathf.Sin(t*2.6f)*.04f*travel:0f);
            caster.rotation=Quaternion.Slerp(recoveryStartRotation,recoveryRotation,travel);
            float swing=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/1.3f));
            cameraYaw=Mathf.LerpAngle(yawStart,shotYaw,swing);
            cameraRadius=Mathf.Lerp(radiusStart,PushRadius(t),swing);
            cameraHeight=Mathf.Lerp(heightStart,finisherCameraHeight,swing);
            cameraFocus=Vector3.Lerp(focusStart,ShotFocus(),swing);
            cameraRoll=Mathf.Lerp(rollStart,0,swing); cinematicFov=Mathf.Lerp(64,finisherCameraFov,swing);
            // 角色慢慢移到畫面右下角（鏡頭往左上轉，看到後面的爆炸）
            cameraFrameAngles=FrameAnglesFor(finisherCharacterScreenPos,cinematicFov)*swing;
            victimHeld=Vector3.Lerp(heldStart,victimLanding+Vector3.up*launchHeight,Mathf.SmoothStep(0,1,t/2.4f));
            victimTumbleDegrees+=12f*Time.unscaledDeltaTime;
            SetDistortion(.18f + Mathf.Sin(t*7)*.08f);
            yield return null;
        }
        caster.position=recoveryHold;
        caster.rotation=recoveryRotation;
        cameraYaw=shotYaw; cameraRadius=PushRadius(2.4f); cameraHeight=finisherCameraHeight;
        cameraFocus=ShotFocus(); cinematicFov=finisherCameraFov; cameraRoll=0;
        cameraFrameAngles=FrameAnglesFor(finisherCharacterScreenPos,cinematicFov);
        victimHeld=victimLanding+Vector3.up*launchHeight;
        SetStage("FinalBreak");
        ReleaseDashLines(.4f);
        onFinisherImpact.Invoke();
        UpdateLiftedVictims();
        DamageExplosion(center);
        SpawnSpaceRift(center);
        SpawnSpatialLattice(center, 37, 32, 1.8f);
        SpawnRing(arenaCenter+.1f*Vector3.up,.5f,4.7f*effectRangeMultiplier,.4f,1.5f,pale);
        CreateSmoke(arenaCenter+Vector3.up,false,3.2f,2.4f);
        SpawnParticles(center,150,1.5f,18,.03f,.15f,pale);
        Flash(Color.white,.65f); PunchCamera(1.1f,9); SetDistortion(.72f);
        for(float t=0;t<.12f;t+=Time.unscaledDeltaTime)
        {
            cameraRadius=PushRadius(2.4f+t);
            yield return null;
        }
        for(float t=0;t<.8f;t+=Time.unscaledDeltaTime)
        {
            float u=t/.8f;
            victimHeld=Vector3.Lerp(victimLanding+Vector3.up*launchHeight,victimLanding,u*u);
            // 掉回地面：翻滾慢慢轉正，落地時腳踩在原本的地面上（不會陷進地裡或浮在空中）
            if(t==0f)tumbleAtFall=victimTumbleDegrees;
            victimTumbleDegrees=Mathf.LerpAngle(tumbleAtFall,0f,Mathf.SmoothStep(0,1,u));
            cameraRadius=PushRadius(2.52f+t); cameraFocus=ShotFocus();
            SetDistortion((1-u)*.35f);
            yield return null;
        }
        // 鏡頭拉回玩家視角的這段還是停在空中（馬步），技能結束後才交給控制器落下
        SetStage("Recover");
        float recoveryYaw=cameraYaw;
        // 有玩家鏡頭：先把它打開（在角色背後），再從收招鏡頭繞著角色平順地混過去，交接時不會跳
        if (BeginCameraHandoff())
        {
            for(float t=0;t<finisherCameraReturnSeconds;t+=Time.unscaledDeltaTime)
            {
                cameraHandoffPivot=caster.position+Vector3.up*.4f;
                cameraHandoffWeight=Mathf.SmoothStep(0,1,t/finisherCameraReturnSeconds);
                yield return null;
            }
            cameraHandoffWeight=1f;
        }
        else for(float t=0;t<.35f;t+=Time.unscaledDeltaTime)
        {
            float u=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.35f));
            cameraYaw=recoveryYaw+u*8f;
            cameraRadius=Mathf.Lerp(recoveryCameraDistance,normalCameraRadius,u);
            cameraHeight=Mathf.Lerp(1.6f,normalCameraHeight,u);
            cameraFocus=Vector3.Lerp(cameraFocus,caster.position+Vector3.up*.9f,1f-Mathf.Exp(-7f*Time.unscaledDeltaTime));
            cinematicFov=Mathf.Lerp(60f,cameraStartFov,u);
            yield return null;
        }
        ReleaseVictim(false); completedLanding=true; SetDistortion(0); charge=0; cameraRoll=0;
        voidRendCinematicActive = false;
        cinematicCamera = false;
        ReleaseCinematicCamera();
        EnsureCasterNotInsideWalls(); // 保險：交還操作之前確定角色沒有卡在牆 / 房子裡（不然會動不了）
        SetStage("Complete"); playing=false; CompletedRuns++;
        if(casterAnimator)
        {
            casterAnimator.enabled=animatorWasEnabled;
            casterAnimator.applyRootMotion=animatorRootMotionWasEnabled;
            RestoreCasterVisualRoot();
            if (!useLegacyProceduralPose)
            {
                // 人還在空中：直接進下落動畫，交給玩家控制器往下掉，落地時跟跳躍一樣播 Land
                bool inAir = holdInAir && HasSkillClipPoses && practiceController != null && casterAnimator.HasState(0, Animator.StringToHash("Fall"));
                if (inAir) { casterAnimator.Play("Fall", 0, 0f); BeginHandoffToAnimator(); }
                else casterAnimator.CrossFadeInFixedTime("Idle", 0.16f, 0, 0f);
            }
        }
        if (weaponPresenter) weaponPresenter.EndSkillDraw(true);
        Time.timeScale=priorTimeScale;
        RestorePracticeControlsAfterSkill();
        if(loopPreview) { yield return Wait(loopDelay+2); PlaySkill(); }
    }

    void UpdateFinisherCamera(Vector3 shake,Vector3 rotationShake)
    {
        if(playing) Elapsed=FinisherClock-sequenceStart;
        if(refractionMaterial) refractionMaterial.SetFloat("_Clock",FinisherClock);
        if (playing) Shader.SetGlobalFloat("_SpatialClock", FinisherClock);
        // Z 的電影鏡頭必須一路維持到落地姿勢完成，不能被一般環繞鏡頭提早接管。
        if(!cinematicCamera && !voidRendCinematicActive) return;
        cinematicCamera = true;
        if (fixedShot)
        {
            HideSkillOccluders(fixedShotPosition);
            QueueCinematicCameraPose(fixedShotPosition + shake, fixedShotRotation * Quaternion.Euler(rotationShake), fixedShotFov - fovKick);
            return;
        }
        float a=cameraYaw*Mathf.Deg2Rad;
        Vector3 p=cameraFocus+new Vector3(Mathf.Sin(a)*cameraRadius,cameraHeight,Mathf.Cos(a)*cameraRadius);
        // 不被建築擋：大東西擋住就拉近 / 往上抬，小東西直接隱藏
        p=ResolveSkillCamera(cameraFocus,p);
        HideSkillOccluders(p);
        Quaternion rotation=Quaternion.LookRotation(cameraFocus-p)*Quaternion.Euler(-cameraFrameAngles.y,-cameraFrameAngles.x,0f)
            *Quaternion.Euler(rotationShake+Vector3.forward*cameraRoll);
        QueueCinematicCameraPose(p+shake, rotation, cinematicFov-fovKick);
    }

    void ApplyFinisherPose()
    {
        if (IsDrawDashStage())
        {
            foreach (var p in poseRest) if (p.Key) p.Key.localRotation = p.Value;
            if (pelvis) pelvis.localPosition = pelvisRest;
            ApplyDrawDashPose();
            return;
        }
        foreach(var p in poseRest) if(p.Key) p.Key.localRotation=p.Value;
        float t=FinisherClock-stageTime;
        Vector3 rh=new Vector3(.35f,1.6f,.2f), lh=new Vector3(-.35f,1.55f,.12f);
        float spin=0;
        if(Stage=="OverheadWindup") rh=new Vector3(.12f,2.25f,.08f);
        if(Stage=="Launch") rh=new Vector3(.15f,Mathf.Lerp(2.25f,2.5f,Mathf.Clamp01(t/.5f)),.3f);
        if(Stage=="AerialRotor")
        {
            float flourish=Mathf.Clamp01(t/1.8f);
            rh=new Vector3(.38f+Mathf.Sin(flourish*Mathf.PI*2)*.25f,2.3f+Mathf.Sin(flourish*Mathf.PI)*.25f,.2f);
            lh=new Vector3(-.35f,1.5f,.22f);
            spin=flourish*720;
        }
        if(Stage=="Punch") { rh=new Vector3(.16f,1.85f,.85f); lh=new Vector3(-.25f,1.55f,.15f); }
        if(Stage=="DashLattice")
        {
            rh=new Vector3(.5f*poseSide,1.95f,.65f); lh=new Vector3(-.45f,1.55f,-.3f);
            if(spine) spine.localRotation=spineBase*Quaternion.Euler(0,20*poseSide,18);
        }
        if(Stage=="SheatheOrbit" || Stage=="FinalBreak" || Stage=="Complete")
        {
            float u=Stage=="SheatheOrbit"?Mathf.SmoothStep(0,1,Mathf.Clamp01(t/2.1f)):1;
            rh=Vector3.Lerp(new Vector3(.7f,2.3f,.3f),new Vector3(-.26f,1.55f,.3f),u);
            lh=new Vector3(-.4f,1.5f,.18f);
        }
        else if(weapon) weapon.localScale=Vector3.one;
        if(pelvis)
        {
            pelvis.localPosition=pelvisRest;
            float crouch=Stage=="DashLattice"?.32f:Stage=="SheatheOrbit"?.12f:0;
            pelvis.position-=Vector3.up*crouch;
            SolveArm(leftThigh,leftCalf,leftFoot,caster.TransformPoint(new Vector3(-.23f,.14f,.3f)),caster.forward);
            SolveArm(rightThigh,rightCalf,rightFoot,caster.TransformPoint(new Vector3(.23f,.14f,-.25f)),caster.forward);
        }
        Vector3 rightGoal=caster.TransformPoint(rh),leftGoal=caster.TransformPoint(lh);
        if(weapon)
        {
            weapon.localScale=Vector3.one;
            bool sheathing=Stage=="SheatheOrbit"||Stage=="FinalBreak"||Stage=="Complete";
            if(sheathing && sheath)
            {
                float u=Stage=="SheatheOrbit"?Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.35f)/1.65f)):1;
                Vector3 mouth=sheath.position+sheath.up*.575f;
                Vector3 grip=mouth+sheath.up*Mathf.Lerp(1.4f,.05f,u);
                rightGoal=grip;
                leftGoal=mouth;
                SolveArm(rightUpperArm,rightForearm,rightHand,rightGoal,caster.right);
                SolveArm(leftUpper,leftFore,leftHand,leftGoal,-caster.right);
                weapon.position=grip;
                weapon.rotation=Quaternion.FromToRotation(Vector3.left,-sheath.up);
                var blade=FindDeepChild(weapon,"Energy Katana Blade");
                if(blade)
                {
                    float visible=Mathf.Lerp(1.38f,.015f,u);
                    blade.localScale=new Vector3(visible,.035f,.065f);
                    blade.localPosition=new Vector3(-visible*.5f-.03f,0,0);
                }
            }
            else
            {
                SolveArm(rightUpperArm,rightForearm,rightHand,rightGoal,caster.right);
                SolveArm(leftUpper,leftFore,leftHand,leftGoal,-caster.right);
                weapon.position=rightHand.position;
                weapon.rotation=Quaternion.LookRotation(caster.forward,Vector3.up)*Quaternion.Euler(Stage=="AerialRotor"?0:25,Stage=="AerialRotor"?spin:0,Stage=="AerialRotor"?0:-30);
                var blade=FindDeepChild(weapon,"Energy Katana Blade");
                if(blade){blade.localScale=new Vector3(1.38f,.035f,.065f);blade.localPosition=new Vector3(-.72f,0,0);}
            }
        }
    }

    void ApplyHumanoidFinisherPose()
    {
        if (caster == null) return;
        BeginSkillPoseBlendIfStageChanged();
        ApplyHumanoidFinisherPoseCore();
        FinishSkillPoseBlend();
    }

    void ApplyHumanoidFinisherPoseCore()
    {
        float t = FinisherClock - stageTime;
        // Tripo 姿勢（轉刀舉手 / 低馬步）有的話當底，沒有就是原本的站姿
        bool clipPose = ApplyStageClipPose(t);
        if (!clipPose) RestoreHumanoidUprightPose();
        if (weapon == null) return;
        if (IsDrawDashStage()) { ApplyDrawDashPose(); return; }

        if (Stage == "AerialRotor" && clipPose)
        {
            ApplySummonedSword(t);
            return;
        }

        // 空中停住、爆炸、鏡頭拉回：維持馬步，刀照最後一刀砍完的樣子掛在手上
        if (IsHorseHoldStage() && clipPose) { ApplyIaiSword(); return; }

        if (Stage == "DashLattice" || Stage == "Punch")
        {
            ApplyProceduralKesaSlash();
            return;
        }

        if (Stage != "OverheadWindup" && Stage != "Launch" && Stage != "AerialRotor")
        {
            // 最終爆發與收勢也維持站姿，不再讓先前匯入動畫的根骨姿勢殘留。
            Vector3 neutralGrip = caster.TransformPoint(new Vector3(.34f, 1.08f, .25f));
            SolveArm(rightUpperArm, rightForearm, rightHand, neutralGrip, caster.right);
            weapon.position = rightHand != null ? rightHand.position : neutralGrip;
            AlignWeaponBlade((caster.forward + caster.right * .12f).normalized, caster.up);
            return;
        }

        Vector3 grip = caster.TransformPoint(new Vector3(.08f, 2.08f, .05f));
        Vector3 bladeDirection = caster.forward;

        if (Stage == "OverheadWindup")
        {
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .16f));
            grip = caster.TransformPoint(new Vector3(.22f, Mathf.Lerp(1.25f, .92f, u), .32f));
            bladeDirection = (caster.forward - caster.up * .5f).normalized;
        }
        else if (Stage == "Launch")
        {
            float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .20f));
            grip = caster.TransformPoint(new Vector3(.14f, Mathf.Lerp(.92f, 2.12f, u), Mathf.Lerp(.32f, .08f, u)));
            bladeDirection = Vector3.Slerp((caster.forward - caster.up * .5f).normalized, caster.forward, u);
            RotateBoneWorld(spine, caster.right, 16f * Mathf.Sin(u * Mathf.PI));
        }
        else
        {
            float u = Mathf.Clamp01(t / Mathf.Max(.1f, chargeOrbitSeconds));
            // 2.75 圈：看得出是刀在旋轉，但不會快成模糊螺旋槳。
            float spin = u * 360f * 2.75f;
            bladeDirection = Vector3.Slerp((caster.forward * .35f + caster.up).normalized,
                Quaternion.AngleAxis(spin, caster.up) * caster.forward, Mathf.SmoothStep(0, 1, t / .16f));
            grip = caster.TransformPoint(new Vector3(.04f, 2.12f + Mathf.Sin(u * Mathf.PI) * .08f, .08f));
            RotateBoneWorld(spine, caster.right, 8f);
        }

        // Keep both elbows stable. Only the independent weapon pivot rotates, not the support hand.
        Vector3 leftGrip = Stage == "AerialRotor"
            ? caster.TransformPoint(new Vector3(-.30f, 1.45f, .22f))
            : grip - caster.forward * .17f - caster.up * .025f;
        SolveArm(rightUpperArm, rightForearm, rightHand, grip, caster.right);
        // IK 的手掌實際位置可能因角色手臂長度與 Avatar 比例而達不到理論 grip。
        // 武器必須貼著解算後的手掌，否則不同模型會出現刀浮在頭頂的情況。
        Vector3 solvedGrip = rightHand != null ? rightHand.position : grip;
        if (Stage != "AerialRotor") leftGrip += solvedGrip - grip;
        SolveArm(leftUpper, leftFore, leftHand, leftGrip, -caster.right);
        weapon.position = solvedGrip;
        AlignWeaponBlade(bladeDirection, caster.up);
    }

    void CaptureHumanoidUprightPose()
    {
        humanoidUprightPose.Clear();
        if (casterAnimator == null || !casterAnimator.isHuman) return;
        for (HumanBodyBones id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++)
        {
            Transform bone = casterAnimator.GetBoneTransform(id);
            if (bone == null || humanoidUprightPose.ContainsKey(bone)) continue;
            humanoidUprightPose.Add(bone, new HumanoidBonePose
            {
                localPosition = bone.localPosition,
                localRotation = bone.localRotation
            });
        }
    }

    void RestoreHumanoidUprightPose()
    {
        foreach (var pair in humanoidUprightPose)
        {
            if (pair.Key == null) continue;
            pair.Key.localPosition = pair.Value.localPosition;
            pair.Key.localRotation = pair.Value.localRotation;
        }
    }

    void ApplyProceduralKesaSlash()
    {
        float age = FinisherClock - proceduralSlashStarted;
        if (age < 0f || age > .24f) return;
        float side = (proceduralSlashSide & 1) == 0 ? 1f : -1f;
        float cut = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - .025f) / .105f));
        float recover = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - .14f) / .10f));
        float weight = Mathf.Min(cut, recover);

        Vector3 high = caster.TransformPoint(new Vector3(.48f * side, 1.78f, .24f));
        Vector3 low = caster.TransformPoint(new Vector3(-.42f * side, .86f, .42f));
        Vector3 grip = Vector3.Lerp(high, low, cut);
        RotateBoneWorld(spine, caster.up, 26f * side * (cut - .35f) * weight);
        RotateBoneWorld(spine, caster.forward, 10f * side * weight);
        SolveArm(rightUpperArm, rightForearm, rightHand, grip, caster.right * side);
        Vector3 solvedGrip = rightHand != null ? rightHand.position : grip;
        weapon.position = solvedGrip;
        Vector3 blade = Vector3.Slerp((caster.forward + caster.right * side).normalized,
                                     (caster.forward - caster.right * side - caster.up * .45f).normalized, cut);
        AlignWeaponBlade(blade, caster.up);
    }

    static void RotateBoneWorld(Transform bone, Vector3 axis, float degrees)
    {
        if (bone != null && axis.sqrMagnitude > .0001f)
            bone.rotation = Quaternion.AngleAxis(degrees, axis.normalized) * bone.rotation;
    }

    void StabilizeCasterVisualRoot()
    {
        if (!playing || casterAnimator == null) return;
        Vector3 euler = caster.eulerAngles;
        caster.rotation = Quaternion.Euler(0f, euler.y, 0f);
        if (casterAnimator.transform == caster) return;
        casterAnimator.transform.localPosition = animatorRootRestPosition;
        casterAnimator.transform.localRotation = animatorRootRestRotation;
    }

    void RestoreCasterVisualRoot()
    {
        if (casterAnimator == null || casterAnimator.transform == caster) return;
        casterAnimator.transform.localPosition = animatorRootRestPosition;
        casterAnimator.transform.localRotation = animatorRootRestRotation;
    }

    void AlignWeaponBlade(Vector3 desiredDirection, Vector3 desiredUp)
    {
        if (weapon == null || desiredDirection.sqrMagnitude < .0001f) return;
        desiredDirection.Normalize();
        Vector3 currentDirection = weapon.TransformDirection(weaponBladeLocalDirection).normalized;
        weapon.rotation = Quaternion.FromToRotation(currentDirection, desiredDirection) * weapon.rotation;

        Vector3 currentUp = Vector3.ProjectOnPlane(weapon.up, desiredDirection).normalized;
        Vector3 targetUp = Vector3.ProjectOnPlane(desiredUp, desiredDirection).normalized;
        if (currentUp.sqrMagnitude > .0001f && targetUp.sqrMagnitude > .0001f)
            weapon.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(currentUp, targetUp, desiredDirection), desiredDirection) * weapon.rotation;
    }

    static void SolveArm(Transform upper,Transform fore,Transform hand,Vector3 goal,Vector3 pole)
    {
        if(!upper||!fore||!hand) return;
        Vector3 root=upper.position, dir=(goal-root).normalized;
        float a=Vector3.Distance(root,fore.position), b=Vector3.Distance(fore.position,hand.position);
        float d=Mathf.Clamp(Vector3.Distance(root,goal),.01f,a+b-.001f);
        float x=(a*a-b*b+d*d)/(2*d), y=Mathf.Sqrt(Mathf.Max(0,a*a-x*x));
        Vector3 bend=Vector3.ProjectOnPlane(pole-Vector3.up*.6f,dir).normalized;
        Vector3 elbow=root+dir*x+bend*y;
        upper.rotation=Quaternion.FromToRotation(fore.position-root,elbow-root)*upper.rotation;
        fore.rotation=Quaternion.FromToRotation(hand.position-fore.position,goal-fore.position)*fore.rotation;
    }

    void FracturedCut(Vector3 from,Vector3 to,float width,float life,bool fracture)
    {
        if((to-from).sqrMagnitude<.0001f)return;
        // Keep beams straight: the fullscreen fracture pass cuts and displaces them.
        int segments=1;
        Vector3 direction=(to-from).normalized;
        Vector3 side=Vector3.Cross(direction,Vector3.up).normalized;
        for(int i=0;i<segments;i++)
        {
            float start=i/(float)segments,end=(i+(fracture?.9f:1f))/segments;
            Vector3 offset=Vector3.zero;
            Vector3 a=Vector3.Lerp(from,to,start)+offset,b=Vector3.Lerp(from,to,end)+offset;
            Vector3 mid=Vector3.Lerp(a,b,.52f);
            var points=new[]{a,mid,b};
            var glow = SpawnRibbon(points,slashMaterial,width*2.4f,life,new Color(.15f,.45f,1,.18f));
            var core = SpawnRibbon(points,slashMaterial,width*.65f,life*.8f,pale);
            var taper = new AnimationCurve(new Keyframe(0,.06f),new Keyframe(.35f,1),new Keyframe(.7f,.65f),new Keyframe(1,.02f));
            glow.widthCurve=taper; core.widthCurve=taper;
        }
    }

    void SpawnSpatialLattice(Vector3 center, int seed, int count, float life)
    {
        var random = new System.Random(seed * 7919 + 31);
        for (int i = 0; i < count; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2;
            Vector3 direction = new Vector3(Mathf.Cos(angle), (float)random.NextDouble() * .9f - .45f, Mathf.Sin(angle)).normalized;
            Vector3 offset = new Vector3((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f) * 9f;
            float length = effectRangeMultiplier * Mathf.Lerp(3f, 6f, (float)random.NextDouble());
            SpawnSlashPair(center + offset, direction, length, .018f + (float)random.NextDouble()*.018f, life, cyan);
        }
    }

    ParticleSystem CreateSmoke(Vector3 position,bool continuous,float life,float size)
    {
        var go=new GameObject("VoidRend_BlueVapor");go.SetActive(false);
        go.transform.SetParent(effectsRoot,false);go.transform.position=position;
        var ps=go.AddComponent<ParticleSystem>();var main=ps.main;
        main.loop=continuous;main.duration=continuous?5:.1f; main.playOnAwake=false;
        main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,life);
        main.startSpeed=new ParticleSystem.MinMaxCurve(.3f,1.1f);
        main.startSize=new ParticleSystem.MinMaxCurve(size,size*2.2f);
        main.startRotation=new ParticleSystem.MinMaxCurve(0,6.28f);
        main.startColor=new Color(.16f,.68f,1,continuous?.26f:.16f);
        main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=120;main.useUnscaledTime=true;
        var e=ps.emission;e.rateOverTime=continuous?25:0;
        if(!continuous)e.SetBursts(new[]{new ParticleSystem.Burst(0, (short)45)});
        var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Cone;sh.angle=continuous?12:75;sh.radius=continuous?.35f:1.2f;
        go.transform.rotation=Quaternion.Euler(-90,0,0);
        var v=ps.velocityOverLifetime;v.enabled=true;v.space=ParticleSystemSimulationSpace.World;
        v.x=new ParticleSystem.MinMaxCurve(0,0);v.y=new ParticleSystem.MinMaxCurve(.7f,1.4f);v.z=new ParticleSystem.MinMaxCurve(0,0);
        var n=ps.noise;n.enabled=true;n.strength=.35f;n.frequency=.65f;n.scrollSpeed=.8f;
        var co=ps.colorOverLifetime;co.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.1f,.45f,1),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(0,1)});co.color=g;
        var sz=ps.sizeOverLifetime;sz.enabled=true;sz.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.4f,1,1.7f));
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=smokeMaterial;
        go.SetActive(true);ps.Play();if(!continuous)Destroy(go,life+1);
        return ps;
    }

    // ---------------- 防卡牆 / 防卡死 ----------------

    bool IgnoredByWallCheck(Collider c) =>
        c == null || c.isTrigger || (caster != null && c.transform.IsChildOf(caster)) || (target != null && c.transform.IsChildOf(target))
        || c is CharacterController; // 其他角色（小怪）會走開，不算牆

    float CasterHeight() => practiceBody != null ? practiceBody.height : 2f;
    /// <summary>角色原點（技能開始時的位置）比腳底高多少</summary>
    float CasterRootAboveFeet() => practiceBody != null ? practiceBody.height * .5f - practiceBody.center.y : 1f;

    /// <summary>角色的膠囊放在 root（角色原點）這裡會不會撞到東西（自己、目標、觸發器不算）</summary>
    bool CasterCapsuleBlocked(Vector3 root, float shrink = .95f)
    {
        float r = CasterRadius() * shrink, h = CasterHeight();
        Vector3 feet = root - Vector3.up * CasterRootAboveFeet();
        Vector3 a = feet + Vector3.up * (r + .06f), b = feet + Vector3.up * Mathf.Max(r + .06f, h - r);
        foreach (var c in Physics.OverlapCapsule(a, b, r, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoredByWallCheck(c)) return true;
        return false;
    }

    /// <summary>p 正下方的地板高度（找不到就回傳 false）</summary>
    bool GroundBelow(Vector3 p, float up, float down, out float groundY)
    {
        groundY = 0f;
        float best = float.MaxValue; bool found = false;
        foreach (var hit in Physics.RaycastAll(p + Vector3.up * up, Vector3.down, up + down, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IgnoredByWallCheck(hit.collider) || hit.normal.y < .5f) continue;
            if (hit.distance < best) { best = hit.distance; groundY = hit.point.y; found = true; }
        }
        return found;
    }

    bool PathBlocked(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; float len = d.magnitude;
        if (len < .01f) return false;
        foreach (var hit in Physics.SphereCastAll(from, .25f, d / len, len, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoredByWallCheck(hit.collider)) return true;
        return false;
    }

    /// <summary>
    /// 收招的落點：從 preferredDir 開始、左右輪流轉開找，距離由遠到近。要同時符合：
    /// 有地板（高低差不大）、角色站得下、跟目標之間沒有牆、空中停的高度沒有撞到天花板。
    /// 都找不到就退回技能開始時站的地方（那裡一定站得下）。dir 會改成實際用的方向。
    /// </summary>
    Vector3 FindSafeRecoveryPoint(Vector3 victim, ref Vector3 dir, float distance, float wantHold, out float hold)
    {
        float rootAbove = CasterRootAboveFeet();
        float startFeet = casterStartPosition.y - rootAbove;
        Vector3 from = new Vector3(victim.x, startFeet + 1.2f, victim.z);
        float[] angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };
        float[] scales = { 1f, .75f, .5f };
        foreach (float s in scales)
            foreach (float a in angles)
            {
                Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * dir;
                float dist = Mathf.Max(1.8f, distance * s);
                Vector3 p = new Vector3(victim.x, startFeet, victim.z) + d * dist;
                if (!GroundBelow(p, 3f, 4f, out float g) || Mathf.Abs(g - startFeet) > 1.6f) continue;
                Vector3 root = new Vector3(p.x, g + rootAbove + .02f, p.z);
                if (CasterCapsuleBlocked(root)) continue;
                if (PathBlocked(from, new Vector3(p.x, g + 1.2f, p.z))) continue;
                // 空中停的高度：上面有天花板 / 屋簷就降低
                float h = wantHold;
                if (h > 0f)
                {
                    Vector3 top = root + Vector3.up * (CasterHeight() - rootAbove);
                    foreach (var hit in Physics.RaycastAll(top, Vector3.up, h + .4f, ~0, QueryTriggerInteraction.Ignore))
                        if (!IgnoredByWallCheck(hit.collider)) h = Mathf.Min(h, Mathf.Max(0f, hit.distance - .4f));
                    if (h > 0f && CasterCapsuleBlocked(root + Vector3.up * h)) h = 0f;
                }
                dir = d; hold = h;
                return root;
            }
        Vector3 back = casterStartPosition - new Vector3(victim.x, casterStartPosition.y, victim.z);
        if (back.sqrMagnitude > .01f) dir = back.normalized;
        hold = 0f;
        return casterStartPosition;
    }

    /// <summary>交還操作前：角色如果跟牆 / 房子重疊，就搬到附近站得下的地方（找不到就回技能開始的位置）</summary>
    void EnsureCasterNotInsideWalls()
    {
        if (caster == null || practiceBody == null) return;
        Physics.SyncTransforms();
        if (!CasterCapsuleBlocked(caster.position)) return;
        Vector3 c = caster.position;
        for (float r = .5f; r <= 4f; r += .5f)
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = c + Quaternion.AngleAxis(i * 30f, Vector3.up) * Vector3.forward * r;
                if (GroundBelow(p, 3f, 6f, out float g)) p.y = Mathf.Max(p.y, g + CasterRootAboveFeet() + .02f);
                if (!CasterCapsuleBlocked(p) && !PathBlocked(c, p)) { caster.position = p; return; }
            }
        caster.position = casterStartPosition;
    }

    /// <summary>技能的協程外面包一層：中途出錯（例如目標在技能中被刪掉）就取消技能、把操作還給玩家，不會卡住不能動</summary>
    IEnumerator GuardedSequence(IEnumerator inner)
    {
        while (true)
        {
            // Cinematic motion uses real time during slow motion, but pauses with the menu.
            while (playing && Time.timeScale <= 0f) yield return null;
            object current;
            try
            {
                if (!inner.MoveNext()) yield break;
                current = inner.Current;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
                Debug.LogWarning("[虛空連斬] 技能中途出錯，已取消並把操作還給玩家。", this);
                if (playing) Time.timeScale = priorTimeScale > 0f ? priorTimeScale : 1f;
                if (weaponPresenter) weaponPresenter.EndSkillDraw(true);
                if (casterAnimator) { casterAnimator.enabled = true; casterAnimator.speed = 1f; casterAnimator.CrossFadeInFixedTime("Idle", .1f, 0, 0f); }
                ReleaseVictim(false);
                CancelSkill();
                yield break;
            }
            if (current is WaitForSecondsRealtime wait)
            {
                float remaining = wait.waitTime;
                while (remaining > 0f)
                {
                    yield return null;
                    if (Time.timeScale > 0f) remaining -= Time.unscaledDeltaTime;
                }
            }
            else yield return current;
        }
    }

    public void CancelSkill()
    {
        StopAllCoroutines(); CleanupFinisher(); RestorePose(); RestoreCamera();
        if(caster) { caster.position=casterStartPosition;caster.rotation=casterStartRotation; }
        if(effectsRoot) foreach(Transform fx in effectsRoot)Destroy(fx.gameObject);
        ReleaseCinematicCamera();
        if (practiceControlsSuspended) RestorePracticeControlsAfterSkill();
        playing=false;cinematicCamera=false;voidRendCinematicActive=false;fixedShot=false;Stage="Cancelled";
    }

    /// <summary>鏡頭 A 後面是牆的話，把鏡頭往角色那邊拉回來（不要拍到牆裡面）</summary>
    Vector3 KeepCameraOutOfWalls(Vector3 from, Vector3 desired)
    {
        Vector3 d = desired - from;
        float best = d.magnitude;
        if (best < .01f) return desired;
        foreach (var hit in Physics.SphereCastAll(from, .2f, d / best, best, ~0, QueryTriggerInteraction.Ignore))
        {
            if (caster != null && hit.transform.IsChildOf(caster)) continue;
            if (target != null && hit.transform.IsChildOf(target)) continue;
            if (hit.distance > 0f && hit.distance < best) best = hit.distance;
        }
        return from + d.normalized * Mathf.Max(.4f, best - .1f);
    }

    void CleanupFinisher()
    {
        fixedShot = false;
        SetWindupInvulnerability(false);
        ReleaseDashLines(.15f);
        ReleaseVictim();SetDistortion(0);
        voidRendCinematicActive=false;
        if(casterAnimator && playing)
        {
            casterAnimator.enabled=animatorWasEnabled;
            casterAnimator.applyRootMotion=animatorRootMotionWasEnabled;
            RestoreCasterVisualRoot();
            RestoreHumanoidUprightPose();
        }
        if (weaponPresenter) weaponPresenter.EndSkillDraw(true);
        if(chargeSmoke)Destroy(chargeSmoke.gameObject);
        foreach(var p in poseRest)if(p.Key)p.Key.localRotation=p.Value;
        if(weapon){weapon.localScale=Vector3.one;weapon.localRotation=weaponRestRotation;}
        if(pelvis)pelvis.localPosition=pelvisRest;
        if(playing)Time.timeScale=priorTimeScale;
    }

#if UNITY_EDITOR
    IEnumerator CaptureFinisher()
    {
        float[] marks = { .45f, 3.5f, 4.3f, 7.8f, 10.8f, 13.2f };
        string[] names = { "Launch", "Charge", "Punch", "Lattice", "Sheathe", "End" };
        System.IO.Directory.CreateDirectory("Temp/VoidRendReview");
        for (int i=0;i<marks.Length;i++)
        {
            while (FinisherClock-sequenceStart < marks[i]) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot("Temp/VoidRendReview/"+names[i]+".png");
            Debug.Log("[VoidRend capture] "+names[i]+" stage="+Stage+" elapsed="+Elapsed+" locked="+TargetLocked);
        }
        while(playing)yield return null;
        Debug.Assert(!TargetLocked,"Finisher must release its target.");
        Debug.Assert(Vector3.Distance(target.position,victimLanding)<.001f,"Target must remain at the landing position.");
        Debug.Assert(Vector3.Dot(target.position-caster.position,caster.forward)<-1,"Target must land behind the caster.");
        Debug.Log("[VoidRend validation] completed="+CompletedRuns+" targetRestored="+(!TargetLocked)+" chargeOrbitSeconds="+chargeOrbitSeconds+" rangeMultiplier="+effectRangeMultiplier);
    }
#endif
}

public sealed class VoidRendWarpLine:MonoBehaviour
{
    LineRenderer line;Vector3[] rest;
    void Start(){line=GetComponent<LineRenderer>();rest=new Vector3[line.positionCount];line.GetPositions(rest);}
    void LateUpdate(){if(!line)return;for(int i=0;i<rest.Length;i++)line.SetPosition(i,rest[i]+Vector3.up*Mathf.Sin(Time.unscaledTime*13+i*4)*.08f);}
}
