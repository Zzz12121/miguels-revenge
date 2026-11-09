using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
public static class DojoGroundBankChecks
{
    static void Check(bool b,string m){if(!b)throw new Exception(m);}
    static Bounds BoundsOf(Transform t){var rs=t.GetComponentsInChildren<MeshRenderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
    static void Render(Camera cam,string name,Vector3 pos,Vector3 target,float fov)
    {
        cam.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(target-pos));cam.fieldOfView=fov;
        var rt=RenderTexture.GetTemporary(1440,900,24);var old=RenderTexture.active;
        try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes("output/dojo-building/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
        finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}
    }
    public static string Run()
    {
        var garden=GameObject.Find("FinalDojoArena/Architecture/LivingGarden").transform;var rear=garden.Find("RearLandscape");
        var path=garden.Cast<Transform>().Where(t=>t.name.StartsWith("SteppingStone_")).OrderBy(t=>t.name).Select(t=>t.name+"|"+t.position.ToString("F4")+"|"+t.localScale.ToString("F4")).ToArray();
        Check(path.Length==34,"Preserve 34 front stepping stones");
        if(File.Exists("Temp/dojo-preserved-front-path.txt"))Check(path.SequenceEqual(File.ReadAllLines("Temp/dojo-preserved-front-path.txt")),"Front path transforms must be unchanged");
        Check(!rear.Find("CourtyardSandSurface")&&!rear.Find("RearGreyGravel")&&!rear.Find("ApronSmallPebbles"),"Remove sand/gravel overlays");
        Check(!garden.Find("GravelGround/Ground").GetComponent<Renderer>().enabled,"Legacy gravel must stay hidden");
        var turf=rear.Find("ContinuousGreenTurf");Check(turf.GetComponent<Renderer>().sharedMaterial.GetFloat("_Grass")==1,"Continuous turf uses grass material");
        var mesh=turf.GetComponent<MeshFilter>().sharedMesh;
        var go=new GameObject("Temporary turf sampling");var mc=go.AddComponent<MeshCollider>();mc.sharedMesh=mesh;
        int floorSamples=0;
        try
        {
            foreach(float x in new[]{-47f,-44f,-36f,-21f,0,21,36,44,47})foreach(float z in new[]{6f,30,72,95,102,107,124.9f,125.6f})
            {RaycastHit h;Check(mc.Raycast(new Ray(new Vector3(x,3,z),Vector3.down),out h,5),"Missing grass base "+x+","+z);floorSamples++;}
        }
        finally{Object.DestroyImmediate(go);}
        int blades=0;
        foreach(var t in rear.Cast<Transform>().Where(t=>t.name.StartsWith("DenseGrass_")))
        {
            var v=t.GetComponent<MeshFilter>().sharedMesh.vertices;
            for(int i=0;i<v.Length;i+=5)
            {var p=(v[i]+v[i+1])*.5f;Check(p.z<101.2f||Mathf.Abs(p.z-DojoRiverWater.Center(p.x))>DojoRiverWater.Width(p.x)*.5f+.119f,"Grass root in water");blades++;}
        }
        var banks=rear.Cast<Transform>().Where(t=>t.name.StartsWith("Riverbank")).ToArray();
        float maxCenterError=0;
        foreach(var t in banks)
        {
            var b=BoundsOf(t);float error=Mathf.Abs(Mathf.Abs(b.center.z-DojoRiverWater.Center(b.center.x))-DojoRiverWater.Width(b.center.x)*.5f-.1f);
            maxCenterError=Mathf.Max(maxCenterError,error);Check(error<.02f,"Bank stone must follow water edge "+t.name);Check(b.min.y<DojoRiverWater.WaterY,"Bank stone floats above water");
        }
        int continuousPairs=0;
        foreach(int side in new[]{-1,1})
        {
            var row=banks.Where(t=>t.name.StartsWith("RiverbankBoulder_")).Select(BoundsOf).Where(b=>Mathf.Sign(b.center.z-DojoRiverWater.Center(b.center.x))==side).OrderBy(b=>b.center.x).ToArray();
            for(int i=1;i<row.Length;i++)
            {if(row[i-1].center.x<12&&row[i].center.x>12)continue;Check(row[i].min.x-row[i-1].max.x<.12f,"Unconnected bank stones");continuousPairs++;}
        }
        var shaderErrors=new[]{"Project/Environment/Dojo Garden Stream","Project/Environment/Dojo Garden Ground","Project/Environment/Dojo Garden Wind"}.SelectMany(n=>ShaderUtil.GetShaderMessages(Shader.Find(n))).Where(m=>m.severity.ToString()=="Error").ToArray();Check(shaderErrors.Length==0,"Garden shader errors");
        var capture=new GameObject("Temporary grass and bank capture");var cam=capture.AddComponent<Camera>();cam.nearClipPlane=.1f;cam.farClipPlane=350;capture.AddComponent<UniversalAdditionalCameraData>();
        try
        {
            Render(cam,"25-grass-connected-riverbanks",new Vector3(47,17,132),new Vector3(0,0,113),66);
            Render(cam,"26-house-surrounding-grass",new Vector3(46,9,102),new Vector3(32,0,87),68);
            Render(cam,"27-rear-grass-and-shore",new Vector3(43,12,124),new Vector3(-12,0,116),66);
            Render(cam,"28-front-stones-preserved",new Vector3(8,7,29),new Vector3(0,0,55),62);
            Render(cam,"29-riverbank-closeup",new Vector3(-18,4,121),new Vector3(-14,-.2f,114),65);
        }
        finally{Object.DestroyImmediate(capture);}
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{success=true,floorSamples,grassBlades=blades,bankStones=banks.Length,continuousPairs,maxCenterError,preservedSteppingStones=path.Length,shaderErrors=shaderErrors.Length},Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/grass-bank-checks.json",json);return json;
    }
}
