using UnityEngine;

/// <summary>Samples the real blade after the weapon presenter; never changes combo or damage timing.</summary>
[DefaultExecutionOrder(31500)]
public sealed class CalligraphyPlayerBladeTrail : MonoBehaviour
{
    PlayerWeaponPresenter weapon;PlayerCombat combat;TrailRenderer trail;
    Vector3 previous;bool wasDrawing;
    void Awake(){weapon=GetComponent<PlayerWeaponPresenter>();combat=GetComponent<PlayerCombat>();}
    void LateUpdate()
    {
        var lib=CalligraphyVfxLibrary.Current;
        if(!weapon || !combat || !weapon.BladeTip || !lib || !lib.bladeTrail)return;
        if(!trail)
        {
            var go=new GameObject("Blade arc - ARPG Lunge material");go.transform.SetParent(transform,false);
            trail=go.AddComponent<TrailRenderer>();trail.sharedMaterial=lib.bladeTrail;trail.time=.13f;trail.minVertexDistance=.025f;
            trail.widthCurve=AnimationCurve.EaseInOut(0,.32f,1,0);trail.numCornerVertices=2;trail.numCapVertices=2;
            trail.startColor=new Color(1,1,1,.8f);trail.endColor=new Color(1,1,1,0);trail.emitting=false;
        }
        Vector3 at=weapon.BladeTip.position;
        bool drawing=combat.IsAttacking && weapon.IsDrawn && (at-previous).sqrMagnitude>.0002f;
        if(drawing && !wasDrawing)trail.Clear();
        trail.transform.position=at;trail.emitting=drawing;wasDrawing=drawing;previous=at;
    }
    void OnDisable(){if(trail)trail.Clear();}
}
