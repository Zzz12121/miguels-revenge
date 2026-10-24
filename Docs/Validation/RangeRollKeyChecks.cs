using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

/// <summary>Isolated input probes; no edits to the open scene or player state.</summary>
public static class RangeRollKeyChecks
{
    public static string Run()
    {
        if(!UnityEditor.EditorApplication.isPlaying)throw new Exception("Enter Play Mode before testing keyboard events");
        Scene Create(string name)
        {
            var scene=EditorSceneManager.NewPreviewScene();scene.name=name;return scene;
        }
        var original=Keyboard.current;
        var keyboard=InputSystem.AddDevice<Keyboard>();
        var background=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.EnableDevice(keyboard);
        var range=Create("VFX_VoidRend_Bocchi");
        var other=Create("Roll key regression probe");
        var rangeObject=new GameObject("Range input probe");
        var otherObject=new GameObject("Other scene input probe");
        try
        {
            SceneManager.MoveGameObjectToScene(rangeObject,range);SceneManager.MoveGameObjectToScene(otherObject,other);
            var rangeMotor=rangeObject.AddComponent<ThirdPersonController>();rangeMotor.enabled=false;
            var otherMotor=otherObject.AddComponent<ThirdPersonController>();otherMotor.enabled=false;
            var read=typeof(ThirdPersonController).GetMethod("ReadMoveInput",BindingFlags.Instance|BindingFlags.NonPublic);
            void Check(ThirdPersonController motor,Key key,bool expected)
            {
                var update=typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null);
                update.Invoke(null,new object[]{InputUpdateType.Dynamic});keyboard.MakeCurrent();
                foreach(var control in keyboard.allKeys){bool prime=control.wasPressedThisFrame;}
                InputState.Change(keyboard,new KeyboardState(),InputUpdateType.Dynamic);update.Invoke(null,new object[]{InputUpdateType.Dynamic});
                InputState.Change(keyboard,new KeyboardState(key),InputUpdateType.Dynamic);keyboard.MakeCurrent();
                object[] args={false,false,false};read.Invoke(motor,args);
                if((bool)args[2]!=expected)throw new Exception($"{motor.gameObject.scene.name}: {key} roll={(bool)args[2]}, expected={expected}");
            }
            var roll=RangeInputSettings.Binding(rangeMotor,RangeInputSettings.Action.Roll);
            Check(rangeMotor,roll,true);if(roll!=Key.V)Check(rangeMotor,Key.V,false);
            Check(otherMotor,Key.V,true);Check(otherMotor,Key.C,false);
            if(RangeInputSettings.Binding(otherMotor,RangeInputSettings.Action.Sprint)!=Key.LeftShift || RangeInputSettings.Binding(otherMotor,RangeInputSettings.Action.Jump)!=Key.Space)
                throw new Exception("Range bindings leaked to another scene");
            rangeMotor.inputEnabled=false;Check(rangeMotor,roll,false);
            return "PASS current range roll binding="+roll+"; other scene keeps V / Space / LeftShift; input lock preserved";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rangeObject);UnityEngine.Object.DestroyImmediate(otherObject);
            EditorSceneManager.ClosePreviewScene(range);EditorSceneManager.ClosePreviewScene(other);
            InputSystem.RemoveDevice(keyboard);if(original!=null)original.MakeCurrent();InputSystem.settings.backgroundBehavior=background;
        }
    }
}
