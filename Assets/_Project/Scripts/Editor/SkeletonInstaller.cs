#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝骷髏小怪模型
///
/// 把 SazenGames 的 Skeleton 素材裝到小怪上：
///   1. 建立 Animator Controller（Assets/_Project/Models/Skeleton/Skeleton_Enemy.controller），
///      每個動畫一個狀態：Idle、Walk、Run、Jump、Fall、Slash01、Slash02、Stab、Hit、Death、Spawn、Scream
///   2. 修改 Enemy_Grunt.prefab：膠囊、眼睛、方塊劍隱藏起來（碰撞和 AI 不變），
///      放進骷髏模型（手上拿著彎刀），加上 EnemyVisual 把 AI 接到動畫。
///      碰撞膠囊會配合模型的身高變大（腳底位置不變）
///   3. Enemy_Ambusher（從天而降的小怪）是 Grunt 的 Variant，會自動變成骷髏；另外換成紅眼的材質方便分辨
///
/// 可以重複執行（會先拿掉舊的模型再重放）。小怪的數值（血量、傷害、速度）不會被改動。
/// 想換回膠囊：刪掉 Enemy_Grunt 裡的 SkeletonModel，把 Mesh Renderer、Eyes、Blade 重新打開。
/// </summary>
public static class SkeletonInstaller
{
    const string GruntPath = "Assets/_Project/Prefabs/Enemy_Grunt.prefab";
    const string AmbusherPath = "Assets/_Project/Prefabs/Enemy_Ambusher.prefab";
    const string OutDir = "Assets/_Project/Models/Skeleton";
    const string ControllerPath = OutDir + "/Skeleton_Enemy.controller";
    const string AmbusherMatPath = OutDir + "/M_Skeleton_Ambusher.mat";
    const string ModelName = "SkeletonModel";

    // 骷髏原本高 1.73m。放大後大約 2.7m（比 2m 高的玩家大一圈）。想調大小就改這個數字再執行一次選單
    const float ModelScale = 1.57f;
    const float OriginalHeight = 1.73f;
    // 碰撞膠囊跟著模型變大（腳底的位置不變，所以場景裡小怪擺放的高度不用動）
    const float CapsuleRadius = 0.6f;

    // Animator 狀態名稱 → 動畫檔名（Art/Animations 底下）
    static readonly (string state, string file)[] Clips =
    {
        ("Idle", "Skeleton_idle"), ("Walk", "Skeleton_walk_forward"), ("Run", "Skeleton_run_forward"),
        ("Jump", "Skeleton_jump"), ("Fall", "Skeleton_fall"),
        ("Slash01", "Skeleton_slash01"), ("Slash02", "Skeleton_slash02"), ("Stab", "Skeleton_stab"),
        ("Hit", "Skeleton_take_damage"), ("Death", "Skeleton_death"),
        ("Spawn", "Skeleton_spawn"), ("Scream", "Skeleton_scream"),
    };

    [MenuItem("GeminiGrowUp/安裝骷髏小怪模型")]
    public static void Install()
    {
        // ---------- 找素材（不管資料夾被搬到哪裡都找得到） ----------
        string skeletonPrefabPath = FindAsset("Skeleton_110", "t:Prefab", ".prefab");
        if (skeletonPrefabPath == null)
        {
            EditorUtility.DisplayDialog("找不到骷髏素材", "專案裡找不到 Skeleton_110.prefab（SazenGames 的 Skeleton 素材）。", "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(GruntPath) == null)
        {
            EditorUtility.DisplayDialog("找不到小怪", $"找不到 {GruntPath}。\n請先執行「GeminiGrowUp → 更新第二層（寬走廊 + 小怪）」建立小怪 Prefab。", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("安裝骷髏小怪模型",
                "會把 Enemy_Grunt（以及它的 Variant：Enemy_Ambusher）的膠囊換成骷髏模型，並接上動畫。\n" +
                "小怪的數值和 AI 不會被改動。建議先 commit。要繼續嗎？", "繼續", "取消"))
            return;

        BossArenaBuilder.EnsureFolder(OutDir);
        var log = new List<string>();

        var controller = BuildController(log);
        var skeletonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(skeletonPrefabPath);
        if (!PutModelIntoGrunt(skeletonPrefab, controller, log)) return;
        TintAmbusher(log);

        AssetDatabase.SaveAssets();
        Debug.Log("[GeminiGrowUp] 骷髏小怪模型安裝完成：\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("完成", string.Join("\n", log), "OK");
    }

    // =====================================================================

    /// <summary>用檔名找素材，回傳路徑（找不到回傳 null）</summary>
    static string FindAsset(string name, string filter, string extension)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{name} {filter}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name && path.EndsWith(extension)) return path;
        }
        return null;
    }

    static AnimationClip LoadClip(string fileName)
    {
        // 排除 RootMotion 資料夾裡的版本（那些會讓角色自己往前走，我們用程式控制移動）
        foreach (string guid in AssetDatabase.FindAssets($"{fileName} t:Model"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != fileName) continue;
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null) return clip;
        }
        return null;
    }

    static AnimatorController BuildController(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;

        // 不需要拉轉換線：EnemyVisual 直接用名稱切換狀態
        var missing = new List<string>();
        foreach (var (stateName, file) in Clips)
        {
            var clip = LoadClip(file);
            if (clip == null) { missing.Add(file); continue; }
            var state = sm.AddState(stateName);
            state.motion = clip;
            if (stateName == "Idle") sm.defaultState = state;
        }

        log.Add($"Animator：{Clips.Length - missing.Count} 個動畫狀態");
        if (missing.Count > 0) log.Add("⚠ 找不到這些動畫（對應的動作會沒有動畫）：" + string.Join("、", missing));
        return controller;
    }

    static bool PutModelIntoGrunt(GameObject skeletonPrefab, AnimatorController controller, List<string> log)
    {
        var root = PrefabUtility.LoadPrefabContents(GruntPath);
        try
        {
            var cc = root.GetComponent<CharacterController>();
            var combat = root.GetComponent<EnemyCombat>();
            var health = root.GetComponent<EnemyHealth>();
            if (cc == null || health == null || root.GetComponent<EnemyAI>() == null)
            {
                EditorUtility.DisplayDialog("小怪 Prefab 不對", "Enemy_Grunt 上找不到 CharacterController / EnemyHealth / EnemyAI，沒有修改。", "OK");
                return false;
            }

            // 拿掉舊的模型
            var old = root.transform.Find(ModelName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // 膠囊、眼睛、方塊劍：隱藏（不刪掉，之後想換回來還可以打開）
            var capsule = root.GetComponent<MeshRenderer>();
            if (capsule != null) capsule.enabled = false;
            var eyes = root.transform.Find("Eyes");
            if (eyes != null) eyes.gameObject.SetActive(false);
            if (combat != null && combat.weaponPivot != null)
                foreach (Transform child in combat.weaponPivot) child.gameObject.SetActive(false);

            // 碰撞膠囊配合模型的身高：頂端往上長，腳底留在原位
            float feetLocalY = cc.center.y - cc.height * 0.5f;
            float height = Mathf.Max(2f, Mathf.Round(OriginalHeight * ModelScale * 10f) / 10f);
            cc.height = height;
            cc.radius = CapsuleRadius;
            cc.center = new Vector3(0f, feetLocalY + height * 0.5f, 0f);
            health.barHeightOffset = feetLocalY + height + 0.35f; // 血條在頭頂上方

            // 放進骷髏：腳底對齊碰撞膠囊的底部
            var model = (GameObject)PrefabUtility.InstantiatePrefab(skeletonPrefab, root.scene);
            model.name = ModelName;
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = new Vector3(0f, cc.center.y - cc.height * 0.5f, 0f);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * ModelScale;

            // 素材附的示範腳本用不到
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("SazenGames"))
                    Object.DestroyImmediate(mb);

            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;   // 移動由程式控制，動畫只負責姿勢
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var visual = root.GetComponent<EnemyVisual>();
            if (visual == null) visual = root.AddComponent<EnemyVisual>();
            visual.animator = animator;

            health.flashColor = new Color(1f, 0.3f, 0.25f); // 有貼圖的模型閃白色看不出來，改閃紅色

            PrefabUtility.SaveAsPrefabAsset(root, GruntPath);
            log.Add($"Enemy_Grunt：膠囊換成骷髏模型（約 {OriginalHeight * ModelScale:0.0}m 高），加上 EnemyVisual");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>從天而降的小怪換成紅眼的材質，跟一般小怪（綠眼）分得出來</summary>
    static void TintAmbusher(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(AmbusherPath) == null) return;
        string srcMatPath = FindAsset("Skeleton_Mat", "t:Material", ".mat");
        if (srcMatPath == null) { log.Add("⚠ 找不到 Skeleton_Mat，Enemy_Ambusher 沒有換顏色"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(AmbusherMatPath);
        if (mat == null)
        {
            AssetDatabase.CopyAsset(srcMatPath, AmbusherMatPath);
            mat = AssetDatabase.LoadAssetAtPath<Material>(AmbusherMatPath);
        }
        if (mat == null) { log.Add("⚠ 建立紅眼材質失敗，Enemy_Ambusher 沒有換顏色"); return; }
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(2.4f, 0.15f, 0.05f));
        mat.SetColor("_BaseColor", new Color(0.82f, 0.74f, 0.7f));
        EditorUtility.SetDirty(mat);

        var root = PrefabUtility.LoadPrefabContents(AmbusherPath);
        try
        {
            var model = root.transform.Find(ModelName);
            if (model == null) { log.Add("⚠ Enemy_Ambusher 裡沒有骷髏模型，沒有換顏色"); return; }
            // 只換骷髏身體的材質，手上的彎刀不動
            var srcMat = AssetDatabase.LoadAssetAtPath<Material>(srcMatPath);
            int n = 0;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial != srcMat && r.sharedMaterial != mat) continue;
                r.sharedMaterial = mat;
                n++;
            }
            PrefabUtility.SaveAsPrefabAsset(root, AmbusherPath);
            log.Add($"Enemy_Ambusher：換成紅眼材質（{n} 個部位）");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
