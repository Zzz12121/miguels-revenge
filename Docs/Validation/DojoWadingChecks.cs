using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class DojoWadingChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task Wait(float seconds)
    {
        float end=Time.time+seconds,deadline=Time.realtimeSinceStartup+25;
        while(Time.time<end){Check(Time.realtimeSinceStartup<deadline,"Editor must keep ticking");await Task.Delay(20);}
    }
    static float Slow(ThirdPersonController motor)=>(float)typeof(ThirdPersonController).GetField("temporarySpeedMultiplier",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(motor);
    static void Capture(Vector3 target)
    {
        var go=new GameObject("Temporary wading capture");var cam=go.AddComponent<Camera>();go.AddComponent<UniversalAdditionalCameraData>();
        cam.nearClipPlane=.1f;cam.farClipPlane=180;cam.fieldOfView=60;go.transform.SetPositionAndRotation(target+new Vector3(5,3.5f,5),Quaternion.LookRotation(new Vector3(-5,-2.8f,-5)));
        var rt=RenderTexture.GetTemporary(1280,800,24);var prior=RenderTexture.active;
        try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes("output/dojo-building/30-player-wading.png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
        finally{cam.targetTexture=null;RenderTexture.active=prior;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(go);}
    }
    public static async Task<string> Run()
    {
        Check(Application.isPlaying,"Play mode required");var water=Object.FindAnyObjectByType<DojoRiverWater>();var p=PlayerHealth.Find();Check(water&&p,"Open dojo architecture scene");
        var cc=p.GetComponent<CharacterController>();var motor=p.GetComponent<ThirdPersonController>();var start=p.transform.position;var rotation=p.transform.rotation;
        var oldCamera=motor.cameraTransform;bool oldInput=motor.inputEnabled,oldWater=water.enabled;var behavior=InputSystem.settings.editorInputBehaviorInPlayMode;
        var kb0=Keyboard.current;var mouse0=Mouse.current;Keyboard kb=null;Mouse mouse=null;var basis=new GameObject("Temporary wading movement basis");basis.transform.rotation=Quaternion.LookRotation(Vector3.right);
        var cursor=Cursor.lockState;bool visible=Cursor.visible;int other=0;
        var pump=typeof(InputSystem).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(InputUpdateType)},null);
        Action<Key[]> keys=pressed=>{InputSystem.QueueStateEvent(kb,new KeyboardState(pressed));InputSystem.QueueStateEvent(mouse,new MouseState());pump.Invoke(null,new object[]{InputUpdateType.Dynamic});kb.MakeCurrent();mouse.MakeCurrent();};
        Action<Vector3> warp=v=>{keys(Array.Empty<Key>());cc.enabled=false;p.transform.position=v;cc.enabled=true;motor.ResetMotion();Physics.SyncTransforms();};
        var dry=new Vector3(-5,1,65);var wet=new Vector3(-5,1,DojoRiverWater.Center(-5));
        Func<Vector3,bool,Task<(float speed,float travel)>> measure=async(pos,sprint)=>
        {
            warp(pos);await Wait(.8f);keys(sprint?new[]{Key.W,Key.LeftShift}:new[]{Key.W});await Wait(.3f);
            var a=p.transform.position;float t=Time.time;await Wait(.35f);float speed=motor.CurrentSpeed;float travel=Vector3.ProjectOnPlane(p.transform.position-a,Vector3.up).magnitude/(Time.time-t);
            keys(Array.Empty<Key>());await Wait(.2f);return(speed,travel);
        };
        try
        {
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            kb=InputSystem.AddDevice<Keyboard>("Dojo wading keyboard");mouse=InputSystem.AddDevice<Mouse>("Dojo wading mouse");InputSystem.EnableDevice(kb);InputSystem.EnableDevice(mouse);
            motor.cameraTransform=basis.transform;motor.inputEnabled=true;
            var dryWalk=await measure(dry,false);Check(!water.IsPlayerWading,"Dry courtyard must not slow player");
            var wetWalk=await measure(wet,false);Check(water.IsPlayerWading&&cc.isGrounded,"Player must wade on riverbed");Capture(p.transform.position);
            var drySprint=await measure(dry,true);var wetSprint=await measure(wet,true);
            Check(Mathf.Abs(wetWalk.speed/dryWalk.speed-water.wadingSpeedMultiplier)<.05f,"Walking must use wading multiplier");
            Check(Mathf.Abs(wetSprint.speed/drySprint.speed-water.wadingSpeedMultiplier)<.05f,"Sprint must use wading multiplier");
            Check(wetWalk.travel<dryWalk.travel*.7f&&wetSprint.travel<drySprint.travel*.7f,"Actual displacement must be slower in water");
            motor.ResetMotion();await Wait(.1f);Check(water.IsPlayerWading&&Mathf.Abs(Slow(motor)-.55f)<.01f,"Reset in water must renew slow");
            other=motor.AddSpeedMultiplierLease(.35f);await Wait(.1f);Check(Mathf.Abs(Slow(motor)-.35f)<.01f,"Preserve stronger unrelated slow");
            water.enabled=false;Check(!water.IsPlayerWading&&Mathf.Abs(Slow(motor)-.35f)<.01f,"Disabling river removes only river slow");
            motor.RemoveSpeedMultiplierLease(other);other=0;Check(Slow(motor)==1,"River disable cleanup");water.enabled=true;await Wait(.1f);Check(water.IsPlayerWading,"River re-enable restores slow");
            motor.enabled=false;await Wait(.1f);Check(!water.IsPlayerWading,"Disabled motor clears river slow");motor.enabled=true;await Wait(.1f);Check(water.IsPlayerWading,"Motor re-enable restores slow");
            keys(new[]{Key.Space});typeof(ThirdPersonController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,null);
            keys(Array.Empty<Key>());await Wait(.3f);Check(p.FeetY>DojoRiverWater.WaterY&&!water.IsPlayerWading,"Actual jump above water releases slow: feet="+p.FeetY+", vertical="+motor.VerticalVelocity+", wading="+water.IsPlayerWading);
            await Wait(1.3f);Check(water.IsPlayerWading,"Landing back in water restores slow");
            warp(new Vector3(12,1,DojoRiverWater.Center(12)));await Wait(.8f);Check(cc.isGrounded&&p.FeetY>0&&!water.IsPlayerWading&&Slow(motor)==1,"Bridge must not slow player");
            warp(dry);await Wait(.7f);Check(!water.IsPlayerWading&&Slow(motor)==1,"Leaving river clears slow");
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{success=true,multiplier=water.wadingSpeedMultiplier,dryWalk=dryWalk.speed,wetWalk=wetWalk.speed,drySprint=drySprint.speed,wetSprint=wetSprint.speed,dryWalkMetresPerSecond=dryWalk.travel,wetWalkMetresPerSecond=wetWalk.travel,drySprintMetresPerSecond=drySprint.travel,wetSprintMetresPerSecond=wetSprint.travel,resetRecovery=true,otherSlowPreserved=true,disableCleanup=true,jumpAndReland=true,bridgeUnaffected=true},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText("output/dojo-building/wading-checks.json",json);return json;
        }
        finally
        {
            if(kb!=null&&mouse!=null)keys(Array.Empty<Key>());if(other!=0)motor.RemoveSpeedMultiplierLease(other);
            motor.enabled=true;motor.cameraTransform=oldCamera;motor.inputEnabled=oldInput;water.enabled=oldWater;
            cc.enabled=false;p.transform.SetPositionAndRotation(start,rotation);cc.enabled=true;motor.ResetMotion();Physics.SyncTransforms();
            if(kb!=null)InputSystem.RemoveDevice(kb);if(mouse!=null)InputSystem.RemoveDevice(mouse);if(kb0!=null&&kb0.added)kb0.MakeCurrent();if(mouse0!=null&&mouse0.added)mouse0.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode=behavior;Cursor.lockState=cursor;Cursor.visible=visible;Object.DestroyImmediate(basis);
        }
    }
}
