using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

public static class InkboundTownV3FocusedChecks
{
    const string Folder="output/inkbound-town/v3/focused";
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Warp(PlayerHealth p,Vector3 point)
    {var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=point;p.GetComponent<ThirdPersonController>().ResetMotion();cc.enabled=true;Camera.main.GetComponent<ThirdPersonCamera>().SnapToTarget();}
    static void Capture(string name)
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=Resources.FindObjectsOfTypeAll(type).First();var rt=(RenderTexture)type.GetField("m_RenderTexture",Private).GetValue(view);
        var previous=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();if(SystemInfo.graphicsUVStartsAtTop){var bytes=image.GetRawTextureData<byte>();int row=rt.width*3;for(int y=0;y<rt.height/2;y++)for(int x=0;x<row;x++){int a=y*row+x,b=(rt.height-1-y)*row+x;byte v=bytes[a];bytes[a]=bytes[b];bytes[b]=v;}image.Apply();}File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}
        finally{RenderTexture.active=previous;Object.Destroy(image);}
    }
    public static async Task<string> Run()
    {
        Directory.CreateDirectory(Folder);var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();bool input=motor.inputEnabled,ai=motor.aiControlled;
        var director=Object.FindAnyObjectByType<InkboundTownEncounter>();var boss=director.elder;var camera=Camera.main.GetComponent<ThirdPersonCamera>();var guard=new GameObject("Temporary focused check guard");
        var result=new Dictionary<string,object>();var locks=new List<object>();InkboundDisturbanceSource[] sources=null;GameObject probe=null,second=null;int budget=InkboundEcho.HighQualityBudget;
        try
        {
            p.SetPracticeProtection(guard,true);motor.inputEnabled=true;motor.aiControlled=true;motor.SetAIInput(Vector3.zero);director.BeginFight();
            foreach(var point in new[]{new Vector3(-44,0,0),new Vector3(-11,0,-30),new Vector3(28,.18f,-24),new Vector3(-57,0,-22)})
            {
                boss.CancelCast();typeof(InkboundTownEncounter).GetField("awaitingRecovery",Private).SetValue(director,false);typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(director,float.PositiveInfinity);
                Warp(p,point+Vector3.up*1.05f);bool tea=point.x>0;UnityEngine.AI.NavMesh.SamplePosition(point+(tea?Vector3.right*3:Vector3.forward*5),out var ground,2,UnityEngine.AI.NavMesh.AllAreas);director.agent.Warp(ground.position);boss.transform.position=ground.position;
                camera.ClearLock();camera.yaw=tea?90:0;camera.pitch=10;camera.SnapToTarget();await Task.Delay(500);
                typeof(ThirdPersonCamera).GetMethod("AcquireLock",Private).Invoke(camera,new object[]{false});bool acquired=camera.LockedTarget&&camera.LockedTarget.IsChildOf(boss.transform);await Task.Delay(500);
                locks.Add(new{zone=point.ToString(),acquired,retained=camera.LockedTarget&&camera.LockedTarget.IsChildOf(boss.transform)});
            }
            result["stationaryReacquisition"]=locks;
            Warp(p,new Vector3(-44,1.05f,0));director.agent.Warp(new Vector3(-44,.225f,6));boss.transform.position=director.agent.nextPosition;camera.ClearLock();camera.yaw=0;camera.SnapToTarget();
            typeof(InkboundTownEncounter).GetField("decision",Private).SetValue(director,Time.time+.1f);await Task.Delay(1000);Capture("09-boss-game-view");
            boss.CancelCast();director.enabled=false;director.agent.isStopped=true;
            sources=Object.FindObjectsByType<InkboundDisturbanceSource>().Where(s=>s.enabled).ToArray();foreach(var s in sources)s.enabled=false;
            var echo=Object.FindObjectsByType<InkboundEcho>().First(e=>e.name.StartsWith("Market passing life"));var skin=echo.GetComponentInChildren<SkinnedMeshRenderer>();
            Warp(p,echo.transform.position+new Vector3(0,1.05f,-3));camera.ClearLock();camera.yaw=0;camera.pitch=5;camera.distance=3;camera.SnapToTarget();await Task.Delay(300);
            probe=new GameObject("Temporary contact stress source");second=new GameObject("Temporary second contact stress source");var a=probe.AddComponent<InkboundDisturbanceSource>();var b=second.AddComponent<InkboundDisturbanceSource>();b.enabled=false;
            float until=Time.time+1.2f,minimum=1;int before=echo.Contacts;
            while(Time.time<until){Vector3 c=skin.bounds.center;a.SetVisualSweep(c-Vector3.up*.4f,c+Vector3.up*.4f,.65f,Vector3.right*5);minimum=Mathf.Min(minimum,echo.Disturbance);await Task.Delay(20);}
            bool held=echo.Disturbance>.4f;Capture("10-echo-held");
            b.enabled=true;until=Time.time+.8f;while(Time.time<until){Vector3 c=skin.bounds.center;a.SetVisualSweep(c-Vector3.up*.4f,c+Vector3.up*.4f,.65f,Vector3.right*5);b.SetVisualSweep(c+Vector3.right*.3f,c+Vector3.up*.4f,.4f,Vector3.back*6);await Task.Delay(20);}
            Capture("11-echo-two-contacts");a.enabled=false;b.enabled=false;await Task.Delay(4300);bool returned=echo.Disturbance==0;Capture("12-echo-recovered");
            InkboundEcho.HighQualityBudget=0;a.enabled=true;until=Time.time+.4f;while(Time.time<until){Vector3 c=skin.bounds.center;a.SetVisualSweep(c,c+Vector3.up*.5f,.65f,Vector3.right*5);await Task.Delay(20);}
            var positions=(Vector3[])typeof(InkboundEcho).GetField("positions",Private).GetValue(echo);
            result["continuousAndRepeatedInk"]=new{held,twoSources=true,returned,repeatedContact=echo.Contacts>before,lightweight=!echo.HighQuality,samples=positions.Length,withinBudget=positions.Length<=InkboundEcho.LightweightSamples,echo.MaximumOffset};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/checks.json",json);return json;
        }
        finally
        {
            InkboundEcho.HighQualityBudget=budget;if(probe)Object.Destroy(probe);if(second)Object.Destroy(second);if(sources!=null)foreach(var s in sources)if(s)s.enabled=true;
            director.enabled=true;motor.aiControlled=ai;motor.inputEnabled=input;p.SetPracticeProtection(guard,false);Object.Destroy(guard);camera.distance=7;
        }
    }
}
