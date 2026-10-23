using UnityEngine;

/// <summary>Visual-only two-hand constraint for the elder's Generic rig. No damage or AI.</summary>
[DefaultExecutionOrder(180)]
public sealed class CalligraphyBrushPresentation : MonoBehaviour
{
    public Animator animator;
    public Transform brushPivot, brushTip;
    public Transform rightUpper, rightLower, rightHand, leftUpper, leftLower, leftHand;
    public Vector3 rightPalmEuler = Vector3.zero;
    public Vector3 leftPalmEuler = Vector3.zero;
    [Range(0,1)] public float gripWeight = 1;
    public bool showcase = false;
    public float cycleSeconds = 7;
    [Tooltip("World metres between palms along the shaft")]
    public float gripSpacing = 0.43f;
    public string[] animationStates;
    public AnimationClip[] animationClips;
    public string currentAnimation;
    public bool Swinging { get; private set; }
    float clock, swingClock=-1;
    Vector3 visualPosition;
    Quaternion visualRotation;
    Transform visual;
    Quaternion[] fingerRest;
    Transform[] fingers;
    Quaternion rightPalmBasis, leftPalmBasis;
    Vector3[] fingerCurlAxes;
    int combatStroke;
    float combatPhase,combatWeight;
    Transform chest,leftThigh,rightThigh;
    Quaternion chestRest,leftThighRest,rightThighRest;
    public void SetCombatPose(int stroke,float phase,float weight=1)
    { combatStroke=stroke;combatPhase=Mathf.Clamp01(phase);combatWeight=Mathf.Clamp01(weight); }
    public void ClearCombatPose()
    {
        combatWeight=0;
        if(chest) chest.localRotation=chestRest;
        if(leftThigh) leftThigh.localRotation=leftThighRest;
        if(rightThigh) rightThigh.localRotation=rightThighRest;
    }

    void Awake()
    {
        visual=animator.transform; visualPosition=visual.localPosition; visualRotation=visual.localRotation;
        foreach(var t in visual.GetComponentsInChildren<Transform>())
        {
            if(t.name=="Chest") {chest=t;chestRest=t.localRotation;}
            if(t.name=="Left_UpperLeg") {leftThigh=t;leftThighRest=t.localRotation;}
            if(t.name=="Right_UpperLeg") {rightThigh=t;rightThighRest=t.localRotation;}
        }
        rightPalmBasis=PalmBasis(rightHand,"Right_"); leftPalmBasis=PalmBasis(leftHand,"Left_");
        var list=new System.Collections.Generic.List<Transform>();
        foreach(var t in visual.GetComponentsInChildren<Transform>())
            if((t.name.Contains("Proximal") || t.name.Contains("Intermediate") || t.name.Contains("Distal")) && !t.name.EndsWith("End") && !t.name.Contains("Thumb")) list.Add(t);
        fingers=list.ToArray(); fingerRest=new Quaternion[fingers.Length];fingerCurlAxes=new Vector3[fingers.Length];
        for(int i=0;i<fingers.Length;i++)
        {
            fingerRest[i]=fingers[i].localRotation;
            bool right=fingers[i].name.StartsWith("Right_"); Transform hand=right?rightHand:leftHand;
            Quaternion basis=right?rightPalmBasis:leftPalmBasis;
            Vector3 axis=(hand.rotation*Quaternion.Inverse(basis))*Vector3.right;
            fingerCurlAxes[i]=fingers[i].InverseTransformDirection(axis);
        }
    }
    Quaternion PalmBasis(Transform hand,string prefix)
    {
        Transform middle=null,index=null,pinky=null;
        foreach(var t in hand.GetComponentsInChildren<Transform>())
        {
            if(t.name==prefix+"MiddleProximal") middle=t;
            if(t.name==prefix+"IndexProximal") index=t;
            if(t.name==prefix+"PinkyProximal") pinky=t;
        }
        if(!middle || !index || !pinky) return Quaternion.identity;
        Vector3 forward=(middle.position-hand.position).normalized;
        Vector3 normal=Vector3.Cross(index.position-pinky.position,forward).normalized;
        return Quaternion.Inverse(Quaternion.LookRotation(forward,normal))*hand.rotation;
    }
    public void PlayAnimation(int index)
    {
        if(!animator || animationStates==null || index<0 || index>=animationStates.Length) return;
        currentAnimation=animationStates[index]; animator.CrossFadeInFixedTime(currentAnimation,0.18f);
    }
    [ContextMenu("Preview/Two-handed brush sweep")]
    public void Sweep() { swingClock=0; }
    [ContextMenu("Preview/Next imported animation")]
    public void NextAnimation()
    {
        showcase=false;
        int index=System.Array.IndexOf(animationStates,currentAnimation);
        PlayAnimation((index+1)%Mathf.Max(1,animationStates.Length));
    }
    [ContextMenu("Preview/Resume brush showcase")]
    public void ResumeShowcase() { showcase=true; clock=0; PlayAnimation(System.Array.IndexOf(animationStates,"standing_relax")); }
    void Update()
    {
        if(showcase)
        {
            clock+=Time.deltaTime;
            if(clock>=cycleSeconds) { clock=0; Sweep(); }
        }
        if(swingClock>=0) { swingClock+=Time.deltaTime; if(swingClock>2.1f) swingClock=-1; }
        Swinging=swingClock>=0 || combatWeight>.1f;
    }
    void LateUpdate()
    {
        if(!brushPivot || !rightHand || !leftHand || !animator) return;
        // Never allow FBX object-level animation to rotate/translate the character out of its root.
        visual.localPosition=visualPosition; visual.localRotation=visualRotation;
        float t=swingClock;
        float breath=Mathf.Sin(Time.time*1.6f)*0.018f;
        Vector3 neutral=transform.InverseTransformPoint((rightUpper.position+leftUpper.position)*.5f)+new Vector3(0,-.24f,.30f);
        Vector3 center=neutral+Vector3.up*breath;
        Quaternion rotation=Quaternion.Euler(-22,64,0);
        if(t>=0)
        {
            float wind=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/0.65f));
            float cut=Mathf.SmoothStep(0,1,Mathf.Clamp01((t-0.65f)/0.42f));
            float recover=Mathf.SmoothStep(0,1,Mathf.Clamp01((t-1.15f)/0.95f));
            Vector3 high=neutral+new Vector3(.12f,.28f,-.06f), low=neutral+new Vector3(-.12f,-.13f,.02f);
            center=Vector3.Lerp(Vector3.Lerp(center,high,wind),low,cut);
            center=Vector3.Lerp(center,neutral,recover);
            rotation=Quaternion.Slerp(Quaternion.Slerp(rotation,Quaternion.Euler(-64,-46,-20),wind),Quaternion.Euler(22,112,18),cut);
            rotation=Quaternion.Slerp(rotation,Quaternion.Euler(-22,64,0),recover);
        }
        if(combatWeight>0)
        {
            float p=combatPhase;
            float wind=Mathf.SmoothStep(0,1,Mathf.Clamp01(p/.38f));
            float stroke=Mathf.SmoothStep(0,1,Mathf.Clamp01((p-.38f)/.28f));
            float recover=Mathf.SmoothStep(0,1,Mathf.Clamp01((p-.72f)/.28f));
            float side=combatStroke%2==0?1:-1;
            bool down=combatStroke==3,up=combatStroke==2;
            Vector3 from=neutral+new Vector3(.12f*side,down?.48f:.18f,-.10f);
            Vector3 to=neutral+new Vector3(-.2f*side,up?.46f:-.22f,.18f);
            Quaternion a=Quaternion.Euler(down?-85:up?35:-32,down?0:-65*side,-15*side);
            Quaternion b=Quaternion.Euler(down?72:up?-72:8,down?0:80*side,20*side);
            Vector3 grip=Vector3.Lerp(Vector3.Lerp(neutral,from,wind),to,stroke);
            center=Vector3.Lerp(center,Vector3.Lerp(grip,neutral,recover),combatWeight);
            rotation=Quaternion.Slerp(rotation,Quaternion.Slerp(Quaternion.Slerp(Quaternion.Slerp(Quaternion.Euler(-22,64,0),a,wind),b,stroke),Quaternion.Euler(-22,64,0),recover),combatWeight);
            float twist=Mathf.Lerp(-22*side,28*side,stroke)*wind*(1-recover)*combatWeight;
            if(chest) chest.localRotation=chestRest*Quaternion.Euler(down?stroke*16:6*wind,twist,0);
            if(leftThigh) leftThigh.localRotation=leftThighRest*Quaternion.Euler(-14*wind*(1-recover)*combatWeight,0,0);
            if(rightThigh) rightThigh.localRotation=rightThighRest*Quaternion.Euler(10*wind*(1-recover)*combatWeight,0,0);
            visual.localPosition=visualPosition+Vector3.down*(Mathf.Sin(p*Mathf.PI)*.09f*combatWeight);
        }
        brushPivot.SetPositionAndRotation(transform.TransformPoint(center),transform.rotation*rotation);
        Vector3 axis=brushPivot.forward;
        Vector3 palmForward=brushPivot.up;
        Quaternion palmFrame=Quaternion.LookRotation(palmForward,brushPivot.right);
        Vector3 wristOffset=-palmForward*.075f-brushPivot.right*.025f;
        Solve(rightUpper,rightLower,rightHand,brushPivot.position+axis*gripSpacing*0.5f+wristOffset,transform.TransformDirection(new Vector3(1,-0.7f,0)),palmFrame*rightPalmBasis*Quaternion.Euler(rightPalmEuler));
        Solve(leftUpper,leftLower,leftHand,brushPivot.position-axis*gripSpacing*0.5f+wristOffset,transform.TransformDirection(new Vector3(-1,-0.7f,0)),palmFrame*leftPalmBasis*Quaternion.Euler(leftPalmEuler));
        for(int i=0;i<fingers.Length;i++)
        {
            float curl=fingers[i].name.Contains("Proximal")?65:fingers[i].name.Contains("Intermediate")?75:40;
            fingers[i].localRotation=Quaternion.Slerp(fingers[i].localRotation,fingerRest[i]*Quaternion.AngleAxis(curl,fingerCurlAxes[i]),gripWeight);
        }
    }
    void Solve(Transform upper,Transform lower,Transform hand,Vector3 target,Vector3 pole,Quaternion handRotation)
    {
        if(!upper || !lower) return;
        float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,hand.position);
        Vector3 delta=target-upper.position; float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+0.001f,a+b-0.001f);
        Vector3 direction=delta.normalized, bend=Vector3.ProjectOnPlane(pole,direction).normalized;
        float x=(a*a-b*b+distance*distance)/(2*distance);
        Vector3 elbow=upper.position+direction*x+bend*Mathf.Sqrt(Mathf.Max(0,a*a-x*x));
        upper.rotation=Quaternion.Slerp(upper.rotation,Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation,gripWeight);
        lower.rotation=Quaternion.Slerp(lower.rotation,Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation,gripWeight);
        hand.rotation=Quaternion.Slerp(hand.rotation,handRotation,gripWeight);
    }
}
