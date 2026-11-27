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
        master.GetComponent<DojoMasterBoss>().maxHealth=15000;
        master.GetComponent<BossHealth>().maxHealth=15000;
        EditorUtility.SetDirty(master.GetComponent<DojoMasterBoss>());EditorUtility.SetDirty(master.GetComponent<BossHealth>());
        AssetDatabase.SaveAssetIfDirty(master.GetComponent<DojoMasterBoss>());
        return "Shared elder HP: dojo 15000 -> escape at 7500; town continues 7500 / 15000, ultimate at 3750.";
    }
    public static string Verify()
    {
        var master=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/NPC/PF_DojoMaster.prefab");
        var town=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_InkboundTown_Elder.prefab");
        if(master.GetComponent<DojoMasterBoss>().maxHealth!=15000||master.GetComponent<BossHealth>().maxHealth!=15000||town.GetComponent<CalligraphyBossCombat>().maxHealth!=15000)
            throw new Exception("Elder health differs between scenes");
        if(Application.isPlaying)
        {
            var encounter=UnityEngine.Object.FindAnyObjectByType<InkboundTownEncounter>();
            if(!encounter||encounter.Health.maxHealth!=15000||encounter.Health.Health!=7500)throw new Exception("Town did not start at half of shared HP");
            InkboundTownJourney.Carry(encounter.Health);
            if(!InkboundTownJourney.Pending||InkboundTownJourney.MaximumHealth!=15000||InkboundTownJourney.RemainingHealth!=7500)
                throw new Exception("Journey did not retain max/remaining HP");
        }
        return "PASS: shared maximum 15000, half-health town arrival 7500, carried max/remaining unchanged.";
    }
}
