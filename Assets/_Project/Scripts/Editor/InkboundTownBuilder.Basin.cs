#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static partial class InkboundTownBuilder
{
    const string BasinName="Irregular mountain basin around the town";
    static readonly Vector3 BasinCenter=new(0,0,12);

    [MenuItem("GeminiGrowUp/Inkbound Town V3/Update surrounding mountain basin")]
    public static void UpdateSurroundingBasin()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=ScenePath)throw new InvalidOperationException("Open InkboundTown_Exploration first.");
        var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();
        var parent=settings.transform.Find("03 Willow banks and painted mountains");
        if(!parent)throw new InvalidOperationException("Town scenery group is missing.");
        BuildSurroundingBasin(parent);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
    static void BuildSurroundingBasin(Transform parent)
    {
        foreach(Transform child in parent.Cast<Transform>().ToArray())
            if(child.name==BasinName||child.name.StartsWith("V3 brush ridge "))Object.DestroyImmediate(child.gameObject);
        var root=Root(BasinName,parent);
        var ground=AssetDatabase.LoadAssetAtPath<Material>(Materials+"/M_IT_QuietEarthenCourts.mat");
        var water=AssetDatabase.LoadAssetAtPath<Material>(Materials+"/M_IT_River.mat");
        var source=AssetDatabase.LoadAssetAtPath<Material>(Materials+"/M_IT_QuayLayeredStone.mat");
        if(!ground||!water||!source)throw new InvalidOperationException("Missing existing town landscape materials.");
        foreach(int side in new[]{-1,1})
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
            const int rows=144,columns=28;
            for(int row=0;row<=rows;row++)
            {
                float t=row/(float)rows,z=12+(t*2-1)*178;
                float halfWidth=Mathf.Sqrt(Mathf.Max(0,1-Mathf.Pow(t*2-1,2)))*153;
                halfWidth*=1+.065f*Mathf.Sin(t*13+side*.8f)+.035f*Mathf.Sin(t*29+side*2);
                float bank=RiverCenter(z)+side*6.25f,outer=side*Mathf.Max(7,halfWidth);
                // Fill below the saved streets to bury their former rectangular outline.
                for(int col=0;col<=columns;col++)
                {
                    float x=Mathf.Lerp(bank,outer,col/(float)columns);
                    v.Add(new Vector3(x,-.035f,z));uv.Add(new Vector2(x,z));
                    if(row<rows&&col<columns)
                    {int q=row*(columns+1)+col;tr.AddRange(side>0?new[]{q,q+columns+1,q+1,q+1,q+columns+1,q+columns+2}:new[]{q,q+1,q+columns+1,q+1,q+columns+2,q+columns+1});}
                }
            }
            BasinModel(root,side<0?"West curved valley floor":"East curved valley floor",v,uv,tr,ground);
        }
        // Three overlapping, fully three-dimensional ridges surround every bearing. Their
        // variable radii and off-centre crests avoid a regular circle or a straight backdrop.
        for(int layer=0;layer<3;layer++)
        {
            var material=BasinMaterial(source,layer);
            var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
            const int segments=384,bands=40;
            for(int band=0;band<=bands;band++)for(int segment=0;segment<=segments;segment++)
            {
                float a=(segment==segments?0:segment)*Mathf.PI*2/segments,t=band/(float)bands;
                float warp=1+.057f*Mathf.Sin(a*3+.73f)+.032f*Mathf.Sin(a*7+1.8f)+.022f*Mathf.Cos(a*11+layer);
                float innerX=new[]{122f,174,237}[layer],innerZ=new[]{145f,201,268}[layer];
                float width=new[]{67f,78,96}[layer]*(1+.15f*Mathf.Sin(a*5+layer*2));
                // Periodic angular detail keeps the seam closed as well as the silhouette.
                float crest=.44f+.1f*Mathf.Sin(a*4+layer)+.06f*Mathf.Cos(a*7-layer);
                float cross=t<crest?t/crest:(1-t)/(1-crest);
                float shape=Mathf.Pow(Mathf.Clamp01(cross),1.08f+.24f*Mathf.Sin(a*3+layer));
                float height=RidgeHeight(a,layer);
                float x=Mathf.Sin(a)*(innerX+width*t)*warp,z=12+Mathf.Cos(a)*(innerZ+width*t)*warp;
                float rock=(Mathf.PerlinNoise(x*.085f+layer*13,z*.085f+7)-.5f)*13+
                    (Mathf.PerlinNoise(x*.24f+31,z*.24f+layer*19)-.5f)*4;
                float y=-.12f+Mathf.Max(0,height*shape+rock*shape*(.35f+.65f*shape));
                // The river enters/exits through low, irregular saddles in the nearest range.
                if(layer==0)y=Mathf.Lerp(y,-1.6f,(1-Mathf.SmoothStep(5.9f,22,Mathf.Abs(x-RiverCenter(z)))));
                v.Add(new Vector3(x,y,z));uv.Add(new Vector2(segment/(float)segments,shape));
                if(band<bands&&segment<segments)
                {int q=band*(segments+1)+segment;tr.AddRange(new[]{q,q+segments+1,q+1,q+1,q+segments+1,q+segments+2});}
            }
            BasinModel(root,new[]{"Near folded mountain rim","Middle broken mountain rim","Distant pale mountain rim"}[layer],v,uv,tr,material);
        }
        var waterV=new List<Vector3>();var waterUV=new List<Vector2>();var waterTr=new List<int>();
        for(int i=0;i<=144;i++)
        {
            float z=-169+i*2.5f,x=RiverCenter(z);
            waterV.Add(new Vector3(x-6.3f,-1.256f,z));waterV.Add(new Vector3(x+6.3f,-1.256f,z));
            waterUV.Add(new Vector2(0,z/3.05f));waterUV.Add(new Vector2(1,z/3.05f));
            if(i<144){int q=i*2;waterTr.AddRange(new[]{q,q+1,q+2,q+1,q+3,q+2});}
        }
        var extension=BasinModel(root,"River continuing into the mountain valleys",waterV,waterUV,waterTr,water);extension.layer=4;
    }
    static float RidgeHeight(float a,int layer)
    {
        float value=13+layer*11;
        int peaks=11+layer*2;
        for(int p=0;p<peaks;p++)
        {
            float angle=(p+.18f*Mathf.Sin(p*3.17f+layer))*Mathf.PI*2/peaks+layer*.29f;
            float d=Mathf.DeltaAngle(a*Mathf.Rad2Deg,angle*Mathf.Rad2Deg)*Mathf.Deg2Rad;
            float spread=.105f+.075f*(.5f+.5f*Mathf.Sin(p*4.2f+layer));
            float height=20+layer*9+(Mathf.Sin(p*5.71f+layer*1.3f)*.5f+.5f)*27;
            value+=Mathf.Exp(-Mathf.Pow(Mathf.Abs(d)/spread,1.4f)*.65f)*height;
        }
        float broken=.87f+Mathf.PerlinNoise(Mathf.Cos(a)*11+21,Mathf.Sin(a)*11+layer*17+21)*.25f;
        return value*(.94f+.11f*Mathf.Sin(a*5+layer))*broken;
    }
    static Material BasinMaterial(Material source,int layer)
    {
        string path=Materials+"/M_IT_BasinRidge"+layer+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(source);AssetDatabase.CreateAsset(material,path);}
        float tone=new[]{.56f,.71f,.83f}[layer];
        material.SetColor("_BaseColor",new Color(tone,tone,tone*.985f));material.SetColor("_PaperColor",new Color(.957f,.949f,.918f));material.SetFloat("_Pattern",5);
        material.SetFloat("_FadeNear",145+layer*30);material.SetFloat("_FadeFar",690);
        material.SetFloat("_Outline",.065f);material.SetFloat("_BrushWeight",.18f);material.SetFloat("_Grain",.65f);material.SetFloat("_Wash",.2f);
        material.SetFloat("_ContactInk",0);material.SetFloat("_HeightMist",.72f);
        material.SetVector("_BrushPanel",new Vector4(0,.5f,0,0));material.SetFloat("_MountainWorldSpace",0);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
    }
    static GameObject BasinModel(Transform parent,string label,List<Vector3> v,List<Vector2> uv,List<int> triangles,Material material)
    {
        var mesh=new Mesh{name=label,indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Meshes+"/IT_Basin_"+label.Replace(' ','_')+".asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved)
        {saved.Clear(false);saved.indexFormat=mesh.indexFormat;saved.vertices=mesh.vertices;saved.normals=mesh.normals;saved.uv=mesh.uv;saved.triangles=mesh.triangles;saved.RecalculateBounds();saved.UploadMeshData(false);Object.DestroyImmediate(mesh);mesh=saved;EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}
        else AssetDatabase.CreateAsset(mesh,path);
        var go=Model(label,parent,mesh,material);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;return go;
    }
}
#endif
