using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 維京村莊波次敵人管理器：
/// - 自動或透過觸發區域湧出敵人（Enemy_Grunt）。
/// - 追蹤存活敵人數量與任務 HUD 顯示。
/// - 所有敵人被擊敗後解鎖 VikingBoat，引導玩家前往港口搭船。
/// </summary>
public class VikingEnemyWaveManager : MonoBehaviour
{
    [Header("目標船隻")]
    [Tooltip("清除完全部敵人後要解鎖的船隻")]
    public VikingBoat targetBoat;

    [Header("敵人設定")]
    [Tooltip("敵人 Prefab (例如 Enemy_Grunt)")]
    public GameObject enemyPrefab;
    [Tooltip("敵人生成點清單（留空會在廣場周邊自動計算）")]
    public List<Vector3> spawnPoints = new List<Vector3>();

    [Header("波次控制")]
    [Tooltip("遊戲開始後幾秒自動湧出敵人（0 代表等待玩家進入觸發區）")]
    public float autoStartDelay = 2.0f;
    [Tooltip("是否一開始就自動觸發追擊玩家")]
    public bool alertPlayerOnSpawn = false;

    [Header("狀態追蹤")]
    public bool waveStarted = false;
    public bool waveCleared = false;
    public int totalEnemies = 0;
    public int remainingEnemies = 0;

    readonly List<GameObject> activeEnemies = new List<GameObject>();
    float bannerTimer = 0f;
    string bannerMessage = "";
    Color bannerColor = Color.white;

    void Awake()
    {
        // 每次進入遊戲或重新開始，自動重置波次狀態，確保敵人正常湧出
        waveStarted = false;
        waveCleared = false;
        activeEnemies.Clear();

        if (enemyPrefab == null)
            enemyPrefab = Resources.Load<GameObject>("Prefabs/Enemy_Grunt");
        if (targetBoat == null)
            targetBoat = FindFirstObjectByType<VikingBoat>();
    }

    void Start()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            // 廣場周遭預設生成點
            spawnPoints = new List<Vector3>
            {
                new Vector3(19f, 20f, 9f),
                new Vector3(8f, 20f, 13f),
                new Vector3(9f, 20f, -1f),
                new Vector3(20f, 20f, 2f),
                new Vector3(14f, 20f, 15f)
            };
        }

        if (autoStartDelay > 0f)
        {
            Invoke(nameof(StartWave), autoStartDelay);
        }
    }

    /// <summary>
    /// 開始湧出敵人
    /// </summary>
    public void StartWave()
    {
        if (waveStarted || waveCleared) return;
        waveStarted = true;

        if (enemyPrefab == null)
        {
            Debug.LogError("[VikingWaveManager] 未指定 enemyPrefab！請檢查 Inspector 設定。");
            return;
        }

        activeEnemies.Clear();
        totalEnemies = spawnPoints.Count;
        remainingEnemies = totalEnemies;

        var player = GameObject.FindWithTag("Player");

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Vector3 pos = spawnPoints[i];

            // Raycast 從設定座標向下找地面，自動貼地生成，防止敵人在地底或懸空
            Vector3 rayOrigin = new Vector3(pos.x, Mathf.Max(pos.y, 30f), pos.z);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 60f))
            {
                pos.y = hit.point.y + 0.05f; // 略高於地面表面
            }
            // 若 Raycast 未命中（例如在水面或空中），至少確保不低於 0
            else
            {
                pos.y = Mathf.Max(pos.y, 0f);
            }

            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject enemy = Instantiate(enemyPrefab, pos, rot);
            enemy.name = $"ShadowGuard_{i + 1}";
            activeEnemies.Add(enemy);

            // 綁定死亡事件
            var health = enemy.GetComponent<EnemyHealth>();
            if (health != null)
            {
                health.OnDied += () => OnEnemyDied(enemy);
            }

            // 讓敵人直接進入警戒 / 衝刺狀態追擊玩家
            var ai = enemy.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.leashRadius = 60f;
                ai.giveUpDistance = 50f;
                ai.sprintDistance = 5f;
                ai.enableWander = true;
                ai.wanderRadius = 12f; // 在廣場與村莊周圍較大範圍巡邏走動
                if (alertPlayerOnSpawn && player != null)
                {
                    ai.Alert(true);
                }
            }
        }

        ShowBanner("⚠️ 暗影守衛湧出！消滅所有敵人以解鎖出航！", new Color(1f, 0.4f, 0.4f), 4f);
        GameAudio.Play("SFX/RPG/drawSword", transform.position, 1f, 0.8f);
        Debug.Log($"[VikingWaveManager] 已湧出 {totalEnemies} 隻暗影守衛！");
    }

    void OnEnemyDied(GameObject enemy)
    {
        if (!activeEnemies.Contains(enemy)) return;
        activeEnemies.Remove(enemy);
        remainingEnemies = Mathf.Max(0, remainingEnemies - 1);

        Debug.Log($"[VikingWaveManager] 敵人被擊倒！剩餘: {remainingEnemies} / {totalEnemies}");

        if (remainingEnemies <= 0 && !waveCleared)
        {
            waveCleared = true;
            OnAllEnemiesCleared();
        }
        else
        {
            ShowBanner($"⚔️ 擊倒暗影守衛！剩餘敵人：{remainingEnemies} / {totalEnemies}", new Color(1f, 0.85f, 0.3f), 2.5f);
        }
    }

    void OnAllEnemiesCleared()
    {
        ShowBanner("★ 守衛全滅！港口長船已啟動！前往港口搭船啟航 ⛵", new Color(0.3f, 1f, 0.4f), 7f);
        GameAudio.Play("SFX/RPG/coins", transform.position, 1f, 1f);

        if (targetBoat != null)
        {
            targetBoat.UnlockBoat();
        }
        else
        {
            // 嘗試自動尋找場景中的 VikingBoat
            var boat = FindFirstObjectByType<VikingBoat>();
            if (boat != null) boat.UnlockBoat();
        }
    }

    void ShowBanner(string msg, Color col, float duration)
    {
        bannerMessage = msg;
        bannerColor = col;
        bannerTimer = duration;
    }

    void Update()
    {
        if (bannerTimer > 0f) bannerTimer -= Time.deltaTime;
    }

    void OnGUI()
    {
        // 任務狀態頂部 HUD
        float w = 460f, h = 34f;
        float x = (Screen.width - w) * 0.5f;
        float y = 16f;

        GUI.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);

        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold
        };

        if (!waveStarted)
        {
            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            GUI.Label(new Rect(x, y, w, h), "【任務】準備迎擊即將湧出的敵人……", titleStyle);
        }
        else if (!waveCleared)
        {
            GUI.color = new Color(1f, 0.35f, 0.35f);
            GUI.Label(new Rect(x, y, w, h), $"【任務】消滅湧出的敵人！ 剩餘：{remainingEnemies} / {totalEnemies}", titleStyle);
        }
        else
        {
            GUI.color = new Color(0.35f, 1f, 0.5f);
            GUI.Label(new Rect(x, y, w, h), "【任務完成】前往港口碼頭搭船前往下一關 ⛵", titleStyle);
        }

        // 中央醒目快訊 Banner
        if (bannerTimer > 0f)
        {
            float bw = 560f, bh = 46f;
            float bx = (Screen.width - bw) * 0.5f;
            float by = 60f;

            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(bx - 2, by - 2, bw + 4, bh + 4), Texture2D.whiteTexture);

            var bannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                fontStyle = FontStyle.Bold
            };
            GUI.color = bannerColor;
            GUI.Label(new Rect(bx, by, bw, bh), bannerMessage, bannerStyle);
        }

        GUI.color = Color.white;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (spawnPoints != null)
        {
            foreach (var p in spawnPoints)
            {
                Gizmos.DrawWireSphere(p, 1f);
            }
        }
    }
}
