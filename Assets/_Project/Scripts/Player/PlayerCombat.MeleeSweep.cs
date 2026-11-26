using UnityEngine;

public partial class PlayerCombat
{
    readonly RaycastHit[] bladeSightHits = new RaycastHit[64];
    float lastSweepSound = float.NegativeInfinity;

    protected override bool UsesWeaponSweep
    {
        get
        {
            if (!hitWeapon) hitWeapon = GetComponentInChildren<PlayerWeaponPresenter>(true);
            return hitWeapon && hitWeapon.BladePivot && hitWeapon.BladeTip;
        }
    }

    protected override void BeginHitWindow(float damage)
    {
        swingDamage = damage;
        hitWindow = UsesWeaponSweep;
        sampledBlade = hitWindow;
        if (hitWindow)
        {
            previousBladeBase = hitWeapon.BladePivot.position;
            previousBladeTip = hitWeapon.BladeTip.position;
        }
    }

    protected override void EndHitWindow()
    {
        hitWindow = false;
        sampledBlade = false;
    }

    // The late sweep driver runs after the procedural pose and weapon presenter.
    internal void SampleWeapon()
    {
        if (!hitWindow || !IsAttacking || !CanContinueAttack || Time.deltaTime <= 0f || !UsesWeaponSweep)
            return;
        Vector3 bladeBase = hitWeapon.BladePivot.position;
        Vector3 bladeTip = hitWeapon.BladeTip.position;
        if (!sampledBlade)
        {
            previousBladeBase = bladeBase;
            previousBladeTip = bladeTip;
            sampledBlade = true;
        }
        float travel = Mathf.Max(Vector3.Distance(previousBladeBase, bladeBase), Vector3.Distance(previousBladeTip, bladeTip));
        int samples = Mathf.Clamp(Mathf.CeilToInt(travel / Mathf.Max(.03f, bladeRadius)), 1, 16);
        for (int i = 0; i <= samples; i++)
        {
            float fraction = (float)i / samples;
            SweepBlade(Vector3.Lerp(previousBladeBase, bladeBase, fraction), Vector3.Lerp(previousBladeTip, bladeTip, fraction));
        }
        previousBladeBase = bladeBase;
        previousBladeTip = bladeTip;
        if (Phase != AttackPhase.Active) EndHitWindow();
    }

    bool BladeContactVisible(Collider targetCollider, Vector3 point)
    {
        Vector3 origin = transform.position + Vector3.up * .65f;
        Vector3 ray = point - origin;
        int count = Physics.RaycastNonAlloc(origin, ray.normalized, bladeSightHits, ray.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = bladeSightHits[i];
            if (hit.collider == targetCollider || hit.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<IDamageable>() != null || !CalligraphyWorldIsolation.Allows(hit.transform)) continue;
            return false;
        }
        return true;
    }

    void SweepBlade(Vector3 from, Vector3 to)
    {
        int count = Physics.OverlapCapsuleNonAlloc(from, to, bladeRadius, bladeOverlap, ~0, QueryTriggerInteraction.Collide);
        // Saturation is exceptional, but still must not silently drop targets behind scenery colliders.
        Collider[] candidates = count < bladeOverlap.Length ? bladeOverlap :
            Physics.OverlapCapsule(from, to, bladeRadius, ~0, QueryTriggerInteraction.Collide);
        if (candidates != bladeOverlap) count = candidates.Length;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = candidates[i];
            if (!candidate || candidate.transform.IsChildOf(transform) || candidate.GetComponentInParent<IDamageable>() == null) continue;
            Vector3 center = candidate.bounds.center;
            Vector3 blade = to - from;
            float fraction = blade.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(center - from, blade) / blade.sqrMagnitude) : 0f;
            Vector3 nearest = from + blade * fraction;
            Vector3 point = candidate is MeshCollider mesh && !mesh.convex ? candidate.bounds.ClosestPoint(nearest) : candidate.ClosestPoint(nearest);
            float reach = hitForwardOffset + hitRadius;
            if (Vector3.ProjectOnPlane(point - transform.position, Vector3.up).sqrMagnitude > reach * reach) continue;
            if (!BladeContactVisible(candidate, point) || !TryDamage(candidate, swingDamage, point, hitThisSwing)) continue;
            if (Time.time - lastSweepSound < .05f) continue;
            lastSweepSound = Time.time;
            GameAudio.PlayAt(GameAudio.Pick(hitSound, "SFX/RPG/chop"), point, soundVolume * .7f,
                soundPitch * Random.Range(.95f, 1.05f), 1f, 30f);
        }
    }
}
