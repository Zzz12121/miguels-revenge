using UnityEngine;

// Opt-in production encounter control. The original range/manual playback keeps its scheduling.
public sealed partial class CalligraphyBossCombat
{
    [HideInInspector] public bool externallySequenced;
    [HideInInspector] public BossHealth encounterHealth;
    public event System.Action<float,Vector3,Vector3> EncounterMeleeContact;
    public int CompletedEncounterCasts {get;private set;}
    public int EncounterClonesRemaining
    {get{clones.RemoveAll(c=>!c);return clones.FindAll(c=>c.combatClone).Count;}}
    bool encounterBasicMelee;
    int encounterStrikeStyle;
    [HideInInspector] public GameObject encounterShockwave;
    public bool EncounterMeleeInRange=>player && Vector3.ProjectOnPlane(player.transform.position-transform.position,Vector3.up).magnitude<=meleeDistance && Mathf.Abs(player.transform.position.y-transform.position.y)<1.6f;
    public bool EncounterMeleeLostRange {get;private set;}

    public void BeginEncounter(PlayerHealth target,BossHealth health)
    {
        externallySequenced=true;encounterHealth=health;maxHealth=health.maxHealth;
        StartBattle(target);dodgeChance=0;cycleDemonstration=false;
        CompletedEncounterCasts=0;SetWalking(false);
    }
    public bool CastEncounterSkill(int index,bool basicMelee=false)
    {
        if(!externallySequenced || !Active || IsDead || casting || !player || player.IsDead)return false;
        if(index==2 && !EncounterMeleeInRange)return false;
        EncounterMeleeLostRange=false;
        SetWalking(false);
        SelectedSkill=Mathf.Clamp(index,0,9);encounterBasicMelee=basicMelee;
        casting=true;elapsed=0;released=0;impactDone=false;aim=player.transform.position;
        slashVfx=-1;skillEffect=null;WaitingForPreview=false;previousTip=brush.brushTip.position;
        return true;
    }
    public void MoveInEncounter(Vector3 delta)
    {
        if(!Active || casting)return;
        SetWalking(delta.sqrMagnitude>.00001f);Move(delta);
    }
    public void EndEncounterDefeated(){if(!dying)BeginDeath();}
    void EncounterBrushStrike()
    {
        if(!EncounterMeleeInRange){EncounterMeleeLostRange=true;brush.ClearCombatPose();FinishCast();return;}
        const float segment=1.05f;float phase=elapsed/segment;int style=encounterStrikeStyle%5;
        brush.SetCombatPose(4+style,phase);
        if(phase>=.38f && slashVfx<0){slashVfx=0;CalligraphyMeleeEffects.Play(brush,style,inkMelee.vfxScale);}
        if(phase>=.38f && phase<.78f && released==0 &&
           CalligraphyPlayerHitGate.Touches(player,previousTip,brush.brushTip.position,inkMelee.hitRadius))
        {
            released=1;Vector3 d=Vector3.ProjectOnPlane(player.transform.position-transform.position,Vector3.up).normalized;
            EncounterMeleeContact?.Invoke(inkMelee.damage,d,brush.brushTip.position);
            ink.Burst(brush.brushTip.position,.7f,8);
        }
        if(phase>=1){encounterStrikeStyle++;FinishCast();}
    }
}
