using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Unity.Profiling;

public static class InkboundTownV3RuntimeProbe
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    public static object GameView1080()
    {
        var asm=typeof(Editor).Assembly;var sizesType=asm.GetType("UnityEditor.GameViewSizes");
        object sizes=Resources.FindObjectsOfTypeAll(sizesType).FirstOrDefault();
        if(sizes==null)sizes=sizesType.BaseType.GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var group=sizesType.GetProperty("currentGroup",Flags).GetValue(sizes);
        var viewType=asm.GetType("UnityEditor.GameView");var view=Resources.FindObjectsOfTypeAll(viewType).FirstOrDefault();
        if(view==null)throw new Exception("Game View must be open");
        var selection=viewType.GetProperty("selectedSizeIndex",Flags);
        SessionState.SetInt("InkboundV3_PreviousGameViewSize",(int)selection.GetValue(view));
        int built=(int)group.GetType().GetMethod("GetBuiltinCount",Flags).Invoke(group,null);
        int custom=(int)group.GetType().GetMethod("GetCustomCount",Flags).Invoke(group,null);
        var sizeType=asm.GetType("UnityEditor.GameViewSize");var enumType=asm.GetType("UnityEditor.GameViewSizeType");
        var size=Activator.CreateInstance(sizeType,Flags,null,new object[]{Enum.Parse(enumType,"FixedResolution"),1920,1080,"Inkbound V3 validation"},null);
        group.GetType().GetMethod("AddCustomSize",Flags).Invoke(group,new[]{size});selection.SetValue(view,built+custom);
        ((EditorWindow)view).Repaint();return new{requested="1920x1080",index=built+custom};
    }
    public static object RestoreGameView()
    {
        var t=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=Resources.FindObjectsOfTypeAll(t).FirstOrDefault();
        if(view!=null)t.GetProperty("selectedSizeIndex",Flags).SetValue(view,SessionState.GetInt("InkboundV3_PreviousGameViewSize",0));
        return "Restored previous Game View size";
    }
    public static async Task<string> Timings()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        var main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",180);
        var render=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Render Thread",180);
        var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",180);
        var dt=new List<double>();var gpu=new List<double>();var timings=new FrameTiming[1];
        try
        {
            int previous=Time.frameCount;
            while(dt.Count<150)
            {
                await Task.Delay(12);if(Time.frameCount==previous)continue;previous=Time.frameCount;
                dt.Add(Time.unscaledDeltaTime*1000);FrameTimingManager.CaptureFrameTimings();
                if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].gpuFrameTime>0)gpu.Add(timings[0].gpuFrameTime);
            }
            double[] Samples(ProfilerRecorder r)=>r.Valid?r.ToArray().Select(s=>s.Value/1000000.0).Where(x=>x>0).ToArray():Array.Empty<double>();
            object Stats(IEnumerable<double> source)
            {var a=source.OrderBy(x=>x).ToArray();return a.Length==0?null:new{samples=a.Length,mean=a.Average(),median=a[a.Length/2],p95=a[Math.Min(a.Length-1,(int)(a.Length*.95))]};}
            var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=Resources.FindObjectsOfTypeAll(type).FirstOrDefault();
            var target=view!=null?(RenderTexture)type.GetField("m_RenderTexture",Flags).GetValue(view):null;
            var report=new{
                kind="Actual Editor Game View; includes Editor and auto-tick overhead, not a standalone build benchmark",
                resolution=target?target.width+"x"+target.height:"unavailable",fov=Camera.main.fieldOfView,
                hardware=new{SystemInfo.processorType,SystemInfo.graphicsDeviceName},frameMilliseconds=Stats(dt),mainThreadMilliseconds=Stats(Samples(main)),renderThreadMilliseconds=Stats(Samples(render)),
                gpuMilliseconds=Stats(gpu),gpuNote=gpu.Count==0?"FrameTimingManager returned no GPU timings; 60 FPS GPU target is unverified":"Measured FrameTimingManager GPU timings",
                fpsFromFrameMean=1000/dt.Average(),gcBytesPerFrame=gc.Valid?gc.ToArray().Select(s=>(double)s.Value).DefaultIfEmpty().Average():0,
                rendering=new{UnityStats.drawCalls,UnityStats.setPassCalls,UnityStats.triangles,UnityStats.vertices},
                npc=UnityEngine.Object.FindObjectsByType<NpcRoutine>().Length,
                detailedEchoes=UnityEngine.Object.FindObjectsByType<InkboundEcho>().Count(e=>e.HighQuality),
                particleCount=UnityEngine.Object.FindObjectsByType<ParticleSystem>().Sum(p=>p.particleCount),
                allocatedMB=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()/1048576.0
            };
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText("output/inkbound-town/v3/runtime-timings.json",json);return json;
        }
        finally{main.Dispose();render.Dispose();gc.Dispose();}
    }
    public static async Task<string> Combat()
    {
        var p=PlayerHealth.Find();var cc=p.GetComponent<CharacterController>();var guard=new GameObject("Temporary performance protection");p.SetPracticeProtection(guard,true);
        var encounter=UnityEngine.Object.FindAnyObjectByType<InkboundTownEncounter>();
        try
        {
            cc.enabled=false;p.transform.position=new Vector3(-44,1.05f,3);cc.enabled=true;p.GetComponent<ThirdPersonController>().ResetMotion();
            UnityEngine.AI.NavMesh.SamplePosition(new Vector3(-44,0,9),out var g,2,UnityEngine.AI.NavMesh.AllAreas);encounter.agent.Warp(g.position);encounter.elder.transform.position=g.position;encounter.BeginFight();
            var camera=Camera.main.GetComponent<ThirdPersonCamera>();camera.ClearLock();camera.yaw=0;camera.pitch=10;camera.SnapToTarget();
            typeof(InkboundTownEncounter).GetField("decision",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(encounter,Time.time+.1f);
            await Task.Delay(1600);string result=await Timings();System.IO.File.Copy("output/inkbound-town/v3/runtime-timings.json","output/inkbound-town/v3/runtime-combat.json",true);return result;
        }
        finally{p.SetPracticeProtection(guard,false);UnityEngine.Object.Destroy(guard);}
    }
    public static async Task<string> Overdraw()
    {
        var settings=UnityEngine.Rendering.Universal.UniversalRenderPipelineDebugDisplaySettings.Instance.renderingSettings;
        var previous=settings.overdrawMode;int count=settings.maxOverdrawCount;
        var c=new GameObject("Temporary transparent overdraw camera").AddComponent<Camera>();c.CopyFrom(Camera.main);c.enabled=false;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=Color.black;
        var rt=RenderTexture.GetTemporary(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var active=RenderTexture.active;
        try
        {
            settings.overdrawMode=UnityEngine.Rendering.Universal.DebugOverdrawMode.Transparent;settings.maxOverdrawCount=64;
            await Task.Delay(250);c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            System.IO.File.WriteAllBytes("output/inkbound-town/v3/transparent-overdraw.png",image.EncodeToPNG());
            return "URP transparent overdraw debug view; white clamps at 64 or more drawn layers. Counts transparent fragment work including zero-alpha cards. Debug settings restored.";
        }
        finally{settings.overdrawMode=previous;settings.maxOverdrawCount=count;c.targetTexture=null;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(c.gameObject);}
    }
}
