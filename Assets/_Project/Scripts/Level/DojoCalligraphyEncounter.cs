using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Production bridge from the dojo dialogue to the existing calligraphy skill presentation.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(220)]
public sealed class DojoCalligraphyEncounter : MonoBehaviour
{
    public CalligraphyBossCombat formPrefab;
    public float straightenSeconds=.9f,brushRevealSeconds=.65f,betweenAttacks=.9f;
    public float arrivalSeconds=.3f,afterimageLifetime=1f;
    public GameObject meleeShockwave;
    public bool EntranceComplete {get;private set;}
    public bool escapeAtHalfHealth=true;
    public float armsRevealSeconds=1.5f;
    public float brushAssemblySeconds=.8f;
    public Material brushAssemblyMaterial;
    public bool ArmsRaised {get;private set;}
    bool revealingArms;
    float revealWeight;
    Transform revealRight,revealRightLower,revealRightHand,revealLeft,revealLeftLower,revealLeftHand;
    [Range(.01f,1)] public float ultimateHealthThreshold=.25f;
    public CalligraphyBossCombat Form {get;private set;}
    public bool Transformed {get;private set;}
    public bool UltimateTriggered {get;private set;}
    public bool Running {get;private set;}
    public string Phase {get;private set;}="Drinking";
    public Vector3 LastTeleportFrom {get;private set;}
    public Vector3 LastTeleportTo {get;private set;}
    public int Teleports {get;private set;}
    public readonly List<int> CastHistory=new();
    DojoMasterBoss owner;BossHealth health;PlayerHealth player;
    Animator legacyAnimator;Renderer[] legacyRenderers;Collider[] legacyColliders;
    Transform transients,spine,hips,head;
    Quaternion straightStart,straightCorrection;float straightWeight;
    bool straightening;
    Renderer[] formRenderers;Material[][] formMaterials;Collider[] formColliders;
    Coroutine loop;
    bool entranceOverride;
    float entranceDistance,entranceHeight;
    Vector3 formHome;Quaternion formHomeRotation;
    bool Alive=>Running&&owner&&owner.FightActive&&!health.IsDead&&player&&!player.IsDead;

    void Awake()
    {
        owner=GetComponent<DojoMasterBoss>();health=GetComponent<BossHealth>();
        legacyAnimator=GetComponentInChildren<Animator>();legacyRenderers=GetComponentsInChildren<Renderer>();legacyColliders=GetComponentsInChildren<Collider>();
        foreach(var t in GetComponentsInChildren<Transform>())
        {if(t.name=="Spine")spine=t;if(t.name=="Hips")hips=t;if(t.name=="Head")head=t;}
        transients=new GameObject("Dojo transformation and teleport effects").transform;transients.SetParent(transform.parent,false);
    }
    void PrepareForm()
    {
        if(Form)return;
        Form=Instantiate(formPrefab,transform.position,transform.rotation,transform);Form.name="DojoMaster Calligraphy Form";
        Form.transform.localScale=Vector3.one;Form.externallySequenced=true;Form.encounterHealth=health;
        Form.encounterShockwave=meleeShockwave;
        Form.EncounterMeleeContact+=BrushContact;
        var display=Form.GetComponent<CalligraphyInkPresentation>();if(display)display.enabled=false;
        formRenderers=Form.GetComponentsInChildren<Renderer>();formMaterials=new Material[formRenderers.Length][];
        for(int i=0;i<formRenderers.Length;i++)formMaterials[i]=formRenderers[i].sharedMaterials;
        formColliders=Form.GetComponentsInChildren<Collider>();
        foreach(var c in formColliders)c.enabled=false;
        Form.brush.showcase=false;Form.brush.brushPivot.gameObject.SetActive(false);
        Form.brush.PlayAnimation(System.Array.IndexOf(Form.brush.animationStates,"standing_relax"));
    }
    public IEnumerator TransformAfterDialogue(Transform flask)
    {
        if(Transformed)yield break;
        if(!formPrefab){Debug.LogError("Dojo calligraphy form is missing",this);yield break;}
        Phase="Straightening";if(legacyAnimator)legacyAnimator.speed=0;
        straightening=spine&&hips&&head;straightStart=spine?spine.rotation:Quaternion.identity;
        straightCorrection=straightening?Quaternion.FromToRotation(head.position-hips.position,Vector3.up):Quaternion.identity;
        for(float t=0;t<straightenSeconds;t+=Time.deltaTime){straightWeight=Mathf.SmoothStep(0,1,t/Mathf.Max(.01f,straightenSeconds));yield return null;}
        straightWeight=1;yield return null;Phase="Throwing flask";
        if(flask)
        {
            flask.SetParent(transients,true);Vector3 from=flask.position;Quaternion rotation=flask.rotation;
            Vector3 to=transform.position+transform.right*3+transform.forward*1.5f;
            CalligraphyAudio.FlaskThrow(from);
            for(float t=0;t<.55f;t+=Time.deltaTime)
            {float q=t/.55f;flask.position=Vector3.Lerp(from,to,q)+Vector3.up*(Mathf.Sin(q*Mathf.PI)*1.1f);flask.rotation=rotation*Quaternion.Euler(q*420,q*180,0);yield return null;}
            CalligraphyAudio.FlaskLand(to);
            Destroy(flask.gameObject);
        }
        Phase="Opening arms";PrepareForm();
        Form.ink.Burst(transform.position+Vector3.up,1.5f,24);
        foreach(var r in legacyRenderers)if(r)r.enabled=false;
        foreach(var c in legacyColliders)if(c)c.enabled=false;
        if(legacyAnimator)legacyAnimator.enabled=false;straightening=false;
        Form.brush.enabled=false;
        var b=Form.brush;
        revealRight=b.rightUpper;revealRightLower=b.rightLower;revealRightHand=b.rightHand;
        revealLeft=b.leftUpper;revealLeftLower=b.leftLower;revealLeftHand=b.leftHand;
        revealingArms=true;
        for(float t=0;t<armsRevealSeconds;t+=Time.deltaTime)
        {revealWeight=Mathf.SmoothStep(0,1,t/(armsRevealSeconds*.65f));yield return null;}
        ArmsRaised=true;revealWeight=1;Phase="Brush reveal";
        Form.brush.brushPivot.gameObject.SetActive(true);
        Vector3 brushScale=Form.brush.brushPivot.localScale;
        var manifestation=DojoBrushManifestation.Begin(Form.brush.brushPivot,brushAssemblyMaterial);
        CalligraphyAudio.BrushReveal(revealRightHand.position,brushAssemblySeconds);
        for(float t=0;t<brushAssemblySeconds;t+=Time.deltaTime)
        {
            float q=Mathf.SmoothStep(0,1,t/brushAssemblySeconds);
            Form.brush.brushPivot.position=revealRightHand.position;
            Form.brush.brushPivot.rotation=Quaternion.LookRotation((Form.transform.right*.6f+Vector3.up).normalized,Form.transform.forward);
            Form.brush.brushPivot.localScale=brushScale;manifestation.SetProgress(q);
            Vector3 chest=Form.transform.position+Vector3.up*1.2f,hand=revealRightHand.position;
            for(int i=0;i<3;i++)
            {
                float u=Mathf.Repeat(t*1.3f+i*.31f,1);Vector3 point=Vector3.Lerp(chest,hand,u)+Form.transform.forward*(Mathf.Sin(u*Mathf.PI)*.25f);
                Form.ink.Mist(point,(hand-chest).normalized*.3f,.13f,.4f);
            }
            Form.ink.Mist(Form.brush.brushPivot.TransformPoint(0,0,q*manifestation.Reach),Vector3.up*.15f,.16f,.4f);yield return null;
        }
        manifestation.SetProgress(1);manifestation.Complete();
        CalligraphyAudio.BrushComplete(Form.brush.brushPivot.position);
        Form.brush.brushPivot.localScale=brushScale;
        yield return new WaitForSeconds(.3f);
        revealingArms=false;Form.brush.enabled=true;
        yield return new WaitForSeconds(.15f);
        foreach(var c in formColliders)if(c)c.enabled=true;
        Transformed=true;Phase="Ready";formHome=Form.transform.position;formHomeRotation=Form.transform.rotation;
    }
    void LateUpdate()
    {
        if(straightening&&spine)spine.rotation=Quaternion.Slerp(Quaternion.identity,straightCorrection,straightWeight)*straightStart;
        if(revealingArms)
        {
            RaiseArm(revealRight,revealRightLower,revealRightHand,1);
            RaiseArm(revealLeft,revealLeftLower,revealLeftHand,-1);
        }
    }
    void RaiseArm(Transform upper,Transform lower,Transform hand,float side)
    {
        if(!upper||!lower||!hand)return;
        var direction=(Form.transform.right*side+Vector3.up).normalized;
        upper.rotation=Quaternion.Slerp(upper.rotation,Quaternion.FromToRotation(lower.position-upper.position,direction)*upper.rotation,revealWeight);
        lower.rotation=Quaternion.Slerp(lower.rotation,Quaternion.FromToRotation(hand.position-lower.position,direction)*lower.rotation,revealWeight);
    }
    IEnumerator Materialize(float seconds)
    {
        var ink=Form.ink.originalEchoMaterial;
        if(ink)foreach(var r in formRenderers){var mats=r.sharedMaterials;for(int j=0;j<mats.Length;j++)mats[j]=ink;r.sharedMaterials=mats;}
        for(float t=0;t<seconds;t+=Time.deltaTime)
        {
            float q=Mathf.Clamp01(t/Mathf.Max(.01f,seconds));
            foreach(var r in formRenderers)if(r)CalligraphyGeometry.Tint(r,new Color(.003f,.006f,.009f),q,1-q);
            Form.ink.Mist(Form.transform.position+Vector3.up*q*2,Vector3.up*.2f,.55f,.6f);yield return null;
        }
        RestoreMaterials();
    }
    void RestoreMaterials()
    {
        if(formRenderers==null||formMaterials==null)return;
        for(int i=0;i<formRenderers.Length&&i<formMaterials.Length;i++)if(formRenderers[i]&&formMaterials[i]!=null){formRenderers[i].sharedMaterials=formMaterials[i];formRenderers[i].SetPropertyBlock(null);}
    }
    public void BeginFight(PlayerHealth target)
    {
        if(!Transformed||!Form)return;player=target;Running=true;UltimateTriggered=false;CastHistory.Clear();
        Form.BeginEncounter(player,health);if(loop!=null)StopCoroutine(loop);loop=StartCoroutine(Sequence());
    }
    IEnumerator Sequence()
    {
        // Flying ink and four-stroke are follow-ups, not entries in the regular cycle.
        int next=2;yield return new WaitForSeconds(.8f);
        EntranceComplete=false;
        entranceDistance=Form.leapSlam.jumpDistance;entranceHeight=Form.leapSlam.jumpHeight;entranceOverride=true;
        Form.leapSlam.jumpDistance=100;Form.leapSlam.jumpHeight=6;
        yield return Cast(6);
        RestoreEntrance();EntranceComplete=true;
        yield return new WaitForSeconds(betweenAttacks);
        yield return Cycle(next);
    }
    // 玩家的電影式技能（虛空裂斬）把老頭挑到空中時：出招 / 瞬移 / 走路的流程先停下，
    // 不然瞬移會把書法分身直接放回地面。技能放完再從一般循環接回來（不重播進場跳砸）。
    int heldDepth;
    public bool HeldBySkill=>heldDepth>0;
    public void HoldForPlayerSkill(bool held)
    {
        if(held)
        {
            if(heldDepth++>0)return;
            if(loop!=null){StopCoroutine(loop);loop=null;}
            RestoreEntrance();RestoreMaterials();
            if(Form&&Form.gameObject.activeInHierarchy)foreach(var c in formColliders)if(c)c.enabled=true;
            return;
        }
        if(heldDepth==0||--heldDepth>0)return;
        if(Running&&Form&&loop==null)loop=StartCoroutine(ResumeAfterSkill());
    }
    IEnumerator ResumeAfterSkill()
    {
        EntranceComplete=true;
        yield return new WaitForSeconds(betweenAttacks);
        yield return Cycle(2);
    }
    IEnumerator Cycle(int next)
    {
        while(Alive)
        {
            if(!escapeAtHalfHealth&&!UltimateTriggered&&health.Health<=health.maxHealth*ultimateHealthThreshold)
            {UltimateTriggered=true;yield return Cast(9);yield return new WaitForSeconds(betweenAttacks);continue;}
            yield return Cast(next);next=next==8?2:next+1;
            if(!Alive)yield break;yield return new WaitForSeconds(betweenAttacks);
            if(!escapeAtHalfHealth&&!UltimateTriggered&&health.Health<=health.maxHealth*ultimateHealthThreshold)continue;
            bool teleported=false;
            if(next%2==0 || Flat(player.transform.position-Form.transform.position).magnitude>owner.flashStepDistance)
            {yield return TeleportBehindPlayer();teleported=true;}
            else
            {
                Phase="Approaching for brush melee";float until=Time.time+2.2f;
                while(Alive&&Time.time<until&&Flat(player.transform.position-Form.transform.position).magnitude>Form.meleeDistance*.8f)
                {Vector3 d=Flat(player.transform.position-Form.transform.position).normalized;Form.transform.rotation=Quaternion.LookRotation(d);Form.MoveInEncounter(d*Form.moveSpeed*Time.deltaTime);yield return null;}
                if(Alive&&Flat(player.transform.position-Form.transform.position).magnitude>Form.meleeDistance){yield return TeleportBehindPlayer();teleported=true;}
            }
            if(Alive)yield return Cast(teleported?SelectTeleportFollowup():2,!teleported);
            yield return new WaitForSeconds(betweenAttacks);
        }
    }
    IEnumerator Cast(int index,bool melee=false)
    {
        if(index==2 && melee && !Form.EncounterMeleeInRange){index=(rangedFallback++%2);melee=false;}
        if(!Alive)yield break;Phase=melee?"Brush melee":CalligraphyBossCombat.SkillNames[index];
        if(!Form.CastEncounterSkill(index,melee))yield break;
        CastHistory.Add(melee?-1:index);
        while(Alive&&Form.IsCasting)yield return null;
        if(index==2&&!melee&&Alive&&Form.EncounterClonesRemaining>0)
        {
            Phase="Waiting for ink doubles to disappear";
            while(Alive&&Form.EncounterClonesRemaining>0)yield return null;
        }
        if(Alive && index==2 && melee && Form.EncounterMeleeLostRange)yield return Cast(rangedFallback++%2);
    }
    int rangedFallback;
    public int SelectTeleportFollowup()=>Random.value<.5f?2:1;
    public IEnumerator TeleportBehindPlayer()
    {
        if(!Alive)yield break;Phase="Ink teleport departure";
        Vector3 p=player.transform.position,back=-Flat(player.transform.forward).normalized;
        if(back.sqrMagnitude<.01f)back=Vector3.back;
        Vector3 side=Vector3.Cross(Vector3.up,back),dest=Form.transform.position;
        bool found=false;
        foreach(var offset in new[]{back*2.6f,side*2.6f,-side*2.6f,-back*2.6f})
        {
            Vector3 candidate=p+offset;
            if(Flat(candidate-owner.arenaCenter).magnitude>owner.arenaRadius-1)continue;
            candidate=CalligraphyGeometry.Ground(candidate,Form.transform);
            bool blocked=false;
            foreach(var c in Physics.OverlapCapsule(candidate+Vector3.up*.6f,candidate+Vector3.up*1.8f,.42f,~0,QueryTriggerInteraction.Ignore))
                if(!c.transform.IsChildOf(transform)&&!c.GetComponentInParent<PlayerHealth>()){blocked=true;break;}
            if(!blocked){dest=candidate;found=true;break;}
        }
        if(!found)yield break;
        LastTeleportFrom=Form.transform.position;LastTeleportTo=dest;Teleports++;
        DojoInkSilhouette.Create(Form.brush,Form.ink.originalEchoMaterial,transients,afterimageLifetime);
        Form.ink.Burst(LastTeleportFrom+Vector3.up,1.1f,16);
        CalligraphyAudio.Teleport(LastTeleportFrom);
        foreach(var c in formColliders)c.enabled=false;
        Form.transform.SetPositionAndRotation(dest,Quaternion.LookRotation(Flat(p-dest).normalized));
        Phase="Ink teleport materializing";Form.ink.Burst(dest+Vector3.up*.5f,1.2f,16);
        yield return Materialize(arrivalSeconds);
        foreach(var c in formColliders)c.enabled=true;
        Phase="Teleport ready";
    }
    void BrushContact(float damage,Vector3 direction,Vector3 point)=>owner.DeliverBrushHit(damage,direction,point);
    public void StopFight(bool defeated=false)
    {
        revealingArms=false;
        RestoreEntrance();
        Running=false;if(loop!=null)StopCoroutine(loop);loop=null;RestoreMaterials();
        if(Form)
        {
            Form.StopBattle();Form.brush.showcase=false;var display=Form.GetComponent<CalligraphyInkPresentation>();if(display)display.enabled=false;
            if(defeated)Form.EndEncounterDefeated();
        }
        if(transients)foreach(Transform t in transients){t.gameObject.SetActive(false);Destroy(t.gameObject);}
        Phase=defeated?"Defeated":"Waiting for rematch";
    }
    void RestoreEntrance()
    {
        if(entranceOverride&&Form){Form.leapSlam.jumpDistance=entranceDistance;Form.leapSlam.jumpHeight=entranceHeight;}
        entranceOverride=false;
    }
    public void ResetForRematch()
    {
        StopFight();UltimateTriggered=false;
        if(Form){Form.transform.SetPositionAndRotation(formHome,formHomeRotation);foreach(var c in formColliders)c.enabled=true;}
    }
    void OnDisable(){StopFight();straightening=false;}
    void OnDestroy(){if(transients)Destroy(transients.gameObject);}
    static Vector3 Flat(Vector3 p){p.y=0;return p;}
}
