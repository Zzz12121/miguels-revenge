#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>Small repeatable contract checks; opt-in, no test-framework/package dependency.</summary>
public static class CalligraphyBossValidation
{
    [MenuItem("VFX/Calligraphy/Validate Battle Assets")]
    public static void Menu()=>Debug.Log(ValidateAssets());
    public static string ValidateAssets()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab");
        Require(prefab,"Battle prefab missing");
        var boss=prefab.GetComponent<CalligraphyBossCombat>();Require(boss,"Combat adapter missing");
        Require(boss.brush && boss.brush.brushTip && boss.brush.animator,"Brush/rig references missing");
        Require(boss.cloneTemplate && boss.ink,"Clone/ink references missing");
        Require(!prefab.GetComponent<CalligraphyPracticeTarget>(),"Competing practice IDamageable");
        Require(Mathf.Abs(boss.dodgeChance-1f/3)<.00001f,"Dodge default must be 1/3");
        Require(boss.paintingDragonScale>=2.2f,"Painting dragon must be at least twice the original 1.1 scale");
        Require(boss.paintingRadius>=64,"Painting footprint must be at least128m across");
        Require(boss.cloneTemplate.GetComponent<CalligraphyInkPresentation>(),"Original clone ink presentation missing");
        foreach(var m in new[]{boss.ink.ribbonMaterial,boss.ink.sculptureMaterial,boss.ink.paperMaterial,boss.ink.yinYangMaterial,boss.ink.landscapeMaterial,boss.ink.originalInkMaterial,boss.ink.originalEchoMaterial,boss.ink.suspendedWashMaterial,boss.ink.skyVisageMaterial})
        {
            Require(m && m.shader,"Material missing");
            foreach(var message in ShaderUtil.GetShaderMessages(m.shader))
                if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(message.message);
        }
        Require(boss.ink.environmentShader,"Environment shader missing");
        foreach(var message in ShaderUtil.GetShaderMessages(boss.ink.environmentShader))
            if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(message.message);
        return "PASS: rig, prefab references, independent health, 1/3 dodge, enlarged dragons, original ink/echo and wash shaders.";
    }
    [MenuItem("VFX/Calligraphy/Validate Battle Rules (Play Mode)")]
    public static void RuleMenu()=>Debug.Log(ValidateRules());
    public static string ValidateRules()
    {
        Require(Application.isPlaying,"Enter the firing range in Play Mode and start Boss Test first.");
        var range=UnityEngine.Object.FindAnyObjectByType<CalligraphyBossRangeTest>();
        Require(range && range.Running && range.Boss,"Start Boss Test first");
        var boss=range.Boss;float chance=boss.dodgeChance;var player=PlayerHealth.Find();
        boss.CancelCast();
        try
        {
            boss.dodgeChance=1;
            Require(!boss.TryEvade(CalligraphyAttackContext.NewCast(),1),"Skill1 must not evade");
            int cast=CalligraphyAttackContext.NewCast();Require(boss.TryEvade(cast,2),"100% eligible evade failed");
            Require(boss.TryEvade(cast,2),"Dodged cast not remembered");
            boss.CancelCast();boss.dodgeChance=0;cast=CalligraphyAttackContext.NewCast();
            Require(!boss.TryEvade(cast,3),"0% evade failed");boss.dodgeChance=1;
            Require(!boss.TryEvade(cast,3),"Same cast rerolled");
            var clone=CalligraphyClone.Spawn(boss.cloneTemplate,boss.ink,player,boss.transform.position+Vector3.right*4,boss.transform.rotation,true,boss.EffectsRoot);
            cast=CalligraphyAttackContext.NewCast();
            using(CalligraphyAttackContext.Enter(cast,2,1)){clone.TakeDamage(1,clone.transform.position);clone.TakeDamage(1,clone.transform.position);}
            Require(clone.hitsRemaining==4,"Duplicate hit counted");
            for(int i=2;i<=5;i++)using(CalligraphyAttackContext.Enter(cast,2,i))clone.TakeDamage(1,clone.transform.position);
            Require(clone.IsDead,"Fifth hit did not dissolve clone");
            return "PASS: skill1 exclusion, cast memory, five distinct clone hits. Test objects cleaned.";
        }
        finally{boss.dodgeChance=chance;boss.CancelCast();}
    }
    static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
}
#endif
