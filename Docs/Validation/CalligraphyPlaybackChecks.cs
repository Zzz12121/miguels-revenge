using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class CalligraphyPlaybackChecks
{
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static async Task Wait(float seconds){float until=Time.time+seconds;var clock=System.Diagnostics.Stopwatch.StartNew();while(Time.time<until){if(clock.Elapsed.TotalSeconds>seconds*4+8)throw new Exception("Editor stopped ticking");await Task.Delay(16);}}
 static CalligraphyBossCombat Setup(int skill)
 {
  Check(EditorApplication.isPlaying,"Enter Play first");Time.timeScale=1;
  var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();var b=range.Boss;
  b.moveSpeed=0;b.dodgeChance=0;b.cycleDemonstration=false;b.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);b.SelectSkill(skill);
  Place(new Vector3(0,1.05f,15));PlayerHealth.Find().GrantDodgeInvulnerability(120);return b;
 }
 static void Place(Vector3 position)
 {var p=PlayerHealth.Find();var c=p.GetComponent<CharacterController>();c.enabled=false;p.transform.position=position;c.enabled=true;p.GetComponent<ThirdPersonController>().ResetMotion();Physics.SyncTransforms();}
 static Vector3 WorldPosition(ParticleSystem ps,ParticleSystem.Particle particle)
 {var main=ps.main;return main.simulationSpace==ParticleSystemSimulationSpace.World?particle.position:(main.simulationSpace==ParticleSystemSimulationSpace.Custom && main.customSimulationSpace?main.customSimulationSpace:ps.transform).TransformPoint(particle.position);}
 static string Capture(string name)
 {
  var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);view.Repaint();
  var rt=type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view) as RenderTexture;Check(rt,"No Game View");
  var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();RenderTexture.active=old;
  if(SystemInfo.graphicsUVStartsAtTop){var p=tex.GetPixels();var flip=new Color[p.Length];for(int y=0;y<rt.height;y++)Array.Copy(p,y*rt.width,flip,(rt.height-1-y)*rt.width,rt.width);tex.SetPixels(flip);tex.Apply();}
  string path="Logs/Playback-"+name+".png";System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.Destroy(tex);return path;
 }
 static void Frame(Vector3 at,Vector3 offset)
 {var c=Camera.main;var orbit=c.GetComponent<ThirdPersonCamera>();if(orbit)orbit.enabled=false;var brain=c.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;c.transform.SetPositionAndRotation(at+offset,Quaternion.LookRotation(-offset));}
 public static async Task<string> InspectPulseShape()
 {
  var b=Setup(7);b.CancelCast();b.enabled=false;await Wait(.05f);
  var fx=CalligraphyVfxLibrary.Play(CalligraphyVfxLibrary.Current.frontalPulses,new Vector3(0,.05f,0),Quaternion.identity,1.8f,1,0,b.EffectsRoot);fx.ConfigureWaves(1,1);
  Frame(Vector3.zero,new Vector3(0,55,-.001f));await Wait(.3f);Capture("pulse-shape");
  var mesh=new Mesh();var buffer=new ParticleSystem.Particle[256];var s=new StringBuilder();
  foreach(var ps in fx.Systems.Where(p=>p.name.StartsWith("yy_Shockwave_1")))
  {
   var r=ps.GetComponent<ParticleSystemRenderer>();int count=ps.GetParticles(buffer);r.BakeMesh(mesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);
   s.AppendLine(ps.name+" rot="+ps.transform.eulerAngles+" mesh="+r.mesh?.name+" originalBounds="+r.mesh?.bounds+" actualBounds="+mesh.bounds+" material="+r.sharedMaterial?.name);
   for(int i=0;i<count;i++)s.AppendLine("pos="+WorldPosition(ps,buffer[i])+" rot="+buffer[i].rotation3D+" currentSize="+buffer[i].GetCurrentSize3D(ps));
  }
  await Wait(.5f);Capture("pulse-shape-later");UnityEngine.Object.Destroy(mesh);b.enabled=true;b.StopBattle();return s.ToString();
 }
 public static async Task<string> AuditPulses()
 {
  var b=Setup(7);b.CancelCast();b.enabled=false;
  var fx=CalligraphyVfxLibrary.Play(CalligraphyVfxLibrary.Current.frontalPulses,new Vector3(0,.05f,0),Quaternion.Euler(0,-180,0),1.8f,1,0,b.EffectsRoot);fx.ConfigureWaves(1,1);
  ParticleSystem ps=null;var particles=new ParticleSystem.Particle[256];float until=Time.time+3;
  while(Time.time<until && !ps){ps=fx.Systems.FirstOrDefault(s=>s.name.StartsWith("yy_Shockwave_1") && s.particleCount>0);if(!ps)await Wait(.01f);}
  Check(ps,"No live pulse");ps.GetParticles(particles);var seed=particles[0].randomSeed;var before=WorldPosition(ps,particles[0]);
  var mesh=new Mesh();var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.BakeMesh(mesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);var center=renderer.transform.position+mesh.bounds.center;
  await Wait(.2f);int count=ps.GetParticles(particles);var particle=particles.Take(count).First(p=>p.randomSeed==seed);var after=WorldPosition(ps,particle);
  renderer.BakeMesh(mesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);var movedCenter=renderer.transform.position+mesh.bounds.center;
  var s=new StringBuilder();s.AppendLine("root forward="+fx.transform.forward+" right="+fx.transform.right+" ps rotation="+ps.transform.eulerAngles+" sim="+ps.main.simulationSpace);
  s.AppendLine("particle displacement="+(after-before)+" velocity="+particle.velocity+" total="+particle.totalVelocity);
  s.AppendLine("visible mesh center displacement="+(movedCenter-center));
  s.AppendLine("beam prefab core="+string.Join(";",CalligraphyVfxLibrary.Current.beam.GetComponentsInChildren<ParticleSystem>().Where(p=>p.name.StartsWith("Ice_beam_center")).Select(p=>p.name+" delay="+p.main.startDelay.constantMax+" life="+p.main.startLifetime.constantMax+" size="+p.main.startSize3D+":"+p.main.startSize.constantMax)));
  UnityEngine.Object.Destroy(mesh);b.enabled=true;b.StopBattle();return s.ToString();
 }
 public static string SaveRangeDefaults()
 {
  Check(!EditorApplication.isPlaying,"Stop Play before saving");var b=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossCombat>();
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab").GetComponent<CalligraphyBossCombat>();
  Undo.RecordObject(b,"Remove saved diagnostic motion/cooldown values");b.moveSpeed=prefab.moveSpeed;b.recoverySeconds=prefab.recoverySeconds;b.dodgeChance=prefab.dodgeChance;
  EditorUtility.SetDirty(b);EditorSceneManager.MarkSceneDirty(b.gameObject.scene);EditorSceneManager.SaveScene(b.gameObject.scene);
  return "Saved scene moveSpeed="+b.moveSpeed+" recoverySeconds="+b.recoverySeconds+" dodgeChance="+b.dodgeChance;
 }
 public static async Task<string> ManualPlayback()
 {
  var b=Setup(0);Check(b.recoverySeconds<5,"Diagnostic recovery=100 still saved");
  float until=Time.time+5;while(!b.WaitingForPreview && Time.time<until)await Wait(.02f);
  Check(b.WaitingForPreview && !b.IsCasting && b.SelectedSkill==0,"Single shot did not finish into standby");
  await Wait(1.5f);Check(b.WaitingForPreview && b.SelectedSkill==0,"Preview automatically advanced");
  b.ReplaySkill();Check(!b.WaitingForPreview && b.SelectedSkill==0,"Replay failed to arm same skill");
  await Wait(.5f);Check(b.IsCasting,"Replay stuck on long recovery");
  until=Time.time+5;while(!b.WaitingForPreview && Time.time<until)await Wait(.02f);Check(b.WaitingForPreview,"Replay never completed");
  b.NextSkill();Check(b.SelectedSkill==1 && !b.WaitingForPreview,"Next did not arm next skill");
  until=Time.time+7;while(!b.WaitingForPreview && Time.time<until)await Wait(.02f);
  Check(b.WaitingForPreview && b.SelectedSkill==1,"Combo did not return to standby");
  b.StopBattle();return "PASS single/combo play once then standby; replay keeps selection; next advances; no 100-second stall";
 }
 public static async Task<string> BeamStability()
 {
  var b=Setup(5);Frame(new Vector3(0,1.5f,10),new Vector3(12,6,-12));
  CalligraphyPackageEffect fx=null;float until=Time.time+4;
  while(!fx && Time.time<until){fx=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().FirstOrDefault(p=>p.name.StartsWith("Ying Yang Beam"));if(!fx)await Wait(.01f);}
  Check(fx,"No Beam charge");Vector3 origin=fx.transform.position;var tip=b.brush.brushTip.position;
  await Wait(.35f);Check(Vector3.Distance(fx.transform.position,origin)<.01f,"Charge moved with brush");
  Check(Vector3.Distance(b.brush.brushTip.position,tip)>.15f,"Fixture did not observe moving brush");Capture("body-charge");
  while(fx && fx.Age<3.3f)await Wait(.01f);Check(fx && b.IsCasting,"Beam ended before firing");
  var rotation=fx.transform.rotation;origin=fx.transform.position;var scale=fx.transform.localScale;
  var core=fx.Systems.Single(p=>p.name.StartsWith("Ice_beam_center"));var buffer=new ParticleSystem.Particle[32];int count=core.GetParticles(buffer);Check(count>0,"Beam core not visible");
  Place(new Vector3(20,1.05f,12));await Wait(.5f);
  Check(fx && b.IsCasting && Vector3.Distance(fx.transform.position,origin)<.01f && Quaternion.Angle(fx.transform.rotation,rotation)<.01f,"Dodging altered locked beam");
  Check(Vector3.Distance(fx.transform.localScale,scale)<.002f,"Dodging shrank beam: "+scale+" -> "+fx.transform.localScale);Capture("beam-dodged");
  Place(new Vector3(-18,1.05f,8));await Wait(.6f);
  Check(fx && b.IsCasting && Vector3.Distance(fx.transform.localScale,scale)<.002f,"Beam weakened with no target in path");
  until=Time.time+16;while(!b.WaitingForPreview && Time.time<until)await Wait(.02f);
  Check(b.WaitingForPreview && !fx && b.SelectedSkill==5,"Beam did not finish naturally into standby");
  b.ReplaySkill();await Wait(.5f);Check(b.IsCasting && b.SelectedSkill==5,"Beam cannot replay after cleanup");b.StopBattle();
  return "PASS charge fixed at body while brush swings; fired beam position/rotation/scale stable after two dodges; waits for natural cleanup then standby/replay";
 }
 public static async Task<string> ManualSkill(int skill)
 {
  var b=Setup(skill);float until=Time.time+(skill==3?35:20);
  while(!b.WaitingForPreview && Time.time<until)await Wait(.02f);
  Check(b.WaitingForPreview && !b.IsCasting && b.SelectedSkill==skill,"Skill stuck or auto-selected another: "+skill);
  await Wait(.6f);Check(b.WaitingForPreview && b.SelectedSkill==skill,"Skill auto-restarted: "+skill);
  b.ReplaySkill();await Wait(.5f);Check(b.IsCasting && b.SelectedSkill==skill,"Cannot replay skill "+skill);b.StopBattle();
  return "PASS skill "+skill+" finished normally, remained selected/idle, and replayed";
 }
 public static async Task<string> ArrowKeys()
 {
  var b=Setup(0);float until=Time.time+5;while(!b.WaitingForPreview && Time.time<until)await Wait(.02f);Check(b.WaitingForPreview,"No idle preview");
  var original=UnityEngine.InputSystem.Keyboard.current;
  var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
  try
  {
   UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.UpArrow));await Wait(.05f);
   UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());await Wait(.5f);
   Check(b.SelectedSkill==0 && b.IsCasting,"Actual Up input did not replay current skill");
   UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.DownArrow));await Wait(.05f);
   UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());await Wait(.5f);
   Check(b.SelectedSkill==1 && b.IsCasting,"Actual Down input did not select/play next skill");b.StopBattle();
   return "PASS actual Input System Up replays current; Down selects and plays next";
  }
  finally{UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);if(original!=null)original.MakeCurrent();}
 }
}
