using UnityEngine;

/// <summary>
/// 敵艦發射的遠程投射物（弓箭手的箭；isArrow = false 時是舊的火球）：
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
    [Tooltip("箭：細的白色尾跡、箭頭朝著飛行方向、打中時是箭射中的聲音和木屑")]
    public bool isArrow;

    float spawnTime;
    TrailRenderer trail;
    bool hasExploded = false;
    PlayerHealth cachedPlayerHealth;

    void Start()
    {
        spawnTime = Time.time;
        cachedPlayerHealth = FindFirstObjectByType<PlayerHealth>();

        // 拖尾：箭是細的白色氣流，火球是粗的橘紅色
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = isArrow ? 0.18f : 0.3f;
        trail.startWidth = isArrow ? 0.08f : 0.45f;
        trail.endWidth = isArrow ? 0f : 0.05f;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        mat.color = isArrow ? new Color(1f, 1f, 1f, 0.7f) : new Color(1f, 0.4f, 0.05f, 0.95f);
        trail.material = mat;
        trail.startColor = isArrow ? new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 0.5f, 0.1f, 1f);
        trail.endColor = isArrow ? new Color(1f, 1f, 1f, 0f) : new Color(0.9f, 0.1f, 0.0f, 0f);
    }

    void Update()
    {
        if (hasExploded) return;

        // 微幅自然重力（垂直微幅下墜，保持直線拋物線，絕不水平轉彎追蹤）
        velocity += Vector3.down * 2.8f * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        if (isArrow && velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity); // 箭頭跟著拋物線往下

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
                if (isArrow)
                {
                    // 箭插在船身上：木頭的聲音和木屑
                    GameAudio.Play("SFX/RPG/chop", transform.position, 0.7f, 1.4f, 0.1f, 1f, 30f);
                    CombatVFX.Impact(transform.position, new Color(0.75f, 0.55f, 0.3f));
                }
                else GameAudio.Play("SFX/rock_hit", transform.position, 0.6f, 1.3f, 0.1f, 1f, 30f);
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

        if (isArrow)
        {
            GameAudio.Play("SFX/RPG/knifeSlice", transform.position, 0.9f, 0.9f, 0.1f, 1f, 35f);
            CombatVFX.Impact(transform.position, new Color(1f, 0.9f, 0.75f));
        }
        else GameAudio.Play("SFX/rock_hit", transform.position, 0.85f, 1.2f, 0.1f, 1f, 35f);
        Destroy(gameObject);
    }
}
