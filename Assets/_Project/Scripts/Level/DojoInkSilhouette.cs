using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A frozen, harmless ink snapshot with an exact bounded lifetime.</summary>
public sealed class DojoInkSilhouette : MonoBehaviour
{
    readonly List<Mesh> owned=new();
    Renderer[] visuals;
    float born,lifetime;
    public static DojoInkSilhouette Create(CalligraphyBrushPresentation source,Material ink,Transform parent,float lifetime=1)
    {
        var go=new GameObject("Dojo teleport ink afterimage");go.transform.SetParent(parent,false);
        var s=go.AddComponent<DojoInkSilhouette>();s.born=Time.time;s.lifetime=Mathf.Max(.05f,lifetime);
        foreach(var skin in source.animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh{name="Dojo ink pose snapshot"};skin.BakeMesh(mesh,true);s.owned.Add(mesh);
            s.Copy(mesh,skin.transform,ink,skin.sharedMaterials.Length);
        }
        foreach(var f in source.brushPivot.GetComponentsInChildren<MeshFilter>())
            if(f.sharedMesh)s.Copy(f.sharedMesh,f.transform,ink,f.GetComponent<Renderer>().sharedMaterials.Length);
        s.visuals=go.GetComponentsInChildren<Renderer>();return s;
    }
    void Copy(Mesh mesh,Transform source,Material ink,int slots)
    {
        var go=new GameObject("Frozen ink silhouette");go.transform.SetParent(transform,false);
        go.transform.SetPositionAndRotation(source.position,source.rotation);go.transform.localScale=source.lossyScale;
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();
        var materials=new Material[Mathf.Max(1,slots)];for(int i=0;i<materials.Length;i++)materials[i]=ink;
        r.sharedMaterials=materials;r.shadowCastingMode=ShadowCastingMode.Off;
    }
    void Update()
    {
        float t=Mathf.Clamp01((Time.time-born)/lifetime);
        foreach(var r in visuals)if(r)CalligraphyGeometry.Tint(r,new Color(.003f,.006f,.009f),1-t,t);
        if(t>=1){gameObject.SetActive(false);Destroy(gameObject);}
    }
    void OnDestroy(){foreach(var m in owned)if(m)Destroy(m);}
}
