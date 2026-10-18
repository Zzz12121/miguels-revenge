using UnityEngine;

/// <summary>
/// 戰鬥用的粒子特效，全部用程式產生（不用做 Prefab），播完會自己刪掉：
///   CombatVFX.Slash(位置, 方向, 顏色)     揮劍時往前噴的火花（小怪 / 聖騎士）
///   CombatVFX.Impact(位置, 顏色)          打中目標時炸開的火花
///   CombatVFX.Ring(位置, 顏色, 半徑)      地面往外擴散的一圈（時鐘敵人落地震波）
///   CombatVFX.Puff(位置, 方向, 顏色)      噴射口的煙 / 絲（蜘蛛吐絲、敵艦開砲）
///   CombatVFX.Trail(跟著的物件, 顏色)     投射物後面拖的尾巴（蜘蛛絲）
///   CombatVFX.Heal(跟著的物件)            撿到補血道具時往上飄的綠色光點
/// 材質用 BossRageEffect 的粒子材質（URP Particles/Unlit），跟專案其他特效一樣。
/// </summary>
public static class CombatVFX
{
    static Material additive, alpha;

    static Material Additive
    {
        get
        {
            if (additive == null) additive = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);
            return additive;
        }
    }

    static Material Alpha
    {
        get
        {
            if (alpha == null) alpha = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
            return alpha;
        }
    }

    // ---------------------------------------------------------------- 預設特效

    /// <summary>揮劍時往前噴的火花</summary>
    public static void Slash(Vector3 origin, Vector3 forward, Color color)
    {
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        var ps = Create("VFX_Slash", origin, Quaternion.LookRotation(forward), Additive, 0.35f);
        Setup(ps, color, count: 26, speed: new Vector2(5f, 11f), size: new Vector2(0.05f, 0.16f),
              life: new Vector2(0.18f, 0.4f), gravity: 0.6f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 0.25f;
        StretchAlongVelocity(ps, 0.06f);
        ps.Play();
    }

    /// <summary>打中目標時炸開的火花</summary>
    public static void Impact(Vector3 point, Color color)
    {
        var ps = Create("VFX_Impact", point, Quaternion.identity, Additive, 0.3f);
        Setup(ps, color, count: 18, speed: new Vector2(2f, 6f), size: new Vector2(0.06f, 0.18f),
              life: new Vector2(0.15f, 0.35f), gravity: 0.8f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        StretchAlongVelocity(ps, 0.04f);
        ps.Play();
    }

    /// <summary>地面往外擴散的一圈（落地震波）</summary>
    public static void Ring(Vector3 center, Color color, float radius)
    {
        // Circle 形狀在本地 XY 平面，轉 -90 度讓它平躺在地面
        var ps = Create("VFX_Ring", center + Vector3.up * 0.15f, Quaternion.Euler(-90f, 0f, 0f), Additive, 0.6f);
        Setup(ps, color, count: 70, speed: new Vector2(radius * 1.6f, radius * 2.2f), size: new Vector2(0.15f, 0.35f),
              life: new Vector2(0.35f, 0.55f), gravity: 0f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.4f;
        shape.radiusThickness = 0f; // 只從圓周往外噴
        ps.Play();

        // 中間再炸一團往上的光點
        var up = Create("VFX_RingDust", center, Quaternion.Euler(-90f, 0f, 0f), Alpha, 0.6f);
        Setup(up, new Color(color.r * 0.6f, color.g * 0.55f, color.b * 0.45f, 0.6f), count: 20,
              speed: new Vector2(1f, 3f), size: new Vector2(0.4f, 0.9f), life: new Vector2(0.5f, 0.9f), gravity: -0.1f);
        var s2 = up.shape;
        s2.shapeType = ParticleSystemShapeType.Circle;
        s2.radius = radius * 0.5f;
        up.Play();
    }

    /// <summary>噴射口的煙 / 絲（蜘蛛吐絲、敵艦開砲）</summary>
    public static void Puff(Vector3 origin, Vector3 forward, Color color, bool glow = false)
    {
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        var ps = Create("VFX_Puff", origin, Quaternion.LookRotation(forward), glow ? Additive : Alpha, 0.3f);
        Setup(ps, color, count: 16, speed: new Vector2(1.5f, 4.5f), size: new Vector2(0.2f, 0.5f),
              life: new Vector2(0.25f, 0.6f), gravity: -0.05f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.1f;
        ps.Play();
    }

    /// <summary>投射物後面拖的尾巴：掛在 target 底下，跟著它一起被刪掉</summary>
    public static void Trail(Transform target, Color color, float size = 0.18f)
    {
        if (target == null) return;
        var go = new GameObject("VFX_Trail");
        go.transform.SetParent(target, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 12f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;
        FadeAndShrink(ps);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Additive;
        ps.Play();
    }

    /// <summary>撿到補血道具：綠色光點從玩家身上往上飄</summary>
    public static void Heal(Transform target)
    {
        if (target == null) return;
        var ps = Create("VFX_Heal", target.position, Quaternion.Euler(-90f, 0f, 0f), Additive, 1.2f);
        ps.transform.SetParent(target, true);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        Setup(ps, new Color(0.35f, 1f, 0.45f), count: 40, speed: new Vector2(1f, 2.5f), size: new Vector2(0.08f, 0.2f),
              life: new Vector2(0.6f, 1.1f), gravity: 0f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.6f;
        ps.Play();
    }

    // ---------------------------------------------------------------- 共用

    static ParticleSystem Create(string name, Vector3 pos, Quaternion rot, Material mat, float duration)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(pos, rot);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 先停下來才能改 duration
        var main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy; // 播完自己刪掉
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        return ps;
    }

    static void Setup(ParticleSystem ps, Color color, int count, Vector2 speed, Vector2 size, Vector2 life, float gravity)
    {
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        Color bright = Color.Lerp(color, Color.white, 0.45f);
        bright.a = color.a;
        main.startColor = new ParticleSystem.MinMaxGradient(color, bright);
        main.gravityModifier = gravity;
        main.maxParticles = Mathf.Max(count * 2, 32);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        FadeAndShrink(ps);
    }

    static void FadeAndShrink(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
    }

    /// <summary>火花沿著飛行方向拉長，看起來像一條條亮線</summary>
    static void StretchAlongVelocity(ParticleSystem ps, float velocityScale)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = velocityScale;
        r.lengthScale = 1.5f;
    }
}
