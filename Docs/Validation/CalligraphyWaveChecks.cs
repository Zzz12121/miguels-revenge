using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

public static class CalligraphyWaveChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static async Task Wait(float seconds)
    {
        float until=Time.time+seconds;var clock=System.Diagnostics.Stopwatch.StartNew();
        while(Time.time<until){Check(clock.Elapsed.TotalSeconds<seconds*5+10,"Editor stopped ticking");await Task.Delay(16);}
    }
    static CalligraphyBossCombat Setup()
    {
        Check(EditorApplication.isPlaying,"Enter Play Mode first");Time.timeScale=1;
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();
        var boss=range.Boss;boss.CancelCast();boss.enabled=false;Place(new Vector3(30,1.05f,0));return boss;
    }
    static void Place(Vector3 position)
    {
        var p=PlayerHealth.Find();var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=position;
        cc.enabled=true;p.GetComponent<ThirdPersonController>().ResetMotion();Physics.SyncTransforms();
    }
    static void Frame()
    {
        var cam=Camera.main;var orbit=cam.GetComponent<ThirdPersonCamera>();if(orbit)orbit.enabled=false;
        var brain=cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;
        cam.transform.SetPositionAndRotation(new Vector3(0,35,0),Quaternion.Euler(90,0,0));
    }
    static string Capture(string name)
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);view.Repaint();
        var rt=type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view) as RenderTexture;
        Check(rt,"No Game View");var old=RenderTexture.active;RenderTexture.active=rt;
        var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();RenderTexture.active=old;
        if(SystemInfo.graphicsUVStartsAtTop){var p=tex.GetPixels();var flip=new Color[p.Length];for(int y=0;y<rt.height;y++)Array.Copy(p,y*rt.width,flip,(rt.height-1-y)*rt.width,rt.width);tex.SetPixels(flip);tex.Apply();}
        string path="Logs/Wave-"+name+".png";System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.Destroy(tex);return path;
    }
    public static async Task<string> Audit(int light)
    {
        var boss=Setup();Frame();var fx=CalligraphyVfxLibrary.Play(light==1?CalligraphyVfxLibrary.Current.frontalPulses:CalligraphyVfxLibrary.Current.frontal,
            new Vector3(0,.05f,0),Quaternion.identity,1.1f,1,0,boss.EffectsRoot);fx.ConfigureWaves(1,1);
        var s=new StringBuilder();var buffer=new ParticleSystem.Particle[2048];var data=new System.Collections.Generic.List<Vector4>();var mesh=new Mesh();
        foreach(var ps in fx.Systems){var r=ps.GetComponent<ParticleSystemRenderer>();s.AppendLine($"CONFIG {ps.name} active={ps.gameObject.activeInHierarchy} emission={ps.emission.enabled} delay={ps.main.startDelay.constantMax} life={ps.main.startLifetime.constantMax} renderer={r?.enabled} parent={ps.transform.parent?.name}");}
        float[] ages={.01f,.05f,.2f,.4f,.7f,1f,1.5f};
        foreach(float age in ages)
        {
            while(fx.Age<age)await Wait(.01f);Capture(light+"-"+age);
            foreach(var ps in fx.Systems.Where(p=>p.gameObject.activeInHierarchy))
            {
                var r=ps.GetComponent<ParticleSystemRenderer>();int count=ps.GetParticles(buffer);
                if(ps.name.StartsWith("yy_Shockwave_1")){var burst=ps.emission.GetBurst(0);s.AppendLine($"STATUS age={fx.Age:F2} count={count} playing={ps.isPlaying} alive={ps.IsAlive(false)} time={ps.time} duration={ps.main.duration} burst={burst.count.constantMax}/{burst.cycleCount}/{burst.repeatInterval}/{burst.probability}");}
                if(count==0 || !r || !r.enabled)continue;
                if(r.renderMode!=ParticleSystemRenderMode.Mesh && !ps.name.StartsWith("yy_Shockwave_1"))continue;
                r.BakeMesh(mesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                s.AppendLine($"age={fx.Age:F3} {ps.name} count={count} bounds={mesh.bounds} mesh={r.mesh?.name}");
                if(ps.name.StartsWith("yy_Shockwave_1"))
                {
                    ps.GetCustomParticleData(data,ParticleSystemCustomData.Custom1);
                    s.AppendLine($"life={buffer[0].startLifetime-buffer[0].remainingLifetime:F3}/{buffer[0].startLifetime} custom={data[0]} mat={r.sharedMaterial.name} shader={r.sharedMaterial.shader.name}");
                    if(age<.1f)
                    {
                        var uv=r.mesh.uv;var vertices=r.mesh.vertices;
                        s.AppendLine("streams="+string.Join(",",GetStreams(r)));
                        s.AppendLine("UV sample="+string.Join(";",Enumerable.Range(0,uv.Length).Where(i=>i%Math.Max(1,uv.Length/10)==0).Select(i=>vertices[i]+":"+uv[i])));
                    }
                }
            }
        }
        UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(fx.gameObject);boss.enabled=true;boss.StopBattle();return s.ToString();
    }
    static System.Collections.Generic.List<ParticleSystemVertexStream> GetStreams(ParticleSystemRenderer r)
    {var list=new System.Collections.Generic.List<ParticleSystemVertexStream>();r.GetActiveVertexStreams(list);return list;}
    static void ClearDefence()
    {
        var p=PlayerHealth.Find();p.HealFull();
        typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,0f);
        p.GetComponent<PlayerStamina>().Refill();
    }
    static Vector3 Velocity()=> (Vector3)typeof(ThirdPersonController).GetField("horizontalVelocity",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(PlayerHealth.Find().GetComponent<ThirdPersonController>());
    public static async Task<string> PulseArrival(int yaw)
    {
        var boss=Setup();ClearDefence();Frame();var p=PlayerHealth.Find();
        var rotation=Quaternion.Euler(0,yaw,0);Place(rotation*new Vector3(0,1.05f,12));await Wait(.15f);
        var settings=new CalligraphyBossCombat.WaveSettings{vfxScale=2.2f,waveCount=1,damage=1,controlSeconds=.3f,knockbackSpeed=6,launchSpeed=3};
        float hit=-1;Vector3 velocity=default;Action<float> receipt=d=>{hit=Time.time;};p.OnDamaged+=receipt;
        float start=Time.time;
        try
        {
            CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),rotation*Vector3.forward,true,false,boss.EffectsRoot,settings);
            await Wait(.1f);Check(hit<0,"Hit before front reached the distant player");Capture("before-front-"+yaw);
            float until=Time.time+1;while(hit<0 && Time.time<until)await Wait(.005f);
            Check(hit>=0,"Visible front failed to hit grounded player");velocity=Velocity();
            Check(Vector3.Dot(velocity.normalized,rotation*Vector3.forward)>.99f,"Knockback is not aligned to moving wave: "+velocity);
            Capture("front-hit-"+yaw);await Wait(1.5f);
            Check(p.Health==99,"Same wave hit repeatedly");
            return $"PASS yaw={yaw}, front arrival {hit-start:F3}s, no early/duplicate hit, velocity={velocity}";
        }
        finally{p.OnDamaged-=receipt;boss.enabled=true;boss.StopBattle();}
    }
    public static async Task<string> SlamArea(int sample)
    {
        var boss=Setup();ClearDefence();Frame();var p=PlayerHealth.Find();
        Vector3[] points={new(0,1.05f,1),new(0,1.05f,8),new(4,1.05f,8),new(25,1.05f,25)};
        Place(points[sample]);await Wait(.15f);float hit=-1;Action<float> receipt=d=>hit=Time.time;p.OnDamaged+=receipt;
        try
        {
            var settings=new CalligraphyBossCombat.WaveSettings{vfxScale=1.1f,waveCount=1,damage=1,controlSeconds=.3f,knockbackSpeed=9,launchSpeed=7};
            float start=Time.time;CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),Vector3.forward,false,false,boss.EffectsRoot,settings);
            float until=Time.time+1;while(hit<0 && Time.time<until)await Wait(.005f);
            var velocity=Velocity();await Wait(.08f);Capture("slam-area-"+sample);
            if(sample==3)Check(hit<0,"Hit outside visible effect");
            else{Check(hit>=0,"No knockback inside entire visible slam at "+points[sample]);Check(velocity.magnitude>0,"No launch");}
            if(sample==0)Check(velocity.magnitude>7.5f,"Near impact lost its strong launch");
            if(sample==1 || sample==2)Check(velocity.magnitude<6,"Distant slam lacks distance falloff");
            await Wait(1.6f);Check(p.Health==(sample==3?100:99),"Slam repeated damage");
            return $"PASS sample={sample} at={points[sample]}, arrival={(hit<0?-1:hit-start):F3}, horizontal speed={velocity.magnitude:F3}";
        }
        finally{p.OnDamaged-=receipt;boss.enabled=true;boss.StopBattle();}
    }
    public static async Task<string> JumpRepeatedPulses()
    {
        var boss=Setup();ClearDefence();Frame();var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();
        Place(new Vector3(0,1.05f,12));await Wait(.2f);Check(motor.IsGrounded,"Fixture not grounded");
        // Use the controller's existing input hook when the Editor is unfocused.
        // This is its normal jump/gravity path, not Launch or a teleported air pose.
        bool originalAI=motor.aiControlled;motor.aiControlled=true;
        int hits=0;float first=-1;Action<float> receipt=d=>{hits++;if(first<0)first=Time.time;};p.OnDamaged+=receipt;
        float start=Time.time;float clearance1=0,clearance2=0;
        try
        {
            motor.SetAIInput(Vector3.zero,false,true);await Wait(.07f);
            Check(motor.VerticalVelocity>0,"Fixture jump not accepted: locked="+motor.ActionsLocked+" stunned="+motor.IsStunned+" grounded="+motor.IsGrounded);
            CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),Vector3.forward,true,false,boss.EffectsRoot,
                new CalligraphyBossCombat.WaveSettings{vfxScale=2.2f,waveCount=3,waveInterval=1.25f,damage=1,controlSeconds=.3f,knockbackSpeed=6,launchSpeed=3});
            start=Time.time;await Wait(.23f);clearance1=p.FeetY;Capture("jump-first");Check(hits==0 && p.FeetY>.55f,"First jump did not clear pulse: hits="+hits+" feet="+p.FeetY);
            while(Time.time<start+1.2f)await Wait(.005f);
            Check(hits==0 && motor.IsGrounded,"First wave hit after landing or fixture not grounded");
            motor.SetAIInput(Vector3.zero,false,true);await Wait(.035f);await Wait(.23f);clearance2=p.FeetY;Capture("jump-second");
            Check(hits==0 && p.FeetY>.55f,"Second jump did not clear pulse");
            while(Time.time<start+2.35f)await Wait(.005f);Check(hits==0 && motor.IsGrounded,"Second wave hit after landing");
            while(hits==0 && Time.time<start+3.4f)await Wait(.005f);
            Check(hits==1,"Standing player must be hit by the third front");Check(first-start>2.5f,"Damage before third wave arrival");
            await Wait(1.5f);Check(hits==1,"Old waves retriggered");
            return $"PASS normal controller jumps avoided first/second fronts (feet {clearance1:F2}/{clearance2:F2}m); landing did not retrigger; grounded third hit at {first-start:F3}s";
        }
        finally{p.OnDamaged-=receipt;motor.SetAIInput(Vector3.zero);motor.aiControlled=originalAI;boss.enabled=true;boss.StopBattle();}
    }
    public static async Task<string> WaveGap()
    {
        var boss=Setup();ClearDefence();Frame();var p=PlayerHealth.Find();float hit=-1;Action<float> receipt=d=>hit=Time.time;p.OnDamaged+=receipt;
        try
        {
            float start=Time.time;CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),Vector3.forward,true,false,boss.EffectsRoot,
                new CalligraphyBossCombat.WaveSettings{vfxScale=2.2f,waveCount=2,waveInterval=1.25f,damage=1,controlSeconds=.3f});
            await Wait(.6f);Place(new Vector3(0,1.05f,12));await Wait(.35f);Check(hit<0,"Empty gap / old cone damaged the player");
            while(hit<0 && Time.time<start+2)await Wait(.005f);Check(hit>start+1.25f,"Next arriving front failed");
            await Wait(1.5f);return $"PASS entering old cone at 0.6s is safe; next actual front hit at {hit-start:F3}s";
        }
        finally{p.OnDamaged-=receipt;boss.enabled=true;boss.StopBattle();}
    }
    public static async Task<string> DefenceAndSlamResidue()
    {
        var boss=Setup();ClearDefence();var p=PlayerHealth.Find();Place(new Vector3(0,1.05f,12));await Wait(.15f);
        try
        {
            p.GrantDodgeInvulnerability(.5f);
            CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),Vector3.forward,true,false,boss.EffectsRoot,
                new CalligraphyBossCombat.WaveSettings{vfxScale=2.2f,waveCount=1,damage=1});
            await Wait(1.2f);Check(p.Health==100 && !p.GetComponent<ThirdPersonController>().IsBallistic,"Dodged pulse dealt delayed damage/control");
            CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),Vector3.forward,false,true,boss.EffectsRoot,
                new CalligraphyBossCombat.WaveSettings{vfxScale=1.1f,waveCount=1,damage=1});
            Place(new Vector3(0,1.05f,1));await Wait(.3f);Check(p.Health==100,"Direct slam victim received a second area hit");
            Place(new Vector3(30,1.05f,0));
            CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),Vector3.forward,false,false,boss.EffectsRoot,
                new CalligraphyBossCombat.WaveSettings{vfxScale=1.1f,waveCount=1,damage=1});
            await Wait(2);Place(new Vector3(0,1.05f,1));await Wait(.5f);Check(p.Health==100,"Decorative stain became lingering knockback");
            return "PASS dodge invulnerability preserved; direct hit excluded from area duplicate; old ink stain is harmless";
        }
        finally{boss.enabled=true;boss.StopBattle();}
    }
}
