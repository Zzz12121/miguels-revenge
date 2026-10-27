using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class CalligraphyAvatarFacePreview
{
    public static string Inspect()
    {
        var boss=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossCombat>();
        if(!boss)throw new Exception("Open the saved range scene first");
        return string.Join("\n",boss.brush.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>
            r.name+": readable="+r.sharedMesh.isReadable+", vertices="+r.sharedMesh.vertexCount+
            ", head/hair bones="+string.Join(",",r.bones.Where(b=>b && (b.name.Contains("Head") || b.name.Contains("Neck") || b.name.Contains("Hair"))).Select(b=>b.name))));
    }
    public static async Task<string> Preview()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Enter Play Mode first");
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();var boss=range.Boss;
        boss.moveSpeed=0;boss.dodgeChance=0;boss.inkAvatarUltimate.roarSeconds=30;
        boss.SelectSkill(9);await Task.Delay(3800);
        var root=boss.AvatarUltimate.AvatarRoot;
        var rig=root.GetComponent<CalligraphyBrushPresentation>();
        var head=rig.animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Head");
        var eyes=rig.animator.GetComponentsInChildren<Transform>().Where(t=>t.name=="Left_Eye" || t.name=="Right_Eye").ToArray();
        Vector3 focus=eyes.Length==2?(eyes[0].position+eyes[1].position)*.5f:head.position;
        var surfaces=root.GetComponentsInChildren<Renderer>(true);
        var skin=surfaces.OfType<SkinnedMeshRenderer>().First();
        // run_script continuations can resume before LateUpdate repopulates per-renderer motion data.
        typeof(CalligraphyAvatarLife).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
            .Invoke(root.GetComponent<CalligraphyAvatarLife>(),null);
        var shader=skin.sharedMaterial.shader;
        var messages=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
        if(messages.Length>0)throw new Exception(string.Join(";",messages.Select(m=>m.message)));
        if(skin.sharedMaterial.HasProperty("_EyeVisibility") || skin.sharedMaterial.HasProperty("_EyeContrast"))throw new Exception("Artificial eye properties remain");
        var block=new MaterialPropertyBlock();skin.GetPropertyBlock(block);
        var right=(Vector3)block.GetVector("_HeadRight");var up=(Vector3)block.GetVector("_HeadUp");var forward=(Vector3)block.GetVector("_HeadForward");
        float protectedMargin=Vector3.Dot(head.position-(Vector3)block.GetVector("_ClothTop"),up);
        if(protectedMargin<=0)throw new Exception("Chest boundary does not protect the head");
        int[] layers=surfaces.Select(r=>r.gameObject.layer).ToArray();
        bool[] enabled=surfaces.Select(r=>r.enabled).ToArray();
        var cameraObject=new GameObject("Temporary avatar face review camera");var camera=cameraObject.AddComponent<Camera>();
        camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.2f,.23f);
        camera.fieldOfView=34;camera.farClipPlane=250;
        var rt=new RenderTexture(1000,1000,24);var originalActive=RenderTexture.active;
        var image=new Texture2D(1000,1000,TextureFormat.RGB24,false);
        float oldTimeScale=Time.timeScale;Time.timeScale=0;
        try
        {
            for(int i=0;i<surfaces.Length;i++){surfaces[i].gameObject.layer=31;if(!(surfaces[i] is SkinnedMeshRenderer))surfaces[i].enabled=false;}
            camera.targetTexture=rt;
            foreach(var view in new[]{("front",forward), ("side",(forward+right*.7f).normalized), ("back",-forward)})
            {
                camera.transform.SetPositionAndRotation(focus+view.Item2*25+up,Quaternion.LookRotation(-view.Item2,up));
                camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();
                System.IO.File.WriteAllBytes("Logs/Avatar-face-fixed-"+view.Item1+".png",image.EncodeToPNG());
            }
            // Same posed mesh and same shader time: extra cloth motion must not alter the head close-up.
            camera.fieldOfView=18;camera.transform.SetPositionAndRotation(focus+forward*25,Quaternion.LookRotation(-forward,up));
            camera.Render();image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();var moving=image.GetPixels32();
            block.SetFloat("_BodyDeform",0);skin.SetPropertyBlock(block);
            camera.Render();image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();var still=image.GetPixels32();
            int difference=0;for(int i=0;i<still.Length;i++)if(!still[i].Equals(moving[i]))difference++;
            if(difference>1000)throw new Exception("Cloth motion affects head close-up: "+difference+" pixels");
            return "PASS: no artificial eye shader properties/errors; protected head margin="+protectedMargin.ToString("F2")+
                "; head pixels changed with cloth motion="+difference+"/1000000; front/side/back in Logs/Avatar-face-fixed-*.png";
        }
        finally
        {
            block.SetFloat("_BodyDeform",1);skin.SetPropertyBlock(block);
            for(int i=0;i<surfaces.Length;i++)if(surfaces[i]){surfaces[i].gameObject.layer=layers[i];surfaces[i].enabled=enabled[i];}
            camera.targetTexture=null;RenderTexture.active=originalActive;Time.timeScale=oldTimeScale;
            UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);
            boss.CancelCast();
        }
    }
}
