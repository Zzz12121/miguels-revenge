#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 選單：VFX/Characters/Skeleton Centipede/Rebuild Sweep VFX
///
/// 把 Hovl Studio「Magic effects pack」(Magic Effects FREE) 裡用到的特效轉成 URP 版本，給百足駭骨的鐮刀、甩尾、遠程攻擊用：
///   1. 原廠材質是 Built-in 的 Particles/Standard Unlit，在 URP 會變粉紅色 → 另存 URP Particles/Unlit 版本
///      （貼圖、HDR 顏色、Soft Particles 照搬；原廠檔案完全不動）
///   2. 複製需要的 prefab 到 _Project/VFX/SkeletonCentipede/Prefabs，換上 URP 材質、關掉循環、改成跟著物件縮放
///   3. 在百足駭骨 prefab 上掛 SkeletonCentipedeSweepVfx（鐮刀 + 甩尾 + 衝刺）、SkeletonCentipedeRangedVfx（遠程）、
///      SkeletonCentipedeEncircleSkill（殘影圍殺）、SkeletonCentipedeBatGuardSkill（蝠群護體）、
///      SkeletonCentipedeBatSwarmSkill（蝠群襲擊）並指派特效 / 材質；另外建立骷髏蝙蝠 prefab
///
/// 可以重複執行。Bake Animations 也會自動呼叫這裡（素材不存在就略過特效）。
/// </summary>
public static class SkeletonCentipedeVfxInstaller
{
    const string HovlRoot = "Assets/Hovl Studio/Magic effects pack";
    const string OutRoot = "Assets/_Project/VFX/SkeletonCentipede";
    const string MatDir = OutRoot + "/Materials";
    const string PrefabDir = OutRoot + "/Prefabs";
    const string SweepImpact = PrefabDir + "/VFX_SkeletonCentipede_SweepImpact.prefab";
    const string FinisherSlam = PrefabDir + "/VFX_SkeletonCentipede_FinisherSlam.prefab";
    const string FinisherDebris = PrefabDir + "/VFX_SkeletonCentipede_FinisherDebris.prefab";
    const string TailDust = PrefabDir + "/VFX_SkeletonCentipede_TailDust.prefab";
    const string DashFinisher = PrefabDir + "/VFX_SkeletonCentipede_DashFinisher.prefab";
    const string RangedMuzzle = PrefabDir + "/VFX_SkeletonCentipede_RangedMuzzle.prefab";
    const string RangedExplosion = PrefabDir + "/VFX_SkeletonCentipede_RangedExplosion.prefab";
    const string HealAura = PrefabDir + "/VFX_SkeletonCentipede_BatHeal.prefab";
    const string BatHit = PrefabDir + "/VFX_SkeletonCentipede_BatHit.prefab";
    const string BatModelFolder = "Assets/_Project/Models/Boss/flying bat 3d model";
    const string BatPrefabPath = "Assets/_Project/Prefabs/Boss/PF_SkeletonBat.prefab";
    const string TrailSource = HovlRoot + "/Materials/Trail67.mat";
    const string GlowSource = HovlRoot + "/Materials/Point.mat";

    static readonly (string source, string target)[] Effects =
    {
        (HovlRoot + "/Prefabs/Slash effects/Stone slash.prefab", SweepImpact),
        (HovlRoot + "/Prefabs/AoE effects/Ground AOE explosion.prefab", FinisherSlam),
        (HovlRoot + "/Prefabs/Hits and explosions/Stones hit.prefab", FinisherDebris),
        (HovlRoot + "/Prefabs/Smoke effects/Dust puff.prefab", TailDust),
        (HovlRoot + "/Prefabs/AoE effects/Smoke AOE explosion.prefab", DashFinisher),
        (HovlRoot + "/Prefabs/Slash effects/Charge slash red.prefab", RangedMuzzle),
        (HovlRoot + "/Prefabs/AoE effects/Red energy explosion.prefab", RangedExplosion),
        (HovlRoot + "/Prefabs/Character auras/Healing.prefab", HealAura),
        (HovlRoot + "/Prefabs/Sparks/Sparks explode red.prefab", BatHit),
    };

    public static bool SourceAvailable => AssetDatabase.IsValidFolder(HovlRoot);

    [MenuItem("VFX/Characters/Skeleton Centipede/Rebuild Sweep VFX")]
    public static void RebuildMenu()
    {
        if (!SourceAvailable)
        {
            Debug.LogError($"找不到 {HovlRoot}，請先匯入 Magic Effects FREE。");
            return;
        }
        BuildAssets();
        // 重新烘一次，讓 prefab 掛上最新的特效設定
        SkeletonCentipedeAnimationBaker.BakeAll();
    }

    /// <summary>給 Baker 呼叫：在百足駭骨 prefab 的暫存實例上掛特效元件。素材不在就略過。</summary>
    public static void AttachTo(GameObject bossInstance)
    {
        if (!SourceAvailable)
        {
            Debug.LogWarning($"百足駭骨：找不到 {HovlRoot}，這次不掛鐮刀特效。");
            return;
        }
        if (Array.Exists(Effects, e => AssetDatabase.LoadAssetAtPath<GameObject>(e.target) == null)) BuildAssets();

        var vfx = bossInstance.GetComponent<SkeletonCentipedeSweepVfx>();
        if (vfx == null) vfx = bossInstance.AddComponent<SkeletonCentipedeSweepVfx>();
        vfx.trailMaterial = ConvertMaterial(AssetDatabase.LoadAssetAtPath<Material>(TrailSource), additive: true);
        vfx.sweepImpactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SweepImpact);
        vfx.finisherSlamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FinisherSlam);
        vfx.finisherDebrisPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FinisherDebris);
        vfx.tailDustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TailDust);
        vfx.dashFinisherPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DashFinisher);

        var ranged = bossInstance.GetComponent<SkeletonCentipedeRangedVfx>();
        if (ranged == null) ranged = bossInstance.AddComponent<SkeletonCentipedeRangedVfx>();
        ranged.glowMaterial = ConvertMaterial(AssetDatabase.LoadAssetAtPath<Material>(GlowSource), additive: true);
        ranged.trailMaterial = vfx.trailMaterial;
        ranged.muzzlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangedMuzzle);
        ranged.explosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangedExplosion);

        var encircle = bossInstance.GetComponent<SkeletonCentipedeEncircleSkill>();
        if (encircle == null) encircle = bossInstance.AddComponent<SkeletonCentipedeEncircleSkill>();
        encircle.afterimageMaterial = AfterimageMaterial();
        encircle.telegraphMaterial = vfx.trailMaterial;
        encircle.landingSlamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FinisherSlam);
        encircle.landingDebrisPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FinisherDebris);
        encircle.dustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TailDust);

        var batPrefab = BuildBatPrefab();
        var guard = bossInstance.GetComponent<SkeletonCentipedeBatGuardSkill>();
        if (guard == null) guard = bossInstance.AddComponent<SkeletonCentipedeBatGuardSkill>();
        guard.batPrefab = batPrefab;
        guard.healEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HealAura);
        guard.summonEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DashFinisher);

        var swarm = bossInstance.GetComponent<SkeletonCentipedeBatSwarmSkill>();
        if (swarm == null) swarm = bossInstance.AddComponent<SkeletonCentipedeBatSwarmSkill>();
        swarm.batPrefab = batPrefab;
        swarm.summonEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DashFinisher);
        swarm.batHitEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BatHit);
    }

    /// <summary>
    /// 骷髏蝙蝠 prefab：根物件掛 SkeletonBat，模型放在第一個子物件（SkeletonBat 會把直立的模型轉成飛行姿勢）。
    /// </summary>
    static GameObject BuildBatPrefab()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[] { BatModelFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning($"找不到蝙蝠模型（{BatModelFolder}），蝙蝠技能不會生出蝙蝠。");
            return null;
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
        var root = new GameObject("PF_SkeletonBat");
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = "Model";
            body.transform.SetParent(root.transform, false);
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = false;
            }
            root.AddComponent<SkeletonBat>();
            EnsureFolder(Path.GetDirectoryName(BatPrefabPath).Replace('\\', '/'));
            return PrefabUtility.SaveAsPrefabAsset(root, BatPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    /// <summary>殘影材質：URP Particles/Unlit 疊加、不寫深度、雙面，顏色由技能元件每格用 PropertyBlock 淡出。</summary>
    static Material AfterimageMaterial()
    {
        string path = MatDir + "/M_SkeletonCentipede_Afterimage.mat";
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            EnsureFolder(MatDir);
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else m.shader = shader;
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", 5f);
        m.SetFloat("_DstBlend", 1f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3000;
        m.SetColor("_BaseColor", new Color(2.2f, 0.12f, 0.08f, 0.55f));
        EditorUtility.SetDirty(m);
        return m;
    }

    static void BuildAssets()
    {
        EnsureFolder(MatDir);
        EnsureFolder(PrefabDir);
        foreach (var (source, target) in Effects)
            ConvertPrefab(source, target);
        AssetDatabase.SaveAssets();
        Debug.Log($"百足駭骨鐮刀特效：已轉成 URP 版本 → {OutRoot}");
    }

    static void ConvertPrefab(string source, string target)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
        {
            Debug.LogWarning($"找不到特效 {source}");
            return;
        }
        AssetDatabase.DeleteAsset(target);
        AssetDatabase.CopyAsset(source, target);

        var root = PrefabUtility.LoadPrefabContents(target);
        try
        {
            root.transform.localPosition = Vector3.zero;
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = ConvertMaterial(mats[i], additive: false);
                renderer.sharedMaterials = mats;
                if (renderer.trailMaterial != null) renderer.trailMaterial = ConvertMaterial(renderer.trailMaterial, additive: false);
            }
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false;               // 原廠是展示用的循環播放
                main.playOnAwake = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy; // 才會跟著 Boss 的大小縮放
            }
            PrefabUtility.SaveAsPrefabAsset(root, target);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static readonly Dictionary<string, Material> converted = new();

    static Material ConvertMaterial(Material src, bool additive)
    {
        if (src == null) return null;
        if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) return src;

        string path = $"{MatDir}/URP_Hovl_{src.name}{(additive ? "_Add" : "")}.mat";
        if (converted.TryGetValue(path, out var cached) && cached != null) return cached;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            EnsureFolder(MatDir);
            AssetDatabase.CreateAsset(m, path);
        }
        else m.shader = shader;

        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", 5f);
        m.SetFloat("_DstBlend", additive ? 1f : 10f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3000;

        if (src.HasProperty("_MainTex"))
        {
            m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
            m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
            m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex"));
        }
        m.SetColor("_BaseColor", src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);

        if (src.IsKeywordEnabled("_FADING_ON"))
        {
            float near = src.HasProperty("_SoftParticlesNearFadeDistance") ? src.GetFloat("_SoftParticlesNearFadeDistance") : 0f;
            float far = src.HasProperty("_SoftParticlesFarFadeDistance") ? src.GetFloat("_SoftParticlesFarFadeDistance") : 1f;
            m.SetFloat("_SoftParticlesEnabled", 1f);
            m.SetFloat("_SoftParticlesNearFadeDistance", near);
            m.SetFloat("_SoftParticlesFarFadeDistance", far);
            m.SetVector("_SoftParticleFadeParams", new Vector4(near, 1f / Mathf.Max(0.0001f, far - near), 0f, 0f));
            m.EnableKeyword("_SOFTPARTICLES_ON");
        }

        EditorUtility.SetDirty(m);
        converted[path] = m;
        return m;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
