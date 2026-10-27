using System.Collections.Generic;
using UnityEngine;

/// <summary>Local landscape transformation with a seamless return to the entry coordinate.</summary>
public sealed class CalligraphyPaintingWorld : MonoBehaviour
{
    public enum WorldPhase { Entering,Contained,Exiting,Finished }
    public WorldPhase Phase=>Finished?WorldPhase.Finished:age<entrySeconds?WorldPhase.Entering:age<entrySeconds+containedSeconds?WorldPhase.Contained:WorldPhase.Exiting;
    static readonly HashSet<CalligraphyPaintingWorld> owners=new();
    public static bool BlocksTether {get{owners.RemoveWhere(o=>!o || !o.isActiveAndEnabled);return owners.Count>0;}}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatics()=>owners.Clear();
    public float entrySeconds=4,containedSeconds=20,exitSeconds=2.5f,radius=96;
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
    bool dragonsReleased;
    sealed class HangingStroke {public LineRenderer line;public Vector3[] basis,points;public float seed;}
    readonly List<HangingStroke> curtains=new();
    float footprintClock;
    Vector3 previousPlayer;
    Vector3 entryPosition;
    bool returned;
    Vector3 lastFootprintPosition;
    Quaternion previousRotation;
    float sizeMultiplier=1;
    Camera worldCamera;
    float originalFarClip;
    public void Begin(CalligraphyBossCombat owner,PlayerHealth target,CalligraphyInkField field)
    {
        PlayerSkillAvailability.CancelActive(target);
        boss=owner;player=target;entryPosition=player.transform.position;
        transform.position=CalligraphyGeometry.Ground(player.transform.position,player.transform);
        // World pigment has its own lifetime and remains visible while the outside boss is hidden.
        var pool=new GameObject("Painting pigment pool");pool.SetActive(false);pool.transform.SetParent(transform,false);
        ink=pool.AddComponent<CalligraphyInkField>();
        ink.ribbonMaterial=field.ribbonMaterial;ink.sculptureMaterial=field.sculptureMaterial;ink.paperMaterial=field.paperMaterial;
        ink.yinYangMaterial=field.yinYangMaterial;ink.landscapeMaterial=field.landscapeMaterial;
        ink.originalInkMaterial=field.originalInkMaterial;ink.originalEchoMaterial=field.originalEchoMaterial;pool.SetActive(true);
        ink.suspendedWashMaterial=field.suspendedWashMaterial;
        ink.skyVisageMaterial=field.skyVisageMaterial;
        sizeMultiplier=Mathf.Max(.1f,boss.paintingWorld.sizeMultiplier);
        radius=boss.paintingRadius*sizeMultiplier;disturbanceRadius=boss.disturbanceRadius;disturbanceStrength=boss.disturbanceStrength;
        entrySeconds=boss.paintingWorld.entrySeconds;containedSeconds=boss.paintingWorld.containedSeconds;exitSeconds=boss.paintingWorld.exitSeconds;
        worldCamera=Camera.main;if(worldCamera){originalFarClip=worldCamera.farClipPlane;worldCamera.farClipPlane=Mathf.Max(originalFarClip,radius*4);}
        previousPlayer=lastFootprintPosition=player.transform.position;previousRotation=player.transform.rotation;
        SkillAimCancellation.Cancel();owners.Add(this);
        BuildLowerGround();BuildSheets();BuildMountains();BuildTrees();BuildRiver();BuildScenery();BuildSkyFace();BuildHangingInk();
        // Stretch enclosure/backdrop only. Interior scenery retains natural proportions.
        foreach(Transform child in transform)
        {
            if(child==face || !(child.name.StartsWith("Unfolding paper") || child.name.StartsWith("Mountain wash") || child.name.StartsWith("Suspended branching")))continue;
            var p=child.localPosition;p.y*=1.5f*sizeMultiplier;child.localPosition=p;
            var s=child.localScale;s.y*=1.5f*sizeMultiplier;child.localScale=s;
        }
        for(int i=0;i<panelTargets.Count;i++){var p=panelTargets[i];p.y*=1.5f*sizeMultiplier;panelTargets[i]=p;}
        BuildSupport();
        isolation=gameObject.AddComponent<CalligraphyWorldIsolation>();isolation.Initialize(player);isolation.SetReturnPosition(entryPosition);
    }
    void BuildSupport()
    {
        // No renderer: feet stay on the invisible plane, with the original landscape below.
        void Box(string name,Vector3 position,Vector3 size)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);go.transform.localPosition=position;
            go.AddComponent<BoxCollider>().size=size;
        }
        Box("Player-only transparent support",new Vector3(0,-.15f,0),new Vector3(radius*2,.3f,radius*2));
        for(int side=-1;side<=1;side+=2)
        {
            Box("Painting X boundary",new Vector3(side*radius,40*sizeMultiplier,0),new Vector3(1,108*sizeMultiplier,radius*2));
            Box("Painting Z boundary",new Vector3(0,40*sizeMultiplier,side*radius),new Vector3(radius*2,108*sizeMultiplier,1));
        }
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
            float a=i*2.39996f,r=Mathf.Lerp(18,radius*boss.paintingWorld.streamSpread,Mathf.Sqrt((i+.5f)/20));
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
            float a=i*2.39996f,r=Mathf.Lerp(5,radius*.8f,Mathf.Sqrt((i+.5f)/24));
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
            r.sortingOrder=-30;
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
            r.sortingOrder=-20;
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
            float a=i*2.39996f;Vector3 origin=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*Mathf.Lerp(22,radius*boss.paintingWorld.scenerySpread,(i%4)/3f)-Vector3.up*9;
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
        for(int group=0;group<boss.paintingWorld.sceneryGroups;group++)
        {
            var root=new GameObject("Decorative bamboo grove "+group).transform;root.SetParent(transform,false);
            float angle=group*2.39996f,reach=Mathf.Lerp(20,radius*boss.paintingWorld.scenerySpread,(group%3)/2f);
            root.localPosition=new Vector3(Mathf.Cos(angle)*reach,-9,Mathf.Sin(angle)*reach);root.localRotation=Quaternion.Euler(0,group*67,0);
            for(int stalk=0;stalk<3;stalk++)
            {
                float h=6+(stalk%3)*1.6f;Vector3 start=new Vector3((stalk-2)*.7f,0,Mathf.Sin(stalk)*.6f);
                Vector3 bend=new Vector3((stalk-2)*.3f,0,.25f);
                var stem=CalligraphyGeometry.Line("Bamboo jointed stalk",root,ink.sculptureMaterial,new[]{start,start+Vector3.up*h*.5f+bend*.3f,start+Vector3.up*h+bend},.13f);Register(stem,new Color(.03f,.045f,.035f));
                for(int joint=1;joint<6;joint++)
                {
                    Vector3 p=start+Vector3.up*h*(joint/6f)+bend*(joint/6f);
                    var node=CalligraphyGeometry.Line("Bamboo node",root,ink.ribbonMaterial,new[]{p-Vector3.right*.13f,p+Vector3.right*.13f},.09f);Register(node,new Color(.2f,.23f,.2f));
                    if(joint<3)continue;
                    for(int leaf=0;leaf<3;leaf++)
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
            root.localPosition=new Vector3(boat==0?-26:radius*.6f,-8.7f,boat==0?-8:-radius*.36f);root.localRotation=Quaternion.Euler(0,boat==0?22:-30,0);
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
        BuildWetlands();
    }
    void BuildWetlands()
    {
        for(int group=0;group<boss.paintingWorld.sceneryGroups;group++)
        {
            float angle=group*2.39996f+.8f,reach=Mathf.Lerp(26,radius*boss.paintingWorld.scenerySpread,(group%3)/2f);
            var root=new GameObject("Interior ink wetland "+group).transform;root.SetParent(transform,false);
            root.localPosition=new Vector3(Mathf.Cos(angle)*reach,-8.9f,Mathf.Sin(angle)*reach);
            for(int ring=0;ring<4;ring++)
            {
                var points=new Vector3[33];for(int k=0;k<33;k++){float a=k*Mathf.PI/16;points[k]=new Vector3(Mathf.Cos(a)*(4+ring*.7f),.02f,Mathf.Sin(a)*(2+ring*.4f));}
                Register(CalligraphyGeometry.Line("Wetland rippled water",root,ink.ribbonMaterial,points,.12f),new Color(.12f,.16f,.17f,.6f));
            }
            for(int reed=0;reed<7;reed++)
            {
                Vector3 p=new((reed-3)*.45f,0,2.8f+Mathf.Sin(reed));float h=1.4f+(reed%3)*.4f;
                Register(CalligraphyGeometry.Line("Reed stem",root,ink.sculptureMaterial,new[]{p,p+new Vector3(.12f,h*.6f,0),p+new Vector3(.35f,h,0)},.045f),new Color(.07f,.09f,.08f));
                Register(CalligraphyGeometry.Line("Reed plume",root,ink.ribbonMaterial,new[]{p+new Vector3(.35f,h,0),p+new Vector3(.55f,h+.6f,0)},.22f),new Color(.2f,.21f,.18f,.8f));
            }
            // Small sculptural islands; actual meshes, no gameplay components or colliders.
            var mesh=new Mesh{name="Ink shoreline rock"};mesh.vertices=new[]{new Vector3(-2,0,-1),new Vector3(2,0,-1),new Vector3(1,0,2),new Vector3(-1,0,2),new Vector3(-.6f,2.2f,.2f),new Vector3(.8f,1.4f,.5f)};
            mesh.uv=new[]{new Vector2(0,.2f),new Vector2(1,.2f),new Vector2(.8f,.2f),new Vector2(.2f,.2f),new Vector2(.35f,1),new Vector2(.7f,.7f)};
            mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white,Color.white,Color.white};
            mesh.triangles=new[]{0,4,1,1,4,5,1,5,2,2,5,3,3,5,4,3,4,0};mesh.RecalculateNormals();
            var rock=CalligraphyGeometry.MeshObject("Wetland angular scholar stone",root,mesh,ink.landscapeMaterial);rock.transform.localPosition=new Vector3(-4,0,2);Register(rock,new Color(.25f,.28f,.29f));
            if(group%3==0)
            {
                var bird=new GameObject("Decorative white egret").transform;bird.SetParent(root,false);bird.localPosition=new Vector3(1,.1f,1);
                Register(CalligraphyGeometry.Line("Egret legs",bird,ink.sculptureMaterial,new[]{new Vector3(-.1f,0,0),new Vector3(0,.8f,0),new Vector3(.15f,0,.1f)},.035f),new Color(.04f,.05f,.06f));
                Register(CalligraphyGeometry.Line("Egret ink silhouette",bird,ink.ribbonMaterial,new[]{new Vector3(-.4f,.9f,.025f),new Vector3(0,1,.025f),new Vector3(.25f,1.25f,.025f),new Vector3(.1f,1.65f,.025f),new Vector3(.38f,1.85f,.025f)},.36f),new Color(.06f,.08f,.09f));
                Register(CalligraphyGeometry.Line("Egret body and curved neck",bird,ink.ribbonMaterial,new[]{new Vector3(-.4f,.9f,0),new Vector3(0,1,0),new Vector3(.25f,1.25f,0),new Vector3(.1f,1.65f,0),new Vector3(.38f,1.85f,0)},.28f),new Color(.83f,.85f,.84f));
                Register(CalligraphyGeometry.Line("Egret beak",bird,ink.sculptureMaterial,new[]{new Vector3(.38f,1.85f,0),new Vector3(.85f,1.8f,0)},.045f),new Color(.04f,.05f,.06f));
            }
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
            var baked=new Mesh();skin.BakeMesh(baked,false);var vertices=baked.vertices;
            for(int i=0;i<vertices.Length;i++)vertices[i]=boss.transform.InverseTransformDirection(skin.transform.position+skin.transform.rotation*vertices[i]-headBone.position);
            bool InHead(Vector3 p)=>p.y>-.32f && p.y<.65f && Mathf.Abs(p.x)<.38f && Mathf.Abs(p.z)<.42f;
            var wash=new Color[vertices.Length];
            for(int i=0;i<wash.Length;i++)wash[i]=new Color(1,1,1,Mathf.SmoothStep(0,1,(vertices[i].y+.32f)/.22f));
            // Eyes, face, hair and beard have different source materials. Do not flatten submeshes.
            for(int sub=0;sub<baked.subMeshCount;sub++)
            {
                var triangles=baked.GetTriangles(sub);var kept=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                    if(InHead(vertices[triangles[i]]) && InHead(vertices[triangles[i+1]]) && InHead(vertices[triangles[i+2]]))kept.AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});
                if(kept.Count==0)continue;
                // Remove unused body vertices too: bounds otherwise include the entire giant body,
                // which breaks transparent sorting/culling even though only head triangles remain.
                var map=new Dictionary<int,int>();var positions=new List<Vector3>();var uv=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>();var sourceUv=baked.uv;
                foreach(int old in kept)
                {
                    if(!map.TryGetValue(old,out int index)){index=positions.Count;map.Add(old,index);positions.Add(vertices[old]);uv.Add(sourceUv[old]);colors.Add(wash[old]);}
                    indices.Add(index);
                }
                var mesh=new Mesh{name="Elder sky head submesh "+sub};mesh.SetVertices(positions);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                var r=CalligraphyGeometry.MeshObject("Translucent elder head "+sub,face,mesh,ink.skyVisageMaterial?ink.skyVisageMaterial:ink.sculptureMaterial);faceRenderers.Add(r);
                r.sortingOrder=-10;
                var source=sub<skin.sharedMaterials.Length?skin.sharedMaterials[sub]:skin.sharedMaterial;
                var block=new MaterialPropertyBlock();
                block.SetFloat("_EyeVisibility",boss.paintingWorld.eyeVisibility);
                block.SetVector("_EyeCenter",boss.paintingWorld.eyeCenter);
                block.SetTexture("_FaceTex",source && source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap")??Texture2D.whiteTexture:source && source.mainTexture?source.mainTexture:Texture2D.whiteTexture);r.SetPropertyBlock(block);
            }
            Destroy(baked);
        }
        face.localPosition=new Vector3(0,32*sizeMultiplier,radius*.42f)+boss.paintingWorld.faceOffset;face.localScale=Vector3.one*boss.paintingWorld.faceScale;
        face.rotation=Quaternion.LookRotation(player.transform.position-face.position);
    }
    void Update()
    {
        if(!player || player.IsDead || !ink || !boss || boss.IsDead){Destroy(gameObject);return;}
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
            Vector3 full=i<2?new Vector3(radius*2,radius*2,1):new Vector3(radius*2,108*sizeMultiplier,1);
            full.x*=Mathf.SmoothStep(.02f,1,age/1.2f);panels[i].localScale=full;
        }
        if(face)
        {
            Vector3 look=player.transform.position+Vector3.up-face.position;
            face.rotation=Quaternion.Slerp(face.rotation,Quaternion.LookRotation(look),Time.deltaTime*.3f);
            face.localPosition=new Vector3(Mathf.Sin(age*.3f)*.35f,32*sizeMultiplier+Mathf.Sin(age*.5f)*.3f,radius*.42f)+boss.paintingWorld.faceOffset;
            foreach(var r in faceRenderers)CalligraphyGeometry.Tint(r,Color.white,alpha*boss.paintingWorld.faceOpacity*(.88f+.12f*Mathf.Sin(age*.7f)),exit*.8f);
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
        var controller=player.GetComponent<ThirdPersonController>();
        // Capsule depenetration can jitter a few millimetres while idle. Require genuine net
        // travel as well as speed, rather than amplifying a tiny correction on a short frame.
        if(footprintClock<=0 && Vector3.ProjectOnPlane(player.transform.position-lastFootprintPosition,Vector3.up).sqrMagnitude>=.06f*.06f && Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude>.2f && controller && controller.IsGrounded && exit<=0)
        {
            lastFootprintPosition=player.transform.position;
            footprintClock=boss.paintingWorld.pulseInterval;
            var library=CalligraphyVfxLibrary.Current;
            if(library)
            {
                var pulse=CalligraphyVfxLibrary.Play(library.pulse,new Vector3(player.transform.position.x,player.FeetY+.08f,player.transform.position.z),Quaternion.identity,boss.paintingWorld.pulseScale,1.5f,0,transform);
                if(pulse)
                {
                    // Keep the original ground-ring systems, not the package's billowing cloud.
                    foreach(var ps in pulse.Systems)
                    {
                        if(ps.name.StartsWith("yy_Shockwave"))ps.Emit(1);
                        else if(!ps.name.Contains("Liquid_smudge"))ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    // Vendor rings are rate=2/s with a zero-count initial burst. Waiting .12/.3s
                    // then stopping emission used to cancel them before their first particle.
                    pulse.StopAfter(.1f,boss.paintingWorld.pulseFade);
                }
            }
        }
        previousPlayer=player.transform.position;previousRotation=player.transform.rotation;
        spawnClock-=Time.deltaTime;
        if(spawnClock<=0 && exit<=0)
        {
            spawnClock=1.2f;
            ink.Mist(transform.position+new Vector3(Random.Range(-radius,radius),Random.Range(-7,22),Random.Range(-radius,radius)),Vector3.up*.1f,3.5f,5);
        }
        if(age>=entrySeconds && !dragonsReleased)
        {
            dragonsReleased=true;
            int count=Mathf.Clamp(boss.paintingWorld.dragonCount,0,12);
            Vector3 target=CalligraphyGeometry.Ground(player.transform.position,player.transform);
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count;
                Vector3 from=target+new Vector3(Mathf.Cos(a)*34.5f,7.5f+i%3*3,Mathf.Sin(a)*34.5f)*sizeMultiplier;
                var go=new GameObject("Painting dragon "+(i+1));go.transform.SetParent(transform,false);
                var dragon=go.AddComponent<CalligraphyInkDragon>();dragon.Initialize(ink,player,from,target,boss.paintingDragonScale,1+i*2.5f,false,boss.paintingDragonDamage);dragons.Add(dragon);
            }
        }
        // Wait for a fully opaque outside frame, then remove the painting without a final pop.
        if(exit>=1 && isolation.FullyRevealed){ReturnToEntry();Finished=true;owners.Remove(this);Destroy(gameObject);}
    }
    void ReturnToEntry()
    {
        if(returned || !player)return;returned=true;
        Vector3 delta=entryPosition-player.transform.position;
        var motor=player.GetComponent<ThirdPersonController>();
        var body=player.GetComponent<CharacterController>();bool enabled=body && body.enabled;
        if(body)body.enabled=false;
        player.transform.position=entryPosition;motor?.ResetMotion();
        if(body)body.enabled=enabled;
        Physics.SyncTransforms();
        if(worldCamera)
        {
            var orbit=worldCamera.GetComponent<ThirdPersonCamera>();
            if(orbit)orbit.WarpWithTarget(delta);
            else worldCamera.transform.position+=delta;
        }
    }
    void OnDisable()
    {
        ReturnToEntry();
        if(worldCamera)worldCamera.farClipPlane=originalFarClip;
        owners.Remove(this);
    }
}
