#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class InkboundTownBuilder
{
    static void CreatePrefabs()
    {
        foreach(var spec in new[]{new Vector3(8,3.4f,7),new Vector3(10,4,7.5f),new Vector3(9,6.5f,8),new Vector3(12,6.8f,8.5f)})
            housePrefabs.Add(BuildHouse(spec.x,spec.y,spec.z,housePrefabs.Count));
        willowPrefab=BuildTree("Willow",0);pinePrefab=BuildTree("OldPine",1);bambooPrefab=BuildTree("Bamboo",2);
        foreach(string name in new[]{"Merchant","VillagerWoman","Fisherman","Scholar","ChildGirl","Elder","Granny","ErrandBoy"})
            residentPrefabs[name]=BuildResident(name,name=="ChildGirl"?1.18f:name=="ErrandBoy"?1.42f:1.66f);
    }
    static GameObject BuildHouse(float width,float height,float depth,int variant)
    {
        var root=Root("IT_RiverHouse_"+variant,null);
        Box("Limewashed volume",root,new Vector3(0,height*.5f,0),new Vector3(width,height,depth),plaster,true);
        Box("Stone foundation",root,new Vector3(0,.23f,0),new Vector3(width+.24f,.46f,depth+.24f),stone);
        Box("Door inset",root,new Vector3(0,1.35f,-depth*.5f-.025f),new Vector3(1.65f,2.65f,.08f),ink);
        for(int side=-1;side<=1;side+=2)
        {
            Box("Timber door leaf",root,new Vector3(side*.39f,1.25f,-depth*.5f-.085f),new Vector3(.73f,2.4f,.12f),timber);
            Box("Door post",root,new Vector3(side*.91f,1.44f,-depth*.5f-.13f),new Vector3(.15f,2.88f,.17f),ink);
            Box("Corner timber",root,new Vector3(side*(width*.5f-.07f),height*.5f,-depth*.5f-.06f),new Vector3(.17f,height,.16f),timber);
            for(int level=0;level<(height>5?2:1);level++)
            {
                float y=1.8f+level*2.9f,x=side*width*.3f;
                Window(root,new Vector3(x,y,-depth*.5f-.065f),1.65f,1.55f);
            }
        }
        Box("Front lintel",root,new Vector3(0,2.85f,-depth*.5f-.12f),new Vector3(width,.16f,.18f),timber);
        if(height>5)
        {
            Box("Upper timber band",root,new Vector3(0,3.25f,-depth*.5f-.09f),new Vector3(width,.2f,.2f),timber);
            Box("Upper side band",root,new Vector3(width*.5f+.04f,3.25f,0),new Vector3(.14f,.2f,depth),timber);
            if(variant==3)
            {
                Box("Upper open gallery floor",root,new Vector3(0,3.23f,-depth*.5f-.63f),new Vector3(width*.88f,.18f,1.25f),timber);
                for(int p=0;p<9;p++)Box("Gallery slim baluster",root,new Vector3(-width*.4f+p*width*.1f,3.8f,-depth*.5f-1.1f),new Vector3(.065f,1,.075f),timber);
                Box("Gallery handrail",root,new Vector3(0,4.28f,-depth*.5f-1.1f),new Vector3(width*.84f,.09f,.11f),timber);
            }
        }
        Roof(root,width+1.5f,depth+1.8f,height,1.65f);
        if(variant!=1)
        {
            var porch=Root("Deep shaded shop eave",root);porch.localPosition=new Vector3(0,0,-depth*.5f-.9f);
            Roof(porch,width*.88f,2.8f,3.04f,.55f);
            for(int side=-1;side<=1;side+=2)
                Box("Veranda upright",porch,new Vector3(side*width*.39f,1.5f,-.85f),new Vector3(.16f,3,.16f),timber,true);
            Box("Shop shelf",porch,new Vector3(width*.26f,.85f,-.2f),new Vector3(width*.24f,.14f,.9f),timber);
            for(int p=0;p<3;p++)Pot(porch,new Vector3(width*.18f+p*.36f,.93f,-.2f),.14f,.28f,ceramic);
        }
        if(variant==1 || variant==3)
        {
            var wing=Root("Lower side dwelling",root);wing.localPosition=new Vector3(width*.5f+1.1f,0,depth*.13f);
            Box("Side volume",wing,new Vector3(0,1.45f,0),new Vector3(2.8f,2.9f,depth*.7f),plaster,true);
            Roof(wing,3.7f,depth*.7f+1,2.9f,.9f);Window(wing,new Vector3(0,1.6f,-depth*.35f-.04f),1.5f,1.2f);
        }
        for(int side=-1;side<=1;side+=2)
        {
            var sideWall=new Mesh{name="White gable"};
            sideWall.vertices=new[]{new Vector3(side*width*.5f,height,-depth*.5f),new Vector3(side*width*.5f,height,depth*.5f),new Vector3(side*width*.5f,height+1.65f,0)};
            sideWall.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};sideWall.triangles=new[]{0,1,2};sideWall.RecalculateNormals();
            Model("White gable",root,sideWall,plaster);
        }
        for(int i=0;i<4;i++)WornStone(root,new Vector3(-width*.4f+i*width*.23f,.17f,-depth*.5f-.12f),new Vector3(Range(.7f,1.7f),Range(.18f,.29f),.24f),bankStone);
        Combine(root,"RiverHouse"+variant);
        var roofCollider=Root("Roof camera support",root);roofCollider.localPosition=Vector3.up*(height+.7f);
        roofCollider.gameObject.AddComponent<BoxCollider>().size=new Vector3(width+1.25f,1.6f,depth+1.55f);
        if(variant!=1)
        {
            var flag=Root("Shop calligraphy banner",root);flag.localPosition=new Vector3(-width*.32f,0,-depth*.5f-2.05f);
            HangingCloth(flag,new Vector3(0,2.5f,0),.75f,1.5f);
            Sign(flag,variant==0?"茶":variant==2?"酒":"布",new Vector3(0,1.95f,-.035f),0,.23f);
            Lantern(root,new Vector3(width*.32f,2.45f,-depth*.5f-1.35f),.29f);
        }
        return SavePrefab(root,Prefabs+"/PF_IT_RiverHouse_"+variant+".prefab");
    }
    static void Window(Transform parent,Vector3 pos,float width,float height)
    {
        if(V3DetailedWindow(parent,pos,width,height))return;
        Box("Paper lattice window",parent,pos,new Vector3(width,height,.06f),cloth);
        for(int i=0;i<=4;i++)Box("Window upright",parent,pos+new Vector3(-width*.5f+i*width/4,0,-.055f),new Vector3(.045f,height+.09f,.065f),timber);
        for(int i=0;i<=3;i++)Box("Window crosspiece",parent,pos+new Vector3(0,-height*.5f+i*height/3,-.055f),new Vector3(width+.09f,.045f,.065f),timber);
        Box("Window sill",parent,pos+new Vector3(0,-height*.5f-.08f,-.06f),new Vector3(width+.25f,.16f,.25f),timber);
    }
    static void Roof(Transform parent,float width,float depth,float height,float rise)
        =>SculptRoof(parent,width,depth,height,rise);
    static GameObject BuildTree(string name,int kind) => SculptTree(name,kind);
    static void Leaf(Transform parent,Vector3 a,Vector3 b,float width)
    {
        var strokes=new BrushGeometry();strokes.Lance(a,b,width*.55f,a.x+a.z);
        Model("Leaf brush",parent,strokes.Mesh("Curved lance brush stroke"),foliage);
    }
    static GameObject BuildResident(string name,float height)
    {
        string modelPath="Assets/_Project/Models/JapaneseVillage/"+name+"/"+name+".fbx";
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if(!model)throw new System.IO.FileNotFoundException(modelPath);
        var root=Root("IT_Echo_"+name,null);var visual=(GameObject)PrefabUtility.InstantiatePrefab(model,root);
        var renderers=visual.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;
        foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        float scale=height/bounds.size.y;visual.transform.localScale*=scale;visual.transform.localPosition=Vector3.up*(-bounds.min.y*scale);
        var material=Mat("Resident_"+name,.91f,4,.4f);
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Models/JapaneseVillage/"+name+"/T_JV_"+name+"_Base.png"));
        material.SetFloat("_TextureWeight",1);material.SetFloat("_Outline",.18f);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        foreach(var renderer in renderers)
        {
            renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            if(renderer is SkinnedMeshRenderer skinned)skinned.updateWhenOffscreen=true;
        }
        foreach(var collider in root.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
        var animator=visual.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Animations/JapaneseVillage/AC_Villager.controller");
        animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        if(!animator.GetComponent<AnimationEventSink>())animator.gameObject.AddComponent<AnimationEventSink>();
        var echo=root.gameObject.AddComponent<InkboundEcho>();echo.plumeMaterial=plume;
        return SavePrefab(root,Residents+"/PF_IT_"+name+".prefab");
    }
}
#endif
