#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 選單：VFX/Characters/Skeleton Centipede
///
/// 百足駭骨（Tripo 自動綁骨的模型，90 根骨頭、沒有任何動畫）的動作是用程式「擺姿勢 → 每格取樣」烘成一般的 .anim：
///   1. 模型匯入設定改成 Generic + 從模型建立 Avatar
///   2. 依骨頭位置自動找出身體節、左右腳、上半身、鐮刀前肢、大顎（不用管 Tripo 的骨頭軸向）
///   3. 產生 Idle、Crawl、Slash、SweepCombo、TailWhipLeft/Right、Dash、Ranged、Pounce、Roar、Bite、Hit、Death 十三個片段（Animations/SkeletonCentipede/）
///      Slash / Bite 在命中格放 OnAttackImpact 事件，Death 結尾放 OnDeathFinished 事件
///   4. 建立 AC_SkeletonCentipede.controller 和 Prefabs/Boss/PF_SkeletonCentipede_Animated.prefab
///      （有匯入 Magic Effects FREE 的話，順便掛上鐮刀刀光 / 命中特效，見 SkeletonCentipedeVfxInstaller）
///
/// 所有旋轉都用「角色空間」描述（前 F、上 U、右 R），所以想調整動作只要改下面各個 Pose 函式裡的角度。
/// 可以重複執行，會覆蓋舊的片段。
/// </summary>
public static class SkeletonCentipedeAnimationBaker
{
    const string ModelPath = "Assets/_Project/Models/Boss/Skeleton_Centipede/tripo_convert_1f1476cb-f1cd-4e9e-a1f7-c7a78e34cddd.fbx";
    const string OutDir = "Assets/_Project/Animations/SkeletonCentipede";
    const string ControllerPath = OutDir + "/AC_SkeletonCentipede.controller";
    const string PrefabPath = "Assets/_Project/Prefabs/Boss/PF_SkeletonCentipede_Animated.prefab";
    const string PreviewScenePath = "Assets/_Project/Scenes/SkeletonCentipedeAnimPreview.unity";
    const float SampleRate = 30f;
    const float PreviewBodyLength = 7f;

    delegate void PoseFn(Rig rig, float t);

    class ClipDef
    {
        public string name;
        public float length;
        public bool loop;
        public PoseFn pose;
        public (float time, string function, string arg)[] events = Array.Empty<(float, string, string)>();
    }

    static readonly ClipDef[] Clips =
    {
        new ClipDef { name = "Idle", length = 3f, loop = true, pose = PoseIdle },
        new ClipDef { name = "Crawl", length = 1f, loop = true, pose = PoseCrawl },
        new ClipDef { name = "Slash", length = 1.4f, pose = PoseSlash, events = new[] { (0.45f, "OnSweepSwing", "Right"), (0.56f, "OnAttackImpact", "Slash") } },
        new ClipDef
        {
            name = "SweepCombo", length = 3f, pose = PoseSweepCombo,
            events = new[]
            {
                (0.35f, "OnSweepSwing", "Right"), (0.45f, "OnAttackImpact", "Sweep1"),
                (0.75f, "OnSweepSwing", "Left"), (0.85f, "OnAttackImpact", "Sweep2"),
                (1.2f, "OnSweepSwing", "Right"), (1.3f, "OnAttackImpact", "Sweep3"),
                (1.75f, "OnSweepSwing", "Both"), (1.88f, "OnAttackImpact", "Sweep4"),
            },
        },
        new ClipDef
        {
            name = "TailWhipLeft", length = 1.8f, pose = (r, t) => PoseTailWhip(r, t, 1f),
            events = new[] { (0.5f, "OnTailSwing", "Left"), (0.74f, "OnAttackImpact", "TailWhip") },
        },
        new ClipDef
        {
            name = "TailWhipRight", length = 1.8f, pose = (r, t) => PoseTailWhip(r, t, -1f),
            events = new[] { (0.5f, "OnTailSwing", "Right"), (0.74f, "OnAttackImpact", "TailWhip") },
        },
        new ClipDef
        {
            name = "Dash", length = 2f, pose = PoseDash,
            events = new[]
            {
                (0.12f, "OnDashCharge", ""), (0.5f, "OnDashStart", ""),
                (1.28f, "OnDashEnd", ""), (1.3f, "OnSweepSwing", "Both"), (1.4f, "OnAttackImpact", "Dash"),
            },
        },
        new ClipDef
        {
            name = "Ranged", length = 1.6f, pose = PoseRanged,
            events = new[] { (0.08f, "OnRangedCharge", ""), (0.66f, "OnAttackImpact", "Ranged") },
        },
        new ClipDef
        {
            name = "Pounce", length = 1.3f, pose = PosePounce,
            events = new[] { (0.25f, "OnPounceLeap", ""), (0.55f, "OnAttackImpact", "Pounce") },
        },
        new ClipDef
        {
            name = "Roar", length = 2.2f, pose = PoseRoar,
            events = new[] { (0.55f, "OnRoar", "") },
        },
        new ClipDef { name = "Bite", length = 1.25f, pose = PoseBite, events = new[] { (0.52f, "OnAttackImpact", "Bite") } },
        new ClipDef { name = "Hit", length = 0.6f, pose = PoseHit },
        new ClipDef { name = "Death", length = 2.6f, pose = PoseDeath, events = new[] { (2.55f, "OnDeathFinished", "") } },
    };

    // ───────────────────────── 選單 ─────────────────────────

    [MenuItem("VFX/Characters/Skeleton Centipede/Bake Animations")]
    public static void BakeAll()
    {
        ConfigureModelImport();
        EnsureFolder(OutDir);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new FileNotFoundException("找不到百足駭骨模型", ModelPath);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        var clips = new Dictionary<string, AnimationClip>();
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var rig = new Rig(instance.transform);
            Debug.Log(rig.Describe());

            foreach (var def in Clips)
                clips[def.name] = BakeClip(rig, def);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }

        var controller = BuildController(clips);
        BuildPrefab(model, controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"百足駭骨：已產生 {clips.Count} 個動畫片段、{ControllerPath}、{PrefabPath}");
    }

    [MenuItem("VFX/Characters/Skeleton Centipede/Build Preview Scene")]
    public static void BuildPreviewScene()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) BakeAll();
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f);

        var boss = SpawnPreview(scene, Vector3.zero, Quaternion.identity, 0f);

        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(PreviewBodyLength * 0.75f, PreviewBodyLength * 0.45f, PreviewBodyLength * 0.95f);
            cam.transform.LookAt(new Vector3(0f, PreviewBodyLength * 0.2f, 0f));
        }

        EditorSceneManager.SaveScene(scene, PreviewScenePath);
        Selection.activeGameObject = boss;
        Debug.Log($"百足駭骨預覽場景：{PreviewScenePath}（按 Play，用數字鍵 5~0 或左側按鈕切換動作）");
    }

    const string VoidRendScenePath = "Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity";
    const string VoidRendInstanceName = "SkeletonCentipede_AnimTest";
    static readonly Vector3 VoidRendSpot = new Vector3(-10f, 0f, -4f);

    /// <summary>
    /// 放一隻百足駭骨到 VFX_VoidRend_Bocchi 測試場（玩家出生點左前方、面向玩家），只掛預覽元件，不接戰鬥。
    /// 重複執行會先刪掉舊的那隻再放新的。
    /// </summary>
    [MenuItem("VFX/Characters/Skeleton Centipede/Install Into Void Rend Test Scene")]
    public static void InstallIntoVoidRendScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("請先停止 Play 再安裝。");
        BakeAll();

        var scene = SceneManager.GetSceneByPath(VoidRendScenePath);
        if (!scene.isLoaded)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(VoidRendScenePath, OpenSceneMode.Single);
        }

        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == VoidRendInstanceName).ToArray())
            UnityEngine.Object.DestroyImmediate(old);

        // 找地面高度：從上往下打射線，打不到就用 y = 0
        Physics.SyncTransforms();
        Vector3 spot = VoidRendSpot;
        float groundY = Physics.Raycast(spot + Vector3.up * 50f, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y : 0f;

        var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "PlayerRig");
        Vector3 lookAt = player != null ? player.transform.position : Vector3.zero;
        Vector3 face = lookAt - spot;
        face.y = 0f;
        var rotation = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;

        var boss = SpawnPreview(scene, spot, rotation, groundY);
        boss.name = VoidRendInstanceName;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = boss;
        EditorGUIUtility.PingObject(boss);
        Debug.Log($"百足駭骨已放進 {VoidRendScenePath}（{boss.transform.position}）。按 Play，用數字鍵 5~0 切換動作。");
    }

    /// <summary>放一隻縮放到 PreviewBodyLength 身長、腳貼地、掛預覽元件的百足駭骨。</summary>
    static GameObject SpawnPreview(Scene scene, Vector3 position, Quaternion rotation, float groundY)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        boss.name = "SkeletonCentipede_Preview";
        boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var bounds = RenderBounds(boss);
        float length = Mathf.Max(bounds.size.x, bounds.size.z);
        float scale = length > 0.0001f ? PreviewBodyLength / length : 1f;
        boss.transform.localScale = Vector3.one * scale;
        bounds = RenderBounds(boss);
        float lift = groundY - bounds.min.y;

        // 模型本身的「前方」不一定是 +Z，用尾巴 → 頭的方向修正，讓 rotation 指的就是頭朝的方向
        var bones = boss.GetComponentsInChildren<Transform>(true);
        var head = bones.FirstOrDefault(t => t.name.EndsWith("Head_0"));
        var tailEnd = bones.FirstOrDefault(t => t.name == "bone_7");
        Vector3 modelFwd = head != null && tailEnd != null ? head.position - tailEnd.position : Vector3.forward;
        modelFwd.y = 0f;
        var facingFix = modelFwd.sqrMagnitude > 0.0001f ? Quaternion.Inverse(Quaternion.LookRotation(modelFwd)) : Quaternion.identity;

        boss.transform.SetPositionAndRotation(new Vector3(position.x, lift, position.z), rotation * facingFix);
        boss.AddComponent<SkeletonCentipedeAnimationPreview>();
        return boss;
    }

    // ───────────────────────── 骨架對應 ─────────────────────────

    class Leg
    {
        public Transform hip, knee, foot;
        public float side;      // +1 右、-1 左
        public float along01;   // 0 = 最前面的腳、1 = 最後面
    }

    class Rig
    {
        public readonly Transform root;
        public readonly Transform hips;
        public readonly Transform[] body;
        public readonly Transform[] tail;
        public readonly Transform[] torso;
        public readonly Transform neck, head, underJaw;
        public readonly List<(Transform bone, float side)> fangs = new();
        public readonly List<Leg> legs = new();
        public readonly Transform rightArm, rightBlade, leftArm, leftBlade;
        public readonly Vector3 F, U, R;
        public readonly float size;
        public readonly Transform[] all;

        readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> bindLocal = new();
        readonly Dictionary<Transform, Quaternion> bindWorld = new();
        readonly Vector3 hipsBindPos;

        public Rig(Transform root)
        {
            this.root = root;
            all = root.GetComponentsInChildren<Transform>(true).Where(t => t != root).ToArray();

            hips = Find("Root");
            body = Enumerable.Range(1, 7).Select(i => Find($"bone_{i}")).ToArray();
            tail = new[] { Find("bone_8"), Find("bone_9"), Find("bone_11") };
            torso = new[] { Find("bone_66"), Find("bone_71"), Find("Spine_0"), Find("Spine_1"), Find("bone_78") };
            neck = Find("bone_79");
            head = Find("Head_0");
            underJaw = Find("bone_83");

            foreach (var t in all)
            {
                bindLocal[t] = (t.localPosition, t.localRotation);
                bindWorld[t] = t.rotation;
            }
            hipsBindPos = hips.localPosition;

            // 角色空間：尾巴 → 頭 為前方
            Vector3 fwd = head.position - body[^1].position;
            fwd.y = 0f;
            F = fwd.normalized;
            U = Vector3.up;
            R = Vector3.Cross(U, F).normalized;
            size = Vector3.Distance(head.position, body[^1].position);

            foreach (var name in new[] { "bone_81", "bone_82" })
            {
                var f = Find(name);
                fangs.Add((f, Side(f.position - head.position)));
            }

            var a = Find("bone_85");
            var b = Find("bone_87");
            bool aIsRight = Side(a.GetChild(0).position - neck.position) > 0f;
            rightArm = aIsRight ? a : b;
            leftArm = aIsRight ? b : a;
            rightBlade = rightArm.childCount > 0 ? rightArm.GetChild(0) : null;
            leftBlade = leftArm.childCount > 0 ? leftArm.GetChild(0) : null;

            // 腳：每隻是「髖 → 膝 → 腳尖」三根一串
            int[] hipIds = { 51, 54, 57, 60, 42, 45, 48, 36, 39, 30, 33, 24, 27, 18, 21, 13, 16 };
            var raw = new List<(Leg leg, float along)>();
            foreach (int id in hipIds)
            {
                var hip = Find($"bone_{id}");
                var knee = hip.childCount > 0 ? hip.GetChild(0) : null;
                var foot = knee != null && knee.childCount > 0 ? knee.GetChild(0) : null;
                var tip = foot != null ? foot : knee != null ? knee : hip;
                raw.Add((new Leg { hip = hip, knee = knee, foot = foot, side = Side(tip.position - hip.position) },
                    Vector3.Dot(hip.position, F)));
            }
            float maxA = raw.Max(r => r.along), minA = raw.Min(r => r.along);
            foreach (var (leg, along) in raw)
            {
                leg.along01 = Mathf.InverseLerp(maxA, minA, along);
                legs.Add(leg);
            }
        }

        float Side(Vector3 v) => Vector3.Dot(v, R) >= 0f ? 1f : -1f;

        Transform Find(string boneName)
        {
            // Tripo 的骨頭可能帶 "tripo::" 前綴，匯入後名稱可能保留或不保留
            var t = all.FirstOrDefault(x => x.name == boneName || x.name.EndsWith(":" + boneName));
            if (t == null) throw new InvalidOperationException($"百足駭骨骨架找不到 {boneName}");
            return t;
        }

        public void ResetToBind()
        {
            foreach (var kv in bindLocal)
            {
                kv.Key.localPosition = kv.Value.pos;
                kv.Key.localRotation = kv.Value.rot;
            }
        }

        /// <summary>繞角色空間的軸轉 deg 度，軸會跟著父骨頭已經轉過的角度走。</summary>
        public void Rot(Transform t, Vector3 axis, float deg)
        {
            if (t == null || Mathf.Abs(deg) < 0.0001f) return;
            var parent = t.parent;
            Quaternion parentDelta = parent != null && bindWorld.TryGetValue(parent, out var pb)
                ? parent.rotation * Quaternion.Inverse(pb)
                : Quaternion.identity;
            t.rotation = Quaternion.AngleAxis(deg, parentDelta * axis) * t.rotation;
        }

        /// <summary>整隻身體位移（以身長為單位，角色空間）。</summary>
        public void MoveHips(float forward, float up, float right = 0f)
        {
            Vector3 world = (F * forward + U * up + R * right) * size;
            var parent = hips.parent;
            Vector3 local = parent != null ? parent.InverseTransformVector(world) : world;
            hips.localPosition = hipsBindPos + local;
        }

        public string Describe() =>
            $"百足駭骨骨架：{all.Length} 個節點、身長 {size:0.###}、腳 {legs.Count} 隻（右 {legs.Count(l => l.side > 0)} / 左 {legs.Count(l => l.side < 0)}）、" +
            $"右鐮刀 {rightArm.name}、左鐮刀 {leftArm.name}、F={F}";
    }

    // ───────────────────────── 動作 ─────────────────────────
    // 角度方向（Unity 左手座標）：
    //   繞 U 正轉 → 往右轉；腳往前擺 = -side × 角度
    //   繞 F 正轉 → 右側往上；腳抬起 = side × 角度
    //   繞 R 正轉 → 往前低頭；後仰 = 負角度

    const float TwoPi = Mathf.PI * 2f;

    static void PoseIdle(Rig r, float t)
    {
        float w = TwoPi / 3f * t;
        r.MoveHips(0f, 0.008f * Mathf.Sin(2f * w));
        r.Rot(r.hips, r.U, 1.5f * Mathf.Sin(w));

        for (int i = 0; i < r.body.Length; i++)
            r.Rot(r.body[i], r.U, 1.6f * Mathf.Sin(w - i * 0.6f));
        foreach (var tl in r.tail) r.Rot(tl, r.U, 7f * Mathf.Sin(w - 4.5f));

        // 呼吸：上半身前後起伏，頭左右張望
        foreach (var b in r.torso) r.Rot(b, r.R, -1.6f * Mathf.Sin(2f * w));
        r.Rot(r.neck, r.U, 9f * Mathf.Sin(w + 0.4f));
        r.Rot(r.head, r.R, 4f * Mathf.Sin(2f * w + 1f));

        // 鐮刀舉在身側緩慢晃動
        r.Rot(r.rightArm, r.F, 8f + 5f * Mathf.Sin(w + 0.5f));
        r.Rot(r.leftArm, r.F, -(8f + 5f * Mathf.Sin(w + 1.6f)));
        r.Rot(r.rightBlade, r.U, -6f * Mathf.Sin(2f * w));
        r.Rot(r.leftBlade, r.U, 6f * Mathf.Sin(2f * w + 0.7f));

        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * 6f * (0.5f + 0.5f * Mathf.Sin(2f * w)));
        r.Rot(r.underJaw, r.R, 4f * Mathf.Sin(2f * w));

        // 腳偶爾挪一下
        foreach (var leg in r.legs)
        {
            float twitch = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(w + leg.along01 * 9f + (leg.side > 0 ? 0f : 2f))), 6f);
            r.Rot(leg.hip, r.F, leg.side * 6f * twitch);
            r.Rot(leg.knee, r.F, leg.side * 4f * twitch);
        }
    }

    static void PoseCrawl(Rig r, float t)
    {
        float w = TwoPi * t; // 1 秒一個循環
        r.MoveHips(0f, 0.012f * Mathf.Abs(Mathf.Sin(w)));
        r.Rot(r.hips, r.U, 4f * Mathf.Sin(w + 0.9f));

        // 身體蛇行波：從頭往尾巴傳
        for (int i = 0; i < r.body.Length; i++)
            r.Rot(r.body[i], r.U, 6.5f * Mathf.Sin(w - (i + 1) * 0.9f));
        foreach (var tl in r.tail) r.Rot(tl, r.U, 12f * Mathf.Sin(w - 7.5f));

        // 上半身抵銷擺動，保持盯著前方
        r.Rot(r.torso[0], r.U, -4f * Mathf.Sin(w + 0.9f));
        r.Rot(r.torso[0], r.R, 6f);
        r.Rot(r.head, r.R, 3f * Mathf.Sin(2f * w));

        r.Rot(r.rightArm, r.F, 14f + 5f * Mathf.Sin(w));
        r.Rot(r.leftArm, r.F, -(14f + 5f * Mathf.Sin(w + Mathf.PI)));
        r.Rot(r.rightArm, r.U, 8f * Mathf.Sin(w));
        r.Rot(r.leftArm, r.U, 8f * Mathf.Sin(w));

        // 腳：波浪式步伐，左右差半拍
        foreach (var leg in r.legs)
        {
            float phase = w - leg.along01 * TwoPi + (leg.side > 0 ? 0f : Mathf.PI);
            float lift = Mathf.Max(0f, Mathf.Sin(phase));
            float swing = -Mathf.Cos(phase); // -1 在後、+1 在前
            r.Rot(leg.hip, r.U, -leg.side * 16f * swing);
            r.Rot(leg.hip, r.F, leg.side * 20f * lift);
            r.Rot(leg.knee, r.F, leg.side * 14f * lift);
            r.Rot(leg.foot, r.F, -leg.side * 10f * lift);
        }
    }

    static void PoseSlash(Rig r, float t)
    {
        // 0~0.45 蓄力（舉刀、上身後仰轉向右）、0.45~0.62 揮下、0.62~0.9 停留、0.9~1.4 收招
        float lift = Keys(t, (0f, 0f), (0.45f, 62f), (0.62f, -28f), (0.9f, -22f), (1.4f, 0f));
        float yaw = Keys(t, (0f, 0f), (0.45f, 38f), (0.62f, -78f), (0.9f, -70f), (1.4f, 0f));
        float blade = Keys(t, (0f, 0f), (0.45f, 25f), (0.62f, -30f), (0.9f, -20f), (1.4f, 0f));
        float twist = Keys(t, (0f, 0f), (0.45f, 16f), (0.62f, -24f), (0.9f, -20f), (1.4f, 0f));
        float pitch = Keys(t, (0f, 0f), (0.45f, -10f), (0.62f, 16f), (0.9f, 13f), (1.4f, 0f));
        float lunge = Keys(t, (0f, 0f), (0.45f, -0.03f), (0.62f, 0.07f), (0.9f, 0.06f), (1.4f, 0f));
        float shake = t > 0.6f && t < 0.8f ? Mathf.Sin(t * 90f) * (0.8f - t) * 12f : 0f;

        r.MoveHips(lunge, 0f);
        r.Rot(r.torso[0], r.U, twist * 0.5f);
        r.Rot(r.torso[2], r.U, twist * 0.5f);
        foreach (var b in r.torso) r.Rot(b, r.R, pitch / r.torso.Length);
        r.Rot(r.neck, r.U, -twist * 0.4f + shake * 0.3f);

        r.Rot(r.rightArm, r.F, lift);
        r.Rot(r.rightArm, r.U, yaw);
        r.Rot(r.rightBlade, r.F, blade);

        // 左鐮刀護在身前、跟著身體甩
        r.Rot(r.leftArm, r.F, -Keys(t, (0f, 0f), (0.45f, 18f), (0.62f, 30f), (1.4f, 0f)));
        r.Rot(r.leftArm, r.U, Keys(t, (0f, 0f), (0.45f, -15f), (0.62f, 10f), (1.4f, 0f)));

        float brace = Keys(t, (0f, 0f), (0.45f, 1f), (0.62f, 0.3f), (1.4f, 0f));
        foreach (var leg in r.legs.Where(l => l.along01 < 0.35f))
            r.Rot(leg.hip, r.F, -leg.side * 10f * brace);
        foreach (var tl in r.tail) r.Rot(tl, r.U, -twist * 0.6f);
        for (int i = 0; i < r.body.Length; i++) r.Rot(r.body[i], r.U, -twist * 0.12f);
    }

    /// <summary>
    /// 連續大範圍揮砍：右掃 → 左掃 → 右掃 → 雙鐮高舉往前下方交叉重砍。
    /// 右鐮刀用 (lift, yaw)，左鐮刀用鏡像的 (-lift, -yaw)；lift 正值舉高、yaw 正值往後拉。
    /// </summary>
    static void PoseSweepCombo(Rig r, float t)
    {
        float rLift = Keys(t, (0f, 0f), (0.35f, 58f), (0.5f, -18f), (0.75f, 8f), (0.9f, 10f), (1.2f, 58f), (1.35f, -18f),
            (1.5f, 20f), (1.75f, 78f), (1.95f, -32f), (2.3f, -26f), (3f, 0f));
        float rYaw = Keys(t, (0f, 0f), (0.35f, 42f), (0.5f, -90f), (0.75f, -40f), (0.9f, -20f), (1.2f, 42f), (1.35f, -90f),
            (1.5f, -30f), (1.75f, 12f), (1.95f, -62f), (2.3f, -58f), (3f, 0f));
        float lLift = Keys(t, (0f, 0f), (0.35f, 15f), (0.5f, 25f), (0.75f, 58f), (0.9f, -18f), (1.2f, 8f), (1.35f, 15f),
            (1.5f, 20f), (1.75f, 78f), (1.95f, -32f), (2.3f, -26f), (3f, 0f));
        float lYaw = Keys(t, (0f, 0f), (0.35f, -15f), (0.5f, 10f), (0.75f, 42f), (0.9f, -90f), (1.2f, -40f), (1.35f, -20f),
            (1.5f, -30f), (1.75f, 12f), (1.95f, -62f), (2.3f, -58f), (3f, 0f));
        // 上身扭轉：正值轉向右（右刀蓄力時轉右、揮出時甩向左）
        float twist = Keys(t, (0f, 0f), (0.35f, 18f), (0.5f, -22f), (0.75f, -18f), (0.9f, 22f), (1.2f, 18f), (1.35f, -22f),
            (1.75f, 0f), (2.3f, 0f), (3f, 0f));
        float pitch = Keys(t, (0f, 0f), (0.35f, -6f), (0.5f, 10f), (0.75f, -6f), (0.9f, 10f), (1.2f, -6f), (1.35f, 10f),
            (1.75f, -20f), (1.95f, 28f), (2.3f, 24f), (3f, 0f));
        float lunge = Keys(t, (0f, 0f), (0.5f, 0.03f), (0.9f, 0.06f), (1.35f, 0.09f), (1.75f, 0.06f), (1.95f, 0.16f), (2.3f, 0.15f), (3f, 0f));
        float drop = Keys(t, (0f, 0f), (1.75f, 0.02f), (1.95f, -0.03f), (2.3f, -0.025f), (3f, 0f));
        float blade = Keys(t, (0f, 0f), (0.35f, 20f), (0.5f, -25f), (0.75f, 0f), (1.2f, 20f), (1.35f, -25f), (1.75f, 30f), (1.95f, -35f), (3f, 0f));
        float lBlade = Keys(t, (0f, 0f), (0.75f, 20f), (0.9f, -25f), (1.2f, 0f), (1.75f, 30f), (1.95f, -35f), (3f, 0f));

        // 每次揮到底的瞬間加一點震動
        float shake = 0f;
        foreach (float hitT in new[] { 0.48f, 0.88f, 1.33f, 1.92f })
            if (t > hitT && t < hitT + 0.2f) shake += Mathf.Sin((t - hitT) * 80f) * (hitT + 0.2f - t) * 30f;

        r.MoveHips(lunge, drop);
        r.Rot(r.torso[0], r.U, twist * 0.5f);
        r.Rot(r.torso[2], r.U, twist * 0.5f);
        foreach (var b in r.torso) r.Rot(b, r.R, pitch / r.torso.Length);
        r.Rot(r.neck, r.U, -twist * 0.4f + shake * 0.2f);
        r.Rot(r.neck, r.R, -pitch * 0.3f);

        r.Rot(r.rightArm, r.F, rLift);
        r.Rot(r.rightArm, r.U, rYaw);
        r.Rot(r.rightBlade, r.F, blade);
        r.Rot(r.leftArm, r.F, -lLift);
        r.Rot(r.leftArm, r.U, -lYaw);
        r.Rot(r.leftBlade, r.F, -lBlade);

        // 前腳撐地，身體跟著扭
        foreach (var leg in r.legs.Where(l => l.along01 < 0.35f))
            r.Rot(leg.hip, r.F, -leg.side * 10f * Mathf.Clamp01(Mathf.Abs(twist) / 18f + Mathf.Max(0f, pitch) / 25f));
        for (int i = 0; i < r.body.Length; i++) r.Rot(r.body[i], r.U, -twist * 0.12f);
        foreach (var tl in r.tail) r.Rot(tl, r.U, -twist * 0.7f);
    }

    /// <summary>
    /// 甩尾：身體往反方向捲起蓄力 → 從頭到尾依序甩出（像鞭子，越後面的節越晚到）→ 尾巴掃過地面 → 收回。
    /// s = +1 尾巴往左掃、-1 往右掃（繞 U 正轉會讓身後的節往左擺）。所有左右相關的角度都乘上 s 做鏡像。
    /// </summary>
    static void PoseTailWhip(Rig r, float t, float s)
    {
        float Curl(float time) => s * Keys(time, (0f, 0f), (0.45f, -7f), (0.72f, 17f), (0.95f, 15f), (1.8f, 0f));
        float hipsYaw = s * Keys(t, (0f, 0f), (0.45f, -18f), (0.72f, 38f), (0.95f, 34f), (1.8f, 0f));
        float lean = s * Keys(t, (0f, 0f), (0.45f, -6f), (0.72f, 10f), (0.95f, 8f), (1.8f, 0f));
        float crouch = Keys(t, (0f, 0f), (0.45f, -0.03f), (0.72f, 0.01f), (1.8f, 0f));

        r.MoveHips(0f, crouch);
        r.Rot(r.hips, r.U, hipsYaw);
        r.Rot(r.hips, r.F, lean);

        // 鞭子：每一節比前一節晚一點到位，角度一路累加到尾巴
        for (int i = 0; i < r.body.Length; i++)
            r.Rot(r.body[i], r.U, Curl(t - i * 0.03f));
        foreach (var tl in r.tail) r.Rot(tl, r.U, Curl(t - 0.24f) * 2.2f);

        // 上半身反向扭，眼睛盯著前方；鐮刀收在胸前保持平衡
        r.Rot(r.torso[0], r.U, -hipsYaw * 0.65f);
        r.Rot(r.torso[2], r.U, -hipsYaw * 0.25f);
        foreach (var b in r.torso) r.Rot(b, r.R, Keys(t, (0f, 0f), (0.45f, 4f), (0.72f, -3f), (1.8f, 0f)));
        float leadArm = Keys(t, (0f, 0f), (0.45f, -15f), (0.72f, 25f), (1.8f, 0f));
        float trailArm = Keys(t, (0f, 0f), (0.45f, 25f), (0.72f, -15f), (1.8f, 0f));
        r.Rot(r.rightArm, r.F, s > 0f ? leadArm : trailArm);
        r.Rot(r.leftArm, r.F, -(s > 0f ? trailArm : leadArm));
        r.Rot(r.neck, r.U, -hipsYaw * 0.3f);

        // 腳：蓄力時抓地，甩出時往反方向撐
        float brace = s * Keys(t, (0f, 0f), (0.45f, 1f), (0.72f, -1f), (0.95f, -0.8f), (1.8f, 0f));
        foreach (var leg in r.legs)
        {
            r.Rot(leg.hip, r.U, leg.side * 10f * brace * (0.3f + leg.along01));
            r.Rot(leg.hip, r.F, -leg.side * 8f * Mathf.Abs(brace));
        }
    }

    /// <summary>
    /// 衝刺：0~0.5 壓低身體、鐮刀往前收成矛狀蓄力 → 0.5~1.28 高速爬行衝刺（原地動畫，實際位移交給 AI）
    /// → 1.3 雙鐮由下往上撈斬收尾 → 收回。
    /// </summary>
    static void PoseDash(Rig r, float t)
    {
        float dash = Keys(t, (0f, 0f), (0.45f, 0f), (0.55f, 1f), (1.22f, 1f), (1.32f, 0f), (2f, 0f));
        float crouch = Keys(t, (0f, 0f), (0.45f, -0.05f), (0.55f, -0.035f), (1.22f, -0.035f), (1.4f, 0.02f), (2f, 0f));
        float back = Keys(t, (0f, 0f), (0.45f, -0.05f), (0.55f, 0.02f), (1.22f, 0.02f), (1.4f, 0.05f), (2f, 0f));
        float pitch = Keys(t, (0f, 0f), (0.45f, 26f), (1.22f, 24f), (1.4f, -22f), (1.6f, -18f), (2f, 0f));
        // 鐮刀：蓄力時往前下方收攏（像兩支矛），收尾時往上撈
        float lift = Keys(t, (0f, 0f), (0.45f, -18f), (1.22f, -16f), (1.4f, 70f), (1.6f, 62f), (2f, 0f));
        float yaw = Keys(t, (0f, 0f), (0.45f, -62f), (1.22f, -58f), (1.4f, -25f), (1.6f, -20f), (2f, 0f));
        float blade = Keys(t, (0f, 0f), (0.45f, -20f), (1.22f, -20f), (1.4f, 35f), (2f, 0f));
        float shake = t > 0.25f && t < 0.5f ? Mathf.Sin(t * 90f) * 2f * Mathf.InverseLerp(0.25f, 0.5f, t) : 0f;

        // 衝刺中每秒 3 步的高速爬行
        float w = TwoPi * 3f * t;
        r.MoveHips(back, crouch + dash * 0.01f * Mathf.Abs(Mathf.Sin(w)));
        r.Rot(r.hips, r.U, dash * 3f * Mathf.Sin(w + 0.9f) + shake);
        for (int i = 0; i < r.body.Length; i++)
            r.Rot(r.body[i], r.U, dash * 5f * Mathf.Sin(w - (i + 1) * 0.9f));
        foreach (var tl in r.tail) r.Rot(tl, r.U, dash * 10f * Mathf.Sin(w - 7.5f));
        foreach (var tl in r.tail) r.Rot(tl, r.R, -8f * Keys(t, (0f, 0f), (0.45f, 1f), (1.22f, 1f), (2f, 0f)));

        foreach (var b in r.torso) r.Rot(b, r.R, pitch / r.torso.Length);
        r.Rot(r.neck, r.R, -pitch * 0.4f);
        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * Keys(t, (0f, 0f), (0.45f, 22f), (1.22f, 26f), (1.4f, 34f), (2f, 0f)));

        r.Rot(r.rightArm, r.F, lift);
        r.Rot(r.rightArm, r.U, yaw);
        r.Rot(r.rightBlade, r.F, blade);
        r.Rot(r.leftArm, r.F, -lift);
        r.Rot(r.leftArm, r.U, -yaw);
        r.Rot(r.leftBlade, r.F, -blade);

        float ready = Keys(t, (0f, 0f), (0.45f, 1f), (0.55f, 0f), (1.22f, 0f), (1.4f, 0.6f), (2f, 0f));
        foreach (var leg in r.legs)
        {
            float phase = w - leg.along01 * TwoPi + (leg.side > 0 ? 0f : Mathf.PI);
            float stepLift = Mathf.Max(0f, Mathf.Sin(phase)) * dash;
            r.Rot(leg.hip, r.U, -leg.side * (22f * -Mathf.Cos(phase) * dash));
            r.Rot(leg.hip, r.F, leg.side * (24f * stepLift - 12f * ready));
            r.Rot(leg.knee, r.F, leg.side * (16f * stepLift + 10f * ready));
        }
    }

    /// <summary>
    /// 遠程：上身後仰、大顎張開在嘴前聚能量（0~0.6）→ 頭往前一頂射出（0.66）→ 後座力 → 收回。
    /// </summary>
    static void PoseRanged(Rig r, float t)
    {
        float rear = Keys(t, (0f, 0f), (0.55f, -26f), (0.66f, 16f), (0.8f, 10f), (1.6f, 0f));
        float neck = Keys(t, (0f, 0f), (0.55f, -18f), (0.66f, 22f), (0.8f, 12f), (1.6f, 0f));
        float open = Keys(t, (0f, 0f), (0.3f, 30f), (0.6f, 36f), (0.7f, 40f), (1f, 10f), (1.6f, 0f));
        float spread = Keys(t, (0f, 0f), (0.55f, 42f), (0.66f, 30f), (1.6f, 0f));
        float recoil = Keys(t, (0f, 0f), (0.55f, -0.02f), (0.66f, 0.03f), (0.8f, -0.03f), (1.6f, 0f));
        float tremble = t > 0.2f && t < 0.6f ? Mathf.Sin(t * 70f) * 1.5f * Mathf.InverseLerp(0.2f, 0.6f, t) : 0f;

        r.MoveHips(recoil, 0f);
        foreach (var b in r.torso) r.Rot(b, r.R, (rear + tremble) / r.torso.Length);
        r.Rot(r.neck, r.R, neck);
        r.Rot(r.neck, r.U, tremble);
        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * open);
        r.Rot(r.underJaw, r.R, -open * 0.6f);

        // 鐮刀張開像在聚力，射出時往後甩
        r.Rot(r.rightArm, r.F, spread);
        r.Rot(r.leftArm, r.F, -spread);
        r.Rot(r.rightArm, r.U, spread * 0.5f);
        r.Rot(r.leftArm, r.U, -spread * 0.5f);

        float brace = Keys(t, (0f, 0f), (0.55f, 1f), (0.66f, 0.4f), (1.6f, 0f));
        foreach (var leg in r.legs.Where(l => l.along01 < 0.4f))
            r.Rot(leg.hip, r.F, -leg.side * 9f * brace);
        foreach (var tl in r.tail) r.Rot(tl, r.R, -6f * brace);
    }

    /// <summary>
    /// 撲擊（殘影圍殺的收尾）：0~0.25 全身縮起蓄力 → 0.25~0.55 騰空（實際的跳躍軌跡由技能元件移動物件）
    /// → 0.55 落地重砸、大顎咬合 → 收回。
    /// </summary>
    static void PosePounce(Rig r, float t)
    {
        float crouch = Keys(t, (0f, 0f), (0.25f, -0.06f), (0.4f, 0.04f), (0.55f, -0.05f), (0.75f, -0.04f), (1.3f, 0f));
        float back = Keys(t, (0f, 0f), (0.25f, -0.05f), (0.4f, 0.05f), (0.55f, 0.06f), (1.3f, 0f));
        float pitch = Keys(t, (0f, 0f), (0.25f, 16f), (0.4f, -12f), (0.55f, 32f), (0.75f, 28f), (1.3f, 0f));
        float lift = Keys(t, (0f, 0f), (0.25f, 34f), (0.4f, 12f), (0.55f, -30f), (0.75f, -26f), (1.3f, 0f));
        float yaw = Keys(t, (0f, 0f), (0.25f, 42f), (0.4f, -72f), (0.55f, -60f), (0.75f, -55f), (1.3f, 0f));
        float open = Keys(t, (0f, 0f), (0.25f, 32f), (0.5f, 38f), (0.56f, -8f), (0.8f, -5f), (1.3f, 0f));
        float tuck = Keys(t, (0f, 0f), (0.25f, -0.4f), (0.32f, 1f), (0.5f, 1f), (0.58f, -0.6f), (0.8f, -0.4f), (1.3f, 0f));
        float shake = t > 0.55f && t < 0.75f ? Mathf.Sin(t * 85f) * (0.75f - t) * 25f : 0f;

        r.MoveHips(back, crouch);
        foreach (var b in r.torso) r.Rot(b, r.R, (pitch + shake * 0.3f) / r.torso.Length);
        r.Rot(r.neck, r.R, -pitch * 0.3f);
        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * open);
        r.Rot(r.underJaw, r.R, -open * 0.5f);

        r.Rot(r.rightArm, r.F, lift);
        r.Rot(r.rightArm, r.U, yaw);
        r.Rot(r.leftArm, r.F, -lift);
        r.Rot(r.leftArm, r.U, -yaw);

        // 騰空時腳往內收、落地時往外撐開
        foreach (var leg in r.legs)
        {
            r.Rot(leg.hip, r.F, leg.side * 20f * tuck);
            r.Rot(leg.knee, r.F, leg.side * 22f * Mathf.Max(0f, tuck));
            r.Rot(leg.hip, r.U, leg.side * 10f * tuck * (leg.along01 - 0.5f));
        }
        for (int i = 0; i < r.body.Length; i++) r.Rot(r.body[i], r.R, -2.5f * tuck);
        foreach (var tl in r.tail) r.Rot(tl, r.R, -14f * Mathf.Max(0f, tuck));
    }

    /// <summary>
    /// 吼叫（召喚蝙蝠用）：0~0.5 上半身高高立起、雙鐮往上張開、大顎全開 → 0.55 吼出（召喚） → 抖動維持到 1.6 → 收回。
    /// </summary>
    static void PoseRoar(Rig r, float t)
    {
        float rear = Keys(t, (0f, 0f), (0.5f, -34f), (0.6f, -28f), (1.6f, -30f), (2.2f, 0f));
        float neck = Keys(t, (0f, 0f), (0.5f, -30f), (0.6f, -18f), (1.6f, -20f), (2.2f, 0f));
        float rise = Keys(t, (0f, 0f), (0.5f, 0.05f), (1.6f, 0.045f), (2.2f, 0f));
        float open = Keys(t, (0f, 0f), (0.45f, 20f), (0.6f, 48f), (1.6f, 44f), (2.2f, 0f));
        float lift = Keys(t, (0f, 0f), (0.5f, 62f), (0.6f, 70f), (1.6f, 66f), (2.2f, 0f));
        float yaw = Keys(t, (0f, 0f), (0.5f, 30f), (0.6f, 38f), (1.6f, 34f), (2.2f, 0f));
        float roar = t > 0.55f && t < 1.6f ? Mathf.Sin(t * 75f) * Mathf.Min(1f, (1.6f - t) * 3f) : 0f;

        r.MoveHips(-0.02f * rise / 0.05f, rise);
        foreach (var b in r.torso) r.Rot(b, r.R, (rear + roar * 1.5f) / r.torso.Length);
        r.Rot(r.neck, r.R, neck);
        r.Rot(r.neck, r.U, roar * 3f);
        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * (open + roar * 4f));
        r.Rot(r.underJaw, r.R, -open * 0.6f);

        r.Rot(r.rightArm, r.F, lift + roar * 4f);
        r.Rot(r.rightArm, r.U, yaw);
        r.Rot(r.leftArm, r.F, -(lift + roar * 4f));
        r.Rot(r.leftArm, r.U, -yaw);

        float brace = Keys(t, (0f, 0f), (0.5f, 1f), (1.6f, 1f), (2.2f, 0f));
        foreach (var leg in r.legs)
            r.Rot(leg.hip, r.F, -leg.side * 10f * brace + leg.side * roar * 3f);
        foreach (var tl in r.tail) r.Rot(tl, r.R, -18f * brace + roar * 5f);
        for (int i = 0; i < r.body.Length; i++) r.Rot(r.body[i], r.U, roar * 1.2f * Mathf.Sin(i));
    }

    static void PoseBite(Rig r, float t)
    {
        // 0~0.4 後仰張口、0.4~0.55 撲出、0.52 咬合、0.55~0.75 停留、0.75~1.25 收回
        float rear = Keys(t, (0f, 0f), (0.4f, -24f), (0.55f, 34f), (0.75f, 30f), (1.25f, 0f));
        float neck = Keys(t, (0f, 0f), (0.4f, -22f), (0.55f, 24f), (0.75f, 20f), (1.25f, 0f));
        float fwd = Keys(t, (0f, 0f), (0.4f, -0.05f), (0.55f, 0.22f), (0.75f, 0.2f), (1.25f, 0f));
        float drop = Keys(t, (0f, 0f), (0.4f, 0.02f), (0.55f, -0.04f), (0.75f, -0.035f), (1.25f, 0f));
        float open = Keys(t, (0f, 0f), (0.38f, 30f), (0.5f, 34f), (0.53f, -6f), (0.7f, -4f), (1.25f, 0f));
        float spread = Keys(t, (0f, 0f), (0.4f, 28f), (0.55f, 40f), (0.75f, 35f), (1.25f, 0f));

        r.MoveHips(fwd, drop);
        foreach (var b in r.torso) r.Rot(b, r.R, rear / r.torso.Length);
        r.Rot(r.neck, r.R, neck);
        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * open);
        r.Rot(r.underJaw, r.R, -open * 0.5f);

        // 撲的時候鐮刀往兩側張開往後
        r.Rot(r.rightArm, r.F, spread);
        r.Rot(r.leftArm, r.F, -spread);
        r.Rot(r.rightArm, r.U, spread * 0.6f);
        r.Rot(r.leftArm, r.U, -spread * 0.6f);

        // 後半身撐地往前推
        float push = Keys(t, (0f, 0f), (0.4f, 1f), (0.55f, -1f), (0.75f, -0.8f), (1.25f, 0f));
        foreach (var leg in r.legs)
            r.Rot(leg.hip, r.U, leg.side * 14f * push * (0.4f + leg.along01));
        for (int i = 0; i < r.body.Length; i++) r.Rot(r.body[i], r.R, -push * 1.2f);
        foreach (var tl in r.tail) r.Rot(tl, r.R, -push * 10f);
    }

    static void PoseHit(Rig r, float t)
    {
        // 往後彈一下再阻尼回正
        float k = Mathf.Exp(-t * 7f);
        float recoil = -22f * k * Mathf.Cos(t * 18f);
        if (t < 0.06f) recoil *= t / 0.06f;

        r.MoveHips(-0.04f * k, 0.01f * k);
        foreach (var b in r.torso) r.Rot(b, r.R, recoil / r.torso.Length);
        r.Rot(r.neck, r.R, recoil * 0.6f);
        r.Rot(r.neck, r.U, 14f * k * Mathf.Sin(t * 26f));
        r.Rot(r.rightArm, r.F, -recoil * 0.9f);
        r.Rot(r.leftArm, r.F, recoil * 0.9f);
        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * 20f * k);
        foreach (var leg in r.legs) r.Rot(leg.hip, r.F, leg.side * 10f * k * Mathf.Sin(t * 30f + leg.along01 * 6f));
        foreach (var tl in r.tail) r.Rot(tl, r.U, 15f * k * Mathf.Sin(t * 20f));
    }

    static void PoseDeath(Rig r, float t)
    {
        // 0~0.4 踉蹌後仰、0.4~1.4 上半身往前倒、1.2~2.0 翻向一側、之後腳抽搐停住
        float rear = Keys(t, (0f, 0f), (0.35f, -26f), (0.5f, -20f), (1.4f, 70f), (1.55f, 64f), (1.7f, 68f), (2.6f, 68f));
        float neck = Keys(t, (0f, 0f), (0.35f, -20f), (1.4f, 30f), (2.6f, 34f));
        float drop = Keys(t, (0f, 0f), (0.35f, 0.02f), (1.4f, -0.05f), (2.0f, -0.08f), (2.6f, -0.08f));
        float back = Keys(t, (0f, 0f), (0.35f, -0.06f), (1.4f, -0.04f), (2.6f, -0.04f));
        float roll = Keys(t, (0f, 0f), (1.2f, 0f), (2.0f, 18f), (2.2f, 15f), (2.6f, 16f));
        float armsUp = Keys(t, (0f, 0f), (0.35f, 45f), (1.4f, -35f), (2.6f, -40f));
        float curl = Keys(t, (0f, 0f), (0.5f, 0.2f), (2.0f, 1f), (2.6f, 1f));
        float twitchEnv = t > 1.6f ? Mathf.Clamp01((2.5f - t) / 0.9f) : 0f;

        r.MoveHips(back, drop);
        r.Rot(r.hips, r.F, roll);
        foreach (var b in r.torso) r.Rot(b, r.R, rear / r.torso.Length);
        r.Rot(r.neck, r.R, neck);
        r.Rot(r.neck, r.U, Keys(t, (0f, 0f), (0.35f, 15f), (1.4f, -20f), (2.6f, -22f)));
        foreach (var (fang, side) in r.fangs) r.Rot(fang, r.U, side * Keys(t, (0f, 0f), (0.35f, 32f), (2.0f, 18f), (2.6f, 18f)));

        r.Rot(r.rightArm, r.F, armsUp);
        r.Rot(r.leftArm, r.F, -armsUp);
        r.Rot(r.rightBlade, r.F, -25f * curl);
        r.Rot(r.leftBlade, r.F, 25f * curl);

        // 腳往內縮、最後幾下抽搐
        foreach (var leg in r.legs)
        {
            float twitch = twitchEnv * Mathf.Max(0f, Mathf.Sin(t * 34f + leg.along01 * 11f + leg.side));
            r.Rot(leg.hip, r.F, leg.side * (-12f * curl + 8f * twitch));
            r.Rot(leg.knee, r.F, leg.side * 38f * curl);
            r.Rot(leg.foot, r.F, leg.side * 30f * curl);
        }
        for (int i = 0; i < r.body.Length; i++) r.Rot(r.body[i], r.U, Keys(t, (0f, 0f), (1.4f, 3f), (2.6f, 4f)) * Mathf.Sin(i * 0.8f));
        foreach (var tl in r.tail) r.Rot(tl, r.U, 20f * curl);
    }

    /// <summary>關鍵格之間用平滑曲線插值（每段頭尾速度為 0）。</summary>
    static float Keys(float t, params (float time, float value)[] keys)
    {
        if (t <= keys[0].time) return keys[0].value;
        for (int i = 1; i < keys.Length; i++)
        {
            if (t > keys[i].time) continue;
            var a = keys[i - 1];
            var b = keys[i];
            float u = Mathf.InverseLerp(a.time, b.time, t);
            return Mathf.Lerp(a.value, b.value, u * u * (3f - 2f * u));
        }
        return keys[^1].value;
    }

    // ───────────────────────── 烘焙 ─────────────────────────

    static AnimationClip BakeClip(Rig rig, ClipDef def)
    {
        int frames = Mathf.RoundToInt(def.length * SampleRate);
        var bones = rig.all;
        var rotKeys = bones.Select(_ => new Keyframe[4][]).ToArray();
        for (int b = 0; b < bones.Length; b++)
            for (int c = 0; c < 4; c++) rotKeys[b][c] = new Keyframe[frames + 1];
        var posKeys = new Keyframe[3][];
        for (int c = 0; c < 3; c++) posKeys[c] = new Keyframe[frames + 1];
        var prevRot = new Quaternion[bones.Length];

        for (int f = 0; f <= frames; f++)
        {
            float time = f / SampleRate;
            // 循環片段的最後一格直接用第 0 格，接縫才不會跳
            float poseTime = def.loop && f == frames ? 0f : time;
            rig.ResetToBind();
            def.pose(rig, poseTime);

            for (int b = 0; b < bones.Length; b++)
            {
                var q = bones[b].localRotation;
                if (f > 0 && Quaternion.Dot(q, prevRot[b]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                prevRot[b] = q;
                rotKeys[b][0][f] = new Keyframe(time, q.x);
                rotKeys[b][1][f] = new Keyframe(time, q.y);
                rotKeys[b][2][f] = new Keyframe(time, q.z);
                rotKeys[b][3][f] = new Keyframe(time, q.w);
            }
            var p = rig.hips.localPosition;
            posKeys[0][f] = new Keyframe(time, p.x);
            posKeys[1][f] = new Keyframe(time, p.y);
            posKeys[2][f] = new Keyframe(time, p.z);
        }
        rig.ResetToBind();

        string path = $"{OutDir}/{def.name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = SampleRate;

        string[] rotProps = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        for (int b = 0; b < bones.Length; b++)
        {
            string bonePath = AnimationUtility.CalculateTransformPath(bones[b], rig.root);
            for (int c = 0; c < 4; c++)
            {
                var curve = new AnimationCurve(rotKeys[b][c]);
                SmoothTangents(curve);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), rotProps[c]), curve);
            }
        }
        string hipsPath = AnimationUtility.CalculateTransformPath(rig.hips, rig.root);
        string[] posProps = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
        for (int c = 0; c < 3; c++)
        {
            var curve = new AnimationCurve(posKeys[c]);
            SmoothTangents(curve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(hipsPath, typeof(Transform), posProps[c]), curve);
        }
        clip.EnsureQuaternionContinuity();

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = def.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AnimationUtility.SetAnimationEvents(clip, def.events.Select(e => new AnimationEvent
        {
            time = Mathf.Min(e.time, def.length),
            functionName = e.function,
            stringParameter = e.arg,
        }).ToArray());

        EditorUtility.SetDirty(clip);
        return clip;
    }

    static void SmoothTangents(AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++)
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        for (int i = 0; i < curve.length; i++)
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
    }

    // ───────────────────────── Animator / Prefab ─────────────────────────

    static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ac.AddParameter("CrawlRate", AnimatorControllerParameterType.Float);
        ac.parameters = ac.parameters.Select(p => { if (p.name == "CrawlRate") p.defaultFloat = 1f; return p; }).ToArray();
        foreach (var trigger in new[] { "Slash", "Bite", "Sweep", "TailWhipLeft", "TailWhipRight", "Dash", "Ranged", "Pounce", "Roar", "Hit", "Die" })
            ac.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        ac.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        var sm = ac.layers[0].stateMachine;
        AnimatorState Add(string name, Vector2 pos)
        {
            var s = sm.AddState(name, pos);
            s.motion = clips[name];
            return s;
        }

        var idle = Add("Idle", new Vector2(300, 0));
        var crawl = Add("Crawl", new Vector2(300, 120));
        crawl.speedParameter = "CrawlRate";
        crawl.speedParameterActive = true;
        var slash = Add("Slash", new Vector2(600, -120));
        var bite = Add("Bite", new Vector2(600, 0));
        var sweep = Add("SweepCombo", new Vector2(850, -60));
        var tailWhipLeft = Add("TailWhipLeft", new Vector2(850, 60));
        var tailWhipRight = Add("TailWhipRight", new Vector2(1050, 60));
        var dashState = Add("Dash", new Vector2(1050, -60));
        var pounce = Add("Pounce", new Vector2(1050, 180));
        var roarState = Add("Roar", new Vector2(1050, 300));
        var ranged = Add("Ranged", new Vector2(850, 180));
        var hit = Add("Hit", new Vector2(600, 120));
        var death = Add("Death", new Vector2(600, 260));
        sm.defaultState = idle;

        var toCrawl = idle.AddTransition(crawl);
        toCrawl.hasExitTime = false;
        toCrawl.duration = 0.2f;
        toCrawl.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        var toIdle = crawl.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.25f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        void OneShot(AnimatorState state, string trigger, float blendIn)
        {
            var enter = sm.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.duration = blendIn;
            enter.canTransitionToSelf = trigger == "Hit";
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            enter.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            var back = state.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = 0.92f;
            back.duration = 0.18f;
        }
        OneShot(slash, "Slash", 0.1f);
        OneShot(bite, "Bite", 0.1f);
        OneShot(sweep, "Sweep", 0.1f);
        OneShot(tailWhipLeft, "TailWhipLeft", 0.1f);
        OneShot(tailWhipRight, "TailWhipRight", 0.1f);
        OneShot(dashState, "Dash", 0.1f);
        OneShot(pounce, "Pounce", 0.08f);
        OneShot(roarState, "Roar", 0.12f);
        OneShot(ranged, "Ranged", 0.1f);
        OneShot(hit, "Hit", 0.05f);

        var die = sm.AddAnyStateTransition(death);
        die.hasExitTime = false;
        die.duration = 0.12f;
        die.canTransitionToSelf = false;
        die.AddCondition(AnimatorConditionMode.If, 0f, "Die");

        EditorUtility.SetDirty(ac);
        return ac;
    }

    static void BuildPrefab(GameObject model, AnimatorController controller)
    {
        EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = "PF_SkeletonCentipede_Animated";
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (instance.GetComponent<SkeletonCentipedeAnimationDriver>() == null)
                instance.AddComponent<SkeletonCentipedeAnimationDriver>();
            AddBossBody(instance);
            SkeletonCentipedeVfxInstaller.AttachTo(instance);
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// 給玩家打得到的身體：依模型外框加一個 BoxCollider，再掛 BossHealth（隊友的血量 / 血條元件，原樣使用）。
    /// 蝠群護體技能靠 BossHealth 的血量門檻觸發。
    /// </summary>
    static void AddBossBody(GameObject instance)
    {
        var box = instance.GetComponent<BoxCollider>();
        if (box == null) box = instance.AddComponent<BoxCollider>();
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            box.center = instance.transform.InverseTransformPoint(b.center);
            var size = instance.transform.InverseTransformVector(b.size);
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.85f;
        }

        var health = instance.GetComponent<BossHealth>();
        if (health == null) health = instance.AddComponent<BossHealth>();
        health.bossName = "百足駭骨";
        health.maxHealth = 6000f;
    }

    // ───────────────────────── 工具 ─────────────────────────

    static void ConfigureModelImport()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        if (importer == null) throw new FileNotFoundException("找不到百足駭骨模型", ModelPath);
        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; changed = true; }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
        if (importer.optimizeGameObjects) { importer.optimizeGameObjects = false; changed = true; }
        if (changed) importer.SaveAndReimport();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Bounds RenderBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
        return b;
    }
}
#endif
