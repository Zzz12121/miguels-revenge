using UnityEngine;

/// <summary>Explicit package references shared by the boss, clones and player hit feedback.</summary>
public sealed class CalligraphyVfxLibrary : ScriptableObject
{
    public GameObject pulse,beam,smoke,bigExplosion,buffUp,flashAoE,frontal,frontalPulses,rune,runeFall,verticalExplosion;
    public GameObject stab,cLight,cHeavy,circleLight,circleHeavy,lunge,projectile,novaLight,novaHeavy,impact,cone;
    public Material bladeTrail;
    static CalligraphyVfxLibrary cached;
    public static CalligraphyVfxLibrary Current=>cached?cached:(cached=Resources.Load<CalligraphyVfxLibrary>("CalligraphyVfxLibrary"));
    public GameObject Slash(int style)=>style==0?stab:style==1?cLight:style==2?cHeavy:style==3?circleLight:circleHeavy;
    public static CalligraphyPackageEffect Play(GameObject prefab,Vector3 position,Quaternion rotation,float scale=1,float speed=1,float skip=0,Transform parent=null)
    {
        if(!prefab)return null;
        var go=Instantiate(prefab,position,rotation,parent);go.name=prefab.name+" [combat]";
        go.transform.localScale=Vector3.one*scale;
        var fx=go.AddComponent<CalligraphyPackageEffect>();fx.Initialize(speed,skip);return fx;
    }
}
