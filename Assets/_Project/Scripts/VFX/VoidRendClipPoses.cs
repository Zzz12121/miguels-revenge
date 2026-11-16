using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能 1（虛空裂斬）用 Tripo 做的姿勢動畫：
///   VR_Summon（CW_VRSummon，Blender 手擺：右手從胸前往右前上方舉向刀、掌心張開對著刀）：轉刀時單手舉高、手掌張開，刀從手上往上飛、在頭頂旋轉（站姿）
///   VR_Horse（CW_VRHorse）：拔刀的低馬步，出拳 / 22 段突進斬期間整個人維持這個姿勢，刀照原本的袈裟斬揮
///   VR_Iai（CW_VRIai，手擺的）：居合低伏預備（右腳在前深蹲、左腳往後拉長、上身前傾低頭、雙手握在左腰），
///     砍完之後停在空中、爆炸、鏡頭拉回這段用；沒有這個狀態就用馬步
/// 技能期間 Animator 是關掉的（姿勢由程式控制），所以技能一開始先把這兩段動畫取樣存起來，之後每一幀直接套用。
/// 換姿勢的時候（轉刀 → 出拳、突進斬 → 收刀）從上一幀的姿勢混 0.2 秒，不會跳。
/// Animator 沒有這兩個狀態（還沒跑 GeminiGrowUp > 安裝 玩家跳躍・下落・攀爬動畫）就照舊用程式擺的姿勢。
/// </summary>
public sealed partial class VoidRendSkillDemo
{
    [Header("Skill 1 / Tripo 姿勢（轉刀舉手、低馬步）")]
    [Tooltip("轉刀：刀從手掌飛出去，停在頭的前方 / 右邊 / 上方多遠（公尺）。前方遠一點，頭不用抬太高就看得到")]
    public float summonSwordForward = .95f, summonSwordSide = .4f, summonSwordUp = .8f;
    [Tooltip("刀飛過去花幾秒")]
    public float summonLiftSeconds = 1.2f;
    [Tooltip("換姿勢時混合幾秒")]
    [Range(0f, .6f)] public float skillPoseBlendSeconds = .2f;
    [Tooltip("砍完之後停在空中的高度（公尺，從原本站的高度往上算）。鏡頭切回玩家後才從這裡掉下來落地")]
    [Min(0f)] public float finisherAirHoldHeight = 2.2f;
    [Tooltip("突進斬結束、換成居合低伏姿勢時混合幾秒")]
    [Range(.05f, 1f)] public float iaiBlendSeconds = .3f;
    [Tooltip("技能結束、開始往下掉的時候，從馬步混到下落動畫花幾秒")]
    [Range(.05f, 1f)] public float horseToFallBlendSeconds = .25f;

    sealed class SampledPose
    {
        public Transform[] bones;
        public Quaternion[][] rotations;   // [樣本][骨頭]
        public Vector3[] hipsPositions;    // [樣本]
        public bool Valid => bones != null && rotations != null && rotations.Length > 0;
    }

    SampledPose summonPose, horsePose, iaiPose;
    // 頭的「臉朝向」（頭骨本身的座標）：轉刀時讓頭往上看刀
    Vector3 headFaceLocal = Vector3.forward;
    bool headFaceKnown;
    [Tooltip("轉刀時視線看著刀：脖子分擔多少、頭最多抬幾度（超過就只抬到這裡，不會往後折）")]
    [Range(0f, 1f)] public float lookAtSwordNeck = .15f;
    [Range(0f, 70f)] public float lookAtSwordMaxElevation = 40f;
    [Tooltip("舉高的手再額外轉向刀多少：0 = 照動畫（Blender 手擺的轉刀動畫已經往刀的方向舉、掌心對著刀）、1 = 整隻手臂直直指向刀")]
    [Range(0f, 1f)] public float summonArmFollowSword = 0f;
    readonly Dictionary<Transform, Quaternion> lastAppliedRotations = new();
    readonly Dictionary<Transform, Quaternion> blendFromRotations = new();
    Vector3 lastAppliedHips, blendFromHips;
    string lastPoseStage;
    float poseBlendStart = float.NegativeInfinity;
    // 技能結束、Animator 接手之後：從最後的馬步混到 Animator 的下落動畫
    readonly Dictionary<Transform, Quaternion> handoffFromRotations = new();
    Vector3 handoffFromHips;
    float handoffStart = float.NegativeInfinity;

    bool HasSkillClipPoses => (summonPose != null && summonPose.Valid) || (horsePose != null && horsePose.Valid) || HasIaiPose;
    bool HasIaiPose => iaiPose != null && iaiPose.Valid;

    /// <summary>技能開始時（Animator 還開著）把姿勢動畫取樣存起來。之後要再 Play Idle 拿站姿。</summary>
    void CaptureSkillClipPoses()
    {
        headFaceKnown = false;
        summonPose = Sample("VR_Summon", 31, true);
        horsePose = Sample("VR_Horse", 9);
        iaiPose = Sample("VR_Iai", 1);
        lastAppliedRotations.Clear();
        blendFromRotations.Clear();
        lastPoseStage = null;
    }

    SampledPose Sample(string state, int count, bool captureFace = false)
    {
        if (casterAnimator == null || !casterAnimator.isHuman || !casterAnimator.HasState(0, Animator.StringToHash(state))) return null;
        var bones = new List<Transform>();
        for (HumanBodyBones id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++)
        {
            var b = casterAnimator.GetBoneTransform(id);
            if (b != null && !bones.Contains(b)) bones.Add(b);
        }
        var pose = new SampledPose { bones = bones.ToArray(), rotations = new Quaternion[count][], hipsPositions = new Vector3[count] };
        Transform hips = casterAnimator.GetBoneTransform(HumanBodyBones.Hips);
        Transform head = casterAnimator.GetBoneTransform(HumanBodyBones.Head);
        for (int i = 0; i < count; i++)
        {
            casterAnimator.Play(state, 0, count == 1 ? 0f : i / (count - 1f));
            casterAnimator.Update(0f);
            // 第一格是站著看前方：記下臉在頭骨座標裡朝哪個方向
            if (captureFace && i == 0 && head != null && caster != null)
            {
                headFaceLocal = Quaternion.Inverse(head.rotation) * caster.forward;
                headFaceKnown = true;
            }
            var r = new Quaternion[pose.bones.Length];
            for (int b = 0; b < r.Length; b++) r[b] = pose.bones[b].localRotation;
            pose.rotations[i] = r;
            pose.hipsPositions[i] = hips != null ? hips.localPosition : Vector3.zero;
        }
        return pose;
    }

    /// <summary>套用取樣的姿勢（s = 0～1，動畫的播放進度）</summary>
    static void ApplySampled(SampledPose pose, float s)
    {
        float f = Mathf.Clamp01(s) * (pose.rotations.Length - 1);
        int a = Mathf.FloorToInt(f), b = Mathf.Min(a + 1, pose.rotations.Length - 1);
        float k = f - a;
        for (int i = 0; i < pose.bones.Length; i++)
            if (pose.bones[i] != null) pose.bones[i].localRotation = Quaternion.Slerp(pose.rotations[a][i], pose.rotations[b][i], k);
        if (pose.bones.Length > 0 && pose.bones[0] != null)
            pose.bones[0].localPosition = Vector3.Lerp(pose.hipsPositions[a], pose.hipsPositions[b], k);
    }

    /// <summary>這個階段要用 Tripo 姿勢的話套上去並回傳 true（之後再疊程式的手臂 / 刀）</summary>
    bool ApplyStageClipPose(float stageAge)
    {
        if (Stage == "AerialRotor" && summonPose != null && summonPose.Valid)
        {
            // 前 0.35 秒：手從胸前舉上去（動畫第 8 → 16 格）；之後在舉高的那段（16 → 32 格）慢慢來回，看起來是活的
            float s = stageAge < .35f
                ? Mathf.Lerp(.13f, .4f, Mathf.SmoothStep(0f, 1f, stageAge / .35f))
                : .4f + Mathf.PingPong((stageAge - .35f) * .22f, .53f);
            ApplySampled(summonPose, s);
            return true;
        }
        if ((Stage == "Punch" || Stage == "DashLattice") && horsePose != null && horsePose.Valid)
        {
            ApplySampled(horsePose, .25f + .5f * Mathf.PingPong(stageAge * .5f, 1f));
            return true;
        }
        // 砍完之後：飛到目標前方的空中、爆炸、鏡頭拉回（Recover），整段都停在空中維持馬步；
        // 技能結束後才交給玩家控制器往下掉，跟跳躍一樣播下落 → 落地動畫
        if (IsHorseHoldStage() && HasIaiPose)
        {
            ApplySampled(iaiPose, 0f);
            return true;
        }
        if (IsHorseHoldStage() && horsePose != null && horsePose.Valid)
        {
            ApplySampled(horsePose, .5f);
            return true;
        }
        return false;
    }

    bool IsHorseHoldStage() => Stage == "SheatheOrbit" || Stage == "FinalBreak" || Stage == "Recover";

    /// <summary>居合低伏：右手握刀柄在左腰，刀身沿著左腰往後下方（像收在刀鞘裡，刃朝上）</summary>
    void ApplyIaiSword()
    {
        if (!HasIaiPose || weapon == null || rightHand == null) return;
        weapon.position = rightHand.position;
        AlignWeaponBlade((-caster.forward * .85f - caster.up * .35f - caster.right * .12f).normalized, caster.up);
    }

    /// <summary>技能結束、Animator 打開的那一刻呼叫：記下最後的馬步，之後幾幀慢慢混到 Animator 的姿勢</summary>
    void BeginHandoffToAnimator()
    {
        handoffFromRotations.Clear();
        if (!HasSkillClipPoses || lastAppliedRotations.Count == 0) return;
        foreach (var p in lastAppliedRotations) handoffFromRotations[p.Key] = p.Value;
        handoffFromHips = lastAppliedHips;
        handoffStart = Time.unscaledTime;
    }

    /// <summary>技能結束後的 LateUpdate（Animator 已經算完下落動畫）：從馬步混過去，不會一下跳成下落姿勢</summary>
    void ApplyHandoffToAnimator()
    {
        if (handoffFromRotations.Count == 0 || casterAnimator == null) return;
        float w = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - handoffStart) / Mathf.Max(.01f, horseToFallBlendSeconds));
        if (w >= 1f) { handoffFromRotations.Clear(); return; }
        foreach (var p in handoffFromRotations)
            if (p.Key != null) p.Key.localRotation = Quaternion.Slerp(p.Value, p.Key.localRotation, w);
        Transform hips = casterAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null) hips.localPosition = Vector3.Lerp(handoffFromHips, hips.localPosition, w);
    }

    /// <summary>刀飛過去之後停的位置：頭的前上方、稍微偏右（舉的是右手）</summary>
    Vector3 SummonSwordHome()
    {
        Transform head = casterAnimator != null ? casterAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
        Vector3 from = head != null ? head.position : caster.position + caster.up * 1.6f;
        return from + caster.forward * summonSwordForward + caster.right * summonSwordSide + caster.up * summonSwordUp;
    }

    /// <summary>鏡頭 2 特寫要框住的點：臉和刀的中間</summary>
    bool TryGetSummonFocus(out Vector3 focus)
    {
        focus = default;
        if (Stage != "AerialRotor" || summonPose == null || !summonPose.Valid || weapon == null || casterAnimator == null) return false;
        Transform head = casterAnimator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return false;
        focus = Vector3.Lerp(head.position, weapon.position, .45f);
        return true;
    }

    /// <summary>轉刀：刀離開手、往前上方飛出去，停在空中旋轉；手指向刀、視線看著刀</summary>
    void ApplySummonedSword(float stageAge)
    {
        if (weapon == null || rightHand == null) return;
        float u = Mathf.Clamp01(stageAge / Mathf.Max(.1f, chargeOrbitSeconds));
        float spin = u * 360f * 2.75f;
        Vector3 home = SummonSwordHome();
        // 手臂：從動畫的「手直直朝天」轉向刀的方向（整隻手臂一起轉，手肘彎度照動畫）
        float armWeight = summonArmFollowSword * Mathf.SmoothStep(0f, 1f, stageAge / .5f);
        if (rightUpperArm != null && armWeight > 0f)
        {
            Vector3 armDir = rightHand.position - rightUpperArm.position, want = home - rightUpperArm.position;
            if (armDir.sqrMagnitude > .0001f && want.sqrMagnitude > .0001f)
                rightUpperArm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(armDir, want), armWeight) * rightUpperArm.rotation;
        }
        // 刀：從手掌飛到 home，之後在那裡上下浮動
        float fly = Mathf.SmoothStep(0f, 1f, stageAge / Mathf.Max(.1f, summonLiftSeconds));
        weapon.position = Vector3.Lerp(rightHand.position + caster.up * .12f, home, fly) + caster.up * (Mathf.Sin(stageAge * 2.3f) * .04f * fly);
        Vector3 blade = Vector3.Slerp((caster.forward * .35f + caster.up).normalized,
            Quaternion.AngleAxis(spin, caster.up) * caster.forward, Mathf.SmoothStep(0f, 1f, stageAge / .3f));
        AlignWeaponBlade(blade, caster.up);
        LookAtSword(weapon.position, Mathf.SmoothStep(0f, 1f, stageAge / .45f));
    }

    /// <summary>轉刀時頭（和一點點脖子）轉向刀；往上看最多抬到 lookAtSwordMaxElevation 度（再高就只看到那個角度）</summary>
    void LookAtSword(Vector3 point, float weight)
    {
        if (!headFaceKnown || casterAnimator == null || weight <= 0f) return;
        Transform head = casterAnimator.GetBoneTransform(HumanBodyBones.Head);
        Transform neck = casterAnimator.GetBoneTransform(HumanBodyBones.Neck);
        if (head == null) return;
        Vector3 up = caster.up;
        Vector3 to = point - head.position;
        Vector3 flat = Vector3.ProjectOnPlane(to, up);
        if (flat.sqrMagnitude < .0001f) flat = caster.forward;
        float elevation = Mathf.Min(Vector3.Angle(flat, to) * Mathf.Sign(Vector3.Dot(to, up)), lookAtSwordMaxElevation);
        float rad = elevation * Mathf.Deg2Rad; // 正 = 往上看
        Vector3 want = (flat.normalized * Mathf.Cos(rad) + up * Mathf.Sin(rad)).normalized;
        void Turn(Transform bone, float share)
        {
            if (bone == null || share <= 0f) return;
            Vector3 face = head.rotation * headFaceLocal;
            Quaternion full = Quaternion.FromToRotation(face, want);
            full.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -80f, 80f) * share * weight;
            if (axis.sqrMagnitude > .0001f) bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }
        Turn(neck, lookAtSwordNeck);
        Turn(head, 1f);
    }

    /// <summary>換階段時從上一幀的姿勢混過去（只在有 Tripo 姿勢時才做，不影響原本的程式姿勢）</summary>
    void BeginSkillPoseBlendIfStageChanged()
    {
        if (!HasSkillClipPoses || Stage == lastPoseStage) return;
        lastPoseStage = Stage;
        if (lastAppliedRotations.Count == 0) return;
        blendFromRotations.Clear();
        foreach (var p in lastAppliedRotations) blendFromRotations[p.Key] = p.Value;
        blendFromHips = lastAppliedHips;
        poseBlendStart = Time.unscaledTime;
    }

    /// <summary>每一幀姿勢都算完之後：混合換階段的過渡、記下這一幀的姿勢</summary>
    void FinishSkillPoseBlend()
    {
        if (!HasSkillClipPoses || casterAnimator == null) return;
        Transform hips = casterAnimator.GetBoneTransform(HumanBodyBones.Hips);
        float blendSeconds = Stage == "SheatheOrbit" && HasIaiPose ? iaiBlendSeconds : skillPoseBlendSeconds;
        float w = blendSeconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - poseBlendStart) / blendSeconds);
        if (w < 1f && blendFromRotations.Count > 0)
        {
            // 刀在技能期間掛在手上（PlayerWeaponPresenter.MaterializeInHand），骨頭混合時會自己跟著手動
            foreach (var p in blendFromRotations)
                if (p.Key != null) p.Key.localRotation = Quaternion.Slerp(p.Value, p.Key.localRotation, w);
            if (hips != null) hips.localPosition = Vector3.Lerp(blendFromHips, hips.localPosition, w);
        }
        lastAppliedRotations.Clear();
        for (HumanBodyBones id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++)
        {
            var b = casterAnimator.GetBoneTransform(id);
            if (b != null) lastAppliedRotations[b] = b.localRotation;
        }
        if (hips != null) lastAppliedHips = hips.localPosition;
    }
}
