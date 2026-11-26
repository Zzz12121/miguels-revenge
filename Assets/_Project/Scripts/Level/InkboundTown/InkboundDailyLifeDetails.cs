using UnityEngine;

/// <summary>Quiet hand tasks and low opacity kitchen steam. Never responds to combat.</summary>
[DefaultExecutionOrder(100)]
public sealed class InkboundDailyLifeDetails:MonoBehaviour
{
    public enum Kind { ArrangeGoods,Chess,RepairNet,Steam }
    public Kind kind;
    public Vector3 localTaskPoint;
    public Material steamMaterial;
    public float phase;
    Transform upper,fore,hand;
    Mesh steam;
    Vector3[] vertices;
    Color[] colors;
    void Start()
    {
        var animator=GetComponentInChildren<Animator>();
        if(animator&&animator.isHuman)
        {upper=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);fore=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);hand=animator.GetBoneTransform(HumanBodyBones.RightHand);}
        if(kind!=Kind.Steam||!steamMaterial)return;
        var go=new GameObject("Thin rising steam sheets");go.transform.SetParent(transform,false);
        go.AddComponent<MeshRenderer>().sharedMaterial=steamMaterial;steam=new Mesh{name="Kitchen steam sheets"};steam.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=steam;
        vertices=new Vector3[24];colors=new Color[24];var uv=new Vector2[24];var uv2=new Vector2[24];var tri=new int[36];
        for(int j=0;j<6;j++)
        {int q=j*4,t=j*6;uv[q]=Vector2.zero;uv[q+1]=Vector2.right;uv[q+2]=Vector2.one;uv[q+3]=Vector2.up;for(int k=0;k<4;k++)uv2[q+k]=new Vector2(2,j*.71f);tri[t]=q;tri[t+1]=q+1;tri[t+2]=q+2;tri[t+3]=q;tri[t+4]=q+2;tri[t+5]=q+3;}
        steam.vertices=vertices;steam.uv=uv;steam.uv2=uv2;steam.triangles=tri;
    }
    void LateUpdate()
    {
        if(Time.deltaTime<=0)return;
        float time=Time.time+phase*8;
        if(kind==Kind.Steam)
        {
            if(!steam)return;
            Vector3 right=Camera.main?transform.InverseTransformDirection(Camera.main.transform.right):Vector3.right;
            for(int j=0;j<6;j++)
            {
                float t=Mathf.Repeat(time*.13f+j*.173f,1),width=.08f+t*.17f;
                Vector3 p=localTaskPoint+Vector3.up*(t*.9f)+Vector3.right*(Mathf.Sin(t*5+j)*t*.14f);
                int q=j*4;vertices[q]=p-right*width;vertices[q+1]=p+right*width;vertices[q+2]=p+right*width+Vector3.up*.32f;vertices[q+3]=p-right*width+Vector3.up*.32f;
                Color c=new Color(.72f,.72f,.70f,Mathf.Sin(t*Mathf.PI)*.11f);for(int k=0;k<4;k++)colors[q+k]=c;
            }
            steam.vertices=vertices;steam.colors=colors;steam.RecalculateBounds();return;
        }
        if(!upper||!fore||!hand)return;
        float w=Mathf.SmoothStep(0,1,Mathf.Clamp01((Mathf.Sin(time*(kind==Kind.Chess?.35f:.85f))+.15f)*1.7f));
        Vector3 task=transform.TransformPoint(localTaskPoint)+transform.right*Mathf.Sin(time*.7f)*.055f;
        Vector3 elbow=upper.position+transform.forward*.27f+transform.right*.22f-Vector3.up*.21f;
        Aim(upper,fore,elbow-upper.position,w);Aim(fore,hand,task-fore.position,w);
    }
    static void Aim(Transform a,Transform b,Vector3 d,float w)
    {if(d.sqrMagnitude>.001f)a.rotation=Quaternion.Slerp(a.rotation,Quaternion.FromToRotation(b.position-a.position,d)*a.rotation,w);}
    void OnDestroy(){if(steam)Destroy(steam);}
}
