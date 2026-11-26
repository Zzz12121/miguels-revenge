using System.Reflection;
using UnityEngine;

/// <summary>
/// 第三刀（大刀、長槍連段的最後一下）的架式，疊在 PlayerVisualAnimator 擺好的姿勢上（不改它的程式）：
///   1. 兩腳往內收：第三下播的 Tripo 動畫 CW_Slash3 兩腳張得很開，左右距離收 35%、前後收 15%，
///      腳掌方向不變、照樣踩在原本的高度（用腿的 IK 彎膝蓋）
///   2. 大刀：時間不變，抬手的後段把雙手連刀繞著兩肩的連線再往後拉 35°（拉到背後一點）、胸口往後仰一點，
///      揮下去的前 45% 回到原本的刀路，所以從更後面劈下來、砍到底的時間點不變。
///      兩隻上臂繞同一條通過兩個肩關節的軸轉同樣的角度 = 雙手、刀、左手握柄整組一起轉，握法不會跑掉
///   3. 大刀：整個第三刀，刀的模型繞握柄的方向（握柄 → 刀尖）轉 bladeRollAngle 度（-90 = 從握柄往刀尖看順時針 90°），刀鋒換一面
/// 長槍第三下的單手戳在 PlayerLoadout.UpdateSpearThrust。PlayerLoadout 開場時掛上。
/// </summary>
[DefaultExecutionOrder(31100)] // PlayerWeaponPresenter（31000，把刀擺到手上）之後：手轉了刀跟著轉；PlayerLoadout（32000，長槍的手）之前
[DisallowMultipleComponent]
public sealed class FinisherForm : MonoBehaviour
{
    [Header("兩腳往內收")]
    [Range(0f, 0.8f)] public float narrowSide = 0.35f;
    [Range(0f, 0.8f)] public float narrowForward = 0.15f;
    [Tooltip("每隻腳離兩腳中間至少留多少 (m)，不會收到交叉")]
    public float minHalfWidth = 0.09f;

    [Header("大刀：抬手時再往後拉")]
    public float pullBackAngle = 35f;
    public float chestBackAngle = 8f;
    [Tooltip("刀繞握柄方向轉幾度：正的 = 從握柄往刀尖看逆時針（轉反了就改成負的）")]
    public float bladeRollAngle = -90f;

    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo StartedField = typeof(PlayerVisualAnimator).GetField("slashStartedAt", Private);
    static readonly FieldInfo SideField = typeof(PlayerVisualAnimator).GetField("slashSide", Private);
    // 第三刀的時間軸（照原本速度寫的，乘上攻擊速度）：抬手 → 揮 → 停住 → 收刀
    static readonly float Prepare = Const("FinisherPrepare", 0.5f), Cut = Const("SlashCut", 0.34f), Recover = Const("SlashRecover", 0.24f);
    const float Hold = 0.22f; // PlayerVisualAnimator.SlashPaths[2].hold
    static float Total => Prepare + Cut + Hold + Recover;

    static float Const(string name, float fallback)
    {
        var f = typeof(PlayerVisualAnimator).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        return f != null && f.IsLiteral ? (float)f.GetRawConstantValue() : fallback;
    }

    PlayerVisualAnimator visual;
    PlayerCombat combat;
    PlayerWeaponPresenter presenter;
    Animator animator;
    Transform lUp, lLow, lFoot, rUp, rLow, rFoot, lArm, rArm, chest;
    float weight;

    void Awake() => combat = GetComponent<PlayerCombat>();

    void LateUpdate()
    {
        if (visual == null) visual = GetComponentInChildren<PlayerVisualAnimator>(true);
        float age = FinisherAge(out bool on);
        on &= PlayerLoadout.Current != WeaponLoadout.BowKnife;
        // 開始時 0.12 秒混進來，收刀的最後 0.25 秒混回去（都是乘過攻擊速度的時間）
        float target = on ? Mathf.SmoothStep(0f, 1f, age / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, (age - (Total - 0.25f)) / 0.25f)) : 0f;
        weight = Mathf.MoveTowards(weight, target, Time.deltaTime * 8f);
        if (weight <= 0.001f || !Bones()) return;
        if (on && PlayerLoadout.Current == WeaponLoadout.Greatsword) { PullBack(age); RollBlade(); }
        NarrowFeet(weight);
    }

    /// <summary>第三刀揮了多久（乘過攻擊速度）；on = 現在是地面上的第三刀</summary>
    float FinisherAge(out bool on)
    {
        on = false;
        if (visual == null || combat == null || StartedField == null || SideField == null) return 0f;
        if ((int)SideField.GetValue(visual) != 2) return 0f;
        float started = (float)StartedField.GetValue(visual);
        if (float.IsInfinity(started)) return 0f;
        float age = (Time.time - started) * Mathf.Max(0.1f, combat.AttackSpeed);
        on = age >= 0f && age < Total && !combat.IsAirSlash && !combat.IsPlunging && !combat.IsDashSlash;
        return age;
    }

    bool Bones()
    {
        if (lFoot != null) return true;
        if (animator == null)
        {
            if (presenter == null) presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
            animator = presenter != null ? presenter.GetComponentInChildren<Animator>(true) : GetComponentInChildren<Animator>(true);
        }
        if (animator == null || !animator.isHuman) return false;
        lUp = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        lLow = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        rUp = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rLow = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        lArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        return lFoot != null && lUp != null && lLow != null && rUp != null && rLow != null && rFoot != null;
    }

    /// <summary>抬手的後 70% 慢慢往後拉到最多，揮下去的前 45% 回到原本的刀路</summary>
    void PullBack(float age)
    {
        if (lArm == null || rArm == null) return;
        float k = age < Prepare
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, age / Prepare))
            : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - Prepare) / (Cut * 0.45f)));
        k *= weight;
        if (k <= 0.001f) return;
        // 胸口往後仰（負的角度 = 上面往後）
        if (chest != null) chest.rotation = Quaternion.AngleAxis(-chestBackAngle * k, transform.right) * chest.rotation;
        Vector3 axis = rArm.position - lArm.position; // 左肩 → 右肩
        if (axis.sqrMagnitude < 1e-6f) return;
        // 繞兩肩連線往後轉：前面的手往上、頭上的手往後
        Quaternion q = Quaternion.AngleAxis(-pullBackAngle * k, axis.normalized);
        lArm.rotation = q * lArm.rotation;
        rArm.rotation = q * rArm.rotation;
    }

    /// <summary>刀的模型繞自己的握柄 → 刀尖那條線轉（刀尖、握的位置不動，只換刀鋒朝向）。手臂轉完之後再轉，刀跟著手</summary>
    void RollBlade()
    {
        if (presenter == null) presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
        if (presenter == null || !presenter.IsDrawn) return;
        Transform pivot = presenter.BladePivot, tip = presenter.BladeTip;
        if (pivot == null || tip == null) return;
        Vector3 axis = tip.position - pivot.position;
        if (axis.sqrMagnitude < 1e-6f) return;
        // Unity 的 AngleAxis：正的角度從軸的起點往終點看是順時針，所以逆時針用負的
        pivot.rotation = Quaternion.AngleAxis(-bladeRollAngle * weight, axis.normalized) * pivot.rotation;
    }

    void NarrowFeet(float w)
    {
        Vector3 right = transform.right, fwd = transform.forward;
        Vector3 mid = (lFoot.position + rFoot.position) * 0.5f;
        Place(lUp, lLow, lFoot, mid, right, fwd, w);
        Place(rUp, rLow, rFoot, mid, right, fwd, w);
    }

    void Place(Transform up, Transform low, Transform foot, Vector3 mid, Vector3 right, Vector3 fwd, float w)
    {
        Vector3 rel = foot.position - mid;
        float side = Vector3.Dot(rel, right), fore = Vector3.Dot(rel, fwd);
        float newSide = side * (1f - narrowSide * w);
        if (Mathf.Abs(newSide) < minHalfWidth) newSide = Mathf.Sign(side) * Mathf.Min(Mathf.Abs(side), minHalfWidth);
        float newFore = fore * (1f - narrowForward * w);
        Vector3 target = foot.position + right * (newSide - side) + fwd * (newFore - fore);
        // 膝蓋照原本彎的方向
        Vector3 bend = low.position - (up.position + foot.position) * 0.5f;
        Vector3 pole = low.position + (bend.sqrMagnitude > 1e-4f ? bend.normalized : fwd) * 0.5f;
        Quaternion footRot = foot.rotation;
        TwoBoneIK(up, low, foot, target, pole);
        foot.rotation = footRot;
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
        if (cur.sqrMagnitude < 1e-6f || dir.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
    }
}
