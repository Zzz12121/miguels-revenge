#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝聖騎士敵人（維京村莊）
///
/// 把 Mixamo 的 Lite Sword and Shield Pack（Paladin 模型 + 劍盾動畫）裝成維京村莊的敵人：
///   1. 設定匯入：模型和動畫都用 Humanoid；待機 / 跑步 / 防禦待機設成循環，
///      根位移不套用在角色上（移動由 AI 控制，動畫只負責姿勢）
///   2. 建立 URP 材質 M_Paladin（貼圖原本包在 FBX 裡，Unity 沒有拆出來，所以模型會是白色的；
///      貼圖已經拆到 Textures 資料夾，這裡把它們接到材質上）
///   3. 建立 Animator Controller（Assets/_Project/Models/Paladin/Paladin_Enemy.controller），
///      狀態名稱和骷髏一樣（Idle、Walk、Run、Slash01…），EnemyVisual 不用改就能用
///   4. 複製 Enemy_Grunt 成 Enemy_Paladin.prefab：拿掉骷髏、放進聖騎士（約 PaladinHeight 公尺高）
///      小怪的數值和 AI 跟 Enemy_Grunt 一樣；地下城的骷髏小怪不受影響
///   5. 維京村莊的 VikingEnemyWaveManager 改成生成 Enemy_Paladin
///
/// 可以重複執行。
/// </summary>
public static class PaladinInstaller
{
    const string PackDir = "Assets/_Project/ThirdParty/Lite Sword and Shield Pack";
    const string ModelFile = "Paladin WProp J Nordstrom";
    const string OutDir = "Assets/_Project/Models/Paladin";
    const string ControllerPath = OutDir + "/Paladin_Enemy.controller";
    const string MaterialPath = OutDir + "/M_Paladin.mat";
    const string TextureDir = PackDir + "/Textures";
    const string GruntPath = "Assets/_Project/Prefabs/Enemy_Grunt.prefab";
    const string PaladinPrefabPath = "Assets/_Project/Prefabs/Enemy_Paladin.prefab";
    const string VikingScene = "Assets/_Project/Scenes/The_Viking_Village.unity";
    const string ModelName = "PaladinModel";
    const string OldModelName = "SkeletonModel";

    // 聖騎士的身高 (m)。玩家大約 2m；想調大小就改這個數字再執行一次選單
    const float PaladinHeight = 2.3f;
    const float CapsuleRadius = 0.5f;

    // Animator 狀態 → 動畫檔名（EnemyVisual 用狀態名稱切換）
    static readonly (string state, string file)[] Clips =
    {
        ("Idle", "sword and shield idle"),
        ("Walk", "sword and shield run"),           // 沒有走路動畫：用跑步動畫放慢
        ("Run", "sword and shield run"),
        ("Slash01", "sword and shield attack"),
        ("Slash02", "sword and shield attack (2)"),
        ("Stab", "sword and shield attack (3)"),
        ("Hit", "sword and shield block"),           // 被打中：舉盾格擋
        ("Death", "sword and shield death"),
        ("Scream", "draw sword 1"),                  // 發現玩家：拔劍
        ("Spawn", "draw sword 1"),
        ("Jump", "sword and shield block idle"),
        ("Fall", "sword and shield block idle"),
    };

    static readonly string[] LoopingFiles =
    {
        "sword and shield idle", "sword and shield run", "sword and shield run (2)",
        "sword and shield block idle", "sword and shield strafe", "sword and shield strafe (2)",
    };

    [MenuItem("GeminiGrowUp/安裝聖騎士敵人（維京村莊）")]
    public static void Install()
    {
        string modelPath = $"{PackDir}/{ModelFile}.fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
        {
            EditorUtility.DisplayDialog("找不到聖騎士", $"找不到 {modelPath}", "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(GruntPath) == null)
        {
            EditorUtility.DisplayDialog("找不到小怪", $"找不到 {GruntPath}，聖騎士要用它的 AI 和數值。", "OK");
            return;
        }
        if (!Application.isBatchMode)
        {
            if (!EditorUtility.DisplayDialog("安裝聖騎士敵人",
                    "會做這些事：\n" +
                    "・設定 Lite Sword and Shield Pack 的匯入（Humanoid、循環動畫）\n" +
                    "・建立 Enemy_Paladin.prefab（AI 和數值跟 Enemy_Grunt 一樣）\n" +
                    "・維京村莊的敵人換成聖騎士（地下城的骷髏不變）\n\n" +
                    "建議先 commit。要繼續嗎？", "繼續", "取消"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        }

        BossArenaBuilder.EnsureFolder(OutDir);
        var log = new List<string>();

        ConfigureImports(modelPath, log);
        var material = BuildMaterial(log);
        var controller = BuildController(log);
        var prefab = BuildPrefab(modelPath, controller, material, log);
        if (prefab == null) return;
        UseInVikingVillage(prefab, log);

        AssetDatabase.SaveAssets();
        Debug.Log("[GeminiGrowUp] 聖騎士敵人安裝完成：\n" + string.Join("\n", log));
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("完成", string.Join("\n", log), "OK");
    }

    // ---------------------------------------------------------------- 匯入設定

    static void ConfigureImports(string modelPath, List<string> log)
    {
        // 模型：Humanoid，不需要模型裡的動畫
        var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        if (mi.animationType != ModelImporterAnimationType.Human || mi.importAnimation)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.SaveAndReimport();
        }

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
                c.lockRootRotation = true;      // Root Rotation：Bake Into Pose
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;       // Root Y：Bake Into Pose（腳不會飄）
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;   // 前進的位移不烘進姿勢 → Apply Root Motion 關掉時原地播放
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            n++;
        }
        log.Add($"匯入設定：模型 + {n} 個動畫（Humanoid）");
    }

    // ---------------------------------------------------------------- 材質

    static Material BuildMaterial(List<string> log)
    {
        var diffuse = LoadTexture("Paladin_diffuse", false);
        var normal = LoadTexture("Paladin_normal", true);
        if (diffuse == null)
        {
            log.Add($"⚠ 找不到 {TextureDir}/Paladin_diffuse.png，聖騎士會維持白色");
            return null;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.SetTexture("_BaseMap", diffuse);
        mat.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_Metallic", 0.2f);
        mat.SetFloat("_Smoothness", 0.35f);
        EditorUtility.SetDirty(mat);
        log.Add("材質：M_Paladin（顏色貼圖" + (normal != null ? " + 法線貼圖" : "") + "）");
        return mat;
    }

    static Texture2D LoadTexture(string name, bool isNormal)
    {
        string path = $"{TextureDir}/{name}.png";
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        var wanted = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (imp.textureType != wanted || imp.maxTextureSize != 2048)
        {
            imp.textureType = wanted;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static AnimationClip LoadClip(string file)
    {
        string path = $"{PackDir}/{file}.fbx";
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    static AnimatorController BuildController(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;

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
        if (missing.Count > 0) log.Add("⚠ 找不到這些動畫：" + string.Join("、", missing.Distinct()));
        return controller;
    }

    // ---------------------------------------------------------------- Prefab

    static GameObject BuildPrefab(string modelPath, AnimatorController controller, Material material, List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefabPath) == null)
        {
            if (!AssetDatabase.CopyAsset(GruntPath, PaladinPrefabPath))
            {
                EditorUtility.DisplayDialog("失敗", "複製 Enemy_Grunt 失敗", "OK");
                return null;
            }
        }

        var root = PrefabUtility.LoadPrefabContents(PaladinPrefabPath);
        try
        {
            var cc = root.GetComponent<CharacterController>();
            var health = root.GetComponent<EnemyHealth>();
            var combat = root.GetComponent<EnemyCombat>();
            root.name = "Enemy_Paladin";

            // 拿掉骷髏 / 舊的聖騎士
            foreach (string n in new[] { OldModelName, ModelName })
            {
                var old = root.transform.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var capsule = root.GetComponent<MeshRenderer>();
            if (capsule != null) capsule.enabled = false;
            var eyes = root.transform.Find("Eyes");
            if (eyes != null) eyes.gameObject.SetActive(false);
            if (combat != null && combat.weaponPivot != null)
                foreach (Transform child in combat.weaponPivot) child.gameObject.SetActive(false);

            // 碰撞膠囊配合身高（腳底位置不變）
            float feetLocalY = cc.center.y - cc.height * 0.5f;
            cc.height = PaladinHeight;
            cc.radius = CapsuleRadius;
            cc.center = new Vector3(0f, feetLocalY + PaladinHeight * 0.5f, 0f);
            if (health != null)
            {
                health.barHeightOffset = feetLocalY + PaladinHeight + 0.35f;
                health.flashColor = new Color(1f, 0.3f, 0.25f);
            }

            // 放進聖騎士：量出原始身高後縮放到 PaladinHeight，腳底對齊膠囊底部
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.scene);
            model.name = ModelName;
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            float rawHeight = MeasureHeight(model);
            float scale = rawHeight > 0.1f ? PaladinHeight / rawHeight : 1f;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(0f, feetLocalY, 0f);

            // FBX 裡的材質沒有貼圖（白色），全部換成 M_Paladin（身體、劍、盾共用同一張貼圖）
            if (material != null)
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();

            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var visual = root.GetComponent<EnemyVisual>();
            if (visual == null) visual = root.AddComponent<EnemyVisual>();
            visual.animator = animator;
            // 跑步動畫大約對應 3.5 m/s；用跑步動畫當走路時放慢播
            visual.walkAnimSpeed = 3.5f;
            visual.runAnimSpeed = 3.5f;
            visual.attacks = new[]
            {
                Attack("Slash01", "sword and shield attack"),
                Attack("Slash02", "sword and shield attack (2)"),
                Attack("Stab", "sword and shield attack (3)"),
            };
            var death = LoadClip("sword and shield death");
            if (death != null) visual.deathDuration = death.length;

            PrefabUtility.SaveAsPrefabAsset(root, PaladinPrefabPath);
            log.Add($"Enemy_Paladin.prefab：聖騎士約 {PaladinHeight:0.0}m 高（模型縮放 {scale:0.00}）");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefabPath);
    }

    /// <summary>攻擊動畫：揮下去的那一刻大約在動畫的 45%</summary>
    static EnemyVisual.AttackAnim Attack(string state, string file)
    {
        var clip = LoadClip(file);
        return new EnemyVisual.AttackAnim { state = state, hitTime = clip != null ? clip.length * 0.45f : 0.4f };
    }

    /// <summary>
    /// 量模型身高：優先用 Humanoid 骨架（腳底到頭頂，不會被手上的劍盾影響），
    /// 量不到才用所有 Renderer 的外框
    /// </summary>
    static float MeasureHeight(GameObject model)
    {
        var anim = model.GetComponentInChildren<Animator>(true);
        if (anim != null && anim.avatar != null && anim.avatar.isHuman)
        {
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
            {
                float headY = head.position.y - model.transform.position.y;
                if (headY > 0.1f) return headY * 1.12f; // 頭骨在頭的中下方，往上補到頭頂
            }
        }

        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return 0f;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b.size.y;
    }

    // ---------------------------------------------------------------- 場景

    static void UseInVikingVillage(GameObject prefab, List<string> log)
    {
        var scene = EditorSceneManager.OpenScene(VikingScene, OpenSceneMode.Single);
        var managers = Object.FindObjectsByType<VikingEnemyWaveManager>(FindObjectsInactive.Include);
        foreach (var m in managers)
        {
            m.enemyPrefab = prefab;
            EditorUtility.SetDirty(m);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add(managers.Length > 0
            ? $"維京村莊：{managers.Length} 個波次管理器改成生成聖騎士"
            : "⚠ 維京村莊裡找不到 VikingEnemyWaveManager");
    }
}
#endif
