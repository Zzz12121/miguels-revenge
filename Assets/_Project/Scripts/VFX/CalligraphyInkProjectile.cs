using UnityEngine;

/// <summary>Swept collision, fixed launch direction. Defence is resolved only by PlayerHealth.</summary>
public sealed class CalligraphyInkProjectile : MonoBehaviour
{
    CalligraphyInkField ink;
    Transform owner;
    Vector3 velocity;
    float life,damage,trailClock;
    readonly RaycastHit[] hits=new RaycastHit[32];
    public void Launch(CalligraphyInkField field,Transform source,Vector3 from,Vector3 direction,float speed,float amount)
    {ink=field;owner=source;transform.position=from;velocity=direction.normalized*speed;damage=amount;life=3.5f;}
    void Update()
    {
        if(!ink || CalligraphyPaintingWorld.BlocksTether){Destroy(gameObject);return;}
        float dt=Time.deltaTime;life-=dt;
        Vector3 step=velocity*dt;
        int count=Physics.SphereCastNonAlloc(transform.position,.32f,velocity.normalized,hits,step.magnitude,~0,QueryTriggerInteraction.Ignore);
        int nearest=-1;float distance=float.MaxValue;
        for(int i=0;i<count;i++)
        {
            var h=hits[i];
            if(!h.collider || (owner && h.transform.IsChildOf(owner)) || h.collider.GetComponentInParent<CalligraphyClone>())continue;
            if(h.distance<distance){nearest=i;distance=h.distance;}
        }
        if(nearest>=0)
        {
            var h=hits[nearest];var player=h.collider.GetComponentInParent<PlayerHealth>();
            if(player)CalligraphyPlayerHitGate.Hit(player,damage,velocity.normalized*1.4f,"被遊墨行者的筆觸擊倒");
            ink.Burst(h.point,1.1f);Destroy(gameObject);return;
        }
        transform.position+=step;trailClock-=dt;
        if(trailClock<=0)
        {
            trailClock=.055f;
            ink.Emit(transform.position,velocity*.22f,1.2f,.75f);
            ink.Emit(transform.position-velocity.normalized*.3f,-velocity*.1f,.22f,2.1f);
        }
        if(life<=0){ink.Burst(transform.position,.8f,8);Destroy(gameObject);}
    }
}
