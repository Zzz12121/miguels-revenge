using UnityEngine;

/// <summary>
/// 跟玩家對話時一臉不耐煩：右手舉到後腦勺搔頭、頭往旁邊歪、時不時把頭轉開，左腳尖一直點地。
/// 對話一開始就慢慢擺出來，對話結束就放下（程式擺骨頭，蓋在原本的動畫上；過招中不會有）。
/// </summary>
[DefaultExecutionOrder(50)] // 在 MonkSparring（受擊後仰、鞠躬）之後
public class ImpatientTalker : MonoBehaviour
{
    public Animator animator;

    MerchantNPC npc;
    MonkSparring spar;
    float w, seed;
    Transform head, neck, rArm, rFore, rHand, lFoot;

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        spar = GetComponent<MonkSparring>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        seed = Random.value * 10f;
    }

    void Update()
    {
        bool talking = npc != null && npc.InConversation && (spar == null || !spar.Fighting);
        w = Mathf.MoveTowards(w, talking ? 1f : 0f, Time.deltaTime * (talking ? 2.5f : 3.5f));
    }

    void LateUpdate()
    {
        if (w <= 0.001f || animator == null || !animator.isHuman) return;
        if (head == null)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            rArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rFore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (head == null) return;
        }
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        float t = Time.time + seed;
        float s = transform.lossyScale.y;

        // 頭：往右歪、微微低頭；每隔幾秒把臉轉開一下（不想看你）
        float away = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(t * 0.7f) * 2.2f - 1.2f));
        var tilt = Quaternion.AngleAxis(-14f * w, fwd) * Quaternion.AngleAxis(8f * w, side) * Quaternion.AngleAxis(-28f * away * w, up);
        if (neck != null) neck.rotation = Quaternion.Slerp(Quaternion.identity, tilt, 0.4f) * neck.rotation;
        head.rotation = Quaternion.Slerp(Quaternion.identity, tilt, 0.6f) * head.rotation;

        // 右手：手肘往外抬，手掌放到後腦勺，前後來回搔
        if (rArm != null && rFore != null && rHand != null)
        {
            Aim(rArm, rFore, up * 0.5f + side * 0.8f + fwd * 0.05f, w);
            float scratch = Mathf.Sin(t * 13f) * 0.035f * s;
            Vector3 target = head.position + up * 0.1f * s - fwd * 0.07f * s + side * 0.05f * s + fwd * scratch;
            Aim(rFore, rHand, target - rFore.position, w);
        }

        // 左腳尖點地
        if (lFoot != null)
        {
            float tap = Mathf.Max(0f, Mathf.Sin(t * 7f)) * 16f;
            lFoot.rotation = Quaternion.AngleAxis(-tap * w, side) * lFoot.rotation;
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
