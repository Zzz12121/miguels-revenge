using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
public static class DojoPolishVisuals
{
    public static void Shot(string name,Vector3 position,Vector3 target,float fov=60)
    {
        var go=new GameObject("Dojo polish capture");var camera=go.AddComponent<Camera>();go.AddComponent<UniversalAdditionalCameraData>().requiresDepthTexture=true;
        camera.fieldOfView=fov;camera.nearClipPlane=.03f;camera.farClipPlane=700;go.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        var rt=RenderTexture.GetTemporary(1280,800,24);var prior=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1280,800,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,800),0,0);tex.Apply();File.WriteAllBytes("output/dojo-building/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
        finally{camera.targetTexture=null;RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(go);}
    }
    public static string Run()
    {
        Shot("62-fitted-closed-gate",new Vector3(0,2.5f,-7),new Vector3(0,3.4f,5),62);
        Shot("63-courtyard-no-exterior-fog",new Vector3(5,2.4f,60),new Vector3(45,5,88));
        Shot("64-exterior-terrain-hugging-mist",new Vector3(65,3,43),new Vector3(97,18,140));
        Shot("65-interior-mountain-unchanged",new Vector3(57,2.1f,123.5f),new Vector3(72,4,131));
        var gate=Object.FindAnyObjectByType<DojoCourtyardGate>();var left=gate.leftHinge.Find("HeavyTimberGate").GetComponent<Renderer>().bounds;var right=gate.rightHinge.Find("HeavyTimberGate").GetComponent<Renderer>().bounds;
        if(right.min.x-left.max.x>.005f||left.min.x> -5.7f||right.max.x<5.7f||left.max.y<6.1f)throw new Exception("Door gaps remain");
        var filter=Object.FindAnyObjectByType<DojoMountainVeil>().volume.GetComponent<MeshFilter>();if(filter.sharedMesh.vertexCount<20000)throw new Exception("Expected terrain shell, not mist cube");
        return "Door seam="+(right.min.x-left.max.x)+"; frame coverage="+left+" / "+right+"; terrain shell vertices="+filter.sharedMesh.vertexCount;
    }
}
