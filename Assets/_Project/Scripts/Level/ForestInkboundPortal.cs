using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Voluntary, visual-only post-centipede passage; existing boss AI and player movement remain owners.</summary>
[DefaultExecutionOrder(15000)]
public sealed class ForestInkboundPortal : MonoBehaviour
{
    public const string Resource="ScenePortals/PF_ForestInkboundPortal";
    public enum Stage {WaitingForDeath,Ready,Sinking,Loading,ArrivalHold,Falling,Complete,Cancelled}
    [Header("特效")]
    public GameObject verticalRunePrefab;
    public GameObject goldenBeaconPrefab;
    [Range(.2f,2)] public float runeScale=.8f;
    [Header("離場時序（秒）")]
    [Min(0)] public float afterDefeatDelay=3.2f;
    [Min(.2f)] public float idleSourceSeconds=3;
    [Min(.5f)] public float sinkSeconds=3;
    [Min(0)] public float restartAfterEntry=1;
    [Min(.2f)] public float finalTailSeconds=1;
    [Header("位置")]
    [Min(3)] public float nearbyDistance=6;
    [Min(.5f)] public float triggerRadius=1.8f;
    [Min(4)] public float arrivalHeight=18;
    public Stage State {get;private set;}
    public ForestPortalRune Rune {get;private set;}
    public Vector3 PortalPosition {get;private set;}
    public Vector3 ArrivalStart {get;private set;}
    public float EnteredAt {get;private set;}=-1;
    public float RestartedAt {get;private set;}=-1;
    public float SubmergedAt {get;private set;}=-1;
    public float FinishedAt {get;private set;}=-1;
    public float DroppedAt {get;private set;}=-1;
    public float LandedAt {get;private set;}=-1;
    public static ForestInkboundPortal Current {get;private set;}
    SkeletonCentipedeBossAI boss;
    PlayerHealth player;
    ThirdPersonController motor;
    CharacterController body;
    ThirdPersonCamera orbit;
    Transform cameraTarget,cameraAnchor;
    GameObject beacon;
    Renderer[] beaconSurfaces;
    MaterialPropertyBlock beaconBlock;
    readonly List<Behaviour> suspended=new();
    readonly List<(Renderer renderer,bool forced)> submergedSurfaces=new();
    bool originalInput,originalBody,locked,restarted;
    Vector3 entryPosition;
    PlayerState fallbackCarry;
    Animator sinkingAnimator;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic()=>Current=null;
    public static ForestInkboundPortal BeginAfterDefeat(SkeletonCentipedeBossAI defeated)
    {
        if(Current)return Current;
        var prefab=Resources.Load<GameObject>(Resource);
        if(!prefab){Debug.LogError("[Forest portal] Missing Resources/"+Resource);return null;}
        var flow=Instantiate(prefab).GetComponent<ForestInkboundPortal>();
        flow.name="Centipede defeated - passage to the painted town";Current=flow;flow.boss=defeated;
        StoryProgress.Current.centipedeDefeated=true;
        flow.StartCoroutine(flow.OpenAfterDeath());return flow;
    }
    IEnumerator OpenAfterDeath()
    {
        State=Stage.WaitingForDeath;
        yield return new WaitForSeconds(afterDefeatDelay);
        Vector3 at;
        while(!FindPosition(out at))yield return new WaitForSeconds(.5f);
        PortalPosition=at;transform.position=at;
        var root=new GameObject("Vertical Rune timeline");root.transform.SetParent(transform,false);root.transform.localPosition=Vector3.up*.06f;
        Rune=root.AddComponent<ForestPortalRune>();Rune.Initialize(verticalRunePrefab,runeScale);
        beacon=Instantiate(goldenBeaconPrefab,transform);beacon.name="Golden portal guide column";beacon.transform.localPosition=Vector3.zero;
        beaconSurfaces=beacon.GetComponentsInChildren<Renderer>();beaconBlock=new MaterialPropertyBlock();
        State=Stage.Ready;
    }
    bool FindPosition(out Vector3 at)
    {
        at=Vector3.zero;var p=PlayerHealth.Find();if(!p||p.IsDead)return false;
        Vector3 away=boss?Vector3.ProjectOnPlane(p.transform.position-boss.transform.position,Vector3.up):p.transform.forward;
        if(away.sqrMagnitude<.01f)away=Vector3.back;away.Normalize();
        for(int ring=0;ring<3;ring++)for(int i=0;i<24;i++)
        {
            float turn=i==0?0:((i+1)/2)*15*(i%2==0?1:-1);
            Vector3 candidate=p.transform.position+Quaternion.Euler(0,turn,0)*away*(nearbyDistance+ring*2);
            var hits=Physics.RaycastAll(candidate+Vector3.up*60,Vector3.down,130,~0,QueryTriggerInteraction.Ignore);
            var support=hits.Where(h=>h.collider is TerrainCollider&&h.normal.y>.86f).OrderBy(h=>h.distance).FirstOrDefault();
            if(!support.collider)continue;candidate=support.point;
            bool blocked=Physics.OverlapCapsule(candidate+Vector3.up*.3f,candidate+Vector3.up*2.1f,triggerRadius,~0,QueryTriggerInteraction.Ignore)
                .Any(c=>!(c is TerrainCollider)&&!c.transform.IsChildOf(p.transform));
            if(blocked)continue;
            Vector3 from=p.transform.position+Vector3.up*.3f,to=candidate+Vector3.up*1.1f;
            if(Physics.RaycastAll(from,(to-from).normalized,(to-from).magnitude,~0,QueryTriggerInteraction.Ignore)
                .Any(h=>!(h.collider is TerrainCollider)&&!h.transform.IsChildOf(p.transform)))continue;
            at=candidate;return true;
        }
        return false;
    }
    void Update()
    {
        if(Time.deltaTime<=0)return;
        if(State==Stage.Ready)
        {
            Rune.TickIdle(Time.deltaTime,idleSourceSeconds);
            var p=PlayerHealth.Find();
            if(!p||p.IsDead||GameManager.Instance&&GameManager.Instance.IsLoading)return;
            Vector3 delta=p.transform.position-PortalPosition;delta.y=0;
            if(delta.sqrMagnitude<=triggerRadius*triggerRadius&&Mathf.Abs(p.FeetY-PortalPosition.y)<.7f)
                StartCoroutine(SinkAndTravel(p));
        }
    }
    void Acquire(PlayerHealth p)
    {
        player=p;motor=p.GetComponent<ThirdPersonController>();body=p.GetComponent<CharacterController>();
        originalInput=motor.inputEnabled;originalBody=body.enabled;entryPosition=p.transform.position;
        SkillAimCancellation.Cancel();
        p.GetComponent<EnergyTetherSkill>()?.CancelForExternalTransition();
        suspended.Clear();submergedSurfaces.Clear();
        foreach(var skill in p.GetComponentsInChildren<MonoBehaviour>(true))
            if(skill.enabled&&(skill is VoidRendSkillDemo||skill is EnergyTetherSkill||skill is PhantomStrike||skill is LeapCleaveStrike||skill is ShieldSlideStrike||skill is SpearArts))
            {suspended.Add(skill);skill.enabled=false;}
        motor.LockActions();locked=true;motor.inputEnabled=false;motor.BeginScriptedMove();body.enabled=false;
        player.SetSkillInvulnerability(this,true);
    }
    IEnumerator SinkAndTravel(PlayerHealth p)
    {
        State=Stage.Sinking;EnteredAt=Time.time;Acquire(p);
        // Keep a standing pose while sinking instead of switching to the controller's
        // airborne pose merely because its collision capsule is temporarily disabled.
        sinkingAnimator=p.GetComponentsInChildren<Animator>().FirstOrDefault(a=>a.enabled&&a.avatar&&a.avatar.isHuman);
        if(sinkingAnimator)
        {
            foreach(var parameter in sinkingAnimator.parameters)
            {
                if(parameter.name=="Grounded")sinkingAnimator.SetBool(parameter.nameHash,true);
                if(parameter.name=="Speed"||parameter.name=="VerticalSpeed")sinkingAnimator.SetFloat(parameter.nameHash,0);
            }
            if(sinkingAnimator.HasState(0,Animator.StringToHash("Idle")))sinkingAnimator.Play("Idle",0,0);
            sinkingAnimator.Update(0);sinkingAnimator.enabled=false;
        }
        float highest=Mathf.Max(body.height,2);
        var posed=new Mesh();var points=new List<Vector3>();
        foreach(var r in p.GetComponentsInChildren<Renderer>())
        {
            if(r is SkinnedMeshRenderer skin)
            {skin.BakeMesh(posed);posed.GetVertices(points);foreach(var v in points)highest=Mathf.Max(highest,skin.transform.TransformPoint(v).y-PortalPosition.y);}
            else if(r is MeshRenderer)highest=Mathf.Max(highest,r.bounds.max.y-PortalPosition.y);
        }
        Destroy(posed);
        float depth=highest+.35f;
        orbit=Camera.main?Camera.main.GetComponent<ThirdPersonCamera>():null;
        if(orbit)
        {
            orbit.ClearLock();cameraTarget=orbit.target;
            cameraAnchor=new GameObject("Portal sinking camera anchor").transform;cameraAnchor.SetParent(transform,false);cameraAnchor.position=entryPosition;
            orbit.target=cameraAnchor;
        }
        float total=sinkSeconds+finalTailSeconds;
        while(Time.time-EnteredAt<total)
        {
            float t=Time.time-EnteredAt,u=Mathf.Clamp01(t/sinkSeconds);
            // Actual root moves below the terrain, while the camera stays at the surface.
            Vector3 center=new Vector3(PortalPosition.x,entryPosition.y,PortalPosition.z);
            p.transform.position=Vector3.Lerp(entryPosition,center,Mathf.SmoothStep(0,1,Mathf.Clamp01(t)))-Vector3.up*(depth*Mathf.SmoothStep(0,1,u));
            if(t<restartAfterEntry)Rune.TickIdle(Time.deltaTime,idleSourceSeconds);
            else
            {
                if(!restarted){restarted=true;RestartedAt=Time.time;Rune.SetTime(0,true);}
                if(t<sinkSeconds)Rune.SetTime(Mathf.Clamp01((t-restartAfterEntry)/Mathf.Max(.05f,sinkSeconds-restartAfterEntry))*idleSourceSeconds);
                else
                {
                    if(SubmergedAt<0)
                    {
                        SubmergedAt=Time.time;
                        // Conservative animated renderer bounds can grow during descent. Finish
                        // below the terrain and suppress any last hair/weapon/shadow fragments.
                        foreach(var r in p.GetComponentsInChildren<Renderer>())
                        {submergedSurfaces.Add((r,r.forceRenderingOff));r.forceRenderingOff=true;}
                    }
                    Rune.SetTime(Mathf.Lerp(idleSourceSeconds,Rune.SourceEnd+.05f,Mathf.Clamp01((t-sinkSeconds)/finalTailSeconds)));
                }
            }
            if(cameraAnchor)cameraAnchor.position=new Vector3(p.transform.position.x,entryPosition.y,p.transform.position.z);
            if(beaconSurfaces!=null)foreach(var r in beaconSurfaces)
            {r.GetPropertyBlock(beaconBlock);beaconBlock.SetFloat("_Intensity",1-Mathf.SmoothStep(0,1,u));r.SetPropertyBlock(beaconBlock);}
            yield return null;
        }
        Rune.SetTime(Rune.SourceEnd+.05f);FinishedAt=Time.time;
        if(!Application.CanStreamedLevelBeLoaded(InkboundTownJourney.Scene))
        {Debug.LogError("[Forest portal] InkboundTown_Exploration must be enabled in Build Settings.");Cancel();yield break;}
        fallbackCarry=PlayerState.Capture(p.gameObject);InkboundTownJourney.Register();
        State=Stage.Loading;DontDestroyOnLoad(gameObject);SceneManager.sceneLoaded+=Arrived;
        if(Rune)Rune.gameObject.SetActive(false);if(beacon)beacon.SetActive(false);
        GameManager.NextArrivalEvent="踏入陰陽符陣，來到畫中河鎮";
        if(GameManager.Instance)GameManager.Instance.LoadLevel(InkboundTownJourney.Scene);
        else SceneManager.LoadSceneAsync(InkboundTownJourney.Scene);
    }
    void Arrived(Scene scene,LoadSceneMode mode)
    {
        if(mode!=LoadSceneMode.Single)return;
        if(State!=Stage.Loading||scene.name!=InkboundTownJourney.Scene){Cancel();return;}
        // Old player belonged to the unloaded forest. Acquire the new scene's normal player rig.
        player=null;motor=null;body=null;orbit=null;cameraTarget=null;sinkingAnimator=null;suspended.Clear();submergedSurfaces.Clear();locked=false;
        if(cameraAnchor)Destroy(cameraAnchor.gameObject);cameraAnchor=null;
        StartCoroutine(DropIntoTown());
    }
    IEnumerator DropIntoTown()
    {
        State=Stage.ArrivalHold;
        var p=PlayerHealth.Find();
        if(!p){yield return null;p=PlayerHealth.Find();}
        if(!p){Debug.LogError("[Forest portal] Town player is missing.");Cancel();yield break;}
        Acquire(p);if(!GameManager.Instance)fallbackCarry?.Apply(p.gameObject);
        ArrivalStart=p.transform.position+Vector3.up*arrivalHeight;p.transform.position=ArrivalStart;
        yield return null;
        orbit=Camera.main?Camera.main.GetComponent<ThirdPersonCamera>():null;
        if(orbit){orbit.ClearLock();orbit.SnapToTarget();}
        while(GameManager.Instance&&GameManager.Instance.IsLoading)yield return null;
        State=Stage.Falling;DroppedAt=Time.time;body.enabled=originalBody;motor.EndScriptedMove(Vector3.zero);motor.ResetMotion();
        // Use the existing controller's gravity, collision and landing animation.
        float began=Time.time;
        while(p&&!p.IsDead&&(!motor.IsGrounded||Time.time-began<.15f)&&Time.time-began<8)yield return null;
        LandedAt=Time.time;Release(false);State=Stage.Complete;Destroy(gameObject);
    }
    void Release(bool restorePosition)
    {
        if(player)
        {
            if(restorePosition&&body&&!body.enabled)player.transform.position=entryPosition;
            if(motor){motor.EndScriptedMove(Vector3.zero);motor.ResetMotion();if(locked)motor.UnlockActions();motor.inputEnabled=originalInput;}
            if(body)body.enabled=originalBody;player.SetSkillInvulnerability(this,false);
        }
        if(sinkingAnimator)sinkingAnimator.enabled=true;sinkingAnimator=null;
        foreach(var skill in suspended)if(skill)skill.enabled=true;suspended.Clear();locked=false;
        foreach(var surface in submergedSurfaces)if(surface.renderer)surface.renderer.forceRenderingOff=surface.forced;submergedSurfaces.Clear();
        if(orbit&&cameraTarget){orbit.target=cameraTarget;orbit.SnapToTarget();}
        if(cameraAnchor)Destroy(cameraAnchor.gameObject);cameraAnchor=null;
    }
    public void Cancel(){State=Stage.Cancelled;Release(true);Destroy(gameObject);}
    void OnDestroy()
    {SceneManager.sceneLoaded-=Arrived;if(locked)Release(true);if(Current==this)Current=null;}
}
