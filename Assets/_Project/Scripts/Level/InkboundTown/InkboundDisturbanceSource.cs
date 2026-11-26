using System.Collections.Generic;
using UnityEngine;

/// <summary>Visual-only capsule or swept tip. Never sends damage or changes its owner's motion.</summary>
[DefaultExecutionOrder(11000)]
public sealed class InkboundDisturbanceSource : MonoBehaviour
{
    public enum Shape { BodyCapsule, SweptTip, Sphere }
    public Shape shape;
    [Min(.03f)] public float radius=.34f;
    [Range(0,2)] public float strength=1;
    public Vector3 localBottom=new(0,.35f,0),localTop=new(0,1.3f,0);
    public bool onlyWhileMoving;
    [Min(.01f)] public float minimumSpeed=.15f;
    public static readonly List<InkboundDisturbanceSource> Active=new();
    public Vector3 From {get;private set;}
    public Vector3 To {get;private set;}
    public Vector3 Velocity {get;private set;}
    public float Intensity => !onlyWhileMoving || Velocity.magnitude>=minimumSpeed?strength:0;
    Vector3 previous;
    bool visualSweep;
    public void SetVisualSweep(Vector3 from,Vector3 to,float size,Vector3 velocity)
    {visualSweep=true;From=from;To=to;radius=size;Velocity=Vector3.ClampMagnitude(velocity,18);strength=1;}
    void OnEnable(){previous=transform.position;From=To=previous;if(!Active.Contains(this))Active.Add(this);}
    void OnDisable(){Active.Remove(this);}
    void LateUpdate()
    {
        if(Time.deltaTime<=0||visualSweep)return;
        Vector3 position=transform.position;
        Velocity=Vector3.ClampMagnitude((position-previous)/Mathf.Max(.001f,Time.deltaTime),18);
        if(shape==Shape.BodyCapsule)
        {
            var health=GetComponent<PlayerHealth>();
            Vector3 foot=health?new Vector3(position.x,health.FeetY,position.z):position;
            From=foot+transform.rotation*localBottom;To=foot+transform.rotation*localTop;
        }
        else {From=shape==Shape.SweptTip?previous:position;To=position;}
        previous=position;
    }
    public static Vector3 Closest(Vector3 point,Vector3 from,Vector3 to)
    {Vector3 line=to-from;return from+line*Mathf.Clamp01(Vector3.Dot(point-from,line)/Mathf.Max(.0001f,line.sqrMagnitude));}
}
