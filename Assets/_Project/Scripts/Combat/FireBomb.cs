using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 火焰彈：玩家丟出去的拋物線投射物，碰到東西就爆炸，
/// 範圍內的敵人（IDamageable）受到爆炸傷害，並持續燃燒一段時間（Burning）。
/// 用 FireBomb.Throw 產生，不需要事先做 Prefab。
/// </summary>
public class FireBomb : MonoBehaviour
{
    public static readonly Color FireColor = new Color(1f, 0.45f, 0.1f);

    [Tooltip("爆炸半徑 (m)")]
    public float radius = 3.5f;
    [Tooltip("爆炸當下的傷害")]
    public float impactDamage = 15f;
    [Tooltip("燃燒：每一跳的傷害")]
    public float burnDamage = 6f;
    [Tooltip("燃燒：幾秒跳一次")]
    public float burnInterval = 1f;
    [Tooltip("燃燒：總共燒幾秒")]
    public float burnDuration = 5f;
    public float gravity = -20f;
    public float lifetime = 5f;

    Vector3 velocity;
    Transform owner;
    float age;
    bool exploded;
    static readonly RaycastHit[] castHits = new RaycastHit[16];
    static readonly Collider[] overlap = new Collider[48];

    /// <summary>從 origin 以 velocity 丟出一顆火焰彈；owner（丟的人）不會被炸到</summary>
    public static FireBomb Throw(Vector3 origin, Vector3 velocity, Transform owner)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "FireBomb";
        Destroy(go.GetComponent<Collider>()); // 用射線判定碰撞
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.32f;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.35f, 0.08f, 0.02f));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", FireColor * 2.5f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
        CombatVFX.Trail(go.transform, FireColor, 0.3f);

        var bomb = go.AddComponent<FireBomb>();
        bomb.velocity = velocity;
        bomb.owner = owner;
        return bomb;
    }

    void Update()
    {
        if (exploded) return;
        float dt = Time.deltaTime;
        age += dt;
        velocity += Vector3.up * gravity * dt;
        Vector3 step = velocity * dt;
        Vector3 from = transform.position;

        // 這一幀會不會撞到東西（自己、丟的人、觸發區都不算）
        int n = Physics.SphereCastNonAlloc(from, 0.16f, step.normalized, castHits, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 hitPoint = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            var h = castHits[i];
            if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
            if (h.distance <= 0f && h.point == Vector3.zero) continue; // 起點就在碰撞體裡
            if (h.distance < best) { best = h.distance; hitPoint = h.point; }
        }
        if (best < float.MaxValue)
        {
            transform.position = hitPoint;
            Explode();
            return;
        }

        transform.position = from + step;
        transform.Rotate(velocity.magnitude * 40f * dt, 0f, 0f, Space.Self);
        if (age >= lifetime) Explode();
    }

    void Explode()
    {
        exploded = true;
        Vector3 center = transform.position;

        CombatVFX.Ring(center, FireColor, radius);
        CombatVFX.Impact(center + Vector3.up * 0.3f, new Color(1f, 0.75f, 0.3f));
        CombatVFX.Puff(center, Vector3.up, FireColor, true);
        GameAudio.Play("SFX/rock_hit", center, 1f, 1.3f, 0.05f, 1f, 45f);
        GameAudio.Play("SFX/torch_out", center, 0.6f, 0.7f, 0.05f, 1f, 35f);
        ThirdPersonCamera.ShakeMain(0.2f, 0.25f);

        var done = new HashSet<IDamageable>();
        int n = Physics.OverlapSphereNonAlloc(center, radius, overlap, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var c = overlap[i];
            if (owner != null && c.transform.IsChildOf(owner)) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || !done.Add(target)) continue;
            var comp = target as Component;
            if (comp == null) continue;

            target.TakeDamage(impactDamage, c.ClosestPoint(center));
            Burning.Apply(comp.gameObject, burnDamage, burnInterval, burnDuration);
        }
        Destroy(gameObject);
    }
}

/// <summary>燃燒中：每 interval 秒扣一次血、冒火花，時間到或目標死掉就移除。重複燃燒會重新計時</summary>
public class Burning : MonoBehaviour
{
    float damage, interval, endTime, nextTick;
    IDamageable target;

    public static void Apply(GameObject go, float damage, float interval, float duration)
    {
        var b = go.GetComponent<Burning>();
        if (b == null)
        {
            b = go.AddComponent<Burning>();
            b.nextTick = Time.time + interval;
        }
        b.target = go.GetComponent<IDamageable>();
        b.damage = Mathf.Max(b.damage, damage);
        b.interval = interval;
        b.endTime = Time.time + duration;
    }

    void Update()
    {
        if (target == null || target.IsDead || Time.time > endTime)
        {
            Destroy(this);
            return;
        }
        if (Random.value < Time.deltaTime * 6f)
            CombatVFX.Puff(transform.position + Random.insideUnitSphere * 0.4f + Vector3.up * 0.3f, Vector3.up, FireBomb.FireColor, true);

        if (Time.time >= nextTick)
        {
            nextTick = Time.time + interval;
            // 打擊點放在正上方，不會往某個方向一直把敵人推走（敵人自己的受擊反應仍會輕微後退）
            target.TakeDamage(damage, transform.position + Vector3.up * 3f);
            CombatVFX.Impact(transform.position + Vector3.up * 0.8f, FireBomb.FireColor);
        }
    }
}
