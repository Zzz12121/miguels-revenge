#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CalligraphyBrushPresentation))]
public sealed class CalligraphyPresentationEditor : Editor
{
    int index;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var brush=(CalligraphyBrushPresentation)target;
        EditorGUILayout.HelpBox("Play 模式：切換原始動畫或雙手揮筆。展示不含 Boss AI／傷害。Grip Weight=0 可檢查未套持筆修正的原動畫。",MessageType.Info);
        using(new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if(brush.animationStates!=null && brush.animationStates.Length>0)
            {
                index=EditorGUILayout.Popup("Animation preview",Mathf.Clamp(index,0,brush.animationStates.Length-1),brush.animationStates);
                if(GUILayout.Button("播放選取動畫")) {brush.showcase=false;brush.PlayAnimation(index);}
            }
            if(GUILayout.Button("雙手揮筆")) brush.Sweep();
            if(GUILayout.Button("恢復自動展示")) brush.ResumeShowcase();
            if(GUILayout.Button("水墨分身")) brush.GetComponent<CalligraphyInkPresentation>()?.EmitDouble();
        }
    }
}
#endif
