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
    readonly HashSet<(ParticleSystem,uint)> contacted=new();
    readonly Dictionary<(ParticleSystem,uint),float> previousWaveOffset=new();
    readonly List<Vector4> waveUV=new(),customData=new();
    // The package scrolls Tx_Line_08_Up through a stationary cone. These are the
    // bright body's texture-V limits, not the UV limits of that entire cone.
    const float PulseTextureMin=.4f,PulseTextureMax=.85f;
    readonly Vector3[] clipA=new Vector3[8],clipB=new Vector3[8];
    readonly float[] clipVA=new float[8],clipVB=new float[8];
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
    static int ClipBand(Vector3[] from,float[] values,int count,Vector3[] to,float[] output,float limit,bool above)
    {
        int written=0;
        for(int i=0;i<count;i++)
        {
            int j=(i+count-1)%count;bool inside=above?values[i]>=limit:values[i]<=limit;
            bool previous=above?values[j]>=limit:values[j]<=limit;
            if(inside!=previous)
            {float t=(limit-values[j])/(values[i]-values[j]);to[written]=Vector3.LerpUnclamped(from[j],from[i],t);output[written++]=limit;}
            if(inside){to[written]=from[i];output[written++]=values[i];}
        }
        return written;
    }
    bool TouchesMesh(Vector3 foot,Vector3 origin,int begin,int end,float radius,float minV=float.NegativeInfinity,float maxV=float.PositiveInfinity)
    {
        for(int tri=0;tri<waveTriangles.Count;tri+=3)
        {
            int a=waveTriangles[tri],b=waveTriangles[tri+1],c=waveTriangles[tri+2];
            if(a<begin || a>=end || b<begin || b>=end || c<begin || c>=end)continue;
            if(float.IsNegativeInfinity(minV))
            {if(TriangleTouches(foot,origin+waveVertices[a],origin+waveVertices[b],origin+waveVertices[c],radius))return true;continue;}
            clipA[0]=origin+waveVertices[a];clipA[1]=origin+waveVertices[b];clipA[2]=origin+waveVertices[c];
            clipVA[0]=waveUV[a].y;clipVA[1]=waveUV[b].y;clipVA[2]=waveUV[c].y;
            int count=ClipBand(clipA,clipVA,3,clipB,clipVB,minV,true);
            if(count<3)continue;
            count=ClipBand(clipB,clipVB,count,clipA,clipVA,maxV,false);
            for(int i=1;i<count-1;i++)if(TriangleTouches(foot,clipA[0],clipA[i],clipA[i+1],radius))return true;
        }
        return false;
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
            if(player && !excludeDirect)
            {
                Vector3 foot=player.transform.position;foot.y=player.FeetY;
                bool slamContact=false,slamImpactActive=false;float slamRadius=0;
                var controller=player.GetComponent<CharacterController>();
                float bodyRadius=controller?controller.radius*Mathf.Max(Mathf.Abs(player.transform.lossyScale.x),Mathf.Abs(player.transform.lossyScale.z)):.32f;
                foreach(var ps in fx.Systems)
                {
                    if(!ps || !ps.gameObject.activeInHierarchy)continue;
                    bool mainWave=ps.name.StartsWith("yy_Shockwave_1");
                    bool slamSurface=ps.name.IndexOf("Shockwave",System.StringComparison.Ordinal)>=0 || ps.name.StartsWith("yy_Liquid_Splat_Frontal");
                    if(light?!mainWave:!slamSurface)continue;
                    int count=ps.GetParticles(particles);
                    var renderer=ps.GetComponent<ParticleSystemRenderer>();
                    if(!renderer || !renderer.enabled || count==0)continue;
                    if(!light && mainWave)slamImpactActive=true;
                    if(!waveMesh)waveMesh=new Mesh();
                    renderer.BakeMesh(waveMesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                    waveMesh.GetVertices(waveVertices);
                    waveMesh.GetTriangles(waveTriangles,0);
                    if(light){waveMesh.GetUVs(0,waveUV);ps.GetCustomParticleData(customData,ParticleSystemCustomData.Custom1);}
                    int verticesPerParticle=waveVertices.Count/count;
                    if(verticesPerParticle==0)continue;
                    for(int i=0;i<count;i++)
                    {
                        var key=(ps,particles[i].randomSeed);
                        if(particles[i].GetCurrentColor(ps).a<8)continue;
                        int begin=i*verticesPerParticle,end=begin+verticesPerParticle;
                        var bounds=new Bounds(renderer.transform.position+waveVertices[begin],Vector3.zero);
                        for(int v=begin;v<end;v++)
                        {
                            Vector3 vertex=renderer.transform.position+waveVertices[v];bounds.Encapsulate(vertex);
                            if(!light)slamRadius=Mathf.Max(slamRadius,Vector3.ProjectOnPlane(vertex-transform.position,Vector3.up).magnitude);
                        }
                        if(!light)
                        {
                            // Include both the central wash/rings and the whole frontal impact,
                            // not just yy_Shockwave_1's small central circle. Decoration is harmless.
                            if(foot.y>=bounds.min.y-.6f && foot.y<=bounds.max.y+1.8f &&
                                TouchesMesh(foot,renderer.transform.position,begin,end,bodyRadius))slamContact=true;
                            continue;
                        }
                        if(contacted.Contains(key) || customData.Count<=i || waveUV.Count!=waveVertices.Count)continue;
                        float offset=customData[i].w;
                        float previous=previousWaveOffset.TryGetValue(key,out var last)?last:ps.customData.GetVector(ParticleSystemCustomData.Custom1,3).Evaluate(0);
                        previousWaveOffset[key]=offset;
                        var material=renderer.sharedMaterial;
                        float tiling=material.GetTextureScale("_MainTex").y,translation=material.GetTextureOffset("_MainTex").y;
                        if(Mathf.Abs(tiling)<.00001f)continue;
                        float minV=(PulseTextureMin-translation)/tiling-Mathf.Max(previous,offset);
                        float maxV=(PulseTextureMax-translation)/tiling-Mathf.Min(previous,offset);
                        if(!TouchesMesh(foot,renderer.transform.position,begin,end,bodyRadius,minV,maxV))continue;
                        // A jumped/blocked/rolled-over wave is spent as soon as it passes in XZ.
                        // Landing in its old cone must never turn it into a delayed hit.
                        contacted.Add(key);
                        if(foot.y>bounds.max.y+settings.pulseContactHeight || foot.y<bounds.min.y-.6f || Time.time<nextHit)continue;
                        Vector3 launchDirection=Vector3.ProjectOnPlane(bounds.center-fx.transform.position,Vector3.up);
                        if(launchDirection.sqrMagnitude<.0001f)launchDirection=-fx.transform.forward;
                        launchDirection.Normalize();
                        CalligraphyPlayerHitGate.Hit(player,settings.damage,launchDirection,"被墨浪擊倒",foot+Vector3.up*.5f,
                            CalligraphyPlayerControl.Reaction.LightLaunch,settings.controlSeconds,new Vector2(settings.knockbackSpeed,settings.launchSpeed));
                        nextHit=Time.time+settings.hitInterval;
                    }
                }
                // The remaining ink stain is decoration once the impact ring has died.
                if(!light && slamContact && slamImpactActive)
                {
                    Vector3 away=Vector3.ProjectOnPlane(foot-transform.position,Vector3.up);
                    float strength=Mathf.Lerp(1,settings.edgeKnockbackMultiplier,Mathf.Clamp01(away.magnitude/Mathf.Max(.01f,slamRadius)));
                    CalligraphyPlayerHitGate.Hit(player,settings.damage,away.sqrMagnitude>.001f?away.normalized:direction,"被巨筆重砸擊倒",foot+Vector3.up*.5f,
                        CalligraphyPlayerControl.Reaction.StrongLaunch,settings.controlSeconds,new Vector2(settings.knockbackSpeed,settings.launchSpeed)*strength);
                    excludeDirect=true;
                }
            }
            yield return null;
        }
        Destroy(gameObject);
    }
}
