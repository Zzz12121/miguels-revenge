using System.Collections.Generic;
using UnityEngine;

/// <summary>URP adapter for SUIMONO splash/ripple art. No dependency on the legacy Built-in water renderer.</summary>
[DisallowMultipleComponent]
public sealed class DojoRiverWater : MonoBehaviour
{
    public const float WaterY = -.22f;
    public GameObject splashPrefab, ripplePrefab;
    public Material splashMaterial, rippleMaterial;
    public AudioClip splashSound, riverSound;
    [Range(4,24)] public int poolSize = 12;
    [Range(.05f,.5f)] public float contactInterval = .10f;
    public int ImpactCount { get; private set; }
    public int ActorContactCount { get; private set; }
    public int ParticleContactCount { get; private set; }
    public static float Center(float x) => 116.2f + 2.65f*Mathf.Sin(x*.105f) + .65f*Mathf.Sin(x*.24f+1.7f);
    public static float Width(float x) => 4.1f + .65f*Mathf.Sin(x*.16f+.8f);
    public static float Bed(float x,float z)
    {
        if(Mathf.Abs(x)>48) return -.015f;
        float q=Mathf.Abs(z-Center(x))/(Width(x)*.5f+.7f);
        return Mathf.Lerp(-1.05f,-.015f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,1,q)));
    }
    public bool Contains(Vector3 world,float margin=0)
    {
        Vector3 p=transform.InverseTransformPoint(world);
        return Mathf.Abs(p.x)<46.7f && Mathf.Abs(p.z-Center(p.x))<Width(p.x)*.5f+margin;
    }
    sealed class Contact { public Vector3 position; public float last,seen; }
    readonly Dictionary<int,Contact> contacts=new Dictionary<int,Contact>();
    readonly Dictionary<int,float> particleCooldown=new Dictionary<int,float>();
    readonly List<int> expired=new List<int>();
    readonly Collider[] overlaps=new Collider[128];
    readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[128];
    ParticleSystem[] splashes,ripples,sceneParticles;
    AudioSource audioSource;
    int cursor; float tick,scan,nextSound;
    void Awake()
    {
        splashes=new ParticleSystem[poolSize];ripples=new ParticleSystem[poolSize];
        for(int i=0;i<poolSize;i++)
        { splashes[i]=Create(splashPrefab,splashMaterial,false,i);ripples[i]=Create(ripplePrefab,rippleMaterial,true,i); }
        var sound=new GameObject("River ambient audio");sound.transform.SetParent(transform,false);sound.transform.localPosition=new Vector3(0,WaterY,Center(0));
        audioSource=sound.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.spatialBlend=1;audioSource.minDistance=8;audioSource.maxDistance=55;audioSource.volume=.12f;
        if(riverSound){audioSource.clip=riverSound;audioSource.loop=true;audioSource.Play();}
    }
    ParticleSystem Create(GameObject source,Material material,bool ring,int index)
    {
        var go=source?Instantiate(source,transform):new GameObject();
        go.name=(ring?"SUIMONO_Ripple_":"SUIMONO_Splash_")+index;go.transform.SetParent(transform,false);go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;go.layer=0;
        foreach(var extra in go.GetComponentsInChildren<ParticleSystem>())extra.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var ps=go.GetComponent<ParticleSystem>();if(!ps)ps=go.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=false;main.playOnAwake=false;main.duration=2;main.maxParticles=64;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startLifetime=ring?1.4f:.65f;main.startSpeed=0;main.startSize=ring?.32f:.08f;main.gravityModifier=ring?0:.35f;main.startColor=new Color(.72f,.84f,.82f,.65f);
        var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
        var velocity=ps.velocityOverLifetime;velocity.enabled=false;var noise=ps.noise;noise.enabled=false;var trails=ps.trails;trails.enabled=false;var collision=ps.collision;collision.enabled=false;
        var texture=ps.textureSheetAnimation;texture.enabled=false;var rotation=ps.rotationOverLifetime;rotation.enabled=false;
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,ring?1:.8f,1,ring?7:.15f));
        var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.08f),new GradientAlphaKey(0,1)});color.color=gradient;
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ring?ParticleSystemRenderMode.HorizontalBillboard:ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.sortingFudge=ring?1:0;
        return ps;
    }
    /// <summary>Explicit integration point for melee/beam hit code; position must actually touch the river surface.</summary>
    public bool EmitImpact(Vector3 world,float strength=1)
    {
        if(!Contains(world)||Mathf.Abs(transform.InverseTransformPoint(world).y-WaterY)>.55f||splashes==null)return false;
        Vector3 p=world;p.y=transform.TransformPoint(new Vector3(0,WaterY,0)).y+.025f;
        int n=cursor++%poolSize;var s=splashes[n];var r=ripples[n];s.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);r.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        strength=Mathf.Clamp(strength,.25f,3);var e=new ParticleSystem.EmitParams{position=p,startSize=.32f*Mathf.Sqrt(strength),startLifetime=1.4f,velocity=Vector3.zero};r.Emit(e,1);
        if(strength>.45f)for(int i=0;i<Mathf.RoundToInt(9*strength);i++){e.position=p+new Vector3(Random.Range(-.13f,.13f),0,Random.Range(-.13f,.13f));e.velocity=new Vector3(Random.Range(-.7f,.7f),Random.Range(.8f,2.2f),Random.Range(-.7f,.7f))*Mathf.Sqrt(strength);e.startSize=Random.Range(.035f,.09f)*Mathf.Sqrt(strength);e.startLifetime=Random.Range(.35f,.7f);s.Emit(e,1);}
        if(splashSound&&strength>.65f&&Time.time>nextSound){audioSource.PlayOneShot(splashSound,Mathf.Min(.55f,strength*.2f));nextSound=Time.time+.22f;}
        ImpactCount++;return true;
    }
    void Update()
    {
        if(Time.time<tick)return;tick=Time.time+contactInterval;
        int count=Physics.OverlapBoxNonAlloc(transform.TransformPoint(new Vector3(0,WaterY,116.2f)),new Vector3(47,1.5f,7.5f),overlaps,transform.rotation,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var c=overlaps[i];if(c is MeshCollider||c.transform.IsChildOf(transform))continue;
            var actor=c.GetComponentInParent<CharacterController>();var rb=c.attachedRigidbody;var animator=c.GetComponentInParent<Animator>();if(!actor&&!rb&&!animator)continue;
            var owner=actor?actor.transform:rb?rb.transform:animator.transform;int id=owner.GetHashCode();var b=actor?actor.bounds:c.bounds;Vector3 p=b.center;p.y=transform.position.y+WaterY;
            if(!Contains(p)||b.min.y>p.y+.08f||b.max.y<p.y-.1f)continue;
            if(!contacts.TryGetValue(id,out var state)){state=new Contact{position=p,last=-10};contacts.Add(id,state);}
            state.seen=Time.time;
            if(Time.time-state.last>.22f&&(state.last<0||Vector3.Distance(state.position,p)>.18f))
            {if(EmitImpact(p,state.last<0?1.3f:.5f))ActorContactCount++;state.position=p;state.last=Time.time;}
        }
        expired.Clear();foreach(var kv in contacts)if(Time.time-kv.Value.seen>.5f)expired.Add(kv.Key);foreach(int id in expired)contacts.Remove(id);
        if(Time.time>scan){sceneParticles=FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);scan=Time.time+.25f;particleCooldown.Clear();}
        if(sceneParticles==null)return;
        foreach(var ps in sceneParticles)
        {
            if(!ps||!ps.isPlaying||ps.particleCount==0||ps.transform.IsChildOf(transform))continue;
            var renderer=ps.GetComponent<Renderer>();if(renderer&&(!Contains(renderer.bounds.center,renderer.bounds.extents.z)||renderer.bounds.min.y>transform.position.y+WaterY+.2f||renderer.bounds.max.y<transform.position.y+WaterY-.2f))continue;
            int id=ps.GetHashCode();if(particleCooldown.TryGetValue(id,out float time)&&Time.time<time)continue;
            int n=ps.GetParticles(particles,particles.Length);var main=ps.main;
            for(int j=0;j<n;j++)
            {
                Vector3 p=particles[j].position,v=particles[j].velocity;
                if(main.simulationSpace!=ParticleSystemSimulationSpace.World){var basis=main.simulationSpace==ParticleSystemSimulationSpace.Custom&&main.customSimulationSpace?main.customSimulationSpace:ps.transform;p=basis.TransformPoint(p);v=basis.TransformVector(v);}
                float water=transform.position.y+WaterY,prior=p.y-v.y*contactInterval;
                if((Mathf.Abs(p.y-water)<.13f||(p.y-water)*(prior-water)<0)&&Contains(p))
                {p.y=water;if(EmitImpact(p,Mathf.Clamp(v.magnitude*.15f,.5f,2.5f)))ParticleContactCount++;particleCooldown[id]=Time.time+.3f;break;}
            }
        }
    }
}
