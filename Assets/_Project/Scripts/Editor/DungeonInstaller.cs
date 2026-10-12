#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝隨機房間（第二關）
///
/// 把 Floor02 的戰前走廊換成每次都隨機產生的房間（類似元氣騎士），並更新 Boss：
///   1. Floor02 場景：
///      - 加入「Dungeon」物件（DungeonGenerator），填好城堡零件、門和火把的材質、小怪 Prefab
///      - 原本手工擺的走廊（Corridor_Wide）、小怪（Enemies）、伏擊點（Ambush_…）設成「停用」
///        （沒有刪掉，Prefab 檔案也還在；確認不需要之後可以自己刪）
///   2. BossRoom.prefab：
///      - 進門後不再打兩波小怪，Boss 直接從天而降（波次的程式還在，勾回去就有）
///      - 光束關掉，新增召喚技能並填好小怪 Prefab
///   3. Golem.fbx 如果有新的召喚動畫但 Animator 還沒有，會順便重新安裝一次 Boss 模型
///
/// 房間是「按 Play 之後」才蓋出來的，所以編輯模式下 Boss 房外面是空的，這是正常的。
/// 房間數量、每間幾隻小怪等數值在 Dungeon 物件的 DungeonGenerator 上調。可以重複執行。
/// </summary>
public static class DungeonInstaller
{
    const string ScenePath = "Assets/_Project/Scenes/Floor02.unity";
    const string KitDir = "Assets/_Project/ThirdParty/Kenney/RetroFantasyKit";
    const string PrefabDir = "Assets/_Project/Prefabs";
    const string GeneratorName = "Dungeon";

    [MenuItem("GeminiGrowUp/安裝隨機房間（第二關）")]
    public static void Install()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            EditorUtility.DisplayDialog("找不到 Floor02", $"找不到 {ScenePath}。", "OK");
            return;
        }
        var grunt = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Enemy_Grunt.prefab");
        var ambusher = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Enemy_Ambusher.prefab");
        if (grunt == null)
        {
            EditorUtility.DisplayDialog("找不到小怪", $"找不到 {PrefabDir}/Enemy_Grunt.prefab。\n請先執行「更新第二層（寬走廊 + 小怪）」。", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("安裝隨機房間",
                "會修改：\n" +
                "・Floor02 場景：加入隨機房間的產生器；原本的走廊、小怪、伏擊點設成停用（不會刪掉）\n" +
                "・BossRoom.prefab：取消戰前兩波小怪、關掉光束、新增召喚技能\n\n" +
                "建議先在 GitHub Desktop commit。要繼續嗎？", "繼續", "取消"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new List<string>();

        // 改 Prefab 的時候先換成空場景，避免場景開著互相干擾
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        log.Add(UpdateBossRoom(grunt, ambusher));
        if (GolemInstaller.NeedsSummonUpdate())
            log.Add(GolemInstaller.Reinstall() ? "Boss 模型：已重新安裝（加入召喚動畫）"
                                               : "⚠ Boss 模型重新安裝失敗，召喚時不會有動作。請手動執行「安裝 或 更新 Boss 模型（骸骨巨像）」");

        log.Add(UpdateScene(grunt, ambusher));

        AssetDatabase.SaveAssets();
        Debug.Log("[GeminiGrowUp] 隨機房間安裝完成：\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("完成", string.Join("\n", log) + "\n\n房間要按 Play 之後才會蓋出來。", "OK");
    }

    // =====================================================================
    //  BossRoom.prefab
    // =====================================================================

    static string UpdateBossRoom(GameObject grunt, GameObject ambusher)
    {
        string path = Floor02Builder.FindBossRoomPrefab();
        if (path == null) return "⚠ 找不到 BossRoom.prefab，Boss 沒有修改";

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var notes = new List<string>();
            var boss = root.GetComponentInChildren<BossController>(true);
            // 只有第一次安裝（還沒填過召喚的小怪）才改這些開關；之後你自己調過的設定，重複執行不會被蓋掉
            bool firstTime = boss != null && boss.summonGruntPrefab == null;
            if (boss != null)
            {
                if (firstTime)
                {
                    boss.enableBeam = false;
                    boss.enableSummon = true;
                    notes.Add("光束關閉、新增召喚");
                }
                if (boss.summonGruntPrefab == null) boss.summonGruntPrefab = grunt;
                if (boss.summonAmbusherPrefab == null) boss.summonAmbusherPrefab = ambusher;
            }

            var gate = root.GetComponentInChildren<BossGate>(true);
            if (gate != null)
            {
                // 波次控制留著（Boss 從天而降的登場演出是它負責的），只是兩波都不打
                gate.wavesBeforeBoss = true;
                var waves = gate.GetComponent<ArenaWaves>();
                if (waves == null) waves = gate.gameObject.AddComponent<ArenaWaves>();
                gate.waves = waves;
                if (waves.boss == null) waves.boss = boss;
                if (firstTime)
                {
                    waves.playWave1 = false;
                    waves.playWave2 = false;
                    notes.Add("進門後 Boss 直接登場");
                }
                if (waves.gruntPrefab == null) waves.gruntPrefab = grunt;
                if (waves.ambusherPrefab == null) waves.ambusherPrefab = ambusher != null ? ambusher : grunt;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            if (boss == null || gate == null) return "⚠ BossRoom 裡找不到 BossController 或 BossGate";
            return "Boss 房：" + (notes.Count > 0 ? string.Join("、", notes) : "之前已經安裝過，保留目前的設定");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =====================================================================
    //  Floor02 場景
    // =====================================================================

    static string UpdateScene(GameObject grunt, GameObject ambusher)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 舊的手工走廊、小怪、伏擊點：停用
        var disabled = new List<string>();
        foreach (var go in scene.GetRootGameObjects())
        {
            string n = go.name;
            bool old = n.StartsWith("Corridor_") || n == "Enemies" || n.StartsWith("Ambush_");
            if (old && go.activeSelf)
            {
                go.SetActive(false);
                // Prefab 的實例要另外記錄這個修改，不然存檔時可能不會存進去
                if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.RecordPrefabInstancePropertyModifications(go);
                disabled.Add(n);
            }
        }

        // 產生器
        DungeonGenerator gen = null;
        foreach (var go in scene.GetRootGameObjects())
        {
            gen = go.GetComponentInChildren<DungeonGenerator>(true);
            if (gen != null) break;
        }
        bool created = gen == null;
        if (created) gen = new GameObject(GeneratorName).AddComponent<DungeonGenerator>();

        if (gen.gruntPrefab == null) gen.gruntPrefab = grunt;
        if (gen.ambusherPrefab == null) gen.ambusherPrefab = ambusher;

        var missing = new List<string>();
        var kit = gen.kit ?? (gen.kit = new DungeonKit());
        kit.wall = Kit("wall-fortified", missing);
        kit.wallWindow = Kit("wall-fortified-window", missing);
        kit.wallPaint = Kit("wall-fortified-paint", missing);
        kit.battlement = Kit("battlement", missing);
        kit.floor = Kit("floor-flat", missing);
        var props = new List<GameObject>();
        foreach (var n in new[] { "barrels", "detail-crate", "detail-barrel" })
        {
            var p = Kit(n, missing);
            if (p != null) props.Add(p);
        }
        kit.props = props.ToArray();
        kit.doorMaterial = BossArenaBuilder.GetOrCreateMaterial("M_DungeonDoor", new Color(0.3f, 0.22f, 0.16f));
        var planks = AssetDatabase.LoadAssetAtPath<Texture2D>($"{KitDir}/Textures/planks.png");
        if (planks != null)
        {
            kit.doorMaterial.SetTexture("_BaseMap", planks);
            kit.doorMaterial.SetTexture("_MainTex", planks);
            kit.doorMaterial.SetColor("_BaseColor", new Color(0.6f, 0.5f, 0.45f));
            kit.doorMaterial.SetColor("_Color", new Color(0.6f, 0.5f, 0.45f));
            EditorUtility.SetDirty(kit.doorMaterial);
        }
        kit.torchWoodMaterial = BossArenaBuilder.GetOrCreateMaterial("M_TorchWood", new Color(0.22f, 0.15f, 0.1f));
        kit.torchFlameMaterial = BossArenaBuilder.GetOrCreateMaterial("M_TorchFlame", new Color(1f, 0.6f, 0.2f), unlit: true);

        EditorUtility.SetDirty(gen);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = gen.gameObject;

        string result = "Floor02：" + (created ? "加入了" : "更新了") + " Dungeon（隨機房間產生器）";
        if (disabled.Count > 0) result += "；已停用 " + string.Join("、", disabled);
        if (missing.Count > 0) result += $"\n⚠ {KitDir} 裡找不到：{string.Join("、", missing)}（這些會用灰色方塊代替）";
        return result;
    }

    static GameObject Kit(string name, List<string> missing)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitDir}/{name}.fbx");
        if (go == null) missing.Add(name);
        return go;
    }
}
#endif
