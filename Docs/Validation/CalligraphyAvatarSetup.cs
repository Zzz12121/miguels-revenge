using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Run outside Assets with Pipeline run_script; updates only the new ultimate's references.</summary>
public static class CalligraphyAvatarSetup
{
    const string Range="Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity";
    const string Prefab="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab";
    const string Materials="Assets/_Project/VFX/Calligraphy/Materials/";
    static Material Ensure(string name,string shader,bool sea)
    {
        string path=Materials+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material)return material;
        var found=Shader.Find(shader);if(!found)throw new InvalidOperationException("Missing shader "+shader);
        material=new Material(found){name=name};
        if(material.HasProperty("_Sea"))material.SetFloat("_Sea",sea?1:0);
        AssetDatabase.CreateAsset(material,path);return material;
    }
    public static string Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before installing assets.");
        var scene=SceneManager.GetSceneByPath(Range);
        if(scene.isLoaded && scene.isDirty)throw new InvalidOperationException("Range has unsaved edits. Save them first.");
        var avatar=Ensure("M_InkAvatarDetail","Project/VFX/CalligraphyAvatar",false);
        var sea=Ensure("M_InkAvatarSea","Project/VFX/CalligraphyInkDomain",true);
        var platform=Ensure("M_InkAvatarPlatform","Project/VFX/CalligraphyInkDomain",false);
        var pillar=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Piloto Studio/Super Realistic FX Bundle 02/Realistic Ying Yang/Extras/ARPG_White_FlamingPillar.prefab");
        if(!pillar)throw new InvalidOperationException("Missing trusted Flaming Pillar prefab.");
        void Configure(CalligraphyBossCombat boss)
        {
            if(boss.inkAvatarUltimate==null)boss.inkAvatarUltimate=new CalligraphyBossCombat.InkAvatarUltimateSettings();
            var s=boss.inkAvatarUltimate;s.avatar.material=avatar;s.sea.material=sea;s.platforms.material=platform;s.pillars.prefab=pillar;
            if(boss.skillRecovery==null || boss.skillRecovery.Length<10)Array.Resize(ref boss.skillRecovery,10);
            EditorUtility.SetDirty(boss);
        }
        var contents=PrefabUtility.LoadPrefabContents(Prefab);
        try{Configure(contents.GetComponent<CalligraphyBossCombat>());PrefabUtility.SaveAsPrefabAsset(contents,Prefab);}
        finally{PrefabUtility.UnloadPrefabContents(contents);}
        if(!scene.isLoaded)scene=EditorSceneManager.OpenScene(Range,OpenSceneMode.Additive);
        var bossInScene=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CalligraphyBossCombat>(true)).Single();
        Configure(bossInScene);PrefabUtility.RecordPrefabInstancePropertyModifications(bossInScene);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return "Installed skill 9 on saved range CalligraphyElder and reusable Battle prefab; three new materials; existing settings preserved.";
    }
    public static string TunePresentation()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        void Tune(CalligraphyBossCombat boss)
        {boss.inkAvatarUltimate.pillars.scale=1.8f;boss.inkAvatarUltimate.sweep.trajectoryWidth=4.5f;EditorUtility.SetDirty(boss);}
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try{Tune(root.GetComponent<CalligraphyBossCombat>());PrefabUtility.SaveAsPrefabAsset(root,Prefab);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        var scene=SceneManager.GetSceneByPath(Range);
        if(!scene.isLoaded || scene.isDirty)throw new InvalidOperationException("Open a saved range scene first.");
        var target=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CalligraphyBossCombat>(true)).Single();
        Tune(target);PrefabUtility.RecordPrefabInstancePropertyModifications(target);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return "Presentation tuning saved: pillar scale1.8 (larger than the elder, below the avatar face); wake width4.5.";
    }
}
