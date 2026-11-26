using UnityEngine;

/// <summary>Samples the blade after the imported/procedural rig and loadout have posed it.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32500)]
public sealed class PlayerMeleeSweepDriver : MonoBehaviour
{
    PlayerCombat combat;
    void Awake() => combat = GetComponent<PlayerCombat>();
    void LateUpdate()
    {
        if (combat && combat.isActiveAndEnabled) combat.SampleWeapon();
    }
}
