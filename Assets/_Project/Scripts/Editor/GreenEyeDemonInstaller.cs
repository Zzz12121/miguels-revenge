#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 青眼惡魔（Tripo 模型，Mixamo 骨架）：
///   選單 GeminiGrowUp > 青眼惡魔 > 安裝（材質、動畫、Prefab）
///     - 模型用 Humanoid 匯入，套用 Lite Sword and Shield Pack 的動畫（idle / run / 四種攻擊 / 格擋 / 死亡）
///     - URP 材質：顏色、法線、金屬光滑、青綠色眼睛和寶石發光
///     - Prefab：Prefabs/Boss/PF_GreenEyeDemon.prefab（BossHealth + GreenEyeDemonAI + 碰撞）
///   選單 GeminiGrowUp > 青眼惡魔 > 建立測試場景
///     - Scenes/GreenEyeDemon_Test.unity：平地 + 玩家 + 惡魔，用來看招式
/// </summary>
public static class GreenEyeDemonInstaller
{
    const string Root = "Assets/_Project";
    const string ModelDir = Root + "/Models/GreenEyeDemon";
    const string ModelPath = ModelDir + "/SM_GreenEyeDemon.fbx";
    const string AnimDir = Root + "/Animations/GreenEyeDemon";
    const string ControllerPath = AnimDir + "/AC_GreenEyeDemon.controller";
    const string MaterialPath = ModelDir + "/M_GreenEyeDemon.mat";
    const string PrefabPath = Root + "/Prefabs/Boss/PF_GreenEyeDemon.prefab";
    const string TestScenePath = Root + "/Scenes/GreenEyeDemon_Test.unity";
    const string ClipDir = Root + "/ThirdParty/Lite Sword and Shield Pack";
    const float TargetHeight = 3.0f; // 惡魔身高（公尺），玩家約 2m

    /// <summary>Animator 狀態名稱 → 動畫檔（GreenEyeDemonAI 用這些名稱）</summary>
    static readonly (string state, string file)[] States =
    {
        ("Idle",   "sword and shield idle"),
        ("Run",    "sword and shield run"),
        ("Slash",  "sword and shield attack (2)"),
        ("Heavy",  "sword and shield attack (3)"),
        ("Combo",  "sword and shield attack"),
        ("Thrust", "sword and shield attack (4)"),
        ("Block",  "sword and shield block"),
        ("Death",  "sword and shield death"),
    };

    [MenuItem("GeminiGrowUp/青眼惡魔/安裝（材質、動畫、Prefab）")]
    public static void Install()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            EditorUtility.DisplayDialog("找不到模型", $"需要 {ModelPath}", "OK");
            return;
        }
        EnsureHumanoid();
        var mat = BuildMaterial();
        var controller = BuildController();
        BuildPrefab(mat, controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GreenEyeDemon] 安裝完成：{PrefabPath}");
    }

    [MenuItem("GeminiGrowUp/青眼惡魔/建立測試場景")]
    public static void BuildTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildTestSceneNoPrompt();
    }

    /// <summary>batchmode 用：-executeMethod GreenEyeDemonInstaller.InstallAndBuildTestScene</summary>
    public static void InstallAndBuildTestScene()
    {
        Install();
        BuildTestSceneNoPrompt();
    }

    // ---------------------------------------------------------------------

    static void EnsureHumanoid()
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        bool dirty = false;
        if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; dirty = true; }
        if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
        if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
        if (dirty) mi.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
            Debug.LogWarning("[GreenEyeDemon] Humanoid Avatar 沒有建立成功，請在模型的 Rig 分頁按 Configure 檢查骨架對應。");

        foreach (var (path, normal, linear) in new[]
                 {
                     (ModelDir + "/T_GreenEyeDemon_BaseColor.jpg", false, false),
                     (ModelDir + "/T_GreenEyeDemon_Normal.jpg", true, true),
                     (ModelDir + "/T_GreenEyeDemon_MetallicSmoothness.png", false, true),
                     (ModelDir + "/T_GreenEyeDemon_Emission.png", false, false),
                 })
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
            bool changed = false;
            if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
            if (linear && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
            if (changed) ti.SaveAndReimport();
        }
    }

    static Material BuildMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.SetTexture("_BaseMap", Tex("T_GreenEyeDemon_BaseColor.jpg"));
        mat.SetColor("_BaseColor", Color.white);
        var normal = Tex("T_GreenEyeDemon_Normal.jpg");
        if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
        var mask = Tex("T_GreenEyeDemon_MetallicSmoothness.png");
        if (mask != null) { mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_Smoothness", 1f); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); }
        var emission = Tex("T_GreenEyeDemon_Emission.png");
        if (emission != null)
        {
            mat.SetTexture("_EmissionMap", emission);
            mat.SetColor("_EmissionColor", new Color(2.5f, 2.5f, 2.5f)); // HDR：眼睛和寶石配 Bloom 會發光
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/{file}");

    static AnimatorController BuildController()
    {
        BossArenaBuilder.EnsureFolder(AnimDir);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                 ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (!ac.parameters.Any(p => p.name == "RunSpeed"))
            ac.AddParameter(new AnimatorControllerParameter { name = "RunSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var sm = ac.layers[0].stateMachine;
        foreach (var child in sm.states) sm.RemoveState(child.state);

        AnimatorState idle = null;
        foreach (var (state, file) in States)
        {
            string path = $"{ClipDir}/{file}.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) { Debug.LogWarning($"[GreenEyeDemon] 找不到動畫 {path}"); continue; }
            var st = sm.AddState(state);
            st.motion = clip;
            st.writeDefaultValues = true;
            if (state == "Run") { st.speedParameter = "RunSpeed"; st.speedParameterActive = true; }
            if (state == "Idle") idle = st;
        }
        if (idle != null) sm.defaultState = idle;
        EditorUtility.SetDirty(ac);
        return ac;
    }

    static void BuildPrefab(Material mat, AnimatorController controller)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var root = new GameObject("GreenEyeDemon");
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
        vis.name = "Model";
        vis.transform.SetParent(root.transform, false);

        foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
        }

        // 依模型高度縮放到 TargetHeight，腳底放在原點
        var bounds = new Bounds();
        bool first = true;
        foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
        {
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
        float scale = bounds.size.y > 0.01f ? TargetHeight / bounds.size.y : 1f;
        vis.transform.localScale = Vector3.one * scale;
        vis.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);

        var anim = vis.GetComponent<Animator>();
        if (anim == null) anim = vis.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar != null) anim.avatar = avatar;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = TargetHeight;
        col.radius = 0.75f;
        col.center = new Vector3(0f, TargetHeight * 0.5f, 0f);
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var hp = root.AddComponent<BossHealth>();
        hp.bossName = "青眼惡魔";
        hp.maxHealth = 3000f;

        var ai = root.AddComponent<GreenEyeDemonAI>();
        ai.animator = anim;

        BossArenaBuilder.EnsureFolder(Root + "/Prefabs/Boss");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
    }

    static void BuildTestSceneNoPrompt()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var rig = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PlayerRig.prefab");
        if (prefab == null) { Debug.LogWarning("[GreenEyeDemon] 先執行「安裝」"); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(80f, 1f, 80f);
        var gm = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/ShuraArena/M_Shura_FloorStone.mat")
                 ?? AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_ArenaFloor.mat");
        if (gm != null) ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

        if (rig != null)
        {
            var r = (GameObject)PrefabUtility.InstantiatePrefab(rig, scene);
            r.transform.position = new Vector3(0f, 0f, -10f);
            var cam = r.GetComponentInChildren<Camera>(true);
            if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        var demon = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        demon.transform.SetPositionAndRotation(new Vector3(0f, 0f, 8f), Quaternion.Euler(0f, 180f, 0f));
        var ai = demon.GetComponent<GreenEyeDemonAI>();
        ai.arenaCenter = Vector3.zero;
        ai.arenaRadius = 38f;
        ai.aggroRange = 14f;

        // 讓發光的眼睛看得到：沿用修羅場的後製設定（有的話）
        var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(Root + "/Scenes/ShuraArena/VP_ShuraArena.asset");
        if (profile != null)
        {
            var vol = new GameObject("Global Volume").AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        EditorSceneManager.SaveScene(scene, TestScenePath);
        Debug.Log($"[GreenEyeDemon] 測試場景：{TestScenePath}（玩家走近 14m 內開打）");
    }
}
#endif
