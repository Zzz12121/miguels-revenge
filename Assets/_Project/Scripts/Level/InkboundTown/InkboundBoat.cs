using UnityEngine;

/// <summary>A preserved ferry route. The passenger never reacts to the visitor.</summary>
public sealed class InkboundBoat : MonoBehaviour
{
    public Vector3[] route;
    [Min(.05f)] public float speed = .7f;
    public float timeOffset;
    public Animator rower;
    public Transform oar;
    [Range(0,1)] public int wakeIndex;
    int segment;
    float distance;
    Quaternion armRest, oarRest;
    Transform upperArm;
    public float Travelled { get; private set; }

    void Start()
    {
        if (route == null || route.Length < 2) { enabled = false; return; }
        float best=float.PositiveInfinity;
        for(int j=0;j<route.Length;j++)
        {
            Vector3 a=route[j],b=route[(j+1)%route.Length];
            Vector3 q=InkboundDisturbanceSource.Closest(transform.position,a,b);
            float error=(q-transform.position).sqrMagnitude;
            if(error<best){best=error;segment=j;distance=Vector3.Distance(a,q);}
        }
        if (rower && rower.isHuman) { upperArm = rower.GetBoneTransform(HumanBodyBones.RightUpperArm); if (upperArm) armRest = upperArm.localRotation; }
        if (oar) oarRest = oar.localRotation;
    }
    void Update()
    {
        if (Time.deltaTime <= 0f) return;
        float step = speed * Time.deltaTime;
        distance += step; Travelled += step;
        float length = Vector3.Distance(route[segment], route[(segment+1)%route.Length]);
        int guard = 0;
        while (distance > length && guard++ < route.Length)
        {
            distance -= length; segment = (segment + 1) % route.Length;
            length = Vector3.Distance(route[segment], route[(segment+1)%route.Length]);
        }
        Vector3 a = route[segment], b = route[(segment+1)%route.Length];
        transform.position = Vector3.Lerp(a,b,distance/Mathf.Max(.01f,length)) + Vector3.up * (Mathf.Sin(Time.time*.75f + timeOffset)*.025f);
        Vector3 direction = b-a; direction.y=0;
        if (direction.sqrMagnitude>.01f) transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(direction),Time.deltaTime*1.8f);
        Vector3 p=transform.position,f=transform.forward;
        Shader.SetGlobalVector(wakeIndex==0?"_InkBoat0":"_InkBoat1",new Vector4(p.x,p.z,f.x,f.z));
    }
    void OnDisable(){Shader.SetGlobalVector(wakeIndex==0?"_InkBoat0":"_InkBoat1",Vector4.zero);}
    void LateUpdate()
    {
        if (Time.deltaTime <= 0f) return;
        float stroke = Mathf.Sin(Time.time*1.65f + timeOffset);
        if (upperArm) upperArm.localRotation = armRest * Quaternion.Euler(-28 + stroke*12,0,-24);
        if (oar) oar.localRotation = oarRest * Quaternion.Euler(stroke*18,0,0);
    }
}
