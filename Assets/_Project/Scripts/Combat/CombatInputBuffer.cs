using UnityEngine;

/// <summary>A single replaceable command, measured on the scaled gameplay clock.</summary>
public struct CombatInputBuffer
{
    bool pending;
    float expiresAt;

    public bool Pending => pending && Time.time <= expiresAt;

    public void Queue(float seconds)
    {
        pending = true;
        expiresAt = Time.time + Mathf.Max(0f, seconds);
    }

    public void Clear() => pending = false;
}
