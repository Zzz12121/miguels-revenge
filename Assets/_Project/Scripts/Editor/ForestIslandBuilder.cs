#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 選單：GeminiGrowUp > 建立孤島森林場景 (Forest Island)
///
/// 產生 Assets/_Project/Scenes/ForestIsland.unity：老者打到半血後逃過吊橋，玩家追到的「霧海孤島森林」。
///   - Unity Terrain 地形：中央空地（戰鬥區，半徑約 24m）→ 森林 → 不規則懸崖，崖下是霧海
///   - 南邊吊橋（玩家出生在橋上，往北走進島）；橋的另一端消失在霧裡
///   - 程序生成的樹 Prefab（高樹 4 種 + 歪脖古松 2 種），樹冠是程序生成的葉叢面片（每棵幾千頂點）
///   - 懸崖用維京村莊的巨石堆疊、空地外圍一圈立石（menhir）、北側石台給老者站
///   - 遠方石峰剪影、雲海、林間薄霧、落葉、浮游光點、後製
///   - GameplayMarkers：玩家出生點、老者站位、8 個召喚點、戰鬥區中心、橋尾觸發區（只有位置，沒有玩法程式）
///
/// 只放裝飾和碰撞（地形、樹幹、石頭、島邊與橋側的隱形牆）。召喚、Boss 流程、場景切換由隊友的系統接。
/// 可重複執行（整個場景重建）；地形、Mesh、貼圖、材質、樹 Prefab 會覆蓋更新。
/// </summary>
public static class ForestIslandBuilder
{
    const string Root = "Assets/_Project";
    public const string ScenePath = Root + "/Scenes/ForestIsland.unity";
    const string SceneAssetDir = Root + "/Scenes/ForestIsland";
    const string MatDir = Root + "/Materials/ForestIsland";
    const string TexDir = Root + "/VFX/ForestIsland";
    const string PrefabDir = Root + "/Prefabs/ForestIsland";
    const string MeshDir = PrefabDir + "/Meshes";

    const string CentipedePrefabPath = Root + "/Prefabs/Boss/PF_SkeletonCentipede_Animated.prefab";
    const float CentipedeScale = 5.26f; // 和修羅場、VFX_VoidRend_Bocchi 測試場景一致
    const string Viking = Root + "/ThirdParty/Viking Village";
    const string Veg = Viking + "/Book of the Dead/Vegetation";

    // ---- 版面（公尺）。島中心 = 世界原點，島面高度 y ≈ 0，吊橋在南邊（-Z）----
    public const float ArenaRadius = 24f;     // 中央空地（戰鬥區）半徑
    const float RimBase = 62f;                // 島的平均半徑
    const float AbyssY = -80f;                // 地形最低處（藏在霧裡）
    const float TerrainSize = 320f;
    const float TerrainHeight = 130f;
    const int HeightRes = 513;
    const float BridgeLength = 110f;
    const float BridgeSag = 5f;
    const float BridgeWidth = 2.6f;
    const float BridgeSpawnDistance = 70f;    // 玩家出生點離島邊多遠（在橋上；中間是骷髏騎士守橋）

    static System.Random rng;
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static int RI(int a, int bExclusive) => rng.Next(a, bExclusive);
    static bool Chance(float p) => rng.NextDouble() < p;

    static Material mBark, mDeadBark, mRope, mPlank, mCloud, mMist, mLeaf, mMote, mRock, mMistFloor, mLeaves, mPineLeaves;
    static Terrain terrain;

    // 素材 Prefab
    static GameObject pfBroadleaf, pfGreenBush;
    static readonly List<GameObject> pfGrass = new List<GameObject>();
    static readonly List<GameObject> pfFerns = new List<GameObject>();
    static readonly List<GameObject> pfBoulders = new List<GameObject>();
    static GameObject pfMenhir1, pfMenhir2, pfRune, pfSkull1, pfSkull2, pfSkullStick, pfBoulderFlat;
    static readonly Dictionary<GameObject, Bounds> boundsCache = new Dictionary<GameObject, Bounds>();

    [MenuItem("GeminiGrowUp/建立孤島森林場景 (Forest Island)")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("重建孤島森林場景？",
                "ForestIsland.unity 已存在，會整個重建（在場景裡手動加的東西會消失）。\n" +
                "樹 Prefab、地形資料、材質會用相同路徑覆蓋更新。",
                "重建", "取消"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildScene();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    /// <summary>不跳對話框的版本，給 batchmode（-executeMethod ForestIslandBuilder.BuildScene）使用。</summary>
    public static void BuildScene()
    {
        rng = new System.Random(2026);
        boundsCache.Clear();
        foreach (var d in new[] { SceneAssetDir, MatDir, TexDir, PrefabDir, MeshDir, PrefabDir + "/Trees" })
            EnsureFolder(d);

        if (!LoadSourcePrefabs()) return;
        // 先開新場景：之後量 Prefab 尺寸、組樹 Prefab 的暫存物件都只會出現在這個新場景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CreateMaterials();
        var treePrefabs = BuildTreePrefabs();

        var env = new GameObject("ForestIsland_Environment").transform;
        terrain = BuildTerrain(env);
        var markers = BuildMarkers();
        SetupCentipedeBoss();
        BuildBridge(env, out Vector3 spawn);
        BuildBridgeSoldiers();
        BuildCliffRocks(env);
        BuildArenaDressing(env);
        BuildForest(env, treePrefabs);
        BuildForestBoulders(env);
        BuildUndergrowth(env);
        BuildDistantPeaks(env, treePrefabs);
        BuildBoundary(env);
        BuildAtmosphereVfx(env);
        BuildLighting();

        // 玩家攻擊打擊感（頓幀、火花、音效）；數值可在這個物件上調
        new GameObject("PlayerHitFeedback").AddComponent<PlayerHitFeedback>();

        // 玩家：直接放 PlayerRig Prefab（不修改 Prefab 本身），站在吊橋上面向島
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PlayerRig.prefab");
        if (rigPrefab != null)
        {
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            rig.transform.SetPositionAndRotation(spawn, Quaternion.identity);
            EnablePostProcessing(rig);
        }
        else
        {
            Debug.LogWarning("[ForestIsland] 找不到 PlayerRig.prefab，場景裡沒有玩家，只放了出生點標記。");
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ForestIsland] 已建立孤島森林場景：{ScenePath}。戰鬥區半徑 {ArenaRadius}m，玩家出生在南邊吊橋上。");
    }

    // =====================================================================
    //  預覽截圖（給 batchmode 驗證用；也可以從選單執行，圖片存到專案的 Temp/ForestIslandShots）
    // =====================================================================

    [MenuItem("GeminiGrowUp/孤島森林：輸出預覽截圖")]
    public static void CapturePreviews()
    {
        string dir = System.Environment.GetEnvironmentVariable("FOREST_ISLAND_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp/ForestIslandShots");
        Directory.CreateDirectory(dir);
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        terrain = Terrain.activeTerrain;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
            ps.Simulate(25f, true, true);

        float spawnZ = BridgeRimZ + 2.5f - BridgeSpawnDistance;
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("01_bridge_player_view", new Vector3(0f, 0.6f, spawnZ - 5f), new Vector3(0f, 1.5f, 0f)),
            ("02_arena_from_path", new Vector3(0f, 5f, -ArenaRadius - 6f), new Vector3(0f, 1.5f, 12f)),
            ("03_aerial", new Vector3(110f, 85f, -120f), new Vector3(0f, -5f, 0f)),
            ("04_edge_look_out", new Vector3(-30f, 3f, 40f), new Vector3(-110f, -15f, 150f)),
            ("05_forest_interior", new Vector3(38f, GroundY(38f, 8f) + 1.8f, 8f), new Vector3(30f, GroundY(30f, 40f) + 2.5f, 40f)),
            ("06_boss_view_south", new Vector3(0f, 2.6f, 17f), new Vector3(0f, 1f, -40f)),
            ("08_bridge_soldiers", BridgePoint(BridgeSpawnDistance - 4f, 0f, 2.4f), BridgePoint(BridgeSpawnDistance - 16f, 0f, 1.4f)),
            ("07_centipede_from_player", new Vector3(3f, 3.2f, -12f), new Vector3(0f, 2f, 15f)),
        };
        var go = new GameObject("PreviewCamera");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 800f;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        foreach (var s in shots)
        {
            go.transform.position = s.pos;
            go.transform.rotation = Quaternion.LookRotation(s.look - s.pos);
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);
        Debug.Log($"[ForestIsland] 預覽截圖已存到 {dir}");
    }

    /// <summary>batchmode：建場景後立刻截圖。</summary>
    public static void BuildAndCapture()
    {
        BuildScene();
        CapturePreviews();
    }

    // =====================================================================
    //  島的形狀
    // =====================================================================

    /// <summary>某個方位角（弧度，0 = +X、逆時針）上的島邊半徑。</summary>
    static float RimRadius(float a)
    {
        float x = Mathf.Cos(a), z = Mathf.Sin(a);
        float n1 = Mathf.PerlinNoise(x * 1.2f + 11.7f, z * 1.2f + 5.3f);
        float n2 = Mathf.PerlinNoise(x * 3.4f + 2.1f, z * 3.4f + 8.8f);
        float r = RimBase + (n1 - 0.5f) * 24f + (n2 - 0.5f) * 7f;
        // 吊橋落腳處附近把島邊拉平，橋頭不會正好接在凹口
        float bridgeA = Mathf.DeltaAngle(a * Mathf.Rad2Deg, -90f);
        float w = Mathf.Exp(-(bridgeA * bridgeA) / (2f * 12f * 12f));
        return Mathf.Lerp(r, RimBase, w);
    }

    static float BridgeRimZ => -RimRadius(-Mathf.PI * 0.5f);

    static float PathOffsetX(float z) => Mathf.Sin(z * 0.09f) * 2.2f;

    /// <summary>0~1：越靠近從空地通往吊橋的小路中心越接近 1。</summary>
    static float PathMask(float x, float z)
    {
        if (z > -ArenaRadius + 6f) return 0f;
        float dx = Mathf.Abs(x - PathOffsetX(z));
        return 1f - Smooth(2.2f, 4.5f, dx);
    }

    static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

    static float Fbm(float x, float z)
    {
        float v = 0f, amp = 0.5f, f = 1f;
        for (int i = 0; i < 4; i++)
        {
            v += amp * Mathf.PerlinNoise(x * f + 31.4f, z * f + 17.2f);
            f *= 2.03f; amp *= 0.5f;
        }
        return v / 0.9375f;
    }

    /// <summary>世界座標 (x,z) 的地面高度（設計值，地形就是取樣這個函式）。</summary>
    static float DesignHeight(float x, float z)
    {
        float d = Mathf.Sqrt(x * x + z * z);
        float a = Mathf.Atan2(z, x);
        float rim = RimRadius(a);

        float hills = Fbm(x * 0.03f, z * 0.03f);
        float forest = Smooth(ArenaRadius, ArenaRadius + 16f, d);
        // 空地：戰鬥區內幾乎全平（百足骨的 AI 固定在出生高度移動），外圈才有一點起伏
        float y = (Fbm(x * 0.12f, z * 0.12f) - 0.5f) * Mathf.Lerp(0.06f, 0.35f, Smooth(ArenaRadius - 4f, ArenaRadius + 4f, d));
        y += forest * (hills - 0.3f) * 9f;                            // 森林：起伏的小丘
        y = Mathf.Max(y, -0.6f);
        y += Smooth(rim - 14f, rim - 3f, d) * 1.2f;                   // 崖邊微微隆起

        // 小路（空地 → 吊橋）壓平
        float pm = PathMask(x, z);
        if (pm > 0f)
        {
            float pathY = Mathf.Lerp(0f, 0.3f, Mathf.InverseLerp(-ArenaRadius, BridgeRimZ, z));
            y = Mathf.Lerp(y, pathY, pm);
        }

        // 懸崖：島邊外迅速掉到深淵，加一點雜訊讓崖面不規則
        float jag = (Mathf.PerlinNoise(x * 0.15f + 3f, z * 0.15f + 9f) - 0.5f) * 5f;
        float drop = Smooth(rim - 0.5f + jag * 0.3f, rim + 13f + jag, d);
        bool bridgeLanding = Mathf.Abs(x) < 4f && z < 0f && d < rim + 1.5f;
        if (!bridgeLanding) y = Mathf.Lerp(y, AbyssY, drop);
        return y;
    }

    // =====================================================================
    //  地形
    // =====================================================================

    static Terrain BuildTerrain(Transform env)
    {
        string tdPath = SceneAssetDir + "/TD_ForestIsland.asset";
        AssetDatabase.DeleteAsset(tdPath);
        var td = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = 512 };
        td.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);

        Vector3 origin = new Vector3(-TerrainSize * 0.5f, AbyssY, -TerrainSize * 0.5f);
        var h = new float[HeightRes, HeightRes];
        for (int iz = 0; iz < HeightRes; iz++)
            for (int ix = 0; ix < HeightRes; ix++)
            {
                float wx = origin.x + ix / (float)(HeightRes - 1) * TerrainSize;
                float wz = origin.z + iz / (float)(HeightRes - 1) * TerrainSize;
                h[iz, ix] = Mathf.Clamp01((DesignHeight(wx, wz) - AbyssY) / TerrainHeight);
            }
        td.SetHeights(0, 0, h);

        // 地表貼圖：草地 / 暗草（林下）/ 濕泥（小路、戰鬥磨損）/ 泥岩（陡坡）
        td.terrainLayers = new[]
        {
            Layer("TL_FI_Grass", "terrain_grass_01_a", "terrain_grass_01_n", 7f, new Color(0.92f, 0.97f, 0.88f)),
            Layer("TL_FI_ForestFloor", "terrain_grass_01_a_darker", "terrain_grass_01_n", 6f, new Color(0.78f, 0.82f, 0.7f)),
            Layer("TL_FI_Mud", "terrain_wetmud_01_a", "terrain_wetmud_01_n", 5f, Color.white),
            Layer("TL_FI_Cliff", "terrain_mudslide_01_a", "terrain_mudslide_01_n", 9f, new Color(0.8f, 0.8f, 0.82f)),
        };
        int res = td.alphamapResolution;
        var alpha = new float[res, res, 4];
        for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float nx = ix / (float)(res - 1), nz = iz / (float)(res - 1);
                float wx = origin.x + nx * TerrainSize, wz = origin.z + nz * TerrainSize;
                float steep = td.GetSteepness(nx, nz);
                float d = Mathf.Sqrt(wx * wx + wz * wz);
                float n = Fbm(wx * 0.08f, wz * 0.08f);

                float cliff = Smooth(28f, 42f, steep);
                float path = PathMask(wx, wz) * 0.95f;
                // 空地中央被打鬥踩禿的一圈
                float worn = (1f - Smooth(ArenaRadius * 0.35f, ArenaRadius * 0.85f, d)) * Smooth(0.35f, 0.65f, n) * 0.75f;
                float mud = Mathf.Clamp01(Mathf.Max(path, worn));
                float forestFloor = Smooth(ArenaRadius + 2f, ArenaRadius + 12f, d) * Smooth(0.3f, 0.6f, n + 0.15f);

                float wCliff = cliff;
                float wMud = (1f - wCliff) * mud;
                float rest = 1f - wCliff - wMud;
                alpha[iz, ix, 0] = rest * (1f - forestFloor);
                alpha[iz, ix, 1] = rest * forestFloor;
                alpha[iz, ix, 2] = wMud;
                alpha[iz, ix, 3] = wCliff;
            }
        td.SetAlphamaps(0, 0, alpha);
        AssetDatabase.CreateAsset(td, tdPath);

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "IslandTerrain";
        go.transform.SetParent(env, false);
        go.transform.position = origin;
        var t = go.GetComponent<Terrain>();
        var terrainMat = AssetDatabase.LoadAssetAtPath<Material>(Viking + "/Materials/TerrainURP.mat");
        if (terrainMat == null)
        {
            terrainMat = GetOrCreateMat("M_FI_Terrain", "Universal Render Pipeline/Terrain/Lit");
        }
        t.materialTemplate = terrainMat;
        t.basemapDistance = 160f;
        t.heightmapPixelError = 4f;
        t.drawInstanced = true;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return t;
    }

    static TerrainLayer Layer(string name, string albedo, string normal, float tile, Color tint)
    {
        string path = $"{MatDir}/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = LoadTex($"{Viking}/Textures/Terrain/{albedo}.tif");
        layer.normalMapTexture = LoadTex($"{Viking}/Textures/Terrain/{normal}.tif");
        layer.normalScale = 1f;
        layer.tileSize = new Vector2(tile, tile);
        layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
        layer.smoothness = 0.1f;
        layer.metallic = 0f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static float GroundY(float x, float z) =>
        terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : DesignHeight(x, z);

    // =====================================================================
    //  吊橋上的骷髏騎士（Enemy_SkeletonKnight；沒有的話先建立，失敗就退回 Enemy_Grunt）
    // =====================================================================

    static void BuildBridgeSoldiers()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonKnightInstaller.PrefabPath);
        if (prefab == null) prefab = SkeletonKnightInstaller.BuildPrefab();
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Enemy_Grunt.prefab");
        if (prefab == null) { Debug.LogWarning("[ForestIsland] 找不到小怪 Prefab，橋上沒有士兵。"); return; }

        var root = new GameObject("BridgeSoldiers").transform;
        // (離島邊的距離, 左右偏移)：由遠到近一路守到橋頭，兩隻並排的那組堵住橋面
        var slots = new (float s, float x)[]
        {
            (56f, 0f), (46f, -0.6f), (46f, 0.6f), (34f, 0f), (22f, 0.3f), (10f, -0.3f),
            (-4f, -1.6f), (-4f, 1.6f),   // 橋頭（島上）兩隻守衛
        };
        int i = 0;
        foreach (var slot in slots)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = $"SkeletonKnight_{i++:00}";
            go.transform.SetParent(root, false);
            Vector3 p = BridgePoint(Mathf.Max(slot.s, 0f), slot.x, 0f);
            if (slot.s < 0f)
            {
                p.z = BridgePoint(slot.s, 0f, 0f).z;   // BridgePoint 的 z 不夾，橋頭往島內延伸
                p.y = GroundY(p.x, p.z);
            }
            // Prefab 根物件在碰撞膠囊中心：往上抬到腳底貼地
            var cc = go.GetComponent<CharacterController>();
            float feet = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
            go.transform.position = p + Vector3.up * (0.08f - feet);
            go.transform.rotation = Quaternion.LookRotation(Vector3.back);   // 面向走來的玩家

            var ai = go.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.enableWander = false;   // 不在窄橋上亂走
                ai.canJump = false;        // 不做跳躍撲擊（會跳出橋外）
                ai.leashRadius = 30f;
                ai.giveUpDistance = 24f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
            }
        }
    }

    // =====================================================================
    //  Boss：百足駭骨＋它自己的 AI（和修羅場同一套設定方式，不修改 Prefab 與 AI 程式）
    // =====================================================================

    static void SetupCentipedeBoss()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CentipedePrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[ForestIsland] 找不到百足駭骨 Prefab，場景裡沒有 Boss。");
            return;
        }
        var stand = GameObject.Find("GameplayMarkers/BossStandPoint");
        Vector3 pos = stand != null ? stand.transform.position : new Vector3(0f, GroundY(0f, 15f), 15f);
        Quaternion rot = stand != null ? stand.transform.rotation : Quaternion.LookRotation(Vector3.back);

        var boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        boss.name = "SkeletonCentipede Boss";
        boss.transform.SetPositionAndRotation(pos, rot);
        boss.transform.localScale = Vector3.one * CentipedeScale;

        // 會移動的碰撞體：kinematic Rigidbody，玩家的攻擊才打得到、也擋得住
        var rb = boss.GetComponent<Rigidbody>();
        if (rb == null) rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var ai = boss.GetComponent<SkeletonCentipedeBossAI>();
        if (ai == null) ai = boss.AddComponent<SkeletonCentipedeBossAI>();
        // 玩家走進空地（半徑 24m）待滿 startDelay 秒就開打；Boss 不會離開空地
        ai.arenaCenter = new Vector3(0f, GroundY(0f, 0f), 0f);
        ai.arenaRadius = ArenaRadius;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
    }

    // =====================================================================
    //  玩法標記（只有位置，沒有程式）
    // =====================================================================

    static Transform BuildMarkers()
    {
        var root = new GameObject("GameplayMarkers").transform;

        var center = Group("ArenaCenter (r=24m)", root);
        center.position = new Vector3(0f, GroundY(0f, 0f), 0f);

        var boss = Group("BossStandPoint", root);
        boss.position = new Vector3(0f, GroundY(0f, 15f), 15f);
        boss.rotation = Quaternion.LookRotation(Vector3.back);   // 面向南邊（玩家進場方向）

        var summon = Group("SummonPoints", root);
        for (int i = 0; i < 8; i++)
        {
            float a = (i / 8f) * Mathf.PI * 2f + Mathf.PI / 8f;
            float rr = i % 2 == 0 ? 13f : 18f;
            var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
            p.y = GroundY(p.x, p.z);
            var t = Group($"SummonPoint_{i}", summon);
            t.position = p;
            t.rotation = Quaternion.LookRotation(-new Vector3(p.x, 0f, p.z));
        }
        return root;
    }

    // =====================================================================
    //  吊橋（島 → 南方霧裡）
    // =====================================================================

    static Vector3 BridgePoint(float s, float side, float lift)
    {
        // s = 離島邊的距離（0 = 橋頭），side = 左右偏移
        float z0 = BridgeRimZ + 2.5f;
        float t = Mathf.Clamp01(s / BridgeLength);
        float y = 0.35f - BridgeSag * 4f * t * (1f - t);
        return new Vector3(side, y + lift, z0 - s);
    }

    static void BuildBridge(Transform env, out Vector3 spawn)
    {
        var root = Group("SuspensionBridge", env);
        var planks = Group("Planks", root);
        var ropes = Group("Ropes", root);
        var cols = Group("WalkColliders", root);

        // 木板：略有間隙、少數歪斜或缺片
        float step = 0.42f;
        int n = Mathf.CeilToInt(BridgeLength / step);
        for (int i = 0; i < n; i++)
        {
            float s = i * step;
            if (i > 12 && Chance(0.03f)) continue; // 缺一片
            var a = BridgePoint(s, 0f, 0f);
            var b = BridgePoint(s + 0.01f, 0f, 0f);
            float pitch = -Mathf.Atan2(b.y - a.y, 0.01f) * Mathf.Rad2Deg;
            Box(planks, $"Plank_{i:000}", a, new Vector3(pitch + R(-2f, 2f), R(-3f, 3f), R(-1.5f, 1.5f)),
                new Vector3(BridgeWidth + R(-0.15f, 0.15f), 0.07f, 0.32f), mPlank);
        }
        // 兩條縱向承重木
        foreach (float side in new[] { -BridgeWidth * 0.42f, BridgeWidth * 0.42f })
            RopeAlong(ropes, "Stringer", s => BridgePoint(s, side, -0.07f), 0f, BridgeLength, 2f, 0.07f, mPlank);

        // 扶手繩、下緣繩、垂直吊繩
        float half = BridgeWidth * 0.5f + 0.1f;
        foreach (float side in new[] { -half, half })
        {
            RopeAlong(ropes, "HandRope", s => BridgePoint(s, side, 1.15f), 0f, BridgeLength, 1.5f, 0.035f, mRope);
            RopeAlong(ropes, "LowRope", s => BridgePoint(s, side, 0.05f), 0f, BridgeLength, 1.5f, 0.03f, mRope);
            for (float s = 0f; s <= BridgeLength; s += 1.8f)
                Segment(ropes, "Hanger", BridgePoint(s, side, 0.05f), BridgePoint(s, side, 1.15f), 0.018f, mRope);
        }

        // 橋頭木柱（島端；遠端也放一組，藏在霧裡）
        foreach (float s in new[] { -0.6f, BridgeLength + 0.4f })
            foreach (float side in new[] { -half - 0.25f, half + 0.25f })
            {
                var p = BridgePoint(Mathf.Clamp(s, 0f, BridgeLength), side, 0f);
                p.z = BridgePoint(s, 0f, 0f).z;
                float baseY = s < 0f ? GroundY(p.x, p.z) - 0.6f : p.y - 3f;
                var post = Cyl(root, "BridgePost", new Vector3(p.x, baseY + 2.4f, p.z), new Vector3(R(-2f, 2f), 0f, R(-2f, 2f)),
                    new Vector3(0.38f, 2.4f, 0.38f), mDeadBark);
                AddCollider(post);
                Cyl(root, "PostRopeWrap", new Vector3(p.x, p.y + 1.15f, p.z), Vector3.zero, new Vector3(0.44f, 0.12f, 0.44f), mRope);
                // 錨繩：柱頂斜拉到地面
                if (s < 0f)
                    Segment(ropes, "AnchorRope", new Vector3(p.x, baseY + 4.6f, p.z),
                        new Vector3(p.x + Mathf.Sign(side) * 1.2f, GroundY(p.x, p.z + 4f), p.z + 4f), 0.03f, mRope);
            }

        // 走道碰撞：每 4m 一段斜板；兩側隱形扶手牆
        for (float s = -1f; s < BridgeLength; s += 4f)
        {
            var a = BridgePoint(Mathf.Max(s, 0f), 0f, 0f);
            var b = BridgePoint(Mathf.Min(s + 4.2f, BridgeLength), 0f, 0f);
            if (s < 0f) a.z += 1f;
            var mid = (a + b) * 0.5f;
            var dir = b - a;
            var c = new GameObject("Deck");
            c.transform.SetParent(cols, false);
            c.transform.position = mid + Vector3.down * 0.05f;
            c.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var box = c.AddComponent<BoxCollider>();
            box.size = new Vector3(BridgeWidth, 0.12f, dir.magnitude + 0.1f);
            foreach (float side in new[] { -half, half })
            {
                var w = new GameObject("RailWall");
                w.transform.SetParent(cols, false);
                w.transform.position = mid + new Vector3(side, 1f, 0f);
                w.transform.rotation = c.transform.rotation;
                w.AddComponent<BoxCollider>().size = new Vector3(0.2f, 2.2f, dir.magnitude + 0.1f);
            }
        }
        // 遠端封口（隊友之後可換成回庭院的觸發區）
        var endWall = new GameObject("BridgeFarEndWall");
        endWall.transform.SetParent(cols, false);
        endWall.transform.position = BridgePoint(BridgeLength - 6f, 0f, 1.5f);
        endWall.AddComponent<BoxCollider>().size = new Vector3(BridgeWidth + 1f, 4f, 0.4f);

        var trigger = new GameObject("BridgeFarEndTrigger (給隊友接場景切換)");
        trigger.transform.SetParent(GameObject.Find("GameplayMarkers").transform, false);
        trigger.transform.position = BridgePoint(BridgeLength - 9f, 0f, 1.2f);
        var tc = trigger.AddComponent<BoxCollider>();
        tc.isTrigger = true;
        tc.size = new Vector3(BridgeWidth, 2.4f, 3f);

        spawn = BridgePoint(BridgeSpawnDistance, 0f, 0.05f);
        var sp = Group("PlayerSpawn (Bridge)", GameObject.Find("GameplayMarkers").transform);
        sp.position = spawn;
        sp.rotation = Quaternion.identity; // 面向 +Z（島）
    }

    /// <summary>沿著曲線放一串細圓柱當繩子 / 木條。</summary>
    static void RopeAlong(Transform parent, string name, System.Func<float, Vector3> f, float s0, float s1, float step, float radius, Material m)
    {
        var g = Group(name, parent);
        for (float s = s0; s < s1 - 0.01f; s += step)
            Segment(g, name, f(s), f(Mathf.Min(s + step, s1)), radius, m);
    }

    static GameObject Segment(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material m)
    {
        var dir = b - a;
        var go = Prim(PrimitiveType.Cylinder, parent, name, (a + b) * 0.5f, Vector3.zero,
            new Vector3(radius * 2f, dir.magnitude * 0.5f + radius, radius * 2f), m, false);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        return go;
    }

    // =====================================================================
    //  懸崖：沿島邊一圈不規則石柱（合併成一個 Mesh），崖頂再撒半埋的小石
    // =====================================================================

    static void BuildCliffRocks(Transform env)
    {
        var root = Group("Cliffs", env);
        var parts = new List<CombineInstance>();
        int idx = 0;
        for (float deg = 0f; deg < 360f; deg += R(3.5f, 5.5f))
        {
            float a = deg * Mathf.Deg2Rad;
            float rim = RimRadius(a);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var tangent = Vector3.Cross(Vector3.up, dir);
            int rows = RI(2, 4);
            for (int L = 0; L < rows; L++)
            {
                float rad = L == 0 ? R(3.2f, 5.5f) : R(5f, 9f);
                var p = dir * (rim + rad * 0.55f + L * R(3f, 5f)) + tangent * R(-1.5f, 1.5f);
                // 吊橋走廊兩側留空
                if (p.z < 0f && Mathf.Abs(p.x) < rad + BridgeWidth * 0.5f + 0.8f) continue;
                float topY = GroundY(dir.x * (rim - 0.5f), dir.z * (rim - 0.5f)) - R(0.2f, 0.8f) - L * R(5f, 11f);
                var mesh = RockColumnMesh(null, topY - R(60f, 75f), topY, rad, R(0.55f, 0.85f), RI(0, 100000), 0.3f);
                parts.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.Translate(new Vector3(p.x, 0f, p.z)) });
            }
            // 崖頂小石（半埋）
            if (Chance(0.55f))
            {
                var p = dir * (rim - R(1.5f, 4f));
                if (p.z < 0f && Mathf.Abs(p.x) < 4.5f) continue;
                p.y = GroundY(p.x, p.z) - 0.35f;
                float sz = R(1.2f, 2.6f);
                var stone = PlaceFitted(root, pfBoulders[RI(0, pfBoulders.Count)], $"RimStone_{idx++:000}", p,
                    Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-20f, 20f)), sz, true);
                MakeDestructibleRock(stone, sz < 1.8f ? 3 : 4);
            }
        }
        var cliff = CombineAndSave("Mesh_FI_Cliffs", parts, SceneAssetDir);
        var go = MeshObj(root, "CliffColumns", cliff, Vector3.zero, Vector3.zero, Vector3.one, mRock);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
    }

    /// <summary>
    /// 不規則石柱：由下往上逐圈縮小，半徑用雜訊擾動，出現節理與凹凸。
    /// 中心在原點，y 從 bottom 到 top。name = null 時不存 asset（給合併用）。
    /// </summary>
    static Mesh RockColumnMesh(string name, float bottom, float top, float radius, float topScale, int seed, float roughness)
    {
        const int sides = 14;
        float height = top - bottom;
        int rings = Mathf.Clamp(Mathf.CeilToInt(height / 2.2f), 3, 60);
        float o1 = seed % 997 * 0.37f, o2 = seed % 389 * 0.91f;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            float y = bottom + height * t;
            // 越往上越細，頂端收一點圓角；中段偶爾有內縮的節理
            float taper = Mathf.Lerp(1f, topScale, t * t);
            float cap = i == rings ? 0.75f : 1f;
            float band = 1f - 0.12f * Mathf.Clamp01(Mathf.Sin(y * 0.45f + o1) * 3f - 2f);
            for (int k = 0; k <= sides; k++)
            {
                float a = k / (float)sides * Mathf.PI * 2f;
                float n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.3f + o1 + y * 0.05f, Mathf.Sin(a) * 1.3f + o2 + y * 0.09f);
                float n2 = Mathf.PerlinNoise(Mathf.Cos(a) * 3.1f + o2, Mathf.Sin(a) * 3.1f + y * 0.25f);
                float r = radius * taper * cap * band * (1f + (n - 0.5f) * 0.9f * roughness * 2f + (n2 - 0.5f) * 0.35f * roughness * 2f);
                verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                uvs.Add(new Vector2(a * radius / 6f, y / 6f));
            }
        }
        int ring = sides + 1;
        for (int i = 0; i < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = i * ring + k, b = a + 1, c = a + ring, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        // 頂蓋
        int center = verts.Count;
        verts.Add(new Vector3(0f, top + radius * 0.15f, 0f));
        uvs.Add(Vector2.zero);
        int topRing = rings * ring;
        for (int k = 0; k < sides; k++) { tris.Add(topRing + k); tris.Add(center); tris.Add(topRing + k + 1); }

        var mesh = new Mesh { name = name ?? "RockColumn", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        if (name != null)
        {
            string path = $"{SceneAssetDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }
        return mesh;
    }

    static Mesh CombineAndSave(string name, List<CombineInstance> parts, string dir)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        mesh.RecalculateBounds();
        foreach (var p in parts)
            if (p.mesh != null && !AssetDatabase.Contains(p.mesh)) Object.DestroyImmediate(p.mesh);
        string path = $"{dir}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // =====================================================================
    //  戰鬥區：立石圈、北側石台、骷髏
    // =====================================================================

    static void BuildArenaDressing(Transform env)
    {
        var root = Group("ArenaDressing", env);

        // 立石圈（南邊小路口空著）
        int stones = 11;
        for (int i = 0; i < stones; i++)
        {
            float deg = -90f + 360f * (i + 0.5f) / stones;
            if (Mathf.Abs(Mathf.DeltaAngle(deg, -90f)) < 22f) continue;
            float a = deg * Mathf.Deg2Rad;
            float rr = ArenaRadius + R(1.5f, 3.5f);
            var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
            p.y = GroundY(p.x, p.z) - 0.35f;
            var face = Quaternion.LookRotation(-new Vector3(p.x, 0f, p.z));
            bool toppled = i == 2 || i == 7;
            var rot = toppled
                ? face * Quaternion.Euler(R(75f, 85f), R(-30f, 30f), 0f)
                : face * Quaternion.Euler(R(-12f, 12f), R(-15f, 15f), R(-12f, 12f));
            if (toppled) p.y += 0.6f;
            var menhir = PlaceFittedHeight(root, Chance(0.5f) ? pfMenhir1 : pfMenhir2, $"Menhir_{i:00}", p, rot, R(3.8f, 5.2f), true);
            MakeDestructibleRock(menhir, 6);
        }

        // 北側石台：老者站位後方的平台與符文石
        var dais = Group("ElderDais", root);
        var dc = new Vector3(0f, 0f, 21f);
        for (int i = 0; i < 6; i++)
        {
            var off = new Vector3(R(-5f, 5f), 0f, R(-1.5f, 3f));
            var p = dc + off;
            p.y = GroundY(p.x, p.z) - R(1.6f, 2.2f);
            var go = PlaceFitted(dais, pfBoulderFlat, $"DaisRock_{i}", p, Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f)), R(4.5f, 6.5f), true);
            go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(1f, 0.55f, 1f));
        }
        if (pfRune != null)
        {
            var p = new Vector3(0f, 0f, 24.5f);
            p.y = GroundY(p.x, p.z) - 0.3f;
            PlaceFittedHeight(dais, pfRune, "RuneStone", p, Quaternion.LookRotation(Vector3.back), 3.2f, true);
        }

        // 骷髏：暗示老者會召喚亡者
        if (pfSkullStick != null)
            foreach (float deg in new[] { 60f, 120f, 200f, 340f })
            {
                float a = deg * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ArenaRadius - 1.5f);
                p.y = GroundY(p.x, p.z) - 0.2f;
                PlaceFittedHeight(root, pfSkullStick, "SkullStick", p, Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)), R(1.8f, 2.3f), false);
            }
        for (int i = 0; i < 9; i++)
        {
            var pf = Chance(0.5f) ? pfSkull1 : pfSkull2;
            if (pf == null) continue;
            float a = R(0f, Mathf.PI * 2f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R(ArenaRadius - 4f, ArenaRadius + 4f);
            if (PathMask(p.x, p.z) > 0.1f) continue;
            p.y = GroundY(p.x, p.z) - 0.05f;
            PlaceFittedHeight(root, pf, "Skull", p, Quaternion.Euler(R(-30f, 30f), R(0f, 360f), R(-30f, 30f)), R(0.22f, 0.3f), false);
        }
    }

    // =====================================================================
    //  樹 Prefab：程序生成樹幹＋樹枝（合併成一個 Mesh），
    //  樹冠用程序生成的葉叢面片（LeafCardMesh），LOD1 面片減半
    // =====================================================================

    static List<GameObject> BuildTreePrefabs()
    {

        var list = new List<GameObject>();
        for (int i = 0; i < 4; i++) list.Add(MakeTree($"PF_FI_TallTree_{i}", false, 100 + i));
        for (int i = 0; i < 2; i++) list.Add(MakeTree($"PF_FI_OldPine_{i}", true, 200 + i));
        return list;
    }

    struct Cluster { public Vector3 pos; public float diameter, flatten; }

    static GameObject MakeTree(string name, bool pine, int seed)
    {
        var saved = rng;
        rng = new System.Random(seed);
        var root = new GameObject(name);
        var bark = new List<CombineInstance>();
        var clusters = new List<Cluster>();

        float h = pine ? R(8f, 11f) : R(14f, 18f);
        float r0 = pine ? R(0.42f, 0.55f) : R(0.36f, 0.48f);
        float phase = R(0f, 6.28f);
        float leanX = pine ? R(1.8f, 3.2f) : R(-0.4f, 0.4f);
        float leanZ = pine ? R(-1f, 1f) : R(-0.4f, 0.4f);

        var pts = new List<Vector3>();
        var rad = new List<float>();
        int seg = 12;
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;
            float wob = pine ? Mathf.Sin(t * Mathf.PI * 1.3f + phase) * 1.1f : Mathf.Sin(t * 5f + phase) * 0.25f;
            pts.Add(new Vector3(leanX * t * t + wob * t, h * t, leanZ * t + Mathf.Cos(t * 4f + phase) * 0.15f * t));
            float flare = 1f + 0.8f * Mathf.Exp(-t * 14f);
            rad.Add(r0 * Mathf.Lerp(1f, pine ? 0.35f : 0.32f, t) * flare);
        }
        bark.Add(new CombineInstance { mesh = TubeMesh(null, pts, rad, 12, 2f), transform = Matrix4x4.identity });

        int branches = pine ? RI(4, 6) : RI(6, 9);
        for (int b = 0; b < branches; b++)
        {
            float t0 = pine ? R(0.45f, 0.92f) : R(0.4f, 0.88f);
            Vector3 start = SamplePolyline(pts, t0);
            float yaw = b * (360f / branches) + R(-25f, 25f);
            float pitch = pine ? R(-8f, 18f) : R(25f, 50f);
            float len = (pine ? R(2.4f, 4.2f) : R(3f, 5.2f)) * Mathf.Lerp(1.1f, 0.6f, t0);
            var dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            var bp = new List<Vector3>();
            var br = new List<float>();
            float br0 = Mathf.Lerp(r0 * 0.4f, r0 * 0.2f, t0);
            for (int k = 0; k <= 4; k++)
            {
                float u = k / 4f;
                bp.Add(start + dir * len * u + Vector3.up * (pine ? -0.3f * u * u : 0.4f * u * u));
                br.Add(br0 * Mathf.Lerp(1f, 0.35f, u));
            }
            bark.Add(new CombineInstance { mesh = TubeMesh(null, bp, br, 6, 1f), transform = Matrix4x4.identity });

            int n = pine ? 2 : 3;
            for (int c = 0; c < n; c++)
            {
                var cp = Vector3.Lerp(bp[4], start, c * 0.3f) + new Vector3(R(-0.5f, 0.5f), R(0f, 0.6f), R(-0.5f, 0.5f));
                clusters.Add(new Cluster { pos = cp, diameter = pine ? R(2.8f, 4f) : R(3.4f, 4.8f), flatten = pine ? 0.45f : 0.8f });
            }
        }
        var top = pts[pts.Count - 1];
        clusters.Add(new Cluster { pos = top + Vector3.down * 0.3f, diameter = pine ? R(3f, 4f) : R(4.5f, 6f), flatten = pine ? 0.5f : 0.85f });
        if (!pine)
            for (int k = 0; k < 3; k++)
                clusters.Add(new Cluster { pos = SamplePolyline(pts, R(0.72f, 0.95f)) + new Vector3(R(-1.2f, 1.2f), 0f, R(-1.2f, 1.2f)), diameter = R(4.5f, 6f), flatten = 0.85f });

        // 樹皮
        var barkMesh = CombineAndSave($"{name}_Bark", bark, MeshDir);
        var barkGo = MeshObj(root.transform, "Bark", barkMesh, Vector3.zero, Vector3.zero, Vector3.one, pine ? mDeadBark : mBark);

        // 樹冠：每一團用 4 張交叉的「葉叢面片」，法線從樹冠中心往外（光影像一整團柔軟的葉子）。
        // LOD0 全部面片、LOD1 每團 2 張；每棵樹只有幾千頂點（舊版直接複製灌木 Mesh，一棵就 26 萬頂點）。
        var crownCenter = Vector3.zero;
        foreach (var cl in clusters) crownCenter += cl.pos;
        crownCenter /= Mathf.Max(1, clusters.Count);
        var crown0Mesh = LeafCardMesh($"{name}_Crown0", clusters, crownCenter, 4, pine);
        var crown1Mesh = LeafCardMesh($"{name}_Crown1", clusters, crownCenter, 2, pine);
        var leafMat = pine ? mPineLeaves : mLeaves;
        var crown0 = MeshObj(root.transform, "Crown_LOD0", crown0Mesh, Vector3.zero, Vector3.zero, Vector3.one, leafMat);
        var crown1 = MeshObj(root.transform, "Crown_LOD1", crown1Mesh, Vector3.zero, Vector3.zero, Vector3.one, leafMat);
        crown1.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var lod = root.AddComponent<LODGroup>();
        var barkR = barkGo.GetComponent<Renderer>();
        lod.SetLODs(new[]
        {
            new LOD(0.12f, new[] { barkR, crown0.GetComponent<Renderer>() }),
            new LOD(0.012f, new[] { barkR, crown1.GetComponent<Renderer>() }),
        });
        lod.RecalculateBounds();

        var cap = root.AddComponent<CapsuleCollider>();
        cap.radius = r0 * 1.15f;
        cap.height = pine ? 4f : 8f;
        cap.center = new Vector3(0f, cap.height * 0.5f, 0f);

        // 可以砍倒：不能是 Static Batching（否則倒下時畫面不會動）
        var fell = root.AddComponent<ForestFallingTree>();
        fell.hitsToFell = pine ? 3 : 4;
        ClearStatic(root);
        string path = $"{PrefabDir}/Trees/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        rng = saved;
        return prefab;
    }

    /// <summary>每個葉團產生 cardsPerCluster 張交叉面片（4 頂點一張）。</summary>
    static Mesh LeafCardMesh(string name, List<Cluster> clusters, Vector3 center, int cardsPerCluster, bool pine)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var tris = new List<int>();
        var local = new System.Random(name.GetHashCode());
        float LR(float a, float b) => a + (float)local.NextDouble() * (b - a);
        foreach (var cl in clusters)
        {
            float size = cl.diameter * (cardsPerCluster <= 2 ? 1.25f : 1.05f);
            float yaw0 = LR(0f, 180f);
            for (int c = 0; c < cardsPerCluster; c++)
            {
                // 前三張繞垂直軸交叉，第四張接近水平（從上往下看也是一團）
                Quaternion rot = c < 3
                    ? Quaternion.Euler(LR(-15f, 15f), yaw0 + c * 60f, LR(-10f, 10f))
                    : Quaternion.Euler(LR(60f, 85f), LR(0f, 360f), 0f);
                var right = rot * Vector3.right * size * 0.5f;
                var up = rot * Vector3.up * size * 0.5f * (pine ? 0.6f : 0.85f);
                var p = cl.pos + new Vector3(LR(-0.2f, 0.2f), 0f, LR(-0.2f, 0.2f)) * cl.diameter;
                var corners = new[] { p - right - up, p + right - up, p + right + up, p - right + up };
                int b = verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    verts.Add(corners[k]);
                    // 球形法線：從整個樹冠中心往外，再偏上一點
                    var n = (corners[k] - center).normalized + Vector3.up * 0.35f;
                    normals.Add(n.normalized);
                    // 越靠樹冠下方越暗（頂點色，給 shader 當 AO 用不到也無妨）
                    float ao = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(center.y - 3f, center.y + 3f, corners[k].y));
                    colors.Add(new Color(ao, ao, ao, 1f));
                }
                // 隨機使用貼圖的四個象限之一，避免每團長得一樣
                int q = local.Next(0, 4);
                float u0 = (q % 2) * 0.5f, v0 = (q / 2) * 0.5f;
                uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0 + 0.5f, v0));
                uvs.Add(new Vector2(u0 + 0.5f, v0 + 0.5f)); uvs.Add(new Vector2(u0, v0 + 0.5f));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        string path = $"{MeshDir}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>葉叢貼圖（2×2 四種變化）：一團一團的小葉片，邊緣稀疏、中間密。</summary>
    static Texture2D MakeLeafClusterTexture(string name, Color dark, Color light, float leafLen)
    {
        const int S = 512;
        var col = new Color[S * S];
        for (int i = 0; i < col.Length; i++) col[i] = new Color(dark.r, dark.g, dark.b, 0f);
        var local = new System.Random(name.GetHashCode());
        float LR(float a, float b) => a + (float)local.NextDouble() * (b - a);
        for (int q = 0; q < 4; q++)
        {
            float cx = (q % 2) * 0.5f + 0.25f, cy = (q / 2) * 0.5f + 0.25f;
            for (int i = 0; i < 520; i++)
            {
                // 圓內分布，越外圈越少
                float ang = LR(0f, Mathf.PI * 2f);
                float rr = Mathf.Pow(LR(0f, 1f), 0.65f) * 0.21f;
                float lx = cx + Mathf.Cos(ang) * rr, ly = cy + Mathf.Sin(ang) * rr * 0.9f;
                float len = leafLen * LR(0.7f, 1.2f), wid = len * LR(0.28f, 0.42f);
                float rot = LR(0f, Mathf.PI);
                float shade = Mathf.Clamp01(LR(0f, 1f) * 0.6f + (ly - cy + 0.21f) / 0.42f * 0.5f);
                var c = Color.Lerp(dark, light, shade);
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                int x0 = Mathf.FloorToInt((lx - len) * S), x1 = Mathf.CeilToInt((lx + len) * S);
                int y0 = Mathf.FloorToInt((ly - len) * S), y1 = Mathf.CeilToInt((ly + len) * S);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        // 每個象限各自裁切，避免葉片畫到隔壁
                        int qx = Mathf.Clamp(x, (q % 2) * S / 2 + 2, (q % 2) * S / 2 + S / 2 - 3);
                        int qy = Mathf.Clamp(y, (q / 2) * S / 2 + 2, (q / 2) * S / 2 + S / 2 - 3);
                        if (qx != x || qy != y) continue;
                        float dx = (x + 0.5f) / S - lx, dy = (y + 0.5f) / S - ly;
                        float u = (dx * cs + dy * sn) / len, v = (-dx * sn + dy * cs) / wid;
                        float e = u * u + v * v;
                        if (e > 1f) continue;
                        float edge = 1f - e;
                        var cc = c * Mathf.Lerp(0.85f, 1.1f, edge);
                        col[y * S + x] = new Color(cc.r, cc.g, cc.b, 1f);
                    }
            }
        }
        string path = $"{TexDir}/{name}.png";
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(col);
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.mipMapsPreserveCoverage = true;   // 遠處 mipmap 不會讓葉子變透明消失
        ti.alphaTestReferenceValue = 0.5f;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material LeafMaterial(string name, Texture2D tex, Color tint)
    {
        var mat = GetOrCreateMat(name, "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Smoothness", 0.05f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cull", 0f);              // 雙面
        mat.doubleSidedGI = true;
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Vector3 SamplePolyline(List<Vector3> pts, float t)
    {
        float f = t * (pts.Count - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, pts.Count - 2);
        return Vector3.Lerp(pts[i], pts[i + 1], f - i);
    }

    /// <summary>沿著中心線產生圓管 Mesh（樹幹 / 樹枝）。name = null 時不存 asset。</summary>
    static Mesh TubeMesh(string name, List<Vector3> pts, List<float> radii, int sides, float vScale)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Vector3 prevSide = Vector3.right;
        float vAcc = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 tan = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(tan, Vector3.Cross(prevSide, tan)).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(tan, Vector3.forward).normalized;
            prevSide = side;
            Vector3 bin = Vector3.Cross(tan, side);
            if (i > 0) vAcc += Vector3.Distance(pts[i], pts[i - 1]) / (2f * Mathf.PI * Mathf.Max(0.05f, radii[i])) * vScale;
            for (int k = 0; k <= sides; k++)
            {
                float a = k / (float)sides * Mathf.PI * 2f;
                float bump = 1f + 0.08f * Mathf.Sin(a * 3f + i * 0.7f);
                Vector3 n = side * Mathf.Cos(a) + bin * Mathf.Sin(a);
                verts.Add(pts[i] + n * radii[i] * bump);
                normals.Add(n);
                uvs.Add(new Vector2(k / (float)sides * 2f, vAcc));
            }
        }
        int ring = sides + 1;
        for (int i = 0; i < pts.Count - 1; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = i * ring + k, b = a + 1, c = a + ring, d = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
        var mesh = new Mesh { name = name ?? "Tube" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        if (name != null)
        {
            string path = $"{MeshDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }
        return mesh;
    }

    // =====================================================================
    //  森林、林下植物
    // =====================================================================

    static bool BlockedForVegetation(float x, float z, float pathPad)
    {
        float d = Mathf.Sqrt(x * x + z * z);
        if (d > RimRadius(Mathf.Atan2(z, x)) - 3f) return true;
        if (PathMask(x, z) > pathPad) return true;
        // 北側石台
        if (Mathf.Abs(x) < 7f && z > 17f && z < 28f) return true;
        return false;
    }

    static void BuildForest(Transform env, List<GameObject> trees)
    {
        var root = Group("Forest", env);
        var placed = new List<Vector2>();
        int count = 0, tries = 0;
        while (count < 170 && tries++ < 6000)
        {
            float a = R(0f, Mathf.PI * 2f);
            float d = Mathf.Sqrt(R((ArenaRadius + 5f) * (ArenaRadius + 5f), 75f * 75f));
            float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
            if (BlockedForVegetation(x, z, 0.02f)) continue;
            if (PathMask(x * 0.98f, z) > 0f && Mathf.Abs(x - PathOffsetX(z)) < 5.5f) continue;
            var p2 = new Vector2(x, z);
            bool tooClose = false;
            foreach (var q in placed) if ((q - p2).sqrMagnitude < 4.8f * 4.8f) { tooClose = true; break; }
            if (tooClose) continue;
            placed.Add(p2);

            float rim = RimRadius(a);
            bool nearEdge = d > rim - 12f;
            // 崖邊偏向放歪脖古松，林內放高樹
            var pf = nearEdge && Chance(0.6f) ? trees[RI(4, 6)] : trees[RI(0, 4)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(x, GroundY(x, z) - 0.25f, z);
            // 古松朝島外傾
            float yaw = nearEdge && pf.name.Contains("Pine") ? -a * Mathf.Rad2Deg + 90f + R(-30f, 30f) : R(0f, 360f);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            float s = R(0.85f, 1.2f);
            go.transform.localScale = new Vector3(s, s * R(0.95f, 1.1f), s);
            count++;
        }

        // 空地邊緣幾棵枯樹，增加輪廓
        for (int i = 0; i < 4; i++)
        {
            float deg = R(0f, 360f);
            if (Mathf.Abs(Mathf.DeltaAngle(deg, -90f)) < 25f || Mathf.Abs(Mathf.DeltaAngle(deg, 90f)) < 20f) continue;
            float a = deg * Mathf.Deg2Rad;
            float d = ArenaRadius + R(3f, 5f);
            var p = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            p.y = GroundY(p.x, p.z) - 0.2f;
            var g = Group("DeadTree", root);
            g.position = p;
            g.rotation = Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f));
            float th = R(6f, 9f);
            var pts = new List<Vector3>();
            var rad = new List<float>();
            for (int k = 0; k <= 6; k++)
            {
                float t = k / 6f;
                pts.Add(new Vector3(Mathf.Sin(t * 3f + i) * 0.5f * t, th * t, 0f));
                rad.Add(Mathf.Lerp(0.38f, 0.06f, t) * (1f + 0.6f * Mathf.Exp(-t * 12f)));
            }
            var trunk = MeshObj(g, "Trunk", TubeMesh($"FI_DeadTree_{i}", pts, rad, 9, 2f), Vector3.zero, Vector3.zero, Vector3.one, mDeadBark);
            var cap = trunk.AddComponent<CapsuleCollider>();
            cap.radius = 0.4f; cap.height = 4f; cap.center = new Vector3(0f, 2f, 0f);
            for (int k = 0; k < 2; k++)
            {
                var sp = g.TransformPoint(SamplePolyline(pts, R(0.5f, 0.85f)));
                Segment(g, "DeadBranch", sp, sp + Quaternion.Euler(-R(20f, 50f), R(0f, 360f), 0f) * Vector3.forward * R(1.2f, 2.2f), 0.07f, mDeadBark);
            }
        }
    }

    /// <summary>林間散落的可破壞巨石（大小不一，越大越耐打）。</summary>
    static void BuildForestBoulders(Transform env)
    {
        var root = Group("ForestBoulders (destructible)", env);
        Physics.SyncTransforms();
        int placed = 0, tries = 0;
        while (placed < 28 && tries++ < 600)
        {
            float a = R(0f, Mathf.PI * 2f);
            float d = R(ArenaRadius + 4f, 66f);
            float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
            if (BlockedForVegetation(x, z, 0.05f)) continue;
            var p = new Vector3(x, GroundY(x, z) - 0.3f, z);
            bool blocked = false;   // 不要塞進樹幹或其他石頭（地形本身不算）
            foreach (var c in Physics.OverlapSphere(p + Vector3.up * 1.2f, 1.8f, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider)) { blocked = true; break; }
            if (blocked) continue;
            float sz = R(1.4f, 3.4f);
            var go = PlaceFitted(root, pfBoulders[RI(0, pfBoulders.Count)], $"Boulder_{placed:00}", p,
                Quaternion.Euler(R(-15f, 15f), R(0f, 360f), R(-15f, 15f)), sz, true);
            MakeDestructibleRock(go, sz < 2f ? 3 : sz < 2.8f ? 4 : 5);
            Physics.SyncTransforms();
            placed++;
        }
    }

    static void MakeDestructibleRock(GameObject go, int hits)
    {
        if (go == null) return;
        var rock = go.AddComponent<ForestDestructibleRock>();
        rock.hitsToBreak = hits;
        ClearStatic(go);
    }

    static void ClearStatic(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
    }

    static void BuildUndergrowth(Transform env)
    {
        var root = Group("Undergrowth", env);
        Scatter(root, "Grass", pfGrass, 800, ArenaRadius - 6f, 74f, 0.6f, 1.2f, 0.4f, true);
        // 空地裡稀疏的草（保留戰鬥時的可讀性）
        Scatter(root, "ArenaGrass", pfGrass, 140, 4f, ArenaRadius, 0.5f, 0.9f, 0.4f, true);
        Scatter(root, "Fern", pfFerns, 240, ArenaRadius + 3f, 74f, 0.8f, 1.5f, 0.3f, false);
        // GreenBush 一叢 1.3 萬頂點太重，只用 Broadleaf
        Scatter(root, "Bush", new List<GameObject> { pfBroadleaf }, 70, ArenaRadius + 4f, 72f, 0.7f, 1.3f, 0.1f, false);
    }

    static void Scatter(Transform parent, string name, List<GameObject> pool, int count, float minR, float maxR, float sMin, float sMax, float pathPad, bool allowOnPathEdge)
    {
        if (pool.Count == 0) return;
        var g = Group(name, parent);
        int placed = 0, tries = 0;
        while (placed < count && tries++ < count * 6)
        {
            float a = R(0f, Mathf.PI * 2f);
            float d = Mathf.Sqrt(R(minR * minR, maxR * maxR));
            float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
            if (BlockedForVegetation(x, z, allowOnPathEdge ? 0.6f : pathPad)) continue;
            var pf = pool[RI(0, pool.Count)];
            if (pf == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            go.transform.SetParent(g, false);
            go.transform.position = new Vector3(x, GroundY(x, z) - 0.05f, z);
            go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * R(sMin, sMax);
            FixMissingMaterials(go);
            // 小植物不投影子（陰影要把所有東西再畫一次，草叢最吃效能）
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            placed++;
        }
    }

    // =====================================================================
    //  遠景：霧中的石柱群（水墨山水的層次），頂上長幾棵古松
    // =====================================================================

    static void BuildDistantPeaks(Transform env, List<GameObject> trees)
    {
        var root = Group("DistantPeaks", env);
        var parts = new List<CombineInstance>();
        for (int i = 0; i < 22; i++)
        {
            float deg = i * (360f / 22f) + R(-6f, 6f);
            if (Mathf.Abs(Mathf.DeltaAngle(deg, -90f)) < 9f) continue;
            float a = deg * Mathf.Deg2Rad;
            float dist = R(140f, 290f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist;
            float top = R(-5f, 60f) * Mathf.InverseLerp(100f, 300f, dist) + R(-10f, 25f);
            float radius = R(9f, 20f);
            parts.Add(new CombineInstance { mesh = RockColumnMesh(null, -140f, top, radius, R(0.4f, 0.7f), RI(0, 100000), 0.45f), transform = Matrix4x4.Translate(p) });
            // 旁邊再一根較矮的副峰
            if (Chance(0.6f))
            {
                var q = p + new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)).normalized * radius * R(1.4f, 2f);
                parts.Add(new CombineInstance { mesh = RockColumnMesh(null, -140f, top - R(15f, 35f), radius * R(0.5f, 0.75f), 0.6f, RI(0, 100000), 0.45f), transform = Matrix4x4.Translate(q) });
            }
            // 峰頂古松
            int pines = RI(1, 3);
            for (int k = 0; k < pines; k++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(trees[RI(4, 6)]);
                go.transform.SetParent(root, false);
                go.transform.position = p + new Vector3(R(-0.4f, 0.4f), 0f, R(-0.4f, 0.4f)) * radius * 0.5f + Vector3.up * (top - 0.5f);
                go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                go.transform.localScale = Vector3.one * R(1.6f, 2.4f);
                foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            }
        }
        var mesh = CombineAndSave("Mesh_FI_DistantPeaks", parts, SceneAssetDir);
        var peaks = MeshObj(root, "PeakColumns", mesh, Vector3.zero, Vector3.zero, Vector3.one, mRock);
        peaks.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // 霧海底板：和霧同色，擋住地形最底下的方形邊界
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.name = "MistFloor";
        floor.transform.SetParent(root, false);
        floor.transform.position = new Vector3(0f, -36f, 0f);
        floor.transform.localScale = new Vector3(160f, 1f, 160f);
        var fr = floor.GetComponent<MeshRenderer>();
        fr.sharedMaterial = mMistFloor;
        fr.shadowCastingMode = ShadowCastingMode.Off;
        fr.receiveShadows = false;
    }

    // =====================================================================
    //  隱形邊界：島邊一圈 + 吊橋口留空
    // =====================================================================

    static void BuildBoundary(Transform env)
    {
        var root = Group("IslandBoundary (invisible)", env);
        float step = 5f;
        for (float deg = 0f; deg < 360f; deg += step)
        {
            float mid = deg + step * 0.5f;
            if (Mathf.Abs(Mathf.DeltaAngle(mid, -90f)) < 3.2f) continue;
            float a0 = deg * Mathf.Deg2Rad, a1 = (deg + step) * Mathf.Deg2Rad;
            var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (RimRadius(a0) - 1.2f);
            var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (RimRadius(a1) - 1.2f);
            var c = (p0 + p1) * 0.5f;
            c.y = Mathf.Max(GroundY(p0.x, p0.z), GroundY(p1.x, p1.z)) + 2f;
            var go = new GameObject($"Wall_{deg:000}");
            go.transform.SetParent(root, false);
            go.transform.position = c;
            go.transform.rotation = Quaternion.LookRotation(p1 - p0);
            go.AddComponent<BoxCollider>().size = new Vector3(0.6f, 8f, (p1 - p0).magnitude + 0.6f);
        }
        // 橋頭兩側補牆，避免從橋柱旁繞出去
        foreach (float side in new[] { -1f, 1f })
        {
            var go = new GameObject("BridgeMouthWall");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(side * (BridgeWidth * 0.5f + 2.3f), 2f, BridgeRimZ + 1.5f);
            go.AddComponent<BoxCollider>().size = new Vector3(4f, 8f, 4f);
        }
    }

    // =====================================================================
    //  氣氛粒子：雲海、林間薄霧、落葉、浮游光點
    // =====================================================================

    static void BuildAtmosphereVfx(Transform env)
    {
        var root = Group("AtmosphereVFX", env);

        // 雲海：島下方大片緩慢飄動的霧團
        {
            var ps = NewPS("CloudSea", root, new Vector3(0f, -24f, 0f));
            var main = ps.main;
            main.duration = 20f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(40f, 60f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(45f, 80f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.82f, 0.84f, 0.55f), new Color(0.9f, 0.92f, 0.93f, 0.75f));
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(480f, 18f, 480f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.2f, 0.75f, 1f);
            SetRenderer(ps, mCloud, 0.6f);
        }

        // 崖邊往上飄的霧
        {
            var ps = NewPS("CliffUpdraftMist", root, new Vector3(0f, -10f, 0f));
            var main = ps.main;
            main.duration = 10f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(12f, 22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.85f, 0.88f, 0.9f, 0.35f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 3.5f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = RimBase + 14f; sh.radiusThickness = 0.15f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            vel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.25f, 0.55f, 1f);
            SetRenderer(ps, mCloud, 0.45f);
        }

        // 林間低霧
        {
            var ps = NewPS("ForestGroundMist", root, new Vector3(0f, 1.2f, 0f));
            var main = ps.main;
            main.duration = 10f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 26f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(7f, 13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.86f, 0.9f, 0.88f, 0.12f);
            main.maxParticles = 70;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 3f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(130f, 1.5f, 130f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.05f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.2f, 0.7f, 1f);
            SetRenderer(ps, mMist, 0.4f);
        }

        // 落葉
        {
            var ps = NewPS("FallingLeaves", root, new Vector3(0f, 16f, 0f));
            var main = ps.main;
            main.duration = 10f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.6f, 0.25f), new Color(0.75f, 0.55f, 0.22f));
            main.gravityModifier = 0.02f;
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 16f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(110f, 1f, 110f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.7f, -0.35f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.3f; noise.scrollSpeed = 0.3f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.08f, 0.85f, 1f);
            SetRenderer(ps, mLeaf, 2f);
        }

        // 浮游光點（空地上方，暖色微光）
        {
            var ps = NewPS("FloatingMotes", root, new Vector3(0f, 2.2f, 0f));
            var main = ps.main;
            main.duration = 8f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(0.85f, 1f, 0.75f));
            main.maxParticles = 220;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 24f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = ArenaRadius + 10f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.4f; noise.scrollSpeed = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.2f, 0.6f, 1f);
            SetRenderer(ps, mMote, 2f);
        }
    }

    // =====================================================================
    //  光線 / 天空 / 霧 / 後製
    // =====================================================================

    static void BuildLighting()
    {
        var lighting = new GameObject("Lighting").transform;

        var sunGo = new GameObject("Sun (Misty Afternoon)");
        sunGo.transform.SetParent(lighting, false);
        sunGo.transform.rotation = Quaternion.Euler(34f, 150f, 0f);   // 從北側斜後方照過來，玩家面向北時有逆光輪廓
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.92f, 0.78f);
        sun.intensity = 1.5f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.8f;

        var sky = GetOrCreateMat("M_FI_Skybox", "Skybox/Procedural");
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_SunSizeConvergence", 4f);
        sky.SetFloat("_AtmosphereThickness", 1.35f);
        sky.SetColor("_SkyTint", new Color(0.55f, 0.6f, 0.62f));
        sky.SetColor("_GroundColor", new Color(0.62f, 0.66f, 0.68f));
        sky.SetFloat("_Exposure", 1.15f);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.5f, 0.46f);
        RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.18f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.66f, 0.71f, 0.72f);    // 偏灰青的霧，遠景融成水墨般的層次
        RenderSettings.fogDensity = 0.0115f;
        DynamicGI.UpdateEnvironment();

        string profilePath = SceneAssetDir + "/VP_ForestIsland.asset";
        AssetDatabase.DeleteAsset(profilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, profilePath);

        var bloom = AddVolumeComponent<Bloom>(profile);
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.6f);
        bloom.scatter.Override(0.75f);

        var grade = AddVolumeComponent<ColorAdjustments>(profile);
        grade.postExposure.Override(0.1f);
        grade.contrast.Override(12f);
        grade.saturation.Override(-18f);
        grade.colorFilter.Override(new Color(0.98f, 1f, 0.98f));

        var smh = AddVolumeComponent<ShadowsMidtonesHighlights>(profile);
        smh.shadows.Override(new Vector4(0.92f, 1f, 1.05f, 0f));   // 暗部偏冷
        smh.highlights.Override(new Vector4(1.04f, 1.01f, 0.95f, 0f));

        var vignette = AddVolumeComponent<Vignette>(profile);
        vignette.intensity.Override(0.26f);
        vignette.smoothness.Override(0.5f);

        var tone = AddVolumeComponent<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);
        AssetDatabase.SaveAssets();

        var volGo = new GameObject("Global Volume (ForestIsland)");
        volGo.transform.SetParent(lighting, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;
        vol.sharedProfile = profile;
    }

    static T AddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var comp = profile.Add<T>(true);
        comp.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(comp, profile);
        return comp;
    }

    static void EnablePostProcessing(GameObject rig)
    {
        var cam = rig.GetComponentInChildren<Camera>(true);
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(data);
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 600f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
    }

    // =====================================================================
    //  素材載入 / 材質 / 貼圖
    // =====================================================================

    static bool LoadSourcePrefabs()
    {
        pfGrass.Clear(); pfFerns.Clear(); pfBoulders.Clear();
        pfBroadleaf = LoadPrefab($"{Veg}/BroadleafShrub_01/Broadleaf_Shrub_01_Var4_Prefab.prefab");
        pfGreenBush = LoadPrefab($"{Veg}/GreenBush/GreenBush_Var01_Prefab.prefab");
        for (int i = 1; i <= 6; i++) AddIf(pfGrass, LoadPrefab($"{Veg}/MeadowGrass_01/Meadow_Grass_01_Var{i}_Prefab.prefab"));
        for (int i = 1; i <= 3; i++) AddIf(pfFerns, LoadPrefab($"{Veg}/Ferns/Fern_var0{i}_Prefab.prefab"));
        foreach (var n in new[] { "pf_boulder_01_150", "pf_boulder_01", "pf_boulder_02_025", "pf_boulder_01_040" })
            AddIf(pfBoulders, LoadPrefab($"{Viking}/Prefabs/Props/{n}.prefab"));
        pfBoulderFlat = LoadPrefab($"{Viking}/Prefabs/Props/pf_boulder_02_025.prefab") ?? (pfBoulders.Count > 0 ? pfBoulders[0] : null);
        pfMenhir1 = LoadPrefab($"{Viking}/Prefabs/Props/pf_menhir_01.prefab");
        pfMenhir2 = LoadPrefab($"{Viking}/Prefabs/Props/pf_menhir_02.prefab") ?? pfMenhir1;
        pfRune = LoadPrefab($"{Viking}/Prefabs/Props/pf_rune_01.prefab");
        pfSkull1 = LoadPrefab($"{Viking}/Prefabs/Props/pf_skull_01.prefab");
        pfSkull2 = LoadPrefab($"{Viking}/Prefabs/Props/pf_skull_02.prefab") ?? pfSkull1;
        pfSkullStick = LoadPrefab($"{Viking}/Prefabs/Props/pf_skull_stick_01.prefab");

        if (pfBoulders.Count == 0 || pfMenhir1 == null || (pfBroadleaf == null && pfGreenBush == null))
        {
            Debug.LogError("[ForestIsland] 找不到維京村莊 / Book of the Dead 的石頭或植物 Prefab，無法建立場景。");
            return false;
        }
        if (pfBroadleaf == null) pfBroadleaf = pfGreenBush;
        if (pfGreenBush == null) pfGreenBush = pfBroadleaf;
        return true;
    }

    static GameObject LoadPrefab(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) Debug.LogWarning($"[ForestIsland] 找不到 {path}");
        return go;
    }

    static void AddIf(List<GameObject> list, GameObject go) { if (go != null) list.Add(go); }

    static Texture2D LoadTex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static void CreateMaterials()
    {
        // 樹皮：程序貼圖（縱向纖維＋裂紋），配同一張高度圖算出的法線
        var barkH = MakeBarkHeight(256);
        var barkAlbedo = SaveTexture("T_FI_Bark", 256, (x, y) =>
        {
            float hgt = barkH[y * 256 + x];
            var c = Color.Lerp(new Color(0.12f, 0.1f, 0.08f), new Color(0.36f, 0.32f, 0.27f), hgt);
            return new Color(c.r, c.g, c.b, 1f);
        }, false, TextureWrapMode.Repeat);
        var barkNormal = SaveNormalMap("T_FI_Bark_N", 256, barkH, 3f);

        mBark = Lit("M_FI_Bark", new Color(0.85f, 0.82f, 0.78f), 0.12f);
        mBark.SetTexture("_BaseMap", barkAlbedo);
        mBark.SetTexture("_BumpMap", barkNormal);
        mBark.EnableKeyword("_NORMALMAP");
        mBark.SetTextureScale("_BaseMap", Vector2.one);

        mDeadBark = Lit("M_FI_OldBark", new Color(0.62f, 0.6f, 0.58f), 0.08f);
        mDeadBark.SetTexture("_BaseMap", barkAlbedo);
        mDeadBark.SetTexture("_BumpMap", barkNormal);
        mDeadBark.EnableKeyword("_NORMALMAP");

        mRope = Lit("M_FI_Rope", new Color(0.48f, 0.4f, 0.28f), 0.05f);
        mPlank = Lit("M_FI_BridgePlank", new Color(0.62f, 0.55f, 0.46f), 0.1f);
        var wood = LoadTex($"{Viking}/Textures/wood_tiled_02_a.tif");
        var woodN = LoadTex($"{Viking}/Textures/wood_tiled_02_n.tif");
        if (wood != null) mPlank.SetTexture("_BaseMap", wood);
        if (woodN != null) { mPlank.SetTexture("_BumpMap", woodN); mPlank.EnableKeyword("_NORMALMAP"); }
        mRock = Lit("M_FI_CliffRock", new Color(0.7f, 0.72f, 0.7f), 0.08f);
        var rockA = LoadTex($"{Viking}/Textures/Props/prop_boulder_01_d.tif");
        var rockN = LoadTex($"{Viking}/Textures/Props/prop_boulder_01_n.tif");
        if (rockA != null) mRock.SetTexture("_BaseMap", rockA);
        if (rockN != null) { mRock.SetTexture("_BumpMap", rockN); mRock.EnableKeyword("_NORMALMAP"); }

        mLeaves = LeafMaterial("M_FI_Leaves",
            MakeLeafClusterTexture("T_FI_LeafCluster", new Color(0.16f, 0.24f, 0.1f), new Color(0.46f, 0.56f, 0.24f), 0.022f),
            new Color(0.9f, 0.95f, 0.85f));
        mPineLeaves = LeafMaterial("M_FI_PineLeaves",
            MakeLeafClusterTexture("T_FI_PineCluster", new Color(0.08f, 0.15f, 0.1f), new Color(0.3f, 0.4f, 0.26f), 0.03f),
            new Color(0.85f, 0.9f, 0.85f));

        mMistFloor = GetOrCreateMat("M_FI_MistFloor", "Universal Render Pipeline/Unlit");
        mMistFloor.SetColor("_BaseColor", new Color(0.66f, 0.71f, 0.72f));
        foreach (var m in new[] { mBark, mDeadBark, mRope, mPlank, mRock, mMistFloor }) EditorUtility.SetDirty(m);

        // 粒子貼圖
        var soft = SaveTexture("T_FI_SoftCloud", 128, (x, y) =>
        {
            float u = (x + 0.5f) / 128f * 2f - 1f, v = (y + 0.5f) / 128f * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            float n = Fbm(u * 2.2f + 5f, v * 2.2f + 3f);
            float a = Mathf.Clamp01(1f - d) * Mathf.Clamp01(n * 1.6f - 0.25f);
            a = a * a * (3f - 2f * a);
            return new Color(1f, 1f, 1f, a);
        }, true, TextureWrapMode.Clamp);
        var leaf = SaveTexture("T_FI_Leaf", 64, (x, y) =>
        {
            float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 64f * 2f - 1f;
            float e = (u * u) / 0.16f + (v * v) / 0.9f;           // 細長橢圓
            float vein = Mathf.Abs(u) < 0.04f ? 0.75f : 1f;
            float a = Mathf.Clamp01((1f - e) * 6f);
            return new Color(vein, vein, vein, a);
        }, true, TextureWrapMode.Clamp);
        var dot = SaveTexture("T_FI_Dot", 32, (x, y) =>
        {
            float u = (x + 0.5f) / 32f * 2f - 1f, v = (y + 0.5f) / 32f * 2f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
            return new Color(1f, 1f, 1f, a * a);
        }, true, TextureWrapMode.Clamp);

        mCloud = ParticleMat("M_FI_CloudSea", soft, false);
        mMist = ParticleMat("M_FI_GroundMist", soft, false);
        mLeaf = ParticleMat("M_FI_Leaf", leaf, false);
        mMote = ParticleMat("M_FI_Mote", dot, true);
    }

    static float[] MakeBarkHeight(int size)
    {
        var h = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                // 縱向纖維（x 方向週期、y 方向拉長），用可平鋪的取樣
                float fib = TileNoise(u * 18f, v * 3f, 18f, 3f);
                float crack = TileNoise(u * 7f + 3.3f, v * 1.5f + 1.1f, 7f, 1.5f);
                float ridge = 1f - Mathf.Abs(crack * 2f - 1f);
                float val = fib * 0.55f + Mathf.SmoothStep(0.55f, 0.95f, ridge) * 0.45f;
                h[y * size + x] = Mathf.Clamp01(val);
            }
        return h;
    }

    /// <summary>四角混合的可平鋪 Perlin（週期 px, py）。</summary>
    static float TileNoise(float x, float y, float px, float py)
    {
        float a = Mathf.PerlinNoise(x, y);
        float b = Mathf.PerlinNoise(x - px, y);
        float c = Mathf.PerlinNoise(x, y - py);
        float d = Mathf.PerlinNoise(x - px, y - py);
        float fx = (x % px) / px, fy = (y % py) / py;
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static Texture2D SaveTexture(string name, int size, System.Func<int, int, Color> f, bool alpha, TextureWrapMode wrap)
    {
        string path = $"{TexDir}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, f(x, y));
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = alpha;
        ti.wrapMode = wrap;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D SaveNormalMap(string name, int size, float[] h, float strength)
    {
        string path = $"{TexDir}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float l = h[y * size + (x + size - 1) % size], r = h[y * size + (x + 1) % size];
                float d = h[((y + size - 1) % size) * size + x], u = h[((y + 1) % size) * size + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
            }
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.NormalMap;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material GetOrCreateMat(string name, string shaderName)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(shaderName);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (shader != null && mat.shader != shader) mat.shader = shader;
        return mat;
    }

    static Material Lit(string name, Color color, float smoothness)
    {
        var mat = GetOrCreateMat(name, "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material ParticleMat(string name, Texture2D tex, bool additive)
    {
        var mat = GetOrCreateMat(name, "Universal Render Pipeline/Particles/Unlit");
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // =====================================================================
    //  物件小工具
    // =====================================================================

    static Bounds PrefabBounds(GameObject prefab)
    {
        if (boundsCache.TryGetValue(prefab, out var b)) return b;
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        tmp.transform.localScale = Vector3.one;
        bool has = false;
        b = new Bounds(Vector3.zero, Vector3.one);
        foreach (var r in tmp.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        Object.DestroyImmediate(tmp);
        boundsCache[prefab] = b;
        return b;
    }

    /// <summary>放 Prefab，縮放到最大邊 = size。</summary>
    static GameObject PlaceFitted(Transform parent, GameObject prefab, string name, Vector3 pos, Quaternion rot, float size, bool keepColliders)
    {
        var b = PrefabBounds(prefab);
        float s = size / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        return Place(parent, prefab, name, pos, rot, s, keepColliders);
    }

    /// <summary>放 Prefab，縮放到高度 = height，並讓底部貼在 pos。</summary>
    static GameObject PlaceFittedHeight(Transform parent, GameObject prefab, string name, Vector3 pos, Quaternion rot, float height, bool keepColliders)
    {
        var b = PrefabBounds(prefab);
        float s = height / Mathf.Max(0.01f, b.size.y);
        var go = Place(parent, prefab, name, pos, rot, s, keepColliders);
        go.transform.position += rot * new Vector3(0f, -b.min.y * s, 0f);
        return go;
    }

    static GameObject Place(Transform parent, GameObject prefab, string name, Vector3 pos, Quaternion rot, float scale, bool keepColliders)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = Vector3.one * scale;
        if (!keepColliders)
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    /// <summary>有些 Book of the Dead 灌木的 LOD1/LOD2 材質在專案裡遺失（會變洋紅），只在場景實例上補成 LOD0 的材質。</summary>
    static void FixMissingMaterials(GameObject go)
    {
        Material fallback = null;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rs) foreach (var m in r.sharedMaterials) if (m != null && fallback == null) fallback = m;
        if (fallback == null) return;
        foreach (var r in rs)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++) if (mats[i] == null) { mats[i] = fallback; changed = true; }
            if (changed) r.sharedMaterials = mats;
        }
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m, bool shadows = true)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static GameObject Box(Transform p, string n, Vector3 pos, Vector3 euler, Vector3 scale, Material m) =>
        Prim(PrimitiveType.Cube, p, n, pos, euler, scale, m);

    /// <summary>scale.y = 半高（Unity 圓柱高度 2）</summary>
    static GameObject Cyl(Transform p, string n, Vector3 pos, Vector3 euler, Vector3 scale, Material m) =>
        Prim(PrimitiveType.Cylinder, p, n, pos, euler, scale, m);

    static void AddCollider(GameObject go) => go.AddComponent<CapsuleCollider>();

    static GameObject MeshObj(Transform parent, string name, Mesh mesh, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static ParticleSystem NewPS(string name, Transform parent, Vector3 worldPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        return ps;
    }

    static void SetRenderer(ParticleSystem ps, Material m, float maxScreenSize)
    {
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = m;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
        pr.maxParticleSize = maxScreenSize;
        pr.sortMode = ParticleSystemSortMode.Distance;
    }

    static Gradient FadeInOut(Color c, float inT, float outStart, float peak)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inT), new GradientAlphaKey(peak, outStart), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
