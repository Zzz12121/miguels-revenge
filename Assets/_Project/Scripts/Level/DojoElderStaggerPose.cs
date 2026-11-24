using System.Linq;
using UnityEngine;

/// <summary>Poses the Generic visual rig while the half-health cinematic owns it.</summary>
internal sealed class DojoElderStaggerPose
{
    readonly Animator animator;
    readonly CalligraphyBrushPresentation brush;
    readonly Transform body, hips, spine, chest, head, leftArm, rightArm;
    readonly Transform[] bones;
    readonly Quaternion[] rest;
    readonly Leg leftLeg, rightLeg;
    readonly Vector3 bodyPosition, bodyScale, brushScale, hipsPosition, origin, forward, right;
    readonly Vector3 brushOffset;
    readonly Quaternion brushRotation;
    readonly float travelScale;
    readonly Vector3 stepTarget;
    readonly bool animatorEnabled, brushEnabled;

    public Vector3 Forward => forward;
    public Vector3 Focus => chest ? chest.position : body.position + Vector3.up * 1.2f;
    public Vector3 InkOrigin => Focus + forward * .24f;

    public DojoElderStaggerPose(CalligraphyBrushPresentation brush, Transform root, float travelScale)
    {
        this.brush = brush;
        this.travelScale = travelScale;
        animator = brush.animator;
        body = animator.transform;
        animatorEnabled = animator.enabled;
        brushEnabled = brush.enabled;
        brush.enabled = false;
        animator.enabled = false;
        bodyPosition = body.localPosition;
        bodyScale = body.localScale;
        brushScale = brush.brushPivot.localScale;
        origin = body.position;
        forward = root.forward;
        right = root.right;

        var rig = body.GetComponentsInChildren<Transform>(true);
        Transform Bone(string name) => rig.FirstOrDefault(bone => bone.name == name);
        hips = Bone("Hips");
        spine = Bone("Spine");
        chest = Bone("Chest");
        head = Bone("Head");
        leftArm = brush.leftUpper;
        rightArm = brush.rightUpper;
        leftLeg = new Leg(Bone("Left_UpperLeg"), Bone("Left_LowerLeg"), Bone("Left_Foot"));
        rightLeg = new Leg(Bone("Right_UpperLeg"), Bone("Right_LowerLeg"), Bone("Right_Foot"));
        stepTarget = (forward * .60f + right * .14f) * travelScale;
        stepTarget += rightLeg.LandingHeight(stepTarget);
        bones = new[] { hips, spine, chest, head, leftArm, rightArm,
            leftLeg.Upper, leftLeg.Lower, leftLeg.Foot, rightLeg.Upper, rightLeg.Lower, rightLeg.Foot };
        rest = bones.Select(b => b ? b.localRotation : Quaternion.identity).ToArray();
        if (hips) hipsPosition = hips.localPosition;
        brushOffset = brush.rightHand.InverseTransformPoint(brush.brushPivot.position);
        brushRotation = Quaternion.Inverse(brush.rightHand.rotation) * brush.brushPivot.rotation;
    }

    static float Ease(float t, float start, float end) => Mathf.SmoothStep(0, 1, (t - start) / (end - start));

    public void Apply(float stagger, float pounce, bool speaking)
    {
        ResetBones();
        float step = Ease(stagger, .13f, .33f);
        float weightTransfer = Ease(stagger, .24f, .44f);
        float load = Ease(stagger, .73f, 1f);
        float launch = pounce * pounce;
        float sway = Mathf.Sin(stagger * 13f) * Ease(stagger, 0f, .16f) * (1f - load * .67f);
        float swing = Mathf.Sin(step * Mathf.PI);
        body.position = origin
            + forward * ((weightTransfer * .27f + launch * .66f) * travelScale)
            + right * (.078f * sway + .06f * weightTransfer)
            + Vector3.up * (.13f * Mathf.Sin(pounce * Mathf.PI) - .19f * launch);
        if (hips) hips.position += Vector3.down * (.07f * swing + .07f * weightTransfer + .13f * load * (1f - pounce));

        Turn(spine, right, 5f * weightTransfer + 9f * load + 23f * launch);
        Turn(spine, forward, 12f * sway - 6f * weightTransfer);
        Turn(chest, forward, 8f * sway);
        Turn(chest, right, 6f * load + 27f * launch);
        Turn(chest, Vector3.up, -10f * sway + 5f * load);
        Turn(head, forward, -9f * sway);
        Turn(head, right, -6f * load - 17f * launch + (speaking ? 2.6f * Mathf.Sin(stagger * 36f) : 0f));
        Turn(leftArm, forward, 19f * Mathf.Abs(sway) + 14f * weightTransfer);
        Turn(leftArm, right, -24f * weightTransfer - 51f * launch);
        Turn(rightArm, right, 14f * sway - 30f * launch);

        rightLeg.Place(stepTarget * step + Vector3.up * (.17f * swing + .09f * launch), forward, pounce * 16f);
        leftLeg.Place(forward * (.32f * launch * travelScale) + Vector3.up * (.23f * launch), forward, pounce * 37f);
        brush.brushPivot.SetPositionAndRotation(brush.rightHand.TransformPoint(brushOffset), brush.rightHand.rotation * brushRotation);
    }

    static void Turn(Transform bone, Vector3 axis, float angle)
    {
        if (bone) bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
    }

    void ResetBones()
    {
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) bones[i].localRotation = rest[i];
        if (hips) hips.localPosition = hipsPosition;
    }

    public void Restore()
    {
        if (!body) return;
        body.localPosition = bodyPosition;
        body.localScale = bodyScale;
        brush.brushPivot.localScale = brushScale;
        ResetBones();
        brush.brushPivot.SetPositionAndRotation(brush.rightHand.TransformPoint(brushOffset), brush.rightHand.rotation * brushRotation);
        brush.enabled = brushEnabled;
        animator.enabled = animatorEnabled;
    }

    readonly struct Leg
    {
        public readonly Transform Upper, Lower, Foot;
        readonly Vector3 start;
        readonly Quaternion rotation;

        public Leg(Transform upper, Transform lower, Transform foot)
        {
            Upper = upper;
            Lower = lower;
            Foot = foot;
            start = foot ? foot.position : Vector3.zero;
            rotation = foot ? foot.rotation : Quaternion.identity;
        }

        public Vector3 LandingHeight(Vector3 offset)
        {
            if (!Foot || offset.sqrMagnitude < .001f) return Vector3.zero;
            return Vector3.up * Mathf.Clamp(GroundHeight(start + offset) - GroundHeight(start), -.27f, .27f);
        }

        static float GroundHeight(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * .33f, Vector3.down, out var hit, .8f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.normal.y > .55f && hit.collider.GetComponentInParent<IDamageable>() == null)
                return hit.point.y;
            return at.y;
        }

        public void Place(Vector3 offset, Vector3 forward, float toeAngle)
        {
            if (!Upper || !Lower || !Foot) return;
            Vector3 hip = Upper.position;
            float thigh = Vector3.Distance(hip, Lower.position);
            float shin = Vector3.Distance(Lower.position, Foot.position);
            Vector3 delta = start + offset - hip;
            if (Mathf.Min(thigh, shin, delta.magnitude) < .01f) return;
            Vector3 direction = delta.normalized;
            float length = Mathf.Clamp(delta.magnitude, Mathf.Abs(thigh - shin) + .001f, thigh + shin - .001f);
            Vector3 bend = Vector3.ProjectOnPlane(forward, direction).normalized;
            float along = (thigh * thigh - shin * shin + length * length) / (2f * length);
            Vector3 knee = hip + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, thigh * thigh - along * along));
            Upper.rotation = Quaternion.FromToRotation(Lower.position - hip, knee - hip) * Upper.rotation;
            Lower.rotation = Quaternion.FromToRotation(Foot.position - Lower.position, hip + direction * length - Lower.position) * Lower.rotation;
            Foot.rotation = Quaternion.AngleAxis(toeAngle, Vector3.Cross(Vector3.up, forward)) * rotation;
        }
    }
}
