#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝玩家（聖騎士 + Cinemachine）
///
/// 1. 玩家的膠囊換成聖騎士模型（Mixamo Lite Sword and Shield Pack），每個動作都有動畫：
///    待機 / 走路 / 跑步 / 跳躍 / 空中 / 落地 / 三段連擊 / 舉盾衝撞 / 受擊 / 死亡 / 在船上拉弓
///    （跳躍、空中、落地、走路用 Starter Assets 的 Humanoid 動畫，其他用 Mixamo 劍盾動畫）
/// 2. 鏡頭改用 Cinemachine：Main Camera 加 CinemachineBrain，PlayerRig 底下放 CinemachineCamera
///    （Third Person Follow + Avoid Obstacles）和注視點，ThirdPersonCamera 只負責讀滑鼠和震動
/// 3. 維京村莊的敵人聖騎士換成暗紅色盔甲，跟玩家分得出來
///
/// 會修改 Prefabs/PlayerRig 和 Resources/Prefabs/PlayerRig（三個關卡用的都是這個 Prefab）。可以重複執行。
/// </summary>
public static class PlayerInstaller
{
    static readonly string[] RigPaths =
    {
        "Assets/_Project/Prefabs/PlayerRig.prefab",
        "Assets/_Project/Resources/Prefabs/PlayerRig.prefab",
    };
    const string EnemyPaladinPath = "Assets/_Project/Prefabs/Enemy_Paladin.prefab";
    const string StarterDir = "Assets/_Project/ThirdParty/StarterAssets Animations";
    const string ControllerPath = PaladinInstaller.OutDir + "/Paladin_Player.controller";
    const string ModelName = "PaladinModel";
    const string CmCameraName = "CM PlayerFollowCamera";
    const string CmTargetName = "CM CameraTarget";

    // 玩家身高 (m)，跟 CharacterController 的高度（2m）差不多
    const float PlayerHeight = 1.95f;

    // Animator 狀態 → 動畫（"pack:" = Mixamo 劍盾包，"starter:" = Starter Assets）
    static readonly (string state, string clip)[] Clips =
    {
        ("Idle", "pack:sword and shield idle"),
        ("Walk", "starter:Locomotion--Walk_N.anim"),
        ("Run", "pack:sword and shield run"),
        ("Jump", "starter:Jump--Jump.anim"),
        ("Fall", "starter:Jump--InAir.anim"),
        ("Land", "starter:Locomotion--Walk_N_Land.anim"),
        ("Slash01", "pack:sword and shield attack"),
        ("Slash02", "pack:sword and shield attack (2)"),
        ("Stab", "pack:sword and shield attack (3)"),
        ("Hit", "pack:sword and shield block (2)"),
        ("Death", "pack:sword and shield death"),
        ("ShieldBash", "pack:sword and shield block"),  // 右鍵舉盾衝撞
        ("BlockIdle", "pack:sword and shield block idle"), // 右鍵按住格擋
        ("BlockHit", "pack:sword and shield block (2)"),   // 盾牌擋下攻擊
        ("BowDraw", "bow:standing draw arrow"),        // 在船上拉弓（Longbow Pack）
    };

    [MenuItem("GeminiGrowUp/安裝玩家（聖騎士 + Cinemachine）")]
    public static void Install()
    {
        string modelPath = $"{PaladinInstaller.PackDir}/{PaladinInstaller.ModelFile}.fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
        {
            EditorUtility.DisplayDialog("找不到聖騎士", $"找不到 {modelPath}", "OK");
            return;
        }
        if (!Application.isBatchMode)
        {
            if (!EditorUtility.DisplayDialog("安裝玩家",
                    "會做這些事：\n" +
                    "・玩家的膠囊換成聖騎士模型，每個動作都有動畫\n" +
                    "・鏡頭改用 Cinemachine（跟隨玩家、不穿牆）\n" +
                    "・維京村莊的敵人聖騎士換成暗紅色盔甲\n\n" +
                    "會修改 PlayerRig.prefab（三個關卡共用）。建議先 commit。要繼續嗎？", "繼續", "取消"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        }

        BossArenaBuilder.EnsureFolder(PaladinInstaller.OutDir);
        var log = new List<string>();

        PaladinInstaller.ConfigureImports(modelPath, log);
        var material = PaladinInstaller.BuildMaterial(log);
        var controller = BuildController(log);
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

        foreach (string rigPath in RigPaths)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(rigPath) != null)
                SetupRig(rigPath, modelAsset, material, controller, log);

        RecolorEnemyPaladins(material, log);

        AssetDatabase.SaveAssets();
        Debug.Log("[GeminiGrowUp] 玩家安裝完成：\n" + string.Join("\n", log));
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("完成", string.Join("\n", log), "OK");
    }

    // ---------------------------------------------------------------- 動畫

    static AnimationClip LoadClip(string spec)
    {
        if (spec.StartsWith("pack:")) return PaladinInstaller.LoadClip(spec.Substring(5));
        if (spec.StartsWith("bow:"))
            return AssetDatabase.LoadAllAssetsAtPath($"Assets/_Project/ThirdParty/Lite Longbow Pack/{spec.Substring(4)}.fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        string path = $"{StarterDir}/{spec.Substring(8)}.fbx";
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    static AnimatorController BuildController(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;

        // 不需要拉轉換線：PlayerVisual 直接用名稱切換狀態
        var missing = new List<string>();
        foreach (var (stateName, spec) in Clips)
        {
            var clip = LoadClip(spec);
            if (clip == null) { missing.Add(spec); continue; }
            var state = sm.AddState(stateName);
            state.motion = clip;
            if (stateName == "Idle") sm.defaultState = state;
        }
        AddLocomotionBlendTree(controller, log);
        log.Add($"玩家 Animator：{Clips.Length - missing.Count} 個動畫狀態 + Locomotion 混合樹");
        if (missing.Count > 0) log.Add("⚠ 找不到這些動畫：" + string.Join("、", missing));
        return controller;
    }

    // 混合樹算出來的走路 / 跑步動畫原本的移動速度，SetupRig 會寫進 PlayerVisual
    static float walkNaturalSpeed = 1.8f, runNaturalSpeed = 4.5f;

    /// <summary>
    /// 移動用的混合樹（狀態名稱 Locomotion，參數 Speed = 移動速度 m/s）：
    /// 待機 → 走路 → 跑步依速度連續混合，不會在門檻附近跳來跳去；
    /// 每個動畫放在它自己原本的移動速度上（用動畫的根位移量出來），腳步比較不會滑
    /// </summary>
    static void AddLocomotionBlendTree(AnimatorController controller, List<string> log)
    {
        var idle = LoadClip(Clips.First(c => c.state == "Idle").clip);
        var walk = LoadClip(Clips.First(c => c.state == "Walk").clip);
        var run = LoadClip(Clips.First(c => c.state == "Run").clip);
        if (idle == null || walk == null || run == null) return;

        walkNaturalSpeed = NaturalSpeed(walk, 1.8f);
        runNaturalSpeed = Mathf.Max(NaturalSpeed(run, 4.5f), walkNaturalSpeed + 1f);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, walkNaturalSpeed);
        tree.AddChild(run, runNaturalSpeed);
        controller.layers[0].stateMachine.defaultState = state;
        log.Add($"Locomotion 混合樹：待機 0 → 走路 {walkNaturalSpeed:0.0} → 跑步 {runNaturalSpeed:0.0} m/s");
    }

    /// <summary>動畫原本的水平移動速度（用根位移算）；動畫是原地播放的就用 fallback</summary>
    static float NaturalSpeed(AnimationClip clip, float fallback)
    {
        Vector3 v = clip.averageSpeed;
        float s = new Vector2(v.x, v.z).magnitude;
        return s > 0.3f ? s : fallback;
    }

    // ---------------------------------------------------------------- PlayerRig

    static void SetupRig(string rigPath, GameObject modelAsset, Material material, AnimatorController controller, List<string> log)
    {
        var root = PrefabUtility.LoadPrefabContents(rigPath);
        try
        {
            var health = root.GetComponentInChildren<PlayerHealth>(true);
            if (health == null) { log.Add($"⚠ {rigPath} 裡找不到玩家（PlayerHealth），跳過"); return; }
            var player = health.gameObject;
            var cc = player.GetComponent<CharacterController>();

            // ---- 模型 ----
            var old = player.transform.Find(ModelName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var capsule = player.GetComponent<MeshRenderer>();
            if (capsule != null) capsule.enabled = false;
            var facing = player.transform.Find("FacingIndicator");
            if (facing != null) facing.gameObject.SetActive(false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.scene);
            model.name = ModelName;
            model.transform.SetParent(player.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            // MeasureHeight 量的是世界座標的身高（已經含玩家物件本身的縮放）
            float rawHeight = PaladinInstaller.MeasureHeight(model);
            float scale = rawHeight > 0.1f ? PlayerHeight / rawHeight : 1f;
            model.transform.localScale = Vector3.one * scale;
            float feetLocalY = cc != null ? cc.center.y - cc.height * 0.5f : -1f;
            model.transform.localPosition = new Vector3(0f, feetLocalY, 0f);

            if (material != null)
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();

            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var visual = player.GetComponent<PlayerVisual>();
            if (visual == null) visual = player.AddComponent<PlayerVisual>();
            visual.animator = animator;
            visual.walkAnimSpeed = walkNaturalSpeed;
            visual.runAnimSpeed = runNaturalSpeed;
            visual.attacks = new[]
            {
                Attack("Slash01", "sword and shield attack"),
                Attack("Slash02", "sword and shield attack (2)"),
                Attack("Stab", "sword and shield attack (3)"),
            };

            // ---- Cinemachine ----
            var tpc = root.GetComponentInChildren<ThirdPersonCamera>(true);
            if (tpc != null)
            {
                if (tpc.GetComponent<CinemachineBrain>() == null) tpc.gameObject.AddComponent<CinemachineBrain>();

                var cmTf = root.transform.Find(CmCameraName);
                var cmGo = cmTf != null ? cmTf.gameObject : new GameObject(CmCameraName);
                cmGo.transform.SetParent(root.transform, false);
                var vcam = cmGo.GetComponent<CinemachineCamera>();
                if (vcam == null) vcam = cmGo.AddComponent<CinemachineCamera>();
                if (cmGo.GetComponent<CinemachineThirdPersonFollow>() == null) cmGo.AddComponent<CinemachineThirdPersonFollow>();

                var targetTf = root.transform.Find(CmTargetName);
                if (targetTf == null)
                {
                    targetTf = new GameObject(CmTargetName).transform;
                    targetTf.SetParent(root.transform, false);
                }
                vcam.Follow = targetTf;
                var cam = tpc.GetComponent<Camera>();
                if (cam != null)
                {
                    vcam.Lens.FieldOfView = cam.fieldOfView;
                    vcam.Lens.NearClipPlane = cam.nearClipPlane;
                    vcam.Lens.FarClipPlane = cam.farClipPlane;
                }
                tpc.cinemachineCamera = vcam;
                tpc.cameraTarget = targetTf;
                if (tpc.target == null) tpc.target = player.transform;
            }

            PrefabUtility.SaveAsPrefabAsset(root, rigPath);
            log.Add($"{rigPath}：聖騎士約 {PlayerHeight:0.0}m（縮放 {scale:0.00}），Cinemachine 鏡頭");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>攻擊動畫：揮下去的那一刻大約在動畫的 45%</summary>
    static EnemyVisual.AttackAnim Attack(string state, string file)
    {
        var clip = PaladinInstaller.LoadClip(file);
        return new EnemyVisual.AttackAnim
        {
            state = state,
            hitTime = clip != null ? clip.length * 0.45f : 0.5f,
            clipLength = clip != null ? clip.length : 0f,
        };
    }

    // ---------------------------------------------------------------- 敵人換色

    static void RecolorEnemyPaladins(Material baseMat, List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPaladinPath) == null) return;
        var enemyMat = PaladinInstaller.BuildEnemyMaterial(baseMat, log);
        if (enemyMat == null) return;

        var root = PrefabUtility.LoadPrefabContents(EnemyPaladinPath);
        try
        {
            var model = root.transform.Find(ModelName);
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(enemyMat, r.sharedMaterials.Length).ToArray();
            // 攻擊火花用橘紅色，跟骷髏（淡綠色）分得出來
            var combat = root.GetComponent<EnemyCombat>();
            if (combat != null) combat.attackVfxColor = new Color(1f, 0.45f, 0.15f);
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPaladinPath);
            log.Add("Enemy_Paladin：換成暗紅色盔甲、橘紅色攻擊火花");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
