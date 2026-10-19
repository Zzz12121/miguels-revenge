using UnityEngine;

/// <summary>
/// 純視覺武器掛點切換器。平時隱藏，攻擊或技能期間以粒子聚合到右手，
/// 最後一次使用後保留一段時間，再化成霧狀粒子散去。
/// 不處理傷害、命中或玩家輸入，可安全重用在正式 PlayerRig 與獨立 VFX 測試場景。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerWeaponPresenter : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] Transform weaponPivot;
    [SerializeField] Transform handSocket;
    [SerializeField] Transform waistSocket;
    [SerializeField] Vector3 handLocalPosition = Vector3.zero;
    [SerializeField] Vector3 handLocalEuler = new Vector3(60f, 25f, 0f);

    [Header("武器聚散視覺")]
    [Tooltip("攻擊或技能結束後，武器仍留在手上的秒數。再次使用會重新計時。")]
    [SerializeField, Min(0f)] float lingerAfterUse = 3f;
    [Tooltip("首次抽刀時，先讓粒子聚合多久才顯示完整刀身。")]
    [SerializeField, Range(0f, 0.3f)] float materializeRevealDelay = 0.035f;
    [Tooltip("從四周聚合到刀身的粒子數。")]
    [SerializeField, Range(12, 240)] int materializeParticleCount = 144;
    [Tooltip("武器散成霧時的粒子數。")]
    [SerializeField, Range(12, 240)] int disperseParticleCount = 112;
    [SerializeField] Color energyBlue = new Color(0.12f, 0.62f, 1f, 1f);
    [SerializeField] Color energyWhite = new Color(0.88f, 0.97f, 1f, 1f);

    [Header("腰後收刀姿勢（以角色朝向為準，不使用骨盆的歪斜座標軸）")]
    [Tooltip("X=角色右方，Y=上方，Z=角色前方；負 Z 代表背後。單位是公尺。")]
    [SerializeField] Vector3 holsterOffset = new Vector3(0.30f, -0.22f, -0.32f);
    [Tooltip("刀尖相對角色指向哪裡。預設向角色左側，讓刀橫掛在腰後。")]
    [SerializeField] Vector3 holsterBladeDirection = Vector3.left;
    [Tooltip("沿刀身軸旋轉；若刀刃正反面不對，只需要調這個值。")]
    [SerializeField, Range(-180f, 180f)] float holsterRollDegrees;

    Transform hips;
    Transform weaponTip;
    Renderer[] weaponRenderers;
    ParticleSystem weaponParticles;
    Material runtimeParticleMaterial;
    Texture2D runtimeLightningTexture;

    float drawUntilUnscaledTime;
    float revealAtUnscaledTime;
    bool skillDrawActive;
    bool drawn;
    bool pendingReveal;

    public bool IsDrawn => drawn;

    void Awake()
    {
        EnsureReferences();
        SetDrawnImmediate(false);
    }

    void Update()
    {
        if (pendingReveal && drawn && Time.unscaledTime >= revealAtUnscaledTime)
        {
            pendingReveal = false;
            SetWeaponRenderers(true);
        }
        if (!skillDrawActive && drawn && Time.unscaledTime >= drawUntilUnscaledTime)
            DisperseAndHide();
    }

    void LateUpdate()
    {
        // Humanoid 的 Hips 每幀都會被動畫改變。收刀時在動畫之後重新套用世界姿勢，
        // 才不會因模型骨盆本身的軸向旋轉而跑到胸前或穿過身體。
        if (!drawn && weaponRenderers != null && weaponRenderers.Length > 0 && weaponRenderers[0].enabled)
        {
            // Animator 在 Awake 時不一定已完成 Humanoid 初始化，因此在第一次 LateUpdate
            // 再補抓一次骨盆與刀尖；成功後後續只會做輕量的姿勢更新。
            if (hips == null || weaponTip == null) EnsureReferences();
            else ApplyWaistSocketPose();
        }
    }

    public void Configure(Animator newAnimator, Transform newWeaponPivot, Transform newHandSocket, Transform newWaistSocket)
    {
        animator = newAnimator;
        weaponPivot = newWeaponPivot;
        handSocket = newHandSocket;
        waistSocket = newWaistSocket;
        ApplyWaistSocketPose();
    }

    public void DrawForSeconds(float seconds)
    {
        drawUntilUnscaledTime = Mathf.Max(drawUntilUnscaledTime,
            Time.unscaledTime + Mathf.Max(0.1f, seconds) + lingerAfterUse);
        MaterializeInHand();
    }

    public void BeginSkillDraw()
    {
        // 技能不可同時施放，使用狀態而不是累加計數，避免技能中斷後計數殘留導致刀永遠不消失。
        skillDrawActive = true;
        MaterializeInHand();
    }

    public void EndSkillDraw(bool sheatheImmediately = true)
    {
        skillDrawActive = false;
        drawUntilUnscaledTime = Time.unscaledTime + lingerAfterUse;
    }

    public void SheatheNow()
    {
        skillDrawActive = false;
        drawUntilUnscaledTime = 0f;
        SetDrawnImmediate(false);
    }

    public void SetDrawnImmediate(bool shouldDraw)
    {
        EnsureReferences();
        Transform destination = shouldDraw ? handSocket : waistSocket;
        if (weaponPivot == null || destination == null) return;

        weaponPivot.SetParent(destination, false);
        weaponPivot.localPosition = shouldDraw ? handLocalPosition : Vector3.zero;
        weaponPivot.localRotation = shouldDraw ? Quaternion.Euler(handLocalEuler) : Quaternion.identity;
        weaponPivot.localScale = Vector3.one;
        pendingReveal = false;
        if (!shouldDraw && weaponParticles != null)
            weaponParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        SetWeaponRenderers(shouldDraw);
        drawn = shouldDraw;
    }

    void MaterializeInHand()
    {
        EnsureReferences();
        if (weaponPivot == null || handSocket == null) return;

        bool wasDrawn = drawn;
        weaponPivot.SetParent(handSocket, false);
        weaponPivot.localPosition = handLocalPosition;
        weaponPivot.localRotation = Quaternion.Euler(handLocalEuler);
        weaponPivot.localScale = Vector3.one;
        drawn = true;

        if (wasDrawn)
        {
            SetWeaponRenderers(true);
            return;
        }

        // 只有真正從「無刀」切換成「有刀」時播放聚合；連段期間直接揮刀，不重播出場特效。
        SetWeaponRenderers(false);
        pendingReveal = true;
        revealAtUnscaledTime = Time.unscaledTime + materializeRevealDelay;
        EmitMaterialize(materializeParticleCount);
    }

    void DisperseAndHide()
    {
        if (!drawn) return;
        pendingReveal = false;
        EmitDisperse(disperseParticleCount);
        SetWeaponRenderers(false);
        drawn = false;
        drawUntilUnscaledTime = 0f;
    }

    void SetWeaponRenderers(bool visible)
    {
        if (weaponRenderers == null) return;
        foreach (Renderer item in weaponRenderers)
            if (item != null) item.enabled = visible;
    }

    void EnsureReferences()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            if (handSocket == null) handSocket = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hips == null) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (waistSocket == null)
            {
                if (hips != null)
                {
                    Transform existing = hips.Find("WaistWeaponSocket");
                    if (existing == null)
                    {
                        existing = new GameObject("WaistWeaponSocket").transform;
                        existing.SetParent(hips, false);
                    }
                    waistSocket = existing;
                }
            }
        }

        if (weaponPivot == null) weaponPivot = FindDeepChild(transform, "WeaponPivot");
        if (weaponTip == null && weaponPivot != null) weaponTip = FindDeepChild(weaponPivot, "WeaponTip");
        if (weaponRenderers == null && weaponPivot != null)
        {
            Renderer[] all = weaponPivot.GetComponentsInChildren<Renderer>(true);
            var visibleMeshes = new System.Collections.Generic.List<Renderer>(all.Length);
            foreach (Renderer item in all)
                if (item != null && !(item is ParticleSystemRenderer) && !(item is LineRenderer)) visibleMeshes.Add(item);
            weaponRenderers = visibleMeshes.ToArray();
        }
        EnsureParticleSystem();
        ApplyWaistSocketPose();
    }

    void EnsureParticleSystem()
    {
        if (weaponParticles != null || weaponPivot == null) return;

        Transform existing = weaponPivot.Find("WeaponAssemblyParticles");
        GameObject fxObject = existing != null ? existing.gameObject : new GameObject("WeaponAssemblyParticles");
        fxObject.transform.SetParent(weaponPivot, false);
        fxObject.transform.localPosition = Vector3.zero;
        fxObject.transform.localRotation = Quaternion.identity;

        weaponParticles = fxObject.GetComponent<ParticleSystem>();
        if (weaponParticles == null) weaponParticles = fxObject.AddComponent<ParticleSystem>();

        var main = weaponParticles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 480;
        main.startSpeed = 0f;
        main.startLifetime = 0.12f;
        main.startSize = 0.035f;
        weaponParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var emission = weaponParticles.emission;
        emission.enabled = false;
        var shape = weaponParticles.shape;
        shape.enabled = false;
        var noise = weaponParticles.noise;
        noise.enabled = true;
        noise.strength = 0.045f;
        noise.frequency = 1.8f;
        noise.scrollSpeed = 1.2f;

        // 粒子快速移動時留下細長拖尾，形成許多短促閃電束；不使用方形 Billboard。
        var trails = weaponParticles.trails;
        trails.enabled = true;
        trails.ratio = 1f;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.055f, 0.12f);
        trails.minVertexDistance = 0.012f;
        trails.worldSpace = false;
        trails.dieWithParticles = true;
        trails.sizeAffectsWidth = true;
        trails.inheritParticleColor = true;

        var colorOverLifetime = weaponParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(energyBlue, 0.48f), new GradientColorKey(energyWhite, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer = fxObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = 0.08f;
        particleRenderer.lengthScale = 0.34f;
        particleRenderer.cameraVelocityScale = 0f;
        particleRenderer.sortingOrder = 20;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            runtimeParticleMaterial = new Material(shader) { name = "Runtime Weapon Assembly Particles" };
            runtimeLightningTexture = CreateLightningTexture();
            if (runtimeParticleMaterial.HasProperty("_BaseMap")) runtimeParticleMaterial.SetTexture("_BaseMap", runtimeLightningTexture);
            if (runtimeParticleMaterial.HasProperty("_MainTex")) runtimeParticleMaterial.SetTexture("_MainTex", runtimeLightningTexture);
            if (runtimeParticleMaterial.HasProperty("_BaseColor")) runtimeParticleMaterial.SetColor("_BaseColor", Color.white);
            if (runtimeParticleMaterial.HasProperty("_Color")) runtimeParticleMaterial.SetColor("_Color", Color.white);
            if (runtimeParticleMaterial.HasProperty("_Surface")) runtimeParticleMaterial.SetFloat("_Surface", 1f);
            if (runtimeParticleMaterial.HasProperty("_ZWrite")) runtimeParticleMaterial.SetFloat("_ZWrite", 0f);
            runtimeParticleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            runtimeParticleMaterial.renderQueue = 3000;
            particleRenderer.material = runtimeParticleMaterial;
            particleRenderer.trailMaterial = runtimeParticleMaterial;
        }
    }

    static Texture2D CreateLightningTexture()
    {
        const int width = 64;
        const int height = 16;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Runtime Weapon Lightning Streak",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        Color[] pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            float u = x / (width - 1f);
            float center = (height - 1f) * 0.5f
                + Mathf.Sin(u * 25f) * 1.15f
                + Mathf.Sin(u * 53f + 0.7f) * 0.55f;
            float endFade = Mathf.SmoothStep(0f, 1f, Mathf.Min(u * 7f, (1f - u) * 7f));
            for (int y = 0; y < height; y++)
            {
                float distance = Mathf.Abs(y - center);
                float core = Mathf.Clamp01(1f - distance / 1.25f);
                float glow = Mathf.Clamp01(1f - distance / 4.2f) * 0.45f;
                float alpha = Mathf.Max(core, glow) * endFade;
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    void EmitMaterialize(int count)
    {
        if (weaponParticles == null) return;
        Vector3 tip = LocalWeaponTip();
        for (int i = 0; i < count; i++)
        {
            float along = Random.value;
            Vector3 targetPoint = Vector3.Lerp(Vector3.zero, tip, along);
            Vector3 startPoint = targetPoint + Random.insideUnitSphere * Random.Range(0.10f, 0.30f);
            float lifetime = Random.Range(0.065f, 0.14f);
            var emit = new ParticleSystem.EmitParams
            {
                position = startPoint,
                velocity = (targetPoint - startPoint) / lifetime,
                startLifetime = lifetime,
                startSize = Random.Range(0.018f, 0.045f),
                startColor = Random.value > 0.38f ? energyBlue : energyWhite
            };
            weaponParticles.Emit(emit, 1);
        }
    }

    void EmitDisperse(int count)
    {
        if (weaponParticles == null) return;
        Vector3 tip = LocalWeaponTip();
        for (int i = 0; i < count; i++)
        {
            Vector3 bladePoint = Vector3.Lerp(Vector3.zero, tip, Random.value);
            Vector3 outward = Random.insideUnitSphere;
            outward.y = Mathf.Abs(outward.y) * 0.75f + 0.12f;
            var emit = new ParticleSystem.EmitParams
            {
                position = bladePoint + Random.insideUnitSphere * 0.025f,
                velocity = outward * Random.Range(0.18f, 0.55f),
                startLifetime = Random.Range(0.30f, 0.65f),
                startSize = Random.Range(0.025f, 0.075f),
                startColor = Random.value > 0.45f ? energyBlue : energyWhite
            };
            weaponParticles.Emit(emit, 1);
        }
    }

    Vector3 LocalWeaponTip()
    {
        if (weaponPivot == null || weaponTip == null) return Vector3.forward * 0.9f;
        Vector3 tip = weaponPivot.InverseTransformPoint(weaponTip.position);
        return tip.sqrMagnitude > 0.001f ? tip : Vector3.forward * 0.9f;
    }

    void OnDestroy()
    {
        if (runtimeParticleMaterial != null) Destroy(runtimeParticleMaterial);
        if (runtimeLightningTexture != null) Destroy(runtimeLightningTexture);
    }

    void ApplyWaistSocketPose()
    {
        if (waistSocket == null || hips == null || animator == null) return;

        Transform facing = animator.transform;
        waistSocket.position = hips.position
            + facing.right * holsterOffset.x
            + facing.up * holsterOffset.y
            + facing.forward * holsterOffset.z;

        Vector3 sourceDirection = Vector3.forward;
        if (weaponPivot != null && weaponTip != null)
        {
            Vector3 localTip = weaponPivot.InverseTransformPoint(weaponTip.position);
            if (localTip.sqrMagnitude > 0.0001f) sourceDirection = localTip.normalized;
        }

        Vector3 desiredDirection = holsterBladeDirection.sqrMagnitude > 0.0001f
            ? holsterBladeDirection.normalized
            : Vector3.left;
        Quaternion alignBlade = Quaternion.FromToRotation(sourceDirection, desiredDirection);
        Quaternion roll = Quaternion.AngleAxis(holsterRollDegrees, desiredDirection);
        waistSocket.rotation = facing.rotation * roll * alignBlade;
        waistSocket.localScale = Vector3.one;
    }

    static Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindDeepChild(child, childName);
            if (found != null) return found;
        }
        return null;
    }
}
