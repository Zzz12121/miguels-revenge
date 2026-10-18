#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 放置補血道具
///
/// 1. 建立 Prefabs/HealthPickup.prefab（漂浮的紅十字 + 紅光）和材質 M_HealthPickup
/// 2. 維京村莊散佈 PerLevel 個補血道具（第一關沙漠不放）：以玩家起點、檢查點、敵人、篝火等地方為中心，
///    往旁邊找平坦、空曠、看得到的地面放（不會放進牆裡、水裡或隔壁房間）
///    放好的道具都在場景的「HealthPickups」物件底下，想調整位置直接拖就好
///    地下城（Floor02）每次按 Play 才隨機蓋房間，改成由 DungeonGenerator 在每間戰鬥房間 + Boss 門前各放一個
///
/// 可以重複執行（會先刪掉舊的 HealthPickups 再重放）。
/// </summary>
public static class HealthPickupInstaller
{
    const string PrefabPath = "Assets/_Project/Prefabs/HealthPickup.prefab";
    const string MaterialPath = "Assets/_Project/Materials/M_HealthPickup.mat";
    const string GroupName = "HealthPickups";
    const int PerLevel = 6;
    const float MinSpacing = 12f;
    const float WaterLevel = 1.3f; // 維京村莊的海平面

    static readonly string[] LevelScenes =
    {
        // 第一關（Planet）不放補血道具
        "Assets/_Project/Scenes/The_Viking_Village.unity",
        "Assets/_Project/Scenes/Floor02.unity",
    };

    [MenuItem("GeminiGrowUp/放置補血道具")]
    public static void Install()
    {
        if (!Application.isBatchMode)
        {
            if (!EditorUtility.DisplayDialog("放置補血道具",
                    $"會在維京村莊放 {PerLevel} 個、地下城每間戰鬥房間一個補血道具（舊的會先刪掉）。\n建議先 commit。要繼續嗎？", "繼續", "取消"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        }

        var prefab = EnsurePrefab();
        var log = new List<string>();
        foreach (string path in LevelScenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var dungeon = Object.FindAnyObjectByType<DungeonGenerator>();
            if (dungeon != null)
            {
                // 地下城每次按 Play 才隨機蓋房間，編輯器裡還沒有地板：交給 DungeonGenerator 每間房間放一個
                var old = GameObject.Find(GroupName);
                if (old != null) Object.DestroyImmediate(old);
                dungeon.healthPickupPrefab = prefab;
                EditorUtility.SetDirty(dungeon);
                log.Add($"{scene.name}：隨機地城，每間戰鬥房間 + Boss 門前各一個（遊戲開始時放）");
            }
            else
            {
                int n = Scatter(prefab);
                log.Add($"{scene.name}：{n} 個補血道具" + (n == 0 ? "　⚠ 找不到可以放的地面，請手動擺放 Prefabs/HealthPickup" : ""));
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        EditorSceneManager.OpenScene(LevelScenes[0], OpenSceneMode.Single);

        Debug.Log("[GeminiGrowUp] 補血道具放置完成：\n" + string.Join("\n", log));
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("完成", string.Join("\n", log), "OK");
    }

    // ---------------------------------------------------------------- 散佈

    static int Scatter(GameObject prefab)
    {
        var old = GameObject.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old);
        var group = new GameObject(GroupName).transform;
        Physics.SyncTransforms();

        bool hasWater = Object.FindAnyObjectByType<VikingBoat>() != null;
        var placed = new List<Vector3>();
        var rng = new System.Random(1234); // 每次執行放的位置都一樣

        foreach (var anchor in Anchors())
        {
            if (placed.Count >= PerLevel) break;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float dist = 3.5f + (float)rng.NextDouble() * 4f;
                Vector3 candidate = anchor + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                if (!TryGround(anchor, candidate, hasWater, out Vector3 ground)) continue;
                if (placed.Any(p => Vector3.Distance(p, ground) < MinSpacing)) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = $"HealthPickup_{placed.Count + 1}";
                go.transform.SetParent(group, true);
                go.transform.position = ground;
                placed.Add(ground);
                break;
            }
        }
        return placed.Count;
    }

    /// <summary>放道具的中心點：玩家起點、檢查點、篝火、敵人、能力植物、Boss 房門…（越前面越優先）</summary>
    static IEnumerable<Vector3> Anchors()
    {
        var list = new List<Vector3>();
        var player = Object.FindAnyObjectByType<PlayerHealth>();
        if (player != null) list.Add(player.transform.position);
        list.AddRange(Object.FindObjectsByType<Checkpoint>().Select(c => c.transform.position));
        list.AddRange(Object.FindObjectsByType<Campfire>().Select(c => c.transform.position));
        list.AddRange(Object.FindObjectsByType<SpiderWebAttack>().Select(c => c.transform.position));
        list.AddRange(Object.FindObjectsByType<ClockEnemyHealth>().Select(c => c.transform.position));
        list.AddRange(Object.FindObjectsByType<EnemyHealth>().Select(c => c.transform.position));
        list.AddRange(Object.FindObjectsByType<BossGate>().Select(c => c.transform.position));
        foreach (var w in Object.FindObjectsByType<VikingEnemyWaveManager>())
        {
            // 波次的生成點沒填時，用 VikingEnemyWaveManager 預設的廣場周邊位置
            var pts = w.spawnPoints != null && w.spawnPoints.Count > 0
                ? w.spawnPoints
                : new List<Vector3> { new(12f, 5f, 9f), new(-30f, 9f, 13f), new(-35f, 9f, -8f), new(25f, 9f, -10f), new(-50f, 10f, 20f) };
            list.AddRange(pts);
        }
        list.AddRange(Object.FindObjectsByType<UpgradePlant>().Select(c => c.transform.position));
        return list;
    }

    /// <summary>找 candidate 底下的地面：要平坦、上方空曠、不在水裡，而且從 anchor 看得到（不會放到牆後面）</summary>
    static bool TryGround(Vector3 anchor, Vector3 candidate, bool hasWater, out Vector3 ground)
    {
        ground = default;
        // 先找 anchor 自己的地面高度，從那個高度往上一點開始往下找（室內不會打到天花板）
        float baseY = anchor.y;
        if (Physics.Raycast(anchor + Vector3.up * 1.5f, Vector3.down, out var ah, 20f, ~0, QueryTriggerInteraction.Ignore))
            baseY = ah.point.y;
        Vector3 origin = new Vector3(candidate.x, baseY + 3f, candidate.z);

        if (!Physics.Raycast(origin, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (hit.normal.y < 0.8f) return false;                          // 太陡
        if (hit.collider is CharacterController) return false;          // 打到角色
        if (hit.collider.attachedRigidbody != null) return false;       // 打到會動的木桶、石頭
        if (hasWater && hit.point.y < WaterLevel + 0.3f) return false;  // 海裡
        if (Mathf.Abs(hit.point.y - baseY) > 4f) return false;           // 高低差太大（屋頂、懸崖底下）

        Vector3 p = hit.point;
        // 上方要空曠（不是卡在牆裡或桌子底下）
        if (Physics.CheckSphere(p + Vector3.up * 0.9f, 0.45f, ~0, QueryTriggerInteraction.Ignore)) return false;
        // 從 anchor 看得到（不在牆的另一邊）
        Vector3 from = new Vector3(anchor.x, baseY + 1.2f, anchor.z);
        Vector3 to = p + Vector3.up * 1.2f;
        if (Physics.Linecast(from, to, out var block, ~0, QueryTriggerInteraction.Ignore)
            && !(block.collider is CharacterController)) return false;

        ground = p;
        return true;
    }

    // ---------------------------------------------------------------- Prefab

    static GameObject EnsurePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.9f, 0.08f, 0.08f));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1.6f, 0.1f, 0.08f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetFloat("_Smoothness", 0.7f);
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }

        var root = new GameObject("HealthPickup");
        var pickup = root.AddComponent<HealthPickup>();
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        visual.localPosition = new Vector3(0f, 0.9f, 0f);

        // 紅十字：一橫一豎兩個方塊
        Part("Vertical", visual, new Vector3(0.18f, 0.6f, 0.18f), mat);
        Part("Horizontal", visual, new Vector3(0.6f, 0.18f, 0.18f), mat);

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(visual, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.3f, 0.3f);
        light.range = 3.5f;
        light.intensity = 2f;
        light.shadows = LightShadows.None;

        pickup.visual = visual;
        var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    static void Part(string name, Transform parent, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
#endif
