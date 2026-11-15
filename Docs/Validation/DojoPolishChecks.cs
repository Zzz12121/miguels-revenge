using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
public static class DojoPolishChecks
{
    static IEnumerator MeleeAndCompletion(DojoCalligraphyEncounter encounter,Action done)
    {yield return (IEnumerator)typeof(DojoCalligraphyEncounter).GetMethod("Cast",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(encounter,new object[]{2,false});done();}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task Until(Func<bool> ok,float seconds,string message)
    {float end=Time.realtimeSinceStartup+seconds;while(!ok()){Check(Time.realtimeSinceStartup<end,message);await Task.Delay(12);}}
    static async Task Wait(float seconds){float end=Time.time+seconds;await Until(()=>Time.time>=end,10,"Clock stopped");}
    static void Shot(string name,Vector3 at,Vector3 target,float fov=55)
    {
        var go=new GameObject("Polish runtime capture");var camera=go.AddComponent<Camera>();go.AddComponent<UniversalAdditionalCameraData>().requiresDepthTexture=true;
        camera.nearClipPlane=.03f;camera.fieldOfView=fov;go.transform.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));
        var rt=RenderTexture.GetTemporary(1280,800,24);var prior=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1280,800,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,800),0,0);tex.Apply();File.WriteAllBytes("output/dojo-building/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
        finally{camera.targetTexture=null;RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(go);}
    }
    public static async Task<string> Run()
    {
        var cut=Object.FindAnyObjectByType<DojoOpeningCutscene>();cut.enabled=false;var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();var cc=p.GetComponent<CharacterController>();
        var encounter=cut.master.GetComponent<DojoCalligraphyEncounter>();var boss=cut.master.GetComponent<DojoMasterBoss>();var hp=boss.GetComponent<BossHealth>();var escape=boss.GetComponent<DojoElderEscape>();
        float scale=Time.timeScale;try
        {
            Time.timeScale=2;cc.enabled=false;p.transform.position=new Vector3(0,1,58.7f);cc.enabled=true;motor.ResetMotion();p.GetComponent<DojoTestMode>().SetActive(true);
            cut.master.SetPositionAndRotation(new Vector3(0,1.2f,76.6f),Quaternion.Euler(0,180,0));encounter.StartCoroutine(encounter.TransformAfterDialogue(null));
            await Until(()=>Object.FindAnyObjectByType<DojoBrushManifestation>(),8,"No progressive brush assembly");
            var assembly=Object.FindAnyObjectByType<DojoBrushManifestation>();float early=assembly.Progress;var fullScale=assembly.transform.localScale;
            await Wait(.35f);Check(assembly&&assembly.Progress>early&&assembly.Progress<1,"Brush must assemble progressively");Check(assembly.transform.localScale==fullScale,"Brush reveal must not scale a complete solid brush");
            var reveal=encounter.Form.transform;Shot("68-brush-early-ink-reassembly",reveal.position+reveal.forward*5+Vector3.up*2.4f,reveal.position+Vector3.up*1.8f,55);
            await Until(()=>assembly&&assembly.Progress>.7f,4,"Brush assembly did not advance");Shot("69-brush-late-ink-reassembly",reveal.position+reveal.forward*5+Vector3.up*2.4f,reveal.position+Vector3.up*1.8f,55);
            await Until(()=>encounter.Transformed,8,"No combat form");
            boss.BeginFight();encounter.StopAllCoroutines();var form=encounter.Form;form.CancelCast();
            var random=UnityEngine.Random.state;UnityEngine.Random.InitState(17395);int melee=0,ranged=0;
            for(int i=0;i<2000;i++){int skill=encounter.SelectTeleportFollowup();if(skill==2)melee++;else if(skill==1)ranged++;else throw new Exception("Unexpected teleport follow-up");}
            UnityEngine.Random.state=random;Check(melee>900&&melee<1100&&ranged>900&&ranged<1100,"Teleport choice must be 50/50");
            encounter.StartCoroutine(encounter.TeleportBehindPlayer());await Until(()=>encounter.Phase=="Teleport ready",5,"No teleport completion");
            bool meleeFinished=false;encounter.StartCoroutine(MeleeAndCompletion(encounter,()=>meleeFinished=true));await Until(()=>Object.FindObjectsByType<CalligraphyClone>().Count(c=>c.IsFlanking)==2,4,"Flank clones missing");
            var clones=Object.FindObjectsByType<CalligraphyClone>().Where(c=>c.IsFlanking).ToArray();Check(clones.All(c=>Mathf.Abs(c.ChaseSpeed-form.inkMelee.cloneMoveSpeed*2*.7f)<.001f),"Clone speed must be 0.7 of previous doubled speed");float chase=clones[0].ChaseSpeed;
            await Until(()=>encounter.Phase=="Waiting for ink doubles to disappear",8,"Boss did not wait for ink clones");Check(!meleeFinished&&form.EncounterClonesRemaining==2,"Cannot finish melee while clones remain");
            foreach(var clone in clones)clone.Dissolve();await Wait(.15f);Check(!meleeFinished,"Boss must wait for the visible dissolve, not only clone death flag");
            await Until(()=>meleeFinished,5,"Melee did not finish after clone cleanup");Check(form.EncounterClonesRemaining==0,"Wait finished before clones disappeared");
            Check(form.CastEncounterSkill(1),"Four-stroke follow-up must start");await Wait(.4f);Check(form.SelectedSkill==1&&form.IsCasting,"Four-stroke skill selection incorrect");form.CancelCast();
            hp.SetHealth(hp.maxHealth*.5f);await Until(()=>escape.Cloud&&escape.Phase=="Flying toward main house",7,"No half-health escape");
            var cloud=escape.Cloud;Check(cloud.OrbitCount==10,"Expected ten independently orbiting original effects");
            Check(cloud.NativeScales.Max()/cloud.NativeScales.Min()>1.5f,"Native effects must have varied sizes");
            Check(!cloud.GetComponentsInChildren<MeshFilter>().Any(),"Escape must not have a custom sphere mesh");
            Check(cloud.GetComponentsInChildren<ParticleSystemRenderer>().Where(r=>r.sharedMaterial).All(r=>r.sharedMaterial.shader.name.StartsWith("Piloto Studio/")),"Escape must use only original package shaders");
            var orbiters=Enumerable.Range(0,cloud.OrbitCount).Select(i=>cloud.transform.GetChild(i)).ToArray();var startOrbit=orbiters.Select(o=>o.localPosition).ToArray();
            await Wait(.35f);Check(orbiters.Select((o,i)=>Vector3.Distance(startOrbit[i],o.localPosition)).Max()>.1f,"Effects must orbit independently of forward travel");
            Check(cloud.TailBursts>0,"Extra native tail effects must be left behind");
            await Wait(.3f);var center=cloud.transform.position;Shot("66-flowing-black-white-ink-ball",center+new Vector3(2,1.1f,-3),center,48);
            await Wait(.18f);center=cloud.transform.position;Shot("67-moving-ink-ball-and-half-second-tail",center+new Vector3(3,1.6f,2),center,54);
            Check(cloud.BlackEffects>0&&cloud.WhiteEffects>0,"Black and white accents must coexist");
            Check(cloud.GetComponentsInChildren<ParticleSystem>().All(ps=>ps.main.startLifetime.constantMax<=.501f),"Every ink particle lifetime must stay within half a second");
            await Until(()=>escape.Phase=="Returning to player",18,"Never reached first torii");
            Check(cloud.gameObject.activeInHierarchy&&!escape.CloudHiddenAfterCameraReturn,"Ink must remain visible when return shot begins");var departure=cloud.transform.position;
            await Wait(.7f);Check(cloud.gameObject.activeInHierarchy&&Vector3.Distance(departure,cloud.transform.position)>1,"Ink must continue travelling during camera return");float continued=Vector3.Distance(departure,cloud.transform.position);
            await Until(()=>escape.CloudHiddenAfterCameraReturn&&StoryDialogue.Active,5,"Ink must be removed only after player-camera handoff");
            Check(!cloud.gameObject.activeSelf,"Old ink actor must be hidden after handoff");
            typeof(StoryDialogue).GetMethod("Finish",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(Object.FindAnyObjectByType<StoryDialogue>(),null);await Until(()=>escape.Completed,3,"Finale did not finish");Check(!hp.ShowBar&&motor.inputEnabled&&!motor.ActionsLocked,"Finale must release controls and hide bar");
            var result=new{success=true,progressiveBrushAssembly=true,cloneDissolveWait=true,teleportMelee=melee,teleportFourStroke=ranged,cloneSpeed=chase,relativeSpeed=.7f,tailLifetime=.5f,nativeOnly=true,orbitingOriginalEffects=cloud.OrbitCount,continuedDuringCameraReturn=continued,removedAfterPlayerCamera=true};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/dojo-polish-checks.json",json);return json;
        }
        finally{if(!StoryDialogue.Active)Time.timeScale=scale;}
    }
}
