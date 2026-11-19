using UnityEngine;

/// <summary>
/// 修正「劍刃方向反了」：骷髏騎士（Tripo 模型）的劍是直接蒙皮在手骨上的網格，
/// 套上 Mixamo 動畫後劍刃朝後（像反手握）。這裡在動畫算完之後，把握劍的手
/// 沿著「手腕 → 中指」那條軸轉 flipDegrees 度：劍刃翻到另一側，拳頭仍然握著。
///
/// 掛在有 Animator 的模型物件上（Enemy_SkeletonKnight 的 SkeletonKnightModel）。
/// 方向還是不對時，Play 中直接在 Inspector 調 flipDegrees / axisMode 就能即時看到結果。
/// </summary>
[DisallowMultipleComponent]
public sealed class SwordGripFix : MonoBehaviour
{
    public enum Axis { WristToFingers, PalmNormal, ThumbSide }

    [Tooltip("哪一隻手拿劍")]
    public HumanBodyBones hand = HumanBodyBones.RightHand;
    [Tooltip("繞哪一條軸轉：WristToFingers = 沿手指方向（劍刃前後翻轉，最常用）")]
    public Axis axisMode = Axis.WristToFingers;
    [Tooltip("轉幾度（180 = 劍刃完全反過來）")]
    [Range(-180f, 180f)] public float flipDegrees = 180f;

    Animator animator;
    Transform handBone, middle, index, pinky;
    Quaternion lastApplied;
    bool hasLast;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        bool right = hand == HumanBodyBones.RightHand;
        handBone = animator.GetBoneTransform(hand);
        middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        pinky = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        if (handBone == null) enabled = false;
    }

    void LateUpdate()
    {
        if (handBone == null || Mathf.Approximately(flipDegrees, 0f)) return;
        // Animator 這一幀沒有更新骨頭（畫面外被剔除）時不要再轉一次，不然會一直累加
        if (hasLast && Quaternion.Angle(handBone.localRotation, lastApplied) < 0.01f) return;

        Vector3 axis = LocalAxis();
        if (axis.sqrMagnitude < 1e-6f) return;
        handBone.localRotation = handBone.localRotation * Quaternion.AngleAxis(flipDegrees, axis.normalized);
        lastApplied = handBone.localRotation;
        hasLast = true;
    }

    /// <summary>手骨自己的座標系裡的旋轉軸</summary>
    Vector3 LocalAxis()
    {
        Vector3 toFingers = middle != null ? handBone.InverseTransformPoint(middle.position) : Vector3.up;
        switch (axisMode)
        {
            case Axis.ThumbSide:
                if (index != null && pinky != null) return handBone.InverseTransformPoint(index.position) - handBone.InverseTransformPoint(pinky.position);
                return Vector3.right;
            case Axis.PalmNormal:
                if (index != null && pinky != null)
                    return Vector3.Cross(toFingers, handBone.InverseTransformPoint(index.position) - handBone.InverseTransformPoint(pinky.position));
                return Vector3.forward;
            default:
                return toFingers;
        }
    }
}
