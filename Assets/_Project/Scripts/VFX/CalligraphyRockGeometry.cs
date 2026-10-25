using System.Collections.Generic;
using UnityEngine;

/// <summary>Faceted cliff, fractured slab and hanging spire profiles, with a shared flat convex rim.</summary>
public static class CalligraphyRockGeometry
{
    public static Mesh Create(float radius,float depth,int seed,float irregularity,float sharpness,out Vector3[] rim)
    {
        var random=new System.Random(seed);
        float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
        int count=7+seed%5,kind=seed%3;
        float aspect=kind==1?R(.48f,.68f):R(.72f,1);
        var points=new List<Vector2>();
        for(int i=0;i<count;i++)
        {
            float angle=(i+R(-.16f,.16f))*Mathf.PI*2/count;
            float r=radius*R(1-irregularity*.3f,1);
            points.Add(new Vector2(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r*aspect));
        }
        // Convex hull makes the visible top exactly match the cooked collider and ledge queries.
        points.Sort((a,b)=>a.x==b.x?a.y.CompareTo(b.y):a.x.CompareTo(b.x));
        float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
        var hull=new List<Vector2>();
        foreach(var p in points){while(hull.Count>1 && Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
        int lower=hull.Count;
        for(int i=points.Count-2;i>=0;i--){while(hull.Count>lower && Cross(hull[hull.Count-2],hull[hull.Count-1],points[i])<=0)hull.RemoveAt(hull.Count-1);hull.Add(points[i]);}
        hull.RemoveAt(hull.Count-1);count=hull.Count;rim=new Vector3[count];
        var shape=new List<Vector3>();
        for(int j=0;j<4;j++)for(int i=0;i<count;i++)
        {
            Vector2 p=hull[i];float ridge=(i%3==0?1:.65f);
            float factor=j==0?1:j==1?R(.85f,.98f):j==2?(kind==1?R(.68f,.87f):R(.32f,.7f)):R(.04f,kind==1?.45f:.23f)*(1.2f-sharpness*.5f);
            float y=j==0?0:j==1?-depth*R(.15f,.28f):j==2?-depth*R(.48f,.75f):-depth*R(.8f,1.1f);
            if(kind==2 && j>1)y*=ridge;
            Vector3 v=new(p.x*factor+(j>1?radius*.13f:0),y,p.y*factor+(j>1?-radius*.09f:0));
            shape.Add(v);if(j==0)rim[i]=v;
        }
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        void Tri(Vector3 a,Vector3 b,Vector3 c)
        {int k=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);foreach(var v in new[]{a,b,c})uv.Add(new Vector2(v.x/radius*.5f+.5f,v.z/radius*.5f+.5f));tris.Add(k);tris.Add(k+1);tris.Add(k+2);}
        for(int i=0;i<count;i++)
        {
            int n=(i+1)%count;Tri(Vector3.zero,shape[n],shape[i]);
            for(int j=0;j<3;j++){int a=j*count+i,b=j*count+n,c=a+count,d=b+count;Tri(shape[a],shape[b],shape[c]);Tri(shape[b],shape[d],shape[c]);}
            Tri(new Vector3(radius*.13f,-depth*1.05f,-radius*.09f),shape[3*count+i],shape[3*count+n]);
        }
        var mesh=new Mesh{name="Fractured ink cliff "+seed};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
}
