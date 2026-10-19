using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 流沙引導路線：沿著子物件（路徑點）一路往前流動的沙粒，引導玩家從蜘蛛巢穴走到 Boss 廣場。
/// 子物件的順序就是流動方向（第一個 → 最後一個）；在 Scene 視窗直接拖子物件就能改路線，
/// 黃色 Gizmo 線就是流沙會出現的位置。執行時每一段會貼著地形（Terrain）高度擺放。
/// 玩家走到最後一個路徑點附近（或 Boss 被打倒）之後流沙會慢慢消失。
/// </summary>
public class SandFlowPath : MonoBehaviour
{
    [Tooltip("每隔幾公尺放一個流沙發射點")]
    public float spacing = 4f;
    [Tooltip("流沙帶的寬度 (m)")]
    public float width = 1.4f;
    [Tooltip("沙粒往前流的速度 (m/s)")]
    public float flowSpeed = 3f;
    [Tooltip("離地面的高度 (m)")]
    public float heightOffset = 0.08f;
    public Color sandColor = new Color(0.95f, 0.78f, 0.45f, 0.85f);
    public Color glintColor = new Color(1f, 0.85f, 0.4f, 0.9f);
    [Tooltip("玩家走到最後一個路徑點多近時，流沙消失 (m)。0 = 不會消失")]
    public float hideWhenReachedRadius = 6f;
    [Tooltip("這個 Boss 被打倒後流沙消失（可留空）")]
    public ClockEnemyHealth boss;

    readonly List<ParticleSystem> emitters = new List<ParticleSystem>();
    Transform player;
    Vector3 endPoint;
    bool hidden;

    void Start()
    {
        var hp = PlayerHealth.Find();
        if (hp != null) player = hp.transform;

        var points = new List<Vector3>();
        foreach (Transform c in transform) points.Add(Ground(c.position));
        if (points.Count < 2) return;
        endPoint = points[points.Count - 1];

        var sandMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
        var glintMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);

        // 沿著路線每 spacing 公尺放一個發射點，朝向下一個點（貼地形算坡度）
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector3 a = points[i], b = points[i + 1];
            float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / spacing));
            for (int s = 0; s < steps; s++)
            {
                Vector3 p0 = Ground(Vector3.Lerp(a, b, (float)s / steps));
                Vector3 p1 = Ground(Vector3.Lerp(a, b, (float)(s + 1) / steps));
                Vector3 dir = p1 - p0;
                if (dir.sqrMagnitude < 0.01f) continue;
                emitters.Add(CreateEmitter(p0, dir, sandMat, glintMat));
            }
        }
    }

    /// <summary>點 p 正下方的地形高度（加上 heightOffset）</summary>
    Vector3 Ground(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
        p.y += heightOffset;
        return p;
    }

    ParticleSystem CreateEmitter(Vector3 pos, Vector3 dir, Material sandMat, Material glintMat)
    {
        float length = dir.magnitude;
        var go = new GameObject("SandFlow");
        go.transform.SetParent(transform, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.normalized));

        // 主體：細沙粒貼著地面往前流（拉長的光點看起來像一條條沙紋）
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 2f;
        main.prewarm = true;
        float life = length / flowSpeed;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(flowSpeed * 0.85f, flowSpeed * 1.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        main.startColor = sandColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 120;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.rateOverTime = 26f * width;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(width, 0.02f, 0.3f);
        shape.position = Vector3.zero;

        // 往前流（本地 +Z），左右輕輕擺動像流沙
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.4f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = sandMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.12f;
        rend.lengthScale = 2.5f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 偶爾閃一下的金色亮點，遠遠也看得到路線
        var glintGo = new GameObject("Glint");
        glintGo.transform.SetParent(go.transform, false);
        var gl = glintGo.AddComponent<ParticleSystem>();
        gl.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var gm = gl.main;
        gm.loop = true;
        gm.prewarm = true;
        gm.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        gm.startSpeed = new ParticleSystem.MinMaxCurve(flowSpeed * 0.9f, flowSpeed * 1.1f);
        gm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        gm.startColor = glintColor;
        gm.simulationSpace = ParticleSystemSimulationSpace.Local;
        gm.maxParticles = 20;
        var ge = gl.emission;
        ge.rateOverTime = 2.5f;
        var gs = gl.shape;
        gs.shapeType = ParticleSystemShapeType.Box;
        gs.scale = new Vector3(width * 0.7f, 0.02f, 0.3f);
        var gc = gl.colorOverLifetime;
        gc.enabled = true;
        gc.color = g;
        var gr = glintGo.GetComponent<ParticleSystemRenderer>();
        gr.sharedMaterial = glintMat;
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ps.Play(true);
        return ps;
    }

    void Update()
    {
        if (hidden || emitters.Count == 0) return;
        bool reached = hideWhenReachedRadius > 0f && player != null
                       && Vector3.Distance(player.position, endPoint) <= hideWhenReachedRadius;
        if (reached || (boss != null && boss.IsDead)) Hide();
    }

    /// <summary>停止流沙（已經流出來的沙粒會自然消失）</summary>
    public void Hide()
    {
        hidden = true;
        foreach (var ps in emitters) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
        Transform prev = null;
        foreach (Transform c in transform)
        {
            Gizmos.DrawSphere(c.position, 0.6f);
            if (prev != null)
            {
                Gizmos.DrawLine(prev.position, c.position);
                // 箭頭：流動方向
                Vector3 mid = (prev.position + c.position) * 0.5f;
                Vector3 d = (c.position - prev.position).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, d) * 1.5f;
                Gizmos.DrawLine(mid, mid - d * 2f + side);
                Gizmos.DrawLine(mid, mid - d * 2f - side);
            }
            prev = c;
        }
    }
}
