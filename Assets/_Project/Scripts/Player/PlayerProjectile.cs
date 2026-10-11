using UnityEngine;

/// <summary>
/// 玩家發射的遠程投射物（風刃/魔法矢/劍氣）：
/// - 往瞄準方向高速飛行
/// - 擊中實作 IDamageable 的目標（例如追擊敵艦）造成傷害
/// - 命中後播放打擊特效與音效
/// </summary>
public class PlayerProjectile : MonoBehaviour
{
    public float damage = 35f;
    public float speed = 36f;
    public float lifeTime = 4.0f;
    public Vector3 direction;

    float spawnTime;
    TrailRenderer trail;

    void Start()
    {
        spawnTime = Time.time;
        // 加上藍色/青色發光拖尾
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.2f;
        trail.startWidth = 0.45f;
        trail.endWidth = 0.05f;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        mat.color = new Color(0.2f, 0.85f, 1f, 0.95f);
        trail.material = mat;
        trail.startColor = new Color(0.2f, 0.85f, 1f, 1f);
        trail.endColor = new Color(0f, 0.4f, 1f, 0f);
    }

    void Update()
    {
        transform.position += direction * (speed * Time.deltaTime);

        // 碰水或超時銷毀
        if (transform.position.y < 0.8f || Time.time - spawnTime > lifeTime)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // 忽略玩家與玩家自己的船
        if (other.GetComponentInParent<PlayerHealth>() != null) return;
        if (other.GetComponentInParent<VikingBoat>() != null) return;

        var target = other.GetComponentInParent<IDamageable>();
        if (target != null && !target.IsDead)
        {
            target.TakeDamage(damage, transform.position);
            ThirdPersonCamera.ShakeMain(0.12f, 0.15f);
            GameAudio.Play("SFX/rock_hit", transform.position, 0.9f, 1.4f, 0.08f, 1f, 35f);
            Destroy(gameObject);
            return;
        }

        // 擊中其他實體
        if (!other.isTrigger)
        {
            Destroy(gameObject);
        }
    }
}
