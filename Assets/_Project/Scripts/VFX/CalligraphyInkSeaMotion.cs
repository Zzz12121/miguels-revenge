using System.Collections.Generic;
using UnityEngine;

/// <summary>A calm ink plane with staggered, branching ink diffusion into the air. No physics.</summary>
[DefaultExecutionOrder(310)]
public sealed class CalligraphyInkSeaMotion : MonoBehaviour
{
    CalligraphyBossCombat.AvatarSeaSettings settings;
    Renderer surface,plumeRenderer;
    Mesh plumes;
    Material fallbackMaterial;
    Vector3[] vertices;
    Color32[] colours;
    Vector2[] phases;
    Patch[] patches;
    MaterialPropertyBlock block;
    float age;
    const int Branches=7,Segments=9,VerticesPerBranch=(Segments+1)*2;
    struct Patch
    {
        public Vector3 centre;
        public float seed,lifetime,offset,height,yaw;
    }
    public void Initialize(CalligraphyBossCombat.AvatarSeaSettings tuning,Mesh surfaceMesh)
    {
        block=new MaterialPropertyBlock();settings=tuning;surface=GetComponent<Renderer>();
        var bounds=surfaceMesh.bounds;bounds.Expand(new Vector3(2,2,2));surfaceMesh.bounds=bounds;
        int count=Mathf.Clamp(settings.airPlumeCount,0,160);patches=new Patch[count];
        var random=new System.Random(441);
        float Next()=>(float)random.NextDouble();
        for(int i=0;i<count;i++)
        {
            // Equal-area spiral covers centre and outer sea without a regular ring/grid layout.
            float a=i*2.39996323f+(Next()-.5f)*.7f;
            float r=Mathf.Sqrt((i+.2f+Next()*.6f)/Mathf.Max(1,count))*settings.radius*.94f;
            patches[i]=new Patch{
                centre=new Vector3(Mathf.Sin(a)*r,0,Mathf.Cos(a)*r),seed=Next()*83,
                lifetime=Mathf.Lerp(Mathf.Max(1,settings.airLifetime.x),Mathf.Max(1,settings.airLifetime.y),Next()),
                offset=Mathf.Repeat(i*.61803399f+Next()*.15f,1),
                height=Mathf.Lerp(Mathf.Max(.4f,settings.airHeight.x),Mathf.Max(.4f,settings.airHeight.y),Next()),yaw=Next()*6.283185f};
        }
        Material material=settings.diffusionMaterial;
        if(!material)
        {
            var shader=Shader.Find("Project/VFX/CalligraphyInkDiffusion");if(!shader)return;
            fallbackMaterial=new Material(shader){name="Owned airborne ink diffusion"};material=fallbackMaterial;
        }
        int vertexCount=count*Branches*VerticesPerBranch;
        vertices=new Vector3[vertexCount];colours=new Color32[vertexCount];phases=new Vector2[vertexCount];
        var uv=new Vector2[vertexCount];var triangles=new List<int>(count*Branches*Segments*6);
        for(int k=0;k<count;k++)for(int branch=0;branch<Branches;branch++)for(int j=0;j<=Segments;j++)
        {
            int n=(k*Branches+branch)*VerticesPerBranch+j*2;float v=j/(float)Segments;
            uv[n]=new Vector2(0,v);uv[n+1]=new Vector2(1,v);
            if(j<Segments)triangles.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});
        }
        plumes=new Mesh{name="Branching airborne ink diffusion"};plumes.MarkDynamic();
        plumes.vertices=vertices;plumes.uv=uv;plumes.SetTriangles(triangles,0);
        plumeRenderer=CalligraphyGeometry.MeshObject("Ink seeping upward and dissolving",transform,plumes,material);
        plumeRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;plumeRenderer.receiveShadows=false;
        plumes.bounds=new Bounds(Vector3.up*Mathf.Max(settings.airHeight.x,settings.airHeight.y)*.6f,
            new Vector3(settings.radius*2+20,Mathf.Max(settings.airHeight.x,settings.airHeight.y)*3+10,settings.radius*2+20));
    }
    void LateUpdate()
    {
        if(!surface || settings==null)return;age+=Time.deltaTime;surface.GetPropertyBlock(block);
        block.SetFloat("_WaveAmplitude",Mathf.Clamp(settings.waveAmplitude,0,.35f));
        block.SetFloat("_ChurnSpeed",settings.churnSpeed);block.SetFloat("_VortexDensity",settings.vortexDensity);
        block.SetFloat("_VortexStrength",settings.vortexStrength);block.SetFloat("_EdgeStrength",settings.edgeStrength);
        block.SetFloat("_Age",age);surface.SetPropertyBlock(block);
        if(!plumes || !plumeRenderer)return;
        float reveal=block.GetFloat("_Reveal"),opacity=block.GetFloat("_Opacity");
        var camera=Camera.main;Vector3 view=camera?transform.InverseTransformPoint(camera.transform.position):new Vector3(0,15,25);
        for(int k=0;k<patches.Length;k++)
        {
            Patch patch=patches[k];float clock=age/patch.lifetime+patch.offset;
            int cycle=Mathf.FloorToInt(clock);float p=clock-cycle;float cycleAngle=patch.yaw+cycle*2.173f;
            Vector3 drift=new Vector3(Mathf.Cos(cycleAngle),0,Mathf.Sin(cycleAngle));
            Vector3 centre=patch.centre+drift*(Mathf.Sin(cycle*4.13f+patch.seed)*1.6f);
            float revealWeight=Mathf.SmoothStep(0,1,(reveal*settings.radius-centre.magnitude)/4);
            float envelope=Mathf.SmoothStep(0,1,p/.13f)*(1-Mathf.SmoothStep(0,1,(p-.55f)/.45f))*revealWeight;
            float rise=.08f+.92f*Mathf.SmoothStep(0,1,p/.67f);
            byte alpha=(byte)Mathf.RoundToInt(Mathf.Clamp01(envelope)*255);
            for(int branch=0;branch<Branches;branch++)for(int j=0;j<=Segments;j++)
            {
                float v=j/(float)Segments;
                Vector3 point=Point(patch,centre,drift,cycleAngle,branch,v,rise,p);
                Vector3 tangent=Point(patch,centre,drift,cycleAngle,branch,v+.01f,rise,p)-point;
                Vector3 side=Vector3.Cross((view-point).normalized,tangent.normalized);
                if(side.sqrMagnitude<.001f)side=Vector3.right;else side.Normalize();
                float level=branch==0?1:branch<3?.65f:.43f;
                // Broad soft ribbons, not cylindrical/heavy liquid strands. Erosion adds fine sub-branches.
                float width=level*(.35f+settings.airSpread*.33f)*(1+.65f*p)*(.62f+.38f*Mathf.Sin(v*2.4f));
                int n=(k*Branches+branch)*VerticesPerBranch+j*2;
                vertices[n]=point-side*width;vertices[n+1]=point+side*width;
                colours[n]=colours[n+1]=new Color32(255,255,255,alpha);
                phases[n]=phases[n+1]=new Vector2(patch.seed+branch*3.17f+cycle*.731f,p);
            }
        }
        plumes.vertices=vertices;plumes.colors32=colours;plumes.uv2=phases;
        block.Clear();block.SetFloat("_Opacity",opacity*settings.airOpacity);
        block.SetFloat("_Branching",settings.airBranching);block.SetFloat("_Age",age);plumeRenderer.SetPropertyBlock(block);
    }
    Vector3 Point(Patch patch,Vector3 centre,Vector3 drift,float yaw,int branch,float v,float rise,float progress)
    {
        int depth=branch==0?0:branch<3?1:2;float start=depth==0?0:depth==1?.24f:.53f;
        float h=start+v*(1-start)*(branch==0?1:1.04f+(branch%3)*.035f);
        float parentSign=branch==0?0:branch==1 || branch==3 || branch==4?-1:1;
        Vector3 across=new Vector3(-Mathf.Sin(yaw),0,Mathf.Cos(yaw));float split=Mathf.Max(0,h-.24f);
        Vector3 offset=across*parentSign*Mathf.Pow(split,1.35f)*settings.airSpread;
        if(depth==2)
        {
            float childSign=branch%2==0?1:-1;Vector3 child=(across*.6f+drift*.8f)*childSign;
            offset+=child*Mathf.Pow(Mathf.Max(0,h-.53f),1.1f)*settings.airSpread*1.25f;
        }
        float curl=Mathf.Sin(h*7+patch.seed+progress*1.5f)*h*h*.32f;
        return centre+Vector3.up*(.04f+patch.height*h*rise)+drift*(h*h*(.6f+progress*.65f))+offset*rise+across*curl*rise;
    }
    void OnDestroy(){if(fallbackMaterial)Destroy(fallbackMaterial);}
}
