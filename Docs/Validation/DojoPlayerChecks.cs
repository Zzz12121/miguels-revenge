using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
public static class DojoPlayerChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task<string> Run()
    {
        Require(Application.isPlaying, "Play mode required");
        var player = PlayerHealth.Find();
        var movement = player.GetComponent<ThirdPersonController>();
        var cc = player.GetComponent<CharacterController>();
        var follow = Camera.main.GetComponent<ThirdPersonCamera>();
        Require(Camera.allCamerasCount == 1 && UnityEngine.Object.FindObjectsByType<AudioListener>().Count(l=>l.isActiveAndEnabled)==1, "Exactly one camera and listener required");
        Require(!player.IsDead, "Player must be alive before the courtyard check");
        var originalKeyboard=Keyboard.current; var originalMouse=Mouse.current;
        var behavior=InputSystem.settings.editorInputBehaviorInPlayMode;
        var cursor=Cursor.lockState; bool visible=Cursor.visible;
        Keyboard keyboard=null; Mouse mouse=null;
        Vector3 start=player.transform.position;
        var pump=typeof(InputSystem).GetMethod("Update", BindingFlags.Static|BindingFlags.NonPublic, null,new[]{typeof(InputUpdateType)},null);
        Action<Key[]> keys=null;
        try
        {
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard=InputSystem.AddDevice<Keyboard>("Dojo player check"); mouse=InputSystem.AddDevice<Mouse>("Dojo player check mouse");
            InputSystem.EnableDevice(keyboard); InputSystem.EnableDevice(mouse);
            keys=pressed=> { InputSystem.QueueStateEvent(keyboard,new KeyboardState(pressed)); InputSystem.QueueStateEvent(mouse,new MouseState()); pump.Invoke(null,new object[]{InputUpdateType.Dynamic}); keyboard.MakeCurrent(); mouse.MakeCurrent(); };
            keys(new Key[0]);
            // Always exercise the same courtyard route even if the user was exploring the rear garden.
            cc.enabled=false;player.transform.position=new Vector3(0,1.19f,65);cc.enabled=true;movement.ResetMotion();
            follow.yaw=0;follow.pitch=15;follow.SnapToTarget();await Task.Delay(450);
            Require(cc.isGrounded && player.FeetY < .35f, "Player must stand on courtyard floor or shallow stepping stone");
            Vector3 spawn=player.transform.position;
            float before=player.transform.position.y;
            keys(new[]{Key.Space});
            typeof(ThirdPersonController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(movement,null);
            keys(new Key[0]);
            await Task.Delay(220);
            Require(player.transform.position.y > before+.25f, "Existing jump must lift the player");
            await Task.Delay(1300);
            Require(cc.isGrounded, "Jump must land back on courtyard");
            follow.yaw=0; follow.pitch=15; follow.SnapToTarget();
            keys(new[]{Key.W});
            float deadline=Time.realtimeSinceStartup+9;
            while (player.transform.position.z < 84 && Time.realtimeSinceStartup < deadline) await Task.Delay(30);
            keys(new Key[0]); await Task.Delay(450);
            Require(player.transform.position.z >= 84, "W must walk through front stairs and open hall doorway: "+player.transform.position);
            Require(cc.isGrounded && Math.Abs(player.FeetY-1.2f)<.18f, "Hall floor must support player: feet="+player.FeetY);
            Require(!player.IsDead && follow.target==player.transform, "Health and camera target must remain valid");
            var result=new {success=true, checks=7, spawn=spawn.ToString(), hallPosition=player.transform.position.ToString(), hallFeetY=player.FeetY, input="Virtual Keyboard exercised shared player jump and W movement from courtyard up stairs into main hall; no gameplay scripts modified."};
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText("output/dojo-building/player-checks.json",json);
            return json;
        }
        finally
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard); if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(originalKeyboard!=null&&originalKeyboard.added)originalKeyboard.MakeCurrent();
            if(originalMouse!=null&&originalMouse.added)originalMouse.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode=behavior;
            cc.enabled=false; player.transform.position=start; player.transform.rotation=Quaternion.identity; cc.enabled=true; movement.ResetMotion();
            follow.yaw=0; follow.pitch=15; follow.SnapToTarget();
            Cursor.lockState=cursor; Cursor.visible=visible;
        }
    }
}
