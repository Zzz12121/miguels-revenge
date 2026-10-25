using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class CalligraphyAvatarChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static async Task Wait(float seconds)
    {
        float until=Time.time+seconds;var clock=System.Diagnostics.Stopwatch.StartNew();
        while(Time.time<until){Check(clock.Elapsed.TotalSeconds<seconds*6+15,"Editor stopped ticking");await Task.Delay(16);}
    }
    static async Task Until(Func<bool> condition,float seconds,string message)
    {
        float end=Time.time+seconds;var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!condition()){Check(Time.time<end && clock.Elapsed.TotalSeconds<seconds*6+15,message);await Task.Delay(16);}
    }
    static CalligraphyBossCombat Setup()
    {
        Check(EditorApplication.isPlaying,"Enter Play Mode first");Time.timeScale=1;
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();
        var boss=range.Boss;boss.moveSpeed=0;boss.dodgeChance=0;
        var p=PlayerHealth.Find();var body=p.GetComponent<CharacterController>();body.enabled=false;
        p.transform.position=boss.transform.position+boss.transform.forward*16+Vector3.up*1.05f;
        body.enabled=true;p.GetComponent<ThirdPersonController>().ResetMotion();Physics.SyncTransforms();
        var s=boss.inkAvatarUltimate;s.roarSeconds=2.2f;s.avatar.appearSeconds=1.7f;s.avatar.dissolveSeconds=1.3f;
        s.sweep.seconds=1.2f;s.sweep.interval=.3f;s.sweep.firstKnockback=false;s.sweep.secondKnockback=false;s.slam.seconds=1.5f;
        s.sea.duration=12;s.sea.fadeSeconds=1;s.airAttackInterval=1.5f;s.airAttackDamage=0;
        var cam=Camera.main;var orbit=cam.GetComponent<ThirdPersonCamera>();if(orbit)orbit.enabled=false;
        var brain=cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;
        Frame(boss);boss.SelectSkill(9);return boss;
    }
    static void Frame(CalligraphyBossCombat boss)
    {
        var cam=Camera.main;cam.farClipPlane=600;cam.fieldOfView=58;
        Vector3 look=boss.transform.position+Vector3.up*20;
        Vector3 at=boss.transform.position+boss.transform.forward*90+boss.transform.right*48+Vector3.up*38;
        cam.transform.SetPositionAndRotation(at,Quaternion.LookRotation(look-at));
    }
    static string Capture(string name)
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");EditorWindow.GetWindow(type).Repaint();
        var rt=type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(EditorWindow.GetWindow(type)) as RenderTexture;
        Check(rt,"No Game View texture");var previous=RenderTexture.active;RenderTexture.active=rt;
        var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=previous;
        if(SystemInfo.graphicsUVStartsAtTop){var pixels=image.GetPixels();var flipped=new Color[pixels.Length];for(int y=0;y<rt.height;y++)Array.Copy(pixels,y*rt.width,flipped,(rt.height-1-y)*rt.width,rt.width);image.SetPixels(flipped);image.Apply();}
        string path="Logs/Avatar-"+name+".png";System.IO.File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.Destroy(image);return path;
    }
    public static string AssetAudit()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab");
        var boss=prefab.GetComponent<CalligraphyBossCombat>();var s=boss.inkAvatarUltimate;
        Check(CalligraphyBossCombat.SkillNames.Length==10,"New skill missing from the selector");
        Check(s.avatar.size>=100 && s.platforms.count>=60,"Default spectacle scale/count too small");
        Check(s.sweep.angle==60,"Default sweep not 60 degrees");
        Check(s.pillars.prefab && s.avatar.material && s.platforms.material && s.sea.material,"Missing serialized assets");
        var errors=new[]{s.avatar.material.shader,s.platforms.material.shader}.SelectMany(ShaderUtil.GetShaderMessages).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m=>m.message).ToArray();
        Check(errors.Length==0,string.Join(";",errors));return "PASS asset references, default height100, platforms64, fan60, shader diagnostics";
    }
    public static async Task<string> Flow()
    {
        var boss=Setup();var player=PlayerHealth.Find();var motor=player.GetComponent<ThirdPersonController>();var cc=player.GetComponent<CharacterController>();
        Vector3 entry=player.transform.position,bossEntry=boss.transform.position;
        await Until(()=>boss.AvatarUltimate,3,"No skill runtime");var skill=boss.AvatarUltimate;
        await Wait(1.5f);
        Check(skill.AvatarRoot,"No giant avatar");
        var detail=skill.AvatarRoot.GetComponentsInChildren<Renderer>().First(r=>r is SkinnedMeshRenderer);
        Check(detail.sharedMaterials[0].GetTexture("_BaseMap"),"Lost original face/robe texture");
        Check(!skill.AvatarRoot.GetComponentsInChildren<Collider>().Any(c=>c.enabled),"Avatar is not visual-only");
        Capture("roar");
        var giantPen=skill.AvatarRoot.GetComponent<CalligraphyBrushPresentation>();Vector3 facing=boss.transform.forward,side=boss.transform.right;
        await Until(()=>skill.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.FirstSweep,5,"No first sweep");await Wait(.3f);
        float firstStart=Vector3.Dot(giantPen.brushTip.position-bossEntry,side);await Wait(.55f);Capture("sweep-right-left");
        Check(firstStart>0 && Vector3.Dot(giantPen.brushTip.position-bossEntry,side)<0,"Actual brush did not sweep right to left");
        Check(Mathf.Abs(giantPen.brushTip.position.y-bossEntry.y-1.2f)<.3f,"Sweep brush not near the ground contact band");
        await Until(()=>skill.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.SecondSweep,5,"No second sweep");await Wait(.3f);
        float secondStart=Vector3.Dot(giantPen.brushTip.position-bossEntry,side);await Wait(.55f);Capture("sweep-left-right");
        Check(secondStart<0 && Vector3.Dot(giantPen.brushTip.position-bossEntry,side)>0,"Actual brush did not sweep left to right");
        await Until(()=>skill.GroundIsHazardous,5,"No brush impact/ink sea");
        Check(skill.SweepsCompleted==2,"Did not complete both sweeps");
        Check(skill.SpawnedPlatformCount==64,"Did not generate all 64 footholds");
        Check(skill.PlayerLaunched,"No forced player launch");
        Check(Vector3.ProjectOnPlane(skill.ImpactPoint-(bossEntry+facing*boss.inkAvatarUltimate.slam.forwardDistance),Vector3.up).magnitude<.4f,"Brush impact ignored its authored forward distance");
        foreach(var platform in skill.Platforms)
        {Check(platform.GetComponent<MeshCollider>().convex,"Non-convex foothold");Check(platform.GetComponent<Rigidbody>().isKinematic,"Platform not kinematic");}
        for(int i=0;i<skill.Platforms.Count;i++)for(int j=i+1;j<skill.Platforms.Count;j++)
        {
            var a=skill.Platforms[i];var b=skill.Platforms[j];
            Check(Vector3.ProjectOnPlane(a.transform.position-b.transform.position,Vector3.up).magnitude+.01f>=a.Radius+b.Radius+boss.inkAvatarUltimate.platforms.minimumGap,"Overlapping footholds");
        }
        await Wait(1.15f);Frame(boss);Capture("ink-sea-launch");
        Check(player.transform.position.y>8,"Launch too weak to reach air combat");
        Check(!motor.ActionsLocked,"Air combat still locks player input");
        await Until(()=>motor.IsGrounded && cc.bounds.min.y>5,7,"Player never landed on the reserved platform");
        float landedY=cc.bounds.min.y;Capture("landed-on-platform");
        Check(skill.Platforms.Any(p=>Mathf.Abs(p.Top-landedY)<.25f),"Player landed on something other than a foothold");
        // Sixth revision: the sea hurts and slows but NEVER launches a fallen player again.
        player.SetPracticeProtection(boss,false);float beforeSea=player.Health;
        cc.enabled=false;player.transform.position=new Vector3(skill.ImpactPoint.x,1.05f,skill.ImpactPoint.z);cc.enabled=true;motor.ResetMotion();Physics.SyncTransforms();
        await Until(()=>skill.PlayerInSea && player.Health<beforeSea,3,"Ink sea caused no damage");
        Check(skill.RescueCount==0 && !motor.IsBallistic,"Ink sea still bounces player");
        boss.NextSkill();await Wait(.15f);
        Check(!boss.AvatarUltimate,"Skill pointer survived next-skill cleanup");
        Check(Vector3.Distance(player.transform.position,entry)<.25f,"Player did not return to the entry coordinate");
        Check(Vector3.Distance(boss.transform.position,bossEntry)<.05f,"Boss did not return to the original ground");
        Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>(),"Ultimate root survived");
        Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkPlatform>(),"Foothold colliders survived");
        Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyUltimateCameraShake>(),"Camera effect survived");
        Check(!motor.ActionsLocked,"Leaked input lock");
        return $"PASS complete summon, textured upper-body avatar, two directional sweeps (hits={skill.SweepHits}), 64 separated convex footholds, force launch, landing at {landedY:F2}m, unsafe sea without rescue bounce, next-skill full cleanup and return. Five screenshots in Logs/Avatar-*.png";
    }
    public static async Task<string> Cancellation()
    {
        var boss=Setup();boss.inkAvatarUltimate.roarSeconds=.7f;boss.inkAvatarUltimate.avatar.appearSeconds=.5f;
        boss.inkAvatarUltimate.sweep.seconds=.6f;boss.inkAvatarUltimate.sweep.interval=.1f;boss.inkAvatarUltimate.slam.seconds=.7f;
        boss.inkAvatarUltimate.sea.duration=1.3f;boss.inkAvatarUltimate.sea.fadeSeconds=.4f;boss.inkAvatarUltimate.avatar.dissolveSeconds=.6f;
        foreach(var phase in new[]{CalligraphyInkAvatarUltimate.Phase.Roar,CalligraphyInkAvatarUltimate.Phase.FirstSweep,CalligraphyInkAvatarUltimate.Phase.BrushSlam,CalligraphyInkAvatarUltimate.Phase.AirCombat,CalligraphyInkAvatarUltimate.Phase.Dissolve})
        {
            boss.SelectSkill(9);await Until(()=>boss.AvatarUltimate && boss.AvatarUltimate.CurrentPhase==phase,8,"Phase never reached: "+phase);
            boss.SelectSkill(0);await Wait(.1f);
            Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>(),"Root remains after "+phase);
            Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkPlatform>(),"Platforms remain after "+phase);
            Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyUltimateCameraShake>(),"Shake remains after "+phase);
        }
        boss.SelectSkill(9);await Until(()=>boss.WaitingForPreview,10,"Natural finish did not enter manual standby");
        Check(boss.AvatarUltimate.Completed,"Natural cleanup not complete");
        Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkPlatform>(),"Natural finish left platforms");
        boss.SelectSkill(9);await Until(()=>boss.AvatarUltimate && boss.AvatarUltimate.GroundIsHazardous,8,"No replay impact");boss.enabled=false;await Wait(.1f);
        Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>(),"Disabling boss leaked ultimate");
        return "PASS cancellation during roar, first sweep, brush slam, air combat, dissolve; natural standby; replay; disable cleanup";
    }
    public static async Task<string> Presentation()
    {
        var boss=Setup();boss.inkAvatarUltimate.sea.duration=90;
        await Until(()=>boss.AvatarUltimate && boss.AvatarUltimate.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.AirCombat,10,"No air arena");
        await Wait(3.3f);var skill=boss.AvatarUltimate;Time.timeScale=0;
        var camera=Camera.main;Vector3 centre=skill.ImpactPoint;
        Vector3 at=centre+boss.transform.forward*100+boss.transform.right*85+Vector3.up*85;
        camera.transform.SetPositionAndRotation(at,Quaternion.LookRotation((centre+Vector3.up*5)-at));
        await Task.Delay(100);Capture("overview");
        return $"Frozen presentation: impact={skill.ImpactPoint}, avatar={skill.AvatarRoot.position}, tip={skill.AvatarRoot.GetComponent<CalligraphyBrushPresentation>().brushTip.position}, platforms={skill.SpawnedPlatformCount}";
    }
    public static async Task<string> DefaultPlayback()
    {
        var boss=Setup();
        var saved=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab").GetComponent<CalligraphyBossCombat>();
        boss.inkAvatarUltimate=JsonUtility.FromJson<CalligraphyBossCombat.InkAvatarUltimateSettings>(JsonUtility.ToJson(saved.inkAvatarUltimate));
        var s=boss.inkAvatarUltimate;Check(s.roarSeconds>=4 && s.platforms.count==64,"Not saved defaults");
        await Until(()=>boss.AvatarUltimate,3,"No default skill");var skill=boss.AvatarUltimate;
        await Wait(3.4f);Capture("default-roar");
        Check(skill.GetComponentsInChildren<ParticleSystem>().Any(p=>p.particleCount>0),"Original Flaming Pillars not playing");
        await Until(()=>skill.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.AirCombat,14,"Default skill never reached air combat");
        await Wait(3.1f);
        Check(skill.SpawnedPlatformCount==64 && skill.PlayerLaunched,"Default platforms / launch failed");
        var giant=skill.AvatarRoot.GetComponent<CalligraphyBrushPresentation>();
        Check(Mathf.Abs(giant.brushTip.position.y-.04f)<.35f,"Giant brush did not remain planted");
        Vector3 axis=giant.brushPivot.forward,centre=giant.brushPivot.position;
        float HandGap(Transform hand)=>Vector3.ProjectOnPlane(hand.position-centre,axis).magnitude;
        Check(HandGap(giant.leftHand)<.8f && HandGap(giant.rightHand)<.8f,"Hands lost the giant shaft");
        // A clear overview and portrait of the real textured face, without altering persistent cameras.
        var cam=Camera.main;Vector3 at=skill.ImpactPoint+boss.transform.right*105+Vector3.up*70-boss.transform.forward*75;
        cam.transform.SetPositionAndRotation(at,Quaternion.LookRotation((skill.ImpactPoint+Vector3.up*9)-at));await Wait(.1f);Capture("overview");
        var skull=giant.animator.GetComponentsInChildren<Transform>().First(t=>t.name=="Head");
        Vector3 portrait=skull.position+giant.transform.forward*27+giant.transform.right*11+Vector3.up*3;
        cam.transform.SetPositionAndRotation(portrait,Quaternion.LookRotation(skull.position-portrait));await Wait(.1f);Capture("face-detail");
        boss.CancelCast();await Wait(.1f);Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkPlatform>(),"Default cancel left platforms");
        return "PASS saved defaults: full roar with live original Pillars, height100 avatar, 64 platforms, real launch, brush remains on ground, both hands constrained; overview and face-detail screenshots";
    }
    public static async Task<string> InterruptDuringHit()
    {
        var boss=Setup();boss.inkAvatarUltimate.sweep.firstKnockback=true;
        var motor=PlayerHealth.Find().GetComponent<ThirdPersonController>();
        await Until(()=>boss.AvatarUltimate && boss.AvatarUltimate.SweepHits>0,7,"No sweep impact");
        Check(motor.IsBallistic,"Enabled sweep knockback did not launch");
        boss.NextSkill();await Wait(.08f);
        Check(!motor.ActionsLocked && !motor.IsStunned && !motor.IsBallistic,"Immediate interruption left a reaction lease");
        Check(!UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>(),"Impact interruption leaked runtime");
        return "PASS cancellation immediately after sweep hit: no action lock, stun or ballistic lease remains";
    }
}
