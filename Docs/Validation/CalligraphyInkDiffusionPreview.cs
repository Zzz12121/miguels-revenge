using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class CalligraphyInkDiffusionPreview
{
    const string MaterialPath="Assets/_Project/VFX/Calligraphy/Materials/M_InkAvatarDiffusion.mat";
    public static string Install()
    {
        if(EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)throw new Exception("Need saved Edit Mode scene");
        var shader=Shader.Find("Project/VFX/CalligraphyInkDiffusion");if(!shader)throw new Exception("Diffusion shader not imported");
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(!material){material=new Material(shader){name="M_InkAvatarDiffusion"};AssetDatabase.CreateAsset(material,MaterialPath);}
        void Tune(CalligraphyBossCombat boss)
        {
            var sea=boss.inkAvatarUltimate.sea;sea.waveAmplitude=.18f;sea.airPlumeCount=112;
            sea.airHeight=new Vector2(2.5f,7);sea.airLifetime=new Vector2(3.2f,5.5f);sea.airSpread=2.3f;
            sea.airOpacity=.62f;sea.airBranching=.8f;sea.diffusionMaterial=material;EditorUtility.SetDirty(boss);
        }
        string path="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try{Tune(prefab.GetComponent<CalligraphyBossCombat>());PrefabUtility.SaveAsPrefabAsset(prefab,path);}
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
        var actor=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossCombat>();Tune(actor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(actor);EditorSceneManager.MarkSceneDirty(actor.gameObject.scene);EditorSceneManager.SaveScene(actor.gameObject.scene);
        foreach(var target in new[]{shader,actor.inkAvatarUltimate.sea.material.shader})
        {
            var errors=ShaderUtil.GetShaderMessages(target).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            if(errors.Length>0)throw new Exception(string.Join(";",errors.Select(e=>e.message)));
        }
        return "Saved calm sea and 112 independently staggered ink diffusion sites on range elder and Battle prefab; shader diagnostics clean";
    }
    static async Task Until(Func<bool> ready,float seconds)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!ready()){if(clock.Elapsed.TotalSeconds>seconds)throw new Exception("Timed out waiting for impact");await Task.Delay(30);}
    }
    static void Capture(string name,Camera camera)
    {
        var rt=new RenderTexture(1400,900,24);var image=new Texture2D(1400,900,TextureFormat.RGB24,false);
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();System.IO.File.WriteAllBytes("Logs/Ink-diffusion-"+name+".png",image.EncodeToPNG());
        }
        finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);}
    }
    static string SurfaceFlow(CalligraphyInkSeaMotion motion)
    {
        var surface=motion.GetComponent<Renderer>();var original=new MaterialPropertyBlock();surface.GetPropertyBlock(original);
        var errors=ShaderUtil.GetShaderMessages(surface.sharedMaterial.shader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
        if(errors.Length>0)throw new Exception(string.Join(";",errors.Select(e=>e.message)));
        var cameraObject=new GameObject("Temporary top-down surface review");var camera=cameraObject.AddComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=80;camera.cullingMask=1<<31;camera.farClipPlane=250;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.48f,.5f,.53f);
        camera.transform.SetPositionAndRotation(motion.transform.position+Vector3.up*110,Quaternion.LookRotation(Vector3.down,Vector3.forward));
        var target=new RenderTexture(1000,1000,24);var image=new Texture2D(1000,1000,TextureFormat.RGB24,false);
        var active=RenderTexture.active;int oldLayer=surface.gameObject.layer;
        try
        {
            surface.gameObject.layer=31;camera.targetTexture=target;var block=new MaterialPropertyBlock();surface.GetPropertyBlock(block);
            block.SetFloat("_Reveal",1);block.SetFloat("_Opacity",1);block.SetFloat("_WaveAmplitude",0);
            block.SetFloat("_ChurnSpeed",.85f);block.SetFloat("_VortexDensity",1);block.SetFloat("_VortexStrength",1.3f);block.SetFloat("_EdgeStrength",.8f);
            Color32[] first=null,last=null;
            foreach(float age in new[]{0f,2f,4f})
            {
                block.SetFloat("_Age",age);surface.SetPropertyBlock(block);camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();
                if(first==null)first=image.GetPixels32();last=image.GetPixels32();
                System.IO.File.WriteAllBytes("Logs/Ink-diffusion-surface-"+age.ToString("F0")+".png",image.EncodeToPNG());
            }
            int changed=0;
            for(int y=220;y<780;y++)for(int x=220;x<780;x++)
            {
                int n=y*1000+x;
                int d=Math.Abs(first[n].r-last[n].r)+Math.Abs(first[n].g-last[n].g)+Math.Abs(first[n].b-last[n].b);
                if(d>6)changed++;
            }
            if(changed<25000)throw new Exception("The horizontal ink pattern is not moving: "+changed+" changed pixels");
            return $"Surface flow: {changed}/313600 interior pixels changed over 4s with vertex motion disabled";
        }
        finally
        {
            surface.gameObject.layer=oldLayer;surface.SetPropertyBlock(original);camera.targetTexture=null;RenderTexture.active=active;
            UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);
        }
    }
    public static async Task<string> Preview()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Enter Play Mode first");
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();var boss=range.Boss;
        boss.moveSpeed=0;boss.dodgeChance=0;var settings=boss.inkAvatarUltimate;
        settings.roarSeconds=.8f;settings.avatar.appearSeconds=.6f;settings.sweep.seconds=.6f;settings.sweep.interval=.1f;settings.slam.seconds=.8f;
        settings.sweep.firstKnockback=false;settings.sweep.secondKnockback=false;settings.airCombatProjectiles=false;settings.sea.duration=80;
        PlayerHealth.Find().SetPracticeProtection(boss,true);
        var camera=Camera.main;var orbit=camera.GetComponent<ThirdPersonCamera>();if(orbit)orbit.enabled=false;
        var brain=camera.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;
        boss.SelectSkill(9);
        try
        {
            await Until(()=>boss.AvatarUltimate && boss.AvatarUltimate.GroundIsHazardous,20);await Task.Delay(3000);
            var owner=boss.AvatarUltimate;
            var motion=owner.GetComponentInChildren<CalligraphyInkSeaMotion>();
            var filter=motion.GetComponentsInChildren<MeshFilter>().First(f=>f.sharedMesh.name=="Branching airborne ink diffusion");
            var mesh=filter.sharedMesh;Vector3 point=owner.ImpactPoint;
            string surfaceResult=SurfaceFlow(motion);
            camera.fieldOfView=55;camera.farClipPlane=600;
            camera.transform.SetPositionAndRotation(point+boss.transform.forward*50+boss.transform.right*16+Vector3.up*9,
                Quaternion.LookRotation((point+Vector3.up*2)-(point+boss.transform.forward*50+boss.transform.right*16+Vector3.up*9)));
            int minActive=int.MaxValue,maxActive=0;var first=mesh.vertices;int samples=0;
            for(int frame=0;frame<20;frame++)
            {
                // Each site's first colour carries its independent fade envelope (140 vertices per site).
                var colours=mesh.colors32;int active=0;
                for(int i=0;i<colours.Length;i+=140)if(colours[i].a>32)active++;
                minActive=Math.Min(minActive,active);maxActive=Math.Max(maxActive,active);samples++;
                if(frame==0 || frame==5 || frame==10)Capture("sea-"+frame,camera);
                await Task.Delay(200);
            }
            var last=mesh.vertices;
            if(!last.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)))throw new Exception("Non-finite diffusion geometry");
            if(minActive<settings.sea.airPlumeCount*.4f)throw new Exception("Airborne ink has a coverage gap: "+minActive);
            if(last.Where((v,i)=>Vector3.Distance(v,first[i])>.15f).Count()<first.Length/2)throw new Exception("Airborne ink is static");
            var surface=motion.GetComponent<Renderer>();var block=new MaterialPropertyBlock();surface.GetPropertyBlock(block);
            if(block.GetFloat("_WaveAmplitude")>.35f)throw new Exception("Thick waves still enabled");
            var plume=filter.GetComponent<Renderer>();
            if(plume.sharedMaterial.shader.name!="Project/VFX/CalligraphyInkDiffusion")throw new Exception("Wrong diffusion material");
            camera.transform.SetPositionAndRotation(point+boss.transform.forward*19+boss.transform.right*9+Vector3.up*3.5f,
                Quaternion.LookRotation(point+boss.transform.right*3+Vector3.up*3-(point+boss.transform.forward*19+boss.transform.right*9+Vector3.up*3.5f)));
            Capture("close",camera);
            int vertexCount=mesh.vertexCount;
            settings.sea.duration=1;
            await Until(()=>owner.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.Dissolve,5);
            await Task.Delay(900);
            surface.GetPropertyBlock(block);float seaOpacity=block.GetFloat("_Opacity");
            plume.GetPropertyBlock(block);float airOpacity=block.GetFloat("_Opacity");
            if(Mathf.Abs(airOpacity-seaOpacity*settings.sea.airOpacity)>.03f)throw new Exception("Diffusion is not fading with its sea");
            await Until(()=>owner.Completed,8);await Task.Delay(80);
            if(filter || plume)throw new Exception("Natural finish leaked diffusion geometry");
            return $"PASS: {surfaceResult}; {samples} continuity samples, {minActive}–{maxActive}/{settings.sea.airPlumeCount} sites visible, animated finite branching geometry ({vertexCount} vertices); synchronized dissolve and cleanup; screenshots in Logs/Ink-diffusion-*.png";
        }
        finally{boss.CancelCast();PlayerHealth.Find().SetPracticeProtection(boss,false);Time.timeScale=1;}
    }
}
