using UnityEngine;

/// <summary>
/// 刀劍格擋的姿勢（純視覺，用程式擺骨架，疊在待機動畫上；PlayerCombat.swordParry 開著時才作用）：
///   格擋中：側身（左肩朝前、身體轉開）、上身稍微後仰，雙手把刀斜斜舉在身前（刀尖朝左上、刀刃朝外）
///   擋到攻擊：刀往外一撥（架開），身體往側步的方向倒一下（側身躲開），接著交給反擊的揮刀
/// 由 PlayerShieldBowAnimator 自動加上。執行順序在 PlayerVisualAnimator（100）之後、PlayerWeaponPresenter（31000）之前，
/// 刀的方向（AimSlashBlade）才會在同一幀套用。
/// </summary>
[DefaultExecutionOrder(150)]
[DisallowMultipleComponent]
public sealed class SwordParryPose : MonoBehaviour
{
    [Tooltip("側身轉開幾度（左肩朝前）")]
    public float sideTurn = 38f;
    [Tooltip("上身後仰幾度")]
    public float leanBack = 7f;
    [Tooltip("擋到攻擊時刀往外撥幾度")]
    public float deflectDegrees = 40f;
    [Tooltip("側步時身體往側邊倒幾度")]
    public float sidestepTilt = 14f;
    [Tooltip("擋到攻擊之後，架開 + 側身的動作持續幾秒")]
    public float deflectSeconds = 0.28f;

    PlayerCombat combat;
    PlayerWeaponPresenter presenter;
    Animator animator;
    Transform hips, spine, chest, head, rUpper, rLower, rHand, lUpper, lLower, lHand;
    float weight, deflectAt = -10f, side = 1f;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
    }

    void OnEnable() { if (combat != null) combat.OnSwordSidestep += HandleSidestep; }
    void OnDisable() { if (combat != null) combat.OnSwordSidestep -= HandleSidestep; }

    void HandleSidestep(float stepSide)
    {
        side = stepSide >= 0f ? 1f : -1f;
        deflectAt = Time.time;
    }

    bool EnsureBones()
    {
        var a = GetComponentInChildren<Animator>();
        if (a == null || !a.isHuman) return false;
        if (a == animator && rUpper != null) return true;
        animator = a;
        hips = a.GetBoneTransform(HumanBodyBones.Hips);
        spine = a.GetBoneTransform(HumanBodyBones.Spine);
        chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
        head = a.GetBoneTransform(HumanBodyBones.Head);
        rUpper = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rLower = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rHand = a.GetBoneTransform(HumanBodyBones.RightHand);
        lUpper = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lLower = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        lHand = a.GetBoneTransform(HumanBodyBones.LeftHand);
        return rUpper != null && rLower != null && rHand != null;
    }

    void LateUpdate()
    {
        if (combat == null || !combat.swordParry || combat.bowMode || !EnsureBones()) { weight = 0f; return; }

        bool guarding = combat.IsBlocking;
        weight = Mathf.MoveTowards(weight, guarding ? 1f : 0f, Time.deltaTime * (guarding ? 16f : 7f));
        // 架開 / 側身：擋到之後的一小段，0 → 1 → 0
        float d = Mathf.Clamp01((Time.time - deflectAt) / Mathf.Max(0.01f, deflectSeconds));
        float deflect = d >= 1f ? 0f : Mathf.Sin(d * Mathf.PI);
        // 反擊開始揮刀之後，手臂交給揮刀；身體的側倒還留著（看起來是邊躲邊砍）
        bool countering = combat.IsShieldCountering;
        float armWeight = countering ? 0f : Mathf.Max(weight, deflect);
        float bodyWeight = Mathf.Max(weight, deflect);
        if (bodyWeight <= 0.001f) return;
        if (presenter != null && armWeight > 0.05f) presenter.DrawForSeconds(0.4f);

        Transform actor = combat.transform;
        Vector3 up = actor.up, fwd = actor.forward, right = actor.right;

        // 1. 側身：髖部轉開、胸口再轉一點；頭轉回來看前面
        float turn = sideTurn * bodyWeight;
        if (hips != null) hips.rotation = Quaternion.AngleAxis(turn * 0.6f, up) * hips.rotation;
        if (chest != null) chest.rotation = Quaternion.AngleAxis(turn * 0.4f, up) * chest.rotation;
        // 2. 後仰 + 側步時往側邊倒
        Transform bend = spine != null ? spine : chest;
        if (bend != null)
        {
            Quaternion back = Quaternion.AngleAxis(-leanBack * bodyWeight, right);
            Quaternion tilt = Quaternion.AngleAxis(-sidestepTilt * side * deflect, fwd);
            bend.rotation = tilt * back * bend.rotation;
        }
        if (head != null) head.rotation = Quaternion.AngleAxis(-turn * 0.85f, up) * head.rotation;

        if (armWeight <= 0.001f) return;

        // 3. 刀：斜舉在身前，刀尖朝左上；擋到時往外撥（繞著前方的軸轉）
        Vector3 blade = (up * 0.72f - right * 0.6f + fwd * 0.35f).normalized;
        blade = Quaternion.AngleAxis(deflectDegrees * deflect * -side, fwd) * blade;
        Vector3 handPoint = actor.position + up * 0.35f + fwd * 0.5f + right * 0.08f;   // 右手大約在胸口前方
        if (chest != null) handPoint = chest.position + fwd * 0.42f + right * 0.12f - up * 0.05f;

        // 右手：上臂往前下、前臂往前上，手停在胸口前
        Aim(rUpper, rLower, (handPoint - rUpper.position + (rLower.position - rHand.position) * 0.5f - up * 0.15f).normalized, armWeight);
        Aim(rLower, rHand, (handPoint - rLower.position).normalized, armWeight);
        // 左手：扶在右手旁邊（雙手握刀）
        if (lUpper != null && lLower != null && lHand != null)
        {
            Vector3 grip = handPoint - blade * 0.1f;
            Aim(lUpper, lLower, ((grip - lUpper.position) * 0.6f - up * 0.25f).normalized, armWeight);
            Aim(lLower, lHand, (grip - lLower.position).normalized, armWeight);
        }
        if (presenter != null && armWeight > 0.3f) presenter.AimSlashBlade(blade, fwd);
    }

    /// <summary>把 bone 轉到「bone → child 的方向 = dir」，weight 是混合比例</summary>
    static void Aim(Transform bone, Transform child, Vector3 dir, float weight)
    {
        if (bone == null || child == null || dir.sqrMagnitude < 1e-6f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-6f) return;
        Quaternion delta = Quaternion.FromToRotation(cur, dir);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
    }
}
