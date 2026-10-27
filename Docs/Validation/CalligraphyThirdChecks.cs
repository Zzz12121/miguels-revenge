// Run with Unity Pipeline in the VFX_VoidRend_Bocchi scene, Play Mode.
// Outside Assets intentionally: validation code never ships with gameplay.
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

public static class CalligraphyThirdChecks
{
    public static string NativeWaveEmission()
    {
        var lib=CalligraphyVfxLibrary.Current;var result=new System.Text.StringBuilder();
        foreach(var prefab in new[]{lib.frontalPulses,lib.frontal})
        foreach(var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
        {
            if(!ps.name.StartsWith("yy_Shockwave_1"))continue;
            var e=ps.emission;result.AppendLine(prefab.name+"/"+ps.name+" rate="+e.rateOverTime.constantMax+" duration="+ps.main.duration);
            for(int i=0;i<e.burstCount;i++){var burst=e.GetBurst(i);result.AppendLine($"time={burst.time} count={burst.count.constantMax} cycles={burst.cycleCount} interval={burst.repeatInterval}");}
        }
        return result.ToString();
    }
    static async Task Wait(float seconds)
    {
        float end=Time.time+seconds;var wall=System.Diagnostics.Stopwatch.StartNew();
        while(Time.time<end){if(wall.Elapsed.TotalSeconds>seconds*4+10)throw new Exception("Play Mode stopped ticking");await Task.Delay(16);}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static CalligraphyBossCombat Setup(int skill)
    {
        Time.timeScale=1;EditorApplication.isPaused=false;
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();
        var b=range.Boss;b.cycleDemonstration=false;b.moveSpeed=0;b.dodgeChance=0;b.recoverySeconds=100;
        b.paintingWorld.containedSeconds=20;
        b.transform.SetPositionAndRotation(new Vector3(12,0,12),Quaternion.identity);b.SelectSkill(skill);
        PlayerHealth.Find().GrantDodgeInvulnerability(60);return b;
    }
    static void Place(PlayerHealth p,Vector3 position)
    {
        p.GetComponent<ThirdPersonController>().ResetMotion();var cc=p.GetComponent<CharacterController>();
        cc.enabled=false;p.transform.position=position;cc.enabled=true;Physics.SyncTransforms();
    }
    public static async Task<string> Directions()
    {
        var b=Setup(7);var p=PlayerHealth.Find();Place(p,new Vector3(22,1.05f,12));
        await Wait(1.4f);
        var pulse=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Single(f=>f.name.StartsWith("Ying Yang Frontal Pulses"));
        Check(Mathf.Abs(pulse.transform.lossyScale.x-b.frontalPulses.vfxScale)<.001f,"Pulses scale differs from Inspector");
        Check(Vector3.Dot(pulse.transform.right,Quaternion.Euler(0,b.frontalPulses.yawOffset,0)*Vector3.right)>.999f,"Pulses native +X missed configured yaw");
        b.CancelCast();b.SelectSkill(6);Place(p,new Vector3(12,1.05f,22));
        await Wait(1.4f);Place(p,new Vector3(20,1.05f,19));await Wait(1.6f);
        var frontal=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Single(f=>f.name.StartsWith("Ying Yang Frontal ["));
        Vector3 expected=Quaternion.Euler(0,b.leapSlam.wave.yawOffset+b.leapSlam.forwardYawOffset,0)*Vector3.forward;
        Check(Vector3.Dot(frontal.transform.forward,expected)>.999f,"Frontal reacquired player after jump");
        var locked=frontal.transform.rotation;Place(p,new Vector3(8,1.05f,8));await Wait(.25f);
        Check(Quaternion.Angle(locked,frontal.transform.rotation)<.01f,"Frontal tracked after release");
        b.StopBattle();return "PASS Pulses Inspector scale / configured left yaw; slam locks pre-jump direction plus configured yaw";
    }
    public static async Task<string> Clones()
    {
        var b=Setup(2);var p=PlayerHealth.Find();Place(p,new Vector3(12,1.05f,24));
        await Wait(1.12f);
        var doubles=b.EffectsRoot.GetComponentsInChildren<CalligraphyClone>().Where(c=>c.combatClone).ToArray();
        Check(doubles.Length==2,"Expected two valid left/right spawns");
        var local=doubles.Select(c=>b.transform.InverseTransformPoint(c.transform.position)).OrderBy(v=>v.x).ToArray();
        Check(local[0].x<-5.4f && local[1].x>5.4f,"Clone side spacing incorrect: "+string.Join(";",local));
        Check(doubles.All(c=>c.hitsRemaining==5 && c.lifetime==10),"Clone rules changed");
        b.StopBattle();return "PASS clones local positions "+string.Join(";",local)+"; max2/five hits/ten seconds retained";
    }
    public static async Task<string> Homing()
    {
        var b=Setup(0);b.CancelCast();b.enabled=false;var p=PlayerHealth.Find();var old=p.transform.position;
        Place(p,new Vector3(90,1.05f,120));
        var go=new GameObject("Validation projectile");var shot=go.AddComponent<CalligraphyInkProjectile>();
        var settings=new CalligraphyBossCombat.ProjectileSettings{maximumDistance=20,turnRate=24,homingStrength=.65f};
        try
        {
            shot.Launch(b.ink,b.transform,new Vector3(80,1,100),Vector3.forward,10,1,p,settings);
            await Wait(.5f);Check(shot && shot.Heading.x>.01f && shot.Heading.x<.2f,"Homing missing or too abrupt");
            while(shot && !shot.HeadingLocked)await Wait(.02f);
            Check(shot && shot.DistanceTravelled>=18 && shot.DistanceTravelled<18.6f,"90% distance cutoff incorrect");
            var heading=shot.Heading;Place(p,new Vector3(50,1.05f,100));await Wait(.08f);
            Check(shot && Vector3.Angle(heading,shot.Heading)<.001f,"Final segment reacquired target");
            float cutoff=shot.DistanceTravelled;await Wait(.8f);Check(!shot,"Projectile exceeded maximum path distance");
            Check(CalligraphyBossCombat.Abdomen(p).y<p.GetComponent<CharacterController>().bounds.center.y,"Aim is above abdomen");
            return "PASS bounded smooth homing, lock at18/20 units, no reacquisition, distance expiry; abdomen below capsule center";
        }
        finally{if(go)UnityEngine.Object.Destroy(go);Place(p,old);b.enabled=true;b.StopBattle();}
    }
    public static async Task<string> Runes()
    {
        var b=Setup(8);var p=PlayerHealth.Find();Place(p,new Vector3(12,1.05f,20));
        var sequences=new System.Collections.Generic.HashSet<CalligraphySkillSequence>();float start=Time.time;Vector3? attackCenter=null;
        while(Time.time-start<7)
        {
            foreach(var s in b.EffectsRoot.GetComponentsInChildren<CalligraphySkillSequence>()){if(!attackCenter.HasValue)attackCenter=s.transform.position;sequences.Add(s);}
            await Wait(.03f);
        }
        Check(sequences.Count==12,"Rune count="+sequences.Count);
        var live=sequences.Where(s=>s).ToArray();var positions=live.Select(s=>s.transform.position).ToArray();
        Check(attackCenter.HasValue && positions.All(v=>Vector3.ProjectOnPlane(v-attackCenter.Value,Vector3.up).magnitude<=20.1f),"Rune outside snapshotted disk: center="+attackCenter+" positions="+string.Join(";",positions));
        for(int i=0;i<positions.Length;i++)for(int j=0;j<i;j++)Check(Vector3.Distance(positions[i],positions[j])>=3.99f,"Rune overlap");
        foreach(var s in live.Where(s=>s.FallStartedAt>=0))Check(s.FallStartedAt==s.WarningFinishedAt,"Gap between warning and fall");
        Check(live.Any(s=>s.ImpactAt>s.FallStartedAt && s.FallStartedAt>=0),"No actual impact burst observed");
        string timings=string.Join(";",live.Take(3).Select(s=>$"warningEnd={s.WarningFinishedAt:F3},fall={s.FallStartedAt:F3},impact={s.ImpactAt:F3}"));
        b.StopBattle();return "PASS 12 runes, radius20, spacing>=4, same-frame warning/fall, observed impact burst: "+timings;
    }
    public static async Task<string> Dragon()
    {
        var b=Setup(4);var p=PlayerHealth.Find();Place(p,new Vector3(12,1.05f,22));await Wait(.7f);
        var smoke=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Single(f=>f.name.StartsWith("Ying Yang Smoke"));
        Check(Mathf.Abs(smoke.transform.lossyScale.x-.7f)<.001f,"Smoke not doubled");
        b.transform.rotation=Quaternion.Euler(0,85,0);await Wait(.2f);
        Check(Quaternion.Angle(smoke.transform.rotation,Quaternion.identity)<.001f,"Smoke follows body rotation");
        await Wait(3.8f);
        Check(!b.EffectsRoot.GetComponentsInChildren<Transform>().Any(t=>t.name=="Yin yang impact" || t.name=="Ink landing telegraph"),"Old Taiji still created");
        Check(b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Any(f=>f.name.StartsWith("Ying Yang Big Explotion")),"Missing new explosion");
        bool buff=false,flash=false;float end=Time.time+16;
        while(Time.time<end && !(buff&&flash))
        {
            var effects=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>();
            buff|=effects.Any(f=>f.name.StartsWith("Ying Yang Buff",StringComparison.OrdinalIgnoreCase));flash|=effects.Any(f=>f.name.StartsWith("Ying Yang Flash"));await Wait(.04f);
        }
        Check(buff && flash,"Missing explosion tail combination");
        b.StopBattle();return "PASS Smoke scale .7 fixed rotation; old Taiji absent; Big Explotion + Buff Up + Flash AoE";
    }
    public static async Task<string> Isolation()
    {
        var b=Setup(3);b.paintingWorld.dragonCount=0;var p=PlayerHealth.Find();
        await Wait(5);var world=UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>();
        Check(world && Mathf.Abs(world.radius-163.2f)<.01f,"World not1.7x");
        var support=world.GetComponentsInChildren<BoxCollider>().Single(c=>c.name=="Player-only transparent support");Check(Mathf.Abs(support.size.x-326.4f)<.01f,"Support not scaled");
        Check(!CalligraphyWorldIsolation.Allows(b.transform) && CalligraphyWorldIsolation.Allows(p.transform),"UI subject gate invalid");
        var invulnerability=typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic);
        invulnerability.SetValue(p,0f);
        float hp=p.Health;
        Check(!p.TakeDamage(1,Vector3.zero,true,"outside isolation test",false) && p.Health==hp,"Outside attack passed into painting");
        Check(!CalligraphyPlayerHitGate.Hit(p,1,Vector3.zero,"outside pending wave",source:b.transform),"Outside VFX hit passed into painting");
        Check(CalligraphyPlayerHitGate.Hit(p,1,Vector3.zero,"inside painting test",source:world.transform),"Inside painting damage was blocked");
        p.GrantDodgeInvulnerability(60);
        float health=b.Health;b.TakeDamage(10,b.transform.position);Check(b.Health==health,"Outside boss damaged from painting");
        var targets=UnityEngine.Object.FindObjectsByType<CalligraphyPracticeTarget>();
        foreach(var t in targets){float before=t.Health;t.TakeDamage(10,t.transform.position);Check(t.Health==before,"Outside dummy damaged");}
        var iso=world.GetComponent<CalligraphyWorldIsolation>();
        typeof(CalligraphyPaintingWorld).GetField("age",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(world,24f);
        await Wait(1);Check(iso.CapturedFrames>1 && !iso.FullyRevealed,"Crossfade absent");await Wait(1.8f);
        Check(!world && CalligraphyWorldIsolation.Allows(b.transform),"Normal exit not restored");
        b.SelectSkill(3);await Wait(.7f);Check(!CalligraphyWorldIsolation.Allows(b.transform),"Second entry missing");
        b.CancelCast();await Wait(.1f);Check(CalligraphyWorldIsolation.Allows(b.transform),"Cancellation not restored");
        b.StopBattle();return "PASS dimensions/support, external UI/damage gates, continuous reveal, normal and cancel restore";
    }
    public static async Task<string> Ripples()
    {
        var b=Setup(3);b.paintingWorld.dragonCount=0;var p=PlayerHealth.Find();
        var controller=p.GetComponent<ThirdPersonController>();bool input=controller.inputEnabled;controller.inputEnabled=false;controller.ResetMotion();
        try
        {
        await Wait(5);
        var world=UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>();var cc=p.GetComponent<CharacterController>();
        bool visibleParticle=false;int peak=0;
        for(int i=0;i<30;i++)
        {
            cc.Move(Vector3.right*.05f);await Wait(.03f);
            var effects=world.GetComponentsInChildren<CalligraphyPackageEffect>();peak=Mathf.Max(peak,effects.Length);
            visibleParticle|=effects.Any(f=>f.Systems.Any(s=>s.name.StartsWith("yy_Shockwave") && s.particleCount>0));
        }
        // Verify zero *actual* displacement, not merely released keys: motor/collision settling
        // can still move the capsule after input is disabled, and should legitimately emit waves.
        Check(visibleParticle,"Ground ripple never emitted a ring");controller.ResetMotion();controller.enabled=false;Vector3 stopped=p.transform.position;await Wait(2);
        Check(world.GetComponentsInChildren<CalligraphyPackageEffect>().Length==0,"Ripples remained 2s after stopping: position="+stopped+" -> "+p.transform.position+" FX="+string.Join(";",world.GetComponentsInChildren<CalligraphyPackageEffect>().Select(f=>f.name+" age="+f.Age)));
        controller.enabled=false;Vector3 grounded=p.transform.position;
        try
        {
            cc.Move(Vector3.up*3);for(int i=0;i<20;i++){cc.Move(Vector3.right*.05f);await Wait(.03f);}
            Check(world.GetComponentsInChildren<CalligraphyPackageEffect>().Length==0,"Air movement emitted ground ripple");
        }
        finally{cc.enabled=false;p.transform.position=grounded;cc.enabled=true;controller.enabled=true;controller.ResetMotion();b.StopBattle();}
        return "PASS passive grounded displacement creates actual ring particles, peak="+peak+"; stopped2s=0; airborne movement=0";
        }
        finally{controller.inputEnabled=input;controller.enabled=true;controller.ResetMotion();b.StopBattle();}
    }
    public static async Task<string> InterruptedWorld()
    {
        var b=Setup(3);b.paintingWorld.dragonCount=0;var p=PlayerHealth.Find();
        var data=new TerrainData{heightmapResolution=33,size=new Vector3(10,1,10)};
        var terrainObject=Terrain.CreateTerrainGameObject(data);terrainObject.transform.position=new Vector3(300,0,300);
        var terrain=terrainObject.GetComponent<Terrain>();terrain.drawHeightmap=true;terrain.drawTreesAndFoliage=true;
        try
        {
            await Wait(1);
            Check(!terrain.drawHeightmap && !terrain.drawTreesAndFoliage,"Outside terrain/foliage not isolated");
            // Simulate the death event directly: normal player damage is intentionally prevented
            // from reaching the outside boss while the player is in the painting.
            typeof(CalligraphyBossCombat).GetProperty("Health").SetValue(b,0f);
            typeof(CalligraphyBossCombat).GetMethod("BeginDeath",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(b,null);
            await Wait(.1f);
            Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>() && CalligraphyWorldIsolation.Allows(b.transform),"Death did not restore outside");
            Check(terrain.drawHeightmap && terrain.drawTreesAndFoliage,"Terrain state not restored");
            var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();await Wait(.1f);
            b=range.Boss;b.cycleDemonstration=false;b.moveSpeed=0;b.paintingWorld.dragonCount=0;b.SelectSkill(3);await Wait(1);
            Check(!CalligraphyWorldIsolation.Allows(b.transform),"Re-entry missing");
            range.StartTest();await Wait(.1f);
            Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>() && CalligraphyWorldIsolation.Allows(b.transform),"Restart did not restore outside");
            Check(terrain.drawHeightmap && terrain.drawTreesAndFoliage,"Restart lost terrain state");
            range.StopTest();return "PASS death event and range restart restore world/terrain/foliage; no leaked painting";
        }
        finally{if(b)b.StopBattle();UnityEngine.Object.Destroy(terrainObject);UnityEngine.Object.Destroy(data);}
    }
    public static async Task<string> InspectorAndStab()
    {
        var b=Setup(7);b.frontalPulses.vfxScale=.9f;b.frontalPulses.waveCount=2;b.frontalPulses.waveInterval=.2f;var p=PlayerHealth.Find();Place(p,new Vector3(12,1.05f,22));await Wait(1.4f);
        var pulse=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Single(f=>f.name.StartsWith("Ying Yang Frontal Pulses"));
        Check(Mathf.Abs(pulse.transform.lossyScale.x-.9f)<.001f,"Inspector scale not consumed");
        var wave=pulse.Systems.First(s=>s.name.StartsWith("yy_Shockwave_1"));var burst=wave.emission.GetBurst(0);
        Check(burst.cycleCount==2 && Mathf.Abs(burst.repeatInterval-.2f)<.001f && wave.emission.rateOverTime.constantMax==0,"Inspector wave count/interval not consumed");
        b.frontalPulses.vfxScale=1.8f;b.frontalPulses.waveCount=6;b.frontalPulses.waveInterval=1;b.StopBattle();
        b.brush.showcase=false;b.brush.SetCombatPose(4,.48f);b.brush.SetAimPoint(CalligraphyBossCombat.Abdomen(p));await Wait(.2f);
        var axis=(b.brush.brushTip.position-b.brush.brushPivot.position).normalized;
        Check(Mathf.Abs(axis.y)<.05f && Vector3.Dot(axis,b.transform.forward)>.98f,"Thrust shaft not horizontal: "+axis);
        CalligraphyMeleeEffects.Play(b.brush,0);await Wait(.04f);
        var stab=UnityEngine.Object.FindObjectsByType<CalligraphyPackageEffect>().First(f=>f.name.StartsWith("ARPG_White__Slash_Stab"));
        Check(Vector3.Dot(stab.transform.up,b.transform.forward)>.999f,"Stab native +Y axis not compensated");
        Selection.activeGameObject=b.gameObject;
        return "PASS changed Inspector scale affects next spawned effect; shaft horizontal and Stab +Y mapped to forward";
    }
}
