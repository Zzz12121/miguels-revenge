using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

public static class InkboundMarketLayoutChecks
{
    public const string Folder="output/inkbound-town/riverside-market";
    public static Bounds BoundsOf(Transform root,bool roof=false)
    {
        bool found=false;var b=new Bounds(root.position,Vector3.zero);
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if(r is ParticleSystemRenderer||r is LineRenderer||roof&&!r.sharedMaterials.Any(m=>m&&m.HasProperty("_Pattern")&&Mathf.Abs(m.GetFloat("_Pattern")-2)<.1f))continue;
            if(found)b.Encapsulate(r.bounds);else{b=r.bounds;found=true;}
        }
        return b;
    }
    public static string Inspect()
    {
        Directory.CreateDirectory(Folder);
        var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();var buildings=settings.transform.Find("02 Inhabited town architecture");
        var houses=buildings.Cast<Transform>().Where(t=>t.position.x<0&&t.position.z>-20&&t.position.z<32&&t.GetComponentsInChildren<BoxCollider>().Any(c=>c.name=="Solid stone foundation"))
            .Select(t=>new{t.name,position=t.position.ToString("F3"),yaw=t.eulerAngles.y,roofMin=BoundsOf(t,true).min.ToString("F3"),roofMax=BoundsOf(t,true).max.ToString("F3")}).ToArray();
        var npcs=Object.FindObjectsByType<NpcRoutine>().Where(n=>n.transform.position.x<-25&&n.transform.position.x>-58&&n.transform.position.z>-23&&n.transform.position.z<30)
            .Select(n=>new{n.name,position=n.transform.position.ToString("F3"),stops=n.stops.Select(s=>s.position.ToString("F3")).ToArray()}).ToArray();
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{houses,npcs},Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/before.json",json);return json;
    }
    static void Shot(string name,Vector3 point,Vector3 target,bool top=false)
    {
        var c=new GameObject("Temporary market layout review camera").AddComponent<Camera>();c.CopyFrom(Camera.main);c.enabled=false;c.farClipPlane=850;c.fieldOfView=65;c.orthographic=top;if(top)c.orthographicSize=35;
        c.transform.SetPositionAndRotation(point,Quaternion.LookRotation(target-point));
        int w=1440,h=top?1080:810;var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;var image=new Texture2D(w,h,TextureFormat.RGB24,false);
        try{Object.FindAnyObjectByType<InkboundWaterReflection>()?.RefreshFor(c);c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(c.gameObject);}
    }
    public static string ApplyAndCheck()
    {
        Directory.CreateDirectory(Folder);var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();var root=settings.transform;
        var block=root.Find("02 Inhabited town architecture");
        var oldMarket=block.Cast<Transform>().Where(t=>t.position.z>-15&&t.position.z<18&&(Mathf.Abs(t.position.x+51.1f)<.1f||Mathf.Abs(t.position.x+36.9f)<.1f)).ToArray();
        float oldClear=oldMarket.Where(t=>t.position.x>-44).Min(t=>BoundsOf(t,true).min.x)-oldMarket.Where(t=>t.position.x<-44).Max(t=>BoundsOf(t,true).max.x);
        var river=block.Cast<Transform>().Where(t=>t.position.x>-24&&t.position.x<-17&&t.position.z>-19&&t.position.z<34).ToArray();var riverPositions=river.Select(t=>t.position).ToArray();
        int residents=root.GetComponentsInChildren<NpcRoutine>(true).Length,renderers=root.GetComponentsInChildren<Renderer>(true).Length;
        string timing=EditorJsonUtility.ToJson(settings);
        Shot("01-before-overhead",new Vector3(-32,76,-4),new Vector3(-32,0,4),true);
        Shot("02-before-market-width",new Vector3(-44,2.8f,-20),new Vector3(-44,2.8f,20));
        InkboundTownBuilder.UpdateRiversideMarket();
        var east=oldMarket.OrderByDescending(t=>t.position.x).Take(4).ToArray();var west=oldMarket.Except(east).ToArray();
        float clear=east.Min(t=>BoundsOf(t,true).min.x)-west.Max(t=>BoundsOf(t,true).max.x);
        var gaps=new System.Collections.Generic.List<float>();
        foreach(var market in east)foreach(var back in river)
        {var a=BoundsOf(market,true);var b=BoundsOf(back,true);if(a.max.z>b.min.z&&a.min.z<b.max.z)gaps.Add(b.min.x-a.max.x);}
        var elder=root.GetComponent<InkboundTownEncounter>().elder;var path=new NavMeshPath();
        bool start=NavMesh.SamplePosition(elder.transform.position,out var from,2,NavMesh.AllAreas);
        bool end=NavMesh.SamplePosition(new Vector3(-12,0,-22),out var to,2,NavMesh.AllAreas);
        bool connected=start&&end&&NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
        Shot("03-after-overhead",new Vector3(-32,76,-4),new Vector3(-32,0,4),true);
        Shot("04-wider-market-street",new Vector3(InkboundTownBuilder.RiversideMarketCenter,2.8f,-20),new Vector3(InkboundTownBuilder.RiversideMarketCenter,2.8f,20));
        Shot("05-back-to-back-block",new Vector3(-38,20,-23),new Vector3(-26,2.5f,3));
        var report=new{movedShops=oldMarket.Length,oldMinimumClearWidth=oldClear,newMinimumClearWidth=clear,pavedWidth=InkboundTownBuilder.RiversideMarketPavingWidth,
            marketCenterX=InkboundTownBuilder.RiversideMarketCenter,eastMarketRowX=east.Average(t=>t.position.x),westMarketRowX=west.Average(t=>t.position.x),
            roofBackGaps=gaps.ToArray(),oppositeFronts=east.All(a=>river.All(b=>Vector3.Dot(a.forward,b.forward)<-.99f)),
            riverHousesPreserved=river.Select((t,i)=>t.position==riverPositions[i]).All(v=>v),residentsPreserved=root.GetComponentsInChildren<NpcRoutine>(true).Length==residents,
            renderersPreserved=root.GetComponentsInChildren<Renderer>(true).Length==renderers,inkSettingsPreserved=EditorJsonUtility.ToJson(settings)==timing,
            mountainBasinPreserved=root.Find("03 Willow banks and painted mountains/Irregular mountain basin around the town")!=null,marketConnectedToRiver=connected,
            elderPosition=elder.transform.position.ToString("F3")};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/layout-checks.json",json);return json;
    }
    public static async Task<string> CheckWalkAndRestoreView()
    {
        var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();var camera=Camera.main.GetComponent<ThirdPersonCamera>();bool input=motor.inputEnabled;float health=p.Health;
        var kb=InputSystem.AddDevice<Keyboard>("MarketStreetWalkCheck");bool held=false;
        void Pump(){kb.MakeCurrent();InputState.Change(kb,held?new KeyboardState(Key.W):new KeyboardState());}
        void Warp(Vector3 point){var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=point;motor.ResetMotion();cc.enabled=true;}
        InputSystem.onBeforeUpdate+=Pump;
        try
        {
            motor.inputEnabled=true;Warp(new Vector3(-39.375f,1.14f,-15));camera.ClearLock();camera.yaw=0;camera.pitch=12;camera.distance=8;camera.SnapToTarget();await Task.Delay(200);
            Vector3 before=p.transform.position;held=true;float until=Time.time+1.1f;while(Time.time<until)await Task.Delay(20);held=false;
            await Task.Delay(100);float distance=p.transform.position.z-before.z;
            var report=new{forwardDistance=distance,walkable=distance>2,grounded=motor.IsGrounded,healthPreserved=p.Health==health,
                civilians=Object.FindObjectsByType<NpcRoutine>().Length,elderPosition=Object.FindAnyObjectByType<InkboundTownEncounter>().elder.transform.position.ToString("F3")};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/walk-checks.json",json);return json;
        }
        finally
        {
            held=false;InputSystem.onBeforeUpdate-=Pump;if(kb.added)InputSystem.RemoveDevice(kb);motor.inputEnabled=input;
            Warp(new Vector3(-31.1068f,1.05f,-24.4255f));camera.yaw=585.117065f;camera.pitch=62.08002f;camera.distance=12;camera.SnapToTarget();
        }
    }
}
