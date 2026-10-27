using UnityEngine;

/// <summary>Distance-bounded mild homing. The final flight segment permanently locks its heading.</summary>
public sealed class CalligraphyInkProjectile : MonoBehaviour
{
    CalligraphyInkField ink;
    Transform owner;
    Vector3 velocity;
    float life,damage,trailClock;
    CalligraphyPackageEffect visual;
    PlayerHealth target;
    CalligraphyBossCombat.ProjectileSettings settings;
    Transform cleanupRoot;
    bool scopedLifetime;
    public float DistanceTravelled {get;private set;}
    public bool HeadingLocked {get;private set;}
    public Vector3 Heading=>velocity.normalized;
    readonly RaycastHit[] hits=new RaycastHit[32];
    public void Launch(CalligraphyInkField field,Transform source,Vector3 from,Vector3 direction,float speed,float amount,PlayerHealth victim=null,CalligraphyBossCombat.ProjectileSettings tuning=null,Transform cleanupOwner=null)
    {
        cleanupRoot=cleanupOwner;scopedLifetime=cleanupOwner;
        settings=tuning??new CalligraphyBossCombat.ProjectileSettings();target=victim;
        ink=field;owner=source;transform.position=from;velocity=direction.normalized*Mathf.Max(.1f,speed);damage=amount;life=settings.maximumDistance/Mathf.Max(.1f,speed)+1;
        var library=CalligraphyVfxLibrary.Current;
        if(library)visual=CalligraphyVfxLibrary.Play(library.projectile,from,Quaternion.LookRotation(direction),settings.vfxScale,1,0,transform);
    }
    void OnDestroy()
    {
        if(!visual)return;
        if(scopedLifetime && (!cleanupRoot || !cleanupRoot.gameObject.activeInHierarchy)){Destroy(visual.gameObject);return;}
        visual.transform.SetParent(scopedLifetime?cleanupRoot:null,true);visual.FadeOut(.4f);
    }
    void Update()
    {
        if(!ink || CalligraphyPaintingWorld.BlocksTether){Destroy(gameObject);return;}
        float dt=Time.deltaTime;life-=dt;
        float remaining=Mathf.Max(0,settings.maximumDistance-DistanceTravelled);
        float steerDistance=settings.maximumDistance*Mathf.Clamp01(settings.homingEndPercentage)-DistanceTravelled;
        if(steerDistance<=0)HeadingLocked=true;
        if(!HeadingLocked && target && !target.IsDead)
        {
            Vector3 desired=CalligraphyBossCombat.Abdomen(target)-transform.position;
            // No U-turn toward a player who has already dodged past the projectile.
            if(Vector3.Dot(velocity,desired)>0)
                velocity=Vector3.RotateTowards(velocity.normalized,desired.normalized,settings.turnRate*Mathf.Deg2Rad*Mathf.Clamp01(settings.homingStrength)*Mathf.Min(dt,steerDistance/velocity.magnitude),0)*velocity.magnitude;
        }
        Vector3 step=velocity.normalized*Mathf.Min(velocity.magnitude*dt,remaining);
        if(visual)visual.transform.rotation=Quaternion.LookRotation(velocity);
        int count=Physics.SphereCastNonAlloc(transform.position,settings.collisionRadius,velocity.normalized,hits,step.magnitude,~0,QueryTriggerInteraction.Ignore);
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
            if(player)CalligraphyPlayerHitGate.Hit(player,damage,velocity.normalized*1.4f,"被遊墨行者的筆觸擊倒",h.point,feedbackParent:scopedLifetime?cleanupRoot:null);
            ink.Burst(h.point,1.1f);Destroy(gameObject);return;
        }
        transform.position+=step;DistanceTravelled+=step.magnitude;trailClock-=dt;
        HeadingLocked|=DistanceTravelled>=settings.maximumDistance*Mathf.Clamp01(settings.homingEndPercentage);
        if(trailClock<=0 && !visual)
        {
            trailClock=.055f;
            ink.Emit(transform.position,velocity*.22f,1.2f,.75f);
            ink.Emit(transform.position-velocity.normalized*.3f,-velocity*.1f,.22f,2.1f);
        }
        if(life<=0 || DistanceTravelled>=settings.maximumDistance){ink.Burst(transform.position,.8f,8);Destroy(gameObject);}
    }
}
