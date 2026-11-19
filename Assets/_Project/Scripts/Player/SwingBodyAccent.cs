using UnityEngine;

/// <summary>
/// 揮刀時上半身的擺動更明顯：每一刀抬手時把腰往起刀的那一側扭（蓄力），砍下去時一路甩到另一側、身體往前壓、往砍的方向側彎，
/// 收招時回正。疊在原本的揮刀動作（PlayerVisualAnimator 的刀路、PlayerLoadout 的小刀 / 長槍）上面，只轉脊椎下段（Spine），
/// 腳和刀的判定都不動；頭大致保持看著前方。
/// 大刀三段：右下→左上、左下→右上、由上往下劈（最後一刀最重）；長槍是往前戳；小刀幅度小一點。空中的刀、衝刺斬、跳劈不加。
/// 大刀第三刀（劈）另外把兩隻手臂伸直：雙手握刀從頭頂後上方，沿著身體正前方一個大圓弧劈到前下方（手臂和刀成一直線），
/// 蓋過原本動畫裡彎著手、刀和手對不上的姿勢（StraightChop）。
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
        new[] { new Pose(6f, -10f, -2f), new Pose(-6f, 16f, 3f) },    // 由上往下劈（手臂伸直劈下去，身體只壓一點，不要折太多）
    };
    static readonly Pose[] Spear = { new Pose(16f, -6f, 0f), new Pose(-20f, 14f, 3f) };

    PlayerCombat combat;
    Animator animator;
    Transform spine, head;
    Transform rUpper, rFore, rHand, lUpper, lFore, lHand, rClav, lClav;
    PlayerWeaponPresenter presenter;
    float chopW; // 第三刀伸直手臂的權重（平滑過）
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
            rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rFore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lFore = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rClav = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            lClav = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            if (spine == null) return;
        }

        Pose target = default;
        float want = 0f;
        float chopWant = 0f, chopAngle = 0f;
        if (swingAt >= 0f && combat.IsAttacking && !combat.IsAirSlash && !combat.IsDashSlash && !combat.IsPlunging && !combat.IsAimingBow)
        {
            float t = Time.time - swingAt;
            float wu = Mathf.Max(0.02f, combat.WindupFor(index)), sw = Mathf.Max(0.02f, combat.SwingFor(index));
            float rec = combat.stepRecovery != null && index < combat.stepRecovery.Length ? combat.stepRecovery[index] : combat.recoveryTime;
            bool spear = PlayerLoadout.Current == WeaponLoadout.Spear;
            var move = spear ? Spear : Greatsword[index % Greatsword.Length];
            float scale = PlayerLoadout.UsingKnife ? 0.6f : 1f;
            // 大刀第三刀：手臂伸直的劈（角度：手臂和身體正前方水平線的夾角，+ = 往上）
            if (index % 3 == 2 && PlayerLoadout.Current == WeaponLoadout.Greatsword)
            {
                if (t < wu)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / wu);
                    chopAngle = Mathf.Lerp(60f, ChopHigh, k); // 舉到頭頂、稍微往後
                    chopWant = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (wu * 0.6f)));
                }
                else if (t < wu + sw)
                {
                    float k = (t - wu) / sw;
                    chopAngle = Mathf.Lerp(ChopHigh, ChopLow, 1f - Mathf.Pow(1f - k, 2.2f)); // 越劈越快
                    chopWant = 1f;
                }
                else
                {
                    float k = Mathf.Clamp01((t - wu - sw) / Mathf.Max(0.08f, rec));
                    chopAngle = ChopLow;
                    chopWant = 1f - Mathf.SmoothStep(0.25f, 1f, k); // 劈到底停一下，再放回原本的收刀動作
                }
            }
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
        chopW = Mathf.MoveTowards(chopW, chopWant, Time.deltaTime * (chopWant > chopW ? 10f : 5f));
        float a = 1f - Mathf.Exp(-40f * Time.deltaTime);
        current = Pose.Lerp(current, target, a);
        float w = weight * amount;
        if (w <= 0.001f) { chopW = 0f; return; }

        Transform actor = transform;
        Quaternion headRot = head != null ? head.rotation : Quaternion.identity;
        // 先扭腰（繞身體的上方軸），再繞扭過之後的左右軸前壓、繞扭過之後的前方軸側彎
        Quaternion twist = Quaternion.AngleAxis(current.twist * w, actor.up);
        Vector3 right = twist * actor.right, fwd = twist * actor.forward;
        Quaternion rot = Quaternion.AngleAxis(current.bend * w, fwd) * Quaternion.AngleAxis(current.lean * w, right) * twist;
        spine.rotation = rot * spine.rotation;
        // 頭跟著轉一點點就好（大致還是看著前方）
        if (head != null) head.rotation = Quaternion.Slerp(head.rotation, headRot, 0.7f);

        if (chopW > 0.001f) StraightChop(chopAngle, chopW * Mathf.Min(1f, amount), actor);
    }

    // ---------------- 大刀第三刀：手臂伸直劈下去 ----------------

    const float ChopHigh = 105f, ChopLow = -38f; // 舉到最高（頭頂稍微往後）、劈到底（前下方）
    const float ChopSlant = 10f;                 // 劈的平面稍微斜（右上 → 左下）
    const float GripGap = 0.075f;                // 左手握在右手下面多遠（劍柄上）
    const float Protract = 14f;                  // 兩邊肩膀往前、往中間收幾度（雙手握在中間時手臂才伸得直）

    /// <summary>
    /// 兩隻手臂伸直、雙手握刀：手在兩肩中間的正前方，沿著一個以肩膀為中心的大圓弧移動（angle = 手臂和水平前方的夾角）；
    /// 刀身接著手臂的方向（手臂和刀一直線），左手握在右手下面。w = 蓋過原本手臂姿勢多少
    /// </summary>
    void StraightChop(float angle, float w, Transform actor)
    {
        if (rUpper == null || rFore == null || rHand == null) return;
        // 刀是 PlayerWeaponPresenter 拿著的：遊戲開始以後才掛上，找不到就每次再找
        if (presenter == null) presenter = GetComponentInChildren<PlayerWeaponPresenter>(true) ?? GetComponentInParent<PlayerWeaponPresenter>();
        Vector3 up = actor.up, fwd = actor.forward, right = actor.right;
        // 劈的平面：包含身體正前方和上方，再繞前方斜一點點（舉起來時偏右、劈下去偏左）
        Quaternion slant = Quaternion.AngleAxis(ChopSlant, fwd);
        Vector3 planeUp = slant * up;
        float a = angle * Mathf.Deg2Rad;
        Vector3 dir = (fwd * Mathf.Cos(a) + planeUp * Mathf.Sin(a)).normalized;

        // 肩膀往前收（鎖骨往劈的方向轉一點）：兩手握在身體中間，手臂才不用彎
        foreach (var clav in new[] { rClav, lClav })
        {
            if (clav == null || clav.childCount == 0) continue;
            Vector3 c = (clav == rClav ? rUpper.position : (lUpper != null ? lUpper.position : clav.position)) - clav.position;
            Vector3 axis = Vector3.Cross(c, dir);
            if (axis.sqrMagnitude > 1e-8f) clav.rotation = Quaternion.AngleAxis(Protract * w, axis.normalized) * clav.rotation;
        }
        Vector3 rS = rUpper.position;
        Vector3 lS = lUpper != null ? lUpper.position : rS - right * 0.3f;
        Vector3 mid = (rS + lS) * 0.5f;
        float halfW = Vector3.Distance(rS, mid);
        float L = Vector3.Distance(rS, rFore.position) + Vector3.Distance(rFore.position, rHand.position);
        float reach = Mathf.Sqrt(Mathf.Max(0.01f, (0.985f * L) * (0.985f * L) - halfW * halfW)); // 右手伸直
        Vector3 handGoal = mid + dir * reach;

        // 右手：伸到圓弧上（手肘往外下方），刀身轉到和手臂同一個方向
        Vector3 rFrom = rHand.position;
        Quaternion rFromRot = rHand.rotation;
        Vector3 rGoal = Vector3.Lerp(rFrom, handGoal, w);
        Solve(rUpper, rFore, rHand, rGoal, right - up * 0.4f);
        if (presenter != null && presenter.BladeTip != null && presenter.BladePivot != null)
        {
            Vector3 blade = presenter.BladeTip.position - presenter.BladePivot.position;
            if (blade.sqrMagnitude > 1e-6f)
            {
                Quaternion turn = Quaternion.FromToRotation(blade.normalized, dir);
                rHand.rotation = Quaternion.Slerp(Quaternion.identity, turn, w) * rHand.rotation;
            }
        }
        else rHand.rotation = Quaternion.Slerp(rFromRot, rHand.rotation, w);

        // 左手：握在右手下面（劍柄靠尾端），手肘往外下方
        if (lUpper != null && lFore != null && lHand != null)
        {
            Vector3 lGoal = Vector3.Lerp(lHand.position, rHand.position - dir * GripGap, w);
            Solve(lUpper, lFore, lHand, lGoal, -right - up * 0.4f);
        }
    }

    static void Solve(Transform upper, Transform fore, Transform hand, Vector3 goal, Vector3 pole)
    {
        Vector3 root = upper.position;
        Vector3 to = goal - root;
        if (to.sqrMagnitude < 1e-6f) return;
        float l1 = Vector3.Distance(root, fore.position), l2 = Vector3.Distance(fore.position, hand.position);
        float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 0.01f, (l1 + l2) * 0.999f);
        Vector3 dir = to.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.up, dir);
        bend.Normalize();
        float along = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
        Vector3 elbow = root + dir * along + bend * Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
        upper.rotation = Quaternion.FromToRotation(fore.position - root, elbow - root) * upper.rotation;
        fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, root + dir * d - fore.position) * fore.rotation;
    }
}
