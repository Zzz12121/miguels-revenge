#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class VoidRendDemoBuilder
{
    const string Root = "Assets/_Project/VFX/VoidRend";
    const string MaterialDir = Root + "/Materials";
    const string PrefabDir = Root + "/Prefabs";
    const string ScenePath = "Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity";
    const string PrefabPath = PrefabDir + "/PF_VoidRendSkill.prefab";
    const string ProfilePath = Root + "/VP_VoidRend.asset";
    const string BocchiPath = "Assets/_Project/ThirdParty/bocchi/source/bo pi 1.fbx";

    [MenuItem("VFX/Void Rend/Build Bocchi Demo Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(Root);
        EnsureFolder(MaterialDir);
        EnsureFolder(PrefabDir);

        Material slash = GetOrCreateVfxMaterial("M_VoidRend_Slash", "Project/VFX/VoidRendAdditive", new Color(0.65f, 0.88f, 1f, 1f), 9f, 1f);
        Material afterimage = GetOrCreateVfxMaterial("M_VoidRend_Afterimage", "Project/VFX/VoidRendAdditive", new Color(0.24f, 0.86f, 1f, 0.62f), 4.2f, 0f);
        Material particles = GetOrCreateVfxMaterial("M_VoidRend_Particles", "Project/VFX/VoidRendAdditive", new Color(0.48f, 0.95f, 1f, 1f), 8.5f, 1f);
        Material riftCore = GetOrCreateVfxMaterial("M_VoidRend_Core", "Project/VFX/VoidRendCore", new Color(0.002f, 0.004f, 0.014f, 0.98f), 1f, 1f);
        Material ground = GetOrCreateLitMaterial("M_VoidRend_Ground", new Color(0.012f, 0.02f, 0.04f), new Color(0.01f, 0.08f, 0.12f), 0.78f, 0.65f);
        Material architecture = GetOrCreateLitMaterial("M_VoidRend_Architecture", new Color(0.025f, 0.035f, 0.06f), new Color(0.015f, 0.04f, 0.08f), 0.42f, 0.5f);
        Material dummyMat = GetOrCreateLitMaterial("M_VoidRend_Dummy", new Color(0.1f, 0.13f, 0.18f), new Color(0.12f, 0.62f, 0.88f), 0.15f, 0.35f);
        Material swordMat = GetOrCreateLitMaterial("M_VoidRend_Sword", new Color(0.52f, 0.8f, 0.9f), new Color(0.25f, 1.2f, 1.8f), 0.8f, 0.92f);

        CreateControllerPrefab(slash, riftCore, afterimage, particles);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Camera camera = CreateCamera();
        CreateLighting();
        CreatePostProcessing();
        CreateStage(ground, architecture);

        GameObject caster = InstantiateBocchi();
        caster.name = "Bocchi_VoidRendCaster";
        caster.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1.8f), Quaternion.identity);
        AttachKatana(caster.transform, swordMat);

        GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        dummy.name = "VoidRend_DummyTarget";
        dummy.transform.position = new Vector3(0f, 1f, 2.25f);
        dummy.transform.localScale = new Vector3(0.78f, 1.05f, 0.78f);
        dummy.GetComponent<MeshRenderer>().sharedMaterial = dummyMat;
        Object.DestroyImmediate(dummy.GetComponent<CapsuleCollider>());

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject controller = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        controller.name = "VoidRend_SkillController";
        VoidRendSkillDemo demo = controller.GetComponent<VoidRendSkillDemo>();
        demo.caster = caster.transform;
        demo.target = dummy.transform;
        demo.skillCamera = camera;
        demo.playOnStart = true;
        demo.loopPreview = true;
        demo.teleportCount = 6;
        demo.teleportRadius = 3.4f;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = controller;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"[VoidRend] Built demo scene: {ScenePath}. Enter Play Mode; Space replays the skill.");
    }

    static void CreateControllerPrefab(Material slash, Material core, Material afterimage, Material particles)
    {
        GameObject temp = new GameObject("PF_VoidRendSkill");
        VoidRendSkillDemo demo = temp.AddComponent<VoidRendSkillDemo>();
        demo.slashMaterial = slash;
        demo.riftCoreMaterial = core;
        demo.afterimageMaterial = afterimage;
        demo.particleMaterial = particles;
        demo.smokeMaterial = GetOrCreateVfxMaterial("M_VoidRend_Smoke", "Project/VFX/VoidRendSmoke", Color.white, 1, 0);
        demo.refractionMaterial = GetOrCreateVfxMaterial("M_VoidRend_Refraction", "Project/VFX/VoidRendRefraction", Color.white, 1, 0);
        demo.previewPipeline = CreatePreviewPipeline(demo.refractionMaterial);
        demo.effectRangeMultiplier = 4;
        demo.chargeOrbitSeconds = 3.5f;
        demo.playOnStart = false;
        demo.loopPreview = false;
        PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
        Object.DestroyImmediate(temp);
    }

    static Camera CreateCamera()
    {
        GameObject go = new GameObject("Main Camera") { tag = "MainCamera" };
        Camera camera = go.AddComponent<Camera>();
        go.AddComponent<AudioListener>();
        UniversalAdditionalCameraData data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        camera.allowHDR = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.004f, 0.008f, 0.02f);
        camera.nearClipPlane = 0.08f;
        camera.farClipPlane = 120f;
        camera.fieldOfView = 48f;
        go.transform.position = new Vector3(3.8f, 2.65f, -9.6f);
        go.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.15f, 0.75f) - go.transform.position, Vector3.up);
        return camera;
    }

    static void CreateLighting()
    {
        GameObject key = new GameObject("Key Light");
        Light keyLight = key.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.4f;
        keyLight.color = new Color(0.56f, 0.72f, 1f);
        key.transform.rotation = Quaternion.Euler(38f, -28f, 0f);

        GameObject rim = new GameObject("Cyan Rim Light");
        Light rimLight = rim.AddComponent<Light>();
        rimLight.type = LightType.Point;
        rimLight.range = 13f;
        rimLight.intensity = 10f;
        rimLight.color = new Color(0.05f, 0.52f, 1f);
        rim.transform.position = new Vector3(-3f, 4.2f, 2.8f);

        GameObject fill = new GameObject("Violet Fill Light");
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.range = 10f;
        fillLight.intensity = 7f;
        fillLight.color = new Color(0.45f, 0.12f, 1f);
        fill.transform.position = new Vector3(4f, 2.5f, -1f);
    }

    static void CreatePostProcessing()
    {
        VolumeProfile profile = RepairPostProcessingProfile();
        GameObject go = new GameObject("Global Volume");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.sharedProfile = profile;
    }

    public static VolumeProfile RepairPostProcessingProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        profile.components.RemoveAll(component => component == null);
        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.active = true;
        bloom.threshold.Override(0.72f);
        bloom.intensity.Override(2.4f);
        bloom.scatter.Override(0.78f);

        ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
        color.active = true;
        color.postExposure.Override(0.18f);
        color.contrast.Override(28f);
        color.saturation.Override(-10f);
        color.colorFilter.Override(new Color(0.78f, 0.9f, 1f));

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.active = true;
        vignette.color.Override(new Color(0.01f, 0.02f, 0.06f));
        vignette.intensity.Override(0.34f);
        vignette.smoothness.Override(0.72f);

        EditorUtility.SetDirty(profile);
        profile.components.RemoveAll(component => component == null);
        foreach (var component in profile.components)
        {
            if (!AssetDatabase.Contains(component)) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(component);
        }
        AssetDatabase.SaveAssets();

        return profile;
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) return component;
        return profile.Add<T>(true);
    }

    static void CreateStage(Material ground, Material architecture)
    {
        GameObject root = new GameObject("VoidRend_Stage");
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Reflective Dark Floor";
        floor.transform.SetParent(root.transform);
        floor.transform.localScale = new Vector3(8f, 1f, 8f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = ground;

        for (int i = -3; i <= 3; i++)
        {
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = $"Background Pillar {i + 4}";
            pillar.transform.SetParent(root.transform);
            pillar.transform.position = new Vector3(i * 7.5f, 4.1f, 24f + Mathf.Abs(i) * 0.3f);
            pillar.transform.localScale = new Vector3(0.38f, 4.2f + (i & 1) * 0.8f, 0.65f);
            pillar.GetComponent<MeshRenderer>().sharedMaterial = architecture;
        }

        // Side silhouettes add depth without crossing the central combat read.
        for (int i = 0; i < 8; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = $"Floating Architecture {i + 1}";
            shard.transform.SetParent(root.transform);
            float side = i < 4 ? -1f : 1f;
            int row = i % 4;
            shard.transform.position = new Vector3(side * (24f + row * 0.72f), 0.85f + (row % 3) * 0.8f, -0.5f + row * 2.1f);
            shard.transform.rotation = Quaternion.Euler(18f * i, 35f * i, 12f * i);
            shard.transform.localScale = new Vector3(0.18f, 0.62f, 1.15f);
            shard.GetComponent<MeshRenderer>().sharedMaterial = architecture;
        }
    }

    static GameObject InstantiateBocchi()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BocchiPath);
        if (model == null) throw new System.IO.FileNotFoundException("Bocchi model was not found", BocchiPath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        return instance != null ? instance : Object.Instantiate(model);
    }

    static void AttachKatana(Transform caster, Material material)
    {
        Transform hand = FindDeepChild(caster, "Bip001 R Hand");
        if (hand == null) return;

        GameObject root = new GameObject("VoidRend_KatanaSocket");
        root.transform.SetParent(hand, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade.name = "Energy Katana Blade";
        blade.transform.SetParent(root.transform, false);
        blade.transform.localPosition = new Vector3(-0.72f, 0f, 0f);
        blade.transform.localScale = new Vector3(1.38f, 0.035f, 0.065f);
        blade.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(blade.GetComponent<BoxCollider>());

        GameObject guard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        guard.name = "Katana Guard";
        guard.transform.SetParent(root.transform, false);
        guard.transform.localPosition = new Vector3(-0.035f, 0f, 0f);
        guard.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        guard.transform.localScale = new Vector3(0.12f, 0.025f, 0.12f);
        guard.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(guard.GetComponent<CapsuleCollider>());
        GameObject sheath = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sheath.name = "VoidRend_Sheath";
        sheath.transform.SetParent(caster, false);
        sheath.transform.localPosition = new Vector3(-.38f, 1.05f, -.12f);
        sheath.transform.localRotation = Quaternion.Euler(-35, 0, 15);
        sheath.transform.localScale = new Vector3(.07f, 1.15f, .08f);
        sheath.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/M_VoidRend_Architecture.mat");
        Object.DestroyImmediate(sheath.GetComponent<BoxCollider>());
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    static Material GetOrCreateVfxMaterial(string name, string shaderName, Color color, float intensity, float lineMask)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new System.InvalidOperationException($"Shader not found: {shaderName}");
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.shader = shader;

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Intensity")) material.SetFloat("_Intensity", intensity);
        if (material.HasProperty("_UseLineMask")) material.SetFloat("_UseLineMask", lineMask);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material GetOrCreateLitMaterial(string name, Color baseColor, Color emission, float metallic, float smoothness)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.shader = shader;

        material.SetColor("_BaseColor", baseColor);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent)) return;
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static UniversalRenderPipelineAsset CreatePreviewPipeline(Material refraction)
    {
        string pipelinePath = Root + "/VoidRend_Pipeline.asset";
        string rendererPath = Root + "/VoidRend_Renderer.asset";
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (!pipeline)
        {
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!source) throw new System.InvalidOperationException("Void Rend requires the active URP pipeline.");
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), pipelinePath);
            pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        }
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (!data)
        {
            AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset", rendererPath);
            data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        }
        FullScreenPassRendererFeature feature = null;
        foreach (var f in data.rendererFeatures) if (f is FullScreenPassRendererFeature full && f.name == "Void Rend Refraction") feature = full;
        if (!feature)
        {
            feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "Void Rend Refraction";
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
        }
        feature.passMaterial = refraction;
        feature.fetchColorBuffer = true;
        feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
        feature.Create(); feature.SetActive(true); EditorUtility.SetDirty(feature);
        data.SetDirty(); EditorUtility.SetDirty(data);
        var serialized = new SerializedObject(pipeline);
        var list = serialized.FindProperty("m_RendererDataList");
        list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = data;
        serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        pipeline.supportsHDR = true;
        EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssets();
        return pipeline;
    }
}
#endif
