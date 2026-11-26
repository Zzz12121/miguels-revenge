#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class InkboundTownBuilder
{
    static Vector3 Bezier(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float t)
    {float s=1-t;return s*s*s*a+3*s*s*t*b+3*s*t*t*c+t*t*t*d;}

    // Mesh batches are authored directly: no hidden forest of primitive leaf GameObjects.
    sealed class BrushGeometry
    {
        readonly List<Vector3> v=new();readonly List<Vector2> uv=new();readonly List<int> tr=new();
        public void Tube(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float from,float to,int steps=14,int sides=7)
        {
            int first=v.Count;float length=Vector3.Distance(a,b)+Vector3.Distance(b,c)+Vector3.Distance(c,d);
            for(int k=0;k<=steps;k++)
            {
                float t=k/(float)steps;Vector3 p=Bezier(a,b,c,d,t);
                Vector3 tangent=(Bezier(a,b,c,d,Mathf.Min(1,t+.008f))-Bezier(a,b,c,d,Mathf.Max(0,t-.008f))).normalized;
                Vector3 x=Vector3.Cross(tangent,Mathf.Abs(tangent.z)>.9f?Vector3.right:Vector3.forward).normalized,z=Vector3.Cross(tangent,x);
                float r=Mathf.Lerp(from,to,Mathf.Pow(t,.82f))*(1+Mathf.Sin(k*1.73f)*.045f);
                for(int j=0;j<=sides;j++)
                {
                    float angle=j*Mathf.PI*2/sides;v.Add(p+(x*Mathf.Cos(angle)+z*Mathf.Sin(angle))*r);uv.Add(new Vector2(j/(float)sides,t*length));
                    if(k<steps && j<sides){int q=first+k*(sides+1)+j;tr.AddRange(new[]{q,q+1,q+sides+1,q+1,q+sides+2,q+sides+1});}
                }
            }
        }
        public void Lance(Vector3 a,Vector3 b,float width,float seed)
        {
            int first=v.Count;Vector3 direction=b-a;
            Vector3 side=Vector3.Cross(direction,new Vector3(Mathf.Sin(seed),.23f,Mathf.Cos(seed))).normalized;
            Vector3 bend=Vector3.Cross(side,direction).normalized*direction.magnitude*.12f;
            const int segments=6;
            for(int k=0;k<=segments;k++)
            {
                float t=k/(float)segments;Vector3 p=Vector3.Lerp(a,b,t)+bend*Mathf.Sin(t*Mathf.PI);
                float w=Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),.8f)*width*(1-t*.32f);
                v.Add(p-side*w);v.Add(p+side*w);uv.Add(new Vector2(0,t));uv.Add(new Vector2(1,t));
                if(k<segments){int q=first+k*2;tr.AddRange(new[]{q,q+1,q+2,q+1,q+3,q+2});}
            }
        }
        public Mesh Mesh(string name)
        {var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
    }
    static GameObject SculptTree(string name,int kind)
    {
        var root=Root("IT_"+name,null);var wood=new BrushGeometry();var dark=new BrushGeometry();var pale=new BrushGeometry();
        if(kind==2)
        {
            for(int stalk=0;stalk<9;stalk++)
            {
                Vector3 a=new Vector3(Range(-1.4f,1.4f),0,Range(-1,1)),d=a+new Vector3(Range(-.8f,.8f),Range(4.8f,7.8f),Range(-.7f,.7f));
                wood.Tube(a,Vector3.Lerp(a,d,.33f),Vector3.Lerp(a,d,.66f),d,.065f,.025f,10,6);
                for(int branch=3;branch<8;branch++)
                {
                    Vector3 at=Vector3.Lerp(a,d,branch/8f),end=at+Quaternion.Euler(0,branch*137+stalk*41,0)*new Vector3(1.1f,.3f,.2f);
                    wood.Tube(at,at+Vector3.up*.2f,end+Vector3.up*.3f,end,.024f,.005f,6,5);
                    for(int leaf=0;leaf<7;leaf++)
                    {
                        Vector3 p=Vector3.Lerp(at,end,.25f+leaf*.1f);(leaf%3==0?dark:pale).Lance(p,p+new Vector3(Range(-.4f,.4f),Range(-.65f,-.15f),Range(-.35f,.35f)),Range(.035f,.065f),stalk+leaf);
                    }
                }
            }
        }
        else
        {
            float h=kind==0?8.5f:9;
            Vector3 top=new Vector3(.5f,h,0);
            wood.Tube(Vector3.zero,new Vector3(-1.15f,h*.3f,.3f),new Vector3(1.05f,h*.55f,-.3f),top,.62f,.105f,24,10);
            for(int r=0;r<6;r++)
            {
                float angle=r*1.047f;Vector3 p=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                wood.Tube(p*1.4f,p*.75f+Vector3.up*.15f,p*.2f+Vector3.up*.4f,Vector3.up*.8f,.025f,.26f,8,7);
            }
            int branches=kind==0?11:13;
            for(int b=0;b<branches;b++)
            {
                float angle=b*2.39996f;Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 a=Bezier(Vector3.zero,new Vector3(-1.15f,h*.3f,.3f),new Vector3(1.05f,h*.55f,-.3f),top,Range(.39f,.88f));
                Vector3 d=a+radial*Range(2.6f,4.5f)+Vector3.up*Range(.25f,1.3f);
                wood.Tube(a,a+radial*.7f+Vector3.up*1.4f,d+Vector3.up*.9f,d,Range(.11f,.22f),.025f,16,7);
                for(int twig=0;twig<(kind==0?6:4);twig++)
                {
                    float offset=(twig-2.5f)*.28f;Vector3 start=d+Vector3.Cross(radial,Vector3.up)*offset;
                    if(kind==0)
                    {
                        float drop=Range(1.9f,4.25f);Vector3 end=start+radial*Range(.3f,.9f)-Vector3.up*drop;
                        Vector3 c1=start+radial*.8f+Vector3.up*.5f,c2=end-radial*.24f+Vector3.up*(drop*.32f);
                        wood.Tube(start,c1,c2,end,.021f,.0025f,16,5);
                        int leaves=rng.Next(9,16);
                        for(int k=1;k<leaves;k++)
                        {
                            float t=k/(float)leaves;Vector3 p=Bezier(start,c1,c2,end,t);
                            float length=Range(.29f,.64f);Vector3 side=Vector3.Cross(radial,Vector3.up)*(k%2==0?1:-1);
                            Vector3 tip=p+side*Range(.09f,.24f)-Vector3.up*length+radial*.08f;
                            (rng.NextDouble()<.24?dark:pale).Lance(p,tip,Range(.035f,.075f),b*.7f+twig*.4f+k*.9f);
                        }
                    }
                    else
                    {
                        Vector3 tip=start+radial*Range(.5f,1.1f)+Vector3.up*.25f;
                        wood.Tube(start,start+Vector3.up*.3f,tip,tip,.035f,.006f,8,5);
                        for(int k=0;k<32;k++)
                        {
                            Vector3 p=Vector3.Lerp(start,tip,Range(0,1))+new Vector3(Range(-.8f,.8f),Range(-.03f,.22f),Range(-.7f,.7f));
                            (k%3==0?pale:dark).Lance(p,p+new Vector3(Range(-.32f,.32f),Range(.12f,.32f),Range(-.32f,.32f)),Range(.02f,.048f),k);
                        }
                    }
                }
            }
            var collision=root.gameObject.AddComponent<CapsuleCollider>();collision.center=new Vector3(-.12f,2.4f,0);collision.radius=.46f;collision.height=4.8f;
        }
        Model("Curved tapered bark and branches",root,SaveMesh(wood.Mesh(name+" bark"),name+"_Bark"),bark);
        Model("Dense clustered brush leaves",root,SaveMesh(dark.Mesh(name+" dark leaves"),name+"_Leaves"),foliage);
        Model("Pale hanging leaf washes",root,SaveMesh(pale.Mesh(name+" pale leaves"),name+"_Wash"),leafWash);
        return SavePrefab(root,Prefabs+"/PF_IT_"+name+".prefab");
    }
}
#endif
