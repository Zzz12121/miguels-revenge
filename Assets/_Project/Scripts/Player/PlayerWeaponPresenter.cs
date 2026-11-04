using UnityEngine;

/// <summary>
/// 純視覺武器掛點切換器。平時隱藏，攻擊或技能期間以粒子聚合到右手，
/// 最後一次使用後保留一段時間，再化成霧狀粒子散去。
/// 不處理傷害、命中或玩家輸入，可安全重用在正式 PlayerRig 與獨立 VFX 測試場景。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(31000)]
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
    Material lightningLineMaterial;
    [Header("武器電弧音效（留空使用合成電流聲）")]
    [SerializeField] AudioClip materializeSound;
    [SerializeField] AudioClip disperseSound;
    [SerializeField, Range(0f,1f)] float arcSoundVolume = .32f;
    AudioClip generatedMaterializeSound, generatedDisperseSound;
    readonly System.Collections.Generic.Dictionary<Renderer, Material[]> originalMaterials = new();
    readonly System.Collections.Generic.Dictionary<Renderer, Material[]> dissolveMaterials = new();
    float dissolveAmount = 1f;
    float nextLightningAt;
    bool dispersing;

    float drawUntilUnscaledTime;
    float revealAtUnscaledTime;
    bool skillDrawActive;
    bool drawn;
    bool pendingReveal;
    Vector3 slashBladeDirection, slashBladeUp;
    int slashPoseFrame = -1;
    /// <summary>揮刀時劍指的方向；edgeUp = 劍身的「上方」朝哪裡（不給 = 角色的上方）。
    /// 劍幾乎直立時（例如挑斬）一定要給，不然角色上方和劍同方向，刀鋒會亂轉</summary>
    public void AimSlashBlade(Vector3 direction, Vector3 edgeUp = default)
    {
        slashBladeDirection = direction.normalized;
        slashBladeUp = edgeUp;
        slashPoseFrame = Time.frameCount;
    }

    public bool IsDrawn => drawn;
    public Transform BladeTip => weaponTip;
    public Transform BladePivot => weaponPivot;

    void Awake()
    {
        EnsureReferences();
        SetDrawnImmediate(false);
    }

    void Update()
    {
        if (pendingReveal || dispersing)
        {
            dissolveAmount = Mathf.MoveTowards(dissolveAmount, dispersing ? 1f : 0f,
                Time.unscaledDeltaTime / (dispersing ? .65f : .18f));
            ApplyDissolve();
            if (Time.unscaledTime >= nextLightningAt)
            {
                EmitBladeLightning(dispersing);
                nextLightningAt = Time.unscaledTime + .11f;
            }
            if (dispersing && dissolveAmount >= 1f) { dispersing = false; SetWeaponRenderers(false); RestoreMaterials(); }
            else if (pendingReveal && dissolveAmount <= 0f) { pendingReveal = false; RestoreMaterials(); }
        }
        if (!skillDrawActive && drawn && Time.unscaledTime >= drawUntilUnscaledTime)
            DisperseAndHide();
    }

    void LateUpdate()
    {
        if (weaponRenderers == null || weaponRenderers.Length == 0) EnsureReferences();
        // Visibility belongs to this presenter, not an animation's renderer-enabled curve.
        SetWeaponRenderers(drawn || dispersing);
        // MeleeCombat also rotates its legacy weapon pivot. Keep the visual grip fixed;
        // the arm pose supplies the slash, not a second independent pivot spin.
        if (drawn && !skillDrawActive && weaponPivot != null && weaponPivot.parent == handSocket)
        {
            weaponPivot.localPosition = handLocalPosition;
            weaponPivot.localRotation = Quaternion.Euler(handLocalEuler);
            if (slashPoseFrame == Time.frameCount && animator != null)
            {
                Vector3 localAxis = LocalWeaponTip().normalized;
                Vector3 up = slashBladeUp.sqrMagnitude > .0001f ? slashBladeUp : animator.transform.up;
                weaponPivot.rotation = Quaternion.LookRotation(slashBladeDirection, up)
                    * Quaternion.FromToRotation(localAxis, Vector3.forward);
            }
        }
    }

    public void Configure(Animator newAnimator, Transform newWeaponPivot, Transform newHandSocket, Transform newWaistSocket)
    {
        animator = newAnimator;
        weaponPivot = newWeaponPivot;
        handSocket = newHandSocket;
        waistSocket = newWaistSocket;
        weaponRenderers = null;
        weaponTip = null;
        EnsureReferences();
        SetWeaponRenderers(drawn && !pendingReveal);
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
        dispersing = false;
        RestoreMaterials();
        dissolveAmount = shouldDraw ? 0f : 1f;
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
        bool wasDispersing = dispersing;
        dispersing = false;
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
        if (!wasDispersing) dissolveAmount = 1f;
        SetWeaponRenderers(true);
        pendingReveal = true;
        revealAtUnscaledTime = Time.unscaledTime + materializeRevealDelay;
        EmitMaterialize(materializeParticleCount);
        ApplyDissolve();
    }

    void DisperseAndHide()
    {
        if (!drawn) return;
        pendingReveal = false;
        EmitDisperse(disperseParticleCount);
        PlayArcSound(true);
        dispersing = true;
        dissolveAmount = 0f;
        ApplyDissolve();
        drawn = false;
        drawUntilUnscaledTime = 0f;
    }

    void SetWeaponRenderers(bool visible)
    {
        if (weaponRenderers == null) return;
        foreach (Renderer item in weaponRenderers)
            if (item != null)
            {
                item.enabled = visible;
                item.forceRenderingOff = !visible;
            }
    }

    void ApplyDissolve()
    {
        Shader shader = Resources.Load<Shader>("WeaponLightningDissolve");
        if (shader == null || weaponRenderers == null) return;
        foreach (Renderer r in weaponRenderers)
        {
            if (r == null) continue;
            if (!dissolveMaterials.TryGetValue(r, out Material[] mats))
            {
                Material[] originals = r.sharedMaterials;
                originalMaterials[r] = originals;
                mats = new Material[originals.Length];
                for (int i=0;i<mats.Length;i++)
                {
                    mats[i] = new Material(shader);
                    var source = originals[i];
                    if (source == null) continue;
                    string map = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    if (source.HasProperty(map))
                    {
                        mats[i].SetTexture("_BaseMap",source.GetTexture(map));
                        mats[i].SetTextureScale("_BaseMap",source.GetTextureScale(map));
                        mats[i].SetTextureOffset("_BaseMap",source.GetTextureOffset(map));
                    }
                    if (source.HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor",source.GetColor("_BaseColor"));
                }
                dissolveMaterials[r] = mats;
            }
            r.sharedMaterials = mats;
            foreach(var m in mats) m.SetFloat("_Amount",dissolveAmount);
        }
    }
    void RestoreMaterials()
    {
        foreach(var pair in originalMaterials) if(pair.Key != null) pair.Key.sharedMaterials = pair.Value;
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
        if ((weaponRenderers == null || weaponRenderers.Length == 0) && weaponPivot != null)
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
        EmitBladeLightning(false);
    }

    void PlayArcSound(bool disperse)
    {
        AudioClip clip = disperse ? disperseSound : materializeSound;
        if (clip == null)
        {
            if (disperse)
            {
                if (generatedDisperseSound == null) generatedDisperseSound = CreateArcSound(true);
                clip = generatedDisperseSound;
            }
            else
            {
                if (generatedMaterializeSound == null) generatedMaterializeSound = CreateArcSound(false);
                clip = generatedMaterializeSound;
            }
        }
        GameAudio.PlayAt(clip, weaponPivot != null ? weaponPivot.position : transform.position,
            Mathf.Clamp01(arcSoundVolume * 3f) * .8f, 1f, .35f, 28f);
    }

    static AudioClip CreateArcSound(bool disperse)
    {
        const int rate = 22050;
        float duration = disperse ? .72f : .38f;
        float[] data = new float[Mathf.CeilToInt(duration*rate)];
        var random = new System.Random(disperse ? 927 : 413);
        float filteredNoise = 0f, phase = 0f;
        for (int i=0;i<data.Length;i++)
        {
            float t=i/(float)rate, u=t/duration;
            float noise=(float)random.NextDouble()*2f-1f;
            // Low-pass the crackle instead of differentiating it into a sharp hiss.
            filteredNoise += .12f*(noise-filteredNoise);
            phase += 2f*Mathf.PI*Mathf.Lerp(disperse ? 145f : 85f,disperse ? 65f : 135f,u)/rate;
            float pulse=.48f+.52f*Mathf.Pow(.5f+.5f*Mathf.Sin(t*2f*Mathf.PI*24f),2f);
            float buzz=Mathf.Sin(phase)*.22f+Mathf.Sin(phase*2f)*.10f+Mathf.Sin(phase*3f)*.06f;
            float envelope=Mathf.SmoothStep(0f,1f,Mathf.Min(t/.025f,1f))*Mathf.Pow(1f-u,disperse ? 1.2f : .65f);
            data[i]=Mathf.Clamp((filteredNoise*.8f+buzz)*pulse*envelope*1.8f,-.9f,.9f);
        }
        var clip=AudioClip.Create(disperse ? "Weapon Arc Disperse" : "Weapon Arc Assemble",data.Length,1,rate,false);
        clip.SetData(data,0);
        return clip;
    }

    void EmitDisperse(int count)
    {
        EmitBladeLightning(true);
    }

    void EmitBladeLightning(bool disperse)
    {
        if (weaponPivot == null) return;
        if (lightningLineMaterial == null)
            lightningLineMaterial = new Material(Shader.Find("Sprites/Default"));
        Vector3 root = weaponPivot.position;
        Vector3 tip = weaponPivot.TransformPoint(LocalWeaponTip());
        Vector3 axis = (tip - root).normalized;
        Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
        if (side.sqrMagnitude < .01f) side = Vector3.right;
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("Weapon_DispersingFilament");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = lightningLineMaterial;
            line.positionCount = 9;
            line.widthCurve = new AnimationCurve(new Keyframe(0,0),new Keyframe(.2f,1),new Keyframe(.75f,.6f),new Keyframe(1,0));
            float start = Random.Range(0f,.85f);
            float end = Mathf.Min(1f,start+Random.Range(.04f,.18f));
            Vector3 drift = side*(i-5.5f)*.07f + Vector3.up*Random.Range(-.08f,.15f);
            for (int j = 0; j < 9; j++)
            {
                float t = Mathf.Lerp(start,end,j/8f);
                line.SetPosition(j, Vector3.Lerp(root, tip, t) + (disperse ? Vector3.zero : drift*.4f)
                    + side*Random.Range(-.008f,.008f));
            }
            Color tint = new Color(.08f,.38f,.72f,.5f);
            go.AddComponent<VoidRendTransientLine>().Initialize(line, disperse ? .65f : .3f, .012f,tint);
            StartCoroutine(DriftFlash(line, drift * (disperse ? 1f : -1f), disperse ? .65f : .3f));
        }
    }

    System.Collections.IEnumerator DriftFlash(LineRenderer line, Vector3 velocity, float duration)
    {
        float age = 0;
        while (line != null && age < duration)
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < line.positionCount; i++) line.SetPosition(i, line.GetPosition(i) + velocity * dt);
            age += dt;
            yield return null;
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
        if (generatedMaterializeSound != null) Destroy(generatedMaterializeSound);
        if (generatedDisperseSound != null) Destroy(generatedDisperseSound);
        RestoreMaterials();
        foreach(var pair in dissolveMaterials) foreach(var m in pair.Value) if(m != null) Destroy(m);
        if (runtimeParticleMaterial != null) Destroy(runtimeParticleMaterial);
        if (runtimeLightningTexture != null) Destroy(runtimeLightningTexture);
        if (lightningLineMaterial != null) Destroy(lightningLineMaterial);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        dispersing = pendingReveal = drawn = skillDrawActive = false;
        dissolveAmount = 1f;
        SetWeaponRenderers(false);
        RestoreMaterials();
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
