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
    Quaternion victimRotation;
    float cameraYaw = 160, cameraRadius = 8, cameraHeight = 2.2f, cinematicFov = 48, cameraRoll;
    float charge, distortion, sequenceStart, stageTime;
    bool targetLocked, cinematicCamera, voidRendCinematicActive;
    RenderPipelineAsset previousPipeline;
    bool pipelineChanged;
    Rigidbody victimBody;
    bool wasKinematic, usedGravity;
    Vector3 oldVelocity, oldAngularVelocity;
    readonly Dictionary<Behaviour, bool> suspended = new Dictionary<Behaviour, bool>();
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
        victimHome = target.position;
        victimRotation = target.rotation;
        victimHeld = victimHome;
        victimBody = target.GetComponent<Rigidbody>();
        if (victimBody)
        {
            wasKinematic = victimBody.isKinematic; usedGravity = victimBody.useGravity;
            oldVelocity = victimBody.linearVelocity; oldAngularVelocity = victimBody.angularVelocity;
            victimBody.isKinematic = true; victimBody.useGravity = false;
        }
        suspended.Clear();
        foreach (Behaviour controller in targetControllers)
            SuspendVictimBehaviour(controller);
        foreach (Behaviour controller in target.GetComponentsInChildren<Behaviour>(true))
            if (ShouldSuspendVictimBehaviour(controller)) SuspendVictimBehaviour(controller);
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

    void SuspendVictimBehaviour(Behaviour controller)
    {
        if (controller == null || controller == this || suspended.ContainsKey(controller)) return;
        if (controller is EnemyCombat combat) combat.CancelAttack();
        if (controller is ThirdPersonController movement)
        {
            movement.SetAIInput(Vector3.zero);
            movement.ResetMotion();
        }
        suspended.Add(controller, controller.enabled);
        controller.enabled = false;
    }

    void ReleaseVictim(bool restorePosition = true)
    {
        if (!targetLocked) return;
        if (target) { target.position = restorePosition ? victimHome : victimLanding; target.rotation = victimRotation; }
        if (victimBody)
        {
            victimBody.isKinematic = wasKinematic; victimBody.useGravity = usedGravity;
            if (!wasKinematic) { victimBody.linearVelocity = oldVelocity; victimBody.angularVelocity = oldAngularVelocity; }
        }
        foreach (var pair in suspended) if (pair.Key) pair.Key.enabled = pair.Value;
        suspended.Clear(); targetLocked = false;
        onTargetReleased.Invoke();
    }

    void SetStage(string stage) { Stage = stage; stageTime = Time.unscaledTime; }
    void SetDistortion(float amount)
    {
        distortion = amount;
        Shader.SetGlobalFloat("_SpatialStrength", amount);
        Shader.SetGlobalFloat("_SpatialClock", Time.unscaledTime);
        if (refractionMaterial) { refractionMaterial.SetFloat("_Strength", amount); refractionMaterial.SetFloat("_Clock", Time.unscaledTime); }
    }

    IEnumerator PlaySequence()
    {
        SuspendPracticeControlsForSkill();
        playing = true; priorTimeScale = Time.timeScale; sequenceStart = Time.unscaledTime;
        ResetPreviewState();
        // Reset the isolated demo only when the next replay actually begins.
        if(completedLanding) { target.position=previewVictimStart; completedLanding=false; }
        LockVictim();
        if (casterAnimator)
        {
            animatorWasEnabled = casterAnimator.enabled;
            animatorRootMotionWasEnabled = casterAnimator.applyRootMotion;
            casterAnimator.applyRootMotion = false;
            if (useLegacyProceduralPose) casterAnimator.enabled = false;
            else
            {
                // 先取樣一幀真正的站姿，再關掉 Animator。Z 技能期間由程序姿勢接手，
                // 匯入動畫就不會在後段把骨盆翻倒或讓腿誤播跑步。
                casterAnimator.enabled = true;
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
        arenaCenter = new Vector3(victimHome.x, casterStartPosition.y, victimHome.z);
        cameraFocus = arenaCenter + Vector3.up * 1.5f;
        cameraYaw = 155; cameraRadius = 8; cameraHeight = 2.2f; cinematicFov = 48;
        float normalCameraRadius = Mathf.Clamp(Vector3.ProjectOnPlane(cameraFollowOffset, Vector3.up).magnitude, 4.5f, 10f);
        float normalCameraHeight = Mathf.Clamp(cameraFollowOffset.y - cameraLookOffset.y, .8f, 4f);
        float spinCameraStartRadius = Mathf.Max(7.2f, normalCameraRadius);
        float spinCameraEndRadius = spinCameraStartRadius + 3.4f;

        // 先面向目標跑到擊飛位置；只啟動一次 Run，逐幀推進而不重播起始幀。
        SetStage("ApproachRun");
        Vector3 approachStart = caster.position;
        Vector3 approachDirection = Vector3.ProjectOnPlane(arenaCenter - approachStart, Vector3.up);
        float approachDistance = approachDirection.magnitude;
        if (approachDistance > .05f)
        {
            caster.rotation = Quaternion.LookRotation(approachDirection.normalized, Vector3.up);
            bool sampleRun = casterAnimator != null && !useLegacyProceduralPose;
            if (sampleRun)
            {
                casterAnimator.SetFloat("Speed", 1f);
                casterAnimator.SetBool("Grounded", true);
                casterAnimator.SetFloat("VerticalSpeed", 0f);
                casterAnimator.Play("Run", 0, 0f);
            }
            float approachDuration = Mathf.Max(.06f, approachDistance / 24f);
            for (float t = 0; t < approachDuration; t += Time.unscaledDeltaTime)
            {
                caster.position = Vector3.Lerp(approachStart, arenaCenter, Mathf.Clamp01(t / approachDuration));
                if (sampleRun) casterAnimator.Update(Time.unscaledDeltaTime * 2f);
                cameraFocus = caster.position + Vector3.up * 1.35f;
                yield return null;
            }
        }
        caster.position = arenaCenter;
        if (casterAnimator != null && !useLegacyProceduralPose)
        {
            casterAnimator.SetFloat("Speed", 0f);
            casterAnimator.Play("Idle", 0, 0f);
            casterAnimator.Update(0f);
        }

        // 地面蓄力：先微蹲並把刀舉到頭頂，避免一開始就用不相干的對空斬。
        SetStage("OverheadWindup");
        SpawnRing(caster.position + Vector3.up * 0.06f, 0.18f, 1.75f, .08f, .62f, cyan);
        for (float t = 0; t < .42f; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / .42f);
            cameraRadius = Mathf.Lerp(7.2f, spinCameraStartRadius, Mathf.SmoothStep(0, 1, u));
            cameraHeight = Mathf.Lerp(2.0f, 1.85f, u);
            cameraFocus = caster.position + Vector3.up * 1.35f;
            cinematicFov = Mathf.Lerp(48f, 47f, u);
            SetDistortion(.025f * u);
            yield return null;
        }

        SetStage("Launch");
        SpawnArc(victimHome, Quaternion.Euler(0, 0, 80), 3.2f, -100, 100, .18f, .55f, pale);
        PunchCamera(.78f, 7); Flash(cyan, .18f);
        for (float t = 0; t < .6f; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / .6f);
            victimHeld = victimHome + Vector3.up * (launchHeight * (1 - Mathf.Pow(1-u, 3)));
            yield return null;
        }
        victimHeld = victimHome + Vector3.up * launchHeight;
        caster.position = arenaCenter;
        chargeSmoke = CreateSmoke(caster.position, true, 3.5f, .38f);
        chargeSmoke.transform.SetParent(caster, true);
        SetStage("AerialRotor");
        float nextParticle = 0;
        for (float t = 0; t < chargeOrbitSeconds; t += Time.unscaledDeltaTime)
        {
            float u = t / chargeOrbitSeconds;
            charge = u;
            // 轉刀期間角色固定站穩，只有手臂與武器在頭頂旋轉。
            caster.position = arenaCenter;
            FaceCurrentTarget();
            cameraYaw = Mathf.Lerp(155, 0, Mathf.SmoothStep(0,1,u));
            // 蓄力轉刀時鏡頭一邊環繞、一邊緩慢拉遠，讓力量逐漸累積而不是越拍越近。
            cameraRadius = Mathf.Lerp(spinCameraStartRadius, spinCameraEndRadius, Mathf.SmoothStep(0,1,u));
            cameraHeight = Mathf.Lerp(1.85f,2.45f,u);
            cameraFocus = caster.position + Vector3.up * 1.5f;
            cinematicFov = Mathf.Lerp(47f,52f,u);
            if (t >= nextParticle)
            {
                nextParticle += .12f;
                SpawnParticles(caster.position + Vector3.up * 1.4f, 6, .7f, .65f, .008f, .024f, cyan);
                if (Mathf.FloorToInt(t / .38f) != Mathf.FloorToInt((t - .12f) / .38f))
                    SpawnArc(caster.position + Vector3.up * 2.05f, Quaternion.Euler(90f, 0f, 0f), 1.2f, -150f, 150f, .055f, .42f, pale);
            }
            SetDistortion(.035f * u);
            yield return null;
        }
        caster.position = arenaCenter;
        FaceCurrentTarget();
        chargeSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(chargeSmoke.gameObject, 4);
        SetStage("Punch"); charge = 1;
        if (!useLegacyProceduralPose) PlaySwordAnimation(1);
        Vector3 fist = caster.position + Vector3.up*1.8f + caster.forward*.65f;
        SpawnRing(fist, .2f, 4.7f * effectRangeMultiplier, .26f, .95f, pale);
        SpawnParticles(fist, 160, 1.4f, 16, .02f, .12f, cyan);
        CreateSmoke(fist, false, 2.5f, 1.4f);
        Flash(pale, .68f); PunchCamera(.9f, 12); SetDistortion(.85f);
        yield return Wait(.42f);
        SetStage("DashLattice");
        Vector3 center = victimHeld;
        cameraFocus = center - Vector3.up*1.2f;
        Vector3 last = caster.position;
        int cuts = 22;
        for (int i=0; i<cuts; i++)
        {
            if (!useLegacyProceduralPose && i % 3 == 0) PlaySwordAnimation(i / 3);
            float angle = (i * 137.508f + 24) * Mathf.Deg2Rad;
            float radius = teleportRadius * effectRangeMultiplier * (i%3 == 0 ? .8f : .57f);
            Vector3 next = arenaCenter + new Vector3(Mathf.Cos(angle)*radius, 2 + (i%5)*1.05f, Mathf.Sin(angle)*radius);
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
            DamageCurrentTarget(voidRendDashDamage);
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
        Vector3 recoveryGround = victimHome + recoveryForward * 2f;
        recoveryGround.y = casterStartPosition.y;
        Quaternion recoveryRotation = Quaternion.LookRotation(recoveryForward, Vector3.up);
        Quaternion recoveryStartRotation = caster.rotation;
        float frontCameraYaw = Mathf.Atan2(recoveryForward.x, recoveryForward.z) * Mathf.Rad2Deg;
        float yawStart=cameraYaw;
        float radiusStart=cameraRadius, heightStart=cameraHeight, rollStart=cameraRoll;
        Vector3 focusStart=cameraFocus, heldStart=victimHeld;
        // 目標留在切割與爆炸中心，爆炸後才垂直落回原地。
        victimLanding=victimHome;
        Vector3 recoveryFocus=recoveryGround+Vector3.up*1.25f;
        for(float t=0;t<2.4f;t+=Time.unscaledDeltaTime)
        {
            // 先靠近目標前方，在爆炸前落地一次，目標隨後落下。
            float travel=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/1.75f));
            float fall=Mathf.Clamp01((t-1.75f)/.65f);
            Vector3 landingPath=Vector3.Lerp(recoveryFallStart,recoveryGround,travel);
            landingPath.y=Mathf.Lerp(recoveryFallStart.y,recoveryGround.y,fall*fall);
            caster.position=landingPath;
            caster.rotation=Quaternion.Slerp(recoveryStartRotation,recoveryRotation,travel);
            float u=t/2.4f;
            cameraYaw=Mathf.LerpAngle(yawStart,frontCameraYaw,Mathf.SmoothStep(0,1,u));
            float ease=Mathf.SmoothStep(0,1,u);
            cameraRadius=Mathf.Lerp(radiusStart,recoveryCameraDistance,ease); cameraHeight=Mathf.Lerp(heightStart,1.6f,ease);
            cameraFocus=Vector3.Lerp(focusStart,recoveryFocus,ease);
            cameraRoll=Mathf.Lerp(rollStart,0,ease); cinematicFov=Mathf.Lerp(64,60,ease);
            victimHeld=Vector3.Lerp(heldStart,victimLanding+Vector3.up*launchHeight,ease);
            SetDistortion(.18f + Mathf.Sin(t*7)*.08f);
            yield return null;
        }
        caster.position=recoveryGround;
        caster.rotation=recoveryRotation;
        cameraYaw=frontCameraYaw; cameraRadius=recoveryCameraDistance; cameraHeight=1.6f;
        cameraFocus=recoveryFocus; cinematicFov=60; cameraRoll=0;
        victimHeld=victimLanding+Vector3.up*launchHeight;
        SetStage("FinalBreak");
        ReleaseDashLines(.4f);
        onFinisherImpact.Invoke();
        DamageCurrentTarget(voidRendDamage);
        SpawnSpaceRift(center);
        SpawnSpatialLattice(center, 37, 32, 1.8f);
        SpawnRing(arenaCenter+.1f*Vector3.up,.5f,4.7f*effectRangeMultiplier,.4f,1.5f,pale);
        CreateSmoke(arenaCenter+Vector3.up,false,3.2f,2.4f);
        SpawnParticles(center,150,1.5f,18,.03f,.15f,pale);
        Flash(Color.white,.65f); PunchCamera(1.1f,9); SetDistortion(.72f);
        yield return Wait(.12f);
        for(float t=0;t<.8f;t+=Time.unscaledDeltaTime)
        {
            float u=t/.8f;
            victimHeld=Vector3.Lerp(victimLanding+Vector3.up*launchHeight,victimLanding,u*u);
            SetDistortion((1-u)*.35f);
            yield return null;
        }
        // 已在收勢時自然落地，不再抬高角色或播放第二次落地。
        float recoveryYaw=cameraYaw;
        for(float t=0;t<.35f;t+=Time.unscaledDeltaTime)
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
        SetStage("Complete"); playing=false; CompletedRuns++;
        if(casterAnimator)
        {
            casterAnimator.enabled=animatorWasEnabled;
            casterAnimator.applyRootMotion=animatorRootMotionWasEnabled;
            RestoreCasterVisualRoot();
            if (!useLegacyProceduralPose) casterAnimator.CrossFadeInFixedTime("Idle", 0.16f, 0, 0f);
        }
        if (weaponPresenter) weaponPresenter.EndSkillDraw(true);
        Time.timeScale=priorTimeScale;
        RestorePracticeControlsAfterSkill();
        if(loopPreview) { yield return Wait(loopDelay+2); PlaySkill(); }
    }

    void UpdateFinisherCamera(Vector3 shake,Vector3 rotationShake)
    {
        if(targetLocked && target) { target.position=victimHeld; target.rotation=victimRotation; }
        if(playing) Elapsed=Time.unscaledTime-sequenceStart;
        if(refractionMaterial) refractionMaterial.SetFloat("_Clock",Time.unscaledTime);
        if (playing) Shader.SetGlobalFloat("_SpatialClock", Time.unscaledTime);
        // Z 的電影鏡頭必須一路維持到落地姿勢完成，不能被一般環繞鏡頭提早接管。
        if(!cinematicCamera && !voidRendCinematicActive) return;
        cinematicCamera = true;
        float a=cameraYaw*Mathf.Deg2Rad;
        Vector3 p=cameraFocus+new Vector3(Mathf.Sin(a)*cameraRadius,cameraHeight,Mathf.Cos(a)*cameraRadius);
        Quaternion rotation=Quaternion.LookRotation(cameraFocus-p)*Quaternion.Euler(rotationShake+Vector3.forward*cameraRoll);
        QueueCinematicCameraPose(p+shake, rotation, cinematicFov-fovKick);
    }

    void ApplyFinisherPose()
    {
        if (Stage == "ApproachRun") return;
        foreach(var p in poseRest) if(p.Key) p.Key.localRotation=p.Value;
        float t=Time.unscaledTime-stageTime;
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
        if (Stage == "ApproachRun") return;
        RestoreHumanoidUprightPose();
        if (weapon == null) return;

        float t = Time.unscaledTime - stageTime;

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
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .32f));
            grip = caster.TransformPoint(new Vector3(.08f, Mathf.Lerp(1.45f, 2.12f, u), .08f));
            bladeDirection = Vector3.Slerp(caster.right, caster.forward, u).normalized;
        }
        else if (Stage == "Launch")
        {
            float u = Mathf.Clamp01(t / .6f);
            grip = caster.TransformPoint(new Vector3(.08f, Mathf.Lerp(2.12f, 2.22f, u), Mathf.Lerp(.08f, .26f, u)));
            bladeDirection = caster.forward;
            RotateBoneWorld(spine, caster.right, 16f * Mathf.Sin(u * Mathf.PI));
        }
        else
        {
            float u = Mathf.Clamp01(t / Mathf.Max(.1f, chargeOrbitSeconds));
            // 2.75 圈：看得出是刀在旋轉，但不會快成模糊螺旋槳。
            float spin = u * 360f * 2.75f;
            bladeDirection = Quaternion.AngleAxis(spin, caster.up) * caster.forward;
            grip = caster.TransformPoint(new Vector3(.04f, 2.12f + Mathf.Sin(u * Mathf.PI) * .08f, .08f));
            RotateBoneWorld(spine, caster.right, 8f);
        }

        Vector3 leftGrip = grip - bladeDirection * .17f - caster.up * .025f;
        SolveArm(rightUpperArm, rightForearm, rightHand, grip, caster.right);
        // IK 的手掌實際位置可能因角色手臂長度與 Avatar 比例而達不到理論 grip。
        // 武器必須貼著解算後的手掌，否則不同模型會出現刀浮在頭頂的情況。
        Vector3 solvedGrip = rightHand != null ? rightHand.position : grip;
        leftGrip += solvedGrip - grip;
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
        float age = Time.unscaledTime - proceduralSlashStarted;
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

    public void CancelSkill()
    {
        StopAllCoroutines(); CleanupFinisher(); RestorePose(); RestoreCamera();
        if(caster) { caster.position=casterStartPosition;caster.rotation=casterStartRotation; }
        if(effectsRoot) foreach(Transform fx in effectsRoot)Destroy(fx.gameObject);
        ReleaseCinematicCamera();
        playing=false;cinematicCamera=false;voidRendCinematicActive=false;Stage="Cancelled";
    }

    void CleanupFinisher()
    {
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
            while (Time.unscaledTime-sequenceStart < marks[i]) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot("Temp/VoidRendReview/"+names[i]+".png");
            Debug.Log("[VoidRend capture] "+names[i]+" stage="+Stage+" elapsed="+Elapsed+" locked="+TargetLocked);
        }
        while(playing)yield return null;
        Debug.Assert(!TargetLocked,"Finisher must release its target.");
        Debug.Assert(Vector3.Distance(target.position,victimLanding)<.001f,"Target must remain at the landing position.");
        Debug.Assert(Vector3.Dot(target.position-caster.position,caster.forward)<-1,"Target must land behind the caster.");
        Debug.Assert(cameraRadius>=recoveryCameraDistance-.01f,"Recovery shot must stay wide.");
        Debug.Assert(Mathf.Abs(cameraYaw-360)<1,"Recovery camera must finish in front of the caster.");
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
