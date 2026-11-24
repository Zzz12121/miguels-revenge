using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 霧海孤島（ForestIsland）倒下重來時的狀態（自動執行，不用在場景裡放東西）：
///   - 已經被打死的骷髏士兵不會再出現：記下死掉的士兵，重新載入場景之後把它們關掉
///     （正常從道場走吊橋進島 = 新的一輪，全部士兵重新出現）
///   - 重生時在樹林邊緣撒幾顆石頭（PowderStone，可以磨成石粉），每顆頭上有黃色菱形提示
/// 只是在場景載入後做的小調整，不改士兵 AI、玩家重生流程（VillageReturn）本身。
/// </summary>
public class ForestRetry : MonoBehaviour
{
    public const string Scene = VillageReturn.BridgeRestartScene;

    /// <summary>重生時撒幾顆石頭</summary>
    public static int RetryStones = 5;
    /// <summary>石頭撒在離戰鬥區中心多遠的範圍（樹林邊緣；戰鬥區半徑 24m）</summary>
    public static Vector2 StoneRing = new Vector2(21f, 30f);

    static ForestRetry host;
    static readonly HashSet<string> deadSoldiers = new HashSet<string>();
    static bool retryPending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { host = null; deadSoldiers.Clear(); retryPending = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null) return;
        host = new GameObject("ForestRetry").AddComponent<ForestRetry>();
        DontDestroyOnLoad(host.gameObject);
        host.OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);   // 第一個場景的 sceneLoaded 已經發生過了
    }

    PlayerHealth player;

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single && s != SceneManager.GetActiveScene()) return;
        if (s.name != Scene) { retryPending = false; Unhook(); return; }

        bool retry = retryPending;
        retryPending = false;
        if (!retry) deadSoldiers.Clear();   // 新的一輪：士兵全部重新出現

        foreach (var ai in FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ai.gameObject.scene != s) continue;
            string key = Key(ai);
            if (retry && deadSoldiers.Contains(key)) { ai.gameObject.SetActive(false); continue; }
            ai.stayDeadOnPlayerRespawn = true;
            var hp = ai.GetComponent<EnemyHealth>();
            if (hp != null) hp.OnDied += () => deadSoldiers.Add(key);
        }

        Hook();
        if (retry) StartCoroutine(ScatterStones());
    }

    static string Key(EnemyAI ai)
    {
        var t = ai.transform;
        string path = t.name;
        for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    void Hook()
    {
        Unhook();
        player = PlayerHealth.Find();
        if (player != null) player.OnDied += OnPlayerDied;
    }

    void Unhook()
    {
        if (player != null) player.OnDied -= OnPlayerDied;
        player = null;
    }

    void OnPlayerDied()
    {
        if (SceneManager.GetActiveScene().name == Scene) retryPending = true;   // 接下來 VillageReturn 會重新載入這個場景
    }

    /// <summary>等地形、碰撞都好了再撒石頭（貼齊地面）</summary>
    IEnumerator ScatterStones()
    {
        yield return null;   // 醒來的對話會暫停遊戲（timeScale = 0），不要等物理更新
        Physics.SyncTransforms();
        var centerMarker = GameObject.Find("GameplayMarkers/ArenaCenter (r=24m)");
        Vector3 center = centerMarker != null ? centerMarker.transform.position : Vector3.zero;

        var rnd = new System.Random(System.Environment.TickCount);
        int placed = 0;
        float startAngle = (float)rnd.NextDouble() * Mathf.PI * 2f;
        for (int attempt = 0; attempt < RetryStones * 12 && placed < RetryStones; attempt++)
        {
            // 平均分在一圈上（每顆一個扇區，扇區內隨機），整片樹林邊緣都找得到
            float sector = Mathf.PI * 2f / RetryStones;
            float ang = startAngle + placed * sector + (float)rnd.NextDouble() * sector * 0.7f;
            float r = Mathf.Lerp(StoneRing.x, StoneRing.y, (float)rnd.NextDouble());
            Vector3 p = center + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            if (!Ground(p, out Vector3 g)) continue;
            PowderStone.Create(g, (float)rnd.NextDouble() * 360f).ShowMarker();
            placed++;
        }
    }

    /// <summary>往下找平坦的地面（避開樹幹、石頭頂端和陡坡）</summary>
    static bool Ground(Vector3 p, out Vector3 ground)
    {
        ground = p;
        var hits = Physics.RaycastAll(p + Vector3.up * 60f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            if (h.collider.GetComponentInParent<EnemyAI>() != null) continue;
            if (h.point.y <= best) continue;
            best = h.point.y;
            found = h.collider is TerrainCollider && h.normal.y > 0.8f;   // 最上面那一層要是地形（不是樹幹、石頭）
            ground = h.point;
        }
        if (!found) return false;
        // 旁邊 0.8m 內不能有樹幹或石頭
        foreach (var c in Physics.OverlapSphere(ground + Vector3.up * 0.9f, 0.8f, ~0, QueryTriggerInteraction.Ignore))
            if (!(c is TerrainCollider)) return false;
        return true;
    }
}
