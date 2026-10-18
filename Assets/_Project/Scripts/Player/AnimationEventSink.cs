using UnityEngine;

/// <summary>
/// 接住 Starter Assets 動畫裡內建的 Animation Event（OnLand、OnFootstep）。
/// 這些事件原本是給 Starter Assets 的角色播音效用的；我們的腳步聲、落地聲由 ThirdPersonController 處理，
/// 這裡只是讓事件有地方接，不然 Console 會一直跳「AnimationEvent has no receiver」。
/// PlayerVisual 會自動加到模型（Animator 所在的物件）上。
/// </summary>
public class AnimationEventSink : MonoBehaviour
{
    void OnLand(AnimationEvent e) { }
    void OnFootstep(AnimationEvent e) { }
}
