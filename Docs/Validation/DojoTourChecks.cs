using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class DojoTourChecks
{
    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static string Run()
    {
        Require(Application.isPlaying, "Enter preview Play mode first.");
        var tour = Camera.main.GetComponent<DojoArchitectureTour>();
        Require(tour != null && tour.enabled, "Saved preview camera must have active tour controls.");
        var originalKeyboard = Keyboard.current;
        var originalMouse = Mouse.current;
        var savedCursor = Cursor.lockState;
        bool savedVisible = Cursor.visible;
        var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        Keyboard keyboard = null;
        Mouse mouse = null;
        var update = typeof(DojoArchitectureTour).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        var roofs = GameObject.Find("FinalDojoArena").GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "RemovableRoofs" || t.name == "RemovableCeiling").Select(t => t.gameObject).ToArray();
        bool[] initialRoofs = roofs.Select(g => g.activeSelf).ToArray();
        int checks = 0;
        try
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var dynamicUpdate = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null);
            Action pump = () => dynamicUpdate.Invoke(null, new object[] { InputUpdateType.Dynamic });
            keyboard = InputSystem.AddDevice<Keyboard>("Dojo tour validation keyboard");
            mouse = InputSystem.AddDevice<Mouse>("Dojo tour validation mouse");
            InputSystem.EnableDevice(keyboard); InputSystem.EnableDevice(mouse);
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            Action<Key[]> keys = pressed =>
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
                pump();
                keyboard.MakeCurrent(); mouse.MakeCurrent();
                update.Invoke(tour, null);
            };
            keys(new[] { Key.F3 });
            Require(Vector3.Distance(tour.transform.position, new Vector3(0, 3, 82)) < .01f,
                "F3 input must place the camera inside the hall. position="+tour.transform.position+
                " pressed="+keyboard.f3Key.isPressed+" edge="+keyboard.f3Key.wasPressedThisFrame+
                " enabled="+keyboard.enabled+" update="+InputState.currentUpdateType+
                " current="+(Keyboard.current==keyboard)); checks++;
            keys(new Key[0]);
            Vector3 before = tour.transform.position;
            keys(new[] { Key.W });
            float regular = Vector3.Distance(tour.transform.position, before);
            Require(regular > 0, "W input must move the actual preview camera."); checks++;
            before = tour.transform.position;
            keys(new[] { Key.W, Key.LeftShift });
            Require(Vector3.Distance(tour.transform.position, before) > regular * 2.5f, "Shift must speed up actual W movement."); checks++;
            keys(new Key[0]);
            before = tour.transform.position;
            keys(new[] { Key.E });
            Require(tour.transform.position.y > before.y, "E must raise the camera."); checks++;
            keys(new Key[0]);
            keys(new[] { Key.H });
            Require(tour.IsCutaway && roofs.All(g => !g.activeSelf), "H must hide roof and ceiling groups."); checks++;
            keys(new Key[0]); keys(new[] { Key.H });
            Require(!tour.IsCutaway && roofs.Select(g => g.activeSelf).SequenceEqual(initialRoofs), "H again must restore prior visibility."); checks++;
            keys(new Key[0]);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            pump(); mouse.MakeCurrent(); keyboard.MakeCurrent(); update.Invoke(tour, null);
            Require(tour.IsLooking, "Right mouse button must enable look control."); checks++;
            Quaternion rotation = tour.transform.rotation;
            var drag = new MouseState().WithButton(MouseButton.Right); drag.delta = new Vector2(80, -20);
            InputSystem.QueueStateEvent(mouse, drag);
            pump(); mouse.MakeCurrent(); keyboard.MakeCurrent(); update.Invoke(tour, null);
            Require(Quaternion.Angle(rotation, tour.transform.rotation) > 2, "Right-drag mouse delta must rotate the actual camera."); checks++;
            keys(new[] { Key.Escape });
            Require(!tour.IsLooking && Cursor.lockState == CursorLockMode.None, "Escape must release mouse capture even while right button is held."); checks++;
            InputSystem.QueueStateEvent(mouse, new MouseState()); pump(); keys(new Key[0]);
            keys(new[] { Key.F2 });
            Require(Vector3.Distance(tour.transform.position, new Vector3(0, 2.7f, 74)) < .01f, "F2 input must return to the entrance."); checks++;
            tour.SetCutaway(true); tour.enabled = false;
            Require(roofs.Select(g => g.activeSelf).SequenceEqual(initialRoofs), "Disabling the tour must restore roof visibility."); checks++;
            tour.enabled = true;
            var result = new { success = true, checks, scene = tour.gameObject.scene.name,
                input = "Virtual Input System devices exercised real Update for F2/F3/W/Shift/E/H/right-drag/Escape; physical devices restored." };
            Directory.CreateDirectory("output/dojo-building");
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText("output/dojo-building/tour-checks.json", json);
            return json;
        }
        finally
        {
            if (tour != null) { if (!tour.enabled) tour.enabled = true; tour.SetCutaway(false); tour.Visit(2); }
            for (int i = 0; i < roofs.Length; i++) if (roofs[i] != null) roofs[i].SetActive(initialRoofs[i]);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (originalKeyboard != null && originalKeyboard.added) originalKeyboard.MakeCurrent();
            if (originalMouse != null && originalMouse.added) originalMouse.MakeCurrent();
            Cursor.lockState = savedCursor; Cursor.visible = savedVisible;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }
    }
}
