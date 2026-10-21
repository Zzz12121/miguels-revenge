#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝 或 更新 Boss 模型（骸骨巨像）
///
/// Boss 的外型現在是「骸骨巨像」（Art/bone_colossus_generator.py 產生的）。
/// 檔名、骨頭、動畫、材質的名稱都沿用石像時期的（Golem.fbx、Golem_Slam、M_GolemStone…），
/// 這樣 BossRoom.prefab 裡的參照不會斷，所以下面還是到處看得到 Golem 這個字。
///
/// 每次在 Blender 改完模型、重新匯出覆蓋 Assets/_Project/Models/Golem/Golem.fbx 之後，
/// 再執行一次這個選單就會：
///   1. 設定 FBX 匯入：Generic 骨架、動畫名稱（Golem_Slam → Slam）、Idle / Walk / SummonLoop 循環、材質換成 URP 材質（眼睛 / 核心會發光）
///   2. 重建 Golem.controller（Animator，每個動畫一個狀態，名稱跟招式一樣）
///   3. 修改 BossRoom.prefab：拿掉舊的身體，換成新模型，並加上 BossVisual
///
/// 材質對應：M_GolemStone = 骨頭、M_GolemStoneDark = 關節 / 角 / 爪、M_GolemEye = 眼睛（綠光）、
///           M_GolemCore = 胸口核心和手臂的能量（綠光）、M_GolemVoid = 眼窩和裂痕（黑）
/// 想換顏色就改 ConfigureImporter 裡的數字再執行一次選單（直接改 .mat 會在下次執行時被蓋掉）。
/// </summary>
public static class GolemInstaller
{
    const string ModelDir = "Assets/_Project/Models/Golem";
    const string FbxPath = ModelDir + "/Golem.fbx";
    const string ControllerPath = ModelDir + "/Golem.controller";
    const string ModelName = "GolemModel";
    // Blender 匯出的模型在 Unity 裡會面向 -Z，轉 180 度讓臉朝向 Boss 的前方（+Z）
    const float ModelYaw = 180f;

    static readonly string[] LoopingClips = { "Idle", "Walk", "SummonLoop" };

    [MenuItem("GeminiGrowUp/安裝 或 更新 Boss 模型（骸骨巨像）")]
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
        if (!EditorUtility.DisplayDialog("安裝 Boss 模型（骸骨巨像）",
                "會設定 Golem.fbx 的匯入方式和材質顏色、重建 Golem.controller，並把 BossRoom.prefab 裡的 Boss 換成最新的模型。\n" +
                "Boss 的招式數值不會被改動。建議先 commit。要繼續嗎？",
                "繼續", "取消"))
            return;

        if (Reinstall()) Debug.Log("[GeminiGrowUp] Boss 模型（骸骨巨像）已安裝到 BossRoom。按 Play 測試。");
    }

    /// <summary>
    /// 不跳視窗，直接重新安裝一次（別的安裝選單在 Golem.fbx 多了新動畫之後會呼叫這個）。
    /// 回傳有沒有成功。
    /// </summary>
    internal static bool Reinstall()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null) return false;
        string bossRoomPath = Floor02Builder.FindBossRoomPrefab();
        if (bossRoomPath == null) return false;

        ConfigureImporter();
        var controller = BuildController();
        bool ok = PutModelIntoBossRoom(bossRoomPath, controller);
        AssetDatabase.SaveAssets();
        return ok;
    }

    /// <summary>
    /// Golem.fbx 裡已經有召喚動畫（Summon / SummonLoop）、但目前的 Animator 還沒有 → 需要重新安裝一次。
    /// 還沒安裝過模型（沒有 Golem.controller）或 FBX 是舊的（沒有這些動畫）時回傳 false。
    /// </summary>
    internal static bool NeedsSummonUpdate()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null) return false;
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null) return false;
        foreach (string need in new[] { "Summon", "SummonLoop" })
        {
            if (ControllerHasState(need)) continue;
            foreach (var clip in imp.defaultClipAnimations)
                if (clip.takeName.EndsWith("_" + need)) return true;
        }
        return false;
    }

    /// <summary>Golem.controller 裡有沒有某個招式的動畫狀態</summary>
    internal static bool ControllerHasState(string stateName)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0) return false;
        foreach (var s in controller.layers[0].stateMachine.states)
            if (s.state != null && s.state.name == stateName) return true;
        return false;
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

        // 動畫：Golem_Slam → Slam，Idle / Walk / SummonLoop 設成循環
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
        RemapMaterial(imp, "M_GolemStone", new Color(0.8f, 0.76f, 0.66f), null);                                    // 骨頭
        RemapMaterial(imp, "M_GolemStoneDark", new Color(0.58f, 0.54f, 0.47f), null);                               // 關節、角、爪
        RemapMaterial(imp, "M_GolemEye", new Color(0.4f, 1f, 0.55f), new Color(0.15f, 1f, 0.35f) * 4f);             // 眼睛（綠光）
        RemapMaterial(imp, "M_GolemCore", new Color(0.3f, 1f, 0.6f), new Color(0.1f, 1f, 0.4f) * 3f);               // 核心、手臂能量（綠光）
        RemapMaterial(imp, "M_GolemVoid", new Color(0.02f, 0.02f, 0.025f), null);                                   // 眼窩、裂痕（黑）

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
        foreach (var need in new[] { "Idle", "Walk", "Swipe", "Slam", "Roar", "Beam", "Jump", "JumpLand", "Summon", "SummonLoop", "Death" })
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

            // 放入 Boss 模型
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
