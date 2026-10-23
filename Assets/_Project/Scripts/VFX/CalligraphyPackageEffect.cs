using UnityEngine;

/// <summary>Bounded one-shot playback of original package particles, with natural tail cleanup.</summary>
public sealed class CalligraphyPackageEffect : MonoBehaviour
{
    public float Age {get;private set;}
    public float Duration {get;private set;}
    public ParticleSystem[] Systems {get;private set;}
    float speed=1,fadeAt=float.PositiveInfinity,fadeSeconds;
    readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[8192];
    float previousFade=1;
    static float Maximum(ParticleSystem.MinMaxCurve curve)
    {
        float maximum=Mathf.Max(curve.constantMin,curve.constantMax);
        if(curve.mode==ParticleSystemCurveMode.Curve || curve.mode==ParticleSystemCurveMode.TwoCurves)
            for(int i=0;i<=32;i++){maximum=Mathf.Max(maximum,curve.Evaluate(i/32f,0));maximum=Mathf.Max(maximum,curve.Evaluate(i/32f,1));}
        return maximum;
    }
    static float VisibleLifetime(ParticleSystem ps)
    {
        var e=ps.emission;var m=ps.main;var renderer=ps.GetComponent<ParticleSystemRenderer>();
        if(!ps.gameObject.activeInHierarchy || !e.enabled || !renderer || !renderer.enabled || !renderer.sharedMaterial)return 0;
        float last=-1;
        if(Maximum(e.rateOverTime)>0 || Maximum(e.rateOverDistance)>0)last=m.duration;
        for(int i=0;i<e.burstCount;i++){var b=e.GetBurst(i);if(Maximum(b.count)>0)last=Mathf.Max(last,b.time+Mathf.Max(0,b.cycleCount-1)*b.repeatInterval);}
        return last<0?0:Maximum(m.startDelay)+last+Maximum(m.startLifetime);
    }
    float stopAt=float.PositiveInfinity,scheduledFade=1.5f;
    Transform groundTarget;
    Vector3 groundOffset;
    Quaternion groundRotation=Quaternion.identity;
    public bool PlaybackComplete
    {
        get
        {
            if(Age<.05f || Systems==null)return false;
            foreach(var ps in Systems)
            {
                if(!ps || !ps.gameObject.activeInHierarchy)continue;
                var emission=ps.emission;var renderer=ps.GetComponent<ParticleSystemRenderer>();
                if(renderer && renderer.enabled && emission.enabled && ps.IsAlive(false))return false;
            }
            return true;
        }
    }
    public void FollowGround(Transform target,Vector3 offset,float spinMultiplier,bool follow=true,bool worldSpace=true,float yaw=0)
    {
        groundTarget=follow?target:null;groundOffset=offset;groundRotation=Quaternion.Euler(0,yaw,0);
        if(target)transform.SetPositionAndRotation(CalligraphyGeometry.Ground(target.position,target)+offset,groundRotation);
        foreach(var ps in Systems)
        {
            var main=ps.main;main.simulationSpace=worldSpace?ParticleSystemSimulationSpace.World:ParticleSystemSimulationSpace.Local;
            var r=ps.rotationOverLifetime;
            if(r.enabled){r.xMultiplier*=spinMultiplier;r.yMultiplier*=spinMultiplier;r.zMultiplier*=spinMultiplier;}
        }
    }
    public void ConfigureWaves(int count,float interval)
    {
        count=Mathf.Clamp(count,1,16);interval=Mathf.Max(.1f,interval);
        foreach(var ps in Systems)
        {
            if(!ps || !ps.name.StartsWith("yy_Shockwave_1"))continue;
            ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.duration=Mathf.Max(.1f,(count-1)*interval+.1f);
            var emission=ps.emission;emission.rateOverTime=0;emission.rateOverDistance=0;
            emission.SetBursts(new[]{new ParticleSystem.Burst(0,1,1,count,interval)});
            Duration=Mathf.Max(Duration,(main.startDelay.constantMax+main.duration+main.startLifetime.constantMax)/speed);
            ps.Play(false);
        }
    }
    public bool HasLiveParticles(string particleName)
    {
        foreach(var ps in Systems)if(ps && ps.gameObject.activeInHierarchy && ps.name==particleName && ps.particleCount>0)return true;
        return false;
    }
    // Burst-only systems may have a long duration with no emission at all. Do not
    // use IsAlive/duration as a visual end marker (the Rune used to wait on these).
    public float MarkerEnd(string name)
    {
        float end=0;
        foreach(var ps in Systems)if(ps && ps.name==name && ps.gameObject.activeInHierarchy)
            end=Mathf.Max(end,(ps.main.startDelay.constantMax+ps.main.startLifetime.constantMax)/speed);
        return end;
    }
    public void RestartAtTimeline(float start)
    {
        Duration=0;Age=0;
        foreach(var ps in Systems)
        {
            ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(!ps.gameObject.activeInHierarchy)continue;
            var m=ps.main;var e=ps.emission;
            // Keep the original impact particles and their relative offsets; omit charge.
            if(m.startDelay.constantMax+0.001f<start){e.enabled=false;continue;}
            m.startDelay=Mathf.Max(0,m.startDelay.constantMax-start);
            Duration=Mathf.Max(Duration,VisibleLifetime(ps)/speed);
            ps.Play(false);
        }
        Duration=Mathf.Max(.2f,Duration);
    }
    public void MakeResidue(float lifetime)
    {
        Age=0;Duration=lifetime;
        foreach(var ps in Systems)
        {
            ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var e=ps.emission;
            if(ps.name!="yy_Shockwave_3_Distorted_still_alpha"){e.enabled=false;continue;}
            var m=ps.main;m.startDelay=0;m.startLifetime=lifetime;m.startSpeed=0;
            e.enabled=true;e.rateOverTime=0;e.SetBursts(new[]{new ParticleSystem.Burst(0,1)});
            ps.Play(false);
        }
    }
    public void StopAfter(float delay,float tail=1.5f){stopAt=Age+delay;scheduledFade=tail;}
    public void Initialize(float playbackSpeed,float skip)
    {
        speed=Mathf.Max(.01f,playbackSpeed);Systems=GetComponentsInChildren<ParticleSystem>(true);
        // Screen overlays belong to the demo. World-space combat effects must remain at impact.
        foreach(var helper in GetComponentsInChildren<PilotoStudio.PilotoCameraOffset>(true))
        {helper.enabled=false;if(helper.transform!=transform)helper.gameObject.SetActive(false);}
        foreach(var handler in GetComponentsInChildren<PilotoStudio.ParticleHandler>(true))handler.enabled=false;
        for(int i=0;i<Systems.Length;i++)
        {
            var ps=Systems[i];ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;
            m.loop=false;m.stopAction=ParticleSystemStopAction.None;m.simulationSpeed=speed;m.scalingMode=ParticleSystemScalingMode.Hierarchy;
            Duration=Mathf.Max(Duration,(VisibleLifetime(ps)-skip)/speed);
            if(ps.gameObject.activeInHierarchy){if(skip>0)ps.Simulate(skip,false,true,true);ps.Play(false);}
        }
        Duration=Mathf.Clamp(Duration,.2f,30);
    }
    public void FadeOut(float seconds=1.5f)
    {
        if(fadeAt<float.PositiveInfinity)return;fadeAt=Age;fadeSeconds=Mathf.Max(.05f,seconds);
        foreach(var ps in Systems)if(ps)ps.Stop(false,ParticleSystemStopBehavior.StopEmitting);
    }
    void Update()
    {
        Age+=Time.deltaTime;
        if(groundTarget)transform.SetPositionAndRotation(CalligraphyGeometry.Ground(groundTarget.position,groundTarget)+groundOffset,groundRotation);
        if(Age>=stopAt){stopAt=float.PositiveInfinity;FadeOut(scheduledFade);}
        if(Age>=fadeAt)
        {
            float a=1-Mathf.Clamp01((Age-fadeAt)/fadeSeconds);
            // Fade the actual particles, independently of each vendor shader's color property.
            float ratio=previousFade>.0001f?a/previousFade:0;previousFade=a;
            foreach(var ps in Systems)if(ps)
            {
                int count=ps.GetParticles(particles);
                for(int i=0;i<count;i++){Color color=particles[i].startColor;color.a*=ratio;particles[i].startColor=color;}
                ps.SetParticles(particles,count);
            }
            if(a<=0){Destroy(gameObject);return;}
        }
        if(Age>Duration+.2f)Destroy(gameObject);
    }
}
