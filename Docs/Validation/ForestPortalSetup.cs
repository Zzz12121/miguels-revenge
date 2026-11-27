using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

public static class ForestPortalSetup
{
    public const string RunePath="Assets/Piloto Studio/Super Realistic FX Bundle 02/Realistic Ying Yang/Ying Yang Vertical Rune.prefab";
    static void Folder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
    public static string Install()
    {
        if(Application.isPlaying)throw new Exception("Exit Play mode before installing portal prefabs.");
        const string art="Assets/_Project/VFX/ForestIsland/Portal";
        const string resources="Assets/_Project/Resources/ScenePortals";
        Folder(art);Folder(resources);
        var shader=Shader.Find("Project/VFX/Forest Portal Beacon");if(!shader)throw new Exception("Beacon shader did not compile.");
        Material MakeMaterial(string name,bool sparkle)
        {
            string path=art+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",new Color(4,2.5f,.35f,1));m.SetFloat("_Sparkle",sparkle?1:0);m.SetFloat("_Intensity",1);
            EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return m;
        }
        var beam=MakeMaterial("M_ForestPortalGoldColumn",false);var spark=MakeMaterial("M_ForestPortalGoldSpark",true);
        var root=new GameObject("PF_GoldenPortalBeacon");
        try
        {
            var v=new System.Collections.Generic.List<Vector3>();var uv=new System.Collections.Generic.List<Vector2>();var tr=new System.Collections.Generic.List<int>();
            foreach(float radius in new[]{.12f,.36f,.8f})
            {
                int first=v.Count;
                for(int y=0;y<=8;y++)for(int i=0;i<=48;i++)
                {
                    float t=y/8f,a=i*Mathf.PI*2/48,r=radius*Mathf.Lerp(1.2f,.6f,t);
                    v.Add(new Vector3(Mathf.Cos(a)*r,.06f+t*11,Mathf.Sin(a)*r));uv.Add(new Vector2(i/48f,t));
                    if(y<8&&i<48){int q=first+y*49+i;tr.AddRange(new[]{q,q+49,q+1,q+1,q+49,q+50});}
                }
            }
            string meshPath=art+"/SM_ForestPortalGoldColumn.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(!mesh){mesh=new Mesh{name="Soft layered golden portal column"};AssetDatabase.CreateAsset(mesh,meshPath);}
            mesh.Clear();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.colors=Enumerable.Repeat(Color.white,v.Count).ToArray();mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
            root.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=beam;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var particles=new GameObject("Rising golden sparkles");particles.transform.SetParent(root.transform,false);particles.SetActive(false);
            var ps=particles.AddComponent<ParticleSystem>();var main=ps.main;main.loop=true;main.duration=3;main.startLifetime=new ParticleSystem.MinMaxCurve(1.5f,3);main.startSpeed=new ParticleSystem.MinMaxCurve(.4f,1.1f);main.startSize=new ParticleSystem.MinMaxCurve(.055f,.13f);main.startColor=new Color(1,.84f,.5f,.8f);main.maxParticles=96;main.simulationSpace=ParticleSystemSimulationSpace.Local;
            var emission=ps.emission;emission.rateOverTime=18;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=7;shape.radius=.75f;shape.rotation=new Vector3(-90,0,0);
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;
            velocity.x=new ParticleSystem.MinMaxCurve(0,0);velocity.y=new ParticleSystem.MinMaxCurve(.55f,1.4f);velocity.z=new ParticleSystem.MinMaxCurve(0,0);
            var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.65f,.18f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(0,1)});color.color=gradient;
            var pr=ps.GetComponent<ParticleSystemRenderer>();pr.sharedMaterial=spark;pr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;particles.SetActive(true);
            var lightRoot=new GameObject("Warm portal ground light");lightRoot.transform.SetParent(root.transform,false);lightRoot.transform.localPosition=Vector3.up*1.4f;
            var light=lightRoot.AddComponent<Light>();light.type=LightType.Point;light.range=8;light.intensity=2.3f;light.color=new Color(1,.71f,.24f);light.shadows=LightShadows.None;
            var beacon=PrefabUtility.SaveAsPrefabAsset(root,art+"/PF_GoldenPortalBeacon.prefab");
            var config=new GameObject("PF_ForestInkboundPortal");
            try
            {
                var flow=config.AddComponent<ForestInkboundPortal>();flow.verticalRunePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(RunePath);flow.goldenBeaconPrefab=beacon;
                if(!flow.verticalRunePrefab)throw new Exception("Original Vertical Rune prefab missing.");
                PrefabUtility.SaveAsPrefabAsset(config,resources+"/PF_ForestInkboundPortal.prefab");
            }
            finally{Object.DestroyImmediate(config);}
        }
        finally{Object.DestroyImmediate(root);}
        var scenes=EditorBuildSettings.scenes.ToList();
        foreach(string path in new[]{"Assets/_Project/Scenes/ForestIsland.unity",InkboundTownBuilder.ScenePath})
        {
            if(!File.Exists(path))continue;
            var listed=scenes.FirstOrDefault(s=>s.path==path);
            if(listed!=null)listed.enabled=true;else scenes.Add(new EditorBuildSettingsScene(path,true));
        }
        EditorBuildSettings.scenes=scenes.ToArray();AssetDatabase.SaveAssets();
        return "Installed reusable Vertical Rune portal and golden beacon; enabled ForestIsland and InkboundTown_Exploration.";
    }
    public static string InspectRune()
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(RunePath);
        var report=new{scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,dirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty,
            scale=asset.transform.localScale.ToString(),systems=asset.GetComponentsInChildren<ParticleSystem>(true).Select(p=>new{p.name,active=p.gameObject.activeSelf,duration=p.main.duration,delay=p.main.startDelay.constantMax,life=p.main.startLifetime.constantMax,
                bursts=Enumerable.Range(0,p.emission.burstCount).Select(i=>new{time=p.emission.GetBurst(i).time,count=p.emission.GetBurst(i).count.constantMax}).ToArray(),rate=p.emission.rateOverTime.constantMax}).ToArray()};
        Directory.CreateDirectory("output/forest-town-portal");string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/forest-town-portal/source-rune.json",json);return json;
    }
    public static string PreviewSource()
    {
        Directory.CreateDirectory("output/forest-town-portal");
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RunePath));root.name="Temporary source rune inspection";root.transform.position=Vector3.zero;
        foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
        foreach(var helper in root.GetComponentsInChildren<PilotoStudio.PilotoCameraOffset>(true)){helper.enabled=false;if(helper.transform!=root.transform)helper.gameObject.SetActive(false);}
        foreach(var helper in root.GetComponentsInChildren<PilotoStudio.ParticleHandler>(true))helper.enabled=false;
        var c=new GameObject("Temporary source rune camera").AddComponent<Camera>();c.enabled=false;c.cullingMask=1<<31;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.12f,.16f,.13f);c.fieldOfView=55;c.transform.SetPositionAndRotation(new Vector3(10,7,-12),Quaternion.LookRotation(new Vector3(-10,-5.5f,12)));
        var rt=RenderTexture.GetTemporary(1280,720,24);var old=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            foreach(float time in new[]{1.4f,2.7f,3.5f})
            {
                foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {var main=ps.main;main.loop=false;main.stopAction=ParticleSystemStopAction.None;ps.Simulate(time,false,true,true);ps.Pause(false);}
                c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("output/forest-town-portal/source-"+time.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+".png",image.EncodeToPNG());
            }
            return "Saved three native renders of the original Vertical Rune.";
        }
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(c.gameObject);Object.DestroyImmediate(root);}
    }
}
