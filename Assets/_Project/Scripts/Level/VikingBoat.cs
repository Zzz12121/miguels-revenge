using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 維京長船互動與駕駛系統：
/// 1. 擊敗所有敵人後解鎖搭船。
/// 2. 玩家靠近船隻按 [E] 鍵登船掌舵。
/// 3. 玩家可親自駕駛長船（W 前進、S 倒退、A/D 轉向）。
/// 4. 畫面上顯示航向目標導引點 HUD 與操作提示。
/// 5. 駕駛船隻進入指定外海目的地範圍後，平滑淡出全黑並顯示進入下一關。
/// </summary>
public class VikingBoat : MonoBehaviour
{
    public enum BoatState
    {
        Locked,         // 敵人未全滅，不可啟航
        ReadyToBoard,   // 敵人已全滅，等待玩家登船
        Driving,        // 玩家登船掌舵，自由駕駛中
        Transitioning   // 抵達目標地點，漸層淡出換關中
    }

    [Header("Debug 快速測試")]
    [Tooltip("勾選後一按下 Play 就會直接將玩家傳送到船上並進入駕駛狀態！")]
    public bool autoBoardOnStart = false;

    [Header("狀態設定")]
    public BoatState currentState = BoatState.Locked;
    [Tooltip("登船互動半徑 (公尺)")]
    public float interactionRadius = 6.5f;
    [Tooltip("指定要去的場景名稱；留空 = 依 GameManager 的關卡順序前往下一關")]
    public string nextSceneName = "";

    [Header("玩家站立點與朝向")]
    [Tooltip("玩家站在船上的位置，留空會自動抓船隻中心")]
    public Transform playerStandingPoint;
    [Tooltip("如果玩家站立點有旋轉，是否直接跟隨站立點的旋轉方向；若為 false 則使用船隻朝向")]
    public bool useStandingPointRotation = true;
    [Tooltip("手動微調玩家登船後的面向角度偏差 (度，如 90、-90、180)")]
    public float playerFacingOffsetAngle = -90f;

    [Header("自由駕駛控制參數")]
    [Tooltip("船隻視覺船頭的偏轉角度校正（預設 90 度，讓 W 前進永遠朝著視覺龍頭/船首方向推進）")]
    public float boatForwardOffsetAngle = 0f;
    [Tooltip("最大前進航速 (m/s)")]
    public float forwardSpeed = 9.0f;
    [Tooltip("最大倒退航速 (m/s)")]
    public float reverseSpeed = 3.5f;
    [Tooltip("轉向速度 (deg/s)")]
    public float steeringTurnSpeed = 42.0f;
    [Tooltip("航速加減速平滑度")]
    public float acceleration = 6.0f;

    [Header("不能開上陸地")]
    [Tooltip("船頭到船中心的距離 (m)：從這裡往下探測水深")]
    public float hullHalfLength = 4.5f;
    [Tooltip("船身半寬 (m)：船頭左右兩側也會探測")]
    public float hullHalfWidth = 1.4f;
    [Tooltip("船底至少要比海平面低多少 (m)。海底（或陸地、碼頭）比這個高就算擱淺，不能再往前")]
    public float minWaterDepth = 0.5f;

    [Header("目的地設定")]
    [Tooltip("駛向的目的地座標（外海開闊航道）")]
    public Vector3 destinationPoint = new Vector3(260f, 1.3f, 120f);
    [Tooltip("進入目的地的觸發半徑")]
    public float destinationRadius = 16.0f;

    [Header("隨海浪起伏（海面高度由 SeaWaves 依 Water 的波浪算出，浪的大小改 WaterSurfaceData.asset）")]
    [Tooltip("船身傾斜程度：1 = 完全照海面坡度，越小越平穩")]
    [Range(0f, 2f)] public float waveTiltScale = 1f;
    [Tooltip("船身跟上海面的速度：越大越跟手，越小越慢、越滑順")]
    public float waveFollowSmooth = 8f;
    [Tooltip("船身吃水：船的原點比海面高多少 (m)。負的 = 往下沉一點，船身邊緣壓進水裡，看不到船和水之間的縫")]
    public float waveDraft = -0.2f;

    [Header("特效與提示")]
    [Tooltip("船上未解鎖 / 解鎖時的指示標記物件（例如金色光柱）")]
    public GameObject objectiveIndicator;

    [Header("敵人追擊船")]
    [Tooltip("敵人追擊船的船體 Prefab（留空時會用玩家船相同的 pf_build_boat_01 複製）")]
    public GameObject enemyBoatPrefab;
    [Tooltip("要生成幾艘敵艦（0 = 不生成）")]
    public int enemyBoatCount = 3;
    [Tooltip("開船出發多少公尺後才在海上觸發敵艦追擊")]
    public float triggerPursuitDistance = 15.0f;
    [Tooltip("敵艦出現時離玩家船後方多遠（公尺）")]
    public float enemyBoatSpawnDistance = 100f;
    [Tooltip("敵艦追擊速度（m/s）")]
    public float enemyBoatChaseSpeed = 11.5f;
    [Tooltip("敵艦血量")]
    public float enemyBoatHealth = 80f;
    [Tooltip("敵艦船首偏移（與 boatForwardOffsetAngle 保持一致）")]
    public float enemyBoatForwardOffset = 90f;
    [Tooltip("離終點剩多少公尺時，終點附近出現增援敵艦")]
    public float reinforcementDistance = 50f;
    [Tooltip("增援敵艦數量（0 = 不增援）")]
    public int reinforcementCount = 3;

    // 運行期私有變數
    GameObject playerObj;
    public float currentSpeed = 0f;
    float currentYaw = 0f;
    float baseWaterY;
    float fadeAlpha = 0f;
    bool isTransitioning = false;
    string lockedReason = "村莊內仍有湧出的敵人，請先擊潰所有敵人才能啟航！";
    Vector3 boardedStartPosition;
    bool hasTriggeredPursuit = false;
    bool hasTriggeredReinforcement = false;
    float reinforcementMessageTime = float.NegativeInfinity;
    Vector3 wavePose; // 平滑後的 (高度, 俯仰, 翻滾)

    void Awake()
    {
        // 確保場景即使先前序列化為 0 或 1，運行時也強制保證 3 艘敵艦追擊
        if (enemyBoatCount < 3) enemyBoatCount = 3;
        if (Mathf.Abs(enemyBoatForwardOffset) < 1f) enemyBoatForwardOffset = -90f;
        baseWaterY = 1.3f; // 維京村莊海平面高度為 y = 1.3
        currentYaw = transform.eulerAngles.y;
        Vector3 p = transform.position;
        p.y = baseWaterY;
        transform.position = p;
        wavePose = new Vector3(baseWaterY + waveDraft, 0f, 0f);
    }

    void Start()
    {
        UpdateIndicator();

        if (autoBoardOnStart)
        {
            StartCoroutine(AutoBoardRoutine());
        }
    }

    IEnumerator AutoBoardRoutine()
    {
        // 等候 1 幀讓玩家等物件皆生成完畢
        yield return null;
        if (playerObj == null) playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            Debug.Log("[VikingBoat] 【Debug 模式】自動登船駕駛！");
            BoardAndDrive(playerObj);
        }
    }

    /// <summary>
    /// 由 EnemyWaveManager 呼叫：所有敵人清除完畢，解鎖搭船
    /// </summary>
    public void UnlockBoat()
    {
        if (currentState == BoatState.Locked)
        {
            currentState = BoatState.ReadyToBoard;
            UpdateIndicator();
            Debug.Log("[VikingBoat] 敵人已清除，港口長船已解鎖！");
        }
    }

    void UpdateIndicator()
    {
        if (objectiveIndicator != null)
        {
            objectiveIndicator.SetActive(currentState == BoatState.ReadyToBoard);
        }
    }

    void Update()
    {
        switch (currentState)
        {
            case BoatState.Locked:
            case BoatState.ReadyToBoard:
                CheckPlayerInteraction();
                ApplyIdleFloating();
                break;

            case BoatState.Driving:
                UpdatePlayerDriving();
                CheckDestinationReached();
                break;

            case BoatState.Transitioning:
                // 淡出過場時保持朝視覺船首微幅前進慣性
                Vector3 transFwd = Quaternion.Euler(0f, currentYaw + boatForwardOffsetAngle, 0f) * Vector3.forward;
                transform.position += transFwd * (Mathf.Max(currentSpeed, 4f) * 0.5f) * Time.deltaTime;
                ApplyWaveFloating(currentSpeed);
                break;
        }
    }

    void CheckPlayerInteraction()
    {
        if (playerObj == null)
        {
            playerObj = GameObject.FindWithTag("Player");
            if (playerObj == null) return;
        }

        float dist = Vector3.Distance(transform.position, playerObj.transform.position);
        if (dist <= interactionRadius)
        {
            bool interactPressed = false;
            var kb = Keyboard.current;
            if (kb != null) interactPressed = kb.eKey.wasPressedThisFrame;
            if (!interactPressed)
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                interactPressed = Input.GetKeyDown(KeyCode.E);
#endif
            }

            if (interactPressed)
            {
                if (currentState == BoatState.ReadyToBoard)
                {
                    BoardAndDrive(playerObj);
                }
            }
        }
    }

    /// <summary>
    /// 玩家登船並掌舵
    /// </summary>
    public void BoardAndDrive(GameObject player)
    {
        currentState = BoatState.Driving;
        UpdateIndicator();

        // 1. 停用角色自身的移動腳本與碰撞體
        var tpc = player.GetComponent<ThirdPersonController>();
        if (tpc != null)
        {
            tpc.inputEnabled = false;
            tpc.enabled = false; // 直接停用移動腳本
        }

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        var combat = player.GetComponent<PlayerCombat>();
        if (combat != null)
        {
            combat.enabled = true;
            combat.bowMode = true; // 登船後改用弓箭：右鍵瞄準、左鍵射箭，射倒敵艦上的弓箭手
        }

        // 2. 將角色固定在船隻掌舵站立點上，並依據站立點或設定角度精確朝向船頭前方
        player.transform.SetParent(transform);
        if (playerStandingPoint != null)
        {
            player.transform.position = playerStandingPoint.position;
            Quaternion targetRot = useStandingPointRotation
                ? playerStandingPoint.localRotation * Quaternion.Euler(0f, playerFacingOffsetAngle, 0f)
                : Quaternion.Euler(0f, playerFacingOffsetAngle, 0f);
            player.transform.localRotation = targetRot;
        }
        else
        {
            player.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            player.transform.localRotation = Quaternion.Euler(0f, playerFacingOffsetAngle, 0f);
        }

        // 3. 調整攝影機：將攝影機解除階層依附，平滑拉開至航海視角，跟隨長船，視角自動對準船頭正前方海面
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.transform.SetParent(null); // 解除 PlayerRig 階層限制，避免被原主角位置束縛
            if (mainCam.TryGetComponent(out ThirdPersonCamera camScript))
            {
                camScript.target = transform;
                camScript.distance = 13f;
                camScript.minDistance = 6f;
                camScript.maxDistance = 24f;
                camScript.targetOffset = new Vector3(0f, 3.0f, 0f);
                camScript.yaw = currentYaw + boatForwardOffsetAngle; // 鏡頭自動對準視覺船首正前方海面！
                camScript.pitch = 14f;
                camScript.SnapToTarget(); // 立即將鏡頭瞬移至長船後方，防止平滑過渡被陸地建物卡住
            }
        }

        // 4. 記錄啟航初始位置，等待玩家真正開船駛向外海後再生成敵艦追擊
        boardedStartPosition = transform.position;
        hasTriggeredPursuit = false;
        hasTriggeredReinforcement = false;

        GameAudio.Play("SFX/RPG/drawSword", transform.position, 1f, 0.9f);
        Debug.Log("[VikingBoat] 玩家已登船掌舵！可用 W/S 前進倒退，A/D 轉向！");
    }

    /// <summary>
    /// 延遲生成追擊敵艦，從玩家船尾後方出現（後方是陸地的話往玩家這邊找一個水夠深的位置）
    /// </summary>
    System.Collections.IEnumerator SpawnEnemyBoatsDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);

        // 計算船尾方向（視覺船首的反方向）
        Vector3 bowDir = Quaternion.Euler(0f, currentYaw + boatForwardOffsetAngle, 0f) * Vector3.forward;
        Vector3 sternDir = -bowDir;
        // 垂直於航向的橫向向量（船與船左右展開）
        Vector3 sternRight = Vector3.Cross(Vector3.up, sternDir).normalized;

        for (int i = 0; i < enemyBoatCount; i++)
        {
            // 基礎位置在身後 enemyBoatSpawnDistance 公尺處，每艘船之間間隔 30 公尺（呈左右展開陣型）
            float lateralOffset = (i - (enemyBoatCount - 1) * 0.5f) * 30.0f;
            Vector3 wanted = transform.position + sternDir * enemyBoatSpawnDistance + sternRight * lateralOffset;
            if (FindOpenWater(wanted, transform.position, out var spawnPos))
                SpawnEnemyBoat(spawnPos, $"EnemyBoat_{i + 1}");
            else
                Debug.LogWarning($"[VikingBoat] 船尾後方找不到夠深的海面，少生成一艘敵艦");
        }
    }

    /// <summary>離終點剩 reinforcementDistance 公尺：終點另一側出現增援敵艦，迎面攔截</summary>
    void SpawnReinforcements()
    {
        Vector3 toDest = destinationPoint - transform.position;
        toDest.y = 0f;
        Vector3 dir = toDest.sqrMagnitude > 0.01f ? toDest.normalized
                    : Quaternion.Euler(0f, currentYaw + boatForwardOffsetAngle, 0f) * Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        Vector3 centre = new Vector3(destinationPoint.x, baseWaterY, destinationPoint.z) + dir * 35f;

        int spawned = 0;
        for (int i = 0; i < reinforcementCount; i++)
        {
            float lateral = (i - (reinforcementCount - 1) * 0.5f) * 25f;
            // 找不到水就往終點 / 玩家這邊靠過來找
            if (FindOpenWater(centre + right * lateral, transform.position, out var pos))
            {
                var eb = SpawnEnemyBoat(pos, $"EnemyBoat_Reinforcement_{i + 1}");
                if (eb != null)
                {
                    // 一出現就面向玩家
                    Vector3 face = transform.position - pos;
                    face.y = 0f;
                    eb.FaceTowards(face);
                    spawned++;
                }
            }
        }
        if (spawned == 0) return;
        reinforcementMessageTime = Time.time;
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 1f, 0.6f, 0f, 1f, 80f);
        Debug.Log($"[VikingBoat] 離終點 {reinforcementDistance} 公尺：{spawned} 艘增援敵艦出現！");
    }

    /// <summary>從 wanted 往 towards 每 10 公尺找一次，找到放得下一艘船的海面就回傳</summary>
    bool FindOpenWater(Vector3 wanted, Vector3 towards, out Vector3 result)
    {
        wanted.y = baseWaterY;
        towards.y = baseWaterY;
        float dist = Vector3.Distance(wanted, towards);
        for (float d = 0f; d < dist - 15f; d += 10f)
        {
            Vector3 p = Vector3.MoveTowards(wanted, towards, d);
            if (BoatNavigation.IsOpenWater(p, hullHalfLength, hullHalfWidth, baseWaterY, minWaterDepth))
            {
                result = p;
                return true;
            }
        }
        result = wanted;
        return false;
    }

    /// <summary>在 spawnPos 生成一艘追擊玩家的敵艦</summary>
    EnemyBoat SpawnEnemyBoat(Vector3 spawnPos, string boatName)
    {
        spawnPos.y = baseWaterY; // 固定在海平面
        GameObject boatObj = null;

        // 優先用指定 prefab；其次複製玩家自己的船體 mesh
        if (enemyBoatPrefab != null)
        {
            boatObj = Instantiate(enemyBoatPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            // 從 Resources 載入相同船體
            var bPrefab = Resources.Load<GameObject>("Prefabs/pf_build_boat_01");
            if (bPrefab != null)
            {
                boatObj = Instantiate(bPrefab, spawnPos, Quaternion.identity);
                VikingBoat.SetStaticRecursively(boatObj, false);
                foreach (var col in boatObj.GetComponentsInChildren<Collider>())
                    col.enabled = false;
            }
            else
            {
                // 最後退路：建立一個簡單的 Cube 當佔位
                boatObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                boatObj.transform.localScale = new Vector3(3f, 1f, 7f);
                boatObj.transform.position = spawnPos;
                Destroy(boatObj.GetComponent<Collider>());
                var mr = boatObj.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                    mat.color = new Color(0.6f, 0.25f, 0.1f);
                    mr.sharedMaterial = mat;
                }
            }
        }

        if (boatObj == null) return null;
        boatObj.name = boatName;

        // 附加 EnemyBoat 腳本：船身大小、水深限制、海浪起伏都和玩家的船一樣
        var eb = boatObj.AddComponent<EnemyBoat>();
        eb.targetBoat = this.transform;
        eb.chaseSpeed = enemyBoatChaseSpeed;
        eb.maxHealth  = enemyBoatHealth;
        eb.boatForwardOffsetAngle = enemyBoatForwardOffset;
        eb.hullHalfLength = hullHalfLength;
        eb.hullHalfWidth = hullHalfWidth;
        eb.minWaterDepth = minWaterDepth;
        eb.waterY = baseWaterY;
        eb.waveTiltScale = waveTiltScale * 0.9f;
        eb.waveFollowSmooth = waveFollowSmooth;
        eb.waveDraft = waveDraft;

        Debug.Log($"[VikingBoat] 敵艦 {boatObj.name} 已在 {spawnPos} 生成，正在追擊玩家！");
        return eb;
    }

    /// <summary>
    /// 玩家親自駕駛長船
    /// </summary>
    void UpdatePlayerDriving()
    {
        float dt = Time.deltaTime;

        // 讀取鍵盤輸入（同時支援 New Input System 與 Legacy Input Fallback）
        float moveInput = 0f;
        float turnInput = 0f;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) moveInput -= 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) moveInput += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turnInput += 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turnInput -= 1f;
        }
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Mathf.Abs(moveInput) < 0.01f) moveInput = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(turnInput) < 0.01f) turnInput = Input.GetAxisRaw("Horizontal");
#endif

        // 目標航速運算
        float targetSpeed = 0f;
        if (moveInput > 0f) targetSpeed = moveInput * forwardSpeed;
        else if (moveInput < 0f) targetSpeed = moveInput * reverseSpeed;

        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * dt);

        // 轉向控制：不論停船或前進中，轉向皆靈敏可用
        if (Mathf.Abs(turnInput) > 0.05f)
        {
            float steerDir = currentSpeed < -0.1f ? -1f : 1f;
            currentYaw += turnInput * steeringTurnSpeed * dt * steerDir;
        }

        // 前進推進（依 currentYaw 與 boatForwardOffsetAngle 計算精確的視覺船首方向）
        Vector3 forwardDir = Quaternion.Euler(0f, currentYaw + boatForwardOffsetAngle, 0f) * Vector3.forward;
        Vector3 move = forwardDir * (currentSpeed * dt);
        if (move.sqrMagnitude > 0f &&
            BoatNavigation.WouldRunAground(transform, move, hullHalfLength, hullHalfWidth, baseWaterY, minWaterDepth))
        {
            // 前面是陸地 / 淺灘 / 碼頭：撞一下停住（還是可以轉向、倒退離開）
            if (Mathf.Abs(currentSpeed) > 2f)
            {
                GameAudio.Play("SFX/rock_hit", transform.position, 0.7f, 0.8f, 0.05f, 1f, 30f);
                ThirdPersonCamera.ShakeMain(0.12f, 0.2f);
            }
            currentSpeed = 0f;
        }
        else transform.position += move;

        // 檢查是否開船出海，達到一定距離後觸發身後追擊
        if (!hasTriggeredPursuit && enemyBoatCount > 0)
        {
            float sailedDistance = Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z),
                                                   new Vector3(boardedStartPosition.x, 0f, boardedStartPosition.z));
            if (sailedDistance >= triggerPursuitDistance)
            {
                hasTriggeredPursuit = true;
                StartCoroutine(SpawnEnemyBoatsDelayed(1.0f));
            }
        }

        // 離終點剩 reinforcementDistance 公尺：增援敵艦出現
        if (!hasTriggeredReinforcement && reinforcementCount > 0)
        {
            Vector2 d = new Vector2(destinationPoint.x - transform.position.x, destinationPoint.z - transform.position.z);
            if (d.magnitude <= reinforcementDistance)
            {
                hasTriggeredReinforcement = true;
                SpawnReinforcements();
            }
        }

        // 加上海浪顛簸與浮動效果
        ApplyWaveFloating(currentSpeed);
    }

    /// <summary>
    /// 船身隨海浪起伏：跟著海面高度與坡度（BoatNavigation.FloatOnWaves），航行時船頭微微上揚
    /// </summary>
    void ApplyWaveFloating(float speed)
    {
        float speedRatio = Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(forwardSpeed, 0.01f));
        float lift = speed > 0.1f ? -1.6f * speedRatio : 0f;
        Vector3 fwd = Quaternion.Euler(0f, currentYaw + boatForwardOffsetAngle, 0f) * Vector3.forward;
        BoatNavigation.FloatOnWaves(transform, currentYaw, fwd, hullHalfLength, hullHalfWidth, baseWaterY,
                                    waveDraft, lift, waveTiltScale, waveFollowSmooth, ref wavePose);
    }

    void ApplyIdleFloating() => ApplyWaveFloating(0f);

    /// <summary>
    /// 檢查是否駛入指定地點
    /// </summary>
    void CheckDestinationReached()
    {
        if (isTransitioning) return;

        Vector2 boatPos2D = new Vector2(transform.position.x, transform.position.z);
        Vector2 dest2D = new Vector2(destinationPoint.x, destinationPoint.z);

        if (Vector2.Distance(boatPos2D, dest2D) <= destinationRadius)
        {
            StartLevelTransition();
        }
    }

    void StartLevelTransition()
    {
        if (isTransitioning) return;
        isTransitioning = true;
        currentState = BoatState.Transitioning;
        StartCoroutine(TransitionRoutine());
    }

    IEnumerator TransitionRoutine()
    {
        // 抵達就直接進下一關：GameManager 會馬上蓋上「海域 → 地下城」的轉場動畫，不先黑屏
        yield return null;

        Debug.Log($"[VikingBoat] 抵達指定航行海域，前往：{(string.IsNullOrEmpty(nextSceneName) ? "下一關" : nextSceneName)}");
        if (string.IsNullOrEmpty(nextSceneName)) GameManager.Instance.LoadNextLevel();
        else GameManager.Instance.LoadLevel(nextSceneName);
    }

    
    void LateUpdate()
    {
        // 駕駛或過場中，強制讓主角維持在站立點位置
        if ((currentState == BoatState.Driving || currentState == BoatState.Transitioning) && playerObj != null)
        {
            if (playerStandingPoint != null)
                playerObj.transform.position = playerStandingPoint.position;

            // 若玩家沒有在揮劍攻擊，則自動校正面向船首；攻擊時則允許隨鏡頭/滑鼠瞄準方向揮劍反擊
            var combat = playerObj.GetComponent<PlayerCombat>();
            bool isAttacking = combat != null && combat.ControlsFacing; // 揮劍、拉弓瞄準時由玩家決定面向

            if (!isAttacking)
            {
                Quaternion targetRot = (playerStandingPoint != null && useStandingPointRotation)
                    ? playerStandingPoint.localRotation * Quaternion.Euler(0f, playerFacingOffsetAngle, 0f)
                    : Quaternion.Euler(0f, playerFacingOffsetAngle, 0f);
                playerObj.transform.localRotation = targetRot;
            }
        }
    }

    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        var cam = Camera.main;

        // 增援敵艦出現的提示
        float sinceReinforce = Time.time - reinforcementMessageTime;
        if (sinceReinforce < 3f)
        {
            var warn = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 0.35f, 0.25f, Mathf.Clamp01(3f - sinceReinforce));
            GUI.Label(new Rect(0f, Screen.height * 0.28f, Screen.width, 40f), "⚠ 前方出現敵艦增援！", warn);
            GUI.color = Color.white;
        }

        // Debug 提示與按鈕（非駕駛模式時顯示在左下角）
        if (currentState != BoatState.Driving && currentState != BoatState.Transitioning)
        {
            if (GUI.Button(new Rect(16f, Screen.height - 48f, 210f, 32f), "⛵ [Debug] 直接瞬移登船 (B)"))
            {
                if (playerObj == null) playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null) BoardAndDrive(playerObj);
            }
        }

        // 1. 靠近船隻時的登船提示 HUD
        if ((currentState == BoatState.Locked || currentState == BoatState.ReadyToBoard) && playerObj != null)
        {
            float dist = Vector3.Distance(transform.position, playerObj.transform.position);
            if (dist <= interactionRadius)
            {
                float w = 500f, h = 44f;
                float x = (Screen.width - w) * 0.5f;
                float y = Screen.height * 0.72f;

                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold
                };

                if (currentState == BoatState.ReadyToBoard)
                {
                    GUI.color = new Color(1f, 0.88f, 0.2f);
                    GUI.Label(new Rect(x, y, w, h), "【按 E 鍵】登船掌舵！出海航向新海域 ⛵", style);
                }
                else
                {
                    GUI.color = new Color(0.95f, 0.35f, 0.35f);
                    GUI.Label(new Rect(x, y, w, h), lockedReason, style);
                }
                GUI.color = Color.white;
            }
        }

        // 2. 玩家掌舵駕駛中的操控介面與目標指引 HUD
        if (currentState == BoatState.Driving)
        {
            // 頂部操控說明條
            float hudW = 680f, hudH = 38f;
            float hx = (Screen.width - hudW) * 0.5f;
            float hy = 58f; // 避開頂部可能重疊的任務文字

            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(hx, hy, hudW, hudH), Texture2D.whiteTexture);

            var controlStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            GUI.color = Color.white;
            GUI.Label(new Rect(hx, hy, hudW, hudH), "⛵ 【W/S/A/D】航行駕駛　|　🏹【右鍵按住】瞄準　【左鍵】射箭（只有射中敵艦上的弓箭手才有效）", controlStyle);

            // 目的地距離提示
            Vector2 boatPos2D = new Vector2(transform.position.x, transform.position.z);
            Vector2 dest2D = new Vector2(destinationPoint.x, destinationPoint.z);
            float distToDest = Mathf.Max(0f, Vector2.Distance(boatPos2D, dest2D) - destinationRadius);

            float destW = 440f, destH = 34f;
            float dx = (Screen.width - destW) * 0.5f;
            float dy = 100f;

            GUI.color = new Color(0.1f, 0.22f, 0.35f, 0.85f);
            GUI.DrawTexture(new Rect(dx, dy, destW, destH), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 0.9f, 0.4f);
            var destStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(dx, dy, destW, destH), $"📍 前往外海未知海域（剩餘 {distToDest:F0} 公尺）", destStyle);

            // 畫面上目的地 3D 浮動標記
            if (cam != null)
            {
                Vector3 screenPos = cam.WorldToScreenPoint(destinationPoint + Vector3.up * 4f);
                if (screenPos.z > 0f)
                {
                    float markerW = 160f, markerH = 32f;
                    float mx = screenPos.x - markerW * 0.5f;
                    float my = Screen.height - screenPos.y - markerH * 0.5f;

                    GUI.color = new Color(0f, 0f, 0f, 0.7f);
                    GUI.DrawTexture(new Rect(mx, my, markerW, markerH), Texture2D.whiteTexture);

                    GUI.color = new Color(1f, 0.85f, 0.2f);
                    var markerStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 14,
                        fontStyle = FontStyle.Bold
                    };
                    GUI.Label(new Rect(mx, my, markerW, markerH), "▼ 下一關海域", markerStyle);
                }
            }
            GUI.color = Color.white;
        }

        // 3. 抵達指定地點後的過場畫面（黑屏淡出 + 進入下一關標題）
        if (currentState == BoatState.Transitioning || fadeAlpha > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            // 居中大標題
            if (fadeAlpha > 0.3f)
            {
                float titleAlpha = Mathf.Clamp01((fadeAlpha - 0.3f) / 0.5f);
                var titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 32,
                    fontStyle = FontStyle.Bold
                };
                GUI.color = new Color(1f, 0.95f, 0.85f, titleAlpha);
                GUI.Label(new Rect(0, Screen.height * 0.42f, Screen.width, 50f), "⛵ 揚帆破浪 ‧ 駛入未知海域", titleStyle);

                var subStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20
                };
                GUI.color = new Color(0.85f, 0.85f, 0.85f, titleAlpha);
                GUI.Label(new Rect(0, Screen.height * 0.50f, Screen.width, 40f), "正在進入下一關……", subStyle);
            }

            GUI.color = Color.white;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(destinationPoint, destinationRadius);
        Gizmos.DrawLine(transform.position, destinationPoint);

        // 繪製視覺船首朝向指針
        Gizmos.color = Color.magenta;
        Vector3 bowDir = Quaternion.Euler(0f, (Application.isPlaying ? currentYaw : transform.eulerAngles.y) + boatForwardOffsetAngle, 0f) * Vector3.forward;
        Gizmos.DrawRay(transform.position + Vector3.up * 1.5f, bowDir * 8f);
    }

    // ================= 自動運行支援（只要一按 Play 便自動準備好） =================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetupInVikingVillage()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "The_Viking_Village") return;

        // 1. 關閉展示導覽攝影機
        var camerasObj = GameObject.Find("Cameras");
        if (camerasObj != null) camerasObj.SetActive(false);

        // 2. 確保有 PlayerRig
        if (GameObject.Find("PlayerRig") == null && GameObject.FindWithTag("Player") == null)
        {
            var pPrefab = Resources.Load<GameObject>("Prefabs/PlayerRig");
            if (pPrefab != null)
            {
                var p = Instantiate(pPrefab, new Vector3(13.7f, 5.4f, 4.4f), Quaternion.Euler(0f, 150f, 0f));
                p.name = "PlayerRig";
            }
        }

        // 3. 確保有 QuestBoat
        var existingBoat = FindFirstObjectByType<VikingBoat>();
        if (existingBoat == null)
        {
            var bPrefab = Resources.Load<GameObject>("Prefabs/pf_build_boat_01");
            // 碼頭棧橋水面停泊位置（Y 座標 1.3f，面向開闊外海方位角 290 度）
            GameObject boatObj = bPrefab != null
                ? Instantiate(bPrefab, new Vector3(-35f, 1.3f, 48f), Quaternion.Euler(0f, 315f, 0f))
                : new GameObject("QuestBoat");
            boatObj.name = "QuestBoat";
            SetStaticRecursively(boatObj, false);

            // 停用船體自身自帶的碰撞體，避免航行時卡在淺灘或與岸邊卡住
            foreach (var col in boatObj.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            existingBoat = boatObj.AddComponent<VikingBoat>();
            existingBoat.interactionRadius = 7.0f;
            existingBoat.forwardSpeed = 9.5f;
            existingBoat.reverseSpeed = 3.5f;
            existingBoat.steeringTurnSpeed = 42.0f;
            existingBoat.destinationPoint = new Vector3(-85f, 1.3f, 85f);
            existingBoat.destinationRadius = 16f;

            // 站立點
            var sp = new GameObject("StandingPoint");
            sp.transform.SetParent(boatObj.transform);
            sp.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            sp.transform.localRotation = Quaternion.identity;
            existingBoat.playerStandingPoint = sp.transform;

            // 目的地金色指引光柱
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "ObjectiveBeacon";
            beacon.transform.SetParent(boatObj.transform);
            beacon.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            beacon.transform.localScale = new Vector3(0.5f, 3.5f, 0.5f);
            var bCol = beacon.GetComponent<Collider>();
            if (bCol != null) Destroy(bCol);
            var mr = beacon.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                mat.color = new Color(1f, 0.85f, 0.2f, 0.8f);
                mr.sharedMaterial = mat;
            }
            var lightObj = new GameObject("BeaconLight");
            lightObj.transform.SetParent(beacon.transform);
            lightObj.transform.localPosition = Vector3.zero;
            var l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.85f, 0.3f);
            l.range = 15f;
            l.intensity = 2.5f;

            existingBoat.objectiveIndicator = beacon;
            beacon.SetActive(false);

            // 在外海目的地也建立一個視覺目標浮標光柱
            CreateDestinationBuoy(existingBoat.destinationPoint);
        }

        // 4. 確保有 EnemyWaveManager
        var waveMgr = FindFirstObjectByType<VikingEnemyWaveManager>();
        if (waveMgr == null)
        {
            var waveObj = new GameObject("EnemyWaveManager");
            waveMgr = waveObj.AddComponent<VikingEnemyWaveManager>();
            waveMgr.targetBoat = existingBoat;
            waveMgr.enemyPrefab = Resources.Load<GameObject>("Prefabs/Enemy_Grunt");
            waveMgr.autoStartDelay = 2.0f;
            waveMgr.spawnPoints = new List<Vector3>
            {
                new Vector3(12f, 5f, 9f),
                new Vector3(-30f, 9f, 13f),
                new Vector3(-35f, 9f, -8f),
                new Vector3(25f, 9f, -10f),
                new Vector3(-50f, 10f, 20f)
            };
        }
    }

    static void CreateDestinationBuoy(Vector3 pos)
    {
        var buoy = new GameObject("DestinationBuoy");
        buoy.transform.position = pos;

        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.transform.SetParent(buoy.transform);
        pillar.transform.localPosition = new Vector3(0f, 5f, 0f);
        pillar.transform.localScale = new Vector3(0.8f, 5f, 0.8f);
        var col = pillar.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = pillar.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = new Color(0.2f, 0.8f, 1f, 0.7f); // 水藍色光柱標記
            mr.sharedMaterial = mat;
        }

        var lObj = new GameObject("BuoyLight");
        lObj.transform.SetParent(buoy.transform);
        lObj.transform.localPosition = new Vector3(0f, 2f, 0f);
        var l = lObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.2f, 0.8f, 1f);
        l.range = 25f;
        l.intensity = 3.5f;
    }

    static void SetStaticRecursively(GameObject obj, bool isStatic)
    {
        obj.isStatic = isStatic;
        foreach (Transform child in obj.transform)
        {
            SetStaticRecursively(child.gameObject, isStatic);
        }
    }
}
