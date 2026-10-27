using UnityEngine;

/// <summary>One owner for boss stun / launch reactions. Existing controller handles gravity and collision.</summary>
[DefaultExecutionOrder(32000)]
public sealed class CalligraphyPlayerControl : MonoBehaviour
{
    public enum Reaction { None, Stun, LightLaunch, StrongLaunch }
    public Reaction Current {get;private set;}
    public bool Active=>Current!=Reaction.None;
    ThirdPersonController controller;PlayerHealth health;Animator animator;
    float started,ends;bool locked;
    Transform chest,head,neck,leftArm,rightArm;
    void Awake(){controller=GetComponent<ThirdPersonController>();health=GetComponent<PlayerHealth>();animator=GetComponentInChildren<Animator>();}
    public bool Apply(Reaction reaction,float seconds,Vector3 direction,Vector2? launch=null)
    {
        if(!controller || !health || health.IsDead || controller.IsRolling || controller.ActionsLocked || Active)return false;
        Current=reaction;started=Time.time;ends=Time.time+seconds;controller.LockActions();locked=true;
        GetComponent<PlayerCombat>()?.CancelAttack();controller.Stun(seconds);
        Vector2 speed=launch??new Vector2(reaction==Reaction.StrongLaunch?9:3,reaction==Reaction.StrongLaunch?7:3);
        if(reaction!=Reaction.Stun)controller.Launch(Vector3.ProjectOnPlane(direction,Vector3.up).normalized*speed.x+Vector3.up*speed.y);
        else controller.StopHorizontalMotion();
        if(animator && animator.isHuman)
        {
            animator.CrossFadeInFixedTime("Idle",.08f);chest=animator.GetBoneTransform(HumanBodyBones.Chest);head=animator.GetBoneTransform(HumanBodyBones.Head);neck=animator.GetBoneTransform(HumanBodyBones.Neck);
            leftArm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);rightArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }
        return true;
    }
    void Update(){if(Active && (Time.time>=ends || !health || health.IsDead))Release();}
    void LateUpdate()
    {
        if(!Active || !animator || !animator.enabled)return;
        float w=Mathf.Min(Mathf.Clamp01((Time.time-started)/.12f),Mathf.Clamp01((ends-Time.time)/.2f));
        if(chest)chest.rotation=Quaternion.AngleAxis((Current==Reaction.Stun?12:-18)*w,transform.right)*chest.rotation;
        if(head){float sway=Current==Reaction.Stun?Mathf.Sin((Time.time-started)*7)*6:0;head.rotation=Quaternion.AngleAxis(sway*w,transform.up)*Quaternion.AngleAxis(10*w,transform.right)*head.rotation;}
        if(leftArm)leftArm.rotation=Quaternion.AngleAxis(-8*w,transform.forward)*leftArm.rotation;
        if(rightArm)rightArm.rotation=Quaternion.AngleAxis(8*w,transform.forward)*rightArm.rotation;
    }
    void Release(){if(locked && controller)controller.UnlockActions();locked=false;Current=Reaction.None;}
    void OnDisable()=>Release();
}
