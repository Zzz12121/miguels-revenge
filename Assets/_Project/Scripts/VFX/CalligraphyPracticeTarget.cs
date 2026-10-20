using UnityEngine;

/// <summary>Range-only damage receiver. Deliberately has no enemy AI or outgoing attacks.</summary>
[DisallowMultipleComponent]
public sealed class CalligraphyPracticeTarget : MonoBehaviour, IDamageable
{
    public float DamageTaken { get; private set; }
    public bool IsDead => false; // Reusable practice dummy: survives a full skill and repeated tests.
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (amount <= 0) return;
        DamageTaken += amount;
        var targetable = GetComponent<VoidRendTargetable>();
        if (targetable) targetable.PlayHitReaction();
    }
    [ContextMenu("Reset damage counter")]
    public void ResetDamage() => DamageTaken = 0;
}
