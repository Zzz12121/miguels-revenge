#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;

public static partial class InkboundTownBuilder
{
    public const string RiversideMarketMarker="06 Widened market beside the riverside block";
    public const float RiversideMarketPavingWidth=13.2f;
    static float newMarketWest=-49.1f,newMarketEast=-29.65f;
    public static float RiversideMarketCenter=>(newMarketWest+newMarketEast)*.5f;
    static float MarketX(float x)
    {
        float u=(x+51.1f)/14.2f;
        if(u<0)return x+newMarketWest+51.1f;
        if(u>1)return x+newMarketEast+36.9f;
        return Mathf.Lerp(newMarketWest,newMarketEast,u);
    }
    static Bounds MarketBounds(Transform t,bool roofs=false)
    {
        var bounds=new Bounds(t.position,Vector3.zero);bool found=false;
        foreach(var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if(r is ParticleSystemRenderer||r is LineRenderer)continue;
            if(roofs&&!r.sharedMaterials.Any(m=>m&&m.HasProperty("_Pattern")&&Mathf.Abs(m.GetFloat("_Pattern")-2)<.1f))continue;
            if(found)bounds.Encapsulate(r.bounds);else{bounds=r.bounds;found=true;}
        }
        return bounds;
    }
    static void ShiftMarketTransform(Transform t,float delta)
    {
        Undo.RecordObject(t,"Move market toward river");t.position+=Vector3.right*delta;
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }
    [MenuItem("GeminiGrowUp/Inkbound Town V3/Widen and join riverside market")]
    public static void UpdateRiversideMarket()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying||scene.path!=ScenePath)throw new InvalidOperationException("Open the town in Edit mode first.");
        var root=scene.GetRootGameObjects().First(g=>g.GetComponent<InkboundTownInkSettings>()).transform;
        ApplyRiversideMarket(root);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
    static void ApplyRiversideMarket(Transform root)
    {
        if(root.Find(RiversideMarketMarker))return;
        var block=root.Find("02 Inhabited town architecture");var ground=root.Find("01 Stone streets and winding river");
        var landscape=root.Find("03 Willow banks and painted mountains");
        var houses=block.Cast<Transform>().Where(t=>t.GetComponentsInChildren<BoxCollider>().Any(c=>c.name=="Solid stone foundation")).ToArray();
        var west=houses.Where(t=>Mathf.Abs(t.position.x+51.1f)<.1f&&t.position.z>-15&&t.position.z<18).ToArray();
        var east=houses.Where(t=>Mathf.Abs(t.position.x+36.9f)<.1f&&t.position.z>-15&&t.position.z<18).ToArray();
        var river=houses.Where(t=>t.position.x>-24&&t.position.x<-17&&t.position.z>-19&&t.position.z<34).ToArray();
        if(west.Length!=4||east.Length!=4||river.Length<4)throw new InvalidOperationException("The original market rows could not be identified; no layout was changed.");
        float riverRear=river.Min(t=>MarketBounds(t,true).min.x);
        float rearOverhang=east.Max(t=>MarketBounds(t,true).max.x-t.position.x);
        newMarketWest=west.Average(t=>t.position.x)+2;
        newMarketEast=riverRear-.2f-rearOverhang;
        var shops=west.Concat(east).ToArray();
        var shifts=shops.ToDictionary(t=>t,t=>t.position.x<-44?newMarketWest-t.position.x:newMarketEast-t.position.x);

        // Determine each shopkeeper/guest's building before moving anything. Their held cups,
        // tasks and current 1.1 scale remain attached to the same complete resident root.
        foreach(var npc in root.GetComponentsInChildren<NpcRoutine>(true))
        {
            bool pedestrian=npc.name.StartsWith("Market passing life")||npc.name.StartsWith("Additional browsing customer");
            Transform shop=null;
            if(!pedestrian&&npc.stops!=null&&npc.stops.Length==1)
            {
                shop=shops.FirstOrDefault(t=>
                {
                    var local=t.InverseTransformPoint(npc.stops[0].position);
                    var floor=t.GetComponentsInChildren<BoxCollider>().First(c=>c.name=="Solid stone foundation");
                    return Mathf.Abs(local.x)<floor.transform.localScale.x*.5f&&Mathf.Abs(local.z)<floor.transform.localScale.z*.5f;
                });
            }
            if(!pedestrian&&!shop)continue;
            Undo.RecordObject(npc,"Relocate market resident routine");
            float delta=pedestrian?MarketX(npc.transform.position.x)-npc.transform.position.x:shifts[shop];
            ShiftMarketTransform(npc.transform,delta);
            for(int i=0;i<npc.stops.Length;i++)
            {var stop=npc.stops[i];stop.position.x=pedestrian?MarketX(stop.position.x):stop.position.x+delta;npc.stops[i]=stop;}
            EditorUtility.SetDirty(npc);PrefabUtility.RecordPrefabInstancePropertyModifications(npc);
        }
        foreach(var shop in shops)ShiftMarketTransform(shop,shifts[shop]);
        var gate=block.Find("Market street timber paifang");
        if(gate)
        {
            ShiftMarketTransform(gate,RiversideMarketCenter-gate.position.x);
            Undo.RecordObject(gate,"Widen market paifang span");gate.localScale=new Vector3(gate.localScale.x*1.5f,gate.localScale.y,gate.localScale.z);
        }
        var slice=ground.Find("V3 first street - 28m quality slice");
        foreach(var c in slice.GetComponentsInChildren<BoxCollider>())if(c.name=="Smooth street collision")
        {
            ShiftMarketTransform(c.transform,RiversideMarketCenter-c.transform.position.x);
            Undo.RecordObject(c.transform,"Widen market street support");var scale=c.transform.localScale;scale.x=RiversideMarketPavingWidth;c.transform.localScale=scale;
        }
        foreach(Transform detail in slice)
            if(detail.name=="Uneven shop threshold")
            {float x=MarketBounds(detail).center.x;ShiftMarketTransform(detail,MarketX(x)-x);}
        foreach(Transform detail in landscape)
        {
            if(detail.position.x<-58||detail.position.x>-30||detail.position.z<-27||detail.position.z>28)continue;
            // Curved basin meshes and distant mountain groups are excluded by their root positions.
            ShiftMarketTransform(detail,MarketX(detail.position.x)-detail.position.x);
        }
        WidenBakedMarketPaving(ground);
        var encounter=root.GetComponent<InkboundTownEncounter>();
        if(encounter&&encounter.elder)
            ShiftMarketTransform(encounter.elder.transform,MarketX(encounter.elder.transform.position.x)-encounter.elder.transform.position.x);
        Root(RiversideMarketMarker,root);
        RebuildMarketNavigation(root);
    }
    static void WidenBakedMarketPaving(Transform ground)
    {
        var lanes=ground.Find("Market lanes connecting the quays");
        var filter=lanes.GetComponentsInChildren<MeshFilter>().Single();
        var mesh=Object.Instantiate(filter.sharedMesh);mesh.name="Existing town lanes with wider relocated market street";
        var vertices=mesh.vertices;int changed=0;
        for(int i=0;i<vertices.Length;i++)
        {
            Vector3 world=filter.transform.TransformPoint(vertices[i]);
            // Only this original cube's two x edges match; bridge approaches and cross streets
            // share the material batch but retain their original vertices and coordinates.
            if(world.z<-22.51f||world.z>30.51f)continue;
            if(Mathf.Abs(world.x+47)>.001f&&Mathf.Abs(world.x+41)>.001f)continue;
            world.x=RiversideMarketCenter+(world.x<-44?-1:1)*RiversideMarketPavingWidth*.5f;
            vertices[i]=filter.transform.InverseTransformPoint(world);changed++;
        }
        if(changed!=24){Object.DestroyImmediate(mesh);throw new InvalidOperationException("Could not isolate the 24 market paving vertices.");}
        mesh.vertices=vertices;mesh.RecalculateBounds();
        string path=Meshes+"/IT_WidenedRiversideMarketLanes.asset";
        var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(previous)
        {
            previous.Clear(false);previous.indexFormat=mesh.indexFormat;previous.vertices=mesh.vertices;previous.normals=mesh.normals;previous.uv=mesh.uv;previous.triangles=mesh.triangles;previous.RecalculateBounds();previous.UploadMeshData(false);
            Object.DestroyImmediate(mesh);mesh=previous;EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
        }
        else AssetDatabase.CreateAsset(mesh,path);
        Undo.RecordObject(filter,"Widen only market street paving");filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);
    }
    static void RebuildMarketNavigation(Transform root)
    {
        var surface=root.GetComponent<NavMeshSurface>();if(!surface)return;
        var previous=surface.navMeshData;string path=AssetDatabase.GetAssetPath(previous);
        Physics.SyncTransforms();surface.BuildNavMesh();
        if(previous&&!string.IsNullOrEmpty(path)&&surface.navMeshData!=previous)
        {
            var generated=surface.navMeshData;surface.RemoveData();EditorUtility.CopySerialized(generated,previous);
            surface.navMeshData=previous;surface.AddData();Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(previous);AssetDatabase.SaveAssetIfDirty(previous);
        }
        EditorUtility.SetDirty(surface);
    }
}
#endif
