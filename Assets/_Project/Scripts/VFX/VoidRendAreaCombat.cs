using System.Collections.Generic;
using UnityEngine;

public sealed partial class VoidRendSkillDemo
{
    [Header("Void Rend area combat")]
    [Min(.1f)] public float launchRadius = 4.5f;
    [Min(0f)] public float launchDamage = 100f;
    [Min(.1f)] public float dashDamageRadius = 1.6f;
    [Min(.1f)] public float explosionRadius = 12f;

    sealed class LiftedVictim
    {
        public Transform root;
        public Vector3 home;
        public Quaternion rotation;
        public IDamageable damageable;
        public readonly Dictionary<Behaviour, bool> behaviours = new();
        public readonly Dictionary<CharacterController, bool> controllers = new();
        public Rigidbody body;
        public bool kinematic, gravity;
        public Vector3 velocity, angularVelocity;
    }
    readonly List<LiftedVictim> liftedVictims = new();
    readonly HashSet<IDamageable> hitThisCut = new();
    PlayerHealth protectedCaster;

    void SetWindupInvulnerability(bool value)
    {
        if (value && caster) protectedCaster = caster.GetComponent<PlayerHealth>();
        if (protectedCaster) protectedCaster.SetSkillInvulnerability(this, value);
        if (!value) protectedCaster = null;
    }

    Transform CombatRoot(Collider collider)
    {
        if (!collider || (caster && collider.transform.IsChildOf(caster))) return null;
        if (collider.GetComponentInParent<PlayerHealth>()) return null;
        var damageable = collider.GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.IsDead) return null;
        if (damageable is Component owner) return owner.transform;
        var marker = collider.GetComponentInParent<VoidRendTargetable>();
        return marker ? marker.transform : null;
    }

    void HoldVictim(Transform root)
    {
        if (!root || liftedVictims.Exists(v => v.root == root)) return;
        // The new lift takes over from a previous tether throw, never two motion owners at once.
        root.GetComponent<TetherThrownMotion>()?.Interrupt();
        var v = new LiftedVictim { root = root, home = root.position, rotation = root.rotation,
            damageable = root.GetComponent<IDamageable>(), body = root.GetComponent<Rigidbody>() };
        if (v.damageable != null && v.damageable.IsDead) return;
        liftedVictims.Add(v);
        if (v.body)
        {
            v.kinematic = v.body.isKinematic; v.gravity = v.body.useGravity;
            v.velocity = v.body.linearVelocity; v.angularVelocity = v.body.angularVelocity;
            v.body.isKinematic = true; v.body.useGravity = false;
        }
        foreach (var c in root.GetComponentsInChildren<CharacterController>(true))
        { v.controllers[c] = c.enabled; c.enabled = false; }
        foreach (var c in root.GetComponentsInChildren<Behaviour>(true)) HoldBehaviour(v, c);
        if (root == target) foreach (var c in targetControllers) HoldBehaviour(v, c, true);
    }

    void HoldBehaviour(LiftedVictim victim, Behaviour component, bool explicitOptIn = false)
    {
        if (!component || component == this || victim.behaviours.ContainsKey(component)) return;
        if (!explicitOptIn && !ShouldSuspendVictimBehaviour(component) &&
            !(component is VoidRendMovingTarget) && !(component is CalligraphyBrushPresentation) &&
            !(component is CalligraphyBossCombat) && !(component is CalligraphyClone)) return;
        victim.behaviours[component] = component.enabled;
        // Disabling a MonoBehaviour alone does NOT cancel its attack coroutines.
        if (component is EnemyCombat combat) { combat.CancelAttack(); combat.StopAllCoroutines(); }
        if (component is BossController boss) boss.SetSkillAirborne(true);
        if (component is ClockEnemyController clock) clock.SetSkillAirborne(true);
        if (component is ThirdPersonController movement) { movement.SetAIInput(Vector3.zero); movement.ResetMotion(); }
        component.enabled = false;
    }

    void LaunchNearbyVictims()
    {
        Physics.SyncTransforms();
        // Centre at torso height, but retain each victim's own floor height and spacing.
        foreach (var collider in Physics.OverlapSphere(victimHome + Vector3.up, launchRadius, ~0, QueryTriggerInteraction.Collide))
            HoldVictim(CombatRoot(collider));
        hitThisCut.Clear();
        foreach (var v in liftedVictims) DamageOnce(v.damageable, launchDamage, v.home + Vector3.up);
    }

    void UpdateLiftedVictims()
    {
        if (!targetLocked) return;
        Vector3 offset = victimHeld - victimHome;
        foreach (var v in liftedVictims)
            if (v.root) v.root.SetPositionAndRotation(v.home + offset, v.rotation);
    }

    void ReleaseLiftedVictims(bool cancelled)
    {
        foreach (var v in liftedVictims)
        {
            if (v.root) v.root.SetPositionAndRotation(v.home, v.rotation);
            bool dead = v.damageable is Component owner && owner && v.damageable.IsDead;
            if (v.body)
            {
                v.body.isKinematic = v.kinematic; v.body.useGravity = v.gravity;
                if (!v.kinematic) { v.body.linearVelocity = cancelled ? v.velocity : Vector3.zero; v.body.angularVelocity = cancelled ? v.angularVelocity : Vector3.zero; }
            }
            foreach (var pair in v.controllers) if (pair.Key && !dead) pair.Key.enabled = pair.Value;
            foreach (var pair in v.behaviours)
            {
                if (!pair.Key) continue;
                // Never resurrect dead AI after restoring the suspension snapshot.
                if (!dead || pair.Key is Animator) pair.Key.enabled = pair.Value;
                if (pair.Key is BossController boss) boss.SetSkillAirborne(false);
                if (pair.Key is ClockEnemyController clock) clock.SetSkillAirborne(false);
            }
        }
        liftedVictims.Clear();
    }

    void DamageOnce(IDamageable damageable, float amount, Vector3 point)
    {
        if (damageable == null || !(damageable is Component owner) || !owner || damageable.IsDead || !hitThisCut.Add(damageable)) return;
        damageable.TakeDamage(amount, point);
    }
    void DamageColliders(Collider[] colliders, float damage, Vector3 hit)
    {
        foreach (var c in colliders)
        {
            Transform root = CombatRoot(c);
            if (root) DamageOnce(root.GetComponent<IDamageable>(), damage, hit);
        }
    }
    void DamageDashArea(Vector3 from, Vector3 to)
    {
        Physics.SyncTransforms(); hitThisCut.Clear();
        foreach (var v in liftedVictims) if (v.root) DamageOnce(v.damageable, voidRendDashDamage, v.root.position + Vector3.up);
        DamageColliders(Physics.OverlapCapsule(from, to, dashDamageRadius, ~0, QueryTriggerInteraction.Collide), voidRendDashDamage, to);
        // The lattice cuts through a vertical volume, including enemies underneath its aerial tracks.
        from.y = to.y = arenaCenter.y + .9f;
        DamageColliders(Physics.OverlapCapsule(from, to, dashDamageRadius, ~0, QueryTriggerInteraction.Collide), voidRendDashDamage, to);
    }
    void DamageExplosion(Vector3 center)
    {
        Physics.SyncTransforms(); hitThisCut.Clear();
        DamageColliders(Physics.OverlapSphere(center, explosionRadius, ~0, QueryTriggerInteraction.Collide), voidRendDamage, center);
        // CharacterControllers are disabled while held, so use the held positions too.
        foreach (var v in liftedVictims)
            if (v.root && Vector3.Distance(v.root.position, center) <= explosionRadius)
                DamageOnce(v.damageable, voidRendDamage, center);
    }
}
