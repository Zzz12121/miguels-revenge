using UnityEngine;

/// <summary>
/// 玩家在船上射出的箭：
///   - 直線飛、帶一點點重力，箭頭跟著飛行方向
///   - 射中敵艦的船員（BoatCrewMember）→ 有效，船員受傷 / 倒下
///   - 射中敵艦船身 → 無效（插在船上，跳出「無效」）
///   - 射中其他東西就插在上面；掉進海裡濺起水花
/// 船員優先：同一段飛行路徑上同時碰到船身和船員（船員站在船舷後面）時，算射中船員。
/// </summary>
public class PlayerArrow : MonoBehaviour
{
    public Vector3 velocity;
    public float gravity = 4f;
    public float lifeTime = 4f;
    public float waterLevel = 1.3f;
    [HideInInspector] public Transform owner;      // 射箭的玩家（不會射到自己）
    [HideInInspector] public Transform ownerBoat;  // 玩家搭的船
    [HideInInspector] public PlayerCombat shooter; // 射中時通知準心顯示命中

    float spawnTime;
    bool stuck;
    static readonly RaycastHit[] hits = new RaycastHit[16];

    void Start()
    {
        spawnTime = Time.time;
        var trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.2f;
        trail.startWidth = 0.06f;
        trail.endWidth = 0f;
        trail.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"))
        {
            color = new Color(0.8f, 0.95f, 1f, 0.8f)
        };
        trail.startColor = new Color(0.85f, 0.95f, 1f, 0.9f);
        trail.endColor = new Color(0.85f, 0.95f, 1f, 0f);
    }

    void Update()
    {
        if (stuck) return;
        if (Time.time - spawnTime > lifeTime) { Destroy(gameObject); return; }

        Vector3 from = transform.position;
        velocity += Vector3.down * gravity * Time.deltaTime;
        Vector3 step = velocity * Time.deltaTime;
        float len = step.magnitude;
        if (len > 0.0001f)
        {
            int n = Physics.RaycastNonAlloc(from, step / len, hits, len, ~0, QueryTriggerInteraction.Ignore);
            if (n > 0 && Resolve(n)) return;
        }

        transform.position = from + step;
        if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity);

        if (transform.position.y < waterLevel)
        {
            CombatVFX.Puff(transform.position, Vector3.up, new Color(0.8f, 0.9f, 1f, 0.7f));
            GameAudio.Play("SFX/RPG/cloth4", transform.position, 0.5f, 1.4f, 0.1f, 1f, 30f);
            Destroy(gameObject);
        }
    }

    /// <summary>處理這一步碰到的東西；回傳 true = 箭停下來了</summary>
    bool Resolve(int n)
    {
        // 1. 船員優先
        BoatCrewMember crew = null;
        RaycastHit crewHit = default;
        float crewDist = float.MaxValue;
        // 2. 其他擋住箭的東西（最近的那個）
        RaycastHit block = default;
        float blockDist = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            Transform t = h.collider.transform;
            if (owner != null && t.IsChildOf(owner)) continue;
            if (ownerBoat != null && t.IsChildOf(ownerBoat)) continue;

            var c = h.collider.GetComponentInParent<BoatCrewMember>();
            if (c != null && !c.IsDead)
            {
                if (h.distance < crewDist) { crew = c; crewHit = h; crewDist = h.distance; }
                continue;
            }
            if (h.distance < blockDist) { block = h; blockDist = h.distance; }
        }

        if (crew != null)
        {
            crew.TakeDamage(1f, crewHit.point);
            if (shooter != null) shooter.NotifyArrowHit(true);
            Destroy(gameObject);
            return true;
        }
        if (blockDist < float.MaxValue)
        {
            var boat = block.collider.GetComponentInParent<EnemyBoat>();
            if (boat != null)
            {
                boat.ShowInvalidHit(block.point); // 射到船身：無效
                if (shooter != null) shooter.NotifyArrowHit(false);
            }
            else GameAudio.Play("SFX/RPG/chop", block.point, 0.5f, 1.5f, 0.1f, 1f, 30f);
            Stick(block);
            return true;
        }
        return false;
    }

    /// <summary>插在打到的東西上，過幾秒消失</summary>
    void Stick(RaycastHit hit)
    {
        stuck = true;
        transform.position = hit.point - transform.forward * 0.3f;
        transform.SetParent(hit.collider.transform, true);
        Destroy(gameObject, 3f);
    }
}
