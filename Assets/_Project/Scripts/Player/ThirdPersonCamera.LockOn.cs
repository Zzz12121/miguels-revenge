using System.Collections.Generic;
using UnityEngine;

public partial class ThirdPersonCamera
{
    [Header("戰鬥鎖定（L 切換，J 換目標，可於設定改鍵）")]
    public bool lockOnEnabled = true;
    [Min(1f)] public float lockAcquireRange = 22f;
    [Min(1f)] public float lockRetainRange = 28f;
    [Min(0f)] public float lockOcclusionGrace = .9f;
    [Min(1f)] public float lockTurnSpeed = 240f;
    [Range(5f, 70f)] public float lockMaxPitch = 35f;
    Component lockOwner;
    IDamageable lockedDamageable;
    Vector3 lockLocalAnchor;
    float lockBodyRadius, lockOccludedSince = -1f, lockFrameDistance, lockPivotHeight, lockPivotVelocity;
    readonly Collider[] lockCandidates = new Collider[128];
    readonly HashSet<IDamageable> seenLockTargets = new HashSet<IDamageable>();
    readonly RaycastHit[] lockSightHits = new RaycastHit[64];
    public Transform LockedTarget => lockOwner ? lockOwner.transform : null;
    float LockFramingDistance => lockFrameDistance;
    Vector3 LockFramingOffset => Vector3.up * lockPivotHeight;

    public void ClearLock()
    {
        lockOwner = null;
        lockedDamageable = null;
        lockOccludedSince = -1f;
    }

    [Tooltip("在鎖定的身體注視點顯示小型記號")]
    public bool showLockIndicator = true;

    void OnGUI()
    {
        if (!showLockIndicator || !LockedTarget || GameUI.OverlayOpen || !Camera.main) return;
        Vector3 screen = Camera.main.WorldToScreenPoint(lockOwner.transform.TransformPoint(lockLocalAnchor));
        if (screen.z <= 0f) return;
        var point = new Vector2(screen.x, Screen.height - screen.y);
        var oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        GUI.color = new Color(.95f, .93f, .85f, .85f);
        GUIUtility.RotateAroundPivot(45f, point);
        GUI.DrawTexture(new Rect(point.x - 3f, point.y - 3f, 6f, 6f), Texture2D.whiteTexture);
        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    void HandleLockInput()
    {
        if (!lockOnEnabled || !target || aiming || SkillAimCancellation.IsAiming ||
            Cursor.lockState != CursorLockMode.Locked || GameUI.OverlayOpen || Time.timeScale <= 0f) return;
        if (InputBindings.Pressed(GameAction.LockOn))
        {
            if (LockedTarget) ClearLock();
            else AcquireLock(false);
        }
        if (LockedTarget && InputBindings.Pressed(GameAction.SwitchTarget)) AcquireLock(true);
    }

    void AcquireLock(bool switching)
    {
        int count = Physics.OverlapSphereNonAlloc(target.position, lockAcquireRange,
            lockCandidates, ~0, QueryTriggerInteraction.Collide);
        Collider[] candidates = count < lockCandidates.Length ? lockCandidates :
            Physics.OverlapSphere(target.position, lockAcquireRange, ~0, QueryTriggerInteraction.Collide);
        if (candidates != lockCandidates) count = candidates.Length;
        Component best = null;
        Collider bestCollider = null;
        float bestScore = float.MaxValue;
        seenLockTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            var collider = candidates[i];
            var damageable = collider.GetComponentInParent<IDamageable>();
            var owner = damageable as Component;
            if (!owner || damageable.IsDead || owner.transform.IsChildOf(target) ||
                damageable is IImpactSurface && !(damageable is BossHealth) && !(damageable is EnemyHealth) ||
                !CalligraphyWorldIsolation.Allows(owner.transform) || !seenLockTargets.Add(damageable) ||
                switching && owner == lockOwner) continue;
            Vector3 direction = collider.bounds.center - transform.position;
            float alignment = Vector3.Dot(transform.forward, direction.normalized);
            if (alignment < .3f || !LockVisible(owner, collider.bounds.center)) continue;
            float score = (1f - alignment) * 14f + Vector3.Distance(target.position, owner.transform.position) * .15f;
            if (switching && LockedTarget)
            {
                Vector3 currentDirection = LockedTarget.position - target.position;
                Vector3 candidateDirection = owner.transform.position - target.position;
                score = Mathf.Repeat(Vector3.SignedAngle(currentDirection, candidateDirection, Vector3.up), 360f);
            }
            if (score >= bestScore) continue;
            bestScore = score;
            best = owner;
            bestCollider = collider;
        }
        if (!best) return;
        lockOwner = best;
        lockedDamageable = best.GetComponent<IDamageable>();
        // Cache a root-relative body anchor instead of following animated hands or heads.
        Bounds bounds = bestCollider.bounds;
        lockLocalAnchor = best.transform.InverseTransformPoint(bounds.center);
        lockBodyRadius = Mathf.Clamp(Mathf.Max(bounds.extents.y, bounds.extents.x), .6f, 5f);
        lockOccludedSince = -1f;
    }

    bool LockVisible(Component owner, Vector3 point)
    {
        Vector3 origin = target.position + Vector3.up * 1.1f;
        Vector3 ray = point - origin;
        int count = Physics.RaycastNonAlloc(origin, ray.normalized, lockSightHits, ray.magnitude,
            collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = lockSightHits[i];
            if (hit.transform.IsChildOf(target) || hit.transform.IsChildOf(owner.transform) ||
                hit.collider.GetComponentInParent<IDamageable>() != null || !CalligraphyWorldIsolation.Allows(hit.transform)) continue;
            return false;
        }
        return true;
    }

    void UpdateLockOn(float deltaTime)
    {
        if (!LockedTarget)
        {
            lockFrameDistance = Mathf.MoveTowards(lockFrameDistance, 0f, deltaTime * 4f);
            lockPivotHeight = Mathf.SmoothDamp(lockPivotHeight, 0f, ref lockPivotVelocity, .18f, Mathf.Infinity, deltaTime);
            return;
        }
        if (!lockOnEnabled || lockedDamageable == null || lockedDamageable.IsDead || !lockOwner.gameObject.activeInHierarchy ||
            !target || Vector3.Distance(target.position, lockOwner.transform.position) > lockRetainRange ||
            targetMotor && (!targetMotor.inputEnabled || targetMotor.ActionsLocked))
        {
            ClearLock();
            return;
        }
        Vector3 point = lockOwner.transform.TransformPoint(lockLocalAnchor);
        if (LockVisible(lockOwner, point)) lockOccludedSince = -1f;
        else
        {
            if (lockOccludedSince < 0f) lockOccludedSince = Time.unscaledTime;
            if (Time.unscaledTime - lockOccludedSince > lockOcclusionGrace) { ClearLock(); return; }
        }
        float height = Mathf.Max(0f, point.y - target.position.y - FramedOffset().y);
        lockPivotHeight = Mathf.SmoothDamp(lockPivotHeight, Mathf.Min(6f, height * .4f), ref lockPivotVelocity,
            .15f, Mathf.Infinity, deltaTime);
        Vector3 direction = point - (target.position + FramedOffset() + LockFramingOffset);
        float horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).magnitude;
        float wantedFrame = Mathf.Clamp(distance + lockBodyRadius * .45f + Mathf.Min(4f, height * .25f), minDistance, maxDistance);
        lockFrameDistance = Mathf.MoveTowards(Mathf.Max(distance, lockFrameDistance), wantedFrame, deltaTime * 3f);
        if (aiming || SkillAimCancellation.IsAiming || GameUI.OverlayOpen || Time.timeScale <= 0f ||
            Time.unscaledTime - lastManualLook < .6f) return;
        // Passing underneath the boss must not force an instantaneous half turn.
        if (horizontal > 1.2f)
        {
            float desiredYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, lockTurnSpeed * deltaTime);
        }
        float desiredPitch = -Mathf.Atan2(direction.y, Mathf.Max(2.5f, horizontal)) * Mathf.Rad2Deg;
        pitch = Mathf.MoveTowards(pitch, Mathf.Clamp(desiredPitch, Mathf.Max(minPitch, -lockMaxPitch),
            Mathf.Min(maxPitch, lockMaxPitch)), lockTurnSpeed * .4f * deltaTime);
    }
}
