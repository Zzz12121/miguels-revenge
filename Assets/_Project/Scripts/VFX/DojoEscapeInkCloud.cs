using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Only the four authored ARPG effects: varied, independently orbiting ink filaments.</summary>
public sealed class DojoEscapeInkCloud : MonoBehaviour
{
    public GameObject casting,embers,nova,muzzle;
    public int BlackEffects {get;private set;}
    public int WhiteEffects {get;private set;}
    public const float ParticleLifetime=.5f;
    public int OrbitCount=>orbits.Count;
    public int TailBursts {get;private set;}
    public readonly List<float> NativeScales=new();
    public readonly List<bool> InvertedEffects=new();
    readonly List<Orbit> orbits=new();
    float accentAt;
    float tailAt;Vector3 previousPosition;Transform detachedTail;
    sealed class Orbit
    {
        public Transform root;public Quaternion tilt;public float radius,angle,speed,vertical,phase;
    }
    public void Begin()
    {
        // Balanced pairs keep both pigments visible; each member has a 50% inversion chance.
        for(int pair=0;pair<5;pair++)
        {
            bool inverse=Random.value<.5f;
            AddOrbit(pair<2?casting:embers,inverse,pair<2?Random.Range(.45f,.85f):Random.Range(.9f,1.8f));
            AddOrbit(pair<2?casting:embers,!inverse,pair<2?Random.Range(.3f,.65f):Random.Range(.65f,1.5f));
        }
        accentAt=Time.time+Random.Range(.35f,.7f);
        previousPosition=transform.position;
        detachedTail=new GameObject("Detached original Ying Yang tail").transform;detachedTail.SetParent(transform.parent,true);
    }
    public void RelocateWhileHidden(Vector3 position,Quaternion rotation)
    {
        // Keep the local orbit alive and leave the old detached trail to fade on its own.
        foreach(var ps in GetComponentsInChildren<ParticleSystem>())
            if(ps.main.simulationSpace==ParticleSystemSimulationSpace.World)
                ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        transform.SetPositionAndRotation(position,rotation);
        previousPosition=position;
        tailAt=Time.time+.06f;
    }
    void AddOrbit(GameObject prefab,bool inverse,float scale)
    {
        var effect=Spawn(prefab,inverse,scale,true);if(!effect)return;
        var orbit=new Orbit{root=effect.transform,tilt=Quaternion.Euler(Random.Range(10,160),Random.Range(0,360),Random.Range(-65,65)),radius=Random.Range(.15f,.7f),angle=Random.Range(0,360),speed=Random.Range(48,105)*(Random.value<.5f?-1:1),vertical=Random.Range(.04f,.22f),phase=Random.Range(0,6.28f)};
        orbits.Add(orbit);Place(orbit);
    }
    static Color Invert(Color c)=>new Color(1-Mathf.Clamp01(c.r),1-Mathf.Clamp01(c.g),1-Mathf.Clamp01(c.b),c.a);
    static Gradient Invert(Gradient gradient)
    {
        var result=new Gradient();var colors=gradient.colorKeys;for(int i=0;i<colors.Length;i++)colors[i].color=Invert(colors[i].color);
        result.SetKeys(colors,gradient.alphaKeys);result.mode=gradient.mode;return result;
    }
    static ParticleSystem.MinMaxGradient Invert(ParticleSystem.MinMaxGradient value)
    {
        switch(value.mode)
        {
            case ParticleSystemGradientMode.Color:return new ParticleSystem.MinMaxGradient(Invert(value.color));
            case ParticleSystemGradientMode.TwoColors:return new ParticleSystem.MinMaxGradient(Invert(value.colorMin),Invert(value.colorMax));
            case ParticleSystemGradientMode.TwoGradients:return new ParticleSystem.MinMaxGradient(Invert(value.gradientMin),Invert(value.gradientMax));
            default:var gradient=new ParticleSystem.MinMaxGradient(Invert(value.gradient));gradient.mode=value.mode;return gradient;
        }
    }
    GameObject Spawn(GameObject prefab,bool inverse,float scale,bool orbiting,Transform parent=null)
    {
        if(!prefab)return null;
        var go=Instantiate(prefab,parent?parent:transform,false);go.name=prefab.name+(inverse?" [inverted black]":" [original white]");go.transform.localScale=Vector3.one*scale;
        var owner=go.AddComponent<DojoInkParticleMaterials>();
        foreach(var helper in go.GetComponentsInChildren<PilotoStudio.PilotoCameraOffset>(true)){helper.enabled=false;if(helper.transform!=go.transform)helper.gameObject.SetActive(false);}
        foreach(var helper in go.GetComponentsInChildren<PilotoStudio.ParticleHandler>(true))helper.enabled=false;
        if(inverse)foreach(var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                if(!materials[i])continue;
                // Retain the native shader, texture panning, distortion, mesh and UV animation.
                var material=new Material(materials[i]){name=materials[i].name+" [runtime color inversion]"};
                foreach(string property in new[]{"_WhiteColor","_MidColor","_LastColor","_FresnelColor","_Color","_BaseColor","_EmissionColor"})
                    if(material.HasProperty(property))material.SetColor(property,Invert(material.GetColor(property)));
                // Additive blending cannot show inverted black; native SG exposes alpha blend factors.
                foreach(var property in new[]{"_Blend","_BUILTIN_Blend"})if(material.HasProperty(property))material.SetFloat(property,0);
                foreach(var property in new[]{"_SrcBlend","_BUILTIN_SrcBlend"})if(material.HasProperty(property))material.SetFloat(property,(float)BlendMode.SrcAlpha);
                foreach(var property in new[]{"_DstBlend","_BUILTIN_DstBlend"})if(material.HasProperty(property))material.SetFloat(property,(float)BlendMode.OneMinusSrcAlpha);
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.DisableKeyword("_ALPHAMODULATE_ON");
                if(material.HasProperty("_MainAlphaChannel"))
                {
                    // Read the original flowing texture's red channel as opacity, using its
                    // existing distorted UVs. Additive textures often have solid alpha.
                    material.SetVector("_MainAlphaChannel",new Vector4(1,0,0,0));
                    if(material.HasProperty("_USEALPHAOVERRIDE"))material.SetFloat("_USEALPHAOVERRIDE",0);
                    material.DisableKeyword("_USEALPHAOVERRIDE");
                }
                materials[i]=material;owner.materials.Add(material);
            }
            renderer.sharedMaterials=materials;
        }
        foreach(var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;
            main.loop=orbiting;main.duration=orbiting?.45f:.1f;main.startDelay=0;main.startLifetime=ParticleLifetime;main.stopAction=ParticleSystemStopAction.None;
            main.maxParticles=orbiting?28:16;main.simulationSpace=orbiting?ParticleSystemSimulationSpace.Local:ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            if(inverse)main.startColor=Invert(main.startColor);
            // Suppress the stock flat light flare; keep the authored flowing ribbons and embers.
            if(ps.name.Contains("Glow")){var emission=ps.emission;emission.enabled=false;}
            if(ps.gameObject.activeInHierarchy){if(orbiting)ps.Simulate(.2f,false,true,true);ps.Play(false);}
        }
        NativeScales.Add(scale);InvertedEffects.Add(inverse);if(inverse)BlackEffects++;else WhiteEffects++;
        if(!orbiting)Destroy(go,ParticleLifetime);
        return go;
    }
    void Place(Orbit orbit)
    {
        float a=orbit.angle*Mathf.Deg2Rad;
        orbit.root.localPosition=orbit.tilt*new Vector3(Mathf.Cos(a)*orbit.radius,Mathf.Sin(Time.time*2+orbit.phase)*orbit.vertical,Mathf.Sin(a)*orbit.radius);
        orbit.root.localRotation=orbit.tilt*Quaternion.Euler(0,orbit.angle,Mathf.Sin(a)*25);
    }
    void Update()
    {
        foreach(var orbit in orbits)if(orbit.root){orbit.angle+=orbit.speed*Time.deltaTime;Place(orbit);}
        if(Time.time>=tailAt&&Vector3.Distance(previousPosition,transform.position)>.025f)
        {
            tailAt=Time.time+.08f;TailBursts++;
            var trail=Spawn(TailBursts%3==0?casting:embers,Random.value<.5f,Random.Range(.13f,.28f),false,detachedTail);
            if(trail){trail.transform.position=Vector3.Lerp(previousPosition,transform.position,.3f)+Random.insideUnitSphere*.13f;trail.transform.rotation=Random.rotation;}
            previousPosition=transform.position;
        }
        if(Time.time<accentAt)return;accentAt=Time.time+Random.Range(.45f,.85f);
        var effect=Spawn(Random.value<.5f?nova:muzzle,Random.value<.5f,Random.Range(.18f,.36f),false);
        if(effect){effect.transform.localPosition=Random.onUnitSphere*.45f;effect.transform.localRotation=Random.rotation;}
    }
    void OnDestroy(){if(detachedTail)Destroy(detachedTail.gameObject,ParticleLifetime);}
}
