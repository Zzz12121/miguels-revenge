using UnityEngine;

public sealed partial class CalligraphyBossCombat
{
    [Header("近身震波（獨立於技能）")]
    public bool proximityWaveEnabled=true;
    [Min(.1f)] public float proximityWaveRadius=3.5f;
    [Range(0,1)] public float proximityWaveChance=.35f;
    [Min(1)] public float proximityWaveCooldown=6;
    public int ProximityWaves {get;private set;}
    float nextProximityWave,proximityWaveClock=-1;
    bool proximityWaveHit;
    Vector3 proximityWaveOrigin;
    LineRenderer proximityWaveRing;

    bool UpdateProximityWave()
    {
        if(proximityWaveClock<0)
        {
            if(!proximityWaveEnabled||!externallySequenced||casting||EncounterClonesRemaining>0||Time.time<nextProximityWave)return false;
            nextProximityWave=Time.time+2;
            if(!EncounterMeleeInRange||Random.value>proximityWaveChance)return false;
            nextProximityWave=Time.time+Mathf.Max(1,proximityWaveCooldown);
            proximityWaveClock=0;proximityWaveHit=false;proximityWaveOrigin=transform.position;
            proximityWaveOrigin.y=CalligraphyGeometry.Ground(proximityWaveOrigin,transform).y;
            SetWalking(false);ProximityWaves++;
            var library=CalligraphyVfxLibrary.Current;
            if(library)CalligraphyVfxLibrary.Play(library.novaHeavy,proximityWaveOrigin+Vector3.up*.35f,Quaternion.identity,.8f,1.4f,0,effectsRoot)?.StopAfter(.4f,.5f);
            if(!proximityWaveRing)
            {
                var go=new GameObject("Elder proximity ink shockwave");go.transform.SetParent(effectsRoot,false);
                proximityWaveRing=go.AddComponent<LineRenderer>();proximityWaveRing.loop=true;
                proximityWaveRing.useWorldSpace=true;proximityWaveRing.positionCount=64;
                proximityWaveRing.sharedMaterial=ink.ribbonMaterial;
                proximityWaveRing.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            proximityWaveRing.enabled=true;
        }
        proximityWaveClock+=Time.deltaTime;
        float progress=Mathf.Clamp01((proximityWaveClock-.18f)/.38f);
        float radius=Mathf.Lerp(.35f,proximityWaveRadius,progress);
        proximityWaveRing.widthMultiplier=Mathf.Lerp(.16f,.03f,progress);
        proximityWaveRing.startColor=proximityWaveRing.endColor=new Color(.015f,.02f,.025f,1-progress);
        var block=new MaterialPropertyBlock();block.SetFloat("_Fade",progress);block.SetFloat("_Opacity",1);proximityWaveRing.SetPropertyBlock(block);
        for(int i=0;i<64;i++)
        {
            float angle=i*Mathf.PI/32;
            proximityWaveRing.SetPosition(i,proximityWaveOrigin+new Vector3(Mathf.Cos(angle)*radius,.14f,Mathf.Sin(angle)*radius));
        }
        Vector3 delta=Vector3.ProjectOnPlane(player.transform.position-proximityWaveOrigin,Vector3.up);
        if(!proximityWaveHit&&proximityWaveClock>=.18f&&delta.magnitude<=radius+.35f&&Mathf.Abs(player.FeetY-proximityWaveOrigin.y)<1.5f)
        {
            proximityWaveHit=true;
            Vector3 direction=delta.sqrMagnitude>.001f?delta.normalized:transform.forward;
            // Respect rolling, blocking and damage immunity; never turn this into a large skill hit.
            bool hit=CalligraphyPlayerHitGate.Hit(player,5,direction*6,"被老者近身震波推開",source:transform,allowOrdinaryStun:false);
            var motor=player.GetComponent<ThirdPersonController>();
            if(hit&&motor&&!motor.ActionsLocked&&!motor.IsControlImmune)motor.Launch(direction*6+Vector3.up*2);
        }
        if(proximityWaveClock>=.56f)CancelProximityWave();
        return true;
    }
    void CancelProximityWave()
    {
        proximityWaveClock=-1;
        if(proximityWaveRing)proximityWaveRing.enabled=false;
    }
}
