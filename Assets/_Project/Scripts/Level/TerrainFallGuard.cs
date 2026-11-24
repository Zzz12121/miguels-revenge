using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 穿地保護（自動執行，不用在場景裡放東西）：
/// 角色（玩家、敵人，有 CharacterController 的都算）的腳如果跑到地形（Terrain）表面底下，
/// 就把它拉回地面上，避免「地形突然破掉、人掉下去」。
///
/// 為什麼會穿地：有些招式 / 技能會先關掉 CharacterController、直接改位置（例如貼著斜坡往上滑、
/// 落地點算低了一點），開回來的時候腳已經在地形裡面，CharacterController 就從地形底下掉下去；
/// 鏡頭也跟著跑到地底，看起來就像地形破了一個洞。
///
/// 只在 Scenes 列表裡的場景作用（有地底洞穴、從地底鑽出來的場景不要加）。
/// CharacterController 關著的時候（技能、挑空、傳送中）不管，交給那個技能自己處理。
/// </summary>
public class TerrainFallGuard : MonoBehaviour
{
    /// <summary>哪些場景要開穿地保護</summary>
    public static readonly HashSet<string> Scenes = new HashSet<string> { "ForestIsland" };
    /// <summary>腳在地形表面下多深才拉回來（m）。小一點的誤差交給 CharacterController 自己</summary>
    public static float Tolerance = 0.35f;

    static TerrainFallGuard host;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => host = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null) return;
        host = new GameObject("TerrainFallGuard").AddComponent<TerrainFallGuard>();
        DontDestroyOnLoad(host.gameObject);
    }

    readonly List<CharacterController> bodies = new List<CharacterController>();
    readonly Dictionary<CharacterController, float> lastLog = new Dictionary<CharacterController, float>();
    Terrain[] terrains = new Terrain[0];
    float nextScan;
    bool activeScene;

    void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;
    void OnDisable() => SceneManager.activeSceneChanged -= OnSceneChanged;
    void OnSceneChanged(Scene a, Scene b) => nextScan = 0f;

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 1f;
            activeScene = Scenes.Contains(SceneManager.GetActiveScene().name);
            bodies.Clear();
            if (activeScene)
            {
                bodies.AddRange(FindObjectsByType<CharacterController>());
                terrains = Terrain.activeTerrains;
            }
        }
        if (!activeScene) return;

        for (int i = 0; i < bodies.Count; i++)
        {
            var cc = bodies[i];
            if (cc == null || !cc.enabled || !cc.gameObject.activeInHierarchy) continue;
            // 有踩在東西上（例如吊橋、木板埋進坡裡的那一段）就不是穿地，不要管
            if (cc.isGrounded) continue;
            var ai = cc.GetComponent<EnemyAI>();
            if (ai != null && ai.IsUnderground) continue;   // 正在從地底鑽出來

            Vector3 pos = cc.transform.position;
            if (!SurfaceY(pos, out float surface)) continue;
            float feet = cc.bounds.min.y;
            if (feet >= surface - Tolerance) continue;

            // 拉回地面上（CharacterController 要先關掉才能直接改位置）
            float lift = surface + cc.skinWidth + 0.02f - feet;
            cc.enabled = false;
            cc.transform.position = pos + Vector3.up * lift;
            cc.enabled = true;
            var motor = cc.GetComponent<ThirdPersonController>();
            if (motor != null) motor.ResetMotion();

            if (!lastLog.TryGetValue(cc, out float t) || Time.unscaledTime - t > 2f)
            {
                lastLog[cc] = Time.unscaledTime;
                Debug.LogWarning($"[TerrainFallGuard] {cc.name} 跑到地形底下 {surface - feet:0.00}m（{pos}），已拉回地面", cc);
            }
        }
    }

    /// <summary>pos 正下方 / 正上方的地形表面高度（不在任何地形範圍內 → false）</summary>
    bool SurfaceY(Vector3 pos, out float y)
    {
        y = 0f;
        foreach (var t in terrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (pos.x < o.x || pos.z < o.z || pos.x > o.x + s.x || pos.z > o.z + s.z) continue;
            y = t.SampleHeight(pos) + o.y;
            return true;
        }
        return false;
    }
}
