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
    [HideInInspector] public float avatarSweepAngle=60;
    [HideInInspector] public bool useGroundHeightOverride;
    [HideInInspector] public float groundHeightOverride;
    [HideInInspector] public bool useAvatarBrushTarget;
    [HideInInspector] public Vector3 avatarBrushTarget;
    [HideInInspector] public float avatarVerticalWeight;
    [HideInInspector] public Vector3 avatarGripOffset;
    [HideInInspector] public Vector3 avatarBasePosition;
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
    Transform neck,head,leftShin,rightShin;
    Quaternion neckRest,headRest,leftShinRest,rightShinRest;
    Vector3 aimPoint;
    public void SetAimPoint(Vector3 point)=>aimPoint=point;
    public void SetCombatPose(int stroke,float phase,float weight=1)
    { combatStroke=stroke;combatPhase=Mathf.Clamp01(phase);combatWeight=Mathf.Clamp01(weight); }
    public void ClearCombatPose()
    {
        combatWeight=0;
        if(chest) chest.localRotation=chestRest;
        if(leftThigh) leftThigh.localRotation=leftThighRest;
        if(rightThigh) rightThigh.localRotation=rightThighRest;
        if(leftShin)leftShin.localRotation=leftShinRest;
        if(rightShin)rightShin.localRotation=rightShinRest;
        if(neck)neck.localRotation=neckRest;
        if(head)head.localRotation=headRest;
    }

    void Awake()
    {
        visual=animator.transform; visualPosition=visual.localPosition; visualRotation=visual.localRotation;
        foreach(var t in visual.GetComponentsInChildren<Transform>())
        {
            if(t.name=="Chest") {chest=t;chestRest=t.localRotation;}
            if(t.name=="Left_UpperLeg") {leftThigh=t;leftThighRest=t.localRotation;}
            if(t.name=="Right_UpperLeg") {rightThigh=t;rightThighRest=t.localRotation;}
            if(t.name=="Left_LowerLeg"){leftShin=t;leftShinRest=t.localRotation;}
            if(t.name=="Right_LowerLeg"){rightShin=t;rightShinRest=t.localRotation;}
            if(t.name=="Head"){head=t;headRest=t.localRotation;}
            if(t.name=="Neck"){neck=t;neckRest=t.localRotation;}
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
        if(useAvatarBrushTarget)transform.position=avatarBasePosition;
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
            if(chest){chest.localRotation=chestRest;chest.rotation=Quaternion.AngleAxis(twist,transform.up)*Quaternion.AngleAxis(down?stroke*16:6*wind,transform.right)*chest.rotation;}
            if(leftThigh) leftThigh.localRotation=leftThighRest*Quaternion.Euler(-14*wind*(1-recover)*combatWeight,0,0);
            if(rightThigh) rightThigh.localRotation=rightThighRest*Quaternion.Euler(10*wind*(1-recover)*combatWeight,0,0);
            visual.localPosition=visualPosition+Vector3.down*(Mathf.Sin(p*Mathf.PI)*.09f*combatWeight);
        }
        if(combatWeight>0 && combatStroke>=4)SpecialPose(neutral,ref center,ref rotation);
        brushPivot.SetPositionAndRotation(transform.TransformPoint(center),transform.rotation*rotation);
        if(combatWeight>0 && (combatStroke==11 || combatStroke==15))
        {
            float open=Mathf.SmoothStep(0,1,combatPhase/.4f);
            Solve(rightUpper,rightLower,rightHand,rightUpper.position+transform.right*.5f*open-transform.forward*.3f*open-Vector3.up*.32f,transform.forward,transform.rotation*rightPalmBasis);
            Solve(leftUpper,leftLower,leftHand,leftUpper.position-transform.right*.5f*open-transform.forward*.3f*open-Vector3.up*.32f,transform.forward,transform.rotation*leftPalmBasis);
            brushPivot.position=rightHand.position;
            return;
        }
        Vector3 axis=brushPivot.forward;
        Vector3 palmForward=brushPivot.up;
        Quaternion palmFrame=Quaternion.LookRotation(palmForward,brushPivot.right);
        Vector3 wristOffset=-palmForward*.075f-brushPivot.right*.025f;
        if(combatWeight>0 && combatStroke>=4)
        {
            // Keep both grips within the two arm chains, including the overhead leap pose.
            // An unreachable IK target otherwise leaves the hand visibly behind the shaft.
            Vector3 gripCenter=brushPivot.position;
            for(int pass=0;pass<8;pass++)
            {
                FitGrip(ref gripCenter,rightUpper,rightLower,rightHand,axis*gripSpacing*.5f+wristOffset);
                FitGrip(ref gripCenter,leftUpper,leftLower,leftHand,-axis*gripSpacing*.5f+wristOffset);
            }
            brushPivot.position=gripCenter;
        }
        if(combatWeight>0 && (combatStroke==10 || combatStroke==18) && combatPhase>=.74f && combatPhase<(combatStroke==18?.86f:.82f))
        {
            // Solve AFTER reachable grip adjustment, otherwise FitGrip lifts the bristles
            // again. The damage event runs later in this same LateUpdate frame.
            Vector3 tipVector=brushTip.position-brushPivot.position;
            float length=tipVector.magnitude;
            float groundY=(useGroundHeightOverride?groundHeightOverride:CalligraphyGeometry.Ground(transform.position,transform).y)+.04f;
            float height=Mathf.Clamp(brushPivot.position.y-groundY,0,length*.99f);
            Vector3 contact=brushPivot.position+transform.forward*Mathf.Sqrt(Mathf.Max(0,length*length-height*height))-Vector3.up*height;
            brushPivot.rotation=Quaternion.FromToRotation(tipVector,contact-brushPivot.position)*brushPivot.rotation;
            axis=brushPivot.forward;palmForward=brushPivot.up;
            palmFrame=Quaternion.LookRotation(palmForward,brushPivot.right);
            wristOffset=-palmForward*.075f-brushPivot.right*.025f;
        }
        if(combatWeight>0 && (combatStroke==16 || combatStroke==17) && useGroundHeightOverride)
        {
            Vector3 tipVector=brushTip.position-brushPivot.position;
            float length=tipVector.magnitude,height=Mathf.Clamp(brushPivot.position.y-groundHeightOverride-1.2f,0,length*.99f);
            Vector3 direction=Vector3.ProjectOnPlane(brushPivot.forward,Vector3.up).normalized;
            Vector3 contact=brushPivot.position+direction*Mathf.Sqrt(Mathf.Max(0,length*length-height*height))-Vector3.up*height;
            brushPivot.rotation=Quaternion.FromToRotation(tipVector,contact-brushPivot.position)*brushPivot.rotation;
            axis=brushPivot.forward;palmForward=brushPivot.up;palmFrame=Quaternion.LookRotation(palmForward,brushPivot.right);
            wristOffset=-palmForward*.075f-brushPivot.right*.025f;
        }
        if(useAvatarBrushTarget)
        {
            // The enlarged rig must actually reach the authored fan / slam point. Fit the
            // visual shaft length, then solve both hands; never stretch the character's arms.
            for(int pass=0;pass<5;pass++)
            {
                Vector3 offset=brushTip.position-brushPivot.position,to=avatarBrushTarget-brushPivot.position;
                if(offset.sqrMagnitude<.0001f || to.sqrMagnitude<.0001f)break;
                brushPivot.rotation=Quaternion.FromToRotation(offset,to)*brushPivot.rotation;
                Vector3 size=brushPivot.localScale;size.z*=to.magnitude/offset.magnitude;brushPivot.localScale=size;
                axis=brushPivot.forward;palmForward=brushPivot.up;palmFrame=Quaternion.LookRotation(palmForward,brushPivot.right);
                wristOffset=-palmForward*.075f-brushPivot.right*.025f;
                Vector3 centre=brushPivot.position;
                FitGrip(ref centre,rightUpper,rightLower,rightHand,axis*gripSpacing*.5f+wristOffset);
                FitGrip(ref centre,leftUpper,leftLower,leftHand,-axis*gripSpacing*.5f+wristOffset);
                brushPivot.position=centre;
            }
        }
        if(useAvatarBrushTarget && avatarVerticalWeight>0)
        {
            // Bring the whole upper body over its pillar during the gather, not a one-frame
            // rotation at contact. The arm-chain lengths and both palms remain constrained.
            float w=Mathf.Clamp01(avatarVerticalWeight);
            var q=Quaternion.FromToRotation(brushTip.position-brushPivot.position,Vector3.down)*brushPivot.rotation;
            brushPivot.rotation=Quaternion.Slerp(brushPivot.rotation,q,w);
            axis=brushPivot.forward;palmForward=brushPivot.up;
            wristOffset=-palmForward*.075f-brushPivot.right*.025f;
            Vector3 centre=transform.TransformPoint(neutral+avatarGripOffset);
            centre.y+=Mathf.Sin(Mathf.Clamp01(combatPhase/.74f)*Mathf.PI)*gripSpacing*.65f;
            for(int pass=0;pass<10;pass++)
            {
                FitGrip(ref centre,rightUpper,rightLower,rightHand,axis*gripSpacing*.5f+wristOffset);
                FitGrip(ref centre,leftUpper,leftLower,leftHand,-axis*gripSpacing*.5f+wristOffset);
            }
            Vector3 shift=Vector3.ProjectOnPlane(avatarBrushTarget-centre,Vector3.up)*w;
            transform.position+=shift;brushPivot.position=Vector3.Lerp(brushPivot.position,centre+shift,w);
            float length=Vector3.Distance(brushTip.position,brushPivot.position);
            float targetLength=Mathf.Max(gripSpacing*.55f,brushPivot.position.y-avatarBrushTarget.y);
            Vector3 scale=brushPivot.localScale;scale.z*=Mathf.Lerp(1,targetLength/Mathf.Max(.01f,length),w);brushPivot.localScale=scale;
            palmFrame=Quaternion.LookRotation(palmForward,brushPivot.right);
        }
        Solve(rightUpper,rightLower,rightHand,brushPivot.position+axis*gripSpacing*0.5f+wristOffset,transform.TransformDirection(new Vector3(1,-0.7f,0)),palmFrame*rightPalmBasis*Quaternion.Euler(rightPalmEuler));
        Solve(leftUpper,leftLower,leftHand,brushPivot.position-axis*gripSpacing*0.5f+wristOffset,transform.TransformDirection(new Vector3(-1,-0.7f,0)),palmFrame*leftPalmBasis*Quaternion.Euler(leftPalmEuler));
        for(int i=0;i<fingers.Length;i++)
        {
            float curl=fingers[i].name.Contains("Proximal")?65:fingers[i].name.Contains("Intermediate")?75:40;
            fingers[i].localRotation=Quaternion.Slerp(fingers[i].localRotation,fingerRest[i]*Quaternion.AngleAxis(curl,fingerCurlAxes[i]),gripWeight);
        }
    }
    void SpecialPose(Vector3 neutral,ref Vector3 center,ref Quaternion rotation)
    {
        if(combatStroke>=16)
        {if(neck)neck.localRotation=neckRest;if(head)head.localRotation=headRest;}
        float p=combatPhase,wind=Mathf.SmoothStep(0,1,p/.38f),cut=Mathf.SmoothStep(0,1,(p-.38f)/.28f),recover=Mathf.SmoothStep(0,1,(p-.72f)/.28f);
        float weight=wind*(1-recover);Vector3 grip=neutral;Quaternion q=Quaternion.Euler(-22,64,0);
        float lean=0,twist=0,crouch=0;
        if(combatStroke==4) // Straight thrust, shoulder-led extension and quick withdrawal.
        {
            float thrust=Mathf.SmoothStep(0,1,(p-.34f)/.14f);
            grip=neutral+new Vector3(.05f,-.04f,Mathf.Lerp(-.34f,.58f,thrust))*weight;
            // Hold the shaft level before release; extend shoulders and both arms together.
            q=Quaternion.Slerp(q,Quaternion.identity,Mathf.SmoothStep(0,1,p/.30f)*(1-recover));lean=14*thrust*weight;twist=-8*weight;
        }
        else if(combatStroke<=8)
        {
            bool heavy=combatStroke==6 || combatStroke==8,circle=combatStroke>=7;
            float angle=Mathf.Lerp(65,circle?-295:-85,cut);
            grip=neutral+new Vector3(Mathf.Sin(cut*Mathf.PI*2)*.20f,Mathf.Lerp(-.25f,.42f,cut),.05f)*weight;
            // AngleAxis avoids shortest-path quaternion interpolation collapsing a full circle.
            q=Quaternion.Slerp(q,Quaternion.AngleAxis(angle,Vector3.right)*Quaternion.Euler(0,heavy?-12:12,0),weight);
            twist=Mathf.Lerp(heavy?-38:-18,heavy?42:20,cut)*weight;
            crouch=(heavy?.20f:.07f)*wind*(1-cut);lean=Mathf.Lerp(heavy?18:8,-9,cut)*weight;
        }
        else if(combatStroke==9 || combatStroke==12)
        {
            float dot=combatStroke==12?Mathf.Sin(cut*Mathf.PI)*.20f:0;
            grip=neutral+new Vector3(.06f,.04f,.20f+dot);
            Vector3 direction=transform.InverseTransformDirection(aimPoint-transform.TransformPoint(grip));
            q=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction):Quaternion.identity;
            lean=5+dot*20;twist=-8;
        }
        else if(combatStroke==10 || combatStroke==18)
        {
            float raise=Mathf.SmoothStep(0,1,p/.18f),slam=Mathf.SmoothStep(0,1,(p-.58f)/.16f),settle=Mathf.SmoothStep(0,1,(p-.8f)/.2f);
            grip=Vector3.Lerp(neutral,neutral+new Vector3(0,.92f,-.06f),raise);
            grip=Vector3.Lerp(grip,neutral+new Vector3(0,-.26f,.17f),slam);
            q=Quaternion.Slerp(Quaternion.Euler(-22,64,0),Quaternion.Euler(-28,15,0),raise);
            q=Quaternion.Slerp(q,Quaternion.Euler(58,0,0),slam);
            // At impact, solve pitch from the actual shaft length so the bristles reach ground.
            if(p>=.72f && p<.82f)
            {
                float length=Vector3.Distance(brushPivot.position,brushTip.position);
                float height=transform.TransformPoint(grip).y-(useGroundHeightOverride?groundHeightOverride:CalligraphyGeometry.Ground(transform.position,transform).y);
                q=Quaternion.Euler(Mathf.Asin(Mathf.Clamp(height/Mathf.Max(.1f,length),0,.98f))*Mathf.Rad2Deg,0,0);
            }
            crouch=.22f*Mathf.Sin(Mathf.Clamp01(p/.25f)*Mathf.PI)+.23f*slam*(1-settle);
            lean=Mathf.Lerp(-12,24,slam)*(1-settle);
            grip=Vector3.Lerp(grip,neutral,settle);q=Quaternion.Slerp(q,Quaternion.Euler(-22,64,0),settle);
        }
        else if(combatStroke==11 || combatStroke==15)
        {
            float open=Mathf.SmoothStep(0,1,p/.4f);lean=-27*open;grip=neutral+new Vector3(.65f,-.3f,-.3f)*open;q=Quaternion.Euler(58,35,-20);
            if(neck){neck.localRotation=neckRest;neck.rotation=Quaternion.AngleAxis(-16*open,transform.right)*neck.rotation;}
            if(head){head.localRotation=headRest;head.rotation=Quaternion.AngleAxis(-18*open,transform.right)*head.rotation;}
        }
        else if(combatStroke==13 || combatStroke==14)
        {
            float side=combatStroke==13?1:-1;
            Vector3 high=neutral+new Vector3(side*1.25f,1.15f,1.3f);
            Vector3 low=neutral+new Vector3(-side*1.25f,-.75f,1.3f);
            grip=neutral+new Vector3(side*Mathf.Lerp(.12f,-.12f,cut),Mathf.Lerp(.18f,-.13f,cut),.04f)*weight;
            Quaternion diagonal=Quaternion.LookRotation(Vector3.Lerp(high,low,cut)-grip);
            q=Quaternion.Slerp(q,diagonal,weight);twist=side*Mathf.Lerp(-18,23,cut)*weight;lean=8*cut*weight;
        }
        else if(combatStroke==16 || combatStroke==17)
        {
            float sweep=Mathf.SmoothStep(0,1,(p-.2f)/.6f);
            float side=combatStroke==16?1:-1;
            float yaw=Mathf.Lerp(avatarSweepAngle*.5f,-avatarSweepAngle*.5f,sweep)*side;
            grip=neutral+new Vector3(-.06f*side,-.08f,.08f);
            q=Quaternion.Euler(24,yaw,0);
            lean=9+Mathf.Sin(sweep*Mathf.PI)*7;twist=yaw*.6f;
        }
        center=Vector3.Lerp(center,grip,combatWeight);rotation=Quaternion.Slerp(rotation,q,combatWeight);
        if(chest){chest.localRotation=chestRest;chest.rotation=Quaternion.AngleAxis(twist,transform.up)*Quaternion.AngleAxis(lean,transform.right)*chest.rotation;}
        if(leftThigh){leftThigh.localRotation=leftThighRest;leftThigh.rotation=Quaternion.AngleAxis(-crouch*95,transform.right)*leftThigh.rotation;}
        if(rightThigh){rightThigh.localRotation=rightThighRest;rightThigh.rotation=Quaternion.AngleAxis(-crouch*85,transform.right)*rightThigh.rotation;}
        if(leftShin){leftShin.localRotation=leftShinRest;leftShin.rotation=Quaternion.AngleAxis(crouch*150,transform.right)*leftShin.rotation;}
        if(rightShin){rightShin.localRotation=rightShinRest;rightShin.rotation=Quaternion.AngleAxis(crouch*145,transform.right)*rightShin.rotation;}
        if(combatStroke==10)
        {
            float tuck=Mathf.SmoothStep(0,1,(p-.2f)/.1f)*(1-Mathf.SmoothStep(0,1,(p-.58f)/.16f));
            if(leftThigh)leftThigh.rotation=Quaternion.AngleAxis(-38*tuck,transform.right)*leftThigh.rotation;
            if(rightThigh)rightThigh.rotation=Quaternion.AngleAxis(-22*tuck,transform.right)*rightThigh.rotation;
            if(leftShin)leftShin.rotation=Quaternion.AngleAxis(65*tuck,transform.right)*leftShin.rotation;
            if(rightShin)rightShin.rotation=Quaternion.AngleAxis(40*tuck,transform.right)*rightShin.rotation;
        }
        visual.localPosition=visualPosition+Vector3.down*crouch;
    }
    static void FitGrip(ref Vector3 center,Transform upper,Transform lower,Transform hand,Vector3 offset)
    {
        if(!upper || !lower || !hand)return;
        float reach=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,hand.position)-.003f;
        Vector3 delta=center+offset-upper.position;
        if(delta.magnitude>reach)center-=delta.normalized*(delta.magnitude-reach);
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
