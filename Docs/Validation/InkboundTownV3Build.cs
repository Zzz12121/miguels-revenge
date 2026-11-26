using System;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class InkboundTownV3Build
{
    public static string Run()
    {
        if(Application.isPlaying)throw new Exception("Exit Play mode first");
        var s=EditorSceneManager.GetActiveScene();
        if(s.path!=InkboundTownBuilder.ScenePath)throw new Exception("Open the existing Inkbound town scene first");
        if(s.isDirty)EditorSceneManager.SaveScene(s);
        InkboundTownBuilder.V3SliceOnly=false;InkboundTownBuilder.Build();return "V3 town rebuilt and saved";
    }
}
