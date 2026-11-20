using UnityEngine;

/// <summary>
/// 村民之間聊天時的動作（程式擺骨頭，蓋在待機動畫上）：
///   說話的一方：一隻手抬起來往外比劃、頭微微點；聽的一方：點頭、偶爾笑一下往後仰。
/// VillageLife 設定 active / speaking；被拜訪的人（站著不動的）會轉向 faceTarget，結束後轉回原本的方向。
/// </summary>
[DefaultExecutionOrder(60)]
public class TalkGestures : MonoBehaviour
{
    public Animator animator;
    [System.NonSerialized] public bool active;
    [System.NonSerialized] public bool speaking;
    [System.NonSerialized] public Transform faceTarget;
    [Tooltip("開：被拜訪時會轉身面向對方（站著不動的人）")]
    public bool turnToVisitor;

    float w, seed, laughUntil = -1f;
    Quaternion homeRotation;
    bool homeSet;
    MerchantNPC npc;
    Transform head, rArm, rFore, rHand, lArm, lFore, lHand, spine;

    void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        npc = GetComponent<MerchantNPC>();
        seed = Random.value * 20f;
    }

    void Update()
    {
        bool on = active && (npc == null || !npc.InConversation);
        w = Mathf.MoveTowards(w, on ? 1f : 0f, Time.deltaTime * 2.5f);
        if (turnToVisitor)
        {
            if (!homeSet) { homeRotation = transform.rotation; homeSet = true; }
            if (npc != null && npc.InConversation) return; // 跟玩家說話時交給 MerchantNPC
            Quaternion want = homeRotation;
            if (on && faceTarget != null)
            {
                Vector3 d = faceTarget.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) want = Quaternion.LookRotation(d);
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 120f * Time.deltaTime);
        }
        if (on && !speaking && laughUntil < Time.time && Random.value < Time.deltaTime * 0.12f) laughUntil = Time.time + 1.1f;
    }

    void LateUpdate()
    {
        if (w <= 0.001f || animator == null || !animator.isHuman) return;
        if (head == null)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            spine = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            rArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rFore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            lArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lFore = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (head == null) return;
        }
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        float t = Time.time + seed;

        if (speaking)
        {
            // 比手畫腳：一陣子右手、一陣子左手，手掌朝上往外攤
            bool right = Mathf.Repeat(t * 0.35f, 1f) < 0.6f;
            float sweep = Mathf.Sin(t * 3.1f) * 0.35f;
            float lift = 0.5f + 0.5f * Mathf.Sin(t * 1.7f);
            var arm = right ? rArm : lArm;
            var fore = right ? rFore : lFore;
            var hand = right ? rHand : lHand;
            float sgn = right ? 1f : -1f;
            Aim(arm, fore, -up * 0.75f + fwd * 0.45f + side * sgn * 0.35f, w);
            Aim(fore, hand, fwd * 0.8f + up * (0.2f + 0.4f * lift) + side * sgn * (0.3f + sweep), w);
            head.rotation = Quaternion.AngleAxis(Mathf.Sin(t * 2.3f) * 6f * w, side) * head.rotation;
        }
        else
        {
            // 聽：點頭；偶爾笑一下往後仰
            float nod = Mathf.Max(0f, Mathf.Sin(t * 2.2f)) * 9f;
            head.rotation = Quaternion.AngleAxis(nod * w, side) * head.rotation;
            if (spine != null && laughUntil > Time.time)
            {
                float k = Mathf.Sin(Mathf.Clamp01((laughUntil - Time.time) / 1.1f) * Mathf.PI);
                spine.rotation = Quaternion.AngleAxis((-10f + Mathf.Sin(t * 18f) * 2f) * k * w, side) * spine.rotation;
            }
        }
    }

    static void Aim(Transform bone, Transform child, Vector3 dir, float w)
    {
        if (bone == null || child == null || w <= 0.001f || dir.sqrMagnitude < 1e-6f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, dir.normalized), w) * bone.rotation;
    }
}
