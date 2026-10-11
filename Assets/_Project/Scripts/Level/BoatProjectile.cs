using UnityEngine;

/// <summary>
/// 敵艦發射的遠程投射物（火球/拋射物）：
/// - 直線純物理軌道飛行（包含微幅自然重力，飛行途中不轉彎、不導引）
/// - 在掠過玩家 1 公尺範圍內或碰擊時觸發範圍傷害引爆
/// </summary>
public class BoatProjectile : MonoBehaviour
{
    public float damage = 12f;
    public float speed = 25f;
    public float lifeTime = 5.0f;
    public float explosionRadius = 1.0f; // 攻擊半徑 1 公尺
    public Vector3 velocity;

    float spawnTime;
    TrailRenderer trail;
    bool hasExploded = false;
    PlayerHealth cachedPlayerHealth;

    void Start()
    {
        spawnTime = Time.time;
        cachedPlayerHealth = FindFirstObjectByType<PlayerHealth>();

        // 發光拖尾
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.3f;
        trail.startWidth = 0.45f;
        trail.endWidth = 0.05f;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        mat.color = new Color(1f, 0.4f, 0.05f, 0.95f);
        trail.material = mat;
        trail.startColor = new Color(1f, 0.5f, 0.1f, 1f);
        trail.endColor = new Color(0.9f, 0.1f, 0.0f, 0f);
    }

    void Update()
    {
        if (hasExploded) return;

        // 微幅自然重力（垂直微幅下墜，保持直線拋物線，絕不水平轉彎追蹤）
        velocity += Vector3.down * 2.8f * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;

        // 飛行中檢查是否進入玩家 1 公尺攻擊半徑
        if (cachedPlayerHealth != null && !cachedPlayerHealth.IsDead)
        {
            float distToPlayer = Vector3.Distance(transform.position, cachedPlayerHealth.transform.position + Vector3.up * 1.0f);
            if (distToPlayer <= explosionRadius)
            {
                Explode();
                return;
            }
        }

        // 碰水或超時銷毀
        if (transform.position.y < 0.8f || Time.time - spawnTime > lifeTime)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasExploded) return;
        // 忽略敵艦自己
        if (other.GetComponentInParent<EnemyBoat>() != null) return;

        // 碰到玩家本體
        var ph = other.GetComponentInParent<PlayerHealth>();
        if (ph != null && !ph.IsDead)
        {
            cachedPlayerHealth = ph;
            Explode();
            return;
        }

        // 碰到長船船身：檢查是否在玩家攻擊半徑內
        var boat = other.GetComponentInParent<VikingBoat>();
        if (boat != null)
        {
            if (cachedPlayerHealth != null && Vector3.Distance(transform.position, cachedPlayerHealth.transform.position) <= explosionRadius + 1.2f)
            {
                Explode();
            }
            else
            {
                hasExploded = true;
                GameAudio.Play("SFX/rock_hit", transform.position, 0.6f, 1.3f, 0.1f, 1f, 30f);
                Destroy(gameObject);
            }
        }
    }

    void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        if (cachedPlayerHealth != null && !cachedPlayerHealth.IsDead)
        {
            cachedPlayerHealth.TakeDamage(damage, velocity.normalized * 3f);
            ThirdPersonCamera.ShakeMain(0.22f, 0.15f);
        }

        GameAudio.Play("SFX/rock_hit", transform.position, 0.85f, 1.2f, 0.1f, 1f, 35f);
        Destroy(gameObject);
    }
}
