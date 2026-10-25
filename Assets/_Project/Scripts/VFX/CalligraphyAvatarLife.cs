using UnityEngine;

/// <summary>Robe-only secondary motion. The shared head/hair mesh retains its original posed shape.</summary>
[DefaultExecutionOrder(320)]
public sealed class CalligraphyAvatarLife : MonoBehaviour
{
    CalligraphyBrushPresentation presentation;
    CalligraphyBossCombat.AvatarVisualSettings settings;
    Transform head,chest;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    Vector3 lastHead,inertia,velocity,headRight,headUp,headForward;
    public void Initialize(CalligraphyBrushPresentation rig,Transform headBone,CalligraphyBossCombat.AvatarVisualSettings tuning,Renderer[] surfaces)
    {
        block=new MaterialPropertyBlock();presentation=rig;head=headBone;settings=tuning;renderers=surfaces;
        if(rig.animator && rig.animator.isHuman)
        {
            chest=rig.animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if(!chest)chest=rig.animator.GetBoneTransform(HumanBodyBones.Chest);
        }
        if(!chest && rig.animator)
            foreach(var bone in rig.animator.GetComponentsInChildren<Transform>(true))
                if(bone.name=="UpperChest" || (!chest && bone.name=="Chest"))chest=bone;
        if(head)
        {
            lastHead=head.position;headRight=head.InverseTransformDirection(transform.right);
            headUp=head.InverseTransformDirection(transform.up);headForward=head.InverseTransformDirection(transform.forward);
        }
    }
    void LateUpdate()
    {
        if(!head || renderers==null)return;
        float dt=Mathf.Max(.001f,Time.deltaTime),scale=settings.size/100;
        Vector3 movement=Vector3.ClampMagnitude((head.position-lastHead)/dt,35);lastHead=head.position;
        inertia=Vector3.SmoothDamp(inertia,-movement*.055f,ref velocity,.55f,8,dt);
        Vector3 r=head.TransformDirection(headRight),u=head.TransformDirection(headUp),f=head.TransformDirection(headForward);
        foreach(var renderer in renderers)
        {
            if(!renderer)continue;renderer.GetPropertyBlock(block);
            block.SetVector("_HeadCenter",head.position);block.SetVector("_HeadRight",r);block.SetVector("_HeadUp",u);block.SetVector("_HeadForward",f);
            block.SetVector("_ClothTop",chest?chest.position:head.position-u*20*scale);
            block.SetFloat("_AvatarScale",scale);block.SetFloat("_FlowSpeed",settings.flowSpeed);
            block.SetFloat("_RobeAmplitude",settings.robeAmplitude);
            block.SetFloat("_RobeSpeed",settings.robeSpeed);block.SetVector("_MotionLag",inertia*settings.motionInertia);
            block.SetVector("_RightHand",presentation.rightHand.position);block.SetVector("_LeftHand",presentation.leftHand.position);
            // No reliable chest boundary means no secondary displacement, rather than a guessed face mask.
            block.SetFloat("_BodyDeform",chest && renderer is SkinnedMeshRenderer?1:0);
            renderer.SetPropertyBlock(block);
        }
    }
}
