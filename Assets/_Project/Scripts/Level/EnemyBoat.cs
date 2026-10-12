using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敵人追擊船：
/// - 當玩家登上 VikingBoat 啟航後，從船尾後方生成。
/// - 每艘船隨機生成 2~5 個敵人站在甲板上，面向玩家並進行遠程攻擊。
/// - 敵船船頭轉向校正預設為 90 度（配合模型外觀軸向）。
/// - 接近時仍有衝撞傷害。
/// - 實作 IDamageable，帶有實體 Collider，可被玩家近戰與遠程劍氣擊中。
/// - 被摧毀時播放視覺破壞效果並沉入水中。
/// </summary>
public class EnemyBoat : MonoBehaviour, IDamageable
{
    [HideInInspector] public Transform targetBoat; // 玩家的 VikingBoat

    // 全體敵艦清單，用於防重疊排斥與群聚分離
    public static readonly List<EnemyBoat> AllEnemyBoats = new List<EnemyBoat>();

    [Header("追擊移動與保留間距")]
    public float chaseSpeed = 11.5f;
    public float turnSpeed = 55f;
    [Tooltip("與玩家船同速保持距離 (公尺)")]
    public float matchSpeedDistance = 30.0f;
    [Tooltip("平滑速度調配緩衝範圍 (公尺)")]
    public float speedBufferRange = 6.0f;

    [Header("近身碰撞防撞排斥")]
    [Tooltip("若過於貼近時的緩衝推力")]
    public float minSafeDistance = 8.5f;
    [Tooltip("敵艦相互防疊穿推力半徑 (公尺)")]
    public float boatSeparationRadius = 25.0f;
    [Tooltip("敵艦相互排斥推力強度")]
    public float separationStrength = 8.0f;

    [Header("敵人船員與遠程攻擊設定")]
    [Tooltip("船上敵人數量最小值")]
    public int minEnemies = 2;
    [Tooltip("船上敵人數量最大值")]
    public int maxEnemies = 5;
    [Tooltip("是否開啟遠程攻擊")]
    public bool enableRangedAttack = true;
    [Tooltip("遠程攻擊射程")]
    public float rangedRange = 40f;
    [Tooltip("每個敵人射擊間隔冷卻 (秒)")]
    public float shootInterval = 2.8f;
    [Tooltip("投射物飛行速度")]
    public float projectileSpeed = 19f;
    [Tooltip("遠程投射物傷害")]
    public float rangedDamage = 10f;

    [Header("生命值")]
    public float maxHealth = 70f;
    float health;
    bool isDead = false;

    [Header("波浪起伏")]
    public float waveFrequency = 1.4f;
    public float waveAmplitude = 0.12f;
    public float pitchAmount = 1.8f;
    public float rollAmount = 2.5f;

    [Header("船首偏移校正")]
    [Tooltip("敵人船頭角度校正，預設為 90 度以配合模型視覺朝向")]
    public float boatForwardOffsetAngle = -90f;

    [Header("血條 UI")]
    public bool showHealthBar = true;
    public float barHeightOffset = 2.8f;
    float lastHitTime = float.NegativeInfinity;
    const float BAR_SHOW_TIME = 6f;

    float currentYaw;
    float baseY;
    Renderer[] renderers;
    bool isAttackingPlayer;
    PlayerHealth playerHealth;
    Transform playerTransform;

    // 船上的多名遠程射手
    class ArcherMember
    {
        public Transform transform;
        public float nextShootTime;
    }
    List<ArcherMember> archers = new List<ArcherMember>();

    public bool IsDead => isDead;

    void Awake()
    {
        if (!AllEnemyBoats.Contains(this)) AllEnemyBoats.Add(this);
        health = maxHealth;
        renderers = GetComponentsInChildren<Renderer>(true);
        currentYaw = transform.eulerAngles.y;
        baseY = transform.position.y;

        // 確保 EnemyBoat 具有可用於射擊/近戰碰撞檢測的 Collider
        var col = GetComponent<Collider>();
        if (col == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(3.5f, 2.2f, 7.5f);
            box.center = new Vector3(0f, 0.8f, 0f);
        }
        else
        {
            col.enabled = true;
        }

        SpawnVisualArchers();
    }

    void Start()
    {
        var playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerHealth = playerObj.GetComponent<PlayerHealth>();
            playerTransform = playerObj.transform;
        }
    }

    /// <summary>
    /// 在敵艦甲板上生成 2~5 名敵人站立
    /// </summary>
    void SpawnVisualArchers()
    {
        int enemyCount = Random.Range(minEnemies, maxEnemies + 1);
        var gruntPrefab = Resources.Load<GameObject>("Prefabs/Enemy_Grunt");

        // 甲板站位槽位 (相對船體中心)
        Vector3[] slotPositions = new Vector3[]
        {
            new Vector3(0f, 0.7f, 1.2f),
            new Vector3(-0.6f, 0.7f, 0.0f),
            new Vector3(0.6f, 0.7f, -0.2f),
            new Vector3(-0.5f, 0.7f, -1.3f),
            new Vector3(0.5f, 0.7f, 1.0f)
        };

        for (int i = 0; i < enemyCount; i++)
        {
            Vector3 localPos = (i < slotPositions.Length)
                ? slotPositions[i]
                : new Vector3(Random.Range(-0.6f, 0.6f), 0.7f, Random.Range(-1.2f, 1.2f));

            GameObject archerObj = null;
            if (gruntPrefab != null)
            {
                archerObj = Instantiate(gruntPrefab, transform);
                // 停用本體邏輯，僅保留外觀
                Destroy(archerObj.GetComponent<EnemyAI>());
                Destroy(archerObj.GetComponent<CharacterController>());
                Destroy(archerObj.GetComponent<ThirdPersonController>());
                Destroy(archerObj.GetComponent<EnemyHealth>());
                Destroy(archerObj.GetComponent<EnemyCombat>());
            }
            else
            {
                archerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                archerObj.transform.SetParent(transform);
                Destroy(archerObj.GetComponent<Collider>());
                archerObj.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            }

            archerObj.name = $"ShipCrew_{i + 1}";
            archerObj.transform.localPosition = localPos;

            var member = new ArcherMember
            {
                transform = archerObj.transform,
                nextShootTime = Time.time + Random.Range(1.0f, 3.0f) // 隨機錯開射擊時間
            };
            archers.Add(member);
        }
    }

    void Update()
    {
        if (isDead || targetBoat == null) return;

        ChaseTarget();
        ApplyWave();
        HandleRangedShooting();
    }

    void ChaseTarget()
    {
        Vector3 toTarget = targetBoat.position - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist > 0.5f)
        {
            Vector3 desiredDir = toTarget.normalized;
            // 依據視覺船頭 boatForwardOffsetAngle 校準轉向目標
            Quaternion desired = Quaternion.LookRotation(desiredDir) * Quaternion.Euler(0f, -boatForwardOffsetAngle, 0f);
            currentYaw = Quaternion.RotateTowards(
                Quaternion.Euler(0f, currentYaw, 0f),
                desired,
                turnSpeed * Time.deltaTime
            ).eulerAngles.y;
        }

        // 取得玩家船的當前航速
        float playerBoatSpeed = 0f;
        var playerBoat = targetBoat != null ? targetBoat.GetComponent<VikingBoat>() : null;
        if (playerBoat != null)
        {
            playerBoatSpeed = Mathf.Max(0f, playerBoat.currentSpeed);
        }

        // 速度控制：距離 > 30 公尺時全速追擊；抵達 30 公尺後與玩家船保持同速巡航
        float currentTargetSpeed = 0f;
        if (dist > matchSpeedDistance + speedBufferRange)
        {
            // 遠距離 (例如 100m ~ 36m)：以 chaseSpeed (11.5 m/s) 全速衝刺追趕拉近距離
            currentTargetSpeed = chaseSpeed;
        }
        else if (dist > matchSpeedDistance)
        {
            // 接近 30 公尺 (36m ~ 30m)：平滑將航速過渡到與玩家船相同速度
            float t = (dist - matchSpeedDistance) / speedBufferRange;
            currentTargetSpeed = Mathf.Lerp(playerBoatSpeed, chaseSpeed, t);
        }
        else if (dist < minSafeDistance)
        {
            // 距玩家過近時微幅倒退/制動，防止碰撞
            currentTargetSpeed = -2.0f;
        }
        else
        {
            // 在 30 公尺附近或進入 30 公尺範圍時：完全與玩家船航速同步
            // 若玩家停船，敵船也會停船；若玩家加速到 9m/s，敵船也維持 9m/s 航速
            currentTargetSpeed = playerBoatSpeed;
        }

        // 計算與其他敵艦的相互碰撞排斥力（確保船隻不疊穿在一起，保持碰撞體感）
        Vector3 separationForce = Vector3.zero;
        foreach (var other in AllEnemyBoats)
        {
            if (other == null || other == this || other.isDead) continue;
            Vector3 diff = transform.position - other.transform.position;
            diff.y = 0f;
            float d = diff.magnitude;
            if (d < boatSeparationRadius && d > 0.01f)
            {
                // 距離越近，互相推開的力道越強（彈性碰撞排斥）
                float push = (1f - d / boatSeparationRadius);
                separationForce += diff.normalized * (push * separationStrength);
            }
        }

        Vector3 moveVelocity = Vector3.zero;
        if (Mathf.Abs(currentTargetSpeed) > 0.05f)
        {
            Vector3 fwd = Quaternion.Euler(0f, currentYaw + boatForwardOffsetAngle, 0f) * Vector3.forward;
            moveVelocity += fwd * currentTargetSpeed;
        }

        // 加上排斥推力位移
        moveVelocity += separationForce;
        transform.position += moveVelocity * Time.deltaTime;

        // 船員轉向面對玩家
        if (playerTransform != null)
        {
            foreach (var member in archers)
            {
                if (member.transform == null) continue;
                Vector3 toPlayer = playerTransform.position - member.transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f)
                    member.transform.rotation = Quaternion.LookRotation(toPlayer);
            }
        }
    }

    void HandleRangedShooting()
    {
        if (!enableRangedAttack || isDead || playerTransform == null) return;
        if (playerHealth != null && playerHealth.IsDead) return;

        float dist = Vector3.Distance(transform.position, playerTransform.position);
        if (dist > rangedRange) return;

        float now = Time.time;
        foreach (var member in archers)
        {
            if (member.transform == null) continue;
            if (now >= member.nextShootTime)
            {
                member.nextShootTime = now + shootInterval + Random.Range(-0.4f, 0.4f);
                ShootProjectileFrom(member.transform);
            }
        }
    }

    void ShootProjectileFrom(Transform crew)
    {
        Vector3 shootOrigin = crew.position + Vector3.up * 1.3f;

        // 計算玩家當前航行移動速度與前進向量，以發射直線前置量瞄準
        Vector3 playerVelocity = Vector3.zero;
        var playerBoat = targetBoat != null ? targetBoat.GetComponent<VikingBoat>() : null;
        if (playerBoat != null && Mathf.Abs(playerBoat.currentSpeed) > 0.1f)
        {
            Vector3 boatFwd = Quaternion.Euler(0f, playerBoat.transform.eulerAngles.y + playerBoat.boatForwardOffsetAngle, 0f) * Vector3.forward;
            playerVelocity = boatFwd * playerBoat.currentSpeed;
        }

        float directDist = Vector3.Distance(shootOrigin, playerTransform.position);
        float flyTime = directDist / Mathf.Max(projectileSpeed, 10f);

        // 前置量目標點（胸口位置 + 航行提前量）
        Vector3 targetPos = (playerTransform.position + Vector3.up * 1.0f) + (playerVelocity * flyTime);

        // 極小瞄準隨機散佈（落在半徑 1 公尺範圍內）
        Vector2 spread = Random.insideUnitCircle * 0.35f;
        targetPos += new Vector3(spread.x, 0f, spread.y);

        Vector3 toTarget = targetPos - shootOrigin;
        float dist = toTarget.magnitude;

        // 直線純物理方向（包含微幅向上仰角補償重力）
        Vector3 flightDir = toTarget.normalized;
        flightDir.y += Mathf.Clamp(dist * 0.012f, 0.03f, 0.18f);
        flightDir.Normalize();

        var projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projObj.name = "BoatProjectile";
        projObj.transform.position = shootOrigin;
        projObj.transform.localScale = Vector3.one * 0.42f;

        var col = projObj.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        var mr = projObj.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = new Color(1.0f, 0.35f, 0.05f); // 亮橘紅火球
            mr.sharedMaterial = mat;
        }

        var proj = projObj.AddComponent<BoatProjectile>();
        proj.damage = rangedDamage;
        proj.speed = projectileSpeed;
        proj.velocity = flightDir * projectileSpeed;

        GameAudio.Play("SFX/RPG/drawKnife1", shootOrigin, 0.7f, 1.3f, 0.05f, 1f, 35f);
    }

    void ApplyWave()
    {
        float t = Time.time;
        Vector3 p = transform.position;
        p.y = baseY + Mathf.Sin(t * waveFrequency) * waveAmplitude;
        transform.position = p;

        float pitch = Mathf.Sin(t * (waveFrequency * 1.2f)) * pitchAmount;
        float roll = Mathf.Cos(t * waveFrequency) * rollAmount;
        transform.rotation = Quaternion.Euler(pitch, currentYaw, roll);
    }



    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (isDead || amount <= 0f) return;
        health = Mathf.Max(0f, health - amount);
        lastHitTime = Time.time;

        StartCoroutine(FlashWhite());

        if (health <= 0f) Die();
    }

    IEnumerator FlashWhite()
    {
        var mpb = new MaterialPropertyBlock();
        int baseColorId = Shader.PropertyToID("_BaseColor");
        int colorId = Shader.PropertyToID("_Color");

        foreach (var r in renderers)
        {
            if (r == null) continue;
            mpb.Clear();
            mpb.SetColor(baseColorId, Color.white);
            mpb.SetColor(colorId, Color.white);
            r.SetPropertyBlock(mpb);
        }
        yield return new WaitForSeconds(0.08f);
        mpb.Clear();
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.SetPropertyBlock(mpb);
        }
    }

    void OnDestroy()
    {
        AllEnemyBoats.Remove(this);
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;
        AllEnemyBoats.Remove(this);

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        GameAudio.Play("SFX/rock_hit", transform.position, 1f, 0.8f, 0.1f, 1f, 40f);
        StartCoroutine(SinkRoutine());
    }

    IEnumerator SinkRoutine()
    {
        Quaternion startRot = transform.rotation;
        Vector3 startPos = transform.position;
        float t = 0f;
        float duration = 2.5f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float k = t / duration;
            transform.rotation = Quaternion.Slerp(startRot, startRot * Quaternion.Euler(0f, 0f, 75f), k);
            Vector3 p = startPos;
            p.y = startPos.y - k * 3.5f;
            transform.position = p;
            yield return null;
        }

        foreach (var r in renderers)
            if (r != null) r.enabled = false;

        Destroy(gameObject, 2f);
    }

    void OnGUI()
    {
        if (!showHealthBar || isDead) return;
        if (Time.time - lastHitTime > BAR_SHOW_TIME && health >= maxHealth) return;

        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * barHeightOffset);
        if (sp.z <= 0f || sp.z > 60f) return;

        const float w = 90f, h = 9f;
        float x = sp.x - w * 0.5f;
        float y = Screen.height - sp.y - h;

        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, h + 2), Texture2D.whiteTexture);
        GUI.color = new Color(0.95f, 0.25f, 0.15f);
        GUI.DrawTexture(new Rect(x, y, w * (health / maxHealth), h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter
        };
        GUI.color = new Color(1f, 0.9f, 0.3f);
        GUI.Label(new Rect(x - 20f, y - 20f, w + 40f, 20f), $"🏹 敵艦 (船員 {archers.Count})", style);
        GUI.color = Color.white;
    }
}
