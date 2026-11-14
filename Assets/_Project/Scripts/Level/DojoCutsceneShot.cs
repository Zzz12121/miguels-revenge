using UnityEngine;

/// <summary>
/// 道場開場演出（DojoOpeningCutscene）裡的一個鏡頭。放在 DojoOpeningCutscene/Shots 底下，由上到下依序播放，
/// 每個鏡頭停一樣久（DojoOpeningCutscene 的 Shot Seconds），時間到就切下一個；最後的「高空俯視」鏡頭等兩人走到定位。
/// 固定鏡頭會慢慢往老師父的方向推近（dollySpeed）。
///   - 固定鏡頭：用這個物件的位置和方向拍（藍色箭頭 Z = 鏡頭看的方向）。
///     調整方法：在 Scene 視窗把視角轉到想要的畫面 → 選這個物件 → GameObject > Align With View（Ctrl+Shift+F）。
///     反過來想看這個鏡頭拍到什麼：選這個物件 → GameObject > Align View to Selected。
///   - 其他種類：位置跟著玩家 / 大門 / 會面點自動算，不用擺。
/// </summary>
[DisallowMultipleComponent]
public sealed class DojoCutsceneShot : MonoBehaviour
{
    public enum Kind
    {
        [InspectorName("固定鏡頭（用這個物件的位置）")] Fixed = 0,
        [InspectorName("玩家推門特寫（自動）")] PlayerCloseUp = 1,
        [InspectorName("高空俯視兩人走近（自動，最後一個）")] Meeting = 2,
        [InspectorName("玩家走進大門（自動）")] PlayerWalkIn = 3,
    }

    public Kind kind = Kind.Fixed;
    [Range(10f, 90f)] public float fov = 40f;

    [Header("老師父")]
    [Tooltip("這個鏡頭裡老師父在走路；關掉 = 老師父還坐著")]
    public bool masterWalks = true;
    [Tooltip("切到這個鏡頭時，老師父至少要在路線上的哪裡（公尺，從起點算；負數 = 從終點往回算）。還沒走到就直接移過去")]
    public float startMeters;

    [Header("對準（固定鏡頭）")]
    [Tooltip("鏡頭開始時自動轉向老師父的臉（位置還是用這個物件的位置）")]
    public bool lookAtMasterFace;
    [Tooltip("對準臉的時候，往上 / 往下偏多少公尺")]
    public float faceAimOffset = 0f;

    [Header("鏡頭移動（固定鏡頭）")]
    [Tooltip("鏡頭每秒往老師父的方向推近幾公尺（0 = 不動）")]
    [Min(0f)] public float dollySpeed = 0.2f;
    [Tooltip("推近到離老師父這麼近就停")]
    [Min(0.5f)] public float dollyMinDistance = 1.6f;

    void OnDrawGizmos()
    {
        if (kind != Kind.Fixed) return;
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawFrustum(Vector3.zero, fov, 2.5f, 0.15f, 16f / 9f);
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawSphere(transform.position, 0.12f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.35f, name);
#endif
    }
}
