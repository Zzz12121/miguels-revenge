using System.Collections.Generic;
using UnityEngine;

/// <summary>Local six-panel landscape transformation. No camera cuts, teleport or global render-setting mutation.</summary>
public sealed class CalligraphyPaintingWorld : MonoBehaviour
{
    static readonly HashSet<CalligraphyPaintingWorld> owners=new();
    public static bool BlocksTether {get{owners.RemoveWhere(o=>!o || !o.isActiveAndEnabled);return owners.Count>0;}}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatics()=>owners.Clear();
    public float entrySeconds=4,containedSeconds=20,exitSeconds=2.5f,radius=64;
    public float disturbanceRadius=5,disturbanceStrength=.8f;
    public bool Finished {get;private set;}
    CalligraphyBossCombat boss;
    PlayerHealth player;
    CalligraphyInkField ink;
    readonly List<Renderer> landscape=new();
    readonly List<Color> landscapeColors=new();
    readonly List<Transform> panels=new();
    readonly List<Vector3> panelTargets=new();
    readonly List<Quaternion> panelRotations=new();
    readonly List<CalligraphyInkDragon> dragons=new();
    readonly List<Renderer> inkPatches=new();
    Transform face;
    CalligraphyWorldIsolation isolation;
    readonly List<Renderer> faceRenderers=new();
    float age,spawnClock;
    bool sixReleased;
    sealed class HangingStroke {public LineRenderer line;public Vector3[] basis,points;public float seed;}
    readonly List<HangingStroke> curtains=new();
    float footprintClock;
    Vector3 previousPlayer;
    Quaternion previousRotation;
    public void Begin(CalligraphyBossCombat owner,PlayerHealth target,CalligraphyInkField field)
    {
        boss=owner;player=target;transform.position=CalligraphyGeometry.Ground(player.transform.position,player.transform);
        // World pigment has its own lifetime and remains visible while the outside boss is hidden.
        var pool=new GameObject("Painting pigment pool");pool.SetActive(false);pool.transform.SetParent(transform,false);
        ink=pool.AddComponent<CalligraphyInkField>();
        ink.ribbonMaterial=field.ribbonMaterial;ink.sculptureMaterial=field.sculptureMaterial;ink.paperMaterial=field.paperMaterial;
        ink.yinYangMaterial=field.yinYangMaterial;ink.landscapeMaterial=field.landscapeMaterial;
        ink.originalInkMaterial=field.originalInkMaterial;ink.originalEchoMaterial=field.originalEchoMaterial;pool.SetActive(true);
        ink.suspendedWashMaterial=field.suspendedWashMaterial;
        ink.skyVisageMaterial=field.skyVisageMaterial;
        radius=boss.paintingRadius;disturbanceRadius=boss.disturbanceRadius;disturbanceStrength=boss.disturbanceStrength;
        previousPlayer=player.transform.position;previousRotation=player.transform.rotation;
        SkillAimCancellation.Cancel();owners.Add(this);
        BuildLowerGround();BuildSheets();BuildMountains();BuildTrees();BuildRiver();BuildScenery();BuildSkyFace();BuildHangingInk();
        isolation=gameObject.AddComponent<CalligraphyWorldIsolation>();isolation.Initialize(player);
    }
    void BuildLowerGround()
    {
        // Re-render the original supporting mesh lower down; never move its collider or scene object.
        var hits=Physics.RaycastAll(player.transform.position+Vector3.up*2,Vector3.down,8,~0,QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var hit in hits)
        {
            if(hit.collider.GetComponentInParent<IDamageable>()!=null || hit.normal.y<.5f)continue;
            var mf=hit.collider.GetComponent<MeshFilter>();var source=hit.collider.GetComponent<MeshRenderer>();
            if(mf && source && mf.sharedMesh)
            {
                var go=new GameObject("Original ground visible below the transparent support");go.transform.SetParent(transform,false);
                go.transform.SetPositionAndRotation(mf.transform.position-Vector3.up*9,mf.transform.rotation);go.transform.localScale=mf.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh=mf.sharedMesh;go.AddComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;
            }
            break;
        }
    }
    void BuildHangingInk()
    {
        for(int i=0;i<20;i++)
        {
            float a=i*2.39996f,r=9+(i%5)*3.3f;
            var basis=new Vector3[48];
            for(int k=0;k<basis.Length;k++)
            {
                float u=k/(float)(basis.Length-1);
                basis[k]=new Vector3(Mathf.Cos(a)*r+Mathf.Sin(u*7+i)*2.8f,23-u*32,Mathf.Sin(a)*r+Mathf.Cos(u*9+i)*2.4f);
            }
            for(int strand=0;strand<3;strand++)
            {
                var line=CalligraphyGeometry.Line("Suspended branching ink stream",transform,ink.suspendedWashMaterial?ink.suspendedWashMaterial:ink.ribbonMaterial,new Vector3[48],strand==0?3.4f:.35f);
                line.widthCurve=new AnimationCurve(new Keyframe(0,.05f),new Keyframe(.2f,.7f),new Keyframe(.45f,.24f),new Keyframe(.7f,1),new Keyframe(1,0));
                var shifted=new Vector3[48];for(int k=0;k<48;k++)shifted[k]=basis[k]+new Vector3(Mathf.Sin(k*.16f+i+strand)*strand*.8f,0,Mathf.Cos(k*.2f+i)*strand*.5f);
                curtains.Add(new HangingStroke{line=line,basis=shifted,points=new Vector3[48],seed=i*.7f+strand});
                Register(line,new Color(.002f,.004f,.006f,strand==0?.8f:.7f));
            }
        }
        if(ink.originalInkMaterial)for(int i=0;i<24;i++)
        {
            var patch=CalligraphyGeometry.MeshObject("Reactive ink on invisible support",transform,CalligraphyGeometry.Quad(),ink.originalInkMaterial);
            float a=i*2.39996f,r=2.5f+(i%6)*2.8f;
            patch.transform.localPosition=new Vector3(Mathf.Sin(a)*r,.025f,Mathf.Cos(a)*r);patch.transform.localRotation=Quaternion.Euler(90,i*57,0);
            patch.transform.localScale=Vector3.one*2.8f;inkPatches.Add(patch);
        }
    }
    void Register(Renderer r,Color c){landscape.Add(r);landscapeColors.Add(c);CalligraphyGeometry.Tint(r,c,0);}
    void BuildSheets()
    {
        Vector3[] positions={new Vector3(0,-10,0),new Vector3(0,62,0),new Vector3(-radius,26,0),new Vector3(radius,26,0),new Vector3(0,26,-radius),new Vector3(0,26,radius)};
        Quaternion[] rotations={Quaternion.Euler(90,0,0),Quaternion.Euler(90,0,0),Quaternion.Euler(0,90,0),Quaternion.Euler(0,90,0),Quaternion.identity,Quaternion.identity};
        for(int i=0;i<6;i++)
        {
            var r=CalligraphyGeometry.MeshObject("Unfolding paper face "+i,transform,CalligraphyGeometry.Quad(),ink.paperMaterial);
            r.transform.localScale=i<2?new Vector3(radius*2,radius*2,1):new Vector3(radius*2,72,1);
            panels.Add(r.transform);panelTargets.Add(positions[i]);panelRotations.Add(rotations[i]);Register(r,new Color(.87f,.9f,.92f));
        }
    }
    void BuildMountains()
    {
        // Four rings of asymmetric brush-cut ridges, pale distance and dark foreground.
        for(int layer=0;layer<3;layer++)for(int side=0;side<4;side++)
        {
            const int n=144;var vertices=new Vector3[n*2];var uv=new Vector2[n*2];var colors=new Color[n*2];var indices=new List<int>();
            float seed=layer*7.17f+side*11.4f;
            for(int k=0;k<n;k++)
            {
                float u=k/(float)(n-1),x=(u-.5f)*radius*2;
                float peak=Mathf.Pow(Mathf.PerlinNoise(u*5+seed,seed),2)*15+Mathf.PerlinNoise(u*14+seed,3)*3;
                vertices[k*2]=new Vector3(x,0,0);vertices[k*2+1]=new Vector3(x,peak+2+layer*2,0);
                uv[k*2]=new Vector2(u,0);uv[k*2+1]=new Vector2(u,1);colors[k*2]=new Color(1,1,1,.08f);colors[k*2+1]=Color.white;
                if(k<n-1){int q=k*2;indices.AddRange(new[]{q,q+1,q+2,q+1,q+3,q+2});}
            }
            var mesh=new Mesh{name="Layered ink mountain ridge"};mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=indices.ToArray();mesh.RecalculateNormals();
            var r=CalligraphyGeometry.MeshObject("Mountain wash "+layer+" "+side,transform,mesh,ink.landscapeMaterial?ink.landscapeMaterial:ink.paperMaterial);
            float angle=side*90;r.transform.localRotation=Quaternion.Euler(0,angle,0);r.transform.localPosition=r.transform.localRotation*new Vector3(0,-9,radius-3-layer*2.5f);
            float grey=.57f-layer*.18f;Register(r,new Color(grey,grey+.01f,grey));
            // Jagged internal brush veins break up the silhouette without a photographic texture.
            for(int vein=0;vein<8;vein++)
            {
                int index=4+vein*17;Vector3 top=vertices[index*2+1];
                var line=CalligraphyGeometry.Line("Mountain dry brush",r.transform,ink.ribbonMaterial,new[]{top,top*.74f+Vector3.left*.8f,top*.35f+Vector3.right*.5f},.8f);
                Register(line,new Color(grey*.8f,grey*.8f,grey*.8f,.5f));
            }
        }
    }
    void BuildTrees()
    {
        for(int i=0;i<12;i++)
        {
            float a=i*Mathf.PI*2/12;Vector3 origin=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(radius-9)-Vector3.up*9;
            float h=Random.Range(4,7);Vector3 bend=new Vector3(Mathf.Sin(i)*1.1f,0,Mathf.Cos(i)*.7f);
            var trunk=CalligraphyGeometry.Line("Ink pine trunk",transform,ink.sculptureMaterial,new[]{origin,origin+Vector3.up*h*.35f+bend*.2f,origin+Vector3.up*h*.7f+bend,origin+Vector3.up*h+bend*.7f},.25f);
            Register(trunk,new Color(.04f,.05f,.05f));
            for(int b=0;b<4;b++)
            {
                Vector3 p=origin+Vector3.up*h*(.35f+b*.15f)+bend*.6f;
                Vector3 dir=Quaternion.Euler(0,b*110+i*33,0)*Vector3.right;
                var branch=CalligraphyGeometry.Line("Pine branch",transform,ink.sculptureMaterial,new[]{p,p+dir*1.6f+Vector3.up*.15f,p+dir*2.1f+Vector3.up*.45f},.1f);Register(branch,new Color(.08f,.09f,.09f));
                for(int leaf=0;leaf<3;leaf++)
                {
                    var canopy=CalligraphyGeometry.MeshObject("Dry brush pine foliage",transform,CalligraphyGeometry.Quad(),ink.ribbonMaterial);
                    canopy.transform.localPosition=p+dir*(.9f+leaf*.5f)+Vector3.up*.3f;canopy.transform.localRotation=Quaternion.Euler(35,b*110+i*33,Random.Range(-10,10));canopy.transform.localScale=new Vector3(2.8f,.9f,1);
                    Register(canopy,new Color(.045f,.055f,.05f,.82f));
                }
            }
        }
    }
    void BuildRiver()
    {
        for(int i=0;i<16;i++)
        {
            var points=new Vector3[28];
            for(int j=0;j<points.Length;j++){float u=j/(float)(points.Length-1);points[j]=new Vector3((u-.5f)*radius*1.8f,.045f+i*.001f,Mathf.Sin(u*6)*4-8+i*.2f);}
            var line=CalligraphyGeometry.Line("River ink reflection",transform,ink.ribbonMaterial,points,.12f+i*.025f);line.transform.localPosition=Vector3.down*9;Register(line,new Color(.3f,.33f,.32f,.5f));
        }
    }
    void BuildScenery()
    {
        // Sparse silhouettes below the invisible walking plane; intentionally no colliders.
        for(int group=0;group<3;group++)
        {
            var root=new GameObject("Decorative bamboo grove "+group).transform;root.SetParent(transform,false);
            root.localPosition=new Vector3(group==1?24:-22,-9,group==2?26:-12);root.localRotation=Quaternion.Euler(0,group*67,0);
            for(int stalk=0;stalk<5;stalk++)
            {
                float h=6+(stalk%3)*1.6f;Vector3 start=new Vector3((stalk-2)*.7f,0,Mathf.Sin(stalk)*.6f);
                Vector3 bend=new Vector3((stalk-2)*.3f,0,.25f);
                var stem=CalligraphyGeometry.Line("Bamboo jointed stalk",root,ink.sculptureMaterial,new[]{start,start+Vector3.up*h*.5f+bend*.3f,start+Vector3.up*h+bend},.13f);Register(stem,new Color(.03f,.045f,.035f));
                for(int joint=1;joint<6;joint++)
                {
                    Vector3 p=start+Vector3.up*h*(joint/6f)+bend*(joint/6f);
                    var node=CalligraphyGeometry.Line("Bamboo node",root,ink.ribbonMaterial,new[]{p-Vector3.right*.13f,p+Vector3.right*.13f},.09f);Register(node,new Color(.2f,.23f,.2f));
                    if(joint<3)continue;
                    for(int leaf=0;leaf<4;leaf++)
                    {
                        float sign=(joint%2==0?1:-1);Vector3 tip=p+new Vector3(sign*(.9f+leaf*.28f),.8f-leaf*.35f,(leaf-1.5f)*.3f);
                        var stroke=CalligraphyGeometry.Line("Bamboo lance leaf",root,ink.ribbonMaterial,new[]{p,Vector3.Lerp(p,tip,.65f)+Vector3.up*.15f,tip},.28f);
                        stroke.widthCurve=AnimationCurve.EaseInOut(0,1,1,0);Register(stroke,new Color(.035f,.055f,.04f,.9f));
                    }
                }
            }
        }
        for(int boat=0;boat<2;boat++)
        {
            var root=new GameObject("Decorative bamboo raft "+boat).transform;root.SetParent(transform,false);
            root.localPosition=new Vector3(boat==0?-12:18,-8.7f,boat==0?-8:-10);root.localRotation=Quaternion.Euler(0,boat==0?22:-30,0);
            for(int log=0;log<7;log++)
            {
                float x=(log-3)*.34f;
                var beam=CalligraphyGeometry.Line("Raft bamboo hull",root,ink.sculptureMaterial,new[]{new Vector3(x,.25f,-2.7f),new Vector3(x,0,-1.8f),new Vector3(x,0,2),new Vector3(x,.3f,2.6f)},.3f);Register(beam,new Color(.15f,.17f,.14f));
            }
            for(int tie=0;tie<3;tie++)
            {
                var beam=CalligraphyGeometry.Line("Raft binding",root,ink.sculptureMaterial,new[]{new Vector3(-1.2f,.2f,(tie-1)*1.6f),new Vector3(1.2f,.2f,(tie-1)*1.6f)},.12f);Register(beam,new Color(.025f,.03f,.025f));
            }
            var pole=CalligraphyGeometry.Line("Resting bamboo pole",root,ink.sculptureMaterial,new[]{new Vector3(-.8f,.3f,-3.2f),new Vector3(.8f,.3f,3.3f)},.09f);Register(pole,new Color(.04f,.05f,.04f));
        }
    }
    void BuildSkyFace()
    {
        // Reuse the actual posed elder mesh, not a face photograph. Clip away torso triangles by bone head height.
        if(!boss.brush || !boss.brush.animator)return;
        Transform headBone=null;
        foreach(var t in boss.brush.animator.GetComponentsInChildren<Transform>())if(t.name=="Head")headBone=t;
        if(!headBone)return;
        var root=new GameObject("Elder visage in the ink sky");root.transform.SetParent(transform,false);face=root.transform;
        foreach(var skin in boss.brush.animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // This rig's baked vertices already contain the bone hierarchy's 2.25 scale.
            // Rotation+translation matches its renderer world bounds; TransformPoint doubles that scale.
            var baked=new Mesh();skin.BakeMesh(baked,false);var vertices=baked.vertices;var triangles=baked.triangles;var kept=new List<int>();
            for(int i=0;i<vertices.Length;i++)vertices[i]=boss.transform.InverseTransformDirection(skin.transform.position+skin.transform.rotation*vertices[i]-headBone.position);
            bool InHead(Vector3 p)=>p.y>-.32f && p.y<.65f && Mathf.Abs(p.x)<.38f && Mathf.Abs(p.z)<.42f;
            for(int i=0;i<triangles.Length;i+=3)
                if(InHead(vertices[triangles[i]]) && InHead(vertices[triangles[i+1]]) && InHead(vertices[triangles[i+2]]))
                    kept.AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});
            var wash=new Color[vertices.Length];
            for(int i=0;i<wash.Length;i++)wash[i]=new Color(1,1,1,Mathf.SmoothStep(0,1,(vertices[i].y+.32f)/.22f));
            baked.vertices=vertices;baked.colors=wash;baked.triangles=kept.ToArray();baked.RecalculateNormals();baked.RecalculateBounds();
            var r=CalligraphyGeometry.MeshObject("Translucent elder head",face,baked,ink.skyVisageMaterial?ink.skyVisageMaterial:ink.sculptureMaterial);faceRenderers.Add(r);
            if(skin.sharedMaterial && skin.sharedMaterial.HasProperty("_BaseMap"))
            {
                var block=new MaterialPropertyBlock();block.SetTexture("_FaceTex",skin.sharedMaterial.GetTexture("_BaseMap")??Texture2D.whiteTexture);r.SetPropertyBlock(block);
            }
        }
        face.localPosition=new Vector3(0,32,radius*.58f);face.localScale=Vector3.one*42;
        face.rotation=Quaternion.LookRotation(player.transform.position-face.position);
    }
    void Update()
    {
        if(!player || !ink || !boss){Destroy(gameObject);return;}
        age+=Time.deltaTime;
        float enter=Mathf.SmoothStep(0,1,age/entrySeconds);
        float exit=Mathf.Clamp01((age-entrySeconds-containedSeconds)/exitSeconds);
        float alpha=enter*(1-Mathf.SmoothStep(0,1,exit));
        isolation.RevealProgress=exit;
        for(int i=0;i<landscape.Count;i++)if(landscape[i])CalligraphyGeometry.Tint(landscape[i],landscapeColors[i],alpha,exit*.85f);
        float fold=Mathf.SmoothStep(0,1,Mathf.Clamp01((age-1.1f)/2.9f));
        for(int i=0;i<panels.Count;i++)
        {
            Vector3 open=new Vector3((i%3-1)*radius*2,-1,(i/3-.5f)*radius*2);
            panels[i].localPosition=Vector3.Lerp(open,panelTargets[i],fold);
            panels[i].localRotation=Quaternion.Slerp(Quaternion.Euler(90,0,0),panelRotations[i],fold);
            // Sheets unroll first, then pivot upward into the enclosure.
            Vector3 full=i<2?new Vector3(radius*2,radius*2,1):new Vector3(radius*2,72,1);
            full.x*=Mathf.SmoothStep(.02f,1,age/1.2f);panels[i].localScale=full;
        }
        if(face)
        {
            Vector3 look=player.transform.position+Vector3.up-face.position;
            face.rotation=Quaternion.Slerp(face.rotation,Quaternion.LookRotation(look),Time.deltaTime*.3f);
            face.localPosition=new Vector3(Mathf.Sin(age*.3f)*.35f,32+Mathf.Sin(age*.5f)*.3f,radius*.58f);
            foreach(var r in faceRenderers)CalligraphyGeometry.Tint(r,Color.white,alpha*(.35f+.27f*(.5f+.5f*Mathf.Sin(age*.7f))),exit*.8f);
        }
        Vector3 velocity=(player.transform.position-previousPlayer)/Mathf.Max(.001f,Time.deltaTime);
        float turn=Quaternion.Angle(previousRotation,player.transform.rotation)/Mathf.Max(.001f,Time.deltaTime);
        var combat=player.GetComponent<PlayerCombat>();
        Vector3 impulse=velocity*disturbanceStrength+player.transform.right*Mathf.Min(4,turn*.02f);
        if(combat && combat.IsAttacking)impulse+=player.transform.forward*6;
        ink.Disturb(player.transform.position+Vector3.up,impulse,disturbanceRadius);
        Vector3 localPlayer=transform.InverseTransformPoint(player.transform.position);
        foreach(var patch in inkPatches)
        {
            bool touching=Vector3.Distance(patch.transform.localPosition,localPlayer)<2.4f;
            float size=touching && velocity.magnitude>.3f?7:2.8f;
            patch.transform.localScale=Vector3.Lerp(patch.transform.localScale,Vector3.one*size,Time.deltaTime*(touching?2.3f:.35f));
            CalligraphyGeometry.Tint(patch,new Color(.002f,.004f,.006f,alpha*.7f));
        }
        float nearest=100;
        foreach(var s in curtains)
        {
            for(int k=0;k<s.points.Length;k++)
            {
                Vector3 p=s.basis[k],delta=p-localPlayer;float distance=delta.magnitude;
                nearest=Mathf.Min(nearest,distance);
                float push=(1-Mathf.Clamp01(distance/disturbanceRadius))*Mathf.Min(2.4f,velocity.magnitude*.3f);
                s.points[k]=p+new Vector3(Mathf.Sin(age*.38f+k*.18f+s.seed),0,Mathf.Cos(age*.31f+k*.13f+s.seed))*.6f+delta.normalized*push;
            }
            s.line.SetPositions(s.points);
        }
        footprintClock-=Time.deltaTime;
        if(footprintClock<=0 && velocity.magnitude>.6f && nearest<disturbanceRadius)
        {
            footprintClock=.18f;ink.Mist(player.transform.position+Vector3.up*.1f,velocity*.12f,2.1f,3.6f);
            ink.Burst(player.transform.position+Vector3.up*.2f,.85f,4);
        }
        previousPlayer=player.transform.position;previousRotation=player.transform.rotation;
        spawnClock-=Time.deltaTime;
        if(spawnClock<=0 && exit<=0)
        {
            spawnClock=.15f;
            Vector3 p=player.transform.position+Random.insideUnitSphere*3+Vector3.up;
            ink.Emit(p,impulse*.15f+Vector3.up*.2f,.6f,3.5f,Random.value<.2f);
            ink.Mist(transform.position+new Vector3(Random.Range(-radius,radius),Random.Range(-7,22),Random.Range(-radius,radius)),Vector3.up*.1f,3.5f,5);
        }
        if(age>=entrySeconds && !sixReleased)
        {
            sixReleased=true;
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI*2/6;
                Vector3 target=CalligraphyGeometry.Ground(player.transform.position,player.transform);
                Vector3 from=target+new Vector3(Mathf.Cos(a)*23,5+i%3*2,Mathf.Sin(a)*23);
                var go=new GameObject("Painting dragon "+(i+1));go.transform.SetParent(transform,false);
                var dragon=go.AddComponent<CalligraphyInkDragon>();dragon.Initialize(ink,player,from,target,boss.paintingDragonScale,1+i*2.5f,false,boss.paintingDragonDamage);dragons.Add(dragon);
            }
        }
        // Wait for a fully opaque outside frame, then remove the painting without a final pop.
        if(exit>=1 && isolation.FullyRevealed){Finished=true;owners.Remove(this);Destroy(gameObject);}
    }
    void OnDisable()
    {
        owners.Remove(this);
    }
}
