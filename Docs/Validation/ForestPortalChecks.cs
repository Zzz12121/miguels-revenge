using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

public static class ForestPortalChecks
{
    const string Folder="output/forest-town-portal";
    static bool Rolling(CentipedeCredits c)=>(bool)typeof(CentipedeCredits).GetField("rolling",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task Until(Func<bool> test,float seconds,string label)
    {float start=Time.realtimeSinceStartup;while(!test()){if(Time.realtimeSinceStartup-start>seconds)throw new Exception("Timed out: "+label);await Task.Delay(20);}}
    static void Warp(PlayerHealth p,Vector3 point)
    {var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=point;p.GetComponent<ThirdPersonController>().ResetMotion();cc.enabled=true;}
    static void Shot(string name,Vector3 at,Vector3 target)
    {
        var main=Camera.main;var c=new GameObject("Temporary forest portal review camera").AddComponent<Camera>();c.CopyFrom(main);c.enabled=false;c.transform.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));
        var rt=RenderTexture.GetTemporary(1280,720,24);var old=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{Object.FindAnyObjectByType<InkboundWaterReflection>()?.RefreshFor(c);c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(image);Object.Destroy(c.gameObject);}
    }
    public static async Task<string> CheckBeaconAndCancellation()
    {
        Directory.CreateDirectory(Folder);var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();
        var boss=Object.FindObjectsByType<SkeletonCentipedeBossAI>().First(b=>!b.IsClone);boss.enabled=false;
        Vector3 point=boss.arenaCenter+new Vector3(-8,0,-7);SkeletonCentipedeBossAI.TerrainY(point,out float y);point.y=y+p.transform.position.y-p.FeetY;Warp(p,point);
        var orbit=Camera.main.GetComponent<ThirdPersonCamera>();Transform originalTarget=orbit.target;
        var flow=ForestInkboundPortal.BeginAfterDefeat(boss);
        await Until(()=>flow.State==ForestInkboundPortal.Stage.Ready,10,"portal ready");await Task.Delay(500);
        var ps=flow.transform.Find("Golden portal guide column").GetComponentInChildren<ParticleSystem>();
        bool matchingVelocityModes=ps.velocityOverLifetime.x.mode==ps.velocityOverLifetime.y.mode&&ps.velocityOverLifetime.y.mode==ps.velocityOverLifetime.z.mode;
        bool animatedSparkles=ps.particleCount>0;
        Warp(p,flow.PortalPosition+Vector3.up*(p.transform.position.y-p.FeetY));
        await Until(()=>flow.SubmergedAt>=0,6,"submerged before cancellation");flow.Cancel();await Task.Delay(100);
        var result=new{matchingVelocityModes,animatedSparkles,controlsRestored=motor.inputEnabled&&!motor.ActionsLocked&&!motor.IsScriptedMoving,
            collisionRestored=p.GetComponent<CharacterController>().enabled,modelRestored=p.GetComponentsInChildren<SkinnedMeshRenderer>().All(r=>!r.forceRenderingOff),
            animatorRestored=p.GetComponentsInChildren<Animator>().Any(a=>a.enabled&&a.avatar&&a.avatar.isHuman),cameraRestored=orbit.target==originalTarget,portalCleanedUp=!ForestInkboundPortal.Current};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/cleanup-checks.json",json);return json;
    }
    public static async Task<string> Run()
    {
        Require(Application.isPlaying&&SceneManager.GetActiveScene().name=="ForestIsland","Run in the original ForestIsland scene in Play mode.");
        Directory.CreateDirectory(Folder);
        var gm=GameManager.Instance;bool autosave=gm.autoSaveOnLevelStart;gm.autoSaveOnLevelStart=false;
        var kb=InputSystem.AddDevice<Keyboard>("ForestPortalCheckKeyboard");bool heldW=false;
        void Input(){kb.MakeCurrent();InputState.Change(kb,heldW?new KeyboardState(Key.W):new KeyboardState());}
        InputSystem.onBeforeUpdate+=Input;
        try
        {
            await Until(()=>Object.FindAnyObjectByType<CentipedeCredits>(),3,"forest ending adapter");await Task.Delay(150);
            var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();var boss=Object.FindObjectsByType<SkeletonCentipedeBossAI>().First(b=>!b.IsClone);
            float footOffset=p.transform.position.y-p.FeetY;
            var point=boss.arenaCenter+new Vector3(-8,0,-7);
            Require(SkeletonCentipedeBossAI.TerrainY(point,out float y),"No forest ground.");point.y=y+footOffset;
            Warp(p,point);motor.inputEnabled=true;p.SetHealth(p.maxHealth,p.maxHealth*.73f);float carriedHealth=p.Health;
            var orbit=Camera.main.GetComponent<ThirdPersonCamera>();orbit.ClearLock();orbit.yaw=0;orbit.pitch=12;orbit.SnapToTarget();
            boss.BeginFight();boss.GetComponent<BossHealth>().Invulnerable=false;boss.GetComponent<BossHealth>().SetHealth(0);
            await Until(()=>ForestInkboundPortal.Current&&ForestInkboundPortal.Current.State==ForestInkboundPortal.Stage.Ready,12,"post-defeat portal opens");
            var flow=ForestInkboundPortal.Current;var forestCredits=Object.FindAnyObjectByType<CentipedeCredits>();
            bool stillForest=SceneManager.GetActiveScene().name=="ForestIsland"&&!Rolling(forestCredits)&&!gm.IsGameWon&&motor.inputEnabled;
            int loops=flow.Rune.IdleLoops;float began=Time.time;bool chargeOnly=true;
            while(Time.time-began<6.3f){chargeOnly&=flow.Rune.SourceTime<3.001f;await Task.Delay(30);}
            bool cycles=flow.Rune.IdleLoops-loops>=2;
            var beacon=flow.transform.Find("Golden portal guide column");
            bool golden=beacon&&beacon.GetComponentsInChildren<ParticleSystem>().Any(ps=>ps.particleCount>0)&&beacon.GetComponent<Renderer>().enabled;
            Shot("01-ready-rune-and-gold-column",flow.PortalPosition+new Vector3(-10,6,-12),flow.PortalPosition+Vector3.up*3);
            Vector3 portal=flow.PortalPosition;
            Warp(p,portal+new Vector3(0,footOffset,-2.8f));orbit.yaw=0;orbit.pitch=12;orbit.SnapToTarget();heldW=true;
            await Until(()=>flow.State==ForestInkboundPortal.Stage.Sinking,4,"walk into the portal");heldW=false;
            Require(!p.GetComponent<CharacterController>().enabled,"Sinking must bypass terrain collision.");
            float oldHeight=p.transform.position.y;
            await Until(()=>Time.time-flow.EnteredAt>=1.6f,4,"visible descent");
            float descended=oldHeight-p.transform.position.y;
            Shot("02-player-sinking",portal+new Vector3(-5,2.6f,-6),portal+Vector3.up*.65f);
            await Until(()=>flow.SubmergedAt>=0,4,"fully submerged");
            float highestPose=float.NegativeInfinity;
            foreach(var skin in p.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=new Mesh();skin.BakeMesh(mesh);foreach(var v in mesh.vertices)highestPose=Mathf.Max(highestPose,skin.transform.TransformPoint(v).y);Object.Destroy(mesh);
            }
            bool completelyBelow=highestPose<portal.y&&p.GetComponentsInChildren<SkinnedMeshRenderer>().All(r=>r.forceRenderingOff);
            Shot("03-rune-finishing-after-submersion",portal+new Vector3(-7,3.5f,-8),portal+Vector3.up);
            await Until(()=>SceneManager.GetActiveScene().name==InkboundTownJourney.Scene,18,"town loads");
            await Until(()=>flow.State==ForestInkboundPortal.Stage.Falling,8,"arrival fade reveals sky drop");
            await Task.Delay(200);var townPlayer=PlayerHealth.Find();
            bool fromAbove=townPlayer.transform.position.y>8&&flow.ArrivalStart.y>15;
            Shot("04-falling-into-town",Camera.main.transform.position,Camera.main.transform.position+Camera.main.transform.forward*10);
            await Until(()=>!flow||flow.State==ForestInkboundPortal.Stage.Complete,8,"normal landing");
            var landed=townPlayer.GetComponent<ThirdPersonController>();
            bool controls=landed.inputEnabled&&!landed.ActionsLocked&&!landed.IsScriptedMoving&&townPlayer.GetComponent<CharacterController>().enabled;
            bool landedAtStart=Vector2.Distance(new Vector2(townPlayer.transform.position.x,townPlayer.transform.position.z),new Vector2(-14,-68))<.2f&&landed.IsGrounded;
            bool healthPreserved=Mathf.Abs(townPlayer.Health-carriedHealth)<.01f;
            Shot("05-landed-at-town-start",Camera.main.transform.position,Camera.main.transform.position+Camera.main.transform.forward*10);
            var credits=Object.FindAnyObjectByType<CentipedeCredits>();bool noPrematureCredits=credits&&!Rolling(credits);
            var encounter=Object.FindAnyObjectByType<InkboundTownEncounter>();encounter.Health.Invulnerable=false;encounter.Health.SetHealth(0);
            await Until(()=>Rolling(credits),9,"credits after town elder death");
            // The check ends at the verified credits trigger; do not let its later village
            // return run in the background after the tool has already returned.
            credits.StopAllCoroutines();
            var report=new{stillForest,chargeOnly,idleCycles=cycles,golden,walkedIntoPortal=true,descended,completelyBelow,highestPoseAtSubmersion=highestPose,portalGround=portal.y,
                replayDelay=flow.RestartedAt-flow.EnteredAt,sinkDuration=flow.SubmergedAt-flow.EnteredAt,tailDuration=flow.FinishedAt-flow.SubmergedAt,
                fromAbove,landedAtStart,controls,healthPreserved,noPrematureCredits,creditsAfterTownDeath=Rolling(credits),centipedeProgress=StoryProgress.Current.centipedeDefeated};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/flow-checks.json",json);return json;
        }
        finally
        {
            heldW=false;InputSystem.onBeforeUpdate-=Input;if(kb.added)InputSystem.RemoveDevice(kb);
            if(gm)gm.autoSaveOnLevelStart=autosave;
        }
    }
}
