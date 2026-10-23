using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
public static class CalligraphyFourthChecks
{
 public static string SaveDefaults()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
  const string path="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab";
  var root=PrefabUtility.LoadPrefabContents(path);
  try{var b=root.GetComponent<CalligraphyBossCombat>();b.inkProjectile.homingEndPercentage=.9f;b.inkProjectile.homingStrength=.9f;b.inkProjectile.turnRate=65;
   b.paintingWorld.eyeCenter=new Vector2(.035f,.07f);b.paintingWorld.eyeVisibility=1;
   b.inkDragon.explosionScale=1.1f;b.inkDragon.buffScale=.65f;b.inkDragon.flashScale=.8f;b.inkDragon.explosionOffset=new Vector3(0,.35f,0);
   PrefabUtility.SaveAsPrefabAsset(root,path);return "Saved fourth-round settings only to "+path;}
  finally{PrefabUtility.UnloadPrefabContents(root);}
 }
 static async Task Wait(float seconds)
 {float end=Time.time+seconds;var wall=System.Diagnostics.Stopwatch.StartNew();while(Time.time<end){if(wall.Elapsed.TotalSeconds>seconds*5+10)throw new Exception("Editor stopped ticking");await Task.Delay(16);}}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static CalligraphyBossCombat Setup(int skill)
 {
  Time.timeScale=1;EditorApplication.isPaused=false;
  var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();
  var b=range.Boss;b.cycleDemonstration=false;b.moveSpeed=0;b.dodgeChance=0;b.recoverySeconds=100;
  b.transform.SetPositionAndRotation(new Vector3(12,0,12),Quaternion.identity);b.SelectSkill(skill);
  Place(new Vector3(12,1.05f,22));PlayerHealth.Find().GrantDodgeInvulnerability(60);return b;
 }
 static void Place(Vector3 position)
 {var p=PlayerHealth.Find();p.GetComponent<ThirdPersonController>().ResetMotion();var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=position;cc.enabled=true;Physics.SyncTransforms();}
 static void Frame(Vector3 at,Vector3 offset)
 {var cam=Camera.main;var t=cam.GetComponent<ThirdPersonCamera>();if(t)t.enabled=false;var brain=cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;cam.transform.position=at+offset;cam.transform.LookAt(at);}
 public static string Capture(string name)
 {
  var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(type);window.Repaint();
  var rt=type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window) as RenderTexture;
  if(!rt)throw new Exception("No Game View texture");var old=RenderTexture.active;RenderTexture.active=rt;
  var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();RenderTexture.active=old;
  if(SystemInfo.graphicsUVStartsAtTop){var p=tex.GetPixels();var flip=new Color[p.Length];for(int y=0;y<rt.height;y++)Array.Copy(p,y*rt.width,flip,(rt.height-1-y)*rt.width,rt.width);tex.SetPixels(flip);tex.Apply();}
  string path="Logs/FourthSkills-"+name+".png";System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.Destroy(tex);return path;
 }
 static int Visible(CalligraphyPackageEffect fx)
 {
  int n=0;var buffer=new ParticleSystem.Particle[8192];
  foreach(var ps in fx.Systems){var r=ps.GetComponent<ParticleSystemRenderer>();if(!ps.gameObject.activeInHierarchy || !r.enabled || !r.sharedMaterial)continue;
   int count=ps.GetParticles(buffer);for(int i=0;i<count;i++)if(buffer[i].GetCurrentColor(ps).a>8 && buffer[i].GetCurrentSize(ps)>.01f)n++;}
  return n;
 }
 public static async Task<string> World()
 {
  var b=Setup(3);b.paintingWorld.dragonCount=0;b.paintingWorld.containedSeconds=120;
  await Wait(5);var world=UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>();
  var p=PlayerHealth.Find();Frame(p.transform.position+new Vector3(0,32,55),new Vector3(0,-30,-60));
  await Wait(.3f);Capture("sky");
  var s=new StringBuilder();
  var streams=world.GetComponentsInChildren<LineRenderer>().Where(l=>l.name=="Suspended branching ink stream").ToArray();
  s.AppendLine("streams="+streams.Length+" span="+string.Join(",",streams.Take(6).Select(l=>l.GetPosition(24))));
  s.AppendLine("wetlands="+world.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Interior ink wetland")));
  foreach(var r in world.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Translucent elder")))s.AppendLine(r.GetComponent<MeshFilter>().sharedMesh.bounds.ToString());
  Frame(world.transform.position+new Vector3(0,-4,0),new Vector3(0,50,-80));await Wait(.3f);Capture("landscape");
  var wetland=world.GetComponentsInChildren<Transform>().First(t=>t.name=="Interior ink wetland 0");
  Frame(wetland.position+Vector3.up,new Vector3(7,5,-9));await Wait(.3f);Capture("wetland");
  Check(streams.Length==60,"Stream count changed");Check(world.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Interior ink wetland"))==9,"Missing interior scenery");
  Time.timeScale=0;return s.ToString();
 }
 public static async Task<string> RuneAndExplosion()
 {
  var b=Setup(0);b.CancelCast();b.enabled=false;var p=PlayerHealth.Find();Place(new Vector3(12,1.05f,20));Frame(new Vector3(12,0,20),new Vector3(8,8,-12));
  CalligraphySkillSequence.Rune(p,new Vector3(12,.04f,20),0,b.EffectsRoot,b.runeAttack);
  var seq=b.EffectsRoot.GetComponentInChildren<CalligraphySkillSequence>();
  await Wait(.55f);Capture("rune-warning");
  while(seq && seq.FallStartedAt<0)await Wait(.01f);
  await Wait(.12f);Capture("rune-fall");
  var fall=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().First(f=>f.name.StartsWith("Ying Yang Rune Fall"));
  Check(Visible(fall)>0,"Fall invisible after handoff");Check(seq.ImpactAt<0,"Damage before impact");
  while(seq && seq.ImpactAt<0)await Wait(.01f);
  float latency=seq.ImpactAt-seq.FallStartedAt;
  await Wait(.2f);Capture("rune-impact");
  var motor=p.GetComponent<ThirdPersonController>();var field=typeof(ThirdPersonController).GetField("temporarySpeedMultiplier",BindingFlags.Instance|BindingFlags.NonPublic);
  // Dodge invulnerability does not prevent the source-owned slow, but roll immunity does.
  await Wait(.1f);Check(Mathf.Abs((float)field.GetValue(motor)-.6f)<.001f,"Residue did not slow to .6");
  Place(new Vector3(20,1.05f,20));await Wait(2.6f);Check(Mathf.Abs((float)field.GetValue(motor)-1)<.001f,"Slow did not expire");
  Place(new Vector3(12,1.05f,20));await Wait(.1f);Check((float)field.GetValue(motor)<1,"Re-entry missing");
  b.CancelCast();await Wait(.05f);Check((float)field.GetValue(motor)==1,"Cancel left slow");
  CalligraphySkillSequence.DragonImpact(new Vector3(12,0,20),b.EffectsRoot,b.inkDragon);
  await Wait(.12f);Capture("explosion-main");
  var main=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().First(f=>f.name.StartsWith("Ying Yang Big"));int visibleMain=Visible(main);Check(visibleMain>50,"Main explosion burst skipped");
  await Wait(b.inkDragon.tailStartSeconds);Capture("explosion-tail");
  var tail=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Where(f=>f.name.StartsWith("Ying Yang Buff")||f.name.StartsWith("Ying Yang Flash")).ToArray();
  Check(tail.Length==2 && tail.All(f=>Visible(f)>0),"Tail effects not visible together");
  Check(Mathf.Abs(tail[0].Age-tail[1].Age)<.04f,"Tail timing split");b.enabled=true;b.StopBattle();
  return "PASS rune visible handoff; fall-to-impact="+latency+"; slow .6 expiry2.5/cancel/re-entry; main visible particles="+visibleMain+"; two visible simultaneous tails";
 }
 public static async Task<string> SlamBeamHoming()
 {
  var b=Setup(6);var p=PlayerHealth.Find();Place(new Vector3(12,1.05f,22));Frame(new Vector3(12,1,20),new Vector3(10,5,-8));
  await Wait(1);var locked=b.transform.forward;Place(new Vector3(22,1.05f,16));
  while(!b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Any(f=>f.name.StartsWith("Ying Yang Frontal [")))await Wait(.01f);
  var frontal=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().First(f=>f.name.StartsWith("Ying Yang Frontal ["));
  Check(Vector3.Dot(frontal.transform.forward,locked)>.999f,"Slam reacquired moved player");
  float tipY=b.brush.brushTip.position.y;Check(Mathf.Abs(tipY-.04f)<.12f,"Tip missed floor: "+tipY);
  Check(Vector3.Distance(frontal.transform.position,b.brush.brushTip.position)<.15f,"Impact not at tip");Frame(b.brush.brushTip.position+Vector3.up*.5f,new Vector3(4,3,-5));await Wait(.08f);Capture("slam-impact");
  await Wait(.3f);Check(Vector3.Dot(frontal.transform.forward,locked)>.999f,"Frontal changed direction");
  b.SelectSkill(5);Place(new Vector3(12,1.05f,35));Frame(new Vector3(12,1,25),new Vector3(12,4,-8));await Wait(5.5f);
  var beam=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().First(f=>f.name.StartsWith("Ying Yang Beam"));
  var direction=(Vector3)typeof(CalligraphyBossCombat).GetField("beamDirection",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(b);
  Check(Mathf.Abs(direction.y)<.01f,"Beam tilts down: "+direction);Capture("beam");
  float beamY=beam.transform.position.y;b.CancelCast();b.enabled=false;Place(new Vector3(90,1.05f,120));
  var go=new GameObject("Fourth homing test");var shot=go.AddComponent<CalligraphyInkProjectile>();
  var settings=new CalligraphyBossCombat.ProjectileSettings{maximumDistance=20};
  shot.Launch(b.ink,b.transform,new Vector3(80,1,100),Vector3.forward,10,1,p,settings);
  await Wait(.5f);float turn=Vector3.Angle(Vector3.forward,shot.Heading);Check(turn>8 && turn<35,"Homing not stronger/smooth: "+turn);
  while(shot && !shot.HeadingLocked)await Wait(.01f);
  Check(shot && shot.DistanceTravelled>=18 && shot.DistanceTravelled<18.7f,"90% cutoff wrong");
  var heading=shot.Heading;Place(new Vector3(50,1.05f,100));await Wait(.08f);Check(shot && Vector3.Angle(heading,shot.Heading)<.01f,"Terminal heading reacquired");
  await Wait(.5f);Check(!shot,"Shot did not expire");b.enabled=true;b.StopBattle();
  return "PASS locked slam / actual tip Y="+tipY+"; horizontal beam atY="+beamY+"; homing .5s angle="+turn+" / cutoff18 of20 / locked terminal";
 }
 public static async Task<string> FullDragon()
 {
  var b=Setup(4);Place(new Vector3(12,1.05f,22));Frame(new Vector3(12,2,22),new Vector3(12,8,-12));
  CalligraphyPackageEffect main=null;float until=Time.time+9;
  while(!main && Time.time<until){main=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().FirstOrDefault(f=>f.name.StartsWith("Ying Yang Big"));await Wait(.01f);}
  Check(main,"Dragon never produced impact");await Wait(.15f);Capture("dragon-impact");int count=Visible(main);Check(count>50,"Full dragon explosion invisible");
  Check(b.EffectsRoot.GetComponentInChildren<CalligraphyGroundImpact>(),"Fracture missing");
  Check(!b.EffectsRoot.GetComponentsInChildren<Transform>().Any(t=>t.name=="Yin yang impact"),"Old Taiji returned");
  await Wait(b.inkDragon.tailStartSeconds);Capture("dragon-tail");
  var tail=b.EffectsRoot.GetComponentsInChildren<CalligraphyPackageEffect>().Where(f=>f.name.StartsWith("Ying Yang Buff")||f.name.StartsWith("Ying Yang Flash")).ToArray();
  Check(tail.Length==2 && tail.All(f=>Visible(f)>0),"Full cast lost visible tails");
  await Wait(6);Check(!b.EffectsRoot.GetComponentInChildren<CalligraphySkillSequence>(),"Explosion sequence leaked");b.StopBattle();
  return "PASS complete dragon cast: visible main="+count+", fracture retained, no old Taiji, two visible tails, natural cleanup";
 }
 public static async Task<string> DamageTiming()
 {
  var b=Setup(8);b.runeAttack.count=1;Place(new Vector3(12,1.05f,20));var p=PlayerHealth.Find();p.HealFull();
  typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,0f);
  float hp=p.Health;await Wait(1);Check(p.Health==hp,"Warning dealt damage");
  var s=b.EffectsRoot.GetComponentInChildren<CalligraphySkillSequence>();Check(s && s.ImpactAt<0,"Test did not sample pre-impact");
  while(s && s.ImpactAt<0)await Wait(.01f);await Wait(.05f);Check(p.Health<hp,"Actual Rune impact did not damage");
  float after=p.Health;await Wait(1.5f);Check(p.Health==after,"Residue dealt repeated damage");b.StopBattle();
  return "PASS Rune warning no damage; impact damage="+(hp-after)+"; residue no damage";
 }
 public static async Task<string> SlamDirect()
 {
  var b=Setup(6);Place(new Vector3(12,1.05f,22));var p=PlayerHealth.Find();p.HealFull();
  typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,0f);
  await Wait(1);
  // Deterministic exact-contact fixture: run the same ordered animation/impact callbacks.
  // The moving-target test separately exercises the full real-time cast.
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  typeof(CalligraphyBossCombat).GetField("elapsed",flags).SetValue(b,b.leapSlam.castSeconds*.74f);
  typeof(CalligraphyBossCombat).GetMethod("LeapSlam",flags).Invoke(b,null);
  typeof(CalligraphyBrushPresentation).GetMethod("LateUpdate",flags).Invoke(b.brush,null);
  var point=b.brush.brushTip.position;Place(new Vector3(point.x,1.05f,point.z));
  float start=Time.time,hp=p.Health,baseY=p.transform.position.y,peak=0;bool launch=false;
  typeof(CalligraphyBossCombat).GetMethod("LateUpdate",flags).Invoke(b,null);
  while(Time.time-start<1.5f){launch|=p.GetComponent<CalligraphyPlayerControl>()?.Current==CalligraphyPlayerControl.Reaction.StrongLaunch;peak=Mathf.Max(peak,p.transform.position.y-baseY);await Wait(.01f);}
  b.StopBattle();Check(launch && p.Health<hp && peak>.4f,"Direct hit failed launch: "+launch+" damage="+(hp-p.Health)+" peak="+peak);
  return "PASS direct brush StrongLaunch damage="+(hp-p.Health)+" peak="+peak;
 }
 public static async Task<string> BeamDamage()
 {
  var b=Setup(5);var p=PlayerHealth.Find();Place(new Vector3(12,1.05f,25));p.HealFull();
  typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,0f);
  float start=Time.time,first=-1;Action<float> hit=amount=>{if(first<0)first=Time.time-start;};p.OnDamaged+=hit;
  try
  {await Wait(6.5f);Check(first>5 && first<6.4f,"Beam early/missing damage="+first);b.StopBattle();
   b=Setup(5);b.inkBeam.maximumRange=8;Place(new Vector3(12,1.05f,35));typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,0f);first=-1;
   await Wait(8.5f);Check(first<0,"Beam damaged past visual range");b.StopBattle();return "PASS horizontal live beam hit after charge; far capsule no damage";}
  finally{p.OnDamaged-=hit;if(b)b.StopBattle();}
 }
 public static async Task<string> RollSafety()
 {
  var b=Setup(0);b.CancelCast();b.enabled=false;var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();
  typeof(PlayerHealth).GetField("dodgeInvulnerableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,0f);
  await Wait(.2f);typeof(ThirdPersonController).GetMethod("TryStartRoll",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,new object[]{Vector3.right});
  Check(motor.IsRolling,"Fixture could not start actual roll");float hp=p.Health;
  bool hit=CalligraphyPlayerHitGate.Hit(p,24,Vector3.forward,"roll validation",p.transform.position,CalligraphyPlayerControl.Reaction.StrongLaunch,1);
  Check(!hit && p.Health==hp && motor.AddSpeedMultiplierLease(.6f)==0,"Roll did not prevent damage/slow");
  await Wait(1);b.enabled=true;b.StopBattle();return "PASS actual roll: no damage, launch or slow lease";
 }
 public static string FaceAudit()
 {
  var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab");
  return string.Join("\n",root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials.Select(m=>r.name+" / "+m.name+" shader="+m.shader.name+" texture="+(m.mainTexture?AssetDatabase.GetAssetPath(m.mainTexture):"none")+" color="+(m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):Color.white))));
 }
 public static async Task<string> EyeCoordinates()
 {
  Time.timeScale=1;var p=PlayerHealth.Find();Frame(p.transform.position+new Vector3(0,32,55),new Vector3(0,-30,-60));await Wait(.15f);Time.timeScale=0;Capture("eye-audit");
  var world=UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>();var r=world.GetComponentsInChildren<MeshRenderer>().First(r=>r.name.StartsWith("Translucent elder"));
  var cam=Camera.main;var vertices=r.GetComponent<MeshFilter>().sharedMesh.vertices;
  var s=new StringBuilder();var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);s.AppendLine("eye="+block.GetVector("_EyeCenter")+" strength="+block.GetFloat("_EyeVisibility"));
  foreach(float x in new[]{.473f,.520f})
  {
   var near=vertices.OrderBy(v=>{var q=cam.WorldToViewportPoint(r.transform.TransformPoint(v));return Vector2.Distance(new Vector2(q.x,q.y),new Vector2(x,.726f));}).Take(4);
   s.AppendLine(string.Join(";",near));
  }
  return s.ToString();
 }
 public static string Audit()
 {
  var s=new StringBuilder();
  s.AppendLine("Scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
  var lib=Resources.Load<CalligraphyVfxLibrary>("CalligraphyVfxLibrary");
  foreach(var prefab in new[]{lib.rune,lib.runeFall,lib.bigExplosion,lib.buffUp,lib.flashAoE})
  {
   s.AppendLine("PREFAB "+AssetDatabase.GetAssetPath(prefab));
   foreach(var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
   {
    var m=ps.main;var e=ps.emission;var r=ps.GetComponent<ParticleSystemRenderer>();
    s.Append(ps.name+" active="+ps.gameObject.activeSelf+" delay="+m.startDelay.constantMax+" duration="+m.duration+" life="+m.startLifetime.constantMax+" rate="+e.rateOverTime.constantMax+" renderer="+r.enabled+" alpha="+m.startColor.colorMax.a+" bursts=");
    for(int i=0;i<e.burstCount;i++){var b=e.GetBurst(i);s.Append(b.time+":"+b.count.constantMax+" ");}
    s.AppendLine();
   }
  }
  return s.ToString();
 }
}
