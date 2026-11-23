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
        // 鎖血：開打之後（含逃走演出開始、在等玩家技能放完的這段）血量都不會低於一半。
        // 虛空連斬這類技能打到半血之後還有好幾十刀和爆炸，原本開始等待之後就不再擋，會一路打穿 50%
        // 只看 encounter.Running 不夠：老頭還沒變身（一般型態的 Brain 在打）時 Running 是 false，會被一路打死。
        // 改成只要 Boss 戰開打（FightActive）就鎖；關掉「半血逃走」（escapeAtHalfHealth）的版本才可以打到 50% 以下。
        bool fighting=Started||(encounter&&encounter.Running)||(boss&&boss.FightActive);
        bool locks=!encounter||encounter.escapeAtHalfHealth;
        if(locks && fighting && health.Health<health.maxHealth*.5f)health.SetHealth(health.maxHealth*.5f);
        if(Started || !encounter || !encounter.Running || health.Health>health.maxHealth*.5f)return;
        Started=true;
        StartCoroutine(EscapeWhenPlayerSkillEnds());
    }
    // 半血那一下常常是玩家的電影式技能（例如虛空裂斬）打出來的：技能還在接管角色與鏡頭時就開演，
    // 演出結束會把玩家還原成「技能中」的狀態而卡住。先讓老頭停手、無敵，等技能放完再開始逃走演出。
    IEnumerator EscapeWhenPlayerSkillEnds()
    {
        boss.SuspendForEscape();
        Phase="Waiting for player skill to finish";
        float until=Time.unscaledTime+10f;
        while(Time.unscaledTime<until && PlayerSkillPlaying())yield return null;
        yield return null;
        yield return Escape();
    }
    static bool PlayerSkillPlaying()
    {
        foreach(var skill in FindObjectsByType<VoidRendSkillDemo>(FindObjectsInactive.Exclude))
            if(skill.IsPlaying)return true;
        return false;
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
        CalligraphyAudio.EscapeTwist(root.position+Vector3.up);
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
        // 化成墨雲飛走：墨炸開一聲，墨雲身上掛一個一直循環的風聲（墨雲關掉 / 刪掉時自動停）
        CalligraphyAudio.EscapeBurst(cloudRoot.transform.position);
        CalligraphyAudio.AttachLoop(cloudRoot,"SFX/Ink/ink_flight_loop",.55f,1f,1f,60f);
        var route=new List<Vector3>{cloudRoot.transform.position};var anchorIndices=new List<int>();
        var navigation=new DojoInkFlightPath(transform);
        if(flightPath!=null)for(int waypoint=0;waypoint<flightPath.Length;waypoint++)
        {
            var point=flightPath[waypoint];if(!point)continue;
            var leg=SafeFind(navigation,route[route.Count-1],point.position);AvoidanceCorners+=Mathf.Max(0,leg.Count-1);
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
                var at=Drift(navigation,center,desired,cloudRoot.transform.position);
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
            var endpoint=Drift(navigation,c,c,cloudRoot.transform.position);
            cloudRoot.transform.position=endpoint;FlightSamples.Add(endpoint);
            if(i==anchorIndices.Count-1||anchorIndices[i+1]!=anchorIndices[i])ReachedWaypoint=anchorIndices[i];
        }
        Phase="Returning to player";
        Vector3 departing=cloudRoot.transform.position;
        Vector3 beyond=departureBeyondTorii?departureBeyondTorii.position:departing+cloudRoot.transform.forward*8;
        var departureRoute=SafeFind(navigation,departing,beyond);int departureIndex=0;
        if(follow)
        {
            follow.enabled=true;follow.yaw=Quaternion.LookRotation(Vector3.ProjectOnPlane(route[route.Count-1]-player.transform.position,Vector3.up)).eulerAngles.y;
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
                    cloudRoot.transform.position=Drift(navigation,next,next,cloudRoot.transform.position);
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
    // 防卡死：找不到避開障礙的飛行通道時（例如老頭半血時站在平台下方很低的地方），
    // 不要丟例外讓演出停在一半（玩家會被鎖住、鏡頭回不來），改成先垂直升到目標高度再飛；還是不行就直線飛過去。
    public bool UsedFallbackRoute{get;private set;}
    bool ignoreObstacles;
    List<Vector3> SafeFind(DojoInkFlightPath navigation,Vector3 from,Vector3 to)
    {
        try{return navigation.Find(from,to);}
        catch(System.InvalidOperationException e){Debug.LogWarning("[DojoElderEscape] "+e.Message+"，改用備用路線。",this);}
        UsedFallbackRoute=true;
        Vector3 rise=new Vector3(from.x,Mathf.Max(from.y,to.y),from.z);
        try
        {
            var path=navigation.Find(rise,to);
            if(navigation.Clear(from,rise)){path.Insert(0,rise);return path;}
        }
        catch(System.InvalidOperationException){}
        // 連升高都找不到：直線飛過去，飛行途中不再閃避障礙物（墨雲可能穿過牆，但演出一定會走完）
        ignoreObstacles=true;
        return new List<Vector3>{rise,to};
    }
    Vector3 Drift(DojoInkFlightPath navigation,Vector3 center,Vector3 desired,Vector3 previous)=>
        ignoreObstacles?desired:navigation.Drift(center,desired,previous);
    void Aim(Vector3 at,Vector3 target){if(camera)camera.transform.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));}
    void ReleasePlayer()
    {
        // 演出結束一律交還操作與跟隨鏡頭（不還原成演出開始時的狀態：那時玩家可能正在放技能，操作是暫時關閉的）
        if(ownsLock&&motor)
        {
            motor.UnlockActions();
            if(!motor.enabled)motor.enabled=true;
            var body=motor.GetComponent<CharacterController>();if(body&&!body.enabled)body.enabled=true;
            motor.inputEnabled=player&&!player.IsDead;motor.SuppressJump(.3f);ownsLock=false;
        }
        if(follow)follow.enabled=true;
    }
    void OnDisable()
    {
        if(health)health.OnDamaged-=OnDamaged;StopAllCoroutines();
        if(camera)Destroy(camera.gameObject);if(Cloud)Destroy(Cloud.gameObject);ReleasePlayer();
    }
}
