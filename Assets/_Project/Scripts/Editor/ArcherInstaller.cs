#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝敵艦弓箭手
///
/// 把 Mixamo 的 Lite Longbow Pack（Erika Archer + 弓箭動畫）裝成第二關追擊敵艦上的弓箭手：
///   1. 設定匯入：模型和動畫都用 Humanoid；待機、走、跑設成循環
///   2. 把 FBX 裡包著的貼圖拆到 Textures 資料夾（不拆的話模型會是白色的），重新匯入讓材質接上貼圖
///   3. 建立 Animator Controller（Models/Archer/Archer_Crew.controller）：Idle、Draw（拉弓放箭）
///   4. 建立 Resources/Prefabs/BoatArcher.prefab（約 ArcherHeight 公尺高，掛 BoatArcherVisual）
///      EnemyBoat 生成船員時會優先用它；找不到才用原本的骷髏
///
/// 可以重複執行。
/// </summary>
public static class ArcherInstaller
{
    const string PackDir = "Assets/_Project/ThirdParty/Lite Longbow Pack";
    const string ModelFile = "Erika Archer With Bow Arrow";
    const string TextureDir = PackDir + "/Textures";
    const string OutDir = "Assets/_Project/Models/Archer";
    const string ControllerPath = OutDir + "/Archer_Crew.controller";
    const string PrefabPath = "Assets/_Project/Resources/Prefabs/BoatArcher.prefab";
    const float ArcherHeight = 1.8f;
    const string DrawClip = "standing draw arrow";

    static readonly (string state, string file)[] Clips =
    {
        ("Idle", "standing idle 01"),
        ("Draw", DrawClip),
        ("Equip", "standing equip bow"),
    };

    static readonly string[] LoopingFiles = { "standing idle 01", "unarmed idle 01", "standing walk forward", "standing run forward" };

    [MenuItem("GeminiGrowUp/安裝敵艦弓箭手")]
    public static void Install()
    {
        string modelPath = $"{PackDir}/{ModelFile}.fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
        {
            EditorUtility.DisplayDialog("找不到弓箭手", $"找不到 {modelPath}", "OK");
            return;
        }
        BossArenaBuilder.EnsureFolder(OutDir);
        var log = new List<string>();

        ConfigureModel(modelPath, log);
        ConfigureAnimations(log);
        var controller = BuildController(log);
        BuildPrefab(modelPath, controller, log);

        AssetDatabase.SaveAssets();
        Debug.Log("[GeminiGrowUp] 敵艦弓箭手安裝完成：\n" + string.Join("\n", log));
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("完成", string.Join("\n", log), "OK");
    }

    // ---------------------------------------------------------------- 匯入

    static void ConfigureModel(string modelPath, List<string> log)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        bool changed = false;
        if (mi.animationType != ModelImporterAnimationType.Human)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }
        if (mi.importAnimation) { mi.importAnimation = false; changed = true; }
        if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            changed = true;
        }
        if (changed) mi.SaveAndReimport();

        // 貼圖包在 FBX 裡：拆出來，再重新匯入一次，材質才會接上貼圖
        if (!AssetDatabase.IsValidFolder(TextureDir))
        {
            BossArenaBuilder.EnsureFolder(TextureDir);
            mi.ExtractTextures(TextureDir);
            AssetDatabase.Refresh();
            int n = AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }).Length;
            log.Add($"從 FBX 拆出 {n} 張貼圖到 {TextureDir}");
        }
        // 法線貼圖要設成 Normal Map
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!(name.Contains("normal") || name.EndsWith("_nm") || name.Contains("stdnm"))) continue;
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti.textureType == TextureImporterType.NormalMap) continue;
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
        mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        mi.SaveAndReimport();
        log.Add("模型：Humanoid，材質從 FBX 的材質描述匯入（含貼圖）");
    }

    static void ConfigureAnimations(List<string> log)
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { PackDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (file == ModelFile) continue;

            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
            }
            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
            bool loop = LoopingFiles.Contains(file);
            foreach (var c in clips)
            {
                c.name = file;
                c.loopTime = loop;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;  // 站在船上不會走動：位移全部烘進姿勢
                c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            n++;
        }
        log.Add($"動畫：{n} 個（Humanoid）");
    }

    static AnimationClip LoadClip(string file) =>
        AssetDatabase.LoadAllAssetsAtPath($"{PackDir}/{file}.fbx").OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    static AnimatorController BuildController(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        int ok = 0;
        foreach (var (stateName, file) in Clips)
        {
            var clip = LoadClip(file);
            if (clip == null) { log.Add($"⚠ 找不到動畫 {file}"); continue; }
            var state = sm.AddState(stateName);
            state.motion = clip;
            if (stateName == "Idle") sm.defaultState = state;
            ok++;
        }
        log.Add($"Animator：{ok} 個動畫狀態");
        return controller;
    }

    // ---------------------------------------------------------------- Prefab

    static void BuildPrefab(string modelPath, AnimatorController controller, List<string> log)
    {
        var root = new GameObject("BoatArcher");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            model.name = "ArcherModel";
            model.transform.SetParent(root.transform, false);
            float raw = PaladinInstaller.MeasureHeight(model);
            float scale = raw > 0.1f ? ArcherHeight / raw : 1f;
            model.transform.localScale = Vector3.one * scale;

            // FBX 裡有一支沒綁在骨架上的箭，T-pose 時平躺在腳邊，播動畫也會一直留在甲板上：藏起來
            // （射出去的箭由 EnemyBoat 另外產生）
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                if (b.max.y < 0.3f && b.size.y < 0.2f)
                {
                    r.enabled = false;
                    log.Add($"藏起地上的 {r.name}");
                }
            }

            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var visual = root.AddComponent<BoatArcherVisual>();
            visual.animator = animator;
            var draw = LoadClip(DrawClip);
            // 拉弓動畫大約在 60% 的地方放手
            if (draw != null) visual.releaseTime = draw.length * 0.6f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            log.Add($"BoatArcher.prefab：約 {ArcherHeight:0.0}m（縮放 {scale:0.00}），放箭時間 {visual.releaseTime:0.00} 秒");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
#endif
