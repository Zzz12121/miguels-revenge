using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss 二階段（憤怒）的外觀：
///   - 身體泛紅光（石頭微微發紅，眼睛 / 核心變成刺眼的紅色），像心跳一樣一明一暗
///   - 身上不停冒出紅色火星
///   - 腳邊一圈往上竄的紅色火焰 + 往上飄的暗紅黑煙
///   - 繞著身體旋轉的火花
///   - 每隔一段時間從腳下擴散出一圈紅色波紋（像心跳 / 威壓）
///   - 胸口的紅色點光源，照亮周圍地板
/// 由 BossController 呼叫 SetRage / Burst / ResetRage，不需要手動設定。
/// </summary>
public class BossRageEffect : MonoBehaviour
{
    [Tooltip("憤怒時的顏色")]
    public Color rageColor = new Color(1f, 0.12f, 0.04f);
    [Tooltip("眼睛 / 核心這些本來就會發光的部位，憤怒時的亮度")]
    public float glowPartIntensity = 6f;
    [Tooltip("石頭身體泛紅的亮度（0 = 身體不發紅，只有眼睛 / 核心變紅）")]
    public float bodyIntensity = 0.35f;
    [Tooltip("紅光心跳的速度（每秒幾下）")]
    public float pulseSpeed = 1.2f;
    [Tooltip("閃爍的幅度（0 = 不閃）")]
    [Range(0f, 1f)] public float pulseAmount = 0.35f;
    [Tooltip("紅光淡入 / 淡出需要的秒數")]
    public float fadeTime = 1f;

    [Header("身上的火星")]
    public float emberRate = 45f;
    [Tooltip("觸發二階段瞬間爆出多少火星")]
    public int burstCount = 150;

    [Header("腳邊的火焰圈")]
    [Tooltip("每秒冒出多少團火焰")]
    public float flameRate = 70f;
    [Tooltip("火焰圈的內 / 外半徑 (m)")]
    public Vector2 flameRadius = new Vector2(1.8f, 4f);
    [Tooltip("火焰往上竄的速度 (m/s)")]
    public Vector2 flameRiseSpeed = new Vector2(2f, 4.5f);
    [Tooltip("火焰繞著 Boss 旋轉的速度 (m/s)")]
    public float flameSwirl = 1.5f;

    [Header("黑煙")]
    public float smokeRate = 14f;
    public Color smokeColor = new Color(0.12f, 0.02f, 0.02f, 0.55f);

    [Header("環繞的火花")]
    public float sparkRate = 40f;
    [Tooltip("火花環繞的半徑 (m)")]
    public float sparkRadius = 4.5f;
    [Tooltip("火花環繞的速度 (m/s)")]
    public float sparkOrbitSpeed = 6f;
    [Tooltip("火花出現的高度範圍 (m)")]
    public Vector2 sparkHeight = new Vector2(0.5f, 6f);

    [Header("地面波紋")]
    [Tooltip("每隔幾秒擴散一圈波紋（0 = 不要）")]
    public float ringInterval = 1.6f;
    [Tooltip("波紋擴散到的最大直徑 (m)")]
    public float ringSize = 16f;

    [Header("燈光")]
    public float lightIntensity = 4f;
    public float lightRange = 12f;
    [Tooltip("燈光的高度（從 Boss 腳底算）")]
    public float lightHeight = 4f;

    [Header("材質（不填會自動建立）")]
    public Material glowMaterial;
    public Material smokeMaterial;
    public Material ringMaterial;

    public float Rage => rage;

    struct MatSlot
    {
        public Material mat;
        public bool hadEmission;
        public Color baseEmission;
    }

    readonly List<MatSlot> slots = new List<MatSlot>();
    ParticleSystem embers, flames, smoke, sparks, rings;
    Light rageLight;
    float rage, targetRage;
    float flameAcc, smokeAcc, sparkAcc, ringTimer;
    bool initialized;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake() => Init();

    void Init()
    {
        if (initialized) return;
        initialized = true;

        // 收集身上所有材質（光束、粒子不算）
        var bc = GetComponent<BossController>();
        Renderer beamRenderer = bc != null && bc.beam != null ? bc.beam.GetComponent<Renderer>() : null;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == beamRenderer || r is ParticleSystemRenderer) continue;
            foreach (var m in r.materials) // .materials 會複製一份，不會改到專案裡的材質檔
            {
                if (m == null || !m.HasProperty(EmissionId)) continue;
                bool had = m.IsKeywordEnabled("_EMISSION");
                slots.Add(new MatSlot { mat = m, hadEmission = had, baseEmission = had ? m.GetColor(EmissionId) : Color.black });
            }
        }

        if (glowMaterial == null) glowMaterial = CreateParticleMaterial(SoftDot(), true);
        if (smokeMaterial == null) smokeMaterial = CreateParticleMaterial(SoftDot(), false);
        if (ringMaterial == null) ringMaterial = CreateParticleMaterial(RingTexture(), true);

        CreateEmbers();
        CreateFlames();
        CreateSmoke();
        CreateSparks();
        CreateRings();
        CreateLight();
    }

    /// <summary>設定憤怒程度（0 = 正常、1 = 全開），會慢慢淡過去</summary>
    public void SetRage(float value)
    {
        Init();
        targetRage = Mathf.Clamp01(value);
    }

    /// <summary>觸發二階段的瞬間：火星、火焰一起爆開，地面炸出一圈大波紋</summary>
    public void Burst()
    {
        Init();
        embers.Emit(burstCount);
        for (int i = 0; i < 80; i++) EmitFlame(2.5f);
        for (int i = 0; i < 20; i++) EmitSmoke(1.5f);
        for (int i = 0; i < 60; i++) EmitSpark(2f);
        EmitRing(ringSize * 1.8f, 1.2f, 1f);
        EmitRing(ringSize * 1.2f, 0.9f, 0.8f);
        ringTimer = 0f;
    }

    /// <summary>戰鬥重置：立刻恢復原樣</summary>
    public void ResetRage()
    {
        Init();
        rage = targetRage = 0f;
        foreach (var ps in new[] { embers, flames, smoke, sparks, rings })
            if (ps != null) ps.Clear();
        Apply();
    }

    void Update()
    {
        if (rage == targetRage && rage <= 0f) return;
        rage = Mathf.MoveTowards(rage, targetRage, Time.deltaTime / Mathf.Max(fadeTime, 0.01f));
        Apply();

        // 腳邊的粒子用程式一顆一顆放，位置才會剛好圍成一圈
        float dt = Time.deltaTime;
        flameAcc += flameRate * rage * dt;
        while (flameAcc >= 1f) { flameAcc -= 1f; EmitFlame(1f); }
        smokeAcc += smokeRate * rage * dt;
        while (smokeAcc >= 1f) { smokeAcc -= 1f; EmitSmoke(1f); }
        sparkAcc += sparkRate * rage * dt;
        while (sparkAcc >= 1f) { sparkAcc -= 1f; EmitSpark(1f); }

        if (ringInterval > 0f && rage > 0.5f)
        {
            ringTimer += dt;
            if (ringTimer >= ringInterval)
            {
                ringTimer = 0f;
                EmitRing(ringSize, 1.4f, 0.6f * rage);
            }
        }
    }

    void Apply()
    {
        float pulse = 1f - pulseAmount + pulseAmount * (0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
        float k = rage * pulse;

        foreach (var s in slots)
        {
            if (s.mat == null) continue;
            Color target = rageColor * (s.hadEmission ? glowPartIntensity : bodyIntensity);
            if (rage > 0f || s.hadEmission) s.mat.EnableKeyword("_EMISSION");
            else s.mat.DisableKeyword("_EMISSION");
            s.mat.SetColor(EmissionId, Color.Lerp(s.baseEmission, target, k));
        }

        if (embers != null)
        {
            var em = embers.emission;
            em.rateOverTime = emberRate * rage;
        }
        if (rageLight != null)
        {
            rageLight.enabled = rage > 0.01f;
            rageLight.intensity = lightIntensity * k;
        }
    }

    // =====================================================================
    //  放粒子
    // =====================================================================

    Vector3 Feet => transform.position;

    static Vector3 Ring(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    static Vector3 Tangent(float angle) => new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

    void EmitFlame(float power)
    {
        float a = Random.value * Mathf.PI * 2f;
        float r = Random.Range(flameRadius.x, flameRadius.y);
        var p = new ParticleSystem.EmitParams
        {
            position = Feet + Ring(a) * r + Vector3.up * Random.Range(0f, 0.4f),
            velocity = Vector3.up * Random.Range(flameRiseSpeed.x, flameRiseSpeed.y) * power
                     + Tangent(a) * flameSwirl + Ring(a) * (power - 1f) * 3f,
            startSize = Random.Range(0.7f, 1.6f),
            startLifetime = Random.Range(0.6f, 1.2f),
            startColor = Color.Lerp(new Color(1f, 0.35f, 0.08f), rageColor, Random.value),
            applyShapeToPosition = false,
        };
        flames.Emit(p, 1);
    }

    void EmitSmoke(float power)
    {
        float a = Random.value * Mathf.PI * 2f;
        float r = Random.Range(0.5f, flameRadius.y);
        var p = new ParticleSystem.EmitParams
        {
            position = Feet + Ring(a) * r + Vector3.up * Random.Range(0.5f, 2f),
            velocity = Vector3.up * Random.Range(1f, 2f) * power + Tangent(a) * flameSwirl * 0.5f,
            startSize = Random.Range(2f, 3.5f),
            startLifetime = Random.Range(2f, 3f),
            startColor = smokeColor,
            applyShapeToPosition = false,
        };
        smoke.Emit(p, 1);
    }

    void EmitSpark(float power)
    {
        float a = Random.value * Mathf.PI * 2f;
        float r = sparkRadius * Random.Range(0.6f, 1.1f);
        var p = new ParticleSystem.EmitParams
        {
            position = Feet + Ring(a) * r + Vector3.up * Random.Range(sparkHeight.x, sparkHeight.y),
            velocity = Tangent(a) * sparkOrbitSpeed * power + Vector3.up * Random.Range(0.5f, 2f) - Ring(a) * 0.8f,
            startSize = Random.Range(0.08f, 0.2f),
            startLifetime = Random.Range(0.5f, 1f),
            startColor = new Color(1f, Random.Range(0.4f, 0.8f), 0.3f),
            applyShapeToPosition = false,
        };
        sparks.Emit(p, 1);
    }

    void EmitRing(float size, float lifetime, float alpha)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = Feet + Vector3.up * 0.15f,
            velocity = Vector3.zero,
            startSize = size,
            startLifetime = lifetime,
            startColor = new Color(rageColor.r, rageColor.g, rageColor.b, alpha),
            applyShapeToPosition = false,
        };
        rings.Emit(p, 1);
    }

    // =====================================================================
    //  建立粒子系統
    // =====================================================================

    ParticleSystem NewSystem(string name, Material mat, int max, float gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // Boss 走動時粒子留在原地，看起來像拖尾
        main.maxParticles = max;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = false;

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.material = mat;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        return ps;
    }

    static Gradient FadeGradient(Color start, Color mid, Color end, float fadeIn)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(mid, 0.4f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    void CreateEmbers()
    {
        embers = NewSystem("RageEmbers", glowMaterial, 600, -0.35f);
        var main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.25f, 0.05f), new Color(1f, 0.05f, 0.02f));

        // 從模型表面冒出來；找不到模型就從身體範圍的方塊冒
        var shape = embers.shape;
        shape.enabled = true;
        var skinned = GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinned != null)
        {
            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = skinned;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 3f, 0f);
            shape.scale = new Vector3(3f, 5f, 2.5f);
        }

        var col = embers.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(FadeGradient(new Color(1f, 0.6f, 0.2f), rageColor, new Color(0.3f, 0f, 0f), 0.1f));
        var size = embers.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
        var noise = embers.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.8f;
        embers.Play();
    }

    void CreateFlames()
    {
        flames = NewSystem("RageFlames", glowMaterial, 400, -0.2f);
        var col = flames.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(FadeGradient(new Color(1f, 0.7f, 0.3f), rageColor, new Color(0.25f, 0f, 0f), 0.15f));
        var size = flames.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        var noise = flames.noise;
        noise.enabled = true;
        noise.strength = 1f;
        noise.frequency = 0.6f;
        flames.Play();
    }

    void CreateSmoke()
    {
        smoke = NewSystem("RageSmoke", smokeMaterial, 120, -0.05f);
        var col = smoke.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(FadeGradient(Color.white, Color.white, Color.white, 0.25f));
        var size = smoke.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        var noise = smoke.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.3f;
        // 煙畫在火焰後面
        smoke.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;
        smoke.Play();
    }

    void CreateSparks()
    {
        sparks = NewSystem("RageSparks", glowMaterial, 300, 0f);
        var col = sparks.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(FadeGradient(Color.white, new Color(1f, 0.6f, 0.3f), rageColor, 0.05f));
        var pr = sparks.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Stretch; // 拉成一條線，看起來速度很快
        pr.velocityScale = 0.08f;
        pr.lengthScale = 1f;
        sparks.Play();
    }

    void CreateRings()
    {
        rings = NewSystem("RagePulseRing", ringMaterial, 10, 0f);
        var col = rings.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var size = rings.sizeOverLifetime;
        size.enabled = true;
        // 很快擴散出去，再慢慢變大
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.3f, 0.7f), new Keyframe(1f, 1f)));
        var pr = rings.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.HorizontalBillboard; // 平躺在地上
        pr.maxParticleSize = 100f;
        rings.Play();
    }

    void CreateLight()
    {
        var go = new GameObject("RageLight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, lightHeight, 0f);
        rageLight = go.AddComponent<Light>();
        rageLight.type = LightType.Point;
        rageLight.color = rageColor;
        rageLight.range = lightRange;
        rageLight.intensity = 0f;
        rageLight.enabled = false;
    }

    // =====================================================================
    //  程式產生的貼圖和材質
    // =====================================================================

    static Texture2D SoftDot()
    {
        return MakeTexture(64, d => { float a = Mathf.Clamp01(1f - d); return a * a; });
    }

    static Texture2D RingTexture()
    {
        // 外緣亮、內側漸淡的環
        return MakeTexture(128, d =>
        {
            float edge = Mathf.Clamp01(1f - Mathf.Abs(d - 0.88f) / 0.1f);
            float inner = d < 0.88f ? Mathf.Pow(d / 0.88f, 4f) * 0.35f : 0f;
            return Mathf.Clamp01(edge * edge + inner);
        });
    }

    static Texture2D MakeTexture(int n, System.Func<float, float> alphaOfDistance)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, d >= 1f ? 0f : alphaOfDistance(d)));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>additive = true：疊加發光（火焰、火花）；false：一般半透明（黑煙）</summary>
    static Material CreateParticleMaterial(Texture2D tex, bool additive)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material m;
        if (sh != null)
        {
            m = new Material(sh);
            m.SetFloat("_Surface", 1f);                  // Transparent
            m.SetFloat("_Blend", additive ? 2f : 0f);    // Additive / Alpha
            m.SetFloat("_SrcBlend", 5f);                 // SrcAlpha
            m.SetFloat("_DstBlend", additive ? 1f : 10f); // One / OneMinusSrcAlpha
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
        }
        else
        {
            m = new Material(Shader.Find("Sprites/Default"));
            m.mainTexture = tex;
        }
        return m;
    }
}
