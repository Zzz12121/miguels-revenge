using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>One-way 50% encounter exit. Owns only its cinematic camera, ink actor and input lease.</summary>
[DisallowMultipleComponent]
public sealed class DojoElderEscape : MonoBehaviour
{
    public GameObject casting,embers,nova,muzzle;
    public Transform departureBeyondTorii;
    public bool CloudHiddenAfterCameraReturn {get;private set;}
    public Transform[] flightPath;
    public float flightSpeed=11f,twistSeconds=1.3f;
    public float outdoorSway=1.65f,indoorSway=.18f;
    public readonly List<Vector3> FlightSamples=new();
    public int AvoidanceCorners {get;private set;}
    public bool Started {get;private set;}
    public bool Completed {get;private set;}
    public string Phase {get;private set;}="Waiting for half health";
    public DojoEscapeInkCloud Cloud {get;private set;}
    public int ReachedWaypoint {get;private set;}=-1;
    public float RearCameraHeight {get;private set;}
    BossHealth health;DojoCalligraphyEncounter encounter;DojoMasterBoss boss;
    PlayerHealth player;ThirdPersonController motor;ThirdPersonCamera follow;
    CinemachineCamera camera;
    bool ownsLock,oldInput,oldCameraEnabled;
    void Awake(){health=GetComponent<BossHealth>();encounter=GetComponent<DojoCalligraphyEncounter>();boss=GetComponent<DojoMasterBoss>();}
    void OnEnable(){health.OnDamaged+=OnDamaged;}
    void OnDamaged(float amount)
    {
        if(Started || !encounter || !encounter.Running || health.Health>health.maxHealth*.5f)return;
        Started=true;
        // The story transition also catches a single attack which crosses the threshold by a large amount.
        if(health.Health<health.maxHealth*.5f)health.SetHealth(health.maxHealth*.5f);
        StartCoroutine(Escape());
    }
    IEnumerator Escape()
    {
        Phase="Twisting into ink";boss.SuspendForEscape();
        player=PlayerHealth.Find();motor=player?player.GetComponent<ThirdPersonController>():null;
        follow=FindAnyObjectByType<ThirdPersonCamera>();
        if(motor){oldInput=motor.inputEnabled;motor.LockActions();ownsLock=true;motor.inputEnabled=false;motor.StopHorizontalMotion();}
        if(follow){oldCameraEnabled=follow.enabled;follow.enabled=false;}
        camera=new GameObject("Dojo half-health escape camera").AddComponent<CinemachineCamera>();camera.Priority=2000;
        var lens=camera.Lens;lens.FieldOfView=54;camera.Lens=lens;
        var form=encounter.Form;var root=form.transform;
        var cloudRoot=new GameObject("Agile airborne ink elder");Cloud=cloudRoot.AddComponent<DojoEscapeInkCloud>();
        Cloud.casting=casting;Cloud.embers=embers;Cloud.nova=nova;Cloud.muzzle=muzzle;
        cloudRoot.transform.position=root.position+Vector3.up*1.25f;
        Cloud.Begin();cloudRoot.transform.localScale=Vector3.one*.05f;
        var body=form.brush.animator.transform;var scale=body.localScale;var rotation=body.localRotation;
        var brushScale=form.brush.brushPivot.localScale;
        form.brush.enabled=false;form.brush.animator.enabled=false;
        foreach(var collider in form.GetComponentsInChildren<Collider>())collider.enabled=false;
        Vector3 initial=root.position+root.forward*6+Vector3.up*3;
        for(float t=0;t<twistSeconds;t+=Time.deltaTime)
        {
            float q=Mathf.Clamp01(t/twistSeconds);float e=Mathf.SmoothStep(0,1,q);
            body.localRotation=rotation*Quaternion.Euler(Mathf.Sin(q*8)*22,q*q*650,Mathf.Sin(q*11)*18);
            body.localScale=scale*(1-e*.98f);cloudRoot.transform.localScale=Vector3.one*e;
            form.brush.brushPivot.localScale=brushScale*(1-e);
            Aim(initial,cloudRoot.transform.position);yield return null;
        }
        foreach(var renderer in form.GetComponentsInChildren<Renderer>())renderer.enabled=false;
        form.brush.brushPivot.gameObject.SetActive(false);cloudRoot.transform.localScale=Vector3.one;
        var route=new List<Vector3>{cloudRoot.transform.position};var anchorIndices=new List<int>();
        var navigation=new DojoInkFlightPath(transform);
        if(flightPath!=null)for(int waypoint=0;waypoint<flightPath.Length;waypoint++)
        {
            var point=flightPath[waypoint];if(!point)continue;
            var leg=navigation.Find(route[route.Count-1],point.position);AvoidanceCorners+=Mathf.Max(0,leg.Count-1);
            foreach(var corner in leg){route.Add(corner);anchorIndices.Add(waypoint);}
        }
        Phase="Flying toward main house";float rearStarted=-1;
        for(int i=0;i<route.Count-1;i++)
        {
            var b=route[i];var c=route[i+1];
            float seconds=Mathf.Max(.2f,Vector3.Distance(b,c)/flightSpeed);
            for(float t=0;t<seconds;t+=Time.deltaTime)
            {
                float q=Mathf.Clamp01(t/seconds);var center=Vector3.Lerp(b,c,q);
                // Chest-height air travel: vertical flutter and sideways weaving, without a ground motor.
                Vector3 forward=(c-b).normalized;Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;
                bool indoors=center.z>77.5f&&center.z<101&&Mathf.Abs(center.x)<19.5f;
                float sway=indoors?indoorSway:outdoorSway;
                float envelope=Mathf.Sin(q*Mathf.PI);
                var desired=center+envelope*(Vector3.up*(Mathf.Sin(Time.time*4.3f)*.28f+Mathf.Sin(Time.time*9.1f)*.09f)+side*(Mathf.Sin(Time.time*2.8f)*sway+Mathf.Sin(Time.time*5.3f)*sway*.24f));
                var at=navigation.Drift(center,desired,cloudRoot.transform.position);
                FlightSamples.Add(at);
                cloudRoot.transform.SetPositionAndRotation(at,Quaternion.LookRotation(forward)*Quaternion.Euler(0,0,Mathf.Sin(Time.time*5)*20));
                if(at.z<80 && rearStarted<0)Aim(at+new Vector3(5,2.4f,-7),at);
                else
                {
                    if(rearStarted<0)rearStarted=Time.time;
                    float descent=Mathf.SmoothStep(0,1,(Time.time-rearStarted)/5f);
                    RearCameraHeight=Mathf.Lerp(32,8,descent);
                    bool rear=at.x<47;
                    Phase=rear?"Rear courtyard descending view":"Flying through side gate toward mountains";
                    Vector3 destination=rear?new Vector3(at.x-8,8,Mathf.Clamp(at.z+8,115,124)):at+new Vector3(-9,6,-8);
                    var pos=Vector3.Lerp(new Vector3(-5,32,120),destination,descent);
                    var target=indoors?new Vector3(19,1.3f,108):at;
                    Aim(pos,target);
                }
                yield return null;
            }
            var endpoint=navigation.Drift(c,c,cloudRoot.transform.position);
            cloudRoot.transform.position=endpoint;FlightSamples.Add(endpoint);
            if(i==anchorIndices.Count-1||anchorIndices[i+1]!=anchorIndices[i])ReachedWaypoint=anchorIndices[i];
        }
        Phase="Returning to player";
        Vector3 departing=cloudRoot.transform.position;
        Vector3 beyond=departureBeyondTorii?departureBeyondTorii.position:departing+cloudRoot.transform.forward*8;
        var departureRoute=navigation.Find(departing,beyond);int departureIndex=0;
        if(follow)
        {
            follow.enabled=oldCameraEnabled;follow.yaw=Quaternion.LookRotation(Vector3.ProjectOnPlane(route[route.Count-1]-player.transform.position,Vector3.up)).eulerAngles.y;
            follow.pitch=12;follow.SnapToTarget();
            var playerCam=follow.cinemachineCamera;var standby=playerCam.StandbyUpdate;playerCam.StandbyUpdate=CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
            Vector3 from=camera.transform.position;Quaternion fromRot=camera.transform.rotation;yield return null;
            for(float t=0;t<1.6f;t+=Time.deltaTime)
            {
                float q=Mathf.SmoothStep(0,1,t/1.6f);var state=playerCam.State;
                camera.transform.SetPositionAndRotation(Vector3.Lerp(from,state.GetFinalPosition(),q),Quaternion.Slerp(fromRot,state.GetFinalOrientation(),q));
                if(departureIndex<departureRoute.Count)
                {
                    var target=departureRoute[departureIndex];var next=Vector3.MoveTowards(cloudRoot.transform.position,target,flightSpeed*Time.deltaTime);
                    cloudRoot.transform.position=navigation.Drift(next,next,cloudRoot.transform.position);
                    if(Vector3.Distance(cloudRoot.transform.position,target)<.1f)departureIndex++;
                }
                yield return null;
            }
            playerCam.StandbyUpdate=standby;
        }
        Destroy(camera.gameObject);camera=null;
        // Only remove the actor after the player camera has taken over and can no longer see it.
        yield return null;cloudRoot.SetActive(false);CloudHiddenAfterCameraReturn=true;
        Phase="Player remarks on escape";bool done=false;
        StoryDialogue.Play(new[]{StoryDialogue.L("米格爾",player.transform,false,string.Empty,new Color(.65f,.85f,1))},()=>done=true);
        while(!done)yield return null;
        health.ShowBar=false;Completed=true;Phase="Escaped";
        Destroy(cloudRoot);ReleasePlayer();
    }
    void Aim(Vector3 at,Vector3 target){if(camera)camera.transform.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));}
    void ReleasePlayer()
    {
        if(ownsLock&&motor){motor.UnlockActions();motor.inputEnabled=oldInput&&!player.IsDead;motor.SuppressJump(.3f);ownsLock=false;}
        if(follow)follow.enabled=oldCameraEnabled;
    }
    void OnDisable()
    {
        if(health)health.OnDamaged-=OnDamaged;StopAllCoroutines();
        if(camera)Destroy(camera.gameObject);if(Cloud)Destroy(Cloud.gameObject);ReleasePlayer();
    }
}
