using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss 戰的流沙牆：戰鬥開始時沿著競技場的四邊，從上方（周圍的岩壁高處）倒下一道道流沙瀑布，
/// 底下有看不見的牆擋住玩家，打倒 Boss 之前出不去。
/// 由 ClockEnemyController 在開戰時呼叫 BossSandWall.Raise 建立；Boss 被打倒時 Collapse（流沙停止、牆消失），
/// 玩家死掉重生時 Remove（直接拿掉，下次開戰再重新升起）。
/// 每一段會依照地形高度擺放，所以斜坡、高低差也擋得住。
/// </summary>
public class BossSandWall : MonoBehaviour
{
    [Tooltip("每一段牆的長度 (m)")]
    public float segmentLength = 6f;
    [Tooltip("牆的高度（從地面往上）(m)")]
    public float wallHeight = 25f;
    [Tooltip("流沙從地面上方多高的地方倒下來 (m)")]
    public float fallHeight = 9f;
    public Color sandColor = new Color(0.86f, 0.7f, 0.45f, 0.9f);

    readonly List<ParticleSystem> streams = new List<ParticleSystem>();
    readonly List<Collider> walls = new List<Collider>();
    float raisedTime;
    static Material sandMat;

    // 專案關掉了進 Play 時重新載入腳本：上一次 Play 建立的材質可能已經被清掉，重新建立
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => sandMat = null;

    /// <summary>在 min ~ max（XZ 平面的長方形）四邊升起流沙牆</summary>
    public static BossSandWall Raise(Vector2 min, Vector2 max, Color? color = null)
    {
        var wall = new GameObject("BossSandWall").AddComponent<BossSandWall>();
        if (color.HasValue) wall.sandColor = color.Value;
        wall.Build(min, max);
        return wall;
    }

    void Build(Vector2 min, Vector2 max)
    {
        if (sandMat == null) sandMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);

        var corners = new[]
        {
            new Vector3(min.x, 0f, min.y), new Vector3(max.x, 0f, min.y),
            new Vector3(max.x, 0f, max.y), new Vector3(min.x, 0f, max.y),
        };
        Vector3 center = (corners[0] + corners[2]) * 0.5f;
        for (int e = 0; e < 4; e++)
        {
            Vector3 a = corners[e], b = corners[(e + 1) % 4];
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / segmentLength));
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = Vector3.Lerp(a, b, (float)i / n), p1 = Vector3.Lerp(a, b, (float)(i + 1) / n);
                BuildSegment(p0, p1, center);
            }
        }

        raisedTime = Time.time;
        GameAudio.Play("SFX/roar_rock_crumble", center, 0.9f, 0.7f, 0f, 0f);
        ThirdPersonCamera.ShakeMain(0.25f, 0.6f);
        VictoryBanner.ShowSmall("流沙封住了退路！", "打倒時間吞噬者才能離開", new Color(1f, 0.8f, 0.45f));
    }

    static float Ground(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        return terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : p.y;
    }

    void BuildSegment(Vector3 p0, Vector3 p1, Vector3 arenaCenter)
    {
        Vector3 mid = (p0 + p1) * 0.5f;
        float len = Vector3.Distance(p0, p1);
        // 地面取兩端和中間比較低的那個，斜坡上也不會有縫可以鑽
        float ground = Mathf.Min(Ground(p0), Ground(p1), Ground(mid));
        float groundMax = Mathf.Max(Ground(p0), Ground(p1), Ground(mid));
        Quaternion rot = Quaternion.LookRotation((p1 - p0).normalized);

        // 看不見的牆
        var seg = new GameObject("SandWallSegment");
        seg.transform.SetParent(transform, false);
        seg.transform.SetPositionAndRotation(new Vector3(mid.x, ground, mid.z), rot);
        var box = seg.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, wallHeight + (groundMax - ground) + 2f, len + 0.5f);
        box.center = new Vector3(0f, box.size.y * 0.5f - 2f, 0f);
        walls.Add(box);

        // 流沙瀑布：從上方的岩壁倒下來，略往競技場內側噴一點
        Vector3 inward = arenaCenter - mid;
        inward.y = 0f;
        inward.Normalize();
        var fall = new GameObject("SandFall");
        fall.transform.SetParent(seg.transform, false);
        fall.transform.position = new Vector3(mid.x, groundMax + fallHeight, mid.z) - inward * 0.6f;
        fall.transform.rotation = rot;
        var ps = fall.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startColor = sandColor;
        main.gravityModifier = 0.9f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;
        var em = ps.emission;
        em.rateOverTime = 14f * len;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.6f, 0.2f, len);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(inward.x * 0.6f, inward.x * 1.4f);
        vel.y = new ParticleSystem.MinMaxCurve(-2f, -1f);
        vel.z = new ParticleSystem.MinMaxCurve(inward.z * 0.6f, inward.z * 1.4f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.5f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.8f, 0.7f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rend = fall.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = sandMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.15f;
        rend.lengthScale = 2f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        streams.Add(ps);

        // 底部揚起的沙塵
        var dust = new GameObject("SandDust");
        dust.transform.SetParent(seg.transform, false);
        dust.transform.position = new Vector3(mid.x, ground + 0.3f, mid.z);
        dust.transform.rotation = rot;
        var ds = dust.AddComponent<ParticleSystem>();
        ds.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var dm = ds.main;
        dm.loop = true;
        dm.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
        dm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        dm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        dm.startColor = new Color(sandColor.r, sandColor.g, sandColor.b, 0.35f);
        dm.gravityModifier = -0.05f;
        dm.simulationSpace = ParticleSystemSimulationSpace.World;
        dm.maxParticles = 40;
        var de = ds.emission;
        de.rateOverTime = 2.5f * len;
        var dsh = ds.shape;
        dsh.shapeType = ParticleSystemShapeType.Box;
        dsh.scale = new Vector3(1.2f, 0.2f, len);
        var dc = ds.colorOverLifetime;
        dc.enabled = true;
        dc.color = g;
        var dr = dust.GetComponent<ParticleSystemRenderer>();
        dr.sharedMaterial = sandMat;
        dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ds.Play();
        streams.Add(ds);
    }

    /// <summary>Boss 被打倒：流沙停止、牆消失（已經落下的沙會自然散掉）</summary>
    public void Collapse()
    {
        foreach (var w in walls) if (w != null) w.enabled = false;
        foreach (var s in streams) if (s != null) s.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        GameAudio.Play("SFX/roar_rock_crumble", transform.position, 0.6f, 1.2f, 0f, 0f);
        Destroy(gameObject, 3f);
    }

    /// <summary>玩家死掉、戰鬥重置：直接拿掉</summary>
    public void Remove() => Destroy(gameObject);

    /// <summary>在 Scene 視窗畫出流沙牆會出現的位置（給 ClockEnemyController 的 OnDrawGizmosSelected 用）</summary>
    public static void DrawGizmo(Vector2 min, Vector2 max, float y)
    {
        Gizmos.color = new Color(0.95f, 0.75f, 0.4f, 0.9f);
        Vector3 c = new Vector3((min.x + max.x) * 0.5f, y + 5f, (min.y + max.y) * 0.5f);
        Gizmos.DrawWireCube(c, new Vector3(max.x - min.x, 10f, max.y - min.y));
    }
}
