#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 建立 Boss 房
/// 產生圓形競技場（小方塊地板 + 環形牆）、膠囊體 Boss、玩家、第三人稱鏡頭。
/// 存在 Assets/_Project/Scenes/BossArena.unity，可重複執行（會覆蓋）。
/// </summary>
public static class BossArenaBuilder
{
    const string Root = "Assets/_Project";
    const string MatDir = Root + "/Materials";
    const string SceneDir = Root + "/Scenes";
    const string ScenePath = SceneDir + "/BossArena.unity";

    const float ArenaRadius = 18f;
    const float TileSize = 1.5f;
    const float WallHeight = 7f;
    const int WallSegments = 48;

    [MenuItem("GeminiGrowUp/建立 Boss 房 (Build Boss Arena)")]
    public static void Build()
    {
        if (!EditorUtility.DisplayDialog("重建 BossArena 場景？",
                "這會整個重建 BossArena.unity：場景裡的 PlayerRig、BossRoom Prefab 等都會被換成舊的測試物件。\n" +
                "現在大部分情況都不需要用這個選單，確定要重建嗎？",
                "重建", "取消"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolder(MatDir);
        EnsureFolder(SceneDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ---------- 材質 ----------
        var floorMat = GetOrCreateMaterial("M_ArenaFloor", new Color(0.45f, 0.43f, 0.48f));
        var wallMat = GetOrCreateMaterial("M_ArenaWall", new Color(0.22f, 0.21f, 0.25f));
        var bossMat = GetOrCreateMaterial("M_Boss", new Color(0.35f, 0.12f, 0.45f));
        var eyeMat = GetOrCreateMaterial("M_BossEye", new Color(1f, 0.85f, 0.2f), unlit: true);
        var beamMat = GetOrCreateMaterial("M_BossBeam", new Color(1f, 0.25f, 0.1f), unlit: true);
        var playerMat = GetOrCreateMaterial("M_Player", new Color(0.9f, 0.9f, 0.95f));
        var noseMat = GetOrCreateMaterial("M_PlayerFront", new Color(0.9f, 0.2f, 0.2f));
        floorMat.enableInstancing = true;

        // ---------- 競技場 ----------
        var arenaRoot = new GameObject("Arena").transform;

        var floorGo = new GameObject("Floor");
        floorGo.transform.SetParent(arenaRoot, false);
        var floor = floorGo.AddComponent<ArenaFloor>();
        floor.radius = ArenaRadius;
        floor.tileSize = TileSize;
        floor.tileThickness = 2.5f;
        floor.tileMaterial = floorMat;
        floor.rockMaterial = GetOrCreateMaterial("M_ArenaRock", new Color(0.36f, 0.35f, 0.39f));
        floor.Generate();

        // 環形牆：很多段方塊圍成一圈，內側剛好在可站立半徑外
        var walls = new GameObject("Walls").transform;
        walls.SetParent(arenaRoot, false);
        const float thickness = 2f;
        float wallCenterR = ArenaRadius + thickness * 0.5f;
        float segWidth = 2f * Mathf.PI * (ArenaRadius + thickness) / WallSegments * 1.05f;
        for (int i = 0; i < WallSegments; i++)
        {
            float a = i * Mathf.PI * 2f / WallSegments;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = $"Wall_{i:00}";
            seg.transform.SetParent(walls, false);
            seg.transform.position = dir * wallCenterR + Vector3.up * (WallHeight * 0.5f - 1f);
            seg.transform.rotation = Quaternion.LookRotation(dir);
            seg.transform.localScale = new Vector3(segWidth, WallHeight, thickness);
            seg.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
            seg.isStatic = true;
        }

        // ---------- 玩家 ----------
        var player = CreatePlayer(new Vector3(0f, 1f, -12f), playerMat, noseMat);

        // ---------- Boss ----------
        var boss = new GameObject("Boss");
        boss.transform.SetPositionAndRotation(new Vector3(0f, 0f, 6f), Quaternion.Euler(0f, 180f, 0f));

        var bossCol = boss.AddComponent<CapsuleCollider>();
        bossCol.center = new Vector3(0f, 3f, 0f);
        bossCol.height = 6f;
        bossCol.radius = 1.5f;
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var pivot = new GameObject("BodyPivot").transform; // 軸心在腳底，前傾後仰才自然
        pivot.SetParent(boss.transform, false);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
        body.transform.SetParent(pivot, false);
        body.transform.localPosition = new Vector3(0f, 3f, 0f);
        body.transform.localScale = new Vector3(3f, 3f, 3f); // 高 6m、半徑 1.5m
        body.GetComponent<MeshRenderer>().sharedMaterial = bossMat;

        var eye = GameObject.CreatePrimitive(PrimitiveType.Cube);
        eye.name = "Eye (Facing)";
        Object.DestroyImmediate(eye.GetComponent<BoxCollider>());
        eye.transform.SetParent(pivot, false);
        eye.transform.localPosition = new Vector3(0f, 4.6f, 1.3f);
        eye.transform.localScale = new Vector3(1.6f, 0.35f, 0.5f);
        eye.GetComponent<MeshRenderer>().sharedMaterial = eyeMat;

        var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "Beam";
        Object.DestroyImmediate(beam.GetComponent<BoxCollider>());
        beam.transform.SetParent(boss.transform, false);
        var beamRend = beam.GetComponent<MeshRenderer>();
        beamRend.sharedMaterial = beamMat;
        beamRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.SetActive(false);

        boss.AddComponent<BossHealth>();
        var bc = boss.AddComponent<BossController>();
        bc.player = player.transform;
        bc.arena = floor;
        bc.bodyPivot = pivot;
        bc.beam = beam.transform;

        // ---------- 鏡頭 ----------
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.nearClipPlane = 0.1f;
        cam.transform.position = new Vector3(0f, 3f, -17f);
        var tpc = cam.gameObject.AddComponent<ThirdPersonCamera>();
        tpc.target = player.transform;
        tpc.distance = 6f;
        tpc.maxDistance = 10f;
        tpc.targetOffset = new Vector3(0f, 1.8f, 0f);
        player.GetComponent<ThirdPersonController>().cameraTransform = cam.transform;

        // ---------- 光源 / 氣氛 ----------
        var light = GameObject.Find("Directional Light");
        if (light != null)
        {
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var l = light.GetComponent<Light>();
            if (l != null) l.color = new Color(1f, 0.9f, 0.85f);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Selection.activeGameObject = boss;
        Debug.Log($"[GeminiGrowUp] 已建立 Boss 房：{ScenePath}。按 Play 開始，數字鍵 1/2/3 可直接測試三種招式。");
    }

    // ================= 工具函式 =================

    internal static GameObject CreatePlayer(Vector3 pos, Material bodyMat, Material noseMat)
    {
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        player.transform.position = pos;
        Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
        player.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

        var cc = player.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = Vector3.zero;
        cc.stepOffset = 0.35f;
        cc.slopeLimit = 45f;
        cc.skinWidth = 0.05f;

        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "FacingIndicator";
        Object.DestroyImmediate(nose.GetComponent<BoxCollider>());
        nose.transform.SetParent(player.transform, false);
        nose.transform.localPosition = new Vector3(0f, 0.5f, 0.45f);
        nose.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
        nose.GetComponent<MeshRenderer>().sharedMaterial = noseMat;

        player.AddComponent<ThirdPersonController>();
        player.AddComponent<PlayerHealth>();
        player.AddComponent<PlayerCombat>();
        return player;
    }

    internal static Material GetOrCreateMaterial(string name, Color color, bool unlit = false)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find(unlit ? "Unlit/Color" : "Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    internal static void AddToBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
