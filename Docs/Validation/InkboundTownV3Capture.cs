// Body-only live Editor eval. Actual Unity camera renders, no concept/background substitution.
const string folder="output/inkbound-town/v3/captures";
System.IO.Directory.CreateDirectory(folder);
var camera=new GameObject("Temporary V3 review camera").AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
var shots=new[]{
    new{name="01-main-street",p=new Vector3(-44,2.2f,-16),at=new Vector3(-44,1.6f,8)},
    new{name="02-cloth-shop",p=new Vector3(-43,1.9f,-16),at=new Vector3(-49.4f,1.6f,-11)},
    new{name="03-tea-veranda",p=new Vector3(17,2.3f,-24),at=new Vector3(28,1.4f,-24)},
    new{name="04-tea-inside",p=new Vector3(27,1.9f,-24),at=new Vector3(30,1.4f,-27)},
    new{name="05-river",p=new Vector3(-3.3f,2.8f,-50),at=new Vector3(-1,2.1f,-21)},
    new{name="06-quay",p=new Vector3(-10.8f,2.5f,-39),at=new Vector3(-12,2,5)},
    new{name="07-overview",p=new Vector3(-55,40,-45),at=new Vector3(0,1,15)},
    new{name="08-chess",p=new Vector3(49,2,39),at=new Vector3(54,1.1f,43)}
};
void Capture(string name)
{
    UnityEngine.Object.FindAnyObjectByType<InkboundWaterReflection>()?.RefreshFor(camera);
    var rt=RenderTexture.GetTemporary(1920,1080,24);var previous=RenderTexture.active;var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);
    try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();System.IO.File.WriteAllBytes(folder+"/"+name+".png",tex.EncodeToPNG());}
    finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(tex);}
}
async System.Threading.Tasks.Task Run()
{
    try
    {
        foreach(var shot in shots){camera.fieldOfView=Camera.main.fieldOfView;camera.transform.SetPositionAndRotation(shot.p,Quaternion.LookRotation(shot.at-shot.p));Capture(shot.name);await System.Threading.Tasks.Task.Delay(15);}
        System.IO.File.WriteAllText(folder+"/manifest.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{kind="Actual Unity cameras, normal player FOV",resolution="1920x1080",fov=Camera.main.fieldOfView,shots=shots.Select(s=>new{s.name,position=s.p.ToString(),lookAt=s.at.ToString()})},Newtonsoft.Json.Formatting.Indented));
    }
    catch(Exception e){System.IO.File.WriteAllText(folder+"/error.txt",e.ToString());}
    finally{UnityEngine.Object.Destroy(camera.gameObject);}
}
_ = Run();return "Capturing actual V3 views";
