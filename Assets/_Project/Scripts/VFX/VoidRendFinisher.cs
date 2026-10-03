using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed partial class VoidRendSkillDemo
{
    [Header("Finisher choreography")]
    [Min(1)] public float effectRangeMultiplier = 4f;
    [Min(0.1f)] public float chargeOrbitSeconds = 3.5f;
    public float launchHeight = 4.5f;
    [Min(1f)] public float landingDistanceBehind = 3.2f;
    [Min(8f)] public float recoveryCameraDistance = 11.5f;
    public Material smokeMaterial;
    public Material refractionMaterial;
    public UniversalRenderPipelineAsset previewPipeline;
    [Tooltip("Explicit opt-in: movement/AI components to suspend during this preview.")]
    public Behaviour[] targetControllers = new Behaviour[0];
    public UnityEvent onTargetLocked = new UnityEvent();
    public UnityEvent onTargetReleased = new UnityEvent();
    public UnityEvent onFinisherImpact = new UnityEvent();
    public string Stage { get; private set; } = "Ready";
    public float Elapsed { get; private set; }
    public bool IsPlaying => playing;
    public bool TargetLocked => targetLocked;
    public int CompletedRuns { get; private set; }

    Vector3 victimHome, victimHeld, arenaCenter, cameraFocus;
    Vector3 victimLanding, previewVictimStart;
    bool completedLanding;
    Quaternion victimRotation;
    float cameraYaw = 160, cameraRadius = 8, cameraHeight = 2.2f, cinematicFov = 48, cameraRoll;
    float charge, distortion, sequenceStart, stageTime;
    bool targetLocked, cinematicCamera;
    RenderPipelineAsset previousPipeline;
    bool pipelineChanged;
    Rigidbody victimBody;
    bool wasKinematic, usedGravity;
    Vector3 oldVelocity, oldAngularVelocity;
    readonly Dictionary<Behaviour, bool> suspended = new Dictionary<Behaviour, bool>();
    readonly Dictionary<Transform, Quaternion> poseRest = new Dictionary<Transform, Quaternion>();
    Transform leftUpper, leftFore, leftHand, weapon, sheath;
    Vector3 weaponRestPosition;
    Quaternion weaponRestRotation;
    Animator casterAnimator;
    bool animatorWasEnabled;
    float priorTimeScale;
    ParticleSystem chargeSmoke;
    Transform pelvis, leftThigh, leftCalf, leftFoot, rightThigh, rightCalf, rightFoot;
    Vector3 pelvisRest;

    void InitializeFinisher()
    {
        if (target != null) { victimHome = target.position; previewVictimStart = target.position; victimRotation = target.rotation; }
        if (caster != null)
        {
            leftUpper = FindDeepChild(caster, "Bip001 L UpperArm");
            leftFore = FindDeepChild(caster, "Bip001 L Forearm");
            leftHand = FindDeepChild(caster, "Bip001 L Hand");
            weapon = FindDeepChild(caster, "VoidRend_KatanaSocket");
            sheath = FindDeepChild(caster, "VoidRend_Sheath");
            pelvis = FindDeepChild(caster, "Bip001 Pelvis");
            if(pelvis)pelvisRest=pelvis.localPosition;
            leftThigh=FindDeepChild(caster,"Bip001 L Thigh");leftCalf=FindDeepChild(caster,"Bip001 L Calf");leftFoot=FindDeepChild(caster,"Bip001 L Foot");
            rightThigh=FindDeepChild(caster,"Bip001 R Thigh");rightCalf=FindDeepChild(caster,"Bip001 R Calf");rightFoot=FindDeepChild(caster,"Bip001 R Foot");
            foreach (Transform bone in caster.GetComponentsInChildren<Transform>())
                if (bone.name.StartsWith("Bip001")) poseRest[bone] = bone.localRotation;
            if (weapon) { weaponRestPosition = weapon.localPosition; weaponRestRotation = weapon.localRotation; }
            casterAnimator = caster.GetComponentInChildren<Animator>();
        }
        if (previewPipeline != null)
        {
            previousPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = previewPipeline;
            pipelineChanged = true;
        }
        SetDistortion(0);
    }

    void LockVictim()
    {
        victimHome = target.position;
        victimRotation = target.rotation;
        victimHeld = victimHome;
        victimBody = target.GetComponent<Rigidbody>();
        if (victimBody)
        {
            wasKinematic = victimBody.isKinematic; usedGravity = victimBody.useGravity;
            oldVelocity = victimBody.linearVelocity; oldAngularVelocity = victimBody.angularVelocity;
            victimBody.isKinematic = true; victimBody.useGravity = false;
        }
        suspended.Clear();
        foreach (Behaviour controller in targetControllers)
            if (controller && controller != this && !suspended.ContainsKey(controller))
            { suspended.Add(controller, controller.enabled); controller.enabled = false; }
        var animator = target.GetComponentInChildren<Animator>();
        if (animator && !suspended.ContainsKey(animator)) { suspended.Add(animator, animator.enabled); animator.enabled = false; }
        targetLocked = true;
        onTargetLocked.Invoke();
    }

    void ReleaseVictim(bool restorePosition = true)
    {
        if (!targetLocked) return;
        if (target) { target.position = restorePosition ? victimHome : victimLanding; target.rotation = victimRotation; }
        if (victimBody)
        {
            victimBody.isKinematic = wasKinematic; victimBody.useGravity = usedGravity;
            if (!wasKinematic) { victimBody.linearVelocity = oldVelocity; victimBody.angularVelocity = oldAngularVelocity; }
        }
        foreach (var pair in suspended) if (pair.Key) pair.Key.enabled = pair.Value;
        suspended.Clear(); targetLocked = false;
        onTargetReleased.Invoke();
    }

    void SetStage(string stage) { Stage = stage; stageTime = Time.unscaledTime; }
    void SetDistortion(float amount)
    {
        distortion = amount;
        if (refractionMaterial) { refractionMaterial.SetFloat("_Strength", amount); refractionMaterial.SetFloat("_Clock", Time.unscaledTime); }
    }

    IEnumerator PlaySequence()
    {
        playing = true; priorTimeScale = Time.timeScale; sequenceStart = Time.unscaledTime;
        ResetPreviewState();
        // Reset the isolated demo only when the next replay actually begins.
        if(completedLanding) { target.position=previewVictimStart; completedLanding=false; }
        LockVictim();
        if (casterAnimator) { animatorWasEnabled = casterAnimator.enabled; casterAnimator.enabled = false; }
        cinematicCamera = true;
        arenaCenter = new Vector3(victimHome.x, casterStartPosition.y, victimHome.z);
        cameraFocus = arenaCenter + Vector3.up * 1.5f;
        cameraYaw = 155; cameraRadius = 8; cameraHeight = 2.2f; cinematicFov = 48;
        SetStage("Launch");
        Vector3 approach = arenaCenter + Vector3.back * 0.8f;
        SpawnArc(victimHome, Quaternion.Euler(0, 0, 80), 3.2f, -100, 100, .18f, .55f, pale);
        PunchCamera(.65f, 5); Flash(cyan, .15f);
        for (float t = 0; t < .6f; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / .6f);
            victimHeld = victimHome + Vector3.up * (launchHeight * (1 - Mathf.Pow(1-u, 3)));
            caster.position = Vector3.Lerp(casterStartPosition, approach, Mathf.SmoothStep(0,1,u));
            yield return null;
        }
        victimHeld = victimHome + Vector3.up * launchHeight;
        caster.position = arenaCenter;
        caster.rotation = Quaternion.identity;
        chargeSmoke = CreateSmoke(caster.position, true, 3.5f, .38f);
        chargeSmoke.transform.SetParent(caster, true);
        SetStage("ChargeOrbit");
        float nextParticle = 0;
        for (float t = 0; t < chargeOrbitSeconds; t += Time.unscaledDeltaTime)
        {
            float u = t / chargeOrbitSeconds;
            charge = u;
            cameraYaw = Mathf.Lerp(155, 0, Mathf.SmoothStep(0,1,u));
            cameraRadius = Mathf.Lerp(7, 4.5f, u); cameraHeight = Mathf.Lerp(2.1f,1.35f,u);
            cameraFocus = caster.position + Vector3.up * 1.5f;
            cinematicFov = Mathf.Lerp(48, 42, u);
            if (t >= nextParticle)
            {
                nextParticle += .1f;
                SpawnParticles(caster.position + Vector3.up * 1.4f, 6, .7f, .65f, .008f, .024f, cyan);
            }
            SetDistortion(.035f * u);
            yield return null;
        }
        chargeSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(chargeSmoke.gameObject, 4);
        SetStage("Punch"); charge = 1;
        Vector3 fist = caster.position + Vector3.up*1.8f + caster.forward*.65f;
        SpawnRing(fist, .2f, 4.7f * effectRangeMultiplier, .26f, .95f, pale);
        SpawnParticles(fist, 160, 1.4f, 16, .02f, .12f, cyan);
        CreateSmoke(fist, false, 2.5f, 1.4f);
        Flash(pale, .68f); PunchCamera(.9f, 12); SetDistortion(.85f);
        yield return Wait(.42f);
        SetStage("DashLattice");
        Vector3 center = victimHeld;
        cameraFocus = center - Vector3.up*1.2f;
        Vector3 last = caster.position;
        int cuts = 22;
        for (int i=0; i<cuts; i++)
        {
            float angle = (i * 137.508f + 24) * Mathf.Deg2Rad;
            float radius = teleportRadius * effectRangeMultiplier * (i%3 == 0 ? .8f : .57f);
            Vector3 next = arenaCenter + new Vector3(Mathf.Cos(angle)*radius, 2 + (i%5)*1.05f, Mathf.Sin(angle)*radius);
            CreateAfterimage(); poseSide = (i%2 == 0 ? 1 : -1);
            // Geometry is emitted from the exact swept character path, with a advancing head.
            const float dashDuration = .15f;
            Vector3 previous = last + Vector3.up*.9f;
            var growing=SpawnRibbon(new[]{previous,previous},slashMaterial,.13f,2.1f,pale);
            var halo=SpawnRibbon(new[]{previous,previous},slashMaterial,.42f,2.1f,new Color(.15f,.65f,1,.28f));
            for(float t=0;t<dashDuration;t+=Time.unscaledDeltaTime)
            {
                float u = Mathf.Clamp01(t/dashDuration);
                caster.position = Vector3.Lerp(last, next, u);
                Vector3 head = caster.position + Vector3.up*.9f;
                growing.SetPosition(1,head);halo.SetPosition(1,head);
                caster.rotation = Quaternion.LookRotation((next-last).normalized, Vector3.up);
                cameraYaw = Mathf.Lerp(0, 300, (i+u)/cuts);
                cameraRadius = Mathf.Lerp(7.2f, 9f, Mathf.Sin((i+u)/cuts*Mathf.PI));
                cameraHeight = 2.2f + Mathf.Sin(i*.4f)*.7f;
                cameraRoll = Mathf.Sin((i+u)*1.2f)*7;
                cinematicFov = 64;
                SetDistortion(.2f + .18f*Mathf.Sin((i+u)*3));
                yield return null;
            }
            caster.position = next;
            growing.SetPosition(1,next+Vector3.up*.9f);halo.SetPosition(1,next+Vector3.up*.9f);
            FracturedCut(last+Vector3.up*.9f,next+Vector3.up*.9f,.065f,8.4f,true);
            if (i%3==0)
            {
                CreateSmoke(Vector3.Lerp(last,next,.5f),false,2.3f,.8f);
                SpawnParticles(center, 18, 1.2f, 7, .025f,.08f,pale);
                PunchCamera(.3f, 2); Flash(pale,.08f);
            }
            last=next;
            yield return Wait(.04f);
        }
        SetStage("SheatheOrbit");
        CreateAfterimage(); caster.position = arenaCenter; caster.rotation = Quaternion.identity;
        float yawStart=cameraYaw;
        float radiusStart=cameraRadius, heightStart=cameraHeight, rollStart=cameraRoll;
        Vector3 focusStart=cameraFocus, heldStart=victimHeld;
        victimLanding=caster.position-caster.forward*landingDistanceBehind+caster.right*1.3f;
        victimLanding.y=victimHome.y;
        Vector3 recoveryFocus=caster.position+Vector3.up*3.0f;
        for(float t=0;t<2.4f;t+=Time.unscaledDeltaTime)
        {
            float u=t/2.4f;
            cameraYaw=Mathf.Lerp(yawStart,360,Mathf.SmoothStep(0,1,u));
            float ease=Mathf.SmoothStep(0,1,u);
            cameraRadius=Mathf.Lerp(radiusStart,recoveryCameraDistance,ease); cameraHeight=Mathf.Lerp(heightStart,1.6f,ease);
            cameraFocus=Vector3.Lerp(focusStart,recoveryFocus,ease);
            cameraRoll=Mathf.Lerp(rollStart,0,ease); cinematicFov=Mathf.Lerp(64,60,ease);
            victimHeld=Vector3.Lerp(heldStart,victimLanding+Vector3.up*launchHeight,ease);
            SetDistortion(.18f + Mathf.Sin(t*7)*.08f);
            yield return null;
        }
        cameraYaw=360; cameraRadius=recoveryCameraDistance; cameraHeight=1.6f;
        cameraFocus=recoveryFocus; cinematicFov=60; cameraRoll=0;
        victimHeld=victimLanding+Vector3.up*launchHeight;
        SetStage("FinalBreak");
        onFinisherImpact.Invoke();
        SpawnSpaceRift(center);
        for(int i=0;i<12;i++)
        {
            Vector3 a=Random.onUnitSphere*effectRangeMultiplier*4;
            FracturedCut(center-a,center+a,.16f,2.2f,true);
        }
        SpawnRing(arenaCenter+.1f*Vector3.up,.5f,4.7f*effectRangeMultiplier,.4f,1.5f,pale);
        CreateSmoke(arenaCenter+Vector3.up,false,3.2f,2.4f);
        SpawnParticles(center,150,1.5f,18,.03f,.15f,pale);
        Flash(Color.white,.94f); PunchCamera(1.1f,9); SetDistortion(1);
        yield return Wait(.12f);
        for(float t=0;t<1.6f;t+=Time.unscaledDeltaTime)
        {
            float u=t/1.6f;
            victimHeld=Vector3.Lerp(victimLanding+Vector3.up*launchHeight,victimLanding,u*u);
            SetDistortion((1-u)*.35f);
            yield return null;
        }
        ReleaseVictim(false); completedLanding=true; SetDistortion(0); charge=0; cameraRoll=0;
        SetStage("Complete"); playing=false; CompletedRuns++;
        if(casterAnimator) casterAnimator.enabled=animatorWasEnabled;
        Time.timeScale=priorTimeScale;
        if(loopPreview) { yield return Wait(loopDelay+2); PlaySkill(); }
    }

    void UpdateFinisherCamera(Vector3 shake,Vector3 rotationShake)
    {
        if(targetLocked && target) { target.position=victimHeld; target.rotation=victimRotation; }
        if(playing) Elapsed=Time.unscaledTime-sequenceStart;
        if(refractionMaterial) refractionMaterial.SetFloat("_Clock",Time.unscaledTime);
        if(!cinematicCamera) return;
        float a=cameraYaw*Mathf.Deg2Rad;
        Vector3 p=cameraFocus+new Vector3(Mathf.Sin(a)*cameraRadius,cameraHeight,Mathf.Cos(a)*cameraRadius);
        skillCamera.transform.position=p+shake;
        skillCamera.transform.rotation=Quaternion.LookRotation(cameraFocus-p)*Quaternion.Euler(rotationShake+Vector3.forward*cameraRoll);
        skillCamera.fieldOfView=cinematicFov-fovKick;
    }

    void ApplyFinisherPose()
    {
        foreach(var p in poseRest) if(p.Key) p.Key.localRotation=p.Value;
        float t=Time.unscaledTime-stageTime;
        Vector3 rh=new Vector3(.35f,1.6f,.2f), lh=new Vector3(-.35f,1.55f,.12f);
        float spin=0;
        if(Stage=="Launch") rh=new Vector3(.15f,Mathf.Lerp(.95f,2.65f,Mathf.Clamp01(t/.5f)),.5f);
        if(Stage=="ChargeOrbit")
        {
            float flourish=Mathf.Clamp01(t/1.8f);
            rh=new Vector3(.38f+Mathf.Sin(flourish*Mathf.PI*2)*.25f,2.3f+Mathf.Sin(flourish*Mathf.PI)*.25f,.2f);
            lh=new Vector3(-.35f,1.5f,.22f);
            spin=flourish*720;
        }
        if(Stage=="Punch") { rh=new Vector3(.16f,1.85f,.85f); lh=new Vector3(-.25f,1.55f,.15f); }
        if(Stage=="DashLattice")
        {
            rh=new Vector3(.5f*poseSide,1.95f,.65f); lh=new Vector3(-.45f,1.55f,-.3f);
            if(spine) spine.localRotation=spineBase*Quaternion.Euler(0,20*poseSide,18);
        }
        if(Stage=="SheatheOrbit" || Stage=="FinalBreak" || Stage=="Complete")
        {
            float u=Stage=="SheatheOrbit"?Mathf.SmoothStep(0,1,Mathf.Clamp01(t/2.1f)):1;
            rh=Vector3.Lerp(new Vector3(.7f,2.3f,.3f),new Vector3(-.26f,1.55f,.3f),u);
            lh=new Vector3(-.4f,1.5f,.18f);
        }
        else if(weapon) weapon.localScale=Vector3.one;
        if(pelvis)
        {
            pelvis.localPosition=pelvisRest;
            float crouch=Stage=="DashLattice"?.32f:Stage=="SheatheOrbit"?.12f:0;
            pelvis.position-=Vector3.up*crouch;
            SolveArm(leftThigh,leftCalf,leftFoot,caster.TransformPoint(new Vector3(-.23f,.14f,.3f)),caster.forward);
            SolveArm(rightThigh,rightCalf,rightFoot,caster.TransformPoint(new Vector3(.23f,.14f,-.25f)),caster.forward);
        }
        Vector3 rightGoal=caster.TransformPoint(rh),leftGoal=caster.TransformPoint(lh);
        if(weapon)
        {
            weapon.localScale=Vector3.one;
            bool sheathing=Stage=="SheatheOrbit"||Stage=="FinalBreak"||Stage=="Complete";
            if(sheathing && sheath)
            {
                float u=Stage=="SheatheOrbit"?Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.35f)/1.65f)):1;
                Vector3 mouth=sheath.position+sheath.up*.575f;
                Vector3 grip=mouth+sheath.up*Mathf.Lerp(1.4f,.05f,u);
                rightGoal=grip;
                leftGoal=mouth;
                SolveArm(rightUpperArm,rightForearm,rightHand,rightGoal,caster.right);
                SolveArm(leftUpper,leftFore,leftHand,leftGoal,-caster.right);
                weapon.position=grip;
                weapon.rotation=Quaternion.FromToRotation(Vector3.left,-sheath.up);
                var blade=FindDeepChild(weapon,"Energy Katana Blade");
                if(blade)
                {
                    float visible=Mathf.Lerp(1.38f,.015f,u);
                    blade.localScale=new Vector3(visible,.035f,.065f);
                    blade.localPosition=new Vector3(-visible*.5f-.03f,0,0);
                }
            }
            else
            {
                SolveArm(rightUpperArm,rightForearm,rightHand,rightGoal,caster.right);
                SolveArm(leftUpper,leftFore,leftHand,leftGoal,-caster.right);
                weapon.position=rightHand.position;
                weapon.rotation=Quaternion.LookRotation(caster.forward,Vector3.up)*Quaternion.Euler(Stage=="ChargeOrbit"?0:25,Stage=="ChargeOrbit"?spin:0,Stage=="ChargeOrbit"?0:-30);
                var blade=FindDeepChild(weapon,"Energy Katana Blade");
                if(blade){blade.localScale=new Vector3(1.38f,.035f,.065f);blade.localPosition=new Vector3(-.72f,0,0);}
            }
        }
    }

    static void SolveArm(Transform upper,Transform fore,Transform hand,Vector3 goal,Vector3 pole)
    {
        if(!upper||!fore||!hand) return;
        Vector3 root=upper.position, dir=(goal-root).normalized;
        float a=Vector3.Distance(root,fore.position), b=Vector3.Distance(fore.position,hand.position);
        float d=Mathf.Clamp(Vector3.Distance(root,goal),.01f,a+b-.001f);
        float x=(a*a-b*b+d*d)/(2*d), y=Mathf.Sqrt(Mathf.Max(0,a*a-x*x));
        Vector3 bend=Vector3.ProjectOnPlane(pole-Vector3.up*.6f,dir).normalized;
        Vector3 elbow=root+dir*x+bend*y;
        upper.rotation=Quaternion.FromToRotation(fore.position-root,elbow-root)*upper.rotation;
        fore.rotation=Quaternion.FromToRotation(hand.position-fore.position,goal-fore.position)*fore.rotation;
    }

    void FracturedCut(Vector3 from,Vector3 to,float width,float life,bool fracture)
    {
        if((to-from).sqrMagnitude<.0001f)return;
        int segments=fracture?7:1;
        Vector3 direction=(to-from).normalized;
        Vector3 side=Vector3.Cross(direction,Vector3.up).normalized;
        for(int i=0;i<segments;i++)
        {
            float start=i/(float)segments,end=(i+(fracture?.9f:1f))/segments;
            Vector3 offset=fracture?side*Random.Range(-.22f,.22f):Vector3.zero;
            Vector3 a=Vector3.Lerp(from,to,start)+offset,b=Vector3.Lerp(from,to,end)+offset;
            Vector3 mid=Vector3.Lerp(a,b,.52f)+(fracture?Vector3.up*Random.Range(-.16f,.16f):Vector3.zero);
            var points=new[]{a,mid,b};
            SpawnRibbon(points,slashMaterial,width*3.3f,life,new Color(.15f,.65f,1,.28f));
            SpawnRibbon(points,slashMaterial,width,life,pale);
            if(fracture)
            {
                var r=SpawnRibbon(points,riftCoreMaterial,width*4,life,Color.black);
                r.gameObject.AddComponent<VoidRendWarpLine>();
            }
        }
    }

    ParticleSystem CreateSmoke(Vector3 position,bool continuous,float life,float size)
    {
        var go=new GameObject("VoidRend_BlueVapor");go.SetActive(false);
        go.transform.SetParent(effectsRoot,false);go.transform.position=position;
        var ps=go.AddComponent<ParticleSystem>();var main=ps.main;
        main.loop=continuous;main.duration=continuous?5:.1f; main.playOnAwake=false;
        main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,life);
        main.startSpeed=new ParticleSystem.MinMaxCurve(.3f,1.1f);
        main.startSize=new ParticleSystem.MinMaxCurve(size,size*2.2f);
        main.startRotation=new ParticleSystem.MinMaxCurve(0,6.28f);
        main.startColor=new Color(.16f,.68f,1,continuous?.26f:.16f);
        main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=120;main.useUnscaledTime=true;
        var e=ps.emission;e.rateOverTime=continuous?25:0;
        if(!continuous)e.SetBursts(new[]{new ParticleSystem.Burst(0, (short)45)});
        var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Cone;sh.angle=continuous?12:75;sh.radius=continuous?.35f:1.2f;
        go.transform.rotation=Quaternion.Euler(-90,0,0);
        var v=ps.velocityOverLifetime;v.enabled=true;v.space=ParticleSystemSimulationSpace.World;
        v.x=new ParticleSystem.MinMaxCurve(0,0);v.y=new ParticleSystem.MinMaxCurve(.7f,1.4f);v.z=new ParticleSystem.MinMaxCurve(0,0);
        var n=ps.noise;n.enabled=true;n.strength=.35f;n.frequency=.65f;n.scrollSpeed=.8f;
        var co=ps.colorOverLifetime;co.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.1f,.45f,1),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(0,1)});co.color=g;
        var sz=ps.sizeOverLifetime;sz.enabled=true;sz.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.4f,1,1.7f));
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=smokeMaterial;
        go.SetActive(true);ps.Play();if(!continuous)Destroy(go,life+1);
        return ps;
    }

    public void CancelSkill()
    {
        StopAllCoroutines(); CleanupFinisher(); RestorePose(); RestoreCamera();
        if(caster) { caster.position=casterStartPosition;caster.rotation=casterStartRotation; }
        if(effectsRoot) foreach(Transform fx in effectsRoot)Destroy(fx.gameObject);
        playing=false;cinematicCamera=false;Stage="Cancelled";
    }

    void CleanupFinisher()
    {
        ReleaseVictim();SetDistortion(0);
        if(casterAnimator && playing)casterAnimator.enabled=animatorWasEnabled;
        if(chargeSmoke)Destroy(chargeSmoke.gameObject);
        foreach(var p in poseRest)if(p.Key)p.Key.localRotation=p.Value;
        if(weapon){weapon.localScale=Vector3.one;weapon.localRotation=weaponRestRotation;}
        if(pelvis)pelvis.localPosition=pelvisRest;
        if(playing)Time.timeScale=priorTimeScale;
    }

#if UNITY_EDITOR
    IEnumerator CaptureFinisher()
    {
        float[] marks = { .45f, 3.5f, 4.3f, 7.8f, 10.8f, 13.2f };
        string[] names = { "Launch", "Charge", "Punch", "Lattice", "Sheathe", "End" };
        System.IO.Directory.CreateDirectory("Temp/VoidRendReview");
        for (int i=0;i<marks.Length;i++)
        {
            while (Time.unscaledTime-sequenceStart < marks[i]) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot("Temp/VoidRendReview/"+names[i]+".png");
            Debug.Log("[VoidRend capture] "+names[i]+" stage="+Stage+" elapsed="+Elapsed+" locked="+TargetLocked);
        }
        while(playing)yield return null;
        Debug.Assert(!TargetLocked,"Finisher must release its target.");
        Debug.Assert(Vector3.Distance(target.position,victimLanding)<.001f,"Target must remain at the landing position.");
        Debug.Assert(Vector3.Dot(target.position-caster.position,caster.forward)<-1,"Target must land behind the caster.");
        Debug.Assert(cameraRadius>=recoveryCameraDistance-.01f,"Recovery shot must stay wide.");
        Debug.Assert(Mathf.Abs(cameraYaw-360)<1,"Recovery camera must finish in front of the caster.");
        Debug.Log("[VoidRend validation] completed="+CompletedRuns+" targetRestored="+(!TargetLocked)+" chargeOrbitSeconds="+chargeOrbitSeconds+" rangeMultiplier="+effectRangeMultiplier);
    }
#endif
}

public sealed class VoidRendWarpLine:MonoBehaviour
{
    LineRenderer line;Vector3[] rest;
    void Start(){line=GetComponent<LineRenderer>();rest=new Vector3[line.positionCount];line.GetPositions(rest);}
    void LateUpdate(){if(!line)return;for(int i=0;i<rest.Length;i++)line.SetPosition(i,rest[i]+Vector3.up*Mathf.Sin(Time.unscaledTime*13+i*4)*.08f);}
}
