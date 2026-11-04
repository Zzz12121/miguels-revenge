using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 百足駭骨近戰攻擊（鐮刀、甩尾、衝刺）的特效（只負責畫面，不做傷害判定）：
///   SweepSwing("Right" / "Left" / "Both") → 對應鐮刀尖端開始拖出刀光
///   AttackImpact("Slash" / "Sweep1~3")      → 在剛揮完的刀尖生成斬擊命中特效
///   AttackImpact("Sweep4")                  → 雙鐮重砍：地面爆裂 + 兩邊刀尖碎石
///   TailSwing / AttackImpact("TailWhip")     → 尾巴拖出弧光，掃過的地面揚起塵土和碎石（左右都可以）
///   DashCharge → DashStart → DashEnd         → 蓄力揚塵、衝刺中雙鐮和尾巴拖光、腳下一路噴塵土、煞車揚塵
///   AttackImpact("Dash")                      → 收尾上撈：兩邊刀尖斬擊 + 前方地面煙塵爆開
///
/// 特效 prefab 由 VFX/Characters/Skeleton Centipede/Rebuild Sweep VFX 從 Hovl Magic Effects FREE 轉成 URP 版本後指派。
/// 拿掉這個元件不影響動畫和 Boss 邏輯。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SkeletonCentipedeAnimationDriver))]
public class SkeletonCentipedeSweepVfx : MonoBehaviour
{
    [Header("刀光拖尾")]
    public Material trailMaterial;
    public Gradient trailColor = DefaultTrailColor();
    [Tooltip("拖尾留在畫面上的秒數")]
    public float trailTime = 0.22f;
    [Tooltip("每一刀開始後拖尾持續發射的秒數")]
    public float swingWindow = 0.28f;
    [Tooltip("拖尾寬度（鐮刀長度的倍數）")]
    public float trailWidthScale = 0.9f;

    [Header("命中特效")]
    public GameObject sweepImpactPrefab;
    public GameObject finisherSlamPrefab;
    public GameObject finisherDebrisPrefab;
    [Tooltip("特效大小（鐮刀長度的倍數）")]
    public float impactScale = 1.4f;
    public float finisherScale = 1.1f;
    public float effectLifetime = 4f;

    [Header("甩尾")]
    public GameObject tailDustPrefab;
    [Tooltip("尾巴拖尾持續發射的秒數")]
    public float tailSwingWindow = 0.45f;
    [Tooltip("尾巴拖尾寬度（尾巴長度的倍數）")]
    public float tailTrailWidthScale = 0.35f;
    public string tailTipBone = "bone_8";

    [Header("衝刺")]
    public GameObject dashFinisherPrefab;
    [Tooltip("衝刺中腳下噴塵土的間隔（秒）")]
    public float dashDustInterval = 0.07f;
    [Tooltip("衝刺塵土大小（尾巴長度的倍數）")]
    public float dashDustScale = 0.45f;
    public string tailBaseBone = "bone_4";

    [Header("骨頭（Tripo 骨架編號）")]
    public string armBoneA = "bone_85";
    public string armBoneB = "bone_87";

    class Blade
    {
        public Transform arm, tip;
        public TrailRenderer trail;
        public float emitUntil;
        public float length;
    }

    SkeletonCentipedeAnimationDriver driver;
    Blade right, left, tail;
    Blade lastSwung;
    bool dashing;
    float nextDashDust;

    void Awake()
    {
        driver = GetComponent<SkeletonCentipedeAnimationDriver>();
        var bones = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true)) bones.TryAdd(t.name, t);

        if (!bones.TryGetValue(armBoneA, out var a) || !bones.TryGetValue(armBoneB, out var b) || a.childCount == 0 || b.childCount == 0)
        {
            Debug.LogWarning($"{name}: 找不到鐮刀骨頭 {armBoneA} / {armBoneB}，刀光特效停用。", this);
            enabled = false;
            return;
        }

        bool aIsRight = Vector3.Dot(a.GetChild(0).position - transform.position, transform.right) > 0f;
        right = CreateBlade(aIsRight ? a : b, "Right");
        left = CreateBlade(aIsRight ? b : a, "Left");
        lastSwung = right;

        if (bones.TryGetValue(tailTipBone, out var tailTip) && bones.TryGetValue(tailBaseBone, out var tailBase))
            tail = CreateTail(tailTip, tailBase);
    }

    void OnEnable()
    {
        if (driver == null) return;
        driver.SweepSwing += OnSwing;
        driver.TailSwing += OnTailSwing;
        driver.DashCharge += OnDashCharge;
        driver.DashStart += OnDashStart;
        driver.DashEnd += OnDashEnd;
        driver.AttackImpact += OnImpact;
    }

    void OnDisable()
    {
        if (driver == null) return;
        driver.SweepSwing -= OnSwing;
        driver.TailSwing -= OnTailSwing;
        driver.DashCharge -= OnDashCharge;
        driver.DashStart -= OnDashStart;
        driver.DashEnd -= OnDashEnd;
        driver.AttackImpact -= OnImpact;
        dashing = false;
    }

    void Update()
    {
        UpdateTrail(right);
        UpdateTrail(left);
        UpdateTrail(tail);

        if (dashing && Time.time >= nextDashDust && tail != null)
        {
            nextDashDust = Time.time + dashDustInterval;
            // 塵土從身體中段和尾巴附近輪流噴，看起來是整排腳在刨地
            Vector3 at = Random.value < 0.5f ? transform.position : tail.tip.position;
            at += transform.right * Random.Range(-0.3f, 0.3f) * tail.length;
            Spawn(tailDustPrefab, Ground(at), tail.length * dashDustScale * Random.Range(0.8f, 1.2f));
        }
    }

    Blade CreateBlade(Transform arm, string label)
    {
        var bladeBone = arm.GetChild(0);
        Vector3 dir = bladeBone.position - arm.position;
        float length = dir.magnitude;

        // 刀光錨點放在刀身中後段，寬度涵蓋大半把鐮刀
        var tip = new GameObject($"SweepTrail_{label}").transform;
        tip.SetParent(bladeBone, false);
        tip.position = bladeBone.position + dir * 0.25f;

        return new Blade { arm = arm, tip = tip, trail = CreateTrail(tip, length, length * trailWidthScale, trailTime), length = length };
    }

    Blade CreateTail(Transform tip, Transform tailBase)
    {
        // 身長用「尾端到身體中段」估，拖尾寬度和塵土大小都跟著它
        float length = Vector3.Distance(tip.position, tailBase.position) * 2f;
        var anchor = new GameObject("TailTrail").transform;
        anchor.SetParent(tip, false);
        return new Blade { arm = tailBase, tip = anchor, trail = CreateTrail(anchor, length, length * tailTrailWidthScale, trailTime * 1.4f), length = length };
    }

    TrailRenderer CreateTrail(Transform anchor, float length, float width, float time)
    {
        var trail = anchor.gameObject.AddComponent<TrailRenderer>();
        trail.sharedMaterial = trailMaterial;
        trail.time = time;
        trail.minVertexDistance = length * 0.03f;
        trail.widthMultiplier = width;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
        trail.colorGradient = trailColor;
        trail.textureMode = LineTextureMode.Stretch;
        trail.alignment = LineAlignment.View;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
        return trail;
    }

    void UpdateTrail(Blade blade)
    {
        if (blade == null) return;
        bool emit = Time.time < blade.emitUntil;
        if (blade.trail.emitting != emit) blade.trail.emitting = emit;
    }

    void OnSwing(string arm)
    {
        if (arm == "Left" || arm == "Both") StartSwing(left);
        if (arm == "Right" || arm == "Both") StartSwing(right);
        lastSwung = arm == "Left" ? left : right;
    }

    void OnTailSwing(string side)
    {
        if (tail == null) return;
        tail.trail.Clear();
        tail.emitUntil = Time.time + tailSwingWindow;
        tail.trail.emitting = true;
    }

    void OnDashCharge()
    {
        if (tail == null) return;
        Spawn(tailDustPrefab, Ground(transform.position), tail.length * dashDustScale * 1.3f);
    }

    void OnDashStart()
    {
        dashing = true;
        nextDashDust = 0f;
        // 衝刺全程拖光：時間設長一點，直到 DashEnd 才關
        foreach (var blade in new[] { right, left, tail })
        {
            if (blade == null) continue;
            blade.trail.Clear();
            blade.emitUntil = float.MaxValue;
            blade.trail.emitting = true;
        }
        if (tail != null) Spawn(tailDustPrefab, Ground(tail.tip.position), tail.length * dashDustScale * 2f);
    }

    void OnDashEnd()
    {
        dashing = false;
        foreach (var blade in new[] { right, left, tail })
            if (blade != null) blade.emitUntil = Time.time + 0.05f;
        if (tail != null)
        {
            Vector3 front = right != null && left != null ? (right.tip.position + left.tip.position) * 0.5f : transform.position;
            Spawn(tailDustPrefab, Ground(front), tail.length * dashDustScale * 1.8f);
        }
    }

    void StartSwing(Blade blade)
    {
        blade.trail.Clear();
        blade.emitUntil = Time.time + swingWindow;
        blade.trail.emitting = true;
    }

    void OnImpact(string attack)
    {
        if (attack == "TailWhip")
        {
            if (tail == null) return;
            Spawn(tailDustPrefab, Ground(tail.tip.position), tail.length * finisherScale);
            Spawn(finisherDebrisPrefab, Ground(tail.tip.position), tail.length * impactScale * 0.5f);
            return;
        }

        if (attack == "Dash")
        {
            Vector3 mid = (right.tip.position + left.tip.position) * 0.5f;
            Spawn(sweepImpactPrefab, right.tip.position, right.length * impactScale);
            Spawn(sweepImpactPrefab, left.tip.position, left.length * impactScale);
            Spawn(dashFinisherPrefab, Ground(mid), right.length * finisherScale);
            return;
        }

        if (attack == "Sweep4")
        {
            Vector3 mid = (right.tip.position + left.tip.position) * 0.5f;
            Spawn(finisherSlamPrefab, Ground(mid), right.length * finisherScale);
            Spawn(finisherDebrisPrefab, Ground(right.tip.position), right.length * impactScale);
            Spawn(finisherDebrisPrefab, Ground(left.tip.position), left.length * impactScale);
            return;
        }

        if (attack == "Slash" || attack.StartsWith("Sweep"))
        {
            var blade = attack == "Slash" ? right : lastSwung;
            Spawn(sweepImpactPrefab, blade.tip.position, blade.length * impactScale);
        }
    }

    void Spawn(GameObject prefab, Vector3 position, float scale)
    {
        if (prefab == null) return;
        Vector3 facing = transform.forward;
        facing.y = 0f;
        var fx = Instantiate(prefab, position, Quaternion.LookRotation(facing.sqrMagnitude > 0.001f ? facing : Vector3.forward));
        fx.transform.localScale = Vector3.one * scale;
        Destroy(fx, effectLifetime);
    }

    Vector3 Ground(Vector3 from)
    {
        // 從刀尖往下找地面；打到自己身上的碰撞體就略過
        var hits = Physics.RaycastAll(from + Vector3.up * 2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 point = new Vector3(from.x, transform.position.y, from.z);
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best) { best = h.distance; point = h.point; }
        }
        return point;
    }

    static Gradient DefaultTrailColor()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.92f, 0.85f), 0f), new GradientColorKey(new Color(0.85f, 0.12f, 0.08f), 0.55f), new GradientColorKey(new Color(0.25f, 0.02f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return g;
    }
}
