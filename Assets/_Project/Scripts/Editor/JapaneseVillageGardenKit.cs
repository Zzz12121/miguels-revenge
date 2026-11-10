#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 用 URP 3D Sample「The Garden」的模組拼出日式房屋。
/// 組裝方式照官方場景：地板高 0.81 m，下牆 2.13 m + 上牆 1.7 m；模組的原點在一端，往 local -X 延伸，local +Z 朝屋外。
/// </summary>
public static class JVGarden
{
    public const string ArtRoot = "Assets/_Project/ThirdParty/URP 3D Sample Garden/Art";

    static Dictionary<string, GameObject> prefabs;

    public static GameObject Prefab(string name)
    {
        if (prefabs == null)
        {
            prefabs = new Dictionary<string, GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                prefabs[Path.GetFileNameWithoutExtension(path)] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        }
        if (prefabs.TryGetValue(name + "_Prefab", out var prefab) || prefabs.TryGetValue(name, out prefab)) return prefab;
        throw new FileNotFoundException($"[JapaneseVillage] 找不到 Garden 模組：{name}");
    }

    public static void ClearCache() => prefabs = null;

    /// <summary>
    /// 放一個原點不好預測的模組（家具、裝飾）：讓它的外框底面中心對到 parent 座標的 bottomCenter。
    /// </summary>
    public static GameObject PlaceOn(string name, Transform parent, Vector3 bottomCenter, float yaw, float scale = 1f)
    {
        var go = Place(name, parent, Vector3.zero, yaw);
        go.transform.localScale *= scale;
        var b = WorldBounds(go);
        go.transform.position += parent.TransformPoint(bottomCenter) - new Vector3(b.center.x, b.min.y, b.center.z);
        return go;
    }

    public static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>把物件和所有子物件的 Static 旗標清掉（會動的東西不能被靜態合併）</summary>
    public static void ClearStatic(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
    }

    /// <summary>在 parent 底下放一個模組（座標都是 parent 的 local 座標）</summary>
    public static GameObject Place(string name, Transform parent, Vector3 localPosition, float yaw, Vector3? scale = null)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(name), parent);
        go.transform.SetLocalPositionAndRotation(localPosition, Quaternion.Euler(0f, yaw, 0f));
        if (scale.HasValue) go.transform.localScale = Vector3.Scale(go.transform.localScale, scale.Value);
        return go;
    }
}

/// <summary>
/// Japanese Garden Pack（Broccoli Tree Creator 匯出）的樹是舊管線的材質，這裡換成 URP Lit，
/// 並重新包成根物件帶 LODGroup 的 Prefab（Terrain Tree 要求 LODGroup 在根物件上）。
/// </summary>
public static class JVPack
{
    public const string Root = "Assets/Waldemarst/JapaneseGardenPackage";

    static Material Urp(string name, Texture map, bool cutout, Color color)
    {
        var m = JapaneseVillageAssets.ShaderMaterial(name, "Universal Render Pipeline/Lit");
        m.shader = Shader.Find("Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", color);
        m.SetTexture("_BaseMap", map);
        m.SetFloat("_Smoothness", cutout ? 0.05f : 0.12f);
        m.SetFloat("_Metallic", 0f);
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.32f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    public static Material Rock() => Urp("M_JV_PackRock", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/brocco_jg_rock.png"), false, new Color(1.5f, 1.45f, 1.4f));
    public static Material Torii() => Urp("M_JV_PackTorii", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/brocco_jg_torii_wood.png"), false, Color.white);

    /// <summary>重新包裝一棵 Pack 的樹：只留 LOD0、LOD1，看板那層用舊管線 shader，拿掉</summary>
    public static GameObject WrapTree(string tree, string prefabDir, float radius, float colliderHeight, System.Func<Mesh, Mesh> saveMesh)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/{tree}.prefab");
        var materials = new Dictionary<Material, Material>();
        Material Map(Material src)
        {
            if (src == null) return null;
            if (materials.TryGetValue(src, out var mapped)) return mapped;
            bool leaf = src.name.Contains("Sprout");
            var tex = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            mapped = Urp($"M_JV_{tree}_{(leaf ? "Leaf" : "Bark")}", tex, leaf, Color.white);
            materials[src] = mapped;
            return mapped;
        }
        return Wrap($"JV_{tree}", source, prefabDir, new[] { 0.14f, 0.008f }, radius, colliderHeight, Map, saveMesh);
    }

    /// <summary>重新包裝 Garden 的植物（材質原本就是 URP，直接沿用）。竹子數量多，只有近景的模型投影子；castShadows 關掉就完全不投</summary>
    public static GameObject WrapGardenTree(string gardenPrefab, string name, string prefabDir, float radius, float colliderHeight, System.Func<Mesh, Mesh> saveMesh, bool castShadows = true)
        => Wrap(name, JVGarden.Prefab(gardenPrefab), prefabDir, new[] { 0.09f, 0.02f, 0.004f }, radius, colliderHeight, m => m, saveMesh, castShadows ? 1 : 0);

    static GameObject Wrap(string name, GameObject source, string prefabDir, float[] thresholds, float radius, float colliderHeight,
                           System.Func<Material, Material> mapMaterial, System.Func<Mesh, Mesh> saveMesh, int shadowLods = int.MaxValue)
    {
        var inst = Object.Instantiate(source);
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var group = inst.GetComponentInChildren<LODGroup>();

        var root = new GameObject(name);
        var lods = new List<LOD>();
        foreach (var lod in group.GetLODs())
        {
            if (lods.Count >= thresholds.Length) break;
            MeshRenderer renderer = null;
            foreach (var r in lod.renderers)
                if (r is MeshRenderer mr) { renderer = mr; break; }
            if (renderer == null) continue;

            // Terrain Tree 不看子物件的 Transform：把位移、旋轉、縮放直接烤進網格
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var matrix = renderer.transform.localToWorldMatrix;
            if (!IsIdentity(matrix))
                mesh = saveMesh(Baked(mesh, matrix, $"{name}_LOD{lods.Count}"));

            var child = new GameObject($"LOD{lods.Count}");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var target = child.AddComponent<MeshRenderer>();
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mapMaterial(mats[i]);
            target.sharedMaterials = mats;
            target.shadowCastingMode = lods.Count < shadowLods ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            lods.Add(new LOD(thresholds[lods.Count], new Renderer[] { target }));
        }
        var rootGroup = root.AddComponent<LODGroup>();
        rootGroup.SetLODs(lods.ToArray());
        rootGroup.RecalculateBounds();

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, colliderHeight * 0.5f, 0f);
        capsule.height = colliderHeight;
        capsule.radius = radius;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{prefabDir}/{name}.prefab");
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(inst);
        return prefab;
    }

    static bool IsIdentity(in Matrix4x4 m)
    {
        for (int i = 0; i < 16; i++)
            if (Mathf.Abs(m[i] - Matrix4x4.identity[i]) > 0.0005f) return false;
        return true;
    }

    static Mesh Baked(Mesh source, in Matrix4x4 matrix, string name)
    {
        var mesh = Object.Instantiate(source);
        mesh.name = name;
        var vertices = source.vertices;
        var normals = source.normals;
        var tangents = source.tangents;
        var normalMatrix = matrix.inverse.transpose;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
        for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
        for (int i = 0; i < tangents.Length; i++)
        {
            var d = matrix.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
            tangents[i] = new Vector4(d.x, d.y, d.z, tangents[i].w);
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.RecalculateBounds();
        return mesh;
    }
}

public struct GardenHouseSpec
{
    public int width;           // 東西寬（公尺，4 的倍數），屋脊沿著這個方向
    public int depth;           // 南北深（公尺，4 的倍數，最大 14）
    public string facade;       // 正面：D = 4 m 拉門、O = 4 m 開著的門、B = 4 m 會滑開的門（BackDoor）、W = 2 m 窗、S = 2 m 牆，加起來要等於 width
    public string backFacade;   // 背面，格式同 facade；留空 = 一般的牆
    public bool hollow;         // 可以走進去：沒有實心內芯，有地板、榻榻米和天花板
    public bool veranda;        // 緣側（木造外廊）
    public int seed;
}

/// <summary>日式房屋：局部座標原點在地面中心，正面朝 +Z</summary>
public static class JapaneseVillageHouses
{
    public const float Floor = 0.81f;
    public const float UpperY = Floor + 2.13f;
    public const float WallTop = UpperY + 1.7f;
    public const float VerandaDepth = 1.03f;

    /// <summary>屋簷往牆外伸出的距離（Roof_tile_200x800 從屋脊往下 7.74 m）</summary>
    public const float TileLength = 7.74f;
    /// <summary>山牆端的屋瓦往屋脊方向外伸（Roof_tile_400x800）</summary>
    public const float GableOverhang = 2.59f;

    /// <summary>屋瓦截面：離屋脊 d 公尺往下掉多少（量自 Roof_tile_200x800，總落差 4.46 m，靠屋簷越平緩）</summary>
    public static float RoofDrop(float d)
    {
        float s = Mathf.Clamp01(d / TileLength);
        return 4.46f * (1f - Mathf.Pow(1f - s, 1.25f));
    }

    public static float RidgeY(int depth) => WallTop + RoofDrop(depth * 0.5f) + 0.1f;

    /// <summary>沿著一道牆放模組：s = 從起點量的距離；withWall 的方向一律是模組的 local -X</summary>
    readonly struct WallRun
    {
        public readonly Transform parent;
        public readonly Vector3 origin, dir;
        public readonly float yaw;

        public WallRun(Transform parent, Vector3 origin, float yaw)
        {
            this.parent = parent;
            this.origin = origin;
            this.yaw = yaw;
            dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.left;
        }

        public GameObject Put(string name, float s, float y, bool flip = false, float outward = 0f)
        {
            var normal = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return JVGarden.Place(name, parent, origin + dir * s + Vector3.up * y + normal * outward, flip ? yaw + 180f : yaw);
        }
    }

    public static GameObject Build(string name, in GardenHouseSpec h)
    {
        var root = new GameObject(name);
        var t = root.transform;
        float halfW = h.width * 0.5f, halfD = h.depth * 0.5f;
        var rng = new System.Random(h.seed);

        var walls = new GameObject("Walls").transform;
        walls.SetParent(t, false);

        // 四面牆，local +Z 朝外：南北兩面沿 X、東西兩面沿 Z
        var front = new WallRun(walls, new Vector3(halfW, 0f, halfD), 0f);
        var back = new WallRun(walls, new Vector3(-halfW, 0f, -halfD), 180f);
        var east = new WallRun(walls, new Vector3(halfW, 0f, -halfD), 90f);
        var west = new WallRun(walls, new Vector3(-halfW, 0f, halfD), -90f);

        BuildFacade(front, h.facade, h.width, h.veranda, rng);
        if (string.IsNullOrEmpty(h.backFacade)) BuildPlainWall(back, h.width, 4, rng);
        else BuildFacade(back, h.backFacade, h.width, false, rng);
        BuildPlainWall(east, h.depth, 6, rng);
        BuildPlainWall(west, h.depth, 6, rng);

        if (h.veranda) BuildVeranda(walls, h.width, halfD);
        if (h.hollow) BuildHallInterior(t, h.width, h.depth);
        else BuildCore(t, h.width, h.depth);

        BuildRoof(t, h.width, h.depth);
        return root;
    }

    /// <summary>屋內的實心內芯：上牆的通風窗和地板下的縫不會透出對面的天空</summary>
    static void BuildCore(Transform parent, int width, int depth)
    {
        var core = new GameObject("Core");
        core.transform.SetParent(parent, false);
        core.transform.localPosition = new Vector3(0f, (WallTop - 0.3f) * 0.5f, 0f);
        core.transform.localScale = new Vector3(width - 0.3f, WallTop + 0.3f, depth - 0.3f);
        core.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        core.AddComponent<MeshRenderer>().sharedMaterial = JapaneseVillageAssets.Get(JVMat.Black);
        core.isStatic = true;
    }

    /// <summary>可以走進去的房子：木地板 + 中間的榻榻米 + 天花板（家具另外放）</summary>
    static void BuildHallInterior(Transform parent, int width, int depth)
    {
        var hall = new GameObject("Hall").transform;
        hall.SetParent(parent, false);
        float halfW = width * 0.5f, halfD = depth * 0.5f;

        for (float x = halfW; x > -halfW + 0.1f; x -= 4f)
            for (float z = -halfD; z < halfD - 0.1f; z += 2f)
                JVGarden.Place("Wooden_Platform_400x200_01", hall, new Vector3(x, Floor, z), 0f);

        // 榻榻米：中間留一圈木地板當走道
        for (float x = -halfW + 4f; x < halfW - 4f - 0.1f; x += 2f)
            for (float z = -halfD + 2f; z < halfD - 2f - 0.1f; z += 1f)
                JVGarden.PlaceOn(((int)(x + z) & 1) == 0 ? "Tatami_200x100_01" : "Tatami_200x100_02", hall, new Vector3(x + 1f, Floor + 0.005f, z + 0.5f), 0f);

        // 天花板：Ceiling_600x600 一片 6 × 6 m，拉成剛好蓋滿整間
        int tilesX = Mathf.Max(1, Mathf.RoundToInt(width / 6f)), tilesZ = Mathf.Max(1, Mathf.RoundToInt(depth / 6f));
        float tileW = (float)width / tilesX, tileD = (float)depth / tilesZ;
        for (int ix = 0; ix < tilesX; ix++)
            for (int iz = 0; iz < tilesZ; iz++)
            {
                var tile = JVGarden.Place("Ceiling_600x600_01", hall, Vector3.zero, 0f);
                var size = JVGarden.WorldBounds(tile).size;
                tile.transform.localScale = Vector3.Scale(tile.transform.localScale, new Vector3(tileW / size.x, 1f, tileD / size.z));
                var b = JVGarden.WorldBounds(tile);
                tile.transform.position += new Vector3(-halfW + tileW * (ix + 0.5f), WallTop, -halfD + tileD * (iz + 0.5f)) - new Vector3(b.center.x, b.max.y, b.center.z);
            }

        // 四根內柱
        foreach (var (x, z) in new[] { (-halfW * 0.5f, -halfD * 0.5f), (halfW * 0.5f, -halfD * 0.5f), (-halfW * 0.5f, halfD * 0.5f), (halfW * 0.5f, halfD * 0.5f) })
        {
            var post = JVGarden.PlaceOn("BeamWooden_13x16x380_03", hall, new Vector3(x, Floor, z), 0f);
            post.transform.localScale = Vector3.Scale(post.transform.localScale, new Vector3(2f, 1f, 2f));
        }
    }

    /// <summary>屋頂的簡化碰撞體（凸多面體：三角柱）。可以走進去的房子底面放到牆頂，屋內才有空間</summary>
    public static Mesh RoofColliderMesh(in GardenHouseSpec h, string name)
    {
        float reach = TileLength, wr = h.width * 0.5f + GableOverhang;
        float ridge = RidgeY(h.depth), low = h.hollow ? WallTop : ridge - 4.4f;
        var mesh = new Mesh { name = name };
        mesh.vertices = new[]
        {
            new Vector3(-wr, low, -reach), new Vector3(wr, low, -reach),
            new Vector3(-wr, low, reach), new Vector3(wr, low, reach),
            new Vector3(-wr, ridge + 0.2f, 0f), new Vector3(wr, ridge + 0.2f, 0f),
        };
        mesh.triangles = new[]
        {
            0, 4, 5, 0, 5, 1,
            3, 5, 4, 3, 4, 2,
            2, 4, 0,
            1, 5, 3,
            0, 1, 3, 0, 3, 2,
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>主屋正面的門廊：小山牆屋頂 + 兩根柱子 + 木平台（村長家用）</summary>
    public static void AddPorch(Transform parent, in GardenHouseSpec h)
    {
        var porch = new GameObject("Porch").transform;
        porch.SetParent(parent, false);
        float front = h.depth * 0.5f;
        float ridge = RidgeY(h.depth);

        const float startInside = 1.2f;
        float y = ridge - RoofDrop(front - startInside) + 0.35f;
        JVGarden.Place("Roof_tile_400x400_01", porch, new Vector3(0f, y, front - startInside), 0f);

        float deckZ = front + VerandaDepth;
        for (float x = 2f; x > -2f - 0.1f; x -= 4f)
            JVGarden.Place("Wooden_Platform_400x200_01", porch, new Vector3(x, Floor, deckZ), 0f);
        foreach (float x in new[] { -1.95f, 1.95f })
            JVGarden.Place("BeamWooden_13x16x380_03", porch, new Vector3(x, 0f, deckZ + 1.85f), 0f);
    }

    static string UpperPiece(System.Random rng) => rng.Next(3) switch
    {
        0 => "Window_Upper_400x170_02",
        1 => "Window_Upper_400x170_03",
        _ => "Window_Upper_400x170_03",
    };

    static void Skirt(in WallRun run, float s, float outward)
    {
        // 地板下的腰板（細木條）+ 石基座
        run.Put("Wall_200x55_02", s, Floor - 0.75f, false, outward);
        run.Put("StoneSlab_01", s, -0.06f, false, outward);
    }

    static void Post(in WallRun run, float s)
    {
        run.Put("BeamWooden_13x16x380_03", s, Floor);
        run.Put("BeamWooden_20x100_01", s, Floor - 1.1f);
    }

    static void DoorBay(in WallRun run, float s)
    {
        run.Put("Door_SlidingRails_400x200_01", s, Floor);
        run.Put("Door_100x200_01", s + 1.08f, Floor, true);
        run.Put("Door_100x200_01", s + 1.02f, Floor);
        run.Put("Door_100x200_01_Prefab_Flipped", s + 2.91f, Floor, true);
        run.Put("Door_100x200_01_Prefab_Flipped", s + 2.96f, Floor);
    }

    /// <summary>開著的門：兩片門板收在兩邊，中間 1.83 m 可以走（Flipped 門板的延伸方向和一般門板相反）</summary>
    static void OpenBay(in WallRun run, float s)
    {
        run.Put("Door_SlidingRails_400x200_01", s, Floor);
        run.Put("Door_100x200_01", s + 1.08f, Floor, true);
        run.Put("Door_100x200_01_Prefab_Flipped", s + 2.91f, Floor, true);
    }

    /// <summary>會滑開的門：開著的門再加兩片關著的門板（BackDoor 底下的 PanelA、PanelB，由 SlidingDoor 滑開）</summary>
    static void SlidingBay(in WallRun run, float s)
    {
        OpenBay(run, s);
        var door = new GameObject("BackDoor").transform;
        door.SetParent(run.parent, false);
        void Panel(string name, GameObject piece)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(door, false);
            piece.transform.SetParent(holder, true);
            JVGarden.ClearStatic(holder.gameObject);
        }
        Panel("PanelA", run.Put("Door_100x200_01", s + 1.02f, Floor, false, 0.05f));
        Panel("PanelB", run.Put("Door_100x200_01_Prefab_Flipped", s + 2.96f, Floor, false, 0.05f));
    }

    static void WindowBay(in WallRun run, float s)
    {
        run.Put("Wall_Window_200x200_01", s, Floor);
        run.Put("Window_100x200_01", s + 0.97f, Floor + 0.6f, true);
        run.Put("Window_100x200_01", s + 1.03f, Floor + 0.6f);
    }

    static void BuildFacade(in WallRun run, string facade, int width, bool veranda, System.Random rng)
    {
        float s = 0f;
        foreach (char c in facade)
        {
            switch (c)
            {
                case 'D':
                    DoorBay(run, s);
                    s += 4f;
                    break;
                case 'O':
                    OpenBay(run, s);
                    s += 4f;
                    break;
                case 'B':
                    SlidingBay(run, s);
                    s += 4f;
                    break;
                case 'W':
                    WindowBay(run, s);
                    s += 2f;
                    break;
                default:
                    run.Put("Wall_200x200_01", s, Floor);
                    s += 2f;
                    break;
            }
        }
        if (!Mathf.Approximately(s, width)) Debug.LogWarning($"[JapaneseVillage] 正面 {facade} 總長 {s} 不等於房寬 {width}");

        float skirtOut = veranda ? VerandaDepth - 0.05f : 0f;
        for (float x = 0f; x < width - 0.1f; x += 2f) Skirt(run, x, skirtOut);
        for (float x = 0f; x < width + 0.1f; x += 4f) Post(run, x);
        for (float x = 0f; x < width - 0.1f; x += 4f) run.Put(UpperPiece(rng), x, UpperY);
    }

    /// <summary>沒有門的牆：每 windowEvery 公尺開一扇窗</summary>
    static void BuildPlainWall(in WallRun run, int length, int windowEvery, System.Random rng)
    {
        for (float s = 0f; s < length - 0.1f; s += 2f)
        {
            if (Mathf.Abs(Mathf.Repeat(s + 2f, windowEvery)) < 0.01f) WindowBay(run, s);
            else run.Put("Wall_200x200_01", s, Floor);
            Skirt(run, s, 0f);
        }
        for (float s = 0f; s < length + 0.1f; s += 4f) Post(run, s);
        for (float s = 0f; s < length - 0.1f; s += 4f) run.Put(UpperPiece(rng), s, UpperY);
    }

    static void BuildVeranda(Transform parent, int width, float halfD)
    {
        for (float x = width * 0.5f; x > -width * 0.5f + 0.1f; x -= 4f)
        {
            JVGarden.Place("Wooden_Platform_400x100_01", parent, new Vector3(x, Floor, halfD), 0f);
            JVGarden.Place("BeamWooden_20x100_01", parent, new Vector3(x - 2f, Floor - 1.1f, halfD + VerandaDepth - 0.1f), 0f);
        }
    }

    static void BuildRoof(Transform parent, int width, int depth)
    {
        var roof = new GameObject("Roof").transform;
        roof.SetParent(parent, false);
        float ridge = RidgeY(depth);
        float halfW = width * 0.5f;

        for (int i = 0; i < width / 2; i++)
        {
            JVGarden.Place("Roof_tile_200x800_01", roof, new Vector3(halfW - 2f * i, ridge, 0f), 0f);
            JVGarden.Place("Roof_tile_200x800_01", roof, new Vector3(-halfW + 2f * i, ridge, 0f), 180f);
        }
        JVGarden.Place("Roof_tile_400x800_01", roof, new Vector3(halfW, ridge, 0f), 90f);
        JVGarden.Place("Roof_tile_400x800_01", roof, new Vector3(-halfW, ridge, 0f), -90f);

        // 屋脊：兩端是上翹的鬼瓦，中間直線
        JVGarden.Place("Roof_Ornament_400_03", roof, new Vector3(halfW, ridge, 0f), 0f);
        JVGarden.Place("Roof_Ornament_400_03", roof, new Vector3(-halfW, ridge, 0f), 180f);
        for (float x = halfW - 4f; x - 4f >= -halfW + 4f - 0.1f; x -= 4f)
            JVGarden.Place("Roof_Ornament_400_01", roof, new Vector3(x, ridge, 0f), 0f);
    }
}
#endif
