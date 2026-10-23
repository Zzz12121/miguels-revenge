using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Opt-in combat adapter for the existing elder presentation. Never auto-starts in production scenes.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed partial class CalligraphyBossCombat : MonoBehaviour,IDamageable
{
    public CalligraphyBrushPresentation brush;
    public CalligraphyInkField ink;
    public GameObject cloneTemplate;
    public PlayerHealth player;
    public float maxHealth=10000,moveSpeed=2.8f,meleeDistance=3.6f,projectileSpeed=15,projectileDamage=12;
    public float recoverySeconds=1.1f,dodgeDistance=2.8f;
    public bool cycleDemonstration=true;
    public float paintingCooldown=32,dragonCooldown=22;
    [Header("Painting world / ink dragons")]
    public float paintingRadius=96,disturbanceRadius=5,disturbanceStrength=.8f;
    public float giantDragonScale=2.2f,giantDragonDamage=35,paintingDragonDamage=15;
    public float paintingDragonScale=2.6f;
    [Range(0,1)] public float dodgeChance=1f/3f;
    [Tooltip("Invoked on dragon impact; terrain systems can subscribe without depending on VFX internals.")]
    public UnityEvent<Vector3> onDragonImpact=new();
    public bool Active {get;private set;}
    public bool IsDead=>Health<=0;
    public float Health {get;private set;}
    public int SelectedSkill {get;private set;}
    public string SkillName=>SkillNames[Mathf.Clamp(SelectedSkill,0,SkillNames.Length-1)];
    public static readonly string[] SkillNames={"飛墨一筆","連筆四式","近戰・墨影合擊","畫中世界","天降墨龍","交叉運筆・墨光","躍筆重砸","連環墨浪","點墨落印"};
    public bool IsCasting=>casting;
    public Transform EffectsRoot=>effectsRoot;
    readonly Dictionary<int,bool> dodgeResults=new();
    readonly Queue<int> dodgeOrder=new();
    readonly List<CalligraphyClone> clones=new();
    Transform effectsRoot;
    CalligraphyPaintingWorld painting;
    CalligraphyInkDragon descendingDragon;
    float elapsed,cooldown,inkClock,lastDamage,evadeClock=-1;
    Vector3 evadeFrom,evadeTo,aim;
    Vector3 previousTip;
    int released;
    bool casting,impactDone;
    bool practiceImmortal;
    bool walking;
    float nextPainting,nextDragon;
    void Awake()
    {
        if(!brush)brush=GetComponent<CalligraphyBrushPresentation>();
        Health=maxHealth;
        var go=new GameObject("Calligraphy battle transients");
        go.transform.SetParent(transform.parent,false);effectsRoot=go.transform;
        if(brush && ink)gameObject.AddComponent<CalligraphyBrushWake>().Initialize(brush,ink);
    }
    public void StartBattle(PlayerHealth target,bool practice=false)
    {
        enabled=true;CancelCast();player=target;Active=true;Health=maxHealth;practiceImmortal=practice;SelectedSkill=0;cooldown=.8f;Vanished=false;
        dodgeResults.Clear();dodgeOrder.Clear();brush.showcase=false;
        previousTip=brush.brushTip.position;
        nextPainting=Time.time+8;nextDragon=Time.time+14;
        if(practice)cycleDemonstration=true;
        var display=GetComponent<CalligraphyInkPresentation>();if(display)display.enabled=false;
    }
    public void StopBattle()
    {
        Active=false;CancelCast();
        var display=GetComponent<CalligraphyInkPresentation>();if(display)display.enabled=true;
        if(brush)brush.showcase=true;
    }
    public void NextSkill()
    { if(!Active)return;CancelCast();SelectedSkill=(SelectedSkill+1)%SkillNames.Length;cooldown=.35f; }
    public void SelectSkill(int index)
    { if(IsDead)return;CancelCast();SelectedSkill=Mathf.Clamp(index,0,SkillNames.Length-1);cooldown=.35f; }
    public void TakeDamage(float amount,Vector3 point)
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if(amount<=0 || IsDead)return;
        var attack=CalligraphyAttackContext.Current;
        if(attack.cast!=0 && TryEvade(attack.cast,attack.skill))return;
        Health=Mathf.Max(0,Health-amount);lastDamage=Time.time;
        if(IsDead)
        {
            BeginDeath();
            return;
        }
        if(ink && Active)ink.Burst(point,.45f,4);
        if(!casting)GetComponent<VoidRendTargetable>()?.PlayHitReaction();
    }
    public bool TryEvade(int cast,int skill)
    {
        if(!Active || !enabled || cast==0 || skill<=1)return false;
        if(dodgeResults.TryGetValue(cast,out bool result))return result;
        bool allowed=evadeClock<0 && (!casting || elapsed<.3f) && !(GetComponent<TetherThrownMotion>()?.IsActive??false);
        result=allowed && Random.value<dodgeChance;
        dodgeResults[cast]=result;dodgeOrder.Enqueue(cast);
        while(dodgeOrder.Count>128)dodgeResults.Remove(dodgeOrder.Dequeue());
        if(!result)return false;
        var decoy=CalligraphyClone.Spawn(cloneTemplate,ink,player,transform.position,transform.rotation,false,effectsRoot,brush);
        clones.Add(decoy);evadeFrom=transform.position;evadeTo=evadeFrom-transform.forward*dodgeDistance;
        if(Physics.SphereCast(evadeFrom+Vector3.up,.4f,-transform.forward,out var obstruction,dodgeDistance,~0,QueryTriggerInteraction.Ignore) && !obstruction.transform.IsChildOf(transform))
            evadeTo=evadeFrom-transform.forward*Mathf.Max(0,obstruction.distance-.1f);
        evadeClock=0;return true;
    }
    void Update()
    {
        if(dying){UpdateDeath();return;}
        if(!Active || !player || player.IsDead || Time.deltaTime<=0)return;
        if(evadeClock>=0)
        {
            evadeClock+=Time.deltaTime;
            transform.position=Vector3.Lerp(evadeFrom,evadeTo,Mathf.SmoothStep(0,1,evadeClock/.28f));
            if(evadeClock>=.28f)evadeClock=-1;
            return;
        }
        Vector3 direction=player.transform.position-transform.position;direction.y=0;
        if(direction.sqrMagnitude>.001f && (!casting || (SelectedSkill!=6 && elapsed<.4f)))
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),240*Time.deltaTime);
        if(!casting)
        {
            cooldown-=Time.deltaTime;
            float wanted=SelectedSkill==2 || SelectedSkill==7?meleeDistance*.7f:7;
            bool shouldWalk=direction.magnitude>wanted+.3f;
            SetWalking(shouldWalk);
            if(shouldWalk)Move(direction.normalized*moveSpeed*Time.deltaTime);
            if(cooldown<=0){SetWalking(false);casting=true;elapsed=0;released=0;impactDone=false;aim=player.transform.position;slashVfx=-1;skillEffect=null;}
            return;
        }
        elapsed+=Time.deltaTime;
        switch(SelectedSkill)
        {
            case 0: Ranged(Mathf.Max(1,inkProjectile.singleCount));break;
            case 1: Ranged(Mathf.Max(1,inkProjectile.comboCount));break;
            case 2: Melee();break;
            case 3: Painting();break;
            case 4: Dragon();break;
            case 5: Beam();break;
            case 6: LeapSlam();break;
            case 7: FrontalPulses();break;
            case 8: Runes();break;
        }
        inkClock-=Time.deltaTime;
        if(inkClock<=0 && brush && brush.brushTip)
        {inkClock=.08f;ink.Emit(brush.brushTip.position,-transform.forward*.6f,.45f,1.8f);}
        previousTip=brush.brushTip.position;
    }
    void SetWalking(bool value)
    {
        if(walking==value || !brush)return;walking=value;
        int index=System.Array.IndexOf(brush.animationStates,value?"walk":"standing_relax");
        if(index>=0)brush.PlayAnimation(index);
    }
    void Move(Vector3 delta)
    {
        if(delta.sqrMagnitude<.00001f)return;
        foreach(var hit in Physics.SphereCastAll(transform.position+Vector3.up,.42f,delta.normalized,delta.magnitude+.08f,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform))return;
        Vector3 next=transform.position+delta;
        // Only walk onto supported ground, never off a ledge. Tether/skill1 retain their own motion owner.
        foreach(var h in Physics.RaycastAll(next+Vector3.up*1.2f,Vector3.down,2.4f,~0,QueryTriggerInteraction.Ignore))
            if(!h.transform.IsChildOf(transform) && h.normal.y>.5f){next.y=h.point.y;transform.position=next;return;}
    }
    void Ranged(int count)
    {
        float segment=Mathf.Max(.1f,inkProjectile.strokeSeconds);
        int index=Mathf.Min(count-1,(int)(elapsed/segment));
        float phase=(elapsed-index*segment)/segment;
        brush.SetCombatPose(index%2,phase);
        if(phase<.57f)aim=Abdomen(player);
        if(phase>=.57f && released<=index)
        {
            released=index+1;
            var shot=new GameObject("Flying brushstroke");shot.transform.SetParent(effectsRoot,false);
            shot.AddComponent<CalligraphyInkProjectile>().Launch(ink,transform,brush.brushTip.position,aim-brush.brushTip.position,projectileSpeed,projectileDamage,player,inkProjectile);
            ink.Burst(brush.brushTip.position,.55f,6);
        }
        if(elapsed>=count*segment+.35f)FinishCast();
    }
    void Melee()
    {
        if(elapsed<1.2f)
        {
            brush.SetCombatPose(2,elapsed/1.2f);
            if(elapsed>.7f && released==0)
            {
                released=1;
                clones.RemoveAll(c=>!c);
                int live=clones.FindAll(c=>c && c.combatClone && !c.IsDead).Count;
                for(int i=0;i<2 && live<Mathf.Clamp(inkMelee.cloneCount,0,2);i++)
                {
                    if(!ClonePosition(i==0?-1:1,out var p))continue;
                    var clone=CalligraphyClone.Spawn(cloneTemplate,ink,player,p,transform.rotation,true,effectsRoot);
                    clone.Configure(inkMelee);
                    clones.Add(clone);live++;
                }
            }
            return;
        }
        float t=elapsed-1.2f,segment=Mathf.Max(.1f,inkMelee.strokeSeconds);int index=(int)(t/segment);float phase=(t%segment)/segment;
        int style=index%5;
        brush.SetCombatPose(4+style,phase);
        if(phase>=.38f && slashVfx<index){slashVfx=index;CalligraphyMeleeEffects.Play(brush,style,inkMelee.vfxScale);}
        Vector3 d=player.transform.position-transform.position;d.y=0;
        if(phase<.3f && d.magnitude>meleeDistance*.8f)Move(d.normalized*moveSpeed*1.5f*Time.deltaTime);
        if(phase>.38f && phase<.72f && released<index+2 &&
            CalligraphyPlayerHitGate.Touches(player,previousTip,brush.brushTip.position,inkMelee.hitRadius))
        {
            released=index+2;
            CalligraphyPlayerHitGate.Hit(player,inkMelee.damage,d.normalized*2,"被遊墨行者的巨筆擊倒",brush.brushTip.position);
            ink.Burst(brush.brushTip.position,1,12);
        }
        if(t>=Mathf.Max(1,inkMelee.comboCount)*segment)FinishCast();
    }
    void Painting()
    {
        if(elapsed<4)
        {
            brush.SetCombatPose((int)elapsed%4,elapsed%1);
            if(!painting)
            {
                var go=new GameObject("Painting world");go.transform.SetParent(effectsRoot,false);
                painting=go.AddComponent<CalligraphyPaintingWorld>();painting.Begin(this,player,ink);
            }
        }
        else brush.ClearCombatPose();
        if(elapsed>4 && (!painting || painting.Finished))FinishCast();
    }
    void Dragon()
    {
        if(elapsed<2.4f)brush.SetCombatPose((int)(elapsed/.8f)%3,(elapsed%.8f)/.8f);
        else brush.SetCombatPose(3,(elapsed-2.4f)/1.6f);
        if(released==0)
        {
            var library=CalligraphyVfxLibrary.Current;
            if(library)
            {
                var smoke=CalligraphyVfxLibrary.Play(library.smoke,transform.position+inkDragon.smokeOffset,Quaternion.identity,inkDragon.smokeScale,1,0,effectsRoot);
                if(smoke){smoke.FollowGround(transform,inkDragon.smokeOffset,inkDragon.smokeSpinMultiplier,inkDragon.smokeFollowPosition,inkDragon.smokeWorldSpace,inkDragon.smokeYawOffset);smoke.StopAfter(inkDragon.releaseSeconds+.55f,1.5f);}
            }
            aim=CalligraphyGeometry.Ground(player.transform.position,player.transform);released=1;
            var go=new GameObject("Descending Chinese ink dragon");go.transform.SetParent(effectsRoot,false);
            descendingDragon=go.AddComponent<CalligraphyInkDragon>();
            descendingDragon.Initialize(ink,player,aim+Vector3.up*inkDragon.spawnHeight,aim,giantDragonScale,inkDragon.releaseSeconds,true,giantDragonDamage,inkDragon.diveSpeed,inkDragon.impactRadius);
        }
        if(elapsed>=inkDragon.releaseSeconds && released==1)
        {
            released=2;
            if(descendingDragon){descendingDragon.Dive();aim=descendingDragon.TrackedPosition;}
        }
        if(descendingDragon && descendingDragon.Impacted && !impactDone)
        {
            impactDone=true;aim=descendingDragon.TrackedPosition;onDragonImpact.Invoke(aim);
            CalligraphyGroundImpact.Create(effectsRoot,ink,aim,inkDragon.impactRadius,inkDragon);
            CalligraphySkillSequence.DragonImpact(aim,effectsRoot,inkDragon);
        }
        if(elapsed>=inkDragon.castSeconds)FinishCast();
    }
    void FinishCast()
    {
        float recovery=skillRecovery!=null && SelectedSkill<skillRecovery.Length && skillRecovery[SelectedSkill]>0?skillRecovery[SelectedSkill]:recoverySeconds;
        casting=false;elapsed=0;brush.ClearCombatPose();
        if(SelectedSkill==3)nextPainting=Time.time+paintingCooldown;
        if(SelectedSkill==4)nextDragon=Time.time+dragonCooldown;
        if(cycleDemonstration)SelectedSkill=(SelectedSkill+1)%SkillNames.Length;
        else if(Time.time>=nextPainting)SelectedSkill=3;
        else if(Time.time>=nextDragon)SelectedSkill=4;
        else SelectedSkill=player && Vector3.Distance(player.transform.position,transform.position)<meleeDistance?(Random.value<.5f?2:7):new[]{0,1,5,6,8}[Random.Range(0,5)];
        cooldown=recovery;
        // Ordinary attacks and doubles may finish their bounded lifetimes between attacks.
    }
    public void CancelCast()
    {
        runePoints.Clear();
        cooldown=Mathf.Max(cooldown,recoverySeconds);
        if(beamMesh){Destroy(beamMesh);beamMesh=null;}
        if(leaping){transform.position=leapGround;leaping=false;}
        casting=false;elapsed=0;evadeClock=-1;painting=null;descendingDragon=null;
        if(brush)brush.ClearCombatPose();
        if(effectsRoot)for(int i=effectsRoot.childCount-1;i>=0;i--)
        {var go=effectsRoot.GetChild(i).gameObject;go.SetActive(false);Destroy(go);}
        clones.Clear();if(ink)ink.Clear();
    }
    void OnDisable(){CancelCast();cooldown=.7f;}
    void OnDestroy(){if(effectsRoot)Destroy(effectsRoot.gameObject);}
    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if(!Active)return;
        float width=360,x=(Screen.width-width)*.5f;
        GUI.Box(new Rect(x,18,width,46),"遊墨行者 — "+SkillName);
        var old=GUI.color;GUI.color=new Color(.7f,.65f,.5f);GUI.DrawTexture(new Rect(x+5,48,(width-10)*Health/maxHealth,7),Texture2D.whiteTexture);GUI.color=old;
    }
}
