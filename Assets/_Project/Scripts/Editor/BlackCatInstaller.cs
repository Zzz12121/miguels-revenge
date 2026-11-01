using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 黑貓 NPC 的匯入設定 + 一鍵建立 Prefab。
///   模型：Assets/_Project/Models/BlackCat/BlackCat.fbx（Blender 產生，動畫 BC_Idle / BC_Gesture）
///   貼圖：Assets/_Project/Models/BlackCat/Textures/
/// 選單 GeminiGrowUp > 黑貓 > 建立 Prefab 並放進目前場景：
///   建立材質、Animator Controller、Prefab（Assets/_Project/Prefabs/NPC/PF_BlackCat.prefab），
///   並把一隻黑貓放在目前場景裡玩家前方的地板上（要先打開 VFX_VoidRend_Bocchi，放好後記得存檔）。
/// </summary>
public sealed class BlackCatInstaller : AssetPostprocessor
{
    const string Root = "Assets/_Project/Models/BlackCat/";
    const string FbxPath = Root + "BlackCat.fbx";
    const string TexDir = Root + "Textures/";
    const string AudioDir = Root + "Audio/";
    const string MaterialPath = Root + "M_BlackCat.mat";
    const string ControllerPath = Root + "AC_BlackCat.controller";
    const string PrefabDir = "Assets/_Project/Prefabs/NPC";
    const string PrefabPath = PrefabDir + "/PF_BlackCat.prefab";
    const float CatHeight = 1.0f; // Prefab 裡黑貓站直的高度（公尺）；想要大一點就改 Prefab 的 Scale

    void OnPreprocessModel()
    {
        if (assetPath != FbxPath) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; // 材質由下面的選單建立（URP Lit）
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        importer.useFileScale = true;
        importer.globalScale = 1f;
    }

    void OnPreprocessAnimation()
    {
        if (assetPath != FbxPath) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            string name = clip.takeName.Split('|').Last();
            clip.name = name;
            clip.loopTime = name == "BC_Idle" || name == "BC_Gesture";
            clip.loopPose = false;
            clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(TexDir)) return;
        var importer = (TextureImporter)assetImporter;
        string file = Path.GetFileNameWithoutExtension(assetPath);
        importer.maxTextureSize = 2048;
        if (file.EndsWith("_Normal")) importer.textureType = TextureImporterType.NormalMap;
        else if (file.EndsWith("_MetallicSmoothness")) { importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.alphaSource = TextureImporterAlphaSource.FromInput; }
        else importer.textureType = TextureImporterType.Default;
    }

    [MenuItem("GeminiGrowUp/黑貓/建立 Prefab 並放進目前場景")]
    static void InstallAndPlace()
    {
        var prefab = BuildPrefab();
        if (prefab == null) return;
        Place(prefab);
    }

    [MenuItem("GeminiGrowUp/黑貓/只更新 Prefab（不放進場景）")]
    static void InstallOnly()
    {
        if (BuildPrefab() != null) EditorUtility.DisplayDialog("黑貓", "Prefab 已更新：\n" + PrefabPath, "OK");
    }

    static GameObject BuildPrefab()
    {
        // 檔案可能比這支腳本先匯入（那時匯入設定還沒生效），所以先強制重新匯入一次
        foreach (var tex in new[] { "BlackCat_BaseColor.jpg", "BlackCat_BaseColor_NoMouth.jpg", "BlackCat_Normal.jpg", "BlackCat_MetallicSmoothness.png" })
            if (File.Exists(TexDir + tex)) AssetDatabase.ImportAsset(TexDir + tex, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (model == null) { EditorUtility.DisplayDialog("黑貓", "找不到模型：" + FbxPath, "OK"); return null; }
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        var idle = clips.FirstOrDefault(c => c.name == "BC_Idle");
        var gesture = clips.FirstOrDefault(c => c.name == "BC_Gesture");
        if (idle == null || gesture == null) { EditorUtility.DisplayDialog("黑貓", "FBX 裡找不到 BC_Idle / BC_Gesture 動畫", "OK"); return null; }

        var material = BuildMaterial();
        var controller = BuildController(idle, gesture);

        EnsureFolder(PrefabDir);
        var root = new GameObject("PF_BlackCat");
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = "BlackCat_Model";
            body.transform.SetParent(root.transform, false);
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            var animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // 依待機姿勢的大小把整隻貓縮放到 CatHeight，腳底放在 Prefab 的原點
            idle.SampleAnimation(body, 0f);
            var bounds = MeshBounds(body);
            float scale = bounds.size.y > .001f ? CatHeight / bounds.size.y : 1f;
            body.transform.localScale = Vector3.one * scale;
            bounds = MeshBounds(body);
            body.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = CatHeight;
            capsule.radius = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * .3f, .15f, CatHeight * .5f);
            capsule.center = new Vector3(0f, CatHeight * .5f, 0f);

            var greeter = root.AddComponent<BlackCatGreeter>();
            greeter.animator = animator;
            greeter.grinTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "BlackCat_BaseColor.jpg");
            greeter.plainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "BlackCat_BaseColor_NoMouth.jpg");
            // 檔名是從短片最後一聲往前編號（1 = 最後一聲），動畫裡第 1 下要配短片的第 1 聲 → 反過來排
            greeter.clapSounds = Enumerable.Range(1, 8).Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "BlackCat_Clap_" + i + ".wav")).Where(c => c != null).Reverse().ToArray();
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 1f; source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f; source.maxDistance = 20f;
            greeter.audioSource = source;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            return saved;
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Bounds MeshBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = default; bool first = true;
        foreach (var r in renderers)
        {
            Bounds rb;
            if (r is SkinnedMeshRenderer smr)
            {
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var m = smr.transform.localToWorldMatrix;
                rb = new Bounds(m.MultiplyPoint3x4(mesh.vertices.Length > 0 ? mesh.vertices[0] : Vector3.zero), Vector3.zero);
                foreach (var v in mesh.vertices) rb.Encapsulate(m.MultiplyPoint3x4(v));
                Object.DestroyImmediate(mesh);
            }
            else rb = r.bounds;
            if (first) { b = rb; first = false; } else b.Encapsulate(rb);
        }
        // 換回 go 父物件的座標（Prefab 根節點在原點，所以直接用世界座標）
        return b;
    }

    static Material BuildMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
        else material.shader = shader;
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "BlackCat_BaseColor.jpg");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "BlackCat_Normal.jpg");
        var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "BlackCat_MetallicSmoothness.png");
        if (material.HasProperty("_BaseMap")) { material.SetTexture("_BaseMap", baseMap); material.SetColor("_BaseColor", Color.white); }
        material.mainTexture = baseMap;
        if (normal != null && material.HasProperty("_BumpMap")) { material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", 1f); material.EnableKeyword("_NORMALMAP"); }
        if (ms != null && material.HasProperty("_MetallicGlossMap"))
        {
            material.SetTexture("_MetallicGlossMap", ms);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f);
            if (material.HasProperty("_GlossMapScale")) material.SetFloat("_GlossMapScale", 1f);
        }
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static AnimatorController BuildController(AnimationClip idle, AnimationClip gesture)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Near", AnimatorControllerParameterType.Bool);
        controller.AddParameter(new AnimatorControllerParameter { name = "GestureSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        var sm = controller.layers[0].stateMachine;
        var idleState = sm.AddState("Idle", new Vector3(260, 80));
        idleState.motion = idle;
        var gestureState = sm.AddState("Gesture", new Vector3(520, 80));
        gestureState.motion = gesture;
        gestureState.speedParameter = "GestureSpeed";
        gestureState.speedParameterActive = true;
        sm.defaultState = idleState;

        var toGesture = idleState.AddTransition(gestureState);
        toGesture.hasExitTime = false; toGesture.duration = .12f; toGesture.hasFixedDuration = true;
        toGesture.AddCondition(AnimatorConditionMode.If, 0, "Near");

        // 玩家走開：把這一輪做完（回到雙手在胸前）再接回待機
        var toIdle = gestureState.AddTransition(idleState);
        toIdle.hasExitTime = true; toIdle.exitTime = .96f; toIdle.duration = .2f; toIdle.hasFixedDuration = true;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Near");
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void Place(GameObject prefab)
    {
        var scene = EditorSceneManager.GetActiveScene();
        Vector3 position; Quaternion rotation = Quaternion.identity;
        var motor = Object.FindAnyObjectByType<ThirdPersonController>();
        Vector3 probe;
        if (motor != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(motor.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            probe = motor.transform.position + forward * 4f;
            rotation = Quaternion.LookRotation(-forward, Vector3.up); // 面向玩家
        }
        else probe = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        position = probe;
        var hits = Physics.RaycastAll(probe + Vector3.up * 6f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => motor == null || !h.transform.IsChildOf(motor.transform)).OrderBy(h => h.distance).ToArray();
        if (hits.Length > 0) position = hits[0].point;

        var cat = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        cat.transform.SetPositionAndRotation(position, rotation);
        Undo.RegisterCreatedObjectUndo(cat, "Place Black Cat");
        Selection.activeGameObject = cat;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.DisplayDialog("黑貓",
            "已經把黑貓放在「" + scene.name + "」玩家前方 4 公尺的地板上（" + (hits.Length > 0 ? "有找到地板" : "沒找到地板，請自己調整高度") + "）。\n\n" +
            "可以直接拖動調整位置，記得 Ctrl+S 存檔。\n觸發範圍、速度在 BlackCatGreeter 元件上調整。", "OK");
    }
}
