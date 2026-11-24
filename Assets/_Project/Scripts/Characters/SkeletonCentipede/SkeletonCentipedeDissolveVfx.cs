using System.Collections;
using UnityEngine;

/// <summary>
/// 百足駭骨（分身）倒下後「消散」：骨頭從上往下一塊塊碎成灰，邊緣帶暗紅餘燼光，
/// 同時從身體表面飄起骨灰和零星火星，最後整隻消失、物件刪掉。
///
/// 用法：StartCoroutine(SkeletonCentipedeDissolveVfx.For(gameObject).Run(延遲秒數));
///   SkeletonCentipedeBossAI 的分身死掉時會自動呼叫，不用手動放。
/// 材質：Resources/SkeletonCentipedeDissolve.shader（執行時複製原本材質的貼圖和顏色）。
/// 效能：一個 SkinnedMeshRenderer 換材質 + 兩個粒子系統（最多約 450 顆），只在消散的那幾秒存在。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkeletonCentipedeDissolveVfx : MonoBehaviour
{
    [Tooltip("消散花幾秒")]
    public float duration = 2.6f;
    [Tooltip("消散邊緣的顏色（HDR，越亮越像餘燼）")]
    [ColorUsage(false, true)] public Color edgeColor = new Color(2.4f, 0.55f, 0.18f);
    [Tooltip("骨灰顏色")]
    public Color ashColor = new Color(0.78f, 0.74f, 0.68f, 0.75f);
    [Tooltip("火星顏色")]
    public Color emberColor = new Color(1f, 0.45f, 0.15f, 1f);
    [Tooltip("骨灰最多每秒噴幾顆（消散最快的時候）")]
    public float ashRate = 160f;
    [Tooltip("火星最多每秒噴幾顆")]
    public float emberRate = 55f;

    static readonly int AmountId = Shader.PropertyToID("_Amount");
    static readonly int HeightMinId = Shader.PropertyToID("_HeightMin");
    static readonly int HeightMaxId = Shader.PropertyToID("_HeightMax");
    static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    static Shader shader;
    static Material ashMat, emberMat;

    public bool Running { get; private set; }

    public static SkeletonCentipedeDissolveVfx For(GameObject go)
    {
        var v = go.GetComponent<SkeletonCentipedeDissolveVfx>();
        return v != null ? v : go.AddComponent<SkeletonCentipedeDissolveVfx>();
    }

    /// <summary>等 delay 秒（讓死亡動畫播完）→ 消散 → 刪掉整個物件</summary>
    public IEnumerator Run(float delay)
    {
        if (Running) yield break;
        Running = true;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // 碰撞先關掉：消散途中不擋玩家、也不會被打到
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        // 鐮刀 / 尾巴的拖尾一起收掉
        foreach (var t in GetComponentsInChildren<TrailRenderer>()) { t.emitting = false; t.enabled = false; }

        var skins = GetComponentsInChildren<SkinnedMeshRenderer>();
        var mats = new System.Collections.Generic.List<Material>();
        if (shader == null) shader = Resources.Load<Shader>("SkeletonCentipedeDissolve");
        if (shader != null && shader.isSupported)
        {
            foreach (var r in skins)
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var m = new Material(shader) { name = "SkeletonCentipedeDissolve (Instance)" };
                    if (src[i] != null)
                    {
                        if (src[i].HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", src[i].GetTexture("_BaseMap")); m.SetTextureScale("_BaseMap", src[i].GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", src[i].GetTextureOffset("_BaseMap")); }
                        if (src[i].HasProperty("_BaseColor")) m.SetColor("_BaseColor", src[i].GetColor("_BaseColor"));
                    }
                    m.SetColor(EdgeColorId, edgeColor);
                    dst[i] = m;
                    mats.Add(m);
                }
                r.sharedMaterials = dst;
            }
        }

        var main = skins.Length > 0 ? skins[0] : null;
        var ash = main != null ? CreateParticles("VFX_CentipedeAsh", main, false) : null;
        var ember = main != null ? CreateParticles("VFX_CentipedeEmbers", main, true) : null;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float u = t / duration;
            float amount = u * u * (3f - 2f * u);   // 先慢、中間快、最後收尾
            Bounds b = BodyBounds(skins);
            foreach (var m in mats)
            {
                m.SetFloat(AmountId, amount);
                m.SetFloat(HeightMinId, b.min.y);
                m.SetFloat(HeightMaxId, b.max.y);
            }
            // 散得最快的時候灰最多（4u(1-u) 在一半的時候最大）
            float burst = Mathf.Clamp01(4f * u * (1f - u) * 1.3f);
            if (ash != null) { var e = ash.emission; e.rateOverTime = ashRate * burst; }
            if (ember != null) { var e = ember.emission; e.rateOverTime = emberRate * burst; }
            yield return null;
        }

        foreach (var r in skins) r.enabled = false;
        foreach (var ps in new[] { ash, ember })
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            ps.transform.SetParent(null, true);     // 物件刪掉後，飄在空中的灰繼續飄完
            Destroy(ps.gameObject, 3f);
        }
        foreach (var m in mats) Destroy(m);
        Destroy(gameObject);
    }

    static Bounds BodyBounds(SkinnedMeshRenderer[] skins)
    {
        Bounds b = default; bool any = false;
        foreach (var r in skins)
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    ParticleSystem CreateParticles(string name, SkinnedMeshRenderer skin, bool embers)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var m = ps.main;
        m.duration = 10f;
        m.loop = true;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.scalingMode = ParticleSystemScalingMode.Shape;
        m.maxParticles = embers ? 120 : 330;
        m.startLifetime = embers ? new ParticleSystem.MinMaxCurve(0.8f, 1.6f) : new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        m.startSpeed = embers ? new ParticleSystem.MinMaxCurve(0.4f, 1.6f) : new ParticleSystem.MinMaxCurve(0.1f, 0.7f);
        m.startSize = embers ? new ParticleSystem.MinMaxCurve(0.04f, 0.1f) : new ParticleSystem.MinMaxCurve(0.12f, 0.38f);
        m.startColor = embers ? emberColor : ashColor;
        m.gravityModifier = embers ? -0.12f : -0.05f;   // 往上飄
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var e = ps.emission;
        e.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
        shape.skinnedMeshRenderer = skin;
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        shape.normalOffset = 0.05f;

        // 被風吹散：往上 + 隨機飄
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        vel.y = embers ? new ParticleSystem.MinMaxCurve(0.6f, 1.6f) : new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = embers ? 0.9f : 0.55f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.4f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        if (embers)
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.3f), 0.5f), new GradientColorKey(new Color(0.4f, 0.1f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        else
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.7f), 0f), new GradientColorKey(Color.white, 0.25f), new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, embers ? AnimationCurve.Linear(0f, 1f, 1f, 0.2f) : AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));

        var rend = go.GetComponent<ParticleSystemRenderer>();
        if (embers) { if (emberMat == null) emberMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true); rend.sharedMaterial = emberMat; }
        else { if (ashMat == null) ashMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false); rend.sharedMaterial = ashMat; }
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortingFudge = embers ? -1f : 0f;

        ps.Play();
        return ps;
    }
}
