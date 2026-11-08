// Apply imported garden art without regenerating architecture or changing shared gameplay.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
public static class DojoGardenBuild
{
    const string Base="Assets/_Project/Models/Environment/FinalDojoArena/Garden";
    const string Mats="Assets/_Project/Materials/FinalDojoArena/Garden";
    const string FJG="Assets/Waldemarst/FreeJapaneseGarden/";
    static System.Random random;
    static Transform root;
    static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
    static float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    static Transform Group(string name,Transform parent,Vector3 pos=default)
    {var g=new GameObject(name).transform;g.SetParent(parent,false);g.localPosition=pos;return g;}
    static T Save<T>(T value,string path) where T:Object
    {var prior=AssetDatabase.LoadAssetAtPath<T>(path);if(prior){EditorUtility.CopySerialized(value,prior);Object.DestroyImmediate(value);return prior;} AssetDatabase.CreateAsset(value,path);return value;}
    static Material Lit(string name,Texture texture,Color color)
    {
        if(materials.TryGetValue(name,out var cached))return cached;
        var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;
        m.SetColor("_BaseColor",color);m.SetTexture("_BaseMap",texture);m.SetFloat("_Smoothness",.18f);
        materials[name]=Save(m,Mats+"/"+name+".mat");return materials[name];
    }
    static Material Adapt(Material source,Mesh mesh,bool plant,float scale)
    {
        if(!source)return Lit("FallbackStone",null,new Color(.4f,.42f,.38f));
        string sourceId=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
        string key=source.name+"_"+(sourceId.Length>8?sourceId.Substring(0,8):sourceId)+(plant?"_Wind_"+mesh.name+"_"+Mathf.RoundToInt(mesh.bounds.size.y*1000):"_URP");
        key=string.Concat(key.Select(c=>char.IsLetterOrDigit(c)||c=='_'?c:'_'));
        if(materials.TryGetValue(key,out var result))return result;
        Texture tex=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):null;
        if(!tex && source.HasProperty("_MainTex"))tex=source.GetTexture("_MainTex");
        if(!tex)tex=source.mainTexture;
        string textureId=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tex));
        key += "_"+(textureId.Length>8?textureId.Substring(0,8):textureId);
        if(materials.TryGetValue(key,out result))return result;
        Color color=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
        // Broccoli bark/leaf tint is often .5 grey for the legacy tree lighting; preserve atlas colours.
        if(plant)color=Color.white;
        Material m;
        if(plant)
        {
            m=new Material(Shader.Find("Project/Environment/Dojo Garden Wind"));m.name=key;
            m.SetTexture("_BaseMap",tex);m.SetColor("_BaseColor",color);
            m.SetFloat("_RootY",mesh.bounds.min.y);m.SetFloat("_Height",mesh.bounds.size.y);
            m.SetFloat("_Cutoff",.35f);m.SetFloat("_BendStrength",key.ToLower().Contains("bamboo")?.26f:.12f);
            m.SetFloat("_FlutterStrength",.025f);m.SetFloat("_WindSpeed",.8f);
            m.enableInstancing=true;
        }
        else
        {
            m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=key;
            m.SetTexture("_BaseMap",tex);m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.15f);
            if(source.HasProperty("_BumpMap")&&source.GetTexture("_BumpMap"))
            {m.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));m.EnableKeyword("_NORMALMAP");}
        }
        materials[key]=Save(m,Mats+"/"+key+".mat");return materials[key];
    }
    static Bounds BoundsOf(GameObject go)
    {
        var rs=go.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).ToArray();
        if(rs.Length==0)throw new Exception("No mesh in "+go.name);
        Bounds b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;
    }
    static GameObject Place(string path,string name,Vector3 pos,float height,bool plant=false,float yaw=0)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!asset)throw new Exception("Missing garden asset "+path);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,root.gameObject.scene);
        // Art instances remain independent of legacy tree behaviours and demo controllers.
        PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        go.name=name;go.transform.SetParent(root,false);
        foreach(var mb in go.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(mb);
        foreach(var component in go.GetComponentsInChildren<Transform>(true))GameObjectUtility.RemoveMonoBehavioursWithMissingScript(component.gameObject);
        foreach(var b in go.GetComponentsInChildren<BillboardRenderer>(true))Object.DestroyImmediate(b);
        go.transform.localRotation=Quaternion.Euler(0,yaw,0);
        Bounds bounds=BoundsOf(go);float factor=height/Mathf.Max(bounds.size.y,.001f);go.transform.localScale*=factor;
        bounds=BoundsOf(go);go.transform.position+=pos-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter=renderer.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;
            if(plant && (renderer.name.ToLower().Contains("billboard") || renderer.sharedMaterials.Any(m=>m && m.name.ToLower().Contains("billboard"))))
            {renderer.gameObject.SetActive(false);continue;}
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Adapt(m,filter.sharedMesh,plant,factor)).ToArray();
            renderer.shadowCastingMode=ShadowCastingMode.On;
            if(plant)renderer.allowOcclusionWhenDynamic=false;
        }
        foreach(var lod in go.GetComponentsInChildren<LODGroup>(true))
        {
            var levels=lod.GetLODs();
            var valid=levels.Where(l=>l.renderers.Any(r=>r&&r.gameObject.activeInHierarchy&&!(r is BillboardRenderer))).ToArray();
            for(int i=0;i<valid.Length;i++)valid[i].screenRelativeTransitionHeight=i==0?.16f:i==1?.07f:.015f;
            lod.fadeMode=LODFadeMode.None;lod.animateCrossFading=false;lod.SetLODs(valid);lod.RecalculateBounds();
        }
        return go;
    }
    static void Visual(string name,Mesh mesh,Material material,Transform parent)
    {var g=Group(name,parent);g.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;g.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;}
    static Mesh Plane(float w,float d)
    {var m=new Mesh();m.vertices=new[]{new Vector3(-w/2,0,-d/2),new Vector3(w/2,0,-d/2),new Vector3(w/2,0,d/2),new Vector3(-w/2,0,d/2)};m.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};m.triangles=new[]{0,2,1,0,3,2};m.RecalculateNormals();m.RecalculateBounds();return m;}
    static void Ground()
    {
        var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(FJG+"Textures/Terrain/Ground_Gravel_02/T_FJG_Ground_Gravel_02_Albedo.png");
        var mat=Lit("Courtyard_Gravel",tex,new Color(.65f,.67f,.59f));mat.mainTextureScale=new Vector2(38,48);
        var g=Group("GravelGround",root,new Vector3(0,.006f,60));Visual("Ground",Save(Plane(97,121),Base+"/SM_GravelGround.asset"),mat,g);
    }
    static void Path()
    {
        // Flat top slabs sit just above ground; existing courtyard collision stays level.
        for(int i=0;i<34;i++)
        {
            float z=7+i*2.0f;float x=Mathf.Sin(i*.63f)*.6f;
            var slab=Place(FJG+"Prefabs/Misc/Rocks/JG_TobiIshi_A_0"+(i%2+1)+".prefab","SteppingStone_"+i,new Vector3(x,-.08f,z),.22f,false,R(-28,28));
            Bounds b=BoundsOf(slab);slab.transform.localScale=new Vector3(slab.transform.localScale.x*R(2.3f,3.1f)/b.size.x,slab.transform.localScale.y,slab.transform.localScale.z*R(1.6f,2.0f)/b.size.z);
            foreach(var renderer in slab.GetComponentsInChildren<MeshRenderer>())
            {
                var source=renderer.sharedMaterial;
                var stone=Lit("SteppingStone_Grey",source.mainTexture,new Color(1.35f,1.38f,1.32f));
                renderer.sharedMaterial=stone;
            }
            foreach(var c in slab.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            var box=slab.AddComponent<BoxCollider>();Bounds local=new Bounds(slab.transform.InverseTransformPoint(BoundsOf(slab).center),Vector3.zero);
            foreach(var f in slab.GetComponentsInChildren<MeshFilter>())foreach(var p in new[]{f.sharedMesh.bounds.min,f.sharedMesh.bounds.max})local.Encapsulate(slab.transform.InverseTransformPoint(f.transform.TransformPoint(p)));
            box.center=local.center;box.size=local.size;
        }
    }
    static bool GrassAllowed(float x,float z)
    {
        if(z<7||z>74||Mathf.Abs(x)>35)return false;
        if(Mathf.Abs(x)<2.2f)return false;
        if(Mathf.Abs(x)>27)return true;
        // More lush near the stepping stones and edges; low sparse tufts in fighting space.
        return Mathf.Abs(x)<5.3f || random.NextDouble()<.16;
    }
    static void Grass()
    {
        var mat=new Material(Shader.Find("Project/Environment/Dojo Garden Wind"));mat.name="Grass_Wind";
        mat.SetFloat("_Cutoff",0);mat.SetFloat("_UseVertexMask",1);mat.SetFloat("_UseVertexColor",1);
        mat.SetFloat("_BendStrength",.13f);mat.SetFloat("_FlutterStrength",.025f);mat.SetFloat("_WindSpeed",1.2f);
        mat.enableInstancing=true;mat=Save(mat,Mats+"/Grass_Wind.mat");
        for(int tile=0;tile<14;tile++)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var masks=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>();
            float z0=7+(tile%7)*10;float x0=tile<7?-35:0;
            for(int j=0;j<6000;j++)
            {
                float x=R(x0,x0+35),z=R(z0,Mathf.Min(z0+10,74));if(!GrassAllowed(x,z))continue;
                float h=R(.13f,.35f)*(Mathf.Abs(x)>27?1.35f:1);float angle=R(0,Mathf.PI*2);float width=R(.018f,.045f);
                Vector3 side=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*width;
                Vector3 point=new Vector3(x,.018f,z);Vector3 lean=new Vector3(R(-.08f,.08f),0,R(-.08f,.08f));int index=v.Count;
                v.Add(point-side);v.Add(point+side);v.Add(point+Vector3.up*h*.52f-side*.65f+lean*.35f);v.Add(point+Vector3.up*h*.52f+side*.65f+lean*.35f);v.Add(point+Vector3.up*h+lean);
                uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(new Vector2(0,.52f));uv.Add(new Vector2(1,.52f));uv.Add(new Vector2(.5f,1));
                masks.Add(Vector2.zero);masks.Add(Vector2.zero);masks.Add(new Vector2(0,.52f));masks.Add(new Vector2(0,.52f));masks.Add(Vector2.up);
                Color dark=Color.Lerp(new Color(.025f,.075f,.016f),new Color(.07f,.15f,.03f),R(0,1));Color tip=Color.Lerp(new Color(.12f,.25f,.05f),new Color(.22f,.33f,.09f),R(0,1));
                colors.Add(dark);colors.Add(dark);colors.Add(Color.Lerp(dark,tip,.6f));colors.Add(Color.Lerp(dark,tip,.6f));colors.Add(tip);
                indices.AddRange(new[]{index,index+2,index+1,index+1,index+2,index+3,index+2,index+4,index+3});
            }
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetUVs(1,masks);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();var b=mesh.bounds;b.Expand(.8f);mesh.bounds=b;
            Visual("GrassPatch_"+tile,Save(mesh,Base+"/SM_Grass_"+tile+".asset"),mat,root);
        }
    }
    static Mesh Petal(bool bamboo)
    {
        var m=new Mesh();m.vertices=bamboo?new[]{new Vector3(0,0,-.65f),new Vector3(-.13f,.035f,0),new Vector3(.13f,.035f,0),new Vector3(0,0,.65f)}:new[]{new Vector3(0,0,-.42f),new Vector3(-.32f,.10f,.08f),new Vector3(.32f,.10f,.08f),new Vector3(-.16f,0,.4f),new Vector3(.16f,0,.4f)};
        m.triangles=bamboo?new[]{0,1,2,1,3,2}:new[]{0,1,2,1,3,2,2,3,4};m.uv=m.vertices.Select(v=>new Vector2(v.x+.5f,v.z+.5f)).ToArray();m.RecalculateNormals();m.RecalculateBounds();return Save(m,Base+"/SM_"+(bamboo?"BambooLeaf":"CherryPetal")+".asset");
    }
    static void Falling(string name,Vector3 pos,bool bamboo,Mesh mesh,int seed)
    {
        var go=Group(name,root,pos).gameObject;var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(100+seed);
        var main=ps.main;main.duration=16;main.loop=true;main.prewarm=true;main.startLifetime=new ParticleSystem.MinMaxCurve(7,12);main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(bamboo?.14f:.12f,bamboo?.24f:.22f);main.startRotation3D=true;main.startRotationX=new ParticleSystem.MinMaxCurve(0,6.28f);main.startRotationY=new ParticleSystem.MinMaxCurve(0,6.28f);main.startRotationZ=new ParticleSystem.MinMaxCurve(0,6.28f);main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=120;main.cullingMode=ParticleSystemCullingMode.Automatic;
        var emission=ps.emission;emission.rateOverTime=bamboo?1.6f:2.5f;emission.SetBursts(new[]{new ParticleSystem.Burst(2+(seed%5),8,16,3,4)});
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(7,1,6);
        var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=new ParticleSystem.MinMaxCurve(.35f,.8f);velocity.y=new ParticleSystem.MinMaxCurve(-.8f,-.45f);velocity.z=new ParticleSystem.MinMaxCurve(.15f,.5f);
        var noise=ps.noise;noise.enabled=true;noise.strength=.35f;noise.frequency=.3f;noise.scrollSpeed=.25f;noise.octaveCount=1;
        var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.separateAxes=true;rotation.x=new ParticleSystem.MinMaxCurve(-1.3f,1.3f);rotation.y=new ParticleSystem.MinMaxCurve(-1.4f,1.4f);rotation.z=new ParticleSystem.MinMaxCurve(-.9f,.9f);
        var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.1f),new GradientAlphaKey(1,.75f),new GradientAlphaKey(0,1)});color.color=gradient;
        var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=mesh;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        var mat=Lit(bamboo?"Falling_BambooLeaf":"Falling_CherryPetal",null,bamboo?new Color(.32f,.47f,.10f):new Color(1,.68f,.78f));mat.SetFloat("_Cull",0);
        // Use opaque geometry with a shrinking end instead of transparent overdraw.
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0),new Keyframe(.08f,1),new Keyframe(.8f,1),new Keyframe(1,0)));
        renderer.sharedMaterial=mat;
    }
    static void RemoveOldArt(Transform architecture)
    {
        var garden=architecture.Find("CourtyardAndGarden");garden.Find("BakedVisuals").gameObject.SetActive(false);
        foreach(Transform t in garden)if(t.name=="StoneGardenLantern"||t.name=="GardenPine"||t.name=="GardenStone"||t.name=="GardenBorder"||t.name=="CentralApproachPavingEdge")t.gameObject.SetActive(false);
        // Torii was baked with the boundary wall. Strip only triangles within its isolated bounds.
        var boundary=architecture.Find("OuterWallsAndGate");boundary.Find("VermilionTorii").gameObject.SetActive(false);
        foreach(var f in boundary.Find("BakedVisuals").GetComponentsInChildren<MeshFilter>())
        {
            var source=f.sharedMesh;var vertices=source.vertices;var original=source.triangles;var retained=new List<int>();
            for(int i=0;i<original.Length;i+=3)
            {
                Vector3 c=f.transform.TransformPoint((vertices[original[i]]+vertices[original[i+1]]+vertices[original[i+2]])/3);
                if(c.z>13&&c.z<17&&Mathf.Abs(c.x)<8.5f&&c.y<8.3f)continue;
                retained.AddRange(new[]{original[i],original[i+1],original[i+2]});
            }
            if(retained.Count==original.Length)continue;
            var mesh=Object.Instantiate(source);mesh.triangles=retained.ToArray();mesh.RecalculateBounds();f.sharedMesh=Save(mesh,Base+"/"+source.name+"_WithoutTorii.asset");
        }
    }
    public static string Build()
    {
        if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!="Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity")throw new Exception("Open dojo preview in edit mode.");
        Directory.CreateDirectory(Base);Directory.CreateDirectory(Mats);AssetDatabase.Refresh();random=new System.Random(2049);materials.Clear();
        var architecture=GameObject.Find("FinalDojoArena/Architecture").transform;
        if(!AssetDatabase.LoadAssetAtPath<GameObject>(FJG+"Prefabs/Trees/Sakura/Tree_Sakura_Big_Spring_01.prefab"))throw new Exception("Garden packages not imported yet");
        var old=architecture.Find("LivingGarden");if(old)Object.DestroyImmediate(old.gameObject);
        root=Group("LivingGarden",architecture);RemoveOldArt(architecture);Ground();Path();Grass();
        var petal=Petal(false);var leaf=Petal(true);
        Place("Assets/JTS_Shrine_Torii_Gates/Prefabs/Torii_Inari_Large1_LOD.prefab","ShrineTorii",new Vector3(0,0,15),8.2f);
        int index=0;
        foreach(int side in new[]{-1,1})
        {
            for(int i=0;i<5;i++)Place("Assets/JTS_StoneLanterns/Prefabs/Lantern_Stone_"+(i%2==0?"A1":"C2")+".prefab","StoneLantern_"+side+"_"+i,new Vector3(side*(i%2==0?7:29),0,22+i*11),i%2==0?1.8f:2.3f,false,i*35);
            for(int i=0;i<4;i++)
            {
                Vector3 p=new Vector3(side*(i%2==0?19:31),0,23+i*16);
                string path=i==1?"Assets/Waldemarst/JapaneseGardenPackage/Prefabs/SakuraTree_A.prefab":FJG+"Prefabs/Trees/Sakura/Tree_Sakura_"+(i%2==0?"Big":"Mid")+"_Spring_01.prefab";
                Place(path,"CherryTree_"+index,p,R(8,11),true,R(0,360));Falling("CherryPetals_"+index,p+Vector3.up*6.5f,false,petal,index++);
            }
            for(int i=0;i<4;i++)Place(FJG+"Prefabs/Trees/BlackPine/Tree_BlackPine_"+(i%2==0?"Mid":"Big")+"_Green_01.prefab","BlackPine_"+side+"_"+i,new Vector3(side*32,0,31+i*14),R(6.5f,9),true,R(0,360));
            for(int i=0;i<9;i++)
            {
                Vector3 p=new Vector3(side*R(29,34),0,18+i*6.5f);
                Place(FJG+"Prefabs/Trees/Bamboo/Tree_Bamboo_"+(i%3==0?"Big":"Mid")+"_Green_01.prefab","Bamboo_"+side+"_"+i,p,R(5,8),true,R(0,360));
                if(i%3==0)Falling("BambooLeaves_"+side+"_"+i,p+Vector3.up*5,true,leaf,index++);
            }
            for(int i=0;i<10;i++)
            {
                Vector3 p=new Vector3(side*R(27.5f,34),0,R(19,74));
                Place(FJG+"Prefabs/Plants/Boxwood/Plant_Boxwood_Spring_01.prefab","Boxwood_"+side+"_"+i,p,R(.7f,1.2f),true,R(0,360));
                Place(FJG+"Prefabs/Plants/PaintedFern/Plant_PaintedFern_Spring_01.prefab","Fern_"+side+"_"+i,p+new Vector3(R(-2,2),0,R(-2,2)),R(.45f,.8f),true,R(0,360));
            }
            for(int i=0;i<3;i++)Place("Assets/Waldemarst/JapaneseGardenPackage/Prefabs/JapaneseMapleTree_"+(i%2==0?"A":"C")+".prefab","JapaneseMaple_"+side+"_"+i,new Vector3(side*R(25,33),0,25+i*22),R(4.5f,6.5f),true,R(0,360));
            for(int i=0;i<7;i++)Place(FJG+"Prefabs/Misc/Rocks/JG_Rock_A_0"+(i%3+1)+".prefab","GardenRock_"+side+"_"+i,new Vector3(side*R(28,34),-.12f,22+i*8),R(.7f,1.8f),false,R(0,360));
        }
        var wind=Group("GardenBreeze",root).gameObject.AddComponent<WindZone>();wind.mode=WindZoneMode.Directional;wind.transform.rotation=Quaternion.Euler(0,65,0);wind.windMain=.4f;wind.windTurbulence=.3f;
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);
        var prefab=PrefabUtility.SaveAsPrefabAsset(root.gameObject,"Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoLivingGarden.prefab");
        var summary=new { success=true, trees=root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("CherryTree_")||t.name.StartsWith("Bamboo_")||t.name.StartsWith("BlackPine_")||t.name.StartsWith("JapaneseMaple_")), renderers=root.GetComponentsInChildren<MeshRenderer>(true).Length, particles=root.GetComponentsInChildren<ParticleSystem>().Length, stones=34, prefab=AssetDatabase.GetAssetPath(prefab)};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(summary,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/garden-build.json",json);return json;
    }
    public static string Finish()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play mode before saving garden");
        root=GameObject.Find("FinalDojoArena/Architecture/LivingGarden").transform;materials.Clear();
        Object.DestroyImmediate(root.Find("ShrineTorii").gameObject);
        Place("Assets/JTS_Shrine_Torii_Gates/Prefabs/Torii_Inari_Large1_LOD.prefab","ShrineTorii",new Vector3(0,0,15),8.2f);
        ApplyLayout();
        PrefabUtility.SaveAsPrefabAsset(root.gameObject,"Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoLivingGarden.prefab");
        PrefabUtility.SaveAsPrefabAsset(GameObject.Find("FinalDojoArena"),"Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_FinalDojoArena_Living.prefab");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);
        return "Saved vermilion Inari torii, living garden prefab and complete living compound prefab";
    }

    // Idempotent follow-up: cherry-lined front courtyard, other trees behind the main hall.
    public static string ApplyLayout()
    {
        if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
        root=GameObject.Find("FinalDojoArena/Architecture/LivingGarden").transform;
        random=new System.Random(2049);materials.Clear();
        var trees=root.Cast<Transform>().ToArray();
        var pines=trees.Where(t=>t.name.StartsWith("BlackPine_")).OrderBy(t=>t.name).ToArray();
        float[] pineX={-36,-26,-15,-5,5,15,26,36};
        for(int i=0;i<pines.Length;i++)
        {
            // The source's low root tips extend below its actual trunk collar. Bury those tips.
            pines[i].position=new Vector3(pineX[i],-.12f,105.5f+(i%2)*3.6f);
        }
        var maples=trees.Where(t=>t.name.StartsWith("JapaneseMaple_")).OrderBy(t=>t.name).ToArray();
        float[] mapleX={-40,-22,-9,9,22,40};
        for(int i=0;i<maples.Length;i++)maples[i].position=new Vector3(mapleX[i],-.06f,111.2f-(i%2)*.8f);
        var bamboo=trees.Where(t=>t.name.StartsWith("Bamboo_")).OrderBy(t=>t.name).ToArray();
        for(int i=0;i<bamboo.Length;i++)
        {
            // A separate rear-border row keeps bamboo clumps out of the pine trunks.
            bamboo[i].position=new Vector3(-43+i*5.05f,-.04f,112.4f-(i%2)*.45f);
            string leafName=bamboo[i].name.Replace("Bamboo_","BambooLeaves_");var leaves=root.Find(leafName);
            if(leaves)leaves.position=bamboo[i].position+Vector3.up*5;
        }
        var petal=AssetDatabase.LoadAssetAtPath<Mesh>(Base+"/SM_CherryPetal.asset");
        // Original builder shares one counter with bamboo emitters; reserve a distinct cherry range.
        for(int i=11;i<18;i++)foreach(string prefix in new[]{"CherryTree_","CherryPetals_"})
        {var stale=root.Find(prefix+i);if(stale)Object.DestroyImmediate(stale.gameObject);}
        int n=100;
        foreach(int side in new[]{-1,1})
        for(int i=0;i<5;i++)
        {
            float[] x={11,23,13,23,12};float[] z={17,31,46,61,70};
            Vector3 p=new Vector3(side*x[i],0,z[i]);string name="CherryTree_"+n;
            if(!root.Find(name))Place(FJG+"Prefabs/Trees/Sakura/Tree_Sakura_Mid_Spring_01.prefab",name,p,R(7.5f,9.5f),true,R(0,360));
            if(!root.Find("CherryPetals_"+n))Falling("CherryPetals_"+n,p+Vector3.up*6,false,petal,n);
            n++;
        }
        PrefabUtility.SaveAsPrefabAsset(root.gameObject,"Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoLivingGarden.prefab");
        PrefabUtility.SaveAsPrefabAsset(GameObject.Find("FinalDojoArena"),"Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_FinalDojoArena_Living.prefab");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);
        return "18 front-yard cherry trees; pines, maples and bamboo relocated behind the hall; pine root collars lowered";
    }
}
