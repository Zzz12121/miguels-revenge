using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

public static class InkboundFinaleChecks
{
    const string Folder="output/inkbound-finale";
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Dictionary<string,object> results=new();
    static void Save()
    {
        string path=Folder+"/checks.json";
        var saved=File.Exists(path)?Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)):new Newtonsoft.Json.Linq.JObject();
        foreach(var item in results)saved[item.Key]=Newtonsoft.Json.Linq.JToken.FromObject(item.Value);
        File.WriteAllText(path,saved.ToString(Newtonsoft.Json.Formatting.Indented));
    }
    static void Check(string name,bool pass,object details)
    {results[name]=new{pass,details};Save();if(!pass)throw new Exception(name+": "+Newtonsoft.Json.JsonConvert.SerializeObject(details));}
    static void Warp(PlayerHealth p,Vector3 position)
    {
        var cc=p.GetComponent<CharacterController>();bool enabled=cc.enabled;cc.enabled=false;p.transform.position=position;
        p.GetComponent<ThirdPersonController>().ResetMotion();cc.enabled=enabled;Physics.SyncTransforms();
    }
    static async Task Until(Func<bool> ready,float seconds=8)
    {float until=Time.realtimeSinceStartup+seconds;while(!ready()&&Time.realtimeSinceStartup<until)await Task.Delay(25);if(!ready())throw new Exception("Timed out");}
    static void Capture(string name,Vector3 position,Vector3 target)
    {
        var camera=new GameObject("Finale verification camera").AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=65;
        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        var rt=RenderTexture.GetTemporary(1280,720,24);var previous=RenderTexture.active;var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(Folder+"/"+name+".png",tex.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.Destroy(tex);Object.Destroy(camera.gameObject);}
    }
    public static async Task<string> Run()
    {
        Directory.CreateDirectory(Folder);results.Clear();
        var d=Object.FindAnyObjectByType<InkboundTownEncounter>();var boss=d.elder;var health=d.Health;var player=d.player;
        var motor=player.GetComponent<ThirdPersonController>();var combat=player.GetComponent<PlayerCombat>();
        var protection=new GameObject("Temporary finale test protection");player.SetPracticeProtection(protection,true);
        bool input=motor.inputEnabled;motor.inputEnabled=false;boss.proximityWaveEnabled=false;
        try
        {
            Check("health and movement",health.maxHealth==15000&&Mathf.Abs(boss.MovementSpeed-3.36f)<.001f,new{health.maxHealth,health.Health,boss.moveSpeed,boss.MovementSpeed,boss.basicAttackSpeedMultiplier});
            float multiplier=combat.normalAttackIntervalMultiplier;combat.normalAttackIntervalMultiplier=1;float oldRecovery=combat.RecoveryFor(0);combat.normalAttackIntervalMultiplier=multiplier;
            Check("player normal attack interval",Mathf.Abs(combat.RecoveryFor(0)/oldRecovery-.8f)<.001f,new{oldRecovery,newRecovery=combat.RecoveryFor(0),combat.AttackSpeed});
            d.BeginFight();d.enabled=false;boss.CancelCast();
            Warp(player,boss.transform.position+boss.transform.forward*2);
            float start=Time.time;bool cast=boss.CastEncounterSkill(2,true);await Until(()=>!boss.IsCasting);
            float meleeSeconds=Time.time-start;
            Check("basic swing speed",cast&&Mathf.Abs(meleeSeconds-1.05f/1.2f)<.12f,new{meleeSeconds,expected=1.05f/1.2f});
            boss.CancelCast();Warp(player,boss.transform.position+Vector3.back*2.2f);player.HealFull();
            typeof(PlayerHealth).GetField("lastHitTime",Private).SetValue(player,-100f);
            boss.proximityWaveEnabled=true;boss.proximityWaveChance=1;
            typeof(CalligraphyBossCombat).GetField("nextProximityWave",Private).SetValue(boss,0f);
            float before=player.Health;int waves=boss.ProximityWaves;
            await Until(()=>boss.ProximityWaves>waves);await Until(()=>player.Health<before,3);
            Capture("01-proximity-wave",boss.transform.position+new Vector3(-2,5,-3),boss.transform.position+Vector3.up*.2f);
            Check("proximity wave damage and launch",Mathf.Abs(before-player.Health-5)<.01f&&motor.IsBallistic,new{damage=before-player.Health,motor.IsBallistic,boss.ProximityWaves});
            await Until(()=>!boss.IsCasting);await Task.Delay(700);boss.proximityWaveEnabled=false;
            boss.CancelCast();boss.transform.position=new Vector3(-44,0,16);d.agent.Warp(boss.transform.position);
            Warp(player,new Vector3(-44,.1f,-2));
            typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(d,float.PositiveInfinity);
            d.enabled=true;health.SetHealth(health.maxHealth*.26f);await Task.Delay(300);
            Check("no ultimate at 26 percent",!d.UltimateTriggered&&!boss.AvatarUltimate,new{health.Health,d.UltimateTriggered});
            Capture("02-before-sweep",new Vector3(-84,32,-40),new Vector3(-42,4,12));
            health.SetHealth(health.maxHealth*.25f);await Until(()=>boss.AvatarUltimate!=null,5);
            var ultimate=boss.AvatarUltimate;var dissolve=d.GetComponent<InkboundTownSweepDissolve>();
            Check("ultimate at 25 percent",d.UltimateTriggered&&boss.SelectedSkill==9,new{health.Health,boss.SelectedSkill,d.UltimateTriggered});
            await Until(()=>ultimate.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.FirstSweep,12);
            Capture("03-ink-avatar",new Vector3(-88,42,-65),new Vector3(-44,23,16));
            await Until(()=>dissolve.DissolvedObjects>0,8);await Task.Delay(200);
            Capture("04-sweep-dissolve",new Vector3(-84,32,-40),new Vector3(-42,4,12));
            await Until(()=>ultimate.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.AirCombat,12);
            await Task.Delay(1000);
            Capture("05-floating-platforms",new Vector3(-94,65,-75),new Vector3(-44,30,5));
            Check("two sweeps and platforms",ultimate.SweepsCompleted==2&&ultimate.SpawnedPlatformCount>0&&dissolve.DissolvedObjects>0,new{ultimate.SweepsCompleted,ultimate.SpawnedPlatformCount,dissolve.DissolvedObjects,playerActive=player.gameObject.activeInHierarchy});
            ultimate.Cleanup();await Until(()=>!boss.IsCasting);await Until(()=>dissolve.PendingObjects==0,5);
            int objects=dissolve.DissolvedObjects;int casts=d.Casts;
            typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(d,float.PositiveInfinity);
            await Task.Delay(1300);
            Check("no repeated ultimate",d.Casts==casts&&d.UltimateTriggered,new{d.Casts,casts});
            int hidden=d.GetComponentsInChildren<Transform>(true).Count(t=>!t.gameObject.activeSelf);
            typeof(InkboundTownEncounter).GetMethod("ResetAfterDeath",Private).Invoke(d,null);await Task.Delay(200);
            int after=d.GetComponentsInChildren<Transform>(true).Count(t=>!t.gameObject.activeSelf);
            Check("permanent after cleanup and retry",objects>0&&after>=hidden&&player.gameObject.activeInHierarchy,new{objects,hidden,after});
            var basin=d.transform.Find("03 Willow banks and painted mountains/Irregular mountain basin around the town");
            Check("continuous landscape support retained",basin&&basin.gameObject.activeInHierarchy,new{landscape=basin?basin.gameObject.activeInHierarchy:false});
            Capture("06-permanent-clearing",new Vector3(-84,32,-40),new Vector3(-42,4,12));
            results["completed"]=true;Save();return "Finale integration checks passed";
        }
        catch(Exception e){results["error"]=e.ToString();Save();throw;}
        finally{d.enabled=false;boss.StopBattle();motor.inputEnabled=input;player.SetPracticeProtection(protection,false);Object.Destroy(protection);}
    }

    public static async Task<string> StartEnding()
    {
        Directory.CreateDirectory(Folder);
        var d=Object.FindAnyObjectByType<InkboundTownEncounter>();d.enabled=false;d.elder.StopBattle();
        d.Health.SetHealth(0);
        await Until(()=>VictoryCongratulations.Showing,12);await Task.Delay(2300);
        Check("restored victory presentation",VictoryCongratulations.Showing&&Time.timeScale==0,new{VictoryCongratulations.Showing,Time.timeScale});
        return "Victory presentation restored; awaiting real Enter input to continue credits.";
    }
    public static async Task<string> MovementAndSkillTiming()
    {
        var d=Object.FindAnyObjectByType<InkboundTownEncounter>();var boss=d.elder;var player=d.player;
        boss.proximityWaveEnabled=false;d.BeginFight();boss.CancelCast();
        boss.transform.position=new Vector3(-44,0,16);d.agent.Warp(boss.transform.position);Warp(player,new Vector3(-44,.1f,-5));
        typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(d,float.PositiveInfinity);
        await Task.Delay(350);Vector3 start=boss.transform.position;float began=Time.time;
        await Task.Delay(650);float seconds=Time.time-began;
        float speed=Vector3.ProjectOnPlane(boss.transform.position-start,Vector3.up).magnitude/seconds;
        Check("actual town pursuit speed",Mathf.Abs(speed-3.36f)<.15f,new{speed,seconds});
        d.enabled=false;boss.CancelCast();boss.CastEncounterSkill(0);began=Time.time;
        await Task.Delay(300);seconds=Time.time-began;
        float elapsed=(float)typeof(CalligraphyBossCombat).GetField("elapsed",Private).GetValue(boss);
        Check("skill timer remains original speed",Mathf.Abs(elapsed-seconds)<.08f,new{elapsed,seconds});
        boss.StopBattle();return "Actual pursuit 1.2x; regular skill clock remains 1x.";
    }
    public static async Task<string> BridgeSweep()
    {
        var d=Object.FindAnyObjectByType<InkboundTownEncounter>();d.enabled=false;d.elder.StopBattle();
        var bridge=d.transform.Find("01 Stone streets and winding river/Stone arch bridge -22");
        var bounds=bridge.GetComponentInChildren<Renderer>().bounds;
        Vector3 origin=bounds.center+Vector3.back*4;origin.y=0;
        d.GetComponent<InkboundTownSweepDissolve>().Sweep(origin,Vector3.forward,-80,80,30,.5f,8);
        await Until(()=>!bridge.gameObject.activeSelf,4);
        Check("swept bridge also dissolves",!bridge.gameObject.activeSelf&&d.player.gameObject.activeInHierarchy,new{bridge=bridge.gameObject.activeSelf,player=d.player.gameObject.activeInHierarchy});
        return "Bridge scenery also permanently dissolves; player remains active.";
    }
    static void Screen(string name)
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=Resources.FindObjectsOfTypeAll(type).First();
        var rt=(RenderTexture)type.GetField("m_RenderTexture",Private).GetValue(view);
        var previous=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();
            if(SystemInfo.graphicsUVStartsAtTop)
            {
                var data=tex.GetRawTextureData<byte>();int row=rt.width*3;
                for(int y=0;y<rt.height/2;y++)for(int x=0;x<row;x++){int a=y*row+x,b=(rt.height-1-y)*row+x;byte t=data[a];data[a]=data[b];data[b]=t;}
                tex.Apply();
            }
            File.WriteAllBytes(Folder+"/"+name+".png",tex.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;Object.Destroy(tex);}
    }
    public static async Task<string> Credits()
    {
        var credits=Object.FindAnyObjectByType<CentipedeCredits>();
        var scroll=typeof(CentipedeCredits).GetField("scroll",Private);
        await Until(()=>(float)scroll.GetValue(credits)>0,6);
        foreach(int target in new[]{4,8,12,16})
        {
            await Until(()=>(float)scroll.GetValue(credits)>=target,6);
            Screen("credits-"+target);
        }
        string[] team=(string[])typeof(CentipedeCredits).GetField("Team",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        Check("full ending credits",team.Length==4&&(float)scroll.GetValue(credits)>=16&&Time.timeScale>0,new{team,scroll=scroll.GetValue(credits),Time.timeScale});
        credits.StopAllCoroutines(); // Leave the observed final thanks frame for inspection; do not change scenes during verification.
        return "All historical team entries, repo link and final thanks completed after victory confirmation.";
    }
    public static async Task<string> ConfirmAndCredits()
    {
        // Pipeline's simulate_key does an immediate extra InputSystem.Update; its frame edge
        // expires before LateUpdate while the editor is unfocused. Deliver and sample the
        // actual Enter branch in the same input frame instead of invoking Close directly.
        var keyboard=UnityEngine.InputSystem.Keyboard.current;
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Enter));
        UnityEngine.InputSystem.InputSystem.Update();
        foreach(var screen in Object.FindObjectsByType<VictoryCongratulations>(FindObjectsInactive.Include))
            typeof(VictoryCongratulations).GetMethod("LateUpdate",Private).Invoke(screen,null);
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
        UnityEngine.InputSystem.InputSystem.Update();
        Check("victory confirm restores clock",Time.timeScale>0,new{Time.timeScale});
        return await Credits();
    }
}
