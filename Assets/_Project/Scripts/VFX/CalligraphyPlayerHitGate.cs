using UnityEngine;

/// <summary>Uses existing damage/defence, only throttles ordinary stagger from this boss.</summary>
public sealed class CalligraphyPlayerHitGate : MonoBehaviour
{
    public float minimumStunInterval=.35f;
    float nextStun;
    static readonly Collider[] sweepHits=new Collider[32];
    public static bool Touches(PlayerHealth player,Vector3 from,Vector3 to,float radius)
    {
        int n=Physics.OverlapCapsuleNonAlloc(from,to,radius,sweepHits,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<n;i++)if(sweepHits[i].GetComponentInParent<PlayerHealth>()==player)return true;
        return false;
    }
    public static void Hit(PlayerHealth player,float damage,Vector3 impulse,string reason)
    {
        if(!player)return;
        var gate=player.GetComponent<CalligraphyPlayerHitGate>()??player.gameObject.AddComponent<CalligraphyPlayerHitGate>();
        var controller=player.GetComponent<ThirdPersonController>();
        bool controlled=(controller && (controller.ActionsLocked || controller.IsStunned || controller.IsBallistic)) || (player.GetComponent<TetherThrownMotion>()?.IsActive??false);
        bool stun=!controlled && Time.time>=gate.nextStun;
        bool actual=false;System.Action<float> receipt=a=>actual|=a>0;
        player.OnDamaged+=receipt;
        try{player.TakeDamage(damage,impulse,false,reason,stun);}
        finally{player.OnDamaged-=receipt;}
        if(actual && stun)gate.nextStun=Time.time+gate.minimumStunInterval;
    }
}
