using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using Object=UnityEngine.Object;

public static class InkboundTownV3GameplayChecks
{
    const string Folder="output/inkbound-town/v3/gameplay";
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static Texture2D videoPixels;
    static int frame;
    static float videoStarted,nextFrame;
    static readonly List<object> times=new();
    static void Progress(string phase)=>File.WriteAllText(Folder+"/progress.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,time=Time.time}));
    static void Warp(PlayerHealth p,Vector3 point)
    {
        var cc=p.GetComponent<CharacterController>();bool was=cc.enabled;cc.enabled=false;Vector3 previous=p.transform.position;p.transform.position=point;p.GetComponent<ThirdPersonController>().ResetMotion();cc.enabled=was;
        var orbit=Camera.main.GetComponent<ThirdPersonCamera>();if(orbit){orbit.WarpWithTarget(point-previous);orbit.SnapToTarget();}
    }
    static void Record()
    {
        if(Time.unscaledTime<nextFrame)return;nextFrame=Time.unscaledTime+1f/12;
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=Resources.FindObjectsOfTypeAll(type).FirstOrDefault();if(view==null)return;
        var target=(RenderTexture)type.GetField("m_RenderTexture",Private).GetValue(view);if(!target)return;
        if(!videoPixels)videoPixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        var old=RenderTexture.active;
        try
        {
            RenderTexture.active=target;videoPixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);videoPixels.Apply();
            if(SystemInfo.graphicsUVStartsAtTop)
            {var data=videoPixels.GetRawTextureData<byte>();int row=target.width*3;for(int y=0;y<target.height/2;y++)for(int x=0;x<row;x++){int a=y*row+x,b=(target.height-1-y)*row+x;byte t=data[a];data[a]=data[b];data[b]=t;}videoPixels.Apply();}
            string name="frame-"+(frame++).ToString("D4")+".jpg";File.WriteAllBytes(Folder+"/"+name,videoPixels.EncodeToJPG(82));times.Add(new{name,seconds=Time.unscaledTime-videoStarted});
        }
        finally{RenderTexture.active=old;}
    }
    static void CloseShot(string name,Vector3 subject)
    {
        var c=new GameObject("Temporary echo inspection camera").AddComponent<Camera>();c.CopyFrom(Camera.main);c.enabled=false;c.fieldOfView=60;
        Vector3 p=subject+new Vector3(-1.3f,1.4f,-3.1f);c.transform.SetPositionAndRotation(p,Quaternion.LookRotation(subject+Vector3.up-p));
        var rt=RenderTexture.GetTemporary(1280,720,24);var old=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(image);Object.Destroy(c.gameObject);}
    }
    static async Task<bool> WalkTo(PlayerHealth p,ThirdPersonController motor,Vector3 target)
    {
        if(!NavMesh.SamplePosition(p.transform.position,out var a,2,NavMesh.AllAreas)||!NavMesh.SamplePosition(target,out var b,2,NavMesh.AllAreas))return false;
        var path=new NavMeshPath();if(!NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)return false;
        motor.aiControlled=true;motor.inputEnabled=true;
        try
        {
            foreach(var corner in path.corners.Skip(1))
            {
                float until=Time.time+55;
                while(Time.time<until&&Vector3.ProjectOnPlane(corner-p.transform.position,Vector3.up).magnitude>.38f)
                {
                    Vector3 d=Vector3.ProjectOnPlane(corner-p.transform.position,Vector3.up).normalized;
                    motor.SetAIInput(d);
                    var camera=Camera.main.GetComponent<ThirdPersonCamera>();camera.ClearLock();camera.yaw=Mathf.MoveTowardsAngle(camera.yaw,Quaternion.LookRotation(d).eulerAngles.y,160*Time.deltaTime);camera.pitch=10;
                    Record();await Task.Delay(20);
                }
                if(Vector3.ProjectOnPlane(corner-p.transform.position,Vector3.up).magnitude>1)return false;
            }
            return true;
        }
        finally{motor.SetAIInput(Vector3.zero);motor.inputEnabled=true;}
    }
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");Directory.CreateDirectory(Folder);
        var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();bool input=motor.inputEnabled,ai=motor.aiControlled;
        var director=Object.FindAnyObjectByType<InkboundTownEncounter>();var boss=director.elder;var health=director.Health;
        var protection=new GameObject("Temporary integration check protection");bool autoSave=GameManager.Instance&&GameManager.Instance.autoSaveOnLevelStart;
        var results=new Dictionary<string,object>();
        try
        {
            if(GameManager.Instance)GameManager.Instance.autoSaveOnLevelStart=false;
            p.SetPracticeProtection(protection,true);motor.inputEnabled=false;
            typeof(Editor).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear").Invoke(null,null);
            Progress("Actual elder pursuit across bridge");
            Warp(p,new Vector3(11,1.05f,-22));
            var camera=Camera.main.GetComponent<ThirdPersonCamera>();camera.yaw=270;camera.pitch=12;camera.SnapToTarget();
            NavMesh.SamplePosition(new Vector3(-11,0,-22),out var bridgeStart,2,NavMesh.AllAreas);director.agent.Warp(bridgeStart.position);boss.transform.position=bridgeStart.position;
            director.BeginFight();health.TakeDamage(137,boss.transform.position+Vector3.up);float remaining=health.Health;
            typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(director,Time.time+60);
            var trajectory=new List<string>();float until=Time.time+15,highest=-100;videoStarted=Time.unscaledTime;frame=0;times.Clear();
            while(Time.time<until&&Vector3.ProjectOnPlane(p.transform.position-boss.transform.position,Vector3.up).magnitude>3.1f)
            {trajectory.Add(boss.transform.position.ToString("F3"));highest=Mathf.Max(highest,boss.transform.position.y);Record();await Task.Delay(40);}
            results["bridgePursuit"]=new{crossed=boss.transform.position.x>6,highest,healthPreserved=Mathf.Abs(health.Health-remaining)<.01f,position=boss.transform.position.ToString(),trajectory};
            Warp(p,new Vector3(-12,1.05f,-22));camera.yaw=90;camera.SnapToTarget();until=Time.time+15;
            while(Time.time<until&&Vector3.ProjectOnPlane(p.transform.position-boss.transform.position,Vector3.up).magnitude>3.1f){Record();await Task.Delay(40);}
            results["reverseBridgePursuit"]=new{crossed=boss.transform.position.x< -7,healthPreserved=Mathf.Abs(health.Health-remaining)<.01f};
            typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(director,Time.time+.1f);
            int before=director.Casts;until=Time.time+7;
            while(Time.time<until){Record();await Task.Delay(40);}
            results["existingSkills"]=new{casts=director.Casts-before,boss.Active,state=director.State};
            Progress("City zones preserve the same elder health");
            var id=boss.GetEntityId();var zones=new List<object>();
            foreach(var point in new[]{new Vector3(-44,0,0),new Vector3(-11,0,-30),new Vector3(28,.18f,-24),new Vector3(-57,0,-22)})
            {
                Progress("Walking and fighting at "+point);
                boss.CancelCast();typeof(InkboundTownEncounter).GetField("awaitingRecovery",Private).SetValue(director,false);typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(director,Time.time+1000);
                bool walked=await WalkTo(p,motor,point);until=Time.time+25;
                while(Time.time<until&&Vector3.ProjectOnPlane(p.transform.position-boss.transform.position,Vector3.up).magnitude>7){Record();await Task.Delay(35);}
                Vector3 toward=Vector3.ProjectOnPlane(boss.transform.position-p.transform.position,Vector3.up);
                camera.yaw=Quaternion.LookRotation(toward).eulerAngles.y;camera.pitch=12;camera.SnapToTarget();await Task.Delay(250);
                typeof(ThirdPersonCamera).GetMethod("AcquireLock",Private).Invoke(camera,new object[]{false});
                typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(director,Time.time+.1f);before=director.Casts;until=Time.time+5;
                while(Time.time<until){Record();await Task.Delay(35);}
                zones.Add(new{position=p.transform.position.ToString(),bossPosition=boss.transform.position.ToString(),path=director.agent.pathStatus.ToString(),walked,sameActor=id==boss.GetEntityId(),health=health.Health,sameHealth=Mathf.Abs(health.Health-remaining)<.01f,casts=director.Casts-before,lockedToElder=camera.LockedTarget&&camera.LockedTarget.IsChildOf(boss.transform)});
            }
            results["crossZoneContinuity"]=zones;
            Progress("Local ink displacement and return to walking animation");
            director.enabled=false;boss.StopBattle();director.agent.isStopped=true;
            foreach(var s in boss.GetComponentsInChildren<InkboundDisturbanceSource>())s.enabled=false;
            var npc=Object.FindObjectsByType<NpcRoutine>().First(n=>n.name.StartsWith("Market passing life"));var echo=npc.GetComponent<InkboundEcho>();
            camera.ClearLock();camera.yaw=0;camera.pitch=10;Warp(p,npc.transform.position+new Vector3(-1.7f,1.05f,0));await Task.Delay(250);CloseShot("01-echo-intact",npc.transform.position);
            float start=Time.time;int contacts=echo.Contacts;float peak=0;bool contactShot=false;
            while(Time.time-start<1.15f)
            {
                float t=(Time.time-start)/1.15f;Vector3 desired=npc.transform.position+new Vector3(Mathf.Lerp(-1.4f,1.4f,t),1.05f,0);
                p.GetComponent<CharacterController>().Move(desired-p.transform.position);peak=Mathf.Max(peak,echo.Disturbance);
                if(t>.47f&&!contactShot){CloseShot("02-echo-contact",npc.transform.position);contactShot=true;}
                Record();await Task.Delay(16);
            }
            CloseShot("03-echo-flow",npc.transform.position);Warp(p,npc.transform.position+new Vector3(5,1.05f,0));Vector3 animatedFrom=npc.transform.position;
            until=Time.time+4f;while(Time.time<until){Record();await Task.Delay(35);}CloseShot("04-echo-return",npc.transform.position);
            var samples=(Vector3[])typeof(InkboundEcho).GetField("positions",Private).GetValue(echo);var targets=(Vector3[])typeof(InkboundEcho).GetField("targets",Private).GetValue(echo);
            float error=samples==null?0:samples.Zip(targets,(a,b)=>Vector3.Distance(a,b)).DefaultIfEmpty().Max();
            results["animatedEcho"]=new{contacts=echo.Contacts-contacts,peak,echo.MaximumOffset,returned=echo.Disturbance<.012f&&error<.12f,error,animationContinued=Vector3.Distance(animatedFrom,npc.transform.position)>.1f};
            Progress("Death and respawn use the existing player lifecycle");
            director.enabled=true;p.SetPracticeProtection(protection,false);p.Kill("Inkbound V3 integration check");
            until=Time.unscaledTime+16;bool died=p.IsDead;
            while(p&&p.IsDead&&Time.unscaledTime<until)await Task.Delay(100);
            results["retry"]=new{died,respawned=p&&!p.IsDead,bossResetToEntry=Mathf.Abs(health.Health-health.maxHealth*.5f)<.1f,director.State,players=Object.FindObjectsByType<PlayerHealth>().Length};
            Progress("Victory leaves town life running");
            p.SetPracticeProtection(protection,true);var boat=Object.FindObjectsByType<InkboundBoat>().First();float boatBefore=boat.Travelled;
            var walker=Object.FindObjectsByType<NpcRoutine>().First(n=>n.Walking);Vector3 walkBefore=walker.transform.position;health.SetHealth(0);await Task.Delay(3800);
            results["victoryLife"]=new{health.IsDead,director.State,residentsContinue=Vector3.Distance(walkBefore,walker.transform.position)>.1f,boatsContinue=boat.Travelled>boatBefore+.5f};
            Progress("Finished");
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/checks.json",json);return json;
        }
        catch(Exception error){File.WriteAllText(Folder+"/error.txt",error.ToString());Progress("Failed");throw;}
        finally
        {
            if(p){p.SetPracticeProtection(protection,false);motor.SetAIInput(Vector3.zero);motor.aiControlled=ai;motor.inputEnabled=input;}
            if(GameManager.Instance)GameManager.Instance.autoSaveOnLevelStart=autoSave;
            if(protection)Object.Destroy(protection);if(videoPixels)Object.Destroy(videoPixels);
            File.WriteAllText(Folder+"/video-frames.json",Newtonsoft.Json.JsonConvert.SerializeObject(times,Newtonsoft.Json.Formatting.Indented));
        }
    }
}
