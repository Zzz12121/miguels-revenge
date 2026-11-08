using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
public static class DojoGardenMotionChecks
{
 static Color32[] Capture(Camera cam,string file)
 {
  var rt=RenderTexture.GetTemporary(960,540,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
  try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(960,540,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,960,540),0,0);tex.Apply();var pixels=tex.GetPixels32();File.WriteAllBytes("output/dojo-building/"+file+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);return pixels;}
  finally{cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
 }
 public static async Task<string> Run()
 {
  if(!Application.isPlaying)throw new Exception("Play mode required");
  var root=GameObject.Find("FinalDojoArena/Architecture/LivingGarden");var systems=root.GetComponentsInChildren<ParticleSystem>();
  var beforeParticles=new Dictionary<uint,Vector3>();var buffer=new ParticleSystem.Particle[150];
  foreach(var ps in systems){int count=ps.GetParticles(buffer);for(int i=0;i<count;i++)beforeParticles[buffer[i].randomSeed]=buffer[i].position;}
  var renderers=systems.Select(s=>s.GetComponent<ParticleSystemRenderer>()).ToArray();var enabled=renderers.Select(r=>r.enabled).ToArray();
  var go=new GameObject("Temporary wind verification camera");var cam=go.AddComponent<Camera>();cam.nearClipPlane=.08f;cam.farClipPlane=180;cam.fieldOfView=55;cam.clearFlags=CameraClearFlags.Skybox;go.AddComponent<UniversalAdditionalCameraData>();
  go.transform.position=new Vector3(3.5f,1.1f,34);go.transform.LookAt(new Vector3(7,.25f,41));
  try
  {
   foreach(var r in renderers)r.enabled=false;
   var a=Capture(cam,"11-garden-wind-a");await Task.Delay(2100);var b=Capture(cam,"12-garden-wind-b");
   int changed=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>20)changed++;
   if(changed<250)throw new Exception("Wind not visible in temporal render comparison: "+changed);
   Vector3 drift=Vector3.zero;int tracked=0;
   foreach(var ps in systems){int count=ps.GetParticles(buffer);for(int i=0;i<count;i++)if(beforeParticles.TryGetValue(buffer[i].randomSeed,out var p)){drift+=buffer[i].position-p;tracked++;}}
   if(tracked==0)throw new Exception("No continuous live particles tracked");drift/=tracked;
   if(drift.y>=-.15f||drift.x<=.05f)throw new Exception("Leaves must fall and drift with wind: "+drift);
   var summary=new{success=true,changedPixels=changed,totalPixels=a.Length,trackedLeaves=tracked,averageLeafDrift=drift.ToString(),note="Fixed camera, leaf renderers hidden during wind capture; foliage motion changes pixels. Existing particles tracked separately."};
   string json=Newtonsoft.Json.JsonConvert.SerializeObject(summary,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/garden-motion-checks.json",json);return json;
  }
  finally{for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].enabled=enabled[i];Object.DestroyImmediate(go);}
 }
}
