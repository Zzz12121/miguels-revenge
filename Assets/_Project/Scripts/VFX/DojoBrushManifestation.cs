using System.Collections.Generic;
using UnityEngine;

/// <summary>Reassembles the real brush outward from the grip, retaining its original meshes and textures.</summary>
public sealed class DojoBrushManifestation : MonoBehaviour
{
    public float Progress {get;private set;}
    public float Reach {get;private set;}
    Renderer[] renderers;Material[][] originals;readonly List<Material> owned=new();
    MaterialPropertyBlock block;bool restored;
    public static DojoBrushManifestation Begin(Transform brush,Material template)
    {
        var effect=brush.gameObject.AddComponent<DojoBrushManifestation>();effect.Initialize(template);return effect;
    }
    void Initialize(Material template)
    {
        block=new MaterialPropertyBlock();
        renderers=GetComponentsInChildren<Renderer>();originals=new Material[renderers.Length][];Reach=.1f;
        for(int i=0;i<renderers.Length;i++)
        {
            var renderer=renderers[i];originals[i]=renderer.sharedMaterials;
            var b=renderer.bounds;
            for(int corner=0;corner<8;corner++)
            {
                var p=b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                Reach=Mathf.Max(Reach,Mathf.Abs(transform.InverseTransformPoint(p).z));
            }
            var replacements=new Material[originals[i].Length];
            for(int j=0;j<replacements.Length;j++)
            {
                var source=originals[i][j];var m=template?new Material(template):new Material(Shader.Find("Project/Dojo Brush Ink Assembly"));m.name="Owned grip-outward brush assembly";
                if(source)
                {
                    foreach(string texture in new[]{"_BaseMap","_MainTex"})if(source.HasProperty(texture)&&source.GetTexture(texture)){m.SetTexture("_BaseMap",source.GetTexture(texture));m.SetTextureScale("_BaseMap",source.GetTextureScale(texture));m.SetTextureOffset("_BaseMap",source.GetTextureOffset(texture));break;}
                    foreach(string color in new[]{"_BaseColor","_Color"})if(source.HasProperty(color)){m.SetColor("_BaseColor",source.GetColor(color));break;}
                }
                replacements[j]=m;owned.Add(m);
            }
            renderer.sharedMaterials=replacements;
        }
        SetProgress(0);
    }
    public void SetProgress(float progress)
    {
        Progress=Mathf.Clamp01(progress);
        foreach(var renderer in renderers)if(renderer)
        {
            renderer.GetPropertyBlock(block);block.SetFloat("_Assembly",Progress);block.SetFloat("_Reach",Reach);block.SetMatrix("_BrushWorldToLocal",transform.worldToLocalMatrix);renderer.SetPropertyBlock(block);
        }
    }
    public void Complete(){Restore();Destroy(this);}
    void Restore()
    {
        if(restored)return;restored=true;
        if(renderers!=null&&originals!=null)for(int i=0;i<renderers.Length&&i<originals.Length;i++)if(renderers[i]&&originals[i]!=null){renderers[i].sharedMaterials=originals[i];renderers[i].SetPropertyBlock(null);}
        foreach(var material in owned)if(material)Destroy(material);owned.Clear();
    }
    void OnDestroy()=>Restore();
}
