using UnityEngine;

/// <summary>
/// 揮刀時上半身的擺動更明顯：每一刀抬手時把腰往起刀的那一側扭（蓄力），砍下去時一路甩到另一側、身體往前壓、往砍的方向側彎，
/// 收招時回正。疊在原本的揮刀動作（PlayerVisualAnimator 的刀路、PlayerLoadout 的小刀 / 長槍）上面，只轉脊椎下段（Spine），
/// 腳和刀的判定都不動；頭大致保持看著前方。
/// 大刀三段：右下→左上、左下→右上、由上往下劈（最後一刀最重）；長槍是往前戳；小刀幅度小一點。空中的刀、衝刺斬、跳劈不加。
/// PlayerCombat.Awake 自動掛上。
/// </summary>
[DefaultExecutionOrder(32100)] // 在 PlayerVisualAnimator、PlayerLoadout（32000）擺完姿勢之後
[DisallowMultipleComponent]
public sealed class SwingBodyAccent : MonoBehaviour
{
    [Tooltip("整體幅度（1 = 預設，0 = 關掉）")]
    [Range(0f, 2f)] public float amount = 1f;

    /// <summary>一刀的身體姿勢：扭腰（+ = 往右轉）、前壓（+ = 往前）、側彎（+ = 往左彎），單位度</summary>
    struct Pose
    {
        public float twist, lean, bend;
        public Pose(float twist, float lean, float bend) { this.twist = twist; this.lean = lean; this.bend = bend; }
        public static Pose Lerp(Pose a, Pose b, float t) => new Pose(Mathf.LerpUnclamped(a.twist, b.twist, t), Mathf.LerpUnclamped(a.lean, b.lean, t), Mathf.LerpUnclamped(a.bend, b.bend, t));
    }

    // (抬手到底, 砍到底)
    static readonly Pose[][] Greatsword =
    {
        new[] { new Pose(22f, -4f, -5f), new Pose(-30f, 12f, 9f) },   // 右下 → 左上
        new[] { new Pose(-22f, -2f, 5f), new Pose(30f, 10f, -9f) },   // 左下 → 右上
        new[] { new Pose(12f, -16f, -3f), new Pose(-14f, 28f, 6f) },  // 由上往下劈
    };
    static readonly Pose[] Spear = { new Pose(16f, -6f, 0f), new Pose(-20f, 14f, 3f) };

    PlayerCombat combat;
    Animator animator;
    Transform spine, head;
    float swingAt = -1f;
    int index;
    float weight; // 平滑過的整體權重（換下一刀時不會跳）
    Pose current;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.OnSwing += OnSwing;
    }

    void OnDestroy()
    {
        if (combat != null) combat.OnSwing -= OnSwing;
    }

    void OnSwing(int i)
    {
        if (combat.IsAirSlash || combat.IsDashSlash || combat.IsPlunging) { swingAt = -1f; return; }
        swingAt = Time.time;
        index = i;
    }

    void LateUpdate()
    {
        if (amount <= 0f || combat == null) return;
        if (spine == null)
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (spine == null) return;
        }

        Pose target = default;
        float want = 0f;
        if (swingAt >= 0f && combat.IsAttacking && !combat.IsAirSlash && !combat.IsDashSlash && !combat.IsPlunging && !combat.IsAimingBow)
        {
            float t = Time.time - swingAt;
            float wu = Mathf.Max(0.02f, combat.WindupFor(index)), sw = Mathf.Max(0.02f, combat.SwingFor(index));
            float rec = combat.stepRecovery != null && index < combat.stepRecovery.Length ? combat.stepRecovery[index] : combat.recoveryTime;
            bool spear = PlayerLoadout.Current == WeaponLoadout.Spear;
            var move = spear ? Spear : Greatsword[index % Greatsword.Length];
            float scale = PlayerLoadout.UsingKnife ? 0.6f : 1f;
            if (t < wu)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / wu);
                target = Pose.Lerp(default, move[0], k);
                want = scale;
            }
            else if (t < wu + sw)
            {
                // 砍的時候甩過頭一點點再回來（不是等速轉過去）
                float k = (t - wu) / sw;
                float e = 1f - Mathf.Pow(1f - k, 3f);
                target = Pose.Lerp(move[0], move[1], e * 1.08f);
                want = scale;
            }
            else
            {
                float k = Mathf.Clamp01((t - wu - sw) / Mathf.Max(0.08f, rec));
                target = Pose.Lerp(move[1], default, Mathf.SmoothStep(0f, 1f, k));
                want = scale * (1f - k * 0.3f);
            }
        }
        else swingAt = combat.IsAttacking ? swingAt : -1f;

        // 姿勢平滑一點：抬手、甩身跟得很快，收招回正慢一點
        float rate = want > weight ? 18f : 7f;
        weight = Mathf.MoveTowards(weight, want, Time.deltaTime * rate);
        float a = 1f - Mathf.Exp(-40f * Time.deltaTime);
        current = Pose.Lerp(current, target, a);
        float w = weight * amount;
        if (w <= 0.001f) return;

        Transform actor = transform;
        Quaternion headRot = head != null ? head.rotation : Quaternion.identity;
        // 先扭腰（繞身體的上方軸），再繞扭過之後的左右軸前壓、繞扭過之後的前方軸側彎
        Quaternion twist = Quaternion.AngleAxis(current.twist * w, actor.up);
        Vector3 right = twist * actor.right, fwd = twist * actor.forward;
        Quaternion rot = Quaternion.AngleAxis(current.bend * w, fwd) * Quaternion.AngleAxis(current.lean * w, right) * twist;
        spine.rotation = rot * spine.rotation;
        // 頭跟著轉一點點就好（大致還是看著前方）
        if (head != null) head.rotation = Quaternion.Slerp(head.rotation, headRot, 0.7f);
    }
}
