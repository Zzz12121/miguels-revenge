using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
public static class DojoGateBuild
{
    const string MeshPath="Assets/_Project/Models/Environment/FinalDojoArena/Garden/";
    static Transform Group(Transform parent,string name,Vector3 position)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
    static void Box(Transform parent,string name,Vector3 at,Vector3 size,Material material,bool collision=true)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    public static string Run()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var a=GameObject.Find("FinalDojoArena/Architecture");if(!a)throw new Exception("Open dojo scene first");
        var boundary=a.transform.Find("OuterWallsAndGate");var baked=boundary.Find("BakedVisuals");
        var oldLeaves=boundary.Cast<Transform>().Where(t=>t.name=="OpenGateLeaf").ToArray();
        foreach(var f in baked.GetComponentsInChildren<MeshFilter>())
        {
            var source=f.sharedMesh;var verts=source.vertices;var triangles=source.triangles;var kept=new List<int>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                Vector3 c=f.transform.TransformPoint((verts[triangles[i]]+verts[triangles[i+1]]+verts[triangles[i+2]])/3);
                bool door=oldLeaves.Any(t=>{var p=t.InverseTransformPoint(c);float side=Mathf.Sign(t.localPosition.x);return Mathf.Abs(p.x+side*2.6f)<=2.61f&&p.y>=-.01f&&p.y<=5.31f&&Mathf.Abs(p.z)<=.2f;});
                if(!door)kept.AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});
            }
            if(kept.Count==triangles.Length)continue;
            string name=source.name.Split(new[]{"_InteractiveGate"},StringSplitOptions.None)[0]+"_InteractiveGate";
            var mesh=Object.Instantiate(source);mesh.name=name;mesh.triangles=kept.ToArray();mesh.RecalculateBounds();
            string path=MeshPath+name+".asset";var prior=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(prior){EditorUtility.CopySerialized(mesh,prior);Object.DestroyImmediate(mesh);f.sharedMesh=prior;}else{AssetDatabase.CreateAsset(mesh,path);f.sharedMesh=mesh;}
        }
        foreach(var t in oldLeaves)t.gameObject.SetActive(false);
        var existing=boundary.Find("InteractiveEntrance");if(existing)Object.DestroyImmediate(existing.gameObject);
        var g=Group(boundary,"InteractiveEntrance",new Vector3(0,0,5));
        Func<string,Material> mat=name=>baked.Find(name).GetComponent<MeshRenderer>().sharedMaterial;
        var wood=mat("M_Dojo_CedarTimber");var dark=mat("M_Dojo_DarkJoinery");var stone=mat("M_Dojo_FoundationStone");var plaster=mat("M_Dojo_Plaster");
        foreach(int side in new[]{-1,1})
        {
            Box(g,"WallConnectorBase_"+side,new Vector3(side*7.05f,1,0),new Vector3(2.12f,2,1.1f),stone);
            Box(g,"WallConnectorPlaster_"+side,new Vector3(side*7.05f,3,0),new Vector3(2.12f,2,.75f),plaster);
            Box(g,"WallConnectorCoping_"+side,new Vector3(side*7.05f,4.08f,0),new Vector3(2.12f,.22f,1.25f),dark);
        }
        var component=g.gameObject.AddComponent<DojoCourtyardGate>();
        foreach(int side in new[]{-1,1})
        {
            var hinge=Group(g,side<0?"LeftHinge":"RightHinge",new Vector3(side*5.6f,0,0));
            var body=hinge.gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            Box(hinge,"HeavyTimberGate",new Vector3(-side*2.75f,2.73f,0),new Vector3(5.5f,5.3f,.26f),wood);
            foreach(float y in new[]{1f,4.45f})Box(hinge,"IronGateStrap",new Vector3(-side*2.75f,y,-.16f),new Vector3(5.28f,.2f,.07f),dark,false);
            Box(hinge,"IronPullHandle",new Vector3(-side*5.0f,2.55f,-.21f),new Vector3(.16f,.62f,.13f),dark,false);
            Box(hinge,"InsidePullHandle",new Vector3(-side*5.0f,2.55f,.21f),new Vector3(.16f,.62f,.13f),dark,false);
            if(side<0)component.leftHinge=hinge;else component.rightHinge=hinge;
        }
        PrefabUtility.SaveAsPrefabAsset(g.gameObject,"Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoInteractiveEntrance.prefab");
        PrefabUtility.SaveAsPrefabAsset(GameObject.Find("FinalDojoArena"),"Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_FinalDojoArena_Living.prefab");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(a.scene);EditorSceneManager.SaveScene(a.scene);Physics.SyncTransforms();
        return "Saved connected gate walls, independent swinging leaves/colliders and E interaction component";
    }
}
