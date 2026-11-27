using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

public static class InkboundPassThroughChecks
{
    const string Folder="output/inkbound-town/pass-through-impact";
    public static string UpdateScene()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying||scene.path!=InkboundTownBuilder.ScenePath)throw new Exception("Open the saved town outside Play mode first.");
        var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();float speed=settings.inkPlaybackSpeed;
        EditorSceneManager.SaveScene(scene);
        InkboundTownBuilder.UpdatePassableFurnishings();
        settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();settings.inkPlaybackSpeed=speed;
        if(!settings.GetComponent<InkboundPassThroughImpact>())settings.gameObject.AddComponent<InkboundPassThroughImpact>();
        EditorUtility.SetDirty(settings);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return "Saved individual furnishings and elder hit playback; preserved speed "+speed;
    }
    public static string SaveInkMaterial()
    {
        var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();
        var encounter=Object.FindAnyObjectByType<InkboundTownEncounter>();
        settings.passThroughInkMaterial=encounter.elder.ink.ribbonMaterial;
        EditorUtility.SetDirty(settings);EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        return AssetDatabase.GetAssetPath(settings.passThroughInkMaterial);
    }
    public static string CheckSavedFurnishings()
    {
        int fixedBoards=0,checkedMeshes=0;
        for(int type=0;type<8;type++)for(int variant=0;variant<2;variant++)
        {
            string path="Assets/_Project/Prefabs/Environment/InkboundTown/PF_IT_V3_Shop"+type+"_"+variant+".prefab";
            var root=PrefabUtility.LoadPrefabContents(path);bool changed=false;
            try
            {
                if(type==7)foreach(var filter in root.GetComponentsInChildren<MeshFilter>().Where(f=>f.transform.parent==root.transform&&f.name=="Bevelled joinery board").ToArray())
                {Object.DestroyImmediate(filter.gameObject);changed=true;fixedBoards++;}
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {if(!filter.sharedMesh||!AssetDatabase.Contains(filter.sharedMesh))throw new Exception("Unpersisted mesh: "+path+" / "+filter.name);checkedMeshes++;}
                if(changed)PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        return "Verified persisted meshes: "+checkedMeshes+"; removed duplicate facade boards: "+fixedBoards;
    }
    static void Shot(string name,Vector3 point,Vector3 target)
    {
        var c=new GameObject("Temporary pass-through check camera").AddComponent<Camera>();c.CopyFrom(Camera.main);c.enabled=false;c.fieldOfView=55;
        c.transform.SetPositionAndRotation(point,Quaternion.LookRotation(target-point));
        var rt=RenderTexture.GetTemporary(1280,720,24);var old=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{Object.FindAnyObjectByType<InkboundWaterReflection>()?.RefreshFor(c);c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(image);Object.Destroy(c.gameObject);}
    }
    static void Warp(PlayerHealth p,Vector3 point)
    {var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=point;p.GetComponent<ThirdPersonController>().ResetMotion();cc.enabled=true;}
    static bool Hidden(InkboundEcho echo)
    {var owner=InkboundPassThroughImpact.Owner(echo);return owner.GetComponentsInChildren<Renderer>().Where(r=>InkboundPassThroughImpact.Eligible(r,owner)).All(r=>r.forceRenderingOff);}
    static float Rebuild(InkboundEcho echo)
    {var block=new MaterialPropertyBlock();echo.GetComponentInChildren<SkinnedMeshRenderer>().GetPropertyBlock(block);return block.GetFloat("_InkRebuild");}
    static Vector3[] LiveOrigins(CalligraphyInkField ink)
        =>ink.GetComponentsInChildren<LineRenderer>().Where(l=>l.enabled).Select(l=>l.useWorldSpace?l.GetPosition(0):l.transform.TransformPoint(l.GetPosition(0))).Distinct().ToArray();
    public static async Task<string> MultiPointPreview()
    {
        Directory.CreateDirectory(Folder);var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();float speed=settings.inkPlaybackSpeed;
        var player=PlayerHealth.Find();var motor=player.GetComponent<ThirdPersonController>();bool input=motor.inputEnabled;Vector3 position=player.transform.position;
        var echo=Object.FindObjectsByType<InkboundEcho>().First(e=>e.name=="Dock net repair");
        var actor=Object.FindObjectsByType<InkboundEcho>().First(e=>e.name.StartsWith("Market passing life"));
        var animator=actor.GetComponentInChildren<Animator>();var playback=settings.ImpactPlayer;float health=player.Health;
        var encounter=Object.FindAnyObjectByType<InkboundTownEncounter>();bool encounterEnabled=encounter.enabled,combatEnabled=encounter.elder.enabled;
        try
        {
            motor.inputEnabled=false;encounter.enabled=false;encounter.elder.CancelCast();encounter.elder.enabled=false;
            playback.enabled=false;playback.enabled=true;await Task.Delay(50);
            // Inspect emitted geometry at birth; these are real enabled ink ribbons, not planned anchors.
            playback.Trigger(actor,Vector3.forward);
            var actorInk=Object.FindObjectsByType<CalligraphyInkField>().First(f=>f.name=="Pass-through elder hit ink");
            var actorOrigins=LiveOrigins(actorInk);
            Vector3 head=animator.GetBoneTransform(HumanBodyBones.Head).position,left=animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,right=animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
            Vector3 belly=Vector3.Lerp(animator.GetBoneTransform(HumanBodyBones.Hips).position,animator.GetBoneTransform(HumanBodyBones.Chest).position,.35f);
            var body=new{points=actorOrigins.Length,ribbons=actorInk.GetComponentsInChildren<LineRenderer>().Count(l=>l.enabled),
                head=actorOrigins.Min(p=>Vector3.Distance(p,head))<.01f,belly=actorOrigins.Min(p=>Vector3.Distance(p,belly))<.01f,
                leftFoot=actorOrigins.Min(p=>Vector3.Distance(p,left))<.01f,rightFoot=actorOrigins.Min(p=>Vector3.Distance(p,right))<.01f};
            Warp(player,echo.transform.position+new Vector3(0,1.05f,-5));playback.Trigger(echo,Vector3.forward);await Task.Delay(450);
            Shot("09-multiple-body-ink",echo.transform.position+new Vector3(-2.7f,1.5f,-2.7f),echo.GetComponentInChildren<SkinnedMeshRenderer>().bounds.center);
            var table=Object.FindObjectsByType<InkboundPassableGroup>().First(g=>g.name=="Complete table");
            var tableEcho=table.GetComponentInChildren<InkboundEcho>();playback.Trigger(tableEcho,Vector3.forward);
            var field=Object.FindObjectsByType<CalligraphyInkField>().Where(f=>f.name=="Pass-through elder hit ink").OrderBy(f=>Vector3.Distance(f.transform.position,tableEcho.GetComponent<Renderer>().bounds.center)).First();
            var origins=LiveOrigins(field);var coverage=new Bounds(origins[0],Vector3.zero);foreach(var p in origins)coverage.Encapsulate(p);
            var furniture=new{points=origins.Length,ribbons=field.GetComponentsInChildren<LineRenderer>().Count(l=>l.enabled),coverage=coverage.size.ToString("F3"),spreadAcrossTable=coverage.size.x>.6f&&coverage.size.z>.5f};
            await Task.Delay(450);Vector3 center=tableEcho.GetComponent<Renderer>().bounds.center;
            Warp(player,center+new Vector3(0,2,-5));Shot("10-multiple-furniture-ink",center+new Vector3(-2.5f,1.7f,-2.8f),center);
            bool hidden=Hidden(actor)&&Hidden(echo)&&Hidden(tableEcho);float start=Time.time;
            while(playback.ActiveCount>0&&Time.time-start<12)await Task.Delay(30);
            bool restored=!playback.IsPlaying(actor.transform)&&!playback.IsPlaying(echo.transform)&&!playback.IsPlaying(table.transform)&&!Hidden(actor)&&!Hidden(echo)&&!Hidden(tableEcho);
            var report=new{body,furniture,hidden,restored,healthUnchanged=player.Health==health,preservedSpeed=settings.inkPlaybackSpeed};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/multiple-points.json",json);return json;
        }
        finally{playback.enabled=false;playback.enabled=true;settings.inkPlaybackSpeed=speed;motor.inputEnabled=input;encounter.elder.enabled=combatEnabled;encounter.enabled=encounterEnabled;Warp(player,position);}
    }
    public static async Task<string> Preview()
    {
        Directory.CreateDirectory(Folder);var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();float speed=settings.inkPlaybackSpeed;
        var player=PlayerHealth.Find();var motor=player.GetComponent<ThirdPersonController>();bool input=motor.inputEnabled;Vector3 position=player.transform.position;
        var echo=Object.FindObjectsByType<InkboundEcho>().First(e=>e.name=="Dock net repair");
        var skin=echo.GetComponentInChildren<SkinnedMeshRenderer>();var playback=settings.ImpactPlayer;
        try
        {
            settings.inkPlaybackSpeed=1;motor.inputEnabled=false;Warp(player,echo.transform.position+new Vector3(0,1.05f,-5));
            Vector3 point=echo.transform.position+new Vector3(-2.7f,1.5f,-2.7f),target=skin.bounds.center;
            Shot("05-net-worker-before",point,target);playback.Trigger(echo,Vector3.right);await Task.Delay(600);
            Shot("06-net-worker-black-ink",point,target);bool shot=false;float start=Time.time;
            while(playback.IsPlaying(echo.transform)&&Time.time-start<8)
            {if(!shot&&Rebuild(echo)>.4f&&Rebuild(echo)<.8f){shot=true;Shot("07-net-worker-reassembly",point,skin.bounds.center);}await Task.Delay(20);}
            Shot("08-net-worker-restored",point,skin.bounds.center);return "Saved unobstructed native game-camera views of black ink and reassembly.";
        }
        finally{settings.inkPlaybackSpeed=speed;motor.inputEnabled=input;Warp(player,position);}
    }
    public static async Task<string> Verify()
    {
        Directory.CreateDirectory(Folder);
        var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();var player=PlayerHealth.Find();var motor=player.GetComponent<ThirdPersonController>();
        Vector3 position=player.transform.position;bool input=motor.inputEnabled;float originalSpeed=settings.inkPlaybackSpeed,health=player.Health;
        var encounter=Object.FindAnyObjectByType<InkboundTownEncounter>();bool encounterEnabled=encounter.enabled,combatEnabled=encounter.elder.enabled;
        var echo=Object.FindObjectsByType<InkboundEcho>().First(e=>e.name.StartsWith("Market passing life"));
        var skin=echo.GetComponentInChildren<SkinnedMeshRenderer>();var playback=settings.ImpactPlayer;
        try
        {
            motor.inputEnabled=false;encounter.enabled=false;encounter.elder.CancelCast();encounter.elder.enabled=false;
            settings.inkPlaybackSpeed=1;Warp(player,echo.transform.position+new Vector3(0,1.05f,-3));await Task.Delay(250);
            Shot("01-resident-before",echo.transform.position+new Vector3(-1.8f,1.8f,-4),skin.bounds.center);
            int before=playback.TriggerCount;
            // Cross the actual resident body over several frames; an outside point alone cannot trigger.
            for(int i=0;i<=16;i++)
            {Vector3 at=echo.transform.position;Warp(player,new Vector3(at.x,1.05f,at.z+Mathf.Lerp(-1.4f,1.4f,i/16f)));await Task.Delay(50);}
            bool triggered=playback.TriggerCount>before&&playback.IsPlaying(echo.transform);
            bool hidden=Hidden(echo);Vector3 poseAt=echo.transform.position;
            var fx=Object.FindObjectsByType<CalligraphyInkField>().Where(f=>f.name=="Pass-through elder hit ink").ToArray();
            bool originalBlackInk=fx.Length>0&&fx.All(f=>f.ribbonMaterial==encounter.elder.ink.ribbonMaterial);
            bool effectAtObject=fx.Any(f=>Vector3.Distance(f.transform.position,skin.bounds.center)<.1f);
            Shot("02-resident-hidden-in-hit-ink",echo.transform.position+new Vector3(-1.8f,1.8f,-4),skin.bounds.center);
            bool stayedHidden=true,rebuildSeen=false;float start=Time.time;
            while(playback.IsPlaying(echo.transform)&&Time.time-start<25)
            {
                var residentFx=fx.Where(f=>f).ToArray();
                if(residentFx.Any(f=>f.ActiveCount>0)&&!Hidden(echo))stayedHidden=false;
                float pigment=Rebuild(echo);
                if(!rebuildSeen&&pigment>.2f&&pigment<.9f)
                {rebuildSeen=true;Shot("03-resident-reassembling",echo.transform.position+new Vector3(-1.8f,1.8f,-4),skin.bounds.center);}
                await Task.Delay(20);
            }
            bool restored=!Hidden(echo)&&!playback.IsPlaying(echo.transform)&&Mathf.Abs(Rebuild(echo)-1)<.001f;
            bool animationContinued=Vector3.Distance(poseAt,echo.transform.position)>.05f;
            Shot("04-resident-returned",echo.transform.position+new Vector3(-1.8f,1.8f,-4),skin.bounds.center);
            var stools=Object.FindObjectsByType<InkboundPassableGroup>().Where(g=>g.name=="Complete stool"&&g.GetComponentInChildren<InkboundEcho>()).ToArray();
            var stool=stools.First();var stoolEcho=stool.GetComponentInChildren<InkboundEcho>();
            var sibling=stools.First(s=>s!=stool&&s.transform.parent==stool.transform.parent);
            bool propTriggered=playback.Trigger(stoolEcho,Vector3.forward),propHidden=Hidden(stoolEcho);
            bool siblingVisible=sibling.GetComponentsInChildren<Renderer>().All(r=>!r.forceRenderingOff);
            // Disable the local adapter while active to check all visual overrides are restored.
            playback.enabled=false;bool cleanup=!Hidden(stoolEcho);playback.enabled=true;
            bool noOldFilaments=!Object.FindObjectsByType<MeshRenderer>().Any(r=>r.name=="Returning ink filaments"&&r.enabled);
            var report=new{triggered,hidden,stayedHidden,originalBlackInk,rebuildSeen,restored,animationContinued,effectAtObject,healthUnchanged=player.Health==health,
                individualStools=stools.Length,propTriggered,propHidden,siblingVisible,cleanup,noOldFilaments,preservedSpeed=originalSpeed};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/checks.json",json);return json;
        }
        finally
        {
            playback.enabled=false;playback.enabled=true;settings.inkPlaybackSpeed=originalSpeed;motor.inputEnabled=input;
            encounter.elder.enabled=combatEnabled;encounter.enabled=encounterEnabled;Warp(player,position);
        }
    }
}
