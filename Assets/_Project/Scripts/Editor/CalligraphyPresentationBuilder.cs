#if UNITY_EDITOR
using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

public static class BuildCalligraphyPresentation
{
    [MenuItem("VFX/Calligraphy/Rebuild Elder Brush Showcase")]
    public static void BuildFromMenu()
    {
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Main());
    }
    const string Root="Assets/_Project/VFX/Calligraphy";
    const string Prefab="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Presentation.prefab";
    const string Scene="Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity";
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path)) return;
        string parent=path.Substring(0,path.LastIndexOf('/')); Folder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    static string Model(string folder)=>AssetDatabase.FindAssets("t:Model",new[]{"Assets/_Project/Models/Boss/"+folder}).Select(AssetDatabase.GUIDToAssetPath).First();
    static Material Material(string name,string shader)
    {
        string path=Root+"/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) {m=new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m,path);} return m;
    }
    static Material ModelMaterial(string model,string name)
    {
        string dir=Path.ChangeExtension(model,"fbm").Replace('\\','/');
        string diffuse=AssetDatabase.FindAssets("t:Texture",new[]{dir}).Select(AssetDatabase.GUIDToAssetPath).First(p=>Path.GetFileName(p).StartsWith("tripo_rgb"));
        var mat=Material(name,"Universal Render Pipeline/Lit"); mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse)); mat.SetFloat("_Smoothness",.28f);
        return mat;
    }
    public static string Main()
    {
        if(EditorApplication.isPlaying) throw new Exception("Stop Play before building");
        Folder(Root+"/Materials"); Folder("Assets/_Project/Prefabs/Boss"); Folder("Assets/_Project/Animations/CalligraphyElder");
        string elderPath=Model("CalligraphyElderBoss_Rigged_Source"),brushPath=Model("CalligraphyBoss_GiantBrush");
        var sourceClips=AssetDatabase.LoadAllAssetsAtPath(elderPath).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).OrderBy(c=>c.name).ToArray();
        string controllerPath="Assets/_Project/Animations/CalligraphyElder/AC_CalligraphyElder.controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if(!controller) controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var machine=controller.layers[0].stateMachine;
        var clips=new AnimationClip[sourceClips.Length];
        for(int i=0;i<sourceClips.Length;i++)
        {
            string name=sourceClips[i].name.Replace(".001","");
            string path="Assets/_Project/Animations/CalligraphyElder/"+name+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(!clip) {clip=Object.Instantiate(sourceClips[i]);clip.name=name;AssetDatabase.CreateAsset(clip,path);}
            // Keep the imported skeleton animation; strip only object-level root displacement.
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                if(binding.path=="" && binding.type==typeof(Transform)) AnimationUtility.SetEditorCurve(clip,binding,null);
            var settings=AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime=new[]{"idle","standing_relax","wait","look_around","walk","run"}.Contains(name);
            AnimationUtility.SetAnimationClipSettings(clip,settings); EditorUtility.SetDirty(clip); clips[i]=clip;
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name,new Vector3((i%6)*230,(i/6)*80)); state.motion=clip;
            if(name=="standing_relax") machine.defaultState=state;
        }
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(Scene);
        if(!scene.isLoaded) scene=EditorSceneManager.OpenScene(Scene,OpenSceneMode.Single);
        var previous=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="CalligraphyElder_Showcase");
        if(previous) Object.DestroyImmediate(previous);
        var root=new GameObject("CalligraphyElder_Showcase");
        var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(elderPath),root.transform); model.name="ElderVisual";
        var animator=model.GetComponent<Animator>(); if(!animator) animator=model.AddComponent<Animator>(); animator.runtimeAnimatorController=controller; animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var idle=clips.First(c=>c.name=="standing_relax"); idle.SampleAnimation(model,0);
        var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        var repairedRobe=AssetDatabase.LoadAssetAtPath<Mesh>(GarmentSkinningRepair.ElderMesh);
        foreach(var skin in skins) {skin.sharedMaterial=ModelMaterial(elderPath,"M_Elder");skin.updateWhenOffscreen=true;
            if(repairedRobe && skin.sharedMesh.vertexCount==repairedRobe.vertexCount) skin.sharedMesh=repairedRobe;}
        var baked=new Mesh(); skins[0].BakeMesh(baked);
        var points=baked.vertices.Select(v=>skins[0].transform.TransformPoint(v)).ToArray();
        float height=points.Max(v=>v.y)-points.Min(v=>v.y); float scale=2.25f/height;
        model.transform.localScale=Vector3.one*scale;
        model.transform.localPosition=Vector3.up*(-points.Min(v=>v.y)*scale);
        Object.DestroyImmediate(baked);
        var presentation=root.AddComponent<CalligraphyBrushPresentation>(); presentation.animator=animator;
        presentation.animationStates=clips.Select(c=>c.name).ToArray(); presentation.animationClips=clips; presentation.currentAnimation="standing_relax";
        Transform Find(string n)=>model.GetComponentsInChildren<Transform>().First(t=>t.name==n);
        presentation.rightUpper=Find("Right_UpperArm");presentation.rightLower=Find("Right_LowerArm");presentation.rightHand=Find("Right_Hand");
        presentation.leftUpper=Find("Left_UpperArm");presentation.leftLower=Find("Left_LowerArm");presentation.leftHand=Find("Left_Hand");
        var pivot=new GameObject("BrushGripPivot").transform; pivot.SetParent(root.transform,false); pivot.localPosition=new Vector3(0,1.3f,.48f);pivot.localRotation=Quaternion.Euler(-22,64,0);
        presentation.brushPivot=pivot;
        var brush=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(brushPath),pivot); brush.name="GiantBrush";
        var mesh=brush.GetComponent<MeshFilter>().sharedMesh; var vertices=mesh.vertices; var bounds=mesh.bounds;
        // The FBX shaft follows mesh Y. Find the bristle end by comparing its radial width.
        var lowVertices=vertices.Where(v=>v.y<bounds.min.y+bounds.size.y*.24f).ToArray();
        var highVertices=vertices.Where(v=>v.y>bounds.max.y-bounds.size.y*.24f).ToArray();
        float low=lowVertices.Max(v=>v.x)-lowVertices.Min(v=>v.x);
        float high=highVertices.Max(v=>v.x)-highVertices.Min(v=>v.x);
        float sign=high>low?1:-1;
        Quaternion align=Quaternion.FromToRotation(Vector3.up*sign,Vector3.forward);
        float brushScale=2.6f/bounds.size.y;
        Vector3 grip=bounds.center-Vector3.up*sign*bounds.size.y*.15f;
        brush.transform.localRotation=align; brush.transform.localScale=Vector3.one*brushScale;
        brush.transform.localPosition=-(align*grip)*brushScale;
        brush.GetComponent<Renderer>().sharedMaterial=ModelMaterial(brushPath,"M_GiantBrush");
        var tip=new GameObject("BrushInkTip").transform;tip.SetParent(pivot,false); tip.localPosition=Vector3.forward*(2.6f*.65f);presentation.brushTip=tip;
        var ink=root.AddComponent<CalligraphyInkPresentation>();ink.brush=presentation;ink.bodyMeshes=skins;ink.accessoryMeshes=brush.GetComponentsInChildren<MeshFilter>();
        ink.inkMaterial=Material("M_InkDiffusion","Project/VFX/CalligraphyInk");ink.echoMaterial=Material("M_InkDouble","Project/VFX/CalligraphyEcho");
        ink.enabled=true;
        var collider=root.AddComponent<CapsuleCollider>();collider.center=Vector3.up*1.1f;collider.height=2.2f;collider.radius=.42f;
        PrefabUtility.SaveAsPrefabAssetAndConnect(root,Prefab,InteractionMode.AutomatedAction);
        root.transform.SetPositionAndRotation(new Vector3(-7f,0,2.5f),Quaternion.Euler(0,155,0));
        presentation.showcase=true; PrefabUtility.RecordPrefabInstancePropertyModifications(presentation); PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        CalligraphySceneSetup.ConfigurePractice(root);
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return "Created elder + brush, "+clips.Length+" animations, ink pool and doubles. Model scale="+scale+", brush bristle sign="+sign+". Scene="+Scene;
    }
}
#endif
