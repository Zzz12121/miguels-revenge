using UnityEngine;

public sealed partial class CalligraphyBossCombat
{
    [Header("Additional calligraphy attacks")]
    public float slamStunSeconds=1.6f,slamDamage=24,beamDamage=10,runeDamage=18;
    public bool Vanished {get;private set;}
    public float VanishedAt {get;private set;}
    bool dying,leaping;
    float deathClock,deathDuration=7,beamHitAt;
    int slashVfx=-1;
    Vector3 leapStart,leapGround,leapForward,beamDirection;
    float beamRangeRatio=1,beamAuthoredLength,beamBaseScaleZ;
    bool beamFired;
    Vector3 BeamOrigin()=>transform.position+Vector3.up*inkBeam.emitterHeight+inkBeam.emitterOffset;
    Vector3 BeamAim(Vector3 origin)
    {
        Vector3 point=player.transform.position;
        float emitterY=origin.y;
        float groundDelta=player.FeetY-CalligraphyGeometry.Ground(transform.position,transform).y;
        point.y=inkBeam.horizontalOnLevelGround && Mathf.Abs(groundDelta)<=inkBeam.heightTolerance?emitterY:player.GetComponent<CharacterController>().bounds.max.y;
        return point;
    }
    CalligraphyPackageEffect skillEffect;
    Renderer[] deathRenderers;
    Material[][] deathMaterials;
    Mesh beamMesh;
    readonly System.Collections.Generic.List<Vector3> beamVertices=new();
    readonly System.Collections.Generic.List<Vector3> runePoints=new();
    void Face(Vector3 point)
    {
        Vector3 d=point-transform.position;d.y=0;
        if(d.sqrMagnitude>.001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(d),Time.deltaTime*240);
        brush.SetAimPoint(point);
    }
    void Beam()
    {
        float stroke=Mathf.Max(.05f,inkBeam.windupSeconds)*.5f;
        bool winding=elapsed<stroke*2;
        if(winding){if(elapsed<.4f)Face(Abdomen(player));brush.SetCombatPose(elapsed<stroke?13:14,(elapsed%stroke)/stroke);}
        // Start the package's charge at the midpoint, while the second X stroke continues.
        if(elapsed<stroke)return;
        if(!skillEffect && released==0)
        {
            Vector3 origin=BeamOrigin();aim=BeamAim(origin);
            beamDirection=(aim-origin).normalized;beamRangeRatio=1;beamAuthoredLength=0;beamFired=false;
            var lib=CalligraphyVfxLibrary.Current;
            if(lib)skillEffect=CalligraphyVfxLibrary.Play(lib.beam,origin,Quaternion.LookRotation(-beamDirection)*Quaternion.Euler(inkBeam.vfxEuler),inkBeam.vfxScale,1,0,effectsRoot);
            if(skillEffect)beamBaseScaleZ=skillEffect.transform.localScale.z;
            released=1;beamHitAt=0;
        }
        if(!winding){Face(aim);brush.SetCombatPose(9,1);}
        if(skillEffect && !beamFired)
        {
            Vector3 origin=BeamOrigin();aim=BeamAim(origin);
            beamDirection=(aim-origin).normalized;
            skillEffect.transform.SetPositionAndRotation(origin,Quaternion.LookRotation(-beamDirection)*Quaternion.Euler(inkBeam.vfxEuler));
            if(skillEffect.Age>=3)beamFired=true;
        }
        // Only the live beam meshes can deal damage. Charge particles cannot extend the hit volume.
        if(skillEffect && beamFired)
        {
            Vector3 from=skillEffect.transform.position;
            Vector3 actualDirection=skillEffect.transform.TransformDirection(Vector3.back).normalized;
            float length=0;
            foreach(var ps in skillEffect.Systems)
            {
                if(!ps.name.StartsWith("Ice_beam_center") || ps.particleCount==0)continue;
                var renderer=ps.GetComponent<ParticleSystemRenderer>();if(!renderer)continue;
                if(!beamMesh)beamMesh=new Mesh();
                renderer.BakeMesh(beamMesh,Camera.main,ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                beamMesh.GetVertices(beamVertices);
                // BakeRotationAndScale has already applied the renderer's world rotation/scale.
                foreach(var v in beamVertices)length=Mathf.Max(length,Vector3.Dot(renderer.transform.position+v-from,actualDirection));
            }
            // Compare against unscaled authored length: never multiply the last frame's
            // shortened scale again. Range limiting is independent of player/hit timing.
            beamAuthoredLength=Mathf.Max(beamAuthoredLength,length/Mathf.Max(.0001f,beamRangeRatio));
            float ratio=beamAuthoredLength>0?Mathf.Min(1,inkBeam.maximumRange/beamAuthoredLength):1;
            length*=ratio/Mathf.Max(.0001f,beamRangeRatio);beamRangeRatio=ratio;
            var scale=skillEffect.transform.localScale;scale.z=beamBaseScaleZ*ratio;skillEffect.transform.localScale=scale;
            length=Mathf.Min(length,inkBeam.maximumRange);
            if(length>0 && Time.time>=beamHitAt && CalligraphyPlayerHitGate.Touches(player,from,from+actualDirection*length,inkBeam.hitRadius))
            {CalligraphyPlayerHitGate.Hit(player,beamDamage,actualDirection,"被墨光擊倒",Abdomen(player));beamHitAt=Time.time+inkBeam.hitInterval;}
        }
        if(released>0 && !skillEffect){if(beamMesh)Destroy(beamMesh);FinishCast();}
    }
    void LeapSlam()
    {
        float total=Mathf.Max(.1f,leapSlam.castSeconds);float phase=elapsed/total;brush.SetCombatPose(10,phase);
        if(released==0)
        {
            released=1;leapStart=transform.position;
            Vector3 destination=CalligraphyGeometry.Ground(player.transform.position,player.transform);
            Vector3 d=Vector3.ProjectOnPlane(destination-leapStart,Vector3.up);d=Vector3.ClampMagnitude(d,leapSlam.jumpDistance);
            leapForward=d.sqrMagnitude>.001f?d.normalized:transform.forward;
            transform.rotation=Quaternion.LookRotation(leapForward);brush.SetAimPoint(destination);
            leapGround=CalligraphyGeometry.Ground(leapStart+d-leapForward*1.4f,transform);leaping=true;
        }
        float t=Mathf.Clamp01((phase-.2f)/.54f);
        transform.position=Vector3.Lerp(leapStart,leapGround,Mathf.SmoothStep(0,1,t))+Vector3.up*(Mathf.Sin(t*Mathf.PI)*leapSlam.jumpHeight);
        if(phase>=1)FinishCast();
    }
    // Brush presentation (order 180) solves hands and ground contact first.
    void LateUpdate()
    {
        if(!Active || !casting || SelectedSkill!=6 || impactDone || elapsed/Mathf.Max(.1f,leapSlam.castSeconds)<.74f)return;
        {
            impactDone=true;leaping=false;transform.position=leapGround;
            Vector3 at=brush.brushTip.position;
            bool direct=CalligraphyPlayerHitGate.Touches(player,at,at+Vector3.up*.8f,leapSlam.directHitRadius);
            if(direct)CalligraphyPlayerHitGate.Hit(player,slamDamage,leapForward,"被巨筆重砸擊倒",at+Vector3.up*.8f,CalligraphyPlayerControl.Reaction.StrongLaunch,leapSlam.wave.controlSeconds,new Vector2(leapSlam.wave.knockbackSpeed,leapSlam.wave.launchSpeed));
            CalligraphySkillSequence.Wave(player,at,Quaternion.Euler(0,leapSlam.forwardYawOffset,0)*leapForward,false,direct,effectsRoot,leapSlam.wave);
        }
    }
    void FrontalPulses()
    {
        brush.SetCombatPose(2,Mathf.Clamp01(elapsed/1.4f));
        if(released==0)Face(Abdomen(player));
        if(elapsed>=frontalPulses.releaseSeconds && released==0)
        {
            Vector3 direction=HorizontalAim(transform.position);transform.rotation=Quaternion.LookRotation(direction);
            released=1;CalligraphySkillSequence.Wave(player,transform.position+direction*1.4f+Vector3.up*.05f,direction,true,false,effectsRoot,frontalPulses);
        }
        if(elapsed>frontalPulses.castSeconds)FinishCast();
    }
    void Runes()
    {
        float segment=Mathf.Max(.1f,runeAttack.strokeSeconds);
        if(released==0 && runePoints.Count==0)
        {
            Vector3 center=CalligraphyGeometry.Ground(player.transform.position,player.transform);
            runePoints.Add(center);
            // Snapshot the attack center once. Golden-angle sampling leaves an open escape sector.
            for(int attempt=0;attempt<Mathf.Max(1,runeAttack.count)*40 && runePoints.Count<runeAttack.count;attempt++)
            {
                float angle=attempt*137.50776f%300+30;
                float radius=runeAttack.spawnRadius*Mathf.Sqrt((attempt*.618034f)%1);
                Vector3 p=center+Quaternion.Euler(0,angle,0)*Vector3.forward*radius;
                p=CalligraphyGeometry.Ground(p,player.transform);
                if(runePoints.Exists(q=>Vector3.ProjectOnPlane(q-p,Vector3.up).magnitude<runeAttack.minimumSpacing))continue;
                runePoints.Add(p);
            }
        }
        int index=Mathf.Min(runePoints.Count-1,(int)(elapsed/segment));float phase=(elapsed-index*segment)/segment;
        if(index<0)return;aim=runePoints[index];
        Face(aim);brush.SetCombatPose(12,phase);
        if(phase>=.55f && released<=index)
        {released=index+1;CalligraphySkillSequence.Rune(player,aim,runeDamage,effectsRoot,runeAttack);}
        if(elapsed>runePoints.Count*segment+.4f){runePoints.Clear();FinishCast();}
    }
    void BeginDeath()
    {
        Active=false;CancelCast();dying=true;deathClock=0;SetWalking(false);
        GetComponent<TetherThrownMotion>()?.Interrupt();
        foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
        var library=CalligraphyVfxLibrary.Current;
        if(library)
        {
            skillEffect=CalligraphyVfxLibrary.Play(library.verticalExplosion,transform.position,Quaternion.identity,deathPresentation.vfxScale,deathPresentation.playbackSpeed,0,effectsRoot);
            if(skillEffect)deathDuration=Mathf.Max(3,skillEffect.Duration*.8f);
        }
        deathRenderers=GetComponentsInChildren<Renderer>();deathMaterials=new Material[deathRenderers.Length][];
        for(int i=0;i<deathRenderers.Length;i++)deathMaterials[i]=deathRenderers[i].sharedMaterials;
    }
    public void ResetPracticeActor()
    {
        dying=false;Vanished=false;Health=maxHealth;deathClock=0;
        if(deathRenderers!=null)for(int i=0;i<deathRenderers.Length;i++)if(deathRenderers[i])
        {deathRenderers[i].enabled=true;deathRenderers[i].sharedMaterials=deathMaterials[i];CalligraphyGeometry.Tint(deathRenderers[i],Color.white);}
        deathRenderers=null;deathMaterials=null;
        foreach(var c in GetComponentsInChildren<Collider>())c.enabled=true;
        if(brush){brush.enabled=true;brush.ClearCombatPose();}
    }
    void UpdateDeath()
    {
        if(Vanished)return;
        deathClock+=Time.deltaTime;brush.SetCombatPose(11,deathClock/deathDuration);
        float fade=Mathf.Clamp01((deathClock-deathDuration+deathPresentation.fadeSeconds)/Mathf.Max(.05f,deathPresentation.fadeSeconds));
        if(fade>0)for(int i=0;i<deathRenderers.Length;i++)
        {
            var r=deathRenderers[i];if(!r)continue;
            var materials=new Material[deathMaterials[i].Length];for(int k=0;k<materials.Length;k++)materials[k]=ink.originalEchoMaterial;
            if(ink.originalEchoMaterial)r.sharedMaterials=materials;
            CalligraphyGeometry.Tint(r,new Color(.025f,.03f,.035f),1-fade,fade);
        }
        if(!Vanished && deathClock>=deathDuration)
        {
            Vanished=true;VanishedAt=Time.time;
            foreach(var r in deathRenderers)if(r)r.enabled=false;
            brush.enabled=false;
        }
    }
}
