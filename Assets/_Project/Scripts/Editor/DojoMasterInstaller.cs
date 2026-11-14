using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 道場老師父（NPC）的匯入設定 + 一鍵建立 Prefab。
///   模型：Assets/_Project/Models/DojoMaster/DojoMaster.fbx
///         （Art/DojoMaster/dojomaster_build.py 把 Tripo 的三段動畫合在一起，並修好長袍被手指骨頭拉走的蒙皮）
///   動畫：DM_SitIdle（跪坐喝茶，循環；來自 Tripo 的喝茶動畫 DM_SitDrink）、DM_Walk（走路，循環）、DM_Talk（說話，循環）、
///         DM_Kneel（跪坐不動，Boss 被打倒時用）
///         Tripo 的「起身」那段沒有真的站起來（跪著往前滑），所以只取最前面坐著的部分；演出時用鏡頭切換帶過起身。
///         走路原本會真的往前走（循環時彈回原點），build 腳本已經改成原地走，往前移動交給 DojoOpeningCutscene。
/// 選單 GeminiGrowUp > 道場 > 建立老師父 Prefab → Assets/_Project/Prefabs/NPC/PF_DojoMaster.prefab
/// </summary>
public sealed class DojoMasterInstaller : AssetPostprocessor
{
    const string Root = "Assets/_Project/Models/DojoMaster/";
    const string FbxPath = Root + "DojoMaster.fbx";
    const string TexDir = Root + "Textures/";
    const string MaterialPath = Root + "M_DojoMaster.mat";
    const string ControllerPath = Root + "AC_DojoMaster.controller";
    const string PrefabDir = "Assets/_Project/Prefabs/NPC";
    const string PrefabPath = PrefabDir + "/PF_DojoMaster.prefab";
    const float MasterHeight = 1.75f;   // 站直時的身高（公尺）
    const int SitLastFrame = 36;        // DM_SitStand 裡真正坐著不動的部分（24 fps，前 36 格；後面他會跪著往前滑）

    void OnPreprocessModel()
    {
        if (assetPath != FbxPath) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; // 材質由選單建立（URP Lit）
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
    }

    void OnPreprocessAnimation()
    {
        if (assetPath != FbxPath) return;
        var importer = (ModelImporter)assetImporter;
        var takes = importer.defaultClipAnimations;
        ModelImporterClipAnimation Take(string key) => takes.FirstOrDefault(t => t.takeName.EndsWith(key));
        var list = new System.Collections.Generic.List<ModelImporterClipAnimation>();
        void Add(ModelImporterClipAnimation src, string name, float last = -1f)
        {
            if (src == null) return;
            var c = new ModelImporterClipAnimation
            {
                takeName = src.takeName, name = name, firstFrame = src.firstFrame,
                lastFrame = last >= 0f ? Mathf.Min(src.lastFrame, src.firstFrame + last) : src.lastFrame,
                loopTime = true, loopPose = true,
                lockRootRotation = true, lockRootHeightY = true, lockRootPositionXZ = true,
                keepOriginalOrientation = true, keepOriginalPositionY = true, keepOriginalPositionXZ = true,
            };
            list.Add(c);
        }
        // 坐著：有 Tripo 的喝茶動畫（DM_SitDrink，已經剪成循環）就用它，沒有就用跪坐起身最前面不動的那段
        var drink = Take("DM_SitDrink");
        if (drink != null) Add(drink, "DM_SitIdle");
        else Add(Take("DM_SitStand"), "DM_SitIdle", SitLastFrame);
        Add(Take("DM_SitStand"), "DM_Kneel", SitLastFrame);   // 被打倒時跪坐在地上
        Add(Take("DM_Walk"), "DM_Walk");
        Add(Take("DM_Talk"), "DM_Talk");
        importer.clipAnimations = list.ToArray();
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

    [MenuItem("GeminiGrowUp/道場/建立老師父 Prefab")]
    static void Install()
    {
        var prefab = BuildPrefab();
        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("道場老師父", "Prefab 已建立：\n" + PrefabPath + "\n\n動畫狀態：SitIdle（預設，喝茶）、Walk、Talk、Kneel。\n開場演出請執行「GeminiGrowUp > 道場 > 建立開場演出」。", "OK");
        }
    }

    static GameObject BuildPrefab()
    {
        foreach (var tex in new[] { "DojoMaster_BaseColor.jpg", "DojoMaster_Normal.png", "DojoMaster_MetallicSmoothness.png" })
            if (File.Exists(TexDir + tex)) AssetDatabase.ImportAsset(TexDir + tex, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (model == null) { EditorUtility.DisplayDialog("道場老師父", "找不到模型：" + FbxPath, "OK"); return null; }
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
        foreach (var need in new[] { "DM_SitIdle", "DM_Walk", "DM_Talk" })
            if (!clips.ContainsKey(need)) { EditorUtility.DisplayDialog("道場老師父", "FBX 裡找不到動畫 " + need, "OK"); return null; }

        var material = BuildMaterial();
        clips.TryGetValue("DM_Kneel", out var kneel);
        var controller = BuildController(clips["DM_SitIdle"], clips["DM_Walk"], clips["DM_Talk"], kneel);

        EnsureFolder(PrefabDir);
        var root = new GameObject("PF_DojoMaster");
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = "DojoMaster_Model";
            body.transform.SetParent(root.transform, false);
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
            }
            var animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // 用「說話」（站著）的姿勢量身高，縮放到 MasterHeight，腳底放在 Prefab 原點
            clips["DM_Talk"].SampleAnimation(body, 0f);
            var bounds = MeshBounds(body);
            float scale = bounds.size.y > .001f ? MasterHeight / bounds.size.y : 1f;
            body.transform.localScale *= scale; // 乘上去（模型根節點本身可能就有縮放）
            bounds = MeshBounds(body);
            body.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = MasterHeight;
            capsule.radius = .35f;
            capsule.center = new Vector3(0f, MasterHeight * .5f, 0f);

            // Boss 戰：血條（BossHealth）+ AI（DojoMasterBoss）。數值可以在 Prefab 上調
            root.AddComponent<DojoMasterBoss>();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            return saved;
        }
        finally { Object.DestroyImmediate(root); }
    }

    static AnimatorController BuildController(AnimationClip sit, AnimationClip walk, AnimationClip talk, AnimationClip kneel)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        var s = sm.AddState("SitIdle", new Vector3(260, 60)); s.motion = sit;
        var w = sm.AddState("Walk", new Vector3(260, 140)); w.motion = walk;
        var t = sm.AddState("Talk", new Vector3(260, 220)); t.motion = talk;
        if (kneel != null) { var k = sm.AddState("Kneel", new Vector3(260, 300)); k.motion = kneel; }
        sm.defaultState = s;
        // 狀態之間的切換由演出程式用 CrossFade 控制
        AssetDatabase.SaveAssets();
        return controller;
    }

    static Material BuildMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
        else material.shader = shader;
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "DojoMaster_BaseColor.jpg");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "DojoMaster_Normal.png");
        var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "DojoMaster_MetallicSmoothness.png");
        if (material.HasProperty("_BaseMap")) { material.SetTexture("_BaseMap", baseMap); material.SetColor("_BaseColor", Color.white); }
        material.mainTexture = baseMap;
        if (normal != null && material.HasProperty("_BumpMap")) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
        if (ms != null && material.HasProperty("_MetallicGlossMap"))
        {
            material.SetTexture("_MetallicGlossMap", ms);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f);
        }
        // 長袍、袖子是單面網格：兩面都畫，從袖口往裡看才不會破洞
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        material.doubleSidedGI = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    /// <summary>目前姿勢的網格外框（世界座標）。用 SkinnedMeshBounds：自動判斷模型階層的縮放，不會差 100 倍</summary>
    static Bounds MeshBounds(GameObject go)
    {
        if (SkinnedMeshBounds.TryGet(go, out var b)) return b;
        return new Bounds(go.transform.position, Vector3.one);
    }
}
