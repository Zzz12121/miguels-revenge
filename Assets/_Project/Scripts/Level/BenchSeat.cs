using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 可以坐下的長椅（茶屋門口的兩張，VillageEconomy 自動掛上）：
///   - 走近按 E「坐下」：武器收起來，人走到椅子前面、轉身背對椅子，屁股坐到椅面上（照椅子的高度把整個骨架往下、往後移），
///     雙腳用 IK 踩在椅子前面的地上、膝蓋往前彎，雙手放在大腿上
///   - 坐在椅子上離玩家最近的位子；茶屋老闆娘（或其他村民）坐著的那一邊就換另一邊
///   - 按 E、空白鍵或方向鍵起身
///   - 坐著的時候，掛在身上的東西（收在背上的刀、箭筒……）先藏起來，不會插進椅子；起身再拿出來
/// 椅子的長邊是 X（Builder 的 PlaceBench），坐的人面向 -Z（跟老闆娘一樣）；椅面高度 = 碰撞盒的頂。
/// 姿勢在所有動畫擺完之後才擺（DefaultExecutionOrder）。
/// </summary>
[DefaultExecutionOrder(32350)]
public sealed class BenchSeat : MonoBehaviour
{
    [Tooltip("離椅子多近可以坐 (m)")]
    public float range = 1.6f;
    [Tooltip("坐下 / 起身要多久 (秒)")]
    public float blendTime = 0.45f;

    PlayerHealth health;
    ThirdPersonController controller;
    Animator animator;
    BoxCollider box;
    bool sitting, leaving;
    float weight, standAt = -1f;
    Vector3 seatPoint, facing, footBase;
    float ankleHeight;

    readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();

    Transform hips, lThigh, lKnee, lFoot, rThigh, rKnee, rFoot, lArm, lFore, lHand, rArm, rFore, rHand;

    void Start() => box = GetComponent<BoxCollider>();

    float SeatTop => box != null ? transform.TransformPoint(box.center + Vector3.up * box.size.y * 0.5f).y : transform.position.y + 0.48f;
    float HalfLength => box != null ? box.size.x * 0.5f * transform.lossyScale.x : 0.8f;

    void Update()
    {
        if (health == null)
        {
            health = PlayerHealth.Find();
            if (health == null) return;
            controller = health.GetComponent<ThirdPersonController>();
        }
        if (GameUI.OverlayOpen) return;

        if (!sitting)
        {
            if (health.IsDead || StoryDialogue.Active) return;
            Vector3 local = transform.InverseTransformPoint(health.transform.position);
            float along = Mathf.Clamp(local.x, -HalfLength, HalfLength);
            Vector3 nearest = transform.TransformPoint(new Vector3(along, local.y, 0f));
            Vector3 d = health.transform.position - nearest;
            float dist = new Vector2(d.x, d.z).magnitude;
            if (dist > range) return;
            InteractPrompt.Offer(this, dist, "坐下");
            if (InteractPrompt.Pressed(this)) SitDown(local.x);
            return;
        }

        if (health.IsDead || StoryDialogue.Active) { StandUp(true); return; }
        if (leaving)
        {
            if (Time.time >= standAt) StandUp(true);
            return;
        }
        var kb = Keyboard.current;
        bool move = kb != null && (kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
        if (weight > 0.95f && (move || InputBindings.Pressed(GameAction.Interact) || PadInput.CancelPressed || PadInput.ConfirmPressed))
        {
            leaving = true;
            standAt = Time.time + blendTime;
        }
    }

    void SitDown(float wantX)
    {
        animator = health.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        ResolveBones();
        if (hips == null || lFoot == null || rFoot == null) return;
        // 位子：離玩家最近、但不要坐到別人身上（左右各留 0.35 m）
        float half = Mathf.Max(0f, HalfLength - 0.35f);
        float x = Mathf.Clamp(wantX, -half, half);
        foreach (var npc in FindObjectsByType<MerchantNPC>(FindObjectsSortMode.None))
        {
            Vector3 l = transform.InverseTransformPoint(npc.transform.position);
            if (Mathf.Abs(l.z) < 0.8f && Mathf.Abs(l.x - x) < 0.7f) x = Mathf.Clamp(l.x > x ? l.x - 0.75f : l.x + 0.75f, -half, half);
        }
        facing = transform.TransformDirection(Vector3.back);
        facing.y = 0f;
        facing.Normalize();
        float top = SeatTop;
        seatPoint = transform.TransformPoint(new Vector3(x, 0f, 0f));
        seatPoint.y = top;
        // 人站到椅子前面（腳要踩的地方），轉身背對椅子
        Vector3 stand = seatPoint + facing * (box != null ? box.size.z * 0.5f + 0.32f : 0.55f);
        stand.y = health.transform.position.y;
        if (Physics.Raycast(stand + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) stand.y = hit.point.y + 0.02f;
        var cc = health.GetComponent<CharacterController>();
        bool was = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        health.transform.SetPositionAndRotation(stand, Quaternion.LookRotation(facing));
        Physics.SyncTransforms();
        if (cc != null && was) cc.enabled = true;
        footBase = new Vector3(stand.x, health.FeetY, stand.z);
        ankleHeight = Mathf.Max(0.04f, (lFoot.position.y + rFoot.position.y) * 0.5f - health.FeetY);
        if (controller != null)
        {
            controller.LockActions();
            controller.inputEnabled = false;
        }
        PlayerLoadout.Holster(this);
        HideGear(true);
        sitting = true;
        leaving = false;
        GameAudio.Play("SFX/RPG/cloth2", seatPoint, 0.6f, 0.9f, 0.05f, 0.3f);
    }

    void StandUp(bool unlock)
    {
        if (!sitting) return;
        sitting = leaving = false;
        weight = 0f;
        HideGear(false);
        PlayerLoadout.Unholster(this);
        if (unlock && controller != null)
        {
            controller.UnlockActions();
            controller.SuppressJump(0.4f);
            if (health == null || !health.IsDead) controller.inputEnabled = true;
        }
    }

    void OnDisable() => StandUp(true);

    /// <summary>
    /// 藏起（或拿回）掛在身上的東西：玩家底下除了身體本身（SkinnedMeshRenderer）、造型（Cosmetic_）以外，現在看得到的網格。
    /// 只拿回自己藏起來的那些
    /// </summary>
    void HideGear(bool hide)
    {
        if (!hide)
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
            return;
        }
        hidden.Clear();
        foreach (var r in health.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is SkinnedMeshRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
            bool cosmetic = false;
            for (var t = r.transform; t != null && t != health.transform; t = t.parent)
                if (t.name.StartsWith("Cosmetic")) { cosmetic = true; break; }
            if (cosmetic) continue;
            r.enabled = false;
            hidden.Add(r);
        }
    }

    void ResolveBones()
    {
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        hips = B(HumanBodyBones.Hips);
        lThigh = B(HumanBodyBones.LeftUpperLeg); lKnee = B(HumanBodyBones.LeftLowerLeg); lFoot = B(HumanBodyBones.LeftFoot);
        rThigh = B(HumanBodyBones.RightUpperLeg); rKnee = B(HumanBodyBones.RightLowerLeg); rFoot = B(HumanBodyBones.RightFoot);
        lArm = B(HumanBodyBones.LeftUpperArm); lFore = B(HumanBodyBones.LeftLowerArm); lHand = B(HumanBodyBones.LeftHand);
        rArm = B(HumanBodyBones.RightUpperArm); rFore = B(HumanBodyBones.RightLowerArm); rHand = B(HumanBodyBones.RightHand);
    }

    void LateUpdate()
    {
        if (!sitting || hips == null) return;
        weight = Mathf.MoveTowards(weight, leaving ? 0f : 1f, Time.deltaTime / Mathf.Max(0.05f, blendTime));
        float w = Mathf.SmoothStep(0f, 1f, weight);
        if (w < 0.001f) return;
        Transform p = health.transform;
        Vector3 right = Vector3.Cross(Vector3.up, facing);
        // 骨盆坐到椅面上（大腿骨的高度約在椅面上 9 cm），往後移到椅子中間
        float thighY = (lThigh.position.y + rThigh.position.y) * 0.5f;
        Vector3 hipsTarget = new Vector3(seatPoint.x, seatPoint.y + 0.09f + (hips.position.y - thighY), seatPoint.z) + facing * 0.04f;
        hips.position = Vector3.Lerp(hips.position, hipsTarget, w);
        // 雙腳：踩在椅子前面的地上（跟髖關節同寬再開一點），膝蓋往前上方彎
        Leg(lThigh, lKnee, lFoot, -1f, right, w);
        Leg(rThigh, rKnee, rFoot, 1f, right, w);
        // 雙手放在大腿上（膝蓋往回三成的地方），手肘往後外
        Arm(lArm, lFore, lHand, lThigh, lKnee, -1f, right, w);
        Arm(rArm, rFore, rHand, rThigh, rKnee, 1f, right, w);
    }

    void Leg(Transform thigh, Transform knee, Transform foot, float side, Vector3 right, float w)
    {
        if (thigh == null || knee == null || foot == null) return;
        float hipWidth = Mathf.Abs(Vector3.Dot(thigh.position - hips.position, right));
        Vector3 target = footBase + facing * 0.12f + right * side * (hipWidth + 0.04f) + Vector3.up * ankleHeight;
        Quaternion flat = foot.rotation; // 站著的腳掌是平的：IK 之後轉回來，腳掌才會平踩在地上
        TwoBone(thigh, knee, foot, target, knee.position + facing * 0.5f + Vector3.up * 0.3f, w);
        foot.rotation = Quaternion.Slerp(foot.rotation, flat, w);
    }

    void Arm(Transform arm, Transform fore, Transform hand, Transform thigh, Transform knee, float side, Vector3 right, float w)
    {
        if (arm == null || fore == null || hand == null || thigh == null || knee == null) return;
        Vector3 target = Vector3.Lerp(thigh.position, knee.position, 0.7f) + Vector3.up * 0.06f + right * side * 0.02f;
        TwoBone(arm, fore, hand, target, arm.position - facing * 0.4f + right * side * 0.3f - Vector3.up * 0.2f, w);
    }

    /// <summary>雙骨 IK（跟 FishingSpot 的一樣）：先彎中間的關節，再整段轉過去，最後讓中間的關節朝 hint</summary>
    static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float w)
    {
        Quaternion origA = a.rotation, origB = b.localRotation;
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        Vector3 toT = target - a.position;
        float lt = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 0.01f, la + lb - 0.005f);
        Vector3 u = a.position - b.position, v = c.position - b.position;
        Vector3 n = Vector3.Cross(u, v);
        if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(toT, hint - a.position);
        if (n.sqrMagnitude > 1e-8f)
        {
            float cur = Vector3.Angle(u, v);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - lt * lt) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
            b.rotation = Quaternion.AngleAxis(want - cur, n.normalized) * b.rotation;
        }
        Vector3 cur2 = c.position - a.position;
        if (cur2.sqrMagnitude > 1e-8f && toT.sqrMagnitude > 1e-8f) a.rotation = Quaternion.FromToRotation(cur2, toT) * a.rotation;
        Vector3 d = toT.normalized;
        Vector3 e = Vector3.ProjectOnPlane(b.position - a.position, d), h = Vector3.ProjectOnPlane(hint - a.position, d);
        if (e.sqrMagnitude > 1e-8f && h.sqrMagnitude > 1e-8f) a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(e, h, d), d) * a.rotation;
        if (w < 0.999f)
        {
            Quaternion finA = a.rotation, finB = b.localRotation;
            a.rotation = Quaternion.Slerp(origA, finA, w);
            b.localRotation = Quaternion.Slerp(origB, finB, w);
        }
    }

    void OnGUI()
    {
        if (!sitting || leaving || GameUI.OverlayOpen || weight < 0.95f) return;
        GUI.depth = -1150;
        var r = new Rect((Screen.width - 320f) * 0.5f, Screen.height * 0.72f, 320f, 44f);
        GameUI.Panel(r, GameUI.MutedColor);
        GUI.color = GameUI.TextColor;
        GUI.Label(new Rect(r.x + 16f, r.y, r.width - 140f, r.height), "坐著歇一會……", GameUI.Small);
        string key = InteractPrompt.KeyName;
        float kw = Mathf.Max(26f, GameUI.Small.CalcSize(new GUIContent(key)).x + 12f);
        GUI.Label(new Rect(r.xMax - 64f, r.y, 60f, r.height), "起身", GameUI.Small);
        GameUI.Key(new Rect(r.xMax - 72f - kw, r.y + 9f, kw, 26f), key);
        GUI.color = Color.white;
    }
}
