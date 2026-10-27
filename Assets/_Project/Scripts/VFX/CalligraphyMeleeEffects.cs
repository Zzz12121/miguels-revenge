using UnityEngine;

/// <summary>Shared visual vocabulary; pose IDs 4..8 are thrust, two upcuts, two complete circles.</summary>
public static class CalligraphyMeleeEffects
{
    public static void Play(CalligraphyBrushPresentation brush,int style,float scale=1)
    {
        var library=CalligraphyVfxLibrary.Current;if(!library || !brush || !brush.brushTip)return;
        Quaternion rotation=brush.transform.rotation;
        // Vendor Stab travels along local +Y (25..42 units/s), not +Z.
        if(style==0)rotation*=Quaternion.FromToRotation(Vector3.up,Vector3.forward);
        if(style==1 || style==2)rotation*=Quaternion.Euler(0,0,90);
        CalligraphyVfxLibrary.Play(library.Slash(style),brush.brushTip.position,rotation,(style==2 || style==4?1:.75f)*scale,1.5f);
    }
}
