using UnityEngine;

/// <summary>
/// 黑光特效：身體周圍冒出黑色煙霧、外圍飄著暗紫色的光點，再加一盞暗紫色的光。
/// 用 Active 開關（例如時間吞噬者受擊後的無敵時間），開關時會淡入淡出，不會突然出現 / 消失。
/// 粒子和材質全部用程式產生，掛上去就能用；大小依照物件的模型範圍自動調整。
/// </summary>
public class DarkAuraEffect : MonoBehaviour
{
    public Color smokeColor = new Color(0.03f, 0f, 0.05f, 0.85f);
    public Color sparkColor = new Color(0.55f, 0.2f, 1f, 1f);
    public Color lightColor = new Color(0.35f, 0.05f, 0.6f);
    [Tooltip("開關的淡入淡出速度（每秒）")]
    public float fadeSpeed = 6f;
    public float smokeRate = 70f;
    public float sparkRate = 30f;
    public float lightIntensity = 4f;

    public bool Active { get; set; }

    ParticleSystem smoke, sparks;
    Light glow;
    float level;

    void Start()
    {
        // 模型範圍：決定煙霧大小和位置（不算粒子之類的特效）
        var bounds = new Bounds(transform.position, Vector3.one);
        bool any = false;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        float radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.5f, 6f);
        float height = Mathf.Clamp(bounds.size.y, 1f, 12f);

        var root = new GameObject("DarkAura").transform;
        root.SetParent(transform, false);
        root.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        smoke = Build(root, "Smoke", false, height);
        var main = smoke.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.6f, radius * 1.1f);
        main.startColor = smokeColor;
        main.gravityModifier = -0.15f; // 往上飄
        var shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.7f;
        var size = smoke.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.4f));
        FadeOut(smoke, 0.15f);

        sparks = Build(root, "Sparks", true, height);
        main = sparks.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = sparkColor;
        main.gravityModifier = -0.3f;
        shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.9f;
        FadeOut(sparks, 0f);

        glow = new GameObject("DarkAuraLight").AddComponent<Light>();
        glow.transform.SetParent(root, false);
        glow.transform.localPosition = Vector3.up * height * 0.5f;
        glow.type = LightType.Point;
        glow.color = lightColor;
        glow.range = radius * 4f + 2f;
        glow.intensity = 0f;
        glow.enabled = false;
    }

    static ParticleSystem Build(Transform parent, string name, bool additive, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * height * 0.5f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), additive);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    /// <summary>粒子一出生先淡入（fadeIn 比例），最後淡出</summary>
    static void FadeOut(ParticleSystem ps, float fadeIn)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  fadeIn > 0f
                      ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) }
                      : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    void Update()
    {
        if (smoke == null) return;
        level = Mathf.MoveTowards(level, Active ? 1f : 0f, fadeSpeed * Time.deltaTime);
        var em = smoke.emission;
        em.rateOverTime = smokeRate * level;
        em = sparks.emission;
        em.rateOverTime = sparkRate * level;
        glow.enabled = level > 0.01f;
        glow.intensity = lightIntensity * level * (0.85f + 0.15f * Mathf.Sin(Time.time * 9f));
    }
}
