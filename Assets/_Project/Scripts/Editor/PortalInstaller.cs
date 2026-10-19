#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 設定關卡流程（傳送門、能力植物）
///
/// 1. 建立傳送門外觀用的材質與 Assets/_Project/Prefabs/Portal.prefab（已經有就沿用）
/// 2. Build Settings 設成：主選單 → 星球 → 維京村莊 → 地下城（關卡順序要和 GameManager.levels 一樣）
/// 3. 星球：
///      - 只放一個傳送門 Portal_Exit 在 Boss 附近的火堆旁：一直都在，但鎖著，打倒時鐘敵人（ClockEnemyHealth）才打開 → 維京村莊
///      - 蜘蛛（SpiderWebAttack）附近 PlantSearchRadius 公尺內的植物（Succulent 開頭、不是 Herb 採集點）
///        掛上 UpgradePlant：撿起後三選一升級
///      - Boss 戰的重生點用場景裡的石碑檢查點 BossCheckpoint_StoneStele（以前自動放的 Checkpoint_Boss 會被拿掉）
/// 4. 維京村莊靠搭船、地下城是最後一關，都不放傳送門；船改成交給 GameManager 決定下一關
/// 每次執行會先刪掉場景裡擺好的傳送門，所以可以重複執行。
/// </summary>
public static class PortalInstaller
{
    const string Root = "Assets/_Project";
    const string PrefabPath = Root + "/Prefabs/Portal.prefab";
    const string StoneMatPath = Root + "/Materials/M_PortalStone.mat";
    const string SurfaceMatPath = Root + "/Materials/M_PortalSurface.mat";
    const float PlantSearchRadius = 35f;
    const string OldBossCheckpointName = "Checkpoint_Boss";
    const string BossSteleName = "BossCheckpoint_StoneStele";
    const float PortalFromFire = 6f;

    const string MainMenuScene = Root + "/Scenes/MainMenu.unity";

    static readonly string[] LevelScenes =
    {
        Root + "/Scenes/Planet.unity",
        Root + "/Scenes/The_Viking_Village.unity",
        Root + "/Scenes/Floor02.unity",
    };

    [MenuItem("GeminiGrowUp/設定關卡流程（傳送門、能力植物）")]
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            if (!EditorUtility.DisplayDialog("設定關卡流程",
                    "會做這些事：\n" +
                    "・Build Settings 改成：主選單 → 星球 → 維京村莊 → 地下城\n" +
                    "・星球：火堆旁放一個傳送門（打倒時鐘敵人才打開）；蜘蛛附近的植物改成三選一能力植物\n" +
                    "・刪掉場景裡原本擺好的傳送門\n" +
                    "・維京村莊的船改成依關卡順序前往下一關\n\n" +
                    "建議先 commit。要繼續嗎？", "繼續", "取消"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        }

        var prefab = EnsurePrefab();

        EditorBuildSettings.scenes = new[] { MainMenuScene }.Concat(LevelScenes)
            .Select(p => new EditorBuildSettingsScene(p, true)).ToArray();

        SetupScene(LevelScenes[0], () =>
        {
            SetupClockEnemyBoss(prefab);
            SetupUpgradePlants();
            // 第一關不放補血道具
            var pickups = GameObject.Find("HealthPickups");
            if (pickups != null) Object.DestroyImmediate(pickups);
        });
        SetupScene(LevelScenes[1], () =>
        {
            var boat = Object.FindAnyObjectByType<VikingBoat>();
            if (boat != null)
            {
                boat.nextSceneName = "";
                EditorUtility.SetDirty(boat);
            }
        });
        SetupScene(LevelScenes[2], () => { }); // 最後一關，打倒 Boss 就結束

        EditorSceneManager.OpenScene(LevelScenes[0], OpenSceneMode.Single);
        Debug.Log("[PortalInstaller] 完成：關卡流程已設定，Build Settings 已更新。");
    }

    static void SetupScene(string path, System.Action setup)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        foreach (var old in Object.FindObjectsByType<Portal>(FindObjectsInactive.Include))
            Object.DestroyImmediate(old.gameObject);

        setup();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// 星球：只有一個傳送門 Portal_Exit，擺在離 Boss 最近的火堆（Campfire）旁邊 PortalFromFire 公尺、面向 Boss 戰檢查點。
    /// 傳送門一直都在，但一開始鎖著（Start Locked），打倒 Boss 時 ClockEnemyHealth 會呼叫 Unlock() 才能進入。
    /// 想換位置直接在場景裡拖 Portal_Exit 就好。也會拿掉以前掛上的 SpawnOnDeath
    /// </summary>
    static void SetupClockEnemyBoss(GameObject portalPrefab)
    {
        var enemies = Object.FindObjectsByType<ClockEnemyHealth>(FindObjectsInactive.Include);
        if (enemies.Length == 0)
        {
            Debug.LogWarning("[PortalInstaller] 星球裡找不到時鐘敵人（ClockEnemyHealth），沒辦法放傳送門和 Boss 戰檢查點！");
            return;
        }
        var boss = enemies[0];

        foreach (var enemy in enemies)
        {
            var spawner = enemy.GetComponent<SpawnOnDeath>();
            if (spawner != null) Undo.DestroyObjectImmediate(spawner);
        }

        var player = Object.FindAnyObjectByType<PlayerHealth>();
        Vector3 bossPos = boss.transform.position;
        Vector3 playerStart = player != null ? player.transform.position : bossPos + Vector3.back * 20f;
        // Boss 戰的重生點用場景裡的石碑檢查點（BossCheckpoint_StoneStele）；以前自動放的 Checkpoint_Boss 拿掉
        var oldCheckpoint = GameObject.Find(OldBossCheckpointName);
        if (oldCheckpoint != null) Object.DestroyImmediate(oldCheckpoint);
        var stele = GameObject.Find(BossSteleName);
        Vector3 checkpointPos = stele != null ? stele.transform.position : playerStart;

        // 傳送門：離 Boss 最近的火堆旁邊，往石碑（玩家走過來的方向）偏一點
        var fires = Object.FindObjectsByType<Campfire>(FindObjectsInactive.Include);
        Vector3 anchor = fires.Length > 0
            ? fires.OrderBy(f => Vector3.Distance(f.transform.position, bossPos)).First().transform.position
            : bossPos;
        Vector3 toCheckpoint = checkpointPos - anchor;
        toCheckpoint.y = 0f;
        toCheckpoint = toCheckpoint.sqrMagnitude > 0.01f ? toCheckpoint.normalized : Vector3.back;
        Vector3 pos = anchor + toCheckpoint * PortalFromFire;
        if (Physics.Raycast(new Vector3(pos.x, anchor.y + 40f, pos.z), Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore))
            pos = hit.point;

        var portalGO = (GameObject)PrefabUtility.InstantiatePrefab(portalPrefab);
        portalGO.name = "Portal_Exit";
        portalGO.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toCheckpoint));
        var portal = portalGO.GetComponent<Portal>();
        portal.destination = Portal.Destination.NextLevel;
        portal.startLocked = true; // 打倒 Boss 才打開
        EditorUtility.SetDirty(portal);
        PrefabUtility.RecordPrefabInstancePropertyModifications(portal);
        Debug.Log($"[PortalInstaller] 傳送門放在火堆旁 {pos}，打倒 {boss.name} 後才能進入");
    }

    static void SetupUpgradePlants()
    {
        var spiders = Object.FindObjectsByType<SpiderWebAttack>(FindObjectsInactive.Include);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (!t.name.StartsWith("Succulent") || t.GetComponentInParent<HarvestablePlant>(true) != null) continue;
            // 只處理植物的最外層（Prefab 根物件），不要把子物件也各掛一個
            if (t.parent != null && t.parent.name.StartsWith("Succulent")) continue;
            if (!spiders.Any(s => Vector3.Distance(s.transform.position, t.position) <= PlantSearchRadius)) continue;

            if (t.GetComponent<UpgradePlant>() == null)
            {
                var plant = Undo.AddComponent<UpgradePlant>(t.gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(plant);
            }
            Debug.Log($"[PortalInstaller] {t.name} {t.position}：改成能力植物");
        }
    }

    // ---------------------------------------------------------------- 外觀

    static GameObject EnsurePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;

        var stone = EnsureMaterial(StoneMatPath, new Color(0.32f, 0.3f, 0.34f), Color.black);
        var swirl = EnsureMaterial(SurfaceMatPath, new Color(0.45f, 0.6f, 1f), new Color(0.9f, 1.2f, 2f));

        var root = new GameObject("Portal");
        var portal = root.AddComponent<Portal>();
        const float centerY = 2.2f, radius = 1.8f;

        var baseStep = Part(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.15f, 0f), Quaternion.identity, new Vector3(4.6f, 0.3f, 1.4f), stone);
        Object.DestroyImmediate(baseStep.GetComponent<Collider>());

        var ring = new GameObject("Ring").transform;
        ring.SetParent(root.transform, false);
        const int segments = 18;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            var seg = Part(PrimitiveType.Cube, "Stone_" + i, ring,
                new Vector3(Mathf.Cos(a) * radius, centerY + Mathf.Sin(a) * radius, 0f),
                Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg),
                new Vector3(0.55f, 0.75f, 0.6f), stone);
            Object.DestroyImmediate(seg.GetComponent<Collider>());
        }

        var surface = Part(PrimitiveType.Cylinder, "Surface", root.transform, new Vector3(0f, centerY, 0f),
            Quaternion.Euler(90f, 0f, 0f), new Vector3(radius * 2f - 0.2f, 0.02f, radius * 2f - 0.2f), swirl);
        Object.DestroyImmediate(surface.GetComponent<Collider>());

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, centerY, 1f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 9f;
        light.intensity = 3f;
        light.color = portal.openColor;

        portal.surface = surface.GetComponent<Renderer>();
        portal.glowLight = light;

        var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material EnsureMaterial(string path, Color baseColor, Color emission)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", baseColor);
        if (emission != Color.black)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
#endif
