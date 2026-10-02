#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 選單：GeminiGrowUp > 更新第二層（寬走廊 + 小怪）
///
/// 直接修改現有的 Floor02 場景（不會重建整個場景，PlayerRig、BossRoom、檢查點都保留）：
///   1. 建立 Corridor_Wide.prefab：寬 16m、長 90m 的走廊，靠 Boss 房那端只留跟大門一樣寬的開口
///   2. 建立小怪 Prefab：Enemy_Grunt（一般小怪）、Enemy_Ambusher（陷阱小怪，是 Grunt 的 Variant）
///   3. Floor02：舊走廊換成新走廊、玩家移到新走廊的起點、放入小怪和一個陷阱
///
/// 重複執行時：走廊 / 小怪 Prefab 已經存在就不覆蓋（保留你的修改），
/// 場景裡由這個工具放的 Enemies、Ambush_ 物件會先刪掉再重放。
/// </summary>
public static class Floor02Updater
{
    const string ScenePath = "Assets/_Project/Scenes/Floor02.unity";
    const string PrefabDir = "Assets/_Project/Prefabs";
    const string CorridorPath = PrefabDir + "/Corridor_Wide.prefab";
    const string OldCorridorPath = PrefabDir + "/Corridor_Straight.prefab";
    const string GruntPath = PrefabDir + "/Enemy_Grunt.prefab";
    const string AmbusherPath = PrefabDir + "/Enemy_Ambusher.prefab";

    const float CorridorWidth = 16f;   // 內側寬度
    const float CorridorLength = 90f;  // 原本 30m 的三倍
    const float DoorWidth = 6f;        // 跟 BossRoom 大門一樣（門不變）
    const float WallHeight = 7f;
    const float WallThickness = 1f;

    [MenuItem("GeminiGrowUp/更新第二層（寬走廊 + 小怪）")]
    public static void Apply()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            EditorUtility.DisplayDialog("找不到 Floor02", $"找不到 {ScenePath}。", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("更新第二層",
                "會修改 Floor02 場景：換成寬 16m、長 90m 的走廊，放入小怪和陷阱，並把玩家移到走廊起點。\n" +
                "PlayerRig、BossRoom、檢查點會保留。建議先備份或 commit。要繼續嗎？",
                "繼續", "取消"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BossArenaBuilder.EnsureFolder(PrefabDir);

        // ---------- 材質 ----------
        var wallMat = BossArenaBuilder.GetOrCreateMaterial("M_ArenaWall", new Color(0.22f, 0.21f, 0.25f));
        var floorMat = BossArenaBuilder.GetOrCreateMaterial("M_ArenaFloor", new Color(0.45f, 0.43f, 0.48f));
        var enemyMat = BossArenaBuilder.GetOrCreateMaterial("M_Enemy", new Color(0.55f, 0.12f, 0.12f));
        var ambusherMat = BossArenaBuilder.GetOrCreateMaterial("M_EnemyAmbusher", new Color(0.25f, 0.35f, 0.15f));
        var eyeMat = BossArenaBuilder.GetOrCreateMaterial("M_BossEye", new Color(1f, 0.85f, 0.2f), unlit: true);
        var bladeMat = BossArenaBuilder.GetOrCreateMaterial("M_EnemyBlade", new Color(0.35f, 0.33f, 0.3f));

        // ---------- Prefab（已存在就沿用） ----------
        var corridorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CorridorPath);
        if (corridorPrefab == null) corridorPrefab = CreateCorridorPrefab(wallMat, floorMat);
        var gruntPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GruntPath);
        if (gruntPrefab == null) gruntPrefab = CreateGruntPrefab(enemyMat, eyeMat, bladeMat);
        var ambusherPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AmbusherPath);
        if (ambusherPrefab == null) ambusherPrefab = CreateAmbusherPrefab(gruntPrefab, ambusherMat);

        // ---------- 找出競技場的入口位置 ----------
        var arena = FindInScene<ArenaFloor>(scene);
        Vector3 center = arena != null ? arena.transform.position : Vector3.zero;
        float radius = arena != null ? arena.radius : 36f;
        float endZ = center.z - (radius + 2f);   // 走廊接到競技場牆外側

        // d = 離走廊起點（最裡面那面牆）多遠，x = 左右
        Vector3 At(float d, float x) => new Vector3(center.x + x, 0f, endZ - CorridorLength + d);

        // ---------- 拆掉舊走廊、舊的小怪 ----------
        foreach (var root in scene.GetRootGameObjects())
        {
            string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            bool oldCorridor = src == OldCorridorPath || src == CorridorPath;
            bool oldEnemies = root.name == "Enemies" || root.name.StartsWith("Ambush_");
            if (oldCorridor || oldEnemies) Object.DestroyImmediate(root);
        }

        // ---------- 新走廊 ----------
        var corridor = (GameObject)PrefabUtility.InstantiatePrefab(corridorPrefab, scene);
        corridor.transform.SetPositionAndRotation(new Vector3(center.x, 0f, endZ), Quaternion.identity);

        // ---------- 玩家移到走廊起點 ----------
        var playerHealth = FindInScene<PlayerHealth>(scene);
        if (playerHealth != null)
        {
            playerHealth.transform.SetPositionAndRotation(At(3f, 0f) + Vector3.up, Quaternion.identity);
            var cam = FindInScene<ThirdPersonCamera>(scene);
            if (cam != null) cam.transform.position = At(-2f, 0f) + Vector3.up * 3f;
        }
        else Debug.LogWarning("[GeminiGrowUp] Floor02 裡找不到玩家（PlayerHealth），玩家位置沒有移動。");

        // ---------- 一般小怪 ----------
        var enemies = new GameObject("Enemies");
        SceneManager.MoveGameObjectToScene(enemies, scene);
        Quaternion faceEntrance = Quaternion.Euler(0f, 180f, 0f); // 面向玩家走來的方向

        PlaceEnemy(gruntPrefab, enemies.transform, At(22f, -3f), faceEntrance, scene);   // 第一組：兩隻
        PlaceEnemy(gruntPrefab, enemies.transform, At(22f, 3f), faceEntrance, scene);
        PlaceEnemy(gruntPrefab, enemies.transform, At(38f, -5f), faceEntrance, scene);   // 單獨一隻
        PlaceEnemy(gruntPrefab, enemies.transform, At(68f, -4f), faceEntrance, scene);   // 最後一組：三隻
        PlaceEnemy(gruntPrefab, enemies.transform, At(68f, 0f), faceEntrance, scene);
        PlaceEnemy(gruntPrefab, enemies.transform, At(68f, 4f), faceEntrance, scene);

        // ---------- 陷阱：三隻從天而降 ----------
        var ambushGo = new GameObject("Ambush_01");
        SceneManager.MoveGameObjectToScene(ambushGo, scene);
        ambushGo.transform.position = At(50f, 0f) + Vector3.up * 3f;
        var trap = ambushGo.AddComponent<AmbushTrigger>();
        trap.size = new Vector3(CorridorWidth, 6f, 10f);
        for (int i = 0; i < 3; i++)
        {
            var a = PlaceEnemy(ambusherPrefab, ambushGo.transform, At(50f, (i - 1) * 3f), faceEntrance, scene);
            a.SetActive(false); // 平常隱藏，觸發後才出現
            trap.ambushers.Add(a.GetComponent<EnemyAI>());
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = ambushGo;
        Debug.Log("[GeminiGrowUp] Floor02 已更新：寬走廊、6 隻小怪、1 個陷阱（3 隻陷阱小怪）。");
    }

    // =====================================================================

    static GameObject PlaceEnemy(GameObject prefab, Transform parent, Vector3 groundPos, Quaternion rot, Scene scene)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(groundPos + Vector3.up * 1.05f, rot); // 膠囊中心離地 1m
        return go;
    }

    /// <summary>
    /// 寬走廊：軸心在靠 Boss 房那一端（本地 Z = 0），往 -Z 延伸 CorridorLength。
    /// 靠 Boss 房那面牆只留跟大門一樣寬的開口（門框會補在開口兩側）。
    /// </summary>
    static GameObject CreateCorridorPrefab(Material wallMat, Material floorMat)
    {
        var root = new GameObject("Corridor_Wide");
        float halfW = CorridorWidth * 0.5f;
        float L = CorridorLength;
        float wy = WallHeight * 0.5f - 1f;

        Box("Floor", root.transform, new Vector3(0f, -0.5f, -L * 0.5f),
            new Vector3(CorridorWidth + WallThickness * 2f, 1f, L), floorMat);
        Box("Wall_L", root.transform, new Vector3(-(halfW + WallThickness * 0.5f), wy, -L * 0.5f),
            new Vector3(WallThickness, WallHeight, L), wallMat);
        Box("Wall_R", root.transform, new Vector3(halfW + WallThickness * 0.5f, wy, -L * 0.5f),
            new Vector3(WallThickness, WallHeight, L), wallMat);
        Box("Wall_Back", root.transform, new Vector3(0f, wy, -L - WallThickness * 0.5f),
            new Vector3(CorridorWidth + WallThickness * 2f, WallHeight, WallThickness), wallMat);

        // 前牆：中間留大門的開口（門寬 + 兩側門框）
        float doorEdge = DoorWidth * 0.5f + WallThickness;          // 門框外緣
        float segW = halfW + WallThickness - doorEdge;
        float segX = doorEdge + segW * 0.5f;
        Box("Wall_Front_L", root.transform, new Vector3(-segX, wy, -WallThickness * 0.5f),
            new Vector3(segW, WallHeight, WallThickness), wallMat);
        Box("Wall_Front_R", root.transform, new Vector3(segX, wy, -WallThickness * 0.5f),
            new Vector3(segW, WallHeight, WallThickness), wallMat);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, CorridorPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>一般小怪：跟玩家一樣的膠囊 + CharacterController + ThirdPersonController（AI 控制）</summary>
    static GameObject CreateGruntPrefab(Material bodyMat, Material eyeMat, Material bladeMat)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "Enemy_Grunt";
        Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());
        root.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

        var cc = root.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = Vector3.zero;
        cc.stepOffset = 0.35f;
        cc.slopeLimit = 45f;
        cc.skinWidth = 0.05f;

        // 眼睛（看得出面向哪邊）
        var eyes = GameObject.CreatePrimitive(PrimitiveType.Cube);
        eyes.name = "Eyes";
        Object.DestroyImmediate(eyes.GetComponent<BoxCollider>());
        eyes.transform.SetParent(root.transform, false);
        eyes.transform.localPosition = new Vector3(0f, 0.55f, 0.42f);
        eyes.transform.localScale = new Vector3(0.55f, 0.12f, 0.2f);
        eyes.GetComponent<MeshRenderer>().sharedMaterial = eyeMat;

        var weapon = MeleeCombat.CreateDefaultWeapon(root.transform, bladeMat.color, bladeMat);

        var tpc = root.AddComponent<ThirdPersonController>();
        tpc.aiControlled = true;

        var combat = root.AddComponent<EnemyCombat>();
        combat.weaponPivot = weapon;
        combat.comboDamage = new[] { 8f, 8f, 14f };
        combat.windupTime = 0.35f;
        combat.swingTime = 0.16f;
        combat.recoveryTime = 0.35f;
        combat.finisherRecoveryTime = 0.7f;
        combat.hitForwardOffset = 1.1f;
        combat.hitRadius = 1.1f;
        combat.attackMoveMultiplier = 0.1f;
        combat.lungeForce = 4f;

        var hp = root.AddComponent<EnemyHealth>();
        hp.maxHealth = 40f;
        root.AddComponent<EnemyAI>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, GruntPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>陷阱小怪：Grunt 的 Prefab Variant，換顏色、血少一點（改 Grunt 時它也會跟著更新）</summary>
    static GameObject CreateAmbusherPrefab(GameObject gruntPrefab, Material bodyMat)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(gruntPrefab);
        inst.name = "Enemy_Ambusher";
        inst.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;
        inst.GetComponent<EnemyHealth>().maxHealth = 30f;

        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, AmbusherPath); // 從 Prefab 實例存檔 = Prefab Variant
        Object.DestroyImmediate(inst);
        return prefab;
    }

    static void Box(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }
}
#endif
