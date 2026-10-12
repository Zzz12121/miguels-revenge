#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 套用城堡素材（走廊 + Boss 房 + 燈光）
///
/// 用 Kenney Retro Fantasy Kit 把走廊和 Boss 房換成城堡外觀：
///   - 原本的方塊牆 / 地板「保留碰撞、隱藏外觀」，再把城堡零件疊上去當外觀
///     → 玩家、小怪、Boss 的移動和碰撞完全不變
///   - 城堡零件都放在名為 "CastleDressing" 的物件底下，而且拿掉了碰撞（只有木桶、木箱有碰撞）
///   - 競技場地板（會塌陷、會預警變色的方塊）只換貼圖，機制不受影響
///   - 走廊和競技場加上會閃爍的火把，Floor02 場景加上霧、把陽光調暗
///
/// 可以重複執行：會先刪掉舊的 CastleDressing 再重新擺。
/// 想回到方塊外觀：刪掉 CastleDressing，再把隱藏的方塊的 Mesh Renderer 打勾。
/// </summary>
public static class CastleDresser
{
    const string KitDir = "Assets/_Project/ThirdParty/Kenney/RetroFantasyKit";
    const string CorridorPath = "Assets/_Project/Prefabs/Corridor_Wide.prefab";
    const string ScenePath = "Assets/_Project/Scenes/Floor02.unity";
    const string DressingName = "CastleDressing";

    const float U = 4f;   // 一個城堡零件 = 4m（KenneyImportSettings 設定的放大倍率）

    // 走廊尺寸（要跟 Floor02Updater 一樣）
    const float CorridorHalfWidth = 8f;
    const float CorridorLength = 90f;

    static readonly Dictionary<string, GameObject> kit = new Dictionary<string, GameObject>();
    static float frontSign = 1f;   // 零件的「正面」在 Unity 裡是 +Z（1）還是 -Z（-1）
    static System.Random rng;
    static Material torchWoodMat, torchFlameMat;

    [MenuItem("GeminiGrowUp/套用城堡素材（走廊 + Boss 房 + 燈光）")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("套用城堡素材",
                "會修改：\n" +
                "・Corridor_Wide.prefab：牆、地板換成城堡零件，加上火把和木桶\n" +
                "・BossRoom.prefab：環形牆換成城牆和塔、大門兩側加塔、地板換石板貼圖、加上火把\n" +
                "・Floor02 場景：加上霧、陽光調暗\n\n" +
                "碰撞和玩法不變。建議先在 GitHub Desktop commit。要繼續嗎？", "繼續", "取消"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (!LoadKit()) return;
        rng = new System.Random(1234); // 固定亂數：每次執行擺出來都一樣

        torchWoodMat = BossArenaBuilder.GetOrCreateMaterial("M_TorchWood", new Color(0.22f, 0.15f, 0.1f));
        torchFlameMat = BossArenaBuilder.GetOrCreateMaterial("M_TorchFlame", new Color(1f, 0.6f, 0.2f), unlit: true);

        // Floor02 場景先換成空場景，避免改 Prefab 時場景開著
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var log = new List<string>();
        log.Add(DressCorridor());
        log.Add(DressBossRoom());
        log.Add(SetupSceneLighting());

        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(ScenePath);
        Debug.Log("[GeminiGrowUp] 城堡素材套用完成：\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("完成", string.Join("\n", log), "OK");
    }

    // =====================================================================
    //  素材
    // =====================================================================

    static readonly string[] KitNames =
    {
        "wall-fortified", "wall-fortified-window", "wall-fortified-paint", "wall-fortified-gate",
        "battlement", "tower", "tower-base", "tower-top", "floor-flat", "barrels", "detail-crate", "detail-barrel",
    };

    static bool LoadKit()
    {
        kit.Clear();
        var missing = new List<string>();
        foreach (var n in KitNames)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitDir}/{n}.fbx");
            if (go == null) missing.Add(n);
            else kit[n] = go;
        }
        if (missing.Count > 0)
        {
            EditorUtility.DisplayDialog("找不到素材", $"{KitDir} 裡找不到：\n" + string.Join("、", missing), "OK");
            return false;
        }

        // 量一下零件：大小要是 4m，並找出正面方向（城垛凸出在零件的「背面」）
        var probe = Object.Instantiate(kit["battlement"]);
        var block = Object.Instantiate(kit["wall-fortified"]);
        try
        {
            Bounds bb = MeshBounds(probe), wb = MeshBounds(block);
            if (Mathf.Abs(wb.size.y - U) > 0.05f)
            {
                EditorUtility.DisplayDialog("零件大小不對",
                    $"城牆零件的高度是 {wb.size.y:0.##}m，應該是 {U}m。\n請確認 KenneyImportSettings 有生效（對 RetroFantasyKit 資料夾按右鍵 → Reimport）。", "OK");
                return false;
            }
            frontSign = bb.center.z < 0f ? 1f : -1f;
        }
        finally
        {
            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(block);
        }
        return true;
    }

    static Bounds MeshBounds(GameObject go)
    {
        bool has = false;
        var b = new Bounds();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            Vector3 c = go.transform.InverseTransformPoint(mf.transform.TransformPoint(mb.center));
            Vector3 s = Vector3.Scale(mb.size, mf.transform.lossyScale);
            var local = new Bounds(c, s);
            if (!has) { b = local; has = true; } else b.Encapsulate(local);
        }
        return b;
    }

    /// <summary>
    /// 放一個城堡零件。front = 零件正面朝向的世界方向（窗戶、拱門那一面）。
    /// keepCollider = false 時拿掉碰撞（只當外觀）。
    /// </summary>
    static GameObject Put(string name, Transform parent, Vector3 worldPos, Vector3 front, bool keepCollider = false)
    {
        var go = Object.Instantiate(kit[name], parent);
        go.name = name;
        go.transform.SetPositionAndRotation(worldPos, Quaternion.LookRotation(front * frontSign, Vector3.up));
        if (!keepCollider)
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
        return go;
    }

    /// <summary>兩層高的城牆 + 頂端城垛。inward = 牆面朝向可行走區域的方向</summary>
    static void WallColumn(Transform parent, Vector3 basePos, Vector3 inward, bool window, bool paint)
    {
        Put(paint ? "wall-fortified-paint" : "wall-fortified", parent, basePos, inward);
        Put(window ? "wall-fortified-window" : "wall-fortified", parent, basePos + Vector3.up * U, inward);
        // 城垛凸出在零件背面 → 正面朝外，城垛就會在內側邊緣
        Put("battlement", parent, basePos + Vector3.up * U * 2f, -inward);
    }

    /// <summary>塔：塔基 + 幾層塔身 + 塔頂。scale = 放大倍率，solid = 有碰撞（凸出牆面的塔要有，不然玩家會穿進去）</summary>
    static void Tower(Transform parent, Vector3 basePos, Vector3 inward, int floors, float scale = 1f, bool solid = false)
    {
        float h = U * scale;
        Scaled(Put("tower-base", parent, basePos, inward, solid), scale);
        for (int i = 1; i <= floors; i++)
            Scaled(Put("tower", parent, basePos + Vector3.up * h * i, inward, solid), scale);
        Scaled(Put("tower-top", parent, basePos + Vector3.up * h * (floors + 1), inward), scale);
    }

    static void Scaled(GameObject go, float scale) => go.transform.localScale *= scale;

    /// <summary>
    /// 牆上的大塔：塔放大 TowerScale 倍，一小半凸出牆面（TowerBulge 公尺），
    /// 後面照樣補一段牆，八角形塔跟牆之間就不會露出縫。
    /// wallPos = 這段牆的位置（零件中心、腳底），inward = 朝向可行走區域
    /// </summary>
    static void WallTower(Transform parent, Vector3 wallPos, Vector3 inward)
    {
        WallColumn(parent, wallPos, inward, false, false);
        float radius = U * 0.5f * TowerScale;
        // 牆的內側面在 wallPos + inward * U/2；塔中心往內推，讓塔凸出 TowerBulge
        Vector3 center = wallPos + inward * (U * 0.5f - radius + TowerBulge);
        Tower(parent, center, inward, 1, TowerScale, solid: true);
    }

    const float TowerScale = 1.5f;   // 牆上的塔放大倍率（直徑 6m）
    const float TowerBulge = 1.5f;   // 塔凸出牆面多少 (m)

    /// <summary>火把：牆上的木架 + 火焰 + 暖色點光源（會閃爍）</summary>
    static void Torch(Transform parent, Vector3 wallPos, Vector3 inward)
    {
        var torch = new GameObject("Torch");
        torch.transform.SetParent(parent, false);
        torch.transform.SetPositionAndRotation(wallPos, Quaternion.LookRotation(inward));

        var stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stick.name = "Stick";
        Object.DestroyImmediate(stick.GetComponent<Collider>());
        stick.transform.SetParent(torch.transform, false);
        stick.transform.localPosition = new Vector3(0f, 0f, 0.25f);
        stick.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
        stick.transform.localScale = new Vector3(0.15f, 0.8f, 0.15f);
        stick.GetComponent<MeshRenderer>().sharedMaterial = torchWoodMat;

        var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flame.name = "Flame";
        Object.DestroyImmediate(flame.GetComponent<Collider>());
        flame.transform.SetParent(torch.transform, false);
        flame.transform.localPosition = new Vector3(0f, 0.45f, 0.45f);
        flame.transform.localScale = new Vector3(0.28f, 0.45f, 0.28f);
        var fr = flame.GetComponent<MeshRenderer>();
        fr.sharedMaterial = torchFlameMat;
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(torch.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0.9f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.58f, 0.25f);
        l.intensity = 6f;
        l.range = 14f;
        l.shadows = LightShadows.None;

        torch.AddComponent<TorchFlicker>();
    }

    static Transform NewDressing(Transform root)
    {
        var old = root.Find(DressingName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var d = new GameObject(DressingName).transform;
        d.SetParent(root, false);
        return d;
    }

    static void HideRenderer(Transform t)
    {
        if (t == null) return;
        var r = t.GetComponent<MeshRenderer>();
        if (r != null) r.enabled = false;
    }

    static Vector3 RandomQuarterTurn()
    {
        switch (rng.Next(4))
        {
            case 0: return Vector3.forward;
            case 1: return Vector3.right;
            case 2: return Vector3.back;
            default: return Vector3.left;
        }
    }

    static Texture2D KitTexture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{KitDir}/Textures/{name}.png");

    static void ApplyTexture(Material mat, string texture, Vector2 tiling, Color tint)
    {
        if (mat == null) return;
        var tex = KitTexture(texture);
        if (tex == null) return;
        mat.SetTexture("_BaseMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.SetTextureScale("_BaseMap", tiling);
        mat.SetColor("_BaseColor", tint);
        mat.SetColor("_Color", tint);
        EditorUtility.SetDirty(mat);
    }

    // =====================================================================
    //  走廊
    // =====================================================================

    static string DressCorridor()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CorridorPath) == null)
            return "⚠ 找不到 Corridor_Wide.prefab，走廊沒有修改（先執行「更新第二層」）";

        var root = PrefabUtility.LoadPrefabContents(CorridorPath);
        try
        {
            // 原本的方塊：保留碰撞、隱藏外觀
            foreach (Transform c in root.transform)
                if (c.name != DressingName) HideRenderer(c);

            var d = NewDressing(root.transform);
            var floorP = new GameObject("Floor").transform; floorP.SetParent(d, false);
            var wallP = new GameObject("Walls").transform; wallP.SetParent(d, false);
            var torchP = new GameObject("Torches").transform; torchP.SetParent(d, false);
            var propP = new GameObject("Props").transform; propP.SetParent(d, false);
            Transform rt = root.transform;
            Vector3 W(float x, float y, float z) => rt.TransformPoint(new Vector3(x, y, z));

            // 地板：4m 一塊的石板，隨機轉向避免看起來重複
            for (float z = -U * 0.5f; z > -(CorridorLength + U * 0.5f); z -= U)
                for (float x = -CorridorHalfWidth + U * 0.5f; x < CorridorHalfWidth; x += U)
                    Put("floor-flat", floorP, W(x, 0.005f, z), RandomQuarterTurn());

            // 左右兩側的城牆（每 20m 一座塔）
            float wallX = CorridorHalfWidth + U * 0.5f;   // 零件內側剛好貼齊走廊邊緣
            int count = Mathf.CeilToInt((CorridorLength + 2f) / U);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 inward = rt.TransformDirection(Vector3.right * -side);
                for (int i = 0; i < count; i++)
                {
                    float z = -1.5f - U * 0.5f - U * i;
                    Vector3 p = W(side * wallX, 0f, z);
                    if (i % 5 == 2) WallTower(wallP, p, inward);
                    else WallColumn(wallP, p, inward, window: i % 3 == 1, paint: i % 4 == 3);
                }
            }

            // 最裡面（玩家出發點後方）的牆：中間是封住的拱門
            float backZ = -CorridorLength - U * 0.5f;
            Vector3 toFront = rt.TransformDirection(Vector3.forward);
            for (int i = 0; i < 4; i++)
            {
                float x = -CorridorHalfWidth + U * 0.5f + U * i;
                Vector3 p = W(x, 0f, backZ);
                bool middle = i == 1 || i == 2;
                Put(middle ? "wall-fortified-gate" : "wall-fortified", wallP, p, toFront);
                Put("wall-fortified", wallP, p + Vector3.up * U, toFront);
                Put("battlement", wallP, p + Vector3.up * U * 2f, -toFront);
                if (middle) Put("wall-fortified", wallP, W(x, 0f, backZ - U), toFront); // 拱門後面封起來
            }

            // 靠 Boss 房那面牆（大門兩側的塔放在 BossRoom 裡）
            for (int side = -1; side <= 1; side += 2)
                WallColumn(wallP, W(side * (CorridorHalfWidth + 1.5f), 0f, 0.5f), -toFront, false, false);

            // 火把：左右交錯，每 10m 一支
            for (int i = 0; i < 8; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float z = -8f - i * 10f;
                Torch(torchP, W(side * (CorridorHalfWidth - 0.05f), 4f, z), rt.TransformDirection(Vector3.right * -side));
            }

            // 木桶、木箱（有碰撞，貼著牆放，不擋路）
            Vector3 along = rt.TransformDirection(Vector3.forward);
            Put("barrels", propP, W(-7.3f, 0f, -16f), rt.TransformDirection(Vector3.right), true);
            Put("detail-crate", propP, W(7.1f, 0f, -45f), along, true);
            Put("detail-crate", propP, W(7.2f, 0f, -46.4f), rt.TransformDirection(Vector3.right), true);
            Put("detail-barrel", propP, W(6f, 0f, -45.6f), along, true);
            Put("barrels", propP, W(7.3f, 0f, -61f), rt.TransformDirection(Vector3.left), true);
            Put("detail-barrel", propP, W(-7f, 0f, -79f), along, true);
            Put("detail-crate", propP, W(-7.1f, 0f, -80.4f), along, true);

            PrefabUtility.SaveAsPrefabAsset(root, CorridorPath);
            return "走廊：城牆、塔、石板地板、8 支火把、木桶木箱";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =====================================================================
    //  Boss 房
    // =====================================================================

    static string DressBossRoom()
    {
        string path = Floor02Builder.FindBossRoomPrefab();
        if (path == null) return "⚠ 找不到 BossRoom.prefab，Boss 房沒有修改";

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var floor = root.GetComponentInChildren<ArenaFloor>(true);
            var gate = root.GetComponentInChildren<BossGate>(true);
            if (floor == null) return "⚠ BossRoom 裡找不到 ArenaFloor，Boss 房沒有修改";
            Vector3 center = floor.transform.position;
            float r = floor.radius;

            // ---------- 地板：換成石板貼圖（每 tileSize 公尺重複一次，整片接在一起） ----------
            ApplyTexture(floor.tileMaterial, "cobblestoneAlternative", Vector2.one, new Color(0.72f, 0.7f, 0.74f));

            // ---------- 原本的環形牆：保留碰撞、隱藏外觀 ----------
            var walls = Floor02Builder.FindDeep(root.transform, "Walls");
            if (walls != null) foreach (Transform w in walls) HideRenderer(w);

            var d = NewDressing(root.transform);
            var ringP = new GameObject("Walls").transform; ringP.SetParent(d, false);
            var gateP = new GameObject("Gate").transform; gateP.SetParent(d, false);
            var torchP = new GameObject("Torches").transform; torchP.SetParent(d, false);

            // ---------- 城牆一圈（每 60 度一座塔） ----------
            const int N = 60;
            float ringR = r + U * 0.5f;    // 零件內側剛好貼齊競技場邊緣
            for (int k = 0; k < N; k++)
            {
                float a = k * Mathf.PI * 2f / N;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 p = center + dir * ringR;
                if (gate != null && dir.z < 0f && Mathf.Abs(dir.x * ringR) < 7f) continue; // 大門的位置留空
                if (k % 10 == 5) WallTower(ringP, p, -dir);
                else WallColumn(ringP, p, -dir, window: k % 4 == 1, paint: k % 7 == 3);
            }

            // ---------- 大門：兩側高塔 + 門上方的城牆 ----------
            if (gate != null)
            {
                Transform g = gate.transform;
                Vector3 G(float x, float y, float z) => g.TransformPoint(new Vector3(x, y, z));
                Vector3 inArena = g.TransformDirection(Vector3.forward);
                for (int side = -1; side <= 1; side += 2)
                    Tower(gateP, G(side * 5f, 0f, -0.5f), inArena, 3);
                for (int side = -1; side <= 1; side += 2)
                {
                    Put("wall-fortified", gateP, G(side * 2f, 6f, -0.5f), inArena);
                    Put("battlement", gateP, G(side * 2f, 6f + U, -0.5f), -inArena);
                }

                // 門框：石頭貼圖；門板：木頭貼圖；門檻：換成石板
                var frameMat = BossArenaBuilder.GetOrCreateMaterial("M_GateFrame", Color.gray);
                ApplyTexture(frameMat, "cobblestone", new Vector2(1f, 2f), new Color(0.75f, 0.73f, 0.78f));
                foreach (var n in new[] { "Frame_L", "Frame_R" })
                {
                    var f = g.Find(n);
                    if (f != null) f.GetComponent<MeshRenderer>().sharedMaterial = frameMat;
                }
                var door = g.Find("Door");
                if (door != null)
                    ApplyTexture(door.GetComponent<MeshRenderer>().sharedMaterial, "planks", new Vector2(2f, 2f), new Color(0.6f, 0.5f, 0.45f));
                var threshold = g.Find("Threshold");
                if (threshold != null)
                {
                    HideRenderer(threshold);
                    for (int i = 0; i < 4; i++)
                        Put("floor-flat", gateP, G(-6f + U * i, -0.015f, -0.5f), RandomQuarterTurn());
                }
            }

            // ---------- 火把：沿著牆一圈，每 30 度一支（大門那邊不放） ----------
            for (int i = 0; i < 12; i++)
            {
                float a = (i * 30f + 15f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                if (dir.z < -0.9f) continue;
                Torch(torchP, center + dir * (r - 0.05f) + Vector3.up * 4.5f, -dir);
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return "Boss 房：城牆一圈、6 座塔、大門兩側高塔、石板地板、火把";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =====================================================================
    //  場景燈光
    // =====================================================================

    static string SetupSceneLighting()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            return "⚠ 找不到 Floor02 場景，燈光沒有修改";

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 霧：遠處慢慢變暗，營造高塔內部的陰暗感
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.06f, 0.06f, 0.08f);
        RenderSettings.fogDensity = 0.012f;
        RenderSettings.ambientIntensity = 0.6f;

        // 陽光調暗、偏冷色，讓火把的暖光比較明顯
        foreach (var rootGo in scene.GetRootGameObjects())
            foreach (var l in rootGo.GetComponentsInChildren<Light>(true))
                if (l.type == LightType.Directional)
                {
                    l.intensity = 0.5f;
                    l.color = new Color(0.72f, 0.77f, 0.95f);
                }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Floor02：加上霧、陽光調暗（Lighting 視窗 → Environment 可以再調）";
    }
}
#endif
