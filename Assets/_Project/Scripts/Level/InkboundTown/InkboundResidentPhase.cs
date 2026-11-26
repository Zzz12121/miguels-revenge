using UnityEngine;

/// <summary>Offsets the preserved animation and adds quiet social/tea gestures, independent of the visitor.</summary>
[DefaultExecutionOrder(80)]
public sealed class InkboundResidentPhase : MonoBehaviour
{
    public float phase;
    public bool social, speaking, drinkingTea;
    public Transform cup;
    Animator animator;
    Transform arm,fore,hand,head;
    void Start()
    {
        animator=GetComponentInChildren<Animator>();
        if(animator)
        {
            animator.Play("Locomotion",0,phase);
            if(animator.isHuman){arm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);fore=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);hand=animator.GetBoneTransform(HumanBodyBones.RightHand);head=animator.GetBoneTransform(HumanBodyBones.Head);}
        }
        var gestures=GetComponent<TalkGestures>();if(gestures){gestures.active=social;gestures.speaking=speaking;}
    }
    void LateUpdate()
    {
        if(!drinkingTea || !arm || !fore || !hand || !head || Time.deltaTime<=0f)return;
        float w=Mathf.SmoothStep(0,1,Mathf.Clamp01((Mathf.Sin(Time.time*.36f+phase*6)-.2f)*2));
        Vector3 elbow=arm.position+transform.forward*.3f+transform.right*.2f-Vector3.up*.23f;
        Aim(arm,fore,elbow-arm.position,w);
        Aim(fore,hand,head.position-transform.forward*.15f-Vector3.up*.15f-fore.position,w);
        if(cup){cup.position=hand.position+transform.forward*.045f;cup.rotation=Quaternion.Euler(0,transform.eulerAngles.y,0);}
    }
    static void Aim(Transform a,Transform b,Vector3 direction,float weight)
    {Quaternion desired=Quaternion.FromToRotation(b.position-a.position,direction)*a.rotation;a.rotation=Quaternion.Slerp(a.rotation,desired,weight);}
}
