using UnityEngine;

/// <summary>Town-local soft grounding and short, pooled ink traces. No gameplay or animation ownership.</summary>
public sealed class InkboundGroundWash : MonoBehaviour
{
    public Material inkMaterial;
    [Range(0,.5f)] public float contactOpacity=.14f;
    [Range(0,2)] public float traceLifetime=1.2f;
    const int Count=12;
    readonly Vector3[] positions=new Vector3[Count],vertices=new Vector3[Count*4];
    readonly float[] times=new float[Count];readonly Color[] colors=new Color[Count*4];
    Mesh mesh;Transform drawing;ThirdPersonController motor;PlayerHealth health;
    Vector3 lastFoot;int cursor=1;
    void Start()
    {
        motor=GetComponent<ThirdPersonController>();health=GetComponent<PlayerHealth>();lastFoot=transform.position;
        var go=new GameObject("Soft foot wash and pooled traces");drawing=go.transform;
        drawing.SetParent(transform,false);
        mesh=new Mesh{name="Town ground ink traces"};mesh.MarkDynamic();
        var uv=new Vector2[Count*4];var triangles=new int[Count*6];
        for(int i=0;i<Count;i++)
        {
            int q=i*4,t=i*6;uv[q]=Vector2.zero;uv[q+1]=Vector2.right;uv[q+2]=Vector2.one;uv[q+3]=Vector2.up;
            triangles[t]=q;triangles[t+1]=q+1;triangles[t+2]=q+2;triangles[t+3]=q;triangles[t+4]=q+2;triangles[t+5]=q+3;
            times[i]=-100;
        }
        mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.colors=colors;
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=inkMaterial;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    void LateUpdate()
    {
        if(!mesh || !motor || !health || Time.deltaTime<=0)return;
        Vector3 foot=new Vector3(transform.position.x,health.FeetY+.015f,transform.position.z);
        bool grounded=motor.IsGrounded;
        positions[0]=foot;times[0]=grounded?Time.time:-100;
        if(grounded && Vector3.Distance(foot,lastFoot)>.72f)
        {
            positions[cursor]=foot+transform.right*(cursor%2==0?.13f:-.13f);times[cursor]=Time.time;
            cursor=1+cursor%(Count-1);lastFoot=foot;
        }
        Matrix4x4 inverse=drawing.worldToLocalMatrix;
        for(int i=0;i<Count;i++)
        {
            float age=Time.time-times[i],opacity=i==0?contactOpacity:Mathf.Clamp01(1-age/Mathf.Max(.01f,traceLifetime))*.085f;
            Vector3 p=positions[i],x=Vector3.right*(i==0?.45f:.13f),z=Vector3.forward*(i==0?.34f:.24f);
            int q=i*4;vertices[q]=inverse.MultiplyPoint3x4(p-x-z);vertices[q+1]=inverse.MultiplyPoint3x4(p+x-z);vertices[q+2]=inverse.MultiplyPoint3x4(p+x+z);vertices[q+3]=inverse.MultiplyPoint3x4(p-x+z);
            for(int k=0;k<4;k++)colors[q+k]=new Color(0,0,0,opacity);
        }
        mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateBounds();
    }
    void OnDestroy(){if(mesh)Destroy(mesh);}
}
