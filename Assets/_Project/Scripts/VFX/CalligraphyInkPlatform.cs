using UnityEngine;

/// <summary>A level, convex, kinematic foothold. Carries a grounded capsule during ascent; owns no input.</summary>
public sealed class CalligraphyInkPlatform : MonoBehaviour
{
    public float TargetTop {get;private set;}
    public float Radius {get;private set;}
    public float Top=>transform.position.y;
    public bool Raised=>Mathf.Abs(Top-TargetTop)<.05f;
    public float Depth {get;private set;}
    public Vector3[] Rim {get;private set;}
    Rigidbody body;
    CharacterController player;
    ThirdPersonController motor;
    float speed;
    float riseAt;
    public void ConfigureShape(Vector3[] rim,float depth,float delay)
    {Rim=rim;Depth=depth;riseAt=Time.time+Mathf.Max(0,delay);}
    public bool TryLedge(Vector3 point,out Vector3 edge,out Vector3 inward)
    {
        edge=transform.position;inward=Vector3.zero;if(Rim==null)return false;
        Vector3 p=transform.InverseTransformPoint(point);float best=float.MaxValue;
        for(int i=0;i<Rim.Length;i++)
        {
            Vector3 a=Rim[i],b=Rim[(i+1)%Rim.Length],ab=b-a;
            Vector3 q=a+ab*Mathf.Clamp01(Vector3.Dot(p-a,ab)/ab.sqrMagnitude);
            float distance=Vector3.ProjectOnPlane(q-p,Vector3.up).sqrMagnitude;
            if(distance>=best)continue;best=distance;edge=transform.TransformPoint(q);
            inward=transform.TransformDirection(Vector3.Cross(ab,Vector3.up).normalized);
        }
        return Vector3.Dot(point-edge,inward)<.35f;
    }
    public void Initialize(float targetTop,float radius,float riseSpeed,PlayerHealth victim)
    {
        TargetTop=targetTop;Radius=radius;speed=Mathf.Max(.1f,riseSpeed);
        if(victim){player=victim.GetComponent<CharacterController>();motor=victim.GetComponent<ThirdPersonController>();}
        body=gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
        body.interpolation=RigidbodyInterpolation.Interpolate;
    }
    void FixedUpdate()
    {
        if(!body || Raised || Time.time<riseAt)return;
        Vector3 from=body.position,to=from;to.y=Mathf.MoveTowards(from.y,TargetTop,speed*Time.fixedDeltaTime);
        bool riding=false;
        if(player && player.enabled && player.gameObject.activeInHierarchy && motor && !motor.IsBallistic)
        {
            float feet=player.bounds.min.y;
            Vector3 flat=Vector3.ProjectOnPlane(player.transform.position-from,Vector3.up);
            riding=flat.magnitude<Radius*.8f && Mathf.Abs(feet-from.y)<.28f && player.isGrounded;
        }
        body.MovePosition(to);
        if(riding)player.Move(to-from);
    }
}
