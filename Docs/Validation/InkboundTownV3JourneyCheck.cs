using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

// Focused integration: starts at the existing transformation, runs the real half-health
// escape, then sends E at the revealed painting. Does not claim to test the opening dialogue.
public static class InkboundTownV3JourneyCheck
{
    const string Folder="output/inkbound-town/v3/journey";
    static void Progress(string phase)=>File.WriteAllText(Folder+"/progress.json",phase);
    static async Task Wait(Func<bool> condition,int seconds)
    {float until=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>until)throw new Exception("Timed out: "+File.ReadAllText(Folder+"/progress.json"));await Task.Delay(80);}}
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");Directory.CreateDirectory(Folder);
        var manager=GameManager.Instance;bool autosave=manager&&manager.autoSaveOnLevelStart;
        try
        {
            if(manager)manager.autoSaveOnLevelStart=false;
            Progress("Loading existing dojo scene");var load=SceneManager.LoadSceneAsync("DojoArena_ArchitecturePreview");await Wait(()=>load.isDone,90);await Task.Delay(600);
            var opening=Object.FindAnyObjectByType<DojoOpeningCutscene>();if(opening){opening.StopAllCoroutines();opening.enabled=false;}
            var encounter=Object.FindAnyObjectByType<DojoCalligraphyEncounter>();var master=encounter.GetComponent<DojoMasterBoss>();var health=master.GetComponent<BossHealth>();
            var player=PlayerHealth.Find();var guard=new GameObject("Temporary journey protection");player.SetPracticeProtection(guard,true);player.SetHealth(player.maxHealth,73);
            master.transform.SetPositionAndRotation(new Vector3(0,0,58),Quaternion.Euler(0,180,0));
            Progress("Existing transformation");encounter.StartCoroutine(encounter.TransformAfterDialogue(null));await Wait(()=>encounter.Transformed,20);
            master.BeginFight();await Task.Delay(250);health.SetHealth(health.maxHealth*.5f);
            var escape=master.GetComponent<DojoElderEscape>();Progress("Existing half-health escape performance");
            float until=Time.realtimeSinceStartup+100;
            while(!escape.Completed)
            {
                Progress("Escape: "+escape.Phase);
                if(StoryDialogue.Active)
                {var dialogue=Object.FindAnyObjectByType<StoryDialogue>();typeof(StoryDialogue).GetMethod("Finish",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialogue,null);}
                if(Time.realtimeSinceStartup>until)throw new Exception("Escape did not finish");await Task.Delay(100);
            }
            var bridge=master.GetComponent<InkboundTownEntranceBridge>();
            var portal=typeof(InkboundTownEntranceBridge).GetField("portal",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(bridge) as Transform;
            await Task.Delay(1500);bool voluntary=SceneManager.GetActiveScene().name=="DojoArena_ArchitecturePreview";
            var cc=player.GetComponent<CharacterController>();cc.enabled=false;player.transform.position=portal.position+portal.forward*1.8f+Vector3.up;cc.enabled=true;player.GetComponent<ThirdPersonController>().ResetMotion();
            float carry=health.Health,max=health.maxHealth;Vector3 portalPosition=portal.position;
            Progress("Sending E beside the unrolled painting");InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));await Task.Delay(140);InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
            await Wait(()=>SceneManager.GetActiveScene().name==InkboundTownJourney.Scene,90);
            await Wait(()=>Object.FindAnyObjectByType<InkboundTownEncounter>()?.Health!=null,20);await Task.Delay(800);
            var town=Object.FindAnyObjectByType<InkboundTownEncounter>();var arrived=PlayerHealth.Find();
            var report=new{escape.Started,escapeCompleted=true,voluntary,portalPosition=portalPosition.ToString(),scene=SceneManager.GetActiveScene().name,carriedBossHealth=Mathf.Abs(town.Health.Health-carry)<.01f,carriedMaximum=Mathf.Abs(town.Health.maxHealth-max)<.01f,health=town.Health.Health,players=Object.FindObjectsByType<PlayerHealth>().Length,playerHealth=arrived.Health,playerStateCarried=Mathf.Abs(arrived.Health-73)<.1f,town.State};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Folder+"/checks.json",json);Progress("Finished");return json;
        }
        catch(Exception e){File.WriteAllText(Folder+"/error.txt",e.ToString());throw;}
        finally{if(Keyboard.current!=null)InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());if(manager)manager.autoSaveOnLevelStart=autosave;}
    }
}
