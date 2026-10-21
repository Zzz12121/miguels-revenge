using System.Collections.Generic;
using UnityEngine;

/// <summary>Temporary tether ownership, swept gravity and one small landing rebound.
/// Restores original controllers/body settings on landing or interruption.</summary>
[DisallowMultipleComponent]
public sealed class TetherThrownMotion : MonoBehaviour
{
    readonly Dictionary<Behaviour, bool> suspended = new();
    readonly Dictionary<CharacterController, bool> controllers = new();
    Rigidbody body;
    bool originalKinematic, originalGravity, ownsMotion, flying, bounce;
    Vector3 velocity;
    float radius = .3f, bottomOffset, elapsed;
    public bool IsActive => ownsMotion;
    public void Interrupt() => Finish();
    public void Hold()
    {
        enabled=true;
        if (ownsMotion) { flying = false; velocity = Vector3.zero; return; }
        ownsMotion = true; flying = false; elapsed = 0;
        body = GetComponent<Rigidbody>();
        if (body) { originalKinematic = body.isKinematic; originalGravity = body.useGravity;
            if (!body.isKinematic) { body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero; }
            body.isKinematic=true; body.useGravity=false; }
        bool found = false; Bounds bounds = default;
        foreach (var c in GetComponentsInChildren<Collider>())
            if (c.enabled && !c.isTrigger) { if (!found) { bounds=c.bounds; found=true; } else bounds.Encapsulate(c.bounds); }
        bottomOffset = found ? transform.position.y - bounds.min.y : 0;
        if (found) radius = Mathf.Clamp(Mathf.Min(bounds.extents.x,bounds.extents.z),.12f,.5f);
        foreach (var c in GetComponentsInChildren<CharacterController>()) { controllers[c]=c.enabled; c.enabled=false; }
        foreach (var b in GetComponentsInChildren<Behaviour>())
        {
            if (!(b is ThirdPersonController) && !(b is EnemyAI) && !(b is EnemyCombat) &&
                !(b is VoidRendMovingTarget) && !(b is CalligraphyBrushPresentation) && !(b is Animator) &&
                !(b is BossController) && !(b is ClockEnemyController)) continue;
            suspended[b]=b.enabled;
            if (b is EnemyCombat combat) { combat.CancelAttack(); combat.StopAllCoroutines(); }
            if (b is BossController boss) boss.SetSkillAirborne(true);
            if (b is ClockEnemyController clock) clock.SetSkillAirborne(true);
            b.enabled=false;
        }
    }
    public void Release(Vector3 launchVelocity, bool rebound)
    {
        if (!ownsMotion) Hold();
        velocity=launchVelocity; bounce=rebound; flying=true; elapsed=0;
    }
    void FixedUpdate()
    {
        if (!ownsMotion || !flying) return;
        float dt=Time.fixedDeltaTime; elapsed+=dt;
        velocity+=Physics.gravity*dt;
        Vector3 step=velocity*dt;
        Vector3 origin=transform.position + Vector3.up*(radius-bottomOffset+.035f);
        var hits=Physics.SphereCastAll(origin,radius,step.normalized,step.magnitude+.04f,~0,QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            transform.position+=step.normalized*Mathf.Max(0,hit.distance-.04f);
            if (hit.normal.y>.45f && velocity.y<=0)
            {
                if (bounce)
                {
                    bounce=false;
                    velocity=Vector3.ProjectOnPlane(velocity,Vector3.up)*.12f+Vector3.up*Mathf.Clamp(Mathf.Abs(velocity.y)*.12f,1.6f,3.2f);
                    ThirdPersonCamera.ShakeMain(.15f,.12f);
                }
                else Finish();
            }
            else velocity=Vector3.ProjectOnPlane(velocity,hit.normal)*.5f;
            return;
        }
        transform.position+=step;
        if (elapsed>20f) Finish();
    }
    void Finish()
    {
        if (!ownsMotion) return;
        ownsMotion=flying=false;
        if (body) { body.isKinematic=originalKinematic; body.useGravity=originalGravity;
            if (!body.isKinematic) body.linearVelocity=Vector3.zero; }
        foreach (var pair in suspended)
        {
            if (!pair.Key) continue;
            bool dead=GetComponent<IDamageable>()?.IsDead ?? false;
            if (!dead) pair.Key.enabled=pair.Value;
            if (pair.Key is VoidRendMovingTarget mover) mover.RebaseAfterTether();
            if (pair.Key is BossController boss) boss.SetSkillAirborne(false);
            if (pair.Key is ClockEnemyController clock) clock.SetSkillAirborne(false);
        }
        suspended.Clear();
        foreach (var pair in controllers) if (pair.Key) pair.Key.enabled=pair.Value;
        controllers.Clear();
    }
    void OnDisable() => Finish();
}
