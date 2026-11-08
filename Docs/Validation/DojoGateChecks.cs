using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
public static class DojoGateChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task<string> Run()
    {
        Require(Application.isPlaying,"Play mode required");
        var gate=UnityEngine.Object.FindAnyObjectByType<DojoCourtyardGate>();Require(gate,"Gate missing");
        var player=PlayerHealth.Find();var cc=player.GetComponent<CharacterController>();var movement=player.GetComponent<ThirdPersonController>();
        var original=player.transform.position;var kb0=Keyboard.current;var inputBehavior=InputSystem.settings.editorInputBehaviorInPlayMode;
        var cursor=Cursor.lockState;bool visible=Cursor.visible;Keyboard keyboard=null;
        var pump=typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null);
        var update=typeof(DojoCourtyardGate).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
        Action<Key[]> keys=null;
        Action<Vector3> warp=p=>{cc.enabled=false;player.transform.position=p;cc.enabled=true;movement.ResetMotion();Physics.SyncTransforms();};
        Func<bool> barrier=()=>Physics.RaycastAll(new Vector3(2,2,1),Vector3.forward,8,~0,QueryTriggerInteraction.Ignore).Any(h=>h.collider.GetComponentInParent<DojoCourtyardGate>()==gate);
        async Task PressE()
        {
            // Explicitly advance the existing prompt's LateUpdate before injecting a single E frame.
            // Editor async continuations can otherwise resume before a complete player loop.
            keys(Array.Empty<Key>());update.Invoke(gate,null);
            var prompt=UnityEngine.Object.FindAnyObjectByType<InteractPrompt>();
            if(prompt)typeof(InteractPrompt).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(prompt,null);
            keys(new[]{Key.E});bool pressed=InteractPrompt.Pressed(gate);bool can=gate.CanInteract();bool moving=gate.IsMoving;
            update.Invoke(gate,null);keys(Array.Empty<Key>());await Task.Delay(50);
            if(can&&!moving&&!gate.CloseBlocked&&!pressed)throw new Exception("Prompt did not route E: keyboard="+Keyboard.current?.name+" virtual="+keyboard.eKey.wasPressedThisFrame+" owner="+(prompt?typeof(InteractPrompt).GetField("focusedOwner",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(prompt)?.ToString():"none"));
        }
        async Task Settle(){float deadline=Time.realtimeSinceStartup+4;while(gate.IsMoving&&Time.realtimeSinceStartup<deadline)await Task.Delay(30);Require(!gate.IsMoving,"Door animation did not finish");Physics.SyncTransforms();}
        try
        {
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard=InputSystem.AddDevice<Keyboard>("Dojo gate check");InputSystem.EnableDevice(keyboard);keyboard.MakeCurrent();
            keys=pressed=>{if(!keyboard.enabled)InputSystem.EnableDevice(keyboard);InputSystem.QueueStateEvent(keyboard,new KeyboardState(pressed));pump.Invoke(null,new object[]{InputUpdateType.Dynamic});keyboard.MakeCurrent();};
            keys(Array.Empty<Key>());
            Require(!gate.IsOpen&&barrier(),"Closed door must block the entrance");
            int walls=0;
            foreach(int side in new[]{-1,1})foreach(float x in new[]{6.8f,7.4f,7.95f})foreach(float y in new[]{.6f,3.2f})
            {Require(Physics.Raycast(new Vector3(side*x,y,2),Vector3.forward,6),"Gate-wall gap at "+x+" y="+y);walls++;}
            var garden=GameObject.Find("FinalDojoArena/Architecture/LivingGarden").transform;
            var top=garden.Cast<Transform>().ToArray();
            Require(top.Count(t=>t.name.StartsWith("CherryTree_"))==18,"18 front-yard cherry trees required");
            var rear=top.Where(t=>t.name.StartsWith("BlackPine_")||t.name.StartsWith("JapaneseMaple_")||t.name.StartsWith("Bamboo_")).ToArray();
            Require(rear.Length==32&&rear.All(t=>t.position.z>103&&Mathf.Abs(t.position.x)<47),"Non-cherry trees must be behind main hall and inside boundary");
            Require(rear.Where(t=>t.name.StartsWith("BlackPine_")).All(t=>t.position.y<0),"Pine root collars must be buried");
            warp(new Vector3(0,1.2f,8));await Task.Delay(150);
            Require(gate.CanInteract(),"Inside approach must be interactive");await PressE();Require(gate.IsOpen&&gate.IsMoving,"E must open gate from inside");
            Require(!gate.TryToggle(),"Repeated toggle during motion must be ignored");await Settle();Require(!barrier(),"Open gate must clear the walk-through opening");
            warp(new Vector3(0,1.2f,4));await Task.Delay(150);
            Require(gate.CloseBlocked,"Closing sweep must detect player");await PressE();Require(gate.IsOpen&&!gate.IsMoving,"E must not close onto player");
            warp(new Vector3(0,1.2f,1.5f));await Task.Delay(150);await PressE();await Settle();Require(!gate.IsOpen&&barrier(),"E must close from outside and restore collision");
            await Task.Delay(100);await PressE();await Settle();Require(gate.IsOpen&&!barrier(),"E must also open from outside");
            var follow=Camera.main.GetComponent<ThirdPersonCamera>();follow.yaw=0;follow.pitch=15;follow.SnapToTarget();
            keys(new[]{Key.W});float walkDeadline=Time.realtimeSinceStartup+8;
            while(player.transform.position.z<10&&Time.realtimeSinceStartup<walkDeadline)await Task.Delay(30);
            keys(Array.Empty<Key>());Require(player.transform.position.z>=10,"Actual player must walk through open gate with W");
            warp(new Vector3(0,1.2f,1.5f));await Task.Delay(150);
            await Task.Delay(100);await PressE();await Settle();Require(!gate.IsOpen&&barrier(),"Gate must close again");
            warp(original);await Task.Delay(150);await PressE();Require(!gate.IsOpen,"E far from gate must have no effect");
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{success=true,wallSamples=walls,cherries=18,rearTrees=rear.Length,checks="E opens/closes from both sides; W walks actual player through open gate; blocked close; moving debounce; distant input ignored; open/closed collider follows doors; connected walls; grounded rear pines"},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText("output/dojo-building/gate-and-layout-checks.json",json);return json;
        }
        finally
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(kb0!=null&&kb0.added)kb0.MakeCurrent();InputSystem.settings.editorInputBehaviorInPlayMode=inputBehavior;
            warp(original);var camera=Camera.main?Camera.main.GetComponent<ThirdPersonCamera>():null;if(camera)camera.SnapToTarget();Cursor.lockState=cursor;Cursor.visible=visible;
        }
    }
}
