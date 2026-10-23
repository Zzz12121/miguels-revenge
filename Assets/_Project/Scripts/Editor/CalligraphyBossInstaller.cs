#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class CalligraphyBossInstaller
{
    const string Root="Assets/_Project/VFX/Calligraphy/Materials/";
    const string Presentation="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Presentation.prefab";
    const string Battle="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab";
    const string Clone="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_InkClone.prefab";
    const string Range="Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity";
    static Material Material(string name,float mode,Color color)
    {
        string path=Root+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Project/VFX/CalligraphyFluid"));AssetDatabase.CreateAsset(m,path);}
        m.SetFloat("_Mode",mode);m.SetFloat("_Opacity",1);m.SetColor("_BaseColor",color);EditorUtility.SetDirty(m);return m;
    }
    [MenuItem("VFX/Calligraphy/Install Boss Battle Test")]
    public static void Menu()=>Debug.Log(Install());
    public static string Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before installation.");
        var range=SceneManager.GetSceneByPath(Range);
        if(range.isLoaded && range.isDirty)throw new InvalidOperationException("Range has unsaved changes. Save it before installation.");
        var ribbon=Material("M_InkFluidRibbon",0,new Color(.012f,.018f,.022f));
        var sculpture=Material("M_InkFluidSculpture",1,new Color(.012f,.018f,.022f));
        var paper=Material("M_InkPaper",2,new Color(.92f,.91f,.84f));
        var yin=Material("M_InkYinYang",3,Color.white);
        var landscape=Material("M_InkMountainWash",4,new Color(.35f,.36f,.34f));
        var suspended=Material("M_InkSuspendedWash",5,new Color(.003f,.005f,.007f));
        var visage=Material("M_InkSkyVisage",6,Color.white);
        GameObject CloneSource()=>UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Presentation));
        var clone=CloneSource();clone.name="CalligraphyInkClone";
        try
        {
            var display=clone.GetComponent<CalligraphyInkPresentation>();if(display){display.enabled=false;display.echoInterval=.9f;display.inkSize=.7f;display.ambientRate=12;display.maxEchoes=2;}
            foreach(var target in clone.GetComponents<CalligraphyPracticeTarget>())UnityEngine.Object.DestroyImmediate(target);
            clone.GetComponent<CalligraphyBrushPresentation>().showcase=false;
            PrefabUtility.SaveAsPrefabAsset(clone,Clone);
        }
        finally{UnityEngine.Object.DestroyImmediate(clone);}
        var root=CloneSource();root.name="CalligraphyElder_Battle";
        try
        {
            var display=root.GetComponent<CalligraphyInkPresentation>();if(display)display.enabled=false;
            foreach(var target in root.GetComponents<CalligraphyPracticeTarget>())UnityEngine.Object.DestroyImmediate(target);
            var combat=root.AddComponent<CalligraphyBossCombat>();combat.brush=root.GetComponent<CalligraphyBrushPresentation>();combat.brush.showcase=false;
            combat.cycleDemonstration=false;
            combat.cloneTemplate=AssetDatabase.LoadAssetAtPath<GameObject>(Clone);
            var pool=new GameObject("Bounded ink field");pool.transform.SetParent(root.transform,false);
            combat.ink=pool.AddComponent<CalligraphyInkField>();combat.ink.ribbonMaterial=ribbon;combat.ink.sculptureMaterial=sculpture;combat.ink.paperMaterial=paper;combat.ink.yinYangMaterial=yin;
            combat.ink.landscapeMaterial=landscape;combat.ink.environmentShader=Shader.Find("Project/VFX/CalligraphyEnvironment");
            if(display){combat.ink.originalInkMaterial=display.inkMaterial;combat.ink.originalEchoMaterial=display.echoMaterial;}
            combat.ink.suspendedWashMaterial=suspended;
            combat.ink.skyVisageMaterial=visage;
            combat.ink.yinYangPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Piloto Studio/Super Realistic FX Bundle 02/Realistic Ying Yang/Ying Yang Rune.prefab");
            root.tag="Enemy";
            if(!root.GetComponent<VoidRendTargetable>())root.AddComponent<VoidRendTargetable>();
            var tether=root.GetComponent<TetherInteractable>()??root.AddComponent<TetherInteractable>();tether.mode=TetherInteractable.TetherMode.Pullable;
            PrefabUtility.SaveAsPrefabAsset(root,Battle);
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        if(!range.isLoaded)range=EditorSceneManager.OpenScene(Range,OpenSceneMode.Additive);
        var harness=range.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CalligraphyBossRangeTest>(true)).FirstOrDefault();
        if(!harness)
        {
            var go=new GameObject("CalligraphyBoss_TestControls");SceneManager.MoveGameObjectToScene(go,range);harness=go.AddComponent<CalligraphyBossRangeTest>();
        }
        harness.bossPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Battle);
        harness.existingDisplay=range.GetRootGameObjects().FirstOrDefault(g=>g.name=="CalligraphyElder_Showcase");
        EditorUtility.SetDirty(harness);EditorSceneManager.MarkSceneDirty(range);EditorSceneManager.SaveScene(range);AssetDatabase.SaveAssets();
        return "Installed Battle/InkClone prefabs, seven materials and range test button. Dock NPC unchanged.";
    }
}
#endif
