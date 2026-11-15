#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 選單：GeminiGrowUp > 道場：建立後山瀑布與山路 (Dojo → Forest Island)
///
/// 在 DojoArena_ArchitecturePreview 的東北方牆外加一座山，把道場與孤島森林連起來：
///   - 後院溪流往東延伸出牆外，接到從山崖落下的瀑布和水潭（牆下有一段小落差接回院內的溪）
///   - 東牆最北端開一個缺口（只把兩段 KHS_WallBase 關掉，沒有刪除），沿溪流北岸出去
///   - 之字形山路（鳥居、石燈籠、黑松、竹林、櫻花）爬到約 25m 高的山頂平台
///   - 平台往東接一座吊橋伸進霧裡，走到中段觸發 SceneTransitionTrigger → 載入 ForestIsland
///
/// 山的內容做成 Prefab（Prefabs/DojoMountain/PF_DojoMountainPath.prefab），道場場景裡只放一個實例，
/// 對隊友正在改的道場場景影響最小。可重複執行（會覆蓋 Prefab 與地形資料，並重新放實例）。
/// 也會把 ForestIsland 與道場場景加進 Build Settings（不改 GameManager 的關卡順序）。
/// </summary>
public static class DojoMountainPathBuilder
{
    const string Root = "Assets/_Project";
    public const string DojoScenePath = Root + "/Scenes/DojoArena_ArchitecturePreview.unity";
    const string ForestScenePath = Root + "/Scenes/ForestIsland.unity";
    const string OutDir = Root + "/Prefabs/DojoMountain";
    const string MeshDir = OutDir + "/Meshes";
    const string MatDir = Root + "/Materials/DojoMountain";
    const string PrefabPath = OutDir + "/PF_DojoMountainPath.prefab";
    const string InstanceName = "DojoMountainPath (→ ForestIsland)";

    // ---- 版面（道場場景的世界座標）----
    const float WallX = 48.05f;
    static readonly float[] GateWallZ = { 122f, 125f };          // 東牆要開口的兩段（山路出口）

    // 月洞門：溪流穿過東牆的地方（Models/MoonGate，Tripo 模型）
    const string MoonGateDir = Root + "/Models/MoonGate";
    const string MoonGateFbx = MoonGateDir + "/MoonGate.fbx";
    const float RiverWallZ = 114.1f;          // 溪流在東牆處的中心
    const float MoonGateHeight = 6f;          // 月洞門初始高度；之後會再依翼牆高度對齊圍牆（WallTopY）放大
    static readonly Color MoonGateTint = new Color(0.86f, 0.83f, 0.74f);   // 白牆調成和道場灰泥牆相近的米色
    static float moonGateHalfSpan = 0f;       // 實際放好後的半寬
    static float moonGateCenterZ = 122.6f;    // 月洞門（山路入口）中心，北端貼著後牆轉角
    const float TerrainX0 = 48.5f, TerrainZ0 = 40f, TerrainSize = 220f, TerrainHeight = 90f, TerrainY = -0.3f;
    const int HeightRes = 513;

    // 山路中心線 (x, z, 路面高度)
    static readonly Vector3[] PathPts =
    {
        new Vector3(44f, 0f, 123.3f),      // 院內溪流北岸
        new Vector3(49f, 0.05f, 123.3f),   // 東牆缺口
        new Vector3(60f, 0.45f, 124.5f),
        new Vector3(72f, 1.8f, 131f),
        new Vector3(90f, 6f, 138f),
        new Vector3(101f, 9f, 149f),
        new Vector3(88f, 12.5f, 160f),
        new Vector3(75f, 15.5f, 170f),
        new Vector3(85f, 19f, 183f),
        new Vector3(101f, 22f, 190f),
        new Vector3(114f, 24.5f, 197f),
        new Vector3(121f, 25f, 200f),      // 山頂平台中心
    };
    const float PathHalfWidth = 1.9f;
    const float RavineWallHeight = 7f;          // 低處步道兩側山壁高度
    const float RavineStreamWallHeight = 5.5f;  // 溪流 / 水潭外側山壁高度（矮一點，從院子還看得到瀑布）
    static readonly Vector3 Plateau = new Vector3(121f, 25f, 200f);
    const float PlateauRadius = 8f;

    // 牆外的溪流（水面高於地面，兩岸墊高）與水潭、瀑布
    static readonly Vector2[] StreamPts =
    {
        new Vector2(46.6f, 113.8f), new Vector2(48.4f, 114.0f), new Vector2(52f, 114.6f), new Vector2(58f, 115.5f), new Vector2(64f, 116.2f), new Vector2(68f, 116.5f),
    };
    const float StreamHalfWidth = 2.4f;
    const float OuterWaterY = 0.32f;
    static readonly Vector2 PoolCenter = new Vector2(70f, 116.5f);
    const float PoolRadius = 6.5f;
    const float CliffTop = 23f;
    static readonly Vector3 FallLip = new Vector3(80.5f, CliffTop - 0.3f, 116.5f);

    // 吊橋：平台東緣往 +X
    static Vector3 BridgeStart => Plateau + new Vector3(PlateauRadius - 0.5f, 0f, 0f);
    const float BridgeLength = 75f, BridgeSag = 3.5f, BridgeWidth = 2.6f;

    static System.Random rng;
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static int RI(int a, int b) => rng.Next(a, b);
    static bool Chance(float p) => rng.NextDouble() < p;

    static Material mWaterfall, mMist, mPlank, mRope, mWood, mRock, mStreamWater;
    static Terrain terrain;

    [MenuItem("GeminiGrowUp/道場：建立後山瀑布與山路 (Dojo → Forest Island)")]
    public static void Menu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    /// <summary>batchmode：-executeMethod DojoMountainPathBuilder.Build</summary>
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DojoScenePath) == null)
        {
            Debug.LogError($"[DojoMountain] 找不到 {DojoScenePath}");
            return;
        }
        rng = new System.Random(77);
        uvDensity.Clear();
        gardenMats = null;
        fallbackCache.Clear();
        foreach (var d in new[] { OutDir, MeshDir, MatDir }) EnsureFolder(d);

        var scene = EditorSceneManager.OpenScene(DojoScenePath, OpenSceneMode.Single);
        var old = GameObject.Find(InstanceName);
        if (old != null) Object.DestroyImmediate(old);
        RestoreEastWall();

        CreateMaterials();
        var root = new GameObject(InstanceName);
        BuildMoonGate(root.transform);
        // 山路從院內穿過月洞門出去
        PathPts[0].z = PathPts[1].z = moonGateCenterZ;
        terrain = BuildTerrain(root.transform);
        CutBakedEastWall();
        BuildEastWall(root.transform);
        BuildOuterStream(root.transform);
        BuildWaterfall(root.transform);
        BuildCliffRocks(root.transform);
        BuildStoneSteps(root.transform);
        BuildCurbs(root.transform);
        BuildPathDressing(root.transform);
        BuildTrailPlanting(root.transform);
        BuildVegetation(root.transform);
        BuildBridge(root.transform);
        BuildMist(root.transform);
        BuildOffTrailMist(root.transform);
        BuildKillZone(root.transform);
        BuildBoundary(root.transform);

        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        OpenEastGate();
        EnsureBuildSettings();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DojoMountain] 已在道場加入後山：{PrefabPath}。山路從東牆北端出口開始，吊橋走到中段會載入 ForestIsland。");
    }

    // =====================================================================
    //  地形
    // =====================================================================

    static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

    static float Fbm(float x, float z)
    {
        float v = 0f, amp = 0.5f, f = 1f;
        for (int i = 0; i < 4; i++) { v += amp * Mathf.PerlinNoise(x * f + 13.1f, z * f + 7.7f); f *= 2.03f; amp *= 0.5f; }
        return v / 0.9375f;
    }

    /// <summary>點到折線（XZ）的距離，並回傳最近點的路面高度。</summary>
    static float DistToPath(float x, float z, out float pathY)
    {
        float best = float.MaxValue; pathY = 0f;
        var p = new Vector2(x, z);
        for (int i = 0; i < PathPts.Length - 1; i++)
        {
            var a = new Vector2(PathPts[i].x, PathPts[i].z);
            var b = new Vector2(PathPts[i + 1].x, PathPts[i + 1].z);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float d = Vector2.Distance(p, a + ab * t);
            if (d < best) { best = d; pathY = Mathf.Lerp(PathPts[i].y, PathPts[i + 1].y, t); }
        }
        return best;
    }

    static float DistToStream(float x, float z)
    {
        float best = Vector2.Distance(new Vector2(x, z), PoolCenter) - PoolRadius + StreamHalfWidth;
        var p = new Vector2(x, z);
        for (int i = 0; i < StreamPts.Length - 1; i++)
        {
            var a = StreamPts[i]; var ab = StreamPts[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    /// <summary>世界座標的地面高度設計值。</summary>
    static float DesignHeight(float x, float z)
    {
        // 主山坡：越往北越高，靠近道場那一側壓到地面以下（被道場外的地面蓋住）
        float rise = Mathf.Clamp(0.27f * (z - 118f), 0f, 32f) * Smooth(54f, 70f, x);
        rise += Mathf.Max(0f, x - 110f) * 0.08f * Smooth(120f, 160f, z);
        float noise = (Fbm(x * 0.035f, z * 0.035f) - 0.5f) * 7f * Smooth(56f, 75f, x);
        float h = rise + noise;

        // 遠方的主峰（北方）
        h += 34f * Mathf.Exp(-((x - 170f) * (x - 170f) / (2f * 30f * 30f) + (z - 248f) * (z - 248f) / (2f * 22f * 22f)));
        h += 22f * Mathf.Exp(-((x - 210f) * (x - 210f) / (2f * 25f * 25f) + (z - 150f) * (z - 150f) / (2f * 30f * 30f)));

        // 瀑布所在的山肩：頂部平、朝西是陡崖
        float sd = Vector2.Distance(new Vector2(x, z), new Vector2(100f, 113f));
        float shoulder = CliffTop * Smooth(77.5f, 82f, x) * (1f - Smooth(16f, 26f, sd));
        h = Mathf.Max(h, shoulder + (Fbm(x * 0.2f, z * 0.2f) - 0.5f) * 1.2f * Smooth(82f, 86f, x));
        // 崖頂的小溪道
        if (x > 80f && x < 104f && Mathf.Abs(z - 116.5f) < 2.2f) h = Mathf.Min(h, CliffTop - 0.6f);

        // 吊橋下的山谷（平台東側）
        float gorge = Smooth(127f, 134f, x) * (1f - Smooth(26f, 36f, Mathf.Abs(z - 200f)));
        h = Mathf.Lerp(h, 0.8f, gorge * Smooth(0f, 5f, 220f - x));

        // 靠近道場一側：地面以下
        h *= Smooth(51f, 57f, x);
        h = Mathf.Max(h, -0.25f);

        // 溪流與水潭：兩岸墊高，水道底高於道場外的地面
        float ds = DistToStream(x, z);
        // 靠牆那段水面較低：河床壓到道場外地面以下，讓地面當河床（避免和地面重疊閃爍）
        float bed = x < 51f ? -0.1f : 0.08f;
        const float bank = 0.85f;
        if (ds < StreamHalfWidth - 0.4f) h = bed;                                       // 水道底
        else if (ds < StreamHalfWidth + 1.2f)                                            // 岸坡
            h = Mathf.Lerp(bed, Mathf.Max(h, bank), Smooth(StreamHalfWidth - 0.4f, StreamHalfWidth + 1.2f, ds));
        else if (ds < StreamHalfWidth + 3f)                                              // 岸頂往外漸平
            h = Mathf.Lerp(Mathf.Max(h, bank), h, Smooth(StreamHalfWidth + 1.2f, StreamHalfWidth + 3f, ds));

        // 山路與平台：壓平（挖或填）
        float dp = DistToPath(x, z, out float py);
        float pw = 1f - Smooth(PathHalfWidth, PathHalfWidth + 3.5f, dp);
        h = Mathf.Lerp(h, py, pw);
        float dpl = Vector2.Distance(new Vector2(x, z), new Vector2(Plateau.x, Plateau.z));
        h = Mathf.Lerp(h, Plateau.y - 0.04f, 1f - Smooth(PlateauRadius, PlateauRadius + 4f, dpl));

        // 低處的步道（路面低於約 11m）與溪流、水潭：兩側墊高成山壁，像走在山谷裡，玩家出不去
        float pathEdge = dp - PathHalfWidth;
        float streamEdge = DistToStream(x, z) - StreamHalfWidth;
        bool byStream = streamEdge < pathEdge;
        float edge = byStream ? streamEdge : pathEdge;
        float baseY = byStream ? OuterWaterY : py;
        float low = byStream ? 1f : 1f - Smooth(9f, 12.5f, py);
        if (low > 0f && edge > 0f)
        {
            float wallH = (byStream ? RavineStreamWallHeight : RavineWallHeight) * low;
            float wallRise = Smooth(1.2f, 3.6f, edge) * (1f - Smooth(13f, 22f, edge));
            float wn = (Fbm(x * 0.18f, z * 0.18f) - 0.5f) * 1.6f * wallRise;   // 山壁頂不要一條直線
            h = Mathf.Max(h, Mathf.Lerp(h, baseY + wallH + wn, wallRise));
        }
        return h;
    }

    static Terrain BuildTerrain(Transform parent)
    {
        string tdPath = OutDir + "/TD_DojoMountain.asset";
        AssetDatabase.DeleteAsset(tdPath);
        var td = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = 512 };
        td.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
        var h = new float[HeightRes, HeightRes];
        for (int iz = 0; iz < HeightRes; iz++)
            for (int ix = 0; ix < HeightRes; ix++)
            {
                float x = TerrainX0 + ix / (float)(HeightRes - 1) * TerrainSize;
                float z = TerrainZ0 + iz / (float)(HeightRes - 1) * TerrainSize;
                h[iz, ix] = Mathf.Clamp01((DesignHeight(x, z) - TerrainY) / TerrainHeight);
            }
        td.SetHeights(0, 0, h);

        td.terrainLayers = new[]
        {
            Layer("TL_DM_Grass", TexFromMat($"{Root}/Materials/FinalDojoArena/Garden/DenseLawn_Base.mat", "Viking Village/Textures/Terrain/terrain_grass_01_a"), 6f, new Color(0.85f, 0.95f, 0.8f)),
            Layer("TL_DM_Earth", TexFromMat($"{Root}/Materials/FinalDojoArena/Garden/Exterior_Earth.mat", "Viking Village/Textures/Terrain/terrain_wetmud_01_a"), 6f, Color.white),
            Layer("TL_DM_Path", TexFromMat($"{Root}/Materials/FinalDojoArena/Garden/Exterior_Earth.mat", "Viking Village/Textures/Terrain/terrain_wetmud_01_a"), 3f, new Color(0.8f, 0.72f, 0.62f)),
            Layer("TL_DM_Rock", TexFromMat($"{Root}/Materials/FinalDojoArena/Garden/M_FJG_Rock_001_dbf74aa0_URP_6715a30a.mat", "Viking Village/Textures/Terrain/terrain_mudslide_01_a"), 8f, new Color(0.85f, 0.85f, 0.85f)),
        };
        int res = td.alphamapResolution;
        var a = new float[res, res, 4];
        for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float nx = ix / (float)(res - 1), nz = iz / (float)(res - 1);
                float x = TerrainX0 + nx * TerrainSize, z = TerrainZ0 + nz * TerrainSize;
                float steep = td.GetSteepness(nx, nz);
                float dp = DistToPath(x, z, out _);
                float path = 1f - Smooth(PathHalfWidth - 0.4f, PathHalfWidth + 0.6f, dp);
                float dpl = Vector2.Distance(new Vector2(x, z), new Vector2(Plateau.x, Plateau.z));
                path = Mathf.Max(path, 1f - Smooth(PlateauRadius - 1f, PlateauRadius + 1f, dpl));
                float rock = Smooth(30f, 45f, steep);
                float earth = Smooth(0.45f, 0.7f, Fbm(x * 0.06f, z * 0.06f)) * 0.6f + (1f - Smooth(PathHalfWidth + 0.6f, PathHalfWidth + 2.5f, dp)) * 0.4f;
                float wRock = rock;
                float wPath = (1f - wRock) * path;
                float rest = 1f - wRock - wPath;
                a[iz, ix, 0] = rest * (1f - earth);
                a[iz, ix, 1] = rest * earth;
                a[iz, ix, 2] = wPath;
                a[iz, ix, 3] = wRock;
            }
        td.SetAlphamaps(0, 0, a);
        AssetDatabase.CreateAsset(td, tdPath);

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "MountainTerrain";
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(TerrainX0, TerrainY, TerrainZ0);
        var t = go.GetComponent<Terrain>();
        var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/ThirdParty/Viking Village/Materials/TerrainURP.mat");
        if (mat != null) t.materialTemplate = mat;
        t.heightmapPixelError = 4f;
        t.basemapDistance = 150f;
        t.drawInstanced = true;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
        return t;
    }

    static Texture2D TexFromMat(string matPath, string fallbackUnderThirdParty)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        Texture2D t = null;
        if (m != null)
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" })
                if (m.HasProperty(prop) && m.GetTexture(prop) is Texture2D tex) { t = tex; break; }
        if (t == null) t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/ThirdParty/{fallbackUnderThirdParty}.tif");
        return t;
    }

    static TerrainLayer Layer(string name, Texture2D tex, float tile, Color tint)
    {
        string path = $"{MatDir}/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
        layer.diffuseTexture = tex;
        layer.tileSize = new Vector2(tile, tile);
        layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
        layer.smoothness = 0.05f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static float GroundY(float x, float z)
    {
        float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        return Mathf.Max(y, -0.015f);   // 道場外的平地
    }

    // =====================================================================
    //  東牆：道場的牆外觀是 OuterWallsAndGate/BakedVisuals 裡按材質合併好的大 Mesh，
    //  沒辦法只關掉一段。這裡把合併 Mesh 裡「東牆那一條」移除（存成新的 Mesh，原檔不動），
    //  再用同樣材質、同樣貼圖密度重建分段的東牆，在月洞門與山路出口留空；碰撞也一起分段。
    // =====================================================================

    const string CutSuffix = "__EastCut";
    const float EastZ0 = 5f, EastZ1 = 126.2f;
    // 溪流在東牆處約 z 111.8 ~ 116.4：這段的牆下半部開拱形進水口
    const float RiverGapZ0 = 111.2f, RiverGapZ1 = 117f;
    const float WallTopY = 4.2f;              // 道場圍牆高度（含牆頂）
    // (材質名稱, 底 y, 頂 y, 厚度)：和原本三個方塊（WallStoneBase / WhitePlasterWall / DarkWallCoping）一致
    static readonly (string mat, float y0, float y1, float thick)[] WallBands =
    {
        ("M_Dojo_FoundationStone", 0f, 2f, 1.1f), ("M_Dojo_Plaster", 2f, 4f, 0.8f), ("M_Dojo_DarkJoinery", 4f, 4.2f, 1.2f),
    };
    static readonly string[] EastColliderNames = { "WallStoneBase", "WhitePlasterWall", "DarkWallCoping" };
    static readonly Dictionary<string, float> uvDensity = new Dictionary<string, float>();

    static Transform BakedRoot()
    {
        var walls = GameObject.Find("FinalDojoArena/Architecture/OuterWallsAndGate");
        return walls != null ? walls.transform.Find("BakedVisuals") : null;
    }

    static bool IsEastWallTri(Vector3 a, Vector3 b, Vector3 c)
    {
        float cz = (a.z + b.z + c.z) / 3f;
        return Mathf.Abs(a.x - WallX) < 0.8f && Mathf.Abs(b.x - WallX) < 0.8f && Mathf.Abs(c.x - WallX) < 0.8f && cz > 6f && cz < 125.5f;
    }

    /// <summary>還原上一次的修改（重跑生成器時先呼叫，讓結果不會累加）。</summary>
    static void RestoreEastWall()
    {
        var baked = BakedRoot();
        if (baked != null)
            foreach (var mf in baked.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.name.EndsWith(CutSuffix)) continue;
                string orig = mf.sharedMesh.name.Substring(0, mf.sharedMesh.name.Length - CutSuffix.Length);
                var guid = AssetDatabase.FindAssets($"{orig} t:Mesh").FirstOrDefault(g => !AssetDatabase.GUIDToAssetPath(g).Contains(CutSuffix)
                            && AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(g))?.name == orig);
                if (guid != null) mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
                else Debug.LogWarning($"[DojoMountain] 找不到原本的合併 Mesh「{orig}」，無法還原");
            }
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (EastColliderNames.Contains(t.name) && Mathf.Abs(t.position.x - WallX) < 0.6f)
                foreach (var c in t.GetComponents<Collider>()) c.enabled = true;
            if (t.name.StartsWith("KHS_WallBase") && Mathf.Abs(t.position.x - WallX) < 0.6f && t.position.z > 100f && !t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
        }
    }

    /// <summary>移除合併 Mesh 裡的東牆，記下各材質的貼圖密度（UV / 公尺）。</summary>
    static void CutBakedEastWall()
    {
        var baked = BakedRoot();
        if (baked == null) { Debug.LogWarning("[DojoMountain] 找不到 OuterWallsAndGate/BakedVisuals，東牆外觀沒有切開"); return; }
        foreach (var mf in baked.GetComponentsInChildren<MeshFilter>(true))
        {
            var src = mf.sharedMesh;
            if (src == null) continue;
            var r = mf.GetComponent<MeshRenderer>();
            var l2w = mf.transform.localToWorldMatrix;
            var verts = src.vertices;
            var world = verts.Select(v => l2w.MultiplyPoint3x4(v)).ToArray();
            var uv = src.uv;
            bool any = false;
            var newSubs = new List<int[]>();
            float uvLen = 0f, wLen = 0f;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var keep = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    if (IsEastWallTri(world[a], world[b], world[c]))
                    {
                        any = true;
                        if (uv != null && uv.Length == verts.Length)
                            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
                            { uvLen += Vector2.Distance(uv[p], uv[q]); wLen += Vector3.Distance(world[p], world[q]); }
                        continue;
                    }
                    keep.Add(a); keep.Add(b); keep.Add(c);
                }
                newSubs.Add(keep.ToArray());
            }
            if (!any) continue;
            if (r != null && r.sharedMaterial != null && wLen > 0f) uvDensity[r.sharedMaterial.name] = uvLen / wLen;

            var cut = Object.Instantiate(src);
            cut.name = src.name + CutSuffix;
            for (int s = 0; s < newSubs.Count; s++) cut.SetTriangles(newSubs[s], s);
            cut.RecalculateBounds();
            string path = $"{MeshDir}/{cut.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(cut, path);
            mf.sharedMesh = cut;
            EditorUtility.SetDirty(mf);
        }
        // 原本東牆的三個碰撞方塊關掉，改用重建牆段的碰撞
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (EastColliderNames.Contains(t.name) && Mathf.Abs(t.position.x - WallX) < 0.6f)
                foreach (var c in t.GetComponents<Collider>()) { c.enabled = false; EditorUtility.SetDirty(c); }
    }

    /// <summary>
    /// 重建東牆（放在後山 Prefab 裡）：全段等高（4.2m）的直牆。
    ///   溪流經過處：牆的下半部開一個拱形進水口，上方的牆連續不斷。
    ///   北端接月洞門（山路入口）：門已縮放到兩側翼牆和圍牆一樣高，牆段剛好頂到門邊。
    /// </summary>
    static void BuildEastWall(Transform parent)
    {
        var g = Group("EastWallRebuilt", parent);
        float gateSouth = moonGateHalfSpan > 0f ? moonGateCenterZ - moonGateHalfSpan + 0.1f : EastZ1;
        foreach (var band in WallBands)
        {
            var mat = AssetDatabase.FindAssets($"{band.mat} t:Material", new[] { Root + "/Materials/FinalDojoArena" })
                .Select(x => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(x))).FirstOrDefault(m => m != null && m.name == band.mat);
            float dens = uvDensity.TryGetValue(band.mat, out var d) && d > 0f ? d : 0.25f;

            void Straight(string label, float z0, float z1, float y0, float y1)
            {
                if (z1 - z0 < 0.05f || y1 - y0 < 0.01f) return;
                var size = new Vector3(band.thick, y1 - y0, z1 - z0);
                var center = new Vector3(WallX - 0.05f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
                var go = MeshObj(g, $"{band.mat} {label}", BoxMesh($"Mesh_DM_EastWall_{band.mat}_{label}", center, size, dens), mat);
                go.AddComponent<BoxCollider>();
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }

            Straight("South", EastZ0, RiverGapZ0, band.y0, band.y1);
            Straight("North", RiverGapZ1, gateSouth, band.y0, band.y1);
            // 進水口上方：拱頂以上的部分照常是牆
            if (band.y0 >= ArchCrownY) Straight("OverRiver", RiverGapZ0, RiverGapZ1, band.y0, band.y1);
            else if (band.y1 > ArchCrownY)
            {
                var arch = MeshObj(g, $"{band.mat} RiverArch", ArchLintelMesh($"Mesh_DM_EastWall_{band.mat}_RiverArch", RiverGapZ0, RiverGapZ1, band.y1, band.thick, dens), mat);
                arch.AddComponent<MeshCollider>().sharedMesh = arch.GetComponent<MeshFilter>().sharedMesh;
            }
        }

        // 進水口的木格柵（水門）：水看得過去、人過不去（翻滾也鑽不過）
        var grilleMat = AssetDatabase.FindAssets("M_Dojo_DarkJoinery t:Material", new[] { Root + "/Materials/FinalDojoArena" })
            .Select(x => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(x))).FirstOrDefault(m => m != null && m.name == "M_Dojo_DarkJoinery") ?? mWood;
        var grille = Group("RiverGrille", g);
        float mid = (RiverGapZ0 + RiverGapZ1) * 0.5f, half = (RiverGapZ1 - RiverGapZ0) * 0.5f;
        for (float z = RiverGapZ0 + 0.3f; z < RiverGapZ1 - 0.2f; z += 0.42f)
        {
            float u = (z - mid) / half;
            float top = ArchSpringY + (ArchCrownY - ArchSpringY) * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)) + 0.05f;
            float bottom = -0.9f;
            Prim(PrimitiveType.Cube, grille, "GrilleBar", new Vector3(WallX + 0.15f, (top + bottom) * 0.5f, z), Vector3.zero,
                new Vector3(0.09f, top - bottom, 0.09f), grilleMat);
        }
        Prim(PrimitiveType.Cube, grille, "GrilleRail", new Vector3(WallX + 0.15f, 0.62f, mid), Vector3.zero, new Vector3(0.08f, 0.08f, RiverGapZ1 - RiverGapZ0 - 0.2f), grilleMat);
        var block = new GameObject("RiverGrilleCollider");
        block.transform.SetParent(grille, false);
        block.transform.position = new Vector3(WallX, 0.5f, mid);
        block.AddComponent<BoxCollider>().size = new Vector3(1.2f, 3f, RiverGapZ1 - RiverGapZ0 + 0.2f);
    }

    const float ArchSpringY = 0.55f, ArchCrownY = 1.75f;   // 進水口：兩側直立到 0.55m，拱頂 1.75m（水面約 -0.2 ~ 0.3）

    /// <summary>進水口的拱：一段牆，底緣沿半橢圓往上拱，頂端到 topY。</summary>
    static Mesh ArchLintelMesh(string name, float z0, float z1, float topY, float thick, float dens)
    {
        const int N = 32;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float hx = thick * 0.5f, cx = WallX - 0.05f, mid = (z0 + z1) * 0.5f, half = (z1 - z0) * 0.5f;
        float Bottom(float z)
        {
            float u = Mathf.Clamp((z - mid) / half, -1f, 1f);
            return ArchSpringY + (ArchCrownY - ArchSpringY) * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 dd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 outward)
        {
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(dd);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f) t.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            else t.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
        }
        for (int i = 0; i < N; i++)
        {
            float za = Mathf.Lerp(z0, z1, i / (float)N), zb = Mathf.Lerp(z0, z1, (i + 1) / (float)N);
            float ya = Bottom(za), yb = Bottom(zb);
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = cx + sx * hx;
                Quad(new Vector3(x, ya, za), new Vector3(x, yb, zb), new Vector3(x, topY, zb), new Vector3(x, topY, za),
                     new Vector2(za, ya) * dens, new Vector2(zb, yb) * dens, new Vector2(zb, topY) * dens, new Vector2(za, topY) * dens,
                     new Vector3(sx, 0f, 0f));
            }
            // 拱的內面（朝下）
            Quad(new Vector3(cx - hx, ya, za), new Vector3(cx + hx, ya, za), new Vector3(cx + hx, yb, zb), new Vector3(cx - hx, yb, zb),
                 new Vector2(cx - hx, za) * dens, new Vector2(cx + hx, za) * dens, new Vector2(cx + hx, zb) * dens, new Vector2(cx - hx, zb) * dens,
                 Vector3.down);
            // 頂面
            Quad(new Vector3(cx - hx, topY, za), new Vector3(cx + hx, topY, za), new Vector3(cx + hx, topY, zb), new Vector3(cx - hx, topY, zb),
                 new Vector2(cx - hx, za) * dens, new Vector2(cx + hx, za) * dens, new Vector2(cx + hx, zb) * dens, new Vector2(cx - hx, zb) * dens,
                 Vector3.up);
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        string path = $"{MeshDir}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>世界座標的方塊 Mesh，UV 依世界尺寸 × 密度展開（和原本的牆貼圖大小一致）。</summary>
    static Mesh BoxMesh(string name, Vector3 c, Vector3 size, float dens)
    {
        var h = size * 0.5f;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        void Face(Vector3 o, Vector3 du, Vector3 dv, Vector2 uvO, Vector2 uvSize)
        {
            int b = v.Count;
            v.Add(o); v.Add(o + du); v.Add(o + du + dv); v.Add(o + dv);
            uv.Add(uvO); uv.Add(uvO + new Vector2(uvSize.x, 0)); uv.Add(uvO + uvSize); uv.Add(uvO + new Vector2(0, uvSize.y));
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        Vector3 min = c - h;
        float X = size.x, Y = size.y, Z = size.z;
        // ±X（牆面），UV = (z, y)
        Face(min + new Vector3(X, 0, 0), new Vector3(0, 0, Z), new Vector3(0, Y, 0), new Vector2(min.z, min.y) * dens, new Vector2(Z, Y) * dens);
        Face(min + new Vector3(0, 0, Z), new Vector3(0, 0, -Z), new Vector3(0, Y, 0), new Vector2(min.z + Z, min.y) * dens, new Vector2(-Z, Y) * dens);
        // ±Y（頂、底），UV = (z, x)
        Face(min + new Vector3(0, Y, 0), new Vector3(X, 0, 0), new Vector3(0, 0, Z), new Vector2(min.x, min.z) * dens, new Vector2(X, Z) * dens);
        Face(min + new Vector3(X, 0, 0), new Vector3(-X, 0, 0), new Vector3(0, 0, Z), new Vector2(min.x + X, min.z) * dens, new Vector2(-X, Z) * dens);
        // ±Z（兩端），UV = (x, y)
        Face(min, new Vector3(X, 0, 0), new Vector3(0, Y, 0), new Vector2(min.x, min.y) * dens, new Vector2(X, Y) * dens);
        Face(min + new Vector3(X, 0, Z), new Vector3(-X, 0, 0), new Vector3(0, Y, 0), new Vector2(min.x + X, min.y) * dens, new Vector2(-X, Y) * dens);
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        // 確保法線朝外（Face 的頂點順序依方向而定，反了就翻轉）
        var n = mesh.normals; bool flip = false;
        for (int i = 0; i < v.Count; i += 4) if (Vector3.Dot(n[i], v[i] - c) < 0f) { flip = true; break; }
        if (flip)
        {
            for (int i = 0; i < t.Count; i += 3) { (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]); }
            mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents();
        }
        string path = $"{MeshDir}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // =====================================================================
    //  月洞門（取代溪流穿過東牆的那幾段牆）
    // =====================================================================

    static void BuildMoonGate(Transform parent)
    {
        moonGateHalfSpan = 0f;
        if (!File.Exists(MoonGateFbx)) { Debug.LogWarning($"[DojoMountain] 找不到 {MoonGateFbx}，東牆維持原樣"); return; }
        var mi = (ModelImporter)AssetImporter.GetAtPath(MoonGateFbx);
        if (mi != null && (mi.importAnimation || mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.animationType != ModelImporterAnimationType.None))
        {
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.SaveAndReimport();
        }
        var mat = PbrMaterial($"{MoonGateDir}/M_MoonGate.mat", $"{MoonGateDir}/Textures/MoonGate", MoonGateTint, false, 0.12f);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(MoonGateFbx);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        go.name = "MoonGate (Path Entrance)";
        go.transform.SetParent(parent, false);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
        // Tripo 模型的正面是 +Z 或 -Z；門要橫跨在東牆上（門面朝 ±X），所以把較寬的那一軸轉到世界 Z
        var b = GetBounds(go);
        bool wideOnX = b.size.x >= b.size.z;
        go.transform.rotation = Quaternion.Euler(0f, wideOnX ? 90f : 0f, 0f);
        b = GetBounds(go);
        float k = MoonGateHeight / Mathf.Max(0.01f, b.size.y);
        go.transform.localScale = Vector3.one * k;
        LevelBase(go);
        // 放大到「門兩側翼牆的頂」和道場圍牆一樣高，牆和門才能平順相接
        float wing = WingHeight(go);
        if (wing > 0.5f)
        {
            float s = Mathf.Clamp(WallTopY / wing, 0.5f, 3f);
            go.transform.localScale *= s; k *= s;
        }
        b = GetBounds(go);
        // 太寬就縮小：門的南端不能蓋到進水口
        float maxWidth = EastZ1 - RiverGapZ1 - 0.3f;
        if (b.size.z > maxWidth) { go.transform.localScale *= maxWidth / b.size.z; k *= maxWidth / b.size.z; b = GetBounds(go); }
        moonGateHalfSpan = b.extents.z;
        moonGateCenterZ = EastZ1 - moonGateHalfSpan - 0.05f;
        // 底部貼地、門面對齊牆線、北端貼後牆轉角
        go.transform.position += new Vector3(WallX - b.center.x, -0.05f - b.min.y, moonGateCenterZ - b.center.z);
        b = GetBounds(go);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        Debug.Log($"[DojoMountain] 月洞門：寬 {b.size.z:0.0}m、高 {b.size.y:0.0}m、厚 {b.size.x:0.0}m（模型縮放 {k:0.###}）");
    }

    /// <summary>
    /// Tripo 模型常常有一點歪：取最底下一圈頂點，分別算左右（z）與前後（x）的高低差，轉回水平。
    /// </summary>
    static void LevelBase(GameObject go)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                    foreach (var v in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(v));
            if (pts.Count < 10) return;
            float minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y);
            var bottom = pts.Where(p => p.y < minY + (maxY - minY) * 0.06f).ToList();
            var c = new Vector3(bottom.Average(p => p.x), 0f, bottom.Average(p => p.z));
            float Slope(System.Func<Vector3, float> axis)
            {
                var lo = bottom.Where(p => axis(p) < axis(c)).ToList();
                var hi = bottom.Where(p => axis(p) >= axis(c)).ToList();
                if (lo.Count < 3 || hi.Count < 3) return 0f;
                float dy = hi.Average(p => p.y) - lo.Average(p => p.y);
                float dd = hi.Average(axis) - lo.Average(axis);
                return Mathf.Abs(dd) > 0.01f ? Mathf.Atan2(dy, dd) * Mathf.Rad2Deg : 0f;
            }
            float zTilt = Slope(p => p.z), xTilt = Slope(p => p.x);
            if (Mathf.Abs(zTilt) > 12f) zTilt = 0f;
            if (Mathf.Abs(xTilt) > 12f) xTilt = 0f;
            if (Mathf.Abs(zTilt) < 0.05f && Mathf.Abs(xTilt) < 0.05f) break;
            // 繞 X 軸轉正 θ 會把 +Z 那側壓低；繞 Z 軸轉正 φ 會把 +X 那側抬高
            go.transform.rotation = Quaternion.AngleAxis(zTilt, Vector3.right) * Quaternion.AngleAxis(-xTilt, Vector3.forward) * go.transform.rotation;
            if (pass == 0) Debug.Log($"[DojoMountain] 月洞門底部校正：左右 {zTilt:0.00}°、前後 {xTilt:0.00}°");
        }
    }

    /// <summary>門兩側最外緣（寬度的外側 7%）頂點的最高點 − 門底，也就是翼牆的高度。</summary>
    static float WingHeight(GameObject go)
    {
        var pts = new List<Vector3>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) foreach (var v in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(v));
        if (pts.Count == 0) return 0f;
        float minZ = pts.Min(p => p.z), maxZ = pts.Max(p => p.z), minY = pts.Min(p => p.y);
        float edge = (maxZ - minZ) * 0.07f;
        var left = pts.Where(p => p.z < minZ + edge).ToList();
        var right = pts.Where(p => p.z > maxZ - edge).ToList();
        if (left.Count == 0 || right.Count == 0) return 0f;
        float hl = left.Max(p => p.y) - minY, hr = right.Max(p => p.y) - minY;
        Debug.Log($"[DojoMountain] 月洞門翼牆高度：左 {hl:0.00}、右 {hr:0.00}（模型單位）");
        return (hl + hr) * 0.5f;
    }

    static Bounds GetBounds(GameObject go)
    {
        bool has = false; var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        return b;
    }

    /// <summary>Tripo 的 PBR 貼圖組（basecolor / normal / metallic / roughness）→ URP Lit 材質。</summary>
    /// <param name="useMetallicMap">Tripo 的金屬度貼圖常把屋瓦算成金屬，反射天空變藍；建築物通常關掉</param>
    static Material PbrMaterial(string matPath, string texPrefix, Color tint, bool useMetallicMap, float smoothness)
    {
        Texture2D Tex(string suffix, bool normal)
        {
            string p = $"{texPrefix}_{suffix}.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            if (ti == null) return null;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.maxTextureSize != 2048) { ti.textureType = type; ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        var baseTex = Tex("basecolor", false);
        var nrm = Tex("normal", true);
        string msPath = $"{texPrefix}_MetallicSmoothness.png";
        if (!File.Exists(msPath) && File.Exists($"{texPrefix}_metallic.png") && File.Exists($"{texPrefix}_roughness.png"))
        {
            var metal = new Texture2D(2, 2); metal.LoadImage(File.ReadAllBytes($"{texPrefix}_metallic.png"));
            var rough = new Texture2D(2, 2); rough.LoadImage(File.ReadAllBytes($"{texPrefix}_roughness.png"));
            const int S = 1024;
            var m = metal.GetPixels32(); var r = rough.GetPixels32();
            var o = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    byte mv = m[(y * metal.height / S) * metal.width + x * metal.width / S].r;
                    byte sv = (byte)(255 - r[(y * rough.height / S) * rough.width + x * rough.width / S].r);
                    o[y * S + x] = new Color32(mv, mv, mv, sv);
                }
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false); t.SetPixels32(o);
            File.WriteAllBytes(msPath, t.EncodeToPNG());
            Object.DestroyImmediate(t); Object.DestroyImmediate(metal); Object.DestroyImmediate(rough);
            AssetDatabase.ImportAsset(msPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(msPath);
            if (ti != null) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
        }
        var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(msPath);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
        if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", tint);
        if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
        if (useMetallicMap && ms != null) { mat.SetTexture("_MetallicGlossMap", ms); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", smoothness); }
        else
        {
            mat.SetTexture("_MetallicGlossMap", null);
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", smoothness);
        }
        mat.SetFloat("_EnvironmentReflections", 0f);   // 不反射天空（屋瓦不會再泛藍）
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // =====================================================================
    //  牆外溪流、水潭
    // =====================================================================

    /// <summary>
    /// 溪面高度：院內的溪是 -0.22；牆外的地面是平的（約 -0.015），水面要高過它才看得到，
    /// 所以在拱洞底下做一段往院內下降的急流斜面，牆外再回到 OuterWaterY。
    /// </summary>
    static float StreamSurfaceY(float x)
    {
        const float inner = DojoRiverWater.WaterY;   // -0.22
        if (x <= 46.9f) return inner;
        if (x <= 48f) return Mathf.Lerp(inner, 0.04f, Mathf.SmoothStep(0f, 1f, (x - 46.9f) / 1.1f));
        if (x <= 50.5f) return Mathf.Lerp(0.04f, OuterWaterY, Mathf.SmoothStep(0f, 1f, (x - 48f) / 2.5f));
        return OuterWaterY;
    }

    static void BuildOuterStream(Transform parent)
    {
        var g = Group("OuterStream", parent);
        // 溪面：沿折線的帶狀 Mesh
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        float v = 0f;
        var pts = new List<Vector2>();
        for (int i = 0; i < StreamPts.Length - 1; i++)
            for (int k = 0; k < 6; k++) pts.Add(Vector2.Lerp(StreamPts[i], StreamPts[i + 1], k / 6f));
        pts.Add(StreamPts[StreamPts.Length - 1]);
        for (int i = 0; i < pts.Count; i++)
        {
            var dir = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            var side = new Vector2(-dir.y, dir.x) * (StreamHalfWidth + 0.15f);
            if (i > 0) v += Vector2.Distance(pts[i], pts[i - 1]) / 6f;
            float wy = StreamSurfaceY(pts[i].x);
            verts.Add(new Vector3(pts[i].x - side.x, wy, pts[i].y - side.y));
            verts.Add(new Vector3(pts[i].x + side.x, wy, pts[i].y + side.y));
            // Dojo Garden Stream shader：uv.x = 順流方向，uv.y = 橫跨溪寬（0 和 1 是岸邊，會淡出）
            uvs.Add(new Vector2(-v, 0f)); uvs.Add(new Vector2(-v, 1f));
            if (i > 0)
            {
                int b = (i - 1) * 2;
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
        }
        var strip = SaveMesh("Mesh_DM_OuterStream", verts, uvs, tris);
        var s = MeshObj(g, "StreamSurface", strip, mStreamWater);
        s.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // 水潭：圓盤
        verts = new List<Vector3> { new Vector3(PoolCenter.x, OuterWaterY, PoolCenter.y) };
        uvs = new List<Vector2> { new Vector2(0f, 0.5f) };   // 中心 uv.y=0.5（不淡出），邊緣 uv.y=0（淡出）
        tris = new List<int>();
        const int seg = 36;
        for (int i = 0; i <= seg; i++)
        {
            float ang = i * Mathf.PI * 2f / seg;
            float rr = PoolRadius + 0.3f + Mathf.Sin(ang * 3f) * 0.4f;
            verts.Add(new Vector3(PoolCenter.x + Mathf.Cos(ang) * rr, OuterWaterY, PoolCenter.y + Mathf.Sin(ang) * rr));
            uvs.Add(new Vector2(ang * 1.5f, 0f));
            if (i > 0) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
        }
        var pool = SaveMesh("Mesh_DM_Pool", verts, uvs, tris);
        var p = MeshObj(g, "PoolSurface", pool, mStreamWater);
        p.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // 岸邊石頭
        var rock = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_01");
        var rock2 = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_02");
        var rock3 = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_03");
        var rocks = new[] { rock, rock2, rock3 }.Where(r => r != null).ToArray();
        if (rocks.Length == 0) return;
        for (int i = 0; i < 26; i++)
        {
            float t = R(0f, 1f);
            Vector2 c; Vector2 n;
            if (i < 16)
            {
                int k = Mathf.Min(StreamPts.Length - 2, (int)(t * (StreamPts.Length - 1)));
                float u = t * (StreamPts.Length - 1) - k;
                c = Vector2.Lerp(StreamPts[k], StreamPts[k + 1], u);
                var d = (StreamPts[k + 1] - StreamPts[k]).normalized;
                n = new Vector2(-d.y, d.x) * (Chance(0.5f) ? 1f : -1f);
                c += n * (StreamHalfWidth + R(0.1f, 0.9f));
                if (c.x < WallX + 1.5f) continue;
            }
            else
            {
                float ang = R(-150f, 150f) * Mathf.Deg2Rad;   // 水潭周圍（西側出水口留空）
                ang += Mathf.PI;
                if (Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, 180f)) < 20f) continue;
                c = PoolCenter + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (PoolRadius + R(0.2f, 1.2f));
            }
            var go = Place(g, rocks[RI(0, rocks.Length)], "BankRock", new Vector3(c.x, GroundY(c.x, c.y) - 0.15f, c.y),
                Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.6f, 1.3f));
        }
    }

    // =====================================================================
    //  瀑布
    // =====================================================================

    static void BuildWaterfall(Transform parent)
    {
        var g = Group("Waterfall", parent);
        // 崖頂小溪：從山肩流到瀑布口
        var top = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(top.GetComponent<Collider>());
        top.name = "UpperStream";
        top.transform.SetParent(g, false);
        top.transform.position = new Vector3(92f, CliffTop - 0.25f, 116.5f);
        top.transform.rotation = Quaternion.Euler(90f, -90f, 0f);
        top.transform.localScale = new Vector3(24f, 3.6f, 1f);
        top.GetComponent<MeshRenderer>().sharedMaterial = mStreamWater;

        // 水簾：從瀑布口往前拋出再垂直落下的曲面（三層，前後錯開增加厚度）
        for (int layer = 0; layer < 3; layer++)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            const int rows = 16;
            float width = 4.2f - layer * 0.5f;
            float outward = 2.2f + layer * 0.35f;
            for (int i = 0; i <= rows; i++)
            {
                float t = i / (float)rows;
                float y = Mathf.Lerp(FallLip.y, OuterWaterY - 0.1f, t);
                float x = FallLip.x - outward * Mathf.Sqrt(t) - layer * 0.25f;   // 拋物線：先往外再落下
                float w = width * (1f + t * 0.35f);
                verts.Add(new Vector3(x, y, FallLip.z - w * 0.5f));
                verts.Add(new Vector3(x, y, FallLip.z + w * 0.5f));
                uvs.Add(new Vector2(0f, t * 3f)); uvs.Add(new Vector2(1f, t * 3f));
                if (i > 0) { int b = (i - 1) * 2; tris.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            }
            var mesh = SaveMesh($"Mesh_DM_Waterfall_{layer}", verts, uvs, tris);
            var go = MeshObj(g, $"FallSheet_{layer}", mesh, mWaterfall);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var sc = go.AddComponent<UVScroll>();
            sc.speed = new Vector2(0.02f * (layer - 1), -1.6f - layer * 0.35f);
        }

        // 落水處的水花與水霧
        var pos = new Vector3(FallLip.x - 2.6f, OuterWaterY + 0.2f, FallLip.z);
        var splash = NewPS("FallSplash", g, pos);
        {
            var m = splash.main;
            m.loop = true; m.prewarm = true; m.duration = 2f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            m.startColor = new Color(0.92f, 0.96f, 1f, 0.55f);
            m.gravityModifier = 0.6f;
            m.maxParticles = 120;
            var e = splash.emission; e.rateOverTime = 60f;
            var sh = splash.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 40f; sh.radius = 1.6f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = splash.colorOverLifetime; col.enabled = true; col.color = Fade();
            var sol = splash.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
            SetPR(splash, mMist);
        }
        var mist = NewPS("FallMist", g, pos + Vector3.up * 1.5f);
        {
            var m = mist.main;
            m.loop = true; m.prewarm = true; m.duration = 4f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            m.startColor = new Color(0.9f, 0.94f, 0.97f, 0.22f);
            m.maxParticles = 50;
            var e = mist.emission; e.rateOverTime = 10f;
            var sh = mist.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2.5f;
            var vel = mist.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.8f, -0.2f); vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.6f); vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var col = mist.colorOverLifetime; col.enabled = true; col.color = Fade();
            SetPR(mist, mMist);
        }
        // 水聲：沿用道場溪流的音效設定方式（3D，靠近才聽得到）
        var src = new GameObject("WaterfallAudio").AddComponent<AudioSource>();
        src.transform.SetParent(g, false);
        src.transform.position = pos + Vector3.up * 3f;
        src.clip = Resources.Load<AudioClip>("Audio/SFX/river_loop");
        src.loop = true; src.playOnAwake = src.clip != null; src.spatialBlend = 1f; src.minDistance = 6f; src.maxDistance = 60f; src.volume = 0.5f;
        if (src.clip == null) Object.DestroyImmediate(src.gameObject);
    }

    static void BuildCliffRocks(Transform parent)
    {
        // 瀑布兩側的崖面：石柱貼著陡坡，蓋住地形被拉長的貼圖
        var parts = new List<CombineInstance>();
        for (float z = 103f; z <= 130f; z += R(2.6f, 3.6f))
        {
            if (Mathf.Abs(z - FallLip.z) < 3.2f) continue;  // 水簾正後方留給瀑布
            float x = 79.5f + R(-0.6f, 0.8f);
            float topY = GroundY(x + 2f, z) - R(0.3f, 1.5f);
            if (topY < 3f) continue;
            float rad = R(2f, 3.4f);
            parts.Add(new CombineInstance
            {
                mesh = ForestIslandBuilder.RockColumnMesh(null, -1f, topY, rad, R(0.6f, 0.85f), RI(0, 99999), 0.35f),
                transform = Matrix4x4.Translate(new Vector3(x, 0f, z))
            });
        }
        // 水簾後方的凹壁
        parts.Add(new CombineInstance
        {
            mesh = ForestIslandBuilder.RockColumnMesh(null, -1f, CliffTop - 0.8f, 2.8f, 0.9f, 4242, 0.3f),
            transform = Matrix4x4.Translate(new Vector3(FallLip.x + 2.2f, 0f, FallLip.z))
        });
        if (parts.Count == 0) return;
        var mesh = ForestIslandBuilder.CombineAndSave("Mesh_DM_CliffColumns", parts, MeshDir);
        var go = MeshObj(parent, "WaterfallCliffs", mesh, mRock);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // =====================================================================
    //  山路的佈置：鳥居、石燈籠、扶手、路邊石
    // =====================================================================

    // =====================================================================
    //  登山步道：石板階梯、路緣石、夾道的蕨類與樹（參考台灣郊山的石階步道）
    // =====================================================================

    /// <summary>沿著山路中心線，每隔 step 公尺取一個點（位置含路面高度、前進方向）。</summary>
    static List<(Vector3 pos, Vector3 dir)> PathSamples(float step, float startAt = 0f)
    {
        var list = new List<(Vector3, Vector3)>();
        float carry = startAt;
        for (int i = 0; i < PathPts.Length - 1; i++)
        {
            var a = PathPts[i]; var b = PathPts[i + 1];
            var flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float len = flat.magnitude;
            if (len < 0.01f) continue;
            var dir = flat / len;
            for (float s = carry; s < len; s += step)
            {
                float t = s / len;
                list.Add((new Vector3(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.z, b.z, t)), dir));
                carry = s + step - len;
            }
            if (carry < 0f) carry = 0f;
        }
        return list;
    }

    const float TrailStartX = WallX + 1.2f;   // 石階從牆外開始（院內維持原本的地面）

    static void BuildStoneSteps(Transform parent)
    {
        var g = Group("StoneSteps", parent);
        var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/FinalDojoArena/Garden/SteppingStone_Grey.mat") ?? mRock;
        // 內建 Cube 的複本（CombineAndSave 合併後會刪掉非資產的 Mesh，不能直接用內建資源）
        var cube = Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
        var parts = new List<CombineInstance>();
        foreach (var (pos, dir) in PathSamples(0.95f))
        {
            if (pos.x < TrailStartX) continue;
            if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(Plateau.x, Plateau.z)) < PlateauRadius - 1f) continue;
            float width = PathHalfWidth * 2f - R(0.35f, 0.7f);
            float depth = R(0.62f, 0.78f);
            const float thick = 0.28f;
            // 水平石板：上坡處前後兩塊有高低差，就是一級級的台階
            var top = pos.y + 0.1f;
            var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(R(-1.5f, 1.5f), R(-4f, 4f), R(-1.5f, 1.5f));
            var side = Vector3.Cross(Vector3.up, dir);
            var c = new Vector3(pos.x, top - thick * 0.5f, pos.z) + side * R(-0.15f, 0.15f);
            parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(c, rot, new Vector3(width, thick, depth)) });
            // 偶爾多一塊小石補在邊上（石板不一定整齊）
            if (Chance(0.25f))
            {
                float sgn = Chance(0.5f) ? 1f : -1f;
                var c2 = c + side * sgn * (width * 0.5f + 0.2f) + dir * R(-0.2f, 0.2f);
                parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(c2 + Vector3.down * 0.04f, rot * Quaternion.Euler(0f, R(-15f, 15f), 0f), new Vector3(R(0.35f, 0.55f), thick, R(0.4f, 0.6f))) });
            }
        }
        var mesh = ForestIslandBuilder.CombineAndSave("Mesh_DM_StoneSteps", parts, MeshDir);
        var go = MeshObj(g, "StoneSteps", mesh, mat);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static void BuildCurbs(Transform parent)
    {
        var g = Group("CurbStones", parent);
        var rocks = new[]
        {
            Load("Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_01"),
            Load("Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_02"),
            Load("Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_03"),
        }.Where(r => r != null).ToArray();
        if (rocks.Length == 0) return;
        var sizes = rocks.ToDictionary(r => r, r => { var tmp = (GameObject)PrefabUtility.InstantiatePrefab(r); var b = GetBounds(tmp); Object.DestroyImmediate(tmp); return Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)); });
        foreach (var (pos, dir) in PathSamples(0.85f, 0.4f))
        {
            if (pos.x < TrailStartX + 1f) continue;
            float dpl = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(Plateau.x, Plateau.z));
            if (dpl < PlateauRadius + 1f) continue;
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (float sgn in new[] { -1f, 1f })
            {
                var p = pos + side * sgn * (PathHalfWidth + R(0.05f, 0.3f)) + dir * R(-0.15f, 0.15f);
                // 彎道內側和其他段的路重疊時不放
                if (DistToPath(p.x, p.z, out _) < PathHalfWidth - 0.2f) continue;
                if (DistToStream(p.x, p.z) < StreamHalfWidth + 0.5f) continue;
                var pf = rocks[RI(0, rocks.Length)];
                float want = R(0.55f, 0.9f);
                var go = Place(g, pf, "Curb", new Vector3(p.x, GroundY(p.x, p.z) - want * 0.25f, p.z),
                    Quaternion.LookRotation(dir) * Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)), want / sizes[pf]);
            }
        }
    }

    /// <summary>素木鳥居（兩根木柱、笠木與貫），取代原本紅色的鳥居。</summary>
    static void BuildWoodenTorii(Transform parent, Vector3 pos, Vector3 dir)
    {
        var g = Group("WoodenTorii", parent);
        g.position = pos;
        g.rotation = Quaternion.LookRotation(dir);
        var wood = mPlank ?? mWood;
        float half = PathHalfWidth + 0.55f, h = 4.4f;
        foreach (float sx in new[] { -1f, 1f })
        {
            var post = Prim(PrimitiveType.Cylinder, g, "Hashira", Vector3.zero, Vector3.zero, new Vector3(0.38f, h * 0.5f, 0.38f), wood);
            post.transform.localPosition = new Vector3(sx * half, h * 0.5f - 0.2f, 0f);
            post.transform.localRotation = Quaternion.Euler(0f, 0f, sx * 1.5f);   // 柱子微微內傾
            post.AddComponent<CapsuleCollider>();
        }
        var kasagi = Prim(PrimitiveType.Cube, g, "Kasagi", Vector3.zero, Vector3.zero, new Vector3(half * 2f + 1.8f, 0.32f, 0.42f), wood);
        kasagi.transform.localPosition = new Vector3(0f, h - 0.05f, 0f);
        kasagi.transform.localRotation = Quaternion.identity;
        var shimaki = Prim(PrimitiveType.Cube, g, "Shimaki", Vector3.zero, Vector3.zero, new Vector3(half * 2f + 1.4f, 0.18f, 0.34f), wood);
        shimaki.transform.localPosition = new Vector3(0f, h - 0.3f, 0f);
        shimaki.transform.localRotation = Quaternion.identity;
        var nuki = Prim(PrimitiveType.Cube, g, "Nuki", Vector3.zero, Vector3.zero, new Vector3(half * 2f + 0.9f, 0.22f, 0.2f), wood);
        nuki.transform.localPosition = new Vector3(0f, h - 1.05f, 0f);
        nuki.transform.localRotation = Quaternion.identity;
    }

    /// <summary>步道兩側：路緣石外是蕨類與黃楊，再外面一路夾道的樹（樹冠會蓋到路上）。</summary>
    static void BuildTrailPlanting(Transform parent)
    {
        var g = Group("TrailPlanting", parent);
        var fern = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Plants/PaintedFern/Plant_PaintedFern_Spring_01");
        var boxwood = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Plants/Boxwood/Plant_Boxwood_Spring_01");
        var pineMid = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/BlackPine/Tree_BlackPine_Mid_Green_01");
        var pineBig = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/BlackPine/Tree_BlackPine_Big_Green_01");
        var maple = Load("Waldemarst/JapaneseGardenPackage/Prefabs/JapaneseMapleTree_A");
        var mapleC = Load("Waldemarst/JapaneseGardenPackage/Prefabs/JapaneseMapleTree_C");
        var bamboo = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/Bamboo/Tree_Bamboo_Mid_Green_01");

        bool OkSpot(Vector3 p, float minPathDist)
        {
            if (p.x < WallX + 2f) return false;
            if (DistToPath(p.x, p.z, out _) < minPathDist) return false;
            if (DistToStream(p.x, p.z) < StreamHalfWidth + 1.5f) return false;
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Plateau.x, Plateau.z)) < PlateauRadius + 1.5f) return false;
            if (Mathf.Abs(p.z - moonGateCenterZ) < 4f && p.x < WallX + 6f) return false;   // 月洞門前留空
            return true;
        }

        // 蕨類、灌木：緊貼路緣石外側
        foreach (var (pos, dir) in PathSamples(1.3f, 0.6f))
        {
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (float sgn in new[] { -1f, 1f })
            {
                if (Chance(0.25f)) continue;
                var p = pos + side * sgn * (PathHalfWidth + R(0.9f, 1.8f)) + dir * R(-0.4f, 0.4f);
                if (!OkSpot(p, PathHalfWidth + 0.7f)) continue;
                var pf = Chance(0.65f) ? fern : boxwood;
                if (pf == null) continue;
                var go = Place(g, pf, "TrailUnderstory", new Vector3(p.x, GroundY(p.x, p.z) - 0.05f, p.z), Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.8f, 1.3f));
                foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            }
        }

        // 夾道的樹：兩側交錯，距路中心 3.4~5.5m，低處竹林、中段楓與松、高處黑松
        int n = 0;
        foreach (var (pos, dir) in PathSamples(4.2f, 1.5f))
        {
            if (pos.x < WallX + 3f) continue;
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (float sgn in new[] { -1f, 1f })
            {
                if (Chance(0.2f)) continue;
                var p = pos + side * sgn * R(3.4f, 5.5f) + dir * R(-1.2f, 1.2f);
                if (!OkSpot(p, PathHalfWidth + 1.3f)) continue;
                GameObject pf;
                if (pos.y < 2.5f) pf = bamboo;
                else if (pos.y < 16f) pf = Chance(0.5f) ? (Chance(0.5f) ? maple : mapleC) : pineMid;
                else pf = Chance(0.6f) ? pineMid : pineBig;
                if (pf == null) pf = pineMid;
                if (pf == null) continue;
                // 往路的方向稍微傾斜，樹冠罩到步道上
                var lean = Quaternion.AngleAxis(R(3f, 7f), Vector3.Cross(Vector3.up, -side * sgn));
                Place(g, pf, "TrailTree", new Vector3(p.x, GroundY(p.x, p.z) - 0.1f, p.z), lean * Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.25f));
                n++;
            }
        }
        Debug.Log($"[DojoMountain] 步道夾道樹 {n} 棵");
    }

    static void BuildPathDressing(Transform parent)
    {
        var g = Group("PathDressing", parent);
        var lanternA = Load("JTS_StoneLanterns/Prefabs/Lantern_Stone_A1");
        var lanternC = Load("JTS_StoneLanterns/Prefabs/Lantern_Stone_C2");

        // 鳥居：出牆後第一段路的入口
        {
            var a = PathPts[2]; var b = PathPts[3];
            var p = Vector3.Lerp(a, b, 0.15f);
            var dir = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
            BuildWoodenTorii(g, new Vector3(p.x, GroundY(p.x, p.z) - 0.05f, p.z), dir);
        }
        // 石燈籠：牆口兩側、每個彎道外側、平台入口
        var lanternSpots = new List<(Vector3 p, Vector3 face)>();
        void Pair(int i, float t)
        {
            var a = PathPts[i]; var b = PathPts[i + 1];
            var p = Vector3.Lerp(a, b, t);
            var dir = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
            var side = Vector3.Cross(Vector3.up, dir);
            lanternSpots.Add((p + side * (PathHalfWidth + 0.9f), -side));
            lanternSpots.Add((p - side * (PathHalfWidth + 0.9f), side));
        }
        Pair(1, 0.25f); Pair(4, 0.9f); Pair(6, 0.1f); Pair(8, 0.9f); Pair(9, 0.85f);
        int li = 0;
        foreach (var s in lanternSpots)
        {
            var pf = li++ % 2 == 0 ? lanternA : lanternC;
            if (pf == null) continue;
            var go = Place(g, pf, "PathLantern", new Vector3(s.p.x, GroundY(s.p.x, s.p.z) - 0.05f, s.p.z), Quaternion.LookRotation(s.face), 1f);
            FitHeight(go, 1.9f);
        }

        // 彎道外側的木扶手（陡坡那一側）
        for (int i = 3; i < PathPts.Length - 2; i++)
        {
            var a = PathPts[i]; var b = PathPts[i + 1];
            var dir = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float len = dir.magnitude; dir /= len;
            var side = Vector3.Cross(Vector3.up, dir);
            // 外側 = 地勢較低那邊
            float lo = GroundY(((a + b) * 0.5f + side * 5f).x, ((a + b) * 0.5f + side * 5f).z);
            float hi = GroundY(((a + b) * 0.5f - side * 5f).x, ((a + b) * 0.5f - side * 5f).z);
            var outSide = lo < hi ? side : -side;
            Vector3 prev = Vector3.zero;
            int posts = Mathf.Max(2, Mathf.RoundToInt(len / 3f));
            for (int k = 0; k <= posts; k++)
            {
                float t = Mathf.Lerp(0.12f, 0.88f, k / (float)posts);
                var p = Vector3.Lerp(a, b, t) + outSide * (PathHalfWidth + 0.35f);
                p.y = GroundY(p.x, p.z);
                Cyl(g, "RailPost", p + Vector3.up * 0.55f, new Vector3(0.12f, 0.55f, 0.12f), mWood);
                if (k > 0) Segment(g, "RailBar", prev + Vector3.up * 1f, p + Vector3.up * 1f, 0.05f, mWood);
                prev = p;
            }
            // 扶手的碰撞（隱形）
            var mid = (a + b) * 0.5f + outSide * (PathHalfWidth + 0.4f);
            var wall = new GameObject("RailCollider");
            wall.transform.SetParent(g, false);
            wall.transform.position = new Vector3(mid.x, (a.y + b.y) * 0.5f + 1f, mid.z);
            wall.transform.rotation = Quaternion.LookRotation(new Vector3(b.x - a.x, b.y - a.y, b.z - a.z));
            wall.AddComponent<BoxCollider>().size = new Vector3(0.3f, 2.2f, len * 0.8f);
        }
    }

    // =====================================================================
    //  植被
    // =====================================================================

    static void BuildVegetation(Transform parent)
    {
        var g = Group("Vegetation", parent);
        var pineBig = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/BlackPine/Tree_BlackPine_Big_Green_01");
        var pineMid = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/BlackPine/Tree_BlackPine_Mid_Green_01");
        var sakura = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/Sakura/Tree_Sakura_Mid_Spring_01");
        var bambooBig = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/Bamboo/Tree_Bamboo_Big_Green_01");
        var bambooMid = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Trees/Bamboo/Tree_Bamboo_Mid_Green_01");
        var maple = Load("Waldemarst/JapaneseGardenPackage/Prefabs/JapaneseMapleTree_A");
        var rock = Load("Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_03");

        bool Free(float x, float z, float pathPad)
        {
            if (x < WallX + 6f) return false;
            if (DistToPath(x, z, out _) < PathHalfWidth + pathPad) return false;
            if (DistToStream(x, z) < StreamHalfWidth + 2f) return false;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(Plateau.x, Plateau.z)) < PlateauRadius + 2f) return false;
            if (x > 126f && Mathf.Abs(z - 200f) < 30f) return false;              // 吊橋下的山谷
            if (Mathf.Abs(z - FallLip.z) < 5f && x > 74f && x < 100f) return false; // 瀑布口
            float nx = (x - TerrainX0) / TerrainSize, nz = (z - TerrainZ0) / TerrainSize;
            if (nx < 0.02f || nx > 0.98f || nz < 0.02f || nz > 0.98f) return false;
            return terrain.terrainData.GetSteepness(nx, nz) < 32f;
        }
        var placed = new List<Vector2>();
        void Scatter(GameObject pf, string name, int count, Rect area, float spacing, float pathPad, Vector2 scale)
        {
            if (pf == null) return;
            int n = 0, tries = 0;
            while (n < count && tries++ < count * 40)
            {
                float x = R(area.xMin, area.xMax), z = R(area.yMin, area.yMax);
                if (!Free(x, z, pathPad)) continue;
                var p2 = new Vector2(x, z);
                if (placed.Any(q => (q - p2).sqrMagnitude < spacing * spacing)) continue;
                placed.Add(p2);
                Place(g, pf, name, new Vector3(x, GroundY(x, z) - 0.1f, z), Quaternion.Euler(0f, R(0f, 360f), 0f), R(scale.x, scale.y));
                n++;
            }
        }
        // 竹林：牆口外、溪流北邊
        Scatter(bambooBig, "Bamboo", 10, new Rect(54f, 126f, 16f, 14f), 2.2f, 1.2f, new Vector2(0.9f, 1.2f));
        Scatter(bambooMid, "Bamboo", 8, new Rect(52f, 126f, 18f, 16f), 1.8f, 1.2f, new Vector2(0.9f, 1.1f));
        // 櫻花：水潭邊
        Scatter(sakura, "Sakura", 4, new Rect(60f, 104f, 16f, 26f), 6f, 2f, new Vector2(0.9f, 1.1f));
        // 楓樹：山路中段
        Scatter(maple, "Maple", 6, new Rect(70f, 140f, 40f, 40f), 7f, 2.5f, new Vector2(0.8f, 1.1f));
        // 黑松：整座山
        Scatter(pineBig, "BlackPine", 34, new Rect(64f, 128f, 150f, 125f), 8f, 3f, new Vector2(0.9f, 1.4f));
        Scatter(pineMid, "BlackPine", 26, new Rect(64f, 96f, 150f, 155f), 6f, 2.5f, new Vector2(0.9f, 1.3f));
        // 山肩上幾棵松（從院子看得到的瀑布頂）
        Scatter(pineMid, "CliffPine", 5, new Rect(84f, 100f, 18f, 26f), 5f, 1f, new Vector2(1f, 1.3f));
        // 路邊散石（離路遠一點，路緣石另外放）
        Scatter(rock, "SlopeRock", 18, new Rect(56f, 120f, 70f, 85f), 4f, 4f, new Vector2(0.6f, 1.4f));
    }

    // =====================================================================
    //  吊橋（往 ForestIsland）
    // =====================================================================

    static Vector3 BridgePoint(float s, float side, float lift)
    {
        float t = Mathf.Clamp01(s / BridgeLength);
        var p = BridgeStart + new Vector3(s, -BridgeSag * 4f * t * (1f - t) + lift, side);
        return p;
    }

    static void BuildBridge(Transform parent)
    {
        var g = Group("BridgeToForestIsland", parent);
        var cols = Group("WalkColliders", g);
        float step = 0.42f;
        int n = Mathf.CeilToInt(BridgeLength / step);
        for (int i = 0; i < n; i++)
        {
            if (i > 10 && Chance(0.03f)) continue;
            var p = BridgePoint(i * step, 0f, 0f);
            var box = Prim(PrimitiveType.Cube, g, "Plank", p, new Vector3(R(-2f, 2f), 90f + R(-3f, 3f), R(-1.5f, 1.5f)),
                new Vector3(BridgeWidth + R(-0.15f, 0.15f), 0.07f, 0.32f), mPlank);
        }
        float half = BridgeWidth * 0.5f + 0.1f;
        foreach (float side in new[] { -half, half })
        {
            for (float s = 0f; s < BridgeLength - 0.01f; s += 1.5f)
            {
                Segment(g, "HandRope", BridgePoint(s, side, 1.15f), BridgePoint(Mathf.Min(s + 1.5f, BridgeLength), side, 1.15f), 0.035f, mRope);
                Segment(g, "LowRope", BridgePoint(s, side, 0.05f), BridgePoint(Mathf.Min(s + 1.5f, BridgeLength), side, 0.05f), 0.03f, mRope);
            }
            for (float s = 0f; s <= BridgeLength; s += 1.8f)
                Segment(g, "Hanger", BridgePoint(s, side, 0.05f), BridgePoint(s, side, 1.15f), 0.018f, mRope);
            // 橋頭木柱
            var post = BridgePoint(-0.5f, side * 1.15f, 0f);
            Cyl(g, "BridgePost", post + Vector3.up * 1.6f, new Vector3(0.36f, 2.2f, 0.36f), mWood);
        }
        for (float s = -1.5f; s < BridgeLength; s += 4f)
        {
            var a = BridgePoint(Mathf.Max(s, 0f), 0f, 0f);
            var b = BridgePoint(Mathf.Min(s + 4.2f, BridgeLength), 0f, 0f);
            if (s < 0f) a.x -= 1.5f;
            var mid = (a + b) * 0.5f; var dir = b - a;
            var c = new GameObject("Deck");
            c.transform.SetParent(cols, false);
            c.transform.SetPositionAndRotation(mid + Vector3.down * 0.05f, Quaternion.LookRotation(dir.normalized));
            c.AddComponent<BoxCollider>().size = new Vector3(BridgeWidth, 0.12f, dir.magnitude + 0.1f);
            foreach (float side in new[] { -half, half })
            {
                var w = new GameObject("RailWall");
                w.transform.SetParent(cols, false);
                w.transform.SetPositionAndRotation(mid + new Vector3(0f, 1f, side), c.transform.rotation);
                w.AddComponent<BoxCollider>().size = new Vector3(0.2f, 2.2f, dir.magnitude + 0.1f);
            }
        }
        // 走到橋中段 → 載入 ForestIsland（玩家會出現在孤島那一側的吊橋上）
        var trig = new GameObject("ToForestIslandTrigger");
        trig.transform.SetParent(g, false);
        trig.transform.position = BridgePoint(BridgeLength * 0.6f, 0f, 1.2f);
        var tc = trig.AddComponent<BoxCollider>();
        tc.isTrigger = true;
        tc.size = new Vector3(3f, 2.6f, BridgeWidth + 0.4f);
        var st = trig.AddComponent<SceneTransitionTrigger>();
        st.targetScene = "ForestIsland";
        st.delay = 0.3f;
        // 遠端封口（萬一沒有切換場景也不會走出橋外）
        var end = new GameObject("FarEndWall");
        end.transform.SetParent(cols, false);
        end.transform.position = BridgePoint(BridgeLength - 2f, 0f, 1.5f);
        end.AddComponent<BoxCollider>().size = new Vector3(0.4f, 4f, BridgeWidth + 1f);
    }

    // =====================================================================
    //  山谷霧氣、邊界
    // =====================================================================

    static void BuildMist(Transform parent)
    {
        var g = Group("ValleyMist", parent);

        // 大霧區：從最後幾個彎道開始起霧，到山頂平台與吊橋上最濃（顏色接近 ForestIsland 的霧）
        var zone = new GameObject("BridgeFogZone").AddComponent<FogZone>();
        zone.transform.SetParent(g, false);
        zone.start = Plateau + Vector3.up * 1.5f;
        zone.end = BridgePoint(BridgeLength * 0.6f, 0f, 1.5f);
        zone.innerRadius = 7f;
        zone.outerRadius = 48f;
        zone.density = 0.075f;
        zone.fogColor = new Color(0.72f, 0.76f, 0.78f);

        // 霧團：天空盒不吃霧，用大片霧粒子把平台與吊橋四周的天空也蓋掉
        var bank = NewPS("PlateauFogBank", g, Vector3.Lerp(Plateau, BridgePoint(BridgeLength * 0.5f, 0f, 0f), 0.4f) + Vector3.up * 3f);
        var bm = bank.main;
        bm.loop = true; bm.prewarm = true; bm.duration = 8f;
        bm.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
        bm.startSpeed = 0f;
        bm.startSize = new ParticleSystem.MinMaxCurve(10f, 20f);
        bm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        bm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.83f, 0.85f, 0.35f), new Color(0.88f, 0.9f, 0.92f, 0.55f));
        bm.maxParticles = 110;
        var be = bank.emission; be.rateOverTime = 8f;
        var bsh = bank.shape; bsh.shapeType = ParticleSystemShapeType.Box; bsh.scale = new Vector3(BridgeLength * 0.8f, 14f, 46f);
        var bvel = bank.velocityOverLifetime; bvel.enabled = true; bvel.space = ParticleSystemSimulationSpace.World;
        bvel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f); bvel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.15f); bvel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        var bcol = bank.colorOverLifetime; bcol.enabled = true; bcol.color = Fade();
        SetPR(bank, mMist, 1.2f);

        // 沿最後兩段山路飄過的低霧（玩家會感覺霧從前方湧過來）
        for (int i = PathPts.Length - 4; i < PathPts.Length - 1; i++)
        {
            var low = NewPS($"PathFog_{i}", g, Vector3.Lerp(PathPts[i], PathPts[i + 1], 0.5f) + Vector3.up * 1.5f);
            var lm = low.main;
            lm.loop = true; lm.prewarm = true; lm.duration = 6f;
            lm.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
            lm.startSpeed = 0f;
            lm.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
            lm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            lm.startColor = new Color(0.85f, 0.88f, 0.9f, 0.18f + 0.08f * (i - (PathPts.Length - 4)));
            lm.maxParticles = 30;
            var le = low.emission; le.rateOverTime = 3f;
            var lsh = low.shape; lsh.shapeType = ParticleSystemShapeType.Box; lsh.scale = new Vector3(14f, 3f, 14f);
            var lvel = low.velocityOverLifetime; lvel.enabled = true; lvel.space = ParticleSystemSimulationSpace.World;
            lvel.x = new ParticleSystem.MinMaxCurve(-0.6f, -0.2f); lvel.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.05f); lvel.z = new ParticleSystem.MinMaxCurve(-0.4f, -0.1f);
            var lcol = low.colorOverLifetime; lcol.enabled = true; lcol.color = Fade();
            SetPR(low, mMist, 0.8f);
        }
        var ps = NewPS("GorgeMist", g, new Vector3(BridgeStart.x + BridgeLength * 0.5f, 8f, 200f));
        var m = ps.main;
        m.loop = true; m.prewarm = true; m.duration = 10f;
        m.startLifetime = new ParticleSystem.MinMaxCurve(20f, 30f);
        m.startSpeed = 0f;
        m.startSize = new ParticleSystem.MinMaxCurve(18f, 32f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new Color(0.86f, 0.9f, 0.93f, 0.5f);
        m.maxParticles = 90;
        var e = ps.emission; e.rateOverTime = 4f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(BridgeLength + 30f, 12f, 60f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.2f, 0.6f); vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f); vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade();
        SetPR(ps, mMist, 0.6f);

        // 橋的遠端：一堵濃霧牆，切換場景前就看不到盡頭
        var wallMist = NewPS("BridgeEndFog", g, BridgePoint(BridgeLength * 0.75f, 0f, 1.5f));
        var wm = wallMist.main;
        wm.loop = true; wm.prewarm = true; wm.duration = 6f;
        wm.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
        wm.startSpeed = 0f;
        wm.startSize = new ParticleSystem.MinMaxCurve(8f, 14f);
        wm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        wm.startColor = new Color(0.9f, 0.92f, 0.94f, 0.55f);
        wm.maxParticles = 60;
        var we = wallMist.emission; we.rateOverTime = 7f;
        var wsh = wallMist.shape; wsh.shapeType = ParticleSystemShapeType.Box; wsh.scale = new Vector3(BridgeLength * 0.5f, 6f, 14f);
        var wcol = wallMist.colorOverLifetime; wcol.enabled = true; wcol.color = Fade();
        SetPR(wallMist, mMist, 0.7f);
    }

    /// <summary>步道兩側的山坡：一條沿著步道、離路 4~16m 的霧帶（從帶狀 Mesh 表面發射霧粒子）。</summary>
    static void BuildOffTrailMist(Transform parent)
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        foreach (var (pos, dir) in PathSamples(3f, 0f))
        {
            if (pos.x < WallX + 3f) continue;
            if (pos.y < 10.5f) continue;   // 低處是山谷（兩側山壁），不需要迷霧
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (float sgn in new[] { -1f, 1f })
            {
                var a = pos + side * sgn * 4.2f;
                var b = pos + side * sgn * 16f;
                var f = dir * 1.6f;
                int i0 = verts.Count;
                // 霧貼著地形：外側用地形高度（山坡往下就跟著往下）
                verts.Add(new Vector3((a - f).x, GroundY((a - f).x, (a - f).z) + 0.6f, (a - f).z));
                verts.Add(new Vector3((a + f).x, GroundY((a + f).x, (a + f).z) + 0.6f, (a + f).z));
                verts.Add(new Vector3((b + f).x, Mathf.Max(GroundY((b + f).x, (b + f).z), pos.y - 6f) + 0.6f, (b + f).z));
                verts.Add(new Vector3((b - f).x, Mathf.Max(GroundY((b - f).x, (b - f).z), pos.y - 6f) + 0.6f, (b - f).z));
                tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
        }
        if (verts.Count == 0) return;
        var mesh = new Mesh { name = "Mesh_DM_OffTrailMistBand", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = $"{MeshDir}/{mesh.name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        var ps = NewPS("OffTrailMist", Group("OffTrailMist", parent), Vector3.zero);
        var m = ps.main;
        m.loop = true; m.prewarm = true; m.duration = 10f;
        m.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
        m.startSpeed = 0f;
        m.startSize = new ParticleSystem.MinMaxCurve(6f, 12f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.84f, 0.87f, 0.9f, 0.3f), new Color(0.9f, 0.92f, 0.94f, 0.45f));
        m.maxParticles = 320;
        var e = ps.emission; e.rateOverTime = 22f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Mesh;
        sh.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        sh.mesh = mesh;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f); vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.12f); vel.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade();
        SetPR(ps, mMist, 0.9f);
    }

    /// <summary>離開步道 / 平台 / 吊橋就直接死亡。</summary>
    static void BuildKillZone(Transform parent)
    {
        var kz = new GameObject("TrailBoundsKillZone").AddComponent<TrailBoundsKillZone>();
        kz.transform.SetParent(parent, false);
        kz.activeMinX = WallX + 0.6f;
        kz.pathPoints = PathPts.ToArray();
        kz.allowedHalfWidth = PathHalfWidth + 1f;   // 路緣石那一圈還算安全
        kz.plateauCenter = Plateau;
        kz.plateauRadius = PlateauRadius;
        kz.bridgeStart = BridgePoint(-1.5f, 0f, 0f);
        kz.bridgeEnd = BridgePoint(BridgeLength, 0f, 0f);
        kz.bridgeHalfWidth = BridgeWidth * 0.5f;
        kz.maxWanderDistance = 100000f;   // 低處有山壁圍住，不需要「走太遠就迷失」的判定；只保留掉下峭壁死亡
    }

    static void BuildBoundary(Transform parent)
    {
        var g = Group("MountainBoundary (invisible)", parent);
        float x0 = TerrainX0, x1 = TerrainX0 + TerrainSize, z0 = TerrainZ0, z1 = TerrainZ0 + TerrainSize;
        void Wall(Vector3 a, Vector3 b)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(g, false);
            var mid = (a + b) * 0.5f;
            go.transform.position = new Vector3(mid.x, 30f, mid.z);
            go.transform.rotation = Quaternion.LookRotation(b - a);
            go.AddComponent<BoxCollider>().size = new Vector3(1f, 100f, (b - a).magnitude);
        }
        Wall(new Vector3(x1 - 1f, 0, z0), new Vector3(x1 - 1f, 0, z1));     // 東
        Wall(new Vector3(x0, 0, z1 - 1f), new Vector3(x1, 0, z1 - 1f));     // 北
        Wall(new Vector3(x0 + 70f, 0, z0 + 1f), new Vector3(x1, 0, z0 + 1f)); // 南（靠道場那段是平地，不擋）
        // 山頂平台的東緣（吊橋口兩側），避免從平台掉進山谷
        foreach (float side in new[] { -1f, 1f })
        {
            var go = new GameObject("PlateauEdge");
            go.transform.SetParent(g, false);
            go.transform.position = BridgeStart + new Vector3(0.5f, 1.5f, side * (BridgeWidth * 0.5f + 0.3f + 4f));
            go.AddComponent<BoxCollider>().size = new Vector3(1f, 4f, 8f);
        }
    }

    // =====================================================================
    //  道場場景：開東牆、Build Settings
    // =====================================================================

    static void OpenEastGate()
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (!t.name.StartsWith("KHS_WallBase")) continue;
            var p = t.position;
            if (Mathf.Abs(p.x - WallX) > 0.6f) continue;
            // 牆降低處、溪流、月洞門這一帶的石基座全部關掉（新牆自己有基座）
            bool river = p.z > RiverGapZ0 - 1.6f && p.z < RiverGapZ1 + 1.6f;
            bool gate = moonGateHalfSpan > 0f && p.z > moonGateCenterZ - moonGateHalfSpan - 1.5f;
            if (!river && !gate) continue;
            if (t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(false);
                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
            }
            n++;
        }
        Debug.Log($"[DojoMountain] 東牆：關掉 {n} 段 KHS_WallBase（進水口與月洞門處）；月洞門 z = {moonGateCenterZ - moonGateHalfSpan:0.0} ~ {moonGateCenterZ + moonGateHalfSpan:0.0}");
        Physics.SyncTransforms();
        var hits = Physics.OverlapBox(new Vector3(WallX, 1f, moonGateCenterZ), new Vector3(1.2f, 0.8f, 1.6f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
            if (!(h is TerrainCollider) && !h.transform.IsChildOf(GameObject.Find(InstanceName).transform))
                Debug.LogWarning($"[DojoMountain] 缺口處還有碰撞體擋路：{GetPath(h.transform)}");
    }

    static string GetPath(Transform t) => t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;

    static void EnsureBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        bool changed = false;
        foreach (var p in new[] { DojoScenePath, ForestScenePath })
            if (!list.Any(s => s.path == p) && File.Exists(p)) { list.Add(new EditorBuildSettingsScene(p, true)); changed = true; }
        if (changed) EditorBuildSettings.scenes = list.ToArray();
    }

    // =====================================================================
    //  材質
    // =====================================================================

    static void CreateMaterials()
    {
        mStreamWater = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/FinalDojoArena/Garden/Rear_StreamWater.mat");
        mRock = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/ForestIsland/M_FI_CliffRock.mat")
                ?? AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/FinalDojoArena/Garden/M_FJG_Rock_001_dbf74aa0_URP_6715a30a.mat");
        mPlank = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/ForestIsland/M_FI_BridgePlank.mat");
        mRope = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/ForestIsland/M_FI_Rope.mat");
        mWood = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/ForestIsland/M_FI_OldBark.mat") ?? mPlank;

        // 瀑布：縱向水紋，透明、雙面、往下捲動
        var streak = SaveTexture("T_DM_WaterfallStreaks", 128, 256, (u, v) =>
        {
            float cols = Mathf.PerlinNoise(u * 18f, 0.3f) * 0.6f + Mathf.PerlinNoise(u * 41f, 2.1f) * 0.4f;
            float flow = Mathf.PerlinNoise(u * 9f + 4f, v * 5f) * 0.5f + 0.5f;
            float a = Mathf.Clamp01(cols * flow * 1.4f - 0.15f);
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, u)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.88f, u));
            float b = 0.85f + 0.15f * cols;
            return new Color(b, b, b, Mathf.Clamp01(0.25f + a) * edge);
        });
        mWaterfall = GetOrCreateMat("M_DM_Waterfall", "Universal Render Pipeline/Unlit");
        mWaterfall.SetTexture("_BaseMap", streak);
        mWaterfall.SetColor("_BaseColor", new Color(0.86f, 0.93f, 0.97f, 0.85f));
        MakeTransparent(mWaterfall);

        var soft = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/VFX/ForestIsland/T_FI_SoftCloud.png");
        mMist = GetOrCreateMat("M_DM_Mist", "Universal Render Pipeline/Particles/Unlit");
        mMist.SetTexture("_BaseMap", soft);
        mMist.SetColor("_BaseColor", Color.white);
        MakeTransparent(mMist);
        if (mStreamWater == null) mStreamWater = mWaterfall;
    }

    static void MakeTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
    }

    static Material GetOrCreateMat(string name, string shader)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(mat, path); }
        return mat;
    }

    static Texture2D SaveTexture(string name, int w, int h, System.Func<float, float, Color> f)
    {
        string path = $"{MatDir}/{name}.png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, f((x + 0.5f) / w, (y + 0.5f) / h));
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =====================================================================
    //  小工具
    // =====================================================================

    static GameObject Load(string pathUnderAssets)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{pathUnderAssets}.prefab");
        if (go == null) Debug.LogWarning($"[DojoMountain] 找不到 Assets/{pathUnderAssets}.prefab");
        return go;
    }

    static GameObject Place(Transform parent, GameObject prefab, string name, Vector3 pos, Quaternion rot, float scale)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale *= scale;
        FixMaterials(go);
        return go;
    }

    // ---------------------------------------------------------------------
    //  素材的原始材質是內建管線的 Standard（URP 下會變洋紅色）。
    //  道場隊友已經在 Materials/FinalDojoArena/Garden 轉好 URP 版（樹用 DojoGardenWind），
    //  名稱是「原名_雜湊_LOD_編號_頂點數_雜湊」。這裡照同樣的規則對應；找不到就自動轉一份 URP Lit。
    // ---------------------------------------------------------------------
    const string GardenMatDir = Root + "/Materials/FinalDojoArena/Garden";
    static List<Material> gardenMats;
    static readonly Dictionary<Material, Material> fallbackCache = new Dictionary<Material, Material>();

    static bool IsUrpReady(Material m)
    {
        if (m == null || m.shader == null) return false;
        string n = m.shader.name;
        if (n.StartsWith("Hidden/InternalErrorShader")) return false;
        if (n.StartsWith("Universal Render Pipeline") || n.StartsWith("Shader Graphs") || n.StartsWith("Particles/")) return true;
        string path = AssetDatabase.GetAssetPath(m.shader);
        return path.StartsWith("Assets/_Project") || path.Contains("Universal");
    }

    static int LodIndex(string rendererName)
    {
        var mt = System.Text.RegularExpressions.Regex.Match(rendererName, @"LOD_?(\d)");
        return mt.Success ? int.Parse(mt.Groups[1].Value) : -1;
    }

    static void FixMaterials(GameObject go)
    {
        if (gardenMats == null)
            gardenMats = AssetDatabase.FindAssets("t:Material", new[] { GardenMatDir })
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).Where(m => m != null).ToList();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            int lod = LodIndex(r.name);
            var mf = r.GetComponent<MeshFilter>();
            int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || IsUrpReady(m)) continue;
                // 看板（最遠的 LOD）沒有 URP 版：直接不畫
                if (m.name.ToLower().Contains("billboard")) { r.enabled = false; changed = false; break; }
                var cands = gardenMats.Where(c => c.name.StartsWith(m.name + "_")).ToList();
                if (lod >= 0 && cands.Any(c => c.name.Contains($"_LOD_{lod}_"))) cands = cands.Where(c => c.name.Contains($"_LOD_{lod}_")).ToList();
                Material pick = null;
                if (cands.Count > 0)
                    pick = cands.OrderBy(c =>
                    {
                        var mt = System.Text.RegularExpressions.Regex.Match(c.name, @"_LOD_\d_(\d+)_");
                        return mt.Success && verts > 0 ? Mathf.Abs(int.Parse(mt.Groups[1].Value) - verts) : 0;
                    }).First();
                mats[i] = pick != null ? pick : UrpFallback(m);
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// <summary>沒有現成 URP 版的材質：複製貼圖與顏色到 URP Lit（葉子類用 Alpha Clip、雙面）。</summary>
    static Material UrpFallback(Material src)
    {
        if (fallbackCache.TryGetValue(src, out var cached) && cached != null) return cached;
        string dir = MatDir + "/URP";
        EnsureFolder(dir);
        string path = $"{dir}/{src.name}_URP.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        Texture main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
        if (main != null) { m.SetTexture("_BaseMap", main); m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex")); }
        if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.GetColor("_Color"));
        if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null) { m.SetTexture("_BumpMap", src.GetTexture("_BumpMap")); m.EnableKeyword("_NORMALMAP"); }
        string lower = src.name.ToLower();
        bool leafy = lower.Contains("sprout") || lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("atlas") || src.IsKeywordEnabled("_ALPHATEST_ON");
        if (leafy)
        {
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f); m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        m.SetFloat("_Smoothness", 0.1f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        fallbackCache[src] = m;
        Debug.Log($"[DojoMountain] {src.name} 沒有現成的 URP 版，已自動轉成 {path}");
        return m;
    }

    static void FitHeight(GameObject go, float height)
    {
        bool has = false; var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has || b.size.y < 0.01f) return;
        float k = height / b.size.y;
        if (k > 0.5f && k < 2f) return;   // 原尺寸已經合理就不動
        var basePos = go.transform.position;
        go.transform.localScale *= k;
        go.transform.position = basePos;
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static GameObject Cyl(Transform p, string n, Vector3 pos, Vector3 scale, Material m) => Prim(PrimitiveType.Cylinder, p, n, pos, Vector3.zero, scale, m);

    static GameObject Segment(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material m)
    {
        var dir = b - a;
        var go = Prim(PrimitiveType.Cylinder, parent, name, (a + b) * 0.5f, Vector3.zero, new Vector3(radius * 2f, dir.magnitude * 0.5f + radius, radius * 2f), m);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static GameObject MeshObj(Transform parent, string name, Mesh mesh, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    static Mesh SaveMesh(string name, List<Vector3> v, List<Vector2> uv, List<int> t)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        string path = $"{MeshDir}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static ParticleSystem NewPS(string name, Transform parent, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        return ps;
    }

    static void SetPR(ParticleSystem ps, Material m, float maxSize = 0.5f)
    {
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = m;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
        pr.maxParticleSize = maxSize;
    }

    static Gradient Fade()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // =====================================================================
    //  預覽截圖（batchmode 驗證用）
    // =====================================================================

    public static void BuildAndCapture()
    {
        Build();
        Capture();
    }

    [MenuItem("GeminiGrowUp/道場：後山預覽截圖")]
    public static void Capture()
    {
        string dir = System.Environment.GetEnvironmentVariable("DOJO_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp/DojoMountainShots");
        Directory.CreateDirectory(dir);
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != DojoScenePath)
            EditorSceneManager.OpenScene(DojoScenePath, OpenSceneMode.Single);
        terrain = GameObject.Find(InstanceName)?.GetComponentInChildren<Terrain>();
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>()) ps.Simulate(8f, true, true);
        float G(float x, float z) => terrain != null ? GroundY(x, z) : 0f;
        var shots = new (string, Vector3, Vector3)[]
        {
            ("01_courtyard_to_mountain", new Vector3(0f, 3f, 70f), new Vector3(85f, 14f, 140f)),
            ("02_rear_garden_river", new Vector3(20f, 2.2f, 104f), new Vector3(70f, 6f, 120f)),
            ("03_gate_and_torii", new Vector3(40f, 1.9f, 122f), new Vector3(70f, 2f, 128f)),
            ("04_waterfall_pool", new Vector3(56f, 2.5f, 106f), new Vector3(80f, 10f, 117f)),
            ("05_switchback", new Vector3(90f, G(90f, 138f) + 2f, 138f), new Vector3(80f, 13f, 162f)),
            ("06_plateau_bridge", new Vector3(110f, 27f, 196f), new Vector3(160f, 22f, 201f)),
            ("07_aerial", new Vector3(10f, 70f, 60f), new Vector3(100f, 10f, 160f)),
        };
        var go = new GameObject("PreviewCamera");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 55f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 1000f;
        UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).renderPostProcessing = true;
        var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        foreach (var (name, pos, look) in shots)
        {
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            cam.Render(); cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        }
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        Debug.Log($"[DojoMountain] 預覽截圖：{dir}");
    }
}
#endif
