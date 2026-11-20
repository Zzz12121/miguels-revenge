using UnityEngine;

/// <summary>
/// 埋伏弓箭手的程式姿勢（疊在 Animator 的動作上面，弓箭手的動畫包沒有蹲下 / 投降的動作）：
///   crouch   ：蹲在掩體後面——骨盆往下壓、雙腳留在原地（兩段骨 IK 讓膝蓋往前彎）、上身前傾縮起來
///   surrender：投降——跪低、右手舉高（手掌朝外）、左手把弓往前遞給玩家
/// 兩個都是 0~1 的目標值，實際權重會平滑地追上去（Weight / SurrenderWeight）。
/// </summary>
[DisallowMultipleComponent]
public sealed class ArcherCoverPose : MonoBehaviour
{
    [Range(0f, 1f)] public float crouch;
    [Range(0f, 1f)] public float surrender;
    [Tooltip("蹲下時骨盆往下壓多少（站姿骨盆高度的比例）")]
    [Range(0.1f, 0.6f)] public float crouchDepth = 0.4f;
    [Tooltip("投降跪低時骨盆往下壓多少（站姿骨盆高度的比例）")]
    [Range(0.1f, 0.6f)] public float kneelDepth = 0.33f;
    [Tooltip("蹲下時上身前傾幾度")]
    public float crouchLean = 24f;
    [Tooltip("蹲下 / 站起來的速度（每秒）")]
    public float blendSpeed = 5f;
    [Tooltip("遞弓的方向（世界座標；零向量 = 角色正前方）")]
    public Vector3 offerDirection;

    public float Weight { get; private set; }
    public float SurrenderWeight { get; private set; }

    Animator animator;
    Transform hips, spine, chest, head;
    Transform lUpper, lLower, lFoot, rUpper, rLower, rFoot;
    Transform lArm, lFore, lHand, rArm, rFore, rHand;
    float hipsHeight;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        hips = B(HumanBodyBones.Hips);
        spine = B(HumanBodyBones.Spine);
        chest = B(HumanBodyBones.Chest);
        if (chest == null) chest = spine;
        head = B(HumanBodyBones.Head);
        lUpper = B(HumanBodyBones.LeftUpperLeg); lLower = B(HumanBodyBones.LeftLowerLeg); lFoot = B(HumanBodyBones.LeftFoot);
        rUpper = B(HumanBodyBones.RightUpperLeg); rLower = B(HumanBodyBones.RightLowerLeg); rFoot = B(HumanBodyBones.RightFoot);
        lArm = B(HumanBodyBones.LeftUpperArm); lFore = B(HumanBodyBones.LeftLowerArm); lHand = B(HumanBodyBones.LeftHand);
        rArm = B(HumanBodyBones.RightUpperArm); rFore = B(HumanBodyBones.RightLowerArm); rHand = B(HumanBodyBones.RightHand);
        if (hips == null || lFoot == null || rFoot == null) { enabled = false; return; }
        hipsHeight = Mathf.Max(0.5f, hips.position.y - transform.position.y);
    }

    void LateUpdate()
    {
        float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        Weight = Mathf.Lerp(Weight, Mathf.Clamp01(Mathf.Max(crouch, surrender)), k);
        SurrenderWeight = Mathf.Lerp(SurrenderWeight, Mathf.Clamp01(surrender), k);
        if (Weight < 0.001f && SurrenderWeight < 0.001f) return;
        if (animator != null && !animator.enabled) return;   // 倒地（布娃娃）時不擺

        float w = SmoothTween.Evaluate(SmoothTween.Ease.InOutSine, Weight);
        float depth = Mathf.Lerp(crouchDepth, kneelDepth, SurrenderWeight);
        Vector3 fwd = transform.forward, right = transform.right;

        // 1. 先記下動畫裡雙腳的位置，再把骨盆往下壓
        Vector3 lTarget = lFoot.position, rTarget = rFoot.position;
        Quaternion lFootRot = lFoot.rotation, rFootRot = rFoot.rotation;
        hips.position += Vector3.down * (hipsHeight * depth * w) + fwd * (0.06f * w);

        // 2. 兩段骨 IK：腳踩回原位、膝蓋往前（跪的時候右膝更低一點）
        SolveLeg(lUpper, lLower, lFoot, lTarget, fwd, lFootRot);
        Vector3 rT = rTarget;
        if (SurrenderWeight > 0.01f) rT += -fwd * (0.25f * SurrenderWeight);   // 右腳往後收 → 單膝跪
        SolveLeg(rUpper, rLower, rFoot, rT, fwd, rFootRot);

        // 3. 上身：蹲著時縮起來前傾；投降時挺直一點、頭低下
        float lean = crouchLean * w * (1f - 0.6f * SurrenderWeight);
        if (spine != null) spine.rotation = Quaternion.AngleAxis(lean * 0.5f, right) * spine.rotation;
        if (chest != null && chest != spine) chest.rotation = Quaternion.AngleAxis(lean * 0.5f, right) * chest.rotation;
        if (head != null && SurrenderWeight > 0.01f) head.rotation = Quaternion.AngleAxis(18f * SurrenderWeight, right) * head.rotation;

        // 4. 投降的手：右手舉高、左手（拿弓）往前遞
        if (SurrenderWeight > 0.01f)
        {
            float s = SmoothTween.Evaluate(SmoothTween.Ease.OutCubic, SurrenderWeight);
            Vector3 offer = offerDirection.sqrMagnitude > 0.01f ? offerDirection.normalized : fwd;
            offer = (new Vector3(offer.x, 0f, offer.z).normalized + Vector3.down * 0.25f).normalized;
            if (lArm != null && lFore != null && lHand != null)
            {
                Aim(lArm, lFore, (offer + Vector3.down * 0.35f - right * 0.15f).normalized, s);
                Aim(lFore, lHand, offer, s);
            }
            if (rArm != null && rFore != null && rHand != null)
            {
                Aim(rArm, rFore, (Vector3.up * 0.75f + right * 0.55f + fwd * 0.1f).normalized, s);
                Aim(rFore, rHand, (Vector3.up + right * 0.1f).normalized, s);
            }
        }
    }

    /// <summary>馬上回到站姿（不慢慢過渡）：例如交出弓之後要直接轉身跑走</summary>
    public void ResetImmediate()
    {
        crouch = surrender = 0f;
        Weight = SurrenderWeight = 0f;
    }

    /// <summary>a = 大腿、b = 小腿、c = 腳；把 c 放到 target，膝蓋往 bendDir 彎</summary>
    static void SolveLeg(Transform a, Transform b, Transform c, Vector3 target, Vector3 bendDir, Quaternion footRot)
    {
        if (a == null || b == null || c == null) return;
        float ab = Vector3.Distance(a.position, b.position), bc = Vector3.Distance(b.position, c.position);
        Vector3 at = target - a.position;
        float d = Mathf.Clamp(at.magnitude, 0.05f, ab + bc - 0.002f);
        Vector3 dir = at.normalized;
        float cosA = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
        float angA = Mathf.Acos(cosA) * Mathf.Rad2Deg;
        Vector3 n = Vector3.Cross(dir, bendDir);
        if (n.sqrMagnitude < 1e-6f) return;
        // 把 dir 繞 n 轉 angA 度 = 往 bendDir 那邊偏 → 膝蓋的方向
        Vector3 kneePos = a.position + Quaternion.AngleAxis(angA, n.normalized) * dir * ab;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, kneePos - a.position) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
        c.rotation = footRot;
    }

    /// <summary>把 bone 轉到「bone → child 的方向 = dir」，weight 是混合比例</summary>
    static void Aim(Transform bone, Transform child, Vector3 dir, float weight)
    {
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-6f) return;
        Quaternion delta = Quaternion.FromToRotation(cur, dir);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
    }
}
