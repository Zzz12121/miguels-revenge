using UnityEngine;

/// <summary>
/// 攻擊被玩家格擋時，敵人上身往反方向（遠離玩家）後仰一下，再慢慢回來。純視覺，疊在動畫上面，不動 AI 和位置。
///   ParryRecoil.Play(敵人, 往後仰的方向, 角度);
/// 人形敵人：彎脊椎 + 胸口；非人形（百足駭骨這類骨架）：轉整個骨架的根骨頭。
/// 第一次用到時自動加在敵人身上（之後重複使用）。
/// </summary>
[DefaultExecutionOrder(20000)]   // 在敵人的 Animator 和各種程式擺姿勢之後
[DisallowMultipleComponent]
public sealed class ParryRecoil : MonoBehaviour
{
    [Tooltip("仰到最大花幾秒")]
    public float riseSeconds = 0.07f;
    [Tooltip("停在最大角度幾秒")]
    public float holdSeconds = 0.1f;
    [Tooltip("回到原本姿勢花幾秒")]
    public float returnSeconds = 0.38f;

    Animator animator;
    Transform spine, chest, rootBone;
    // 上一幀轉了哪些骨頭、轉了多少、轉完是什麼角度：沒有動畫的骨頭（Animator 不會每幀重設）下一幀先轉回來，才不會越仰越多
    readonly System.Collections.Generic.List<(Transform bone, Quaternion delta, Quaternion result)> applied = new();
    Vector3 away;
    float degrees, startedAt = -1f;

    public static void Play(Component enemy, Vector3 awayFromPlayer, float degrees = 14f)
    {
        if (enemy == null || degrees <= 0f) return;
        awayFromPlayer.y = 0f;
        if (awayFromPlayer.sqrMagnitude < 1e-4f) return;
        var r = enemy.GetComponent<ParryRecoil>();
        if (r == null) r = enemy.gameObject.AddComponent<ParryRecoil>();
        r.Begin(awayFromPlayer.normalized, degrees);
    }

    void Begin(Vector3 dir, float deg)
    {
        away = dir;
        degrees = deg;
        startedAt = Time.time;
        if (animator == null) FindBones();
    }

    void FindBones()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            return;
        }
        // 非人形：用蒙皮網格的根骨頭（整個骨架一起仰）
        var skin = GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin != null && skin.rootBone != null) rootBone = skin.rootBone;
    }

    void Undo()
    {
        for (int i = applied.Count - 1; i >= 0; i--)
        {
            var a = applied[i];
            // 骨頭還停在上一幀轉完的角度 = 動畫沒有重設它：先轉回去
            if (a.bone != null && Quaternion.Angle(a.bone.rotation, a.result) < 0.01f)
                a.bone.rotation = Quaternion.Inverse(a.delta) * a.bone.rotation;
        }
        applied.Clear();
    }

    void Rotate(Transform bone, Quaternion delta)
    {
        bone.rotation = delta * bone.rotation;
        applied.Add((bone, delta, bone.rotation));
    }

    void OnDisable() => Undo();

    void LateUpdate()
    {
        Undo();
        if (startedAt < 0f) return;
        float t = Time.time - startedAt;
        float total = riseSeconds + holdSeconds + returnSeconds;
        if (t >= total) { startedAt = -1f; return; }
        float w = t < riseSeconds ? Smooth(t / riseSeconds)
                : t < riseSeconds + holdSeconds ? 1f
                : 1f - Smooth((t - riseSeconds - holdSeconds) / returnSeconds);
        float deg = degrees * w;
        if (deg < 0.01f) return;

        // 「上方」往 away 倒 deg 度
        Quaternion lean = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, away, deg / 90f));
        if (spine != null || chest != null)
        {
            Quaternion half = Quaternion.Slerp(Quaternion.identity, lean, 0.5f);
            if (spine != null) Rotate(spine, half);
            if (chest != null && chest != spine) Rotate(chest, half);
        }
        else if (rootBone != null)
        {
            // 非人形整隻仰：角度小一點（不然像被整隻掀起來）
            Rotate(rootBone, Quaternion.Slerp(Quaternion.identity, lean, 0.6f));
        }
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
