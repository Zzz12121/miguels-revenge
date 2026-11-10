#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WaterSystem;

/// <summary>
/// 選單：GeminiGrowUp > 建立日式村莊場景
/// 產生 Scenes/JapaneseVillage.unity：南邊是海岸與棧橋、中間三排房屋、東邊盡頭是村長宅院、北邊是後山與竹林。
/// 跟 Planet 一樣使用 Terrain + PlayerRig + MerchantNPC，可以重複執行（會覆蓋場景和產生出來的資產）。
/// 建築、石頭、燈籠、竹子來自 ThirdParty/URP 3D Sample Garden；松、楓、櫻、鳥居、地面貼圖來自 Assets/Waldemarst（Japanese Garden Pack）。
///
/// 座標：+Z = 北（後山），-Z = 南（海），水面 y = 0。
/// 想改配置：改這個檔案上面的常數或 PlaceNpcs 再重跑；場景裡的東西也可以直接手動調整（但重跑會覆蓋）。
/// </summary>
public static class JapaneseVillageBuilder
{
    const string SceneDir = "Assets/_Project/Scenes";
    const string ScenePath = SceneDir + "/JapaneseVillage.unity";
    const string ModelDir = "Assets/_Project/Models/JapaneseVillage";
    const string MeshAssetPath = ModelDir + "/JV_Meshes.asset";
    const string PrefabDir = "Assets/_Project/Prefabs/JapaneseVillage";
    const string HousePrefabDir = PrefabDir + "/Houses";
    const string TreePrefabDir = PrefabDir + "/Trees";
    const string TerrainDir = "Assets/_Project/Terrain/JapaneseVillage";
    const string WaterDir = "Assets/_Project/ThirdParty/Viking Village/Boat Attack Water System/Prefabs";
    const string PlayerPrefabPath = "Assets/_Project/Prefabs/PlayerRig.prefab";
    const string VikingProps = "Assets/_Project/ThirdParty/Viking Village/Prefabs/Props/";

    // ---------------- 配置（公尺）----------------
    const float TerrainSize = 400f;
    const float TerrainHeight = 95f;
    const float TerrainBaseY = -12f;
    const float BeachRise = 0.11f;      // 沙灘的坡度
    const float VillageY = 2.6f;

    // 東西向的路：海岸道路 + 三排房子之間的路（中心 z, 寬度）
    static readonly (float z, float width)[] EastWestRoads = { (-38f, 6f), (-8f, 6f), (20f, 6f), (48f, 6f) };
    // 三排房子（中心 z，正面都朝南 = 朝向海）
    static readonly float[] HouseRows = { -22f, 6f, 34f };
    const float HouseRowHalfSpan = 92f;
    const float HouseStartX = 7.2f;                  // 第一棟的牆離主街中心線多遠
    const float MainStreetHalfWidth = 4f;
    const float LaneX = 50.5f, LaneHalfWidth = 2.5f; // 南北向的小巷
    const float PlazaX = 95f, PlazaHalfWidth = 3f;   // 房屋東側盡頭、村長宅院門前的南北向道路
    const float RoadWest = -100f, RoadEast = 98f;
    const float RoadSouth = -41f, RoadNorth = 51f;

    // 村長宅院（圍牆內）：朝西，大門對著中間那排房屋
    // 東邊多留一段後院，後院東牆的大門就是通往道場的傳送門
    const float ChiefX0 = 100f, ChiefX1 = 156f, ChiefZ0 = -19.3f, ChiefZ1 = 31.3f;
    const float ChiefAxisZ = 6f, ChiefHouseX = 133f, ChiefHouseScale = 0.85f;
    const float ChiefGateHalf = 3.3f;
    const int HouseDepth = 12;
    // 村長家：隊友道場場景（DojoArena）的主殿，外觀和最終決戰的道場一致
    const string DojoHousePrefab = "Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_MainDojo_Complete.prefab";
    static float ChiefGroundY => PlateauY(ChiefAxisZ);

    const float PierX = 22f;
    const float PierDeckY = 1.6f, PierWidth = 3.2f, PierLength = 44f;

    const float StairStartZ = 54f, StairEndZ = 120f, ClearingZ = 132f;

    // 最終決戰：僧侶打開的村長家後院大門要去的場景（Build 時會自動加進 Build Settings）
    const string FinalBattleScene = "DojoArena_ArchitecturePreview";
    const string FinalBattleScenePath = "Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity";
    const string FinalBattleTitle = "黑衣人的道場";
    const string PortalPrefabPath = "Assets/_Project/Prefabs/Portal.prefab";
    const float HillHeight = 44f, HillLength = 140f;

    // 山上的祠堂（可以走進去）和後面的習武場
    const float ShrineZ = 143f, ShrineScale = 1.2f;
    const int ShrineWidth = 20;
    const float TerraceHalfX = 72f, TerraceZ0 = 134f, YardZ1 = 186.2f, TerraceBlend = 18f;
    // 習武場東半邊的深溝：橫跨整個院子（南北），只能靠拉索盪過去
    const float PitX0 = 20f, PitX1 = 60f, PitDepth = 14f;
    static Rect pitRect;   // 對齊 Terrain 挖洞格子之後的實際範圍（BuildTerrain 算出來）
    const string FighterModel = "Assets/_Project/ThirdParty/Plugins/Banana Yellow Games/Characters/Banana Man/Banana Man.fbx";
    const string FighterControllerPath = "Assets/_Project/Animations/JapaneseVillage/AC_WoodenFighter.controller";
    // 村民模型（Tripo 生成、已綁好人形骨架）：貼圖用 Art/JapaneseVillage/make_villager_textures.py 做出幾種衣服顏色
    const string VillagerDir = "Assets/_Project/Models/JapaneseVillage/Villager";
    const string VillagerModel = VillagerDir + "/Villager_Peasant.fbx";
    const string VillagerControllerPath = "Assets/_Project/Animations/JapaneseVillage/AC_Villager.controller";
    const float VillagerHeight = 1.72f;
    // 所有村民（含師父、僧侶）都放大這麼多倍：碰撞、對話距離、走路速度換算都跟著縮放
    const float NpcScale = 1.2f;
    static RuntimeAnimatorController villagerController;
    // 僧侶（Tripo 生成、Mixamo 骨架）：平常在石階上走動，習武場完成後走進習武場跟玩家過招
    const string MonkDir = "Assets/_Project/Models/JapaneseVillage/Monk";
    const string MonkModelPath = MonkDir + "/Monk.fbx";
    const string MonkControllerPath = "Assets/_Project/Animations/JapaneseVillage/AC_Monk.controller";
    const float MonkHeight = 1.7f;
    // 村長（Tripo 生成、Mixamo 骨架）：站在村長家主殿裡，跟玩家講村子的往事和能做的事（台詞在 ShrineTrial.ElderLines）
    const string ElderDir = "Assets/_Project/Models/JapaneseVillage/Elder";
    const string ElderModelPath = ElderDir + "/Elder.fbx";
    const float ElderHeight = 1.68f;
    // 女村民（挑水的婦人、賣花的姑娘、茶屋老闆娘）和鐵匠鋪的女鐵匠：Tripo 生成、Mixamo 骨架
    const string VillagerWomanModel = "Assets/_Project/Models/JapaneseVillage/VillagerWoman/VillagerWoman.fbx";
    const float VillagerWomanHeight = 1.6f;
    const string BlacksmithModel = "Assets/_Project/Models/JapaneseVillage/Blacksmith/Blacksmith.fbx";
    const float BlacksmithHeight = 1.7f;
    // 老漁夫（坐在棧橋末端釣魚）、砍竹人：Tripo 生成、Mixamo 骨架
    const float FishermanHeight = 1.66f, WoodcutterHeight = 1.72f, MerchantHeight = 1.68f, MasterHeight = 1.66f, ChildGirlHeight = 1.6f;

    static HumanModel TripoLook(string name, float height, string label) => new HumanModel
    {
        path = $"Assets/_Project/Models/JapaneseVillage/{name}/{name}.fbx", material = TripoMaterial(name), controller = villagerController, height = height, label = label,
    };

    // 鐵匠鋪：中間那排房子、主街西邊的第一間；門口的架子擺著玩家沒帶的武器（PlayerLoadout）
    const string BowModel = "Assets/_Project/Models/JapaneseVillage/Bow/Bow.fbx";
    const string SaberModel = "Assets/_Project/Models/PlayerCharacter/Weapons/PlayerMagicSaber/PlayerMagicSaber_Tripo/tripo_convert_583226e1-5085-4c21-a3df-556926377a26.fbx";
    const string SaberMaterialPath = "Assets/_Project/Models/PlayerCharacter/Weapons/PlayerMagicSaber/Materials/M_PlayerMagicSaber.mat";
    // 玩家換武器時 PlayerLoadout 從 Resources 載入（任何場景都用得到）
    const string WeaponResourceDir = "Assets/_Project/Resources/Weapons";
    const int SmithyWidth = 16;
    static float SmithyX => -(HouseStartX + SmithyWidth * 0.5f);
    static float SmithyZ => HouseRows[1];
    static float SmithyFrontZ => SmithyZ - HouseDepth * 0.5f;   // 房子轉 180 度朝南，正面在 -Z
    static GameObject knifePrefab, spearPrefab, bowPrefab;
    static Vector3 teaHouseDoor;   // 茶屋（第二排主街東側第一間）正門口的地面位置：老闆娘進屋用

    static HumanModel WomanLook(string label) => new HumanModel
    {
        path = VillagerWomanModel, material = TripoMaterial("VillagerWoman"), controller = villagerController, height = VillagerWomanHeight, label = label,
    };
    static MerchantNPC trialElder;
    static Vector3 elderBenchPos;
    static RuntimeAnimatorController monkController;
    static float[] monkAttackLengths;
    // 木頭人的大小（Banana Man 縮放到幾公尺高）；木樁本身也照這個比例放大
    const float FighterHeight = 3f;
    const float FighterSize = FighterHeight / 2f;

    /// <summary>綁好人形骨架的模型：用哪個 FBX、材質、Animator、多高</summary>
    sealed class HumanModel
    {
        public string path;
        public Material material;
        public RuntimeAnimatorController controller;
        public float height;
        public string label;
    }

    static HumanModel VillagerLook(string variant) => new HumanModel
    {
        path = VillagerModel, material = VillagerMaterial(variant), controller = villagerController, height = VillagerHeight, label = variant,
    };
    static float YardZ0 => ShrineZ + HouseDepth * 0.5f * ShrineScale; // 習武場的南邊就是祠堂的背牆
    static float TrialGroundY => StairY(StairEndZ);

    const float BoundsEast = 175f, BoundsNorth = 190f, WadingDepth = 12f;

    static Terrain terrain;
    static readonly List<Rect> footprints = new List<Rect>();
    static readonly List<Mesh> pendingMeshes = new List<Mesh>();
    static bool meshAssetCreated;

    [MenuItem("GeminiGrowUp/建立日式村莊場景 (Build Japanese Village Scene)")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    /// <summary>給 Unity -batchmode -executeMethod 用</summary>
    public static void Build()
    {
        JapaneseVillageAssets.EnsureFolder(ModelDir);
        JapaneseVillageAssets.EnsureFolder(PrefabDir);
        JapaneseVillageAssets.EnsureFolder(TerrainDir);
        JapaneseVillageAssets.EnsureFolder(HousePrefabDir);
        JapaneseVillageAssets.EnsureFolder(TreePrefabDir);
        JapaneseVillageAssets.BuildAll();
        JVGarden.ClearCache();

        AssetDatabase.DeleteAsset(MeshAssetPath);
        meshAssetCreated = false;
        footprints.Clear();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        SetupLighting();

        var hillTrees = BuildTreePrefabs();
        terrain = BuildTerrain();
        BuildWater();

        var village = new GameObject("Village").transform;
        BuildHouseRows(village);
        BuildStreetDetails(village);
        BuildChiefCompound(village);

        var coast = new GameObject("Coast").transform;
        BuildCoast(coast);

        var hill = new GameObject("BackHill").transform;
        BuildHillFeatures(hill);

        PlantTrees(hillTrees);
        PaintDetails();

        var npcs = new GameObject("NPCs").transform;
        PlaceNpcs(npcs);
        PlaceHerbs();
        BuildBounds();
        PlacePlayer();

        FlushMeshes();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EnsureInBuildSettings(FinalBattleScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"[JapaneseVillage] 已建立場景：{ScenePath}（{footprints.Count} 棟房屋）。打開場景按 Play 就能走動。");
    }

    /// <summary>傳送門要去的場景必須在 Build Settings 裡；已經在的話不動</summary>
    static void EnsureInBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes)
            if (s.path == path) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[JapaneseVillage] 已把 {path} 加進 Build Settings（村長家後院的傳送門要用）");
    }

    // ---------------------------------------------------------------- 網格資產

    /// <summary>所有產生的網格存進同一個 .asset，專案視窗比較乾淨</summary>
    static Mesh SaveMesh(Mesh mesh)
    {
        if (!meshAssetCreated)
        {
            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            meshAssetCreated = true;
        }
        else
        {
            AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);
        }
        return mesh;
    }

    static void FlushMeshes() => AssetDatabase.ImportAsset(MeshAssetPath);

    static GameObject Instance(string name, Transform parent, Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation, bool isStatic = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        go.isStatic = isStatic;
        return go;
    }

    static GameObject Instance(string name, Transform parent, JVMeshKit kit, Vector3 position, Quaternion rotation, bool isStatic = true)
        => Instance(name, parent, SaveMesh(kit.Build(name)), kit.Materials, position, rotation, isStatic);

    /// <summary>同一個模型重複放很多個（共用網格）</summary>
    sealed class SharedModel
    {
        public readonly Mesh mesh;
        public readonly Material[] materials;
        public SharedModel(string name, JVMeshKit kit)
        {
            mesh = SaveMesh(kit.Build(name));
            materials = kit.Materials;
        }
        public GameObject Place(string name, Transform parent, Vector3 position, Quaternion rotation, bool isStatic = true)
            => Instance(name, parent, mesh, materials, position, rotation, isStatic);
    }

    static GameObject PlacePrefab(string path, Transform parent, Vector3 position, Quaternion rotation, float scale = 1f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"[JapaneseVillage] 找不到 Prefab：{path}");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale *= scale;
        return go;
    }

    static float Ground(float x, float z)
        => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;

    // ---------------------------------------------------------------- 光線與天空

    static void SetupLighting()
    {
        var sunGo = new GameObject("Directional Light");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.93f, 0.80f);
        sun.intensity = 1.5f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.82f;
        sunGo.transform.rotation = Quaternion.Euler(42f, 32f, 0f);   // 下午的太陽，從西南照過來，房屋正面有光

        var sky = JapaneseVillageAssets.ShaderMaterial("M_JV_Sky", "Skybox/Procedural");
        sky.SetFloat("_SunDisk", 1f);
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_AtmosphereThickness", 1f);
        sky.SetColor("_SkyTint", new Color(0.50f, 0.50f, 0.52f));
        sky.SetColor("_GroundColor", new Color(0.55f, 0.58f, 0.60f));
        sky.SetFloat("_Exposure", 1.15f);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.60f, 0.68f, 0.80f);
        RenderSettings.ambientEquatorColor = new Color(0.56f, 0.54f, 0.50f);
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.27f, 0.22f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.74f, 0.79f, 0.84f);
        RenderSettings.fogDensity = 0.0034f;
    }

    // ---------------------------------------------------------------- 地形

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>海岸線：z 小於這個值就是海</summary>
    public static float ShoreZ(float x)
        => -70f + 9f * Mathf.Sin(x * 0.018f + 0.6f) + 4f * Mathf.Sin(x * 0.05f + 1.1f);

    static float PlateauY(float z) => VillageY + 0.02f * Mathf.Max(0f, z + 48f);

    /// <summary>石階路的高度：沿著後山原本的斜坡（去掉起伏），石階才不會變成突起的路堤</summary>
    static float StairY(float z)
    {
        z = Mathf.Clamp(z, StairStartZ, StairEndZ);
        return PlateauY(z) + HillHeight * Smooth01((z - StairStartZ) / HillLength);
    }

    static float Bump(float x, float z, float frequency, float ox, float oz)
        => Mathf.PerlinNoise(x * frequency + 500f + ox, z * frequency + 500f + oz) - 0.5f;

    /// <summary>整張地圖的高度：海底 → 沙灘 → 村莊平地 → 後山，兩側有山丘圍成海灣</summary>
    static float HeightAt(float x, float z)
    {
        float d = z - ShoreZ(x);
        float y;
        if (d < 0f)
        {
            // 淺水區可以涉水（到腰），再外面才變深
            y = d > -WadingDepth ? d * 0.1f : -WadingDepth * 0.1f + (d + WadingDepth) * 0.35f;
            y = Mathf.Max(y, -11f);
        }
        else
        {
            y = Mathf.Lerp(d * BeachRise, PlateauY(z), Smooth01((d - 16f) / 10f));
            y += Bump(x, z, 0.07f, 0f, 0f) * 0.25f * Smooth01(d / 6f);
        }

        // 後山
        float t = Smooth01((z - StairStartZ) / HillLength);
        y += HillHeight * t + Bump(x, z, 0.018f, 13f, 7f) * 14f * t + Bump(x, z, 0.06f, 3f, 21f) * 3f * t;

        // 東西兩側的山丘，延伸到海邊
        float side = Smooth01((Mathf.Abs(x) - 130f) / 60f) * Smooth01((d + 30f) / 50f);
        y += side * (24f + Bump(x, z, 0.03f, 40f, 9f) * 12f);

        // 上山的石階路：挖出一條坡度固定的通道
        float corridorZ = Smooth01((z - StairStartZ + 4f) / 6f) * Smooth01((StairEndZ + 8f - z) / 6f);
        float corridor = Smooth01(1f - (Mathf.Abs(x) - 3f) / 9f) * corridorZ;
        y = Mathf.Lerp(y, StairY(z), corridor);

        // 山上的神社空地
        float r = new Vector2(x, z - ClearingZ).magnitude;
        y = Mathf.Lerp(y, StairY(StairEndZ), Smooth01(1f - (r - 16f) / 24f));

        // 祠堂和習武場：把後山切出一塊平台（兩側和北邊的山坡圍住它）
        y = Mathf.Lerp(y, TrialGroundY, Smooth01(1f - TerraceDistance(x, z) / TerraceBlend));

        // 村長宅院：整塊整平（東邊山丘也在這裡讓開）
        y = Mathf.Lerp(y, ChiefGroundY, Smooth01(1f - ChiefDistance(x, z) / 12f));
        return y;
    }

    /// <summary>離祠堂和習武場所在的平台外框多遠（裡面 = 0）</summary>
    static float TerraceDistance(float x, float z)
    {
        float dx = Mathf.Max(Mathf.Abs(x) - TerraceHalfX, 0f);
        float dz = Mathf.Max(TerraceZ0 - z, z - YardZ1, 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>離習武場圍牆外框多遠（裡面 = 0）</summary>
    static float YardDistance(float x, float z)
    {
        float dx = Mathf.Max(Mathf.Abs(x) - TerraceHalfX, 0f);
        float dz = Mathf.Max(YardZ0 - z, z - YardZ1, 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>離村長宅院圍牆外框多遠（裡面 = 0）</summary>
    static float ChiefDistance(float x, float z)
    {
        float dx = Mathf.Max(ChiefX0 - x, x - ChiefX1, 0f);
        float dz = Mathf.Max(ChiefZ0 - z, z - ChiefZ1, 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>距離最近道路中心線的「道路遮罩」：1 = 路面，0 = 不是路</summary>
    static float RoadMask(float x, float z, float jitter)
    {
        float mask = 0f;
        void Road(float distance, float halfWidth) => mask = Mathf.Max(mask, Smooth01((halfWidth + 0.8f + jitter - distance) / 1.6f));

        if (x > RoadWest && x < RoadEast)
            foreach (var (rz, width) in EastWestRoads) Road(Mathf.Abs(z - rz), width * 0.5f);
        if (z > RoadSouth && z < RoadNorth)
        {
            Road(Mathf.Abs(x), MainStreetHalfWidth);
            Road(Mathf.Abs(Mathf.Abs(x) - LaneX), LaneHalfWidth);
            Road(Mathf.Abs(x - PlazaX), PlazaHalfWidth);
        }
        // 村長宅院：大門進去到主屋前的路
        if (x > PlazaX && x < ChiefHouseX - 12f) Road(Mathf.Abs(z - ChiefAxisZ), 1.8f);
        // 通往棧橋的路
        if (z < RoadSouth + 2f && z > ShoreZ(PierX) + 8f) Road(Mathf.Abs(x - PierX), 2f);
        // 石階路與山上的空地
        if (z >= RoadNorth - 2f && z <= StairEndZ + 6f) Road(Mathf.Abs(x), 2.4f);
        Road(new Vector2(x, z - ClearingZ).magnitude, 10f);
        // 習武場：踩實的泥土地
        Road(YardDistance(x, z), 1f);
        return mask;
    }

    static TerrainLayer MakeLayer(string name, string texture, string normal, float tile, Color tint)
    {
        string path = $"{TerrainDir}/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texture);
        layer.normalMapTexture = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null;
        layer.tileSize = new Vector2(tile, tile);
        layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
        layer.smoothness = 0f;
        layer.metallic = 0f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static Terrain BuildTerrain()
    {
        const string pack = "Assets/Waldemarst/JapaneseGardenPackage/Textures/";
        const string sand = "Assets/_Project/ThirdParty/YughuesFreeSandMaterials/Textures/";
        var grass = MakeLayer("JV_Grass", pack + "brocco_jg_grass_03.jpg", null, 9f, new Color(0.86f, 0.9f, 0.74f));
        var beach = MakeLayer("JV_Sand", sand + "T_YFSM_03_d.tga", sand + "T_YFSM_03_n.tga", 9f, new Color(1.25f, 1.18f, 0.98f));
        var dirt = MakeLayer("JV_Road", pack + "brocco_jg_dirt_01.png", null, 7f, Color.white);
        var forest = MakeLayer("JV_ForestFloor", pack + "brocco_jg_moss03.png", pack + "brocco_jg_moss03_normal.png", 8f, new Color(0.5f, 0.56f, 0.38f));

        string dataPath = $"{TerrainDir}/JapaneseVillage_TerrainData.asset";
        AssetDatabase.DeleteAsset(dataPath);
        var data = new TerrainData();
        data.heightmapResolution = 513;
        data.alphamapResolution = 512;
        data.baseMapResolution = 512;
        data.SetDetailResolution(512, 16);
        data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
        AssetDatabase.CreateAsset(data, dataPath);

        // 高度
        int hr = data.heightmapResolution;
        var heights = new float[hr, hr];
        for (int zi = 0; zi < hr; zi++)
            for (int xi = 0; xi < hr; xi++)
            {
                float x = -TerrainSize * 0.5f + TerrainSize * xi / (hr - 1);
                float z = -TerrainSize * 0.5f + TerrainSize * zi / (hr - 1);
                heights[zi, xi] = Mathf.Clamp01((HeightAt(x, z) - TerrainBaseY) / TerrainHeight);
            }
        data.SetHeights(0, 0, heights);

        // 貼圖：草地 / 沙灘 / 道路 / 森林地面
        data.terrainLayers = new[] { grass, beach, dirt, forest };
        int ar = data.alphamapResolution;
        var alpha = new float[ar, ar, 4];
        for (int zi = 0; zi < ar; zi++)
            for (int xi = 0; xi < ar; xi++)
            {
                float x = -TerrainSize * 0.5f + TerrainSize * (xi + 0.5f) / ar;
                float z = -TerrainSize * 0.5f + TerrainSize * (zi + 0.5f) / ar;
                float d = z - ShoreZ(x);
                float jitter = Bump(x, z, 0.22f, 80f, 11f) * 1.6f;

                float hills = Smooth01((Mathf.Abs(x) - 118f) / 25f);
                float sandMix = (1f - Smooth01((d - 15f + jitter * 3f) / 7f)) * Mathf.Max(1f - hills, 1f - Smooth01(d / 6f));
                float road = RoadMask(x, z, jitter);
                // 森林地面只鋪在竹林底下；靠海的山丘留草地，才不會是一座光禿禿的土丘
                float forestMix = Mathf.Max(Smooth01((z - StairStartZ - 8f + jitter * 4f) / 11f), hills * Smooth01((z - 12f) / 30f));
                forestMix = Mathf.Max(forestMix, 1f - Smooth01(ChiefDistance(x, z) / 2f)); // 宅院裡鋪青苔

                float wGrass = (1f - forestMix) * (1f - sandMix) * (1f - road);
                float wForest = forestMix * (1f - sandMix) * (1f - road);
                float wRoad = road * (1f - sandMix * 0.75f);
                float wSand = sandMix * (1f - road * 0.25f);
                float total = wGrass + wForest + wRoad + wSand;
                alpha[zi, xi, 0] = wGrass / total;
                alpha[zi, xi, 1] = wSand / total;
                alpha[zi, xi, 2] = wRoad / total;
                alpha[zi, xi, 3] = wForest / total;
            }
        data.SetAlphamaps(0, 0, alpha);
        CutYardPit(data);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.position = new Vector3(-TerrainSize * 0.5f, TerrainBaseY, -TerrainSize * 0.5f);
        var result = go.GetComponent<Terrain>();
        var terrainMaterial = JapaneseVillageAssets.ShaderMaterial("M_JV_Terrain", "Universal Render Pipeline/Terrain/Lit");
        result.materialTemplate = terrainMaterial;
        result.heightmapPixelError = 4f;
        result.basemapDistance = 220f;
        result.treeDistance = 420f;
        result.treeBillboardDistance = 420f;
        result.treeMaximumFullLODCount = 400;
        result.detailObjectDistance = 85f;
        result.detailObjectDensity = 1f;
        result.drawInstanced = true;
        result.Flush();
        go.isStatic = true;
        return result;
    }

    /// <summary>在習武場東半邊的地形挖一個洞（Terrain Holes），洞裡另外用石牆蓋出深溝</summary>
    static void CutYardPit(TerrainData data)
    {
        int res = data.holesResolution;
        float cell = TerrainSize / res;
        var solid = new bool[res, res];
        int ix0 = int.MaxValue, ix1 = int.MinValue, iz0 = int.MaxValue, iz1 = int.MinValue;
        // 南北兩邊離圍牆留 1 m（格子中心在這個範圍裡才挖），牆邊剩下的窄條另外用隱形牆擋住
        float zMin = YardZ0 + 1f, zMax = YardZ1 - 1f;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = -TerrainSize * 0.5f + (x + 0.5f) * cell, wz = -TerrainSize * 0.5f + (z + 0.5f) * cell;
                bool hole = wx > PitX0 && wx < PitX1 && wz > zMin && wz < zMax;
                solid[z, x] = !hole;
                if (!hole) continue;
                ix0 = Mathf.Min(ix0, x); ix1 = Mathf.Max(ix1, x);
                iz0 = Mathf.Min(iz0, z); iz1 = Mathf.Max(iz1, z);
            }
        data.SetHoles(0, 0, solid);
        pitRect = Rect.MinMaxRect(-TerrainSize * 0.5f + ix0 * cell, -TerrainSize * 0.5f + iz0 * cell,
                                  -TerrainSize * 0.5f + (ix1 + 1) * cell, -TerrainSize * 0.5f + (iz1 + 1) * cell);
    }

    // ---------------------------------------------------------------- 海

    static void BuildWater()
    {
        // 複製一份設定給這個場景：維京村莊的浪比較大，這裡要平靜的海灣；反射和深度只看 Default 層
        string settingsPath = $"{TerrainDir}/WaterSettings_JapaneseVillage.asset";
        string surfacePath = $"{TerrainDir}/WaterSurface_JapaneseVillage.asset";
        AssetDatabase.DeleteAsset(settingsPath);
        AssetDatabase.DeleteAsset(surfacePath);
        AssetDatabase.CopyAsset($"{WaterDir}/WaterSettingsData.asset", settingsPath);
        AssetDatabase.CopyAsset($"{WaterDir}/WaterSurfaceData.asset", surfacePath);

        // 反射用 Cubemap：Planar 要把整座竹林多畫一次，而且 URP 這邊缺它要的第二個 Renderer，每幀都會跳警告
        var settings = AssetDatabase.LoadAssetAtPath<WaterSystem.Data.WaterSettingsData>(settingsPath);
        settings.depthMapCullingMask = 1;
        settings.refType = WaterSystem.Data.ReflectionType.Cubemap;
        settings.cubemapRefType = JapaneseVillageAssets.SkyReflection($"{TerrainDir}/JV_SkyReflection.cubemap",
            new Color(0.30f, 0.47f, 0.74f), new Color(0.74f, 0.83f, 0.92f));
        EditorUtility.SetDirty(settings);

        var surface = AssetDatabase.LoadAssetAtPath<WaterSystem.Data.WaterSurfaceData>(surfacePath);
        surface._waterMaxVisibility = 16f;
        surface._basicWaveSettings.amplitude = 0.22f;
        surface._basicWaveSettings.wavelength = 7f;
        surface._basicWaveSettings.direction = 12f;
        EditorUtility.SetDirty(surface);
        AssetDatabase.SaveAssets();

        var go = PlacePrefab($"{WaterDir}/Water.prefab", null, Vector3.zero, Quaternion.identity);
        var water = go.GetComponent<Water>();
        water.settingsData = settings;
        water.surfaceData = surface;
        PrefabUtility.RecordPrefabInstancePropertyModifications(water);
    }

    // ---------------------------------------------------------------- 房屋

    /// <summary>一種房屋款式：先做成 Prefab（Prefabs/JapaneseVillage/Houses），之後擺很多棟</summary>
    sealed class HouseType
    {
        public GameObject prefab;
        public GardenHouseSpec spec;
        public List<float> doorX = new List<float>();
    }

    static HouseType MakeHouseType(string name, int width, string facade, bool veranda, int seed)
    {
        var spec = new GardenHouseSpec { width = width, depth = HouseDepth, facade = facade, veranda = veranda, seed = seed };
        var type = new HouseType { spec = spec };

        // 門的位置（正面從 +X 往 -X 排）
        float s = 0f;
        foreach (char c in facade)
        {
            float bay = c == 'D' ? 4f : 2f;
            if (c == 'D') type.doorX.Add(width * 0.5f - s - bay * 0.5f);
            s += bay;
        }

        var go = JapaneseVillageHouses.Build(name, spec);
        AddHouseColliders(go, spec);
        type.prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{HousePrefabDir}/{name}.prefab");
        Object.DestroyImmediate(go);
        return type;
    }

    /// <summary>可以走進去的房子：地板、四面牆（門的位置留空）、門楣、屋頂、內柱</summary>
    static void AddHollowHouseColliders(GameObject go, in GardenHouseSpec spec)
    {
        const float thick = 0.3f, doorHalf = 0.92f;
        float floor = JapaneseVillageHouses.Floor, top = JapaneseVillageHouses.WallTop;
        float halfW = spec.width * 0.5f, halfD = spec.depth * 0.5f;
        void Box(Vector3 center, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }
        float wallCenter = (floor + top) * 0.5f, wallH = top - floor;
        float doorTop = floor + 2.13f;

        Box(new Vector3(0f, floor * 0.5f, 0f), new Vector3(spec.width + 0.2f, floor, spec.depth + 0.2f));
        Box(new Vector3(halfW, wallCenter, 0f), new Vector3(thick, wallH, spec.depth));
        Box(new Vector3(-halfW, wallCenter, 0f), new Vector3(thick, wallH, spec.depth));
        foreach (float z in new[] { halfD, -halfD })
        {
            float segment = halfW - doorHalf;
            Box(new Vector3(doorHalf + segment * 0.5f, wallCenter, z), new Vector3(segment, wallH, thick));
            Box(new Vector3(-doorHalf - segment * 0.5f, wallCenter, z), new Vector3(segment, wallH, thick));
            Box(new Vector3(0f, (doorTop + top) * 0.5f, z), new Vector3(doorHalf * 2f, top - doorTop, thick));
        }
        foreach (float x in new[] { -halfW * 0.5f, halfW * 0.5f })
            foreach (float z in new[] { -halfD * 0.5f, halfD * 0.5f })
                Box(new Vector3(x, wallCenter, z), new Vector3(0.3f, wallH, 0.3f));

        var roof = go.AddComponent<MeshCollider>();
        roof.sharedMesh = SaveMesh(JapaneseVillageHouses.RoofColliderMesh(spec, go.name + "_RoofCollider"));
        roof.convex = true;

        if (spec.veranda)
            Box(new Vector3(0f, floor * 0.5f, halfD + JapaneseVillageHouses.VerandaDepth * 0.5f), new Vector3(spec.width + 0.2f, floor, JapaneseVillageHouses.VerandaDepth + 0.1f));
    }

    static void AddHouseColliders(GameObject go, in GardenHouseSpec spec)
    {
        var body = go.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, JapaneseVillageHouses.WallTop * 0.5f, 0f);
        body.size = new Vector3(spec.width + 0.2f, JapaneseVillageHouses.WallTop, spec.depth + 0.2f);
        var roof = go.AddComponent<MeshCollider>();
        roof.sharedMesh = SaveMesh(JapaneseVillageHouses.RoofColliderMesh(spec, go.name + "_RoofCollider"));
        roof.convex = true;
        if (spec.veranda)
        {
            var deck = go.AddComponent<BoxCollider>();
            deck.center = new Vector3(0f, JapaneseVillageHouses.Floor * 0.5f, spec.depth * 0.5f + JapaneseVillageHouses.VerandaDepth * 0.5f);
            deck.size = new Vector3(spec.width + 0.2f, JapaneseVillageHouses.Floor, JapaneseVillageHouses.VerandaDepth + 0.1f);
        }
    }

    static void BuildHouseRows(Transform parent)
    {
        // 寬度都是 4 的倍數；正面：D = 4 m 拉門、W = 2 m 窗、S = 2 m 牆
        var types = new[]
        {
            MakeHouseType("JV_House_12A", 12, "WDSSS", false, 3),
            MakeHouseType("JV_House_12B", 12, "SDWSW", true, 4),
            MakeHouseType("JV_House_16A", 16, "WSDSWSS", false, 5),
            MakeHouseType("JV_House_16B", 16, "DWSWSD", true, 6),
            MakeHouseType("JV_House_16C", 16, "SWWDWSS", false, 7),
            MakeHouseType("JV_House_20A", 20, "WSDWSDSW", true, 8),
            MakeHouseType("JV_House_20B", 20, "SWDSWWDS", false, 9),
            MakeHouseType("JV_House_20C", 20, "WWSDDSWW", true, 10),
        };

        var norenCloths = new[] { JVMat.ClothIndigo, JVMat.ClothRed, JVMat.ClothGreen, JVMat.ClothBrown, JVMat.ClothPurple };
        var noren = new SharedModel[norenCloths.Length];
        for (int i = 0; i < norenCloths.Length; i++)
            noren[i] = new SharedModel($"Noren_{norenCloths[i]}", JapaneseVillageMeshes.Noren(norenCloths[i], 3.5f, 0.8f, 6));
        var jar = new SharedModel("WaterJar", JapaneseVillageMeshes.WaterJar());
        var pile = new SharedModel("WoodPile", JapaneseVillageMeshes.WoodPile());

        var rng = new System.Random(20261105);
        float overhang = JapaneseVillageHouses.GableOverhang;
        for (int row = 0; row < HouseRows.Length; row++)
        {
            float rowZ = HouseRows[row];
            var rowRoot = new GameObject($"Row_{(char)('A' + row)}").transform;
            rowRoot.SetParent(parent, false);

            foreach (int side in new[] { -1, 1 })
            {
                float cursor = HouseStartX + (float)rng.NextDouble();
                if (row == 1 && side < 0)
                {
                    BuildSmithy(rowRoot);
                    cursor = HouseStartX + SmithyWidth + overhang * 2f + 1.2f;
                }
                while (true)
                {
                    var type = types[rng.Next(types.Length)];
                    float w = type.spec.width;

                    // 屋頂也不能蓋到小巷
                    if (cursor - overhang < LaneX + LaneHalfWidth + 0.4f && cursor + w + overhang > LaneX - LaneHalfWidth - 0.4f)
                    {
                        cursor = LaneX + LaneHalfWidth + 0.8f + overhang;
                        continue;
                    }
                    if (cursor + w > HouseRowHalfSpan) break;

                    float x = side * (cursor + w * 0.5f);
                    bool firstEast = row == 1 && side > 0 && cursor < HouseStartX + 1.01f;
                    var door = PlaceHouse(rowRoot, type, new Vector3(x, Ground(x, rowZ), rowZ), rng, noren, jar, pile);
                    if (firstEast) teaHouseDoor = door;
                    cursor += w + overhang * 2f + 1.2f + (float)rng.NextDouble() * 2f;
                }
            }
        }
    }

    // ---------------------------------------------------------------- 鐵匠鋪

    /// <summary>靜態模型（弓）的匯入設定：不要骨架、不要模型自帶的材質和動畫；法線、金屬度貼圖的設定同人物</summary>
    static void EnsurePropImport(string modelPath, string normalPath, string metallicPath)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        if (mi == null) throw new System.IO.FileNotFoundException($"[JapaneseVillage] 找不到模型 {modelPath}");
        bool dirty = false;
        if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
        if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
        if (dirty) mi.SaveAndReimport();
        var ti = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        ti = (TextureImporter)AssetImporter.GetAtPath(metallicPath);
        if (ti != null && ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
    }

    static Material SteelMaterial()
    {
        var mat = JapaneseVillageAssets.ShaderMaterial("M_JV_Steel", "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", new Color(0.74f, 0.76f, 0.8f));
        mat.SetFloat("_Metallic", 0.9f);
        mat.SetFloat("_Smoothness", 0.72f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>武器 Prefab（存在 Resources/Weapons，玩家身上和武器架都用）：模型沿 +Y、握把在原點，Tip 標出刀尖 / 槍尖。kit 裡用 White 的部分換成鋼</summary>
    static GameObject SaveWeaponPrefab(string name, JVMeshKit kit, Material steel, float tipY)
    {
        var go = new GameObject(name);
        var model = Instance("Model", go.transform, kit, Vector3.zero, Quaternion.identity, false);
        var r = model.GetComponent<MeshRenderer>();
        var mats = r.sharedMaterials;
        var white = JapaneseVillageAssets.Get(JVMat.White);
        for (int i = 0; i < mats.Length; i++) if (mats[i] == white) mats[i] = steel;
        r.sharedMaterials = mats;
        var tipGo = new GameObject("Tip");
        tipGo.transform.SetParent(go.transform, false);
        tipGo.transform.localPosition = new Vector3(0f, tipY, 0f);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{WeaponResourceDir}/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>一支箭（沿 +Y、中心在 center）：竹箭桿、鋼箭頭（White，存 Prefab 時換成鋼）、紅白三片箭羽、箭尾</summary>
    static void ArrowKit(JVMeshKit kit, Vector3 center, float scale)
    {
        Vector3 P(float y) => center + new Vector3(0f, y * scale, 0f);
        kit.Cylinder(JVMat.WoodLight, P(-0.42f), P(0.37f), 0.0085f * scale, 0.0085f * scale, 6);
        kit.Cylinder(JVMat.Black, P(-0.44f), P(-0.42f), 0.011f * scale, 0.011f * scale, 6);
        kit.Cylinder(JVMat.Black, P(0.36f), P(0.385f), 0.012f * scale, 0.012f * scale, 6);
        kit.Cylinder(JVMat.White, P(0.385f), P(0.47f), 0.017f * scale, 0.001f, 4);
        for (int v = 0; v < 3; v++)
        {
            var rot = Quaternion.Euler(0f, v * 120f, 0f);
            kit.Box(v == 0 ? JVMat.ClothRed : JVMat.White, center + rot * new Vector3(0.018f * scale, -0.33f * scale, 0f), new Vector3(0.028f * scale, 0.12f * scale, 0.002f * scale), rot);
        }
    }

    /// <summary>小刀、長槍（程式做的）和弓（Tripo 模型）的 Prefab</summary>
    static void BuildWeaponPrefabs()
    {
        JapaneseVillageAssets.EnsureFolder("Assets/_Project/Resources");
        JapaneseVillageAssets.EnsureFolder(WeaponResourceDir);
        var steel = SteelMaterial();

        // 換武器 HUD 的圖示（Art/JapaneseVillage/make_ui_icons.py 產生）：介面用、透明背景、不要 mipmap
        foreach (var icon in new[] { "JV_Icon_Bow", "JV_Icon_Arrow", "JV_Icon_Knife" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath($"Assets/_Project/Resources/UI/{icon}.png");
            if (ti == null) { Debug.LogWarning($"[JapaneseVillage] 找不到圖示 {icon}，請執行 make_ui_icons.py"); continue; }
            if (ti.textureType != TextureImporterType.GUI || !ti.alphaIsTransparency || ti.mipmapEnabled)
            {
                ti.textureType = TextureImporterType.GUI;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
        }

        // 小刀（短刀）：握把 13 cm、刀身 33 cm
        var knife = new JVMeshKit();
        knife.Cylinder(JVMat.Black, new Vector3(0f, -0.13f, 0f), Vector3.zero, 0.017f, 0.016f, 8);
        knife.Cylinder(JVMat.Rope, new Vector3(0f, -0.11f, 0f), new Vector3(0f, -0.02f, 0f), 0.019f, 0.019f, 8);
        knife.Cylinder(JVMat.WoodDark, Vector3.zero, new Vector3(0f, 0.012f, 0f), 0.034f, 0.034f, 10);
        knife.Box(JVMat.White, new Vector3(0f, 0.16f, 0f), new Vector3(0.028f, 0.29f, 0.006f));
        knife.Cylinder(JVMat.White, new Vector3(0f, 0.305f, 0f), new Vector3(0f, 0.345f, 0f), 0.016f, 0.001f, 4);
        knifePrefab = SaveWeaponPrefab("JV_Knife", knife, steel, 0.34f);

        // 長槍：槍桿 2.3 m（握把後面 0.9 m），四稜的槍頭 36 cm
        var spear = new JVMeshKit();
        spear.Cylinder(JVMat.WoodDark, new Vector3(0f, -0.9f, 0f), new Vector3(0f, 1.4f, 0f), 0.022f, 0.02f, 8);
        spear.Cylinder(JVMat.Black, new Vector3(0f, -0.96f, 0f), new Vector3(0f, -0.9f, 0f), 0.026f, 0.026f, 8);
        spear.Cylinder(JVMat.Rope, new Vector3(0f, 1.3f, 0f), new Vector3(0f, 1.42f, 0f), 0.026f, 0.026f, 8);
        spear.Cylinder(JVMat.Black, new Vector3(0f, 1.42f, 0f), new Vector3(0f, 1.47f, 0f), 0.031f, 0.031f, 8);
        spear.Cylinder(JVMat.White, new Vector3(0f, 1.47f, 0f), new Vector3(0f, 1.83f, 0f), 0.034f, 0.002f, 4);
        spearPrefab = SaveWeaponPrefab("JV_Spear", spear, steel, 1.83f);

        // 箭（射出去的、搭在弦上的）：沿 +Y、中心在原點，長 0.9 m：竹箭桿、四稜鋼箭頭、紅白三片箭羽
        var arrow = new JVMeshKit();
        ArrowKit(arrow, Vector3.zero, 1f);
        SaveWeaponPrefab("JV_Arrow", arrow, steel, 0.47f);

        // 箭筒：掛在右後腰，開口朝上（+Y），露出幾支箭的箭羽（Arrows 子物件，箭用完了藏起來）
        var quiverRoot = new GameObject("JV_Quiver");
        var tube = new JVMeshKit();
        tube.Cylinder(JVMat.ClothBrown, new Vector3(0f, -0.3f, 0f), new Vector3(0f, 0.24f, 0f), 0.055f, 0.065f, 10);
        tube.Cylinder(JVMat.Rope, new Vector3(0f, 0.22f, 0f), new Vector3(0f, 0.26f, 0f), 0.07f, 0.07f, 10);
        tube.Cylinder(JVMat.Rope, new Vector3(0f, -0.12f, 0f), new Vector3(0f, -0.09f, 0f), 0.062f, 0.062f, 10);
        tube.Cylinder(JVMat.Black, new Vector3(0f, -0.32f, 0f), new Vector3(0f, -0.3f, 0f), 0.058f, 0.058f, 10);
        Instance("Tube", quiverRoot.transform, tube, Vector3.zero, Quaternion.identity, false);
        var tails = new JVMeshKit();
        for (int i = 0; i < 5; i++)
        {
            float ang = i * 72f * Mathf.Deg2Rad;
            var at = new Vector3(Mathf.Cos(ang) * 0.028f, 0.24f, Mathf.Sin(ang) * 0.028f);
            tails.Cylinder(JVMat.WoodLight, at, at + new Vector3(0f, 0.16f, 0f), 0.008f, 0.008f, 5);
            for (int v = 0; v < 3; v++)
                tails.Box(v == 0 ? JVMat.ClothRed : JVMat.White, at + new Vector3(0f, 0.11f, 0f), new Vector3(0.002f, 0.1f, 0.03f), Quaternion.Euler(0f, i * 72f + v * 120f, 0f) * Quaternion.Euler(0f, 0f, 0f));
        }
        Instance("Arrows", quiverRoot.transform, tails, Vector3.zero, Quaternion.identity, false);
        PrefabUtility.SaveAsPrefabAsset(quiverRoot, $"{WeaponResourceDir}/JV_Quiver.prefab");
        Object.DestroyImmediate(quiverRoot);

        // 弓：Tripo 模型，最長的方向轉成 +Y、長 PlayerLoadout.BowPrefabLength、中心在原點
        EnsurePropImport(BowModel, "Assets/_Project/Models/JapaneseVillage/Bow/T_JV_Bow_Normal.png", "Assets/_Project/Models/JapaneseVillage/Bow/T_JV_Bow_MetallicSmooth.png");
        var bowRoot = new GameObject("JV_Bow");
        var bow = PlacePrefab(BowModel, bowRoot.transform, Vector3.zero, Quaternion.identity);
        bow.name = "Model";
        var bowMat = TripoMaterial("Bow");
        foreach (var r in bow.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = bowMat;
            r.sharedMaterials = mats;
        }
        var bb = JVGarden.WorldBounds(bow);
        Vector3 raw = bb.size;
        Vector3 longest = raw.x >= raw.y && raw.x >= raw.z ? Vector3.right : raw.y >= raw.z ? Vector3.up : Vector3.forward;
        bow.transform.rotation = Quaternion.FromToRotation(longest, Vector3.up) * bow.transform.rotation;
        bb = JVGarden.WorldBounds(bow);
        bow.transform.localScale *= PlayerLoadout.BowPrefabLength / Mathf.Max(0.01f, bb.size.y);
        bb = JVGarden.WorldBounds(bow);
        bow.transform.position -= bb.center;
        bb = JVGarden.WorldBounds(bow);
        Debug.Log($"[JapaneseVillage] 弓：原始大小 {raw}，最長的方向 {longest}，做成 {bb.size}");
        bowPrefab = PrefabUtility.SaveAsPrefabAsset(bowRoot, $"{WeaponResourceDir}/JV_Bow.prefab");
        Object.DestroyImmediate(bowRoot);
    }

    /// <summary>
    /// 鐵匠鋪：16 m 寬、正面中間是開著的大門（看得到裡面的爐子），門口兩側是武器架、旁邊一座鐵砧。
    /// 架上擺的是玩家沒帶在身上的武器（WeaponRackDisplay）；女鐵匠站在門口（PlaceNpcs 放）。
    /// </summary>
    static void BuildSmithy(Transform parent)
    {
        BuildWeaponPrefabs();

        var spec = new GardenHouseSpec { width = SmithyWidth, depth = HouseDepth, facade = "SWSOSWS", veranda = false, hollow = true, seed = 77 };
        var go = JapaneseVillageHouses.Build("Smithy", spec);
        AddHollowHouseColliders(go, spec);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(SmithyX, Ground(SmithyX, SmithyZ), SmithyZ), Quaternion.Euler(0f, 180f, 0f));
        footprints.Add(new Rect(SmithyX - SmithyWidth * 0.5f - 1.5f, SmithyFrontZ - 5f, SmithyWidth + 3f, HouseDepth + 6.5f)); // 含門口的空地
        var t = go.transform;
        float frontZ = spec.depth * 0.5f;

        var noren = new SharedModel("Noren_Smithy", JapaneseVillageMeshes.Noren(JVMat.ClothIndigo, 3.5f, 0.8f, 6));
        noren.Place("Noren", t, t.TransformPoint(new Vector3(0f, JapaneseVillageHouses.Floor + 2.12f, frontZ + 0.12f)), t.rotation);
        foreach (float sx in new[] { -1f, 1f })
            JVGarden.Place("Lantern_MediumHanging_01", t, new Vector3(sx * 2.6f, JapaneseVillageHouses.WallTop - 0.1f, frontZ + 1.05f), 0f);

        // 爐子：從大門看進去的正後方，炭火發光
        var forge = new JVMeshKit();
        forge.Box(JVMat.Stone, new Vector3(0f, 0.45f, 0f), new Vector3(1.7f, 0.9f, 1.1f));
        forge.Box(JVMat.LanternGlow, new Vector3(0f, 0.93f, 0f), new Vector3(1.2f, 0.07f, 0.7f));
        forge.Box(JVMat.Stone, new Vector3(0f, 2.05f, -0.15f), new Vector3(1.3f, 0.16f, 0.8f));
        forge.Box(JVMat.Stone, new Vector3(0f, 2.75f, -0.3f), new Vector3(0.55f, 1.3f, 0.5f));
        forge.Box(JVMat.WoodDark, new Vector3(1.05f, 0.35f, 0f), new Vector3(0.35f, 0.7f, 0.6f)); // 風箱
        var forgeGo = Instance("Forge", t, forge, t.TransformPoint(new Vector3(0f, JapaneseVillageHouses.Floor, -frontZ + 1.6f)), t.rotation);
        var fire = new GameObject("ForgeFire");
        fire.transform.SetParent(forgeGo.transform, false);
        fire.transform.localPosition = new Vector3(0f, 1.2f, 0.2f);
        var light = fire.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.5f, 0.18f);
        light.range = 7f;
        light.intensity = 2.2f;
        light.shadows = LightShadows.None;

        // 門口的東西用世界座標擺（門口中心 = (SmithyX, SmithyFrontZ)，往南是街道）
        Vector3 Front(float dx, float dz) => new Vector3(SmithyX + dx, Ground(SmithyX + dx, SmithyFrontZ - dz), SmithyFrontZ - dz);
        var facing = Quaternion.Euler(0f, 180f, 0f); // 架子的正面（+Z）朝街道

        // 鐵砧：樹墩 + 鐵砧 + 放在上面的鐵鎚
        var anvil = new JVMeshKit();
        anvil.Cylinder(JVMat.WoodDark, Vector3.zero, new Vector3(0f, 0.5f, 0f), 0.3f, 0.27f, 10);
        anvil.Box(JVMat.Black, new Vector3(0f, 0.6f, 0f), new Vector3(0.3f, 0.2f, 0.22f));
        anvil.Box(JVMat.Black, new Vector3(0f, 0.75f, 0f), new Vector3(0.6f, 0.12f, 0.26f));
        anvil.Cylinder(JVMat.Black, new Vector3(0.3f, 0.75f, 0f), new Vector3(0.58f, 0.76f, 0f), 0.07f, 0.01f, 8);
        anvil.Cylinder(JVMat.WoodLight, new Vector3(-0.2f, 0.84f, 0.05f), new Vector3(0.12f, 0.84f, 0.2f), 0.018f, 0.018f, 6);
        anvil.Box(JVMat.Black, new Vector3(-0.22f, 0.86f, 0.04f), new Vector3(0.09f, 0.07f, 0.14f));
        var anvilGo = Instance("Anvil", t, anvil, Front(-2.4f, 2.9f), facing * Quaternion.Euler(0f, 20f, 0f));
        anvilGo.AddComponent<BoxCollider>().size = new Vector3(0.7f, 1.6f, 0.7f);

        // 武器架
        GameObject Stand(string name, JVMeshKit kit, Vector3 at, Vector3 colliderSize)
        {
            var stand = Instance(name, t, kit, at, facing);
            var col = stand.AddComponent<BoxCollider>();
            col.size = colliderSize;
            col.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            return stand;
        }
        GameObject Display(string name, GameObject prefab, Transform stand, Vector3 local, Quaternion rot)
        {
            var go2 = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stand);
            go2.name = name;
            go2.transform.SetLocalPositionAndRotation(local, rot);
            return go2;
        }

        // 大刀架：兩根立柱、兩對托架，刀橫放在上面
        var swordKit = new JVMeshKit();
        swordKit.Box(JVMat.WoodDark, new Vector3(0f, 0.04f, 0f), new Vector3(1.6f, 0.08f, 0.42f));
        foreach (float x in new[] { -0.6f, 0.6f })
        {
            swordKit.Box(JVMat.Lacquer, new Vector3(x, 0.55f, 0f), new Vector3(0.09f, 1.0f, 0.12f));
            foreach (float y in new[] { 0.72f, 1.0f })
                swordKit.Box(JVMat.Lacquer, new Vector3(x, y, 0.1f), new Vector3(0.09f, 0.06f, 0.2f));
        }
        var swordStand = Stand("Rack_Greatsword", swordKit, Front(3.8f, 1.1f), new Vector3(1.7f, 1.2f, 0.5f));
        var saber = PlacePrefab(SaberModel, swordStand.transform, swordStand.transform.position, swordStand.transform.rotation);
        saber.name = "Display_Greatsword";
        var saberMat = AssetDatabase.LoadAssetAtPath<Material>(SaberMaterialPath);
        if (saberMat != null)
            foreach (var r in saber.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = saberMat;
                r.sharedMaterials = mats;
            }
        FitAlong(saber, swordStand.transform.right, 1.45f, swordStand.transform.TransformPoint(new Vector3(0f, 1.0f, 0.12f)));
        var sb2 = JVGarden.WorldBounds(saber);
        saber.transform.position += Vector3.up * (swordStand.transform.position.y + 1.03f - sb2.min.y); // 刀身的下緣貼著托架（托架上緣 1.03 m）

        // 弓與小刀架：立柱 + 兩根橫桿，弓直立掛著、小刀橫放在下面的橫桿
        var bowKit = new JVMeshKit();
        bowKit.Box(JVMat.WoodDark, new Vector3(0f, 0.04f, 0f), new Vector3(1.1f, 0.08f, 0.42f));
        foreach (float x in new[] { -0.48f, 0.48f }) bowKit.Box(JVMat.Lacquer, new Vector3(x, 0.85f, 0f), new Vector3(0.08f, 1.7f, 0.1f));
        bowKit.Box(JVMat.Lacquer, new Vector3(0f, 1.62f, 0f), new Vector3(1.04f, 0.07f, 0.1f));
        bowKit.Box(JVMat.Lacquer, new Vector3(0f, 0.62f, 0.04f), new Vector3(1.04f, 0.06f, 0.12f));
        var bowStand = Stand("Rack_BowKnife", bowKit, Front(6.3f, 1.1f), new Vector3(1.2f, 1.8f, 0.5f));
        var bowSet = new GameObject("Display_BowKnife");
        bowSet.transform.SetParent(bowStand.transform, false);
        Display("Bow", bowPrefab, bowSet.transform, new Vector3(-0.18f, 0.98f, 0.12f), Quaternion.identity);
        Display("Knife", knifePrefab, bowSet.transform, new Vector3(0.22f, 0.69f, 0.1f), Quaternion.Euler(0f, 0f, -90f));

        // 長槍架：兩根立柱、上下兩道橫桿，槍直立
        var spearKit = new JVMeshKit();
        spearKit.Box(JVMat.WoodDark, new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.08f, 0.42f));
        foreach (float x in new[] { -0.36f, 0.36f }) spearKit.Box(JVMat.Lacquer, new Vector3(x, 1.2f, 0f), new Vector3(0.08f, 2.4f, 0.1f));
        foreach (float y in new[] { 0.9f, 2.0f }) spearKit.Box(JVMat.Lacquer, new Vector3(0f, y, 0.06f), new Vector3(0.8f, 0.06f, 0.08f));
        var spearStand = Stand("Rack_Spear", spearKit, Front(-4.6f, 1.1f), new Vector3(1f, 2.5f, 0.5f));
        var spearDisplay = Display("Display_Spear", spearPrefab, spearStand.transform, new Vector3(0f, 1.02f, 0.13f), Quaternion.identity);

        void Slot(GameObject stand, WeaponLoadout loadout, string name, float cardHeight, params string[] info)
        {
            var slot = stand.AddComponent<WeaponRackSlot>();
            slot.loadout = loadout;
            slot.displayName = name;
            slot.info = info;
            slot.cardHeight = cardHeight;
        }
        Slot(swordStand, WeaponLoadout.Greatsword, "大刀", 1.9f, "一刀橫掃一大片，範圍最大", "可以舉盾擋招（右鍵）");
        Slot(bowStand, WeaponLoadout.BowKnife, "弓與小刀", 2.2f, $"右鍵瞄準、左鍵放箭，箭 {PlayerLoadout.MaxArrows} 支", "射完換小刀：距離短、出手快", "拿弓時不能舉盾");
        Slot(spearStand, WeaponLoadout.Spear, "長槍", 2.8f, "用戳的，打得最遠", "左右窄，貼太近打不到");

        var rack = go.AddComponent<WeaponRackDisplay>();
        rack.greatsword = saber;
        rack.bowKnife = bowSet;
        rack.spear = spearDisplay;
        Debug.Log($"[JapaneseVillage] 鐵匠鋪：x {SmithyX:F1}、門口 z {SmithyFrontZ:F1}；武器架 3 座");
    }

    /// <summary>把模型最長的方向轉到 axis、縮放成 length 長、中心移到 center（照世界座標的外框）</summary>
    static void FitAlong(GameObject go, Vector3 axis, float length, Vector3 center)
    {
        var b = JVGarden.WorldBounds(go);
        Vector3 size = b.size;
        Vector3 longest = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        go.transform.rotation = Quaternion.FromToRotation(longest, axis.normalized) * go.transform.rotation;
        b = JVGarden.WorldBounds(go);
        float now = Vector3.Scale(b.size, new Vector3(Mathf.Abs(axis.normalized.x), Mathf.Abs(axis.normalized.y), Mathf.Abs(axis.normalized.z))).magnitude;
        go.transform.localScale *= length / Mathf.Max(0.01f, now);
        b = JVGarden.WorldBounds(go);
        go.transform.position += center - b.center;
    }

    /// <summary>放一棟房子，回傳正門口（門外 0.6 m）的地面位置</summary>
    static Vector3 PlaceHouse(Transform parent, HouseType type, Vector3 position, System.Random rng, SharedModel[] noren, SharedModel jar, SharedModel pile)
    {
        // 模型正面是 +Z，轉 180 度朝南（朝海）
        var spec = type.spec;
        var rotation = Quaternion.Euler(0f, 180f, 0f);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(type.prefab, parent);
        go.name = $"House_{footprints.Count:00}";
        go.transform.SetPositionAndRotation(position, rotation);
        footprints.Add(new Rect(position.x - spec.width * 0.5f - 1.5f, position.z - spec.depth * 0.5f - 2.4f, spec.width + 3f, spec.depth + 3.9f));

        var t = go.transform;
        float frontZ = spec.depth * 0.5f;
        float door = type.doorX[rng.Next(type.doorX.Count)];

        // 暖簾（大約一半的房子是店家）
        if (rng.NextDouble() < 0.55)
        {
            var cloth = noren[rng.Next(noren.Length)];
            cloth.Place("Noren", t, t.TransformPoint(new Vector3(door, JapaneseVillageHouses.Floor + 2.12f, frontZ + 0.12f)), rotation);
        }

        // 屋簷下的吊燈籠、緣側上的紙燈籠
        if (rng.NextDouble() < 0.5)
            foreach (float s in new[] { -1f, 1f })
            {
                var lamp = JVGarden.Place("Lantern_MediumHanging_01", t, new Vector3(door + s * 2.5f, JapaneseVillageHouses.WallTop - 0.1f, frontZ + 1.05f), 0f);
                lamp.transform.localScale *= 0.85f;
            }
        else if (spec.veranda)
            JVGarden.Place("Lantern_Paper_01", t, new Vector3(door + 2.5f, JapaneseVillageHouses.Floor + 0.03f, frontZ + 0.5f), 0f);

        // 石階：門前放一塊踏石
        if (!spec.veranda || rng.NextDouble() < 0.7)
        {
            var world = t.TransformPoint(new Vector3(door, 0f, frontZ + (spec.veranda ? 1.6f : 1.2f)));
            JVGarden.Place("StoneStep_01", t, t.InverseTransformPoint(new Vector3(world.x, Ground(world.x, world.z) - 0.02f, world.z)), 90f);
        }

        // 屋角的水缸、柴堆、灌木
        float propX = (rng.Next(2) == 0 ? -1f : 1f) * (spec.width * 0.5f + 1.9f);
        double roll = rng.NextDouble();
        if (roll < 0.35)
        {
            var world = t.TransformPoint(new Vector3(propX, 0f, frontZ * 0.5f));
            jar.Place("WaterJar", t, new Vector3(world.x, Ground(world.x, world.z) - 0.02f, world.z), rotation);
        }
        else if (roll < 0.7)
        {
            var world = t.TransformPoint(new Vector3(propX, 0f, -frontZ * 0.5f));
            pile.Place("WoodPile", t, new Vector3(world.x, Ground(world.x, world.z), world.z), rotation);
        }
        foreach (float s in new[] { -1f, 1f })
        {
            if (rng.NextDouble() > 0.6) continue;
            string shrub = rng.Next(3) switch { 0 => "Holly_Shrub_01", 1 => "Liriope_Cluster", _ => "Japanese_Spurge_Cluster" };
            var world = t.TransformPoint(new Vector3(s * (spec.width * 0.5f - 0.6f), 0f, frontZ + 2.1f));
            JVGarden.Place(shrub, t, t.InverseTransformPoint(new Vector3(world.x, Ground(world.x, world.z), world.z)), (float)rng.NextDouble() * 360f);
        }
        var doorWorld = t.TransformPoint(new Vector3(door, 0f, frontZ + (spec.veranda ? JapaneseVillageHouses.VerandaDepth : 0f) + 0.6f));
        return new Vector3(doorWorld.x, Ground(doorWorld.x, doorWorld.z), doorWorld.z);
    }

    // ---------------------------------------------------------------- 村長宅院

    /// <summary>
    /// 村長家：三排房屋東側盡頭、全村最大的一棟，放大 1.25 倍（30 × 15 m），有緣側、門廊、圍牆、大門和庭園。
    /// 大門朝西，正對房屋盡頭的南北向道路。
    /// </summary>
    static void BuildChiefCompound(Transform parent)
    {
        var root = new GameObject("ChiefCompound").transform;
        root.SetParent(parent, false);
        float y = ChiefGroundY;
        footprints.Add(new Rect(ChiefX0 - 1f, ChiefZ0 - 1f, ChiefX1 - ChiefX0 + 2f, ChiefZ1 - ChiefZ0 + 2f));

        // 主屋：道場主殿的 Prefab（原點在地面中心、正面朝 -Z），轉 90 度讓正面朝西、對著大門。碰撞和室內都沿用原本的
        AssetDatabase.DeleteAsset($"{HousePrefabDir}/JV_ChiefHouse.prefab"); // 舊版用 Garden 模組拼的村長家
        var main = PlacePrefab(DojoHousePrefab, root, new Vector3(ChiefHouseX, y, ChiefAxisZ), Quaternion.Euler(0f, 90f, 0f), ChiefHouseScale);
        main.name = "ChiefHouse";
        var houseBounds = JVGarden.WorldBounds(main);
        main.transform.position += new Vector3(ChiefHouseX - houseBounds.center.x, 0f, ChiefAxisZ - houseBounds.center.z);
        houseBounds = JVGarden.WorldBounds(main);
        float porchFrontX = houseBounds.min.x;
        Debug.Log($"[JapaneseVillage] 村長家（道場主殿 ×{ChiefHouseScale}）外框 {houseBounds.size}，x {houseBounds.min.x:F1}～{houseBounds.max.x:F1}，最低 y {houseBounds.min.y - y:F2}");

        BuildChiefWall(root, y);

        // 庭園：飛石、石燈籠、大櫻花、灌木、岩石（官方 Garden 那棵大櫻花只放這裡，它比較重）
        var rng = new System.Random(77);
        string[] stones = { "Stone_Walkway_01", "Stone_Walkway_02", "Stone_Walkway_03", "Stone_Walkway_04" };
        for (float x = ChiefX0 + 1f; x < porchFrontX - 3.8f; x += 0.95f)
        {
            float z = ChiefAxisZ + (float)rng.NextDouble() * 0.5f - 0.25f;
            var stone = JVGarden.Place(stones[rng.Next(stones.Length)], root, new Vector3(x, y - 0.03f, z), (float)rng.NextDouble() * 360f);
            stone.transform.localScale *= 1.25f;
        }

        var cherry = JVGarden.Place("Cherry_tree_01", root, new Vector3(107.5f, y, ChiefAxisZ - 13f), 40f);
        cherry.transform.localScale *= 0.85f;
        var cherryTrunk = new GameObject("TrunkCollider").AddComponent<CapsuleCollider>();
        cherryTrunk.transform.SetParent(root, false);
        cherryTrunk.transform.position = new Vector3(107.5f, y + 2f, ChiefAxisZ - 13f);
        cherryTrunk.height = 4f;
        cherryTrunk.radius = 0.6f;

        foreach (var (x, z, prefabName, s, yaw) in new[]
        {
            (106f, ChiefAxisZ + 11f, "Stone_04", 1.5f, 30f), (109f, ChiefAxisZ + 14f, "LandscapeStone_01", 1f, 110f), (104f, ChiefAxisZ + 15f, "Stone_03", 1.8f, 200f),
            (110.5f, ChiefAxisZ + 9f, "Holly_Shrub_01", 1.2f, 15f), (102.5f, ChiefAxisZ + 19f, "Holly_Shrub_01", 1.4f, 80f), (107f, ChiefAxisZ + 20f, "Liriope_Cluster", 1f, 150f),
            (111f, ChiefAxisZ - 18f, "Stone_04", 1.2f, 70f), (103f, ChiefAxisZ - 18f, "Cherry_shrub_01", 1.1f, 20f), (112f, ChiefAxisZ - 8f, "Holly_Shrub_01", 1.1f, 240f),
            (103f, ChiefAxisZ - 8f, "Liriope_Cluster", 1f, 300f), (109f, ChiefAxisZ - 21f, "Japanese_Spurge_Cluster", 1.2f, 45f), (103f, ChiefAxisZ + 8f, "Japanese_Spurge_Cluster", 1f, 110f),
        })
        {
            var go = JVGarden.Place(prefabName, root, new Vector3(x, y + (prefabName.StartsWith("Stone") ? 0.15f : prefabName.StartsWith("Landscape") ? 0.9f : 0f), z), yaw);
            go.transform.localScale *= s;
            if (prefabName.StartsWith("Stone") || prefabName.StartsWith("Landscape"))
            {
                var c = go.AddComponent<SphereCollider>();
                c.radius = prefabName == "Stone_03" ? 0.28f : 0.75f;
            }
        }
        // 大櫻花旁的長椅：村長的日常會來這裡坐（NpcRoutine）
        var elderBench = new SharedModel("ElderBench", JapaneseVillageMeshes.TeaBench(1.7f));
        elderBenchPos = new Vector3(109.5f, y, ChiefAxisZ - 10.5f);
        elderBench.Place("ElderBench", root, elderBenchPos, Quaternion.identity).AddComponent<BoxCollider>(); // 自動貼合長椅的外框

        foreach (float s in new[] { -1f, 1f })
            PlaceStoneLantern(root, ChiefX0 + 6f, ChiefAxisZ + s * 2.6f);
        PlaceStoneLantern(root, porchFrontX - 2.5f, ChiefAxisZ + 4.2f);
        PlaceStoneLantern(root, porchFrontX - 2.5f, ChiefAxisZ - 4.2f);
    }

    /// <summary>圍牆：Fence_400 一段 4 m，四個轉角 Fence_400_02，西側正中間留給大門</summary>
    static void BuildChiefWall(Transform root, float groundY)
    {
        var wall = new GameObject("Wall").transform;
        wall.SetParent(root, false);
        float y = groundY - 0.2f;
        float x0 = ChiefX0, x1 = ChiefX1, z0 = ChiefZ0, z1 = ChiefZ1;

        JVGarden.Place("Fence_400_02", wall, new Vector3(x0 + 2f, y, z0), 0f);
        JVGarden.Place("Fence_400_02", wall, new Vector3(x0, y, z1 - 2f), 90f);
        JVGarden.Place("Fence_400_02", wall, new Vector3(x1 - 2f, y, z1), 180f);
        JVGarden.Place("Fence_400_02", wall, new Vector3(x1, y, z0 + 2f), -90f);

        for (float x = x0 + 6f; x <= x1 - 2f + 0.01f; x += 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x, y, z0), 0f);
        for (float x = x0 + 2f; x <= x1 - 6f + 0.01f; x += 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x, y, z1), 180f);
        // 東側（後院）：同西側，中間留給通往道場的大門
        for (float z = z1 - 2f; z - 4f >= ChiefAxisZ + ChiefGateHalf - 0.01f; z -= 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x1, y, z), -90f);
        for (float z = ChiefAxisZ - ChiefGateHalf; z - 4f >= z0 + 2f - 0.01f; z -= 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x1, y, z), -90f);

        // 西側：從南北轉角往大門排，中間留 2 × ChiefGateHalf
        for (float z = z0 + 2f; z + 4f <= ChiefAxisZ - ChiefGateHalf + 0.01f; z += 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x0, y, z), 90f);
        for (float z = ChiefAxisZ + ChiefGateHalf; z + 4f <= z1 - 2f + 0.01f; z += 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x0, y, z), 90f);

        // 大門（照官方場景：原點偏離門中心 2.885 m、比圍牆線往內 0.46 m）；後院的門是同一座轉 180 度
        JVGarden.Place("Gate_400_01", wall, new Vector3(x0 + 0.46f, groundY + 0.42f, ChiefAxisZ - 2.885f), 90f);
        var backGate = JVGarden.Place("Gate_400_01", wall, new Vector3(x1 - 0.46f, groundY + 0.42f, ChiefAxisZ + 2.885f), -90f);
        BuildDojoGatePortal(root, backGate, x1, groundY);

        // 碰撞：牆一道一個 Box，大門留空
        void Box(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(wall, false);
            go.transform.position = center;
            go.isStatic = true;
            go.AddComponent<BoxCollider>().size = size;
        }
        float h = 4f, cy = groundY + h * 0.5f;
        Box("Wall_South", new Vector3((x0 + x1) * 0.5f, cy, z0), new Vector3(x1 - x0, h, 0.5f));
        Box("Wall_North", new Vector3((x0 + x1) * 0.5f, cy, z1), new Vector3(x1 - x0, h, 0.5f));
        float ez0 = (z0 + ChiefAxisZ - ChiefGateHalf) * 0.5f, ez1 = (z1 + ChiefAxisZ + ChiefGateHalf) * 0.5f;
        Box("Wall_East_S", new Vector3(x1, cy, ez0), new Vector3(0.5f, h, ChiefAxisZ - ChiefGateHalf - z0));
        Box("Wall_East_N", new Vector3(x1, cy, ez1), new Vector3(0.5f, h, z1 - ChiefAxisZ - ChiefGateHalf));
        Box("BackGate_Blocker", new Vector3(x1, cy, ChiefAxisZ), new Vector3(0.5f, h, ChiefGateHalf * 2f)); // 門開了也是傳送門，不能走出去
        float wz0 = (z0 + ChiefAxisZ - ChiefGateHalf) * 0.5f, wz1 = (z1 + ChiefAxisZ + ChiefGateHalf) * 0.5f;
        Box("Wall_West_S", new Vector3(x0, cy, wz0), new Vector3(0.5f, h, ChiefAxisZ - ChiefGateHalf - z0));
        Box("Wall_West_N", new Vector3(x0, cy, wz1), new Vector3(0.5f, h, z1 - ChiefAxisZ - ChiefGateHalf));
    }

    /// <summary>
    /// 後院大門（DojoGate）：僧侶開鎖之前推不開；開鎖後玩家把兩扇門推開，門洞裡亮起墨色漩渦，按 E 進入，
    /// 換場景時輪播四幅古畫。
    /// </summary>
    static void BuildDojoGatePortal(Transform root, GameObject gate, float wallX, float groundY)
    {
        // 門扇：GateDoor_400_01 是一片 2 m 寬的門板，左右各一片（另一片轉 180 度），貼地放進 4 m 的門洞。
        // 每片掛在外側邊緣的門軸（Hinge）底下，推開時往宅院外面（東邊）轉 95 度
        var doors = new GameObject("BackGate_Doors");
        doors.transform.SetParent(root, false);
        var hinges = new List<Transform>();
        var angles = new List<float>();
        foreach (var (side, yaw) in new[] { (-1f, -90f), (1f, 90f) })
        {
            var leaf = JVGarden.Place("GateDoor_400_01", doors.transform, Vector3.zero, yaw);
            var db = JVGarden.WorldBounds(leaf);
            leaf.transform.position += new Vector3(wallX - db.center.x, groundY - db.min.y, ChiefAxisZ + side * db.size.z * 0.5f - db.center.z);
            var hinge = new GameObject(side < 0f ? "Hinge_South" : "Hinge_North").transform;
            hinge.SetParent(doors.transform, false);
            hinge.position = new Vector3(wallX, groundY, ChiefAxisZ + side * db.size.z);
            leaf.transform.SetParent(hinge, true);
            hinges.Add(hinge);
            angles.Add(side > 0f ? -95f : 95f);
        }
        JVGarden.ClearStatic(doors);

        var portalGo = new GameObject("Portal_Dojo");
        portalGo.transform.SetParent(root, false);
        portalGo.transform.SetPositionAndRotation(new Vector3(wallX + 0.3f, groundY, ChiefAxisZ), Quaternion.Euler(0f, -90f, 0f)); // 在門板後面、面向宅院裡面：門推開才看得到
        // 光面：墨色漩渦貼圖（generate_paintings.py 產生）＋半透明、雙面的 Unlit 材質
        var vortex = JapaneseVillageAssets.ShaderMaterial("M_JV_DojoGateVortex", "Universal Render Pipeline/Unlit");
        vortex.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{JapaneseVillageAssets.MatDir}/T_JV_DojoGateVortex.png"));
        vortex.SetFloat("_Surface", 1f);
        vortex.SetFloat("_Blend", 0f);
        vortex.SetFloat("_Cull", 0f);
        vortex.SetFloat("_ZWrite", 0f);
        vortex.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        vortex.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        vortex.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        vortex.SetOverrideTag("RenderType", "Transparent");
        vortex.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(vortex);

        var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.name = "Surface";
        Object.DestroyImmediate(surface.GetComponent<Collider>());
        surface.transform.SetParent(portalGo.transform, false);
        surface.transform.localPosition = new Vector3(0f, 1.75f, 0f);
        surface.transform.localScale = new Vector3(3.9f, 3.4f, 0.02f);
        var surfaceRenderer = surface.GetComponent<MeshRenderer>();
        surfaceRenderer.sharedMaterial = vortex;
        surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(portalGo.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.2f, 1.2f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 9f;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;

        var dojoGate = portalGo.AddComponent<DojoGate>();
        dojoGate.sceneName = FinalBattleScene;
        dojoGate.destinationTitle = FinalBattleTitle;
        dojoGate.doorHinges = hinges.ToArray();
        dojoGate.openAngles = angles.ToArray();
        dojoGate.pushDirection = Vector3.right;
        dojoGate.surface = surfaceRenderer;
        dojoGate.glowLight = light;
        dojoGate.lightIntensity = 2.5f;
        dojoGate.interactDistance = 4.5f;
        // 換場景時輪播四幅古畫
        dojoGate.slides = Paintings.Select(pt => AssetDatabase.LoadAssetAtPath<Texture2D>($"{PaintingDir}/{pt.file}.png")).ToArray();
        trialGate = dojoGate;
    }

    // ---------------------------------------------------------------- 街道

    static void BuildStreetDetails(Transform parent)
    {
        var details = new GameObject("StreetDetails").transform;
        details.SetParent(parent, false);

        // 主街中央的飛石（Garden 的石板）
        string[] walkway = { "Stone_Walkway_01", "Stone_Walkway_02", "Stone_Walkway_03", "Stone_Walkway_04" };
        var rng = new System.Random(66);
        for (float z = RoadSouth + 1f; z < StairStartZ - 0.6f; z += 1.05f)
            foreach (float side in new[] { -0.62f, 0.62f })
            {
                float x = side + (float)rng.NextDouble() * 0.2f - 0.1f;
                var stone = JVGarden.Place(walkway[rng.Next(walkway.Length)], details, new Vector3(x, Ground(x, z) - 0.03f, z), (float)rng.NextDouble() * 360f);
                stone.transform.localScale *= 1.15f;
            }

        // 主街兩旁、路口的石燈籠
        foreach (float z in new[] { -33f, -12.5f, 15.5f, 43.5f })
            foreach (float side in new[] { -1f, 1f })
                PlaceStoneLantern(details, side * 5.2f, z);
        foreach (float z in new[] { -33f, -12.5f, 15.5f, 43.5f })
            PlaceStoneLantern(details, PlazaX - 3.8f, z);

        // 茶屋（第二排主街東側）：長椅 + 大紅傘
        var bench = new SharedModel("TeaBench", JapaneseVillageMeshes.TeaBench());
        bench.Place("TeaBench_1", details, new Vector3(9.5f, Ground(9.5f, TeaZ), TeaZ), Quaternion.identity);
        bench.Place("TeaBench_2", details, new Vector3(13.5f, Ground(13.5f, TeaZ + 0.2f), TeaZ + 0.2f), Quaternion.Euler(0f, 6f, 0f));
        Instance("TeaParasol", details, JapaneseVillageMeshes.Parasol(), new Vector3(11.5f, Ground(11.5f, TeaZ + 0.1f), TeaZ + 0.1f), Quaternion.identity);
    }

    const float TeaZ = -4.4f;

    static void PlaceStoneLantern(Transform parent, float x, float z)
    {
        var go = JVGarden.Place("LanternStone_01", parent, new Vector3(x, Ground(x, z) + 0.03f, z), Mathf.Abs(x * 13f + z * 7f) % 90f);
        go.transform.localScale *= 1.25f;
        go.name = $"StoneLantern_{x:0}_{z:0}";
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.7f, 0f);
        capsule.height = 1.5f;
        capsule.radius = 0.3f;
    }

    // ---------------------------------------------------------------- 海岸

    static void BuildCoast(Transform parent)
    {
        // 棧橋：從沙灘上橋面高度的地方開始往海延伸
        float pierStartZ = RoadSouth;
        for (float z = RoadSouth; z > -110f; z -= 0.25f)
            if (Ground(PierX, z) <= PierDeckY) { pierStartZ = z; break; }
        var pierGo = new GameObject("Pier");
        pierGo.transform.SetParent(parent, false);
        pierGo.transform.position = new Vector3(PierX, PierDeckY, pierStartZ);
        pierGo.isStatic = true;
        var pier = pierGo.transform;
        // Garden 的木平台：原寬 2 m，放寬到棧橋的寬度；每 4 m 一對木樁插進海底
        float plankStretch = PierWidth / 2.03f;
        for (float z = 0f; z < PierLength - 0.01f; z += 4f)
        {
            var plank = JVGarden.Place("Wooden_Platform_400x200_01", pier, new Vector3(PierWidth * 0.5f, 0f, -z), -90f);
            plank.transform.localScale = Vector3.Scale(plank.transform.localScale, new Vector3(1f, 1f, plankStretch));
            foreach (float side in new[] { -1f, 1f })
            {
                var pile = JVGarden.Place("BeamWooden_13x16x380_03", pier, new Vector3(side * (PierWidth * 0.5f + 0.1f), -13.1f, -z - 2f), 0f);
                pile.transform.localScale = Vector3.Scale(pile.transform.localScale, new Vector3(1f, 3.6f, 1f));
            }
        }
        var deck = pierGo.AddComponent<BoxCollider>();
        deck.center = new Vector3(0f, -0.15f, -PierLength * 0.5f);
        deck.size = new Vector3(PierWidth + 0.15f, 0.3f, PierLength);
        PierEndZ = pierStartZ - PierLength;
        PierStartZ = pierStartZ;

        // 繫在棧橋邊的漁船（會輕輕搖）
        var moored = new SharedModel("Boat_Moored", JapaneseVillageMeshes.Boat());
        foreach (var (x, z, yaw) in new[]
        {
            (PierX + 3.6f, pierStartZ - 21f, 8f),
            (PierX - 3.8f, pierStartZ - 31f, 172f),
            (PierX + 3.9f, pierStartZ - 37f, -6f),
        })
        {
            var boat = moored.Place("Boat_Moored", parent, new Vector3(x, -0.22f, z), Quaternion.Euler(0f, yaw, 0f), false);
            boat.AddComponent<GentleBob>();
        }

        // 拉上沙灘的小船、曬網架、魚乾架、魚桶
        var beachedBoat = new SharedModel("Boat_Beached", JapaneseVillageMeshes.Boat(5.6f, 1.4f, 0.62f, true));
        var rack = new SharedModel("NetRack", JapaneseVillageMeshes.NetRack());
        var fishRack = new SharedModel("FishRack", JapaneseVillageMeshes.FishRack());
        foreach (var (x, inland, yaw) in new[] { (-42f, 7.5f, 195f), (-52f, 4f, 165f), (46f, 6f, 200f) })
        {
            float z = ShoreZ(x) + inland;
            var boat = beachedBoat.Place("Boat_Beached", parent, new Vector3(x, Ground(x, z) - 0.05f, z), Quaternion.Euler(4f, yaw, 2f));
            var col = boat.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.4f, 0f);
            col.size = new Vector3(1.4f, 0.8f, 5.4f);
        }
        foreach (var (x, inland, yaw) in new[] { (-31f, 13f, 12f), (-22f, 13.5f, 2f), (-68f, 14f, -8f), (58f, 13f, 6f) })
        {
            float z = ShoreZ(x) + inland;
            rack.Place("NetRack", parent, new Vector3(x, Ground(x, z), z), Quaternion.Euler(0f, yaw, 0f));
        }
        foreach (var (x, inland, yaw) in new[] { (-38f, 16f, 0f), (36f, 15f, 14f), (-60f, 15f, 10f) })
        {
            float z = ShoreZ(x) + inland;
            fishRack.Place("FishRack", parent, new Vector3(x, Ground(x, z), z), Quaternion.Euler(0f, yaw, 0f));
        }
        foreach (var (name, x, z, yaw) in new[]
        {
            ("pf_barrel_fish_01", -47f, -1f, 30f), ("pf_bucket_01", -44.6f, -1.2f, 0f), ("pf_barrels_01", 33f, 2f, 80f),
            ("pf_buckets_01", 19f, 2.5f, 150f),
        })
        {
            float pz = ShoreZ(x) + 12f + z;
            PlacePrefab(VikingProps + name + ".prefab", parent, new Vector3(x, Ground(x, pz), pz), Quaternion.Euler(0f, yaw, 0f));
        }

        // 海岸邊的礁石
        var boulderRng = new System.Random(5150);
        for (int i = 0; i < 16; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * (70f + (float)boulderRng.NextDouble() * 80f);
            float z = ShoreZ(x) + (float)boulderRng.NextDouble() * 10f - 7f;
            PlacePackRock(parent, boulderRng.NextDouble() < 0.5 ? "rock_01" : "rock_02", new Vector3(x, Ground(x, z) - 0.25f, z),
                (float)boulderRng.NextDouble() * 360f, 2.2f + (float)boulderRng.NextDouble() * 2.2f);
        }
    }

    static float PierEndZ, PierStartZ;

    // ---------------------------------------------------------------- 後山

    static void BuildHillFeatures(Transform parent)
    {
        // 山腳和山頂的鳥居（Japanese Garden Pack）
        PlaceTorii(parent, StairStartZ + 1.5f);
        PlaceTorii(parent, StairEndZ - 3f);

        // 石階：每階一塊 Garden 的天然石，放寬成 3.8 m
        var ramp = new JVMeshKit();
        const float tread = 0.72f, rise = 0.2f, stairWidth = 3.8f;
        var stairs = new GameObject("StoneStairs");
        stairs.transform.SetParent(parent, false);
        stairs.isStatic = true;
        var stoneRng = new System.Random(911);
        for (float z = StairStartZ + 0.3f; z < StairEndZ + 1f; z += tread)
        {
            float y = Ground(0f, z);
            // StoneStep_01：寬 1.18（local X）、長 2.31（local Z）、高 0.47，轉 90 度橫放
            var stone = JVGarden.Place("StoneStep_01", stairs.transform, new Vector3((float)stoneRng.NextDouble() * 0.12f - 0.06f, y + rise - 0.47f, z), stoneRng.Next(2) == 0 ? 90f : -90f);
            stone.transform.localScale = new Vector3(0.8f, 1f, stairWidth / 2.31f);
            if (z + tread < StairEndZ + 1f)
            {
                float w = stairWidth * 0.5f, y1 = Ground(0f, z + tread) + rise;
                ramp.Quad(JVMat.Stone, new Vector3(-w, y + rise, z), new Vector3(-w, y1, z + tread), new Vector3(w, y1, z + tread), new Vector3(w, y + rise, z));
            }
        }
        // 碰撞是貼著每階前緣的斜面：腳不會陷進石階，走起來也不會一頓一頓
        stairs.AddComponent<MeshCollider>().sharedMesh = SaveMesh(ramp.Build("StoneStairsRamp"));

        // 石階兩旁的石燈籠
        for (float z = StairStartZ + 9f; z < StairEndZ - 4f; z += 12f)
            foreach (float side in new[] { -1f, 1f })
                PlaceStoneLantern(parent, side * 3f, z);

        BuildShrine(parent);
        BuildTrainingYard(parent);

        // 山上的大石頭（Garden 的青苔岩）
        var rng = new System.Random(4242);
        var boulders = new GameObject("Boulders").transform;
        boulders.SetParent(parent, false);
        for (int i = 0; i < 34; i++)
        {
            float x = (float)rng.NextDouble() * 2f * 165f - 165f;
            float z = StairStartZ + 10f + (float)rng.NextDouble() * 120f;
            if (Mathf.Abs(x) < 12f || TerraceDistance(x, z) < 4f) continue;
            float scale = 1.1f + (float)rng.NextDouble() * 1.6f;
            var stone = JVGarden.Place("Stone_04", boulders, new Vector3(x, Ground(x, z) + 0.2f * scale, z), (float)rng.NextDouble() * 360f);
            stone.transform.localScale *= scale;
            stone.AddComponent<SphereCollider>().radius = 0.72f;
        }
    }

    // ---------------------------------------------------------------- 祠堂與習武場

    // 建好之後由 BuildTrial 接到 ShrineTrial 上
    static SlidingDoor trialBackDoor;
    static Vector3 trialMasterPosition;
    static float trialMasterYaw;
    static readonly List<SteleInscription> trialReadings = new List<SteleInscription>();
    static float trialWallTop;
    static readonly List<TrainingDummy> trialDummies = new List<TrainingDummy>();
    static readonly List<TetherTrainingBeam> trialBeams = new List<TetherTrainingBeam>();
    static DojoGate trialGate;
    static TetherCourse trialCourse;
    static MerchantNPC trialMonk;

    const string PaintingDir = "Assets/_Project/Materials/JapaneseVillage/Paintings";

    /// <summary>
    /// 祠堂地上的四幅古畫（圖由 Art/JapaneseVillage/generate_paintings.py 產生）。
    /// pos / yaw 是祠堂室內座標（+Z = 正門），都放在榻榻米上、避開門到門的走道和柱子。說明一行最多約 20 個字
    /// </summary>
    static readonly (string file, string title, string caption, Vector2 pos, float yaw)[] Paintings =
    {
        ("P1_SeaDragon", "其一　海上鬥龍", "黑潮之夜，玄清立於浪頭，\n一劍逼退興風作浪的海龍。", new Vector2(-4.2f, 1.6f), 14f),
        ("P2_Feast", "其二　與民同醉", "海龍退去，村人擺酒相謝。\n道人喝醉了，笑說要在此終老。", new Vector2(3.4f, 2.4f), -8f),
        ("P3_Centipede", "其三　百足現世", "某夜大地裂開，百足巨蟲破土而出。\n道人力戰到天明，終究不敵。", new Vector2(-2.6f, -2.3f), 171f),
        ("P4_BlackRobes", "其四　黑衣奪骸", "天亮之前，一群黑衣人趁亂而來，\n抬走道人的遺體，往村子東邊去了。", new Vector2(4.6f, -0.6f), 24f),
    };

    const string PoemTitle = "弔玄清";
    const string PoemText = "海上斬龍劍未寒，\n醉歌村裡夜方闌。\n百足破土穿雲至，\n一夜青衫血染殘。\n\n黑衣攜骨歸何處？\n道場深鎖萬重關。\n誰開長者後園門，\n為取先生遺骨還。";
    static readonly Vector2 PoemPos = new Vector2(-5.2f, -1.0f);

    /// <summary>混在古畫裡的村民的書：翻得開，但不算進度（count = 疊幾本）</summary>
    static readonly (string title, string text, JVMat cover, Vector2 pos, int count)[] VillagerBooks =
    {
        ("農家月令", "正月修犁，二月浸種，\n三月插秧，五月除草。\n\n八月收割之前，\n記得先祭田神。", JVMat.ClothOchre, new Vector2(-2.4f, 3.3f), 2),
        ("漁獲帳", "鯛三尾、竹莢魚一簍。\n阿松賒帳：鰹魚兩條。\n\n（帳本上有一塊醬油漬）", JVMat.ClothIndigo, new Vector2(2.2f, 0.4f), 1),
        ("潮汐記", "朔望大潮，\n初八、廿三小潮。\n\n大潮的前一天，\n船不要出港。", JVMat.ClothTeal, new Vector2(5.6f, 1.4f), 3),
        ("竹工手冊", "竹子要挑三年生的，\n冬天砍的才不會生蟲。\n\n劈竹要順著紋路，\n不要硬來。", JVMat.ClothGreen, new Vector2(-3.9f, -3.6f), 1),
        ("草藥圖譜", "石階兩旁多長艾草，\n搗爛敷在傷口上，\n止血最快。", JVMat.ClothBrown, new Vector2(1.9f, 3.6f), 2),
        ("村塾習字帖", "天地玄黃，宇宙洪荒。\n\n（字寫得歪歪扭扭，\n旁邊還畫了一隻貓）", JVMat.ClothGrey, new Vector2(-1.8f, 0.2f), 1),
    };

    static Material PaintingMaterial(string file)
    {
        string texPath = $"{PaintingDir}/{file}.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
        // 圖是 3:2，不能被「縮放到 2 的次方」拉成 2:1
        // 破掉的邊是透明的：要標成「alpha 是透明度」，邊緣才不會有黑邊
        if (importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize < 2048 || importer.npotScale != TextureImporterNPOTScale.None || !importer.alphaIsTransparency)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        var mat = JapaneseVillageAssets.ShaderMaterial($"M_JV_{file}", "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.08f);
        // Alpha Clipping：撕破、蟲蛀的地方直接看穿（露出底下的榻榻米）
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>一張平放在地上、可以撿起來看的紙</summary>
    static SteleInscription FloorSheet(Transform parent, string name, string file, float width, float height, Vector3 localPos, float yaw)
    {
        var go = Instance(name, parent, JapaneseVillageMeshes.Sheet(width, height), Vector3.zero, Quaternion.identity, false);
        go.transform.SetLocalPositionAndRotation(localPos, Quaternion.Euler(0f, yaw, 0f));
        go.GetComponent<MeshRenderer>().sharedMaterial = PaintingMaterial(file);
        return go.AddComponent<SteleInscription>();
    }

    /// <summary>四幅古畫、一首詩、村民的書，散在祠堂的榻榻米上（原點在祠堂地面中心，還沒放大）</summary>
    static void ScatterReadings(Transform shrine, Transform furniture)
    {
        // 榻榻米的上緣（古畫要蓋在它上面）
        float matTop = JapaneseVillageHouses.Floor;
        foreach (var r in shrine.Find("Hall").GetComponentsInChildren<Renderer>())
            if (r.name.StartsWith("Tatami")) matTop = Mathf.Max(matTop, r.bounds.max.y);
        float y = matTop + 0.004f;

        trialReadings.Clear();
        foreach (var (file, title, caption, pos, yaw) in Paintings)
        {
            var sheet = FloorSheet(furniture, $"Painting_{file}", file, 1.0f, 0.667f, new Vector3(pos.x, y, pos.y), yaw);
            sheet.title = title;
            sheet.text = caption;
            sheet.picture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PaintingDir}/{file}.png");
            sheet.pictureOnly = true; // 只秀畫本身，不包在卷軸裡
            sheet.promptText = $"看古畫：{title}";
            sheet.readRadius = 1.6f;
            trialReadings.Add(sheet);
        }
        var poem = FloorSheet(furniture, "Poem", "Poem", 0.7f, 0.5f, new Vector3(PoemPos.x, y, PoemPos.y), -32f);
        poem.title = PoemTitle;
        poem.text = PoemText;
        poem.promptText = $"看詩：{PoemTitle}";
        poem.readRadius = 1.6f;
        trialReadings.Add(poem);

        var rng = new System.Random(612);
        var books = new Dictionary<JVMat, SharedModel>();
        foreach (var (title, text, cover, pos, count) in VillagerBooks)
        {
            if (!books.TryGetValue(cover, out var model)) books[cover] = model = new SharedModel($"Book_{cover}", JapaneseVillageMeshes.Book(cover));
            GameObject top = null;
            for (int i = 0; i < count; i++)
            {
                var offset = new Vector3((float)rng.NextDouble() * 0.06f - 0.03f, y + i * 0.036f, (float)rng.NextDouble() * 0.06f - 0.03f);
                top = model.Place($"Book_{title}_{i + 1}", furniture, Vector3.zero, Quaternion.identity, false);
                top.transform.SetLocalPositionAndRotation(new Vector3(pos.x, 0f, pos.y) + offset, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            }
            var book = top.AddComponent<SteleInscription>();
            book.title = $"《{title}》";
            book.text = text;
            book.promptText = $"翻開《{title}》";
            book.readRadius = 1.3f;
        }
    }

    /// <summary>祠堂：20 × 12 m 放大 1.2 倍（比村長家小一點），前門開著，後門通往習武場</summary>
    static void BuildShrine(Transform parent)
    {
        var spec = new GardenHouseSpec
        {
            width = ShrineWidth, depth = HouseDepth, facade = "WSWSOSWSW", backFacade = "SWSWBWSWS",
            veranda = true, hollow = true, seed = 99,
        };
        float floor = JapaneseVillageHouses.Floor, top = JapaneseVillageHouses.WallTop;
        float halfW = spec.width * 0.5f, halfD = spec.depth * 0.5f;

        // 整棟在原點組好（家具都用屋內座標擺），最後才放大、轉向、移到山上
        var shrine = JapaneseVillageHouses.Build("Shrine", spec);
        AddHollowHouseColliders(shrine, spec);
        var t = shrine.transform;

        // 前後門的階梯：看得到的是石階，走的是斜坡
        foreach (float side in new[] { 1f, -1f })
        {
            float edge = side > 0f ? halfD + JapaneseVillageHouses.VerandaDepth : halfD;
            const float run = 2.3f;
            for (int i = 0; i < 2; i++)
            {
                var stone = JVGarden.Place("StoneStep_01", t, new Vector3(0f, floor * (2 - i) / 3f - 0.47f, side * (edge + 0.42f + i * 0.72f)), 90f);
                stone.transform.localScale = new Vector3(0.75f, 1f, 3.6f / 2.31f);
            }
            var ramp = new GameObject(side > 0f ? "FrontRamp" : "BackRamp");
            ramp.transform.SetParent(t, false);
            ramp.transform.localPosition = new Vector3(0f, floor * 0.5f - 0.1f, side * (edge + run * 0.5f));
            ramp.transform.localRotation = Quaternion.Euler(side * Mathf.Atan2(floor, run) * Mathf.Rad2Deg, 0f, 0f);
            ramp.AddComponent<BoxCollider>().size = new Vector3(3.6f, 0.2f, Mathf.Sqrt(run * run + floor * floor));
            ramp.isStatic = true;
        }

        // 後門：師父答應之前關著
        var doorGo = t.Find("Walls/BackDoor").gameObject;
        var door = doorGo.AddComponent<SlidingDoor>();
        door.panels = new[] { doorGo.transform.Find("PanelA"), doorGo.transform.Find("PanelB") };
        door.openOffsets = new[] { new Vector3(-0.94f, 0f, 0f), new Vector3(0.95f, 0f, 0f) };
        var blocker = doorGo.AddComponent<BoxCollider>();
        blocker.center = new Vector3(0f, floor + 1.1f, -halfD);
        blocker.size = new Vector3(2f, 2.2f, 0.4f);
        door.blocker = blocker;
        trialBackDoor = door;

        // 地上散著古畫、詩和村民寄放的書；牆邊留幾個坐墊
        var furniture = new GameObject("Furniture").transform;
        furniture.SetParent(t, false);
        ScatterReadings(t, furniture);
        foreach (var (x, z, yaw) in new[] { (-halfW + 1.3f, -2.2f, 90f), (-halfW + 1.3f, 2.2f, 90f), (halfW - 1.3f, 2.2f, -90f) })
            JVGarden.PlaceOn("Cushions_01", furniture, new Vector3(x, floor, z), yaw);

        // 師父站在後門旁邊，後面是供桌
        var altar = JVGarden.PlaceOn("Table_01", furniture, new Vector3(4.6f, floor, -halfD + 1.15f), 0f);
        float altarTop = JVGarden.WorldBounds(altar).max.y;
        JVGarden.PlaceOn("Lantern_Paper_01", furniture, new Vector3(3.3f, altarTop, -halfD + 1.15f), 0f, 0.8f);
        JVGarden.PlaceOn("Lantern_Paper_01", furniture, new Vector3(5.9f, altarTop, -halfD + 1.15f), 0f, 0.8f);
        var altarCollider = altar.AddComponent<BoxCollider>();
        altarCollider.center = altar.transform.InverseTransformPoint(JVGarden.WorldBounds(altar).center);
        altarCollider.size = JVGarden.WorldBounds(altar).size;

        // 屋內的吊燈：沒有影子的點光源，比較省
        foreach (var (x, z) in new[] { (-5f, 0f), (5f, 0f) })
        {
            JVGarden.PlaceOn("Lantern_MediumHanging_01", furniture, new Vector3(x, top - 1.1f, z), 0f);
            var lightGo = new GameObject("HallLight");
            lightGo.transform.SetParent(furniture, false);
            lightGo.transform.localPosition = new Vector3(x, top - 1.2f, z);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.55f);
            light.intensity = 6f;
            light.range = 12f;
            light.shadows = LightShadows.None;
        }

        // 放到山上：屋內座標 +Z 是正面，轉 180 度朝南
        t.SetParent(parent, false);
        float y = Ground(0f, ShrineZ);
        t.SetPositionAndRotation(new Vector3(0f, y, ShrineZ), Quaternion.Euler(0f, 180f, 0f));
        t.localScale = Vector3.one * ShrineScale;
        footprints.Add(new Rect(-16f, ShrineZ - 11f, 32f, 19f));

        trialMasterPosition = t.TransformPoint(new Vector3(3f, floor, -halfD + 2.6f));
        trialMasterYaw = 180f;

        float front = ShrineZ - (halfD + JapaneseVillageHouses.VerandaDepth) * ShrineScale;
        var rope = Instance("ShrineRope", t, JapaneseVillageMeshes.ShrineRope(4.2f),
            new Vector3(0f, y + top * ShrineScale - 0.45f, front + 0.1f), Quaternion.Euler(0f, 180f, 0f));
        rope.transform.localScale = Vector3.one / ShrineScale;

        // 賽錢箱放在階梯旁邊，不擋路
        float boxZ = front - 1.5f;
        var box = Instance("OfferingBox", parent, JapaneseVillageMeshes.OfferingBox(),
            new Vector3(-3.9f, Ground(-3.9f, boxZ), boxZ), Quaternion.identity);
        var boxCollider = box.AddComponent<BoxCollider>();
        boxCollider.center = new Vector3(0f, 0.34f, 0f);
        boxCollider.size = new Vector3(1.32f, 0.68f, 0.7f);
    }

    /// <summary>習武場：祠堂後面的圍牆院子，東半邊懸空木條練拉索，西半邊木樁人偶練對打</summary>
    static void BuildTrainingYard(Transform parent)
    {
        var yard = new GameObject("TrainingYard").transform;
        yard.SetParent(parent, false);
        float ground = TrialGroundY, x0 = -TerraceHalfX, x1 = TerraceHalfX, z0 = YardZ0, z1 = YardZ1;
        float fenceY = ground - 0.2f;
        float shrineHalf = ShrineWidth * 0.5f * ShrineScale;

        // 圍牆（同村長宅院的排法）；南邊中間是祠堂，用 2 m 的短牆補到祠堂牆角
        var wall = new GameObject("Wall").transform;
        wall.SetParent(yard, false);
        JVGarden.Place("Fence_400_02", wall, new Vector3(x0 + 2f, fenceY, z0), 0f);
        JVGarden.Place("Fence_400_02", wall, new Vector3(x0, fenceY, z1 - 2f), 90f);
        JVGarden.Place("Fence_400_02", wall, new Vector3(x1 - 2f, fenceY, z1), 180f);
        JVGarden.Place("Fence_400_02", wall, new Vector3(x1, fenceY, z0 + 2f), -90f);
        for (float x = x0 + 6f; x <= x1 - 2f + 0.01f; x += 4f)
            if (x <= -shrineHalf || x - 4f >= shrineHalf) JVGarden.Place("Fence_400_01", wall, new Vector3(x, fenceY, z0), 0f);
        JVGarden.Place("Fence_200_01", wall, new Vector3(-shrineHalf, fenceY, z0), 0f);
        JVGarden.Place("Fence_200_01", wall, new Vector3(shrineHalf + 2f, fenceY, z0), 0f);
        trialWallTop = float.NegativeInfinity;
        for (float x = x0 + 2f; x <= x1 - 6f + 0.01f; x += 4f)
        {
            var piece = JVGarden.Place("Fence_400_01", wall, new Vector3(x, fenceY, z1), 180f);
            trialWallTop = Mathf.Max(trialWallTop, JVGarden.WorldBounds(piece).max.y); // 僧侶的輕功站在北牆頂上
        }
        for (float z = z1 - 2f; z >= z0 + 6f - 0.01f; z -= 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x1, fenceY, z), -90f);
        for (float z = z0 + 2f; z <= z1 - 6f + 0.01f; z += 4f) JVGarden.Place("Fence_400_01", wall, new Vector3(x0, fenceY, z), 90f);

        // 看不見的高牆：拉索也飛不進來，一定要從祠堂的後門走
        void Barrier(string name, Vector3 center, Vector3 size) => BoundsBox(yard, name, center, size, Quaternion.identity);
        const float high = 90f;
        float cy = ground + high * 0.5f - 2f;
        Barrier("Yard_North", new Vector3(0f, cy, z1), new Vector3(x1 - x0 + 0.5f, high, 0.5f));
        Barrier("Yard_East", new Vector3(x1, cy, (z0 + z1) * 0.5f), new Vector3(0.5f, high, z1 - z0 + 0.5f));
        Barrier("Yard_West", new Vector3(x0, cy, (z0 + z1) * 0.5f), new Vector3(0.5f, high, z1 - z0 + 0.5f));
        float southLength = x1 - shrineHalf;
        Barrier("Yard_SouthEast", new Vector3(shrineHalf + southLength * 0.5f, cy, z0), new Vector3(southLength, high, 0.5f));
        Barrier("Yard_SouthWest", new Vector3(-shrineHalf - southLength * 0.5f, cy, z0), new Vector3(southLength, high, 0.5f));
        float ridge = ground + JapaneseVillageHouses.RidgeY(HouseDepth) * ShrineScale;
        Barrier("Yard_OverShrine", new Vector3(0f, ridge + high * 0.5f, ShrineZ), new Vector3(shrineHalf * 2f + 8f, high, 0.5f));

        // 從後門延伸出來的飛石和石燈籠
        string[] walkway = { "Stone_Walkway_01", "Stone_Walkway_02", "Stone_Walkway_03", "Stone_Walkway_04" };
        var rng = new System.Random(808);
        for (float z = z0 + 3.4f; z < z1 - 6f; z += 1f)
        {
            var stone = JVGarden.Place(walkway[rng.Next(walkway.Length)], yard, new Vector3((float)rng.NextDouble() * 0.5f - 0.25f, ground - 0.03f, z), (float)rng.NextDouble() * 360f);
            stone.transform.localScale *= 1.25f;
        }
        foreach (float side in new[] { -1f, 1f })
            PlaceStoneLantern(yard, side * 4.5f, z0 + 6f);

        // 東半邊：深溝 + 懸空木條（柱子從溝底立起來），一路往東盪到對岸
        BuildPit(yard, ground, z0, z1);
        trialBeams.Clear();
        var unlit = new SharedModel("BeamLantern_Unlit", JapaneseVillageMeshes.BeamLantern(false));
        var lit = new SharedModel("BeamLantern_Lit", JapaneseVillageMeshes.BeamLantern(true));
        float zMid = (z0 + z1) * 0.5f, floorY = ground - PitDepth;
        int index = 0;
        // (x, z 偏移, 離溝緣的高度)：每根相隔約 7 m，越往後越高；最後一根在對岸上空，拉過去就落在地面上
        foreach (var (x, dz, height) in new[] { (24f, 0f, 6f), (31f, -5f, 7f), (38f, 4f, 7.5f), (45f, -3f, 8f), (52f, 3f, 8.5f), (63f, 0f, 6.5f) })
        {
            bool overPit = x < pitRect.xMax;
            float baseY = overPit ? floorY : ground;
            var beam = BuildHangingBeam(yard, ++index, new Vector3(x, baseY, zMid + dz), ground - baseY + height, 90f, unlit, lit);
            trialBeams.Add(beam);
            // 第 2 根以後的木條下面吊一塊木板：可以站上去喘口氣，但站一下就會翻（第 1 根沒有，才不能直接從岸邊跳過去）
            if (overPit && index >= 2) BuildSwayPlank(beam.transform.parent, ground - baseY - 1.4f, ground - baseY + height);
        }
        // 起點（西岸）和終點（東岸）各一對石燈籠
        foreach (float x in new[] { pitRect.xMin - 4f, pitRect.xMax + 6f })
            foreach (float side in new[] { -1f, 1f })
                PlaceStoneLantern(yard, x, zMid + side * 4.5f);
        var startPoint = new GameObject("CourseStart").transform;
        startPoint.SetParent(yard, false);
        startPoint.SetPositionAndRotation(new Vector3(pitRect.xMin - 4f, ground + 1.1f, zMid), Quaternion.Euler(0f, 90f, 0f));
        var course = new GameObject("TetherCourse").AddComponent<TetherCourse>();
        course.transform.SetParent(yard, false);
        course.beams = trialBeams.ToArray();
        course.pitArea = pitRect;
        course.rimY = ground;
        course.startPoint = startPoint;
        course.yardArea = new Rect(x0, z0, x1 - x0, z1 - z0);
        trialCourse = course;

        // 西半邊：四根木樁，打一下就變成木頭人
        trialDummies.Clear();
        var post = new SharedModel("DummyPost", JapaneseVillageMeshes.DummyBase());
        var postBody = new SharedModel("DummyBody", JapaneseVillageMeshes.DummyBody());
        var postArms = new SharedModel("DummyArms", JapaneseVillageMeshes.DummyArms());
        var limbStake = new SharedModel("LimbStake", LimbStakeKit());
        var fighterController = FighterController();
        index = 0;
        foreach (var (x, z) in new[] { (-22f, z0 + 12f), (-36f, z0 + 24f), (-50f, z0 + 12f) })
        {
            var position = new Vector3(x, ground, z);
            var root = new GameObject($"TrainingDummy_{++index}");
            root.transform.SetParent(yard, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f)); // 面向南邊（祠堂後門）
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1f, 0f) * FighterSize;
            capsule.height = 2.05f * FighterSize;
            capsule.radius = 0.4f * FighterSize;

            // 還沒被打之前：一根木樁人偶（底座 + 主幹 + 兩隻木臂）
            var postVisual = post.Place("Post", root.transform, Vector3.zero, Quaternion.identity, false);
            postVisual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            postVisual.transform.localScale = Vector3.one * FighterSize;
            var body = postBody.Place("Body", postVisual.transform, Vector3.zero, Quaternion.identity, false);
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var arms = postArms.Place("Arms", body.transform, Vector3.zero, Quaternion.identity, false);
            arms.transform.SetLocalPositionAndRotation(new Vector3(0f, 1.42f, 0f), Quaternion.identity);

            var dummy = root.AddComponent<TrainingDummy>();
            dummy.postVisual = postVisual;
            dummy.standOff = 1.5f * FighterSize;
            dummy.reach = 2.3f * FighterSize;
            dummy.moveSpeed = 2.1f * FighterSize;
            BuildWoodenFighter(root.transform, dummy, fighterController, limbStake);
            // 拉索只能把玩家拉過去，不能把人偶整個拖走
            root.AddComponent<TetherInteractable>().mode = TetherInteractable.TetherMode.StaticAnchor;
            trialDummies.Add(dummy);
        }
        footprints.Add(new Rect(x0, z0, x1 - x0, z1 - z0));
    }

    /// <summary>深溝：地形挖洞的地方用石牆圍起來，溝底散著碎石和削尖的竹樁；牆邊的窄條用隱形牆擋住，只能拉索過去</summary>
    static void BuildPit(Transform yard, float ground, float z0, float z1)
    {
        var pit = new GameObject("TetherPit").transform;
        pit.SetParent(yard, false);
        pit.gameObject.isStatic = true;
        var r = pitRect;
        const float thick = 1.2f;
        float top = ground - 0.04f, bottom = ground - PitDepth;
        var kit = new JVMeshKit();
        void Slab(Vector3 center, Vector3 size)
        {
            kit.Box(JVMat.Stone, center, size);
            var go = new GameObject("PitWall");
            go.transform.SetParent(pit, false);
            go.transform.position = center;
            go.isStatic = true;
            go.AddComponent<BoxCollider>().size = size;
        }
        float h = top - bottom, cy = (top + bottom) * 0.5f;
        Slab(new Vector3(r.xMin - thick * 0.5f, cy, r.center.y), new Vector3(thick, h, r.height + thick * 2f));
        Slab(new Vector3(r.xMax + thick * 0.5f, cy, r.center.y), new Vector3(thick, h, r.height + thick * 2f));
        Slab(new Vector3(r.center.x, cy, r.yMin - thick * 0.5f), new Vector3(r.width, h, thick));
        Slab(new Vector3(r.center.x, cy, r.yMax + thick * 0.5f), new Vector3(r.width, h, thick));
        Slab(new Vector3(r.center.x, bottom - 0.3f, r.center.y), new Vector3(r.width + thick * 2f, 0.6f, r.height + thick * 2f));
        Instance("PitStone", pit, kit, Vector3.zero, Quaternion.identity);

        // 溝底：碎石和削尖的竹樁（只是看的，掉到一半就會回起點）
        var rng = new System.Random(4321);
        var spikes = new JVMeshKit();
        for (int i = 0; i < 70; i++)
        {
            float x = r.xMin + 1f + (float)rng.NextDouble() * (r.width - 2f), z = r.yMin + 1f + (float)rng.NextDouble() * (r.height - 2f);
            float len = 1.2f + (float)rng.NextDouble() * 1.4f;
            var tilt = Quaternion.Euler((float)rng.NextDouble() * 24f - 12f, 0f, (float)rng.NextDouble() * 24f - 12f);
            spikes.Cylinder(JVMat.BambooCulm, new Vector3(x, bottom, z), new Vector3(x, bottom, z) + tilt * Vector3.up * len, 0.06f, 0.005f, 6);
        }
        Instance("PitSpikes", pit, spikes, Vector3.zero, Quaternion.identity);
        for (int i = 0; i < 14; i++)
        {
            float x = r.xMin + 2f + (float)rng.NextDouble() * (r.width - 4f), z = r.yMin + 2f + (float)rng.NextDouble() * (r.height - 4f);
            PlacePackRock(pit, rng.NextDouble() < 0.5 ? "rock_01" : "rock_02", new Vector3(x, bottom - 0.2f, z), (float)rng.NextDouble() * 360f, 1.2f + (float)rng.NextDouble() * 1.4f);
        }

        // 南北牆邊剩下的窄條：隱形牆，不能沿著圍牆走過去
        const float high = 90f;
        float by = ground + high * 0.5f - 2f;
        if (r.yMin - z0 > 0.05f)
            BoundsBox(yard, "Pit_StripSouth", new Vector3(r.center.x, by, (z0 + r.yMin) * 0.5f), new Vector3(r.width + 1f, high, r.yMin - z0 + 0.5f), Quaternion.identity);
        if (z1 - r.yMax > 0.05f)
            BoundsBox(yard, "Pit_StripNorth", new Vector3(r.center.x, by, (z1 + r.yMax) * 0.5f), new Vector3(r.width + 1f, high, z1 - r.yMax + 0.5f), Quaternion.identity);
    }

    /// <summary>木頭人用的 Animator：參數 Speed 0 = 待機（劍盾格擋架勢），1 = 走路</summary>
    static RuntimeAnimatorController FighterController()
    {
        JapaneseVillageAssets.EnsureFolder("Assets/_Project/Animations/JapaneseVillage");
        AssetDatabase.DeleteAsset(FighterControllerPath);
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(FighterControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(FirstClip("Assets/_Project/ThirdParty/Lite Sword and Shield Pack/sword and shield block idle.fbx"), 0f);
        tree.AddChild(FirstClip("Assets/_Project/ThirdParty/StarterAssets Animations/Locomotion--Walk_N.anim.fbx"), 1f);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static AnimationClip FirstClip(string fbx)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
        throw new System.IO.FileNotFoundException($"[JapaneseVillage] {fbx} 裡沒有動畫");
    }

    /// <summary>村民用的 Animator：Speed 0 = 待機（空手站立）、1 = 走路、2 = 跑步；WalkRate 是播放速度倍率（VillagerWalker 依走路速度調整）</summary>
    static RuntimeAnimatorController VillagerController()
    {
        JapaneseVillageAssets.EnsureFolder("Assets/_Project/Animations/JapaneseVillage");
        AssetDatabase.DeleteAsset(VillagerControllerPath);
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(VillagerControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter { name = "WalkRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        var state = controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
        state.speedParameterActive = true;
        state.speedParameter = "WalkRate";
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(FirstClip("Assets/_Project/ThirdParty/Lite Longbow Pack/unarmed idle 01.fbx"), 0f);
        tree.AddChild(FirstClip("Assets/_Project/ThirdParty/StarterAssets Animations/Locomotion--Walk_N.anim.fbx"), 1f);
        tree.AddChild(FirstClip("Assets/_Project/ThirdParty/Lite Longbow Pack/standing run forward.fbx"), 2f); // 小孩跑步用（NpcRoutine 速度超過 runFrom）
        AssetDatabase.SaveAssets();
        return controller;
    }

    /// <summary>
    /// 人形模型的匯入設定：人形骨架（才能套用專案裡的動畫）、不匯入模型自帶的材質和動畫；
    /// 法線貼圖要標成 Normal map，金屬度貼圖不是顏色（關掉 sRGB）
    /// </summary>
    static void EnsureHumanImport(string modelPath, string normalPath, string metallicPath = null)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        if (mi == null) throw new System.IO.FileNotFoundException($"[JapaneseVillage] 找不到模型 {modelPath}");
        bool dirty = false;
        if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }
        if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
        if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
        if (dirty) mi.SaveAndReimport();

        Avatar avatar = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            if (asset is Avatar a) avatar = a;
        Debug.Log($"[JapaneseVillage] 模型 {System.IO.Path.GetFileName(modelPath)}：Avatar {(avatar == null ? "沒有" : $"valid={avatar.isValid} human={avatar.isHuman}")}");

        var ti = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (ti != null && ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
        if (metallicPath != null)
        {
            ti = (TextureImporter)AssetImporter.GetAtPath(metallicPath);
            if (ti != null && ti.sRGBTexture)
            {
                ti.sRGBTexture = false;
                ti.SaveAndReimport();
            }
        }
    }

    /// <summary>Tripo 人物（僧侶 Monk、村長 Elder）的材質：貼圖由 make_monk_textures.py 產生在 Models/JapaneseVillage/{name}/</summary>
    static Material TripoMaterial(string name)
    {
        string dir = $"Assets/_Project/Models/JapaneseVillage/{name}";
        var mat = JapaneseVillageAssets.ShaderMaterial($"M_JV_{name}", "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_JV_{name}_Base.png"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_JV_{name}_Normal.png"));
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        // 金屬的部分（鑲邊、護具）：R = 金屬度、A = 光滑度（make_monk_textures.py 合成的）
        mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_JV_{name}_MetallicSmooth.png"));
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.SetFloat("_Smoothness", 0.8f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 僧侶的 Animator：Locomotion（平常：空手站 / 走路，和村民一樣用 Speed、WalkRate）、
    /// Combat（過招的架勢：Speed 0 站、1 走、2 跑）、Attack1 / Attack2（出招）、Block（擋）；出招和擋播完回到 Combat。
    /// 出招動畫借用劍盾包的揮擊（空手看起來就是掌擊）。
    /// </summary>
    static RuntimeAnimatorController MonkController()
    {
        const string pack = "Assets/_Project/ThirdParty/Lite Sword and Shield Pack/";
        const float attackSpeed = 0.9f;
        JapaneseVillageAssets.EnsureFolder("Assets/_Project/Animations/JapaneseVillage");
        AssetDatabase.DeleteAsset(MonkControllerPath);
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(MonkControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter { name = "WalkRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var walk = FirstClip("Assets/_Project/ThirdParty/StarterAssets Animations/Locomotion--Walk_N.anim.fbx");
        var calm = controller.CreateBlendTreeInController("Locomotion", out var calmTree, 0);
        calm.speedParameterActive = true;
        calm.speedParameter = "WalkRate";
        calmTree.blendParameter = "Speed";
        calmTree.useAutomaticThresholds = false;
        calmTree.AddChild(FirstClip("Assets/_Project/ThirdParty/Lite Longbow Pack/unarmed idle 01.fbx"), 0f);
        calmTree.AddChild(walk, 1f);

        var combat = controller.CreateBlendTreeInController("Combat", out var combatTree, 0);
        combatTree.blendParameter = "Speed";
        combatTree.useAutomaticThresholds = false;
        combatTree.AddChild(FirstClip(pack + "sword and shield idle.fbx"), 0f);
        combatTree.AddChild(walk, 1f);
        combatTree.AddChild(FirstClip(pack + "sword and shield run.fbx"), 2f);

        var sm = controller.layers[0].stateMachine;
        sm.defaultState = calm;
        var lengths = new List<float>();
        foreach (var (name, clipFile, speed) in new[] { ("Attack1", "sword and shield attack (4).fbx", attackSpeed), ("Attack2", "sword and shield attack (2).fbx", attackSpeed), ("Block", "sword and shield block.fbx", 1f) })
        {
            var clip = FirstClip(pack + clipFile);
            var state = sm.AddState(name);
            state.motion = clip;
            state.speed = speed;
            var back = state.AddTransition(combat);
            back.hasExitTime = true;
            back.exitTime = 0.92f;
            back.duration = 0.15f;
            if (name != "Block") lengths.Add(clip.length / speed);
        }
        // 輕功：起跳（播完接空中）、空中（循環，落地時由 MonkSparring 切回 Combat / 出招）
        const string starter = "Assets/_Project/ThirdParty/StarterAssets Animations/";
        var air = sm.AddState("InAir");
        air.motion = FirstClip(starter + "Jump--InAir.anim.fbx");
        var jump = sm.AddState("Jump");
        jump.motion = FirstClip(starter + "Jump--Jump.anim.fbx");
        var toAir = jump.AddTransition(air);
        toAir.hasExitTime = true;
        toAir.exitTime = 0.8f;
        toAir.duration = 0.1f;
        monkAttackLengths = lengths.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[JapaneseVillage] 僧侶出招長度 {string.Join(" / ", System.Array.ConvertAll(monkAttackLengths, l => l.ToString("F2")))} 秒");
        return controller;
    }

    /// <summary>variant：Grey / Indigo / Brown / Olive（衣服的顏色，貼圖檔名的最後一段）</summary>
    static Material VillagerMaterial(string variant)
    {
        var mat = JapaneseVillageAssets.ShaderMaterial($"M_JV_Villager_{variant}", "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{VillagerDir}/T_JV_Villager_Base_{variant}.png"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{VillagerDir}/T_JV_Villager_Normal.png"));
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.12f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>把人形模型放在 root 底下：轉成面向 root.forward、縮放到指定高度、腳踩在 root 的高度</summary>
    static Animator BuildHumanModel(Transform root, HumanModel look)
    {
        var model = PlacePrefab(look.path, root, root.position, root.rotation);
        model.name = "Model";
        var animator = model.GetComponent<Animator>();

        // 模型原本面向哪邊不一定：腳尖在腳跟前面，用這個方向轉正（兩腳平均，腳尖外八的角度才會抵消）
        var footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var toesL = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        var footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var toesR = animator.GetBoneTransform(HumanBodyBones.RightToes);
        float turn = 0f;
        if (footL != null && toesL != null && footR != null && toesR != null)
        {
            Vector3 d = (toesL.position - footL.position).normalized + (toesR.position - footR.position).normalized;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-8f)
            {
                turn = Vector3.SignedAngle(d, root.forward, Vector3.up);
                model.transform.rotation = Quaternion.AngleAxis(turn, Vector3.up) * model.transform.rotation;
            }
        }

        var b = JVGarden.WorldBounds(model);
        float original = b.size.y / Mathf.Max(0.0001f, root.lossyScale.y);
        float scale = look.height * root.lossyScale.y / Mathf.Max(0.01f, b.size.y);
        model.transform.localScale *= scale;
        b = JVGarden.WorldBounds(model);
        model.transform.position += new Vector3(0f, root.position.y - b.min.y, 0f);

        var mat = look.material;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }

        animator.runtimeAnimatorController = look.controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        model.AddComponent<AnimationEventSink>(); // Walk_N 裡有 OnFootstep 事件，要有地方接
        Debug.Log($"[JapaneseVillage] 人物 {root.name}：模型原始高度 {original:F2}、縮放 {scale:F3}、轉正 {turn:F0}°、{look.label}");
        return animator;
    }

    /// <summary>四肢上的短木樁：沿 +Y 長 1、半徑 1（放的時候再縮放成實際的長度和粗細），中間綁一圈草繩，前端削尖</summary>
    static JVMeshKit LimbStakeKit()
    {
        var kit = new JVMeshKit();
        kit.Cylinder(JVMat.WoodDark, Vector3.zero, new Vector3(0f, 0.9f, 0f), 1f, 0.9f, 8);
        kit.Cylinder(JVMat.WoodLight, new Vector3(0f, 0.9f, 0f), new Vector3(0f, 1f, 0f), 0.9f, 0.35f, 8);
        kit.Cylinder(JVMat.Straw, new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.56f, 0f), 1.25f, 1.25f, 8);
        return kit;
    }

    /// <summary>
    /// 在木頭人的上臂、前臂、大腿、小腿外側各綁兩段短木樁（跟著骨頭一起動）。
    /// 木樁貼在四肢的表面：用這段骨頭帶動的頂點算出四肢在「外側」方向有多粗。
    /// </summary>
    static int AddLimbStakes(GameObject fighter, Animator animator, Vector3 fwd, SharedModel stake)
    {
        var smr = fighter.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null) return 0;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        var verts = baked.vertices;
        var weights = smr.sharedMesh.boneWeights;
        var bones = smr.bones;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Vector3 bodyCenter = hips != null ? hips.position : fighter.transform.position;
        float fighterScale = Mathf.Max(0.0001f, fighter.transform.lossyScale.x);

        var limbs = new (HumanBodyBones bone, HumanBodyBones child, bool arm)[]
        {
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, true), (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, true),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, true), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, true),
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, false), (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, false),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, false), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, false),
        };
        int count = 0;
        foreach (var (boneId, childId, arm) in limbs)
        {
            var bone = animator.GetBoneTransform(boneId);
            var child = animator.GetBoneTransform(childId);
            if (bone == null || child == null) continue;
            Vector3 a = bone.position, axis = child.position - a;
            float len = axis.magnitude;
            if (len < 0.01f) continue;
            axis /= len;

            // 外側：手臂是手背那一面（T 字姿勢的上方，手放下來就朝外），腿是正前方（護脛）
            Vector3 outward = Vector3.ProjectOnPlane(arm ? Vector3.up : fwd, axis);
            if (outward.sqrMagnitude < 0.09f) outward = Vector3.ProjectOnPlane(a - bodyCenter, axis);
            outward.Normalize();

            int bi = System.Array.IndexOf(bones, bone);
            float surface = 0f;
            for (int i = 0; i < verts.Length && bi >= 0; i++)
            {
                if (weights[i].boneIndex0 != bi || weights[i].weight0 < 0.5f) continue;
                Vector3 p = toWorld.MultiplyPoint3x4(verts[i]) - a;
                float t = Vector3.Dot(p, axis);
                if (t < 0f || t > len) continue;
                surface = Mathf.Max(surface, Vector3.Dot(p - axis * t, outward));
            }
            if (surface <= 0f) surface = 0.08f * FighterSize;

            float radius = (arm ? 0.04f : 0.05f) * FighterSize;
            foreach (var (t0, t1) in new[] { (0.08f, 0.46f), (0.54f, 0.92f) })
            {
                Vector3 start = a + axis * (len * t0) + outward * (surface + radius * 0.3f);
                var go = stake.Place($"LimbStake_{count++}", fighter.transform, start, Quaternion.FromToRotation(Vector3.up, axis), false);
                go.transform.localScale = new Vector3(radius, len * (t1 - t0), radius) / fighterScale;
                go.transform.SetParent(bone, true);
            }
        }
        Object.DestroyImmediate(baked);
        return count;
    }

    /// <summary>
    /// 木頭人：Banana Man 模型換成木頭材質，背上綁一根木樁、四肢綁著一段段短木樁；頭上的紅布過關才出現。
    /// 一開始關著，木樁被打一下才出現。
    /// </summary>
    static void BuildWoodenFighter(Transform root, TrainingDummy dummy, RuntimeAnimatorController controller, SharedModel limbStake)
    {
        const float height = FighterHeight;
        var fighter = PlacePrefab(FighterModel, root, root.position, root.rotation);
        fighter.name = "Fighter";
        var b = JVGarden.WorldBounds(fighter);
        float scale = height / Mathf.Max(0.01f, b.size.y);
        fighter.transform.localScale *= scale;
        b = JVGarden.WorldBounds(fighter);
        fighter.transform.position += new Vector3(0f, root.position.y - b.min.y, 0f);

        var wood = JapaneseVillageAssets.Get(JVMat.WoodLight);
        var woodDark = JapaneseVillageAssets.Get(JVMat.WoodDark);
        foreach (var r in fighter.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = mats[i] != null && mats[i].name.Contains("Joint") ? woodDark : wood;
            r.sharedMaterials = mats;
        }

        var animator = fighter.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        fighter.AddComponent<AnimationEventSink>(); // Walk_N 裡有 OnFootstep 事件，要有地方接

        // 綁在骨頭上的木樁：先算好世界座標再掛上去，跟著動畫一起動
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        b = JVGarden.WorldBounds(fighter);
        Vector3 fwd = root.forward;
        float halfDepth = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(fwd.x), 0f, Mathf.Abs(fwd.z))).magnitude;
        float halfWidth = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(fwd.z), 0f, Mathf.Abs(fwd.x))).magnitude;

        var stakeKit = new JVMeshKit();
        stakeKit.Cylinder(JVMat.WoodDark, new Vector3(0f, -0.75f, 0f), new Vector3(0f, 0.85f, 0f), 0.11f, 0.1f, 10);
        stakeKit.Cylinder(JVMat.WoodLight, new Vector3(0f, 0.85f, 0f), new Vector3(0f, 1.0f, 0f), 0.1f, 0.03f, 10);
        foreach (float y in new[] { -0.25f, 0.35f })
            stakeKit.Cylinder(JVMat.Straw, new Vector3(0f, y - 0.05f, 0f), new Vector3(0f, y + 0.05f, 0f), 0.13f, 0.13f, 10);
        var stake = Instance("BackStake", fighter.transform, stakeKit, Vector3.zero, root.rotation, false);
        stake.transform.localScale = Vector3.one * FighterSize / Mathf.Max(0.0001f, fighter.transform.lossyScale.x);
        stake.transform.position = (spine != null ? spine.position : b.center) - fwd * (halfDepth * 0.55f + 0.08f * FighterSize);
        stake.transform.SetParent(spine != null ? spine : fighter.transform, true);

        int stakes = AddLimbStakes(fighter, animator, fwd, limbStake);

        GameObject band = null;
        if (head != null)
        {
            var bandKit = new JVMeshKit();
            const float radius = 0.15f;   // 頭巾：比頭大一點點
            bandKit.Cylinder(JVMat.ClothRed, new Vector3(0f, -0.035f, 0f), new Vector3(0f, 0.035f, 0f), radius, radius * 0.97f, 14);
            bandKit.Box(JVMat.ClothRed, new Vector3(0.05f, -0.15f, -radius - 0.01f), new Vector3(0.07f, 0.26f, 0.015f), Quaternion.Euler(0f, 0f, -10f));
            bandKit.Box(JVMat.ClothRed, new Vector3(-0.05f, -0.17f, -radius - 0.01f), new Vector3(0.07f, 0.3f, 0.015f), Quaternion.Euler(0f, 0f, 12f));
            band = Instance("ClearedBand", fighter.transform, bandKit, head.position + Vector3.up * (0.12f * FighterSize), root.rotation, false);
            band.transform.localScale = Vector3.one * FighterSize / Mathf.Max(0.0001f, fighter.transform.lossyScale.x);
            band.transform.SetParent(head, true);
        }

        dummy.fighter = fighter;
        dummy.fighterAnimator = animator;
        dummy.clearedMarker = band;
        fighter.SetActive(false);
        Debug.Log($"[JapaneseVillage] 木頭人：原始高度 {b.size.y / 1f:F2} m 縮放 {scale:F3}，身寬 {halfWidth * 2f:F2}、身厚 {halfDepth * 2f:F2}，骨頭 chest={(chest != null)} head={(head != null)}，四肢木樁 {stakes} 段");
    }

    /// <summary>吊在懸空木條下面的木板（SwayPlank）：板子沿木條方向，繩子從板子兩端拉到木條上；plankY、beamY 是相對 beamRoot 的高度</summary>
    static void BuildSwayPlank(Transform beamRoot, float plankY, float beamY)
    {
        const float halfLen = 1.3f, halfWidth = 0.48f;
        var plankRoot = new GameObject("SwayPlank").transform;
        plankRoot.SetParent(beamRoot, false);
        plankRoot.localPosition = new Vector3(0f, plankY, 0f);
        var board = new GameObject("Board").transform;
        board.SetParent(plankRoot, false);

        var kit = new JVMeshKit();
        foreach (float z in new[] { -0.32f, 0f, 0.32f })
            kit.Box(JVMat.WoodLight, new Vector3(0f, 0f, z), new Vector3(halfLen * 2f, 0.08f, 0.29f));
        foreach (float x in new[] { -0.95f, 0.95f })
            kit.Box(JVMat.WoodDark, new Vector3(x, -0.065f, 0f), new Vector3(0.12f, 0.05f, halfWidth * 2f));
        Instance("BoardModel", board, kit, board.position, board.rotation, false);
        var col = board.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, -0.01f, 0f);
        col.size = new Vector3(halfLen * 2f, 0.14f, halfWidth * 2f);

        var ropes = new JVMeshKit();
        foreach (float x in new[] { -halfLen + 0.15f, halfLen - 0.15f })
            ropes.Cylinder(JVMat.Rope, new Vector3(x, 0.04f, 0f), new Vector3(x, beamY - plankY, 0f), 0.024f, 0.024f, 6);
        Instance("PlankRopes", plankRoot, ropes, plankRoot.position, plankRoot.rotation, false);

        var sway = plankRoot.gameObject.AddComponent<SwayPlank>();
        sway.board = board;
        sway.halfSize = new Vector2(halfLen, halfWidth);
    }

    /// <summary>一組懸空木條：兩根柱子 + 橫梁，木條用兩條繩子吊在半空（只有木條算拉索的目標）。height 是木條離 ground 的高度</summary>
    static TetherTrainingBeam BuildHangingBeam(Transform parent, int index, Vector3 ground, float height, float yaw, SharedModel unlit, SharedModel lit)
    {
        const float length = 4.4f, thick = 3f;
        var root = new GameObject($"HangingBeam_{index}").transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f));

        // BeamWooden_13x16x380：原點在底部，往 +Y 長 3.8 m
        GameObject Timber(string name, Vector3 from, Vector3 to, float thickness)
        {
            var go = JVGarden.Place("BeamWooden_13x16x380_03", root, from, 0f);
            go.name = name;
            Vector3 d = to - from;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            go.transform.localScale = new Vector3(thickness, d.magnitude / 3.8f, thickness);
            return go;
        }

        float postX = length * 0.5f + 0.9f, topY = height + 2.4f;
        foreach (float side in new[] { -1f, 1f })
        {
            int segments = Mathf.Max(1, Mathf.CeilToInt((topY + 0.4f) / 3.6f));
            float segLen = (topY + 0.4f) / segments;
            for (int s = 0; s < segments; s++)
                Timber("Post", new Vector3(side * postX, -0.1f + s * segLen, 0f), new Vector3(side * postX, -0.1f + (s + 1) * segLen, 0f), 2.6f);
            var postCollider = new GameObject("PostCollider");
            postCollider.transform.SetParent(root, false);
            postCollider.transform.localPosition = new Vector3(side * postX, topY * 0.5f, 0f);
            postCollider.AddComponent<BoxCollider>().size = new Vector3(0.42f, topY + 0.3f, 0.42f);
            postCollider.isStatic = true;
        }
        Timber("TopBar", new Vector3(-postX - 0.5f, topY, 0f), new Vector3(postX + 0.5f, topY, 0f), 2.2f);

        var beamGo = new GameObject("Beam");
        beamGo.transform.SetParent(root, false);
        beamGo.transform.localPosition = new Vector3(0f, height, 0f);
        beamGo.AddComponent<BoxCollider>().size = new Vector3(length + 0.1f, 0.54f, 0.54f);
        Timber("BeamModel", new Vector3(-length * 0.5f, height, 0f), new Vector3(length * 0.5f, height, 0f), thick);

        var ropes = new JVMeshKit();
        foreach (float side in new[] { -1f, 1f })
            ropes.Cylinder(JVMat.Rope, new Vector3(side * length * 0.36f, height, 0f), new Vector3(side * length * 0.36f, topY, 0f), 0.028f, 0.028f, 6);
        var ropeGo = Instance("Ropes", root, ropes, ground, root.rotation);

        var lanternY = height - 0.27f;
        var off = unlit.Place("LanternUnlit", root, Vector3.zero, Quaternion.identity);
        off.transform.SetLocalPositionAndRotation(new Vector3(0f, lanternY, 0f), Quaternion.identity);
        var on = lit.Place("LanternLit", root, Vector3.zero, Quaternion.identity, false);
        on.transform.SetLocalPositionAndRotation(new Vector3(0f, lanternY, 0f), Quaternion.identity);

        var beam = beamGo.AddComponent<TetherTrainingBeam>();
        beam.litMarker = on;
        var marker = beamGo.AddComponent<TetherInteractable>();
        marker.mode = TetherInteractable.TetherMode.StaticAnchor;
        return beam;
    }

    /// <summary>師父 + 把所有進度接到 ShrineTrial（要在僧侶放好之後呼叫）</summary>
    static void BuildTrial(Transform npcs)
    {
        var master = NpcAt(npcs, "NPC_ShrineMaster", trialMasterPosition, trialMasterYaw, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.White, obi = JVMat.ClothRed, hair = JVMat.HairGrey, hat = JapaneseVillageMeshes.NpcHat.HairBun,
        }, "守祠的師父", false, true, new[] { "……" }, new[] { "……" }, model: TripoLook("Master", MasterHeight, "守祠的師父"));

        var trial = new GameObject("ShrineTrial").AddComponent<ShrineTrial>();
        trial.master = master;
        trial.monk = trialMonk;
        trial.elder = trialElder;
        // 僧侶的過招：習武場完成後從祠堂後門走進來，在後門和木頭人之間的空地等玩家
        var spar = trialMonk.gameObject.AddComponent<MonkSparring>();
        spar.animator = trialMonk.GetComponentInChildren<Animator>();
        float ground = TrialGroundY, zMid = (YardZ0 + YardZ1) * 0.5f;
        spar.entryPoint = new Vector3(0f, ground, YardZ0 + 2.5f);
        spar.arenaCenter = new Vector3(-9f, ground, zMid + 5f);
        spar.arenaRadius = 6f;
        spar.attackLength = monkAttackLengths;
        spar.animationWalkSpeed = 1.72f * MonkHeight / VillagerHeight * NpcScale;
        spar.standOff = 1.6f * NpcScale;
        spar.reach = 2.4f * NpcScale;
        // 輕功跳上北邊的圍牆（離過招的空地約 13 m）
        spar.wallPerches = new[] { -17f, -9f, -1f }.Select(x => new Vector3(x, trialWallTop, YardZ1 - 0.15f)).ToArray();
        trialMonk.gameObject.AddComponent<ImpatientTalker>().animator = spar.animator; // 跟玩家說話時搔頭、一臉不耐煩
        trialMonk.gameObject.AddComponent<TetherInteractable>().mode = TetherInteractable.TetherMode.StaticAnchor; // 拉索不能把僧侶拖走
        trial.monkSpar = spar;
        trial.backDoor = trialBackDoor;
        trial.finalGate = trialGate;
        trial.dummies = trialDummies.ToArray();
        trial.beams = trialBeams.ToArray();
        trial.course = trialCourse;
        trial.readings = trialReadings.ToArray();
        trial.yardArea = new Rect(-TerraceHalfX, YardZ0, TerraceHalfX * 2f, YardZ1 - YardZ0);
    }

    static GameObject PackModel(string objName, string name, Transform parent, Material material)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{JVPack.Root}/Prefabs/{objName}.obj");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        go.name = name;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
        go.isStatic = true;
        return go;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Japanese Garden Pack 的岩石：縮放成指定的大小（公尺），碰撞用 Box</summary>
    static void PlacePackRock(Transform parent, string objName, Vector3 position, float yaw, float size)
    {
        var go = PackModel(objName, objName, parent, JVPack.Rock());
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        float natural = Mathf.Max(RendererBounds(go).size.x, RendererBounds(go).size.z);
        go.transform.localScale = Vector3.one * (size / Mathf.Max(0.01f, natural));
        var b = RendererBounds(go);
        go.transform.SetPositionAndRotation(position - new Vector3(b.center.x, b.min.y, b.center.z), Quaternion.identity);
        go.transform.RotateAround(position, Vector3.up, yaw);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(position + Vector3.up * b.size.y * 0.5f);
        box.size = new Vector3(b.size.x, b.size.y, b.size.z) * 0.85f / go.transform.localScale.x;
    }

    /// <summary>Japanese Garden Pack 的鳥居：放大到 7.6 m 高，主街和石階都從中間穿過</summary>
    static void PlaceTorii(Transform parent, float z)
    {
        const float height = 7.6f;
        var go = PackModel("torii", "Torii", parent, JVPack.Torii());
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        float natural = RendererBounds(go).size.y;
        go.transform.localScale = Vector3.one * (height / Mathf.Max(0.01f, natural));
        var b = RendererBounds(go);
        var bottom = new Vector3(0f, Ground(0f, z) - 0.05f, z);
        go.transform.position = bottom - new Vector3(b.center.x, b.min.y, b.center.z);

        // 長邊要橫跨石階（X 方向）
        if (b.size.z > b.size.x)
        {
            go.transform.RotateAround(bottom, Vector3.up, 90f);
            b = RendererBounds(go);
        }
        foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
        }
    }

    // ---------------------------------------------------------------- 樹

    const int TreeBamboo = 0, TreePineFirst = 1, TreePineCount = 3, TreeMapleFirst = 4, TreeMapleCount = 2, TreeSakuraFirst = 6, TreeSakuraCount = 4, TreeBambooSeedling = 10;

    /// <summary>做成 Terrain Tree 的樹，之後可以用 Terrain 的筆刷直接再刷。順序要對應上面的 Tree 常數</summary>
    static GameObject[] BuildTreePrefabs()
    {
        GameObject Pack(string tree, float radius, float colliderHeight) => JVPack.WrapTree(tree, TreePrefabDir, radius, colliderHeight, SaveMesh);
        return new[]
        {
            JVPack.WrapGardenTree("Bamboo_Group", "JV_Bamboo", TreePrefabDir, 0.28f, 5f, SaveMesh),
            Pack("BlackPineTree_A", 0.35f, 5f), Pack("BlackPineTree_C", 0.35f, 5f), Pack("BlackPineTree_D", 0.35f, 5f),
            Pack("JapaneseMapleTree_A", 0.3f, 5f), Pack("JapaneseMapleTree_B", 0.3f, 5f),
            Pack("SakuraTree_A", 0.4f, 6f), Pack("SakuraTree_C", 0.4f, 6f), Pack("SakuraTree_D", 0.4f, 6f), Pack("SakuraTree_F", 0.3f, 5f),
            JVPack.WrapGardenTree("Bamboo_Seedling", "JV_BambooSeedling", TreePrefabDir, 0.01f, 0.02f, SaveMesh, false),
        };
    }

    static void PlantTrees(GameObject[] prefabs)
    {
        var data = terrain.terrainData;
        var prototypes = new TreePrototype[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++) prototypes[i] = new TreePrototype { prefab = prefabs[i], bendFactor = 0f };
        data.treePrototypes = prototypes;

        var trees = new List<TreeInstance>();
        var rng = new System.Random(808);
        float R() => (float)rng.NextDouble();

        void Add(int prototype, float x, float z, float scale)
        {
            float h = terrain.SampleHeight(new Vector3(x, 0f, z)) / TerrainHeight;
            trees.Add(new TreeInstance
            {
                position = new Vector3((x + TerrainSize * 0.5f) / TerrainSize, h, (z + TerrainSize * 0.5f) / TerrainSize),
                prototypeIndex = prototype,
                widthScale = scale,
                heightScale = scale * (0.92f + R() * 0.2f),
                rotation = R() * Mathf.PI * 2f,
                color = new Color32(255, 255, 255, 255),
                lightmapColor = new Color32(255, 255, 255, 255),
            });
        }

        bool NearPath(float x, float z)
        {
            if (Mathf.Abs(x) < 6.5f && z > StairStartZ - 4f && z < StairEndZ + 6f) return true;
            if (new Vector2(x, z - ClearingZ).magnitude < 17f) return true;
            return TerraceDistance(x, z) < 2.5f; // 祠堂和習武場
        }

        // 竹林：後山全面都有，靠石階這段最密，走在路上就在竹林裡
        const float spacing = 3.4f;
        for (float z = StairStartZ + 4f; z < 197f; z += spacing)
            for (float x = -198f; x < 198f; x += spacing)
            {
                float px = x + (R() - 0.5f) * spacing * 0.8f;
                float pz = z + (R() - 0.5f) * spacing * 0.8f;
                if (NearPath(px, pz)) continue;
                float edge = Smooth01((pz - StairStartZ - 4f) / 14f);
                float grove = Mathf.PerlinNoise(px * 0.035f + 31f, pz * 0.035f + 77f);
                float nearStair = 1f - Smooth01((Mathf.Abs(px) - 8f) / 45f);
                float chance = edge * Mathf.Lerp(0.25f + grove * 0.75f, 0.95f, nearStair);
                if (R() > chance) continue;
                Add(TreeBamboo, px, pz, 1.55f + R() * 0.75f);
                // 底下的竹苗（林子才有層次），離石階近一點也可以
                if (R() < 0.45f)
                {
                    float sx = px + (R() - 0.5f) * spacing, sz = pz + (R() - 0.5f) * spacing;
                    if (!NearPath(sx, sz)) Add(TreeBambooSeedling, sx, sz, 1.1f + R() * 0.7f);
                }
            }

        // 兩側山丘：松樹
        for (int i = 0; i < 560; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float x = side * (118f + R() * 80f);
            float z = -95f + R() * 160f;
            if (z - ShoreZ(x) < 7f || ChiefDistance(x, z) < 9f) continue;
            if (R() < 0.12f) Add(TreeMapleFirst + rng.Next(TreeMapleCount), x, z, 0.7f + R() * 0.4f);
            else Add(TreePineFirst + rng.Next(TreePineCount), x, z, 0.9f + R() * 0.7f);
        }

        // 海岸道路旁的黑松防風林
        for (float x = -122f; x < 122f; x += 7f)
        {
            float px = x + (R() - 0.5f) * 5f;
            if (Mathf.Abs(px) < 8f || Mathf.Abs(px - PierX) < 7f) continue;
            float pz = -45f - R() * 2.4f;
            if (pz - ShoreZ(px) < 13f) continue;
            if (R() < 0.6f) Add(TreePineFirst + rng.Next(TreePineCount), px, pz, 0.7f + R() * 0.4f);
        }

        // 楓樹：竹林和村子交界
        for (float x = -96f; x < 96f; x += 11f)
        {
            float px = x + (R() - 0.5f) * 7f;
            if (Mathf.Abs(px) < 11f) continue;
            if (R() < 0.55f) Add(TreeMapleFirst + rng.Next(TreeMapleCount), px, StairStartZ + 1f + R() * 3f, 0.65f + R() * 0.35f);
        }

        // 櫻花：山腳、神社空地、海岸道路的路口
        foreach (var (x, z) in new[]
        {
            (-11f, 53.5f), (11.5f, 53.2f), (-34f, 53f), (35f, 53.6f), (-62f, 53f), (66f, 53.2f),
            (-13f, 122f), (13f, 122f), (-24f, 138f), (24f, 140f),
            (-68f, 182f), (68f, 182f), (-68f, 155f), (68f, 155f), // 習武場的四個角落
            (-58f, -44f), (58f, -44f),
        })
            Add(TreeSakuraFirst + rng.Next(TreeSakuraCount), x, z, 0.62f + R() * 0.18f);

        data.SetTreeInstances(trees.ToArray(), true);
        terrain.Flush();
        var collider = terrain.GetComponent<TerrainCollider>();
        collider.enabled = false;
        collider.enabled = true;
        Debug.Log($"[JapaneseVillage] 種了 {trees.Count} 棵樹");
    }

    /// <summary>草地上的短草（Terrain Details，會隨風搖）</summary>
    static void PaintDetails()
    {
        var data = terrain.terrainData;
        data.detailPrototypes = new[]
        {
            new DetailPrototype
            {
                prototypeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{JVPack.Root}/Textures/GrassBillboards/billboardgrass0001.png"),
                renderMode = DetailRenderMode.GrassBillboard,
                usePrototypeMesh = false,
                minWidth = 0.45f,
                maxWidth = 0.8f,
                minHeight = 0.3f,
                maxHeight = 0.6f,
                healthyColor = new Color(0.86f, 1f, 0.66f),
                dryColor = new Color(0.95f, 0.95f, 0.62f),
                noiseSpread = 0.25f,
            },
        };

        // 每一格的數值代表「幾叢草」（新版 Unity 的預設是覆蓋率 0~255，小數字會幾乎看不到）
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        int res = data.detailResolution;
        var layer = new int[res, res];
        var rng = new System.Random(303);
        for (int zi = 0; zi < res; zi++)
            for (int xi = 0; xi < res; xi++)
            {
                float x = -TerrainSize * 0.5f + TerrainSize * (xi + 0.5f) / res;
                float z = -TerrainSize * 0.5f + TerrainSize * (zi + 0.5f) / res;
                float d = z - ShoreZ(x);
                if (d < 22f || RoadMask(x, z, 0.8f) > 0.02f) continue;
                bool blocked = false;
                foreach (var f in footprints)
                    if (f.Contains(new Vector2(x, z))) { blocked = true; break; }
                if (blocked) continue;
                float patch = Mathf.PerlinNoise(x * 0.09f + 11f, z * 0.09f + 3f);
                float forest = Smooth01((z - StairStartZ - 8f) / 14f);
                float density = Mathf.Lerp(patch * 1.3f - 0.1f, patch * 0.5f - 0.15f, forest);
                layer[zi, xi] = Mathf.Max(0, Mathf.FloorToInt(density + (float)rng.NextDouble()));
            }
        data.SetDetailLayer(0, 0, 0, layer);
    }

    // ---------------------------------------------------------------- NPC

    static void PlaceNpcs(Transform parent)
    {
        EnsureHumanImport(VillagerModel, $"{VillagerDir}/T_JV_Villager_Normal.png");
        EnsureHumanImport(MonkModelPath, $"{MonkDir}/T_JV_Monk_Normal.png", $"{MonkDir}/T_JV_Monk_MetallicSmooth.png");
        EnsureHumanImport(ElderModelPath, $"{ElderDir}/T_JV_Elder_Normal.png", $"{ElderDir}/T_JV_Elder_MetallicSmooth.png");
        foreach (var n in new[] { "VillagerWoman", "Blacksmith", "Fisherman", "Woodcutter", "Merchant", "Master", "ChildGirl" })
            EnsureHumanImport($"Assets/_Project/Models/JapaneseVillage/{n}/{n}.fbx", $"Assets/_Project/Models/JapaneseVillage/{n}/T_JV_{n}_Normal.png", $"Assets/_Project/Models/JapaneseVillage/{n}/T_JV_{n}_MetallicSmooth.png");
        villagerController = VillagerController();
        monkController = MonkController();
        NpcAt(parent, "NPC_Merchant", new Vector3(-7.2f, 0f, -33.4f), 150f, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothOchre, obi = JVMat.ClothBrown, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.Kasa, backpack = true,
        }, "行商", true, true,
            new[]
            {
                "喲，旅人！這漁村難得有外地人來，你運氣不錯，今天正好遇到我開張。",
                "我用草藥跟人換貨。後山竹林的石階兩旁長著草藥，靠近按 E 就能採。",
                "想補充體力的話就拿草藥來找我。別客氣，看看我的貨吧！",
            },
            new[] { "又見面了，旅人。草藥帶夠了嗎？" }, model: TripoLook("Merchant", MerchantHeight, "行商"));
        var stall = Instance("MerchantStall", parent, JapaneseVillageMeshes.MerchantStall(), new Vector3(-10.5f, Ground(-10.5f, -33f), -33f), Quaternion.Euler(0f, 186f, 0f));
        var stallCollider = stall.AddComponent<BoxCollider>();
        stallCollider.center = new Vector3(0f, 0.5f, 0f);
        stallCollider.size = new Vector3(2.5f, 1f, 1.05f);

        // 老漁夫：坐在棧橋末端，腳垂在水面上，手上拿著釣竿（釣竿跟著右手，但不是他的子物件，半身像才不會被拉遠）
        var fishermanSpec = new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothIndigo, obi = JVMat.Rope, hair = JVMat.HairGrey, hat = JapaneseVillageMeshes.NpcHat.Kasa, fishBasket = true,
        };
        var fisherSeat = new Vector3(PierX + 0.2f, PierDeckY, PierEndZ + 0.35f);
        var fisherman = NpcAt(parent, "NPC_Fisherman", fisherSeat, 180f, fishermanSpec, "老漁夫", false, false,
            new[]
            {
                "噓……小聲點，魚都被你嚇跑了。",
                "我在這片海灣釣了四十年。太陽西斜的時候，鯛魚最容易上鉤。",
                "想看村子全貌的話，就沿著主街一路往北，穿過鳥居爬上石階。",
            },
            new[] { "今天的魚不太咬鉤啊。" }, model: TripoLook("Fisherman", FishermanHeight, "老漁夫"));
        var fishing = AddRoutine(fisherman, FishermanHeight, At(fisherSeat, 180f, 9999f, NpcRoutine.Act.Sit, PierDeckY));
        fishing.ignorePlayer = true;
        BuildFishingRod(parent, fisherman, fishing.animator);

        float netX = -40f, netZ = ShoreZ(netX) + 9.8f;
        NpcAt(parent, "NPC_NetMender", new Vector3(netX, 0f, netZ), 150f, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothTeal, obi = JVMat.ClothGrey, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.Headband, fishBasket = true,
        }, "年輕漁夫", false, true,
            new[]
            {
                "昨天出海網子被礁石勾破了，得趁太陽好的時候趕快補起來。",
                "沙灘盡頭的礁石區水很淺，可以走過去，但再往外就不行了——那邊可是真正的外海。",
            },
            new[] { "這破洞比我想的還大……" }, model: VillagerLook("Indigo"));

        var teaHost = NpcAt(parent, "NPC_TeaHost", new Vector3(7f, 0f, TeaZ), 215f, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothPurple, obi = JVMat.ClothOchre, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.HairBun,
        }, "茶屋老闆娘", false, true,
            new[]
            {
                "歡迎光臨！走累了就在紅傘下坐坐吧。",
                "這條街往東西兩邊走，就是村裡的三排老屋。掛著暖簾的都是店家，紅燈籠的那家酒特別好喝。",
            },
            new[] { "茶還熱著呢，慢慢來。" }, model: WomanLook("茶屋老闆娘"));

        // 鐵匠鋪門口的女鐵匠：教玩家換武器、說明武器特性（台詞在 BlacksmithShop）
        var smith = NpcAt(parent, "NPC_Blacksmith", new Vector3(SmithyX + 0.4f, 0f, SmithyFrontZ - 1.9f), 180f, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothBrown, obi = JVMat.Black, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.Headband,
        }, "女鐵匠", false, true, new[] { "……" }, new[] { "……" },
            model: new HumanModel { path = BlacksmithModel, material = TripoMaterial("Blacksmith"), controller = villagerController, height = BlacksmithHeight, label = "女鐵匠" });
        smith.gameObject.AddComponent<BlacksmithShop>();

        // 台詞：ShrineTrial 會換成 ElderLines（第一次講往事和能做的事，之後依進度提示下一步）；這裡的是備用
        trialElder = NpcAt(parent, "NPC_Elder", new Vector3(ChiefHouseX - 19.5f, ChiefGroundY, ChiefAxisZ + 2.6f), 270f, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothGrey, obi = JVMat.ClothIndigo, hair = JVMat.HairGrey, hat = JapaneseVillageMeshes.NpcHat.Bald, cane = true,
        }, "村長", false, true,
            new[]
            {
                "歡迎來到寒舍。這村子靠海吃海，靠山吃山：南邊的海養活了漁夫，北邊的竹林供我們蓋房子、做魚簍。",
                "這棟老宅是祖上傳下來的，村裡有事大家都會來這裡商量。後山的祠堂供奉著玄清道人，保佑著全村，有空就上去拜一拜吧。",
                "後院那扇老門從我爺爺那一代就鎖著了，鑰匙在石階上那位僧侶身上。他說時候到了自然會開。",
            },
            new[] { "年輕人，腳步放慢點，好好看看這村子。" }, 1.2f, // 村長比其他村民再大 1.2 倍
            model: new HumanModel { path = ElderModelPath, material = TripoMaterial("Elder"), controller = villagerController, height = ElderHeight, label = "村長" });
        AddDailyRoutines(teaHost, smith, trialElder);

        NpcAt(parent, "NPC_Child", new Vector3(-27f, 0f, -4f), 70f, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothRed, obi = JVMat.White, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.HairBun,
        }, "村裡的小孩", false, true,
            new[]
            {
                "哇，你的劍好帥！你是從海的那邊來的嗎？",
                "我知道竹林裡有祕密基地喔，不過阿爺說不可以爬太高……",
            },
            new[] { "下次再帶我去冒險！" }, 0.68f, model: TripoLook("ChildGirl", ChildGirlHeight, "村裡的小孩"));
        AddChildPlay(GameObject.Find("NPC_Child").GetComponent<MerchantNPC>(), parent);

        NpcAt(parent, "NPC_Woodcutter", new Vector3(6f, 0f, 51.5f), 215f, new JapaneseVillageMeshes.NpcSpec
        {
            robe = JVMat.ClothGreen, obi = JVMat.ClothBrown, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.Headband, woodBundle = true,
        }, "砍竹人", false, true,
            new[]
            {
                "要上後山嗎？穿過鳥居沿著石階走就不會迷路。",
                "竹林密得很，一離開石階就看不到方向。不過底下長著不少草藥，有需要就順手採一些吧。",
            },
            new[] { "今年的竹子長得真好。" }, model: TripoLook("Woodcutter", WoodcutterHeight, "砍竹人"));

        PlaceWalkers(parent);
        BuildVillageLife(parent);
        BuildTrial(parent);
    }

    /// <summary>會實際走動的村民：路線都走在道路中間，路線上不能有房子或樹</summary>
    static void PlaceWalkers(Transform parent)
    {
        villageWalkers.Clear(); // 每次 Build 重新收集（僧侶也在這裡建立）
        WalkerAt(parent, "Walker_WaterCarrier", new[] { new Vector2(-82f, -38.5f), new Vector2(-16f, -38.5f), new Vector2(40f, -38.5f), new Vector2(84f, -38.5f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothTeal, obi = JVMat.White, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.HairBun },
            "挑水的婦人", new[] { "海風把衣服吹得真快乾。你是來買魚的嗎？", "從這條海岸路一直往東走，就能走到村長家門口。" }, new[] { "水缸快見底了，得再跑一趟。" }, 1.25f, model: WomanLook("挑水的婦人"));

        WalkerAt(parent, "Walker_FishSeller", new[] { new Vector2(-88f, -8.5f), new Vector2(-20f, -7.5f), new Vector2(30f, -8.5f), new Vector2(88f, -8f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothBrown, obi = JVMat.Rope, hair = JVMat.HairGrey, hat = JapaneseVillageMeshes.NpcHat.Kasa },
            "賣魚的阿伯", new[] { "新鮮的鯛魚喔——今早才上岸的！", "我每天從街頭走到街尾，村裡誰家晚上吃什麼我都知道。" }, new[] { "今天的魚賣得差不多囉。" }, 1.1f, model: VillagerLook("Brown"));

        WalkerAt(parent, "Walker_Girl", new[] { new Vector2(86f, 20.5f), new Vector2(32f, 19.5f), new Vector2(-30f, 20.5f), new Vector2(-88f, 20f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothRed, obi = JVMat.ClothOchre, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.HairBun },
            "賣花的姑娘", new[] { "這些是後山摘的野花，要不要買一枝？", "櫻花開的時候，山腳那排最好看喔。" }, new[] { "再多走兩條街就賣完了。" }, 1.35f, model: WomanLook("賣花的姑娘"));

        WalkerAt(parent, "Walker_Watchman", new[] { new Vector2(2.4f, -36f), new Vector2(2.4f, 46f), new Vector2(-2.4f, 46f), new Vector2(-2.4f, -36f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothGrey, obi = JVMat.ClothIndigo, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.Headband },
            "巡邏的村丁", new[] { "村長吩咐我每天巡一遍主街。最近海上不太平靜。", "有什麼可疑的事就去東邊盡頭的宅院找村長，那是全村最大的房子，不會認錯。" }, new[] { "今天也沒事，這樣最好。" }, 1.4f, loop: true, model: VillagerLook("Grey"));

        WalkerAt(parent, "Walker_Fisher", new[] { new Vector2(19f, -38.5f), new Vector2(19f, -50f), new Vector2(-30f, -53f), new Vector2(-44f, -55f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothIndigo, obi = JVMat.Rope, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.Headband },
            "收網的漁夫", new[] { "今天風向好，漁獲不錯。", "我得在退潮前把網子收到曬網架上，失陪啦。" }, new[] { "這網子沉得很！" }, 1.2f, model: VillagerLook("Indigo"));

        WalkerAt(parent, "Walker_Boy", new[] { new Vector2(50.5f, -36f), new Vector2(50.5f, 46f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothGreen, obi = JVMat.White, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.None },
            "跑腿的男孩", new[] { "我要幫老闆送貨！小巷子是最快的捷徑！" }, new[] { "讓一讓讓一讓——" }, 2.4f, 0.72f);

        WalkerAt(parent, "Walker_Granny", new[] { new Vector2(94f, -36f), new Vector2(94f, 6f), new Vector2(94f, 46f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothPurple, obi = JVMat.ClothGrey, hair = JVMat.HairGrey, hat = JapaneseVillageMeshes.NpcHat.HairBun },
            "散步的老奶奶", new[] { "我每天都到村長家門口走走，年輕時那棵大櫻花就已經在院子裡了。" }, new[] { "人老了，走慢點也好。" }, 0.75f, 0.94f);

        WalkerAt(parent, "Walker_Servant", new[] { new Vector2(102.5f, 0.5f), new Vector2(111f, -1f), new Vector2(112.5f, -6f), new Vector2(111f, -1f), new Vector2(102.5f, 2.2f), new Vector2(110.5f, 13f), new Vector2(102.5f, 10f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothOchre, obi = JVMat.ClothBrown, hair = JVMat.Hair, hat = JapaneseVillageMeshes.NpcHat.Headband },
            "宅院的家僕", new[] { "這座院子每天都要掃一遍，落櫻再美也是工作啊。", "村長大人在主屋前面，有事可以直接跟他說。" }, new[] { "掃不完，真的掃不完。" }, 1f, model: VillagerLook("Olive"));

        trialMonk = WalkerAt(parent, "Walker_Monk", new[] { new Vector2(0f, StairStartZ - 3f), new Vector2(0f, StairEndZ), new Vector2(0f, ClearingZ - 2f) },
            new JapaneseVillageMeshes.NpcSpec { robe = JVMat.ClothGrey, obi = JVMat.ClothPurple, hair = JVMat.HairGrey, hat = JapaneseVillageMeshes.NpcHat.Bald },
            "僧侶", new[] { "石階一共九十多階，每走一階，心就靜一分。", "山上的祠堂清幽，竹林的風聲很好聽。" }, new[] { "阿彌陀佛。" }, 0.95f, followColliders: true,
            model: new HumanModel { path = MonkModelPath, material = TripoMaterial("Monk"), controller = monkController, height = MonkHeight, label = "僧侶" });
    }

    static readonly List<VillagerWalker> villageWalkers = new List<VillagerWalker>();

    /// <summary>小孩：東張西望 → 跑去蹲下看地上的石頭 → 跑到另一邊蹦蹦跳跳 → 再跑去看另一顆石頭</summary>
    static void AddChildPlay(MerchantNPC child, Transform parent)
    {
        Vector3 home = child.transform.position;
        Vector3 rock1 = G(-33f, -6.4f), rock2 = G(-22.5f, -2.2f);
        foreach (var (at, yaw) in new[] { (rock1, 30f), (rock2, 200f) })
        {
            var stone = JVGarden.Place("Stone_03", parent, at + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.55f + Vector3.up * 0.05f, yaw);
            stone.transform.localScale *= 0.55f;
            JVGarden.ClearStatic(stone);
        }
        var r = AddRoutine(child, ChildGirlHeight,
            At(home, 70f, 4f, NpcRoutine.Act.Stand),
            At(rock1, 30f, 6f, NpcRoutine.Act.Crouch),
            At(G(-26f, -6.6f), 160f, 3.5f, NpcRoutine.Act.Hop),
            At(rock2, 200f, 5f, NpcRoutine.Act.Crouch),
            At(G(-29.5f, -3.2f), 250f, 3f, NpcRoutine.Act.Hop));
        for (int i = 1; i < r.stops.Length; i++) r.stops[i].speed = 2.6f; // 用跑的
        r.stops[0].speed = 2.6f;
        r.animationRunSpeed = 3.4f * ChildGirlHeight / VillagerHeight;
    }

    /// <summary>
    /// 村民之間的互動（VillageLife）：走動的村民會停下來互相聊天，或去拜訪站著工作的人
    /// （海邊補網的年輕漁夫、行商的攤子、砍竹人、鐵匠鋪門口的武器架）。
    /// </summary>
    static void BuildVillageLife(Transform parent)
    {
        VillagerWalker W(string name) => villageWalkers.Find(w => w != null && w.name == name);
        var life = new GameObject("VillageLife").AddComponent<VillageLife>();
        life.transform.SetParent(parent, false);
        life.walkers = villageWalkers.FindAll(w => w != null && w.name != "Walker_Monk" && w.name != "Walker_Servant").ToArray();
        foreach (var w in life.walkers)
            w.gameObject.AddComponent<TalkGestures>().animator = w.GetComponentInChildren<Animator>();

        var spots = new List<VillageLife.Spot>();
        void Host(string hostName, string label, Vector3 offset, params string[] visitors)
        {
            var host = GameObject.Find(hostName);
            if (host == null) return;
            if (host.GetComponent<MerchantNPC>() != null && host.GetComponent<TalkGestures>() == null && host.GetComponent<NpcRoutine>() == null)
            {
                var g = host.AddComponent<TalkGestures>();
                g.animator = host.GetComponentInChildren<Animator>();
                g.turnToVisitor = true;
            }
            Vector3 at = host.transform.position + offset;
            spots.Add(new VillageLife.Spot
            {
                name = label, host = host.transform, standAt = G(at.x, at.z),
                visitors = System.Array.FindAll(System.Array.ConvertAll(visitors, W), v => v != null),
            });
        }
        // 巡邏的村丁、賣魚的阿伯會去海邊看看補網的進度（視察海邊的工作）
        Host("NPC_NetMender", "海邊看人補網", new Vector3(0f, 0f, 2f), "Walker_Watchman", "Walker_FishSeller", "Walker_Fisher", "Walker_WaterCarrier");
        Host("NPC_Merchant", "行商的攤子", new Vector3(1.2f, 0f, 1.6f), "Walker_WaterCarrier", "Walker_Girl", "Walker_Granny", "Walker_Boy", "Walker_FishSeller");
        Host("NPC_Woodcutter", "找砍竹人聊天", new Vector3(-1.6f, 0f, -1.2f), "Walker_Watchman", "Walker_Girl", "Walker_Boy");
        Host("Rack_Greatsword", "鐵匠鋪看兵器", new Vector3(0f, 0f, -1.8f), "Walker_Watchman", "Walker_FishSeller", "Walker_Fisher");
        life.spots = spots.ToArray();
        Debug.Log($"[JapaneseVillage] 村民互動：{life.walkers.Length} 個走動的村民、{spots.Count} 個拜訪點");
    }

    static MerchantNPC WalkerAt(Transform parent, string name, Vector2[] route, in JapaneseVillageMeshes.NpcSpec spec, string speaker, string[] guide, string[] repeat,
                         float speed, float scale = 1f, bool followColliders = false, bool loop = false, HumanModel model = null)
    {
        var points = new Vector3[route.Length];
        for (int i = 0; i < route.Length; i++) points[i] = new Vector3(route[i].x, 0f, route[i].y);
        points[0].y = Ground(points[0].x, points[0].z);

        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        Vector3 toNext = points[1] - points[0];
        toNext.y = 0f;
        root.transform.SetPositionAndRotation(points[0], Quaternion.LookRotation(toNext));
        root.transform.localScale = Vector3.one * scale * NpcScale;

        GameObject Part(string partName, Transform partParent, JVMeshKit kit, Vector3 local)
        {
            var part = Instance(partName, partParent, kit, Vector3.zero, Quaternion.identity, false);
            part.transform.SetLocalPositionAndRotation(local, Quaternion.identity);
            return part;
        }

        // model 有指定：用綁好骨架的人形模型；沒有：用程式拼的身體和手腳
        Animator animator = null;
        GameObject body = null, armL = null, armR = null, footL = null, footR = null;
        if (model != null) animator = BuildHumanModel(root.transform, model);
        else
        {
            body = Part("Body", root.transform, JapaneseVillageMeshes.Villager(spec, true), Vector3.zero);
            armL = Part("ArmLeft", body.transform, JapaneseVillageMeshes.VillagerArm(spec.robe, -1f), JapaneseVillageMeshes.ArmShoulderLeft);
            armR = Part("ArmRight", body.transform, JapaneseVillageMeshes.VillagerArm(spec.robe, 1f), JapaneseVillageMeshes.ArmShoulderRight);
            footL = Part("FootLeft", root.transform, JapaneseVillageMeshes.VillagerFoot(), JapaneseVillageMeshes.FootLeft);
            footR = Part("FootRight", root.transform, JapaneseVillageMeshes.VillagerFoot(), JapaneseVillageMeshes.FootRight);
        }

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.34f;

        var npc = root.AddComponent<MerchantNPC>();
        npc.speakerName = speaker;
        npc.sellsGoods = false;
        npc.facePlayerOnTalk = true;
        npc.guideLines = guide;
        npc.repeatLines = repeat;
        npc.interactRange = 3f;

        var walker = root.AddComponent<VillagerWalker>();
        villageWalkers.Add(walker);
        walker.route = points;
        walker.speed = speed;
        walker.loop = loop;
        walker.followColliders = followColliders;
        walker.animator = animator;
        if (model != null) walker.animationWalkSpeed = 1.72f * model.height / VillagerHeight; // 走路動畫的步幅跟身高成正比
        if (body != null)
        {
            walker.body = body.transform;
            walker.armLeft = armL.transform;
            walker.armRight = armR.transform;
            walker.footLeft = footL.transform;
            walker.footRight = footR.transform;
        }
        return npc;
    }

    /// <summary>position.y = 0 表示貼地形，否則就用指定的高度（站在棧橋上）</summary>
    static MerchantNPC NpcAt(Transform parent, string name, Vector3 position, float yaw, in JapaneseVillageMeshes.NpcSpec spec, string speaker,
                      bool sells, bool faceOnTalk, string[] guide, string[] repeat, float scale = 1f, GameObject portrait = null, HumanModel model = null)
    {
        if (position.y == 0f) position.y = Ground(position.x, position.z);
        GameObject go;
        if (model != null)
        {
            // 綁好骨架的人形模型：站著播待機動畫（Animator 的 Speed 預設 0）
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale * NpcScale;
            BuildHumanModel(go.transform, model);
        }
        else
        {
            go = Instance(name, parent, JapaneseVillageMeshes.Villager(spec), position, Quaternion.Euler(0f, yaw, 0f), false);
            go.transform.localScale = Vector3.one * scale * NpcScale;
        }

        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.34f;

        var npc = go.AddComponent<MerchantNPC>();
        npc.speakerName = speaker;
        npc.sellsGoods = sells;
        npc.facePlayerOnTalk = faceOnTalk;
        npc.guideLines = guide;
        npc.repeatLines = repeat;
        npc.interactRange = 3f;
        npc.portraitPrefab = portrait;
        return npc;
    }

    /// <summary>老漁夫的釣竿（竹竿 + 釣線 + 紅色浮標），HeldProp 讓它跟著右手</summary>
    static void BuildFishingRod(Transform parent, MerchantNPC fisherman, Animator animator)
    {
        var rodKit = new JVMeshKit();
        rodKit.Cylinder(JVMat.BambooCulm, new Vector3(0f, -0.35f, 0f), new Vector3(0f, 3.1f, 0f), 0.026f, 0.008f, 6);
        rodKit.Cylinder(JVMat.Rope, new Vector3(0f, -0.2f, 0f), new Vector3(0f, 0.15f, 0f), 0.03f, 0.03f, 6);
        var rod = Instance("FishingRod", parent, rodKit, fisherman.transform.position, Quaternion.identity, false);
        var bobberKit = new JVMeshKit();
        bobberKit.Ellipsoid(JVMat.ClothRed, Vector3.zero, new Vector3(0.05f, 0.07f, 0.05f), 4, 7);
        bobberKit.Ellipsoid(JVMat.White, new Vector3(0f, 0.05f, 0f), new Vector3(0.03f, 0.03f, 0.03f), 3, 6);
        var bobber = Instance("Bobber", parent, bobberKit, fisherman.transform.position, Quaternion.identity, false);
        var line = rod.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = line.endWidth = 0.008f;
        line.sharedMaterial = JapaneseVillageAssets.Get(JVMat.White);
        line.shadowCastingMode = ShadowCastingMode.Off;
        var held = rod.AddComponent<HeldProp>();
        held.animator = animator;
        held.owner = fisherman.transform;
        held.direction = new Vector3(0.08f, 0.5f, 0.86f);
        held.tipLocal = new Vector3(0f, 3.1f, 0f);
        held.line = line;
        held.bobber = bobber.transform;
        held.waterY = 0f;
    }

    // ---------------------------------------------------------------- 村民的日常（NpcRoutine）

    static NpcRoutine.Stop At(Vector3 p, float yaw, float duration, NpcRoutine.Act act, float seatY = 0f) =>
        new NpcRoutine.Stop { position = p, yaw = yaw, duration = duration, act = act, seatY = seatY };

    static NpcRoutine.Stop Pass(Vector3 p) => At(p, 0f, 0f, NpcRoutine.Act.Pass);

    /// <summary>地面（Terrain）上的點</summary>
    static Vector3 G(float x, float z) => new Vector3(x, Ground(x, z), z);

    static NpcRoutine AddRoutine(MerchantNPC npc, float modelHeight, params NpcRoutine.Stop[] stops)
    {
        var r = npc.gameObject.AddComponent<NpcRoutine>();
        r.animator = npc.GetComponentInChildren<Animator>();
        r.animationWalkSpeed = 1.72f * modelHeight / VillagerHeight;
        r.stops = stops;
        return r;
    }

    /// <summary>(x, z) 往下打射線找最高的碰撞面（不算 ignore 底下的碰撞體）；從 fromY 往下找</summary>
    static float SurfaceY(float x, float z, float fromY, Transform ignore)
    {
        float best = float.NegativeInfinity;
        foreach (var h in Physics.RaycastAll(new Vector3(x, fromY, z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return best > float.NegativeInfinity ? best : Ground(x, z);
    }

    /// <summary>
    /// 茶屋老闆娘、女鐵匠、村長的日常路線（沒跟玩家說話時）：
    ///   老闆娘：攤子前招呼 → 擦長椅 → 坐在長椅上歇一會 → 進茶屋（人進屋裡一陣子）→ 出來回攤子
    ///   女鐵匠：門口招呼 → 在鐵砧上打鐵 → 走進鋪子顧爐火 → 出來
    ///   村長：主殿前看看院子 → 走進主殿裡 → 出來到大櫻花旁的長椅坐一會
    /// </summary>
    static void AddDailyRoutines(MerchantNPC tea, MerchantNPC smith, MerchantNPC elder)
    {
        Physics.SyncTransforms();

        // 茶屋老闆娘
        float seat1 = Ground(9.5f, TeaZ) + 0.495f; // TeaBench 的坐面高度
        Vector3 door = teaHouseDoor != Vector3.zero ? teaHouseDoor : G(10f, -0.6f);
        AddRoutine(tea, VillagerWomanHeight,
            At(G(7f, TeaZ), 215f, 7f, NpcRoutine.Act.Stand),
            Pass(G(10.5f, TeaZ + 1.3f)),
            At(G(13.5f, TeaZ + 0.85f), 180f, 5f, NpcRoutine.Act.Wipe),
            Pass(G(11.4f, TeaZ + 1.2f)),
            Pass(G(9.5f, TeaZ + 0.9f)),
            At(G(9.5f, TeaZ), 180f, 9f, NpcRoutine.Act.Sit, seat1),
            Pass(G(9.5f, TeaZ + 1.2f)),
            Pass(new Vector3(door.x, door.y, door.z - 1.6f)),
            At(door, 0f, 6f, NpcRoutine.Act.Inside),
            Pass(new Vector3(door.x, door.y, door.z - 1.6f)),
            Pass(G(7.5f, TeaZ + 1f)));
        Debug.Log($"[JapaneseVillage] 茶屋老闆娘的日常：茶屋門口 {door}");

        // 女鐵匠（鋪子轉 180 度：門口在 -Z，屋裡往 +Z；地板比地面高 Floor）
        float baseY = Ground(SmithyX, SmithyZ), floorY = baseY + JapaneseVillageHouses.Floor;
        float fz = SmithyFrontZ;
        Vector3 outside = G(SmithyX, fz - 0.7f), inside = new Vector3(SmithyX, floorY, fz + 1.0f);
        AddRoutine(smith, BlacksmithHeight,
            At(G(SmithyX + 0.4f, fz - 1.9f), 180f, 6f, NpcRoutine.Act.Stand),
            At(G(SmithyX - 2.4f, fz - 2.05f), 180f, 9f, NpcRoutine.Act.Hammer),
            Pass(outside),
            Pass(inside),
            At(new Vector3(SmithyX, floorY, fz + HouseDepth - 2.95f), 0f, 7f, NpcRoutine.Act.Tend),
            Pass(inside),
            Pass(outside));

        // 村長：主殿朝西（正面在 -X），沿著中軸往東量台階和地板的高度
        var elderRoot = elder.transform;
        float g0 = ChiefGroundY, z = ChiefAxisZ;
        var samples = new System.Collections.Generic.List<Vector2>();
        for (float x = ChiefHouseX - 18f; x < ChiefHouseX - 2f; x += 0.2f) samples.Add(new Vector2(x, SurfaceY(x, z, g0 + 2.4f, elderRoot)));
        float stepX = float.NaN, hallFloor = g0, edgeX = float.NaN, stepTop = g0;
        foreach (var smp in samples)
        {
            if (float.IsNaN(stepX) && smp.y > g0 + 0.12f) { stepX = smp.x; stepTop = smp.y; }
            if (!float.IsNaN(stepX)) hallFloor = Mathf.Max(hallFloor, smp.y);
        }
        foreach (var smp in samples) if (!float.IsNaN(stepX) && smp.x >= stepX && smp.y >= hallFloor - 0.04f) { edgeX = smp.x; break; }
        var elderStops = new System.Collections.Generic.List<NpcRoutine.Stop>
        {
            At(elderRoot.position, 270f, 8f, NpcRoutine.Act.Stand),
        };
        if (!float.IsNaN(edgeX))
        {
            // 走上台階進主殿（每 0.5 m 一個點，高度照量到的）
            var path = new System.Collections.Generic.List<Vector3>();
            for (float x = stepX - 0.4f; x <= edgeX + 3.5f; x += 0.5f) path.Add(new Vector3(x, Mathf.Max(g0, SurfaceY(x, z, g0 + 2.4f, elderRoot)), z));
            elderStops.Add(Pass(new Vector3(stepX - 1.2f, g0, z)));
            foreach (var pt in path) elderStops.Add(Pass(pt));
            elderStops.Add(At(path[path.Count - 1], 90f, 7f, NpcRoutine.Act.Stand));
            // 下台階回院子，到大櫻花旁的長椅坐一會
            for (int i = path.Count - 1; i >= 0; i--) elderStops.Add(Pass(path[i]));
            elderStops.Add(Pass(new Vector3(stepX - 1.2f, g0, z)));
        }
        if (elderBenchPos != Vector3.zero)
        {
            Vector3 b = elderBenchPos;
            elderStops.Add(Pass(new Vector3(b.x, b.y, b.z + 1.5f)));
            elderStops.Add(At(b, 0f, 11f, NpcRoutine.Act.Sit, b.y + 0.495f));
            elderStops.Add(Pass(new Vector3(b.x, b.y, b.z + 1.5f)));
        }
        Debug.Log($"[JapaneseVillage] 村長的日常：台階 x {stepX:F1}（高 {stepTop - g0:F2} m）、主殿地板邊緣 x {edgeX:F1}（高 {hallFloor - g0:F2} m），長椅 {elderBenchPos}，共 {elderStops.Count} 站");
        AddRoutine(elder, ElderHeight, elderStops.ToArray());
    }

    static GameObject SavePortraitPrefab(string name, in JapaneseVillageMeshes.NpcSpec spec)
    {
        var kit = JapaneseVillageMeshes.Villager(spec);
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(kit.Build(name));
        go.AddComponent<MeshRenderer>().sharedMaterials = kit.Materials;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>竹林石階旁的草藥（和 Planet 一樣按 E 採集，拿去跟行商換藥水）</summary>
    static void PlaceHerbs()
    {
        var root = new GameObject("Herbs").transform;
        var positions = new[]
        {
            new Vector2(-4.8f, 66f), new Vector2(5.2f, 75f), new Vector2(-5.4f, 83f), new Vector2(4.6f, 91f),
            new Vector2(-4.4f, 99f), new Vector2(5.6f, 107f), new Vector2(-8f, 124f), new Vector2(14f, 128f),
        };
        var model = new SharedModel("Herb", JapaneseVillageMeshes.Herb());
        for (int i = 0; i < positions.Length; i++)
        {
            var position = new Vector3(positions[i].x, Ground(positions[i].x, positions[i].y), positions[i].y);
            var go = model.Place($"Herb_{i + 1}", root, position, Quaternion.Euler(0f, i * 73f, 0f), false);
            var plant = go.AddComponent<HarvestablePlant>();
            plant.snapToTerrain = true;
            plant.interactRange = 2f;
        }
    }

    // ---------------------------------------------------------------- 邊界與玩家

    static void BoundsBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, rotation);
        go.layer = 2; // Ignore Raycast：玩家會被擋住，鏡頭和其他射線不受影響
        go.isStatic = true;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
    }

    /// <summary>看不見的牆：可以涉水但不能走進深海，也不能走出地形範圍或從棧橋掉下去</summary>
    static void BuildBounds()
    {
        var root = new GameObject("Bounds").transform;
        const float top = 150f, bottom = -30f;
        float centerY = (top + bottom) * 0.5f, height = top - bottom;

        BoundsBox(root, "East", new Vector3(BoundsEast, centerY, 50f), new Vector3(2f, height, 400f), Quaternion.identity);
        BoundsBox(root, "West", new Vector3(-BoundsEast, centerY, 50f), new Vector3(2f, height, 400f), Quaternion.identity);
        BoundsBox(root, "North", new Vector3(0f, centerY, BoundsNorth), new Vector3(2f * BoundsEast, height, 2f), Quaternion.identity);

        // 沿著海岸線往外 WadingDepth 公尺。棧橋經過的那一段只擋到橋面下方，玩家可以走上棧橋
        float pierWest = PierX - PierWidth * 0.5f - 0.1f, pierEast = PierX + PierWidth * 0.5f + 0.1f;
        var xs = new List<float>();
        for (float x = -BoundsEast; x <= BoundsEast; x += 6f) xs.Add(x);
        xs.Add(pierWest);
        xs.Add(pierEast);
        xs.Sort();
        for (int i = 0; i < xs.Count - 1; i++)
        {
            float x0 = xs[i], x1 = xs[i + 1];
            if (x1 - x0 < 0.01f) continue;
            var p0 = new Vector2(x0, ShoreZ(x0) - WadingDepth);
            var p1 = new Vector2(x1, ShoreZ(x1) - WadingDepth);
            var mid = (p0 + p1) * 0.5f;
            // 一般的牆段稍微重疊避免縫隙；緊鄰棧橋的不能重疊，否則會擋到橋面
            bool touchesPier = Mathf.Abs(x1 - pierWest) < 0.01f || Mathf.Abs(x0 - pierEast) < 0.01f;
            float length = (p1 - p0).magnitude + (touchesPier ? 0f : 1.5f);
            float yaw = -Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;
            bool underPier = x0 >= pierWest - 0.01f && x1 <= pierEast + 0.01f;
            float boxTop = underPier ? PierDeckY - 0.35f : top;
            BoundsBox(root, $"Shore_{i}", new Vector3(mid.x, (boxTop + bottom) * 0.5f, mid.y), new Vector3(length, boxTop - bottom, 1f), Quaternion.Euler(0f, yaw, 0f));
        }

        // 棧橋兩側與盡頭：橋面以上有牆，不會掉進深水
        float pierZ = (PierStartZ + PierEndZ) * 0.5f;
        float bottomOfWall = PierDeckY + 0.05f;
        float wallCenter = (top + bottomOfWall) * 0.5f, wallHeight = top - bottomOfWall;
        BoundsBox(root, "Pier_East", new Vector3(PierX + PierWidth * 0.5f + 0.35f, wallCenter, pierZ), new Vector3(0.5f, wallHeight, PierLength + 0.5f), Quaternion.identity);
        BoundsBox(root, "Pier_West", new Vector3(PierX - PierWidth * 0.5f - 0.35f, wallCenter, pierZ), new Vector3(0.5f, wallHeight, PierLength + 0.5f), Quaternion.identity);
        BoundsBox(root, "Pier_End", new Vector3(PierX, wallCenter, PierEndZ - 0.3f), new Vector3(PierWidth + 1.2f, wallHeight, 0.5f), Quaternion.identity);
    }

    static void PlacePlayer()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.name = "PlayerRig";
        // 主街南端，面向村莊和後山
        const float x = 0f, z = -46f;
        rig.transform.SetPositionAndRotation(new Vector3(x, Ground(x, z) + 1.1f, z), Quaternion.identity);
        var player = rig.transform.Find("Player");
        if (player != null) player.localPosition = Vector3.zero;
    }
}
#endif
