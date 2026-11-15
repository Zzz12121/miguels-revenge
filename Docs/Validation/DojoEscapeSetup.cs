using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class DojoEscapeSetup
{
    public static string Install()
    {
        var cut=Object.FindAnyObjectByType<DojoOpeningCutscene>();
        string result=DojoEncounterPresentationInstaller.Configure(cut);
        EditorSceneManager.SaveScene(cut.gameObject.scene);AssetDatabase.SaveAssets();return result;
    }
}
