using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// 石粉：對付百骨駭足的骷髏蝙蝠。
///   取得：投降的埋伏弓箭手告訴玩家「周圍發著微光的石頭可以磨成粉」，撿一顆石頭（PowderStone）= 一份石粉，最多帶 Max 份。
///   使用：按「撒石粉」鍵（預設 F），在身邊撒出一團石粉，Duration 秒內蝙蝠被擾亂、找不到玩家：
///         俯衝撞過來的蝙蝠（蝠群襲擊）和護體蝙蝠（蝠群護體）都不會造成傷害，撞進粉塵裡就亂飛散開。
/// 數量存在記憶體（Stock），跨場景保留（和弓、箭的數量一樣，不寫進存檔）。
/// </summary>
public static class BatPowder
{
    public const string Item = "StonePowder";
    public const int Max = 10;
    /// <summary>撒一次的有效時間（秒）</summary>
    public static float Duration = 8f;
    /// <summary>粉塵的半徑（只是外觀；有效期間蝙蝠一律打不到玩家）</summary>
    public static float Radius = 3.2f;

    static float activeUntil = -1f, swarmWarnUntil = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeUntil = -1f; swarmWarnUntil = -1f; Stock = 0; }

    /// <summary>石粉現在有效（蝙蝠打不到玩家）</summary>
    public static bool Active => Time.time < activeUntil;
    public static float Remaining => Mathf.Max(0f, activeUntil - Time.time);

    /// <summary>蝙蝠技能開始時呼叫：畫面提示玩家可以撒石粉</summary>
    public static void WarnSwarm(float seconds = 6f) => swarmWarnUntil = Mathf.Max(swarmWarnUntil, Time.time + seconds);
    public static bool SwarmWarning => Time.time < swarmWarnUntil;

    /// <summary>
    /// 身上有幾份石粉。存在記憶體（static）而不是 PlayerInventory：道場 → 霧海孤島（ForestIsland）都不在 GameManager 的關卡清單裡，
    /// 換場景時背包不會帶過去；這樣在道場撿的石粉到百骨駭足那裡還在，倒下從吊橋重來也保留。
    /// </summary>
    public static int Stock { get; private set; }

    public static int Count(PlayerHealth player) => Stock;

    /// <summary>撿到一顆石頭：回傳 false = 已經帶滿了</summary>
    public static bool AddStone(PlayerHealth player)
    {
        if (Stock >= Max) return false;
        Stock++;
        return true;
    }

    /// <summary>撒石粉（扣一份）。回傳 false = 沒有石粉</summary>
    public static bool Use(PlayerHealth player)
    {
        if (player == null || Stock <= 0) return false;
        Stock--;
        activeUntil = Time.time + Duration;
        swarmWarnUntil = -1f;
        BatPowderCloud.Spawn(player.transform, Duration);
        GameAudio.Play("SFX/RPG/dropLeather", player.transform.position, 0.8f, 1.4f, 0.08f, 1f, 25f);
        return true;
    }

    /// <summary>蝙蝠撞進粉塵：不扣血、亂飛散開，粉塵噴一下</summary>
    public static void Confuse(SkeletonBat bat, Vector3 playerPos)
    {
        if (bat == null) return;
        Vector3 at = bat.transform.position;
        // 往隨機方向散開（不是單純往外，看起來是暈頭轉向）
        bat.Disperse(at + Random.insideUnitSphere * 2f - (at - playerPos).normalized * 0.5f);
        CombatVFX.Puff(at, Random.onUnitSphere, new Color(0.85f, 0.83f, 0.78f, 0.7f));
    }
}

/// <summary>撒出去的石粉：一開始噴一大團，之後在玩家身邊持續飄著淡淡的粉塵直到失效</summary>
public sealed class BatPowderCloud : MonoBehaviour
{
    static readonly Color Dust = new Color(0.86f, 0.84f, 0.78f, 0.55f);
    static readonly Color Glint = new Color(1f, 0.97f, 0.85f, 0.9f);
    static Material dustMat, glintMat;
    static BatPowderCloud current;

    public static void Spawn(Transform follow, float duration)
    {
        if (current != null) Destroy(current.gameObject);   // 重撒：換一團新的
        var go = new GameObject("VFX_BatPowder");
        current = go.AddComponent<BatPowderCloud>();
        current.Begin(follow, duration);
    }

    Transform follow;
    ParticleSystem haze;

    void Begin(Transform target, float duration)
    {
        follow = target;
        transform.position = target.position;
        if (dustMat == null) dustMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
        if (glintMat == null) glintMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);

        // 1. 撒出去的那一下：往四周噴開的一大團粉
        var burst = NewSystem("Burst", dustMat);
        {
            var m = burst.main;
            m.duration = 0.5f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            m.startColor = Dust; m.gravityModifier = -0.05f; m.maxParticles = 80;
            var e = burst.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
            var sh = burst.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.6f;
            var lv = burst.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 1f; lv.dampen = 0.15f;
            Fade(burst, 1f);
            burst.transform.localPosition = Vector3.up * 1.1f;
            burst.Play();
        }
        // 2. 持續：身邊淡淡的粉塵 + 閃閃的細粉
        haze = NewSystem("Haze", dustMat);
        {
            var m = haze.main;
            m.loop = true; m.duration = 1f; m.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            m.startSize = new ParticleSystem.MinMaxCurve(1f, 2f);
            m.startColor = new Color(Dust.r, Dust.g, Dust.b, 0.28f); m.maxParticles = 60;
            var e = haze.emission; e.rateOverTime = 18f;
            var sh = haze.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = BatPowder.Radius * 0.7f;
            Fade(haze, 1f);
            haze.transform.localPosition = Vector3.up * 1f;
            haze.Play();
        }
        var glint = NewSystem("Glint", glintMat);
        {
            var m = glint.main;
            m.loop = true; m.duration = 1f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            m.startColor = Glint; m.maxParticles = 60;
            var e = glint.emission; e.rateOverTime = 30f;
            var sh = glint.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = BatPowder.Radius * 0.6f;
            Fade(glint, 1f);
            glint.transform.localPosition = Vector3.up * 1f;
            glint.transform.SetParent(haze.transform, true);
            glint.Play();
        }
        StartCoroutine(Run(duration));
    }

    ParticleSystem NewSystem(string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static void Fade(ParticleSystem ps, float peak)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    IEnumerator Run(float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (follow == null) break;
            transform.position = follow.position;
            yield return null;
        }
        // 時間到：停止產生，等剩下的粉飄完
        foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        yield return new WaitForSeconds(2.5f);
        Destroy(gameObject);
    }

    void OnDestroy() { if (current == this) current = null; }
}

/// <summary>
/// 地上可以撿的石頭（磨成石粉）：發著淡淡微光、有細粉閃爍，靠近按互動鍵撿起來 = 一份石粉。
/// 由投降的弓箭手說完話之後在附近生成（PowderStone.Scatter）；霧海孤島倒下重來時也會撒在樹林邊緣（ForestRetry）。
/// ShowMarker()：頭上加一顆旋轉、上下飄的黃色菱形提示，遠遠就看得到。
/// </summary>
public sealed class PowderStone : MonoBehaviour
{
    public float pickupRange = 2.2f;

    static Material stoneMat, sparkleMat, markerMat;
    static Mesh diamondMesh;
    static readonly Color MarkerColor = new Color(1f, 0.85f, 0.15f);
    PlayerHealth player;
    Transform visual, marker;
    float phase;

    /// <summary>頭上加黃色菱形提示（旋轉、上下飄，不受光影影響）</summary>
    public PowderStone ShowMarker()
    {
        if (marker != null) return this;
        if (diamondMesh == null) diamondMesh = BuildDiamond();
        if (markerMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            markerMat = new Material(sh != null ? sh : Shader.Find("Unlit/Color")) { name = "M_PowderStoneMarker" };
            markerMat.SetColor("_BaseColor", MarkerColor);
            markerMat.SetColor("_Color", MarkerColor);
        }
        marker = new GameObject("Marker").transform;
        marker.SetParent(transform, false);
        marker.localPosition = Vector3.up * 1.5f;
        marker.localScale = new Vector3(0.32f, 0.5f, 0.32f);
        marker.gameObject.AddComponent<MeshFilter>().sharedMesh = diamondMesh;
        var mr = marker.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = markerMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var light = marker.gameObject.AddComponent<Light>();
        light.type = LightType.Point; light.color = MarkerColor; light.range = 3f; light.intensity = 1.2f; light.shadows = LightShadows.None;
        return this;
    }

    /// <summary>上下兩個四角錐拼成的菱形（八面體），平面著色</summary>
    static Mesh BuildDiamond()
    {
        Vector3 top = Vector3.up, bottom = Vector3.down;
        Vector3[] ring = { new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f) };
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = ring[i], b = ring[(i + 1) % 4];
            foreach (var tri in new[] { new[] { top, b, a }, new[] { bottom, a, b } })
            {
                tris.Add(verts.Count); verts.Add(tri[0]);
                tris.Add(verts.Count); verts.Add(tri[1]);
                tris.Add(verts.Count); verts.Add(tri[2]);
            }
        }
        var m = new Mesh { name = "PowderStoneDiamond" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>在 center 附近（minR ~ maxR）撒 count 顆石頭，貼齊地面、避開高低差太大的地方</summary>
    public static int Scatter(Vector3 center, int count, float minR, float maxR, int seed = 0)
    {
        var rnd = new System.Random(seed == 0 ? Mathf.RoundToInt(center.x * 13f + center.z * 7f) : seed);
        int placed = 0;
        for (int attempt = 0; attempt < count * 8 && placed < count; attempt++)
        {
            float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Lerp(minR, maxR, (float)rnd.NextDouble());
            Vector3 p = center + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            if (!Ground(p, center.y, out Vector3 g)) continue;
            Create(g, (float)rnd.NextDouble() * 360f);
            placed++;
        }
        return placed;
    }

    static bool Ground(Vector3 p, float refY, out Vector3 ground)
    {
        ground = p;
        float best = float.MaxValue; bool found = false;
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 8f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<PlayerHealth>() != null || h.collider.GetComponentInParent<AmbushArcher>() != null) continue;
            if (h.normal.y < 0.7f) continue;   // 太陡（石頭側面、山壁）
            float d = Mathf.Abs(h.point.y - refY);
            if (d < best) { best = d; ground = h.point; found = true; }
        }
        return found && best < 2.5f;
    }

    public static PowderStone Create(Vector3 pos, float yaw)
    {
        var go = new GameObject("PowderStone");
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var stone = go.AddComponent<PowderStone>();
        stone.Build();
        return stone;
    }

    void Build()
    {
        if (stoneMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            stoneMat = new Material(sh != null ? sh : Shader.Find("Standard")) { name = "M_PowderStone" };
            stoneMat.SetColor("_BaseColor", new Color(0.78f, 0.76f, 0.7f));
            stoneMat.SetFloat("_Smoothness", 0.25f);
            stoneMat.EnableKeyword("_EMISSION");
            stoneMat.SetColor("_EmissionColor", new Color(0.55f, 0.5f, 0.38f) * 0.6f);   // 淡淡的微光，草叢裡也找得到
        }
        if (sparkleMat == null) sparkleMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);

        // 兩顆疊在一起的扁圓石，看起來像一小堆碎石
        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        for (int i = 0; i < 2; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(visual, false);
            s.transform.localPosition = i == 0 ? new Vector3(0f, 0.09f, 0f) : new Vector3(0.14f, 0.06f, 0.08f);
            s.transform.localRotation = Quaternion.Euler(i * 23f, i * 61f, i * 17f);
            s.transform.localScale = i == 0 ? new Vector3(0.32f, 0.2f, 0.26f) : new Vector3(0.18f, 0.13f, 0.16f);
            s.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
        }

        var go = new GameObject("Sparkle");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.25f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        m.startColor = new Color(1f, 0.93f, 0.7f, 0.9f);
        m.maxParticles = 12; m.simulationSpace = ParticleSystemSimulationSpace.World;
        var e = ps.emission; e.rateOverTime = 5f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.25f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = sparkleMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        phase = Random.value * 10f;
    }

    void Update()
    {
        if (visual != null) visual.localPosition = Vector3.up * (0.03f * Mathf.Sin(Time.time * 2f + phase));
        if (marker != null)
        {
            marker.localPosition = Vector3.up * (1.5f + 0.12f * Mathf.Sin(Time.time * 2.4f + phase));
            marker.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
        }
        if (player == null) { player = PlayerHealth.Find(); if (player == null) return; }
        if (player.IsDead) return;
        float d = Vector3.Distance(player.transform.position, transform.position);
        if (d > pickupRange) return;
        bool full = BatPowder.Count(player) >= BatPowder.Max;
        InteractPrompt.Offer(this, d, full ? $"石粉已經帶滿了（{BatPowder.Max}）" : $"撿起石頭（磨成石粉 {BatPowder.Count(player)}/{BatPowder.Max}）", !full);
        if (!InteractPrompt.Pressed(this) || !BatPowder.AddStone(player)) return;
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.6f, 1.6f, 0.05f, 1f, 15f);
        CombatVFX.Puff(transform.position + Vector3.up * 0.2f, Vector3.up, new Color(0.86f, 0.84f, 0.78f, 0.6f));
        Destroy(gameObject);
    }
}
