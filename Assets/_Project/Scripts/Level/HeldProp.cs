using UnityEngine;

/// <summary>
/// 拿在手上的道具（老漁夫的釣竿）：每幀跟著人物的手骨，方向照人物的朝向（不是手骨的方向，比較穩）。
/// 道具模型沿 +Y、握把在原點。有 line 的話畫一條釣線從竿尖垂到水面的浮標。
/// 道具不是人物的子物件：對話的半身像只拍人物本身，取景才不會被長長的釣竿和水面上的浮標拉遠。
/// 在 NpcRoutine（坐下時擺手臂）之後才跟上。
/// </summary>
[DefaultExecutionOrder(100)]
public class HeldProp : MonoBehaviour
{
    public Animator animator;
    public HumanBodyBones bone = HumanBodyBones.RightHand;
    [Tooltip("人物（方向照它的 forward）")]
    public Transform owner;
    [Tooltip("道具指向哪裡（人物座標：x 右、y 上、z 前）")]
    public Vector3 direction = new Vector3(0.1f, 0.55f, 0.85f);
    [Tooltip("道具前端在道具座標的位置（釣竿的竿尖）")]
    public Vector3 tipLocal = new Vector3(0f, 3f, 0f);
    public LineRenderer line;
    public Transform bobber;
    [Tooltip("水面的高度")]
    public float waterY;

    Transform hand;

    void LateUpdate()
    {
        if (owner == null) return;
        if (hand == null && animator != null && animator.isHuman) hand = animator.GetBoneTransform(bone);
        Vector3 pos = hand != null ? hand.position : owner.position + Vector3.up * 1f;
        Vector3 dir = owner.TransformDirection(direction).normalized;
        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f));
        bool visible = owner.gameObject.activeInHierarchy;
        Vector3 tip = transform.TransformPoint(tipLocal);
        Vector3 floatAt = new Vector3(tip.x, waterY + 0.03f + Mathf.Sin(Time.time * 1.7f) * 0.015f, tip.z);
        if (bobber != null) bobber.position = floatAt;
        if (line != null)
        {
            line.enabled = visible;
            line.SetPosition(0, tip);
            line.SetPosition(1, floatAt);
        }
    }
}
