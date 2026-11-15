using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class DojoRemoveCapsules
{
    public static string Run()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop play first");
        string path="Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity";var scene=SceneManager.GetSceneByPath(path);bool opened=scene.IsValid()&&scene.isLoaded;
        if(!opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        if(scene.isDirty)throw new Exception("Range has unsaved user edits");
        var capsules=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.sharedMesh&&f.sharedMesh.name=="Capsule"&&!f.GetComponentInParent<PlayerHealth>()).Select(f=>f.gameObject).Distinct().ToArray();
        string result=string.Join(", ",capsules.Select(g=>g.name));
        foreach(var capsule in capsules)Object.DestroyImmediate(capsule);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);if(!opened)EditorSceneManager.CloseScene(scene,true);
        return "Removed visible range capsule objects: "+result;
    }
}
