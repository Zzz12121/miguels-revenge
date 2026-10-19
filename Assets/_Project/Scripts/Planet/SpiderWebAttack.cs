using System.Collections;
using UnityEngine;

/// <summary>
/// 蜘蛛遠程射絲攻擊腳本
/// </summary>
public class SpiderWebAttack : MonoBehaviour
{
    [Header("發射設定")]
    [Tooltip("蜘蛛絲發射點 (如蜘蛛嘴部)")]
    public Transform firePoint;
    [Tooltip("蜘蛛絲/蛛網 Prefab")]
    public GameObject webProjectilePrefab;
    [Tooltip("發射速度")]
    public float launchForce = 20f;
    [Tooltip("射程冷卻時間 (秒)")]
    public float attackCooldown = 3f;
    [Tooltip("最大射程 (m)")]
    public float maxShootDistance = 12f;

    [Header("目標")]
    public Transform target;

    private float nextFireTime;
    private EnemyAI ai;

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        // 蜘蛛巢穴的蜘蛛：從卵裡孵化、被打會鑽地偷襲（想調數值可以手動 Add Component → SpiderBurrow）
        if (ai != null && GetComponent<SpiderBurrow>() == null) gameObject.AddComponent<SpiderBurrow>();

        // 自動尋找標籤為 Player 的目標
        if (target == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    void Update()
    {
        if (target == null || ai == null) return;

        // 僅在進入戰鬥狀態且冷卻完畢時發射
        if (ai.IsEngaged && Time.time >= nextFireTime)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            
            // 距離在射程內且大於近戰距離時發射蜘蛛絲
            if (dist <= maxShootDistance && dist > ai.attackRange)
            {
                ShootWeb();
                nextFireTime = Time.time + attackCooldown;
            }
        }
    }

    /// <summary>
    /// 發射蜘蛛絲投射物
    /// </summary>
    public void ShootWeb()
    {
        if (webProjectilePrefab == null || firePoint == null) return;

        // 計算朝向玩家胸口/中心的目標位置
        Vector3 targetPosition = target.position + Vector3.up * 1.0f;
        Vector3 direction = (targetPosition - firePoint.position).normalized;

        // 生成蜘蛛網物件
        GameObject webGO = Instantiate(webProjectilePrefab, firePoint.position, Quaternion.LookRotation(direction));

        // 攻擊特效：嘴邊噴出一團白絲，蜘蛛絲後面拖著尾巴
        CombatVFX.Puff(firePoint.position, direction, new Color(0.92f, 0.92f, 1f, 0.85f));
        CombatVFX.Trail(webGO.transform, new Color(0.85f, 0.9f, 1f, 0.9f), 0.22f);
        GameAudio.Play("SFX/RPG/cloth3", firePoint.position, 0.8f, 1.4f, 0.1f, 1f, 30f);
        
        // 給予飛行物理速度
        Rigidbody rb = webGO.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = direction * launchForce;
        }
    }
}