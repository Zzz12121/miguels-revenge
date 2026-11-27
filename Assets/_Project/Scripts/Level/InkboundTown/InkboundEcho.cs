using System.Collections.Generic;
using UnityEngine;

/// <summary>Local pigment displacement with stable animated-mesh targets. No collider or damage receiver.</summary>
[DefaultExecutionOrder(12000)]
public sealed class InkboundEcho : MonoBehaviour
{
    [Header("局部散墨與回流")]
    [Range(.2f,2f)] public float touchRadius = .88f;
    [Tooltip("原速下的回流時間參數；數值越大，回復越慢。另乘場景播放速度。")]
    [Range(.3f,8f)] public float returnSeconds = 1.3f;
    [Tooltip("個別物件的速度倍率，再乘場景 Ink Playback Speed。1 = 沿用場景設定。")]
    [Range(.05f,2f)] public float playbackSpeed=1;
    [Range(32,320)] public int sampleCount = 176;
    [Range(.1f,3f)] public float flowStrength = .9f;
    [Range(.3f,3f)] public float maximumDisplacement = 1.35f;
    public Material plumeMaterial;
    public float Disturbance { get; private set; }
    public int Contacts { get; private set; }
    public float MaximumOffset { get; private set; }
    static readonly List<InkboundEcho> active = new();
    public static int HighQualityBudget=5,LightweightSamples=32;
    public bool HighQuality=>detailed&&!lightweight;
    Transform visitor;
    PlayerHealth visitorHealth;
    Renderer[] surfaces;
    MaterialPropertyBlock block;
    MaterialPropertyBlock plumeBlock;
    InkboundTownInkSettings settings;
    SkinnedMeshRenderer skin;
    MeshFilter staticSurface;
    Mesh baked, plume;
    Transform meshSpace;
    readonly List<Vector3> source = new();
    readonly List<int> nearbySamples=new();
    int[] samples;
    Vector3[] positions, velocities, targets, vertices;
    Vector2[] uvs, brushData;
    const int QuadsPerSample=12, VerticesPerSample=QuadsPerSample*4;
    readonly Vector4[] regionFrom=new Vector4[4],regionTo=new Vector4[4];
    readonly Vector3[] regionFromLocal=new Vector3[4],regionToLocal=new Vector3[4],regionVelocity=new Vector3[4];
    readonly float[] regionStrength=new float[4],regionRadius=new float[4];
    readonly InkboundDisturbanceSource[] regionSources=new InkboundDisturbanceSource[4];
    Color[] colors;
    float[] weights;
    int[] triangles;
    float nextBake,nextStaticProbe,inkClock;
    bool wasTouching, detailed,lightweight;
    MeshRenderer plumeRenderer;
    static readonly int EchoStrength = Shader.PropertyToID("_EchoStrength");

    void Start()
    {
        // Editor domain reload can restore a null private array as an empty array.
        // Runtime sampling is created only after an actual contact.
        positions=null;targets=null;velocities=null;samples=null;source.Clear();
        visitorHealth = PlayerHealth.Find();
        visitor = visitorHealth ? visitorHealth.transform : null;
        surfaces = GetComponentsInChildren<Renderer>(true);
        skin = GetComponentInChildren<SkinnedMeshRenderer>();
        if (!skin) staticSurface = GetComponentInChildren<MeshFilter>();
        block = new MaterialPropertyBlock();
        plumeBlock=new MaterialPropertyBlock();settings=GetComponentInParent<InkboundTownInkSettings>();
    }

    void ReadStaticSamples()
    {
        meshSpace=staticSurface.transform;var mesh=staticSurface.sharedMesh;mesh.GetVertices(source);
        // A table top or cloth panel can have vertices only at its edges. Include interior
        // samples on broad triangles, so walking through the middle still releases pigment.
        var triangle=mesh.triangles;
        for(int i=0;i+2<triangle.Length;i+=3)
        {
            Vector3 a=source[triangle[i]],b=source[triangle[i+1]],c=source[triangle[i+2]];
            float edge=Mathf.Max((a-b).magnitude,Mathf.Max((b-c).magnitude,(c-a).magnitude));
            if(edge<.4f)continue;
            int divisions=Mathf.Clamp(Mathf.CeilToInt(edge/.35f),2,8);
            for(int u=1;u<divisions;u++)for(int v=1;v<divisions-u;v++)
                source.Add(a+(b-a)*(u/(float)divisions)+(c-a)*(v/(float)divisions));
            source.Add((a+b+c)/3);source.Add((a+b)*.5f);source.Add((a+c)*.5f);source.Add((b+c)*.5f);
        }
    }
    bool FindNearbyStaticSamples()
    {
        nearbySamples.Clear();var matrix=meshSpace.localToWorldMatrix;
        for(int i=0;i<source.Count;i++)
        {
            Vector3 p=matrix.MultiplyPoint3x4(source[i]);
            for(int slot=0;slot<4;slot++)
            {
                if(regionStrength[slot]<=.01f)continue;
                float radius=regionRadius[slot]+.22f;
                if((p-InkboundDisturbanceSource.Closest(p,(Vector3)regionFrom[slot],(Vector3)regionTo[slot])).sqrMagnitude>radius*radius)continue;
                nearbySamples.Add(i);break;
            }
        }
        return nearbySamples.Count>0;
    }

    bool BuildSamples()
    {
        int requested=lightweight?LightweightSamples:sampleCount;
        if(positions!=null&&positions.Length>0&&positions.Length!=Mathf.Min(requested,source.Count))
        {
            if(plumeRenderer)Destroy(plumeRenderer.gameObject);
            if(plume)Destroy(plume);if(baked)Destroy(baked);
            // Destroy is deferred until the frame ends. Never reuse the still-truthy
            // reference while rebuilding at a different sample quality in this frame.
            plumeRenderer=null;plume=null;baked=null;
            positions=null;source.Clear();
        }
        if (positions != null && positions.Length>0) return true;
        if (!plumeMaterial) return false;
        if (skin) { if(!baked)baked=new Mesh{name="Animated ink echo sample"};skin.BakeMesh(baked); meshSpace=skin.transform; baked.GetVertices(source); }
        else if (staticSurface && staticSurface.sharedMesh && staticSurface.sharedMesh.isReadable)
        {if(source.Count==0)ReadStaticSamples();if(!FindNearbyStaticSamples())return false;}
        if (source.Count==0) return false;
        int count = Mathf.Min(lightweight?LightweightSamples:sampleCount,source.Count);
        samples=new int[count]; positions=new Vector3[count]; velocities=new Vector3[count]; targets=new Vector3[count]; weights=new float[count];
        // Each sample owns a soft pigment card and a short curved filament in one shared mesh.
        vertices=new Vector3[count*VerticesPerSample]; uvs=new Vector2[count*VerticesPerSample]; brushData=new Vector2[count*VerticesPerSample]; colors=new Color[count*VerticesPerSample]; triangles=new int[count*QuadsPerSample*6];
        for(int i=0;i<count;i++)
        {
            samples[i]=skin?(i*7919+37)%source.Count:nearbySamples[(i*7919+37)%nearbySamples.Count]; targets[i]=positions[i]=meshSpace.TransformPoint(source[samples[i]]);
            for(int quad=0;quad<QuadsPerSample;quad++)
            {
                int q=i*VerticesPerSample+quad*4,t=(i*QuadsPerSample+quad)*6;
                for(int j=0;j<4;j++)brushData[q+j]=new Vector2(quad<3?quad==2?2:0:1,i*.713f);
                uvs[q]=new Vector2(0,0);uvs[q+1]=new Vector2(1,0);uvs[q+2]=new Vector2(1,1);uvs[q+3]=new Vector2(0,1);
                if(quad>=3 && quad<9)
                {
                    float start=(quad-3)/6f,end=(quad-2)/6f;
                    uvs[q]=new Vector2(0,start);uvs[q+1]=new Vector2(1,start);uvs[q+2]=new Vector2(1,end);uvs[q+3]=new Vector2(0,end);
                }
                triangles[t]=q;triangles[t+1]=q+1;triangles[t+2]=q+2;triangles[t+3]=q;triangles[t+4]=q+2;triangles[t+5]=q+3;
            }
        }
        var child = new GameObject("Returning ink filaments"); child.transform.SetParent(transform,false);
        plume=new Mesh{name="Ink echo cards and curved filaments"};plume.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh=plume;
        plumeRenderer=child.AddComponent<MeshRenderer>();plumeRenderer.sharedMaterial=plumeMaterial;
        plumeRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        plume.vertices=vertices;plume.uv=uvs;plume.uv2=brushData;plume.colors=colors;plume.triangles=triangles;
        return true;
    }

    void LateUpdate()
    {
        if (!visitor || Time.deltaTime<=0f) return;
        float speed=playbackSpeed*(settings?settings.inkPlaybackSpeed:1);
        float dt=Mathf.Min(Time.deltaTime,.04f)*Mathf.Clamp(speed,.0025f,4);
        inkClock+=dt;
        if(!skin&&!detailed&&staticSurface&&!staticSurface.GetComponent<Renderer>().isVisible)return;
        var bounds=skin?skin.bounds:staticSurface?staticSurface.GetComponent<Renderer>().bounds:new Bounds(transform.position+Vector3.up*.8f,Vector3.one);
        Vector3 center=bounds.center;float coarseRadius=skin?1.2f:bounds.extents.magnitude;
        bool touching=false;
        // Retain each contact's moving local region until it has finished returning.
        for(int slot=0;slot<4;slot++)
        {
            var owner=regionSources[slot];
            if(owner && (!owner.isActiveAndEnabled || owner.Intensity<=0 || Vector3.Distance(center,InkboundDisturbanceSource.Closest(center,owner.From,owner.To))>owner.radius+coarseRadius))regionSources[slot]=null;
        }
        foreach(var sourceOwner in InkboundDisturbanceSource.Active)
        {
            if(!sourceOwner || sourceOwner.Intensity<=0 || Vector3.Distance(center,InkboundDisturbanceSource.Closest(center,sourceOwner.From,sourceOwner.To))>sourceOwner.radius+coarseRadius)continue;
            int slot=System.Array.IndexOf(regionSources,sourceOwner);
            if(slot<0)for(int k=0;k<4;k++)if(!regionSources[k] && regionStrength[k]<.08f){slot=k;break;}
            if(slot>=0)regionSources[slot]=sourceOwner;
        }
        for(int slot=0;slot<4;slot++)
        {
            var owner=regionSources[slot];
            if(owner)
            {
                touching=true;regionFromLocal[slot]=transform.InverseTransformPoint(owner.From);regionToLocal[slot]=transform.InverseTransformPoint(owner.To);
                regionVelocity[slot]=Vector3.Lerp(regionVelocity[slot],owner.Velocity,dt*12);
                regionRadius[slot]=(owner.radius+.15f)*(touchRadius/.88f);
                float speedWeight=Mathf.Lerp(.48f,1,Mathf.Clamp01(owner.Velocity.magnitude/6));
                regionStrength[slot]=Mathf.MoveTowards(regionStrength[slot],Mathf.Clamp01(owner.Intensity*speedWeight),dt*8);
            }
            else {regionStrength[slot]*=Mathf.Exp(-3.4f*dt/Mathf.Max(.1f,returnSeconds));if(regionStrength[slot]<.012f)regionStrength[slot]=0;}
            Vector3 from=transform.TransformPoint(regionFromLocal[slot]),to=transform.TransformPoint(regionToLocal[slot]);
            regionFrom[slot]=new Vector4(from.x,from.y,from.z,regionRadius[slot]);regionTo[slot]=new Vector4(to.x,to.y,to.z,regionStrength[slot]);
        }
        if(touching && !wasTouching)Contacts++;
        Disturbance=0;for(int slot=0;slot<4;slot++)Disturbance=Mathf.Max(Disturbance,regionStrength[slot]);
        if(touching&&!detailed&&(skin||Time.time>=nextStaticProbe))
        {
            nextStaticProbe=Time.time+.15f;
            lightweight=active.Count>=HighQualityBudget&&!active.Contains(this);
            if(BuildSamples()){detailed=true;if(!lightweight)active.Add(this);}
        }
        wasTouching=touching;
        foreach(var surface in surfaces)
        {
            if(!surface)continue;
            surface.GetPropertyBlock(block);block.SetVectorArray("_InkFrom",regionFrom);block.SetVectorArray("_InkTo",regionTo);
            block.SetFloat(EchoStrength,Disturbance);surface.SetPropertyBlock(block);
        }
        if(!detailed || positions==null || !plumeRenderer || !plume)return;
        if(!skin&&touching&&Time.time>=nextStaticProbe)
        {
            nextStaticProbe=Time.time+.18f;
            if(FindNearbyStaticSamples())for(int i=0;i<samples.Length;i++)
                if(weights[i]<.015f){samples[i]=nearbySamples[(i*7919+37)%nearbySamples.Count];positions[i]=targets[i]=meshSpace.TransformPoint(source[samples[i]]);velocities[i]=Vector3.zero;}
        }
        if(Time.time>=nextBake)
        {
            nextBake=Time.time+(lightweight?.12f:.05f);
            if(skin){if(!baked)baked=new Mesh{name="Animated ink echo sample"};skin.BakeMesh(baked);baked.GetVertices(source);}
            for(int i=0;i<samples.Length;i++)targets[i]=meshSpace.TransformPoint(source[samples[i]]);
        }
        Camera camera=Camera.main;Vector3 right=camera?camera.transform.right:Vector3.right,up=camera?camera.transform.up:Vector3.up;
        var inverse=plumeRenderer.transform.worldToLocalMatrix;
        for(int i=0;i<positions.Length;i++)
        {
            Vector3 delta=Vector3.zero,transfer=Vector3.zero;float influence=0;
            for(int slot=0;slot<4;slot++)
            {
                if(!regionSources[slot])continue;
                Vector3 nearest=InkboundDisturbanceSource.Closest(targets[i],(Vector3)regionFrom[slot],(Vector3)regionTo[slot]);
                float local=regionStrength[slot]*(1-Mathf.SmoothStep(.12f,1,Vector3.Distance(targets[i],nearest)/Mathf.Max(.01f,regionRadius[slot])));
                if(local>influence){influence=local;delta=targets[i]-nearest;transfer=regionVelocity[slot];}
            }
            weights[i]=Mathf.MoveTowards(weights[i],influence,dt*(touching?7:1.4f/returnSeconds));
            float t=inkClock*1.15f;
            Vector3 pFlow=positions[i];
            Vector3 curl=new Vector3(Mathf.Sin(t+pFlow.y*3.1f+pFlow.z*1.7f),Mathf.Cos(t*.81f+pFlow.x*2)*.55f,Mathf.Sin(t*.67f-pFlow.y*2));
            // Advection bends around the capsule, preserving a wake rather than radial explosions.
            Vector3 lateral=Vector3.Cross(Vector3.up,transfer).normalized*Mathf.Sign(Vector3.Dot(delta,Vector3.Cross(Vector3.up,transfer)));
            if(influence>.01f)velocities[i]+=(transfer*.48f+delta.normalized*.9f+lateral*.65f+curl*flowStrength)*influence*dt*9;
            Vector3 offset=positions[i]-targets[i];
            float spring=touching?8f:21f;
            velocities[i]+=(curl*weights[i]*flowStrength-spring*offset-velocities[i]*7.8f)*dt;
            positions[i]+=velocities[i]*dt;
            Vector3 currentOffset=positions[i]-targets[i];
            if(currentOffset.magnitude>maximumDisplacement)positions[i]=targets[i]+currentOffset.normalized*maximumDisplacement;
            // Untouched parts follow the live pose directly; animation motion alone must not
            // create pigment cards across the entire resident while one shoulder is touched.
            if(weights[i]<.005f && influence<.005f){positions[i]=targets[i];velocities[i]=Vector3.zero;}
            MaximumOffset=Mathf.Max(MaximumOffset,Vector3.Distance(positions[i],targets[i]));
            float opacity=Mathf.Clamp01(weights[i]*2.2f+offset.magnitude*.9f);
            float size=.065f+weights[i]*.18f;
            int first=i*VerticesPerSample;
            Vector3 advect=velocities[i].sqrMagnitude>.01f?velocities[i].normalized:right;
            Vector3 across=Vector3.Cross(advect,camera?camera.transform.forward:Vector3.forward).normalized;
            if(across.sqrMagnitude<.1f)across=up;
            // Three overlapping scales: dense folded core, wet envelope, diluted halo.
            Card(first,positions[i],right,up,size*.82f,opacity*.9f,inverse);
            Card(first+4,positions[i]-advect*size*.35f,right,up,size*1.3f,opacity*.31f,inverse);
            Card(first+8,positions[i]+curl*size*.3f,right,up,size*1.85f,opacity*.04f,inverse);
            Vector3 origin=positions[i],target=targets[i];
            Vector3 control=Vector3.Lerp(origin,target,.45f)+curl*weights[i]*.26f;
            for(int segment=0;segment<6;segment++)
            {
                float a=segment/6f,b=(segment+1)/6f;
                Vector3 p=Quadratic(origin,control,target,a),q=Quadratic(origin,control,target,b);
                Vector3 width=Vector3.Cross(q-p,camera?camera.transform.forward:Vector3.forward).normalized;
                float wa=size*.52f*Mathf.Pow(1-a,.8f)+.003f,wb=size*.52f*Mathf.Pow(1-b,.8f)+.002f;
                PutQuad(first+12+segment*4,p-width*wa,p+width*wa,q+width*wb,q-width*wb,opacity*(.8f-a*.4f),inverse);
            }
            Vector3 joint=Quadratic(origin,control,target,.35f),tip=joint+curl*weights[i]*.32f;
            PutQuad(first+36,joint-across*size*.08f,joint+across*size*.08f,tip+across*.003f,tip-across*.003f,opacity*.34f,inverse);
            Vector3 branch=Vector3.Lerp(origin,joint,.4f)+across*weights[i]*.18f;
            PutQuad(first+40,origin-across*.015f,origin+across*.015f,branch+across*.003f,branch-across*.003f,opacity*.22f,inverse);
            Card(first+44,origin+advect*size*1.5f+curl*.1f,right,up,size*.17f,i%11==0?opacity*.5f:0,inverse);

        }
        plume.vertices=vertices;plume.colors=colors;plume.RecalculateBounds();
        plumeBlock.SetFloat("_UseLocalClock",1);plumeBlock.SetFloat("_InkClock",inkClock);plumeRenderer.SetPropertyBlock(plumeBlock);
        plumeRenderer.enabled=Disturbance>.001f;
        if(!touching && Disturbance<=0f)
        {
            detailed=false;active.Remove(this);plumeRenderer.enabled=false;
            for(int i=0;i<positions.Length;i++){positions[i]=targets[i];velocities[i]=Vector3.zero;weights[i]=0;}
        }
    }
    static Vector3 Quadratic(Vector3 a,Vector3 b,Vector3 c,float t)=>Vector3.Lerp(Vector3.Lerp(a,b,t),Vector3.Lerp(b,c,t),t);
    void Card(int q,Vector3 p,Vector3 right,Vector3 up,float size,float alpha,Matrix4x4 matrix)
    {PutQuad(q,p-right*size-up*size,p+right*size-up*size,p+right*size+up*size,p-right*size+up*size,alpha,matrix);}
    void PutQuad(int q,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float alpha,Matrix4x4 matrix)
    {
        // A zero-alpha transparent quad still shades every covered pixel. Collapse dormant
        // samples instead, keeping the local fluid wake without invisible overdraw.
        if(alpha<.006f)b=c=d=a;
        vertices[q]=matrix.MultiplyPoint3x4(a);vertices[q+1]=matrix.MultiplyPoint3x4(b);vertices[q+2]=matrix.MultiplyPoint3x4(c);vertices[q+3]=matrix.MultiplyPoint3x4(d);
        Color color=new Color(.025f,.029f,.032f,alpha);for(int k=0;k<4;k++)colors[q+k]=color;
    }
    void OnDisable(){active.Remove(this);detailed=false;if(plumeRenderer)plumeRenderer.enabled=false;}
    void OnDestroy(){if(baked)Destroy(baked);if(plume)Destroy(plume);}
}
