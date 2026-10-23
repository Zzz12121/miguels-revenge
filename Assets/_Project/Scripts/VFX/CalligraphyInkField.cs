using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bounded reusable ribbons: viscous heads, delayed curls and split fine filaments.</summary>
public sealed class CalligraphyInkField : MonoBehaviour
{
    public Material ribbonMaterial, sculptureMaterial, paperMaterial, yinYangMaterial, landscapeMaterial;
    public Material originalInkMaterial,originalEchoMaterial;
    public Material suspendedWashMaterial,skyVisageMaterial;
    CalligraphyInkMist mist;
    public void Mist(Vector3 at,Vector3 velocity,float size,float life=3)
    {
        if(!mist){mist=gameObject.AddComponent<CalligraphyInkMist>();mist.Initialize(originalInkMaterial);}
        mist.Emit(at,velocity,size,life);
    }
    public Shader environmentShader;
    [Range(32,160)] public int capacity=96;
    [Range(.1f,2)] public float curlStrength=.65f;
    [Range(.1f,2)] public float diffusion=.38f;
    public float filamentLifetime=2.4f;
    [Range(0,1)] public float whiteRatio=.12f;
    sealed class Strand
    {
        public LineRenderer line;
        public readonly Vector3[] points=new Vector3[40];
        public Vector3 origin,velocity,side,up;
        public float born,life,size,seed;
        public bool active;
    }
    Strand[] strands;
    int cursor;
    MaterialPropertyBlock block;
    public int ActiveCount { get; private set; }
    Vector3 disturbance, disturbanceVelocity;
    float disturbanceRadius, disturbanceUntil;
    void Awake()
    {
        block=new MaterialPropertyBlock();
        strands=new Strand[Mathf.Clamp(capacity,32,160)];
        for(int i=0;i<strands.Length;i++)
        {
            var go=new GameObject("Pooled ink filament"); go.transform.SetParent(transform,false);
            var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=ribbonMaterial;
            line.positionCount=40; line.useWorldSpace=true; line.textureMode=LineTextureMode.Stretch;
            line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
            line.numCornerVertices=3; line.numCapVertices=3; line.enabled=false;
            line.widthCurve=new AnimationCurve(new Keyframe(0,.05f),new Keyframe(.12f,1),new Keyframe(.4f,.7f),new Keyframe(1,0));
            strands[i]=new Strand{line=line};
        }
    }
    public void Emit(Vector3 origin,Vector3 velocity,float size=1,float life=0,bool white=false)
    {
        if(strands==null) return;
        var s=strands[cursor];cursor=(cursor+1)%strands.Length;
        if(velocity.sqrMagnitude<.8f)velocity=(velocity+Random.onUnitSphere*.5f).normalized*1.1f;
        s.origin=origin; s.velocity=velocity; s.size=size; s.life=life>0?life:filamentLifetime;
        s.seed=Random.value*40; s.born=Time.time; s.active=true;
        Vector3 axis=velocity.sqrMagnitude>.001f?velocity.normalized:Vector3.up;
        s.side=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.forward).normalized;
        s.up=Vector3.Cross(axis,s.side);
        s.line.widthMultiplier=size*.36f; s.line.enabled=true;
        block.Clear(); block.SetColor("_BaseColor",white?new Color(.88f,.88f,.81f,.8f):new Color(.008f,.015f,.018f,.94f));
        block.SetFloat("_Seed",s.seed); s.line.SetPropertyBlock(block);
        UpdateStrand(s,0);
    }
    public void Burst(Vector3 at,float strength=1,int count=14)
    {
        for(int i=0;i<5;i++)Mist(at+Random.insideUnitSphere*strength*.3f,Random.insideUnitSphere*.3f,strength*.9f,3);
        for(int i=0;i<Mathf.Min(count,28);i++)
            Emit(at,Random.onUnitSphere*Random.Range(.6f,2.8f)*strength,Random.Range(.5f,1.3f)*strength,Random.Range(1.4f,3.2f),Random.value<whiteRatio);
    }
    public void Disturb(Vector3 at,Vector3 velocity,float radius,float duration=.25f)
    { disturbance=at; disturbanceVelocity=Vector3.ClampMagnitude(velocity,12); disturbanceRadius=radius; disturbanceUntil=Time.time+duration; }
    public void Clear()
    {
        if(mist)mist.Clear();
        if(strands==null) return;
        foreach(var s in strands) {s.active=false;s.line.enabled=false;}
        ActiveCount=0;
    }
    void Update()
    {
        ActiveCount=0;
        foreach(var s in strands)
        {
            if(!s.active) continue;
            float age=Time.time-s.born;
            if(age>=s.life) {s.active=false;s.line.enabled=false;continue;}
            ActiveCount++; UpdateStrand(s,age);
        }
    }
    void UpdateStrand(Strand s,float age)
    {
        float life=age/s.life;
        if(Time.time<disturbanceUntil)
        {
            float influence=1-Mathf.Clamp01(Vector3.Distance(s.origin,disturbance)/Mathf.Max(.1f,disturbanceRadius));
            s.origin+=disturbanceVelocity*(influence*Time.deltaTime*.25f);
        }
        Vector3 head=s.origin+s.velocity*(1-Mathf.Exp(-age*1.7f))*.58f;
        for(int j=0;j<s.points.Length;j++)
        {
            float u=j/(float)(s.points.Length-1),lag=u*Mathf.Min(age+.08f,1.3f);
            float radius=u*u*s.size*curlStrength*(.12f+age*diffusion);
            float phase=u*9+age*(1.4f+u)+s.seed;
            // The tail coils back into itself; independent scales avoid a uniform sine-wave trail.
            s.points[j]=head-s.velocity*lag*.55f+s.side*(Mathf.Sin(phase)*radius)+
                s.up*((1-Mathf.Cos(phase))*radius+Mathf.Sin(phase*1.7f)*radius*.2f)+Vector3.down*(age*u*.06f);
        }
        s.line.SetPositions(s.points); s.line.widthMultiplier=s.size*.36f*(1+age*diffusion);
        s.line.GetPropertyBlock(block); block.SetFloat("_Fade",Mathf.SmoothStep(0,1,life));
        block.SetFloat("_Opacity",Mathf.Min(1,age*18+.2f)); s.line.SetPropertyBlock(block);
    }
}
