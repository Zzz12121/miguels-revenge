using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Three delayed brush-tip trails: dense stroke, broad wash, narrow pale edge.</summary>
[DefaultExecutionOrder(230)]
public sealed class CalligraphyBrushWake : MonoBehaviour
{
    CalligraphyBrushPresentation brush;
    CalligraphyInkField ink;
    TrailRenderer[] trails;
    Vector3 previous;
    float mistClock;
    bool wasActive;
    public void Initialize(CalligraphyBrushPresentation source,CalligraphyInkField field)
    {
        brush=source;ink=field;previous=brush.brushTip.position;trails=new TrailRenderer[3];
        for(int i=0;i<3;i++)
        {
            var go=new GameObject("Layered brush wake "+i);go.transform.SetParent(transform,false);go.transform.position=previous;
            var t=go.AddComponent<TrailRenderer>();trails[i]=t;t.sharedMaterial=field.ribbonMaterial;t.time=i==0?.65f:1.05f;t.minVertexDistance=.045f;
            t.widthMultiplier=i==0?.45f:i==1?.85f:.08f;t.widthCurve=AnimationCurve.EaseInOut(0,1,1,0);t.numCornerVertices=4;t.numCapVertices=3;
            t.shadowCastingMode=ShadowCastingMode.Off;t.receiveShadows=false;t.emitting=false;
            var c=i==2?new Color(.87f,.89f,.9f,.6f):new Color(.002f,.003f,.004f,i==0?.98f:.36f);
            CalligraphyGeometry.Tint(t,c);t.startColor=Color.white;t.endColor=new Color(1,1,1,0);
        }
    }
    void LateUpdate()
    {
        if(!brush || !brush.brushTip || trails==null)return;
        Vector3 p=brush.brushTip.position,v=(p-previous)/Mathf.Max(.001f,Time.deltaTime);
        bool active=brush.enabled && v.magnitude>2 && v.magnitude<70;
        for(int i=0;i<3;i++)
        {
            trails[i].transform.position=p+Vector3.up*(i==2?.15f:0);
            if(v.magnitude>=70 || active&&!wasActive)trails[i].Clear();
            trails[i].emitting=active;
        }
        wasActive=active;
        mistClock-=Time.deltaTime;if(active && mistClock<=0){mistClock=.16f;ink.Emit(p,-v*.04f,.22f,1.1f);}
        previous=p;
    }
    void OnDisable(){if(trails!=null)foreach(var t in trails)if(t){t.emitting=false;t.Clear();}}
}
