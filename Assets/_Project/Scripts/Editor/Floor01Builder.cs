#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 建立第一層（走廊 + Boss 房）
///
/// 會做三件事：
///   1. 修改 BossRoom.prefab：在競技場南側（-Z）的牆開一個入口，裝上 BossGate 大門。
///      順便把光束的蓄力時間 / 鎖定時間更新成目前的建議值。
///      （已經有門的話會跳過，不會重複開洞）
///   2. 建立 Corridor_Straight.prefab：一條空的直走廊（已經存在就不覆蓋，保留你的修改）
///   3. 建立場景 Floor01.unity：玩家 → 檢查點 → 走廊 → 大門 → Boss 房
/// </summary>
public static class Floor01Builder
{
    const string PrefabDir = "Assets/Prefabs";
    const string CorridorPath = PrefabDir + "/Corridor_Straight.prefab";
    const string ScenePath = "Assets/_Project/Scenes/Floor01.unity";
    const string GateName = "BossGate";

    // 走廊尺寸
    const float CorridorLength = 30f;
    const float CorridorWidth = 6f;     // 內側寬度
    const float WallHeight = 7f;        // 跟競技場的牆一樣高
    const float WallThickness = 1f;

    [MenuItem("GeminiGrowUp/建立第一層（走廊 + Boss 房）")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string bossRoomPath = FindBossRoomPrefab();
        if (bossRoomPath == null)
        {
            EditorUtility.DisplayDialog("找不到 BossRoom", "專案裡找不到名為 BossRoom 的 Prefab。", "OK");
            return;
        }

        var wallMat = BossArenaBuilder.GetOrCreateMaterial("M_ArenaWall", new Color(0.22f, 0.21f, 0.25f));
        var floorMat = BossArenaBuilder.GetOrCreateMaterial("M_ArenaFloor", new Color(0.45f, 0.43f, 0.48f));
        var doorMat = BossArenaBuilder.GetOrCreateMaterial("M_BossDoor", new Color(0.12f, 0.1f, 0.14f));
        var glowMat = BossArenaBuilder.GetOrCreateMaterial("M_CheckpointGlow", new Color(1f, 0.8f, 0.3f), unlit: true);
        var playerMat = BossArenaBuilder.GetOrCreateMaterial("M_Player", new Color(0.9f, 0.9f, 0.95f));
        var noseMat = BossArenaBuilder.GetOrCreateMaterial("M_PlayerFront", new Color(0.9f, 0.2f, 0.2f));

        // 1) Boss 房加上入口和大門
        float radius = AddGateToBossRoom(bossRoomPath, wallMat, doorMat, out bool gateAdded);

        // 2) 走廊 Prefab
        BossArenaBuilder.EnsureFolder(PrefabDir);
        var corridorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CorridorPath);
        if (corridorPrefab == null) corridorPrefab = CreateCorridorPrefab(wallMat, floorMat);

        // 3) 場景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var bossRoomAsset = AssetDatabase.LoadAssetAtPath<GameObject>(bossRoomPath);
        var bossRoom = (GameObject)PrefabUtility.InstantiatePrefab(bossRoomAsset);
        bossRoom.transform.position = Vector3.zero;

        // 走廊的尾端接在競技場牆外側（牆厚 2m）
        float corridorEndZ = -(radius + 2f);
        var corridor = (GameObject)PrefabUtility.InstantiatePrefab(corridorPrefab);
        corridor.transform.position = new Vector3(0f, 0f, corridorEndZ - CorridorLength);

        // 玩家在走廊起點，面向 Boss 房
        float startZ = corridorEndZ - CorridorLength + 3f;
        var player = BossArenaBuilder.CreatePlayer(new Vector3(0f, 1f, startZ), playerMat, noseMat);

        // 檢查點：走廊靠近大門的地方
        var checkpoint = CreateCheckpoint(new Vector3(-2f, 0f, corridorEndZ - 6f), glowMat);
        var respawn = new GameObject("RespawnPoint").transform;
        respawn.SetParent(checkpoint.transform, false);
        respawn.position = new Vector3(0f, 1f, corridorEndZ - 7f);
        respawn.rotation = Quaternion.identity; // 面向 +Z（Boss 房）
        checkpoint.GetComponent<Checkpoint>().respawnPoint = respawn;

        // 鏡頭
        var cam = Camera.main;
        cam.nearClipPlane = 0.1f;
        cam.transform.position = new Vector3(0f, 3f, startZ - 5f);
        var tpc = cam.gameObject.AddComponent<ThirdPersonCamera>();
        tpc.target = player.transform;
        tpc.distance = 6f;
        tpc.maxDistance = 10f;
        tpc.targetOffset = new Vector3(0f, 1.8f, 0f);
        player.GetComponent<ThirdPersonController>().cameraTransform = cam.transform;

        var light = GameObject.Find("Directional Light");
        if (light != null) light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        BossArenaBuilder.AddToBuildSettings(ScenePath);
        Selection.activeGameObject = player;

        Debug.Log($"[GeminiGrowUp] 已建立 {ScenePath}。" +
                  (gateAdded ? "BossRoom.prefab 已加上入口和大門。" : "BossRoom.prefab 原本就有大門，沒有修改。"));
    }

    // =====================================================================

    internal static string FindBossRoomPrefab()
    {
        foreach (var guid in AssetDatabase.FindAssets("BossRoom t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == "BossRoom") return path;
        }
        return null;
    }

    /// <summary>在 BossRoom.prefab 的南側牆上開洞、裝門。回傳競技場半徑。</summary>
    static float AddGateToBossRoom(string path, Material wallMat, Material doorMat, out bool added)
    {
        added = false;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var floor = root.GetComponentInChildren<ArenaFloor>(true);
            float radius = floor != null ? floor.radius : 18f;
            Vector3 center = floor != null ? floor.transform.position : root.transform.position;

            // 順便更新光束的建議數值（之前改過預設值，但 Prefab 裡還存著舊值）
            var bc = root.GetComponentInChildren<BossController>(true);
            if (bc != null)
            {
                bc.autoStart = false;
                bc.beamChargeTime = 2f;
                bc.beamLockTime = 0.3f;
            }

            if (root.transform.Find(GateName) != null) return radius;

            BuildGate(root, center, radius, bc, wallMat, doorMat);
            added = true;
            return radius;
        }
        finally
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 在已載入的 BossRoom 裡建（或重建）入口大門：拆掉 -Z 方向的牆段、放門框 / 門檻 / 門板。
    /// 原本有門的話會先刪掉。
    /// </summary>
    internal static void BuildGate(GameObject root, Vector3 center, float radius, BossController bc,
                                   Material wallMat, Material doorMat)
    {
        var old = root.transform.Find(GateName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        {
            // 拆掉入口方向（-Z）的牆段（依照角度判斷，半徑變大時牆段變多也適用）
            float halfGapAngle = Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp01((CorridorWidth * 0.5f + 1.5f) / radius));
            var walls = FindDeep(root.transform, "Walls");
            if (walls != null)
            {
                var toRemove = new List<GameObject>();
                foreach (Transform w in walls)
                {
                    Vector3 d = w.position - center;
                    d.y = 0f;
                    if (Vector3.Angle(d, Vector3.back) < halfGapAngle) toRemove.Add(w.gameObject);
                }
                foreach (var go in toRemove) Object.DestroyImmediate(go);
            }
            else Debug.LogWarning("[GeminiGrowUp] BossRoom 裡找不到 Walls，入口的牆沒有拆掉。");

            // 大門（藍色 Z 軸指向競技場內）
            var gate = new GameObject(GateName);
            gate.transform.SetParent(root.transform, false);
            gate.transform.position = center + new Vector3(0f, 0f, -(radius + 1f));
            gate.transform.rotation = Quaternion.identity;

            // 門框：兩側的柱子，把拆掉牆後的縫補起來
            float frameX = CorridorWidth * 0.5f + WallThickness * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = side < 0 ? "Frame_L" : "Frame_R";
                frame.transform.SetParent(gate.transform, false);
                frame.transform.localPosition = new Vector3(side * frameX, WallHeight * 0.5f - 1f, -0.25f);
                frame.transform.localScale = new Vector3(WallThickness, WallHeight, 3f);
                frame.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
            }

            // 門檻地板：補上圓形地板和走廊之間的縫（比地板低 2cm，避免跟方塊重疊閃爍）
            var threshold = GameObject.CreatePrimitive(PrimitiveType.Cube);
            threshold.name = "Threshold";
            threshold.transform.SetParent(gate.transform, false);
            threshold.transform.localPosition = new Vector3(0f, -0.52f, -0.5f);
            threshold.transform.localScale = new Vector3(CorridorWidth + WallThickness * 2f, 1f, 3.5f);
            threshold.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

            // 門板
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "Door";
            door.transform.SetParent(gate.transform, false);
            door.transform.localPosition = new Vector3(0f, WallHeight * 0.5f - 1f, 0f);
            door.transform.localScale = new Vector3(CorridorWidth, WallHeight, 0.6f);
            door.GetComponent<MeshRenderer>().sharedMaterial = doorMat;

            var bg = gate.AddComponent<BossGate>();
            bg.boss = bc;
            bg.door = door.transform;
        }
    }

    /// <summary>一條空的直走廊：起點在本地 Z = 0，往 +Z 延伸，尾端開放接 Boss 房</summary>
    static GameObject CreateCorridorPrefab(Material wallMat, Material floorMat)
    {
        var root = new GameObject("Corridor_Straight");
        float halfW = CorridorWidth * 0.5f;

        Box("Floor", root.transform, new Vector3(0f, -0.5f, CorridorLength * 0.5f),
            new Vector3(CorridorWidth + WallThickness * 2f, 1f, CorridorLength), floorMat);
        Box("Wall_L", root.transform, new Vector3(-(halfW + WallThickness * 0.5f), WallHeight * 0.5f - 1f, CorridorLength * 0.5f),
            new Vector3(WallThickness, WallHeight, CorridorLength), wallMat);
        Box("Wall_R", root.transform, new Vector3(halfW + WallThickness * 0.5f, WallHeight * 0.5f - 1f, CorridorLength * 0.5f),
            new Vector3(WallThickness, WallHeight, CorridorLength), wallMat);
        Box("Wall_Back", root.transform, new Vector3(0f, WallHeight * 0.5f - 1f, -WallThickness * 0.5f),
            new Vector3(CorridorWidth + WallThickness * 2f, WallHeight, WallThickness), wallMat);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, CorridorPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject CreateCheckpoint(Vector3 pos, Material glowMat)
    {
        var cp = new GameObject("Checkpoint");
        cp.transform.position = pos;

        var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stand.name = "Stand";
        stand.transform.SetParent(cp.transform, false);
        stand.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        stand.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);

        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "Glow";
        Object.DestroyImmediate(glow.GetComponent<Collider>());
        glow.transform.SetParent(cp.transform, false);
        glow.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        glow.transform.localScale = Vector3.one * 0.5f;
        var r = glow.GetComponent<MeshRenderer>();
        r.sharedMaterial = glowMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var c = cp.AddComponent<Checkpoint>();
        c.glow = r;
        return cp;
    }

    static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    internal static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
#endif
