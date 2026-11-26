#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;

public static partial class InkboundTownBuilder
{
    static readonly List<GameObject> v3Plants=new();
    static Material darkCloth,petals,teaPaper,steamInk;
    static bool forceSimpleRoof;
    static readonly Dictionary<string,Material> latticeMaterials=new();
    static bool V3DetailedWindow(Transform parent,Vector3 position,float width,float height)
    {
        string name=height>2?"SM_JWD_Door01b":Mathf.Abs(position.x)>.2f?"SM_JWD_Door01c":"SM_JWD_Door01a";
        string path="Assets/Soswaewon/Meshes/Buildings/"+name+".fbx";
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!asset)return false;
        var source=asset.GetComponentInChildren<MeshFilter>();if(!source)return false;var bounds=source.sharedMesh.bounds;
        if(!latticeMaterials.TryGetValue(name,out var material)||!material)
        {
            material=V3Material("DetailedLattice_"+name,plaster,.92f);
            var original=asset.GetComponentInChildren<Renderer>().sharedMaterial;
            var texture=original.HasProperty("_MainTex")?original.GetTexture("_MainTex"):null;
            material.SetTexture("_BaseMap",texture?texture:original.GetTexture("Material_Texture2D_1"));material.SetFloat("_TextureWeight",1);
            material.SetFloat("_BrushWeight",.18f);material.SetFloat("_Pattern",4);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);latticeMaterials[name]=material;
        }
        var go=Object.Instantiate(asset,parent);go.name="Inset carved lattice and paper panel";
        Vector3 scale=new Vector3(.12f/Mathf.Max(.01f,bounds.size.x),height/bounds.size.y,width/bounds.size.z);
        var rotation=Quaternion.Euler(0,90,0);go.transform.localRotation=rotation;go.transform.localScale=scale;
        go.transform.localPosition=position-rotation*Vector3.Scale(bounds.center,scale);
        foreach(var renderer in go.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=material;
        foreach(var collider in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
        return true;
    }
    // Shader Color properties are converted by Unity in a Linear project; keep the authored sRGB values.
    static Color Display(float r,float g,float b)=>new Color(r,g,b);
    static void CreateV3Materials()
    {
        var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/VFX/InkboundTown/IT_V3_BrushAtlas.png");
        var tones=new[]{(paper,.957f),(plaster,.91f),(timber,.40f),(roof,.36f),(stone,.73f),(earth,.83f),
            (cloth,.79f),(foliage,.40f),(leafWash,.57f),(bankStone,.60f),(bark,.32f),(ink,.105f),(ceramic,.58f)};
        foreach(var pair in tones)
        {
            var m=pair.Item1;float t=pair.Item2;
            m.SetColor("_BaseColor",Display(t,t,t*.985f));m.SetColor("_PaperColor",Display(.957f,.949f,.918f));
            m.SetFloat("_Grain",.24f);m.SetFloat("_Wash",.10f);m.SetFloat("_Outline",.035f);
            m.SetFloat("_FadeNear",55);m.SetFloat("_FadeFar",285);
            int pattern=Mathf.RoundToInt(m.GetFloat("_Pattern"));
            m.SetTexture("_BrushAtlas",atlas);m.SetFloat("_BrushWeight",pattern==6?.12f:.64f);
            m.SetVector("_BrushPanel",pattern==1?new Vector4(.5f,.5f,0,0):pattern==2?Vector4.zero:pattern==3?new Vector4(.5f,0,0,0):new Vector4(0,.5f,0,0));
            EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
        }
        darkCloth=V3Material("FoldedPatternedLinen",cloth,.59f);
        petals=V3Material("PaperWhiteBlossoms",leafWash,.92f);
        teaPaper=V3Material("TeaInteriorPaper",plaster,.77f);
        steamInk=Special("V3_KitchenSteam","Project/InkboundTown/Returning Pigment");steamInk.SetFloat("_VertexTint",1);EditorUtility.SetDirty(steamInk);AssetDatabase.SaveAssetIfDirty(steamInk);
        var masks=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/VFX/InkboundTown/IT_V3_FluidMasks.png");
        plume.SetTexture("_FluidMask",masks);plume.SetFloat("_UseFluidMask",1);EditorUtility.SetDirty(plume);AssetDatabase.SaveAssetIfDirty(plume);
        river.SetColor("_BaseColor",Display(.925f,.922f,.90f));river.SetFloat("_ReflectionStrength",.82f);
        EditorUtility.SetDirty(river);AssetDatabase.SaveAssetIfDirty(river);
    }
    static Material V3Material(string name,Material source,float tone)
    {
        string path=Materials+"/M_IT_V3_"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(source);AssetDatabase.CreateAsset(m,path);}else EditorUtility.CopySerialized(source,m);
        m.name="M_IT_V3_"+name;m.SetColor("_BaseColor",Display(tone,tone,tone*.985f));
        EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return m;
    }
    static void CreateV3Prefabs()
    {
        v3Plants.Clear();
        for(int kind=0;kind<8;kind++)v3Plants.Add(V3Plant(kind,0));
        v3Plants.Add(V3Plant(0,1));v3Plants.Add(V3Plant(4,1));
        willowPrefab=v3Plants[0];pinePrefab=v3Plants[1];bambooPrefab=v3Plants[2];
        for(int type=0;type<8;type++)for(int variant=0;variant<2;variant++)housePrefabs.Add(V3Shop(type,variant));
        foreach(string n in new[]{"Merchant","VillagerWoman","Fisherman","Scholar","ChildGirl","Elder","Granny","ErrandBoy"})
            residentPrefabs[n]=BuildResident(n,n=="ChildGirl"?1.18f:n=="ErrandBoy"?1.42f:1.66f);
    }
    static void RoundTimber(Transform parent,Vector3 a,Vector3 b,float r=.065f,Material material=null)
    {
        var g=new BrushGeometry();g.Tube(a,Vector3.Lerp(a,b,.32f),Vector3.Lerp(a,b,.67f),b,r,r*.94f,3,8);
        Model("Rounded hand hewn timber",parent,g.Mesh("Rounded timber"),material?material:timber);
    }
    static void Board(Transform p,Vector3 at,Vector3 size,Material m=null)
    {
        float x=size.x*.5f,z=size.z*.5f,c=Mathf.Min(.022f,Mathf.Min(x,z)*.12f),bevel=Mathf.Min(.012f,size.y*.18f);
        Vector2[] edge={new(-x,-z+c),new(-x+c,-z),new(x-c,-z),new(x,-z+c),new(x,z-c),new(x-c,z),new(-x+c,z),new(-x,z-c)};
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int ring=0;ring<3;ring++)for(int k=0;k<8;k++)
        {
            Vector2 e=edge[k];if(ring!=1)e-=new Vector2(Mathf.Sign(e.x)*bevel,Mathf.Sign(e.y)*bevel);
            float y=ring==0?-size.y*.5f:ring==1?size.y*.5f-bevel:size.y*.5f;
            v.Add(at+new Vector3(e.x,y,e.y));uv.Add(new Vector2(e.x,e.y));
            if(ring<2){int q=ring*8+k,n=ring*8+(k+1)%8;tr.AddRange(new[]{q,n,q+8,n,n+8,q+8});}
        }
        v.Add(at+Vector3.up*size.y*.5f);uv.Add(Vector2.zero);
        for(int k=0;k<8;k++)tr.AddRange(new[]{24,16+(k+1)%8,16+k});
        var mesh=new Mesh{name="Timber board with small worn bevels"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();Model("Bevelled joinery board",p,mesh,m?m:timber);
    }
    static void Shelf(Transform p,Vector3 at,float w,float h,int count)
    {
        for(int s=-1;s<=1;s+=2)RoundTimber(p,at+new Vector3(s*w*.48f,0,0),at+new Vector3(s*w*.48f,h,0),.045f);
        for(int j=0;j<count;j++)Board(p,at+Vector3.up*(.12f+j*h/(count-1)),new Vector3(w,.075f,.48f));
    }
    static void Ring(Transform p,Vector3 at,float r,float thickness,Material m)
    {
        var g=new BrushGeometry();
        for(int j=0;j<8;j++)
        {
            float a=j*Mathf.PI/4,b=(j+1)*Mathf.PI/4;
            Vector3 x=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),y=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
            g.Tube(at+x*r,at+(x*.75f+y*.27f)*r,at+(x*.27f+y*.75f)*r,at+y*r,thickness,thickness,3,5);
        }
        Model("Hand bound circular rim",p,g.Mesh("Rim"),m);
    }
    static void Cup(Transform p,Vector3 at,float size=.065f)
    {V3Lathe(p,at,size,size*1.1f,new[]{.52f,.66f,.80f,.92f,1f},ceramic);Ring(p,at+Vector3.up*size*1.1f,size,.008f,paper);}
    static void V3Lathe(Transform p,Vector3 at,float radius,float height,float[] shape,Material m)
    {
        const int sides=32;var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int row=0;row<shape.Length;row++)for(int j=0;j<=sides;j++)
        {
            float a=j*Mathf.PI*2/sides;v.Add(at+new Vector3(Mathf.Cos(a)*radius*shape[row],row*height/(shape.Length-1),Mathf.Sin(a)*radius*shape[row]));uv.Add(new Vector2(j/(float)sides,row/(float)(shape.Length-1)));
            if(row<shape.Length-1&&j<sides){int q=row*(sides+1)+j;tr.AddRange(new[]{q,q+sides+1,q+1,q+1,q+sides+1,q+sides+2});}
        }
        var mesh=new Mesh{name="Smooth thrown vessel"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();Model("Open rounded vessel",p,mesh,m);
    }
    static void Steamer(Transform p,Vector3 at,float radius)
    {
        V3Lathe(p,at,radius,.155f,new[]{1f,1f,1f},cloth);
        foreach(float y in new[]{.012f,.145f})Ring(p,at+Vector3.up*y,radius,.014f,timber);
        for(int i=0;i<9;i++)
        {
            float x=(i-4)*radius*.22f,z=Mathf.Sqrt(Mathf.Max(0,radius*radius-x*x));
            RoundTimber(p,at+new Vector3(x,.153f,-z),at+new Vector3(x,.153f,z),.009f,cloth);
        }
    }
    static void TeaSet(Transform p,Vector3 at)
    {
        Board(p,at,new Vector3(.62f,.045f,.39f),cloth);Pot(p,at+Vector3.up*.025f,.12f,.17f,ceramic);
        var handle=Root("Upright teapot handle",p);handle.localPosition=at+new Vector3(.115f,.12f,0);handle.localRotation=Quaternion.Euler(90,0,0);Ring(handle,Vector3.zero,.055f,.014f,ceramic);
        V3Lathe(p,at+Vector3.up*.198f,.075f,.02f,new[]{1f,.87f},ceramic);Pot(p,at+Vector3.up*.218f,.017f,.02f,ceramic);
        RoundTimber(p,at+new Vector3(-.08f,.10f,0),at+new Vector3(-.21f,.16f,0),.028f,ceramic);
        Cup(p,at+new Vector3(-.23f,.025f,.10f));Cup(p,at+new Vector3(.23f,.025f,.1f));
    }
    static void V3Table(Transform p,Vector3 at,bool seats=true,bool tea=true)
    {
        Board(p,at+Vector3.up*.77f,new Vector3(1.7f,.105f,1.15f));
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
            RoundTimber(p,at+new Vector3(x*.65f,0,z*.4f),at+new Vector3(x*.65f,.76f,z*.4f),.055f);
        RoundTimber(p,at+new Vector3(-.65f,.22f,-.4f),at+new Vector3(.65f,.22f,-.4f),.035f);
        if(tea)TeaSet(p,at+Vector3.up*.84f);
        if(seats)foreach(int side in new[]{-1,1})
        {
            Board(p,at+new Vector3(0,.43f,side*1.13f),new Vector3(.68f,.085f,.52f));
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
                RoundTimber(p,at+new Vector3(x*.23f,0,side*1.13f+z*.17f),at+new Vector3(x*.23f,.4f,side*1.13f+z*.17f),.033f);
        }
    }
    static readonly string[] shopNames={"Cloth merchant","Tea pavilion","Wine inn","Herbal pharmacy","Steamer kitchen","Fishmonger","Ceramic workshop","Courtyard dwelling"};
    static GameObject V3Shop(int type,int variation)
    {
        bool upstairs=type==1||type==2||type==3||variation==1;
        float w=type==1?9:7.2f+variation*.45f,d=type==1?8:6.8f,h=upstairs?6.5f:3.7f;
        var r=Root("IT_V3_"+shopNames[type]+"_"+variation,null);
        // Wall segments form a real accessible room, not a solid white box behind a painted door.
        Box("Solid stone foundation",r,new Vector3(0,.09f,0),new Vector3(w,.18f,d),stone,true);
        Box("Rear plaster wall",r,new Vector3(0,h/2,d/2-.12f),new Vector3(w,h,.24f),plaster,true);
        foreach(int s in new[]{-1,1})
        {
            if(type==1)
            {
                Box("Tea low side wall",r,new Vector3(s*(w/2-.12f),.65f,0),new Vector3(.24f,1.3f,d),teaPaper,true);
                Box("Tea upper side wall",r,new Vector3(s*(w/2-.12f),(h+2.8f)*.5f,0),new Vector3(.24f,h-2.8f,d),plaster,true);
                for(int k=0;k<5;k++)
                {
                    float z=-d/2+k*d/4;Box("Window wall pier",r,new Vector3(s*(w/2-.12f),2,z),new Vector3(.24f,1.8f,.20f),teaPaper,true);
                    RoundTimber(r,new Vector3(s*(w/2-.17f),1.3f,z),new Vector3(s*(w/2-.17f),2.8f,z),.033f);
                }
                RoundTimber(r,new Vector3(s*(w/2-.17f),1.35f,-d/2),new Vector3(s*(w/2-.17f),1.35f,d/2),.045f);
                RoundTimber(r,new Vector3(s*(w/2-.17f),2.75f,-d/2),new Vector3(s*(w/2-.17f),2.75f,d/2),.045f);
            }
            else Box("Side lime wall",r,new Vector3(s*(w/2-.12f),h/2,0),new Vector3(.24f,h,d),plaster,true);
            RoundTimber(r,new Vector3(s*(w/2-.28f),.18f,-d/2+.2f),new Vector3(s*(w/2-.28f),h,-d/2+.2f),.115f);
            for(int j=0;j<3;j++)Board(r,new Vector3(s*(w/2-.27f),.35f+j*.16f,-d/2),new Vector3(.32f,.13f,.45f),bankStone);
        }
        for(int i=0;i<3;i++)
        {
            float x=(i-1)*w*.34f;
            RoundTimber(r,new Vector3(x,.18f,-d/2-1.25f),new Vector3(x,3.05f,-d/2-1.25f),.095f);
            RoundTimber(r,new Vector3(x,2.55f,-d/2-1.25f),new Vector3(x,3.15f,-d/2+.05f),.055f);
            var column=Root("Veranda column collision",r);column.localPosition=new Vector3(x,1.55f,-d/2-1.25f);
            var capsule=column.gameObject.AddComponent<CapsuleCollider>();capsule.height=3.05f;capsule.radius=.095f;
        }
        RoundTimber(r,new Vector3(-w/2,2.95f,-d/2-.1f),new Vector3(w/2,2.95f,-d/2-.1f),.11f);
        Roof(r,w+1.15f,d+1.35f,h,1.45f);
        for(int s=-1;s<=1;s+=2)
        {
            var gable=new Mesh{name="Solid limewashed gable"};
            gable.vertices=new[]{new Vector3(s*w/2,h,-d/2),new Vector3(s*w/2,h,d/2),new Vector3(s*w/2,h+1.45f,0)};
            gable.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};gable.triangles=new[]{0,1,2};gable.RecalculateNormals();Model("Painted gable",r,gable,plaster);
        }
        // Separate overhang uses its own local frame so it extends into the street.
        var overhang=Root("Street veranda roof",r);overhang.localPosition=new Vector3(0,0,-d/2-.55f);
        Roof(overhang,w+.6f,2.6f,3.0f,.52f);
        for(int side=-1;side<=1;side+=2)Lantern(r,new Vector3(side*w*.39f,2.55f,-d/2-1.16f),.22f);
        if(upstairs)
        {
            Box("Upper storey floor",r,new Vector3(0,3.35f,0),new Vector3(w-.4f,.15f,d-.4f),timber,true);
            Box("Upper facade plaster",r,new Vector3(0,4.93f,-d/2),new Vector3(w,3.1f,.2f),plaster,true);
            for(int x=-1;x<=1;x++)Window(r,new Vector3(x*w*.3f,4.95f,-d/2-.13f),1.5f,1.65f);
            if(type==1||type==2)
            {
                Board(r,new Vector3(0,3.39f,-d/2-.72f),new Vector3(w,.16f,1.5f));
                for(int j=0;j<14;j++)RoundTimber(r,new Vector3(-w*.47f+j*w*.072f,3.5f,-d/2-1.36f),new Vector3(-w*.47f+j*w*.072f,4.32f,-d/2-1.36f),.026f);
                RoundTimber(r,new Vector3(-w/2,4.35f,-d/2-1.36f),new Vector3(w/2,4.35f,-d/2-1.36f),.055f);
            }
        }
        // One sheltered lattice wall leaves a generous central entry.
        Window(r,new Vector3(-w*.32f,1.65f,-d*.5f+.08f),1.45f,1.45f);
        for(int q=0;q<5;q++)Board(r,new Vector3(-w*.32f+q*.24f,.63f,-d*.5f),new Vector3(.22f,1.05f,.10f));
        V3ShopGoods(r,type,w,d);
        if(type==0||type==1||type==2)V3Vine(r,new Vector3(-w*.5f+.25f,0,-d*.5f-1.28f),3.0f);
        var roofProxy=Root("Roof and camera collision",r);roofProxy.localPosition=new Vector3(0,h+.65f,0);
        roofProxy.gameObject.AddComponent<BoxCollider>().size=new Vector3(w+1.05f,1.3f,d+1.25f);
        var eaveProxy=Root("Veranda ceiling collision",r);eaveProxy.localPosition=new Vector3(0,3.17f,-d/2-.55f);
        eaveProxy.gameObject.AddComponent<BoxCollider>().size=new Vector3(w+.5f,.29f,2.55f);
        if(type==7){var closed=Root("Closed dwelling facade collision",r);closed.localPosition=new Vector3(0,1.65f,-d/2);closed.gameObject.AddComponent<BoxCollider>().size=new Vector3(w,3.3f,.18f);}
        if(type==1)
        {
            for(int j=0;j<2;j++)
            {
                var painting=Root("Unlettered wall scroll",r);painting.localPosition=new Vector3(j==0?-2.6f:2.6f,2,d/2-.15f);
                Board(painting,Vector3.zero,new Vector3(.85f,1.15f,.025f),cloth);
                var bamboo=new BrushGeometry();for(int k=0;k<3;k++)
                {Vector3 a=new Vector3(-.24f+k*.2f,-.48f,-.025f),b=a+new Vector3(.12f,.9f,0);bamboo.Tube(a,Vector3.Lerp(a,b,.3f),Vector3.Lerp(a,b,.7f),b,.009f,.003f,6,5);for(int l=0;l<5;l++){Vector3 p=Vector3.Lerp(a,b,.2f+l*.15f);bamboo.Lance(p,p+new Vector3(l%2==0?.15f:-.13f,.08f,-.004f),.022f,l);}}
                Model("Ink bamboo wall painting",painting,bamboo.Mesh("Bamboo painting"),bankStone);
            }
        }
        // Decorative wall bases transition through stone, timber, paper and plaster.
        for(int i=0;i<7;i++)Board(r,new Vector3(-w*.43f+i*w*.14f,.23f,d/2+.035f),new Vector3(w*.135f,.3f,.22f),bankStone);
        Combine(r,"V3_"+type+"_"+variation);
        InstallHouseLOD(r,w,h,d,type,variation);
        return SavePrefab(r,Prefabs+"/PF_IT_V3_Shop"+type+"_"+variation+".prefab");
    }
    static void InstallHouseLOD(Transform r,float w,float h,float d,int type,int variation)
    {
        var close=r.GetComponentsInChildren<Renderer>();var distant=Root("Distant simplified architecture",r);
        Box("Distant wall silhouette",distant,new Vector3(0,h*.5f,0),new Vector3(w,h,d),plaster);
        forceSimpleRoof=true;
        try{Roof(distant,w+1.15f,d+1.35f,h,1.45f);}
        finally{forceSimpleRoof=false;}
        for(int j=-1;j<=1;j++)
        {
            Box("Distant timber inset",distant,new Vector3(j*w*.31f,1.6f,-d/2-.025f),new Vector3(1.25f,1.6f,.04f),teaPaper);
            if(h>5)Box("Distant upper inset",distant,new Vector3(j*w*.31f,4.8f,-d/2-.025f),new Vector3(1.25f,1.4f,.04f),teaPaper);
        }
        Combine(distant,"V3_House_LOD_"+type+"_"+variation);
        var lod=r.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.105f,close),new LOD(.014f,distant.GetComponentsInChildren<Renderer>())});lod.RecalculateBounds();
    }
    static void V3ShopGoods(Transform r,int type,float w,float d)
    {
        float front=-d/2-.55f;
        if(type==0)
        {
            Shelf(r,new Vector3(1.6f,.18f,1.8f),2.4f,2.05f,4);
            for(int j=0;j<12;j++)Board(r,new Vector3(.82f+(j%3)*.69f,.38f+(j/3)*.62f,1.8f),new Vector3(.61f,.14f,.42f),j%2==0?cloth:darkCloth);
            var rack=Root("Draped fabric rack",r);rack.localPosition=new Vector3(1.65f,0,front);
            for(int s=-1;s<=1;s+=2)RoundTimber(rack,new Vector3(s*1.15f,0,.4f),new Vector3(s*1.15f,2.45f,.1f),.05f);
            RoundTimber(rack,new Vector3(-1.27f,2.4f,0),new Vector3(1.27f,2.4f,0),.05f);
            for(int j=0;j<4;j++)
            {
                HangingCloth(rack,new Vector3(-.84f+j*.56f,2.4f,0),.48f,1.65f+j%2*.30f);
                var roll=Root("Rolled patterned bolt",r);roll.localPosition=new Vector3(-1.2f+j*.51f,.95f,front-.1f);roll.localRotation=Quaternion.Euler(0,0,90);
                V3Lathe(roll,Vector3.zero,.105f,.42f,new[]{.94f,1f,1f,1f,.94f},j%2==0?cloth:darkCloth);Ring(roll,new Vector3(0,.425f,0),.065f,.009f,timber);Ring(roll,new Vector3(0,.428f,0),.035f,.007f,cloth);
            }
            Board(r,new Vector3(-.5f,.82f,front+.4f),new Vector3(2.5f,.10f,1));
        }
        else if(type==1)
        {
            for(int x=-1;x<=1;x+=2)for(int z=0;z<2;z++)V3Table(r,new Vector3(x*2.55f,.18f,-1.1f+z*3));
            Shelf(r,new Vector3(0,.18f,d/2-.7f),4,2.35f,4);
            for(int j=0;j<12;j++)Pot(r,new Vector3(-1.5f+j%4, .42f+(j/4)*.69f,d/2-.7f),.16f,.25f,ceramic);
            Board(r,new Vector3(0,.9f,2),new Vector3(3.6f,.12f,.78f));TeaSet(r,new Vector3(.5f,.99f,2));
            HangingCloth(r,new Vector3(-2.8f,2.85f,2.9f),1.2f,.75f);
            for(int j=0;j<18;j++)RoundTimber(r,new Vector3(-3.4f,2.8f-j*.04f,2.92f),new Vector3(-2.2f,2.8f-j*.04f,2.92f),.012f,cloth);
            var screen=Root("Interior lattice screen",r);screen.localPosition=new Vector3(0,0,.5f);screen.localRotation=Quaternion.Euler(0,90,0);Window(screen,new Vector3(0,1.6f,0),1.2f,2.5f);
        }
        else if(type==2)
        {
            Shelf(r,new Vector3(1.7f,.18f,1.65f),2.2f,2.4f,3);
            for(int j=0;j<9;j++)
            {
                Vector3 p=new Vector3(-2.5f+(j%3)*.71f,.18f+(j/6)*.72f,front+.25f+(j/3%2)*.68f);
                Pot(r,p,.29f,.65f,ceramic);Board(r,p+Vector3.up*.66f,new Vector3(.34f,.045f,.34f),darkCloth);Ring(r,p+Vector3.up*.5f,.19f,.018f,cloth);
            }
            V3Table(r,new Vector3(1.6f,.18f,-.2f));
        }
        else if(type==3)
        {
            Board(r,new Vector3(.7f,1,front+.8f),new Vector3(3.3f,.13f,.8f));
            for(int row=0;row<6;row++)for(int col=0;col<7;col++)
            {
                Vector3 p=new Vector3(-2.5f+col*.58f,.5f+row*.34f,d/2-.25f);
                Board(r,p,new Vector3(.54f,.30f,.18f));RoundTimber(r,p+new Vector3(-.045f,0,-.12f),p+new Vector3(.045f,0,-.12f),.022f,ceramic);
            }
            for(int j=0;j<5;j++)Pot(r,new Vector3(-.7f+j*.57f,1.08f,front+.8f),.16f,.3f,ceramic);
            for(int j=0;j<8;j++)
            {
                var herb=new BrushGeometry();Vector3 p=new Vector3(-2.6f+j*.7f,2.8f,-d/2);
                for(int k=0;k<9;k++)herb.Lance(p,p+new Vector3(Range(-.2f,.2f),-Range(.3f,.6f),Range(-.15f,.15f)),.035f,k+j);
                Model("Tied drying herb bundle",r,herb.Mesh("Herbs"),leafWash);
            }
        }
        else if(type==4)
        {
            ClothCanopy(r,new Vector3(.2f,2.75f,front-.2f),w*.84f,2.4f);
            Board(r,new Vector3(.3f,.85f,front),new Vector3(4.4f,.12f,1.1f));
            for(int s=-1;s<=1;s+=2)RoundTimber(r,new Vector3(s*1.9f,0,front),new Vector3(s*1.9f,.83f,front),.09f);
            for(int j=0;j<3;j++)for(int k=0;k<j+2;k++)
            {Vector3 p=new Vector3(-1.3f+j*1.25f,.94f+k*.16f,front);Steamer(r,p,.36f);}
            Board(r,new Vector3(1.9f,.97f,front+.2f),new Vector3(.55f,.05f,.42f),cloth);
            RoundTimber(r,new Vector3(1.7f,1.04f,front+.2f),new Vector3(2.08f,1.04f,front+.2f),.022f,ink);
            Pot(r,new Vector3(-2.3f,.18f,0),.5f,.65f,bankStone);Ring(r,new Vector3(-2.3f,.86f,0),.34f,.07f,ink);
            for(int j=0;j<6;j++)Cup(r,new Vector3(1.4f+(j%3)*.22f,.94f+(j/3)*.08f,front-.26f),.09f);
            Basket(r,new Vector3(-2.6f,.18f,front),.38f,.62f,true);
        }
        else if(type==5)
        {
            for(int j=0;j<3;j++)
            {
                Vector3 p=new Vector3(-2+j*1.4f,.18f,front);Pot(r,p,.52f,.38f,cloth);Ring(r,p+Vector3.up*.39f,.35f,.035f,timber);
                for(int k=0;k<4;k++){var fish=new BrushGeometry();Vector3 a=p+new Vector3(-.18f+k*.11f,.35f,-.15f);fish.Lance(a,a+Vector3.forward*.32f,.055f,k);Model("Fish in shallow basin",r,fish.Mesh("Fish"),bankStone);}
            }
            V3Net(r,new Vector3(1.4f,2.4f,1.6f),2.4f,1.7f);
            Basket(r,new Vector3(-2.2f,.18f,.5f),.42f,.65f);RoundTimber(r,new Vector3(2.8f,.18f,-2),new Vector3(2.1f,3.5f,0),.02f);
        }
        else if(type==6)
        {
            Shelf(r,new Vector3(1.7f,.18f,1.6f),2.5f,2.35f,4);
            for(int j=0;j<16;j++)Pot(r,new Vector3(.9f+j%4*.55f,.34f+j/4*.69f,1.6f),.10f+(j%3)*.055f,.22f+(j%4)*.07f,j%3==0?stone:ceramic);
            for(int j=0;j<6;j++)Pot(r,new Vector3(-2.7f+j*.55f,.18f,front),.22f+(j%3)*.09f,.4f+(j%2)*.35f,ceramic);
        }
        else
        {
            for(int side=-1;side<=1;side+=2)Board(r,new Vector3(side*.65f,1.46f,-d/2),new Vector3(1.24f,2.6f,.15f));
            for(int j=0;j<4;j++)RoundTimber(r,new Vector3(2,.18f+j*.18f,front+.3f),new Vector3(2.5f,.2f+j*.18f,front+.4f),.095f);
            Basket(r,new Vector3(-2.6f,.18f,front),.35f,.65f);HangingCloth(r,new Vector3(2.1f,2.6f,-.6f),.9f,1.1f);
        }
    }
    static void V3Net(Transform p,Vector3 at,float w,float h)
    {
        var g=new BrushGeometry();
        for(int i=0;i<=12;i++)for(int side=-1;side<=1;side+=2)
        {
            Vector3 a=at+new Vector3(-w/2+i*w/12,0,0),b=a+new Vector3(side*.6f,-h,.06f);
            g.Tube(a,a+Vector3.down*h*.3f,b+Vector3.up*h*.3f,b,.007f,.005f,5,4);
        }
        Model("Hanging fine knotted fishing net",p,g.Mesh("Net"),timber);
    }
    static void V3Vine(Transform p,Vector3 at,float height)
    {
        var stems=new BrushGeometry();var leaves=new BrushGeometry();
        for(int branch=0;branch<3;branch++)
        {
            Vector3 a=at+new Vector3(branch*.04f,0,0),b=a+new Vector3(.06f,height*.35f,.08f),c=a+new Vector3(-.1f,height*.72f,-.08f),d=a+new Vector3(.4f+branch*.28f,height,0);
            stems.Tube(a,b,c,d,.016f,.003f,18,5);
            for(int j=1;j<28;j++)
            {Vector3 q=Bezier(a,b,c,d,j/28f);float side=j%2==0?-1:1;Vector3 end=q+new Vector3(side*Range(.15f,.28f),Range(-.07f,.10f),Range(-.14f,.14f));leaves.Lance(q,end,.075f,j+branch);}
        }
        Model("Fine climbing vine stems",p,stems.Mesh("Vine stems"),bark);Model("Sparse vine leaves",p,leaves.Mesh("Vine foliage"),leafWash);
    }
}
#endif
