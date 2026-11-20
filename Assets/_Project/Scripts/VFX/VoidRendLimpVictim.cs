using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 虛空連斬（技能 1）把敵人挑到空中時：四肢無力、軟軟地甩動（程式擺骨架，不是物理布娃娃，所以不會穿模也不用重建敵人）。
/// 技能把敵人的 Animator 暫停（骨架停在被打中的那一格），這裡每一幀從那一格出發：
///   四肢、頭、脊椎往「重力 + 慣性（往移動的反方向拖）+ 一點晃動」的方向垂下去，用彈簧追上去，看起來是軟的。
/// SlowFactor：慢動作時一起變慢（彈簧和晃動都乘上這個倍率）。Kick()：被砍到時四肢甩一下。
/// 拿掉這個元件（技能結束放開敵人）時把骨架還原成被打中的那一格，Animator 接著播。
/// </summary>
[DisallowMultipleComponent]
public sealed class VoidRendLimpVictim : MonoBehaviour
{
    [Range(0f, 1f)] public float weight;
    public float SlowFactor = 1f;

    sealed class Bone
    {
        public Transform bone, child;
        public Quaternion rest;
        public float limpness;      // 0 = 照原本姿勢、1 = 完全垂下去
        public Vector3 dir, vel;    // 目前的方向（世界座標）與彈簧速度
        public float seed;
    }

    readonly List<Bone> bones = new List<Bone>();
    Animator animator;
    Vector3 lastRoot;
    Vector3 kick;

    public static VoidRendLimpVictim Attach(Transform victim)
    {
        if (victim == null) return null;
        var anim = victim.GetComponentInChildren<Animator>(true);
        if (anim == null || !anim.isHuman) return null;
        var limp = anim.GetComponent<VoidRendLimpVictim>();
        if (limp == null) limp = anim.gameObject.AddComponent<VoidRendLimpVictim>();
        limp.Init(anim);
        return limp;
    }

    void Init(Animator a)
    {
        animator = a;
        bones.Clear();
        void Add(HumanBodyBones b, HumanBodyBones c, float limp)
        {
            var t = a.GetBoneTransform(b); var ct = a.GetBoneTransform(c);
            if (t == null || ct == null) return;
            bones.Add(new Bone { bone = t, child = ct, rest = t.localRotation, limpness = limp, dir = (ct.position - t.position).normalized, seed = Random.value * 50f });
        }
        // 由身體往外：父骨頭先轉，子骨頭的位置才是新的
        Add(HumanBodyBones.Spine, HumanBodyBones.Chest, .25f);
        Add(HumanBodyBones.Neck, HumanBodyBones.Head, .55f);
        Add(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, .85f);
        Add(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, .7f);
        Add(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, .85f);
        Add(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, .7f);
        Add(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, .6f);
        Add(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, .55f);
        Add(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, .6f);
        Add(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, .55f);
        lastRoot = transform.position;
    }

    /// <summary>被砍到：四肢往 direction 甩一下</summary>
    public void Kick(Vector3 direction, float strength = 6f) => kick += direction.normalized * strength;

    void LateUpdate()
    {
        if (animator == null || bones.Count == 0) return;
        if (animator.enabled) { lastRoot = transform.position; return; }   // 被放開了，Animator 自己在播
        float dt = Time.unscaledDeltaTime * Mathf.Max(.02f, SlowFactor);
        Vector3 rootVel = dt > 1e-5f ? (transform.position - lastRoot) / Mathf.Max(1e-4f, Time.unscaledDeltaTime) : Vector3.zero;
        lastRoot = transform.position;
        float t = Time.unscaledTime * Mathf.Max(.02f, SlowFactor);

        foreach (var b in bones)
        {
            if (b.bone == null || b.child == null) continue;
            b.bone.localRotation = b.rest;   // 從被打中的那一格出發（不會越轉越歪）
            Vector3 restDir = (b.child.position - b.bone.position).normalized;

            // 垂下去的方向：重力 + 往移動的反方向拖 + 一點晃動 + 被砍時甩出去
            Vector3 sway = new Vector3(Mathf.PerlinNoise(t * .9f, b.seed) - .5f, 0f, Mathf.PerlinNoise(b.seed, t * .9f) - .5f) * 1.2f;
            Vector3 hang = (Vector3.down + (-rootVel * .06f) + sway + kick * .15f).normalized;
            Vector3 want = Vector3.Slerp(restDir, hang, b.limpness * weight).normalized;

            // 彈簧：慢慢追上去（軟的感覺）
            const float k = 60f, damp = 9f;
            b.vel += ((want - b.dir) * k - b.vel * damp) * dt;
            b.dir = (b.dir + b.vel * dt).normalized;
            if (b.dir.sqrMagnitude < .5f) b.dir = want;

            b.bone.rotation = Quaternion.FromToRotation(restDir, b.dir) * b.bone.rotation;
        }
        kick = Vector3.Lerp(kick, Vector3.zero, 1f - Mathf.Exp(-4f * dt));
    }

    /// <summary>還原成被打中的那一格（技能放開敵人時）</summary>
    public void Release()
    {
        foreach (var b in bones) if (b.bone != null) b.bone.localRotation = b.rest;
        Destroy(this);
    }
}
