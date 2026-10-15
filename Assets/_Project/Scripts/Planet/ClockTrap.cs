using System.Collections;
using UnityEngine;

/// <summary>
/// 時鐘陷阱：隨機大小、玩家踩到觸發定身與扣血、時間到期觸發粒子特效並銷毀
/// </summary>
[RequireComponent(typeof(Collider))]
public class ClockTrap : MonoBehaviour
{
    [Header("時鐘設定")]
    [Tooltip("陷阱存在壽命（秒），時間到未被踩到也會引爆")]
    public float lifeTime = 5.0f;
    [Tooltip("隨機大小的範圍 (最小比例, 最大比例)")]
    public Vector2 minMaxScale = new Vector2(0.6f, 1.8f);

    [Header("效果設定")]
    public float damage = 15f;
    public float rootDuration = 2.0f;
    [Tooltip("時間到期或觸發時在當地生成的粒子特效 Prefab")]
    public GameObject triggerEffectPrefab;

    private bool isTriggered = false;

    void Start()
    {
        // 1. 隨機生成時鐘大小
        float randomScale = Random.Range(minMaxScale.x, minMaxScale.y);
        transform.localScale = Vector3.one * randomScale;

        // 2. 啟動壽命倒數（時間到未被踩到也會觸發特效消失）
        StartCoroutine(LifeCountdown());
    }

    IEnumerator LifeCountdown()
    {
        yield return new WaitForSeconds(lifeTime);
        TriggerExplosion();
    }

    void OnTriggerEnter(Collider other)
    {
        if (isTriggered) return;

        // 偵測玩家踏入
        var playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null && !playerHealth.IsDead)
        {
            isTriggered = true;

            // 扣血
            Vector3 hitDir = (other.transform.position - transform.position).normalized;
            playerHealth.TakeDamage(damage, hitDir * 2f);

            // 定身（透過 ThirdPersonController 將速度歸零，或呼叫現有定身邏輯）
            var playerController = other.GetComponentInParent<ThirdPersonController>();
            if (playerController != null)
            {
                StartCoroutine(RootPlayerRoutine(playerController));
            }

            TriggerExplosion();
        }
    }

    IEnumerator RootPlayerRoutine(ThirdPersonController controller)
    {
        float originalMultiplier = controller.speedMultiplier;
        controller.speedMultiplier = 0f; // 定身

        yield return new WaitForSeconds(rootDuration);

        if (controller != null)
        {
            controller.speedMultiplier = originalMultiplier; // 解除定身
        }
    }

    void TriggerExplosion()
    {
        if (triggerEffectPrefab != null)
        {
            // 在時鐘當前位置生成粒子特效
            Instantiate(triggerEffectPrefab, transform.position, Quaternion.identity);
        }

        Destroy(gameObject);
    }
}