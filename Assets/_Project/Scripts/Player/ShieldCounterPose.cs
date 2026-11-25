using UnityEngine;

/// <summary>
/// 盾反突刺的右手姿勢（純視覺，用程式擺骨架，疊在 ShieldBash 動畫上）：
///   準備：手肘往後收、刀尖朝前（蓄勢）
///   刺出：整隻右手往前伸直、上身前傾，刀尖對準正前方
///   收招：慢慢交還給動畫
/// 由 PlayerCombat.IsShieldCountering / CounterProgress 驅動；PlayerShieldBowAnimator 會自動加上。
/// 執行順序要比 PlayerWeaponPresenter 早，刀的方向（AimSlashBlade）才會在同一幀套用。
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class ShieldCounterPose : MonoBehaviour
{
    [Tooltip("上身往前傾幾度（刺到底時）")]
    public float leanDegrees = 14f;
    [Tooltip("刺出的方向稍微往下多少（0 = 水平）")]
    [Range(0f, 0.5f)] public float thrustDrop = 0.1f;
    [Tooltip("準備（收手肘）佔總長度的比例上限；實際用 PlayerCombat 的 counterDelay")]
    [Range(0.05f, 0.5f)] public float maxChamberPortion = 0.3f;

    PlayerCombat combat;
    Animator animator;
    PlayerWeaponPresenter presenter;
    Transform spine, chest, upperArm, lowerArm, hand;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
    }

    bool EnsureBones()
    {
        var a = GetComponentInChildren<Animator>();
        if (a == null || !a.isHuman) return false;
        if (a == animator && upperArm != null) return true;
        animator = a;   // 換角色模型時重新抓骨頭
        spine = a.GetBoneTransform(HumanBodyBones.Spine);
        chest = a.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = spine;
        upperArm = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
        lowerArm = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
        hand = a.GetBoneTransform(HumanBodyBones.RightHand);
        return upperArm != null && lowerArm != null && hand != null;
    }

    void LateUpdate()
    {
        if (combat == null || combat.swordParry || !combat.IsShieldCountering || !EnsureBones()) return;   // 刀劍格擋：反擊是一般的揮刀，不擺突刺

        float total = combat.counterDelay + combat.counterActive + combat.counterRecovery;
        float p = combat.CounterProgress;
        float chamberEnd = Mathf.Min(maxChamberPortion, combat.counterDelay / Mathf.Max(0.01f, total));
        float thrustEnd = chamberEnd + combat.counterActive / Mathf.Max(0.01f, total);

        // weight：準備時漸入，收招後半段漸出；extend：0 = 收手肘，1 = 伸直刺出
        float weight, extend;
        if (p < chamberEnd) { weight = SmoothTween.Evaluate(SmoothTween.Ease.OutCubic, p / Mathf.Max(0.001f, chamberEnd)); extend = 0f; }
        else if (p < thrustEnd) { weight = 1f; extend = SmoothTween.Evaluate(SmoothTween.Ease.OutBack, (p - chamberEnd) / Mathf.Max(0.001f, thrustEnd - chamberEnd)); }
        else
        {
            float r = (p - thrustEnd) / Mathf.Max(0.001f, 1f - thrustEnd);
            weight = 1f - SmoothTween.Evaluate(SmoothTween.Ease.InOutSine, Mathf.InverseLerp(0.35f, 1f, r));
            extend = 1f;
        }
        if (weight <= 0.001f) return;

        Vector3 fwd = transform.forward, right = transform.right, down = Vector3.down;

        // 上身前傾（刺得越出去越前傾）
        float lean = leanDegrees * Mathf.Clamp01(extend) * weight;
        if (spine != null) spine.rotation = Quaternion.AngleAxis(lean * 0.5f, right) * spine.rotation;
        if (chest != null && chest != spine) chest.rotation = Quaternion.AngleAxis(lean * 0.5f, right) * chest.rotation;

        // 收手肘（上臂往後下、前臂朝前）→ 伸直（上臂、前臂都朝前）
        Vector3 chamberUpper = (-fwd * 0.5f + down * 0.85f + right * 0.25f).normalized;
        Vector3 chamberLower = (fwd * 0.95f + down * 0.1f).normalized;
        Vector3 thrustDir = (fwd + down * thrustDrop).normalized;
        float e = Mathf.Clamp01(extend);
        Aim(upperArm, lowerArm, Vector3.Slerp(chamberUpper, thrustDir, e), weight);
        Aim(lowerArm, hand, Vector3.Slerp(chamberLower, thrustDir, e), weight);

        // 刀尖對準正前方（PlayerWeaponPresenter 在它自己的 LateUpdate 套用）
        if (presenter != null && weight > 0.3f) presenter.AimSlashBlade(thrustDir, Vector3.up);
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
