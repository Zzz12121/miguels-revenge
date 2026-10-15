using System.Collections;
using UnityEngine;

/// <summary>
/// 篝火互動：玩家靠近時回復 20% 血量，回復完畢後粒子特效消失。
/// 當玩家重生時會自動重置，恢復成燃燒狀態。
/// </summary>
public class Campfire : MonoBehaviour
{
    [Header("設定")]
    [Tooltip("玩家靠近互動的範圍 (m)")]
    public float interactRange = 2.5f;
    [Tooltip("火堆的粒子特效物件 (Particle System)")]
    public GameObject fireParticleEffect;

    Transform player;
    PlayerHealth playerHealth;
    bool isUsed = false; // 紀錄是否已被使用

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
            
            // 註冊玩家重生的事件，以便重置營火
            playerHealth.OnRespawned += ResetCampfire;
        }

        ResetCampfire(); // 初始化營火狀態
    }

    void OnDestroy()
    {
        // 移除事件監聽，避免記憶體洩漏
        if (playerHealth != null)
        {
            playerHealth.OnRespawned -= ResetCampfire;
        }
    }

    void Update()
    {
        if (isUsed || player == null || playerHealth == null) return;

        // 計算玩家與火堆的距離
        float dist = Vector3.Distance(transform.position, player.position);

        // 當玩家靠近範圍內，且玩家血量小於最大血量時觸發
        if (dist <= interactRange)
        {
            UseCampfire();
        }
    }

    void UseCampfire()
    {
        isUsed = true;

        // 計算 20% 的血量
        float healAmount = playerHealth.maxHealth * 0.2f;

        StartCoroutine(HealAndExtinguish(healAmount));
    }

    IEnumerator HealAndExtinguish(float healAmount)
{
    yield return new WaitForSeconds(0.2f);

    // 實際幫玩家回復血量
    if (playerHealth != null)
    {
        playerHealth.Heal(healAmount);
    }
    
    Debug.Log($"篝火生效：為玩家回復了 {healAmount} 點血量！目前的血量為：{playerHealth.Health}");

    // 讓粒子特效停止發射並關閉
    if (fireParticleEffect != null)
    {
        ParticleSystem ps = fireParticleEffect.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            var emission = ps.emission;
            emission.enabled = false;
        }

        yield return new WaitForSeconds(1.5f);
        fireParticleEffect.SetActive(false);
    }
}

    /// <summary>
    /// 玩家重生時：重置營火，恢復火焰特效與可使用狀態
    /// </summary>
    public void ResetCampfire()
    {
        StopAllCoroutines();
        isUsed = false;

        if (fireParticleEffect != null)
        {
            fireParticleEffect.SetActive(true);

            // 重新啟用粒子發射器
            ParticleSystem ps = fireParticleEffect.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var emission = ps.emission;
                emission.enabled = true;
                ps.Clear();
                ps.Play();
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.orange;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}