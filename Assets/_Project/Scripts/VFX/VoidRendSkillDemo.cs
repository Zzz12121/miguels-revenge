using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Standalone, model-agnostic showcase controller inspired by rapid spatial sword cuts.
/// It intentionally owns only preview choreography; production combat can call PlaySkill().
/// </summary>
[DefaultExecutionOrder(12000)]
public sealed partial class VoidRendSkillDemo : MonoBehaviour
{
    [Header("Scene References")]
    public Transform caster;
    public Transform target;
    public Camera skillCamera;

    [Header("Shared Player Integration")]
    [Tooltip("顯示所有場景共用的技能按鍵與鎖定提示。")]
    public bool showSkillHud = true;

    [Header("Reusable Materials")]
    public Material slashMaterial;
    public Material riftCoreMaterial;
    public Material afterimageMaterial;
    public Material particleMaterial;

    [Header("Preview")]
    public bool playOnStart = true;
    public bool loopPreview = true;
    [Min(0.5f)] public float loopDelay = 1.7f;
    [Min(2f)] public float teleportRadius = 3.4f;
    [Range(3, 10)] public int teleportCount = 6;

    readonly Color cyan = new Color(0.18f, 0.82f, 1f, 1f);
    readonly Color pale = new Color(0.82f, 0.98f, 1f, 1f);
    readonly Color violet = new Color(0.52f, 0.28f, 1f, 1f);

    Transform effectsRoot;
    Vector3 casterStartPosition;
    Quaternion casterStartRotation;
    Vector3 cameraStartPosition;
    Quaternion cameraStartRotation;
    float cameraStartFov;
    float fixedDeltaStart;
    bool playing;
    float shakeStrength;
    float fovKick;
    float flashAlpha;
    Color flashColor = Color.white;
    float posePulse;
    float poseSide = 1f;
    VoidRendCinematicCameraDriver cinematicCameraDriver;
    Vector3 queuedCameraPosition;
    Quaternion queuedCameraRotation;
    float queuedCameraFov;
    bool hasQueuedCameraPose;

    Transform rightUpperArm;
    Transform rightForearm;
    Transform rightHand;
    Transform spine;
    Quaternion rightUpperBase;
    Quaternion rightForearmBase;
    Quaternion rightHandBase;
    Quaternion spineBase;

    void Awake()
    {
        // Keep the showcase deterministic while Codex/CLI captures the Game View in the background.
        Application.runInBackground = true;
        ResolveRuntimeReferences();
        if (skillCamera == null) skillCamera = Camera.main;
        if (caster != null)
        {
            casterStartPosition = caster.position;
            casterStartRotation = caster.rotation;
            CachePoseBones();
        }

        if (skillCamera != null)
        {
            cameraStartPosition = skillCamera.transform.position;
            cameraStartRotation = skillCamera.transform.rotation;
            cameraStartFov = skillCamera.fieldOfView;
            cinematicCameraDriver = skillCamera.GetComponent<VoidRendCinematicCameraDriver>();
            if (cinematicCameraDriver == null)
                cinematicCameraDriver = skillCamera.gameObject.AddComponent<VoidRendCinematicCameraDriver>();
        }

        fixedDeltaStart = Time.fixedDeltaTime;
        CreateEffectsRoot();
        InitializeFinisher();
        InitializeTargetingAndMovement();
    }

    void ResolveRuntimeReferences()
    {
        // 技能 Prefab 掛在共用 PlayerRig/Player 底下時，不需要每個場景重新指定角色。
        // 靶場、正式關卡與 Resources 動態生成的玩家因此都會走同一套控制與技能。
        if (caster == null)
        {
            ThirdPersonController owner = GetComponentInParent<ThirdPersonController>();
            if (owner != null) caster = owner.transform;
        }

        if (skillCamera == null) skillCamera = Camera.main;
    }

    IEnumerator Start()
    {
#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool("VoidRend.Capture", false))
        {
            UnityEditor.SessionState.SetBool("VoidRend.Capture", false);
            loopPreview = false;
            yield return new WaitForSecondsRealtime(.8f);
            PlaySkill();
            yield return CaptureFinisher();
            yield break;
        }
#endif
        if (!playOnStart) yield break;
        yield return new WaitForSecondsRealtime(0.8f);
        PlaySkill();
    }

    void Update()
    {
        UpdatePreviewInputAndTargeting();

        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.unscaledDeltaTime * 2.8f);
        posePulse = Mathf.MoveTowards(posePulse, 0f, Time.unscaledDeltaTime * 3.8f);
        if (playing && useLegacyProceduralPose) ApplyFinisherPose();
    }

    void LateUpdate()
    {
        // Humanoid 骨架必須在 Animator 更新之後才疊加技能姿勢，否則下一幀會被動畫蓋掉。
        if (playing) StabilizeCasterVisualRoot();
        if (playing && !useLegacyProceduralPose) ApplyHumanoidFinisherPose();
        if (skillCamera == null) return;

        shakeStrength = Mathf.MoveTowards(shakeStrength, 0f, Time.unscaledDeltaTime * 3.5f);
        fovKick = Mathf.MoveTowards(fovKick, 0f, Time.unscaledDeltaTime * 36f);

        float t = Time.unscaledTime * 41f;
        Vector3 shake = new Vector3(
            Mathf.PerlinNoise(t, 1.7f) - 0.5f,
            Mathf.PerlinNoise(4.2f, t) - 0.5f,
            Mathf.PerlinNoise(t, 8.4f) - 0.5f) * (shakeStrength * 0.42f);
        Vector3 rotShake = new Vector3(shake.y, -shake.x, shake.z) * 2.1f;

        UpdateFinisherCamera(shake, rotShake);
        UpdatePracticeCamera();
    }

    void OnGUI()
    {
        if (flashAlpha > 0.001f)
        {
            GUI.depth = -1000;
            Color old = GUI.color;
            GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
        if (showSkillHud) DrawTargetingUI();
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (IsTargeting) CancelTargeting();
        if(playing) Time.fixedDeltaTime = fixedDeltaStart;
        RestorePose();
        RestoreCamera();
        CleanupFinisher();
        if(pipelineChanged){QualitySettings.renderPipeline=previousPipeline;pipelineChanged=false;}
        if(playing && caster){caster.position=casterStartPosition;caster.rotation=casterStartRotation;}
        if (practiceControlsSuspended) RestorePracticeControlsAfterSkill();
        ReleaseCinematicCamera();
        playing=false;cinematicCamera=false;
    }

    [ContextMenu("Play Void Rend")]
    public void PlaySkill()
    {
        if (!playing && isActiveAndEnabled && caster != null && target != null)
        {
            PrepareSkillOrigin();
            StartCoroutine(PlaySequence());
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor-only capture helper; excluded from player builds.</summary>
    public void PlayAndPauseAt(float unscaledSeconds)
    {
        PlaySkill();
        StartCoroutine(PauseForCapture(unscaledSeconds));
    }

    IEnumerator PauseForCapture(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        UnityEditor.EditorApplication.isPaused = true;
    }
#endif

    IEnumerator LegacySequence()
    {
        playing = true;
        ResetPreviewState();

        Vector3 center = target.position + Vector3.up * 0.35f;
        Flash(new Color(0.015f, 0.02f, 0.05f), 0.48f);
        SpawnRing(center, 0.55f, 3.2f, 0.055f, 0.75f, violet);
        SpawnParticles(center, 38, 1.2f, 3.8f, 0.05f, 0.16f, cyan);
        yield return Wait(0.26f);

        Vector3 previous = caster.position;
        for (int i = 0; i < teleportCount; i++)
        {
            float angle = (-145f + i * (290f / Mathf.Max(1, teleportCount - 1))) * Mathf.Deg2Rad;
            float radius = teleportRadius * (i % 2 == 0 ? 1f : 0.78f);
            Vector3 next = target.position + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            next.y = casterStartPosition.y;

            CreateAfterimage();
            SpawnTeleportCut(previous + Vector3.up * 1.15f, next + Vector3.up * 1.15f, i);
            caster.position = next;
            FaceTarget();
            poseSide = i % 2 == 0 ? 1f : -1f;
            posePulse = 1f;

            Quaternion arcRot = Quaternion.Euler(
                Random.Range(-18f, 30f),
                -angle * Mathf.Rad2Deg + 90f,
                Random.Range(-50f, 50f));
            SpawnArc(center, arcRot, 2.1f + i * 0.16f, -112f, 112f, 0.12f, 0.55f, i % 3 == 0 ? violet : cyan);
            SpawnSlashPair(center + Random.insideUnitSphere * 0.45f, arcRot * Vector3.right, 7.5f, 0.13f, 0.52f, pale);
            PunchCamera(0.15f, 1.3f);

            previous = next;
            yield return Wait(i < 2 ? 0.22f : 0.145f);
        }

        // Delayed judgement-cut lattice: thin lines first, bright cores a few frames later.
        for (int i = 0; i < 26; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-2.6f, 2.6f), Random.Range(-1.45f, 1.75f), Random.Range(-1.15f, 1.15f));
            Quaternion q = Quaternion.Euler(Random.Range(-70f, 70f), Random.Range(0f, 180f), Random.Range(-50f, 50f));
            Vector3 dir = q * Vector3.right;
            float width = i % 5 == 0 ? 0.105f : 0.045f;
            SpawnSlashPair(center + offset, dir, Random.Range(4.8f, 9.4f), width, Random.Range(0.58f, 1.0f), i % 4 == 0 ? violet : cyan);
            if (i % 4 == 0) PunchCamera(0.045f, 0.15f);
            yield return Wait(0.028f);
        }

        yield return Wait(0.15f);
        Flash(Color.white, 0.86f);
        PunchCamera(0.5f, 7f);
        Time.timeScale = 0.035f;
        Time.fixedDeltaTime = fixedDeltaStart * Time.timeScale;
        yield return Wait(0.12f);
        Time.timeScale = 1f;
        Time.fixedDeltaTime = fixedDeltaStart;

        SpawnSpaceRift(center);
        PunchCamera(0.28f, 4f);
        yield return Wait(0.11f);
        SpawnBurst(center);
        yield return Wait(0.18f);
        Flash(new Color(0.5f, 0.92f, 1f), 0.58f);
        PunchCamera(0.72f, 11f);

        yield return Wait(1.15f);
        CreateAfterimage();
        caster.position = casterStartPosition;
        caster.rotation = casterStartRotation;
        RestorePose();
        playing = false;

        if (loopPreview)
        {
            yield return Wait(loopDelay);
            PlaySkill();
        }
    }

    void SpawnTeleportCut(Vector3 from, Vector3 to, int index)
    {
        Vector3 midpoint = (from + to) * 0.5f;
        Vector3 direction = (to - from).normalized;
        float length = Vector3.Distance(from, to) + 1.4f;
        SpawnSlashPair(midpoint, direction, length, 0.14f, 0.42f, index % 2 == 0 ? cyan : violet);
        SpawnRing(to - Vector3.up * 0.95f, 0.2f, 1.05f, 0.04f, 0.4f, cyan);
        SpawnParticles(to, 16, 0.55f, 4.8f, 0.035f, 0.095f, pale);
    }

    void SpawnSpaceRift(Vector3 center)
    {
        for (int layer = 0; layer < 3; layer++)
        {
            // Keep the main tears close to the camera plane so their dark cores
            // read as actual holes in space instead of thin lines in perspective.
            float angle = layer == 0 ? 18f : layer == 1 ? -48f : 82f;
            Quaternion q = Quaternion.AngleAxis(angle, Vector3.forward);
            Vector3 axis = q * Vector3.right;
            Vector3 side = q * Vector3.up;
            var points = new Vector3[11];
            for (int i = 0; i < points.Length; i++)
            {
                float u = i / (points.Length - 1f) - 0.5f;
                float jag = (i == 0 || i == points.Length - 1) ? 0f : Random.Range(-0.32f, 0.32f);
                points[i] = center + (axis * (u * (7.8f + layer)) + side * jag) * effectRangeMultiplier + Vector3.back * (0.32f + layer * 0.035f);
            }

            SpawnRibbon(points, riftCoreMaterial, 0.78f - layer * 0.09f, 1.75f, Color.black);
            SpawnRibbon(points, slashMaterial, 0.19f - layer * 0.025f, 1.3f, layer == 1 ? violet : cyan);
            SpawnRibbon(points, slashMaterial, 0.045f, 1.1f, Color.white);
        }

        for (int i = 0; i < 14; i++)
        {
            Quaternion q = Random.rotation;
            SpawnArc(center + Random.insideUnitSphere * 0.35f, q, Random.Range(1.3f, 3.1f), -80f, 80f, 0.05f, Random.Range(0.7f, 1.25f), i % 3 == 0 ? violet : cyan);
        }
    }

    void SpawnBurst(Vector3 center)
    {
        SpawnRing(center, 0.4f, 4.7f, 0.16f, 0.85f, Color.white);
        SpawnRing(center, 0.2f, 3.7f, 0.3f, 0.58f, cyan);
        SpawnParticles(center, 96, 1.2f, 11f, 0.035f, 0.22f, pale);

        for (int i = 0; i < 30; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y *= 0.65f;
            Vector3 start = center + dir.normalized * Random.Range(0.2f, 0.8f);
            SpawnSlashPair(start + dir * 2.3f, dir, Random.Range(3.5f, 8f), Random.Range(0.025f, 0.085f), Random.Range(0.35f, 0.72f), i % 5 == 0 ? violet : cyan);
        }
    }

    void SpawnSlashPair(Vector3 center, Vector3 direction, float length, float width, float lifetime, Color color)
    {
        Vector3 half = direction.normalized * length * 0.5f;
        Vector3[] points = { center - half, center + half };
        SpawnRibbon(points, slashMaterial, width * 5.2f, lifetime, new Color(color.r, color.g, color.b, 0.42f));
        SpawnRibbon(points, slashMaterial, width * 1.45f, lifetime * 0.9f, Color.Lerp(color, Color.white, 0.82f));
    }

    void SpawnArc(Vector3 center, Quaternion rotation, float radius, float startAngle, float endAngle, float width, float lifetime, Color color)
    {
        const int count = 28;
        Vector3[] points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (count - 1f);
            float a = Mathf.Lerp(startAngle, endAngle, t) * Mathf.Deg2Rad;
            float taper = Mathf.Sin(t * Mathf.PI);
            Vector3 local = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.72f, Mathf.Sin(a * 2f) * 0.08f * taper);
            points[i] = center + rotation * local;
        }

        SpawnRibbon(points, slashMaterial, width * 4.6f, lifetime, new Color(color.r, color.g, color.b, 0.38f));
        SpawnRibbon(points, slashMaterial, width * 1.35f, lifetime * 0.92f, Color.Lerp(color, Color.white, 0.72f));
    }

    void SpawnRing(Vector3 center, float startRadius, float endRadius, float width, float lifetime, Color color)
    {
        const int count = 49;
        Vector3[] points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float a = i / (count - 1f) * Mathf.PI * 2f;
            points[i] = center + new Vector3(Mathf.Cos(a) * startRadius, 0.035f, Mathf.Sin(a) * startRadius);
        }

        LineRenderer line = SpawnRibbon(points, slashMaterial, width, lifetime, color);
        var fx = line.GetComponent<VoidRendTransientLine>();
        if (fx != null) fx.SetRadialExpansion(center, startRadius, endRadius);
    }

    LineRenderer SpawnRibbon(IReadOnlyList<Vector3> points, Material material, float width, float lifetime, Color color)
    {
        GameObject go = new GameObject("VoidRend_Ribbon");
        go.transform.SetParent(effectsRoot, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 4;
        line.numCapVertices = 3;
        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        line.sharedMaterial = material;
        line.startWidth = width;
        line.endWidth = width * 0.68f;
        line.startColor = color;
        line.endColor = color;

        VoidRendTransientLine fx = go.AddComponent<VoidRendTransientLine>();
        fx.Initialize(line, lifetime, width, color);
        return line;
    }

    void SpawnParticles(Vector3 position, int count, float lifetime, float speed, float minSize, float maxSize, Color color)
    {
        GameObject go = new GameObject("VoidRend_Shards");
        go.SetActive(false);
        go.transform.SetParent(effectsRoot, false);
        go.transform.position = position;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 0.05f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.55f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(128, count * 2);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.32f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.white, 0.18f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 3.2f;
        renderer.velocityScale = 0.18f;
        renderer.sharedMaterial = particleMaterial != null ? particleMaterial : slashMaterial;

        go.SetActive(true);
        ps.Play();
        Destroy(go, lifetime + 0.6f);
    }

    void CreateAfterimage()
    {
        if (afterimageMaterial == null || caster == null) return;
        foreach (SkinnedMeshRenderer source in caster.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!source.enabled) continue;
            Mesh baked = new Mesh { name = "VoidRend_AfterimageMesh" };
            source.BakeMesh(baked, true);

            GameObject ghost = new GameObject("VoidRend_Afterimage");
            ghost.transform.SetParent(effectsRoot, false);
            ghost.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            ghost.transform.localScale = source.transform.lossyScale;
            ghost.AddComponent<MeshFilter>().sharedMesh = baked;
            MeshRenderer mr = ghost.AddComponent<MeshRenderer>();
            Material[] mats = new Material[Mathf.Max(1, source.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = afterimageMaterial;
            mr.sharedMaterials = mats;
            VoidRendAfterimage fade = ghost.AddComponent<VoidRendAfterimage>();
            fade.Initialize(mr, baked, 0.48f, cyan);
        }
    }

    void PunchCamera(float shake, float fov)
    {
        shakeStrength = Mathf.Max(shakeStrength, shake);
        fovKick = Mathf.Max(fovKick, fov);
    }

    void Flash(Color color, float alpha)
    {
        flashColor = color;
        flashAlpha = Mathf.Max(flashAlpha, alpha);
    }

    void FaceTarget()
    {
        Vector3 flat = target.position - caster.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f) caster.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    void ResetPreviewState()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = fixedDeltaStart;
        caster.position = casterStartPosition;
        caster.rotation = casterStartRotation;
        RestoreCamera();
        RestorePose();
    }

    void RestoreCamera()
    {
        if (skillCamera == null) return;
        hasQueuedCameraPose = false;
        skillCamera.transform.position = cameraStartPosition;
        skillCamera.transform.rotation = cameraStartRotation;
        skillCamera.fieldOfView = cameraStartFov;
    }

    void AcquireCinematicCamera()
    {
        if (skillCamera == null) return;
        if (cinematicCameraDriver == null)
            cinematicCameraDriver = skillCamera.GetComponent<VoidRendCinematicCameraDriver>() ??
                                    skillCamera.gameObject.AddComponent<VoidRendCinematicCameraDriver>();
        cinematicCameraDriver.Claim(this);
    }

    void ReleaseCinematicCamera()
    {
        hasQueuedCameraPose = false;
        if (cinematicCameraDriver != null) cinematicCameraDriver.Release(this);
    }

    internal void QueueCinematicCameraPose(Vector3 position, Quaternion rotation, float fieldOfView)
    {
        queuedCameraPosition = position;
        queuedCameraRotation = rotation;
        queuedCameraFov = fieldOfView;
        hasQueuedCameraPose = true;
        ApplyQueuedCinematicCameraPose();
    }

    internal void ApplyQueuedCinematicCameraPose()
    {
        if (!hasQueuedCameraPose || skillCamera == null || (!cinematicCamera && !voidRendCinematicActive)) return;
        skillCamera.transform.SetPositionAndRotation(queuedCameraPosition, queuedCameraRotation);
        skillCamera.fieldOfView = queuedCameraFov;
    }

    void CreateEffectsRoot()
    {
        GameObject root = new GameObject("VoidRend_RuntimeFX");
        root.transform.SetParent(transform, false);
        effectsRoot = root.transform;
    }

    WaitForSecondsRealtime Wait(float seconds) => new WaitForSecondsRealtime(seconds);

    void CachePoseBones()
    {
        rightUpperArm = FindDeepChild(caster, "Bip001 R UpperArm");
        rightForearm = FindDeepChild(caster, "Bip001 R Forearm");
        rightHand = FindDeepChild(caster, "Bip001 R Hand");
        spine = FindDeepChild(caster, "Bip001 Spine2");

        // 支援一般 Humanoid 模型（例如 Tripo/Mixamo），不再綁死 Bocchi 的 Bip001 骨骼命名。
        Animator humanoid = caster.GetComponentInChildren<Animator>();
        if (humanoid != null && humanoid.isHuman)
        {
            if (rightUpperArm == null) rightUpperArm = humanoid.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (rightForearm == null) rightForearm = humanoid.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (rightHand == null) rightHand = humanoid.GetBoneTransform(HumanBodyBones.RightHand);
            if (spine == null) spine = humanoid.GetBoneTransform(HumanBodyBones.UpperChest) ?? humanoid.GetBoneTransform(HumanBodyBones.Chest);
        }
        if (rightUpperArm != null) rightUpperBase = rightUpperArm.localRotation;
        if (rightForearm != null) rightForearmBase = rightForearm.localRotation;
        if (rightHand != null) rightHandBase = rightHand.localRotation;
        if (spine != null) spineBase = spine.localRotation;
    }

    void ApplyPreviewPose()
    {
        if (!playing) return;
        float eased = posePulse * posePulse;
        if (rightUpperArm != null) rightUpperArm.localRotation = rightUpperBase * Quaternion.Euler(0f, 0f, -48f * eased * poseSide);
        if (rightForearm != null) rightForearm.localRotation = rightForearmBase * Quaternion.Euler(0f, 0f, -62f * eased);
        if (rightHand != null) rightHand.localRotation = rightHandBase * Quaternion.Euler(22f * eased, 0f, 36f * eased * poseSide);
        if (spine != null) spine.localRotation = spineBase * Quaternion.Euler(0f, 20f * eased * poseSide, 7f * eased * poseSide);
    }

    void RestorePose()
    {
        if (rightUpperArm != null) rightUpperArm.localRotation = rightUpperBase;
        if (rightForearm != null) rightForearm.localRotation = rightForearmBase;
        if (rightHand != null) rightHand.localRotation = rightHandBase;
        if (spine != null) spine.localRotation = spineBase;
    }

    static Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform result = FindDeepChild(child, childName);
            if (result != null) return result;
        }
        return null;
    }
}

public sealed class VoidRendTransientLine : MonoBehaviour
{
    LineRenderer line;
    float lifetime;
    float age;
    float baseWidth;
    Color baseColor;
    bool expand;
    Vector3 center;
    float startRadius;
    float endRadius;
    Vector3[] directions;

    public void Initialize(LineRenderer target, float seconds, float width, Color color)
    {
        line = target;
        lifetime = Mathf.Max(0.05f, seconds);
        baseWidth = width;
        baseColor = color;
    }

    public void SetRadialExpansion(Vector3 expansionCenter, float from, float to)
    {
        expand = true;
        center = expansionCenter;
        startRadius = Mathf.Max(0.001f, from);
        endRadius = to;
        directions = new Vector3[line.positionCount];
        for (int i = 0; i < directions.Length; i++)
            directions[i] = (line.GetPosition(i) - center).normalized;
    }

    void Update()
    {
        if (line == null) return;
        age += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(age / lifetime);
        float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 8f));
        float fade = Mathf.Pow(1f - t, 1.35f);
        Color c = baseColor;
        c.a *= appear * fade;
        line.startColor = c;
        line.endColor = c;
        line.startWidth = baseWidth * Mathf.Lerp(1.18f, 0.28f, t);
        line.endWidth = line.startWidth * 0.7f;

        if (expand && directions != null)
        {
            float radius = Mathf.Lerp(startRadius, endRadius, 1f - Mathf.Pow(1f - t, 3f));
            for (int i = 0; i < directions.Length; i++)
                line.SetPosition(i, center + directions[i] * radius + Vector3.up * 0.02f);
        }

        if (age >= lifetime) Destroy(gameObject);
    }
}

public sealed class VoidRendAfterimage : MonoBehaviour
{
    MeshRenderer target;
    Mesh ownedMesh;
    float lifetime;
    float age;
    Color color;
    MaterialPropertyBlock block;

    public void Initialize(MeshRenderer renderer, Mesh mesh, float seconds, Color tint)
    {
        target = renderer;
        ownedMesh = mesh;
        lifetime = seconds;
        color = tint;
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(age / lifetime);
        Color c = color;
        c.a = Mathf.Pow(1f - t, 1.6f) * 0.55f;
        target.GetPropertyBlock(block);
        block.SetColor("_BaseColor", c);
        block.SetFloat("_Intensity", Mathf.Lerp(2.8f, 0.2f, t));
        target.SetPropertyBlock(block);
        transform.localScale *= 1f + Time.unscaledDeltaTime * 0.18f;

        if (age >= lifetime)
        {
            if (ownedMesh != null) Destroy(ownedMesh);
            Destroy(gameObject);
        }
    }
}
