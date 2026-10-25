using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class CalligraphySixthChecks
{
    const string Prefab="Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab";
    public static string Install()
    {
        if(EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)throw new Exception("Need saved Edit Mode scene");
        void Tune(CalligraphyBossCombat b)
        {
            var s=b.inkAvatarUltimate;s.platforms.heightRange=new Vector2(3,85);s.sea.rescueToPlatform=false;b.paintingWorld.dragonCount=5;
            EditorUtility.SetDirty(b);
        }
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try{Tune(root.GetComponent<CalligraphyBossCombat>());PrefabUtility.SaveAsPrefabAsset(root,Prefab);}finally{PrefabUtility.UnloadPrefabContents(root);}
        var boss=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossCombat>();Tune(boss);
        PrefabUtility.RecordPrefabInstancePropertyModifications(boss);EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);EditorSceneManager.SaveScene(boss.gameObject.scene);AssetDatabase.SaveAssets();
        var shaders=new[]{boss.inkAvatarUltimate.avatar.material.shader,boss.inkAvatarUltimate.sea.material.shader};
        var errors=shaders.SelectMany(ShaderUtil.GetShaderMessages).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
        if(errors.Any())throw new Exception(string.Join("\n",errors.Select(m=>m.message)));
        return "Saved revised ultimate on range elder and Battle prefab; all other groups preserved";
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static bool heldW;
    sealed class TestKeyboard : IDisposable
    {
        readonly Keyboard[] previous;readonly Keyboard device;
        public TestKeyboard()
        {
            previous=InputSystem.devices.OfType<Keyboard>().Where(k=>k.enabled).ToArray();
            foreach(var k in previous)InputSystem.DisableDevice(k);
            device=InputSystem.AddDevice<Keyboard>("SixthSkillValidationKeyboard");device.MakeCurrent();
            heldW=false;InputSystem.onAfterUpdate+=Apply;
        }
        void Apply(){device.MakeCurrent();InputState.Change(device,heldW?new KeyboardState(Key.W):new KeyboardState());}
        public void Dispose(){InputSystem.onAfterUpdate-=Apply;heldW=false;InputSystem.RemoveDevice(device);foreach(var k in previous)if(k.added)InputSystem.EnableDevice(k);}
    }
    static void PressW(bool pressed)
    {
        heldW=pressed;
        var keyboard=Keyboard.current;if(keyboard==null)keyboard=InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard,pressed?new KeyboardState(Key.W):new KeyboardState());InputSystem.Update();
    }
    static void FaceEdge(PlayerHealth p,Vector3 edge,Vector3 inward,bool forward=true)
    {
        Vector3 facing=forward?inward:-inward;p.transform.rotation=Quaternion.LookRotation(facing);
        Frame(edge,edge-facing*5+Vector3.up*1.2f);
    }
    static async Task Wait(float seconds){float end=Time.time+seconds;while(Time.time<end)await Task.Delay(16);}
    static async Task Until(Func<bool> condition,float seconds,string message)
    {float end=Time.time+seconds;while(!condition()){Check(Time.time<end,message);await Task.Delay(16);}}
    static CalligraphyBossCombat Setup()
    {
        Check(EditorApplication.isPlaying,"Enter Play Mode");Time.timeScale=1;PressW(false);
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();range.StartTest();var b=range.Boss;
        b.moveSpeed=0;b.dodgeChance=0;b.inkAvatarUltimate.sea.duration=100;b.inkAvatarUltimate.airCombatProjectiles=false;
        b.inkAvatarUltimate.sweep.firstKnockback=false;b.inkAvatarUltimate.sweep.secondKnockback=false;
        var player=PlayerHealth.Find();player.SetPracticeProtection(b,true);
        Warp(player,b.transform.position+b.transform.forward*16+Vector3.up*1.05f);
        var cam=Camera.main;var orbit=cam.GetComponent<ThirdPersonCamera>();if(orbit)orbit.enabled=false;
        var brain=cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;
        Frame(b.transform.position+Vector3.up*25,b.transform.position+b.transform.forward*115+b.transform.right*50+Vector3.up*48);
        b.SelectSkill(9);return b;
    }
    static void Warp(PlayerHealth p,Vector3 at)
    {var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=at;cc.enabled=true;p.GetComponent<ThirdPersonController>().ResetMotion();Physics.SyncTransforms();}
    static void Frame(Vector3 look,Vector3 at)
    {var c=Camera.main;c.fieldOfView=58;c.farClipPlane=600;c.transform.SetPositionAndRotation(at,Quaternion.LookRotation(look-at));}
    static string Shot(string name)
    {
        var cam=Camera.main;var rt=new RenderTexture(1400,900,24);var old=cam.targetTexture;var active=RenderTexture.active;
        var image=new Texture2D(1400,900,TextureFormat.RGB24,false);
        try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();string path="Logs/Sixth-"+name+".png";System.IO.File.WriteAllBytes(path,image.EncodeToPNG());return path;}
        finally{cam.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);}
    }
    public static async Task<string> Flow()
    {
        using var keyboard=new TestKeyboard();
        var b=Setup();var player=PlayerHealth.Find();var motor=player.GetComponent<ThirdPersonController>();var cc=player.GetComponent<CharacterController>();
        Vector3 entry=player.transform.position;await Until(()=>b.AvatarUltimate,3,"Missing owner");var u=b.AvatarUltimate;
        await Wait(3.6f);Shot("summon");
        await Until(()=>u.PlayerLaunched,12,"No impact");
        var pen=u.AvatarRoot.GetComponent<CalligraphyBrushPresentation>();
        float vertical=Vector3.Dot((pen.brushTip.position-pen.brushPivot.position).normalized,Vector3.down);
        Check(vertical>.999f,"Brush not vertical");Check(Mathf.Abs(pen.brushTip.position.y-u.ImpactPoint.y)<.15f,"Bristles not at ground");
        Check(u.SweepsCompleted==2,"Lost sweeps");Check(u.SpawnedPlatformCount==64,"Missing platforms");
        Check(u.Platforms.Min(p=>p.TargetTop)<9 && u.Platforms.Max(p=>p.TargetTop)>75,"Missing extreme heights");
        Check(u.Platforms.Max(p=>p.Radius)>u.Platforms.Min(p=>p.Radius)*4,"Insufficient size variation");
        foreach(var p in u.Platforms){Check(p.GetComponent<MeshCollider>().convex,"Missing convex collider");Check(p.Depth>=2,"Thin wafer");}
        await Until(()=>motor.IsGrounded && cc.bounds.min.y>5,8,"Initial launch did not land on reserved rock");
        Shot("arena");
        var fold=u.GetComponentsInChildren<MeshFilter>().First(m=>m.sharedMesh.name=="Branching airborne ink diffusion");
        var old=fold.sharedMesh.vertices;await Wait(.7f);var now=fold.sharedMesh.vertices;
        Check(now.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Invalid wave vertices");
        Check(now.Where((v,i)=>Vector3.Distance(v,old[i])>.05f).Count()>100,"Sea is not moving");
        Frame(u.ImpactPoint+Vector3.up*3,u.ImpactPoint+b.transform.forward*45+b.transform.right*18+Vector3.up*16);Shot("sea");
        player.SetPracticeProtection(b,false);float health=player.Health;
        Warp(player,u.ImpactPoint+Vector3.up*.15f);
        await Wait(3.5f);
        Check(u.PlayerInSea,"No sea contact");Check(player.Health<health,"No repeated sea damage");Check(!motor.IsBallistic && cc.bounds.min.y<1,"Sea still launches player");
        float slow=(float)typeof(ThirdPersonController).GetField("temporarySpeedMultiplier",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(motor);
        Check(slow<=b.inkAvatarUltimate.sea.slowMultiplier+.01f,"Missing sea slow");
        player.SetPracticeProtection(b,true);
        var rock=u.Platforms[0];Vector3 outside=rock.transform.position+b.transform.right*(rock.Radius+2);
        Check(rock.TryLedge(outside,out var e,out var into),"No edge");
        float footOffset=player.transform.position.y-cc.bounds.min.y;
        Warp(player,e-into*.8f+Vector3.up*(footOffset-1.4f));
        FaceEdge(player,e,into);PressW(true);
        string trace="";float catchEnd=Time.time+1;
        while(!u.Climber.Climbing && Time.time<catchEnd)
        {
            trace+=$"\nW={Keyboard.current.wKey.isPressed} enabled={motor.enabled} input={motor.inputEnabled} ground={motor.IsGrounded} lock={motor.ActionsLocked} stun={motor.IsStunned} roll={motor.IsRolling} waist={player.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips).position.y} edge={e} feet={cc.bounds.min.y} facing={Vector3.Dot(player.transform.forward,into)} position={player.transform.position}";
            await Wait(.04f);
        }
        Check(u.Climber.Climbing,"Falling edge did not start climb: "+trace);
        PressW(false);
        Frame(e+Vector3.up*.3f,e-into*5+b.transform.right*3+Vector3.up*2);
        await Wait(.2f);Shot("climb-hang");await Wait(.48f);Shot("climb-left");await Wait(.5f);Shot("climb-right");
        await Until(()=>!u.Climber.Climbing,3,"Climb stuck");await Wait(.15f);Shot("climb-stand");
        Check(motor.enabled && cc.enabled && !motor.ActionsLocked,"Climb did not restore controls");
        Check(Mathf.Abs(cc.bounds.min.y-rock.Top)<.3f,"Not standing on top");
        b.NextSkill();await Wait(.2f);
        Check(UnityEngine.Object.FindObjectsByType<CalligraphyInkPlatform>().Length==0,"Rock collider leak");
        Check(!motor.ActionsLocked && motor.enabled && cc.enabled,"Cleanup control leak");
        Check(Vector3.Distance(entry,player.transform.position)<.3f,"Cleanup did not restore entry");
        player.SetPracticeProtection(b,false);
        return $"PASS two sweeps; vertical={vertical}; 64 irregular rocks 3..85; safe landing; animated finite curls; sea damage+slow/no bounce; falling ledge climb; next-skill cleanup";
    }
    public static async Task<string> Preview()
    {
        var b=Setup();await Until(()=>b.AvatarUltimate,3,"No ultimate");var u=b.AvatarUltimate;
        await Until(()=>u.PlayerLaunched,15,"No impact");
        var pen=u.AvatarRoot.GetComponent<CalligraphyBrushPresentation>();
        float vertical=Vector3.Dot((pen.brushTip.position-pen.brushPivot.position).normalized,Vector3.down);
        await Wait(5);
        Time.timeScale=0;
        return $"PAUSED vertical={vertical} tip={pen.brushTip.position} impact={u.ImpactPoint} platforms={u.SpawnedPlatformCount} height={u.Platforms.Min(p=>p.TargetTop)}..{u.Platforms.Max(p=>p.TargetTop)} size={u.Platforms.Min(p=>p.Radius)*2}..{u.Platforms.Max(p=>p.Radius)*2}";
    }
    public static string Rig()
    {
        var u=UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>();var p=u.AvatarRoot.GetComponent<CalligraphyBrushPresentation>();
        return $"root={p.transform.position} base={p.avatarBasePosition} target={p.avatarBrushTarget} weight={p.avatarVerticalWeight} axis={p.brushPivot.forward} tip={p.brushTip.position} grip={p.brushPivot.position} localTip={p.brushPivot.InverseTransformPoint(p.brushTip.position)} scale={p.brushPivot.localScale} RH={p.rightHand.position} LH={p.leftHand.position}";
    }
    public static string Face()
    {
        var u=UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>();var p=u.AvatarRoot.GetComponent<CalligraphyBrushPresentation>();
        var head=p.animator.GetComponentsInChildren<Transform>().First(t=>t.name=="Head");
        Frame(head.position,head.position+p.transform.forward*21+p.transform.right*2);
        return Rig();
    }
    public static async Task<string> ClimbProbe()
    {
        Time.timeScale=1;var u=UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>();
        var rock=u.Platforms[0];var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();var cc=p.GetComponent<CharacterController>();
        rock.TryLedge(rock.transform.position+Vector3.right*(rock.Radius+3),out var e,out var into);
        float bottom=p.transform.position.y-cc.bounds.min.y;
        Warp(p,e-into*.8f+Vector3.up*(bottom-1.4f));
        FaceEdge(p,e,into);PressW(true);
        var clear=typeof(CalligraphyPlatformClimb).GetMethod("ClearTop",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        string info=$"e={e} in={into} player={p.transform.position} bottomOffset={bottom} feet={cc.bounds.min.y} ground={motor.IsGrounded} stunned={motor.IsStunned} lock={motor.ActionsLocked} human={p.GetComponentInChildren<Animator>().isHuman} enabled={u.Climber.enabled} clear={clear.Invoke(u.Climber,new object[]{rock,e,into})}";
        await Wait(.1f);Time.timeScale=0;return info+" climbing="+u.Climber.Climbing+" W="+Keyboard.current.wKey.isPressed+" input="+motor.inputEnabled+" facing="+Vector3.Dot(p.transform.forward,into)+" waist="+p.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips).position.y;
    }
    public static async Task<string> TetherAndInterrupt()
    {
        using var keyboard=new TestKeyboard();
        var b=Setup();b.inkAvatarUltimate.roarSeconds=.7f;b.inkAvatarUltimate.avatar.appearSeconds=.5f;
        b.inkAvatarUltimate.sweep.seconds=.5f;b.inkAvatarUltimate.sweep.interval=.1f;b.inkAvatarUltimate.slam.seconds=.8f;
        var player=PlayerHealth.Find();var motor=player.GetComponent<ThirdPersonController>();var cc=player.GetComponent<CharacterController>();
        await Until(()=>b.AvatarUltimate && b.AvatarUltimate.PlayerLaunched,8,"No launch");var u=b.AvatarUltimate;await Wait(4);
        var rock=u.Platforms[0];rock.TryLedge(rock.transform.position+Vector3.right*(rock.Radius+2),out var e,out var into);
        float offset=player.transform.position.y-cc.bounds.min.y;
        Warp(player,e-into*7+Vector3.up*(offset-1.3f));FaceEdge(player,e,into);PressW(true);
        var tether=player.GetComponent<EnergyTetherSkill>();
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var pointType=typeof(EnergyTetherSkill).GetNestedType("AimPoint",System.Reflection.BindingFlags.NonPublic);
        object point=Activator.CreateInstance(pointType);
        pointType.GetField("collider").SetValue(point,rock.GetComponent<Collider>());
        pointType.GetField("localPoint").SetValue(point,rock.transform.InverseTransformPoint(e-Vector3.up*1.2f));
        pointType.GetField("localNormal").SetValue(point,rock.transform.InverseTransformDirection(-into));
        pointType.GetField("hitSomething").SetValue(point,true);
        var routine=(System.Collections.IEnumerator)typeof(EnergyTetherSkill).GetMethod("PullPlayer",flags).Invoke(tether,new[]{point});
        tether.StartCoroutine(routine);
        await Until(()=>u.Climber.Climbing,3,"Real tether pull did not catch edge");
        PressW(false);
        Check(!tether.IsPullingPlayer,"Tether still owns motion during climb");
        Frame(e+Vector3.up*.15f,e-into*3+Vector3.Cross(Vector3.up,into)*1.5f+Vector3.up*1.1f);
        await Wait(.75f);Shot("tether-left-foot");await Wait(.4f);Shot("tether-right-foot");
        await Until(()=>!u.Climber.Climbing,3,"Tether climb stuck");
        Check(motor.enabled && motor.inputEnabled && !motor.ActionsLocked,"Tether input not restored");
        await Wait(.65f);Warp(player,e-into*.8f+Vector3.up*(offset-1.4f));FaceEdge(player,e,into);PressW(true);await Until(()=>u.Climber.Climbing,2,"Second edge catch missing");PressW(false);
        b.NextSkill();await Wait(.15f);
        Check(motor.enabled && cc.enabled && !motor.ActionsLocked && player.GetComponentInChildren<Animator>().enabled,"Interrupted climb leaked control ownership");
        Check(UnityEngine.Object.FindObjectsByType<CalligraphyPlatformClimb>().Length==0,"Climb owner leaked");
        player.SetPracticeProtection(b,false);
        return "PASS actual EnergyTetherSkill.PullPlayer coroutine → ledge catch → climb → restored input; next-skill interruption while hanging restores animator/capsule/actions";
    }
    public static async Task<string> IntentAndDragons()
    {
        using var keyboard=new TestKeyboard();
        var b=Setup();var player=PlayerHealth.Find();var cc=player.GetComponent<CharacterController>();
        try
        {
            await Until(()=>b.AvatarUltimate && b.AvatarUltimate.PlayerLaunched,15,"No launch");await Wait(5);
            var u=b.AvatarUltimate;var rock=u.Platforms[0];rock.TryLedge(rock.transform.position+Vector3.right*(rock.Radius+2),out var e,out var into);
            float offset=player.transform.position.y-cc.bounds.min.y;
            Warp(player,e-into*.8f+Vector3.up*(offset-1.4f));FaceEdge(player,e,into);await Wait(.18f);
            Check(!u.Climber.Climbing,"Auto climb with no W");
            Warp(player,e-into*.8f+Vector3.up*(offset-1.4f));FaceEdge(player,e,into,false);PressW(true);await Wait(.12f);
            Check(!u.Climber.Climbing,"Climbed facing away");PressW(false);
            Warp(player,e-into*.8f+Vector3.up*(offset-.25f));FaceEdge(player,e,into);PressW(true);await Wait(.12f);
            Check(!u.Climber.Climbing,"Climbed with waist above ledge");PressW(false);
            Warp(player,e-into*.8f+Vector3.up*(offset-1.4f));FaceEdge(player,e,into);PressW(true);
            await Until(()=>u.Climber.Climbing,1,"W facing rock below ledge did not climb");PressW(false);
            await Until(()=>!u.Climber.Climbing,3,"Climb never ended");
            foreach(int count in new[]{5,4,6,0})
            {
                b.paintingWorld.dragonCount=count;b.paintingWorld.entrySeconds=.4f;b.SelectSkill(3);
                await Until(()=>UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>(),3,"No painting");
                var w=UnityEngine.Object.FindAnyObjectByType<CalligraphyPaintingWorld>();await Wait(.65f);
                var dragons=w.GetComponentsInChildren<CalligraphyInkDragon>();Check(dragons.Length==count,"Wrong dragon count: "+count);
                var startField=typeof(CalligraphyInkDragon).GetField("start",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                var starts=dragons.Select(d=>(Vector3)startField.GetValue(d)).ToArray();
                if(count>1)
                {
                    Vector3 center=starts.Aggregate(Vector3.zero,(a,v)=>a+v)/count;
                    var angles=starts.Select(s=>Mathf.Repeat(Mathf.Atan2(s.z-center.z,s.x-center.x)*Mathf.Rad2Deg,360)).OrderBy(a=>a).ToArray();
                    for(int i=0;i<count;i++)Check(Mathf.Abs(Mathf.Repeat(angles[(i+1)%count]-angles[i],360)-360f/count)<.1f,"Uneven spawn angles");
                }
                b.NextSkill();await Wait(.15f);
            }
            return "PASS no W=no climb; facing away=no climb; waist above rim=no climb; W+facing+waist below=climb. Painting actual spawns: 5×72°, 4×90°, 6×60°, 0 disabled.";
        }
        finally{PressW(false);player.SetPracticeProtection(b,false);}
    }
    public static async Task<string> Portrait()
    {
        var b=Setup();await Until(()=>b.AvatarUltimate,3,"No avatar");var u=b.AvatarUltimate;await Wait(2.8f);
        var p=u.AvatarRoot.GetComponent<CalligraphyBrushPresentation>();var head=p.animator.GetComponentsInChildren<Transform>().First(t=>t.name=="Head");
        Frame(head.position+p.transform.forward*1+Vector3.down*1,head.position+p.transform.forward*24+p.transform.right*8+Vector3.down*5);
        Shot("portrait-roar");await Wait(.5f);Shot("portrait-motion");
        await Until(()=>u.CurrentPhase==CalligraphyInkAvatarUltimate.Phase.SecondSweep,8,"No second sweep");await Wait(.6f);
        Frame(head.position+Vector3.down*5,head.position+p.transform.forward*38+p.transform.right*18+Vector3.down*10);Shot("portrait-sweep");
        await Until(()=>u.PlayerLaunched,5,"No slam");await Wait(.5f);
        Frame(u.ImpactPoint+Vector3.up*19,u.ImpactPoint+p.transform.forward*65+p.transform.right*28+Vector3.up*23);Shot("vertical-plant");
        Time.timeScale=0;return "Paused after visual captures";
    }
    public static async Task<string> ClimbPose()
    {
        Time.timeScale=1;var u=UnityEngine.Object.FindAnyObjectByType<CalligraphyInkAvatarUltimate>();await Wait(4);
        var rock=u.Platforms[0];var player=PlayerHealth.Find();var cc=player.GetComponent<CharacterController>();
        rock.TryLedge(rock.transform.position+Vector3.right*(rock.Radius+3),out var e,out var into);
        Warp(player,e-into*.8f+Vector3.up*(player.transform.position.y-cc.bounds.min.y-1.4f));
        await Until(()=>u.Climber.Climbing,2,"No climb");await Wait(.45f);Time.timeScale=0;await Task.Delay(100);
        Frame(e+Vector3.up*.15f,e-into*3+Vector3.Cross(Vector3.up,into)*1.5f+Vector3.up*1.1f);Shot("climb-diagnostic");
        var a=player.GetComponentInChildren<Animator>();
        return "edge="+e+" progress="+u.Climber.Progress+" animator="+a.name+" root="+player.transform.position+"\n"+
            string.Join("\n",new[]{HumanBodyBones.Hips,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.LeftFoot}.Select(id=>{var t=a.GetBoneTransform(id);return id+"="+t.position+"/"+t.name;}))+"\n"+
            string.Join("\n",player.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r=>r.name+" bone0="+r.bones.FirstOrDefault()?.name+" includesHand="+r.bones.Contains(a.GetBoneTransform(HumanBodyBones.LeftHand))));
    }
    public static string Inspect()
    {
        var b=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossCombat>();
        var p=UnityEngine.Object.FindAnyObjectByType<ThirdPersonController>();
        return "Scene="+SceneManager.GetActiveScene().path+" dirty="+SceneManager.GetActiveScene().isDirty+
            "\nELDER BONES\n"+string.Join("\n",b.brush.animator.GetComponentsInChildren<Transform>(true).Select(t=>t.name+" "+t.localPosition))+
            "\nMESHES\n"+string.Join("\n",b.brush.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.name+" mesh="+r.sharedMesh.name+" readable="+r.sharedMesh.isReadable+" bones="+r.bones.Length+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m.name))))+
            "\nPLAYER\n"+p.name+" animator="+p.GetComponentInChildren<Animator>(true).isHuman;
    }
    public static async Task<string> PoolReload()
    {
        Check(EditorApplication.isPlaying,"Enter Play Mode");
        var fields=UnityEngine.Object.FindObjectsByType<CalligraphyInkField>();Check(fields.Length>0,"No ink field");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        foreach(var field in fields)
        {
            int before=field.transform.Cast<Transform>().Count(t=>t.name=="Pooled ink filament");
            typeof(CalligraphyInkField).GetField("strands",flags).SetValue(field,null);
            typeof(CalligraphyInkField).GetField("block",flags).SetValue(field,null);
            field.Emit(field.transform.position+Vector3.up,Vector3.up,.2f,.2f);
            typeof(CalligraphyInkField).GetMethod("Update",flags).Invoke(field,null);
            Check(field.ActiveCount>0,"Reloaded pool no longer emits");
            Check(field.transform.Cast<Transform>().Count(t=>t.name=="Pooled ink filament")==before,"Reload duplicated pool");
            field.Clear();
        }
        await Wait(.05f);
        return "PASS ink pool rebuild after lost nonserialized caches; emits again with no additional pooled children";
    }
}
