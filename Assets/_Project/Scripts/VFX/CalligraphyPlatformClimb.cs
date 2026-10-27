using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>W-intent ledge climb on this ultimate's moving rocks. Owns one balanced action lock.</summary>
[DefaultExecutionOrder(800)]
public sealed class CalligraphyPlatformClimb : MonoBehaviour
{
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
        foreach(var pair in rotations)if(pair.Key)pair.Key.localRotation=pair.Value;
        foreach(var pair in positions)if(pair.Key)pair.Key.localPosition=pair.Value;
        if(animator)animator.enabled=animatorEnabled;
        if(capsule)capsule.enabled=capsuleEnabled;
        if(motor){motor.ResetMotion();if(locked)motor.UnlockActions();if(player && !player.IsDead)motor.enabled=motorEnabled;}
        locked=false;Climbing=false;rotations.Clear();positions.Clear();Physics.SyncTransforms();
    }
    void OnDisable()=>Cancel();
    void OnDestroy()=>Cancel();
}
