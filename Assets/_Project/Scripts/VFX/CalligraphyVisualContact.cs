using UnityEngine;

/// <summary>Optional visual observers only. No damage, targeting, collision or timing ownership.</summary>
public static class CalligraphyVisualContact
{
    public static event System.Action<Transform,Vector3,Vector3,float,Vector3> Swept;
    public static bool Observed=>Swept!=null;
    public static void Publish(Transform owner,Vector3 from,Vector3 to,float radius,Vector3 velocity)
        =>Swept?.Invoke(owner,from,to,radius,velocity);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()=>Swept=null;
}
