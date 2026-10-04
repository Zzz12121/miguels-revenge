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

    [Header("狀態設定")]
    public BoatState currentState = BoatState.Locked;
    [Tooltip("登船互動半徑 (公尺)")]
    public float interactionRadius = 6.5f;
    [Tooltip("下一關場景名稱（需加入 Build Settings）")]
    public string nextSceneName = "Floor02";

    [Header("玩家站立點")]
    [Tooltip("玩家站在船上的位置，留空會自動抓船隻中心")]
    public Transform playerStandingPoint;

    [Header("自由駕駛控制參數")]
    [Tooltip("最大前進航速 (m/s)")]
    public float forwardSpeed = 9.0f;
    [Tooltip("最大倒退航速 (m/s)")]
    public float reverseSpeed = 3.5f;
    [Tooltip("轉向速度 (deg/s)")]
    public float steeringTurnSpeed = 42.0f;
    [Tooltip("航速加減速平滑度")]
    public float acceleration = 6.0f;

    [Header("目的地設定")]
    [Tooltip("駛向的目的地座標（外海開闊航道）")]
    public Vector3 destinationPoint = new Vector3(-85f, 1.3f, 85f);
    [Tooltip("進入目的地的觸發半徑")]
    public float destinationRadius = 16.0f;

    [Header("波浪起伏手感")]
    public float waveFrequency = 1.6f;
    public float waveAmplitude = 0.15f;
    public float pitchAmount = 2.0f;
    public float rollAmount = 3.2f;

    [Header("特效與提示")]
    [Tooltip("船上未解鎖 / 解鎖時的指示標記物件（例如金色光柱）")]
    public GameObject objectiveIndicator;

    // 運行期私有變數
    GameObject playerObj;
    float currentSpeed = 0f;
    float currentYaw = 0f;
    float baseWaterY;
    float fadeAlpha = 0f;
    bool isTransitioning = false;
    string lockedReason = "村莊內仍有湧出的敵人，請先擊潰所有敵人才能啟航！";

    void Awake()
    {
        baseWaterY = 1.3f; // 維京村莊海平面高度為 y = 1.3
        currentYaw = transform.eulerAngles.y;
        Vector3 p = transform.position;
        p.y = baseWaterY;
        transform.position = p;
    }

    void Start()
    {
        UpdateIndicator();
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
                // 淡出過場時保持微幅前進慣性
                transform.position += transform.forward * (Mathf.Max(currentSpeed, 4f) * 0.5f) * Time.deltaTime;
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

        // 2. 將角色固定在船隻的掌舵站立點上
        Vector3 standingPos = playerStandingPoint != null
            ? playerStandingPoint.position
            : transform.position + transform.up * 0.6f;

        player.transform.position = standingPos;
        player.transform.rotation = transform.rotation;
        player.transform.SetParent(transform);

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
                camScript.yaw = transform.eulerAngles.y; // 視角自動朝向船頭正前方海面
                camScript.pitch = 14f;
                camScript.SnapToTarget(); // 立即將鏡頭瞬移至長船後方，防止平滑過渡被陸地建物卡住
            }
        }

        GameAudio.Play("SFX/RPG/drawSword", transform.position, 1f, 0.9f);
        Debug.Log("[VikingBoat] 玩家已登船掌舵！可用 W/S 前進倒退，A/D 轉向！");
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
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) moveInput += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) moveInput -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turnInput -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turnInput += 1f;
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

        // 前進推進（依 currentYaw 計算前進向量，不受浪涌俯仰傾斜干擾）
        Vector3 forwardDir = Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
        transform.position += forwardDir * (currentSpeed * dt);

        // 加上海浪顛簸與浮動效果
        ApplyWaveFloating(currentSpeed);
    }

    /// <summary>
    /// 水面起伏動態模擬
    /// </summary>
    void ApplyWaveFloating(float speed)
    {
        float t = Time.time;
        float speedRatio = Mathf.Clamp01(Mathf.Abs(speed) / forwardSpeed);

        float freq = waveFrequency * (1f + speedRatio * 0.4f);
        float amp = waveAmplitude * (1f + speedRatio * 0.7f);

        Vector3 p = transform.position;
        p.y = baseWaterY + Mathf.Sin(t * freq) * amp;
        transform.position = p;

        float pitch = Mathf.Sin(t * (freq * 1.2f)) * (pitchAmount * (1f + speedRatio * 0.5f));
        if (speed > 0.1f) pitch -= 1.6f * speedRatio; // 航行時船頭微微上揚

        float roll = Mathf.Cos(t * freq) * (rollAmount * (1f + speedRatio * 0.3f));

        transform.rotation = Quaternion.Euler(pitch, currentYaw, roll);
    }

    void ApplyIdleFloating()
    {
        float t = Time.time;
        Vector3 p = transform.position;
        p.y = baseWaterY + Mathf.Sin(t * waveFrequency) * (waveAmplitude * 0.4f);
        transform.position = p;

        float pitch = Mathf.Sin(t * 1.2f) * (pitchAmount * 0.4f);
        float roll = Mathf.Cos(t * 1.0f) * (rollAmount * 0.4f);

        transform.rotation = Quaternion.Euler(pitch, currentYaw, roll);
    }

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
        float duration = 2.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            fadeAlpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        Debug.Log($"[VikingBoat] 抵達指定航行海域，切換場景至：{nextSceneName}");
        SceneManager.LoadScene(nextSceneName);
    }

    void OnGUI()
    {
        var cam = Camera.main;

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
            float hudW = 560f, hudH = 38f;
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
            GUI.Label(new Rect(hx, hy, hudW, hudH), "⛵ 【W】加速航行　【S】倒退減速　【A / D】掌舵轉向", controlStyle);

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
            existingBoat.nextSceneName = "Floor02";
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
                new Vector3(19f, 20f, 9f),
                new Vector3(8f, 20f, 13f),
                new Vector3(9f, 20f, -1f),
                new Vector3(20f, 20f, 2f),
                new Vector3(14f, 20f, 15f)
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
