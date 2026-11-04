using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// W-intent ledge climb on this ultimate's moving rocks. Owns one balanced action lock.
///
/// 動畫：Animator 有「Climb」狀態（選單 GeminiGrowUp > 安裝 玩家跳躍・下落・攀爬動畫）時，
/// 播放手做的攀爬動畫 CW_Climb（掛在邊緣 → 雙手拉、單腳蹬牆 → 撐起上半身 → 右膝跨上 → 站起來），
/// 播放進度 = 攀爬進度；角色根節點放在「爬完站好的位置」，一開始從抓住邊緣的位置平順地接過去，
/// 雙手用 IK 釘在邊緣上（石頭在動也不會滑開），撐起之後依序放開。
/// 石頭的側面是斜的（上窄下寬或歪一邊），動畫是照垂直的牆做的；所以每一幀量身體各部位離石頭表面多遠，
/// 快要穿進去時把整個人往外推一點（只在掛著／拉起來的那段，站上平台之後慢慢歸零）。
/// 沒有 Climb 狀態（還沒執行安裝選單）時，退回原本的程式擺姿勢版本。
/// </summary>
[DefaultExecutionOrder(800)]
public sealed class CalligraphyPlatformClimb : MonoBehaviour
{
    // ---- 正在攀爬的玩家（PlayerVisualAnimator 用來暫停自己的動畫切換） ----
    static readonly HashSet<Transform> climbing=new();
    public static bool IsClimbing(Transform player)=>player && climbing.Contains(player);

    // ---- CW_Climb 動畫的尺寸（Blender 裡的單位：角色身高約 1.18） ----
    static readonly int ClimbHash=Animator.StringToHash("Climb");
    const float ClipLegLength=.482f;   // 大腿 + 小腿
    const float ClipEdgeBehind=.30f;   // 爬完站好時，邊緣在腳後面多遠
    bool clipMode;                     // true = 播 CW_Climb；false = 原本的程式姿勢
    bool rootMotionBefore;
    float scale=1;
    Vector3 startOffset;
    float wallPush;                    // 為了不穿進斜的石壁，額外往外推的距離
    Collider[] rockColliders=System.Array.Empty<Collider>();
    // 檢查穿模的骨頭與半徑（動畫單位，會乘上 scale）
    static readonly (HumanBodyBones bone,float radius)[] ClearanceBones=
    {
        (HumanBodyBones.Hips,.11f),(HumanBodyBones.Spine,.10f),(HumanBodyBones.Chest,.12f),(HumanBodyBones.UpperChest,.12f),
        (HumanBodyBones.Neck,.06f),(HumanBodyBones.Head,.10f),
        (HumanBodyBones.LeftUpperLeg,.07f),(HumanBodyBones.RightUpperLeg,.07f),
        (HumanBodyBones.LeftLowerLeg,.06f),(HumanBodyBones.RightLowerLeg,.06f),
        (HumanBodyBones.LeftFoot,.05f),(HumanBodyBones.RightFoot,.05f),
        (HumanBodyBones.LeftToes,.03f),(HumanBodyBones.RightToes,.03f),
    };
    public bool Climbing {get;private set;}
    public int CompletedClimbs {get;private set;}
    public float Progress=>Climbing?Mathf.Clamp01(clock/settings.seconds):0;
    PlayerHealth player;
    ThirdPersonController motor;
    CharacterController capsule;
    EnergyTetherSkill tether;
    Animator animator;
    IReadOnlyList<CalligraphyInkPlatform> platforms;
    CalligraphyBossCombat.AvatarClimbSettings settings;
    CalligraphyInkPlatform platform;
    Vector3 localEdge,localInward,start,stand,edge,inward;
    float clock,bottomOffset,cooldown;
    bool motorEnabled,capsuleEnabled,animatorEnabled,locked;
    readonly Dictionary<Transform,Quaternion> rotations=new();
    readonly Dictionary<Transform,Vector3> positions=new();
    public void Initialize(PlayerHealth victim,IReadOnlyList<CalligraphyInkPlatform> rocks,CalligraphyBossCombat.AvatarClimbSettings tuning)
    {
        player=victim;platforms=rocks;settings=tuning;motor=player.GetComponent<ThirdPersonController>();
        capsule=player.GetComponent<CharacterController>();tether=player.GetComponent<EnergyTetherSkill>();animator=player.GetComponentInChildren<Animator>(true);
    }
    void LateUpdate()
    {
        if(!player || player.IsDead || !motor || !capsule){Cancel();return;}
        if(Climbing)
        {
            if(!platform || !platform.gameObject.activeInHierarchy){Cancel();return;}
            if(clipMode){WallClearance();HandIK();if(clock>=settings.seconds){CompletedClimbs++;Cancel();cooldown=Time.time+.5f;}return;}
            clock+=Time.deltaTime;Animate();
            if(clock>=settings.seconds){CompletedClimbs++;Cancel();cooldown=Time.time+.5f;}
            return;
        }
        if(Time.timeScale<=0 || Time.time<cooldown || !motor.enabled || !capsule.enabled || motor.ActionsLocked || motor.IsRolling || motor.IsStunned || motor.IsGrounded)return;
        if(!WantsClimb())return;
        if(motor.VerticalVelocity>0 && !(tether && tether.IsPullingPlayer))return;
        CalligraphyInkPlatform best=null;Vector3 bestEdge=default,bestIn=default;float distance=float.MaxValue;
        foreach(var rock in platforms)
        {
            if(!rock || !rock.TryLedge(player.transform.position,out var e,out var into))continue;
            float height=e.y-capsule.bounds.min.y;
            if(height<settings.ledgeHeight.x || height>settings.ledgeHeight.y)continue;
            var hips=animator && animator.isHuman?animator.GetBoneTransform(HumanBodyBones.Hips):null;
            float waist=hips?hips.position.y:capsule.bounds.center.y;
            if(waist>=e.y-settings.waistClearance)continue;
            Vector3 facing=Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized;
            if(Vector3.Dot(facing,into)<Mathf.Cos(settings.facingAngle*Mathf.Deg2Rad))continue;
            float d=Vector3.ProjectOnPlane(e-player.transform.position,Vector3.up).magnitude;
            if(d>settings.reach || d>=distance)continue;
            if(!ClearTop(rock,e,into))continue;
            distance=d;best=rock;bestEdge=e;bestIn=into;
        }
        if(best)Begin(best,bestEdge,bestIn);
    }
    // =====================================================================
    //  手做動畫版（CW_Climb）
    // =====================================================================

    void BeginClip(CalligraphyInkPlatform rock,Vector3 e,Vector3 into)
    {
        tether?.CancelForExternalTransition();SkillAimCancellation.Cancel();
        player.GetComponent<PlayerCombat>()?.CancelTransientActions();player.GetComponent<TetherThrownMotion>()?.Interrupt();
        platform=rock;localEdge=rock.transform.InverseTransformPoint(e);localInward=rock.transform.InverseTransformDirection(into);
        clipMode=true;clock=0;wallPush=0;startOffset=Vector3.zero;
        var cols=new List<Collider>();
        foreach(var c in rock.GetComponentsInChildren<Collider>())if(c && c.enabled && !c.isTrigger)cols.Add(c);
        rockColliders=cols.ToArray();
        var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        Vector3 hipsBefore=hips?hips.position:player.transform.position;
        scale=Mathf.Clamp(LegLength()/ClipLegLength,.2f,10f);
        // 玩家的根節點在膠囊中心（不是腳底）：動畫裡的「腳底 = 平台上緣」要換成「根節點 = 上緣 + 這段高度」，
        // 不然整個攀爬會低一截，最後站在石頭裡面
        bottomOffset=player.transform.position.y-capsule.bounds.min.y;

        motorEnabled=motor.enabled;capsuleEnabled=capsule.enabled;animatorEnabled=animator.enabled;
        motor.ResetMotion();motor.LockActions();locked=true;motor.enabled=false;capsule.enabled=false;
        rootMotionBefore=animator.applyRootMotion;animator.applyRootMotion=false;animator.enabled=true;
        climbing.Add(player.transform);
        Climbing=true;

        // 先把根節點放到「爬完站好的位置」、算出動畫第 0 格的髖部在哪裡，
        // 跟玩家現在的髖部差多少 → 一開始從現在的位置平順地接到動畫裡
        PlaceRoot(0f);
        animator.Play(ClimbHash,0,0f);animator.Update(0f);
        startOffset=hips?hipsBefore-hips.position:Vector3.zero;
        PlaceRoot(0f);
        Physics.SyncTransforms();
    }

    /// <summary>腿長（大腿根 → 膝蓋 → 腳踝），跟姿勢無關，用來把動畫裡的距離換成遊戲裡的大小</summary>
    float LegLength()
    {
        var a=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var b=animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var c=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        if(!a||!b||!c)return ClipLegLength*capsule.height/1.18f;
        return Vector3.Distance(a.position,b.position)+Vector3.Distance(b.position,c.position);
    }

    void PlaceRoot(float p)
    {
        edge=platform.transform.TransformPoint(localEdge);
        inward=Vector3.ProjectOnPlane(platform.transform.TransformDirection(localInward),Vector3.up).normalized;
        if(inward.sqrMagnitude<.001f)inward=player.transform.forward;
        stand=edge+inward*(ClipEdgeBehind*scale)+Vector3.up*bottomOffset;
        // 從起跳的位置接到動畫：如果玩家是在石頭外凸的邊緣底下（比動畫的位置更靠石頭裡面），
        // 先很快地往外移出來，再慢慢往上接，手和頭才不會從石頭的邊緣穿過去
        float intoRock=Vector3.Dot(startOffset,inward);
        Vector3 rest=startOffset-inward*intoRock;
        float blend=1-Ease(p,0,.18f),outward=intoRock>0?1-Ease(p,0,.07f):blend;
        Vector3 at=stand+rest*blend+inward*(intoRock*outward)-inward*wallPush;
        player.transform.SetPositionAndRotation(at,Quaternion.LookRotation(inward));
    }

    void Update()
    {
        if(!Climbing || !clipMode)return;
        if(!platform || !platform.gameObject.activeInHierarchy){Cancel();return;}
        clock+=Time.deltaTime;
        float p=Progress;
        PlaceRoot(p);
        animator.Play(ClimbHash,0,Mathf.Min(p,.999f)); // 在 Animator 這一幀計算之前設定進度
    }

    /// <summary>
    /// 石壁不是垂直的：從石頭外面朝石頭方向打射線，量身體各部位（在平台上緣以下的）離表面多遠，
    /// 會穿進去就把整個人往外推（推得快、收回得慢，避免抖動）；站上平台的過程中推的距離慢慢歸零。
    /// </summary>
    void WallClearance()
    {
        if(rockColliders.Length==0)return;
        float p=Progress,s=scale,fade=1-Ease(p,.55f,.8f);
        float back=1.5f*s,worst=float.NegativeInfinity;
        if(fade>0)
            foreach(var (id,radius) in ClearanceBones)
            {
                var bone=animator.GetBoneTransform(id);
                if(!bone || bone.position.y>edge.y-.02f*s)continue;
                float reach=back+(radius+.01f)*s;
                var ray=new Ray(bone.position-inward*back,inward);
                foreach(var c in rockColliders)
                    if(c && c.Raycast(ray,out var hit,reach))worst=Mathf.Max(worst,reach-hit.distance);
            }
        float target=float.IsNegativeInfinity(worst)?0:Mathf.Max(0,wallPush+worst);
        target=Mathf.Min(target*fade,.6f*s);
        float speed=(target>wallPush?4f:fade<1?3f:.8f)*s; // 站上平台的階段收得快
        float next=Mathf.MoveTowards(wallPush,target,speed*Time.deltaTime);
        if(Mathf.Approximately(next,wallPush))return;
        player.transform.position-=inward*(next-wallPush); // 這一幀就生效（骨頭跟著根節點一起移）
        wallPush=next;
    }

    /// <summary>雙手釘在邊緣上：掛著的時候手指勾住上緣，撐起來時手掌平貼在平台上；之後右手、左手依序放開</summary>
    void HandIK()
    {
        float p=Progress;
        Vector3 side=Vector3.Cross(Vector3.up,inward),up=Vector3.up;
        float k=Ease(p,.38f,.55f);
        float s=scale;
        Vector3 hang=edge-inward*(.035f*s)-up*(.025f*s),press=edge+inward*(.05f*s)+up*(.035f*s);
        Vector3 basePoint=Vector3.Lerp(hang,press,k);
        float spread=Mathf.Lerp(.11f,.13f,k)*s;
        Vector3 poleHang=-inward*.5f-up*.3f,polePress=-inward*.6f+up*.5f;
        Vector3 pole=Vector3.Lerp(poleHang,polePress,k);
        float right=1-Ease(p,.58f,.68f),left=1-Ease(p,.7f,.84f);
        if(left>0)ArmIK(true,basePoint-side*spread,-side*.6f+pole,left);
        if(right>0)ArmIK(false,basePoint+side*spread,side*.6f+pole,right);
    }

    void ArmIK(bool left,Vector3 wrist,Vector3 pole,float weight)
    {
        var a=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
        var b=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
        var c=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
        if(!a||!b||!c)return;
        Quaternion handRot=c.rotation;
        Solve(a,b,c,wrist,a.position+pole,weight);
        c.rotation=handRot; // 手掌的方向用動畫的（手指勾住 / 平貼），只修正手腕的位置
    }

    void FinishClip()
    {
        wallPush=0;
        if(platform)PlaceRoot(1f);
        animator.applyRootMotion=rootMotionBefore;
        if(animator.enabled && animator.HasState(0,Animator.StringToHash("Idle")))animator.CrossFadeInFixedTime("Idle",.12f,0,0f);
    }

    bool WantsClimb()
    {
        if(RangeInputSettings.InputBlocked || (!motor.inputEnabled && !(tether && tether.IsPullingPlayer)))return false;
        if(Keyboard.current!=null)return Keyboard.current.wKey.isPressed && !Keyboard.current.sKey.isPressed;
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.W) && !Input.GetKey(KeyCode.S);
#else
        return false;
#endif
    }
    bool ClearTop(CalligraphyInkPlatform rock,Vector3 e,Vector3 into)
    {
        float radius=capsule.radius*player.transform.lossyScale.x;
        Vector3 bottom=e+into*(radius+.65f)+Vector3.up*(radius+.1f);
        foreach(var c in Physics.OverlapCapsule(bottom,bottom+Vector3.up*Mathf.Max(0,capsule.height-2*radius),radius*.9f,~0,QueryTriggerInteraction.Ignore))
            if(!c.transform.IsChildOf(player.transform) && c.GetComponentInParent<CalligraphyInkPlatform>()!=rock)return false;
        return true;
    }
    void Begin(CalligraphyInkPlatform rock,Vector3 e,Vector3 into)
    {
        if(!animator || !animator.isHuman)return;
        if(animator.HasState(0,ClimbHash)){BeginClip(rock,e,into);return;}
        clipMode=false;
        tether?.CancelForExternalTransition();SkillAimCancellation.Cancel();
        player.GetComponent<PlayerCombat>()?.CancelTransientActions();player.GetComponent<TetherThrownMotion>()?.Interrupt();
        platform=rock;localEdge=rock.transform.InverseTransformPoint(e);localInward=rock.transform.InverseTransformDirection(into);
        bottomOffset=player.transform.position.y-capsule.bounds.min.y;start=player.transform.position;clock=0;
        motorEnabled=motor.enabled;capsuleEnabled=capsule.enabled;animatorEnabled=animator.enabled;
        motor.ResetMotion();motor.LockActions();locked=true;motor.enabled=false;capsule.enabled=false;animator.enabled=false;
        rotations.Clear();positions.Clear();
        for(int i=0;i<(int)HumanBodyBones.LastBone;i++)
        {var bone=animator.GetBoneTransform((HumanBodyBones)i);if(bone){rotations[bone]=bone.localRotation;positions[bone]=bone.localPosition;}}
        Climbing=true;Animate();
    }
    static float Ease(float p,float a,float b)=>Mathf.SmoothStep(0,1,(p-a)/Mathf.Max(.001f,b-a));
    void Animate()
    {
        float p=Progress;edge=platform.transform.TransformPoint(localEdge);inward=platform.transform.TransformDirection(localInward);
        Vector3 side=Vector3.Cross(Vector3.up,inward),up=Vector3.up;
        Vector3 hang=edge-inward*(capsule.radius+.08f)+up*(bottomOffset-1.65f);
        stand=edge+inward*(capsule.radius+.65f)+up*(bottomOffset+.09f);
        Vector3 leftStep=edge-inward*.12f+up*(bottomOffset-.95f);
        Vector3 kneel=stand-up*.52f;
        Vector3 pull=Vector3.Lerp(hang,leftStep,Ease(p,.12f,.36f));
        pull=Vector3.Lerp(pull,kneel,Ease(p,.43f,.76f));
        Vector3 at=Vector3.Lerp(start,pull,Ease(p,0,.13f));at=Vector3.Lerp(at,stand,Ease(p,.8f,1));
        player.transform.SetPositionAndRotation(at,Quaternion.LookRotation(inward));
        foreach(var pair in rotations)if(pair.Key)pair.Key.localRotation=pair.Value;
        foreach(var pair in positions)if(pair.Key)pair.Key.localPosition=pair.Value;
        var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        float lean=38*Ease(p,.38f,.7f)*(1-Ease(p,.8f,1));if(hips)hips.rotation=Quaternion.AngleAxis(lean,side)*hips.rotation;
        float release=Ease(p,.78f,1);
        Arm(true,edge-side*.24f+up*.045f,side,-inward,release);
        Arm(false,edge+side*.24f+up*.045f,-side,-inward,release);
        // Lift outside the cliff before crossing its lip: never interpolate a foot through solid rock.
        Vector3 leftFoot=Vector3.Lerp(hang+up*(-bottomOffset+.18f)-side*.2f,edge-inward*.2f-side*.22f+up*.22f,Ease(p,.12f,.34f));
        leftFoot=Vector3.Lerp(leftFoot,edge+inward*.35f-side*.22f+up*.1f,Ease(p,.34f,.43f));
        Vector3 rightFoot=Vector3.Lerp(hang+up*(-bottomOffset+.05f)+side*.2f,edge-inward*.2f+side*.22f+up*.22f,Ease(p,.34f,.55f));
        rightFoot=Vector3.Lerp(rightFoot,edge+inward*.42f+side*.22f+up*.1f,Ease(p,.55f,.64f));
        Foot(true,leftFoot,inward,1-release);
        Foot(false,rightFoot,inward,1-release);
    }
    void Arm(bool left,Vector3 target,Vector3 pole,Vector3 facing,float release)
    {
        var a=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
        var b=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
        var c=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
        Solve(a,b,c,target,pole+facing*.3f,1-release);
        if(c)
        {
            var index=animator.GetBoneTransform(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal);
            if(index)c.rotation=Quaternion.Slerp(c.rotation,Quaternion.FromToRotation(index.position-c.position,inward)*c.rotation,1-release);
        }
    }
    void Foot(bool left,Vector3 target,Vector3 pole,float weight)
    {Solve(animator.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg),animator.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg),animator.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot),target,pole,weight);}
    static void Solve(Transform a,Transform b,Transform c,Vector3 target,Vector3 pole,float weight)
    {
        if(!a || !b || !c)return;
        float ab=Vector3.Distance(a.position,b.position),bc=Vector3.Distance(b.position,c.position);
        Vector3 d=target-a.position;float length=Mathf.Clamp(d.magnitude,Mathf.Abs(ab-bc)+.001f,ab+bc-.001f);
        Vector3 dir=d.normalized,bend=Vector3.ProjectOnPlane(pole,dir).normalized;
        float x=(ab*ab-bc*bc+length*length)/(2*length);
        Vector3 knee=a.position+dir*x+bend*Mathf.Sqrt(Mathf.Max(0,ab*ab-x*x));
        a.rotation=Quaternion.Slerp(a.rotation,Quaternion.FromToRotation(b.position-a.position,knee-a.position)*a.rotation,weight);
        b.rotation=Quaternion.Slerp(b.rotation,Quaternion.FromToRotation(c.position-b.position,target-b.position)*b.rotation,weight);
    }
    public void Cancel()
    {
        if(!Climbing && !locked)return;
        if(clipMode && Climbing)FinishClip();
        if(player)climbing.Remove(player.transform);
        foreach(var pair in rotations)if(pair.Key)pair.Key.localRotation=pair.Value;
        foreach(var pair in positions)if(pair.Key)pair.Key.localPosition=pair.Value;
        if(animator)animator.enabled=animatorEnabled;
        if(capsule)capsule.enabled=capsuleEnabled;
        if(motor){motor.ResetMotion();if(locked)motor.UnlockActions();if(player && !player.IsDead)motor.enabled=motorEnabled;}
        locked=false;Climbing=false;clipMode=false;wallPush=0;rockColliders=System.Array.Empty<Collider>();rotations.Clear();positions.Clear();Physics.SyncTransforms();
    }
    void OnDisable()=>Cancel();
    void OnDestroy()=>Cancel();
}
