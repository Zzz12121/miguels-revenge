#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 建立骷髏騎士小怪 (Skeleton Knight)
///
/// 把 Tripo 做的骷髏騎士（Models/SkeletonKnight/SkeletonKnight.fbx，Mixamo 骨架）做成小怪：
///   1. 模型設成 Humanoid（人形），才能直接用 SazenGames 骷髏的動畫（Skeleton_Enemy.controller）
///   2. 貼圖：4K 原檔匯入時限制成 2048；金屬度＋粗糙度合成 URP 用的 Metallic/Smoothness 貼圖
///   3. 以 Enemy_Grunt 為基礎做 Prefab Variant「Enemy_SkeletonKnight」：
///      AI、攻擊、血量全部沿用，只把外觀換成骷髏騎士（舊的 SkeletonModel 關掉）
///
/// 可以重複執行。Enemy_Grunt 本身不會被修改。
/// </summary>
public static class SkeletonKnightInstaller
{
    const string Dir = "Assets/_Project/Models/SkeletonKnight";
    const string FbxPath = Dir + "/SkeletonKnight.fbx";
    const string TexDir = Dir + "/Textures";
    const string MatPath = Dir + "/M_SkeletonKnight.mat";
    const string ControllerPath = "Assets/_Project/Models/Skeleton/Skeleton_Enemy.controller";
    const string GruntPath = "Assets/_Project/Prefabs/Enemy_Grunt.prefab";
    public const string PrefabPath = "Assets/_Project/Prefabs/Enemy_SkeletonKnight.prefab";
    const string ModelName = "SkeletonKnightModel";

    /// <summary>騎士的身高（公尺）。想調大小就改這裡再執行一次選單</summary>
    const float TargetHeight = 2.5f;

    [MenuItem("GeminiGrowUp/建立骷髏騎士小怪 (Skeleton Knight)")]
    public static void Menu()
    {
        var prefab = BuildPrefab();
        if (prefab != null) EditorUtility.DisplayDialog("完成", $"已建立 {PrefabPath}", "OK");
    }

    public static GameObject BuildPrefab()
    {
        if (!File.Exists(FbxPath))
        {
            Debug.LogError($"[SkeletonKnight] 找不到 {FbxPath}");
            return null;
        }
        var grunt = AssetDatabase.LoadAssetAtPath<GameObject>(GruntPath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (grunt == null || controller == null)
        {
            Debug.LogError("[SkeletonKnight] 需要 Enemy_Grunt.prefab 和 Skeleton_Enemy.controller（先執行「安裝骷髏小怪模型」）。");
            return null;
        }

        SetupModelImporter();
        var mat = BuildMaterial();
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);

        // ---------- Variant：以 Enemy_Grunt 為基礎 ----------
        var root = (GameObject)PrefabUtility.InstantiatePrefab(grunt);
        try
        {
            root.name = "Enemy_SkeletonKnight";
            var cc = root.GetComponent<CharacterController>();
            var health = root.GetComponent<EnemyHealth>();
            var visual = root.GetComponent<EnemyVisual>();
            if (visual == null) visual = root.AddComponent<EnemyVisual>();

            // 原本的 SazenGames 骷髏模型關掉
            var oldModel = root.transform.Find("SkeletonModel");
            if (oldModel != null) oldModel.gameObject.SetActive(false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = ModelName;
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }

            // 量模型高度，縮放到 TargetHeight，腳底對齊碰撞膠囊底部
            var b = RendererBounds(model);
            float s = TargetHeight / Mathf.Max(0.01f, b.size.y);
            model.transform.localScale = Vector3.one * s;
            b = RendererBounds(model);
            float feetLocalY = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
            float minLocal = root.transform.InverseTransformPoint(b.min).y;
            model.transform.localPosition += new Vector3(0f, feetLocalY - minLocal, 0f);

            if (cc != null)
            {
                cc.height = TargetHeight;
                cc.radius = 0.55f;
                cc.center = new Vector3(0f, feetLocalY + TargetHeight * 0.5f, 0f);
            }
            if (health != null) health.barHeightOffset = feetLocalY + TargetHeight + 0.35f;

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = AvatarFromFbx();
            animator.applyRootMotion = false;   // 移動由 AI 控制，動畫只負責姿勢
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            visual.animator = animator;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[SkeletonKnight] 已建立 {PrefabPath}（模型原始高度 {b.size.y / s:0.00}，縮放 {s:0.###}）");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void SetupModelImporter()
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        if (mi == null) return;
        bool dirty = false;
        if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; dirty = true; }
        if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
        if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
        if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
        if (dirty) mi.SaveAndReimport();
    }

    static Avatar AvatarFromFbx()
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            if (o is Avatar a) return a;
        return null;
    }

    static Material BuildMaterial()
    {
        var baseTex = ImportTexture($"{TexDir}/SkeletonKnight_basecolor.png", false);
        var normal = ImportTexture($"{TexDir}/SkeletonKnight_normal.png", true);
        var metalSmooth = BuildMetallicSmoothness();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", Color.white);
        if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
        if (metalSmooth != null)
        {
            mat.SetTexture("_MetallicGlossMap", metalSmooth);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f);    // 有貼圖時是倍率
        }
        else
        {
            mat.SetFloat("_Metallic", 0.3f);
            mat.SetFloat("_Smoothness", 0.35f);
        }
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D ImportTexture(string path, bool normalMap)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) return null;
        bool dirty = false;
        var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; dirty = true; }
        if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; dirty = true; }
        if (!normalMap && !ti.sRGBTexture) { ti.sRGBTexture = true; dirty = true; }
        if (dirty) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>金屬度（R）＋ 1-粗糙度（A）→ URP Lit 的 Metallic/Smoothness 貼圖（2048）。</summary>
    static Texture2D BuildMetallicSmoothness()
    {
        string outPath = $"{TexDir}/SkeletonKnight_MetallicSmoothness.png";
        string metalPath = $"{TexDir}/SkeletonKnight_metallic.png";
        string roughPath = $"{TexDir}/SkeletonKnight_roughness.png";
        if (!File.Exists(metalPath) || !File.Exists(roughPath)) return null;

        if (!File.Exists(outPath))
        {
            var metal = new Texture2D(2, 2);
            var rough = new Texture2D(2, 2);
            metal.LoadImage(File.ReadAllBytes(metalPath));
            rough.LoadImage(File.ReadAllBytes(roughPath));
            const int S = 2048;
            var m = metal.GetPixels32();
            var r = rough.GetPixels32();
            int mw = metal.width, rw = rough.width;
            var outPx = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int mx = x * mw / S, my = y * metal.height / S;
                    int rx = x * rw / S, ry = y * rough.height / S;
                    byte metallic = m[my * mw + mx].r;
                    byte smooth = (byte)(255 - r[ry * rw + rx].r);
                    outPx[y * S + x] = new Color32(metallic, metallic, metallic, smooth);
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx);
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(metal);
            Object.DestroyImmediate(rough);
            AssetDatabase.ImportAsset(outPath);
        }
        var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
        if (ti != null && ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    static Bounds RendererBounds(GameObject go)
    {
        bool has = false;
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }
}
#endif
