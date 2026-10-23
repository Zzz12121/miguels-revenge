using UnityEngine;

/// <summary>Uses existing damage/defence, only throttles ordinary stagger from this boss.</summary>
public sealed class CalligraphyPlayerHitGate : MonoBehaviour
{
    public static bool DeliveringPaintingDamage {get;private set;}
    public float minimumStunInterval=.35f;
    float nextStun;
    int hitSequence;
    static readonly Collider[] sweepHits=new Collider[32];
    public static bool Touches(PlayerHealth player,Vector3 from,Vector3 to,float radius)
    {
        int n=Physics.OverlapCapsuleNonAlloc(from,to,radius,sweepHits,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<n;i++)if(sweepHits[i].GetComponentInParent<PlayerHealth>()==player)return true;
        return false;
    }
    public static bool Hit(PlayerHealth player,float damage,Vector3 impulse,string reason,Vector3? contact=null,
        CalligraphyPlayerControl.Reaction reaction=CalligraphyPlayerControl.Reaction.None,float controlSeconds=1,Vector2? launch=null,Transform source=null)
    {
        if(!player || player.IsDead)return false;
        if(CalligraphyPaintingWorld.BlocksTether && (!source || !CalligraphyWorldIsolation.Allows(source)))return false;
        var gate=player.GetComponent<CalligraphyPlayerHitGate>()??player.gameObject.AddComponent<CalligraphyPlayerHitGate>();
        var controller=player.GetComponent<ThirdPersonController>();
        bool controlled=(controller && (controller.ActionsLocked || controller.IsStunned || controller.IsBallistic)) || (player.GetComponent<TetherThrownMotion>()?.IsActive??false);
        bool stun=reaction==CalligraphyPlayerControl.Reaction.None && !controlled && Time.time>=gate.nextStun;
        float before=player.Health;
        bool actual=false;System.Action<float> receipt=a=>actual|=a>0;
        player.OnDamaged+=receipt;
        bool previousDelivery=DeliveringPaintingDamage;DeliveringPaintingDamage=true;
        try{player.TakeDamage(damage,controlled?Vector3.zero:impulse,false,reason,stun);}
        finally{DeliveringPaintingDamage=previousDelivery;player.OnDamaged-=receipt;}
        actual|=player.Health<before;
        if(!actual)return false;
        if(actual && stun)gate.nextStun=Time.time+gate.minimumStunInterval;
        if(reaction!=CalligraphyPlayerControl.Reaction.None && !controlled && Time.time>=gate.nextStun)
        {
            var control=player.GetComponent<CalligraphyPlayerControl>()??player.gameObject.AddComponent<CalligraphyPlayerControl>();
            if(control.Apply(reaction,controlSeconds,impulse,launch))gate.nextStun=Time.time+controlSeconds+.45f;
        }
        var library=CalligraphyVfxLibrary.Current;
        if(library)
        {
            Vector3 direction=impulse.sqrMagnitude>.001f?impulse.normalized:-player.transform.forward;
            var cc=player.GetComponent<CharacterController>();
            Vector3 at=contact??(cc?cc.ClosestPoint(player.transform.position+Vector3.up-direction):player.transform.position+Vector3.up);
            if(cc)at=cc.ClosestPoint(at);
            Quaternion rotation=Quaternion.LookRotation(direction);
            Transform parent=CalligraphyPaintingWorld.BlocksTether && source?source.parent:null;
            CalligraphyVfxLibrary.Play((gate.hitSequence++%2==0)?library.novaLight:library.novaHeavy,at,rotation,.55f,1.5f,0,parent);
            CalligraphyVfxLibrary.Play(library.impact,at,rotation,.45f,1.5f,0,parent);
            CalligraphyVfxLibrary.Play(library.cone,at,rotation,.45f,1.5f,0,parent);
        }
        return true;
    }
}
