using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class InkboundTownVisualFixChecks
{
    const string Folder="output/inkbound-town/visual-fixes";
    public static string Inspect()
    {
        Directory.CreateDirectory(Folder);
        var p=PlayerHealth.Find();var camera=Object.FindAnyObjectByType<ThirdPersonCamera>();
        var report=new{playing=Application.isPlaying,scene=SceneManager.GetActiveScene().path,dirty=SceneManager.GetActiveScene().isDirty,
            player=p?p.transform.position.ToString("F5"):null,yaw=camera?camera.yaw:0,pitch=camera?camera.pitch:0,
            trees=Object.FindObjectsByType<Transform>().Where(t=>t.name.StartsWith("IT_V3_Willow")||t.name.StartsWith("IT_V3_OldCamphor")).Select(t=>new{t.name,p=t.position.ToString(),wood=t.GetComponentsInChildren<MeshFilter>().Where(m=>m.name.Contains("trunk")).Select(m=>new{m.name,minimum=m.transform.TransformPoint(m.sharedMesh.vertices.OrderBy(v=>v.y).First()).y}).ToArray()}).ToArray(),
            workers=Object.FindObjectsByType<NpcRoutine>().Where(n=>n.name.Contains("Dock net")).Select(n=>new{n.name,p=n.transform.position.ToString(),stops=n.stops.Select(s=>s.position.ToString()).ToArray(),ground=Physics.RaycastAll(n.transform.position+Vector3.up*4,Vector3.down,7,~0,QueryTriggerInteraction.Ignore).Select(h=>new{h.collider.name,y=h.point.y}).ToArray()}).ToArray()};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/before.json",json);return json;
    }
    static void Shot(string name,Vector3 point,Vector3 target)
    {
        var c=new GameObject("Temporary visual correction camera").AddComponent<Camera>();c.CopyFrom(Camera.main);c.enabled=false;c.fieldOfView=60;c.transform.SetPositionAndRotation(point,Quaternion.LookRotation(target-point));
        var rt=RenderTexture.GetTemporary(1280,720,24);var old=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{Object.FindAnyObjectByType<InkboundWaterReflection>()?.RefreshFor(c);c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(image);Object.Destroy(c.gameObject);}
    }
    static void Warp(PlayerHealth p,Vector3 point)
    {var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=point;p.GetComponent<ThirdPersonController>().ResetMotion();cc.enabled=true;}
    public static async Task<string> Verify()
    {
        Directory.CreateDirectory(Folder);var settings=Object.FindAnyObjectByType<InkboundTownInkSettings>();var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();bool input=motor.inputEnabled;
        var camera=Camera.main.GetComponent<ThirdPersonCamera>();float yaw=camera.yaw,pitch=camera.pitch,distance=camera.distance;Vector3 position=p.transform.position;
        var director=Object.FindAnyObjectByType<InkboundTownEncounter>();bool enabled=director.enabled;var combat=director.elder;bool combatEnabled=combat.enabled;
        var sources=Object.FindObjectsByType<InkboundDisturbanceSource>().Where(s=>s.enabled).ToArray();var probe=new GameObject("Temporary visual contact check");var contact=probe.AddComponent<InkboundDisturbanceSource>();contact.enabled=false;float speed=settings.inkPlaybackSpeed;
        try
        {
            motor.inputEnabled=false;director.enabled=false;combat.CancelCast();combat.enabled=false;foreach(var s in sources)s.enabled=false;
            await Task.Delay(500);
            var groups=Object.FindObjectsByType<InkboundPassableGroup>();var residents=Object.FindObjectsByType<InkboundEcho>().Where(e=>e.GetComponentInChildren<SkinnedMeshRenderer>()).ToArray();
            var missing=groups.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.GetComponent<MeshFilter>()&&!r.GetComponentInParent<InkboundEcho>()).Distinct().Select(r=>r.name).ToArray();
            var wood=Object.FindObjectsByType<MeshFilter>().Where(m=>m.name=="Naturally joined trunk and branches").Select(m=>new{name=m.transform.parent.name,baseY=m.sharedMesh.vertices.Min(v=>m.transform.TransformPoint(v).y),rootY=m.transform.parent.position.y}).ToArray();
            var workers=Object.FindObjectsByType<NpcRoutine>().Where(n=>n.name=="Dock net repair").Select(n=>new{n.name,n.transform.position.y,scale=n.transform.localScale.x,head=n.animator.GetBoneTransform(HumanBodyBones.Head).position.y}).ToArray();
            Shot("01-grounded-willow",new Vector3(-17,2.6f,20),new Vector3(-11,1.3f,26));
            Shot("02-net-worker",new Vector3(-14.5f,2.1f,-42),new Vector3(-10.68f,.65f,-39));
            var echo=residents.First(e=>e.name.StartsWith("Market passing life"));var skin=echo.GetComponentInChildren<SkinnedMeshRenderer>();
            var releases=new List<object>();
            foreach(float rate in new[]{1f,.5f})
            {
                settings.inkPlaybackSpeed=rate;Warp(p,echo.transform.position+new Vector3(0,1.05f,-4));camera.ClearLock();camera.yaw=0;camera.pitch=10;camera.distance=4;camera.SnapToTarget();
                contact.enabled=true;float until=Time.time+.7f/rate;
                while(Time.time<until){var c=skin.bounds.center;contact.SetVisualSweep(c-Vector3.up*.4f,c+Vector3.up*.4f,.65f,Vector3.right*6);await Task.Delay(20);}
                if(rate==.5f)Shot("03-half-speed-contact",echo.transform.position+new Vector3(-1,1.7f,-3),skin.bounds.center);
                float peak=echo.Disturbance;contact.enabled=false;float start=Time.time;while(echo.Disturbance>.001f&&Time.time-start<10)await Task.Delay(20);
                releases.Add(new{playbackSpeed=rate,peak,releaseSeconds=Time.time-start,returned=echo.Disturbance==0});
                if(rate==.5f)Shot("04-half-speed-return",echo.transform.position+new Vector3(-1,1.7f,-3),skin.bounds.center);
            }
            var prop=groups.Where(g=>g.name=="Passable daily life furnishings").SelectMany(g=>g.GetComponentsInChildren<InkboundEcho>()).First(e=>e.GetComponent<MeshRenderer>().sharedMaterial.name=="M_IT_BrushTimber");
            var filter=prop.GetComponent<MeshFilter>();Vector3 local=filter.sharedMesh.vertices.First(v=>v.y>.6f&&v.y<1.4f);Vector3 point=filter.transform.TransformPoint(local);
            Warp(p,new Vector3(point.x,1.05f,point.z-3));camera.yaw=0;camera.pitch=5;camera.SnapToTarget();await Task.Delay(400);
            contact.enabled=true;float done=Time.time+1.5f,peakProp=0;
            while(Time.time<done){contact.SetVisualSweep(point-Vector3.up*.2f,point+Vector3.up*.2f,.65f,Vector3.right*5);peakProp=Mathf.Max(peakProp,prop.Disturbance);await Task.Delay(20);}
            bool plume=prop.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name=="Returning ink filaments"&&r.enabled);
            Shot("05-passable-timber",point+new Vector3(-1,1,-3),point);contact.enabled=false;
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{residents=residents.Length,allScaled=residents.All(r=>Mathf.Abs(r.transform.localScale.x-1.1f)<.001f),passableGroups=groups.Length,missingEcho=missing,trees=wood,workers,releases,timberEffect=new{peak=peakProp,visiblePlume=plume}},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Folder+"/checks.json",json);return json;
        }
        finally
        {
            settings.inkPlaybackSpeed=speed;Object.Destroy(probe);foreach(var s in sources)if(s)s.enabled=true;combat.enabled=combatEnabled;director.enabled=enabled;motor.inputEnabled=input;
            Warp(p,position);camera.yaw=yaw;camera.pitch=pitch;camera.distance=distance;camera.SnapToTarget();
        }
    }
    public static object RestoreUserView()
    {
        var p=PlayerHealth.Find();Warp(p,new Vector3(-11.82506f,1.05f,-41.412f));var c=Camera.main.GetComponent<ThirdPersonCamera>();c.ClearLock();c.yaw=617.522461f;c.pitch=70;c.SnapToTarget();return "Returned to the user's quay view";
    }
    public static object SaveWorkerGrounding()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        foreach(var n in Object.FindObjectsByType<NpcRoutine>().Where(n=>n.name=="Dock net repair"))
        {
            for(int i=0;i<n.stops.Length;i++)n.stops[i].position.y=.08f;
            Vector3 p=n.transform.position;p.y=.08f;n.transform.position=p;EditorUtility.SetDirty(n);PrefabUtility.RecordPrefabInstancePropertyModifications(n.transform);
        }
        var scene=SceneManager.GetActiveScene();UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);return "Saved shoe-grounded crouching workers";
    }
    public static async Task<string> VerifyTimberAndShoes()
    {
        var root=Object.FindObjectsByType<InkboundPassableGroup>().First(g=>g.name=="Passable daily life furnishings"&&Mathf.Abs(g.transform.position.x+51.1f)<.1f&&Mathf.Abs(g.transform.position.z+12)<.1f);
        var echo=root.GetComponentsInChildren<InkboundEcho>().First(e=>e.GetComponent<MeshRenderer>().sharedMaterial.name=="M_IT_BrushTimber");var filter=echo.GetComponent<MeshFilter>();
        Vector3 point=filter.sharedMesh.vertices.Select(v=>filter.transform.TransformPoint(v)).Where(v=>v.y>.6f&&v.y<1.2f).OrderBy(v=>(v-new Vector3(-46.7f,.9f,-12)).sqrMagnitude).First();
        var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();bool input=motor.inputEnabled;var source=new GameObject("Temporary timber contact inspection");var contact=source.AddComponent<InkboundDisturbanceSource>();
        try
        {
            motor.inputEnabled=false;Warp(p,new Vector3(point.x+3,1.05f,point.z));var camera=Camera.main.GetComponent<ThirdPersonCamera>();camera.ClearLock();camera.yaw=270;camera.pitch=10;camera.SnapToTarget();
            float until=Time.time+1.6f;while(Time.time<until){contact.SetVisualSweep(point-Vector3.up*.35f,point+Vector3.up*.35f,.65f,Vector3.forward*5);await Task.Delay(20);}
            Shot("05-passable-timber",point+new Vector3(3,1.2f,-1),point);Shot("02-net-worker",new Vector3(-14.5f,2.1f,-42),new Vector3(-10.68f,.65f,-39));
            var workers=Object.FindObjectsByType<NpcRoutine>().Where(n=>n.name=="Dock net repair").Select(n=>new{n.transform.position.y,leftFoot=n.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,leftToe=n.animator.GetBoneTransform(HumanBodyBones.LeftToes).position.y}).ToArray();
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{timberDisturbance=echo.Disturbance,workers},Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/final-grounding.json",json);return json;
        }
        finally{Object.Destroy(source);motor.inputEnabled=input;RestoreUserView();}
    }
    public static async Task<string> VerifyQualitySwitching()
    {
        typeof(Editor).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear").Invoke(null,null);
        var echo=Object.FindObjectsByType<InkboundEcho>().First(e=>e.name.StartsWith("Market passing life"));var skin=echo.GetComponentInChildren<SkinnedMeshRenderer>();
        var source=new GameObject("Temporary quality-switch contact");var contact=source.AddComponent<InkboundDisturbanceSource>();int budget=InkboundEcho.HighQualityBudget;var counts=new List<int>();
        try
        {
            for(int i=0;i<6;i++)
            {
                InkboundEcho.HighQualityBudget=i%2==0?0:100;echo.enabled=false;echo.enabled=true;
                float until=Time.time+.23f;while(Time.time<until){Vector3 p=skin.bounds.center;contact.SetVisualSweep(p-Vector3.up*.3f,p+Vector3.up*.3f,.65f,Vector3.right*5);await Task.Delay(20);}
                var field=typeof(InkboundEcho).GetField("positions",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);counts.Add(((Vector3[])field.GetValue(echo)).Length);
            }
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{sampleCounts=counts,switches=counts.Distinct().Count()==2});File.WriteAllText(Folder+"/quality-switching.json",json);return json;
        }
        finally{InkboundEcho.HighQualityBudget=budget;Object.Destroy(source);}
    }
}
