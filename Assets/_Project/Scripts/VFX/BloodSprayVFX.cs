using System.Collections;
using UnityEngine;

/// <summary>
/// 受擊噴血：BloodSprayVFX.Spawn(打中的位置, 噴出去的方向, 強度)。
///   1. 血滴：往打擊方向噴出的深紅色細長血滴，受重力落下，碰到地面就消失
///   2. 血霧：打中那一點一小團暗紅色的霧
///   3. 地上的血跡：往下打一條射線，在地面貼幾片血漬，幾秒後淡掉
/// 全部用程式產生，播完自己刪掉。strength 1 = 一般受擊，2~3 = 致命一擊（量更多、噴更遠）。
/// </summary>
public static class BloodSprayVFX
{
    public static Color Bright = new Color(0.62f, 0.02f, 0.02f, 1f);
    public static Color Dark = new Color(0.28f, 0f, 0.01f, 1f);
    public static float StainSeconds = 7f;

    static Material dropMat, stainMat;

    static void EnsureMaterials()
    {
        if (dropMat == null) dropMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
        if (stainMat == null) stainMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
    }

    public static void Spawn(Vector3 point, Vector3 direction, float strength = 1f)
    {
        EnsureMaterials();
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
        direction.Normalize();
        strength = Mathf.Clamp(strength, 0.3f, 3f);

        var root = new GameObject("VFX_BloodSpray");
        root.transform.position = point;
        Drops(root.transform, direction, strength);
        Mist(root.transform, strength);
        Object.Destroy(root, 2.5f);
        Stains(point, direction, strength);
    }

    static ParticleSystem NewSystem(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = false; m.playOnAwake = false; m.duration = 0.2f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = dropMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static void Drops(Transform parent, Vector3 dir, float strength)
    {
        var ps = NewSystem("Drops", parent);
        ps.transform.rotation = Quaternion.LookRotation((dir + Vector3.up * 0.35f).normalized);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * strength, 6.5f * Mathf.Sqrt(strength));
        m.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
        m.startColor = new ParticleSystem.MinMaxGradient(Bright, Dark);
        m.gravityModifier = 1.6f;
        m.maxParticles = 120;
        var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(22 * strength)) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 32f; sh.radius = 0.06f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        // 碰到地面就消失（不彈跳）
        var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
        c.bounce = 0f; c.lifetimeLoss = 1f; c.quality = ParticleSystemCollisionQuality.Low; c.radiusScale = 0.5f;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f; r.lengthScale = 2.2f;
        ps.Play();
    }

    static void Mist(Transform parent, float strength)
    {
        var ps = NewSystem("Mist", parent);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.25f * strength, 0.5f * strength);
        m.startColor = new Color(Dark.r, Dark.g, Dark.b, 0.55f);
        m.maxParticles = 12;
        var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.5f));
        ps.Play();
    }

    /// <summary>地上的血跡：打中那一點往噴的方向前方 0.3~1.6m 落幾片</summary>
    static void Stains(Vector3 point, Vector3 dir, float strength)
    {
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude < 0.01f) flat = Random.insideUnitSphere; flat.y = 0f;
        flat.Normalize();
        int n = Mathf.RoundToInt(2 + strength * 2);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = point + flat * Random.Range(0.3f, 1.6f * Mathf.Sqrt(strength)) + Random.insideUnitSphere * 0.35f;
            // 找最上面的「地面」：跳過角色（玩家、敵人、會動的剛體）和太陡的面
            RaycastHit hit = default; bool found = false; float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.normal.y < 0.55f || h.collider.attachedRigidbody != null) continue;
                if (h.collider.GetComponentInParent<IDamageable>() != null || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                if (h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            if (!found) continue;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "BloodStain";
            q.transform.position = hit.point + hit.normal * 0.015f;
            q.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = Random.Range(0.18f, 0.45f) * Mathf.Sqrt(strength);
            q.transform.localScale = new Vector3(s * Random.Range(0.8f, 1.5f), s, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = stainMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            q.AddComponent<StainFade>().Begin(Color.Lerp(Bright, Dark, Random.value), StainSeconds);
        }
    }

    sealed class StainFade : MonoBehaviour
    {
        public void Begin(Color c, float seconds) => StartCoroutine(Run(c, seconds));

        IEnumerator Run(Color c, float seconds)
        {
            var r = GetComponent<MeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            int id = Shader.PropertyToID("_BaseColor");
            Vector3 full = transform.localScale;
            // 血跡一下子暈開，最後慢慢淡掉
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float grow = Mathf.Clamp01(t / 0.12f);
                transform.localScale = full * (0.4f + 0.6f * grow);
                float a = 0.85f * (1f - Mathf.Clamp01((t - seconds * 0.6f) / (seconds * 0.4f)));
                mpb.SetColor(id, new Color(c.r, c.g, c.b, a));
                r.SetPropertyBlock(mpb);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
