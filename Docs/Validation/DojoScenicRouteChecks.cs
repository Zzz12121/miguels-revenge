using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
public static class DojoScenicRouteChecks
{
    public static void Shot(string name,Vector3 position,Vector3 target,float fov=60)
    {
        var go=new GameObject("Scenic verification camera");var camera=go.AddComponent<Camera>();var data=go.AddComponent<UniversalAdditionalCameraData>();data.requiresDepthTexture=true;
        camera.nearClipPlane=.03f;camera.farClipPlane=700;camera.fieldOfView=fov;camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        var rt=RenderTexture.GetTemporary(1280,800,24);var previous=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes("output/dojo-building/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
        finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(go);}
    }
    public static string Run()
    {
        var cut=Object.FindAnyObjectByType<DojoOpeningCutscene>();var escape=cut.master.GetComponent<DojoElderEscape>();var nav=new DojoInkFlightPath(cut.master);
        var current=new Vector3(0,1.5f,59);var results=new System.Collections.Generic.List<string>();
        foreach(var point in escape.flightPath)
        {
            var leg=nav.Find(current,point.position);results.Add(point.name+" "+point.position+" corners="+leg.Count+" via "+string.Join(";",leg));
            foreach(var corner in leg){if(!nav.Clear(current,corner))throw new Exception("Blocked flight leg: "+current+" -> "+corner);current=corner;}
        }
        Shot("50-exterior-mountain-veil",new Vector3(71,2.2f,53),new Vector3(107,22,145));
        Shot("51-courtyard-remains-clear",new Vector3(2,2,52),new Vector3(0,3,88));
        Shot("52-mountain-path-original-mist",new Vector3(57,2.1f,123.5f),new Vector3(72,4,131));
        return string.Join("\n",results);
    }
}
