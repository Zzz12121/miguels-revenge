#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 建立移動測試場景
/// 自動產生：格子地板、外牆、不同高度的跳台、斜坡、樓梯、柱子、膠囊體玩家、第三人稱鏡頭。
/// 產生的場景存在 Assets/_Project/Scenes/MovementTest.unity，可以重複執行（會覆蓋）。
/// </summary>
public static class TestSceneBuilder
{
    const string Root = "Assets/_Project";
    const string MatDir = Root + "/Materials";
    const string SceneDir = Root + "/Scenes";
    const string ScenePath = SceneDir + "/MovementTest.unity";

    [MenuItem("GeminiGrowUp/建立移動測試場景 (Build Movement Test Scene)")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(Root);
        EnsureFolder(MatDir);
        EnsureFolder(SceneDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ---------- 材質 ----------
        Material floorMat = GetOrCreateMaterial("M_Floor", Color.white, CreateGridTexture(), new Vector2(40, 40));
        Material wallMat = GetOrCreateMaterial("M_Wall", new Color(0.35f, 0.36f, 0.4f));
        Material blockMat = GetOrCreateMaterial("M_Block", new Color(0.85f, 0.55f, 0.25f));
        Material rampMat = GetOrCreateMaterial("M_Ramp", new Color(0.35f, 0.6f, 0.85f));
        Material playerMat = GetOrCreateMaterial("M_Player", new Color(0.9f, 0.9f, 0.95f));
        Material noseMat = GetOrCreateMaterial("M_PlayerFront", new Color(0.9f, 0.2f, 0.2f));

        // ---------- 關卡 ----------
        var level = new GameObject("Level").transform;
        const float size = 80f;

        // 地板（上表面在 y = 0）
        Box("Floor", level, new Vector3(0, -0.5f, 0), new Vector3(size, 1, size), floorMat);

        // 外牆
        float h = 4f, half = size / 2f;
        Box("Wall_N", level, new Vector3(0, h / 2, half), new Vector3(size, h, 1), wallMat);
        Box("Wall_S", level, new Vector3(0, h / 2, -half), new Vector3(size, h, 1), wallMat);
        Box("Wall_E", level, new Vector3(half, h / 2, 0), new Vector3(1, h, size), wallMat);
        Box("Wall_W", level, new Vector3(-half, h / 2, 0), new Vector3(1, h, size), wallMat);

        // 不同高度的跳台（測試跳躍高度：0.5 / 1 / 1.5 / 2.5 m）
        var jumps = new GameObject("JumpTest").transform;
        jumps.SetParent(level);
        float[] heights = { 0.5f, 1f, 1.5f, 2.5f };
        for (int i = 0; i < heights.Length; i++)
            Box($"Block_{heights[i]}m", jumps, new Vector3(-12 + i * 4, heights[i] / 2, 10),
                new Vector3(3, heights[i], 3), blockMat);

        // 斜坡
        var ramp = Box("Ramp", level, new Vector3(12, 1.2f, 8), new Vector3(4, 0.5f, 10), rampMat);
        ramp.transform.rotation = Quaternion.Euler(-15f, 0, 0);
        Box("Ramp_Top", level, new Vector3(12, 1.4f, 16), new Vector3(4, 2.8f, 6), blockMat);

        // 樓梯（每階 0.25 m，CharacterController 可直接走上去）
        var stairs = new GameObject("Stairs").transform;
        stairs.SetParent(level);
        for (int i = 0; i < 10; i++)
        {
            float y = 0.25f * (i + 1);
            Box($"Step_{i}", stairs, new Vector3(-14, y / 2, -6 - i * 0.6f), new Vector3(4, y, 0.6f), rampMat);
        }

        // 柱子（測試鏡頭防穿牆）
        var pillars = new GameObject("Pillars").transform;
        pillars.SetParent(level);
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                Box($"Pillar_{x}_{z}", pillars, new Vector3(8 + x * 4, 2, -8 - z * 4), new Vector3(1, 4, 1), wallMat);

        // 一條窄走廊 + 天花板（測試鏡頭在室內）
        Box("Corridor_L", level, new Vector3(-2, 1.5f, -18), new Vector3(0.5f, 3, 12), wallMat);
        Box("Corridor_R", level, new Vector3(2, 1.5f, -18), new Vector3(0.5f, 3, 12), wallMat);
        Box("Corridor_Roof", level, new Vector3(0, 3.25f, -18), new Vector3(4.5f, 0.5f, 12), wallMat);

        // ---------- 玩家 ----------
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        player.transform.position = new Vector3(0, 1f, 0);
        Object.DestroyImmediate(player.GetComponent<CapsuleCollider>()); // 改用 CharacterController 的碰撞
        player.GetComponent<MeshRenderer>().sharedMaterial = playerMat;

        var cc = player.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = Vector3.zero;
        cc.stepOffset = 0.35f;
        cc.slopeLimit = 45f;
        cc.skinWidth = 0.05f;

        // 前方的小方塊，用來看角色面向哪裡
        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "FacingIndicator";
        Object.DestroyImmediate(nose.GetComponent<BoxCollider>());
        nose.transform.SetParent(player.transform, false);
        nose.transform.localPosition = new Vector3(0, 0.5f, 0.45f);
        nose.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
        nose.GetComponent<MeshRenderer>().sharedMaterial = noseMat;

        var controller = player.AddComponent<ThirdPersonController>();

        // ---------- 鏡頭 ----------
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.nearClipPlane = 0.1f;
        cam.transform.position = new Vector3(0, 3, -5);
        var tpc = cam.gameObject.AddComponent<ThirdPersonCamera>();
        tpc.target = player.transform;
        controller.cameraTransform = cam.transform;

        // ---------- 光源 ----------
        var light = GameObject.Find("Directional Light");
        if (light != null) light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ---------- 存檔 ----------
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Selection.activeGameObject = player;
        Debug.Log($"[GeminiGrowUp] 已建立測試場景：{ScenePath}，按 Play 開始測試。");
    }

    // ================= 工具函式 =================

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    static Material GetOrCreateMaterial(string name, Color color, Texture2D tex = null, Vector2? tiling = null)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (tex != null)
        {
            string texProp = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            mat.SetTexture(texProp, tex);
            if (tiling.HasValue) mat.SetTextureScale(texProp, tiling.Value);
        }
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 產生一張格子貼圖（1 格 = 2 m），方便看出移動速度
    static Texture2D CreateGridTexture()
    {
        string path = $"{MatDir}/T_Grid.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int s = 128;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color bg = new Color(0.55f, 0.57f, 0.6f), line = new Color(0.3f, 0.32f, 0.35f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool edge = x < 3 || y < 3;
                bool mid = (x == s / 2 || y == s / 2);
                tex.SetPixel(x, y, edge ? line : mid ? Color.Lerp(bg, line, 0.4f) : bg);
            }
        tex.Apply();

        string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
        File.WriteAllBytes(full, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
