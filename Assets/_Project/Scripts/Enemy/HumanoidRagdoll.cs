using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 執行時把 Humanoid 角色變成布娃娃（不用事先做 Ragdoll Prefab）：
///   骨盆、腰、胸、頭、上臂、前臂、大腿、小腿各放一個剛體 + 碰撞體，用 CharacterJoint 接起來，關掉 Animator，
///   四肢就會照物理軟軟地甩動、倒地。Launch() 把整個人往某個方向打飛。
/// 用法：var rd = HumanoidRagdoll.Create(animator, 60f); rd.Launch(velocity, spin);
/// </summary>
public sealed class HumanoidRagdoll : MonoBehaviour
{
    readonly List<Rigidbody> bodies = new List<Rigidbody>();
    readonly List<Collider> colliders = new List<Collider>();
    public IReadOnlyList<Rigidbody> Bodies => bodies;
    public Rigidbody Pelvis { get; private set; }

    struct Part
    {
        public HumanBodyBones bone, child, parent;
        public float mass, radiusScale;
        public bool sphere;
        public Vector2 twist; public float swing1, swing2;
        public Part(HumanBodyBones bone, HumanBodyBones child, HumanBodyBones parent, float mass, float radiusScale, Vector2 twist, float swing1, float swing2, bool sphere = false)
        { this.bone = bone; this.child = child; this.parent = parent; this.mass = mass; this.radiusScale = radiusScale; this.twist = twist; this.swing1 = swing1; this.swing2 = swing2; this.sphere = sphere; }
    }

    const HumanBodyBones None = HumanBodyBones.LastBone;

    // 質量比例（加起來 ≈ 1），關節限制刻意放寬一點：看起來比較「癱軟」
    static readonly Part[] Parts =
    {
        new Part(HumanBodyBones.Hips, HumanBodyBones.Spine, None, 0.16f, 0.9f, Vector2.zero, 0f, 0f),
        new Part(HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Hips, 0.14f, 0.85f, new Vector2(-25f, 20f), 25f, 15f),
        new Part(HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Spine, 0.16f, 0.95f, new Vector2(-20f, 15f), 20f, 12f),
        new Part(HumanBodyBones.Head, None, HumanBodyBones.Chest, 0.08f, 1f, new Vector2(-40f, 30f), 35f, 25f, true),
        new Part(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.Chest, 0.04f, 0.32f, new Vector2(-80f, 60f), 80f, 30f),
        new Part(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftUpperArm, 0.03f, 0.28f, new Vector2(-120f, 0f), 15f, 10f),
        new Part(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.Chest, 0.04f, 0.32f, new Vector2(-80f, 60f), 80f, 30f),
        new Part(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightUpperArm, 0.03f, 0.28f, new Vector2(-120f, 0f), 15f, 10f),
        new Part(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.Hips, 0.09f, 0.3f, new Vector2(-30f, 90f), 35f, 20f),
        new Part(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftUpperLeg, 0.05f, 0.24f, new Vector2(-120f, 0f), 10f, 10f),
        new Part(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.Hips, 0.09f, 0.3f, new Vector2(-30f, 90f), 35f, 20f),
        new Part(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightUpperLeg, 0.05f, 0.24f, new Vector2(-120f, 0f), 10f, 10f),
    };

    public static HumanoidRagdoll Create(Animator animator, float totalMass = 60f, Collider ignoreWith = null)
    {
        if (animator == null || !animator.isHuman) return null;
        var rd = animator.gameObject.AddComponent<HumanoidRagdoll>();
        rd.Build(animator, totalMass, ignoreWith);
        return rd;
    }

    void Build(Animator animator, float totalMass, Collider ignoreWith)
    {
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform lUp = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), rUp = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        // 角色的整體尺寸（世界座標）：用肩寬當基準算碰撞體粗細
        float shoulder = (lUp != null && rUp != null) ? Vector3.Distance(lUp.position, rUp.position) : 0.35f;
        float torsoR = shoulder * 0.42f;

        var byBone = new Dictionary<HumanBodyBones, Rigidbody>();
        foreach (var p in Parts)
        {
            Transform t = animator.GetBoneTransform(p.bone);
            if (t == null) continue;
            Transform child = p.child != None ? animator.GetBoneTransform(p.child) : null;
            if (p.bone == HumanBodyBones.Chest && child == null) child = head;

            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.5f, totalMass * p.mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;
            rb.maxAngularVelocity = 25f;
            byBone[p.bone] = rb;
            bodies.Add(rb);

            Collider col;
            if (p.sphere || child == null)
            {
                var sc = t.gameObject.AddComponent<SphereCollider>();
                sc.radius = torsoR * 0.75f / Mathf.Max(0.0001f, t.lossyScale.x);
                sc.center = t.InverseTransformPoint(t.position + Vector3.up * torsoR * 0.4f);
                col = sc;
            }
            else
            {
                var cc = t.gameObject.AddComponent<CapsuleCollider>();
                Vector3 localChild = t.InverseTransformPoint(child.position);
                float len = localChild.magnitude;
                Vector3 ax = localChild / Mathf.Max(0.0001f, len);
                // 選最接近骨頭方向的軸
                float x = Mathf.Abs(ax.x), y = Mathf.Abs(ax.y), z = Mathf.Abs(ax.z);
                cc.direction = x > y && x > z ? 0 : (y > z ? 1 : 2);
                cc.center = localChild * 0.5f;
                cc.height = len * 1.05f;
                float r = (p.bone == HumanBodyBones.Hips || p.bone == HumanBodyBones.Spine || p.bone == HumanBodyBones.Chest)
                    ? torsoR * p.radiusScale : shoulder * p.radiusScale * 0.5f;
                cc.radius = r / Mathf.Max(0.0001f, t.lossyScale.x);
                col = cc;
            }
            colliders.Add(col);
            if (ignoreWith != null) Physics.IgnoreCollision(col, ignoreWith);

            if (p.parent != None && byBone.TryGetValue(p.parent, out var parentRb))
            {
                var j = t.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parentRb;
                j.enableProjection = true;
                j.enablePreprocessing = false;
                Vector3 axis = child != null ? t.InverseTransformDirection(child.position - t.position).normalized : Vector3.up;
                j.axis = axis;
                j.swingAxis = Vector3.Cross(axis, Vector3.right).sqrMagnitude > 0.01f ? Vector3.Cross(axis, Vector3.right).normalized : Vector3.forward;
                j.lowTwistLimit = new SoftJointLimit { limit = p.twist.x };
                j.highTwistLimit = new SoftJointLimit { limit = p.twist.y };
                j.swing1Limit = new SoftJointLimit { limit = p.swing1 };
                j.swing2Limit = new SoftJointLimit { limit = p.swing2 };
            }
            if (p.bone == HumanBodyBones.Hips) Pelvis = rb;
        }
        // 身體各部位之間不要互相卡住
        for (int i = 0; i < colliders.Count; i++)
            for (int k = i + 1; k < colliders.Count; k++)
                Physics.IgnoreCollision(colliders[i], colliders[k]);

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
        animator.enabled = false;
    }

    /// <summary>整個人用 velocity 打飛，spin = 翻滾的角速度（世界座標，rad/s）</summary>
    public void Launch(Vector3 velocity, Vector3 spin)
    {
        foreach (var rb in bodies)
        {
            // 四肢的速度稍微亂一點，甩起來比較像真的被打飛
            rb.linearVelocity = velocity * Random.Range(0.85f, 1.1f);
            rb.angularVelocity = spin + Random.insideUnitSphere * 2f;
        }
        if (Pelvis != null) Pelvis.linearVelocity = velocity;
    }

    /// <summary>停在現在的姿勢（之後可以整個沉進地面）</summary>
    public void Freeze()
    {
        foreach (var rb in bodies) { if (rb == null) continue; rb.isKinematic = true; }
        foreach (var c in colliders) if (c != null) c.enabled = false;
    }

    /// <summary>全部剛體都差不多停下來了</summary>
    public bool Settled
    {
        get
        {
            foreach (var rb in bodies)
                if (rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.04f) return false;
            return true;
        }
    }
}
