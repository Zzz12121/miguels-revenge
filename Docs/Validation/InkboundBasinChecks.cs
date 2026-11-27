using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class InkboundBasinChecks
{
    const string Folder="output/inkbound-town/mountain-basin";
    public static string Build()
    {
        Directory.CreateDirectory(Folder);
        int residents=Object.FindObjectsByType<NpcRoutine>().Length;
        var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();float speed=settings.inkPlaybackSpeed;
        InkboundTownBuilder.UpdateSurroundingBasin();
        var basin=Object.FindObjectsByType<Transform>().First(t=>t.name=="Irregular mountain basin around the town");
        var ridges=basin.GetComponentsInChildren<MeshFilter>().Where(m=>m.name.Contains("mountain rim")).ToArray();
        float[] coverage=new float[72];float seam=0;
        foreach(var filter in ridges)
        {
            var mesh=filter.sharedMesh;var vertices=mesh.vertices;
            foreach(var v in vertices)
            {
                Vector3 p=filter.transform.TransformPoint(v);int bin=Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(p.x,p.z-12)*Mathf.Rad2Deg,360)/5);
                coverage[bin]=Mathf.Max(coverage[bin],p.y);
            }
            for(int i=0;i<vertices.Length;i+=385)seam=Mathf.Max(seam,Vector3.Distance(vertices[i],vertices[i+384]));
        }
        var report=new{ridgeLayers=ridges.Length,coveredBearings=coverage.Count(h=>h>20),totalBearings=coverage.Length,
            lowestHorizonPeak=coverage.Min(),seamGap=seam,oldBackdropGroups=Object.FindObjectsByType<Transform>().Count(t=>t.name.StartsWith("V3 brush ridge ")),
            residentsPreserved=Object.FindObjectsByType<NpcRoutine>().Length==residents,speedPreserved=settings.inkPlaybackSpeed==speed,speed,
            persistentMeshes=basin.GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh&&AssetDatabase.Contains(m.sharedMesh)),
            addedColliders=basin.GetComponentsInChildren<Collider>().Length};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/checks.json",json);return json;
    }
    static void Shot(string name,Vector3 at,Vector3 target,bool overview=false)
    {
        var main=Camera.main;var c=new GameObject("Temporary basin review camera").AddComponent<Camera>();
        if(main)c.CopyFrom(main);c.enabled=false;c.farClipPlane=1200;c.fieldOfView=65;c.transform.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));
        c.orthographic=overview;if(overview)c.orthographicSize=370;
        int w=1440,h=overview?1080:810;var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;var image=new Texture2D(w,h,TextureFormat.RGB24,false);
        bool survey=at.y>100;
        var surfaces=survey?Object.FindObjectsByType<Renderer>():Array.Empty<Renderer>();
        var saved=surfaces.Select(r=>{var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);return b;}).ToArray();
        var lods=survey?Object.FindObjectsByType<LODGroup>():Array.Empty<LODGroup>();
        try
        {
            // Survey captures inspect geometry at a distance where normal paper atmosphere
            // intentionally washes out the town. Ground-level captures retain actual game settings.
            foreach(var r in surfaces){var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);b.SetFloat("_FadeNear",10000);b.SetFloat("_FadeFar",20000);r.SetPropertyBlock(b);}
            foreach(var lod in lods)lod.ForceLOD(0);
            Object.FindAnyObjectByType<InkboundWaterReflection>()?.RefreshFor(c);c.targetTexture=rt;c.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());
        }
        finally{for(int i=0;i<surfaces.Length;i++)if(surfaces[i])surfaces[i].SetPropertyBlock(saved[i]);foreach(var lod in lods)if(lod)lod.ForceLOD(-1);c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(c.gameObject);}
    }
    public static string Capture()
    {
        Directory.CreateDirectory(Folder);
        Shot("01-irregular-ring-overview",new Vector3(0,650,-150),new Vector3(0,12,12),true);
        Shot("02-valley-overlook",new Vector3(158,156,-198),new Vector3(0,12,12));
        Shot("03-north-from-quay",new Vector3(-13,2.7f,-38),new Vector3(5,23,150));
        Shot("04-west-from-town",new Vector3(-16,2.7f,-24),new Vector3(-145,26,18));
        Shot("05-east-from-town",new Vector3(15,2.7f,-28),new Vector3(150,28,16));
        Shot("06-south-valley",new Vector3(-12,3,54),new Vector3(0,24,-160));
        return "Saved basin overview and four town-facing mountain views.";
    }
}
