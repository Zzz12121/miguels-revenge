using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地城的一間房間（由 DungeonGenerator 自動產生）。
///   - 玩家走進還沒清過的戰鬥房間 → 所有門關上，小怪從地底鑽出來
///   - 小怪全部打倒 → 門打開、畫面上方出現「COMPLETE」，這間房間之後都是安全的
///   - 玩家在戰鬥中死掉 → 這間房間重置（小怪消失、門打開），下次走進來再打一次；
///     已經清過的房間不會重置
/// 起點房間和 Boss 門前的小房間沒有小怪。
/// </summary>
public class DungeonRoom : MonoBehaviour
{
    public enum State { Unvisited, Fighting, Cleared }

    [Header("由 DungeonGenerator 設定")]
    [Tooltip("房間內部的半寬 / 半深 (m)")]
    public Vector2 halfSize = new Vector2(10f, 10f);
    [Tooltip("這間房間有幾隻小怪（0 = 安全的房間）")]
    public int enemyCount;
    public GameObject gruntPrefab;
    public GameObject ambusherPrefab;
    [Range(0f, 1f)] public float ambusherChance = 0.3f;
    public List<DungeonDoor> doors = new List<DungeonDoor>();

    [Header("手感")]
    [Tooltip("玩家要走進房間多深才會關門 (m)。太小的話門會夾到人")]
    public float enterMargin = 1.5f;
    [Tooltip("小怪出現的位置至少離牆多遠 (m)")]
    public float wallMargin = 3f;
    [Tooltip("小怪出現的位置至少離玩家多遠 (m)")]
    public float minPlayerDistance = 5f;
    [Tooltip("每隻小怪之間隔多久出現 (秒)")]
    public float spawnInterval = 0.2f;

    [Header("清空時的標題（由 DungeonGenerator 設定）")]
    [Tooltip("小怪全部打倒時，畫面上方出現的字。留空 = 不顯示")]
    public string clearTitle = "COMPLETE";
    [Tooltip("標題下面的小字（可以留空）")]
    public string clearSubtitle = "房間已清空";

    public State CurrentState { get; private set; } = State.Unvisited;
    public int AliveCount => enemies.Count + pendingSpawns;

    readonly List<EnemyAI> enemies = new List<EnemyAI>();
    int pendingSpawns;
    Transform player;
    PlayerHealth playerHealth;
    Transform enemyContainer;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
            if (playerHealth != null) playerHealth.OnRespawned += OnPlayerRespawned;
        }
        if (enemyCount <= 0) CurrentState = State.Cleared;
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnRespawned -= OnPlayerRespawned;
    }

    void Update()
    {
        if (player == null) return;

        if (CurrentState == State.Unvisited)
        {
            bool alive = playerHealth == null || !playerHealth.IsDead;
            if (alive && Contains(player.position, enterMargin)) BeginFight();
            return;
        }
        if (CurrentState != State.Fighting) return;

        // 死掉的小怪：等倒地動畫播完就移除
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null) { enemies.RemoveAt(i); continue; }
            if (e.CurrentState == EnemyAI.State.Dead)
            {
                e.enabled = false; // 不然玩家這時候重生的話，牠會被自己的重置機制復活
                Destroy(e.gameObject, 3f);
                enemies.RemoveAt(i);
            }
        }
        if (AliveCount == 0) Clear();
    }

    /// <summary>worldPos 在不在房間內部（往內縮 margin 公尺）</summary>
    public bool Contains(Vector3 worldPos, float margin)
    {
        Vector3 l = transform.InverseTransformPoint(worldPos);
        return Mathf.Abs(l.x) <= halfSize.x - margin && Mathf.Abs(l.z) <= halfSize.y - margin && Mathf.Abs(l.y) < 6f;
    }

    void BeginFight()
    {
        CurrentState = State.Fighting;
        foreach (var d in doors)
            if (d != null) d.Close();
        ThirdPersonCamera.ShakeMain(0.15f, 0.25f);
        StartCoroutine(SpawnRoutine());
    }

    IEnumerator SpawnRoutine()
    {
        pendingSpawns = enemyCount;
        yield return new WaitForSeconds(0.4f); // 門先關上，再冒出小怪
        for (int i = 0; i < enemyCount; i++)
        {
            GameObject prefab = ambusherPrefab != null && (gruntPrefab == null || Random.value < ambusherChance) ? ambusherPrefab : gruntPrefab;
            if (prefab != null)
            {
                if (enemyContainer == null)
                {
                    enemyContainer = new GameObject("Enemies").transform;
                    enemyContainer.SetParent(transform, false);
                }
                var ai = EnemySpawner.SpawnFromGround(prefab, PickSpawnPoint(), transform.position + Vector3.up, enemyContainer, player);
                if (ai != null) enemies.Add(ai);
            }
            pendingSpawns--;
            yield return new WaitForSeconds(spawnInterval);
        }
        pendingSpawns = 0;
    }

    Vector3 PickSpawnPoint()
    {
        float mx = Mathf.Max(halfSize.x - wallMargin, 1f), mz = Mathf.Max(halfSize.y - wallMargin, 1f);
        Vector3 best = transform.position;
        float bestDist = -1f;
        for (int tries = 0; tries < 12; tries++)
        {
            Vector3 p = transform.TransformPoint(new Vector3(Random.Range(-mx, mx), 0f, Random.Range(-mz, mz)));
            float d = player != null ? Vector3.Distance(p, player.position) : 99f;
            if (d >= minPlayerDistance) return p;
            if (d > bestDist) { bestDist = d; best = p; } // 都太近就挑最遠的
        }
        return best;
    }

    void Clear()
    {
        CurrentState = State.Cleared;
        if (!string.IsNullOrEmpty(clearTitle)) VictoryBanner.ShowSmall(clearTitle, clearSubtitle);
        foreach (var d in doors)
            if (d != null) d.Open();
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.9f, 0.8f, 0f, 0.2f);
    }

    /// <summary>玩家重生：打到一半的房間重置，清過的房間維持清過</summary>
    void OnPlayerRespawned()
    {
        if (CurrentState != State.Fighting) return;
        StopAllCoroutines();
        pendingSpawns = 0;
        foreach (var e in enemies)
            if (e != null) Destroy(e.gameObject);
        enemies.Clear();
        foreach (var d in doors)
            if (d != null) d.SetImmediate(false);
        CurrentState = State.Unvisited;
    }

    void OnGUI()
    {
        string text = null;
        Color color = Color.white;
        if (CurrentState == State.Fighting) { text = $"敵人　{AliveCount}"; color = new Color(1f, 0.85f, 0.7f); }
        if (text == null) return; // 清空時的 COMPLETE 標題由 VictoryBanner 負責

        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(2f, 62f, Screen.width, 40f), text, style);
        GUI.color = color;
        GUI.Label(new Rect(0f, 60f, Screen.width, 40f), text, style);
        GUI.color = Color.white;
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = CurrentState == State.Cleared ? new Color(0.3f, 1f, 0.4f, 0.8f)
                     : CurrentState == State.Fighting ? new Color(1f, 0.3f, 0.2f, 0.8f) : new Color(1f, 0.85f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(Vector3.up * 2f, new Vector3(halfSize.x * 2f, 4f, halfSize.y * 2f));
    }
}
