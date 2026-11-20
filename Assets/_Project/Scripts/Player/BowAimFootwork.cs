using UnityEngine;

/// <summary>
/// 拉弓瞄準時走路的腳步：瞄準時全身播的是停在「拉滿弓」那一格的 BowDraw（PlayerShieldBowAnimator），腿是不動的，
/// 角色一移動整個人就在地上滑。這裡在腿上用程式踏步：
///   - 一隻腳踩住地面不動（世界座標固定），另一隻腳抬起來往前（往移動的方向）跨一步，落地後換另一隻；
///     跨出去的前腳落得比站姿前面一點、落地後兩腳一起踩住一下（minPlantTime），前腳定得比較久
///   - 站著不動時兩腳都踩住；轉身轉太多、腳離原本站的位置太遠時才挪一步
///   - 腳的位置、角度、膝蓋往哪邊彎都照動畫本來的站姿，只是把腳釘在地上、輪流跨步；腿伸不到時骨盆稍微往下沉
///   - 開始 / 停止瞄準時慢慢混進 / 混出，不會跳一下
/// PlayerCombat.Awake 自動掛上。只動腿和骨盆的高度，上半身（拉弓的手臂由 PlayerLoadout 擺）不動。
/// </summary>
[DefaultExecutionOrder(31500)] // PlayerVisualAnimator（貼地）之後、PlayerLoadout（32000，擺手臂）之前
[DisallowMultipleComponent]
public sealed class BowAimFootwork : MonoBehaviour
{
    [Tooltip("跨一步要幾秒（走得越快越短）")]
    public float stepTime = 0.32f;
    [Tooltip("腳抬多高 (m)")]
    public float stepHeight = 0.1f;
    [Tooltip("踩住的腳離站姿的位置超過多遠就跨下一步 (m)")]
    public float strideTrigger = 0.27f;
    [Tooltip("一隻腳剛落地後，兩腳都踩住至少多久才跨下一步（秒）：前腳落地後先定住，身體跟上來再換腳")]
    public float minPlantTime = 0.16f;
    [Tooltip("跨出去的腳落在站姿前面多遠（× 速度 × 跨步時間）：越大，落地的前腳越往前、定住越久")]
    public float leadFactor = 0.85f;
    [Tooltip("站著不動時：腳離站姿多遠才挪一步 (m)")]
    public float idleTrigger = 0.16f;

    sealed class Leg
    {
        public Transform up, low, foot;
        public Vector3 planted, from, to;
        public float t = -1f;          // 跨步的進度（-1 = 踩著）
        public Vector3 animPos;        // 這一幀動畫裡腳的位置
        public Quaternion animRot;
        public bool Stepping => t >= 0f;
    }

    PlayerCombat combat;
    PlayerHealth health;
    ThirdPersonController ctrl;
    Animator animator;
    Transform hips;
    readonly Leg[] legs = { new Leg(), new Leg() };
    readonly Vector3[] targets = new Vector3[2];
    float weight;
    bool ready;
    int lastStepped = 1;
    float lastLanded = float.NegativeInfinity;
    static readonly RaycastHit[] hits = new RaycastHit[8];

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        ctrl = GetComponent<ThirdPersonController>();
    }

    bool Setup()
    {
        if (ready) return true;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return false;
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        legs[0].up = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        legs[0].low = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        legs[0].foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        legs[1].up = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        legs[1].low = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        legs[1].foot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        foreach (var l in legs) if (l.up == null || l.low == null || l.foot == null) return false;
        ready = hips != null;
        return ready;
    }

    bool Wanted =>
        combat != null && combat.bowMode && (combat.IsAimingBow || PlayerLoadout.PreviewAimPose)
        && ctrl != null && ctrl.isActiveAndEnabled && ctrl.IsGrounded && !ctrl.IsRolling
        && (health == null || !health.IsDead) && animator != null && animator.enabled;

    void LateUpdate()
    {
        if (!Setup()) return;
        bool want = Wanted;
        foreach (var l in legs) { l.animPos = l.foot.position; l.animRot = l.foot.rotation; }

        if (want && weight <= 0.001f)
            foreach (var l in legs) { l.planted = l.animPos; l.t = -1f; } // 從動畫現在的位置開始踩住，不會跳
        weight = Mathf.MoveTowards(weight, want ? 1f : 0f, Time.deltaTime * (want ? 7f : 5f));
        if (weight <= 0.001f) return;

        float dt = Time.deltaTime;
        Vector3 vel = ctrl.HorizontalVelocity;
        float speed = vel.magnitude;
        bool moving = speed > 0.15f;
        float duration = Mathf.Clamp(stepTime - speed * 0.03f, 0.2f, stepTime);
        // 跨出去的腳落在「站姿的位置 + 往前預估一段」：落地之後定住，身體繼續往前走過它，腳才慢慢落到身體後面
        Vector3 lead = vel * duration * leadFactor;

        // 跨步中的腳：從原本踩的地方弧線移到目標，目標跟著身體一起走
        foreach (var l in legs)
        {
            if (!l.Stepping) continue;
            l.to = Flat(l.animPos) + lead;
            l.t += dt / duration;
            if (l.t >= 1f) { l.planted = l.to; l.t = -1f; lastLanded = Time.time; }
        }

        // 沒有腳在跨步：離站姿最遠的那隻腳（超過門檻）跨下一步；走路時兩腳輪流
        if (!legs[0].Stepping && !legs[1].Stepping && Time.time - lastLanded >= minPlantTime)
        {
            float trigger = moving ? strideTrigger : idleTrigger;
            int pick = -1;
            float best = trigger;
            for (int i = 0; i < 2; i++)
            {
                // 走路時只算「落在身體後面多遠」（加上一點左右偏移）：剛落地在前面的那隻腳要等身體走過它、落到後面夠遠才換
                Vector3 off = Flat(legs[i].animPos) + lead * 0.15f - Flat(legs[i].planted);
                float d = moving
                    ? Mathf.Max(0f, Vector3.Dot(off, vel / speed)) + Vector3.ProjectOnPlane(off, vel / speed).magnitude * 0.5f
                    : Vector3.Distance(Flat(legs[i].planted), Flat(legs[i].animPos));
                if (moving && i == lastStepped) d *= 0.8f; // 偏好換另一隻腳
                if (d > best) { best = d; pick = i; }
            }
            if (pick >= 0)
            {
                var l = legs[pick];
                l.from = l.planted;
                l.to = Flat(l.animPos) + lead;
                l.t = 0f;
                lastStepped = pick;
            }
        }

        // 每隻腳要放的位置：踩住的在原地；跨步中的走弧線（抬起來再放下）
        var targets = this.targets;
        float lift = 0f;
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i];
            Vector3 p;
            float heightOffset = 0f;
            if (l.Stepping)
            {
                float k = Mathf.SmoothStep(0f, 1f, l.t);
                p = Vector3.Lerp(l.from, l.to, k);
                heightOffset = Mathf.Sin(l.t * Mathf.PI) * stepHeight * Mathf.Clamp01(0.5f + speed * 0.4f);
                lift = Mathf.Max(lift, Mathf.Sin(l.t * Mathf.PI));
            }
            else p = l.planted;
            // 高度：動畫裡腳離地多高照舊，再加上目標位置跟現在腳下的地面高低差
            float groundHere = Ground(l.animPos), groundThere = Ground(p);
            p.y = l.animPos.y + (groundThere - groundHere) + heightOffset;
            targets[i] = Vector3.Lerp(l.animPos, p, weight);
        }

        // 骨盆：兩腳之間跨步時稍微往下沉一點（看起來有重量），腿伸不到時再沉到伸得到
        float drop = 0.025f * lift * weight;
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i];
            float len = Vector3.Distance(l.up.position, l.low.position) + Vector3.Distance(l.low.position, l.foot.position);
            float need = Vector3.Distance(l.up.position + Vector3.down * drop, targets[i]) - len * 0.985f;
            if (need > 0f) drop += Mathf.Min(need, 0.1f);
        }
        if (drop > 0f) hips.position += Vector3.down * drop;

        for (int i = 0; i < 2; i++)
        {
            var l = legs[i];
            // 膝蓋往動畫原本彎的方向彎
            Vector3 mid = (l.up.position + l.animPos) * 0.5f;
            Vector3 pole = l.low.position + (l.low.position - mid) * 2f + transform.forward * 0.05f;
            TwoBoneIK(l.up, l.low, l.foot, targets[i], pole);
            l.foot.rotation = l.animRot;
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    float Ground(Vector3 at)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(at.x, transform.position.y + 1.2f, at.z), Vector3.down, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            if (hits[i].point.y > best) best = hits[i].point.y;
        }
        return float.IsNegativeInfinity(best) ? transform.position.y : best;
    }

    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
    {
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        Vector3 toT = target - a.position;
        if (la < 1e-4f || lb < 1e-4f || toT.sqrMagnitude < 1e-6f) return;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
        Vector3 dirT = toT.normalized;
        Vector3 toP = Vector3.ProjectOnPlane(pole - a.position, dirT);
        if (toP.sqrMagnitude < 1e-6f) toP = Vector3.ProjectOnPlane(Vector3.forward, dirT);
        toP.Normalize();
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        Vector3 knee = a.position + (dirT * cosA + toP * Mathf.Sqrt(1f - cosA * cosA)) * la;
        Aim(a, b, knee - a.position);
        Aim(b, c, target - b.position);
    }

    static void Aim(Transform bone, Transform child, Vector3 dir)
    {
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f || dir.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
    }
}
