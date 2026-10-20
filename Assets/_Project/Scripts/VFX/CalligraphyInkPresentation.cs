using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bounded pools of pigment blooms and ink silhouettes, entirely visual.</summary>
[DefaultExecutionOrder(210)]
public sealed class CalligraphyInkPresentation : MonoBehaviour
{
    public CalligraphyBrushPresentation brush;
    public Material inkMaterial, echoMaterial;
    public SkinnedMeshRenderer[] bodyMeshes;
    public MeshFilter[] accessoryMeshes;
    [Range(0,40)] public float ambientRate=5;
    [Range(0,80)] public float strokeRate=28;
    [Range(0,5)] public float echoInterval=3.2f;
    public int maxBlooms=80, maxEchoes=3;
    public float inkSize=0.46f, inkLifetime=2.8f;
    struct Bloom { public bool active; public Vector3 p,v; public float age,life,size,seed; public Transform t; public MeshRenderer r; }
    sealed class Echo { public GameObject root; public Mesh[] meshes; public MeshRenderer[] renderers; public float age; public bool active; }
    Bloom[] blooms;
    Echo[] echoes;
    Mesh quad;
    GameObject pool;
    MaterialPropertyBlock block;
    float emission,echoClock;
    Vector3 lastTip;
    int cursor,echoCursor;
    TrailRenderer stroke;
    static readonly int Fade=Shader.PropertyToID("_Fade");
    void OnEnable()
    {
        if(!Application.isPlaying || !inkMaterial || !echoMaterial || !brush) return;
        block=new MaterialPropertyBlock();
        pool=new GameObject(name+"_InkPool"); pool.transform.SetParent(transform,false);
        quad=new Mesh {name="Calligraphy ink billboard"};
        quad.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(.5f,.5f),new Vector3(-.5f,.5f)};
        quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
        quad.colors=new[]{Color.white,Color.white,Color.white,Color.white}; quad.triangles=new[]{0,2,1,0,3,2}; quad.RecalculateBounds();
        blooms=new Bloom[Mathf.Clamp(maxBlooms,8,160)];
        for(int i=0;i<blooms.Length;i++)
        {
            var go=new GameObject("InkBloom"); go.transform.SetParent(pool.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=quad;
            var r=go.AddComponent<MeshRenderer>(); r.sharedMaterial=inkMaterial; r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false; r.enabled=false;
            blooms[i]=new Bloom {t=go.transform,r=r};
        }
        echoes=new Echo[Mathf.Clamp(maxEchoes,1,4)];
        for(int i=0;i<echoes.Length;i++)
        {
            int meshCount=bodyMeshes.Length+(accessoryMeshes==null?0:accessoryMeshes.Length);
            var e=new Echo {root=new GameObject("InkDouble"),meshes=new Mesh[meshCount],renderers=new MeshRenderer[meshCount]};
            e.root.transform.SetParent(pool.transform,false);
            for(int j=0;j<meshCount;j++)
            {
                var go=new GameObject("InkSilhouette"); go.transform.SetParent(e.root.transform,false);
                e.meshes[j]=j<bodyMeshes.Length?new Mesh {name="Pooled ink snapshot"}:Instantiate(accessoryMeshes[j-bodyMeshes.Length].sharedMesh);
                go.AddComponent<MeshFilter>().sharedMesh=e.meshes[j];
                var r=go.AddComponent<MeshRenderer>(); r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
                e.renderers[j]=r;
            }
            e.root.SetActive(false); echoes[i]=e;
        }
        lastTip=brush.brushTip?brush.brushTip.position:transform.position;
        var strokeObject=new GameObject("BrushInkStroke");strokeObject.transform.SetParent(pool.transform,false);
        stroke=strokeObject.AddComponent<TrailRenderer>();stroke.sharedMaterial=inkMaterial;
        stroke.time=.85f;stroke.minVertexDistance=.035f;stroke.widthMultiplier=.18f;
        stroke.widthCurve=AnimationCurve.EaseInOut(0,1,1,0);
        stroke.shadowCastingMode=ShadowCastingMode.Off;stroke.receiveShadows=false;
        stroke.startColor=new Color(1,1,1,.9f);stroke.endColor=new Color(1,1,1,0);stroke.emitting=false;
    }
    [ContextMenu("Preview/Emit ink double")]
    public void EmitDouble()
    {
        if(echoes==null) return;
        var e=echoes[echoCursor++%echoes.Length]; e.root.SetActive(true); e.age=0; e.active=true;
        Vector3 offset=transform.right*(echoCursor%2==0?-1:1)*1.2f-transform.forward*.35f;
        for(int j=0;j<e.meshes.Length;j++)
        {
            Transform source;
            // In this imported rig, true produces renderer-local vertices; the renderer's
            // world scale is then applied once below (verified against the source bounds).
            if(j<bodyMeshes.Length) {if(!bodyMeshes[j]) continue;bodyMeshes[j].BakeMesh(e.meshes[j],true);source=bodyMeshes[j].transform;}
            else source=accessoryMeshes[j-bodyMeshes.Length].transform;
            var t=e.renderers[j].transform;
            t.SetPositionAndRotation(source.position+offset,source.rotation);
            t.localScale=source.lossyScale; // Pool is intentionally unit-scale in the prefab.
            var mats=new Material[e.meshes[j].subMeshCount]; for(int k=0;k<mats.Length;k++) mats[k]=echoMaterial;
            e.renderers[j].sharedMaterials=mats;
        }
    }
    void LateUpdate()
    {
        if(blooms==null) return;
        float dt=Time.deltaTime; if(dt<=0) return;
        Camera camera=Camera.main;
        Vector3 tip=brush.brushTip?brush.brushTip.position:transform.position+Vector3.up;
        float speed=Vector3.Distance(tip,lastTip)/Mathf.Max(dt,.001f);
        stroke.transform.position=tip; stroke.emitting=speed>.6f;
        emission+=dt*(ambientRate+Mathf.Clamp01(speed/2)*strokeRate);
        int count=Mathf.Min((int)emission,12); emission-=count;
        for(int n=0;n<count;n++)
        {
            int index=cursor++%blooms.Length; var b=blooms[index];
            bool stroke=n%3!=0;
            b.active=true; b.age=0; b.life=inkLifetime*Random.Range(.7f,1.3f); b.seed=Random.value*100;
            b.size=inkSize*Random.Range(.55f,1.3f)*(stroke?.7f:1);
            b.p=stroke?Vector3.Lerp(lastTip,tip,Random.value):transform.position+Vector3.up*Random.Range(.35f,1.8f)+Random.insideUnitSphere*.65f;
            b.v=Random.insideUnitSphere*.08f+Vector3.up*.07f; b.r.enabled=true; blooms[index]=b;
        }
        lastTip=tip;
        for(int i=0;i<blooms.Length;i++)
        {
            var b=blooms[i]; if(!b.active) continue; b.age+=dt; float u=b.age/b.life;
            if(u>=1) {b.active=false;b.r.enabled=false;blooms[i]=b;continue;}
            b.p+=dt*(b.v+new Vector3(Mathf.Sin(b.seed+b.age*1.1f),Mathf.Cos(b.seed*.7f+b.age),Mathf.Sin(b.age*.8f+b.seed))*0.07f);
            b.t.position=b.p; if(camera) b.t.rotation=camera.transform.rotation*Quaternion.Euler(0,0,b.seed*32+b.age*8);
            float growth=Mathf.Lerp(.3f,1.8f,Mathf.Sqrt(u)); b.t.localScale=new Vector3(1,1.25f,1)*b.size*growth;
            float alpha=Mathf.SmoothStep(0,1,u/.13f)*Mathf.Pow(1-u,1.25f);
            block.Clear(); block.SetColor("_BaseColor",new Color(.006f,.012f,.022f,alpha*.85f)); b.r.SetPropertyBlock(block); blooms[i]=b;
        }
        if(echoInterval>0) {echoClock+=dt;if(echoClock>=echoInterval) {echoClock=0;EmitDouble();}}
        foreach(var e in echoes)
        {
            if(!e.active) continue; e.age+=dt;
            if(e.age>=2.6f) {e.active=false;e.root.SetActive(false);continue;}
            block.Clear(); block.SetFloat(Fade,Mathf.SmoothStep(0,1,e.age/2.6f));
            foreach(var r in e.renderers) {r.SetPropertyBlock(block); r.transform.position+=Vector3.up*dt*.035f;}
        }
    }
    void OnDisable()
    {
        if(echoes!=null) foreach(var e in echoes) foreach(var m in e.meshes) Destroy(m);
        if(quad) Destroy(quad); if(pool) Destroy(pool); blooms=null; echoes=null; emission=echoClock=0;
    }
}
