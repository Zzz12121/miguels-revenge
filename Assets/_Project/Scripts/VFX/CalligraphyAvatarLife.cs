using UnityEngine;

/// <summary>Head-relative detail masks follow the posed rig. Delayed motion carries hair and robe.</summary>
[DefaultExecutionOrder(320)]
public sealed class CalligraphyAvatarLife : MonoBehaviour
{
    CalligraphyBrushPresentation presentation;
    CalligraphyBossCombat.AvatarVisualSettings settings;
    Transform head,player,leftEye,rightEye;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    Vector3 lastHead,inertia,velocity,eyeRight,eyeUp,eyeForward;
    Vector2 gaze;
    public void Initialize(CalligraphyBrushPresentation rig,Transform headBone,Transform target,CalligraphyBossCombat.AvatarVisualSettings tuning,Renderer[] surfaces)
    {
        block=new MaterialPropertyBlock();presentation=rig;head=headBone;player=target;settings=tuning;renderers=surfaces;
        foreach(var t in rig.animator.GetComponentsInChildren<Transform>(true))
        {if(t.name=="Left_Eye")leftEye=t;if(t.name=="Right_Eye")rightEye=t;}
        if(head)
        {
            lastHead=head.position;eyeRight=head.InverseTransformDirection(transform.right);
            eyeUp=head.InverseTransformDirection(transform.up);eyeForward=head.InverseTransformDirection(transform.forward);
        }
    }
    void LateUpdate()
    {
        if(!head || renderers==null)return;
        float dt=Mathf.Max(.001f,Time.deltaTime),scale=settings.size/100;
        Vector3 movement=Vector3.ClampMagnitude((head.position-lastHead)/dt,35);lastHead=head.position;
        inertia=Vector3.SmoothDamp(inertia,-movement*.055f,ref velocity,.55f,8,dt);
        Vector3 r=head.TransformDirection(eyeRight),u=head.TransformDirection(eyeUp),f=head.TransformDirection(eyeForward);
        Vector3 eyes=leftEye && rightEye?(leftEye.position+rightEye.position)*.5f:head.position;
        Vector3 direction=player?(player.position-eyes).normalized:f;
        Vector2 desired=new Vector2(Vector3.Dot(direction,r)*.18f,Vector3.Dot(direction,u)*.12f)+settings.gazeOffset;
        gaze=Vector2.Lerp(gaze,Vector2.ClampMagnitude(desired,.22f),1-Mathf.Exp(-settings.gazeSpeed*dt));
        foreach(var renderer in renderers)
        {
            if(!renderer)continue;renderer.GetPropertyBlock(block);
            block.SetVector("_HeadCenter",eyes);block.SetVector("_HeadRight",r);block.SetVector("_HeadUp",u);block.SetVector("_HeadForward",f);
            block.SetVector("_EyeLeft",leftEye?leftEye.position:eyes-r*.6f*scale);
            block.SetVector("_EyeRight",rightEye?rightEye.position:eyes+r*.6f*scale);
            block.SetVector("_Gaze",gaze);block.SetFloat("_EyeVisibility",settings.eyeVisibility);block.SetFloat("_EyeContrast",settings.eyeContrast);
            block.SetFloat("_AvatarScale",scale);block.SetFloat("_FlowSpeed",settings.flowSpeed);
            block.SetVector("_HairMotion",new Vector4(settings.hairAmplitude,settings.hairSpeed,settings.beardAmplitude,settings.robeAmplitude));
            block.SetFloat("_RobeSpeed",settings.robeSpeed);block.SetVector("_MotionLag",inertia*settings.motionInertia);
            block.SetVector("_RightHand",presentation.rightHand.position);block.SetVector("_LeftHand",presentation.leftHand.position);
            block.SetFloat("_BodyDeform",renderer is SkinnedMeshRenderer?1:0);
            renderer.SetPropertyBlock(block);
        }
    }
}
