#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static partial class InkboundTownBuilder
{
    static readonly string[] species={"Willow","LayeredPine","JointedBamboo","FloweringPlum","OldCamphor","SplitBanana","ReedGrass","NotchedLotus"};
    static GameObject V3Plant(int kind,int variation)
    {
        if(kind==4||(kind==0&&variation==1))return V3ImportedTree(kind,variation);
        var r=Root("IT_V3_"+species[kind]+"_"+variation,null);
        r.gameObject.AddComponent<InkboundPassableGroup>();
        var wood=new BrushGeometry();var leaves=new BrushGeometry();var light=new BrushGeometry();var flowers=new BrushGeometry();
        if(kind==0||kind==1||kind==3||kind==4)
        {
            float h=kind==3?4.5f:kind==4?8:7.5f;
            Vector3 bend=new Vector3(kind==1?1.5f:-.65f,h*.35f,.35f),fork=new Vector3(.45f,h*.63f,-.2f),top=new Vector3(-.1f,h,0);
            wood.Tube(Vector3.zero,bend,fork,top,kind==3?.22f:.55f,.055f,23,10);
            for(int j=0;j<5;j++)
            {
                float a=j*1.3f+Range(-.4f,.4f);Vector3 edge=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                wood.Tube(edge*Range(.7f,1.3f),edge*.5f+Vector3.up*.15f,Vector3.up*.5f,Vector3.up*1.2f,.025f,.22f,7,7);
            }
            int arms=kind==3?5:kind==4?6:kind==0?5:7;
            float offset=Range(-1,1);
            for(int arm=0;arm<arms;arm++)
            {
                float angle=offset+arm*2.41f+Range(-.5f,.5f);
                Vector3 dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                float t=kind==1?.35f+arm*.07f:Range(.35f,.79f);
                Vector3 origin=Bezier(Vector3.zero,bend,fork,top,t);
                float reach=kind==3?Range(1.5f,2.9f):kind==4?Range(2.8f,4.5f):Range(2.4f,4.0f);
                Vector3 middle=origin+dir*reach*.55f+Vector3.up*Range(.35f,1.7f),end=origin+dir*reach+Vector3.up*Range(.25f,1.9f);
                wood.Tube(origin,origin+Vector3.up*.45f+dir*.25f,middle,end,kind==3?.105f:Range(.13f,.23f),.035f,13,8);
                for(int sub=0;sub<(kind==3?4:5);sub++)
                {
                    float along=Range(.35f,.98f);
                    Vector3 a=Bezier(origin,origin+Vector3.up*.45f+dir*.25f,middle,end,along);
                    Vector3 side=Quaternion.Euler(0,Range(-75,75),0)*dir;
                    Vector3 b=a+side*Range(.7f,1.6f)+Vector3.up*Range(-.1f,.8f);
                    wood.Tube(a,a+side*.3f+Vector3.up*.35f,b+Vector3.up*.15f,b,.035f,.006f,8,6);
                    if(kind==0)
                    {
                        // Multiple uneven curtains branch from the actual limb, not a radial palm crown.
                        for(int tail=0;tail<Range(3,6);tail++)
                        {
                            Vector3 from=Vector3.Lerp(a,b,Range(.25f,1))+new Vector3(Range(-.28f,.28f),Range(-.15f,.15f),Range(-.26f,.26f));
                            float drop=Range(1.15f,3.4f);Vector3 to=from+side*Range(.1f,.6f)-Vector3.up*drop;
                            Vector3 c1=from+side*.35f,c2=to+Vector3.up*drop*.42f;
                            wood.Tube(from,c1,c2,to,.012f,.0017f,12,5);
                            int count=(int)(drop*Range(9,15));
                            for(int k=1;k<count;k++)
                            {
                                if(rng.NextDouble()<.17)continue;
                                Vector3 p=Bezier(from,c1,c2,to,k/(float)count);
                                Vector3 tip=p-Vector3.up*Range(.22f,.42f)+new Vector3(Range(-.17f,.17f),0,Range(-.16f,.16f));
                                (rng.NextDouble()<.30?leaves:light).Lance(p,tip,Range(.022f,.045f),Range(0,70));
                            }
                        }
                    }
                    else if(kind==1)
                    {
                        // Separate flattened, irregular clouds leave open gaps between boughs.
                        for(int clump=0;clump<4;clump++)
                        {
                            Vector3 center=Vector3.Lerp(a,b,(clump+.5f)/4)+side*.2f;
                            for(int k=0;k<35;k++)
                            {
                                Vector3 p=center+new Vector3(Range(-.55f,.55f),Range(-.04f,.16f),Range(-.5f,.5f));
                                (k%4==0?light:leaves).Lance(p,p+new Vector3(Range(-.16f,.16f),Range(.14f,.31f),Range(-.16f,.16f)),Range(.017f,.035f),k+sub);
                            }
                        }
                    }
                    else if(kind==3)
                    {
                        for(int twig=0;twig<3;twig++)
                        {
                            Vector3 start=Vector3.Lerp(a,b,Range(.35f,1)),tip=start+side*Range(.3f,.7f)+Vector3.up*Range(.12f,.5f);
                            wood.Tube(start,start+side*.2f,tip,tip,.012f,.002f,6,5);
                            for(int k=0;k<5;k++)
                            {
                                Vector3 p=Vector3.Lerp(start,tip,Range(.2f,1));
                                Vector3 normal=new Vector3(Range(-1,1),Range(-.25f,1),Range(-1,1)).normalized;
                                Vector3 u=Vector3.Cross(normal,Vector3.up).normalized;if(u.sqrMagnitude<.1f)u=Vector3.right;
                                Vector3 v=Vector3.Cross(normal,u).normalized;
                                for(int petal=0;petal<5;petal++)
                                {float f=petal*Mathf.PI*.4f;flowers.Lance(p,p+(u*Mathf.Cos(f)+v*Mathf.Sin(f))*.115f+normal*.025f,.058f,f);}
                                wood.Tube(p-normal*.009f,p,p,p+normal*.026f,.018f,.012f,2,6);
                            }
                        }
                    }
                    else
                    {
                        for(int k=0;k<145;k++)
                        {
                            Vector3 spread=new Vector3(Range(-1.15f,1.15f),Range(-.35f,.65f),Range(-1.1f,1.1f));
                            if(spread.sqrMagnitude>1.9f)continue;
                            Vector3 p=b+spread;
                            (k%3==0?light:leaves).Lance(p,p+new Vector3(Range(-.30f,.30f),Range(.05f,.3f),Range(-.3f,.3f)),Range(.07f,.12f),Range(0,80));
                        }
                    }
                }
            }
            var c=r.gameObject.AddComponent<CapsuleCollider>();c.center=new Vector3(0,1.6f,0);c.height=3.2f;c.radius=kind==3?.18f:.39f;
        }
        else if(kind==2)
        {
            for(int stem=0;stem<7;stem++)
            {
                Vector3 a=new Vector3(Range(-.8f,.8f),0,Range(-.65f,.65f)),end=a+new Vector3(Range(-.7f,.7f),Range(3.4f,5.5f),Range(-.4f,.4f));
                wood.Tube(a,Vector3.Lerp(a,end,.3f),Vector3.Lerp(a,end,.7f),end,.045f,.016f,13,7);
                for(int node=1;node<9;node++)
                {
                    Vector3 p=Vector3.Lerp(a,end,node/9f);float h=.018f;
                    wood.Tube(p-Vector3.up*h,p,p,p+Vector3.up*h,.055f,.052f,2,7);
                    if(node<4)continue;
                    Vector3 d=Quaternion.Euler(0,Range(0,360),0)*Vector3.right;
                    Vector3 tip=p+d*Range(.6f,1.1f)+Vector3.up*.23f;
                    wood.Tube(p,p+d*.4f,tip,tip,.013f,.002f,6,5);
                    for(int k=0;k<5;k++)
                    {
                        Vector3 q=Vector3.Lerp(p,tip,.25f+k*.15f),f=Quaternion.Euler(0,(k%2==0?-1:1)*Range(25,70),0)*d;
                        (k%3==0?light:leaves).Lance(q,q+f*Range(.3f,.5f)-Vector3.up*Range(.1f,.28f),.035f,k+node);
                    }
                }
            }
        }
        else if(kind==5)
        {
            for(int leaf=0;leaf<7;leaf++)
            {
                float angle=leaf*2.4f;Vector3 d=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 a=Vector3.up*.15f,b=Vector3.up*Range(1.5f,2.3f)+d*.25f,c=b+d*1.2f,end=c+d*.5f-Vector3.up*.5f;
                wood.Tube(a,Vector3.up*1.2f,b,c,.045f,.015f,10,6);
                // Broad leaves have a raised central rib and torn asymmetric margins.
                for(int segment=0;segment<13;segment++)
                {
                    float t=segment/13f;Vector3 p=Bezier(b,b+d*.6f+Vector3.up*.35f,c,end,t);
                    Vector3 side=Vector3.Cross(d,Vector3.up);float width=Mathf.Sin((t*.88f+.06f)*Mathf.PI)*.47f;
                    if(segment==4||segment==10)width*=.52f;
                    (leaf%3==0?light:leaves).Lance(p,p+side*width-Vector3.up*.12f,.075f,leaf);
                    leaves.Lance(p,p-side*width-Vector3.up*.1f,.075f,leaf+1);
                }
            }
        }
        else if(kind==6)
        {
            for(int stem=0;stem<42;stem++)
            {
                Vector3 a=new Vector3(Range(-.6f,.6f),0,Range(-.4f,.4f)),b=a+new Vector3(Range(-.5f,.5f),Range(.45f,1.45f),Range(-.4f,.4f));
                wood.Tube(a,a+Vector3.up*.5f,b,b,.008f,.002f,8,4);
                light.Lance(a+Vector3.up*.12f,b+new Vector3(.19f,-.17f,.1f),Range(.018f,.038f),stem);
                if(stem%5==0)leaves.Lance(b-Vector3.up*.12f,b+Vector3.up*.12f,.04f,stem);
            }
        }
        else
        {
            for(int leaf=0;leaf<11;leaf++)
            {
                Vector3 at=new Vector3(Range(-1.25f,1.25f),Range(.01f,.14f),Range(-1.2f,1.2f));float radius=Range(.23f,.48f);
                var v=new List<Vector3>{at};var uv=new List<Vector2>{Vector2.one*.5f};var tri=new List<int>();
                for(int i=0;i<=22;i++){float angle=(i/22f*5.65f+.32f);v.Add(at+new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(i*1.7f)*.013f,Mathf.Sin(angle)*radius));uv.Add(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.5f+Vector2.one*.5f);if(i>0)tri.AddRange(new[]{0,i+1,i});}
                var m=new Mesh{name="Notched floating lotus leaf"};m.SetVertices(v);m.SetUVs(0,uv);m.SetTriangles(tri,0);m.RecalculateNormals();Model("Notched lotus",r,SaveMesh(m,"Lotus"),leaf%3==0?leafWash:foliage);
                if(leaf%4==0)for(int petal=0;petal<8;petal++){float f=petal*Mathf.PI/4;flowers.Lance(at+Vector3.up*.05f,at+new Vector3(Mathf.Cos(f)*.13f,.25f,Mathf.Sin(f)*.13f),.065f,f);}
            }
        }
        if(kind!=7)Model("Tapered joined branching structure",r,SaveMesh(wood.Mesh("V3 plant structure"),"V3_"+kind+"_Wood"),kind==2?leafWash:bark);
        if(kind!=3&&kind!=7)
        {
            Model("Dense irregular leaf clusters",r,SaveMesh(leaves.Mesh("V3 leaf ink"),"V3_"+kind+"_Leaves"),foliage);
            Model("Pale leaf planes",r,SaveMesh(light.Mesh("V3 pale leaves"),"V3_"+kind+"_Pale"),leafWash);
        }
        if(kind==3||kind==7)Model("Paper white blossom petals",r,SaveMesh(flowers.Mesh("Blossoms"),"V3_"+kind+"_Flowers"),petals);
        InstallPlantLOD(r);
        return SavePrefab(r,Prefabs+"/PF_IT_V3_"+species[kind]+"_"+variation+".prefab");
    }
    static void InstallPlantLOD(Transform root)
    {
        var near=root.GetComponentsInChildren<MeshRenderer>();var far=new List<Renderer>();
        foreach(var source in near)
        {
            if(!source.name.Contains("leaf")&&!source.name.Contains("Leaf")){far.Add(source);continue;}
            var original=source.GetComponent<MeshFilter>().sharedMesh;int[] all=original.triangles;
            var reduced=new List<int>();
            // Retain entire alpha cards / six-segment lance leaves rather than torn half triangles.
            int leafIndices=source.sharedMaterial.GetFloat("_AlphaClip")>.5f?6:36;
            for(int q=0;q+leafIndices-1<all.Length;q+=leafIndices*3)for(int j=0;j<leafIndices;j++)reduced.Add(all[q+j]);
            if(reduced.Count==0)continue;
            var mesh=Object.Instantiate(original);mesh.triangles=reduced.ToArray();
            var go=Model("Distance plant LOD",root,SaveMesh(mesh,"Plant_LOD"),source.sharedMaterial);
            far.Add(go.GetComponent<Renderer>());
        }
        var lod=root.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.09f,near),new LOD(.018f,far.ToArray())});lod.RecalculateBounds();
    }
    static GameObject V3ImportedTree(int kind,int variation)
    {
        string name=kind==0?"Willow":"Maple";
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Soswaewon/Meshes/Environments/SM_"+name+".fbx");
        var filter=asset.GetComponentInChildren<MeshFilter>();var source=filter.sharedMesh;
        var r=Root("IT_V3_"+species[kind]+"_"+variation,null);
        r.gameObject.AddComponent<InkboundPassableGroup>();
        float size=(kind==0?8.4f:8.1f)/source.bounds.size.y;
        // Hanging leaves can extend below the trunk. Ground the wood, not the full leaf bounds.
        var sourceVertices=source.vertices;float trunkBase=float.PositiveInfinity;
        foreach(int index in source.GetTriangles(0))trunkBase=Mathf.Min(trunkBase,sourceVertices[index].y);
        var original=filter.GetComponent<Renderer>().sharedMaterials;
        for(int sub=0;sub<source.subMeshCount;sub++)
        {
            bool leaf=sub==1;var material=V3Material(name+(leaf?"_LeafCards":"_BranchedBark"),leaf?leafWash:bark,leaf?.46f:.25f);
            material.SetTexture("_BaseMap",original[sub].GetTexture("Material_Texture2D_1"));material.SetFloat("_TextureWeight",leaf?.6f:.30f);
            material.SetFloat("_AlphaClip",leaf?1:0);if(leaf)material.SetTexture("_OpacityMap",original[sub].GetTexture("Material_Texture2D_4"));
            TuneInkSurface(material,leaf?.46f:.25f,leaf?.46f:.54f,leaf?.20f:.18f,leaf?.11f:.09f,leaf?.30f:.85f,leaf?.06f:.5f);
            var map=new Dictionary<int,int>();var v=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
            var vertices=source.vertices;var n=source.normals;var u=source.uv;
            foreach(int index in source.GetTriangles(sub))
            {
                if(!map.TryGetValue(index,out int to))
                {to=v.Count;map[index]=to;v.Add((vertices[index]-Vector3.up*trunkBase)*size);normals.Add(n[index]);uv.Add(u[index]);}
                tr.Add(to);
            }
            var mesh=new Mesh{name="Ink-adapted "+name+" "+sub};mesh.SetVertices(v);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateBounds();
            Model(leaf?"Natural leaf cards with internal gaps":"Naturally joined trunk and branches",r,SaveMesh(mesh,"V3_"+name+"_"+sub),material);
        }
        var c=r.gameObject.AddComponent<CapsuleCollider>();c.center=Vector3.up*1.6f;c.height=3.2f;c.radius=.4f;
        InstallPlantLOD(r);
        return SavePrefab(r,Prefabs+"/PF_IT_V3_"+species[kind]+"_"+variation+".prefab");
    }
}
#endif
