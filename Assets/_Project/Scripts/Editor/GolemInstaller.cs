#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝 / 更新石像 Boss 模型
///
/// 每次在 Blender 改完石像、重新匯出 Golem.fbx 覆蓋 Assets/_Project/Models/Golem/Golem.fbx 之後，
/// 再執行一次這個選單就會：
///   1. 設定 FBX 匯入：Generic 骨架、動畫名稱（Golem_Slam → Slam）、Idle / Walk 循環、材質換成 URP 材質（眼睛 / 核心會發光）
///   2. 重建 Golem.controller（Animator，每個動畫一個狀態，名稱跟招式一樣）
///   3. 修改 BossRoom.prefab：拿掉膠囊，換成石像模型，並加上 BossVisual
/// </summary>
public static class GolemInstaller
{
    const string ModelDir = "Assets/_Project/Models/Golem";
    const string FbxPath = ModelDir + "/Golem.fbx";
    const string ControllerPath = ModelDir + "/Golem.controller";
    const string ModelName = "GolemModel";
    // Blender 匯出的模型在 Unity 裡會面向 -Z，轉 180 度讓臉朝向 Boss 的前方（+Z）
    const float ModelYaw = 180f;

    static readonly string[] LoopingClips = { "Idle", "Walk" };

    [MenuItem("GeminiGrowUp/安裝 或 更新石像 Boss 模型")]
    public static void Install()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
        {
            EditorUtility.DisplayDialog("找不到模型", $"找不到 {FbxPath}。\n請先把 Blender 匯出的 Golem.fbx 放到這個位置。", "OK");
            return;
        }
        string bossRoomPath = Floor02Builder.FindBossRoomPrefab();
        if (bossRoomPath == null)
        {
            EditorUtility.DisplayDialog("找不到 BossRoom", "專案裡找不到名為 BossRoom 的 Prefab。", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("安裝石像 Boss 模型",
                "會設定 Golem.fbx 的匯入方式、重建 Golem.controller，並把 BossRoom.prefab 裡的 Boss 換成石像模型。\n" +
                "Boss 的招式數值不會被改動。建議先 commit。要繼續嗎？",
                "繼續", "取消"))
            return;

        ConfigureImporter();
        var controller = BuildController();
        bool ok = PutModelIntoBossRoom(bossRoomPath, controller);

        AssetDatabase.SaveAssets();
        if (ok) Debug.Log("[GeminiGrowUp] 石像模型已安裝到 BossRoom。按 Play 測試，數字鍵 1~4 可以叫 Boss 放招。");
    }

    // =====================================================================
    //  1. FBX 匯入設定
    // =====================================================================

    static void ConfigureImporter()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.importAnimation = true;
        imp.bakeAxisConversion = true;  // Blender 的 Z-up 直接轉好，模型根節點不會多一個 -90 度旋轉
        imp.globalScale = 1f;
        imp.useFileScale = true;

        // 動畫：Golem_Slam → Slam，Idle / Walk 設成循環
        var clips = imp.defaultClipAnimations;
        foreach (var clip in clips)
        {
            string shortName = clip.takeName.Split('|').Last();
            if (shortName.StartsWith("Golem_")) shortName = shortName.Substring("Golem_".Length);
            clip.name = shortName;
            clip.loopTime = LoopingClips.Contains(shortName);
        }
        imp.clipAnimations = clips;

        // 材質：換成 URP 材質（FBX 內建的材質不會發光）
        RemapMaterial(imp, "M_GolemStone", new Color(0.25f, 0.235f, 0.22f), null);
        RemapMaterial(imp, "M_GolemStoneDark", new Color(0.14f, 0.13f, 0.13f), null);
        RemapMaterial(imp, "M_GolemEye", new Color(1f, 0.8f, 0.25f), new Color(1f, 0.75f, 0.2f) * 4f);
        RemapMaterial(imp, "M_GolemCore", new Color(1f, 0.35f, 0.1f), new Color(1f, 0.3f, 0.05f) * 5f);

        imp.SaveAndReimport();
    }

    static void RemapMaterial(ModelImporter imp, string name, Color baseColor, Color? emission)
    {
        string path = $"{ModelDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_Color", baseColor);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", emission.HasValue ? 0.5f : 0.1f);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);
        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
    }

    // =====================================================================
    //  2. Animator Controller
    // =====================================================================

    static AnimatorController BuildController()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToList();

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;

        // 不需要拉轉換線：BossVisual 直接用名稱切換狀態
        foreach (var clip in clips)
        {
            var state = sm.AddState(clip.name);
            state.motion = clip;
            if (clip.name == "Idle") sm.defaultState = state;
        }

        var missing = new List<string>();
        foreach (var need in new[] { "Idle", "Walk", "Swipe", "Slam", "Roar", "Beam", "Jump", "JumpLand", "Death" })
            if (!clips.Any(c => c.name == need)) missing.Add(need);
        if (missing.Count > 0)
            Debug.LogWarning("[GeminiGrowUp] Golem.fbx 裡少了這些動畫（Boss 放這些招式時會沒有動作）：" + string.Join("、", missing));

        return controller;
    }

    // =====================================================================
    //  3. 換掉 BossRoom 裡的膠囊
    // =====================================================================

    static bool PutModelIntoBossRoom(string bossRoomPath, AnimatorController controller)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var root = PrefabUtility.LoadPrefabContents(bossRoomPath);
        try
        {
            var bc = root.GetComponentInChildren<BossController>(true);
            if (bc == null || bc.bodyPivot == null)
            {
                Debug.LogError("[GeminiGrowUp] BossRoom 裡找不到 BossController 或它的 Body Pivot，沒有修改。");
                return false;
            }
            Transform pivot = bc.bodyPivot;
            // BodyPivot 是程式用來前傾 / 後仰的軸心，必須保持 0 度（轉向要轉在模型上，不能轉在這裡）
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;

            // 拿掉膠囊身體、眼睛、舊的模型
            var remove = new List<GameObject>();
            foreach (Transform child in pivot)
                if (child.name == "Body" || child.name.StartsWith("Eye") || child.name == ModelName)
                    remove.Add(child.gameObject);
            foreach (var go in remove) Object.DestroyImmediate(go);

            // 放入石像模型
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.scene);
            model.name = ModelName;
            model.transform.SetParent(pivot, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            model.transform.localScale = Vector3.one;

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var visual = bc.GetComponent<BossVisual>();
            if (visual == null) visual = bc.gameObject.AddComponent<BossVisual>();
            visual.animator = animator;

            PrefabUtility.SaveAsPrefabAsset(root, bossRoomPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
