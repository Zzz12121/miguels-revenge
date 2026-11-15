using System.Collections.Generic;
using UnityEngine;

/// <summary>Independent damage receiver. Dodge decoys never attack; combat doubles take five distinct hits.</summary>
public sealed class CalligraphyClone : MonoBehaviour,IDamageable
{
    public bool combatClone;
    public int hitsRemaining=5;
    public float lifetime=10;
    public bool IsDead=>dissolving;
    CalligraphyInkField ink;
    PlayerHealth player;
    CalligraphyBrushPresentation brush;
    Renderer[] renderers;
    readonly HashSet<long> seen=new();
    float born,fadeStarted,attackClock,emissionClock;
    bool dissolving,hitInSwing;
    Vector3 previousTip;
    CalligraphyBossCombat.MeleeSettings settings=new();
    public bool IsFlanking {get;private set;}
    public int FlankSide {get;private set;}
    public float ChaseSpeed=>settings.cloneMoveSpeed*(IsFlanking?1.4f:1);
    public bool WithinStrikeRange {get;private set;}
    public int Swings {get;private set;}
    GameObject shockwave;
    public void ConfigureFlank(int side,GameObject impact){IsFlanking=true;FlankSide=side>=0?1:-1;shockwave=impact;}
    public void Configure(CalligraphyBossCombat.MeleeSettings value)
    {settings=value;hitsRemaining=value.cloneHits;lifetime=value.cloneLifetime;}
    public static CalligraphyClone Spawn(GameObject template,CalligraphyInkField field,PlayerHealth victim,Vector3 position,Quaternion rotation,bool combat,Transform parent,CalligraphyBrushPresentation snapshot=null)
    {
        var root=Instantiate(template,position,rotation,parent);root.name=combat?"Ink combat double":"Ink dodge decoy";
        root.SetActive(false);
        foreach(var c in root.GetComponentsInChildren<CalligraphyInkPresentation>(true))c.enabled=false;
        foreach(var c in root.GetComponents<CalligraphyPracticeTarget>())Destroy(c);
        foreach(var c in root.GetComponents<CalligraphyBossCombat>())Destroy(c);
        var clone=root.AddComponent<CalligraphyClone>();clone.combatClone=combat;clone.hitsRemaining=combat?5:1;clone.lifetime=combat?10:3;
        clone.ink=field;clone.player=victim;clone.brush=root.GetComponent<CalligraphyBrushPresentation>();
        if(clone.brush)clone.brush.showcase=false;
        if(!combat && snapshot && clone.brush)
        {
            var source=new Dictionary<string,Transform>();
            foreach(var t in snapshot.animator.GetComponentsInChildren<Transform>())source[t.name]=t;
            foreach(var t in clone.brush.animator.GetComponentsInChildren<Transform>())
                if(source.TryGetValue(t.name,out var s)){t.localPosition=s.localPosition;t.localRotation=s.localRotation;}
            clone.brush.animator.enabled=false;clone.brush.enabled=false;
            clone.brush.brushPivot.SetPositionAndRotation(snapshot.brushPivot.position,snapshot.brushPivot.rotation);
        }
        clone.renderers=root.GetComponentsInChildren<Renderer>(true);
        foreach(var r in clone.renderers)
        {
            var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=field.originalEchoMaterial?field.originalEchoMaterial:field.sculptureMaterial;r.sharedMaterials=mats;
        }
        var oldInk=root.GetComponent<CalligraphyInkPresentation>();
        // The clone itself is the flowing silhouette. Do not spawn extra humanoid snapshots
        // and the presentation prefab's 28/s smoke on top of the two combat doubles.
        if(oldInk)oldInk.enabled=false;
        root.AddComponent<CalligraphyBrushWake>().Initialize(clone.brush,field);
        root.tag="Enemy";
        if(!root.GetComponent<VoidRendTargetable>())root.AddComponent<VoidRendTargetable>();
        if(!root.GetComponent<TetherInteractable>())root.AddComponent<TetherInteractable>();
        clone.born=Time.time;root.SetActive(true);clone.previousTip=clone.brush.brushTip.position;field.Burst(position+Vector3.up,1,10);return clone;
    }
    public void TakeDamage(float amount,Vector3 at)
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if(amount<=0 || dissolving)return;
        var context=CalligraphyAttackContext.Current;
        long key=context.cast!=0?((long)context.cast<<32)|(uint)context.hit:-(long)Time.frameCount-1;
        if(!seen.Add(key))return;
        hitsRemaining--;ink.Burst(at,.5f,5);
        if(hitsRemaining<=0)Dissolve();
    }
    public void Dissolve()
    {
        if(dissolving)return;
        dissolving=true;fadeStarted=Time.time;
        foreach(var c in GetComponentsInChildren<Collider>())c.enabled=false;
        if(brush)brush.ClearCombatPose();
        if(ink)ink.Burst(transform.position+Vector3.up,1.2f,16);
    }
    void Update()
    {
        if(!ink){Destroy(gameObject);return;}
        if(Time.time-born>=lifetime)Dissolve();
        float dissolve=dissolving?Mathf.Clamp01((Time.time-fadeStarted)/Mathf.Max(.1f,settings.cloneFadeSeconds)):0;
        float appear=Mathf.Clamp01((Time.time-born)/.65f);
        foreach(var r in renderers)if(r)CalligraphyGeometry.Tint(r,new Color(.003f,.006f,.009f),appear*.58f,Mathf.Max(dissolve,(1-appear)*.8f));
        if(dissolve>=1){Destroy(gameObject);return;}
        if(dissolving || !combatClone || !player || player.IsDead || CalligraphyPaintingWorld.BlocksTether)return;
        if(IsFlanking){UpdateFlank();return;}
        Vector3 direction=player.transform.position-transform.position;direction.y=0;
        if(direction.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),220*Time.deltaTime);
        float distance=direction.magnitude;
        if(distance>2.8f)
        {
            bool blocked=false;float travel=Time.deltaTime*settings.cloneMoveSpeed;
            foreach(var h in Physics.SphereCastAll(transform.position+Vector3.up,.5f,direction.normalized,travel+.15f,~0,QueryTriggerInteraction.Ignore))
                if(!h.transform.IsChildOf(transform)){blocked=true;break;}
            if(!blocked)
            {
                Vector3 at=CalligraphyGeometry.Ground(transform.position+direction.normalized*travel,transform);
                if(Mathf.Abs(at.y-transform.position.y)<.4f)transform.position=at;
            }
        }
        attackClock+=Time.deltaTime;
        float segment=Mathf.Max(.1f,settings.cloneStrokeSeconds),phase=(attackClock%segment)/segment;
        if(phase<.1f)hitInSwing=false;
        int style=((int)(attackClock/segment))%5;
        if(brush)brush.SetCombatPose(4+style,phase);
        if(phase>=.38f && phase-Time.deltaTime/segment<.38f)CalligraphyMeleeEffects.Play(brush,style,settings.vfxScale);
        if(phase>.38f && phase<.72f && !hitInSwing && brush &&
            CalligraphyPlayerHitGate.Touches(player,previousTip,brush.brushTip.position,.7f))
        {
            hitInSwing=true;CalligraphyPlayerHitGate.Hit(player,settings.cloneDamage,direction.normalized,"被水墨分身擊倒",brush.brushTip.position);
        }
        emissionClock-=Time.deltaTime;
        if(emissionClock<=0 && brush){emissionClock=.12f;ink.Emit(brush.brushTip.position,-transform.forward,.35f,1.3f);}
        if(brush)previousTip=brush.brushTip.position;
    }
    void UpdateFlank()
    {
        Vector3 facing=Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized;
        Vector3 target=player.transform.position+facing*(FlankSide*2.25f);
        Vector3 move=Vector3.ProjectOnPlane(target-transform.position,Vector3.up);
        Vector3 direction=Vector3.ProjectOnPlane(player.transform.position-transform.position,Vector3.up);
        if(direction.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),440*Time.deltaTime);
        if(move.magnitude>.25f)
        {
            Vector3 step=move.normalized*Mathf.Min(move.magnitude,ChaseSpeed*Time.deltaTime);
            // Go around the player rather than passing through their body to reach the assigned flank.
            Vector3 toPlayer=Vector3.ProjectOnPlane(player.transform.position-transform.position,Vector3.up);
            if(toPlayer.magnitude<2 && Vector3.Dot(step.normalized,toPlayer.normalized)>.5f)
                step=Vector3.Cross(Vector3.up,toPlayer.normalized)*FlankSide*step.magnitude;
            bool blocked=false;
            foreach(var h in Physics.SphereCastAll(transform.position+Vector3.up,.4f,step.normalized,step.magnitude+.08f,~0,QueryTriggerInteraction.Ignore))
                if(!h.transform.IsChildOf(transform) && !h.collider.GetComponentInParent<CalligraphyClone>() && !h.collider.GetComponentInParent<DojoMasterBoss>() && !h.collider.GetComponentInParent<PlayerHealth>()){blocked=true;break;}
            if(!blocked){var at=CalligraphyGeometry.Ground(transform.position+step,transform);if(Mathf.Abs(at.y-transform.position.y)<.45f)transform.position=at;}
        }
        WithinStrikeRange=direction.magnitude<=3.6f && Mathf.Abs(player.transform.position.y-transform.position.y)<1.6f;
        if(!WithinStrikeRange)
        {attackClock=0;hitInSwing=false;if(brush){brush.ClearCombatPose();previousTip=brush.brushTip.position;}return;}
        float segment=Mathf.Max(.1f,settings.cloneStrokeSeconds);
        float old=attackClock;attackClock+=Time.deltaTime;
        float phase=(attackClock%segment)/segment;int style=((int)(attackClock/segment))%5;
        if((int)(old/segment)!=(int)(attackClock/segment))hitInSwing=false;
        if(brush)brush.SetCombatPose(4+style,phase);
        if(phase>=.38f && phase-Time.deltaTime/segment<.38f){Swings++;CalligraphyMeleeEffects.Play(brush,style,settings.vfxScale);}
        if(phase>.38f && phase<.72f && !hitInSwing && brush && CalligraphyPlayerHitGate.Touches(player,previousTip,brush.brushTip.position,.7f))
        {
            hitInSwing=true;
            if(CalligraphyPlayerHitGate.Hit(player,settings.cloneDamage,direction.normalized,"被水墨分身擊倒",brush.brushTip.position))
                DojoMeleeImpact.Play(shockwave,brush.brushTip.position,direction);
        }
        if(brush)previousTip=brush.brushTip.position;
    }
}
