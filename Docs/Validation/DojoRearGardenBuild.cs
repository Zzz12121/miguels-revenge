using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
public static class DojoRearGardenBuild
{
    const string MeshDir="Assets/_Project/Models/Environment/FinalDojoArena/Garden/";
    const string MatDir="Assets/_Project/Materials/FinalDojoArena/Garden/";
    const string PrefabDir="Assets/_Project/Prefabs/Environment/FinalDojoArena/";
    const float RearWallZ=126.2f; // Rear depth 21m ->25.2m, a further 1.2x expansion.
    static Transform garden,root,architecture;
    static System.Random rng;
    static float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
    static Transform Group(string name,Transform parent,Vector3 at=default)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=at;return t;}
    static T Save<T>(T asset,string name,string dir)where T:Object
    {string path=dir+name;asset.name=Path.GetFileNameWithoutExtension(name);var prior=AssetDatabase.LoadAssetAtPath<T>(path);if(prior){EditorUtility.CopySerialized(asset,prior);Object.DestroyImmediate(asset);return prior;}AssetDatabase.CreateAsset(asset,path);return asset;}
    static Material Mat(string name,string shader,Color color)
    {var m=new Material(Shader.Find(shader));m.name=name;m.SetColor("_BaseColor",color);return Save(m,name+".mat",MatDir);}
    static Mesh Mesh(string name,List<Vector3> v,List<int> t,List<Vector2> uv=null,List<Color> c=null,List<Vector4> tangent=null)
    {
        var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.SetVertices(v);m.SetTriangles(t,0);
        if(uv!=null)m.SetUVs(0,uv);if(c!=null)m.SetColors(c);if(tangent!=null)m.SetTangents(tangent);
        m.RecalculateNormals();m.RecalculateBounds();return Save(m,name+".asset",MeshDir);
    }
    static GameObject Visual(string name,Mesh mesh,Material material,Transform parent)
    {var t=Group(name,parent);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;return t.gameObject;}
    static void Quad(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
    {int n=v.Count;v.AddRange(new[]{a,b,c,d});t.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});}
    static GameObject Plane(string name,float x0,float x1,float z0,float z1,float y,Material mat,Transform parent)
    {var v=new List<Vector3>();var t=new List<int>();Quad(v,t,new Vector3(x0,y,z0),new Vector3(x1,y,z0),new Vector3(x1,y,z1),new Vector3(x0,y,z1));return Visual(name,Mesh("SM_"+name,v,t),mat,parent);}
    static Bounds BoundsOf(GameObject g)
    {var renderers=g.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.gameObject.activeInHierarchy).ToArray();Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;}
    static void Box(string name,Vector3 at,Vector3 size,Material mat,Transform parent,bool collider=true)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;if(!collider)Object.DestroyImmediate(go.GetComponent<Collider>());}
    public static float RiverZ(float x)=>DojoRiverWater.Center(x);
    public static float RiverWidth(float x)=>DojoRiverWater.Width(x);
    static float ApronEdge(float x)
    {
        float q=Mathf.Clamp01(Mathf.Abs(x)/22f);
        return 101.2f+5.1f*Mathf.Sqrt(1-q*q)+.65f*Mathf.Sin(x*.28f)*Mathf.Sqrt(1-q*q);
    }
    static bool Apron(float x,float z)=>Mathf.Abs(x)<22&&z>=101.1f&&z<ApronEdge(x);
    static bool RearLawn(float x,float z)=>z>101.2f&&z<125.4f&&Mathf.Abs(x)<46.6f&&!Apron(x,z)&&Mathf.Abs(z-RiverZ(x))>RiverWidth(x)*.5f+.78f;
    static bool FrontLawn(float x,float z)=>z>=8&&z<=73.8f&&Mathf.Abs(x)<35.3f&&Mathf.Abs(x)>(2.3f+.30f*Mathf.Sin(z*.17f));
    static void Boundaries()
    {
        var boundary=architecture.Find("OuterWallsAndGate");
        foreach(var f in boundary.Find("BakedVisuals").GetComponentsInChildren<MeshFilter>())
        {
            var source=f.sharedMesh;
            // Work from the prior boundary on reruns so extending never compounds.
            string baseName=System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source)),"(?:_RearExtended)+$","");
            string originalPath=MeshDir+baseName+".asset";
            var original=AssetDatabase.LoadAssetAtPath<Mesh>(originalPath);
            if(!original)
            {
                originalPath="Assets/_Project/Models/Environment/FinalDojoArena/"+baseName+".asset";
                original=AssetDatabase.LoadAssetAtPath<Mesh>(originalPath);
            }
            if(!original)throw new Exception("Missing original boundary mesh "+baseName);
            source=original;
            var mesh=Object.Instantiate(source);var vertices=mesh.vertices;bool changed=false;
            for(int i=0;i<vertices.Length;i++)
            {var w=f.transform.TransformPoint(vertices[i]);if(w.z>114.35f){w.z+=11.2f;vertices[i]=f.transform.InverseTransformPoint(w);changed=true;}}
            if(!changed){Object.DestroyImmediate(mesh);continue;}
            mesh.vertices=vertices;mesh.RecalculateBounds();
            f.sharedMesh=Save(mesh,Path.GetFileNameWithoutExtension(originalPath)+"_RearExtended.asset",MeshDir);
        }
        foreach(Transform t in boundary)
        {
            if(t.name.StartsWith("RearWall")){var p=t.localPosition;p.z=RearWallZ;t.localPosition=p;}
            if(t.name=="WallStoneBase"||t.name=="WhitePlasterWall"||t.name=="DarkWallCoping")
            {var s=t.localScale;s.z=121.2f;t.localScale=s;var p=t.localPosition;p.z=65.6f;t.localPosition=p;}
        }
    }
    static void Ground()
    {
        var gravel=Mat("Rear_FineGreyGravel","Project/Environment/Dojo Garden Ground",new Color(.34f,.37f,.37f));gravel.SetFloat("_PebbleScale",31);
        var turf=Mat("DenseLawn_Base","Project/Environment/Dojo Garden Ground",new Color(.064f,.17f,.031f));turf.SetFloat("_Grass",1);turf.SetFloat("_PebbleScale",9);
        var outer=Mat("Exterior_Earth","Project/Environment/Dojo Garden Ground",new Color(.18f,.205f,.14f));outer.SetFloat("_Grass",1);
        // Replace BOTH former flat surfaces: otherwise the courtyard collider prevents wading.
        var courtyard=architecture.Find("CourtyardAndGarden");
        var legacyGravel=garden.Find("GravelGround");if(legacyGravel){foreach(var r in legacyGravel.GetComponentsInChildren<Renderer>())r.enabled=false;foreach(var c in legacyGravel.GetComponentsInChildren<Collider>())c.enabled=false;}
        var oldFloor=courtyard.Find("FlatCourtyardGround");if(oldFloor)foreach(var c in oldFloor.GetComponents<Collider>())c.enabled=false;
        foreach(var r in courtyard.GetComponentsInChildren<MeshRenderer>())if(r.sharedMaterial&&r.sharedMaterial.name=="M_Dojo_CourtyardSand")r.enabled=false;
        var xs=new List<float>();var zs=new List<float>();
        for(float x=-190;x<-49;x+=5)xs.Add(x);for(float x=-49;x<49;x+=.4f)xs.Add(x);for(float x=49;x<190;x+=5)xs.Add(x);xs.Add(190);
        for(float z=-100;z<100;z+=5)zs.Add(z);for(float z=100;z<129;z+=.35f)zs.Add(z);for(float z=129;z<260;z+=5)zs.Add(z);zs.Add(260);
        var gv=new List<Vector3>();var gt=new List<int>();
        foreach(float z in zs)foreach(float x in xs)gv.Add(new Vector3(x,DojoRiverWater.Bed(x,z),z));
        for(int j=0;j<zs.Count-1;j++)for(int i=0;i<xs.Count-1;i++){int n=j*xs.Count+i;gt.AddRange(new[]{n,n+xs.Count+1,n+1,n,n+xs.Count,n+xs.Count+1});}
        var groundMesh=Mesh("SM_ExtendedWalkableGround",gv,gt);var outside=Visual("ExtendedWalkableGround",groundMesh,outer,root);outside.AddComponent<MeshCollider>().sharedMesh=groundMesh;
        var sand=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/FinalDojoArena/M_Dojo_CourtyardSand.mat");
        if(sand)Plane("CourtyardSandSurface",-47.5f,47.5f,5.1f,101.1f,.005f,sand,root);
        var av=new List<Vector3>();var at=new List<int>();
        for(int i=0;i<160;i++){float x=-22+44*i/160f,nx=-22+44*(i+1)/160f;Quad(av,at,new Vector3(x,.021f,101.1f),new Vector3(nx,.021f,101.1f),new Vector3(nx,.021f,ApronEdge(nx)),new Vector3(x,.021f,ApronEdge(x)));}
        Visual("RearGreyGravel",Mesh("SM_RearGreyGravel",av,at),gravel,root);
        var lawnV=new List<Vector3>();var lawnT=new List<int>();
        for(float z=8;z<125.4f;z+=.45f)for(float x=-46.7f;x<46.7f;x+=.45f)
        {
            bool front=FrontLawn(x+.225f,z+.225f),rear=RearLawn(x+.225f,z+.225f);
            if(front||rear)Quad(lawnV,lawnT,new Vector3(x,.034f,z),new Vector3(x+.45f,.034f,z),new Vector3(x+.45f,.034f,z+.45f),new Vector3(x,.034f,z+.45f));
        }
        Visual("ContinuousGreenTurf",Mesh("SM_DenseLawnBase",lawnV,lawnT),turf,root);
        // Small angular pebbles add real silhouette and parallax to the textured grey apron.
        var v=new List<Vector3>();var t=new List<int>();var colors=new List<Color>();
        for(int i=0;i<12000;i++)
        {
            float x=R(-22,22),z=R(101.2f,107);if(!Apron(x,z))continue;float radius=R(.018f,.045f);float h=R(.006f,.017f);int n=v.Count;
            v.Add(new Vector3(x,.028f+h,z));for(int k=0;k<5;k++){float a=k*Mathf.PI*.4f;v.Add(new Vector3(x+Mathf.Cos(a)*radius,.026f,z+Mathf.Sin(a)*radius*R(.7f,1.2f)));}
            for(int k=0;k<5;k++)t.AddRange(new[]{n,n+1+(k+1)%5,n+1+k});
            Color c=Color.Lerp(new Color(.23f,.25f,.24f),new Color(.62f,.64f,.61f),R(0,1));for(int k=0;k<6;k++)colors.Add(c);
        }
        var pebble=new Material(Shader.Find("Project/Environment/Dojo Garden Wind"));pebble.SetColor("_BaseColor",new Color(.64f,.66f,.66f));pebble.SetFloat("_UseVertexColor",1);pebble.SetFloat("_UseVertexMask",1);pebble.SetFloat("_BendStrength",0);pebble.SetFloat("_FlutterStrength",0);pebble.SetFloat("_Cutoff",0);
        Visual("ApronSmallPebbles",Mesh("SM_RearApronPebbles",v,t,null,colors),Save(pebble,"Rear_ActualPebbles.mat",MatDir),root);
    }
    static void DenseGrass()
    {
        foreach(Transform child in garden)if(child.name.StartsWith("GrassPatch_"))child.gameObject.SetActive(false);
        var grass=new Material(Shader.Find("Project/Environment/Dojo Garden Wind"));grass.SetFloat("_UseVertexColor",1);grass.SetFloat("_UseVertexMask",1);grass.SetFloat("_Cutoff",0);grass.SetFloat("_BendStrength",.075f);grass.SetFloat("_FlutterStrength",.017f);grass.SetFloat("_WindSpeed",1.0f);grass.enableInstancing=true;
        grass=Save(grass,"DenseLawn_Wind.mat",MatDir);
        int total=0,tile=0;
        for(int region=0;region<2;region++)
        {
            float xmin=region==0?-35.3f:-46.6f,xmax=-xmin,zmin=region==0?8:101.2f,zmax=region==0?73.8f:125.4f;
            for(float z0=zmin;z0<zmax;z0+=10)for(float x0=xmin;x0<xmax;x0+=10)
            {
                var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();var mask=new List<Vector2>();var c=new List<Color>();
                int attempts=Mathf.CeilToInt(Mathf.Min(10,xmax-x0)*Mathf.Min(10,zmax-z0)*34);
                for(int b=0;b<attempts;b++)
                {
                    float x=R(x0,Mathf.Min(x0+10,xmax)),z=R(z0,Mathf.Min(z0+10,zmax));if(!(region==0?FrontLawn(x,z):RearLawn(x,z)))continue;
                    // Short, overlapping curved blades with a dark root and several green tips.
                    float h=R(.15f,.37f),w=R(.016f,.038f),a=R(0,6.283f);Vector3 s=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*w;
                    Vector3 p=new Vector3(x,.04f,z),lean=new Vector3(R(-.12f,.12f),0,R(-.12f,.12f));int n=v.Count;
                    v.AddRange(new[]{p-s,p+s,p+Vector3.up*h*.52f+lean*.3f-s*.65f,p+Vector3.up*h*.52f+lean*.3f+s*.65f,p+Vector3.up*h+lean});
                    t.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3,n+2,n+4,n+3});
                    uv.AddRange(new[]{Vector2.zero,Vector2.right,new Vector2(0,.52f),new Vector2(1,.52f),new Vector2(.5f,1)});mask.AddRange(new[]{Vector2.zero,Vector2.zero,new Vector2(0,.52f),new Vector2(0,.52f),Vector2.up});
                    Color dark=new Color(.015f,.054f,.008f),tip=Color.Lerp(new Color(.055f,.16f,.018f),new Color(.16f,.28f,.055f),R(0,1));
                    c.AddRange(new[]{dark,dark,Color.Lerp(dark,tip,.6f),Color.Lerp(dark,tip,.6f),tip});total++;
                }
                if(v.Count==0)continue;
                string name="SM_DenseGrass_"+tile;var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.SetVertices(v);m.SetTriangles(t,0);m.SetUVs(0,uv);m.SetUVs(1,mask);m.SetColors(c);m.RecalculateNormals();m.RecalculateBounds();var bounds=m.bounds;bounds.Expand(.6f);m.bounds=bounds;
                Visual("DenseGrass_"+tile,Save(m,name+".asset",MeshDir),grass,root);tile++;
            }
        }
        File.WriteAllText("Temp/dojo-grass-count.txt",total.ToString());
    }
    static void Stream()
    {
        var water=new Material(Shader.Find("Project/Environment/Dojo Garden Stream"));water.SetFloat("_FlowSpeed",.24f);water.SetFloat("_RippleStrength",.15f);water.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture>("Assets/SUIMONO - WATER SYSTEM 2/TEXTURES/tex_WaveCalm_normal.psd"));water.SetTexture("_DetailNormal",AssetDatabase.LoadAssetAtPath<Texture>("Assets/SUIMONO - WATER SYSTEM 2/TEXTURES/tex_WaveRoll_normal.psd"));water.SetTexture("_FoamMap",AssetDatabase.LoadAssetAtPath<Texture>("Assets/SUIMONO - WATER SYSTEM 2/TEXTURES/tex_foamMask.psd"));water=Save(water,"Rear_StreamWater.mat",MatDir);
        var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();var tangents=new List<Vector4>();
        int cross=10,rows=220;
        for(int i=0;i<=rows;i++)
        {
            float x=Mathf.Lerp(-46.7f,46.7f,i/(float)rows),z=RiverZ(x),width=RiverWidth(x);var dir=new Vector3(1,0,(RiverZ(x+.1f)-RiverZ(x-.1f))/.2f).normalized;
            for(int j=0;j<=cross;j++){float q=j/(float)cross;v.Add(new Vector3(x,DojoRiverWater.WaterY,z+(q-.5f)*width));uv.Add(new Vector2(x,q));tangents.Add(new Vector4(dir.x,0,dir.z,1));}
        }
        for(int i=0;i<rows;i++)for(int j=0;j<cross;j++){int n=i*(cross+1)+j;t.AddRange(new[]{n,n+cross+2,n+1,n,n+cross+1,n+cross+2});}
        Visual("WindingStream",Mesh("SM_RearWindingStream",v,t,uv,null,tangents),water,root);
        var contact=root.gameObject.AddComponent<DojoRiverWater>();
        string fx="Assets/SUIMONO - WATER SYSTEM 2/";
        contact.splashPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(fx+"EFFECTS/fx_SplashSmall.prefab");contact.ripplePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(fx+"EFFECTS/fx_rippleNormals.prefab");
        contact.splashSound=AssetDatabase.LoadAssetAtPath<AudioClip>(fx+"SOUNDS/water_splash01.wav");contact.riverSound=AssetDatabase.LoadAssetAtPath<AudioClip>(fx+"_DEMO/SOUNDS/sfx_river.wav");
        foreach(bool ring in new[]{false,true}){var m=new Material(Shader.Find("Project/Environment/Dojo Water Contact"));m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture>(fx+"EFFECTS/"+(ring?"tex_splash_ringfoam.psd":"tex_splash_drops.psd")));m=Save(m,ring?"River_Ripple_URP.mat":"River_Splash_URP.mat",MatDir);if(ring)contact.rippleMaterial=m;else contact.splashMaterial=m;}
        if(!contact.splashPrefab||!contact.ripplePrefab||!contact.riverSound)throw new Exception("Missing SUIMONO dependencies");
    }
    static GameObject CloneArt(Transform source,string name,Vector3 at,float height,float yaw=0)
    {
        var go=Object.Instantiate(source.gameObject,root);go.name=name;go.SetActive(true);go.transform.rotation=Quaternion.Euler(0,yaw,0);
        foreach(var collider in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
        Bounds b=BoundsOf(go);go.transform.localScale*=height/Mathf.Max(.01f,b.size.y);b=BoundsOf(go);go.transform.position+=at-new Vector3(b.center.x,b.min.y,b.center.z);
        return go;
    }
    static void Scenery()
    {
        var top=garden.Cast<Transform>().ToArray();
        var pines=top.Where(t=>t.name.StartsWith("BlackPine_")).OrderBy(t=>t.name).ToArray();
        var maples=top.Where(t=>t.name.StartsWith("JapaneseMaple_")).OrderBy(t=>t.name).ToArray();
        var bamboo=top.Where(t=>t.name.StartsWith("Bamboo_")).OrderBy(t=>t.name).ToArray();
        var placed=new List<Vector2>();
        Func<bool,Vector3> spot=tree=>
        {
            for(int attempt=0;attempt<500;attempt++)
            {
                float x=R(-43,43),z=R(102.2f,124.5f);if(!RearLawn(x,z))continue;
                if(Mathf.Abs(z-RiverZ(x))<RiverWidth(x)*.5f+(tree?2.3f:.65f))continue;
                var p=new Vector2(x,z);if(tree&&placed.Any(q=>Vector2.Distance(p,q)<5.0f))continue;
                if(tree)placed.Add(p);return new Vector3(x,tree?-.12f:.015f,z);
            }
            throw new Exception("Cannot place rear scenery without overlaps");
        };
        foreach(var pine in pines){pine.position=spot(true);pine.rotation=Quaternion.Euler(0,R(0,360),0);}
        foreach(var maple in maples){maple.position=spot(true);maple.rotation=Quaternion.Euler(0,R(0,360),0);}
        // Irregular clumps of bamboo along alternating stretches of the rear edge.
        float[] clusterX={-40,-28,-7,12,34};
        for(int i=0;i<bamboo.Length;i++)
        {
            float x=clusterX[i%clusterX.Length]+R(-2.1f,2.1f),z=R(123.0f,124.6f);
            bamboo[i].position=new Vector3(x,-.04f,z);bamboo[i].rotation=Quaternion.Euler(0,R(0,360),0);
            var emitter=garden.Find(bamboo[i].name.Replace("Bamboo_","BambooLeaves_"));if(emitter)emitter.position=bamboo[i].position+Vector3.up*5;
        }
        var shrubs=top.Where(t=>t.name.StartsWith("Boxwood_")).ToArray();var ferns=top.Where(t=>t.name.StartsWith("Fern_")).ToArray();
        // Existing front shrubs remain; new independent clusters enrich the rear garden.
        for(int i=0;i<56;i++)CloneArt(shrubs[i%shrubs.Length],"RearLowShrub_"+i,spot(false),R(.38f,.95f),R(0,360));
        for(int i=0;i<108;i++)CloneArt(ferns[i%ferns.Length],"RearFern_"+i,spot(false),R(.24f,.52f),R(0,360));
        var rocks=top.Where(t=>t.name.StartsWith("GardenRock_")).ToArray();int index=0;
        foreach(int side in new[]{-1,1})for(float x=-46;x<46;x+=R(1.25f,2.35f))
        {
            if(Mathf.Abs(x-12)<2.2f)continue;float z=RiverZ(x)+side*(RiverWidth(x)*.5f+R(-.08f,.25f));
            var rock=CloneArt(rocks[index%rocks.Length],"RiverbankBoulder_"+index,new Vector3(x,-.07f,z),R(.45f,1.05f),R(0,360));
            rock.transform.localScale=new Vector3(rock.transform.localScale.x*R(.9f,1.5f),rock.transform.localScale.y,rock.transform.localScale.z*R(.7f,1.1f));index++;
        }
        for(int i=0;i<12;i++)CloneArt(rocks[i%rocks.Length],"RearAccentBoulder_"+i,spot(false),R(.8f,1.6f),R(0,360));
        for(int i=0;i<8;i++)
        {
            float x=-39+i*10.8f+R(-1.7f,1.7f),side=i%2==0?-1:1;
            CloneArt(rocks[(i*3)%rocks.Length],"RiverbankLargeBoulder_"+i,
                new Vector3(x,-.16f,RiverZ(x)+side*(RiverWidth(x)*.5f+.6f)),R(1.05f,1.75f),R(0,360));
        }
        var lantern=top.First(t=>t.name.StartsWith("StoneLantern_"));
        for(int i=0;i<4;i++)CloneArt(lantern,"RearStoneLantern_"+i,new Vector3(-32+i*21,.01f,106.6f+(i%2)*.7f),R(1.5f,1.9f),R(0,360));
    }
    static Material StoneMaterial(string source,string name)
    {
        var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));string path="Assets/Soswaewon/Textures/Buildings/T_"+source;
        m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture>(path+"_BC.png"));var normal=AssetDatabase.LoadAssetAtPath<Texture>(path+"_NM.png");if(normal){m.SetTexture("_BumpMap",normal);m.EnableKeyword("_NORMALMAP");}m.SetFloat("_Smoothness",.12f);m.enableInstancing=true;return Save(m,name+".mat",MatDir);
    }
    static GameObject StoneAsset(string asset,string name,Vector3 position,Vector3 scale,float yaw,Material material)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Soswaewon/Prefabs/Structure/"+asset+".prefab");if(!prefab)throw new Exception("Missing KHS "+asset);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);go.name=name;go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));go.transform.localScale=scale;
        foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>())renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
        return go;
    }
    static void Stonework()
    {
        var stone=StoneMaterial("Stonewall","KHS_Stonewall_URP");
        // Stonewall02a runs from local Z=0 to Z=3. Repeated segments keep stones at human scale.
        int index=0;
        foreach(float x in new[]{-48.05f,48.05f})for(float z=5;z<RearWallZ;z+=3)StoneAsset("SM_Stonewall02a","KHS_WallBase_"+index++,new Vector3(x,-.12f,z),new Vector3(1,.62f,Mathf.Min(3,RearWallZ-z)/3),0,stone);
        for(float x=-48;x<48;x+=3)StoneAsset("SM_Stonewall02a","KHS_WallBase_"+index++,new Vector3(x,-.12f,RearWallZ),new Vector3(1,.62f,Mathf.Min(3,48-x)/3),90,stone);
        foreach(int side in new[]{-1,1})for(float x=6.4f;x<48;x+=3)StoneAsset("SM_Stonewall02a","KHS_WallBase_"+index++,new Vector3(side*x,-.12f,5),new Vector3(1,.62f,Mathf.Min(3,48-x)/3),side*90,stone);
        var bridge=StoneAsset("SM_Bridge01b","KHS_StoneBridge",new Vector3(12,0,RiverZ(12)),new Vector3(2.1f,1,1.55f),0,StoneMaterial("Bridge01b","KHS_Bridge_URP"));
        var b=BoundsOf(bridge);bridge.transform.position+=new Vector3(12-b.center.x,.1f-b.max.y,RiverZ(12)-b.center.z);
        foreach(var collider in bridge.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
        // The stone crossing has a continuous level walking surface with shallow approach ramps.
        var deck=Group("BridgeWalkableDeck",root);deck.position=new Vector3(12,.055f,RiverZ(12));var box=deck.gameObject.AddComponent<BoxCollider>();box.size=new Vector3(3.15f,.14f,7.75f);
        foreach(var fern in root.Cast<Transform>().Where(t=>t.name.StartsWith("RearFern_")||t.name.StartsWith("RearLowShrub_")).ToArray())if(Mathf.Abs(fern.position.x-12)<2&&Mathf.Abs(fern.position.z-RiverZ(12))<5){var p=fern.position;p.x+=4;fern.position=p;}
    }
    public static string Build()
    {
        if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!="Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity")throw new Exception("Open dojo scene in Edit mode");
        AssetDatabase.Refresh();if(!Shader.Find("Project/Environment/Dojo Garden Stream")||!Shader.Find("Project/Environment/Dojo Garden Ground"))throw new Exception("Import garden shaders first");
        architecture=GameObject.Find("FinalDojoArena/Architecture").transform;garden=architecture.Find("LivingGarden");
        var old=garden.Find("RearLandscape");if(old)Object.DestroyImmediate(old.gameObject);
        root=Group("RearLandscape",garden);rng=new System.Random(20261108);
        Boundaries();Ground();DenseGrass();Stream();Scenery();Stonework();
        PrefabUtility.SaveAsPrefabAsset(root.gameObject,PrefabDir+"PF_DojoRearLandscape.prefab");
        PrefabUtility.SaveAsPrefabAsset(garden.gameObject,PrefabDir+"PF_DojoLivingGarden.prefab");
        PrefabUtility.SaveAsPrefabAsset(GameObject.Find("FinalDojoArena"),PrefabDir+"PF_FinalDojoArena_Living.prefab");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);Physics.SyncTransforms();
        var summary=new{success=true,rearDepth=25.2,previousRearDepth=21,rearScale=1.2,exteriorGround="380 x 360m",grassBlades=File.ReadAllText("Temp/dojo-grass-count.txt"),shrubs=root.Cast<Transform>().Count(t=>t.name.StartsWith("RearLowShrub_")),ferns=root.Cast<Transform>().Count(t=>t.name.StartsWith("RearFern_")),bankBoulders=root.Cast<Transform>().Count(t=>t.name.StartsWith("RiverbankBoulder_")),largeBankBoulders=8};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(summary,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/rear-landscape-build.json",json);return json;
    }
}
