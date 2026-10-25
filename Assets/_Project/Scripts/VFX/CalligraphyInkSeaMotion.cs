using UnityEngine;

/// <summary>Dense displaced sea plus limited, genuinely curled liquid sheets. No physics impulses.</summary>
[DefaultExecutionOrder(310)]
public sealed class CalligraphyInkSeaMotion : MonoBehaviour
{
    CalligraphyBossCombat.AvatarSeaSettings settings;
    Renderer surface,foldRenderer;
    Mesh folds;
    Vector3[] vertices;
    Vector2[] uv;
    Vector3[] centers;
    float[] phases;
    MaterialPropertyBlock block;
    float age;
    const int Across=16,Roll=10;
    public void Initialize(CalligraphyBossCombat.AvatarSeaSettings tuning,Mesh surfaceMesh)
    {
        block=new MaterialPropertyBlock();settings=tuning;surface=GetComponent<Renderer>();
        var bounds=surfaceMesh.bounds;bounds.Expand(new Vector3(12,settings.waveAmplitude*8+10,12));surfaceMesh.bounds=bounds;
        int count=Mathf.Clamp(settings.inkFilaments,0,48);centers=new Vector3[count];phases=new float[count];
        var rand=new System.Random(441);
        for(int i=0;i<count;i++)
        {float a=(float)rand.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)rand.NextDouble())*settings.radius*.85f;centers[i]=new Vector3(Mathf.Sin(a)*r,0,Mathf.Cos(a)*r);phases[i]=(float)rand.NextDouble()*6.28f;}
        vertices=new Vector3[count*(Across+1)*(Roll+1)];uv=new Vector2[vertices.Length];
        var triangles=new System.Collections.Generic.List<int>();
        for(int k=0;k<count;k++)for(int i=0;i<=Across;i++)for(int j=0;j<=Roll;j++)
        {
            int n=k*(Across+1)*(Roll+1)+i*(Roll+1)+j;uv[n]=new Vector2(i/(float)Across,j/(float)Roll);
            if(i<Across && j<Roll){int b=n+Roll+1;triangles.AddRange(new[]{n,b,n+1,n+1,b,b+1});}
        }
        folds=new Mesh{name="Overturning ink wave sheets"};folds.MarkDynamic();folds.vertices=vertices;folds.uv=uv;folds.SetTriangles(triangles,0);
        foldRenderer=CalligraphyGeometry.MeshObject("Curling dense ink crests",transform,folds,surface.sharedMaterial);
    }
    void LateUpdate()
    {
        if(!surface || !settings.material)return;age+=Time.deltaTime;
        surface.GetPropertyBlock(block);
        block.SetFloat("_WaveAmplitude",settings.waveAmplitude);block.SetFloat("_ChurnSpeed",settings.churnSpeed);
        block.SetFloat("_VortexDensity",settings.vortexDensity);block.SetFloat("_VortexStrength",settings.vortexStrength);
        block.SetFloat("_EdgeStrength",settings.edgeStrength);block.SetFloat("_Age",age);surface.SetPropertyBlock(block);
        float reveal=block.GetFloat("_Reveal"),opacity=block.GetFloat("_Opacity");
        for(int k=0;k<centers.Length;k++)
        {
            float t=age*settings.churnSpeed,cycle=t*(.25f+k%3*.06f)+phases[k];
            float pulse=.45f+.55f*Mathf.Pow(Mathf.Sin(cycle),2);
            float revealWeight=Mathf.SmoothStep(0,1,(reveal*settings.radius-centers[k].magnitude)/6);
            float height=settings.waveAmplitude*(.6f+.12f*(k%5))*pulse*revealWeight;
            float yaw=phases[k]+t*.09f*(k%2==0?1:-1);var rotation=Quaternion.Euler(0,yaw*Mathf.Rad2Deg,0);
            Vector3 center=centers[k]+new Vector3(Mathf.Sin(cycle*.37f),0,Mathf.Cos(cycle*.43f))*2;
            for(int i=0;i<=Across;i++)for(int j=0;j<=Roll;j++)
            {
                float u=i/(float)Across,v=j/(float)Roll;
                float taper=Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*Mathf.PI)),.6f);
                float angle=v*(3.3f+Mathf.Sin(cycle+u*2)*.8f);
                Vector3 p=new((u-.5f)*(9+k%4*2),(.12f+(1-Mathf.Cos(angle))*height)*taper,
                    Mathf.Sin(angle)*height*1.2f+Mathf.Sin(u*7+t)*.6f);
                vertices[k*(Across+1)*(Roll+1)+i*(Roll+1)+j]=center+rotation*p;
            }
        }
        folds.vertices=vertices;folds.RecalculateBounds();
        block.SetFloat("_Sea",2);block.SetFloat("_Opacity",opacity);foldRenderer.SetPropertyBlock(block);
    }
}
