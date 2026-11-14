using UnityEngine;

/// <summary>
/// 打擊感用的粒子特效（全部程式產生，播完自己刪除）：
///   ImpactFx.Hit(位置, 攻擊方向, 材質, 重擊, 擊破)   命中瞬間的閃光、衝擊環、火花＋依材質的碎屑
///   ImpactFx.Dust(位置, 半徑, 份量)                    落地 / 碎裂揚起的塵土
///   ImpactFx.Leaves(位置, 半徑, 數量)                  樹被打、倒下時落下的葉片
/// 材質沿用 BossRageEffect 的 URP 粒子材質，和專案其他程式特效一致。
/// </summary>
public static class ImpactFx
{
    static Material additive, alpha, ringMat;
    static Texture2D ringTex;

    static Material Additive => additive != null ? additive : additive = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);
    static Material Alpha => alpha != null ? alpha : alpha = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
    static Material Ring => ringMat != null ? ringMat : ringMat = BossRageEffect.CreateParticleMaterial(RingTexture(), true);

    public static Color SparkColor(ImpactSurface s) => s switch
    {
        ImpactSurface.Rock => new Color(1f, 0.62f, 0.28f),
        ImpactSurface.Wood => new Color(1f, 0.82f, 0.5f),
        _ => new Color(1f, 0.92f, 0.7f),
    };

    public static void Hit(Vector3 point, Vector3 dir, ImpactSurface surface, bool heavy, bool finishing)
    {
        dir.y = Mathf.Max(dir.y, 0f);
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
        dir.Normalize();
        float k = heavy ? 1.45f : 1f;
        if (finishing) k *= 1.25f;
        Color spark = SparkColor(surface);

        // 1. 命中閃光（一顆很亮、很短的光球）
        {
            var ps = Create("FX_HitFlash", point, Quaternion.identity, Additive, 0.1f);
            var m = ps.main;
            m.startLifetime = 0.07f; m.startSpeed = 0f;
            m.startSize = 0.9f * k;
            m.startColor = Color.Lerp(spark, Color.white, 0.6f);
            Burst(ps, 1);
            Fade(ps, 1f, 0.4f);
            ps.Play();
        }
        // 2. 衝擊環（往外擴散的一圈）
        {
            var ps = Create("FX_HitRing", point, Quaternion.identity, Ring, 0.2f);
            var m = ps.main;
            m.startLifetime = 0.16f; m.startSpeed = 0f;
            m.startSize = 1.7f * k;
            m.startColor = new Color(spark.r, spark.g, spark.b, 0.8f);
            Burst(ps, 1);
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = AlphaFade(1f, 0f);
            ps.Play();
        }
        // 3. 火花：順著攻擊方向往前噴、沿速度拉長
        {
            var ps = Create("FX_HitSparks", point, Quaternion.LookRotation(dir), Additive, 0.3f);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.28f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f * k, 13f * k);
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            m.startColor = new ParticleSystem.MinMaxGradient(spark, Color.Lerp(spark, Color.white, 0.6f));
            m.gravityModifier = 1.2f;
            int count = Mathf.RoundToInt((surface == ImpactSurface.Wood ? 10 : 18) * k);
            m.maxParticles = count + 4;
            Burst(ps, count);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.08f;
            Fade(ps, 1f, 0.3f);
            Stretch(ps, 0.05f);
            ps.Play();
        }

        switch (surface)
        {
            case ImpactSurface.Rock:
                Chips(point, dir, new Color(0.32f, 0.31f, 0.29f), new Color(0.55f, 0.53f, 0.5f), Mathf.RoundToInt(10 * k), 2.6f);
                Puff(point, new Color(0.6f, 0.58f, 0.54f, 0.55f), Mathf.RoundToInt(6 * k), 0.35f, 1f * k);
                break;
            case ImpactSurface.Wood:
                Chips(point, dir, new Color(0.36f, 0.25f, 0.15f), new Color(0.62f, 0.48f, 0.3f), Mathf.RoundToInt(12 * k), 2.2f);
                break;
            default:
                Chips(point, dir, new Color(0.85f, 0.82f, 0.72f), new Color(1f, 0.97f, 0.9f), Mathf.RoundToInt(6 * k), 1.6f);
                break;
        }
    }

    /// <summary>碎屑（小顆、受重力往下掉）。</summary>
    static void Chips(Vector3 point, Vector3 dir, Color a, Color b, int count, float gravity)
    {
        var ps = Create("FX_HitChips", point, Quaternion.LookRotation(dir), Alpha, 0.9f);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new ParticleSystem.MinMaxGradient(a, b);
        m.gravityModifier = gravity;
        m.maxParticles = count + 4;
        Burst(ps, count);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 70f; sh.radius = 0.1f;
        Fade(ps, 1f, 0.6f);
        ps.Play();
    }

    static void Puff(Vector3 point, Color color, int count, float radius, float size)
    {
        var ps = Create("FX_HitDust", point, Quaternion.identity, Alpha, 1.2f);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.8f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.35f * size, 0.9f * size);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = color;
        m.gravityModifier = -0.05f;
        m.maxParticles = count + 4;
        Burst(ps, count);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));
        var col = ps.colorOverLifetime; col.enabled = true; col.color = AlphaFade(1f, 0f);
        ps.Play();
    }

    /// <summary>落地 / 碎裂揚起的塵土（貼地往外擴散）。</summary>
    public static void Dust(Vector3 pos, float radius, float amount)
    {
        var ps = Create("FX_GroundDust", pos + Vector3.up * 0.3f, Quaternion.Euler(-90f, 0f, 0f), Alpha, 2.5f);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(1f * amount, 2.4f * amount);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.52f, 0.46f, 0.5f), new Color(0.7f, 0.68f, 0.62f, 0.6f));
        m.gravityModifier = -0.02f;
        int count = Mathf.RoundToInt(18 * amount);
        m.maxParticles = count + 4;
        Burst(ps, count);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 0.6f;
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.4f; lim.dampen = 0.08f;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.5f));
        var col = ps.colorOverLifetime; col.enabled = true; col.color = AlphaFade(1f, 0f);
        ps.Play();
    }

    /// <summary>落葉（慢慢飄下、會翻轉）。</summary>
    public static void Leaves(Vector3 pos, float radius, int count)
    {
        var ps = Create("FX_Leaves", pos, Quaternion.identity, Alpha, 3f);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.42f, 0.16f), new Color(0.62f, 0.6f, 0.26f));
        m.gravityModifier = 0.12f;
        m.maxParticles = count + 4;
        Burst(ps, count);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.8f;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        Fade(ps, 1f, 0.7f);
        ps.Play();
    }

    // ---------------------------------------------------------------- 共用

    static ParticleSystem Create(string name, Vector3 pos, Quaternion rot, Material mat, float duration)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(pos, rot);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        // 頓幀時特效照常播放，閃光和火花才會「定格」在畫面上
        main.useUnscaledTime = false;
        var emission = ps.emission; emission.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static void Burst(ParticleSystem ps, int count)
    {
        var e = ps.emission;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });
    }

    static void Fade(ParticleSystem ps, float startAlpha, float endSize)
    {
        var col = ps.colorOverLifetime; col.enabled = true; col.color = AlphaFade(startAlpha, 0f);
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endSize));
    }

    static Gradient AlphaFade(float a0, float a1)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a0 * 0.8f, 0.4f), new GradientAlphaKey(a1, 1f) });
        return g;
    }

    static void Stretch(ParticleSystem ps, float velocityScale)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = velocityScale;
        r.lengthScale = 1.6f;
    }

    static Texture2D RingTexture()
    {
        if (ringTex != null) return ringTex;
        const int S = 128;
        ringTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ImpactRing" };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.12f);
                a *= a;
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        ringTex.Apply();
        return ringTex;
    }
}
