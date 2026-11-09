using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
public static class DojoGardenCapture
{
    static void Render(Camera cam,string name,Vector3 position,Vector3 target,float fov)
    {
        cam.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));cam.fieldOfView=fov;
        var rt=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGB32);var active=RenderTexture.active;
        try
        {
            cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();File.WriteAllBytes("output/dojo-building/"+name+".png",t.EncodeToPNG());Object.DestroyImmediate(t);
        }
        finally{cam.targetTexture=null;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}
    }
    public static string Run()
    {
        var go=new GameObject("Temporary garden capture");var cam=go.AddComponent<Camera>();cam.nearClipPlane=.1f;cam.farClipPlane=350;cam.clearFlags=CameraClearFlags.Skybox;cam.allowHDR=true;go.AddComponent<UniversalAdditionalCameraData>();
        try
        {
            Render(cam,"08-living-garden-approach",new Vector3(7,3.2f,23),new Vector3(0,3,78),61);
            Render(cam,"09-living-garden-overview",new Vector3(42,25,18),new Vector3(0,3,57),62);
            Render(cam,"10-stepping-stones-and-grass",new Vector3(3,1.7f,49),new Vector3(0,0,58),65);
            Render(cam,"13-torii-and-approach",new Vector3(0,3.4f,-1),new Vector3(0,3,42),65);
            Render(cam,"14-connected-entrance",new Vector3(13,4,-9),new Vector3(0,3.3f,5),60);
            Render(cam,"15-rear-garden",new Vector3(42,8,112),new Vector3(-10,3,107),68);
            Render(cam,"16-pine-root-grounding",new Vector3(-32,1.5f,102.7f),new Vector3(-36,.8f,105.5f),58);
            Render(cam,"17-rear-stream-garden",new Vector3(7,3.3f,119.5f),new Vector3(-6,2.7f,97),78);
            Render(cam,"18-rear-stream-overview",new Vector3(43,20,130),new Vector3(-3,1,109),65);
            Render(cam,"19-dense-front-lawn",new Vector3(5,1.2f,43),new Vector3(14,.2f,49),62);
            Render(cam,"22-stone-bridge-and-stream",new Vector3(12,13,118),new Vector3(12,0,118.05f),60); Render(cam,"23-rounded-apron-and-river",new Vector3(0,30,128),new Vector3(0,0,111),72); return "Saved 12 actual Unity garden renders";
        }
        finally{Object.DestroyImmediate(go);}
    }
}
