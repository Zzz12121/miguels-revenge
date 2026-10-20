using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 時鐘敵人控制器：鎖定平地高度、衝刺面向玩家、二階段四方向保護時鐘與震飛
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(ClockEnemyHealth))]
public class ClockEnemyController : MonoBehaviour
{
    [Header("啟動區域設定")]
    public float minX = 600f;
    public float maxX = 700f;
    public float minZ = 480f;
    public float maxZ = 560f;

    [Header("召喚時鐘設定")]
    public float summonInterval = 4.0f;
    public float phaseTwoSummonInterval = 2.0f;
    public GameObject clockPrefab;
    public float summonRange = 6.0f;

    [Header("衝刺與後撤設定")]
    public float dashSpeed = 16f;
    public float dashDuration = 0.6f;
    public float dashDamage = 25f;
    public float retreatDistance = 5.0f;
    public float retreatSpeed = 6.0f;
    [Tooltip("衝刺時的額外高度補正（若地形會亂飄可設為 0 或負值貼地）")]
    public float dashHeightOffset = 0f;

    [Header("二階段設定 (血量低於 40%)")]
    public float shockwaveForce = 8.0f;
    [Tooltip("二階段四周保護時鐘距離敵人的距離 (m)")]
    public float shieldClockOffset = 2.5f;

    [Header("衝刺預兆")]
    [Tooltip("衝刺前面向玩家、臉上時鐘忽大忽小的時間 (秒)")]
    public float dashTelegraphTime = 0.5f;
    [Tooltip("預兆期間時鐘脹縮幾次")]
    public float telegraphPulses = 3f;
    [Tooltip("時鐘脹縮的幅度（0.35 = 大小在 ±35% 之間變化）")]
    public float telegraphPulseAmount = 0.35f;
    [Tooltip("臉上的時鐘（留空 = 自動找名字叫 Clock 的子物件）")]
    public Transform faceClock;
    Vector3 faceClockScale = Vector3.one;

    [Header("地上的時鐘陷阱")]
    [Tooltip("每隔幾秒在玩家腳下放一個平躺的時鐘（一階段 / 二階段）。0 = 不放")]
    public Vector2 floorTrapInterval = new Vector2(7f, 4.5f);
    [Tooltip("倒數秒數的隨機範圍")]
    public Vector2 floorTrapCountdown = new Vector2(1f, 2f);
    [Tooltip("鐘面基本半徑 (m)：倒數結束時玩家在範圍內就中招")]
    public float floorTrapRadius = 2.2f;
    [Tooltip("每次放置時鐘面再隨機放大的倍率（最小, 最大）")]
    public Vector2 floorTrapScale = new Vector2(2.5f, 3.5f);

    [Header("流沙牆（開戰後封住競技場，打倒 Boss 才消失）")]
    public bool raiseSandWall = true;
    [Tooltip("流沙牆圍住的範圍：左下角 (X, Z)。要把 Boss 房的營火、第二株植物、傳送門都包在裡面，檢查點石碑在外面")]
    public Vector2 sandWallMin = new Vector2(596f, 477f);
    [Tooltip("流沙牆圍住的範圍：右上角 (X, Z)")]
    public Vector2 sandWallMax = new Vector2(704f, 594f);
    BossSandWall sandWall;

    void OnBossDied()
    {
        if (sandWall != null) { sandWall.Collapse(); sandWall = null; }
    }

    void OnDrawGizmosSelected()
    {
        // 黃框 = 開戰範圍，沙色框 = 流沙牆
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(new Vector3((minX + maxX) * 0.5f, transform.position.y, (minZ + maxZ) * 0.5f), new Vector3(maxX - minX, 2f, maxZ - minZ));
        if (raiseSandWall) BossSandWall.DrawGizmo(sandWallMin, sandWallMax, transform.position.y);
    }

    [Header("檢查點")]
    [Tooltip("Boss 戰開始時自動啟用的檢查點（石柱）。玩家死在 Boss 戰中會在這裡復活，Boss 回滿血重來；Boss 被打死後就不會再復活")]
    public Checkpoint fightCheckpoint;

    private Transform player;
    private PlayerHealth playerHealth;
    private CharacterController cc;
    private ClockEnemyHealth enemyHealth;
    private bool isActing = false;
    private bool hasActivated = false;
    private bool phaseTwoTriggered = false; // 確保二階段四方時鐘只在進入時生成一次
    private Vector3 homePos;
    private Quaternion homeRot;
    private readonly List<GameObject> spawnedClocks = new List<GameObject>();

    void Start()
    {
        cc = GetComponent<CharacterController>();
        enemyHealth = GetComponent<ClockEnemyHealth>();
        homePos = transform.position;
        homeRot = transform.rotation;
        if (faceClock == null) faceClock = transform.Find("Clock");
        if (faceClock != null) faceClockScale = faceClock.localScale;
        playerHealth = PlayerHealth.Find();
        if (playerHealth != null)
        {
            player = playerHealth.transform;
            playerHealth.OnRespawned += ResetFight;
        }

        enemyHealth.OnEnterPhaseTwo += OnEnterPhaseTwoHandler;
        enemyHealth.OnDied += OnBossDied;
    }

    void OnDestroy()
    {
        if (enemyHealth != null)
        {
            enemyHealth.OnEnterPhaseTwo -= OnEnterPhaseTwoHandler;
            enemyHealth.OnDied -= OnBossDied;
        }
        if (playerHealth != null) playerHealth.OnRespawned -= ResetFight;
    }

    bool skillAirborne;
    public void SetSkillAirborne(bool held)
    {
        if (held == skillAirborne) return;
        skillAirborne = held;
        if (held)
        {
            StopAllCoroutines(); isActing = false;
            if (faceClock) faceClock.localScale = faceClockScale;
        }
        else if (hasActivated && enemyHealth && !enemyHealth.IsDead && isActiveAndEnabled)
        {
            StartCoroutine(SummonClockRoutine()); StartCoroutine(CombatLoopRoutine()); StartCoroutine(FloorTrapRoutine());
        }
    }

    void SpawnClock(Vector3 pos)
    {
        var clock = Instantiate(clockPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        spawnedClocks.Add(clock);
    }

    /// <summary>玩家死在 Boss 戰中重生：Boss 還活著的話回滿血、回到原位、清掉召喚出來的時鐘，等玩家再走進區域重新開戰</summary>
    void ResetFight()
    {
        if (enemyHealth == null || enemyHealth.IsDead || !hasActivated) return;
        StopAllCoroutines();
        foreach (var c in spawnedClocks) if (c != null) Destroy(c);
        spawnedClocks.Clear();
        if (sandWall != null) { sandWall.Remove(); sandWall = null; } // 下次開戰再升起

        isActing = false;
        hasActivated = false;
        phaseTwoTriggered = false;
        enemyHealth.HealFull();
        if (faceClock != null) faceClock.localScale = faceClockScale; // 預兆做到一半被中斷
        enemyHealth.ShowBar = false;

        cc.enabled = false; // CharacterController 要先關掉才能直接傳送
        transform.SetPositionAndRotation(homePos, homeRot);
        cc.enabled = true;
    }

    void Update()
    {
        if (enemyHealth.IsDead || player == null) return;

        if (!hasActivated)
        {
            if (IsPlayerInActivationArea() && (playerHealth == null || !playerHealth.IsDead))
            {
                hasActivated = true;
                if (fightCheckpoint != null) fightCheckpoint.Activate();
                if (raiseSandWall) sandWall = BossSandWall.Raise(sandWallMin, sandWallMax);
                enemyHealth.ShowBar = true;
                Debug.Log("玩家已進入指定區域，時鐘 Boss 戰開始！");
                StartCoroutine(SummonClockRoutine());
                StartCoroutine(CombatLoopRoutine());
                StartCoroutine(FloorTrapRoutine());
            }
        }
    }

    bool IsPlayerInActivationArea()
    {
        Vector3 pPos = player.position;
        return pPos.x >= minX && pPos.x <= maxX && pPos.z >= minZ && pPos.z <= maxZ;
    }

    IEnumerator SummonClockRoutine()
    {
        while (true)
        {
            float currentInterval = enemyHealth.IsPhaseTwo ? phaseTwoSummonInterval : summonInterval;
            yield return new WaitForSeconds(currentInterval);

            if (!enemyHealth.IsDead && clockPrefab != null)
            {
                Vector2 randomCircle = Random.insideUnitCircle * summonRange;
                Vector3 spawnPos = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
                SpawnClock(spawnPos);
            }
        }
    }

    // 當進入二階段時觸發：震飛玩家 + 在自身四周生成四方向保護時鐘
    void OnEnterPhaseTwoHandler()
    {
        if (phaseTwoTriggered || player == null) return;
        phaseTwoTriggered = true;

        // 1. 震飛玩家
        var playerController = player.GetComponent<ThirdPersonController>();
        if (playerController != null)
        {
            Vector3 pushDir = (player.position - transform.position).normalized;
            pushDir.y = 0.3f;
            playerController.AddImpulse(pushDir * shockwaveForce + Vector3.up * 4f);
        }

        // 2. 在自身四周（前後左右四個方向）生成保護時鐘
        SpawnShieldClocks();
        Debug.Log("【二階段爆發】Boss 釋放震波，並在身邊展開四方向保護時鐘！");
    }

    void SpawnShieldClocks()
    {
        if (clockPrefab == null) return;

        Vector3 center = transform.position;
        float d = shieldClockOffset;

        // 四個方向：前、後、左、右
        Vector3[] directions = new Vector3[]
        {
            transform.forward * d,
            -transform.forward * d,
            -transform.right * d,
            transform.right * d
        };

        foreach (var dir in directions)
        {
            Vector3 spawnPos = center + dir;
            spawnPos.y = center.y; // 保持與敵人同高度
            SpawnClock(spawnPos);
        }
    }

    IEnumerator CombatLoopRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(4.0f); // 衝刺冷卻

            if (!enemyHealth.IsDead && player != null && !isActing)
            {
                yield return StartCoroutine(PerformDashAndRetreat());
            }
        }
    }

    /// <summary>衝刺前的預兆：轉向玩家，臉上的時鐘忽大忽小 dashTelegraphTime 秒</summary>
    IEnumerator DashTelegraph()
    {
        Vector3 baseScale = faceClockScale;
        GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.9f, 0.8f, 0f, 1f, 40f);

        float t = 0f;
        while (t < dashTelegraphTime)
        {
            if (enemyHealth.IsDead || player == null) break;
            t += Time.deltaTime;

            // 轉向玩家（平滑轉過去，最後一刻剛好對準）
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 720f * Time.deltaTime);

            // 臉上的時鐘忽大忽小
            if (faceClock != null)
                faceClock.localScale = baseScale * (1f + Mathf.Sin(t / dashTelegraphTime * Mathf.PI * 2f * telegraphPulses) * telegraphPulseAmount);
            yield return null;
        }
        if (faceClock != null) faceClock.localScale = baseScale;
    }

    /// <summary>定期在玩家腳下放一個平躺的時鐘，倒數 1~2 秒，結束時還站在上面就被定身</summary>
    IEnumerator FloorTrapRoutine()
    {
        while (true)
        {
            float interval = enemyHealth.IsPhaseTwo ? floorTrapInterval.y : floorTrapInterval.x;
            if (interval <= 0f) yield break;
            yield return new WaitForSeconds(interval);

            if (enemyHealth.IsDead || clockPrefab == null || player == null) continue;
            if (playerHealth != null && playerHealth.IsDead) continue;
            float countdown = Random.Range(floorTrapCountdown.x, floorTrapCountdown.y);
            float radius = floorTrapRadius * Random.Range(floorTrapScale.x, floorTrapScale.y);
            var trap = ClockTrap.SpawnFloorTrap(clockPrefab, player.position, radius, countdown);
            spawnedClocks.Add(trap.gameObject);
        }
    }

    IEnumerator PerformDashAndRetreat()
    {
        isActing = true;

        // 0. 預兆：面向玩家、臉上的時鐘忽大忽小，之後才照原本的方式衝刺
        yield return StartCoroutine(DashTelegraph());
        if (enemyHealth.IsDead || player == null) { isActing = false; yield break; }

        // 1. 記錄剛開始衝刺時的方向（只鎖定一次，之後衝刺途中不會再跟著玩家轉向）
        Vector3 dashDir = (player.position - transform.position);
        dashDir.y = dashHeightOffset;
        dashDir = dashDir.normalized;

        if (dashDir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(new Vector3(dashDir.x, 0f, dashDir.z));
        }

        float timer = 0f;
        while (timer < dashDuration)
        {
            if (enemyHealth.IsDead || player == null) yield break;
            timer += Time.deltaTime;

            // 2. 衝刺過程中，直接朝著一開始鎖定的方向直線前進，不再每一幀更新旋轉
            cc.Move(dashDir * dashSpeed * Time.deltaTime);
            yield return null;
        }

        // === 新增：檢查衝刺結束時的 Y 高度是否大於 5 ===
        // === 新增：檢查衝刺結束時的 Y 高度是否大於 5 ===
        if (transform.position.y > 4f)
        {
            Debug.Log("【Boss 落地震擊】衝刺高度過高，開始回歸地面！");
            
            // 讓 Boss 以協程方式帶重力往下掉，直到碰到地面
            yield return StartCoroutine(FallToGroundRoutine());

            // 落地瞬間震開周遭玩家
            TriggerLandingShockwave();
        }

        // 3. 如果是二階段，衝刺後會在玩家與敵人之間放一排時鐘
        if (enemyHealth.IsPhaseTwo && clockPrefab != null)
        {
            SpawnClocksBetweenEnemyAndPlayer();
        }

        // 4. 自動後退遠離玩家
        Vector3 retreatDir = (transform.position - player.position);
        retreatDir.y = 0f;
        retreatDir = retreatDir.normalized;
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

    // 讓 Boss 帶重力下落直到觸地
    IEnumerator FallToGroundRoutine()
    {
        float fallSpeed = 0f;
        float gravity = 25f;

        // 持續往下掉，直到 CharacterController 偵測到落地（isGrounded）或超過最大下落時間
        float timeout = 2.0f;
        float elapsed = 0f;

        while (!cc.isGrounded && elapsed < timeout)
        {
            if (enemyHealth.IsDead) yield break;
            
            elapsed += Time.deltaTime;
            fallSpeed += gravity * Time.deltaTime;

            // 往下移動
            Vector3 fallMove = new Vector3(0f, -fallSpeed * Time.deltaTime, 0f);
            cc.Move(fallMove);

            yield return null;
        }
    }

    // 落地震開周遭玩家的輔助方法
    void TriggerLandingShockwave()
    {
        // 攻擊特效：地面擴散一圈金色時光波
        CombatVFX.Ring(transform.position + Vector3.down * 0.9f, new Color(1f, 0.8f, 0.3f), 5f);
        GameAudio.Play("SFX/rock_hit", transform.position, 0.9f, 1.2f, 0.05f, 1f, 40f);
        if (player == null) return;

        // 判斷玩家是否在落地震波的有效半徑內（例如 5 公尺內）
        float shockwaveRadius = 5.0f;
        float distToPlayer = Vector3.Distance(transform.position, player.position);

        if (distToPlayer <= shockwaveRadius)
        {
            var playerController = player.GetComponent<ThirdPersonController>();
            if (playerController != null)
            {
                Vector3 pushDir = (player.position - transform.position).normalized;
                pushDir.y = 0.4f; // 稍微帶點向上斜角
                playerController.AddImpulse(pushDir * shockwaveForce + Vector3.up * 3f);
            }
        }
    }
    void SpawnClocksBetweenEnemyAndPlayer()
    {
        if (clockPrefab == null || player == null) return;

        Vector3 start = transform.position;
        Vector3 end = player.position;
        Vector3 direction = (end - start).normalized;
        float distance = Vector3.Distance(start, end);

        int numClocks = Mathf.FloorToInt(distance / 2.0f); // 每隔 2 公尺生成一個時鐘
        for (int i = 1; i <= numClocks; i++)
        {
            Vector3 spawnPos = start + direction * (i * 2.0f);
            spawnPos.y = start.y; // 保持與敵人同高度
            SpawnClock(spawnPos);
            CombatVFX.Impact(spawnPos, new Color(1f, 0.82f, 0.35f));
        }
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
