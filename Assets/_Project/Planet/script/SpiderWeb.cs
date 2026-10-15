using System.Collections;
using UnityEngine;

/// <summary>
/// 飛行中的蜘蛛絲/蛛網，附帶傷害、擊退以及緩速/定身效果，並會自動修正材質避免粉紅色。
/// </summary>
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public class SpiderWeb : MonoBehaviour
{
    [Header("傷害與擊退")]
    public float damage = 10f;
    public float knockback = 3f;
    public float lifeTime = 4f;

    [Header("纏住與緩速效果")]
    [Tooltip("是否啟用定身（無法移動）；若為 false 則為緩速")]
    public bool enableRoot = false;
    [Tooltip("緩速比例 (例如 0.5 代表移動速度減半)")]
    [Range(0.1f, 0.9f)] public float slowMultiplier = 0.5f;
    [Tooltip("緩速或定身的持續時間 (秒)")]
    public float effectDuration = 2.5f;

    [Header("特效設定")]
    [Tooltip("當玩家被蜘蛛絲纏住/定身時，生成在玩家身上的粒子特效 Prefab")]
    public GameObject webStunEffectPrefab;

    [Header("絲線視覺")]
    public LineRenderer lineRenderer;
    private Vector3 originPoint;
    private static Material defaultWebMat;

    void Start()
    {
        originPoint = transform.position;
        Destroy(gameObject, lifeTime); // 超時自動銷毀

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        if (lineRenderer != null)
        {
            // 如果沒有指定材質，自動建立一個防破圖的相容材質，解決粉紅色問題
            if (lineRenderer.sharedMaterial == null)
            {
                if (defaultWebMat == null)
                {
                    // 嘗試尋找 URP 或內建的 Unlit Shader，作為安全備援
                    var shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Sprites/Default");

                    if (shader != null)
                    {
                        defaultWebMat = new Material(shader);
                        defaultWebMat.SetColor("_BaseColor", Color.white);
                        defaultWebMat.SetColor("_Color", Color.white);
                    }
                }
                if (defaultWebMat != null)
                {
                    lineRenderer.sharedMaterial = defaultWebMat;
                }
            }

            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, originPoint);
            lineRenderer.SetPosition(1, transform.position);
        }
    }

    void Update()
    {
        // 即時繪製從發射起點到目前蛛網位置的絲線
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, originPoint);
            lineRenderer.SetPosition(1, transform.position);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 如果撞到的是蜘蛛自己或其它小怪，直接忽略，不作任何處理[cite: 5]
        if (other.GetComponentInParent<EnemyAI>() != null || other.CompareTag("Enemy"))
            return;

        // 擊中玩家處理[cite: 5]
        var playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null && !playerHealth.IsDead)
        {
            Vector3 hitDir = (other.transform.position - transform.position).normalized;
            hitDir.y = 0.2f;

            // 1. 對玩家造成傷害與少量擊退[cite: 5]
            playerHealth.TakeDamage(damage, hitDir * knockback);

            // 2. 套用緩速或定身效果[cite: 5]
            var playerController = other.GetComponentInParent<ThirdPersonController>();
            if (playerController != null)
            {
                StartCoroutine(ApplyWebEffectRoutine(playerController, other.transform));
            }

            Destroy(gameObject); // 擊中玩家後銷毀網子本體[cite: 5]
        }
        else if (!other.isTrigger)
        {
            // 擊中其他非 Trigger 物件（如牆壁、地板）時銷毀[cite: 5]
            Destroy(gameObject);
        }
    }

    IEnumerator ApplyWebEffectRoutine(ThirdPersonController controller, Transform playerTransform)
    {
        // 記錄原本的速度倍率[cite: 5]
        float originalMultiplier = controller.speedMultiplier;

        // 生成在玩家身上的粒子特效物件
        GameObject activeEffect = null;
        if (webStunEffectPrefab != null)
        {
            // 以玩家為父物件，讓特效跟著玩家移動
            activeEffect = Instantiate(webStunEffectPrefab, playerTransform.position + Vector3.up * 1f, Quaternion.identity, playerTransform);
        }

        if (enableRoot)
        {
            // 定身：將速度倍率歸零[cite: 5]
            controller.speedMultiplier = 0f;
        }
        else
        {
            // 緩速：將速度倍率乘上緩速比例[cite: 5]
            controller.speedMultiplier *= slowMultiplier;
        }

        // 等待持續時間結束[cite: 5]
        yield return new WaitForSeconds(effectDuration);

        // 恢復原本的速度倍率[cite: 5]
        if (controller != null)
        {
            controller.speedMultiplier = originalMultiplier;
        }

        // 銷毀身上的定身特效
        if (activeEffect != null)
        {
            Destroy(activeEffect);
        }
    }
}