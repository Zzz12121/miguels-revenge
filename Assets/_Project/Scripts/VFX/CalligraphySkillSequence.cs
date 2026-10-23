using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Attack-owned package sequences. Warning positions never follow the player.</summary>
public sealed class CalligraphySkillSequence : MonoBehaviour
{
    PlayerHealth player;
    ThirdPersonController slowed;
    int slowLease;
    void OnDisable(){if(slowed && slowLease!=0)slowed.RemoveSpeedMultiplierLease(slowLease);slowLease=0;}
    public float WarningFinishedAt {get;private set;}=-1;
    public float FallStartedAt {get;private set;}=-1;
    public float ImpactAt {get;private set;}=-1;
    readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[2048];
    readonly HashSet<uint> contacted=new();
    Mesh waveMesh;
    readonly List<Vector3> waveVertices=new();
    readonly List<int> waveTriangles=new();
    static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
    {var d=b-a;return Vector2.Distance(p,a+d*(d.sqrMagnitude>.00001f?Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude):0));}
    static bool TriangleTouches(Vector3 point,Vector3 a,Vector3 b,Vector3 c,float radius)
    {
        Vector2 p=new(point.x,point.z),x=new(a.x,a.z),y=new(b.x,b.z),z=new(c.x,c.z);
        float Cross(Vector2 u,Vector2 v)=>u.x*v.y-u.y*v.x;
        float s1=Cross(y-x,p-x),s2=Cross(z-y,p-y),s3=Cross(x-z,p-z);
        if(Mathf.Abs(Cross(y-x,z-x))>.00001f && ((s1>=0 && s2>=0 && s3>=0)||(s1<=0 && s2<=0 && s3<=0)))return true;
        return Mathf.Min(SegmentDistance(p,x,y),Mathf.Min(SegmentDistance(p,y,z),SegmentDistance(p,z,x)))<=radius;
    }
    void OnDestroy(){if(waveMesh)Destroy(waveMesh);}
    static CalligraphySkillSequence Create(Vector3 position,Transform parent)
    {
        var go=new GameObject("Calligraphy timed VFX sequence");go.transform.SetParent(parent,false);go.transform.position=position;
        return go.AddComponent<CalligraphySkillSequence>();
    }
    public static void DragonImpact(Vector3 position,Transform parent,CalligraphyBossCombat.DragonSettings settings=null)
    {var s=Create(position,parent);s.StartCoroutine(s.Explosion(settings??new()));}
    public static void Rune(PlayerHealth player,Vector3 position,float damage,Transform parent,CalligraphyBossCombat.RuneSettings settings=null)
    {var s=Create(position,parent);s.player=player;s.StartCoroutine(s.FallingRune(damage,settings??new()));}
    public static void Wave(PlayerHealth player,Vector3 position,Vector3 direction,bool light,bool excludeDirect,Transform parent,CalligraphyBossCombat.WaveSettings settings=null)
    {var s=Create(position,parent);s.player=player;s.StartCoroutine(s.Frontal(direction,light,excludeDirect,settings??new CalligraphyBossCombat.WaveSettings{waveCount=light?6:1,vfxScale=light?1.8f:.45f,damage=light?10:22,controlSeconds=light?.5f:1.15f,knockbackSpeed=light?3:9,launchSpeed=light?3:7}));}
    IEnumerator Explosion(CalligraphyBossCombat.DragonSettings settings)
    {
        var lib=CalligraphyVfxLibrary.Current;if(!lib){Destroy(gameObject);yield break;}
        var main=CalligraphyVfxLibrary.Play(lib.bigExplosion,transform.position+settings.explosionOffset,Quaternion.Euler(settings.explosionEuler),settings.explosionScale,1,0,transform);
        if(main)main.RestartAtTimeline(settings.explosionTimelineStart);
        yield return new WaitForSeconds(settings.tailStartSeconds);
        var up=CalligraphyVfxLibrary.Play(lib.buffUp,transform.position+settings.buffOffset,Quaternion.Euler(settings.buffEuler),settings.buffScale,1.4f,0,transform);
        var flash=CalligraphyVfxLibrary.Play(lib.flashAoE,transform.position+settings.flashOffset,Quaternion.Euler(settings.flashEuler),settings.flashScale,1.4f,0,transform);
        while(main || up || flash)yield return null;
        Destroy(gameObject);
    }
    IEnumerator FallingRune(float damage,CalligraphyBossCombat.RuneSettings settings)
    {
        var lib=CalligraphyVfxLibrary.Current;if(!lib){Destroy(gameObject);yield break;}
        var warning=CalligraphyVfxLibrary.Play(lib.rune,transform.position+Vector3.up*.04f,Quaternion.identity,settings.scale,settings.warningSpeed,0,transform);
        float warningEnd=settings.warningTimelineEnd>=0?settings.warningTimelineEnd/Mathf.Max(.05f,settings.warningSpeed):warning?warning.MarkerEnd("yy_2_symbol_alpha_soft"):0;
        while(warning && warning.Age<warningEnd)yield return null;
        WarningFinishedAt=Time.time;
        var fall=CalligraphyVfxLibrary.Play(lib.runeFall,transform.position,Quaternion.identity,settings.scale,settings.fallSpeed,0,transform);
        if(fall)fall.RestartAtTimeline(settings.fallTimelineStart);
        FallStartedAt=Time.time;
        // This original prefab's ground impact ring is delayed until the falling beam lands.
        // Observe the actual burst, so changing playback speed cannot desynchronise damage.
        while(fall && !fall.HasLiveParticles("Ring_Halo_faded_add_soft"))yield return null;
        if(fall)ImpactAt=Time.time;
        if(fall && player && CalligraphyPlayerHitGate.Touches(player,transform.position,transform.position+Vector3.up*1.7f,settings.hitRadius*(settings.scale/.45f)))
        {
            Vector3 delta=player.transform.position-transform.position;
            bool center=Vector3.ProjectOnPlane(delta,Vector3.up).magnitude<settings.centerRadius*(settings.scale/.45f);
            CalligraphyPlayerHitGate.Hit(player,damage,delta.sqrMagnitude>.01f?delta.normalized:Vector3.down,"被落印擊倒",player.transform.position+Vector3.up*.9f,
                center?CalligraphyPlayerControl.Reaction.Stun:CalligraphyPlayerControl.Reaction.None,settings.stunSeconds);
        }
        // Residual ink uses the original Fall's ground-wash particles, not another damage blast.
        var residue=CalligraphyVfxLibrary.Play(lib.runeFall,transform.position+Vector3.up*.05f,Quaternion.identity,settings.residueRadius*.3f,1,0,transform);
        if(residue)residue.MakeResidue(settings.residueLifetime);
        float zoneUntil=Time.time+settings.residueLifetime,slowUntil=0;
        while(Time.time<zoneUntil || slowLease!=0 || fall)
        {
            if(slowLease!=0 && Time.time>=slowUntil){if(slowed)slowed.RemoveSpeedMultiplierLease(slowLease);slowLease=0;}
            if(Time.time<zoneUntil && slowLease==0 && player && Mathf.Abs(player.FeetY-transform.position.y)<.6f &&
                Vector3.ProjectOnPlane(player.transform.position-transform.position,Vector3.up).sqrMagnitude<=settings.residueRadius*settings.residueRadius)
            {
                slowed=player.GetComponent<ThirdPersonController>();
                if(slowed){slowLease=slowed.AddSpeedMultiplierLease(settings.slowMultiplier);slowUntil=Time.time+settings.slowSeconds;}
            }
            yield return null;
        }
        Destroy(gameObject);
    }
    IEnumerator Frontal(Vector3 direction,bool light,bool excludeDirect,CalligraphyBossCombat.WaveSettings settings)
    {
        var lib=CalligraphyVfxLibrary.Current;if(!lib){Destroy(gameObject);yield break;}
        direction=Quaternion.Euler(0,settings.yawOffset,0)*direction;
        var fx=CalligraphyVfxLibrary.Play(light?lib.frontalPulses:lib.frontal,transform.position,
            Quaternion.LookRotation(direction)*Quaternion.Euler(0,light?-90:0,0),settings.vfxScale,1,0,transform);
        if(fx)fx.ConfigureWaves(settings.waveCount,settings.waveInterval);
        float nextHit=0;
        while(fx)
        {
            if(player && !excludeDirect && Time.time>=nextHit)
            {
                // Follow actual moving shockwave particles, never an invisible cone ahead of them.
                foreach(var ps in fx.Systems)
                {
                    if(!ps || !ps.name.StartsWith("yy_Shockwave_1") || !ps.gameObject.activeInHierarchy)continue;
                    int count=ps.GetParticles(particles);
                    var renderer=ps.GetComponent<ParticleSystemRenderer>();
                    if(!renderer || !renderer.enabled || count==0)continue;
                    if(!waveMesh)waveMesh=new Mesh();
                    renderer.BakeMesh(waveMesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                    waveMesh.GetVertices(waveVertices);
                    waveMesh.GetTriangles(waveTriangles,0);
                    int verticesPerParticle=waveVertices.Count/count;
                    if(verticesPerParticle==0)continue;
                    for(int i=0;i<count;i++)
                    {
                        uint key=particles[i].randomSeed;if(contacted.Contains(key))continue;
                        if(particles[i].GetCurrentColor(ps).a<8)continue;
                        // Mesh vertices already contain particle growth, rotation and hierarchy scale.
                        // A bound per wave avoids joining the empty gaps between successive pulses.
                        var bounds=new Bounds(renderer.transform.position+waveVertices[i*verticesPerParticle],Vector3.zero);
                        for(int v=1;v<verticesPerParticle;v++)bounds.Encapsulate(renderer.transform.position+waveVertices[i*verticesPerParticle+v]);
                        if(player.FeetY>transform.position.y+.9f)continue;
                        Vector3 foot=player.transform.position;foot.y=player.FeetY+.35f;
                        Vector3 point=bounds.ClosestPoint(foot);
                        if((point-foot).sqrMagnitude>.45f*.45f)continue;
                        bool onWave=false;
                        int begin=i*verticesPerParticle,end=begin+verticesPerParticle;
                        for(int tri=0;tri<waveTriangles.Count;tri+=3)
                        {
                            int a=waveTriangles[tri],b=waveTriangles[tri+1],c=waveTriangles[tri+2];
                            if(a<begin || a>=end || b<begin || b>=end || c<begin || c>=end)continue;
                            if(TriangleTouches(foot,renderer.transform.position+waveVertices[a],renderer.transform.position+waveVertices[b],renderer.transform.position+waveVertices[c],.32f)){onWave=true;break;}
                        }
                        if(!onWave)continue;
                        contacted.Add(key);nextHit=Time.time+settings.hitInterval;
                        CalligraphyPlayerHitGate.Hit(player,settings.damage,direction,"被墨浪擊倒",point+Vector3.up*.5f,
                            light?CalligraphyPlayerControl.Reaction.LightLaunch:CalligraphyPlayerControl.Reaction.StrongLaunch,settings.controlSeconds,new Vector2(settings.knockbackSpeed,settings.launchSpeed));
                        if(!light)excludeDirect=true;
                        break;
                    }
                    if(Time.time<nextHit)break;
                }
            }
            yield return null;
        }
        Destroy(gameObject);
    }
}
