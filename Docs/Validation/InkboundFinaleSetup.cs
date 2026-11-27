using System;
using UnityEditor;
using UnityEngine;

public static class InkboundFinaleSetup
{
    // Absolute target values keep repeat runs from multiplying health again.
    public static string Run()
    {
        if(Application.isPlaying)throw new Exception("Run setup outside Play mode");
        string town="Assets/_Project/Prefabs/Boss/PF_InkboundTown_Elder.prefab";
        var elder=AssetDatabase.LoadAssetAtPath<GameObject>(town).GetComponent<CalligraphyBossCombat>();
        elder.maxHealth=15000;EditorUtility.SetDirty(elder);AssetDatabase.SaveAssetIfDirty(elder);
        string dojo="Assets/_Project/Prefabs/NPC/PF_DojoMaster.prefab";
        var master=AssetDatabase.LoadAssetAtPath<GameObject>(dojo);
        master.GetComponent<DojoMasterBoss>().maxHealth=9000;
        master.GetComponent<BossHealth>().maxHealth=9000;
        EditorUtility.SetDirty(master.GetComponent<DojoMasterBoss>());EditorUtility.SetDirty(master.GetComponent<BossHealth>());
        AssetDatabase.SaveAssetIfDirty(master.GetComponent<DojoMasterBoss>());
        return "Town elder 10000 -> 15000; dojo 6000 -> 9000; carried health keeps its encounter's max and fraction.";
    }
}
