using System.Collections;
using UnityEngine;

/// <summary>
/// 可以砍倒的樹（孤島森林的樹 Prefab 根物件上）。
///   - 每被打一下：樹往被打的方向晃一下再彈回、樹冠掉葉子（打擊火花 / 木屑由 PlayerHitFeedback 負責）
///   - 打滿 hitsToFell 下（重擊算 2 下）：樹幹發出斷裂聲，朝「遠離玩家」的方向像受重力一樣越倒越快，
///     依地形停在倒地的角度，落地時沿著樹幹揚起塵土、掉一大片葉子、鏡頭震動
///   - 倒下後不再有碰撞（不會卡住玩家或 Boss）；玩家死亡重生時樹會立回來
///
/// 樹的軸心必須在樹根（孤島生成器做的樹都是）。不能是 Static Batching 物件。
/// </summary>
public class ForestFallingTree : MonoBehaviour, IDamageable, IImpactSurface
{
    [Min(1)] public int hitsToFell = 4;
    public float heavyDamageThreshold = 25f;

    [Header("受擊搖晃")]
    public float swayAngle = 2.5f;
    public float swayTime = 0.6f;

    [Header("倒下")]
    [Tooltip("樹高（0 = 自動用 Renderer 外框計算）")]
    public float height = 0f;
    public float gravity = 9f;
    [Tooltip("一開始推一把的角速度（度/秒）")]
    public float initialPush = 8f;
    public string crackSound = "SFX/RPG/creak1";
    public string crashSound = "SFX/roar_rock_crumble";
    [Tooltip("斷裂 / 倒地音效最長幾秒（原音檔很長）")]
    public float soundMaxSeconds = 0.8f;
    public float crashShake = 0.35f;
    [Tooltip("超過這個距離就不震鏡頭")]
    public float shakeRange = 35f;

    public ImpactSurface Surface => ImpactSurface.Wood;
    public bool IsDead => felled;

    int hits;
    bool felled;
    Quaternion restRot;
    Vector3 restPos;
    Collider[] colliders;
    PlayerHealth player;
    Coroutine motion;
    float crownRadius;

    void Awake()
    {
        restRot = transform.rotation;
        restPos = transform.position;
        colliders = GetComponentsInChildren<Collider>(true);
        var b = WorldBounds();
        if (height <= 0f) height = Mathf.Max(2f, b.max.y - transform.position.y);
        crownRadius = Mathf.Max(1f, Mathf.Max(b.extents.x, b.extents.z));
    }

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null) player.OnRespawned += Restore;
    }

    void OnDestroy()
    {
        if (player != null) player.OnRespawned -= Restore;
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (felled || amount <= 0f) return;
        hits += amount >= heavyDamageThreshold ? 2 : 1;
        Vector3 away = AwayFromAttacker(hitPoint);

        if (hits >= hitsToFell)
        {
            felled = true;
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(Fall(away));
            return;
        }
        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(Sway(away));
        ImpactFx.Leaves(transform.position + Vector3.up * height * 0.8f, crownRadius * 0.6f, 10);
    }

    Vector3 AwayFromAttacker(Vector3 hitPoint)
    {
        Vector3 from = player != null ? player.transform.position : hitPoint;
        Vector3 d = transform.position - from;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) d = transform.forward;
        return d.normalized;
    }

    IEnumerator Sway(Vector3 away)
    {
        var axis = Vector3.Cross(Vector3.up, away);
        float t = 0f;
        while (t < swayTime)
        {
            t += Time.deltaTime;
            float u = t / swayTime;
            // 衰減的來回擺動
            float a = swayAngle * Mathf.Sin(u * Mathf.PI * 3f) * (1f - u);
            transform.rotation = Quaternion.AngleAxis(a, axis) * restRot;
            yield return null;
        }
        transform.rotation = restRot;
        motion = null;
    }

    IEnumerator Fall(Vector3 away)
    {
        transform.rotation = restRot;
        foreach (var c in colliders) if (c != null) c.enabled = false;
        if (!string.IsNullOrEmpty(crackSound)) AudioCutoff.PlayShort(crackSound, transform.position + Vector3.up * 2f, 1f, 0.7f, soundMaxSeconds, 0.15f, 0.05f, 50f);
        ImpactFx.Leaves(transform.position + Vector3.up * height * 0.8f, crownRadius * 0.7f, 18);

        var axis = Vector3.Cross(Vector3.up, away);
        float target = GroundAngle(axis);
        float angle = 0f, vel = initialPush;
        bool bounced = false;
        // 單擺：角加速度 ∝ sin(角度)，越倒越快
        while (true)
        {
            float dt = Time.deltaTime;
            vel += gravity / (height * 0.5f) * Mathf.Sin((angle + 3f) * Mathf.Deg2Rad) * Mathf.Rad2Deg * dt;
            angle += vel * dt;
            if (angle >= target)
            {
                angle = target;
                if (!bounced)
                {
                    bounced = true;
                    Crash(axis, target);
                    vel = -vel * 0.18f;  // 落地彈一下
                }
                else break;
            }
            if (bounced && vel > 0f && angle >= target - 0.05f) break;
            transform.rotation = Quaternion.AngleAxis(angle, axis) * restRot;
            yield return null;
        }
        transform.rotation = Quaternion.AngleAxis(target, axis) * restRot;
        motion = null;
    }

    void Crash(Vector3 axis, float angle)
    {
        var rot = Quaternion.AngleAxis(angle, axis);
        Vector3 basePos = transform.position;
        Vector3 tip = basePos + rot * Vector3.up * height;
        // 沿著樹幹每隔幾公尺揚一團塵土，樹冠那端最大
        int puffs = Mathf.Clamp(Mathf.RoundToInt(height / 4f), 2, 6);
        for (int i = 1; i <= puffs; i++)
        {
            float u = i / (float)puffs;
            var p = Vector3.Lerp(basePos, tip, u);
            p.y = GroundY(p) + 0.1f;
            ImpactFx.Dust(p, 0.6f + u * crownRadius * 0.4f, 0.6f + u * 0.8f);
        }
        ImpactFx.Leaves(Vector3.Lerp(basePos, tip, 0.85f) + Vector3.up, crownRadius, 30);
        if (!string.IsNullOrEmpty(crashSound)) AudioCutoff.PlayShort(crashSound, tip, 1f, 0.75f, soundMaxSeconds, 0.25f, 0.08f, 60f);
        if (player != null)
        {
            float d = Vector3.Distance(player.transform.position, Vector3.Lerp(basePos, tip, 0.5f));
            float k = Mathf.Clamp01(1f - d / shakeRange);
            if (k > 0f) ThirdPersonCamera.ShakeMain(crashShake * k, 0.35f);
        }
    }

    /// <summary>沿著倒下的方向找出「樹梢碰到地面」的角度。</summary>
    float GroundAngle(Vector3 axis)
    {
        Vector3 basePos = transform.position;
        for (float a = 20f; a <= 100f; a += 1f)
        {
            var rot = Quaternion.AngleAxis(a, axis);
            // 樹冠有厚度，用 85% 高度處 + 樹冠半徑判斷
            Vector3 p = basePos + rot * Vector3.up * height * 0.85f;
            if (p.y <= GroundY(p) + crownRadius * 0.35f) return a;
        }
        return 88f;
    }

    static float GroundY(Vector3 p)
    {
        var t = Terrain.activeTerrain;
        if (t != null) return t.SampleHeight(p) + t.transform.position.y;
        return Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
    }

    /// <summary>立回原狀（玩家重生時自動呼叫）。</summary>
    public void Restore()
    {
        if (motion != null) StopCoroutine(motion);
        motion = null;
        hits = 0;
        felled = false;
        transform.SetPositionAndRotation(restPos, restRot);
        foreach (var c in colliders) if (c != null) c.enabled = true;
    }

    Bounds WorldBounds()
    {
        bool has = false;
        var b = new Bounds(transform.position, Vector3.one);
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }
}
