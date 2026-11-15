using UnityEngine;

/// <summary>The authored cone mesh points +Y and its particle transform rotates -90 X: native -Z is the travel axis.</summary>
public static class DojoMeleeImpact
{
    public static CalligraphyPackageEffect Play(GameObject prefab,Vector3 at,Vector3 attackDirection)
    {
        var d=Vector3.ProjectOnPlane(attackDirection,Vector3.up).normalized;
        if(d.sqrMagnitude<.01f)return null;
        var effect=CalligraphyVfxLibrary.Play(prefab,at,Quaternion.LookRotation(-d),.65f,1.4f);
        if(effect)effect.StopAfter(.45f,.7f);
        return effect;
    }
}
