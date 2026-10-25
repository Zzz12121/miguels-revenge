using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random=System.Random;

/// <summary>One disposable owner for the entire ultimate: no permanent arena or movement rewrites.</summary>
[DefaultExecutionOrder(250)]
public sealed class CalligraphyInkAvatarUltimate : MonoBehaviour
{
    public enum Phase { Roar,FirstSweep,BetweenSweeps,SecondSweep,BrushSlam,AirCombat,Dissolve,Complete }
    public Phase CurrentPhase {get;private set;}
    public bool Completed=>CurrentPhase==Phase.Complete;
    public bool GroundIsHazardous=>impacted && !cleaned && seaVisibility>.05f;
    public int SpawnedPlatformCount=>platforms.Count;
    public int SweepsCompleted {get;private set;}
    public int SweepHits {get;private set;}
    public int RescueCount {get;private set;}
    public bool PlayerLaunched {get;private set;}
    public Transform AvatarRoot=>avatar?avatar.transform:null;
    public Vector3 ImpactPoint {get;private set;}
    public Vector3 LandingPoint {get;private set;}
    public float Age {get;private set;}
    public IReadOnlyList<CalligraphyInkPlatform> Platforms=>platforms;
    CalligraphyBossCombat boss;
    CalligraphyBossCombat.InkAvatarUltimateSettings s;
    PlayerHealth player;
    ThirdPersonController motor;
    CharacterController capsule;
    CalligraphyBrushPresentation avatar;
    Transform head;
    Renderer[] avatarRenderers;
    Renderer sea;
    CalligraphyAvatarLife life;
    CalligraphyInkSeaMotion seaMotion;
    CalligraphyPlatformClimb climber;
    int seaSlowLease;
    public bool PlayerInSea {get;private set;}
    public CalligraphyPlatformClimb Climber=>climber;
    readonly List<CalligraphyInkPlatform> platforms=new();
    readonly List<Material> ownedMaterials=new();
    readonly List<Pillar> pillars=new();
    readonly List<TrailRenderer> trails=new();
    CalligraphyInkPlatform bossPlatform;
    CalligraphyUltimateCameraShake shake;
    MaterialPropertyBlock block;
    Random random;
    Vector3 entryBoss,entryPlayer,forward,right,airBossXZ,launchVelocity,poseStartTip;
    Quaternion entryBossRotation;
    float groundY,phaseClock,airClock,seaVisibility,previousSweep,pillarCycleStart,seaDamageAt;
    float inkAt,sweepLaunchUntil;
    float rockInkAt;
    int pillarIndex,airShotIndex=-1;
    bool impacted,cleaned,summonShaken,sweepHit,launchPending;
    TrailRenderer activeTrail;
    sealed class Pillar {public CalligraphyPackageEffect effect;public float until;public Vector3 offset;}

    public static CalligraphyInkAvatarUltimate Create(CalligraphyBossCombat owner,CalligraphyBossCombat.InkAvatarUltimateSettings tuning,Transform parent)
    {
        var go=new GameObject("Ink Avatar Ultimate [owned]");go.transform.SetParent(parent,false);
        var skill=go.AddComponent<CalligraphyInkAvatarUltimate>();skill.Initialize(owner,tuning);return skill;
    }
    void Initialize(CalligraphyBossCombat owner,CalligraphyBossCombat.InkAvatarUltimateSettings tuning)
    {
        boss=owner;s=tuning;player=owner.player;
        motor=player?player.GetComponent<ThirdPersonController>():null;capsule=player?player.GetComponent<CharacterController>():null;
        entryBoss=boss.transform.position;entryBossRotation=boss.transform.rotation;
        entryPlayer=player?player.transform.position:entryBoss;
        forward=Vector3.ProjectOnPlane(boss.transform.forward,Vector3.up).normalized;right=Vector3.Cross(Vector3.up,forward);
        groundY=CalligraphyGeometry.Ground(entryBoss,boss.transform).y;
        block=new MaterialPropertyBlock();random=new Random(s.randomSeed);
        if(Camera.main)shake=Camera.main.gameObject.AddComponent<CalligraphyUltimateCameraShake>();
        MakeAvatar();SetPhase(Phase.Roar);
    }
    float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    void Update()=>Advance(Time.deltaTime);
    public void Advance(float dt)
    {
        if(cleaned || dt<=0)return;
        if(!boss || !boss.Active || !player || player.IsDead){Cleanup();return;}
        Age+=dt;phaseClock+=dt;
        UpdatePillars();
        switch(CurrentPhase)
        {
            case Phase.Roar:
                boss.brush.SetCombatPose(15,Mathf.Clamp01(phaseClock/1.2f));
                avatar?.SetCombatPose(15,Mathf.Clamp01(phaseClock/2));
                PillarWaves();
                if(!summonShaken && phaseClock>=s.avatar.appearSeconds*.7f){summonShaken=true;Shake(s.summonShake);}
                if(phaseClock>=Mathf.Max(s.roarSeconds,s.avatar.appearSeconds))SetPhase(Phase.FirstSweep);
                break;
            case Phase.FirstSweep:UpdateSweep(false);break;
            case Phase.BetweenSweeps:
                if(phaseClock>=s.sweep.interval)SetPhase(Phase.SecondSweep);
                break;
            case Phase.SecondSweep:UpdateSweep(true);break;
            case Phase.BrushSlam:
                float p=Mathf.Clamp01(phaseClock/Mathf.Max(.2f,s.slam.seconds));
                boss.brush.SetCombatPose(18,p);avatar?.SetCombatPose(18,Mathf.Min(.78f,p));
                if(avatar)
                {
                    avatar.useAvatarBrushTarget=true;
                    Vector3 contact=entryBoss+forward*s.slam.forwardDistance;contact.y=groundY+.04f;
                    Vector3 high=contact+Vector3.up*s.avatar.size*.12f;
                    avatar.avatarVerticalWeight=Mathf.SmoothStep(0,1,p/Mathf.Max(.1f,s.slam.verticalReady));
                    avatar.avatarGripOffset=s.slam.gripOffset;
                    avatar.avatarBrushTarget=p<.4f?Vector3.Lerp(poseStartTip,high,Mathf.SmoothStep(0,1,p/.4f)):
                        Vector3.Lerp(high,contact,Mathf.SmoothStep(0,1,(p-.4f)/.34f));
                }
                // Impact is dispatched in LateUpdate after the actual brush/hand constraint.
                if(impacted && p>=1)SetPhase(Phase.AirCombat);
                break;
            case Phase.AirCombat:
                UpdateAir(dt);
                if(airClock>=s.sea.duration)SetPhase(Phase.Dissolve);
                break;
            case Phase.Dissolve:
                UpdateAir(dt,false);
                if(phaseClock>=Mathf.Max(s.avatar.dissolveSeconds,s.sea.fadeSeconds)){Cleanup();return;}
                break;
        }
        UpdateAppearance();
    }
    void SetPhase(Phase phase)
    {
        CurrentPhase=phase;phaseClock=0;sweepHit=false;previousSweep=0;
        if(avatar)poseStartTip=avatar.brushTip.position;
        if(activeTrail)activeTrail.emitting=false;activeTrail=null;
        switch(phase)
        {
            case Phase.Roar:Shake(s.roarShake);break;
            case Phase.FirstSweep:BeginTrail();Shake(s.firstSweepShake);break;
            case Phase.SecondSweep:BeginTrail();Shake(s.secondSweepShake);break;
            case Phase.BrushSlam:boss.brush.SetCombatPose(18,0);break;
            case Phase.AirCombat:Shake(s.risingShake);break;
            case Phase.Dissolve:if(avatar)avatar.SetCombatPose(18,.86f);break;
        }
    }
    void Shake(CalligraphyBossCombat.AvatarShakeSettings setting){if(shake)shake.Pulse(setting);}
    void MakeAvatar()
    {
        var source=boss.brush;
        if(!source || !source.animator)return;
        var root=new GameObject("Giant elder ink avatar (upper body)");root.SetActive(false);root.transform.SetParent(transform,false);
        // Clone only the visual rig and brush: no health, AI, hitboxes, nameplates or input behaviours.
        var visual=Instantiate(source.animator.gameObject,root.transform,false);
        visual.name="Elder face and robe rig";
        foreach(var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
        foreach(var c in visual.GetComponentsInChildren<Collider>(true))c.enabled=false;
        var rig=visual.GetComponent<Animator>();rig.enabled=false;
        var pen=Instantiate(source.brushPivot.gameObject,root.transform,false);pen.name="Giant liquid ink brush";
        foreach(var behaviour in pen.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
        foreach(var c in pen.GetComponentsInChildren<Collider>(true))c.enabled=false;
        var bones=new Dictionary<string,Transform>();foreach(var t in visual.GetComponentsInChildren<Transform>(true))bones[t.name]=t;
        Transform Bone(Transform original)=>original && bones.TryGetValue(original.name,out var t)?t:null;
        avatar=root.AddComponent<CalligraphyBrushPresentation>();avatar.animator=rig;avatar.brushPivot=pen.transform;
        foreach(var t in pen.GetComponentsInChildren<Transform>(true))if(t.name==source.brushTip.name)avatar.brushTip=t;
        avatar.rightUpper=Bone(source.rightUpper);avatar.rightLower=Bone(source.rightLower);avatar.rightHand=Bone(source.rightHand);
        avatar.leftUpper=Bone(source.leftUpper);avatar.leftLower=Bone(source.leftLower);avatar.leftHand=Bone(source.leftHand);
        avatar.rightPalmEuler=source.rightPalmEuler;avatar.leftPalmEuler=source.leftPalmEuler;avatar.gripWeight=source.gripWeight;
        avatar.showcase=false;avatar.useGroundHeightOverride=true;avatar.groundHeightOverride=groundY;
        avatar.avatarSweepAngle=s.sweep.angle;
        float originalHeight=0;foreach(var r in source.animator.GetComponentsInChildren<Renderer>())originalHeight=Mathf.Max(originalHeight,r.bounds.size.y);
        float scale=Mathf.Max(10,s.avatar.size)/Mathf.Max(.1f,originalHeight);
        root.transform.localScale=Vector3.one*scale;avatar.gripSpacing=source.gripSpacing*scale;
        var penScale=pen.transform.localScale;penScale.z*=s.sweep.brushLengthMultiplier;pen.transform.localScale=penScale;
        bones.TryGetValue("Head",out head);
        avatarRenderers=root.GetComponentsInChildren<Renderer>(true);
        foreach(var r in avatarRenderers)
        {
            var materials=r.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                Material original=materials[i];
                var m=new Material(s.avatar.material?s.avatar.material:boss.ink.sculptureMaterial){name="Owned flowing elder detail"};
                if(original && m.HasProperty("_BaseMap"))
                {
                    string key=original.HasProperty("_BaseMap")?"_BaseMap":original.HasProperty("_MainTex")?"_MainTex":null;
                    if(key!=null){m.SetTexture("_BaseMap",original.GetTexture(key));m.SetTextureScale("_BaseMap",original.GetTextureScale(key));m.SetTextureOffset("_BaseMap",original.GetTextureOffset(key));}
                }
                materials[i]=m;ownedMaterials.Add(m);
            }
            r.sharedMaterials=materials;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            if(r is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;
        }
        SetAvatarPlacement();root.SetActive(true);
        life=root.AddComponent<CalligraphyAvatarLife>();life.Initialize(avatar,head,player.transform,s.avatar,avatarRenderers);
    }
    void SetAvatarPlacement()
    {
        if(!avatar)return;
        Quaternion rotation=s.avatar.followElderRotation?boss.transform.rotation:entryBossRotation;
        Vector3 at=entryBoss-rotation*Vector3.forward*s.avatar.distanceBehind+rotation*s.avatar.positionOffset;
        at.y=groundY-s.avatar.size*.46f+s.avatar.height;
        avatar.avatarBasePosition=at;
        avatar.transform.SetPositionAndRotation(at,rotation);
    }
    void UpdateAppearance()
    {
        float visible=CurrentPhase==Phase.Dissolve?1-Mathf.Clamp01(phaseClock/Mathf.Max(.1f,s.avatar.dissolveSeconds)):Mathf.Clamp01(Age/Mathf.Max(.1f,s.avatar.appearSeconds));
        if(avatar)
        {
            SetAvatarPlacement();
            foreach(var r in avatarRenderers)
            {
                if(!r)continue;block.Clear();block.SetFloat("_Visibility",visible);block.SetFloat("_Opacity",s.avatar.opacity);
                block.SetFloat("_FaceClarity",s.avatar.faceClarity);block.SetFloat("_FlowStrength",s.avatar.flowStrength);
                block.SetFloat("_FaceY",head?head.position.y:groundY+s.avatar.size*.45f);block.SetFloat("_FaceRange",s.avatar.size*.14f);
                block.SetFloat("_CutY",groundY+.25f);r.SetPropertyBlock(block);
            }
        }
        if(sea)
        {
            float fade=CurrentPhase==Phase.Dissolve?1-Mathf.Clamp01(phaseClock/Mathf.Max(.1f,s.sea.fadeSeconds)):1;
            seaVisibility=fade;block.Clear();block.SetFloat("_Reveal",Mathf.Clamp01(airClock/Mathf.Max(.1f,s.sea.spreadSeconds)));
            block.SetFloat("_Opacity",s.sea.opacity*fade);block.SetFloat("_Density",s.sea.density);block.SetFloat("_Sea",1);sea.SetPropertyBlock(block);
            foreach(var p in platforms)if(p){var r=p.GetComponent<Renderer>();block.Clear();block.SetFloat("_Opacity",fade);block.SetFloat("_Sea",0);block.SetFloat("_Seed",p.transform.GetSiblingIndex()*.117f);r.SetPropertyBlock(block);}
        }
    }
    void PillarWaves()
    {
        if(!s.pillars.prefab)return;
        int count=Mathf.Clamp(s.pillars.count,1,12);
        float stagger=Mathf.Max(.01f,s.pillars.stagger),cycle=Mathf.Max(s.pillars.frequency,count*stagger);
        if(pillarIndex>=count && phaseClock>=pillarCycleStart+cycle){pillarIndex=0;pillarCycleStart+=cycle;}
        while(pillarIndex<count && phaseClock>=pillarCycleStart+pillarIndex*stagger)
        {
            if(pillars.Count<16)SpawnPillar(pillarIndex,count);
            pillarIndex++;
        }
    }
    void SpawnPillar(int index,int count)
    {
        float theta=s.pillars.randomDistribution?Range(0,Mathf.PI*2):index*Mathf.PI*2/count;
        float minimum=Mathf.Max(0,s.pillars.minimumDistance),maximum=Mathf.Max(minimum,s.pillars.maximumDistance);
        float distance=s.pillars.randomDistribution?Range(minimum,maximum):(minimum+maximum)*.5f;
        float vertical=s.pillars.randomDistribution?Range(0,Mathf.Abs(s.pillars.spawnRange.y)):Mathf.Abs(s.pillars.spawnRange.y)*.5f;
        var offset=new Vector3(Mathf.Cos(theta)*distance,s.pillars.height+vertical,Mathf.Sin(theta)*distance);
        offset.x=Mathf.Clamp(offset.x,-Mathf.Abs(s.pillars.spawnRange.x),Mathf.Abs(s.pillars.spawnRange.x));
        offset.z=Mathf.Clamp(offset.z,-Mathf.Abs(s.pillars.spawnRange.z),Mathf.Abs(s.pillars.spawnRange.z));
        var fx=CalligraphyVfxLibrary.Play(s.pillars.prefab,boss.transform.position+offset,Quaternion.identity,Mathf.Max(.1f,s.pillars.scale),1,0,transform);
        if(!fx)return;
        fx.StopAfter(Mathf.Max(.1f,s.pillars.duration),.35f);
        fx.KeepAliveFor(s.pillars.duration+.5f);
        foreach(var ps in fx.Systems)
        {
            ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;var colour=main.startColor;main.startColor=Alpha(colour,s.pillars.opacity);
            float life=main.startLifetime.constantMax;
            if(life>.01f && life<=.75f)main.startLifetimeMultiplier*=Mathf.Max(.1f,s.pillars.duration)/life;
            if(ps.emission.rateOverTime.constantMax>0)main.loop=true;
            main.maxParticles=Mathf.Min(main.maxParticles,128);ps.Play(false);
        }
        pillars.Add(new Pillar{effect=fx,until=Age+s.pillars.duration+.4f,offset=offset});
    }
    static ParticleSystem.MinMaxGradient Alpha(ParticleSystem.MinMaxGradient value,float alpha)
    {
        Color Tint(Color c){c.a*=alpha;return c;}
        Gradient Fade(Gradient g)
        {var copy=new Gradient();var keys=g.alphaKeys;for(int i=0;i<keys.Length;i++)keys[i].alpha*=alpha;copy.SetKeys(g.colorKeys,keys);return copy;}
        switch(value.mode)
        {
            case ParticleSystemGradientMode.Color:return new ParticleSystem.MinMaxGradient(Tint(value.color));
            case ParticleSystemGradientMode.TwoColors:return new ParticleSystem.MinMaxGradient(Tint(value.colorMin),Tint(value.colorMax));
            case ParticleSystemGradientMode.TwoGradients:return new ParticleSystem.MinMaxGradient(Fade(value.gradientMin),Fade(value.gradientMax));
            default:return new ParticleSystem.MinMaxGradient(Fade(value.gradient));
        }
    }
    void UpdatePillars()
    {
        for(int i=pillars.Count-1;i>=0;i--)
        {
            var p=pillars[i];
            if(!p.effect || Age>=p.until){if(p.effect){p.effect.gameObject.SetActive(false);Destroy(p.effect.gameObject);}pillars.RemoveAt(i);continue;}
            if(s.pillars.followElder)p.effect.transform.position=boss.transform.position+p.offset;
        }
    }
    void BeginTrail()
    {
        var go=new GameObject("Viscous fan brush wake");go.transform.SetParent(transform,false);
        activeTrail=go.AddComponent<TrailRenderer>();activeTrail.sharedMaterial=boss.ink.ribbonMaterial;
        activeTrail.time=Mathf.Max(.1f,s.sweep.trailLifetime);activeTrail.widthMultiplier=s.sweep.trajectoryWidth*s.sweep.trailSize;
        activeTrail.minVertexDistance=.35f;activeTrail.emitting=false;activeTrail.autodestruct=false;
        activeTrail.widthCurve=new AnimationCurve(new Keyframe(0,1),new Keyframe(.35f,.85f),new Keyframe(1,0));
        activeTrail.startColor=new Color(.002f,.006f,.01f,s.sweep.trailOpacity);activeTrail.endColor=new Color(.04f,.06f,.07f,0);
        activeTrail.shadowCastingMode=ShadowCastingMode.Off;activeTrail.receiveShadows=false;trails.Add(activeTrail);
    }
    void UpdateSweep(bool reverse)
    {
        float p=Mathf.Clamp01(phaseClock/Mathf.Max(.1f,s.sweep.seconds));
        boss.brush.SetCombatPose(reverse?17:16,p);avatar?.SetCombatPose(reverse?17:16,p);
        float progress=Mathf.SmoothStep(0,1,(p-.2f)/.6f),half=s.sweep.angle*.5f;
        float from=Mathf.Lerp(half,-half,previousSweep)*(reverse?-1:1),to=Mathf.Lerp(half,-half,progress)*(reverse?-1:1);
        if(avatar)
        {
            avatar.useAvatarBrushTarget=true;
            Vector3 point=entryBoss+Quaternion.AngleAxis(to,Vector3.up)*forward*s.sweep.radius*s.sweep.brushLengthMultiplier;
            point.y=groundY+1.2f;avatar.avatarBrushTarget=Vector3.Lerp(poseStartTip,point,Mathf.SmoothStep(0,1,p/.2f));
        }
        if(!sweepHit && p>=.2f && p<=.84f && InSweep(player.transform.position,from,to))
        {
            sweepHit=true;
            bool knockback=reverse?s.sweep.secondKnockback:s.sweep.firstKnockback;
            if(Hit(reverse?s.sweep.secondDamage:s.sweep.firstDamage,player.transform.position-entryBoss,"被巨大墨相橫掃擊倒",knockback?new Vector2(s.sweep.knockbackSpeed,3):Vector2.zero,knockback))SweepHits++;
        }
        previousSweep=progress;
        if(p>=1){SweepsCompleted++;SetPhase(reverse?Phase.BrushSlam:Phase.BetweenSweeps);}
    }
    public bool InSweep(Vector3 point,float from,float to)
    {
        Vector3 d=Vector3.ProjectOnPlane(point-entryBoss,Vector3.up);float distance=d.magnitude;
        float feet=capsule?capsule.bounds.min.y:point.y;
        if(distance>s.sweep.radius*s.sweep.brushLengthMultiplier || feet>groundY+s.sweep.contactHeight)return false;
        float yaw=Mathf.Atan2(Vector3.Dot(d,right),Vector3.Dot(d,forward))*Mathf.Rad2Deg;
        float tolerance=Mathf.Atan2(s.sweep.trajectoryWidth,Mathf.Max(1,distance))*Mathf.Rad2Deg;
        return yaw>=Mathf.Min(from,to)-tolerance && yaw<=Mathf.Max(from,to)+tolerance;
    }
    bool Hit(float damage,Vector3 direction,string reason,Vector2 launch,bool knockback=false)
    {
        bool actual=CalligraphyPlayerHitGate.Hit(player,damage,Vector3.zero,reason,CalligraphyBossCombat.Abdomen(player),
            source:transform,feedbackParent:transform,allowOrdinaryStun:false);
        if(actual && knockback && motor && !motor.ActionsLocked && !motor.IsControlImmune)
        {
            motor.Launch(Vector3.ProjectOnPlane(direction,Vector3.up).normalized*launch.x+Vector3.up*launch.y);
            sweepLaunchUntil=Time.time+1;
        }
        return actual;
    }
    void LateUpdate()
    {
        if(cleaned || !boss || !avatar)return;
        if(activeTrail)
        {
            float p=phaseClock/Mathf.Max(.1f,s.sweep.seconds);
            activeTrail.transform.position=avatar.brushTip.position;activeTrail.emitting=p>=.2f && p<=.8f;
        }
        if(CurrentPhase==Phase.BrushSlam && !impacted && phaseClock>=s.slam.seconds*.76f)Impact();
        if(Age>=inkAt && CurrentPhase<=Phase.BrushSlam)
        {
            inkAt=Age+.14f;Vector3 point=head?head.position:avatar.transform.position;
            boss.ink.Emit(point+right*Range(-4,4),Vector3.down*Range(1,3),Range(1,2.2f),1.4f);
        }
    }
    void Impact()
    {
        impacted=true;airClock=0;
        Vector3 point=avatar && avatar.brushTip?avatar.brushTip.position:entryBoss+forward*s.slam.forwardDistance;
        point.y=groundY+.06f;ImpactPoint=point;
        Shake(s.slamShake);boss.ink.Burst(point,6,28);
        var lib=CalligraphyVfxLibrary.Current;
        if(lib)CalligraphyVfxLibrary.Play(lib.frontal,point,entryBossRotation,.9f,1.3f,0,transform);
        MakeSea();MakePlatforms();
        float distance=Vector3.ProjectOnPlane(player.transform.position-point,Vector3.up).magnitude;
        if(distance<=s.slam.radius)Hit(s.slam.damage,player.transform.position-point,"被墨相鎮海震飛",Vector2.zero);
        // Arena transition is a forced launch, not an ordinary stagger. Input is never locked for the air stage.
        SkillAimCancellation.Cancel();
        player.GetComponent<EnergyTetherSkill>()?.CancelForExternalTransition();
        player.GetComponent<TetherThrownMotion>()?.Interrupt();
        foreach(var skill in FindObjectsByType<VoidRendSkillDemo>())
            if(skill.isActiveAndEnabled && skill.IsPlaying)skill.CancelSkill();
        launchPending=true;TryLaunch();
    }
    void MakeSea()
    {
        var mesh=new Mesh{name="Subdivided spreading ink sea"};
        const int sides=192,rings=96;var vertices=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        vertices.Add(Vector3.zero);uv.Add(new Vector2(.5f,.5f));
        for(int r=1;r<=rings;r++)for(int i=0;i<sides;i++)
        {
            float a=i*Mathf.PI*2/sides,f=r/(float)rings;
            vertices.Add(new Vector3(Mathf.Sin(a)*s.sea.radius*f,0,Mathf.Cos(a)*s.sea.radius*f));
            uv.Add(new Vector2(.5f+Mathf.Sin(a)*f*.5f,.5f+Mathf.Cos(a)*f*.5f));
        }
        for(int i=0;i<sides;i++)tris.AddRange(new[]{0,1+i,1+(i+1)%sides});
        for(int r=1;r<rings;r++)for(int i=0;i<sides;i++)
        {int a=1+(r-1)*sides+i,b=1+(r-1)*sides+(i+1)%sides,c=a+sides,d=b+sides;tris.AddRange(new[]{a,c,b,b,c,d});}
        mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        sea=CalligraphyGeometry.MeshObject("Expanding unsafe ink sea",transform,mesh,s.sea.material?s.sea.material:boss.ink.sculptureMaterial);
        sea.transform.position=ImpactPoint+Vector3.up*s.sea.meanHeight;seaVisibility=1;
        seaMotion=sea.gameObject.AddComponent<CalligraphyInkSeaMotion>();seaMotion.Initialize(s.sea,mesh);
    }
    void MakePlatforms()
    {
        float gravity=motor?Mathf.Max(1,-motor.gravity):25;
        float minHeight=Mathf.Max(2,Mathf.Min(s.platforms.heightRange.x,s.platforms.heightRange.y));
        float maxHeight=Mathf.Max(minHeight,s.platforms.heightRange.y);
        float landingHeight=Mathf.Clamp(11,minHeight,maxHeight);
        float apex=Mathf.Max(s.slam.launchHeight,landingHeight+3);
        float vy=Mathf.Max(s.slam.minimumLaunchSpeed,Mathf.Sqrt(2*gravity*apex));
        float travel=(vy+Mathf.Sqrt(Mathf.Max(0,vy*vy-2*gravity*landingHeight)))/gravity;
        Vector3 away=Vector3.ProjectOnPlane(player.transform.position-ImpactPoint,Vector3.up).normalized;if(away.sqrMagnitude<.01f)away=-forward;
        float horizontal=Mathf.Max(0,s.slam.horizontalPush);
        LandingPoint=player.transform.position+away*horizontal*travel;LandingPoint=new Vector3(LandingPoint.x,groundY+landingHeight,LandingPoint.z);
        // The large first foothold is centred on the launch's actual ballistic landing, not a random platform.
        Vector3 flat=Vector3.ProjectOnPlane(LandingPoint-ImpactPoint,Vector3.up);
        float allowed=Mathf.Max(10,Mathf.Min(s.sea.radius,s.platforms.spawnRadius)-s.platforms.landingPlatformSize);
        if(flat.magnitude>allowed)
        {
            Vector3 safe=ImpactPoint+flat.normalized*allowed;
            Vector3 displacement=Vector3.ProjectOnPlane(safe-player.transform.position,Vector3.up);
            launchVelocity=displacement/Mathf.Max(.2f,travel)+Vector3.up*vy;LandingPoint=new Vector3(safe.x,groundY+landingHeight,safe.z);
        }
        else launchVelocity=away*horizontal+Vector3.up*vy;
        var landing=AddPlatform(LandingPoint,s.platforms.landingPlatformSize*.5f,0);
        airBossXZ=entryBoss;
        if(Vector3.ProjectOnPlane(entryBoss-LandingPoint,Vector3.up).magnitude<landing.Radius+6)
        {
            Vector3 side=Vector3.ProjectOnPlane(entryBoss-LandingPoint,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=right;
            airBossXZ=LandingPoint+side*Mathf.Min(4,landing.Radius*.55f);bossPlatform=landing;
        }
        else bossPlatform=AddPlatform(new Vector3(entryBoss.x,groundY+landingHeight,entryBoss.z),5,1);
        int desired=Mathf.Clamp(s.platforms.count,12,128);
        float[] boundaries={0,6f/82,22f/82,42f/82,62f/82,1};
        int placedIndex=platforms.Count,triesForSize=0;
        // Weighted strata with random XZ rejection; no lattice or concentric rings. The
        // reserved low landing remains clear throughout the ascent, not only at final height.
        for(int attempt=0;attempt<12000 && platforms.Count<desired;attempt++)
        {
            int index=platforms.Count,band=0;float sum=0;
            if(index!=placedIndex){placedIndex=index;triesForSize=0;}triesForSize++;
            for(int i=0;i<5;i++)sum+=s.platforms.heightWeights!=null && i<s.platforms.heightWeights.Length?Mathf.Max(0,s.platforms.heightWeights[i]):1;
            float pick=((index*.61803399f)%1)*Mathf.Max(.01f,sum),acc=0;
            for(int i=0;i<5;i++){acc+=s.platforms.heightWeights!=null && i<s.platforms.heightWeights.Length?Mathf.Max(0,s.platforms.heightWeights[i]):1;if(pick<=acc){band=i;break;}}
            Vector3 weights=s.platforms.sizeWeights;float select=(index*.75487766f)%1*(weights.x+weights.y+weights.z);
            Vector2 size=select<weights.x?s.platforms.smallSize:select<weights.x+weights.y?s.platforms.mediumSize:s.platforms.largeSize;
            if(triesForSize>180)size=s.platforms.smallSize;
            float radius=Range(Mathf.Max(3,size.x),Mathf.Max(3,size.y))*.5f;
            float angle=Range(0,Mathf.PI*2),distance=Mathf.Sqrt(Range(0,1))*(s.platforms.spawnRadius-radius);
            Vector3 at=ImpactPoint+new Vector3(Mathf.Cos(angle)*distance,0,Mathf.Sin(angle)*distance);
            if(index>5 && Range(0,1)<s.platforms.clustering)
            {
                var cluster=platforms[2+(index%Mathf.Max(1,platforms.Count-2))];
                at=cluster.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(cluster.Radius+radius+Range(1,5));
            }
            if(Vector3.ProjectOnPlane(at-ImpactPoint,Vector3.up).magnitude+radius>s.platforms.spawnRadius)continue;
            Vector3 bodyColumn=ImpactPoint-forward*18;
            if(Vector3.ProjectOnPlane(at-bodyColumn,Vector3.up).magnitude<radius+12)continue;
            at.y=groundY+Mathf.Lerp(minHeight,maxHeight,Range(boundaries[band],boundaries[band+1]));
            bool overlap=false;foreach(var p in platforms)
            {
                if(Vector3.ProjectOnPlane(at-p.transform.position,Vector3.up).magnitude>=p.Radius+radius+s.platforms.minimumGap)continue;
                overlap=true;break;
            }
            if(overlap)continue;
            AddPlatform(at,radius,platforms.Count);
        }
        if(platforms.Count<desired)Debug.LogWarning($"Ink avatar arena fits {platforms.Count}/{desired} platforms. Increase Spawn Radius or reduce Size / Minimum Gap.",boss);
        if(s.climb.enabled){climber=gameObject.AddComponent<CalligraphyPlatformClimb>();climber.Initialize(player,platforms,s.climb);}
    }
    CalligraphyInkPlatform AddPlatform(Vector3 top,float radius,int index)
    {
        float depth=Mathf.Clamp(radius*Range(1.1f,2.3f),s.platforms.thicknessRange.x,s.platforms.thicknessRange.y);
        depth=Mathf.Min(depth,Mathf.Max(2,top.y-groundY-1));
        var mesh=CalligraphyRockGeometry.Create(radius,depth,s.randomSeed+index,s.platforms.irregularity,s.platforms.sharpness,out var rim);
        var renderer=CalligraphyGeometry.MeshObject("Floating ink foothold "+index,transform,mesh,s.platforms.material?s.platforms.material:boss.ink.sculptureMaterial);
        renderer.transform.position=new Vector3(top.x,groundY-.08f,top.z);
        renderer.transform.rotation=Quaternion.Euler(0,s.platforms.randomYaw?Range(0,360):0,0);
        var collider=renderer.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
        var platform=renderer.gameObject.AddComponent<CalligraphyInkPlatform>();
        float speed=s.platforms.riseSpeed*(index<2?1:Range(1-s.platforms.riseSpeedVariation,1+s.platforms.riseSpeedVariation));
        platform.Initialize(top.y,radius,speed,player);
        platform.ConfigureShape(rim,depth,index<2?0:Range(0,index*s.platforms.spawnStagger));
        platforms.Add(platform);return platform;
    }
    void TryLaunch()
    {
        if(!launchPending || !motor || !player || player.IsDead)return;
        if(motor.IsControlImmune)return;
        player.GetComponent<PlayerCombat>()?.CancelTransientActions();
        float g=Mathf.Max(1,-motor.gravity),rise=LandingPoint.y-(capsule?capsule.bounds.min.y:player.transform.position.y);
        float travel=(launchVelocity.y+Mathf.Sqrt(Mathf.Max(0,launchVelocity.y*launchVelocity.y-2*g*rise)))/g;
        Vector3 displacement=Vector3.ProjectOnPlane(LandingPoint-player.transform.position,Vector3.up)/Mathf.Max(.1f,travel);
        launchVelocity=new Vector3(displacement.x,launchVelocity.y,displacement.z);
        motor.Launch(launchVelocity);launchPending=false;PlayerLaunched=true;
    }
    void UpdateAir(float dt,bool attacks=true)
    {
        airClock+=dt;
        if(airClock<7 && Age>=rockInkAt)
        {
            rockInkAt=Age+.16f;
            for(int i=0;i<platforms.Count;i+=5)
            {
                var rock=platforms[i];if(!rock || rock.Raised)continue;
                Vector3 bottom=rock.transform.position-Vector3.up*rock.Depth;
                if(bottom.y>groundY-.5f && bottom.y<groundY+5)
                    boss.ink.Emit(bottom,Vector3.down*Range(3,7),Range(.3f,.8f),.7f);
            }
        }
        if(launchPending)TryLaunch();
        if(bossPlatform)
        {
            Vector3 at=airBossXZ;at.y=bossPlatform.Top;boss.transform.position=at;
        }
        if(attacks && s.airCombatProjectiles)
        {
            float segment=Mathf.Max(.5f,s.airAttackInterval),p=(phaseClock%segment)/segment;
            Vector3 aim=CalligraphyBossCombat.Abdomen(player);Vector3 d=Vector3.ProjectOnPlane(aim-boss.transform.position,Vector3.up);
            if(d.sqrMagnitude>.01f)boss.transform.rotation=Quaternion.LookRotation(d);
            boss.brush.SetAimPoint(aim);boss.brush.SetCombatPose(0,p);
            int index=Mathf.FloorToInt(phaseClock/segment);
            if(p>=.57f && airShotIndex<index)
            {
                airShotIndex=index;var go=new GameObject("Air arena brush projectile");go.transform.SetParent(transform,false);
                var tuning=new CalligraphyBossCombat.ProjectileSettings{maximumDistance=s.sea.radius*2,collisionRadius=.35f,vfxScale=.65f,homingStrength=.45f,turnRate=55};
                go.AddComponent<CalligraphyInkProjectile>().Launch(boss.ink,boss.transform,boss.brush.brushTip.position,aim-boss.brush.brushTip.position,s.airProjectileSpeed,s.airAttackDamage,player,tuning,transform);
            }
        }
        if(!capsule || !motor)return;
        float revealed=s.sea.radius*Mathf.Clamp01(airClock/Mathf.Max(.1f,s.sea.spreadSeconds));
        PlayerInSea=GroundIsHazardous && airClock>=1.5f && capsule.enabled &&
            capsule.bounds.min.y<=groundY+.45f+s.sea.meanHeight &&
            Vector3.ProjectOnPlane(player.transform.position-ImpactPoint,Vector3.up).magnitude<=revealed;
        if(!PlayerInSea){if(seaSlowLease!=0){motor.RemoveSpeedMultiplierLease(seaSlowLease);seaSlowLease=0;}return;}
        if(seaSlowLease==0)seaSlowLease=motor.AddSpeedMultiplierLease(s.sea.slowMultiplier);
        if(Time.time<seaDamageAt)return;
        seaDamageAt=Time.time+Mathf.Max(.2f,s.sea.damageInterval);
        Hit(s.sea.damage,Vector3.up,"落入墨海",Vector2.zero);
    }
    public void Cleanup()
    {
        if(cleaned)return;cleaned=true;CurrentPhase=Phase.Complete;
        if(climber){climber.Cancel();climber.enabled=false;Destroy(climber);}
        if(motor && seaSlowLease!=0)motor.RemoveSpeedMultiplierLease(seaSlowLease);
        seaSlowLease=0;PlayerInSea=false;
        // Disable colliders and renderers synchronously: Destroy itself is deferred until frame end.
        foreach(var c in GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var r in GetComponentsInChildren<Renderer>(true))r.enabled=false;
        foreach(var ps in GetComponentsInChildren<ParticleSystem>(true))ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(shake){shake.enabled=false;Destroy(shake);shake=null;}
        if(boss){boss.transform.SetPositionAndRotation(entryBoss,entryBossRotation);if(boss.brush)boss.brush.ClearCombatPose();if(boss.ink)boss.ink.Clear();}
        if(!impacted && sweepLaunchUntil>Time.time && motor && motor.IsBallistic)motor.ResetMotion();
        if(impacted && player && !player.IsDead)
        {
            Vector3 delta=entryPlayer-player.transform.position;bool wasEnabled=capsule && capsule.enabled;
            if(capsule)capsule.enabled=false;player.transform.position=entryPlayer;
            if(motor)motor.ResetMotion();if(capsule)capsule.enabled=wasEnabled;Physics.SyncTransforms();
            var camera=Camera.main;if(camera){var orbit=camera.GetComponent<ThirdPersonCamera>();if(orbit)orbit.WarpWithTarget(delta);else camera.transform.position+=delta;}
        }
        // The empty component remains for one boss tick so FinishCast can observe Complete.
        for(int i=transform.childCount-1;i>=0;i--){var child=transform.GetChild(i).gameObject;child.SetActive(false);Destroy(child);}
    }
    void OnDisable()=>Cleanup();
    void OnDestroy()
    {
        Cleanup();foreach(var m in ownedMaterials)if(m)Destroy(m);ownedMaterials.Clear();
    }
}
