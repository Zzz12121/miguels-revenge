using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DojoTourSetup
{
    public static string Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop preview Play mode before persisting the tour camera.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity")
            throw new InvalidOperationException("Install only in the dedicated dojo architecture preview scene.");
        var cameraObject = GameObject.Find("PreviewLightingAndCameras/Main Camera");
        var architecture = GameObject.Find("FinalDojoArena/Architecture");
        if (cameraObject == null || architecture == null) throw new InvalidOperationException("Preview camera/architecture root missing.");
        var tour = cameraObject.GetComponent<DojoArchitectureTour>();
        if (tour == null) tour = cameraObject.AddComponent<DojoArchitectureTour>();
        var serialized = new SerializedObject(tour);
        serialized.FindProperty("architectureRoot").objectReferenceValue = architecture.transform;
        serialized.FindProperty("startAtEntrance").boolValue = true;
        serialized.FindProperty("showControls").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        cameraObject.GetComponent<Camera>().orthographic = false;
        tour.Visit(2);
        EditorUtility.SetDirty(tour);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return "Saved free-flight camera and entrance starting view in the dojo scene only.";
    }
}
