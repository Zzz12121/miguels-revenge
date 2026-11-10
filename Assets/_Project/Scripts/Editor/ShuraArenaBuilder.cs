#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 選單：GeminiGrowUp > 建立修羅場 Boss 場景 (Shura Arena)
///
/// 產生 Assets/_Project/Scenes/ShuraArena.unity：日式「修羅場」風格的 Boss 戰場景。
///   - 玩家、競技場地板、大門沿用 BossArena：直接放 PlayerRig + BossRoom 兩個 Prefab（不修改 Prefab 本身）
///   - Boss 換成百足駭骨（PF_SkeletonCentipede_Animated）＋它自己的 AI（SkeletonCentipedeBossAI）；
///     BossRoom 裡原本的石像 Boss（BossController）在這個場景關掉
///   - 在這個場景裡把西式城牆裝飾關掉，換成：築地塀（白牆＋黑瓦）、血跡、插在牆上的箭、
///     紙燈籠、幟旗（のぼり）、櫓（瞭望台）＋篝火、鳥居、燃燒中的寺院、五重塔、杉林
///   - 赤月夜空（程序天空的太陽圓盤當月亮）、紅霧、櫻花瓣／灰燼／火星粒子、後製（Bloom/暗角/調色）
///
/// 所有裝飾都沒有碰撞體，不影響玩法；地板材質只在這個場景以 override 換色。
/// 可重複執行（整個場景重建）；材質與貼圖放在 ShuraArena 資料夾，重跑會沿用並更新數值。
/// </summary>
public static class ShuraArenaBuilder
{
    const string Root = "Assets/_Project";
    const string ScenePath = Root + "/Scenes/ShuraArena.unity";
    const string SceneAssetDir = Root + "/Scenes/ShuraArena";
    const string MatDir = Root + "/Materials/ShuraArena";
    const string VfxDir = Root + "/VFX/ShuraArena";
    const string CentipedePrefabPath = Root + "/Prefabs/Boss/PF_SkeletonCentipede_Animated.prefab";
    const float CentipedeScale = 5.26f; // 和 VFX_VoidRend_Bocchi 測試場景裡的大小一致

    static System.Random rng;
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static bool Chance(float p) => rng.NextDouble() < p;

    // ---- 材質 ----
    static Material mPlaster, mStone, mWood, mCharred, mRoof, mRoofCap, mVermilion, mLacquer, mBlood, mSoot,
        mFlagRed, mFlagBlack, mFlagWhite, mGold, mCedar, mGround, mLantern, mIron, mFireCore, mFeatherW, mFeatherR;
    static Material pAdd, pSmoke, pPetal, pAsh;
    static Material mStraw, mRope, mClay, mCrate, mImmortalLabel;
    static Mesh coneMesh;

    [MenuItem("GeminiGrowUp/建立修羅場 Boss 場景 (Shura Arena)")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("重建修羅場場景？",
                "ShuraArena.unity 已存在，會整個重建（在場景裡手動加的東西會消失）。\nBossRoom / PlayerRig Prefab 本身不會被修改。",
                "重建", "取消"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildScene();
    }

    /// <summary>不跳對話框的版本，給 batchmode（-executeMethod ShuraArenaBuilder.BuildScene）使用。</summary>
    public static void BuildScene()
    {
        string roomPath = Floor02Builder.FindBossRoomPrefab();
        var roomPrefab = roomPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(roomPath) : null;
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PlayerRig.prefab");
        if (roomPrefab == null || rigPrefab == null)
        {
            EditorUtility.DisplayDialog("找不到 Prefab", "需要 BossRoom.prefab 和 PlayerRig.prefab。", "OK");
            return;
        }

        BossArenaBuilder.EnsureFolder(SceneAssetDir);
        BossArenaBuilder.EnsureFolder(MatDir);
        BossArenaBuilder.EnsureFolder(VfxDir);
        rng = new System.Random(1337);

        CreateMaterials();
        coneMesh = CreateConeMesh();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- 玩法：和 BossArena 一樣的兩個 Prefab ----------
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
        var room = (GameObject)PrefabUtility.InstantiatePrefab(roomPrefab, scene);

        var floor = room.GetComponentInChildren<ArenaFloor>(true);
        var gate = room.GetComponentInChildren<BossGate>(true);
        Vector3 c = floor != null ? floor.transform.position : Vector3.zero;
        float r = floor != null ? floor.radius : 36f;

        RestyleBossRoom(room, floor, gate);
        var bossAI = SetupCentipedeBoss(room, gate, c, r);
        EnablePostProcessing(rig);

        // ---------- 環境 ----------
        var env = new GameObject("ShuraEnvironment").transform;
        BuildGround(env, c, r);
        BuildWallRing(env, c, r, gate);
        BuildLanterns(env, c, r, gate);
        if (gate != null) BuildGateHouse(env, gate);
        BuildBanners(env, c, r, gate);
        BuildYagura(env, c, r);
        BuildToriis(env, c, r, gate);
        var occupied = BuildTemples(env, c, r);
        occupied.Add(BuildPagoda(env, c, r));
        BuildForest(env, c, r, occupied);
        BuildAtmosphereVfx(env, c, r);
        BuildPropPrefabs();
        BuildBreakables(env, c, r, gate);
        SetupImmortalWalls(room, gate);

        // ---------- 光線 / 天空 / 霧 / 後製 ----------
        BuildLighting(c, bossAI);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Shura] 已建立修羅場 Boss 場景：{ScenePath}（競技場半徑 {r}m）。按 Play 開始。");
    }

    // =====================================================================
    //  BossRoom 在這個場景的外觀 override（不改 Prefab）
    // =====================================================================

    static void RestyleBossRoom(GameObject room, ArenaFloor floor, BossGate gate)
    {
        // 西式城牆、塔、火把整組關掉（碰撞用的隱形環形牆不在這裡，會保留）
        var dressing = Floor02Builder.FindDeep(room.transform, "CastleDressing");
        if (dressing != null)
        {
            dressing.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(dressing.gameObject);
        }

        // 地板：沿用原本的石板貼圖，改成偏暗紅的焦黑石疊
        if (floor != null && floor.tileMaterial != null)
        {
            var oldFloor = floor.tileMaterial;
            var newFloor = CopyTinted(oldFloor, "M_Shura_FloorStone", new Color(0.58f, 0.5f, 0.48f), 0.22f);
            var oldRock = floor.rockMaterial;
            var newRock = oldRock != null ? CopyTinted(oldRock, "M_Shura_FloorRock", new Color(0.3f, 0.26f, 0.26f), 0.1f) : newFloor;

            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.sharedMaterial == oldFloor) mr.sharedMaterial = newFloor;
                else if (oldRock != null && mr.sharedMaterial == oldRock) mr.sharedMaterial = newRock;
                else continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(mr);
            }
            floor.tileMaterial = newFloor;
            floor.rockMaterial = newRock;
            PrefabUtility.RecordPrefabInstancePropertyModifications(floor);
        }

        // 大門：門框朱漆、門板黑漆
        if (gate != null)
        {
            foreach (var mr in gate.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name.StartsWith("Frame")) mr.sharedMaterial = mVermilion;
                else if (mr.name == "Door") mr.sharedMaterial = mLacquer;
                else continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(mr);
            }
        }
    }

    // =====================================================================
    //  Boss：百足駭骨＋它自己的 AI（SkeletonCentipedeBossAI），取代 BossRoom 裡的石像 Boss
    // =====================================================================

    static SkeletonCentipedeBossAI SetupCentipedeBoss(GameObject room, BossGate gate, Vector3 c, float r)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CentipedePrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[Shura] 找不到百足駭骨 Prefab，Boss 維持原樣。");
            return null;
        }

        // 原本的石像 Boss（BossController）在這個場景整個關掉；大門不再連到它（玩家一開始就在場內）
        Vector3 pos = c + Vector3.forward * 6f;
        Quaternion rot = Quaternion.Euler(0f, 180f, 0f);
        var oldBoss = room.GetComponentInChildren<BossController>(true);
        if (oldBoss != null)
        {
            pos = oldBoss.transform.position;
            rot = oldBoss.transform.rotation;
            oldBoss.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldBoss.gameObject);
        }
        if (gate != null)
        {
            gate.boss = null;
            PrefabUtility.RecordPrefabInstancePropertyModifications(gate);
        }

        var boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        boss.name = "SkeletonCentipede Boss";
        boss.transform.SetPositionAndRotation(pos, rot);
        boss.transform.localScale = Vector3.one * CentipedeScale;

        // 會移動的碰撞體：加 kinematic Rigidbody，玩家的攻擊才打得到、也擋得住
        var rb = boss.GetComponent<Rigidbody>();
        if (rb == null) rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var ai = boss.GetComponent<SkeletonCentipedeBossAI>();
        if (ai == null) ai = boss.AddComponent<SkeletonCentipedeBossAI>();
        ai.arenaCenter = c;
        ai.arenaRadius = r;
        return ai;
    }

    // =====================================================================
    //  可破壞物件（佔位用的日式道具：酒樽、木箱、米俵、陶甕）＋ 不可破壞的牆
    // =====================================================================

    static GameObject breakEffect;

    // ---------------- 真實模型（Models/ShuraProps，由 Tripo 道具模型拆分、減面）----------------
    const string PropsModelDir = Root + "/Models/ShuraProps";
    const string PropsPrefabDir = Root + "/Prefabs/ShuraProps";

    /// <summary>名稱、血量、碎片數、煙塵大小、模型轉角（讓正面朝 +Z）、是否有燈火</summary>
    static readonly (string name, float hp, int debris, float fx, float yaw, bool light)[] PropSpecs =
    {
        ("SakeBarrel",   40f, 10, 0.8f,   0f, false),
        ("Crate",        30f,  9, 0.8f,   0f, false),
        ("RiceBale",     25f,  8, 0.7f,   0f, false),
        ("ClayPot",      10f,  7, 0.5f,   0f, false),
        ("Tate",         60f, 10, 0.9f, 180f, false),
        ("Taketaba",     50f, 10, 0.9f,   0f, false),
        ("StoneLantern", 80f, 12, 1.0f,   0f, true),
        ("KatanaRack",   25f,  8, 0.6f,   0f, false),
        ("SpearRack",    30f,  9, 0.8f,   0f, false),
        ("BambooFence",  35f, 10, 0.9f,   0f, false),
    };

    static readonly Dictionary<string, GameObject> propPrefabs = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, Vector3> propSizes = new Dictionary<string, Vector3>();

    /// <summary>
    /// 把 Models/ShuraProps 裡的模型做成可破壞 Prefab（Prefabs/ShuraProps/PF_Shura_*.prefab）：
    /// 共用一個 URP 材質（顏色＋法線貼圖）、包住模型的 BoxCollider、ShuraBreakable；石燈籠加一盞燈。
    /// 模型不存在就跳過，場景會改用基本形狀拼的佔位道具。
    /// </summary>
    static void BuildPropPrefabs()
    {
        propPrefabs.Clear();
        propSizes.Clear();
        var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PropsModelDir + "/T_ShuraProps_BaseColor.jpg");
        if (baseTex == null) { Debug.Log("[Shura] 沒有找到 Models/ShuraProps，可破壞物件使用佔位造型。"); return; }

        string normalPath = PropsModelDir + "/T_ShuraProps_Normal.png";
        if (AssetImporter.GetAtPath(normalPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
        {
            ni.textureType = TextureImporterType.NormalMap;
            ni.sRGBTexture = false;
            ni.SaveAndReimport();
        }
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

        var mat = GetOrCreateMat("M_ShuraProps", "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0f);
        if (normalTex != null)
        {
            mat.SetTexture("_BumpMap", normalTex);
            mat.SetFloat("_BumpScale", 0.6f); // 法線是高面數模型烘的，減面後調淡一點比較不會有接縫
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);

        BossArenaBuilder.EnsureFolder(PropsPrefabDir);
        foreach (var spec in PropSpecs)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsModelDir}/SM_Shura_{spec.name}.obj");
            if (model == null) continue;

            var root = new GameObject($"PF_Shura_{spec.name}");
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
            vis.name = "Model";
            vis.transform.SetParent(root.transform, false);
            vis.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f);
            foreach (var mr in vis.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
            }

            var t = root.transform;
            FinishBreakable(t, spec.hp, spec.debris, spec.fx);
            var size = t.GetComponent<BoxCollider>().size;

            if (spec.light)
            {
                // 石燈籠的火袋大約在 62% 高度
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(t, false);
                lightGo.transform.localPosition = new Vector3(0f, size.y * 0.62f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.6f, 0.3f);
                l.intensity = 4f;
                l.range = 12f;
                l.shadows = LightShadows.None;
                var flicker = root.AddComponent<TorchFlicker>();
                flicker.amount = 0.12f;
                flicker.speed = 3f;
            }

            string path = $"{PropsPrefabDir}/PF_Shura_{spec.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            propPrefabs[spec.name] = prefab;
            propSizes[spec.name] = size;
        }
        Debug.Log($"[Shura] 道具 Prefab：{propPrefabs.Count} 個（{PropsPrefabDir}）");
    }

    /// <summary>有真實模型的 Prefab 就放它；scale 為等比縮放。</summary>
    static bool TryProp(string name, Transform parent, float scale, out Transform t)
    {
        t = null;
        if (!propPrefabs.TryGetValue(name, out var prefab) || prefab == null) return false;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.localScale = Vector3.one * scale;
        t = go.transform;
        return true;
    }

    static float PropHeight(string name, float fallback) => propSizes.TryGetValue(name, out var s) ? s.y : fallback;

    static void BuildBreakables(Transform env, Vector3 c, float r, BossGate gate)
    {
        var root = Group("Breakables", env);
        breakEffect = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/VFX/SkeletonCentipede/Prefabs/VFX_SkeletonCentipede_TailDust.prefab");

        // 場地上：幾堆散落的物資（避開中心的出生點、北邊 Boss 起點、南邊大門通道）
        int clusters = 0, tries = 0;
        while (clusters < 9 && tries++ < 200)
        {
            float ang = R(0f, 360f);
            float dist = R(14f, r - 6f);
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 0f)) < 25f && dist < 22f) continue;   // Boss 起點
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 180f)) < 18f) continue;              // 大門通道
            float a = ang * Mathf.Deg2Rad;
            Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * dist;
            var g = Group($"FieldCluster_{clusters:00}", root);
            g.position = p;
            g.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            Cluster(g, rng.Next(3, 6));
            clusters++;
        }

        // 場地上的石燈籠（有燈火，打碎就熄滅）：避開北邊 Boss 起點與南邊大門
        foreach (float deg in new[] { 60f, 120f, 240f, 300f })
        {
            float a = deg * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            if (TryProp("StoneLantern", root, 1f, out var lantern))
            {
                lantern.name = $"StoneLantern_{deg:000}";
                lantern.position = c + dir * (r * 0.5f);
                lantern.rotation = Quaternion.LookRotation(-dir);
            }
        }

        // 牆邊：靠牆堆放的物資（在牆內側，碰撞牆從半徑 r 開始）
        const int wallStacks = 14;
        for (int i = 0; i < wallStacks; i++)
        {
            float a = (i + R(0.2f, 0.8f)) * Mathf.PI * 2f / wallStacks;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = c + dir * (r - 1.4f);
            if (InGateGap(gate, p, 9f)) continue;
            var g = Group($"WallStack_{i:00}", root);
            g.position = p;
            g.rotation = Quaternion.LookRotation(-dir);
            WallStack(g, i);
        }

        // 牆上：木架上的陶甕（架子本身不可破壞，甕可以打碎）
        const int shelves = 8;
        for (int i = 0; i < shelves; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / shelves + 0.12f;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = c + dir * (r + 0.2f);
            if (InGateGap(gate, p, 9f)) continue;
            var g = Group($"WallShelf_{i:00}", root);
            g.position = p;
            g.rotation = Quaternion.LookRotation(-dir);
            Box(g, "Shelf", new Vector3(0f, 2.3f, 0.35f), Vector3.zero, new Vector3(2.6f, 0.08f, 0.7f), mWood);
            Box(g, "BracketL", new Vector3(-1.1f, 2.05f, 0.3f), new Vector3(45f, 0f, 0f), new Vector3(0.08f, 0.5f, 0.08f), mWood);
            Box(g, "BracketR", new Vector3(1.1f, 2.05f, 0.3f), new Vector3(45f, 0f, 0f), new Vector3(0.08f, 0.5f, 0.08f), mWood);
            for (int k = 0; k < 3; k++)
            {
                var pot = Pot(g, R(0.6f, 0.75f));
                pot.localPosition = new Vector3(-0.8f + k * 0.8f, 2.34f, 0.4f);
            }
        }
    }

    static void Cluster(Transform g, int count)
    {
        for (int i = 0; i < count; i++)
        {
            // 圍成一圈擺，避免互相穿插
            float ang = (i + R(-0.25f, 0.25f)) * Mathf.PI * 2f / count;
            float rad = count > 1 ? R(1.3f, 1.9f) : 0f;
            Vector3 off = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * rad;
            Transform item;
            switch (rng.Next(propPrefabs.ContainsKey("Taketaba") ? 5 : 4))
            {
                case 0: item = SakeBarrel(g); break;
                case 1: item = Crate(g, R(0.8f, 1.05f)); break;
                case 2: item = RiceBale(g); break;
                case 3: item = Pot(g, R(0.9f, 1.3f)); break;
                default: TryProp("Taketaba", g, R(0.9f, 1.05f), out item); break;
            }
            item.localPosition = off;
            item.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
        }
        // 掩體：一半的物資堆旁邊立一面置盾
        if (Chance(0.5f) && TryProp("Tate", g, R(0.95f, 1.05f), out var shield))
        {
            float a = R(0f, Mathf.PI * 2f);
            shield.localPosition = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 2.8f;
            shield.localRotation = Quaternion.LookRotation(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
        }
    }

    static void WallStack(Transform g, int index)
    {
        // 牆邊輪流放：刀架 / 槍架、竹柵、堆起來的物資（+Z 朝向場內）
        if (index % 4 == 1 && TryProp(Chance(0.5f) ? "KatanaRack" : "SpearRack", g, 1f, out var rack))
        {
            rack.localPosition = new Vector3(0f, 0f, 0.3f);
            return;
        }
        if (index % 4 == 3 && propPrefabs.ContainsKey("BambooFence"))
        {
            for (int i = -1; i <= 1; i += 2)
                if (TryProp("BambooFence", g, 1f, out var fence))
                    fence.localPosition = new Vector3(i * 0.95f, 0f, 0.4f);
            return;
        }

        // 下層兩個、上層一個
        bool barrels = Chance(0.5f);
        float lower = 0f;
        for (int i = 0; i < 2; i++)
        {
            var item = barrels ? SakeBarrel(g) : Crate(g, 1f);
            item.localPosition = new Vector3(i == 0 ? -0.75f : 0.75f, 0f, 0f);
            if (barrels) item.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            lower = Mathf.Max(lower, (barrels ? PropHeight("SakeBarrel", 1.03f) : PropHeight("Crate", 1f)) * item.localScale.y);
        }
        var top = Chance(0.5f) ? Crate(g, 0.8f) : RiceBale(g);
        top.localPosition = new Vector3(R(-0.2f, 0.2f), lower + 0.02f, 0f);
    }

    /// <summary>建立一個可破壞物件的根物件（底部在 y=0），子物件建完後再呼叫 FinishBreakable。</summary>
    static Transform NewBreakable(Transform parent, string name) => Group(name, parent);

    static Transform FinishBreakable(Transform t, float health, int debris, float fxScale)
    {
        // 這些東西會被打碎（關掉外觀），不參與靜態合批
        foreach (var tr in t.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(tr.gameObject, 0);

        // 包住整個造型的碰撞體（玩家可以撞到、攻擊打得到）
        var mrs = t.GetComponentsInChildren<MeshRenderer>();
        var b = new Bounds(Vector3.zero, Vector3.zero);
        bool first = true;
        foreach (var mr in mrs)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            foreach (var corner in Corners(mb))
            {
                Vector3 lp = t.InverseTransformPoint(mr.transform.TransformPoint(corner));
                if (first) { b = new Bounds(lp, Vector3.zero); first = false; }
                else b.Encapsulate(lp);
            }
        }
        var box = t.gameObject.AddComponent<BoxCollider>();
        box.center = b.center;
        box.size = b.size;

        var br = t.gameObject.AddComponent<ShuraBreakable>();
        br.maxHealth = health;
        br.debrisCount = debris;
        br.breakEffectPrefab = breakEffect;
        br.breakEffectScale = fxScale;
        return t;
    }

    static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 8; i++)
            yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
    }

    /// <summary>酒樽（菰樽）：稻草包覆的木桶＋黑色麻繩＋紅色招牌</summary>
    static Transform SakeBarrel(Transform parent)
    {
        if (TryProp("SakeBarrel", parent, R(0.9f, 1.05f), out var real)) return real;
        var t = NewBreakable(parent, "SakeBarrel");
        Cyl(t, "Body", new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(0.9f, 0.5f, 0.9f), mStraw);
        Cyl(t, "Lid", new Vector3(0f, 1.0f, 0f), Vector3.zero, new Vector3(0.8f, 0.03f, 0.8f), mCrate);
        for (int k = 0; k < 3; k++)
            Cyl(t, "Rope", new Vector3(0f, 0.18f + k * 0.32f, 0f), Vector3.zero, new Vector3(0.93f, 0.03f, 0.93f), mRope);
        Box(t, "Label", new Vector3(0f, 0.55f, 0.455f), Vector3.zero, new Vector3(0.38f, 0.42f, 0.02f), Chance(0.5f) ? mFlagRed : mFlagBlack);
        return FinishBreakable(t, 40f, 10, 0.8f);
    }

    /// <summary>木箱：箱體＋深色框條</summary>
    static Transform Crate(Transform parent, float s)
    {
        if (TryProp("Crate", parent, s, out var real)) return real;
        var t = NewBreakable(parent, "Crate");
        Box(t, "Body", new Vector3(0f, 0.5f * s, 0f), Vector3.zero, new Vector3(s, s, s), mCrate);
        for (int side = -1; side <= 1; side += 2)
        {
            Box(t, "FrameX", new Vector3(0f, 0.5f * s, side * s * 0.505f), Vector3.zero, new Vector3(s * 1.02f, s * 0.12f, 0.03f), mWood);
            Box(t, "FrameZ", new Vector3(side * s * 0.505f, 0.5f * s, 0f), Vector3.zero, new Vector3(0.03f, s * 0.12f, s * 1.02f), mWood);
            Box(t, "Edge", new Vector3(side * s * 0.47f, 0.5f * s, s * 0.505f), Vector3.zero, new Vector3(s * 0.08f, s, 0.03f), mWood);
        }
        return FinishBreakable(t, 30f, 9, 0.8f);
    }

    /// <summary>米俵：橫躺的稻草捆＋三道繩</summary>
    static Transform RiceBale(Transform parent)
    {
        if (TryProp("RiceBale", parent, R(0.9f, 1.05f), out var real)) return real;
        var t = NewBreakable(parent, "RiceBale");
        Prim(PrimitiveType.Capsule, t, "Body", new Vector3(0f, 0.38f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.75f, 0.6f, 0.75f), mStraw);
        for (int k = -1; k <= 1; k++)
            Cyl(t, "Rope", new Vector3(k * 0.35f, 0.38f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.78f, 0.025f, 0.78f), mRope);
        return FinishBreakable(t, 25f, 8, 0.7f);
    }

    /// <summary>陶甕：圓胖的甕身＋窄口</summary>
    static Transform Pot(Transform parent, float s)
    {
        if (TryProp("ClayPot", parent, s * 0.75f, out var real)) return real;
        var t = NewBreakable(parent, "ClayPot");
        Sphere(t, "Body", new Vector3(0f, 0.32f * s, 0f), new Vector3(0.6f * s, 0.64f * s, 0.6f * s), mClay, shadows: true);
        Cyl(t, "Neck", new Vector3(0f, 0.66f * s, 0f), Vector3.zero, new Vector3(0.3f * s, 0.06f * s, 0.3f * s), mClay);
        Cyl(t, "Rim", new Vector3(0f, 0.72f * s, 0f), Vector3.zero, new Vector3(0.36f * s, 0.02f * s, 0.36f * s), mRope);
        return FinishBreakable(t, 10f, 7, 0.5f);
    }

    /// <summary>競技場的環形碰撞牆、大門標成不可破壞；場景加一個顯示紅色標籤的元件。</summary>
    static void SetupImmortalWalls(GameObject room, BossGate gate)
    {
        var parents = new HashSet<Transform>();
        foreach (var col in room.GetComponentsInChildren<Collider>(true))
            if (col.name.StartsWith("Wall_") && col.transform.parent != null) parents.Add(col.transform.parent);
        if (gate != null) parents.Add(gate.transform);
        foreach (var t in parents)
            if (t.GetComponent<ImmortalObject>() == null) t.gameObject.AddComponent<ImmortalObject>();

        var go = new GameObject("ImmortalObjectFeedback");
        var fb = go.AddComponent<ImmortalObjectFeedback>();
        fb.labelMaterial = mImmortalLabel;
        Debug.Log($"[Shura] 不可破壞標記：{parents.Count} 組（環形牆 / 大門）");
    }

    static void EnablePostProcessing(GameObject rig)
    {
        var cam = rig.GetComponentInChildren<Camera>(true);
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(data);
    }

    // =====================================================================
    //  地面（中間挖空，不擋到會下沉的競技場地板）
    // =====================================================================

    static void BuildGround(Transform env, Vector3 c, float r)
    {
        const int seg = 128;
        float r0 = r + 0.2f, r1 = 320f;
        var verts = new Vector3[seg * 2];
        var uvs = new Vector2[seg * 2];
        var tris = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts[i * 2] = d * r0;
            verts[i * 2 + 1] = d * r1;
            uvs[i * 2] = new Vector2(d.x * r0, d.z * r0) / 8f;
            uvs[i * 2 + 1] = new Vector2(d.x * r1, d.z * r1) / 8f;
            int n = (i + 1) % seg;
            int t = i * 6;
            tris[t] = i * 2; tris[t + 1] = i * 2 + 1; tris[t + 2] = n * 2 + 1;
            tris[t + 3] = i * 2; tris[t + 4] = n * 2 + 1; tris[t + 5] = n * 2;
        }
        var mesh = new Mesh { name = "Mesh_ShuraGround", vertices = verts, uv = uvs, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string meshPath = SceneAssetDir + "/Mesh_ShuraGround.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject("Ground");
        go.transform.SetParent(env, false);
        go.transform.position = c + Vector3.down * 0.05f;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mGround;
        MarkStatic(go);
    }

    // =====================================================================
    //  築地塀：環形白牆＋黑瓦屋頂，血跡、焦痕、插在牆上的箭
    // =====================================================================

    static bool InGateGap(BossGate gate, Vector3 p, float halfWidth)
    {
        if (gate == null) return false;
        var lp = gate.transform.InverseTransformPoint(p);
        return Mathf.Abs(lp.x) < halfWidth && Mathf.Abs(lp.z) < 6f;
    }

    static void BuildWallRing(Transform env, Vector3 c, float r, BossGate gate)
    {
        var ring = Group("WallRing_Tsuijibei", env);
        const int N = 72;
        float ringR = r + 1f;
        float w = 2f * Mathf.PI * ringR / N * 1.04f;
        for (int k = 0; k < N; k++)
        {
            float a = k * Mathf.PI * 2f / N;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = c + dir * ringR;
            if (InGateGap(gate, p, 6.2f)) continue;

            var s = Group($"Wall_{k:00}", ring);
            s.position = p;
            s.rotation = Quaternion.LookRotation(-dir); // +Z 朝向競技場中心

            Box(s, "Base", new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(w, 1f, 2f), mStone);
            Box(s, "Plaster", new Vector3(0f, 3.55f, 0f), Vector3.zero, new Vector3(w, 5.1f, 1.6f), mPlaster);
            Box(s, "Band", new Vector3(0f, 5.75f, 0f), Vector3.zero, new Vector3(w + 0.01f, 0.35f, 1.7f), mWood);
            Box(s, "Post", new Vector3(w * 0.5f, 3.3f, 0f), Vector3.zero, new Vector3(0.35f, 6.6f, 1.75f), mWood);
            Box(s, "Eave", new Vector3(0f, 6.25f, 0f), Vector3.zero, new Vector3(w + 0.02f, 0.22f, 3f), mRoof);
            Box(s, "Ridge", new Vector3(0f, 6.35f, 0f), new Vector3(45f, 0f, 0f), new Vector3(w + 0.02f, 1f, 1f), mRoof);
            Box(s, "RidgeCap", new Vector3(0f, 7.05f, 0f), Vector3.zero, new Vector3(w + 0.03f, 0.18f, 0.3f), mRoofCap);

            // 內側牆面的血跡、焦痕
            if (Chance(0.55f)) Splatter(s, w, mBlood, true);
            if (Chance(0.35f)) Splatter(s, w, mSoot, false);
            if (Chance(0.2f)) Splatter(s, w, mBlood, true);

            // 插在牆上的箭
            int arrows = rng.Next(0, 4);
            for (int i = 0; i < arrows; i++)
                Arrow(s, new Vector3(R(-w * 0.45f, w * 0.45f), R(1.4f, 5.4f), 0.8f));
        }
    }

    static void Splatter(Transform wall, float w, Material m, bool drips)
    {
        var g = Group("Stain", wall);
        g.localPosition = new Vector3(R(-w * 0.35f, w * 0.35f), R(1.3f, 4.6f), 0.805f);
        float sx = R(0.4f, 1.4f), sy = R(0.3f, 1.1f);
        Box(g, "Main", Vector3.zero, new Vector3(0f, 0f, R(-30f, 30f)), new Vector3(sx, sy, 0.01f), m);
        for (int i = 0; i < 3; i++)
            Box(g, "Blob", new Vector3(R(-sx, sx) * 0.7f, R(-sy, sy) * 0.7f, 0.004f), new Vector3(0f, 0f, R(0f, 90f)),
                new Vector3(R(0.12f, 0.35f), R(0.12f, 0.35f), 0.01f), m);
        if (!drips) return;
        int n = rng.Next(2, 5);
        for (int i = 0; i < n; i++)
        {
            float len = R(0.4f, 1.6f);
            Box(g, "Drip", new Vector3(R(-sx, sx) * 0.4f, -sy * 0.3f - len * 0.5f, 0.003f), Vector3.zero,
                new Vector3(R(0.04f, 0.09f), len, 0.01f), m);
        }
    }

    static void Arrow(Transform wall, Vector3 localHit)
    {
        var g = Group("Arrow", wall);
        g.localPosition = localHit;
        g.localRotation = Quaternion.Euler(R(-25f, 15f), R(-25f, 25f), R(0f, 360f));
        Cyl(g, "Shaft", new Vector3(0f, 0f, 0.35f), new Vector3(90f, 0f, 0f), new Vector3(0.035f, 0.4f, 0.035f), mWood);
        var feather = Chance(0.5f) ? mFeatherW : mFeatherR;
        Box(g, "FletchA", new Vector3(0f, 0f, 0.66f), Vector3.zero, new Vector3(0.13f, 0.008f, 0.18f), feather);
        Box(g, "FletchB", new Vector3(0f, 0f, 0.66f), Vector3.zero, new Vector3(0.008f, 0.13f, 0.18f), feather);
    }

    // =====================================================================
    //  紙燈籠（取代火把）
    // =====================================================================

    // 牆邊提燈：夜晚戰鬥場的主要照明
    const int LanternCount = 28;               // 一圈幾盞（大門附近會跳過）
    const float LanternLightRange = 22f;   // 從牆邊往場內照約 20m
    const float LanternLightIntensity = 9f;
    static readonly Color LanternLightColor = new Color(1f, 0.55f, 0.25f);

    static void BuildLanterns(Transform env, Vector3 c, float r, BossGate gate)
    {
        var root = Group("PaperLanterns", env);
        for (int i = 0; i < LanternCount; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / LanternCount;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = c + dir * (r + 0.2f);
            if (InGateGap(gate, p, 8f)) continue;

            var g = Group($"Lantern_{i:00}", root);
            g.position = p;
            g.rotation = Quaternion.LookRotation(-dir);
            Box(g, "Bracket", new Vector3(0f, 5.4f, 0.3f), Vector3.zero, new Vector3(0.08f, 0.08f, 0.6f), mWood);
            Cyl(g, "Cord", new Vector3(0f, 5.2f, 0.55f), Vector3.zero, new Vector3(0.02f, 0.2f, 0.02f), mLacquer);
            Sphere(g, "Paper", new Vector3(0f, 4.55f, 0.55f), new Vector3(0.7f, 0.95f, 0.7f), mLantern, shadows: false);
            Cyl(g, "CapTop", new Vector3(0f, 5.0f, 0.55f), Vector3.zero, new Vector3(0.42f, 0.05f, 0.42f), mLacquer);
            Cyl(g, "CapBottom", new Vector3(0f, 4.1f, 0.55f), Vector3.zero, new Vector3(0.42f, 0.05f, 0.42f), mLacquer);

            // 每盞都打光：光源放在燈籠前下方，往場內照亮地板（不投影，控制效能）
            PointLight(g, new Vector3(0f, 4.2f, 1.4f), LanternLightColor, LanternLightIntensity, LanternLightRange);
            var f = g.gameObject.AddComponent<TorchFlicker>();
            f.amount = 0.1f;
            f.speed = 2.5f;
        }
    }

    // =====================================================================
    //  大門上方的門樓（屋頂＋朱柱）
    // =====================================================================

    static void BuildGateHouse(Transform env, BossGate gate)
    {
        var g = Group("GateHouse", env);
        g.SetPositionAndRotation(gate.transform.position, gate.transform.rotation);
        for (int side = -1; side <= 1; side += 2)
        {
            Cyl(g, "Pillar", new Vector3(side * 4.4f, 3.8f, -0.5f), Vector3.zero, new Vector3(0.8f, 3.8f, 0.8f), mVermilion);
            Cyl(g, "PillarBase", new Vector3(side * 4.4f, 0.3f, -0.5f), Vector3.zero, new Vector3(1.0f, 0.3f, 1.0f), mLacquer);
        }
        Box(g, "TieBeam", new Vector3(0f, 6.2f, -0.5f), Vector3.zero, new Vector3(9.6f, 0.45f, 0.7f), mVermilion);
        Box(g, "Lintel", new Vector3(0f, 6.9f, -0.5f), Vector3.zero, new Vector3(10.8f, 0.6f, 1.1f), mLacquer);
        Box(g, "Eave", new Vector3(0f, 7.45f, -0.5f), Vector3.zero, new Vector3(13f, 0.3f, 4.6f), mRoof);
        Box(g, "Roof2", new Vector3(0f, 7.75f, -0.5f), Vector3.zero, new Vector3(12f, 0.35f, 3.4f), mRoof);
        Box(g, "Ridge", new Vector3(0f, 7.9f, -0.5f), new Vector3(45f, 0f, 0f), new Vector3(11.6f, 1.7f, 1.7f), mRoof);
        Box(g, "RidgeCap", new Vector3(0f, 9.1f, -0.5f), Vector3.zero, new Vector3(12f, 0.25f, 0.4f), mRoofCap);
        Box(g, "Plaque", new Vector3(0f, 6.9f, 0.1f), Vector3.zero, new Vector3(2.2f, 1.1f, 0.12f), mLacquer);
        Box(g, "PlaqueRim", new Vector3(0f, 6.9f, 0.08f), Vector3.zero, new Vector3(2.4f, 1.3f, 0.1f), mGold);
    }

    // =====================================================================
    //  幟旗（のぼり）：牆外一圈，從牆頂探出來，有些歪斜
    // =====================================================================

    static void BuildBanners(Transform env, Vector3 c, float r, BossGate gate)
    {
        var root = Group("Nobori", env);
        const int N = 30;
        var flagMats = new[] { mFlagRed, mFlagBlack, mFlagWhite };
        var monMats = new[] { mFlagWhite, mFlagRed, mFlagBlack };
        for (int i = 0; i < N; i++)
        {
            float a = (i + R(-0.3f, 0.3f)) * Mathf.PI * 2f / N;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = c + dir * (r + R(3f, 4.5f));
            if (InGateGap(gate, p, 9f)) continue;

            var g = Group($"Nobori_{i:00}", root);
            g.position = p;
            float tilt = Chance(0.15f) ? R(14f, 26f) : R(-6f, 6f);
            g.rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(R(-5f, 5f), R(-40f, 40f), tilt);

            float H = R(10f, 12.5f), L = R(3.8f, 5.4f);
            int ci = rng.Next(3);
            Cyl(g, "Pole", new Vector3(0f, H * 0.5f, 0f), Vector3.zero, new Vector3(0.13f, H * 0.5f, 0.13f), mWood);
            Box(g, "Crossbar", new Vector3(0.55f, H - 0.25f, 0f), Vector3.zero, new Vector3(1.15f, 0.06f, 0.06f), mWood);
            Box(g, "Flag", new Vector3(0.6f, H - 0.3f - L * 0.5f, 0f), new Vector3(0f, 0f, R(-2f, 2f)), new Vector3(1.0f, L, 0.03f), flagMats[ci]);
            // 家紋（簡化成圓）
            Cyl(g, "MonA", new Vector3(0.6f, H - 1.4f, 0.02f), new Vector3(90f, 0f, 0f), new Vector3(0.62f, 0.006f, 0.62f), monMats[ci]);
            Cyl(g, "MonB", new Vector3(0.6f, H - 1.4f, -0.02f), new Vector3(90f, 0f, 0f), new Vector3(0.62f, 0.006f, 0.62f), monMats[ci]);
        }
    }

    // =====================================================================
    //  櫓（瞭望台）＋篝火：牆外四個斜角
    // =====================================================================

    static void BuildYagura(Transform env, Vector3 c, float r)
    {
        var root = Group("Yagura", env);
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            var g = Group($"Yagura_{i}", root);
            g.position = c + dir * (r + 4.5f);
            g.rotation = Quaternion.LookRotation(-dir);

            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Cyl(g, "Leg", new Vector3(x * 1.9f, 5.2f, z * 1.9f), Vector3.zero, new Vector3(0.4f, 5.2f, 0.4f), mWood);
                    Cyl(g, "UpperPost", new Vector3(x * 2.2f, 12f, z * 2.2f), Vector3.zero, new Vector3(0.22f, 1.6f, 0.22f), mWood);
                }
            for (int side = -1; side <= 1; side += 2)
            {
                Box(g, "BraceX", new Vector3(0f, 5f, side * 1.9f), new Vector3(0f, 0f, 62f), new Vector3(8.2f, 0.18f, 0.18f), mWood);
                Box(g, "BraceZ", new Vector3(side * 1.9f, 5f, 0f), new Vector3(62f, 0f, 0f), new Vector3(0.18f, 0.18f, 8.2f), mWood);
            }
            Box(g, "Platform", new Vector3(0f, 10.3f, 0f), Vector3.zero, new Vector3(5.4f, 0.4f, 5.4f), mWood);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(g, "RailX", new Vector3(0f, 11.1f, side * 2.6f), Vector3.zero, new Vector3(5.4f, 0.9f, 0.12f), mWood);
                Box(g, "RailZ", new Vector3(side * 2.6f, 11.1f, 0f), Vector3.zero, new Vector3(0.12f, 0.9f, 5.4f), mWood);
            }
            Box(g, "Eave", new Vector3(0f, 13.6f, 0f), Vector3.zero, new Vector3(7.2f, 0.3f, 7.2f), mRoof);
            Box(g, "Roof2", new Vector3(0f, 13.95f, 0f), Vector3.zero, new Vector3(5.6f, 0.4f, 5.6f), mRoof);
            Box(g, "Roof3", new Vector3(0f, 14.35f, 0f), Vector3.zero, new Vector3(3.8f, 0.45f, 3.8f), mRoof);
            Box(g, "Roof4", new Vector3(0f, 14.75f, 0f), Vector3.zero, new Vector3(1.8f, 0.45f, 1.8f), mRoofCap);

            // 篝火
            var fire = Group("Kagaribi", g);
            fire.localPosition = new Vector3(0f, 10.5f, 0f);
            Box(fire, "Basket", new Vector3(0f, 0.35f, 0f), new Vector3(0f, 45f, 0f), new Vector3(1.1f, 0.6f, 1.1f), mIron);
            var flame = Sphere(fire, "Flame", new Vector3(0f, 0.85f, 0f), new Vector3(0.9f, 0.7f, 0.9f), mFireCore, shadows: false);
            GameObjectUtility.SetStaticEditorFlags(flame, 0); // TorchFlicker 會縮放它，不能靜態合批
            PointLight(fire, new Vector3(0f, 1.6f, 0f), new Color(1f, 0.5f, 0.2f), 6f, 24f);
            MakeFire(fire, new Vector3(0f, 0.7f, 0f), 0.9f, smoke: true);
            var f = fire.gameObject.AddComponent<TorchFlicker>();
            f.amount = 0.35f;
            f.speed = 7f;
        }
    }

    // =====================================================================
    //  鳥居：大門外一座、Boss 身後（北邊）一座大的
    // =====================================================================

    static void BuildToriis(Transform env, Vector3 c, float r, BossGate gate)
    {
        var root = Group("Torii", env);
        if (gate != null)
            Torii(root, "Torii_Entrance", gate.transform.TransformPoint(new Vector3(0f, 0f, -14f)), gate.transform.rotation, 1.2f, 0f);
        Torii(root, "Torii_North", c + Vector3.forward * (r + 13f), Quaternion.identity, 1.8f, 3.5f);
    }

    static void Torii(Transform parent, string name, Vector3 pos, Quaternion rot, float s, float lean)
    {
        var g = Group(name, parent);
        g.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 0f, lean));
        g.localScale = Vector3.one * s;
        for (int side = -1; side <= 1; side += 2)
        {
            Cyl(g, "Hashira", new Vector3(side * 3.2f, 4.6f, 0f), new Vector3(0f, 0f, side * 2f), new Vector3(0.75f, 4.6f, 0.75f), mVermilion);
            Cyl(g, "Kamaki", new Vector3(side * 3.2f, 0.4f, 0f), Vector3.zero, new Vector3(0.95f, 0.4f, 0.95f), mLacquer);
            Box(g, "KasagiEnd", new Vector3(side * 5.35f, 9.45f, 0f), new Vector3(0f, 0f, side * 12f), new Vector3(1.6f, 0.5f, 0.85f), mLacquer);
        }
        Box(g, "Nuki", new Vector3(0f, 7.2f, 0f), Vector3.zero, new Vector3(8.6f, 0.55f, 0.4f), mVermilion);
        Box(g, "Shimaki", new Vector3(0f, 8.75f, 0f), Vector3.zero, new Vector3(9.4f, 0.5f, 0.65f), mVermilion);
        Box(g, "Kasagi", new Vector3(0f, 9.25f, 0f), Vector3.zero, new Vector3(9.4f, 0.55f, 0.85f), mLacquer);
        Box(g, "Gakuzuka", new Vector3(0f, 7.95f, 0f), Vector3.zero, new Vector3(0.45f, 1.1f, 0.35f), mVermilion);
        Box(g, "Gaku", new Vector3(0f, 7.95f, 0f), Vector3.zero, new Vector3(1.0f, 1.25f, 0.5f), mLacquer);
    }

    // =====================================================================
    //  燃燒中的寺院（遠景）
    // =====================================================================

    static List<Vector4> BuildTemples(Transform env, Vector3 c, float r)
    {
        var root = Group("Temples", env);
        var used = new List<Vector4>(); // xyz = 位置, w = 半徑（給杉林避開）
        float[] angles = { 42f, 78f, 112f, 146f, 214f, 248f, 284f, 318f };
        for (int i = 0; i < angles.Length; i++)
        {
            float a = (angles[i] + R(-8f, 8f)) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = c + dir * (r + R(24f, 36f));
            bool burning = i % 2 == 0;
            float w = R(14f, 22f), d = R(10f, 14f);
            Temple(root, $"Temple_{i}", p, Quaternion.LookRotation(-dir), w, d, burning, tall: i % 3 == 1);
            used.Add(new Vector4(p.x, p.y, p.z, Mathf.Max(w, d) * 0.8f + 4f));
        }
        return used;
    }

    static void Temple(Transform parent, string name, Vector3 pos, Quaternion rot, float w, float d, bool burning, bool tall)
    {
        var g = Group(name, parent);
        g.SetPositionAndRotation(pos, rot);
        var body = burning ? mCharred : mWood;
        Box(g, "Kidan", new Vector3(0f, 0.75f, 0f), Vector3.zero, new Vector3(w + 2f, 1.5f, d + 2f), mStone);
        Box(g, "Body", new Vector3(0f, 4.5f, 0f), Vector3.zero, new Vector3(w, 6f, d), body);
        Box(g, "WallPanel", new Vector3(0f, 4.2f, d * 0.5f + 0.02f), Vector3.zero, new Vector3(w - 2f, 3.2f, 0.05f), burning ? mSoot : mPlaster);

        float y = 7.6f;
        if (tall)
        {
            Box(g, "Eave1", new Vector3(0f, y, 0f), Vector3.zero, new Vector3(w + 5f, 0.4f, d + 5f), mRoof);
            Box(g, "Body2", new Vector3(0f, y + 2.6f, 0f), Vector3.zero, new Vector3(w - 3f, 4.8f, d - 2.5f), body);
            y += 5.2f;
        }

        var roof = Group("Roof", g);
        roof.localPosition = new Vector3(0f, y, 0f);
        if (burning) roof.localRotation = Quaternion.Euler(R(-4f, 4f), 0f, R(6f, 13f) * (Chance(0.5f) ? 1f : -1f)); // 塌了一邊
        Box(roof, "Eave", Vector3.zero, Vector3.zero, new Vector3(w + 5f, 0.4f, d + 5f), mRoof);
        Box(roof, "Roof2", new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(w + 3.4f, 0.5f, d + 3.4f), mRoof);
        Box(roof, "Roof3", new Vector3(0f, 1.05f, 0f), Vector3.zero, new Vector3(w + 1.6f, 0.6f, d + 1.6f), mRoof);
        Box(roof, "Ridge", new Vector3(0f, 1.4f, 0f), new Vector3(45f, 0f, 0f), new Vector3(w + 1f, 3.2f, 3.2f), mRoof);
        Box(roof, "RidgeCap", new Vector3(0f, 3.65f, 0f), Vector3.zero, new Vector3(w + 1.4f, 0.35f, 0.6f), mRoofCap);
        for (int side = -1; side <= 1; side += 2)
            Box(roof, "Shibi", new Vector3(side * (w * 0.5f + 0.6f), 4.1f, 0f), new Vector3(0f, 0f, side * -15f), new Vector3(0.5f, 1.2f, 0.7f), mGold);

        if (!burning) return;
        int fires = rng.Next(2, 4);
        for (int i = 0; i < fires; i++)
            MakeFire(g, new Vector3(R(-w * 0.4f, w * 0.4f), y + R(0f, 2f), R(-d * 0.3f, d * 0.3f)), R(1.8f, 2.8f), smoke: true);
        var lightGroup = Group("FireLight", g);
        lightGroup.localPosition = new Vector3(0f, y + 2f, 0f);
        PointLight(lightGroup, Vector3.zero, new Color(1f, 0.45f, 0.15f), 7f, 38f);
        var f = lightGroup.gameObject.AddComponent<TorchFlicker>();
        f.amount = 0.3f;
        f.speed = 4f;
    }

    // =====================================================================
    //  五重塔（北偏西，赤月旁邊的剪影，頂部在燒）
    // =====================================================================

    static Vector4 BuildPagoda(Transform env, Vector3 c, float r)
    {
        float a = -22f * Mathf.Deg2Rad;
        var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        Vector3 pos = c + dir * (r + 58f);
        var g = Group("Pagoda_Gojunoto", env);
        g.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, 20f, 0f));
        g.localScale = Vector3.one * 1.25f;

        Box(g, "Kidan", new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(12f, 1.2f, 12f), mStone);
        float y = 1.2f;
        for (int i = 0; i < 5; i++)
        {
            float bw = 8f - i * 1.05f;
            Box(g, $"Body{i}", new Vector3(0f, y + 2.1f, 0f), Vector3.zero, new Vector3(bw, 4.2f, bw), i >= 3 ? mCharred : mWood);
            Box(g, $"Eave{i}a", new Vector3(0f, y + 4.35f, 0f), Vector3.zero, new Vector3(bw + 4.4f, 0.3f, bw + 4.4f), mRoof);
            Box(g, $"Eave{i}b", new Vector3(0f, y + 4.65f, 0f), Vector3.zero, new Vector3(bw + 2.8f, 0.35f, bw + 2.8f), mRoof);
            Box(g, $"Eave{i}c", new Vector3(0f, y + 5.0f, 0f), Vector3.zero, new Vector3(bw + 1.2f, 0.4f, bw + 1.2f), mRoof);
            y += 5.2f;
        }
        Cyl(g, "Sorin", new Vector3(0f, y + 4.5f, 0f), Vector3.zero, new Vector3(0.35f, 4.5f, 0.35f), mGold);
        for (int k = 0; k < 9; k++)
            Cyl(g, "Ring", new Vector3(0f, y + 1.5f + k * 0.75f, 0f), Vector3.zero, new Vector3(1.3f - k * 0.05f, 0.06f, 1.3f - k * 0.05f), mGold);
        Sphere(g, "Hoju", new Vector3(0f, y + 9.3f, 0f), Vector3.one * 0.7f, mGold, shadows: true);

        MakeFire(g, new Vector3(1.5f, 1.2f + 5.2f * 3f + 1f, 0f), 2.6f, smoke: true);
        MakeFire(g, new Vector3(-1.8f, 1.2f + 5.2f * 4f + 0.5f, 1f), 2.2f, smoke: true);
        return new Vector4(pos.x, pos.y, pos.z, 14f);
    }

    // =====================================================================
    //  杉林（遠景剪影，部分是燒焦的枯木）
    // =====================================================================

    static void BuildForest(Transform env, Vector3 c, float r, List<Vector4> occupied)
    {
        var root = Group("CedarForest", env);
        int placed = 0, tries = 0;
        while (placed < 90 && tries++ < 1000)
        {
            float ang = R(0f, 360f);
            float dist = r + R(9f, 80f);
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 180f)) < 14f && dist < r + 30f) continue; // 入口參道留空
            float a = ang * Mathf.Deg2Rad;
            Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * dist;
            bool blocked = false;
            foreach (var o in occupied)
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(o.x, o.z)) < o.w) { blocked = true; break; }
            if (blocked) continue;

            var g = Group($"Cedar_{placed:00}", root);
            g.position = p;
            g.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            float h = R(16f, 27f);
            if (Chance(0.2f))
            {
                g.rotation *= Quaternion.Euler(R(-12f, 12f), 0f, R(-12f, 12f));
                Cyl(g, "DeadTrunk", new Vector3(0f, h * 0.35f, 0f), Vector3.zero, new Vector3(0.7f, h * 0.35f, 0.7f), mCharred);
                Box(g, "Branch", new Vector3(0.6f, h * 0.45f, 0f), new Vector3(0f, 0f, -50f), new Vector3(0.25f, 3f, 0.25f), mCharred);
            }
            else
            {
                Cyl(g, "Trunk", new Vector3(0f, h * 0.25f, 0f), Vector3.zero, new Vector3(0.6f, h * 0.25f, 0.6f), mWood);
                // 杉樹：四層圓錐往上疊、越上越窄
                for (int k = 0; k < 4; k++)
                {
                    float baseY = h * (0.22f + k * 0.17f);
                    float th = h * 0.4f * (1f - k * 0.15f);
                    float rad = h * (0.2f - k * 0.04f) * R(0.85f, 1.15f);
                    MeshObj(g, $"Crown{k}", coneMesh, new Vector3(0f, baseY, 0f), new Vector3(0f, R(0f, 360f), 0f),
                        new Vector3(rad * 2f, th, rad * 2f), mCedar);
                }
            }
            placed++;
        }
    }

    // =====================================================================
    //  全場粒子：櫻花瓣、灰燼、火星
    // =====================================================================

    static void BuildAtmosphereVfx(Transform env, Vector3 c, float r)
    {
        var root = Group("AtmosphereVFX", env);
        float span = r * 2.6f;

        // 櫻花瓣：從上空飄落，被風往 +X 吹
        {
            var ps = NewPS("SakuraPetals", root, c + Vector3.up * 24f);
            var main = ps.main;
            main.duration = 10f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.7f, 0.78f), new Color(1f, 0.9f, 0.92f));
            main.gravityModifier = 0.025f;
            main.maxParticles = 450;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 28f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(span, 1f, span);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.6f, -0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.9f; noise.frequency = 0.25f; noise.scrollSpeed = 0.3f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.08f, 0.85f, 1f);
            SetRenderer(ps, pPetal);
        }

        // 灰燼：深灰色小碎屑
        {
            var ps = NewPS("Ash", root, c + Vector3.up * 20f);
            var main = ps.main;
            main.duration = 10f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 15f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.18f, 0.16f, 0.16f), new Color(0.4f, 0.37f, 0.36f));
            main.gravityModifier = 0.02f;
            main.maxParticles = 350;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 26f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(span, 1f, span);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.5f, -0.2f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 1.2f; noise.frequency = 0.4f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.1f, 0.85f, 1f);
            SetRenderer(ps, pAsh);
        }

        // 火星：從地面往上飄
        {
            var ps = NewPS("Embers", root, c + Vector3.up * 0.6f);
            var main = ps.main;
            main.duration = 8f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.65f, 0.25f), new Color(1f, 0.3f, 0.06f));
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 55f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(span, 0.5f, span);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
            vel.y = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.7f; noise.frequency = 0.6f; noise.scrollSpeed = 0.5f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(Color.white, 0.1f, 0.6f, 1f);
            SetRenderer(ps, pAdd);
        }
    }

    /// <summary>可重用的火焰（火焰＋火星＋可選的黑煙）。scale = 1 時約 1.5m 高。</summary>
    static void MakeFire(Transform parent, Vector3 localPos, float s, bool smoke)
    {
        var g = Group("Fire", parent);
        g.localPosition = localPos;

        var ps = NewPS("Flames", g, g.position);
        var main = ps.main;
        main.duration = 1f; main.loop = true; main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * s, 2.6f * s);
        main.startSize = new ParticleSystem.MinMaxCurve(0.9f * s, 1.8f * s);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 26f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.5f * s;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.3f),
                    new GradientColorKey(new Color(0.75f, 0.12f, 0.04f), 0.7f), new GradientColorKey(new Color(0.3f, 0.04f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.2f)));
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.4f * s; noise.frequency = 1.2f;
        SetRenderer(ps, pAdd);

        var sp = NewPS("Sparks", g, g.position);
        var sm = sp.main;
        sm.duration = 1f; sm.loop = true; sm.prewarm = true;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * s, 4f * s);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.7f, 0.3f), new Color(1f, 0.35f, 0.08f));
        sm.gravityModifier = -0.15f;
        sm.maxParticles = 40;
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        var sem = sp.emission; sem.rateOverTime = 10f;
        var ssh = sp.shape; ssh.shapeType = ParticleSystemShapeType.Cone; ssh.angle = 25f; ssh.radius = 0.6f * s;
        ssh.rotation = new Vector3(-90f, 0f, 0f);
        var sn = sp.noise; sn.enabled = true; sn.strength = 1f; sn.frequency = 0.8f;
        var scol = sp.colorOverLifetime; scol.enabled = true; scol.color = FadeInOut(Color.white, 0.05f, 0.6f, 1f);
        SetRenderer(sp, pAdd);

        if (!smoke) return;
        var sk = NewPS("Smoke", g, g.position + Vector3.up * 0.8f * s);
        var km = sk.main;
        km.duration = 2f; km.loop = true; km.prewarm = true;
        km.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        km.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * s, 2.2f * s);
        km.startSize = new ParticleSystem.MinMaxCurve(2.2f * s, 3.6f * s);
        km.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        km.startColor = new Color(0.09f, 0.07f, 0.07f, 1f);
        km.maxParticles = 50;
        km.simulationSpace = ParticleSystemSimulationSpace.World;
        var kem = sk.emission; kem.rateOverTime = 5f;
        var ksh = sk.shape; ksh.shapeType = ParticleSystemShapeType.Cone; ksh.angle = 10f; ksh.radius = 0.6f * s;
        ksh.rotation = new Vector3(-90f, 0f, 0f);
        var kv = sk.velocityOverLifetime; kv.enabled = true; kv.space = ParticleSystemSimulationSpace.World;
        kv.x = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        kv.y = new ParticleSystem.MinMaxCurve(0f, 0.3f);
        kv.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var kc = sk.colorOverLifetime; kc.enabled = true; kc.color = FadeInOut(Color.white, 0.15f, 0.5f, 0.55f);
        var ks = sk.sizeOverLifetime; ks.enabled = true;
        ks.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.2f)));
        var kr = sk.rotationOverLifetime; kr.enabled = true; kr.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        SetRenderer(sk, pSmoke);
    }

    // =====================================================================
    //  光線、赤月天空、紅霧、後製
    // =====================================================================

    static void BuildLighting(Vector3 c, SkeletonCentipedeBossAI bossAI)
    {
        var lighting = new GameObject("Lighting").transform;

        // 月光：方向光反方向就是程序天空的「太陽圓盤」= 赤月，位在北方（Boss 身後）偏東
        var moonGo = new GameObject("Moonlight (Blood Moon)");
        moonGo.transform.SetParent(lighting, false);
        moonGo.transform.rotation = Quaternion.Euler(20f, 196f, 0f);
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.72f, 0.78f, 0.95f);  // 夜晚：清冷的藍白月光，提燈的暖光才明顯
        moon.intensity = 0.5f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.75f;

        // 天空：自製赤月 Skybox，月亮方向 = 月光的反方向
        var sky = GetOrCreateMat("M_Shura_Skybox", "Shura/BloodMoonSky");
        var moonDir = -moonGo.transform.forward;
        sky.SetVector("_MoonDir", new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
        // 夜空配色：藍黑色的天頂與地平線、白黃色的圓月、星空
        sky.SetColor("_ZenithColor", new Color(0.004f, 0.007f, 0.02f));
        sky.SetColor("_HorizonColor", new Color(0.03f, 0.05f, 0.1f));
        sky.SetColor("_GroundColor", new Color(0.01f, 0.012f, 0.02f));
        sky.SetFloat("_HorizonFalloff", 0.35f);
        sky.SetColor("_MoonColor", new Color(2.6f, 2.35f, 1.6f));   // HDR：白黃色，配 Bloom 會發光
        sky.SetFloat("_MoonSize", 0.05f);
        sky.SetFloat("_MoonMottle", 0.3f);
        sky.SetColor("_HaloColor", new Color(0.55f, 0.5f, 0.35f));
        sky.SetFloat("_HaloIntensity", 0.45f);
        sky.SetColor("_StarColor", new Color(1.4f, 1.3f, 1.5f));
        sky.SetFloat("_StarDensity", 0.06f);
        sky.SetColor("_CloudColor", new Color(0.012f, 0.016f, 0.03f));
        sky.SetFloat("_CloudOpacity", 0.7f);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.sun = moon;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.1f, 0.13f, 0.24f);
        RenderSettings.ambientEquatorColor = new Color(0.06f, 0.08f, 0.14f);
        RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.03f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.025f, 0.04f, 0.08f); // 接近夜空地平線的藍黑色，遠景剪影才會融進去
        RenderSettings.fogDensity = 0.012f;
        DynamicGI.UpdateEnvironment();

        // 三階段（Boss 血量 < 20%）時天色轉暗紅；夜晚的數值就是上面這組
        var skyPhase = lighting.gameObject.AddComponent<ShuraSkyPhase>();
        skyPhase.boss = bossAI;
        skyPhase.moonLight = moon;

        // 後製 Volume
        string profilePath = SceneAssetDir + "/VP_ShuraArena.asset";
        AssetDatabase.DeleteAsset(profilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, profilePath);

        var bloom = AddVolumeComponent<Bloom>(profile);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(1.3f);
        bloom.scatter.Override(0.72f);
        bloom.tint.Override(new Color(1f, 0.95f, 0.85f));

        var vignette = AddVolumeComponent<Vignette>(profile);
        vignette.intensity.Override(0.38f);
        vignette.smoothness.Override(0.45f);
        vignette.color.Override(new Color(0f, 0.01f, 0.04f));

        var grade = AddVolumeComponent<ColorAdjustments>(profile);
        grade.postExposure.Override(2.5f);
        grade.contrast.Override(18f);
        grade.saturation.Override(-8f);
        grade.colorFilter.Override(new Color(0.96f, 0.98f, 1f));

        var tone = AddVolumeComponent<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);

        AssetDatabase.SaveAssets();

        var volGo = new GameObject("Global Volume (Shura)");
        volGo.transform.SetParent(lighting, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;
        vol.sharedProfile = profile;
    }

    static T AddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var comp = profile.Add<T>(true);
        comp.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(comp, profile);
        return comp;
    }

    // =====================================================================
    //  材質 / 貼圖
    // =====================================================================

    static void CreateMaterials()
    {
        mPlaster = Lit("M_Shura_Plaster", new Color(0.66f, 0.61f, 0.54f), 0.08f);
        mStone = Lit("M_Shura_Stone", new Color(0.3f, 0.28f, 0.28f), 0.12f);
        mWood = Lit("M_Shura_WoodDark", new Color(0.16f, 0.1f, 0.07f), 0.2f);
        mCharred = Lit("M_Shura_Charred", new Color(0.05f, 0.04f, 0.035f), 0.05f);
        mRoof = Lit("M_Shura_Kawara", new Color(0.12f, 0.12f, 0.14f), 0.35f);
        mRoofCap = Lit("M_Shura_KawaraCap", new Color(0.07f, 0.07f, 0.08f), 0.4f);
        mVermilion = Lit("M_Shura_Vermilion", new Color(0.75f, 0.14f, 0.06f), 0.35f);
        mLacquer = Lit("M_Shura_BlackLacquer", new Color(0.04f, 0.03f, 0.03f), 0.65f);
        mBlood = Lit("M_Shura_Blood", new Color(0.22f, 0.01f, 0.01f), 0.75f);
        mSoot = Lit("M_Shura_Soot", new Color(0.03f, 0.03f, 0.03f), 0f);
        mFlagRed = Lit("M_Shura_FlagCrimson", new Color(0.55f, 0.04f, 0.04f), 0.1f);
        mFlagBlack = Lit("M_Shura_FlagBlack", new Color(0.05f, 0.04f, 0.04f), 0.1f);
        mFlagWhite = Lit("M_Shura_FlagWhite", new Color(0.8f, 0.76f, 0.68f), 0.1f);
        mGold = Lit("M_Shura_Gold", new Color(0.6f, 0.45f, 0.15f), 0.6f, metallic: 0.8f);
        mCedar = Lit("M_Shura_Cedar", new Color(0.05f, 0.09f, 0.06f), 0.05f);
        mGround = Lit("M_Shura_Ground", new Color(0.08f, 0.055f, 0.045f), 0.05f);
        mIron = Lit("M_Shura_Iron", new Color(0.08f, 0.08f, 0.08f), 0.4f, metallic: 0.7f);
        mFeatherW = Lit("M_Shura_FeatherWhite", new Color(0.85f, 0.82f, 0.78f), 0.1f);
        mFeatherR = Lit("M_Shura_FeatherRed", new Color(0.6f, 0.05f, 0.05f), 0.1f);
        mLantern = Lit("M_Shura_PaperLantern", new Color(0.9f, 0.25f, 0.08f), 0.2f, emission: new Color(1f, 0.45f, 0.15f) * 6f);
        mFireCore = Lit("M_Shura_FireCore", new Color(1f, 0.5f, 0.1f), 0f, emission: new Color(1f, 0.45f, 0.1f) * 4f);

        var soft = MakeTexture("T_Shura_SoftDot", 64, (u, v) =>
        {
            float d = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01(1f - d);
            return new Color(1f, 1f, 1f, a * a);
        });
        var smoke = MakeTexture("T_Shura_Smoke", 128, (u, v) =>
        {
            float d = Mathf.Sqrt(u * u + v * v);
            float n = Mathf.PerlinNoise(u * 2.5f + 10f, v * 2.5f + 10f) * 0.6f + Mathf.PerlinNoise(u * 6f + 3f, v * 6f + 7f) * 0.4f;
            float a = Mathf.Clamp01(1f - d * (1.1f - 0.5f * n));
            return new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, a) * (0.55f + 0.45f * n));
        });
        var petal = MakeTexture("T_Shura_Petal", 64, (u, v) =>
        {
            // 橢圓花瓣，頂端有一個小缺口
            float e = (u / 0.55f) * (u / 0.55f) + (v / 0.95f) * (v / 0.95f);
            float a = Mathf.Clamp01((1f - e) * 6f);
            float notch = Mathf.Clamp01((Mathf.Sqrt(u * u + (v - 1f) * (v - 1f)) - 0.22f) * 12f);
            float shade = Mathf.Lerp(0.85f, 1f, (v + 1f) * 0.5f);
            return new Color(shade, shade, shade, a * notch);
        });

        pAdd = ParticleMat("M_Shura_ParticleAdditive", soft, additive: true);
        pSmoke = ParticleMat("M_Shura_ParticleSmoke", smoke, additive: false);
        pPetal = ParticleMat("M_Shura_ParticlePetal", petal, additive: false);
        pAsh = ParticleMat("M_Shura_ParticleAsh", soft, additive: false);

        // 可破壞道具（佔位用的日式造型）
        mStraw = Lit("M_Shura_Straw", new Color(0.72f, 0.63f, 0.42f), 0.05f);
        mRope = Lit("M_Shura_Rope", new Color(0.18f, 0.12f, 0.08f), 0.05f);
        mClay = Lit("M_Shura_Clay", new Color(0.36f, 0.2f, 0.12f), 0.35f);
        mCrate = Lit("M_Shura_CrateWood", new Color(0.42f, 0.29f, 0.17f), 0.15f);

        // 不可破壞物件的紅色六角標籤
        mImmortalLabel = LabelMaterial();
    }

    static Material GetOrCreateMat(string name, string shaderName)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(shaderName);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (shader != null && mat.shader != shader) mat.shader = shader;
        return mat;
    }

    static Material Lit(string name, Color color, float smoothness, float metallic = 0f, Color? emission = null)
    {
        var mat = GetOrCreateMat(name, "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material CopyTinted(Material src, string name, Color tint, float smoothness)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(src);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.CopyPropertiesFromMaterial(src);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material ParticleMat(string name, Texture2D tex, bool additive)
    {
        var mat = GetOrCreateMat(name, "Universal Render Pipeline/Particles/Unlit");
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>紅色六角標籤（左右兩端是尖角、內有蜂巢紋、雙層亮邊），512×128。</summary>
    static Material LabelMaterial()
    {
        const int W = 512, H = 128;
        string path = $"{VfxDir}/T_Shura_ImmortalLabel.png";
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        float halfW = W * 0.5f - 3f, halfH = H * 0.5f - 3f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float px = Mathf.Abs(x + 0.5f - W * 0.5f), py = Mathf.Abs(y + 0.5f - H * 0.5f);
                // 到邊緣的距離（上下平邊 / 左右斜邊）
                float e = Mathf.Min(halfH - py, (halfW - px - py) * 0.7071f);
                if (e <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                float v = (y + 0.5f) / H;
                Color fill = Color.Lerp(new Color(0.45f, 0.02f, 0.05f), new Color(0.82f, 0.08f, 0.1f), v);
                // 蜂巢紋
                float hx = (x + 0.5f) / 15f, hy = (y + 0.5f) / 15f;
                float line = HexLine(hx, hy);
                fill = Color.Lerp(fill, new Color(1f, 0.35f, 0.35f), line * 0.22f);
                float a = 0.86f;
                // 外框（亮）＋內框（細）
                if (e < 3f) { fill = new Color(1f, 0.5f, 0.5f); a = 1f; }
                else if (e > 6f && e < 7.5f) { fill = Color.Lerp(fill, new Color(1f, 0.55f, 0.55f), 0.7f); a = 0.95f; }
                a *= Mathf.Clamp01(e); // 邊緣反鋸齒
                tex.SetPixel(x, y, new Color(fill.r, fill.g, fill.b, a));
            }
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;
        ti.SaveAndReimport();

        var mat = GetOrCreateMat("M_Shura_ImmortalLabel", "Universal Render Pipeline/Unlit");
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent + 50;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>蜂巢格線強度（0~1）</summary>
    static float HexLine(float x, float y)
    {
        const float rx = 1f, ry = 1.732f;
        Vector2 a = new Vector2(Mod(x, rx) - rx * 0.5f, Mod(y, ry) - ry * 0.5f);
        Vector2 b = new Vector2(Mod(x - rx * 0.5f, rx) - rx * 0.5f, Mod(y - ry * 0.5f, ry) - ry * 0.5f);
        Vector2 g = a.sqrMagnitude < b.sqrMagnitude ? a : b;
        float d = Mathf.Max(Mathf.Abs(g.x) * 0.5f + Mathf.Abs(g.y) * 0.866f, Mathf.Abs(g.x));
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.44f, 0.5f, d));
    }

    static float Mod(float x, float m) => x - m * Mathf.Floor(x / m);

    static Texture2D MakeTexture(string name, int size, System.Func<float, float, Color> f)
    {
        string path = $"{VfxDir}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                tex.SetPixel(x, y, f(u, v));
            }
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =====================================================================
    //  物件小工具（全部沒有碰撞體）
    // =====================================================================

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material m, bool shadows = true)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
        MarkStatic(go);
        return go;
    }

    static GameObject Box(Transform p, string n, Vector3 pos, Vector3 euler, Vector3 scale, Material m) =>
        Prim(PrimitiveType.Cube, p, n, pos, euler, scale, m);

    /// <summary>scale.y = 半高（Unity 圓柱高度 2）</summary>
    static GameObject Cyl(Transform p, string n, Vector3 pos, Vector3 euler, Vector3 scale, Material m) =>
        Prim(PrimitiveType.Cylinder, p, n, pos, euler, scale, m);

    static GameObject Sphere(Transform p, string n, Vector3 pos, Vector3 scale, Material m, bool shadows) =>
        Prim(PrimitiveType.Sphere, p, n, pos, Vector3.zero, scale, m, shadows);

    static GameObject MeshObj(Transform parent, string name, Mesh mesh, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        MarkStatic(go);
        return go;
    }

    /// <summary>底面半徑 0.5、高 1 的圓錐（底在 y=0），存成 asset 讓場景共用。</summary>
    static Mesh CreateConeMesh()
    {
        const int seg = 14;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            var p0 = new Vector3(Mathf.Sin(a0) * 0.5f, 0f, Mathf.Cos(a0) * 0.5f);
            var p1 = new Vector3(Mathf.Sin(a1) * 0.5f, 0f, Mathf.Cos(a1) * 0.5f);
            int b = verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(Vector3.up);          // 側面（法線朝外）
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            b = verts.Count;
            verts.Add(p0); verts.Add(Vector3.zero); verts.Add(p1);        // 底面（法線朝下）
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
        }
        var mesh = new Mesh { name = "Mesh_ShuraCone" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string path = SceneAssetDir + "/Mesh_ShuraCone.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static void MarkStatic(GameObject go) =>
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

    static Light PointLight(Transform parent, Vector3 localPos, Color color, float intensity, float range)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    static ParticleSystem NewPS(string name, Transform parent, Vector3 worldPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        return ps;
    }

    static void SetRenderer(ParticleSystem ps, Material m)
    {
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = m;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
        pr.maxParticleSize = 2f;
    }

    static Gradient FadeInOut(Color c, float inT, float outStart, float peak)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inT), new GradientAlphaKey(peak, outStart), new GradientAlphaKey(0f, 1f) });
        return g;
    }
}
#endif
