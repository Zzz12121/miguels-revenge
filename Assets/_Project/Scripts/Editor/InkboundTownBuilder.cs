#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Reproducible V3 town and continuous encounter. Authored assets are scoped to InkboundTown.</summary>
public static partial class InkboundTownBuilder
{
    public const string ScenePath="Assets/_Project/Scenes/InkboundTown_Exploration.unity";
    const string Materials="Assets/_Project/Materials/InkboundTown";
    const string Meshes="Assets/_Project/Models/Environment/InkboundTown";
    const string Prefabs="Assets/_Project/Prefabs/Environment/InkboundTown";
    const string Residents="Assets/_Project/Prefabs/NPC/InkboundTown";
    static Material paper,plaster,timber,roof,stone,earth,cloth,foliage,mountain,river,plume,ink,ceramic,leafWash,bankStone,bark;
    static int meshNumber;
    static System.Random rng;
    static Transform town,buildings,streets,scenery,life;
    static readonly List<GameObject> housePrefabs=new();
    static readonly Dictionary<string,GameObject> residentPrefabs=new();
    static GameObject willowPrefab,pinePrefab,bambooPrefab;
    public static Vector3 Entrance => new Vector3(-14,1.05f,-68);
    public static float RiverCenter(float z)=>Mathf.Sin((z+12)*.035f)*3.8f;
    public static float BridgeHeight(float x,float z)
    {
        float center=RiverCenter(z),t=Mathf.Abs(x-center)/10f;
        return t>1?0:2.5f*Mathf.Cos(t*Mathf.PI*.5f);
    }
    [MenuItem("GeminiGrowUp/Inkbound Town V3/Rebuild town")]
    public static void Build()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play mode before building the town.");
        if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("The current scene has unsaved edits. Save it before building the town.");
        var randomState=UnityEngine.Random.state;
        try
        {
            Directory.CreateDirectory("output/inkbound-town");
            File.WriteAllText("output/inkbound-town/build-status.json","{\"phase\":\"building\"}");
            foreach(string folder in new[]{Materials,Meshes,Prefabs,Residents})Folder(folder);
            rng=new System.Random(3317);UnityEngine.Random.InitState(3317);meshNumber=0;
            housePrefabs.Clear();residentPrefabs.Clear();CreateMaterials();CreateV3Materials();CreateV3Prefabs();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            town=Root("Inkbound Town — a preserved afternoon",null);
            streets=Root("01 Stone streets and winding river",town);buildings=Root("02 Inhabited town architecture",town);
            scenery=Root("03 Willow banks and painted mountains",town);life=Root("04 Preserved lives and ink echoes",town);
            BuildTerrain();BuildBridges();BuildEntrance();BuildV3Blocks();BuildV3Landscape();BuildV3Population();BuildLightingAndPlayer();BuildV3Encounter();InstallV3PassableInteraction();
            EditorSceneManager.SaveScene(scene,ScenePath);
            var listed=EditorBuildSettings.scenes.ToList();
            if(!listed.Any(x=>x.path==ScenePath)){listed.Add(new EditorBuildSettingsScene(ScenePath,true));EditorBuildSettings.scenes=listed.ToArray();}
            File.WriteAllText("output/inkbound-town/build-status.json",$"{{\"phase\":\"built\",\"scene\":\"{ScenePath}\",\"meshAssets\":{meshNumber}}}");
            Selection.activeGameObject=town.gameObject;
            if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(new Vector3(-8,5,-8),Quaternion.Euler(25,30,0),95);
            Debug.Log("[InkboundTown V3] Town, daily life and continuous encounter saved.");
        }
        catch(Exception e){File.WriteAllText("output/inkbound-town/build-status.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{phase="failed",error=e.ToString()}));throw;}
        finally{UnityEngine.Random.state=randomState;}
    }
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    static Material Mat(string name,float tone,int pattern,float grain=.35f)
    {
        string path=Materials+"/M_IT_"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Project/InkboundTown/Painted Surface"));AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",new Color(tone,tone,tone*.985f));material.SetFloat("_Pattern",pattern);
        material.SetColor("_PaperColor",new Color(.957f,.949f,.918f));
        material.SetFloat("_Grain",grain);material.SetFloat("_Wash",pattern==5?.35f:.18f);material.SetFloat("_Outline",pattern==5?.04f:.12f);
        material.SetFloat("_StrokeScale",1);
        material.SetFloat("_DirectionalUV",0);
        material.SetFloat("_FadeNear",pattern==5?100:32);material.SetFloat("_FadeFar",pattern==5?510:235);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
    }
    static void CreateMaterials()
    {
        paper=Mat("Paper",.957f,0);plaster=Mat("Limewash",.93f,0,.2f);timber=Mat("BrushTimber",.17f,1,.72f);
        roof=Mat("InkRoofTiles",.14f,2,.45f);stone=Mat("RiverStone",.74f,3,.6f);earth=Mat("QuietEarthenCourts",.865f,7,.45f);cloth=Mat("Linen",.82f,4);
        foliage=Mat("WillowLeaves",.30f,6,.8f);leafWash=Mat("WillowPaleWash",.54f,6,.85f);bankStone=Mat("QuayLayeredStone",.59f,3,.7f);
        mountain=Mat("MountainWash",.61f,5,.7f);
        bark=Mat("CurvedBark",.12f,1,.9f);bark.SetFloat("_DirectionalUV",1);EditorUtility.SetDirty(bark);AssetDatabase.SaveAssetIfDirty(bark);
        foreach(var lightSurface in new[]{plaster,earth,paper})
        {lightSurface.SetFloat("_Wash",.045f);EditorUtility.SetDirty(lightSurface);AssetDatabase.SaveAssetIfDirty(lightSurface);}
        ink=Mat("ContourInk",.065f,1);ceramic=Mat("Ceramic",.52f,0);
        river=Special("River","Project/InkboundTown/Painted River");plume=Special("ReturningPigment","Project/InkboundTown/Returning Pigment");
        river.SetColor("_BaseColor",new Color(.957f,.949f,.918f));river.SetFloat("_ReflectionStrength",.65f);
        EditorUtility.SetDirty(river);AssetDatabase.SaveAssetIfDirty(river);
    }
    static Material Special(string name,string shader)
    {
        string path=Materials+"/M_IT_"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(mat,path);}return mat;
    }
    static Transform Root(string name,Transform parent)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
    static GameObject Box(string name,Transform parent,Vector3 pos,Vector3 scale,Material material,bool solid=false)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
        go.transform.localPosition=pos;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;
        if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    static GameObject Beam(string name,Transform parent,Vector3 a,Vector3 b,float width,Material material,bool solid=false)
    {
        var go=Box(name,parent,(a+b)*.5f,new Vector3(width,Vector3.Distance(a,b),width),material,solid);
        go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);return go;
    }
    static GameObject Model(string name,Transform parent,Mesh mesh,Material material,bool solid=false)
    {
        if(solid && !AssetDatabase.Contains(mesh))mesh=SaveMesh(mesh,name.Replace(' ','_'));
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        if(solid)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
    }
    static Mesh SaveMesh(Mesh mesh,string name)
    {
        if(mesh.vertexCount==0 || mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))
            throw new InvalidOperationException("Invalid generated mesh: "+name);
        string path=Meshes+"/IT_"+(meshNumber++).ToString("D4")+"_"+name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing)
        {
            // Explicit mesh setters invalidate the live GPU buffers as well as serialized data.
            // CopySerialized alone can leave the previous build's geometry on screen until reimport.
            if(existing==mesh)return existing;
            existing.Clear(false);existing.indexFormat=mesh.indexFormat;
            existing.vertices=mesh.vertices;existing.normals=mesh.normals;existing.uv=mesh.uv;
            if(mesh.colors.Length==mesh.vertexCount)existing.colors=mesh.colors;
            existing.subMeshCount=mesh.subMeshCount;
            for(int sub=0;sub<mesh.subMeshCount;sub++)existing.SetTriangles(mesh.GetTriangles(sub),sub);
            existing.RecalculateBounds();existing.UploadMeshData(false);
            Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);return existing;
        }
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static void Combine(Transform parent,string label)
    {
        var groups=new Dictionary<Material,List<CombineInstance>>();
        var filters=parent.GetComponentsInChildren<MeshFilter>();
        foreach(var filter in filters)
        {
            var renderer=filter.GetComponent<MeshRenderer>();if(!renderer || filter.GetComponent<TextMesh>() || !filter.sharedMesh)continue;
            if(filter.GetComponentInParent<InkboundHangingMotion>())continue;
            var passable=filter.GetComponentInParent<InkboundPassableGroup>();
            if(passable&&passable.transform!=parent)continue;
            var materials=renderer.sharedMaterials;
            for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
            {
                var material=materials.Length>0?materials[Mathf.Min(sub,materials.Length-1)]:null;if(!material)continue;
                if(!groups.TryGetValue(material,out var list))groups[material]=list=new List<CombineInstance>();
                list.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=parent.worldToLocalMatrix*filter.transform.localToWorldMatrix});
            }
            Object.DestroyImmediate(renderer);Object.DestroyImmediate(filter);
        }
        foreach(var pair in groups)
        {
            var mesh=new Mesh{name=label+" "+pair.Key.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.ToArray(),true,true);
            Model(label+" "+pair.Key.name,parent,SaveMesh(mesh,label.Replace(' ','_')),pair.Key);
        }
        foreach(var node in parent.GetComponentsInChildren<Transform>(true).Reverse())
            if(node!=parent&&node.childCount==0&&node.GetComponents<Component>().Length==1)Object.DestroyImmediate(node.gameObject);
    }
    static GameObject SavePrefab(Transform root,string path)
    {BindV3PassableEchoes(root);var prefab=PrefabUtility.SaveAsPrefabAsset(root.gameObject,path);Object.DestroyImmediate(root.gameObject);return prefab;}
    static GameObject Place(GameObject prefab,Transform parent,Vector3 position,float yaw=0,float scale=1)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.transform.localPosition=position;
        go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=Vector3.one*scale;return go;
    }
    static void Sign(Transform parent,string words,Vector3 position,float yaw,float charSize=.12f)
    {
        // The town's signs and hanging cloth deliberately carry no lettering.
    }
    static float Range(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
}
#endif
