#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 貓戰士的新動畫：跳起（CW_Jump）、下落循環（CW_Fall）、落地緩衝（CW_Land）、攀上平台（CW_Climb）。
///
/// 動畫放在 Models/PlayerCharacter/PlayerCatWarrior_AirClimb.fbx（Art/PlayerCatWarrior/player_anim_generator.py 產生）。
///  1. 匯入設定是自動的（下面的 AssetPostprocessor）：Humanoid、跟原本的貓戰士用同一套骨架對應，
///     根節點的移動 / 旋轉全部烘進姿勢（Based Upon Original），CW_Fall 循環。
///  2. 選單「GeminiGrowUp > 安裝 玩家跳躍・下落・攀爬動畫」把它們接到 AC_PlayerCatWarrior：
///     Jump / Fall 換成新動畫、加上 Land（落地）和 Climb（攀爬），並把轉換線調順：
///       走 / 跑 / 待機 → Jump（起跳）→ 接近最高點就慢慢轉到 Fall → 著地 → Land（依落下速度決定緩衝多久）→ 待機 / 走 / 跑
///       Climb 由 CalligraphyPlatformClimb 用程式控制播放進度。
/// 可以重複執行（會先拿掉這些狀態舊的轉換線再重接）。
/// </summary>
public class PlayerAirClimbAnimInstaller : AssetPostprocessor
{
    public const string FbxPath = "Assets/_Project/Models/PlayerCharacter/PlayerCatWarrior_AirClimb.fbx";
    const string SourceModel = "Assets/_Project/Models/PlayerCharacter/PlayerCatWarrior_Animated/tripo_convert_b6817ade-899b-4f50-9c7e-c61b36b6634c.fbx";
    const string ControllerPath = "Assets/_Project/Animations/Player/AC_PlayerCatWarrior.controller";
    /// <summary>importer.userData 設成這個 = 不要複製原本模型的骨架設定，讓 Unity 自動對應（萬一複製的 Avatar 無效時用）</summary>
    const string AutoAvatarFlag = "auto-avatar";

    // ------------------------------------------------------------------ 匯入設定

    void OnPreprocessModel()
    {
        if (!IsOurs(assetPath)) return;
        var imp = (ModelImporter)assetImporter;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = true;
        imp.resampleCurves = true;
        imp.animationCompression = ModelImporterAnimationCompression.Off; // 關鍵格很少，不壓縮才不會跑掉
        if (imp.userData == AutoAvatarFlag)
        {
            imp.autoGenerateAvatarMappingIfUnspecified = true;
            return;
        }
        // 跟原本的貓戰士用完全一樣的骨架對應和 T-Pose：動畫才會一模一樣地套到玩家身上
        var src = AssetImporter.GetAtPath(SourceModel) as ModelImporter;
        if (src == null) { imp.autoGenerateAvatarMappingIfUnspecified = true; return; }
        // 這個 FBX 的階層跟原本一樣：模型 → Armature → Hips…，另外有一個同名的空物件 tripo_node_b6817ade 佔位
        // （只有 Armature 一個根節點的話，Unity 會把它併成模型本身，Hips 的上一層就對不上了）
        var hd = src.humanDescription;
        // 骨架的 T-Pose（每根骨頭的位置 / 旋轉）照抄原本的貓戰士，但重新建立：
        // 原本的資料裡記著「上一層是誰」（最上層是原本模型的名字），直接沿用的話 Unity 會說階層對不上。
        // 重新建立的不帶這個資訊，Unity 就照這個 FBX 自己的階層找骨頭（名字一樣就對得上）
        var skeleton = hd.skeleton.Select((b, i) => new SkeletonBone
        {
            name = i == 0 ? Path.GetFileNameWithoutExtension(assetPath) : b.name,
            position = b.position,
            rotation = b.rotation,
            scale = b.scale,
        }).ToArray();
        hd.skeleton = skeleton;
        imp.humanDescription = hd;
    }

    void OnPreprocessAnimation()
    {
        if (!IsOurs(assetPath)) return;
        var imp = (ModelImporter)assetImporter;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            string n = c.takeName.Split('|').Last();
            c.name = n;
            // 根節點不要自己動：整個身體的位置 / 方向都烘進姿勢，以檔案原本的原點為準（玩家的位置由程式控制）
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
            c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            c.loopTime = n == "CW_Fall";
            c.loopPose = false;
        }
        imp.clipAnimations = clips;
    }

    static bool IsOurs(string path) => path.Replace('\\', '/') == FbxPath;

    // ------------------------------------------------------------------ 選單

    [MenuItem("GeminiGrowUp/安裝 玩家跳躍・下落・攀爬動畫")]
    public static void Install()
    {
        var log = new List<string>();
        // 上一次失敗時可能被切成「自動對應骨頭」：每次都先用「跟原本的貓戰士完全一樣」的設定試
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp != null && imp.userData == AutoAvatarFlag) imp.userData = "";
        if (!Run(log, true))
        {
            EditorUtility.DisplayDialog("安裝失敗", string.Join("\n", log), "OK");
            return;
        }
        EditorUtility.DisplayDialog("完成", string.Join("\n", log) + "\n\n按 Play 跳一下、從高處跳下來、在書法 Boss 的浮石上按住 W 攀爬試試看。", "OK");
    }

    internal static bool Run(List<string> log, bool allowRetry)
    {
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null) { log.Add($"找不到 {FbxPath}"); return false; }
        imp.SaveAndReimport(); // 確保用的是上面的匯入設定

        var avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            if (allowRetry && imp.userData != AutoAvatarFlag)
            {
                // 複製原本的骨架設定失敗 → 改成讓 Unity 自己對應骨頭再試一次
                imp.userData = AutoAvatarFlag;
                imp.SaveAndReimport();
                log.Add("⚠ 複製原本模型的 Avatar 設定失敗，改用自動對應骨頭。");
                return Run(log, false);
            }
            log.Add("⚠ 新動畫的 Humanoid Avatar 無效（Inspector 選這個 FBX → Rig 看錯誤訊息）。");
            return false;
        }
        log.Add("動畫 FBX：Humanoid Avatar 正常");

        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
        foreach (var need in new[] { "CW_Jump", "CW_Fall", "CW_Land", "CW_Climb" })
            if (!clips.ContainsKey(need)) { log.Add($"FBX 裡沒有 {need} 動畫"); return false; }

        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) { log.Add($"找不到 {ControllerPath}"); return false; }
        Wire(ctrl, clips, log);
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return true;
    }

    static void Wire(AnimatorController ctrl, Dictionary<string, AnimationClip> clips, List<string> log)
    {
        EnsureFloat(ctrl, "LandRate", 1f);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState Find(string n) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);

        var idle = Find("Idle"); var walk = Find("Walk"); var run = Find("Run");
        var jump = Find("Jump") ?? sm.AddState("Jump", new Vector3(600, 0));
        var fall = Find("Fall") ?? sm.AddState("Fall", new Vector3(600, 100));
        var land = Find("Land") ?? sm.AddState("Land", new Vector3(600, 200));
        var climb = Find("Climb") ?? sm.AddState("Climb", new Vector3(600, 300));

        jump.motion = clips["CW_Jump"]; jump.speed = 1f;
        fall.motion = clips["CW_Fall"]; fall.speed = 1f;
        land.motion = clips["CW_Land"]; land.speed = 1f;
        land.speedParameterActive = true; land.speedParameter = "LandRate";
        climb.motion = clips["CW_Climb"]; climb.speed = 1f;
        foreach (var s in new[] { jump, fall, land, climb }) s.writeDefaultValues = true;

        Clear(jump); Clear(fall); Clear(land); Clear(climb);

        // 起跳 → 下落：還在往上（速度 < 1.5）就開始慢慢轉，經過最高點時已經是下落的姿勢
        var t = Add(jump, fall, 0.28f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        t.AddCondition(AnimatorConditionMode.Less, 1.5f, "VerticalSpeed");
        Add(jump, land, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        Add(fall, land, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        // 落地 → 馬上又起跳 / 又掉下去
        t = Add(land, jump, 0.08f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        t.AddCondition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed");
        t = Add(land, fall, 0.15f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        t.AddCondition(AnimatorConditionMode.Less, -3.5f, "VerticalSpeed");
        // 落地 → 繼續跑 / 走（緩衝做到一半就接上，不會卡住）/ 站好
        if (run != null) { t = Add(land, run, 0.18f, 0.3f); t.AddCondition(AnimatorConditionMode.Greater, 0.72f, "Speed"); }
        if (walk != null) { t = Add(land, walk, 0.2f, 0.4f); t.AddCondition(AnimatorConditionMode.Greater, 0.06f, "Speed"); }
        if (idle != null) Add(land, idle, 0.2f, 0.85f);
        // 攀爬：進度由程式控制，萬一程式沒接手就自己播完回待機
        if (idle != null) Add(climb, idle, 0.15f, 0.98f);

        // 待機 / 走 / 跑 → 起跳 / 下落：轉換時間拉長一點；
        // 往下掉要真的有在掉（速度 < -3.5）才切，走下小台階、斜坡時不會一直閃成下落的姿勢（站在地上時速度是 -2）
        foreach (var s in new[] { idle, walk, run })
        {
            if (s == null) continue;
            foreach (var tr in s.transitions)
            {
                if (tr.destinationState == jump) tr.duration = 0.1f;
                if (tr.destinationState == fall)
                {
                    tr.duration = 0.15f;
                    var conds = tr.conditions;
                    for (int i = 0; i < conds.Length; i++)
                        if (conds[i].parameter == "VerticalSpeed") conds[i].threshold = -3.5f;
                    tr.conditions = conds;
                }
            }
        }
        log.Add("AC_PlayerCatWarrior：Jump / Fall 換成新動畫，加上 Land、Climb，轉換線已調整");
    }

    static void Clear(AnimatorState s)
    {
        foreach (var tr in s.transitions.ToArray()) s.RemoveTransition(tr);
    }

    static AnimatorStateTransition Add(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = exitTime >= 0f;
        if (exitTime >= 0f) t.exitTime = exitTime;
        t.duration = duration;
        t.hasFixedDuration = true;
        t.interruptionSource = TransitionInterruptionSource.None;
        return t;
    }

    static void EnsureFloat(AnimatorController ctrl, string name, float value)
    {
        var ps = ctrl.parameters;
        int i = System.Array.FindIndex(ps, p => p.name == name);
        if (i < 0) { ctrl.AddParameter(name, AnimatorControllerParameterType.Float); ps = ctrl.parameters; i = ps.Length - 1; }
        ps[i].defaultFloat = value;
        ctrl.parameters = ps;
    }
}
#endif
