using System.Collections.Generic;
using UnityEngine;

public sealed partial class VoidRendSkillDemo
{
    [Header("Skill 1 / left-waist draw dash")]
    [Min(1f)] public float drawDashSpeed = 48f;
    [Range(.06f, .3f)] public float drawBlendSeconds = .14f;
    [Range(10f, 50f)] public float drawForwardLean = 32f;
    [Range(.015f, .12f)] public float drawGhostInterval = .022f;

    [Header("Skill 1 / 開場：固定特寫鏡頭（鏡頭 A）蹲低 → 爆衝")]
    [Tooltip("轉向目標之後，蹲低準備衝刺停多久（秒）")]
    [Range(.2f, 2f)] public float openingCrouchSeconds = .75f;
    [Tooltip("鏡頭 A 的位置（公尺，以角色腳底為準）：x 負 = 左邊、y = 離地高度（放在腳旁邊、幾乎貼地）、z 負 = 後面")]
    public Vector3 openingCameraGroundLevel = new Vector3(-.5f, .12f, -1.15f);
    [Tooltip("鏡頭 A 看的位置：0 = 看角色胸口、1 = 看目標")]
    [Range(0f, 1f)] public float openingLookTowardTarget = .55f;
    [Range(30f, 90f)] public float openingCameraFov = 60f;
    [Tooltip("蹲低的時候鏡頭慢慢往前推近多少（公尺）")]
    [Range(0f, 1f)] public float openingCameraPushIn = .3f;
    [Tooltip("爆衝最短 / 最長花幾秒（太短看不清楚、太長沒有速度感）")]
    public Vector2 openingDashSeconds = new Vector2(.26f, .6f);
    [Tooltip("殘影顏色（跟空中側閃同一種殘影）與存活秒數")]
    public Color drawGhostColor = new Color(.55f, .8f, 1f, .45f);
    [Range(.1f, 1f)] public float drawGhostLife = .38f;

    [Header("Skill 1 / 衝到目標後：鏡頭 2 先特寫刀和表情，再慢慢拉遠")]
    [Tooltip("特寫時鏡頭離角色多遠（公尺）")]
    [Range(1.2f, 5f)] public float spinCloseUpDistance = 2.3f;
    [Tooltip("特寫時看的高度（公尺，介於臉和頭頂的刀之間）")]
    [Range(1f, 2.5f)] public float spinCloseUpFocusHeight = 1.75f;
    [Tooltip("特寫鏡頭在角色正面偏右幾度")]
    [Range(-90f, 90f)] public float spinCloseUpAngle = 35f;
    [Range(25f, 70f)] public float spinCloseUpFov = 45f;
    [Tooltip("轉刀時間的前多少比例維持特寫，之後才開始拉遠")]
    [Range(0f, .8f)] public float spinCloseUpHold = .2f;
    SkinnedMeshRenderer[] drawGhostRenderers;
    readonly Dictionary<Transform, HumanoidBonePose> drawEntryPose = new();
    float nextDrawGhostTime;
    Vector3 drawEntryGrip;
    Quaternion drawEntryWeaponRotation;

    bool IsDrawDashStage() => Stage == "DrawPrepare" || Stage == "ApproachRun" || Stage == "Launch";

    void CaptureDrawEntryPose()
    {
        drawEntryPose.Clear();
        if (casterAnimator != null)
            foreach (Transform bone in casterAnimator.GetComponentsInChildren<Transform>())
                drawEntryPose[bone] = new HumanoidBonePose { localPosition = bone.localPosition, localRotation = bone.localRotation };
        drawEntryGrip = caster.InverseTransformPoint(rightHand ? rightHand.position : caster.position + Vector3.up);
        drawEntryWeaponRotation = weapon ? Quaternion.Inverse(caster.rotation) * weapon.rotation : Quaternion.identity;
    }

    // Work in character/world directions rather than assuming imported bone Euler axes.
    void ApplyDrawDashPose()
    {
        if (!pelvis || !rightHand || !weapon) return;
        float age = FinisherClock - stageTime;
        float cut = Stage == "Launch" ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .23f)) : 0f;
        float settle = Stage == "Launch" ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - .20f) / .15f)) : 0f;
        float blend = Stage == "DrawPrepare" ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / Mathf.Max(.01f, drawBlendSeconds))) : 1f;
        float legLength = leftThigh && leftCalf && leftFoot
            ? Vector3.Distance(leftThigh.position, leftCalf.position) + Vector3.Distance(leftCalf.position, leftFoot.position) : .85f;
        float scale = legLength / .85f;
        Vector3 up = caster.up, forward = caster.forward, right = caster.right;
        Vector3 leftRest = leftFoot ? leftFoot.position : caster.position;
        Vector3 rightRest = rightFoot ? rightFoot.position : caster.position;
        Vector3 waist = pelvis.position;
        // 開場蹲低時慢慢再壓低一點、身體再往前傾一點（蓄力的感覺，不會整段僵住）；衝出去之後維持壓到最低的姿勢
        float coil = Stage == "DrawPrepare" ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / Mathf.Max(.01f, openingCrouchSeconds))) : 1f;
        pelvis.position -= up * ((.15f + .07f * coil) * scale * (1f - cut));
        RotateBoneWorld(spine, right, Mathf.Lerp(drawForwardLean + 8f * coil, -5f, cut));
        RotateBoneWorld(spine, up, Mathf.Lerp(-12f, 14f, cut));

        // Left knee drives forward with the calf folded back; right leg trails almost straight.
        // Extend the leading foot to plant it during the upward draw, then settle into standing.
        Vector3 leftGoal = leftRest + (forward * Mathf.Lerp(.32f, .40f, cut) + up * (.28f * (1f - cut))) * scale;
        Vector3 rightGoal = rightRest - forward * (.46f * scale);
        SolveDrawLimb(leftThigh, leftCalf, leftFoot, Vector3.Lerp(leftGoal, leftRest, settle), forward);
        SolveDrawLimb(rightThigh, rightCalf, rightFoot, Vector3.Lerp(rightGoal, rightRest, settle), forward);

        Vector3 lowGrip = waist + (-right * .23f + up * .06f + forward * .12f) * scale;
        Vector3 highGrip = waist + (right * .16f + up * .92f + forward * .30f) * scale;
        Vector3 grip = Vector3.Lerp(lowGrip, highGrip, cut) + forward * (Mathf.Sin(cut * Mathf.PI) * .30f * scale);
        // Elbow remains on the right; forearm crosses the abdomen to the left-waist grip.
        SolveDrawLimb(rightUpperArm, rightForearm, rightHand, grip, right + forward * .25f - up * .12f);
        Vector3 leftGoalHand = waist + (-right * .34f + up * .12f - forward * .20f) * scale;
        SolveDrawLimb(leftUpper, leftFore, leftHand, leftGoalHand, -right - forward * .3f);

        if (blend < 1f)
            foreach (var pair in drawEntryPose)
            {
                if (!pair.Key || pair.Key == casterAnimator.transform) continue;
                pair.Key.localPosition = Vector3.Lerp(pair.Value.localPosition, pair.Key.localPosition, blend);
                pair.Key.localRotation = Quaternion.Slerp(pair.Value.localRotation, pair.Key.localRotation, blend);
            }
        weapon.position = Vector3.Lerp(caster.TransformPoint(drawEntryGrip), rightHand.position, blend);
        // A continuous arc from behind the hip, through forward, to above the head.
        // Unlike Slerp between opposite vectors this has an explicit, stable cutting plane.
        float angle = Mathf.Lerp(-180f, 70f, cut) * Mathf.Deg2Rad;
        Vector3 bladeDirection = forward * Mathf.Cos(angle) + up * Mathf.Sin(angle);
        AlignWeaponBlade(bladeDirection, right);
        if (blend < 1f) weapon.rotation = Quaternion.Slerp(caster.rotation * drawEntryWeaponRotation, weapon.rotation, blend);
    }

    static void SolveDrawLimb(Transform upper, Transform lower, Transform end, Vector3 goal, Vector3 pole)
    {
        if (!upper || !lower || !end) return;
        Vector3 root = upper.position;
        float a = Vector3.Distance(root, lower.position), b = Vector3.Distance(lower.position, end.position);
        if (a < .001f || b < .001f || (goal - root).sqrMagnitude < .000001f) return;
        Vector3 direction = (goal - root).normalized;
        float distance = Mathf.Clamp(Vector3.Distance(root, goal), Mathf.Abs(a - b) + .001f, a + b - .001f);
        float x = (a * a - b * b + distance * distance) / (2f * distance);
        Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.Cross(direction, Vector3.right).normalized;
        Vector3 joint = root + direction * x + bend * Mathf.Sqrt(Mathf.Max(0, a * a - x * x));
        upper.rotation = Quaternion.FromToRotation(lower.position - root, joint - root) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, root + direction * distance - lower.position) * lower.rotation;
    }

    void UpdateDrawDashAfterimages()
    {
        if (!playing || Stage != "ApproachRun" || FinisherClock < nextDrawGhostTime) return;
        nextDrawGhostTime = FinisherClock + drawGhostInterval;
        // 跟空中側閃同一種殘影（AfterimageGhost），密一點、留久一點，鏡頭固定時才看得出一路衝過去
        if (drawGhostRenderers == null || drawGhostRenderers.Length == 0)
            drawGhostRenderers = caster.GetComponentsInChildren<SkinnedMeshRenderer>();
        AfterimageGhost.Spawn(drawGhostRenderers, drawGhostColor, drawGhostLife);
        // Small parallel, broken arcs follow the silhouette, never a solid white screen flash.
        for (int i = 0; i < 4; i++)
        {
            Vector3 point = caster.position + caster.up * Random.Range(.35f, 1.45f) + caster.right * Random.Range(-.3f, .3f);
            Vector3 tail = point - caster.forward * Random.Range(.35f, 1.1f);
            var arc = SpawnRibbon(new[] { tail, Vector3.Lerp(tail, point, .35f) + caster.up * .04f,
                Vector3.Lerp(tail, point, .65f) - caster.right * .035f, point }, slashMaterial, .018f, .22f, new Color(.8f, .94f, 1f, .65f));
            if (arc) arc.widthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.35f, 1), new Keyframe(1, 0));
        }
    }
}
