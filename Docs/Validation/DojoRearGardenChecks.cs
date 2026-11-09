using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
public static class DojoRearGardenChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static int GroundChecks()
    {
        var rear=GameObject.Find("FinalDojoArena/Architecture/LivingGarden/RearLandscape");Require(rear,"Rear landscape missing");Physics.SyncTransforms();
        int samples=0;
        foreach(float x in new[]{-160f,-90,-52,0,52,90,160})foreach(float z in new[]{-70f,0,60,105,124,180,230})
        {
            var hits=Physics.RaycastAll(new Vector3(x,7,z),Vector3.down,9,~0,QueryTriggerInteraction.Ignore);
            Require(hits.Any(h=>h.collider.name=="ExtendedWalkableGround"),"Exterior floor collision missing "+x+","+z);samples++;
        }
        var boundary=GameObject.Find("FinalDojoArena/Architecture/OuterWallsAndGate").transform;
        Require(boundary.Cast<Transform>().Where(t=>t.name.StartsWith("RearWall")).All(t=>Math.Abs(t.position.z-126.2f)<.01),"Rear depth must expand 21 -> 25.2m");
        Require(boundary.Find("BakedVisuals").GetComponentsInChildren<MeshRenderer>().All(r=>r.bounds.max.z<126.9f),"Extended visual walls must align with rear colliders without compounded extension");
        foreach(string name in new[]{"Project/Environment/Dojo Garden Stream","Project/Environment/Dojo Garden Ground","Project/Environment/Dojo Garden Wind","Project/Environment/Dojo Water Contact"})
        {var shader=Shader.Find(name);Require(shader,"Missing shader "+name);Require(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"),"Shader error "+name);}
        Require(rear.transform.Find("ContinuousGreenTurf")&&rear.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("DenseGrass_")).Sum(f=>f.sharedMesh.vertexCount)>700000,"Dense lawn base and grass geometry required");
        foreach(var f in rear.GetComponentsInChildren<MeshFilter>())Require(f.sharedMesh&&f.GetComponent<MeshRenderer>().sharedMaterial,"Missing persisted mesh or material "+f.name);
        return samples;
    }
    static Color32[] Capture(Camera camera,string file)
    {
        var rt=RenderTexture.GetTemporary(960,600,24);var active=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(960,600,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,960,600),0,0);t.Apply();var p=t.GetPixels32();File.WriteAllBytes("output/dojo-building/"+file+".png",t.EncodeToPNG());Object.DestroyImmediate(t);return p;}
        finally{camera.targetTexture=null;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}
    }
    public static async Task<string> Run()
    {
        Require(Application.isPlaying,"Play mode required");int floorSamples=GroundChecks();
        var player=PlayerHealth.Find();var cc=player.GetComponent<CharacterController>();var movement=player.GetComponent<ThirdPersonController>();var follow=Camera.main.GetComponent<ThirdPersonCamera>();
        Vector3 start=player.transform.position;Quaternion rot=player.transform.rotation;float yaw=follow.yaw,pitch=follow.pitch;var cursor=Cursor.lockState;bool visible=Cursor.visible;
        var kb0=Keyboard.current;var mouse0=Mouse.current;Keyboard kb=null;Mouse mouse=null;var behavior=InputSystem.settings.editorInputBehaviorInPlayMode;
        var pump=typeof(InputSystem).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(InputUpdateType)},null);
        var camGo=new GameObject("Temporary isolated stream motion camera");var cam=camGo.AddComponent<Camera>();cam.nearClipPlane=.01f;cam.fieldOfView=47;cam.farClipPlane=5f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;camGo.AddComponent<UniversalAdditionalCameraData>();
        // Very close top-down view: only the stream fits within the camera's far plane.
        camGo.transform.SetPositionAndRotation(new Vector3(-5,1.5f,DojoRiverWater.Center(-5)),Quaternion.Euler(90,0,0));
        Action<Vector3> warp=p=>{cc.enabled=false;player.transform.position=p;cc.enabled=true;movement.ResetMotion();Physics.SyncTransforms();};
        try
        {
            var a=Capture(cam,"20-stream-flow-a");await Task.Delay(1450);var b=Capture(cam,"21-stream-flow-b");int changed=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>5)changed++;
            Require(changed>1000,"Stream must visibly animate with a fixed camera "+changed);
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            kb=InputSystem.AddDevice<Keyboard>("Rear exterior walk check");InputSystem.EnableDevice(kb);kb.MakeCurrent();mouse=InputSystem.AddDevice<Mouse>("Rear exterior check mouse");InputSystem.EnableDevice(mouse);mouse.MakeCurrent();
            Action<Key[]> keys=pressed=>{if(!kb.enabled)InputSystem.EnableDevice(kb);if(!mouse.enabled)InputSystem.EnableDevice(mouse);InputSystem.QueueStateEvent(kb,new KeyboardState(pressed));InputSystem.QueueStateEvent(mouse,new MouseState());pump.Invoke(null,new object[]{InputUpdateType.Dynamic});kb.MakeCurrent();mouse.MakeCurrent();};
            keys(Array.Empty<Key>());warp(new Vector3(55,1.1f,80));follow.yaw=0;follow.pitch=15;follow.SnapToTarget();await Task.Delay(400);
            Require(cc.isGrounded,"Player must stand outside original narrow map");keys(new[]{Key.W});float end=Time.realtimeSinceStartup+8;
            while(player.transform.position.z<95&&Time.realtimeSinceStartup<end){follow.yaw=0;keys(new[]{Key.W});await Task.Delay(30);}
            keys(Array.Empty<Key>());await Task.Delay(200);Require(player.transform.position.z>95&&cc.isGrounded&&!player.IsDead,"Player must walk along extended exterior without falling: "+player.transform.position+" ground="+cc.isGrounded);
            Vector3 outside=player.transform.position;
            warp(new Vector3(0,1.1f,130));await Task.Delay(500);Require(cc.isGrounded&&player.FeetY>-.1f,"Ground behind expanded rear wall must support player");
            var result=new{success=true,floorSamples,streamChangedPixels=changed,streamPixelCount=a.Length,exteriorWalk=outside.ToString(),rearFeetY=player.FeetY,note="Actual shared player W walk on extended exterior; fixed-camera close water renders isolate flow from foliage."};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/rear-landscape-checks.json",json);return json;
        }
        finally
        {
            if(kb!=null)InputSystem.RemoveDevice(kb);if(mouse!=null)InputSystem.RemoveDevice(mouse);if(kb0!=null&&kb0.added)kb0.MakeCurrent();if(mouse0!=null&&mouse0.added)mouse0.MakeCurrent();InputSystem.settings.editorInputBehaviorInPlayMode=behavior;
            warp(start);player.transform.rotation=rot;follow.yaw=yaw;follow.pitch=pitch;follow.SnapToTarget();Cursor.lockState=cursor;Cursor.visible=visible;Object.DestroyImmediate(camGo);
        }
    }
}
