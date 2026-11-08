using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

public static class DojoArenaChecks
{
    static readonly List<string> checks=new List<string>();
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
    static void ClearCapsule(string label,Vector3 feet)
    {
        Collider[] overlap=Physics.OverlapCapsule(feet+Vector3.up*.37f,feet+Vector3.up*1.42f,.34f,~0,QueryTriggerInteraction.Ignore);
        Require(overlap.Length==0,label+" capsule clearance"+(overlap.Length>0?": "+string.Join(", ",overlap.Select(c=>c.name)):""));
    }
    static void Floor(string label,Vector3 feet,float expectedHeight)
    {
        RaycastHit hit;Require(Physics.Raycast(feet+Vector3.up*.65f,Vector3.down,out hit,1.8f),label+" has a collision floor");
        Require(Mathf.Abs(hit.point.y-expectedHeight)<.04f,label+" floor height "+hit.point.y.ToString("F2"));
    }
    public static string Run()
    {
        checks.Clear();Physics.SyncTransforms();
        var root=GameObject.Find("FinalDojoArena");Require(root!=null,"Architecture root exists");
        Require(SceneManager.GetActiveScene().path=="Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity","Checks target only the separate architecture scene");
        var renderers=root.GetComponentsInChildren<MeshRenderer>();
        Require(renderers.Length<=85,"Opaque architecture batches/roof instances stay under 85 renderers");
        Require(renderers.All(r=>r.sharedMaterial && r.sharedMaterial.shader && r.sharedMaterial.shader.isSupported),"All architecture materials have supported shaders");
        Require(root.GetComponentsInChildren<MonoBehaviour>().Length==0,"No gameplay, AI, player, door-flow or runtime control components on architecture");
        Require(root.GetComponentsInChildren<Collider>().All(c=>!c.isTrigger),"All colliders are static solid architecture");
        Require(Physics.OverlapBox(new Vector3(0,1.6f,45),new Vector3(26,1.55f,26),Quaternion.identity,~0,QueryTriggerInteraction.Ignore).Length==0,"Entire 52x52m fight volume is clear, not only sampled paths");
        foreach(float z in new[]{76f,77.9f,78.2f,82f,88f,91.8f,92.3f}){var p=new Vector3(0,1.25f,z);ClearCapsule("Main route z="+z,p);Floor("Main route z="+z,p,1.2f);}
        ClearCapsule("Rear chamber dais",new Vector3(0,1.47f,95));Floor("Rear chamber dais",new Vector3(0,1.47f,95),1.42f);
        foreach(int side in new[]{-1,1})
        {
            for(float x=9;x<=14;x+=.5f)ClearCapsule("Side room entrance "+side+" x="+x,new Vector3(side*x,1.25f,84.5f));
            for(float x=14;x<=27;x+=.5f){var p=new Vector3(side*x,1.25f,88);ClearCapsule("Annex connector "+side+" x="+x,p);Floor("Annex connector "+side+" x="+x,p,1.2f);}
            foreach(float z in new[]{20f,35f,50f,65f,78f}){var p=new Vector3(side*39,.61f,z);ClearCapsule("Walkway "+side+" z="+z,p);Floor("Walkway "+side+" z="+z,p,.56f);}
            for(int i=0;i<3;i++){float top=.56f+(i+1)*(.64f/3);Floor("Annex bridge stair "+side+" "+i,new Vector3(side*39,top+.05f,79.2f+i*.38f),top);}
            for(int i=0;i<6;i++)Floor("Annex front stair "+side+" "+i,new Vector3(side*27,.05f+(i+1)*.2f,79.8f+i*.4f),(i+1)*.2f);
            for(float x=32;x>=27;x-=.5f)ClearCapsule("Annex front veranda "+side+" x="+x,new Vector3(side*x,1.25f,82.75f));
        }
        for(int i=0;i<6;i++){var p=new Vector3(0,.05f+(i+1)*.2f,72.8f+i*.4f);Floor("Main stair "+i,p,(i+1)*.2f);}
        for(float x=-25;x<=25;x+=5)for(float z=20;z<=70;z+=5)
        {
            var p=new Vector3(x,.05f,z);ClearCapsule("Clear combat area "+x+","+z,p);Floor("Clear combat area "+x+","+z,p,0);
        }
        foreach(var mesh in root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct())
        {
            Require(mesh!=null,"Mesh reference present");Require(AssetDatabase.Contains(mesh),"Mesh persists as a native Unity asset: "+mesh.name);
        }
        var result=new{success=true,checks=checks.Count,scene=SceneManager.GetActiveScene().path,rendererCount=renderers.Length,colliderCount=root.GetComponentsInChildren<Collider>().Length};
        var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{summary=result,passed=checks},Newtonsoft.Json.Formatting.Indented);
        Directory.CreateDirectory("output/dojo-building");File.WriteAllText("output/dojo-building/architecture-checks.json",json);
        return Newtonsoft.Json.JsonConvert.SerializeObject(result);
    }
}
