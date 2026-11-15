using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Short, collision-checked air corridors. Water is passable; walls, trunks and terrain are not.</summary>
public sealed class DojoInkFlightPath
{
    public const float Clearance=.3f;
    readonly Transform actor;
    readonly List<Bounds> trunks=new();
    readonly Collider[] overlap=new Collider[64];
    public DojoInkFlightPath(Transform actor)
    {
        this.actor=actor;
        foreach(var t in UnityEngine.Object.FindObjectsByType<Transform>())
        {
            string name=t.name.ToLowerInvariant();
            if(!(name.Contains("tree")||name.Contains("pine")||name.Contains("maple")||name.Contains("cherry")))continue;
            if(t.GetComponentInParent<PlayerHealth>()||t.GetComponentInParent<DojoMasterBoss>())continue;
            var renderers=t.GetComponentsInChildren<Renderer>();if(renderers.Length==0||renderers.Length>20)continue;
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            if(bounds.size.y<2 || bounds.size.x>18 || bounds.size.z>18)continue;
            // Only a compact trunk column at chest height; overhead canopy does not block low flight.
            float width=Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.z)*.25f,.3f,.9f);
            trunks.Add(new Bounds(new Vector3(bounds.center.x,bounds.min.y+1.6f,bounds.center.z),new Vector3(width*2,3.2f,width*2)));
        }
    }
    bool Ignore(Collider c)=>c.isTrigger || !c.enabled || (actor&&c.transform.IsChildOf(actor)) || c.GetComponentInParent<PlayerHealth>() || c.GetComponentInParent<DojoMasterBoss>() || c.GetComponentInParent<CalligraphyClone>() || c.GetComponentInParent<DojoRiverWater>();
    public bool Free(Vector3 at)
    {
        int count=Physics.OverlapSphereNonAlloc(at,Clearance,overlap,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)if(!Ignore(overlap[i]))return false;
        foreach(var b in trunks)if((b.ClosestPoint(at)-at).sqrMagnitude<Clearance*Clearance)return false;
        return true;
    }
    public bool Clear(Vector3 from,Vector3 to)
    {
        var delta=to-from;float length=delta.magnitude;
        if(length<.001f)return Free(to);
        foreach(var hit in Physics.SphereCastAll(from,Clearance,delta/length,length,~0,QueryTriggerInteraction.Ignore))if(!Ignore(hit.collider))return false;
        int samples=Mathf.CeilToInt(length/.35f);
        for(int i=0;i<=samples;i++)if(!Free(Vector3.Lerp(from,to,i/(float)Mathf.Max(1,samples))))return false;
        return true;
    }
    public List<Vector3> Find(Vector3 from,Vector3 to)
    {
        if(Clear(from,to))return new List<Vector3>{to};
        const float cell=.6f;
        float x0=Mathf.Min(from.x,to.x)-7,z0=Mathf.Min(from.z,to.z)-7;
        int width=Mathf.CeilToInt((Mathf.Abs(from.x-to.x)+14)/cell)+1,height=Mathf.CeilToInt((Mathf.Abs(from.z-to.z)+14)/cell)+1;
        int count=width*height;
        Vector3 Position(int index)
        {
            Vector3 at=new Vector3(x0+(index%width)*cell,0,z0+(index/width)*cell);
            Vector3 span=Vector3.ProjectOnPlane(to-from,Vector3.up);
            float t=span.sqrMagnitude>.001f?Mathf.Clamp01(Vector3.Dot(at-from,span)/span.sqrMagnitude):0;
            at.y=Mathf.Lerp(from.y,to.y,t);return at;
        }
        int Index(Vector3 p)=>Mathf.Clamp(Mathf.RoundToInt((p.z-z0)/cell),0,height-1)*width+Mathf.Clamp(Mathf.RoundToInt((p.x-x0)/cell),0,width-1);
        var free=new Dictionary<int,bool>();bool Open(int i){if(!free.TryGetValue(i,out var value)){value=Free(Position(i));free.Add(i,value);}return value;}
        int Nearest(Vector3 p)
        {
            int origin=Index(p);if(Open(origin)&&Clear(p,Position(origin)))return origin;
            int best=-1;float distance=float.MaxValue;
            for(int dz=-4;dz<=4;dz++)for(int dx=-4;dx<=4;dx++)
            {
                int x=origin%width+dx,z=origin/width+dz;if(x<0||x>=width||z<0||z>=height)continue;
                int n=z*width+x;float dd=(Position(n)-p).sqrMagnitude;
                if(dd<distance&&Open(n)&&Clear(p,Position(n))){best=n;distance=dd;}
            }
            return best;
        }
        int start=Nearest(from),goal=Nearest(to);
        if(start<0||goal<0)throw new InvalidOperationException("Ink flight waypoint has no clearance: "+(start<0?from:to));
        var costs=new float[count];var parent=new int[count];var closed=new bool[count];
        for(int i=0;i<count;i++){costs[i]=float.PositiveInfinity;parent[i]=-1;}
        var frontier=new List<int>{start};costs[start]=0;bool found=false;
        while(frontier.Count>0)
        {
            int selected=0;float best=float.MaxValue;
            for(int i=0;i<frontier.Count;i++){int n=frontier[i];float f=costs[n]+Vector3.Distance(Position(n),Position(goal));if(f<best){best=f;selected=i;}}
            int current=frontier[selected];frontier.RemoveAt(selected);if(closed[current])continue;closed[current]=true;
            if(current==goal){found=true;break;}
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
            {
                if(dx==0&&dz==0)continue;int x=current%width+dx,z=current/width+dz;if(x<0||x>=width||z<0||z>=height)continue;
                int next=z*width+x;if(closed[next]||!Open(next))continue;
                float cost=costs[current]+Mathf.Sqrt(dx*dx+dz*dz)*cell;
                if(cost>=costs[next]||!Clear(Position(current),Position(next)))continue;
                costs[next]=cost;parent[next]=current;frontier.Add(next);
            }
        }
        if(!found)throw new InvalidOperationException("No unobstructed ink flight corridor from "+from+" to "+to);
        var raw=new List<Vector3>{to};for(int n=goal;n!=start;n=parent[n])raw.Add(Position(n));raw.Add(Position(start));raw.Reverse();
        var result=new List<Vector3>();var anchor=from;
        for(int i=0;i<raw.Count;)
        {
            int next=i;for(int j=i;j<raw.Count;j++)if(Clear(anchor,raw[j]))next=j;else break;
            result.Add(raw[next]);anchor=raw[next];i=next+1;
        }
        return result;
    }
    public Vector3 Drift(Vector3 center,Vector3 desired,Vector3 previous)
    {
        for(int i=0;i<9;i++)
        {var candidate=Vector3.Lerp(center,desired,1-i/8f);if(Clear(center,candidate)&&Clear(previous,candidate))return candidate;}
        return Clear(previous,center)?center:previous;
    }
}
