using System.Collections.Generic;
using UnityEngine;

public sealed class CalligraphyGroundImpact : MonoBehaviour
{
    readonly List<Transform> debris=new();
    readonly List<Vector3> origins=new(),velocities=new();
    Renderer[] renderers;
    float age,delay;
    public static void Create(Transform parent,CalligraphyInkField ink,Vector3 center,float radius,CalligraphyBossCombat.DragonSettings settings=null)
    {
        var go=new GameObject("Ink dragon fracture impact");go.transform.SetParent(parent,false);go.transform.position=center;
        var effect=go.AddComponent<CalligraphyGroundImpact>();
        effect.delay=settings?.debrisDelay??0;
        for(int i=0;i<14;i++)
        {
            float a=i*Mathf.PI*2/14;
            Vector3 d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),side=Vector3.Cross(d,Vector3.up);
            var points=new Vector3[7];
            for(int k=0;k<7;k++)points[k]=d*(k/6f)*radius*Random.Range(.75f,1)+side*Random.Range(-.25f,.25f)+Vector3.up*.035f;
            CalligraphyGeometry.Line("Radial ink fissure",go.transform,ink.sculptureMaterial,points,.13f);
            if(i%2==0)CalligraphyGeometry.Line("Fracture branch",go.transform,ink.sculptureMaterial,new[]{points[3],points[4]+side*.7f,points[5]+side*1.4f},.07f);
        }
        for(int i=0;i<(settings?.debrisCount??18);i++)
        {
            Vector3 p=Random.insideUnitSphere*radius*.7f;p.y=.1f;
            var mesh=new Mesh{name="Fractured stone"};
            mesh.vertices=new[]{new Vector3(-.3f,0,-.2f),new Vector3(.35f,0,-.2f),new Vector3(.15f,.45f,.15f),new Vector3(-.2f,0,.3f)};
            mesh.triangles=new[]{0,2,1,0,3,2,1,2,3,0,1,3};mesh.RecalculateNormals();
            var r=CalligraphyGeometry.MeshObject("Lifted ink stone",go.transform,mesh,ink.sculptureMaterial);r.transform.localPosition=p;
            r.transform.localScale=Vector3.one*Random.Range(.7f,2.3f)*(settings?.debrisScale??1);
            effect.debris.Add(r.transform);effect.origins.Add(p);effect.velocities.Add(p.normalized*Random.Range(1,3)+Vector3.up*Random.Range(.6f,1)*(settings?.debrisLift??7));
        }
        effect.renderers=go.GetComponentsInChildren<Renderer>();
    }
    void Update()
    {
        age+=Time.deltaTime;
        float motion=Mathf.Max(0,age-delay);
        for(int i=0;i<debris.Count;i++)
        {
            Vector3 p=origins[i]+velocities[i]*motion+Vector3.down*(4.9f*motion*motion);p.y=Mathf.Max(.04f,p.y);
            debris[i].localPosition=p;if(p.y>.05f)debris[i].Rotate(new Vector3(50,80,35)*Time.deltaTime);
        }
        float fade=Mathf.Clamp01((age-3)/2);
        foreach(var r in renderers)CalligraphyGeometry.Tint(r,new Color(.025f,.028f,.03f),1,fade);
        if(age>=5)Destroy(gameObject);
    }
}
