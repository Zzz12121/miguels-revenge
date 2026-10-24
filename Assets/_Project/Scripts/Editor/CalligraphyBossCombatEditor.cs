#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CalligraphyBossCombat))]
public sealed class CalligraphyBossCombatEditor : Editor
{
    void Group(string title,params string[] fields)
    {
        string key="CalligraphyInspector."+title;
        bool open=SessionState.GetBool(key,true);
        open=EditorGUILayout.BeginFoldoutHeaderGroup(open,title);SessionState.SetBool(key,open);
        if(open)
        {
            EditorGUI.indentLevel++;
            foreach(string name in fields){var property=serializedObject.FindProperty(name);if(property!=null)EditorGUILayout.PropertyField(property,true);}
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();EditorGUILayout.Space(3);
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("紅色：攻擊範圍；青色：生成範圍；黃色：投射物最大距離。選取此物件並開啟 Scene Gizmos。Play 模式調整不會儲存；永久設定請修改 Boss Prefab。",MessageType.Info);
        Group("共用／戰鬥與冷卻","maxHealth","moveSpeed","meleeDistance","recoverySeconds","skillRecovery","cycleDemonstration","dodgeDistance","dodgeChance");
        Group("0–1 飛墨一筆／連筆四式","projectileDamage","projectileSpeed","inkProjectile");
        Group("2 近戰／左右墨影","inkMelee");
        Group("3 畫中世界","paintingRadius","paintingCooldown","paintingWorld","disturbanceRadius","disturbanceStrength","paintingDragonDamage","paintingDragonScale");
        Group("4 天降墨龍","giantDragonScale","giantDragonDamage","dragonCooldown","inkDragon","onDragonImpact");
        Group("5 交叉運筆／墨光","beamDamage","inkBeam");
        Group("6 躍筆重砸／Frontal","slamDamage","leapSlam");
        Group("7 連環墨浪／Frontal Pulses","frontalPulses");
        Group("8 點墨落印／Rune","runeDamage","runeAttack");
        Group("死亡／水墨消散","deathPresentation");
        Group("參考物件","brush","ink","cloneTemplate","player");
        serializedObject.ApplyModifiedProperties();
        var boss=(CalligraphyBossCombat)target;
        if(Application.isPlaying && boss.Active)
        {
            if(boss.WaitingForPreview)EditorGUILayout.HelpBox("此招已結束，等待指令：↑ 重播、↓ 下一招。",MessageType.Info);
            if(GUILayout.Button("重播目前技能（↑）"))boss.ReplaySkill();
            EditorGUILayout.LabelField("測試指定技能（中止目前技能）",EditorStyles.boldLabel);
            for(int i=0;i<CalligraphyBossCombat.SkillNames.Length;i++)if(GUILayout.Button(i+" · "+CalligraphyBossCombat.SkillNames[i]))boss.SelectSkill(i);
        }
    }
}
#endif
