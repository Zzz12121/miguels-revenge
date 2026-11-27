using UnityEngine;

/// <summary>Local timeline playback of the original Vertical Rune; never changes the source prefab.</summary>
public sealed class ForestPortalRune : MonoBehaviour
{
    ParticleSystem[] systems;
    public float SourceTime {get;private set;}
    public float SourceEnd {get;private set;}=4;
    public int IdleLoops {get;private set;}
    public int LiveParticles
    {get{int count=0;if(systems!=null)foreach(var p in systems)if(p&&p.gameObject.activeInHierarchy)count+=p.particleCount;return count;}}
    static float Maximum(ParticleSystem.MinMaxCurve curve)
    {
        float value=Mathf.Max(curve.constantMin,curve.constantMax);
        if(curve.mode==ParticleSystemCurveMode.Curve||curve.mode==ParticleSystemCurveMode.TwoCurves)
            for(int i=0;i<=24;i++)value=Mathf.Max(value,Mathf.Max(curve.Evaluate(i/24f,0),curve.Evaluate(i/24f,1)));
        return value;
    }
    public void Initialize(GameObject prefab,float scale)
    {
        var visual=Instantiate(prefab,transform);visual.name="Original Ying Yang Vertical Rune";visual.transform.localPosition=Vector3.zero;visual.transform.localScale=Vector3.one*scale;
        foreach(var helper in visual.GetComponentsInChildren<PilotoStudio.PilotoCameraOffset>(true))
        {helper.enabled=false;if(helper.transform!=visual.transform)helper.gameObject.SetActive(false);}
        foreach(var helper in visual.GetComponentsInChildren<PilotoStudio.ParticleHandler>(true))helper.enabled=false;
        systems=visual.GetComponentsInChildren<ParticleSystem>(true);
        for(int i=0;i<systems.Length;i++)
        {
            var p=systems[i];p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var m=p.main;
            m.loop=false;m.stopAction=ParticleSystemStopAction.None;m.simulationSpeed=1;m.useUnscaledTime=false;m.scalingMode=ParticleSystemScalingMode.Hierarchy;
            p.useAutoRandomSeed=false;p.randomSeed=(uint)(7177+i*7919);
            var r=p.GetComponent<ParticleSystemRenderer>();var e=p.emission;
            if(!p.gameObject.activeInHierarchy||!r||!r.enabled||!e.enabled)continue;
            float last=-1;
            if(Maximum(e.rateOverTime)>0||Maximum(e.rateOverDistance)>0)last=m.duration;
            for(int j=0;j<e.burstCount;j++){var b=e.GetBurst(j);if(Maximum(b.count)>0)last=Mathf.Max(last,b.time+Mathf.Max(0,b.cycleCount-1)*b.repeatInterval);}
            if(last>=0)SourceEnd=Mathf.Max(SourceEnd,Maximum(m.startDelay)+last+Maximum(m.startLifetime));
        }
        SetTime(.01f,true);
    }
    public void TickIdle(float dt,float firstSeconds)
    {
        float next=SourceTime+dt;
        if(next>=firstSeconds){next=Mathf.Repeat(next,firstSeconds);IdleLoops++;SetTime(next,true);}
        else SetTime(next);
    }
    public void SetTime(float target,bool restart=false)
    {
        if(systems==null)return;
        target=Mathf.Max(0,target);bool reset=restart||target<SourceTime;
        float step=reset?target:target-SourceTime;
        foreach(var p in systems)if(p&&p.gameObject.activeInHierarchy)
        {p.Simulate(step,false,reset,false);p.Pause(false);}
        SourceTime=target;
    }
}
