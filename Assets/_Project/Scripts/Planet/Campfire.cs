using System.Collections;
using UnityEngine;

/// <summary>
/// 篝火互動：玩家靠近時顯示「[E] 使用營火」，按下 E 後回復 40% 血量，回復完畢後粒子特效消失。
/// 滿血時提示會變灰，按了也不會用掉。
/// 用過的火堆在玩家死亡重生後不會恢復，只有開新的一局（重新載入關卡）才會重置。
/// </summary>
public class Campfire : MonoBehaviour
{
    [Header("設定")]
    [Tooltip("玩家靠近互動的範圍 (m)")]
    public float interactRange = 2.5f;
    [Tooltip("回復最大生命的比例")]
    [Range(0f, 1f)] public float healRatio = 0.4f;
    [Tooltip("勾選 = 玩家死亡重生時火堆恢復燃燒；不勾 = 用過就沒了，直到開新的一局")]
    public bool resetOnPlayerRespawn = false;
    [Tooltip("火堆的粒子特效物件 (Particle System)")]
    public GameObject fireParticleEffect;

    Transform player;
    PlayerHealth playerHealth;
    bool isUsed = false; // 紀錄是否已被使用

    void Start()
    {
        playerHealth = PlayerHealth.Find();
        if (playerHealth != null)
        {
            player = playerHealth.transform;
            if (resetOnPlayerRespawn) playerHealth.OnRespawned += ResetCampfire;
        }
        else
        {
            Debug.LogWarning("[Campfire] 場景裡找不到玩家（PlayerHealth）！");
        }

        ResetCampfire();
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnRespawned -= ResetCampfire;
    }

    void Update()
    {
        if (isUsed || player == null || playerHealth == null || playerHealth.IsDead) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > interactRange) return;

        // 確實缺血時才能用，滿血時提示變灰
        bool hurt = playerHealth.Health < playerHealth.maxHealth;
        InteractPrompt.Offer(this, dist, hurt ? $"使用營火（回復 {healRatio * 100f:0}% 血量）" : "營火（血量已滿）", hurt);
        if (InteractPrompt.Pressed(this)) UseCampfire();
    }

    void UseCampfire()
    {
        isUsed = true;
        float healAmount = playerHealth.maxHealth * healRatio;
        StartCoroutine(HealAndExtinguish(healAmount));
    }

    IEnumerator HealAndExtinguish(float healAmount)
    {
        yield return new WaitForSeconds(0.2f);

        if (playerHealth != null)
        {
            playerHealth.Heal(healAmount);
            CombatVFX.Heal(playerHealth.transform);
        }

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

    public void ResetCampfire()
    {
        StopAllCoroutines();
        isUsed = false;

        if (fireParticleEffect != null)
        {
            fireParticleEffect.SetActive(true);

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
