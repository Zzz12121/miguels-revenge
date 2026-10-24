using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class CalligraphyRangeChecks
{
 static string Path(Transform t)=>t.parent?Path(t.parent)+"/"+t.name:t.name;
 public static string Audit()
 {
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var s=new StringBuilder(scene.path+" dirty="+scene.isDirty+"\n");
  foreach(var root in scene.GetRootGameObjects())
  {
   s.AppendLine(root.name+" pos="+root.transform.position+" scale="+root.transform.localScale+" active="+root.activeSelf+" components="+string.Join(",",root.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name)));
   foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.parent==root.transform))s.AppendLine("  "+t.name+" pos="+t.position+" scale="+t.localScale);
  }
  foreach(var c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
   if(c is BoxCollider && c.bounds.size.x>30)s.AppendLine("FLOOR "+Path(c.transform)+" "+c.bounds);
  foreach(var b in UnityEngine.Object.FindObjectsByType<CalligraphyBrushPresentation>(FindObjectsInactive.Include))
   s.AppendLine("ELDER "+Path(b.transform)+" prefab="+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(b)+" combat="+b.GetComponent<CalligraphyBossCombat>());
  return s.ToString();
 }
 public static string Arrange()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stop Play before arranging the range");
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  if(scene.path!="Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity")throw new Exception("Wrong scene");
  var roots=scene.GetRootGameObjects();var stage=roots.Single(g=>g.name=="VoidRend_Stage").transform;
  var floor=stage.Find("Reflective Dark Floor");
  if(floor.localScale.x!=8 && floor.localScale.x!=20)throw new Exception("Unexpected floor size; inspect first");
  Undo.RegisterFullObjectHierarchyUndo(stage.gameObject,"Expand skill range to 200m");
  floor.localScale=new Vector3(20,floor.localScale.y,20);
  foreach(Transform wall in stage.Find("InvisibleBoundaryWalls"))
  {
   var p=wall.localPosition;var scale=wall.localScale;
   if(wall.name=="North" || wall.name=="South"){p.z=wall.name=="North"?101.5f:-101.5f;scale.x=208;scale.z=4;}
   else if(wall.name=="East" || wall.name=="West"){p.x=wall.name=="East"?101.5f:-101.5f;scale.z=208;scale.x=4;}
   wall.localPosition=p;wall.localScale=scale;
  }
  Transform Zone(string name,Vector3 position)
  {var zone=stage.Find(name);if(!zone){zone=new GameObject(name).transform;Undo.RegisterCreatedObjectUndo(zone.gameObject,"Create range zone");zone.SetParent(stage,false);}zone.localPosition=position;return zone;}
  var targets=Zone("Target practice zone",new Vector3(-65,0,0));
  string[] names={"VoidRend_DummyTarget","VoidRend_Target_Left","VoidRend_Target_Right","VoidRend_Target_Moving"};
  Vector3[] positions={new(0,1,2.25f),new(-4.5f,1,4.8f),new(4.5f,1,5.5f),new(0,1,12.5f)};
  for(int i=0;i<names.Length;i++)
  {
   var target=roots.FirstOrDefault(g=>g.name==names[i])?.transform??targets.Find(names[i]);
   if(!target)throw new Exception("Missing target "+names[i]);Undo.SetTransformParent(target,targets,"Move target to practice zone");target.localPosition=positions[i];
  }
  var terrain=Zone("Terrain practice zone",new Vector3(65,0,-10));
  for(int i=1;i<=8;i++)
  {
   var t=stage.Find("Floating Architecture "+i)??terrain.Find("Floating Architecture "+i);if(!t)continue;
   float y=t.position.y;Undo.SetTransformParent(t,terrain,"Move terrain to side zone");t.localPosition=new Vector3(i<=4?-6:6,y,((i-1)%4)*4);
  }
  for(int i=1;i<=7;i++){var t=stage.Find("Background Pillar "+i);t.localPosition=new Vector3((i-4)*25,t.localPosition.y,85);}
  var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();
  var boss=UnityEngine.Object.FindObjectsByType<CalligraphyBossCombat>(FindObjectsInactive.Include).Single();
  Undo.RegisterFullObjectHierarchyUndo(boss.gameObject,"Keep one central elder");boss.gameObject.name="CalligraphyElder";boss.transform.SetPositionAndRotation(new Vector3(0,0,8),Quaternion.Euler(0,180,0));
  var defaults=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab").GetComponent<CalligraphyBossCombat>();
  boss.frontalPulses.vfxScale=defaults.frontalPulses.vfxScale;boss.leapSlam.wave.vfxScale=defaults.leapSlam.wave.vfxScale;
  foreach(var display in UnityEngine.Object.FindObjectsByType<CalligraphyBrushPresentation>(FindObjectsInactive.Include))
   if(display!=boss.brush)Undo.DestroyObjectImmediate(display.gameObject);
  var player=UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();Undo.RecordObject(player.transform,"Central player start");player.transform.SetPositionAndRotation(new Vector3(0,1.05f,-10),Quaternion.identity);
  var camera=Camera.main;var orbit=camera.GetComponent<ThirdPersonCamera>();Undo.RecordObject(camera,"Range camera view");
  camera.farClipPlane=400;camera.transform.SetPositionAndRotation(new Vector3(0,4,-17),Quaternion.LookRotation(new Vector3(0,1,0)-new Vector3(0,4,-17)));
  if(orbit){Undo.RecordObject(orbit,"Enable player range camera");orbit.enabled=true;orbit.target=player.transform;}
  var brain=camera.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain){Undo.RecordObject(brain,"Enable range camera follow");brain.enabled=true;}
  Undo.RecordObject(range,"Bind the same scene elder");range.existingDisplay=boss.gameObject;range.spawnPosition=boss.transform.position;
  EditorUtility.SetDirty(boss);EditorUtility.SetDirty(range);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  return "Saved range 80m -> 200m, target zone x=-65, terrain zone x=65, central elder and player, one elder instance";
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static async Task Wait(float seconds)
 {var clock=System.Diagnostics.Stopwatch.StartNew();float until=Time.time+seconds;while(Time.time<until){if(clock.Elapsed.TotalSeconds>seconds*5+10)throw new Exception("Editor stopped ticking");await Task.Delay(16);}}
 static void Place(PlayerHealth player,Vector3 position)
 {var cc=player.GetComponent<CharacterController>();cc.enabled=false;player.transform.position=position;cc.enabled=true;player.GetComponent<ThirdPersonController>().ResetMotion();Physics.SyncTransforms();}
 static void Frame(Vector3 at,Vector3 offset)
 {var cam=Camera.main;var orbit=cam.GetComponent<ThirdPersonCamera>();if(orbit)orbit.enabled=false;var brain=cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;cam.transform.SetPositionAndRotation(at+offset,Quaternion.LookRotation(-offset));}
 static string Capture(string name)
 {
  var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);view.Repaint();
  var rt=type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view) as RenderTexture;
  Check(rt,"Game View not rendered");var old=RenderTexture.active;RenderTexture.active=rt;
  var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();RenderTexture.active=old;
  if(SystemInfo.graphicsUVStartsAtTop){var pixels=tex.GetPixels();var flipped=new Color[pixels.Length];for(int y=0;y<rt.height;y++)Array.Copy(pixels,y*rt.width,flipped,(rt.height-y-1)*rt.width,rt.width);tex.SetPixels(flipped);tex.Apply();}
  string path="Logs/RangeUpdate-"+name+".png";System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.Destroy(tex);return path;
 }
 static CalligraphyBossRangeTest Range()
 {Check(EditorApplication.isPlaying,"Enter Play Mode first");Time.timeScale=1;var r=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();Check(r,"Wrong scene");return r;}
 public static async Task<string> LayoutAndFacing()
 {
  var range=Range();var player=PlayerHealth.Find();var original=range.Boss;range.StartTest();
  Check(range.Boss==original,"Start created another elder");range.StopTest();range.StartTest();
  Check(range.Boss==original && UnityEngine.Object.FindObjectsByType<CalligraphyBossCombat>().Length==1,"Stop/start duplicated elder");
  var b=range.Boss;b.moveSpeed=0;b.dodgeChance=0;b.cycleDemonstration=false;b.recoverySeconds=100;b.SelectSkill(1);
  b.transform.SetPositionAndRotation(new Vector3(0,0,8),Quaternion.identity);Place(player,new Vector3(0,1.05f,25));player.GrantDodgeInvulnerability(30);
  await Wait(1.7f);Check(b.IsCasting,"Fixture not casting");Place(player,new Vector3(15,1.05f,12));await Wait(.55f);
  float facing=Vector3.Angle(b.transform.forward,Vector3.ProjectOnPlane(player.transform.position-b.transform.position,Vector3.up));
  Check(facing<5,"Ranged boss did not face moved player: "+facing);
  b.CancelCast();b.enabled=false;Frame(Vector3.zero,new Vector3(0,165,-145));await Wait(.1f);Capture("layout");
  var floor=GameObject.Find("Reflective Dark Floor").GetComponent<Renderer>().bounds;
  Check(Mathf.Abs(floor.size.x-200)<.1f && Mathf.Abs(floor.size.z-200)<.1f,"Floor not 200 x 200");
  foreach(var t in UnityEngine.Object.FindObjectsByType<VoidRendTargetable>())if(!t.GetComponent<CalligraphyBossCombat>())Check(Mathf.Abs(t.transform.position.x)>45,"Target still in central zone");
  b.enabled=true;range.StopTest();return "PASS 200x200 floor, side targets, one elder across start/stop; moving-player ranged facing error="+facing;
 }
 public static async Task<string> RollPayment()
 {
  var range=Range();range.StopTest();var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();var stamina=p.GetComponent<PlayerStamina>();
  stamina.idleRegenPerSecond=0;stamina.Refill();motor.inputEnabled=true;Place(p,new Vector3(0,1.05f,-10));await Wait(.3f);
  var start=typeof(ThirdPersonController).GetMethod("TryStartRoll",BindingFlags.Instance|BindingFlags.NonPublic);
  int events=0;Action receipt=()=>events++;motor.OnRollStarted+=receipt;
  try
  {
   Time.timeScale=0;start.Invoke(motor,new object[]{Vector3.forward});Check(!motor.IsRolling && stamina.Stamina==stamina.maxStamina,"Paused roll spent energy");Time.timeScale=1;
   motor.LockActions();start.Invoke(motor,new object[]{Vector3.forward});motor.UnlockActions();Check(!motor.IsRolling && stamina.Stamina==stamina.maxStamina,"Locked roll spent energy");
   motor.Stun(.15f);start.Invoke(motor,new object[]{Vector3.forward});Check(!motor.IsRolling && stamina.Stamina==stamina.maxStamina,"Stunned roll spent energy");await Wait(.2f);
   var body=p.GetComponent<CharacterController>();var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Temporary blocked-roll fixture";wall.transform.localScale=new Vector3(8,4,.2f);wall.transform.position=body.bounds.center+Vector3.forward*(body.bounds.extents.z+.1f);Physics.SyncTransforms();
   start.Invoke(motor,new object[]{Vector3.forward});await Wait(.2f);
   float failed=stamina.Stamina;Check(failed==stamina.maxStamina && events==0,"Blocked roll consumed energy: "+failed+" events="+events);
   UnityEngine.Object.Destroy(wall);await Wait(.05f);Vector3 before=p.transform.position;
   start.Invoke(motor,new object[]{Vector3.forward});await Wait(.17f);
   float distance=Vector3.ProjectOnPlane(p.transform.position-before,Vector3.up).magnitude;
   Check(motor.IsRolling && distance>.2f && events==1,"Actual roll did not move / emit event");
   Check(Mathf.Abs(stamina.Stamina-stamina.maxStamina*(1-stamina.rollCost))<.01f,"Real roll not charged exactly once");
   start.Invoke(motor,new object[]{Vector3.forward});Check(events==1,"Duplicate roll event");Frame(p.transform.position,new Vector3(5,3,-6));await Wait(.05f);Capture("roll");await Wait(.4f);
   return "PASS paused/locked/stunned/blocked roll no cost; actual roll moved="+distance+" and spent exactly "+stamina.maxStamina*stamina.rollCost;
  }
  finally{Time.timeScale=1;motor.OnRollStarted-=receipt;}
 }
 public static async Task<string> PaintingReturn()
 {
  var range=Range();range.StartTest();var b=range.Boss;b.CancelCast();b.enabled=false;var p=PlayerHealth.Find();
  Place(p,new Vector3(0,1.05f,-10));await Wait(.15f);Vector3 entry=p.transform.position;
  var go=new GameObject("Return-coordinate painting test");go.transform.SetParent(b.EffectsRoot,false);var world=go.AddComponent<CalligraphyPaintingWorld>();world.Begin(b,p,b.ink);world.entrySeconds=.1f;world.containedSeconds=1;world.exitSeconds=.8f;
  Place(p,entry+new Vector3(115,0,65));Frame(p.transform.position+Vector3.up*2,new Vector3(8,4,-12));
  var late=GameObject.CreatePrimitive(PrimitiveType.Capsule);late.name="Late outside dummy fixture";late.transform.position=p.transform.position+Vector3.forward*5;
  await Wait(.2f);Check(late.GetComponent<Renderer>().forceRenderingOff,"New outside target leaked");
  foreach(var t in UnityEngine.Object.FindObjectsByType<CalligraphyPracticeTarget>())foreach(var r in t.GetComponentsInChildren<Renderer>())Check(r.forceRenderingOff,"Existing target leaked: "+t.name);
  late.GetComponent<Renderer>().forceRenderingOff=false;await Wait(.04f);Check(late.GetComponent<Renderer>().forceRenderingOff,"Outside renderer reactivation leaked");Capture("inside-no-targets");
  await Wait(1.1f);Check(world && world.Phase==CalligraphyPaintingWorld.WorldPhase.Exiting,"Fixture missed dissolve");Capture("exit-midpoint");
  var isolation=world.GetComponent<CalligraphyWorldIsolation>();Check(isolation.CapturedFrames>0,"No outside dissolve frames");
  Vector3 relative=Camera.main.transform.position-p.transform.position;
  await Wait(.8f);Check(!world,"Painting did not finish");
  Check(Vector2.Distance(new Vector2(p.transform.position.x,p.transform.position.z),new Vector2(entry.x,entry.z))<.01f,"Exit did not restore entry XZ: "+p.transform.position);
  Check(Vector3.Distance(relative,Camera.main.transform.position-p.transform.position)<.1f,"Return camera popped");
  Check(!late.GetComponent<Renderer>().forceRenderingOff,"Outside target not restored");Capture("returned");UnityEngine.Object.Destroy(late);
  var cancelled=new GameObject("Cancelled painting test");cancelled.transform.SetParent(b.EffectsRoot,false);world=cancelled.AddComponent<CalligraphyPaintingWorld>();world.Begin(b,p,b.ink);entry=p.transform.position;Place(p,entry+Vector3.right*110);UnityEngine.Object.Destroy(cancelled);await Wait(.08f);
  Check(Mathf.Abs(p.transform.position.x-entry.x)<.01f,"Cancelled painting left player outside");b.enabled=true;range.StopTest();
  return "PASS moved 115m/65m inside; entry coordinate restored on normal exit and cancellation; new/reactivated targets hidden; continuous outside frames and camera offset";
 }
 public static async Task<string> PulsesLaunch()
 {
  var range=Range();range.StartTest();var b=range.Boss;b.CancelCast();b.enabled=false;var p=PlayerHealth.Find();
  Place(p,new Vector3(0,1.05f,-10));p.HealFull();
  typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,0f);
  var settings=new CalligraphyBossCombat.WaveSettings{vfxScale=1.8f,waveCount=1,damage=1,controlSeconds=1,knockbackSpeed=6,launchSpeed=3};
  CalligraphySkillSequence.Wave(p,new Vector3(0,.05f,0),Vector3.forward,true,false,b.EffectsRoot,settings);
  var fx=b.EffectsRoot.GetComponentInChildren<CalligraphyPackageEffect>();Vector3 expected=fx.transform.right;ParticleSystem pulse=null;
  float until=Time.time+3;
  while(Time.time<until && !pulse){pulse=fx.Systems.FirstOrDefault(ps=>ps.name.StartsWith("yy_Shockwave_1") && ps.particleCount>0);if(!pulse)await Wait(.01f);}
  Check(pulse,"No visible pulse");var mesh=new Mesh();pulse.GetComponent<ParticleSystemRenderer>().BakeMesh(mesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);
  var v=mesh.vertices;var tris=mesh.triangles;var at=pulse.transform.position+(v[tris[0]]+v[tris[1]]+v[tris[2]])/3;UnityEngine.Object.Destroy(mesh);
  Place(p,new Vector3(at.x,1.05f,at.z));Frame(p.transform.position,new Vector3(7,5,-9));var motor=p.GetComponent<ThirdPersonController>();until=Time.time+1;
  while(Time.time<until && !motor.IsBallistic)await Wait(.01f);
  Check(motor.IsBallistic,"Fixture did not meet real shockwave");var velocity=(Vector3)typeof(ThirdPersonController).GetField("horizontalVelocity",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(motor);
  float alignment=Vector3.Dot(velocity.normalized,expected.normalized);Check(alignment>.99f,"Launch is sideways to pulse: "+alignment);Capture("pulse-launch");
  await Wait(1.2f);b.enabled=true;range.StopTest();return "PASS real particle-mesh hit launched along Pulses native +X; alignment="+alignment;
 }
 public static async Task<string> LiveCameraAndReset()
 {
  var range=Range();range.StartTest();var b=range.Boss;var original=b;var p=PlayerHealth.Find();b.CancelCast();b.enabled=false;
  Place(p,new Vector3(0,1.05f,-10));await Wait(.2f);var entry=p.transform.position;
  var orbit=Camera.main.GetComponent<ThirdPersonCamera>();orbit.enabled=true;Camera.main.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled=true;orbit.SnapToTarget();await Wait(.1f);
  var go=new GameObject("Live camera return test");go.transform.SetParent(b.EffectsRoot,false);var world=go.AddComponent<CalligraphyPaintingWorld>();world.Begin(b,p,b.ink);world.entrySeconds=.1f;world.containedSeconds=.6f;world.exitSeconds=1;
  Place(p,entry+new Vector3(90,0,60));await Wait(1.5f);
  var relative=Camera.main.transform.position-p.transform.position;
  await Wait(.35f);Check(!world && Mathf.Abs(p.transform.position.x-entry.x)<.01f,"Live camera return failed");
  float cameraChange=Vector3.Distance(relative,Camera.main.transform.position-p.transform.position);Check(cameraChange<.2f,"Cinemachine warp changed relative view: "+cameraChange);Capture("live-camera-return");
  b.enabled=true;b.dodgeChance=0;b.TakeDamage(b.maxHealth+1,b.transform.position);Check(b.IsDead,"Death fixture did not kill boss");range.StopTest();range.StartTest();
  Check(range.Boss==original && !b.IsDead && b.brush.enabled && b.GetComponentsInChildren<Renderer>().Any(r=>r.enabled),"Same actor did not reset after death");range.StopTest();
  return "PASS active Cinemachine return camera delta="+cameraChange+"; death/reset reuses same elder";
 }
}
