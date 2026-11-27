using System.Collections.Generic;
using UnityEngine;

/// <summary>Town-owned permanent scenery removal. Ultimate cleanup never reconstructs these objects.</summary>
[DefaultExecutionOrder(13000)]
public sealed class InkboundTownSweepDissolve : MonoBehaviour
{
    sealed class Subject
    {
        public Transform root;
        public Renderer[] surfaces;
        public float fade=-1;
        public bool queued;
    }
    readonly List<Subject> subjects=new();
    readonly Queue<Subject> bursts=new();
    readonly List<Subject> fading=new();
    readonly List<CalligraphyInkField> fields=new();
    MaterialPropertyBlock block;
    int fieldIndex;
    public int DissolvedObjects {get;private set;}
    public int PendingObjects=>fading.Count;

    void Start()
    {
        block=new MaterialPropertyBlock();
        // The first level is authored content categories; their children are complete houses,
        // trees, resident rigs, boats and props. Leave continuous streets/river/terrain as support.
        foreach(Transform category in transform)
        {
            foreach(Transform item in category)
            {
                if(item.GetComponentInChildren<PlayerHealth>(true)||item.GetComponentInChildren<CalligraphyBossCombat>(true))continue;
                if(category.name.StartsWith("01 "))
                {
                    // Continuous street meshes and bridges keep the player's landing surface.
                    foreach(var echo in item.GetComponentsInChildren<InkboundEcho>(true))Register(echo.transform);
                    continue;
                }
                if(item.GetComponentInChildren<Terrain>(true))continue;
                // This single continuous landscape contains the valley floor and distant
                // encircling ridges. Its AABB surrounds the whole town, even though the
                // actual mountains are beyond the brush; it is not a sweepable prop.
                if(item.name=="Irregular mountain basin around the town"||item.name.StartsWith("V3 brush ridge "))continue;
                Register(item);
            }
        }
    }
    void Register(Transform root)
    {
        var surfaces=new List<Renderer>();
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if(r is ParticleSystemRenderer||r is TrailRenderer||r is LineRenderer)continue;
            surfaces.Add(r);
        }
        if(surfaces.Count>0)subjects.Add(new Subject{root=root,surfaces=surfaces.ToArray()});
    }
    static Bounds BoundsOf(Subject s)
    {
        Bounds bounds=s.surfaces[0].bounds;
        for(int i=1;i<s.surfaces.Length;i++)if(s.surfaces[i])bounds.Encapsulate(s.surfaces[i].bounds);
        return bounds;
    }
    public void Sweep(Vector3 origin,Vector3 forward,float from,float to,float radius,float width,float height)
    {
        Vector3 right=Vector3.Cross(Vector3.up,forward);
        foreach(var subject in subjects)
        {
            if(subject.queued||!subject.root||!subject.root.gameObject.activeInHierarchy)continue;
            Bounds bounds=BoundsOf(subject);
            if(bounds.min.y>origin.y+height||bounds.max.y<origin.y-.5f)continue;
            Vector3 delta=Vector3.ProjectOnPlane(bounds.center-origin,Vector3.up);
            float extent=new Vector2(bounds.extents.x,bounds.extents.z).magnitude;
            float distance=delta.magnitude;
            if(distance-extent>radius)continue;
            float yaw=Mathf.Atan2(Vector3.Dot(delta,right),Vector3.Dot(delta,forward))*Mathf.Rad2Deg;
            float tolerance=Mathf.Asin(Mathf.Clamp01((extent+width)/Mathf.Max(.01f,distance)))*Mathf.Rad2Deg;
            if(distance>extent+width&&(yaw<Mathf.Min(from,to)-tolerance||yaw>Mathf.Max(from,to)+tolerance))continue;
            subject.queued=true;subject.fade=0;fading.Add(subject);bursts.Enqueue(subject);DissolvedObjects++;
            foreach(var echo in subject.root.GetComponentsInChildren<InkboundEcho>())echo.enabled=false;
        }
    }
    CalligraphyInkField Field()
    {
        var settings=GetComponent<InkboundTownInkSettings>();
        if(!settings||!settings.passThroughInkMaterial)return null;
        if(fields.Count<4)
        {
            var go=new GameObject("Permanent sweep black ink");go.transform.SetParent(transform,false);go.SetActive(false);
            var field=go.AddComponent<CalligraphyInkField>();field.capacity=160;field.whiteRatio=0;field.ribbonMaterial=settings.passThroughInkMaterial;
            go.SetActive(true);fields.Add(field);
        }
        return fields[(fieldIndex++)%fields.Count];
    }
    void LateUpdate()
    {
        if(Time.deltaTime<=0)return;
        // A fixed shared pool limits overdraw even when a broad brush reaches many houses.
        for(int i=0;i<6&&bursts.Count>0;i++)
        {
            var subject=bursts.Dequeue();if(!subject.root)continue;
            var field=Field();if(!field)continue;
            Bounds b=BoundsOf(subject);
            float scale=Mathf.Clamp(b.size.magnitude*.15f,.7f,2.5f);
            field.Burst(b.center,scale,12);
            field.Burst(b.center+Vector3.up*b.extents.y*.6f,scale*.8f,8);
            if(b.size.x>b.size.z)
            {field.Burst(b.center+Vector3.right*b.extents.x*.65f,scale*.8f,8);field.Burst(b.center-Vector3.right*b.extents.x*.65f,scale*.8f,8);}
            else
            {field.Burst(b.center+Vector3.forward*b.extents.z*.65f,scale*.8f,8);field.Burst(b.center-Vector3.forward*b.extents.z*.65f,scale*.8f,8);}
        }
        for(int i=fading.Count-1;i>=0;i--)
        {
            var subject=fading[i];if(!subject.root){fading.RemoveAt(i);continue;}
            subject.fade=Mathf.Min(1,subject.fade+Time.deltaTime/.95f);
            Bounds bounds=BoundsOf(subject);
            foreach(var r in subject.surfaces)if(r)
            {
                r.GetPropertyBlock(block);block.SetFloat("_InkRebuild",1-subject.fade);
                block.SetVector("_InkRebuildMin",bounds.min);block.SetVector("_InkRebuildSize",bounds.size);r.SetPropertyBlock(block);
            }
            if(subject.fade>=1)
            {
                // Deactivation also removes walls and resident contact, and cannot be undone by
                // pass-through reassembly, LOD switching, player respawn or ultimate disposal.
                subject.root.gameObject.SetActive(false);fading.RemoveAt(i);
            }
        }
    }
}
