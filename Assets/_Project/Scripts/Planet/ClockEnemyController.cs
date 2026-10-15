using System.Collections;
using UnityEngine;

/// <summary>
/// 時鐘敵人控制器：限定在指定區域（X: 600~700, Z: 480~560）內才會開始召喚與攻擊
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(ClockEnemyHealth))]
public class ClockEnemyController : MonoBehaviour
{
    [Header("啟動區域設定")]
    [Tooltip("區域 X 軸最小值")]
    public float minX = 600f;
    [Tooltip("區域 X 軸最大值")]
    public float maxX = 700f;
    [Tooltip("區域 Z 軸最小值")]
    public float minZ = 480f;
    [Tooltip("區域 Z 軸最大值")]
    public float maxZ = 560f;

    [Header("召喚時鐘設定")]
    public GameObject clockPrefab;
    public float summonInterval = 4.0f;
    public float summonRange = 6.0f;

    [Header("衝刺與後撤設定")]
    public float dashCooldown = 6.0f;
    public float dashSpeed = 16f;
    public float dashDuration = 0.6f;
    public float dashDamage = 25f;
    [Tooltip("衝刺結束後，要後撤退開的目標安全距離 (m)")]
    public float retreatDistance = 5.0f;
    [Tooltip("後撤移動速度")]
    public float retreatSpeed = 6.0f;

    private Transform player;
    private CharacterController cc;
    private ClockEnemyHealth enemyHealth;
    private bool isActing = false;
    private bool hasActivated = false; // 紀錄是否已經進入過區域並啟動

    void Start()
    {
        cc = GetComponent<CharacterController>();
        enemyHealth = GetComponent<ClockEnemyHealth>();
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    void Update()
    {
        if (enemyHealth.IsDead || player == null) return;

        // 如果還沒啟動過，檢查玩家是否進入指定區域
        if (!hasActivated)
        {
            if (IsPlayerInActivationArea())
            {
                hasActivated = true;
                Debug.Log("玩家已進入指定區域，時鐘敵人啟動攻擊！");
                StartCoroutine(SummonClockRoutine());
                StartCoroutine(CombatLoopRoutine());
            }
        }
    }

    // 檢查玩家座標是否在 X: 600~700, Z: 480~560 範圍內
    bool IsPlayerInActivationArea()
    {
        Vector3 pPos = player.position;
        return pPos.x >= minX && pPos.x <= maxX && pPos.z >= minZ && pPos.z <= maxZ;
    }

    IEnumerator SummonClockRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(summonInterval);

            if (!enemyHealth.IsDead && clockPrefab != null)
            {
                Vector2 randomCircle = Random.insideUnitCircle * summonRange;
                Vector3 spawnPos = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
                Instantiate(clockPrefab, spawnPos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
        }
    }

    IEnumerator CombatLoopRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(dashCooldown);

            if (!enemyHealth.IsDead && player != null && !isActing)
            {
                yield return StartCoroutine(PerformDashAndRetreat());
            }
        }
    }

    // 執行：衝刺 -> 撞擊 -> 自動後退遠離玩家
    IEnumerator PerformDashAndRetreat()
    {
        isActing = true;

        // 1. 轉向玩家並衝刺
        Vector3 dashDir = (player.position - transform.position).normalized;
        dashDir.y = 0f;
        transform.rotation = Quaternion.LookRotation(dashDir);

        float timer = 0f;
        while (timer < dashDuration)
        {
            if (enemyHealth.IsDead) yield break;
            timer += Time.deltaTime;
            cc.Move(dashDir * dashSpeed * Time.deltaTime);
            yield return null;
        }

        // 2. 衝刺結束後，計算「遠離玩家」的方向並自動後退
        Vector3 retreatDir = (transform.position - player.position).normalized;
        retreatDir.y = 0f;
        if (retreatDir.sqrMagnitude < 0.0001f) retreatDir = -transform.forward;

        transform.rotation = Quaternion.LookRotation(-retreatDir);
        float currentRetreatDist = 0f;

        while (currentRetreatDist < retreatDistance)
        {
            if (enemyHealth.IsDead) yield break;

            float step = retreatSpeed * Time.deltaTime;
            cc.Move(retreatDir * step);
            currentRetreatDist += step;

            yield return null;
        }

        isActing = false;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!isActing || enemyHealth.IsDead) return;

        var playerHealth = hit.collider.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            Vector3 knockbackDir = (hit.collider.transform.position - transform.position).normalized;
            playerHealth.TakeDamage(dashDamage, knockbackDir * 5f);
        }
    }
}