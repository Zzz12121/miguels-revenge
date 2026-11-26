using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>City-wide director for the existing elder skills. No arena centre, radius or leash.</summary>
[DefaultExecutionOrder(260)]
public sealed class InkboundTownEncounter:MonoBehaviour
{
    public CalligraphyBossCombat elder;
    public PlayerHealth player;
    public NavMeshAgent agent;
    public float discoveryRadius=8,initialHealthFraction=.5f,repathInterval=.32f;
    public float maximumCastDistance=18;
    public string State {get;private set;}="Exploring";
    public int Casts {get;private set;}
    public int IncompletePaths {get;private set;}
    public bool FightStarted {get;private set;}
    public BossHealth Health {get;private set;}
    Vector3 home;
    float repath,decision,entryHealth;
    bool resetting,awaitingRecovery;

    void Start()
    {
        if(!elder||!player||!agent){enabled=false;return;}
        home=elder.transform.position;
        Health=elder.GetComponent<BossHealth>();
        if(!Health)Health=elder.gameObject.AddComponent<BossHealth>();
        Health.maxHealth=InkboundTownJourney.Pending?InkboundTownJourney.MaximumHealth:elder.maxHealth;
        entryHealth=InkboundTownJourney.Pending?InkboundTownJourney.RemainingHealth:Health.maxHealth*initialHealthFraction;
        Health.ResetHealth();Health.SetHealth(entryHealth);Health.ShowBar=false;Health.bossName="遊墨行者";Health.flashTime=0;
        elder.encounterHealth=Health;elder.externallySequenced=true;
        StandQuietly();
        agent.updatePosition=false;agent.updateRotation=false;agent.autoTraverseOffMeshLink=false;
        if(NavMesh.SamplePosition(home,out var hit,2,NavMesh.AllAreas))agent.Warp(hit.position);
        player.OnRespawned+=ResetAfterDeath;Health.OnDamaged+=OnDamaged;Health.OnDied+=Defeated;
        decision=Time.time+3;
    }
    void OnDamaged(float damage){if(!FightStarted&&!resetting&&!Health.IsDead&&Health.Health>0)BeginFight();}
    public void BeginFight()
    {
        if(FightStarted||!player||player.IsDead||Health.IsDead||Health.Health<=0)return;
        float remaining=Health.Health;
        elder.BeginEncounter(player,Health);Health.SetHealth(remaining);Health.ShowBar=true;
        elder.EncounterMeleeContact+=BrushHit;
        FightStarted=true;State="Fighting across town";decision=Time.time+.8f;
    }
    void BrushHit(float damage,Vector3 direction,Vector3 point)
    {CalligraphyPlayerHitGate.Hit(player,damage,direction*2,"被遊墨行者的巨筆擊倒",point);}
    void Update()
    {
        if(!elder||!player||!Health||Time.deltaTime<=0)return;
        if(Health.IsDead)return;
        if(player.IsDead){if(elder.Active)elder.StopBattle();State="Waiting for retry";if(agent.isOnNavMesh)agent.isStopped=true;return;}
        Vector3 delta=player.transform.position-elder.transform.position;
        if(!FightStarted)
        {if(Time.time>decision&&delta.magnitude<discoveryRadius&&Clear(elder.transform.position+Vector3.up,player.transform.position+Vector3.up,.15f))BeginFight();return;}
        if(awaitingRecovery&&!elder.IsCasting&&!Held())
        {
            awaitingRecovery=false;int index=elder.SelectedSkill;
            float recovery=elder.skillRecovery!=null&&index<elder.skillRecovery.Length&&elder.skillRecovery[index]>0?elder.skillRecovery[index]:elder.recoverySeconds;
            decision=Time.time+recovery;
        }
        if(!agent.isOnNavMesh||elder.IsCasting||Held())
        {if(agent.isOnNavMesh){agent.isStopped=true;agent.nextPosition=elder.transform.position;}return;}
        agent.isStopped=false;agent.nextPosition=elder.transform.position;
        if(Time.time>=repath)
        {
            repath=Time.time+repathInterval;
            if(NavMesh.SamplePosition(player.transform.position,out var target,2.5f,NavMesh.AllAreas))agent.SetDestination(target.position);
        }
        if(agent.pathPending)return;
        if(agent.hasPath&&agent.pathStatus!=NavMeshPathStatus.PathComplete){IncompletePaths++;State="Following reachable street";}
        float flat=Vector3.ProjectOnPlane(delta,Vector3.up).magnitude;
        bool sight=Clear(elder.transform.position+Vector3.up*1.2f,player.transform.position+Vector3.up*.6f,.16f);
        if(flat>2.8f||!sight)
        {
            Vector3 direction=Vector3.ProjectOnPlane(agent.steeringTarget-elder.transform.position,Vector3.up);
            if(direction.sqrMagnitude>.0001f)
            {
                elder.transform.rotation=Quaternion.RotateTowards(elder.transform.rotation,Quaternion.LookRotation(direction),240*Time.deltaTime);
                Vector3 step=direction.normalized*elder.moveSpeed*Time.deltaTime;
                if(NavMesh.SamplePosition(elder.transform.position+step,out var support,.65f,NavMesh.AllAreas))
                {
                    Vector3 before=elder.transform.position;elder.MoveInEncounter(step);
                    // The shared mover's unsorted ground ray can hit the quay below an arch
                    // before its deck. Keep its physical wall sweep, but use this street's
                    // navigation surface for foot height instead of dropping under the bridge.
                    Vector3 after=elder.transform.position;
                    if(Vector3.ProjectOnPlane(after-before,Vector3.up).sqrMagnitude>.0000001f)
                    {after.y=support.position.y;elder.transform.position=after;agent.nextPosition=after;}
                }
            }
        }
        else elder.MoveInEncounter(Vector3.zero);
        if(Time.time<decision||!sight||flat>maximumCastDistance)return;
        int skill=ChooseSkill(flat);
        if(skill<0)return;
        if(elder.CastEncounterSkill(skill,skill==2))
        {Casts++;awaitingRecovery=true;decision=float.PositiveInfinity;State="Fighting across town";agent.isStopped=true;}
    }
    void LateUpdate()
    {
        if(!elder||!elder.IsCasting||elder.SelectedSkill!=5)return;
        Vector3 from=elder.transform.position+Vector3.up*elder.inkBeam.emitterHeight;
        float range=35;
        foreach(var h in Physics.SphereCastAll(from,elder.inkBeam.hitRadius,elder.transform.forward,range,~0,QueryTriggerInteraction.Ignore))
            if(!h.transform.IsChildOf(elder.transform)&&!h.transform.IsChildOf(player.transform)&&!h.collider.GetComponentInParent<InkboundEcho>())range=Mathf.Min(range,Mathf.Max(.2f,h.distance-.12f));
        elder.inkBeam.maximumRange=range;
    }
    bool Held()=> (elder.GetComponent<TetherThrownMotion>()?.IsActive??false)||!elder.enabled;
    int ChooseSkill(float distance)
    {
        // Preserve authored attacks; reject those whose sweep/landing/ceiling cannot fit this street.
        Vector3 from=elder.transform.position,to=player.transform.position;
        bool lateral=Clear(from+Vector3.up,from+elder.transform.right*2.5f+Vector3.up,.45f)&&Clear(from+Vector3.up,from-elder.transform.right*2.5f+Vector3.up,.45f);
        bool overhead=Clear(from+Vector3.up*2,from+Vector3.up*12,.5f)&&Clear(to+Vector3.up*2,to+Vector3.up*12,.5f);
        bool landing=NavMesh.SamplePosition(to,out var ground,1.3f,NavMesh.AllAreas)&&Mathf.Abs(ground.position.y-to.y)<1.4f;
        if(distance<elder.meleeDistance&&lateral&&Random.value<.5f)return 2;
        if(lateral&&OpenGround(from,9)&&distance<8&&Random.value<.25f)return 7;
        if(overhead&&landing&&distance>4&&distance<10&&Random.value<.2f)
        {elder.leapSlam.jumpDistance=Mathf.Min(7,distance);return 6;}
        if(overhead&&landing&&OpenGround(to,elder.runeAttack.spawnRadius)&&Random.value<.18f)return 8;
        if(overhead&&OpenGround(to,elder.inkDragon.impactRadius)&&Random.value<.12f)return 4;
        if(lateral&&distance>5&&Random.value<.25f)return 5;
        return Casts%2;
    }
    bool OpenGround(Vector3 p,float radius)
    {
        if(!NavMesh.SamplePosition(p,out var origin,1.6f,NavMesh.AllAreas))return false;
        for(int j=0;j<12;j++)
        {
            float a=j*Mathf.PI/6;Vector3 end=origin.position+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
            if(NavMesh.Raycast(origin.position,end,out _,NavMesh.AllAreas)||!Clear(p+Vector3.up,end+Vector3.up,.2f))return false;
        }
        return true;
    }
    bool Clear(Vector3 from,Vector3 to,float radius)
    {
        Vector3 d=to-from;if(d.sqrMagnitude<.001f)return true;
        foreach(var h in Physics.SphereCastAll(from,radius,d.normalized,d.magnitude,~0,QueryTriggerInteraction.Ignore))
        {
            var c=h.collider;
            if(c.transform.IsChildOf(elder.transform)||c.transform.IsChildOf(player.transform)||c.GetComponentInParent<InkboundEcho>())continue;
            if(h.distance>.02f)return false;
        }
        return true;
    }
    void Defeated()
    {
        FightStarted=false;State="Life continues";Health.ShowBar=false;
        elder.EncounterMeleeContact-=BrushHit;elder.EndEncounterDefeated();if(agent.isOnNavMesh)agent.isStopped=true;
        // Town residents, boats and ambience keep their original time; no scene collapse or instant exit.
    }
    void ResetAfterDeath()
    {
        if(Health.IsDead)return;
        resetting=true;awaitingRecovery=false;elder.EncounterMeleeContact-=BrushHit;elder.StopBattle();
        Health.ResetHealth();Health.SetHealth(entryHealth);Health.ShowBar=false;
        FightStarted=false;State="Exploring";decision=Time.time+3;elder.transform.position=home;StandQuietly();
        if(NavMesh.SamplePosition(home,out var h,2,NavMesh.AllAreas)){agent.Warp(h.position);agent.nextPosition=h.position;}
        resetting=false;
    }
    void StandQuietly()
    {
        var display=elder.GetComponent<CalligraphyInkPresentation>();if(display)display.enabled=false;
        elder.brush.showcase=false;int index=System.Array.IndexOf(elder.brush.animationStates,"standing_relax");if(index>=0)elder.brush.PlayAnimation(index);
    }
    void OnDestroy()
    {
        if(player)player.OnRespawned-=ResetAfterDeath;
        if(Health){Health.OnDamaged-=OnDamaged;Health.OnDied-=Defeated;}
        if(elder)elder.EncounterMeleeContact-=BrushHit;
    }
}
