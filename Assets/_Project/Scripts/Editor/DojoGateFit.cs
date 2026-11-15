using UnityEngine;
using UnityEditor;

/// <summary>Fits the two leaves to the posts and lintel while retaining their hinge origins and swing.</summary>
public static class DojoGateFit
{
    public static void Fit(DojoCourtyardGate gate)
    {
        if(!gate)return;
        const float leafWidth=5.75f,leafHeight=6.12f;
        foreach(var hinge in new[]{gate.leftHinge,gate.rightHinge})
        {
            if(!hinge)continue;float side=hinge==gate.leftHinge?1:-1;
            var leaf=hinge.Find("HeavyTimberGate");if(!leaf)continue;
            leaf.localPosition=new Vector3(side*(leafWidth*.5f-.15f),leafHeight*.5f+.015f,0);
            leaf.localScale=new Vector3(leafWidth,leafHeight,.3f);
            foreach(Transform part in hinge)
            {
                if(part.name=="IronGateStrap")
                {var p=part.localPosition;p.x=side*(leafWidth*.5f-.15f);part.localPosition=p;var s=part.localScale;s.x=leafWidth-.2f;part.localScale=s;}
                EditorUtility.SetDirty(part);
            }
        }
        gate.doorHeight=leafHeight;EditorUtility.SetDirty(gate);
    }
}
