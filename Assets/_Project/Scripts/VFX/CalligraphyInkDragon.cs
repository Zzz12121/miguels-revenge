using UnityEngine;

/// <summary>Articulated serpentine dragon. Every ring follows an earlier point on the head's path.</summary>
public sealed class CalligraphyInkDragon : MonoBehaviour
{
    public bool Impacted {get;private set;}
    public float speed=22,bodyWave=.65f,bodyDelay=.028f;
    const int Rings=42,Sides=9;
    CalligraphyInkField ink;
    PlayerHealth player;
    Mesh mesh;
    MeshRenderer body,telegraph;
    Transform head,jaw;
    readonly Transform[] legs=new Transform[4];
    readonly LineRenderer[] feet=new LineRenderer[4];
    readonly Renderer[][] limbRenderers=new Renderer[4][];
    readonly LineRenderer[] whiskers=new LineRenderer[2];
    readonly LineRenderer[] mane=new LineRenderer[12];
    readonly Vector3[] whiskerPoints=new Vector3[12];
    readonly Vector3[] centers=new Vector3[Rings];
    readonly Vector3[] verts=new Vector3[Rings*Sides];
    readonly Color[] colors=new Color[Rings*Sides];
    Renderer[] details;
    Vector3 start,target,axis,side,previous;
    float scale,age,delay,travel,flightDuration,impactAge,emitClock;
    bool giant,diving,damaged;
    float damage;
    Vector3 pursuitPosition,pursuitDirection;
    float nextContact,passUntil;
    public bool IsHoming=>!giant;
    public Vector3 TrackedPosition=>target;
    public float BodyScale=>scale;
    public float ThicknessMultiplier=>giant?2:1;
    public bool TelegraphVisible=>telegraph && telegraph.enabled;
    public bool IsDiving=>diving;
    public void Initialize(CalligraphyInkField field,PlayerHealth victim,Vector3 from,Vector3 to,float size,float wait,bool large,float damageAmount=0)
    {
        ink=field;player=victim;start=from;target=to;scale=size;delay=wait;giant=large;
        damage=damageAmount>0?damageAmount:large?35:15;speed=large?26:18;
        axis=(target-start).normalized;side=Vector3.Cross(axis,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=Vector3.right;
        flightDuration=Vector3.Distance(start,target)/speed;previous=from;
        pursuitPosition=from;pursuitDirection=axis;
        for(int i=0;i<Rings;i++)centers[i]=Path(-i*.045f);
        mesh=CalligraphyGeometry.Loft(new Vector3[Rings],MakeRadii(),Sides);mesh.MarkDynamic();
        body=CalligraphyGeometry.MeshObject("Continuous ink spine",transform,mesh,ink.sculptureMaterial);
        var h=new GameObject("Horned dragon head");h.transform.SetParent(transform,false);head=h.transform;
        HeadShape();
        for(int i=0;i<mane.Length;i++)
            mane[i]=CalligraphyGeometry.Line("Dorsal ink mane",transform,ink.ribbonMaterial,new Vector3[4],.28f*scale,true);
        for(int i=0;i<4;i++)
        {
            var limb=new GameObject("Articulated claw "+i);limb.transform.SetParent(transform,false);legs[i]=limb.transform;
            feet[i]=CalligraphyGeometry.Line("Forearm",limb.transform,ink.sculptureMaterial,new Vector3[4],.19f*scale);
            for(int k=0;k<3;k++)
            {
                float f=k-1;
                CalligraphyGeometry.Line("Curved talon",limb.transform,ink.sculptureMaterial,new[]{new Vector3(.7f,-.55f,.2f),new Vector3(1.0f,-.65f,.2f+f*.16f),new Vector3(1.15f,-.72f,.3f+f*.22f)},.075f);
            }
            limbRenderers[i]=limb.GetComponentsInChildren<Renderer>();
        }
        telegraph=CalligraphyGeometry.MeshObject("Ink landing telegraph",transform,CalligraphyGeometry.Quad(),ink.yinYangMaterial);
        telegraph.transform.SetPositionAndRotation(target+Vector3.up*.06f,Quaternion.Euler(90,0,0));telegraph.transform.localScale=Vector3.one*(large?12:3.5f);
        telegraph.enabled=false;
        details=head.GetComponentsInChildren<Renderer>();
    }
    Vector2[] MakeRadii(){var a=new Vector2[Rings];for(int i=0;i<Rings;i++)a[i]=Vector2.one*.1f;return a;}
    void HeadShape()
    {
        var skull=CalligraphyGeometry.Loft(new[]{new Vector3(0,0,-.35f),new Vector3(0,.1f,0),new Vector3(0,.13f,.42f),new Vector3(0,.02f,.85f),new Vector3(0,.09f,1.05f),new Vector3(0,.09f,1.08f)},
            new[]{new Vector2(.2f,.22f),new Vector2(.5f,.42f),new Vector2(.44f,.3f),new Vector2(.26f,.14f),new Vector2(.3f,.2f),new Vector2(.01f,.01f)},14);
        CalligraphyGeometry.MeshObject("Long dragon skull and muzzle",head,skull,ink.sculptureMaterial);
        var j=new GameObject("Hinged lower jaw");j.transform.SetParent(head,false);j.transform.localPosition=new Vector3(0,-.15f,.25f);jaw=j.transform;
        CalligraphyGeometry.MeshObject("Lower jaw",jaw,CalligraphyGeometry.Loft(new[]{Vector3.zero,new Vector3(0,0,.65f),new Vector3(0,.04f,.9f)},new[]{new Vector2(.3f,.09f),new Vector2(.23f,.06f),Vector2.zero}),ink.sculptureMaterial);
        for(int sign=-1;sign<=1;sign+=2)
        {
            Vector3 s=new Vector3(sign,1,1);
            Vector3 V(float x,float y,float z)=>Vector3.Scale(new Vector3(x,y,z),s);
            CalligraphyGeometry.Line("Swept antler",head,ink.sculptureMaterial,new[]{V(.32f,.25f,-.15f),V(.5f,.65f,-.3f),V(.64f,.95f,-.65f),V(.8f,1.15f,-.9f)},.19f*scale);
            CalligraphyGeometry.Line("Antler branch",head,ink.sculptureMaterial,new[]{V(.5f,.65f,-.3f),V(.75f,.82f,-.15f),V(.95f,1.05f,-.22f)},.13f*scale);
            var eye=CalligraphyGeometry.MeshObject("Pearl eye",head,CalligraphyGeometry.Quad(),ink.paperMaterial);
            eye.transform.localPosition=V(.425f,.23f,.38f);eye.transform.localRotation=Quaternion.Euler(0,sign*65,0);eye.transform.localScale=new Vector3(.22f,.12f,.1f);
            var brow=CalligraphyGeometry.Line("Heavy brow",head,ink.sculptureMaterial,new[]{V(.49f,.29f,.2f),V(.46f,.36f,.48f),V(.28f,.3f,.65f)},.16f*scale);
            for(int tuft=0;tuft<4;tuft++)
                CalligraphyGeometry.Line("Dragon cheek mane",head,ink.ribbonMaterial,new[]{V(.4f,.1f-tuft*.07f,-.05f),V(.8f,.3f-tuft*.14f,-.4f),V(.95f,.5f-tuft*.22f,-1.0f)},.3f*scale);
            whiskers[sign==1?1:0]=CalligraphyGeometry.Line("Flowing whisker",head,ink.sculptureMaterial,new Vector3[12],.055f);
            for(int tooth=0;tooth<3;tooth++)
                CalligraphyGeometry.Line("Fang",head,ink.paperMaterial,new[]{V(.22f,-.08f,.55f+tooth*.16f),V(.2f,-.23f,.6f+tooth*.16f)},.06f);
        }
        CalligraphyGeometry.Line("Chin beard",jaw,ink.ribbonMaterial,new[]{new Vector3(0,-.05f,.6f),new Vector3(0,-.45f,.4f),new Vector3(0,-.8f,-.3f)},.25f);
        head.localScale=new Vector3(ThicknessMultiplier,ThicknessMultiplier,1)*scale;
    }
    public void Dive(){if(diving)return;diving=true;travel=0;}
    Vector3 Path(float t)
    {
        if(t<0)
        {
            float d=-t*10;
            return start-axis*d+side*Mathf.Sin(d*.38f)*1.2f*scale+Vector3.Cross(axis,side)*(Mathf.Cos(d*.3f)-1)*.7f*scale;
        }
        float u=Mathf.Clamp01(t/flightDuration);
        float acceleration=giant?u*u:u;
        Vector3 p=Vector3.Lerp(start,target,acceleration);
        p+=side*Mathf.Sin(u*Mathf.PI*2)*Mathf.Sin(u*Mathf.PI)*(giant?1.1f:2.5f);
        return p;
    }
    void Update()
    {
        if(!ink)return;
        age+=Time.deltaTime;
        if(!diving && age>=delay)Dive();
        if(diving)travel+=Time.deltaTime;
        float phase=diving?travel:-.2f;
        if(!giant && player)
        {
            target=player.transform.position+Vector3.up;
            if(diving)
            {
                Vector3 desired=(target-pursuitPosition).normalized;
                if(Time.time>=passUntil)pursuitDirection=Vector3.RotateTowards(pursuitDirection,desired,2.3f*Time.deltaTime,0).normalized;
                pursuitPosition+=pursuitDirection*speed*Time.deltaTime;
            }
            centers[0]=pursuitPosition;
            for(int i=1;i<Rings;i++)
            {
                Vector3 delta=centers[i]-centers[i-1];
                if(delta.sqrMagnitude<.0001f)delta=-pursuitDirection;
                // Thickness is independent of spine length, so a wider dragon does not become a 40m pipe.
                centers[i]=centers[i-1]+delta.normalized*.44f;
            }
            axis=pursuitDirection;side=Vector3.Cross(axis,Vector3.up).normalized;
            if(side.sqrMagnitude<.01f)side=Vector3.right;
            telegraph.enabled=false;
            if(Time.time>=nextContact)damaged=false;
        }
        else for(int i=0;i<Rings;i++)
        {
            float lag=i*bodyDelay*scale;
            centers[i]=Path(phase-lag);
            centers[i]+=side*(Mathf.Sin(age*4-i*.38f)*bodyWave*scale*(i/(float)Rings));
            centers[i]+=Vector3.Cross(axis,side)*(Mathf.Sin(age*3-i*.3f)*.16f*scale*i/Rings);
        }
        head.position=centers[0];head.rotation=Quaternion.LookRotation((centers[0]-centers[1]).normalized,Mathf.Abs(axis.y)>.9f?Vector3.forward:Vector3.up);
        jaw.localRotation=Quaternion.Euler(15+Mathf.Sin(age*5)*14+(diving?16:0),0,0);
        for(int i=0;i<Rings;i++)
        {
            Vector3 tangent=(i==0?centers[0]-centers[1]:centers[i-1]-centers[i]).normalized;
            Vector3 right=Vector3.Cross(tangent,Mathf.Abs(tangent.y)>.9f?Vector3.forward:Vector3.up).normalized;
            Vector3 up=Vector3.Cross(right,tangent);
            float radius=Mathf.Lerp(.32f,.015f,Mathf.Pow(i/(float)(Rings-1),1.4f))*scale*ThicknessMultiplier;
            float dissolve=Impacted?Mathf.Clamp01((travel-flightDuration-i*bodyDelay*scale)/.35f):0;
            float appear=Mathf.Clamp01(age*1.4f-i*.016f);
            for(int k=0;k<Sides;k++)
            {
                int id=i*Sides+k;float a=k*2*Mathf.PI/Sides;
                verts[id]=transform.InverseTransformPoint(centers[i]+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*radius);
                colors[id]=new Color(1,1,1,appear*(1-dissolve));
            }
        }
        mesh.vertices=verts;mesh.colors=colors;mesh.RecalculateNormals();mesh.RecalculateBounds();
        float headFade=Impacted?Mathf.Clamp01((travel-flightDuration)/.4f):0;
        foreach(var r in details)CalligraphyGeometry.Tint(r,r.name=="Pearl eye" || r.name=="Fang"?new Color(.94f,.94f,.84f):new Color(.015f,.023f,.025f),Mathf.Clamp01(age),headFade);
        for(int i=0;i<mane.Length;i++)
        {
            int ring=3+i*3;Vector3 tangent=(centers[ring-1]-centers[ring]).normalized;
            Vector3 dorsal=Vector3.Cross(tangent,side).normalized;
            Vector3 p=centers[ring]+dorsal*.28f*scale;
            mane[i].SetPositions(new[]{p,p+dorsal*.6f*scale-side*.15f*scale,p+dorsal*.8f*scale-tangent*.55f*scale,p-tangent*scale});
            float fade=Impacted?Mathf.Clamp01((travel-flightDuration-ring*bodyDelay*scale)/.4f):0;
            CalligraphyGeometry.Tint(mane[i],new Color(.025f,.03f,.03f),Mathf.Clamp01(age),fade);
        }
        for(int i=0;i<4;i++)
        {
            int ring=i<2?8:23;float sign=i%2==0?-1:1;
            legs[i].position=centers[ring];legs[i].rotation=head.rotation;legs[i].localScale=new Vector3(sign*ThicknessMultiplier,ThicknessMultiplier,1)*scale;
            float swing=Mathf.Sin(age*4+i*1.5f)*.22f;
            feet[i].SetPositions(new[]{Vector3.zero,new Vector3(.4f,-.15f,swing),new Vector3(.55f,-.5f,.25f+swing),new Vector3(.8f,-.55f,.2f)});
            float fade=Impacted?Mathf.Clamp01((travel-flightDuration-ring*bodyDelay*scale)/.4f):0;
            foreach(var r in limbRenderers[i])CalligraphyGeometry.Tint(r,new Color(.01f,.018f,.02f),1,fade);
        }
        for(int i=0;i<2;i++)
        {
            float sign=i==0?-1:1;
            for(int k=0;k<12;k++){float u=k/11f;whiskerPoints[k]=new Vector3(sign*(.24f+u*1.5f),-.02f+Mathf.Sin(u*6+age*3)*u*.16f,.95f-u*u*1.9f);}
            whiskers[i].SetPositions(whiskerPoints);
        }
        // Final brush stroke ends at 4s. Descent remains at 3.25s; only the seal is delayed.
        if(telegraph){telegraph.enabled=giant && age>=4;CalligraphyGeometry.Tint(telegraph,Color.white,Mathf.Clamp01((age-4)*8)*(Impacted?1-headFade:.65f));}
        emitClock-=Time.deltaTime;
        if(emitClock<=0)
        {
            emitClock=.10f;ink.Emit(centers[18],-axis*2,1.1f*scale,2.2f);
            for(int i=0;i<3;i++)ink.Mist(centers[3+i*13],-axis*.45f,scale*.8f,2.7f);
        }
        if(diving && !damaged && player)
        {
            Vector3 p=player.transform.position+Vector3.up;
            Vector3 segment=centers[0]-previous;
            float t=segment.sqrMagnitude>.0001f?Mathf.Clamp01(Vector3.Dot(p-previous,segment)/segment.sqrMagnitude):0;
            if(Vector3.Distance(p,previous+segment*t)<(giant?1.8f:.6f)*scale)
            {damaged=true;nextContact=Time.time+2;passUntil=Time.time+.7f;CalligraphyPlayerHitGate.Hit(player,damage,axis*2,"被水墨龍擊倒");ink.Burst(p,1.2f);}
        }
        previous=centers[0];
        if(giant && diving && travel>=flightDuration && !Impacted)
        {
            Impacted=true;impactAge=age;ink.Burst(target,giant?2.8f:1.1f,26);
            if(!damaged && player && Vector3.Distance(player.transform.position,target)<(giant?6:2))
            {damaged=true;CalligraphyPlayerHitGate.Hit(player,damage,(player.transform.position-target).normalized*2,"被墨龍撞擊波擊倒");}
        }
        if(Impacted && age-impactAge>Rings*bodyDelay*scale+.8f)Destroy(gameObject);
    }
}
