using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class RangeControlsAndPaintingChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task Frames(int count=3){int target=Time.frameCount+count;var watch=System.Diagnostics.Stopwatch.StartNew();while(Time.frameCount<target){Check(watch.Elapsed.TotalSeconds<8,"Editor not ticking");await Task.Delay(16);}}
    static CalligraphyBossCombat Setup()
    {
        Check(EditorApplication.isPlaying,"Enter Play Mode first");
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();Check(range,"Open range first");
        range.StartTest();var boss=range.Boss;boss.CancelCast();boss.enabled=false;return boss;
    }
    static string Capture(string name)
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);view.Repaint();
        var rt=type.GetField("m_RenderTexture",Private).GetValue(view) as RenderTexture;Check(rt,"No Game View");
        var before=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();RenderTexture.active=before;
        if(SystemInfo.graphicsUVStartsAtTop){var p=tex.GetPixels();var flip=new Color[p.Length];for(int y=0;y<rt.height;y++)Array.Copy(p,y*rt.width,flip,(rt.height-1-y)*rt.width,rt.width);tex.SetPixels(flip);tex.Apply();}
        string path="Logs/RangeControls-"+name+".png";System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.Destroy(tex);return path;
    }
    static void Keys(Keyboard keyboard,params Key[] keys)
    {
        // Pipeline runs on the Editor loop: use a real Dynamic input step and
        // direct device state so background/future event timestamps cannot hide presses.
        var dynamicUpdate=typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null);
        void Tick()=>dynamicUpdate.Invoke(null,new object[]{InputUpdateType.Dynamic});
        Tick();keyboard.MakeCurrent();
        foreach(var key in keyboard.allKeys){bool prime=key.wasPressedThisFrame;}
        InputState.Change(keyboard,new KeyboardState(),InputUpdateType.Dynamic);Tick();
        InputState.Change(keyboard,new KeyboardState(keys),InputUpdateType.Dynamic);keyboard.MakeCurrent();
    }
    public static async Task<string> Rebinding()
    {
        var boss=Setup();var player=PlayerHealth.Find();await Frames();var settings=player.GetComponent<RangeInputSettings>();Check(settings,"Range panel not bootstrapped");
        var motor=player.GetComponent<ThirdPersonController>();var stored=(Key[])typeof(RangeInputSettings).GetField("keys",Private).GetValue(settings);var originalKeys=stored.ToArray();
        var originalKeyboard=Keyboard.current;var keyboard=InputSystem.AddDevice<Keyboard>();
        var background=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.EnableDevice(keyboard);
        string[] prefs={"RangeControls.v1.Sprint","RangeControls.v1.Jump","RangeControls.v1.Roll"};
        bool[] existed=prefs.Select(PlayerPrefs.HasKey).ToArray();int[] saved=prefs.Select(k=>PlayerPrefs.GetInt(k)).ToArray();
        var read=typeof(ThirdPersonController).GetMethod("ReadMoveInput",Private);var update=typeof(RangeInputSettings).GetMethod("Update",Private);
        void Input(Key key,int field,bool expected)
        {Keys(keyboard,key);object[] args={false,false,false};read.Invoke(motor,args);Check((bool)args[field]==expected,"Binding failed: "+key+" field "+field);}
        float priorTime=Time.timeScale;bool priorLock=motor.ActionsLocked;
        try
        {
            stored[0]=Key.LeftShift;stored[1]=Key.Space;stored[2]=Key.C;
            Keys(keyboard,Key.K);bool pressed=keyboard.kKey.wasPressedThisFrame;update.Invoke(settings,null);Check(settings.IsOpen && Time.timeScale==0 && motor.ActionsLocked,"K did not open/pause/lock controls: pressed="+pressed+" enabled="+keyboard.enabled+" panel="+settings.IsOpen+" time="+Time.timeScale+" overlay="+GameUI.OverlayOpen+" aiming="+SkillAimCancellation.IsAiming+" cinematic="+UnityEngine.Object.FindObjectsByType<VoidRendSkillDemo>().Any(s=>s.IsPlaying));
            await Frames();Capture("panel");
            settings.BeginRebind(RangeInputSettings.Action.Roll);Keys(keyboard,Key.Space);update.Invoke(settings,null);
            Check(stored[2]==Key.C && settings.Feedback.Contains("衝突"),"Duplicate action accepted");await Frames();Capture("conflict");
            foreach(var key in new[]{Key.W,Key.Digit1,Key.Q,Key.G,Key.K,Key.F8,Key.UpArrow})
                Check(!settings.TryRebind(RangeInputSettings.Action.Roll,key,false),"Reserved key accepted: "+key);
            settings.BeginRebind(RangeInputSettings.Action.Jump);Keys(keyboard,Key.Escape);update.Invoke(settings,null);
            Check(settings.IsOpen && settings.WaitingFor<0 && SkillAimCancellation.EscapeConsumedThisFrame,"Esc capture cancellation leaked to pause/menu");
            Check(settings.TryRebind(RangeInputSettings.Action.Sprint,Key.LeftCtrl,false),"Sprint rebind failed");
            Check(settings.TryRebind(RangeInputSettings.Action.Jump,Key.J,false),"Jump rebind failed");
            Check(settings.TryRebind(RangeInputSettings.Action.Roll,Key.V,false),"Roll rebind failed");
            settings.BeginRebind(RangeInputSettings.Action.Jump);Keys(keyboard,Key.L);update.Invoke(settings,null);
            Check(stored[1]==Key.L && settings.WaitingFor<0,"Captured key did not apply");
            Check(PlayerPrefs.GetInt("RangeControls.v1.Jump")== (int)Key.L,"Capture did not persist");
            var probe=new GameObject("Binding reload probe");
            try
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(probe,player.gameObject.scene);
                var reloaded=probe.AddComponent<RangeInputSettings>();
                Check(RangeInputSettings.Binding(reloaded,RangeInputSettings.Action.Jump)==Key.L,"Saved key not restored on new component");
            }
            finally{UnityEngine.Object.DestroyImmediate(probe);typeof(RangeInputSettings).GetField("instance",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,settings);}
            await Frames();Capture("rebound");Input(Key.L,1,false);
            Keys(keyboard,Key.K);update.Invoke(settings,null);
            Check(!settings.IsOpen && Time.timeScale==priorTime && motor.ActionsLocked==priorLock,"Close did not restore time/action state");
            Input(Key.LeftCtrl,0,true);Input(Key.L,1,true);Input(Key.V,2,true);
            Input(Key.LeftShift,0,false);Input(Key.Space,1,false);Input(Key.C,2,false);
            return "PASS K panel, three binding cards, duplicate/reserved conflicts, capture + persistence/reload, Esc cancellation, live remapped sprint/jump/roll, original keys disabled, paused input suppressed, close restores state";
        }
        finally
        {
            settings.Close();Array.Copy(originalKeys,stored,3);
            for(int i=0;i<prefs.Length;i++){if(existed[i])PlayerPrefs.SetInt(prefs[i],saved[i]);else PlayerPrefs.DeleteKey(prefs[i]);}PlayerPrefs.Save();
            InputSystem.RemoveDevice(keyboard);if(originalKeyboard!=null)originalKeyboard.MakeCurrent();InputSystem.settings.backgroundBehavior=background;boss.enabled=true;boss.StopBattle();
        }
    }
    public static async Task<string> PaintingSkills()
    {
        var boss=Setup();var player=PlayerHealth.Find();await Frames();var gate=player.GetComponent<PlayerSkillAvailability>();Check(gate,"No player skill gate");
        var skill=UnityEngine.Object.FindObjectsByType<VoidRendSkillDemo>().First(s=>s.caster==player.transform);
        var previousTarget=skill.target;
        var tether=player.GetComponent<EnergyTetherSkill>();Check(tether,"No tether");
        var original=Keyboard.current;var keyboard=InputSystem.AddDevice<Keyboard>();
        var background=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.EnableDevice(keyboard);
        var go=new GameObject("Painting skill validation");go.transform.SetParent(boss.EffectsRoot,false);var world=go.AddComponent<CalligraphyPaintingWorld>();
        float entry=boss.paintingWorld.entrySeconds,contained=boss.paintingWorld.containedSeconds,exit=boss.paintingWorld.exitSeconds;
        try
        {
            boss.paintingWorld.entrySeconds=.1f;boss.paintingWorld.containedSeconds=30;boss.paintingWorld.exitSeconds=.3f;
            skill.target=boss.transform;skill.PlaySkill();Check(skill.IsPlaying,"Outside skill fixture did not start");
            world.Begin(boss,player,boss.ink);Check(CalligraphyPaintingWorld.BlocksTether,"No painting boundary");
            Check(!skill.IsPlaying && player.GetComponent<ThirdPersonController>().enabled,"Painting entry failed to cancel/restore active skill");
            var begin=typeof(VoidRendSkillDemo).GetMethod("BeginTargeting",Private);var enumType=begin.GetParameters()[0].ParameterType;
            for(int i=1;i<=4;i++)
            {
                begin.Invoke(skill,new[]{Enum.ToObject(enumType,i)});
                Check(!(bool)typeof(VoidRendSkillDemo).GetProperty("IsTargeting",Private).GetValue(skill) && !skill.IsPlaying,"Player skill entered target/cast: "+i);
            }
            skill.PlaySkill();Check(!skill.IsPlaying,"Public PlaySkill bypassed gate");
            typeof(EnergyTetherSkill).GetMethod("BeginAiming",Private).Invoke(tether,new object[]{false});Check(!tether.IsAiming && !tether.IsExecuting,"Tether bypassed gate");
            foreach(var key in new[]{Key.Digit1,Key.Digit2,Key.Digit3,Key.Digit4})
            {
                typeof(PlayerSkillAvailability).GetField("noticeUntil",Private).SetValue(gate,0f);
                Keys(keyboard,key);typeof(PlayerSkillAvailability).GetMethod("Update",Private).Invoke(gate,null);
                Check(gate.NoticeVisible,"No blocked-skill message for "+key);
            }
            Check(Mathf.Abs(Time.timeScale-1)<.001f,"Blocked skill slowed time");
            await Frames(10);Capture("painting-blocked");
            UnityEngine.Object.DestroyImmediate(go);await Frames();Check(!CalligraphyPaintingWorld.BlocksTether && PlayerSkillAvailability.TryUse() && !gate.NoticeVisible,"Skills / notice not restored outside painting");
            begin.Invoke(skill,new[]{Enum.ToObject(enumType,1)});Check((bool)typeof(VoidRendSkillDemo).GetProperty("IsTargeting",Private).GetValue(skill),"Outside targeting did not recover");SkillAimCancellation.Cancel();
            return "PASS active skill cancelled on entry; all four targeting routes + public PlaySkill + tether blocked inside painting, exact message shown, no slow time; outside targeting restored";
        }
        finally
        {
            if(go)UnityEngine.Object.DestroyImmediate(go);SkillAimCancellation.Cancel();
            if(skill.IsPlaying)skill.CancelSkill();skill.target=previousTarget;
            boss.paintingWorld.entrySeconds=entry;boss.paintingWorld.containedSeconds=contained;boss.paintingWorld.exitSeconds=exit;
            InputSystem.RemoveDevice(keyboard);if(original!=null)original.MakeCurrent();InputSystem.settings.backgroundBehavior=background;boss.enabled=true;boss.StopBattle();
        }
    }
}
