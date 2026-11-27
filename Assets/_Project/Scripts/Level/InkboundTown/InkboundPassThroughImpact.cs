using System.Collections.Generic;
using UnityEngine;

/// <summary>Visual-only reuse of CalligraphyInkProjectile's actual black ink.Burst feedback.</summary>
[DisallowMultipleComponent,DefaultExecutionOrder(12500)]
public sealed class InkboundPassThroughImpact : MonoBehaviour
{
    sealed class Subject
    {
        public Transform owner;
        public Renderer[] surfaces;
        public bool[] forcedOff;
        public GameObject effectRoot;
        public CalligraphyInkField ink;
        public float elapsed;
        public readonly HashSet<InkboundEcho> contacts=new();
        public InkboundEcho exiting;
        public Vector3 exitDirection;
        public float rebuild=-1,speed=1;
    }
    readonly Dictionary<Transform,Subject> subjects=new();
    readonly List<Subject> playing=new();
    readonly HashSet<Subject> pending=new();
    MaterialPropertyBlock block;
    static readonly Vector4[] EmptyRegions=new Vector4[4];
    InkboundTownInkSettings settings;
    public int ActiveCount=>playing.Count;
    public int TriggerCount {get;private set;}
    public bool IsPlaying(Transform owner)=>owner&&subjects.TryGetValue(owner,out var s)&&playing.Contains(s);
    void Awake(){settings=GetComponent<InkboundTownInkSettings>();block=new MaterialPropertyBlock();}

    public static Transform Owner(InkboundEcho echo)
    {
        if(echo.GetComponentInChildren<SkinnedMeshRenderer>())return echo.transform;
        var group=echo.GetComponentInParent<InkboundPassableGroup>();
        return group&&!group.containerOnly?group.transform:echo.transform;
    }
    public static bool Eligible(Renderer surface,Transform owner)
    {
        if(!surface||surface is ParticleSystemRenderer||surface.name=="Returning ink filaments")return false;
        if(surface.GetComponentInParent<InkboundEcho>() is InkboundEcho echo&&Owner(echo)!=owner)return false;
        // A solid trunk must remain visible; only its passable foliage dissolves.
        foreach(var c in owner.GetComponentsInChildren<Collider>())
            if(c.enabled&&!c.isTrigger)
            {
                foreach(var m in surface.sharedMaterials)
                    if(!m||!m.HasProperty("_Pattern")||Mathf.Abs(m.GetFloat("_Pattern")-6)>.1f)return false;
                break;
            }
        return true;
    }
    Subject GetSubject(Transform owner)
    {
        if(subjects.TryGetValue(owner,out var result))return result;
        var surfaces=new List<Renderer>();
        foreach(var r in owner.GetComponentsInChildren<Renderer>(true))
            if(Eligible(r,owner))surfaces.Add(r);
        result=new Subject{owner=owner,surfaces=surfaces.ToArray(),forcedOff=new bool[surfaces.Count]};
        subjects.Add(owner,result);return result;
    }
    static Bounds BoundsOf(Subject s)
    {
        var bounds=new Bounds(s.owner.position+Vector3.up*.8f,Vector3.one);bool found=false;
        foreach(var r in s.surfaces)if(r&&r.gameObject.activeInHierarchy)
        {if(found)bounds.Encapsulate(r.bounds);else{bounds=r.bounds;found=true;}}
        return bounds;
    }
    public void Contact(InkboundEcho echo,bool inside,bool requestEffect,Vector3 direction)
    {
        var s=GetSubject(Owner(echo));
        if(inside)s.contacts.Add(echo);else s.contacts.Remove(echo);
        if(requestEffect){s.exiting=echo;s.exitDirection=direction;pending.Add(s);}
    }
    static Vector3 BonePosition(Animator animator,HumanBodyBones bone,Vector3 fallback)
    {
        if(!animator||!animator.isHuman||!animator.avatar||!animator.avatar.isValid)return fallback;
        var joint=animator.GetBoneTransform(bone);return joint?joint.position:fallback;
    }
    static Vector3 InBounds(Bounds b,float x,float y,float z=0)
        =>b.center+Vector3.Scale(b.size,new Vector3(x,y-.5f,z));
    List<Vector3> BurstPositions(Subject s,Bounds bounds)
    {
        if(s.owner.GetComponentInChildren<SkinnedMeshRenderer>())
        {
            var animator=s.owner.GetComponentInChildren<Animator>();
            var chest=BonePosition(animator,HumanBodyBones.Chest,InBounds(bounds,0,.68f));
            var hips=BonePosition(animator,HumanBodyBones.Hips,InBounds(bounds,0,.43f));
            return new List<Vector3>{
                BonePosition(animator,HumanBodyBones.Head,InBounds(bounds,0,.88f)),chest,Vector3.Lerp(hips,chest,.35f),
                BonePosition(animator,HumanBodyBones.LeftHand,InBounds(bounds,-.3f,.52f)),
                BonePosition(animator,HumanBodyBones.RightHand,InBounds(bounds,.3f,.52f)),
                BonePosition(animator,HumanBodyBones.LeftFoot,InBounds(bounds,-.16f,.06f)),
                BonePosition(animator,HumanBodyBones.RightFoot,InBounds(bounds,.16f,.06f))};
        }
        float spacing=Mathf.Max(.3f,settings.passThroughPointSpacing);
        if(Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z))<spacing)
            return new List<Vector3>{bounds.center};
        int count=Mathf.Clamp(Mathf.CeilToInt((bounds.size.x+bounds.size.y+bounds.size.z)/spacing),2,12);
        var candidates=new List<Vector3>();
        // Sample the actual shape: a table releases ink from its top and legs, not an empty
        // uniform box around it. Bounded sampling runs only once when the object is crossed.
        foreach(var r in s.surfaces)
        {
            var filter=r?r.GetComponent<MeshFilter>():null;
            if(!filter||!filter.sharedMesh||!filter.sharedMesh.isReadable)continue;
            var vertices=filter.sharedMesh.vertices;int step=Mathf.Max(1,vertices.Length/192);
            for(int i=0;i<vertices.Length;i+=step)candidates.Add(filter.transform.TransformPoint(vertices[i]));
        }
        if(candidates.Count==0)
        {
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                candidates.Add(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))*.85f);
        }
        var points=new List<Vector3>();
        Vector3 first=candidates[0];float nearest=float.PositiveInfinity;
        foreach(var p in candidates)if((p-bounds.center).sqrMagnitude<nearest){first=p;nearest=(p-bounds.center).sqrMagnitude;}
        points.Add(first);
        while(points.Count<count)
        {
            Vector3 selected=first;float farthest=0;
            foreach(var p in candidates)
            {
                float distance=float.PositiveInfinity;foreach(var chosen in points)distance=Mathf.Min(distance,(p-chosen).sqrMagnitude);
                if(distance>farthest){farthest=distance;selected=p;}
            }
            if(farthest<.01f)break;points.Add(selected);
        }
        return points;
    }
    public bool Trigger(InkboundEcho echo,Vector3 direction)
    {
        if(!settings||!settings.useProjectileHitEffect)return false;
        if(!settings.passThroughInkMaterial)return false;
        var owner=Owner(echo);var s=GetSubject(owner);
        if(s.surfaces.Length==0||playing.Contains(s)||playing.Count>=settings.maximumPassThroughEffects)return false;
        var bounds=BoundsOf(s);
        s.speed=Mathf.Clamp(settings.inkPlaybackSpeed*echo.playbackSpeed,.0025f,4);
        s.rebuild=-1;s.elapsed=0;
        s.effectRoot=new GameObject("Pass-through elder hit ink");
        s.effectRoot.transform.SetParent(transform,false);s.effectRoot.transform.position=bounds.center;
        var points=BurstPositions(s,bounds);
        int perPoint=Mathf.Min(Mathf.Clamp(settings.passThroughInkPerPoint,6,20),160/points.Count);
        float scale=Mathf.Clamp(bounds.size.magnitude/2.1f,.65f,2.2f)*settings.passThroughEffectScale;
        s.effectRoot.SetActive(false);s.ink=s.effectRoot.AddComponent<CalligraphyInkField>();
        s.ink.capacity=Mathf.Clamp(points.Count*perPoint,32,160);s.ink.ribbonMaterial=settings.passThroughInkMaterial;
        // Multiplying the original white accent strands would turn the whole silhouette pale.
        s.ink.whiteRatio=0;
        s.effectRoot.SetActive(true);s.ink.SetVisualPlayback(s.speed,true);
        // Same black burst as projectile contact, rather than the accompanying white flashes.
        // Multiple smaller plumes retain distinct head/body/feet and furniture silhouettes.
        float pointScale=points.Count>1?.7f:1;
        foreach(var point in points)s.ink.Burst(point,1.1f*scale*pointScale,perPoint);
        for(int i=0;i<s.surfaces.Length;i++)if(s.surfaces[i])
        {s.forcedOff[i]=s.surfaces[i].forceRenderingOff;s.surfaces[i].forceRenderingOff=true;}
        SetPigment(s,0,bounds);playing.Add(s);TriggerCount++;return true;
    }
    void SetPigment(Subject s,float value,Bounds bounds)
    {
        foreach(var r in s.surfaces)if(r)
        {
            r.GetPropertyBlock(block);block.SetFloat("_InkRebuild",value);
            block.SetVector("_InkRebuildMin",bounds.min);block.SetVector("_InkRebuildSize",bounds.size);
            block.SetVectorArray("_InkFrom",EmptyRegions);block.SetVectorArray("_InkTo",EmptyRegions);block.SetFloat("_EchoStrength",0);
            r.SetPropertyBlock(block);
        }
    }
    void LateUpdate()
    {
        // Multi-material objects finish crossing only after every touched part is clear.
        foreach(var s in pending)if(s.contacts.Count==0&&s.exiting)Trigger(s.exiting,s.exitDirection);
        pending.Clear();
        for(int k=playing.Count-1;k>=0;k--)
        {
            var s=playing[k];
            if(!s.owner||!s.owner.gameObject.activeInHierarchy||!settings.useProjectileHitEffect){Restore(s);playing.RemoveAt(k);continue;}
            var bounds=BoundsOf(s);s.effectRoot.transform.position=bounds.center;
            s.elapsed+=Time.deltaTime;
            if(s.rebuild<0)
            {
                if(s.elapsed<.1f||s.ink.ActiveCount>0)continue;
                s.rebuild=0;
                // Restore renderer participation so LODGroup remains the only LOD owner.
                for(int i=0;i<s.surfaces.Length;i++)if(s.surfaces[i])
                {
                    bool painted=true;foreach(var m in s.surfaces[i].sharedMaterials)if(!m||!m.HasProperty("_InkRebuild"))painted=false;
                    if(painted)s.surfaces[i].forceRenderingOff=s.forcedOff[i];
                }
            }
            s.rebuild=Mathf.MoveTowards(s.rebuild,1,Time.deltaTime*s.speed/Mathf.Max(.1f,settings.reassembleSeconds));
            SetPigment(s,s.rebuild,bounds);
            if(s.rebuild>=1){Restore(s);playing.RemoveAt(k);}
        }
    }
    void Restore(Subject s)
    {
        if(s.owner)SetPigment(s,1,BoundsOf(s));
        for(int i=0;i<s.surfaces.Length;i++)if(s.surfaces[i])s.surfaces[i].forceRenderingOff=s.forcedOff[i];
        if(s.effectRoot)Destroy(s.effectRoot);s.ink=null;s.effectRoot=null;s.rebuild=-1;
    }
    void OnDisable(){foreach(var s in playing)Restore(s);playing.Clear();pending.Clear();foreach(var s in subjects.Values)s.contacts.Clear();}
}
