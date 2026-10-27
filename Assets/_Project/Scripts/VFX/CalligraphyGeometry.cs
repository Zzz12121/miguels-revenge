using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class CalligraphyGeometry
{
    static MaterialPropertyBlock tintBlock;
    public static Vector3 Ground(Vector3 position,Transform ignore=null)
    {
        var hits=Physics.RaycastAll(position+Vector3.up*3,Vector3.down,12,~0,QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var h in hits)
            if(h.normal.y>.5f && !(ignore && h.transform.IsChildOf(ignore)) && h.collider.GetComponentInParent<IDamageable>()==null)
                return h.point;
        return position;
    }
    public static MeshRenderer MeshObject(string name,Transform parent,Mesh mesh,Material material)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>(); r.sharedMaterial=material;
        r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
        go.AddComponent<CalligraphyGeneratedMesh>().owned=mesh; return r;
    }
    public static Mesh Quad()
    {
        var m=new Mesh{name="Ink sheet"};
        m.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)};
        m.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};m.triangles=new[]{0,2,1,1,2,3};m.RecalculateNormals();return m;
    }
    public static Mesh Loft(Vector3[] centers,Vector2[] radii,int sides=10)
    {
        var verts=new Vector3[centers.Length*sides];var uv=new Vector2[verts.Length];var triangles=new List<int>();
        for(int j=0;j<centers.Length;j++)
        {
            for(int k=0;k<sides;k++)
            {
                float a=k*Mathf.PI*2/sides;int i=j*sides+k;
                verts[i]=centers[j]+new Vector3(Mathf.Cos(a)*radii[j].x,Mathf.Sin(a)*radii[j].y,0);
                uv[i]=new Vector2(j/(float)(centers.Length-1),k/(float)sides);
                if(j>0) {int n=j*sides+(k+1)%sides;triangles.AddRange(new[]{i,n,i-sides,n,n-sides,i-sides});}
            }
        }
        var m=new Mesh{name="Hand-shaped ink loft"};m.vertices=verts;m.uv=uv;m.triangles=triangles.ToArray();m.RecalculateNormals();m.RecalculateBounds();return m;
    }
    public static LineRenderer Line(string name,Transform parent,Material mat,Vector3[] points,float width,bool world=false)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        var r=go.AddComponent<LineRenderer>();r.sharedMaterial=mat;r.useWorldSpace=world;r.positionCount=points.Length;r.SetPositions(points);
        r.widthMultiplier=width;r.numCornerVertices=2;r.numCapVertices=2;
        r.widthCurve=AnimationCurve.Linear(0,1,1,.05f);r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return r;
    }
    public static void Tint(Renderer r,Color color,float opacity=1,float fade=0)
    {
        tintBlock ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(tintBlock);tintBlock.SetColor("_BaseColor",color);tintBlock.SetFloat("_Opacity",opacity);tintBlock.SetFloat("_Fade",fade);r.SetPropertyBlock(tintBlock);
    }
}

/// <summary>Runtime geometry owner: renderer disabled before native mesh destruction.</summary>
public sealed class CalligraphyGeneratedMesh : MonoBehaviour
{
    public Mesh owned;
    void OnDestroy() { var r=GetComponent<Renderer>();if(r)r.enabled=false;if(owned)Destroy(owned); }
}
