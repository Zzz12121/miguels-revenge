#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class InkboundTownBuilder
{
    static void BuildLandscape()
    {
        for(int bank=-1;bank<=1;bank+=2)for(int i=0;i<8;i++)
        {
            float z=-44+i*16.7f;
            if(Mathf.Abs(z+22)<7 || Mathf.Abs(z-53)<7)continue;
            float x=RiverCenter(z)+bank*8.3f;
            Place(willowPrefab,scenery,new Vector3(x,0,z),Range(0,360),Range(.78f,1.04f));
        }
        foreach(Vector3 p in new[]{new Vector3(-65,0,-49),new Vector3(-65,0,51),new Vector3(72,0,-33),new Vector3(68,0,67),new Vector3(-32,0,80)})
            Place(bambooPrefab,scenery,p,Range(0,360),Range(.8f,1.25f));
        for(int i=0;i<4;i++)Place(pinePrefab,scenery,new Vector3(i<2?-70:73,0,-25+(i%2)*82),Range(0,360),Range(.8f,1.15f));
        for(int layer=0;layer<4;layer++)for(int side=0;side<5;side++)
        {
            float a=(side*72+layer*19)*Mathf.Deg2Rad,dist=170+layer*74;
            var ridge=Root("Layered distant mountain "+layer+" "+side,scenery);
            ridge.localPosition=new Vector3(Mathf.Sin(a)*dist,-7,15+Mathf.Cos(a)*dist);ridge.localRotation=Quaternion.Euler(0,side*72+layer*19+180,0);
            var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            float seed=side*11.3f+layer*5.9f;
            float[] peaks=new float[5],centers=new float[5],widths=new float[5];
            for(int k=0;k<5;k++){peaks[k]=Range(18,53);centers[k]=k*.23f+Range(-.08f,.08f);widths[k]=Range(.065f,.17f);}
            const int columns=100,rows=12;
            for(int row=0;row<=rows;row++)for(int k=0;k<=columns;k++)
            {
                float u=k/(float)columns,t=row/(float)rows,x=(u-.5f)*235,peak=4;
                for(int p=0;p<5;p++)peak+=peaks[p]*Mathf.Exp(-Mathf.Pow((u-centers[p])/widths[p],2));
                peak+=(Mathf.PerlinNoise(u*36,seed)-.45f)*7+(Mathf.PerlinNoise(u*91,seed+4)-.5f)*2.4f;
                float profile=Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),1.5f);
                float y=Mathf.Max(0,peak*profile+(Mathf.PerlinNoise(u*13+t*4,seed)-.5f)*8*profile);
                verts.Add(new Vector3(x,y,(t-.5f)*76+Mathf.Sin(u*9+seed)*8));uv.Add(new Vector2(u,profile));
                if(k<columns && row<rows){int q=row*(columns+1)+k;tris.AddRange(new[]{q,q+columns+1,q+1,q+1,q+columns+1,q+columns+2});}
            }
            var mesh=new Mesh{name="Distant brush-cut mountain"};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();
            var mat=Mat("MountainLayer"+layer,.56f+layer*.105f,5,.65f);
            Model("Mountain wash",ridge,SaveMesh(mesh,"Mountain"),mat);
        }
        // River reeds form narrow foreground accents rather than a wall of foliage.
        var reeds=Root("Small reed clusters",scenery);
        for(int group=0;group<14;group++)
        {
            float z=-45+group*9.5f,side=group%2==0?-1:1;
            Vector3 p=new Vector3(RiverCenter(z)+side*5.9f,-.7f,z);
            for(int i=0;i<6;i++)
            {
                Vector3 start=p+new Vector3(i*.07f,0,Mathf.Sin(i)*.14f),top=start+new Vector3(.15f,Range(.6f,1.2f),.06f);
                Beam("Reed stem",reeds,start,top,.018f,foliage);Leaf(reeds,start+Vector3.up*.4f,top+Vector3.right*.25f,.045f);
            }
        }
        Combine(reeds,"Reeds");
    }
    static NpcRoutine.Stop Stop(Vector3 p,float yaw=0,float seconds=0,NpcRoutine.Act act=NpcRoutine.Act.Pass,float seat=0)
        =>new NpcRoutine.Stop{position=p,yaw=yaw,duration=seconds,act=act,seatY=seat};
    static GameObject Resident(string model,string role,NpcRoutine.Stop[] stops,float speed=1)
    {
        var go=Place(residentPrefabs[model],life,stops[0].position,stops[0].yaw);go.name=role;
        var routine=go.AddComponent<NpcRoutine>();routine.animator=go.GetComponentInChildren<Animator>();
        go.transform.localScale*=1.1f;
        routine.ignorePlayer=true;routine.noticeRadius=0;routine.resumeRadius=0;routine.walkSpeed=speed;routine.stops=stops;
        var phase=go.AddComponent<InkboundResidentPhase>();phase.phase=Range(0,1);
        return go;
    }
    static GameObject Stationary(string model,string role,Vector3 p,float yaw,NpcRoutine.Act activity=NpcRoutine.Act.Stand,float seat=0,bool social=false)
    {
        var go=Resident(model,role,new[]{Stop(p,yaw,10000,activity,seat)});
        if(social)
        {
            var gestures=go.AddComponent<TalkGestures>();gestures.animator=go.GetComponentInChildren<Animator>();gestures.turnToVisitor=false;
            var phase=go.GetComponent<InkboundResidentPhase>();phase.social=true;phase.speaking=Range(0,1)>.4f;
        }
        return go;
    }
    static void BuildPopulation()
    {
        Resident("VillagerWoman","First crossing — basket carrier",new[]{Stop(new Vector3(-18,0,-39),90,2,NpcRoutine.Act.Stand),Stop(new Vector3(-10.5f,0,-39),90,2,NpcRoutine.Act.Stand)},.85f);
        Resident("Merchant","First crossing — passing merchant",new[]{Stop(new Vector3(-12,0,-45),0,0),Stop(new Vector3(-12,0,-28),0,2,NpcRoutine.Act.Stand)},.9f);
        string[] looks={"VillagerWoman","Merchant","Scholar","ErrandBoy","Elder","Fisherman"};
        for(int i=0;i<8;i++)
        {
            float x=i<4?-13.5f:14.3f,z=-32+(i%4)*22;
            Resident(looks[i%looks.Length],"Waterfront passerby "+i,new[]{Stop(new Vector3(x,0,z),0,Range(0,3),NpcRoutine.Act.Stand),Stop(new Vector3(x,0,z+15),0,Range(1,4),NpcRoutine.Act.Stand)},Range(.72f,1.15f));
        }
        foreach(float z in new[]{-22f,53f})
        {
            var route=new List<NpcRoutine.Stop>();float center=RiverCenter(z);
            for(int i=0;i<=16;i++){float x=center-10+i*1.25f;route.Add(Stop(new Vector3(x,BridgeHeight(x,z)+.025f,z+.4f),90,i==0?2:0));}
            for(int i=15;i>=0;i--){float x=center-10+i*1.25f;route.Add(Stop(new Vector3(x,BridgeHeight(x,z)+.025f,z+.4f),-90,0));}
            Resident(z<0?"Scholar":"VillagerWoman","Stone bridge crossing "+z,route.ToArray(),.9f);
        }
        for(int i=0;i<6;i++)
        {
            float x=i<3?-40:-49,z=-9+(i%3)*10;
            Stationary(i%2==0?"Merchant":"Granny","Market keeper "+i,new Vector3(x+(i<3?1:-1)*.55f,0,z),i<3?-90:90,NpcRoutine.Act.Stand,0,true);
        }
        Resident("VillagerWoman","Market customer",new[]{Stop(new Vector3(-44,0,-13),0,2,NpcRoutine.Act.Stand),Stop(new Vector3(-44,0,10),90,6,NpcRoutine.Act.Stand)},.8f);
        var tea=buildings.Find("Riverside open tea house");
        for(int i=0;i<4;i++)
        {
            float localX=i<2?-3.8f:3.6f,localZ=i%2==0?-.5f:2.1f;
            Vector3 p=tea.TransformPoint(new Vector3(localX,.24f,localZ));float yaw=tea.eulerAngles.y+(i%2==0?0:180);
            var guest=Stationary(i%2==0?"Scholar":"Elder","Tea house guest "+i,p,yaw,NpcRoutine.Act.Sit,.76f,true);
            var cupRoot=Root("Porcelain cup in hand",guest.transform);Pot(cupRoot,Vector3.zero,.055f,.10f,ceramic);Combine(cupRoot,"TeaCup");
            var phase=guest.GetComponent<InkboundResidentPhase>();phase.drinkingTea=i%2==0;phase.cup=cupRoot;
            if(!phase.drinkingTea)cupRoot.gameObject.SetActive(false);
        }
        Stationary("Merchant","Tea house host",tea.TransformPoint(new Vector3(1,.24f,2.65f)),tea.eulerAngles.y+180,NpcRoutine.Act.Wipe);
        Stationary("Elder","Chess player south",new Vector3(54,.08f,41.7f),0,NpcRoutine.Act.Grind,.60f);
        Stationary("Fisherman","Chess player north",new Vector3(54,.08f,44.3f),180,NpcRoutine.Act.Sit,.60f,true);
        Resident("ChildGirl","Child running around the old tree",new[]{Stop(new Vector3(44,.08f,40),0,1,NpcRoutine.Act.Hop),Stop(new Vector3(48,.08f,48),90,1,NpcRoutine.Act.Hop),Stop(new Vector3(52,.08f,36),180,2,NpcRoutine.Act.Crouch)},2.4f);
        Resident("ErrandBoy","Square delivery boy",new[]{Stop(new Vector3(31,.08f,18),0,0),Stop(new Vector3(47,.08f,18),90,3,NpcRoutine.Act.Stand),Stop(new Vector3(47,.08f,33),0,0),Stop(new Vector3(31,.08f,33),-90,0)},1.2f);
        Stationary("Granny","Resting beside the square",new Vector3(59,.08f,24),-90,NpcRoutine.Act.Sit,.61f);
        RiversideLifeSets();
        BuildExtraPopulation();
        BuildBoat(-49,0);BuildBoat(16,1);
    }
    static void BuildBoat(float startZ,int index)
    {
        var boat=Root("Preserved river ferry "+index,life);
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        // A shallow, pointed hull with raised gunwales and a woven half-canopy.
        for(int i=0;i<16;i++)
        {
            float a=-2.8f+i*5.6f/16,b=-2.8f+(i+1)*5.6f/16;
            float wa=.9f*Mathf.Sin((a+2.8f)/5.6f*Mathf.PI),wb=.9f*Mathf.Sin((b+2.8f)/5.6f*Mathf.PI);
            AddQuad(vertices,uv,triangles,new Vector3(-wa,0,a),new Vector3(wa,0,a),new Vector3(wb,0,b),new Vector3(-wb,0,b));
            foreach(int side in new[]{-1,1})AddQuad(vertices,uv,triangles,new Vector3(side*wa,0,a),new Vector3(side*wb,0,b),new Vector3(side*wb,.36f,b),new Vector3(side*wa,.36f,a));
        }
        var hull=new Mesh{name="River ferry hull"};hull.SetVertices(vertices);hull.SetUVs(0,uv);hull.SetTriangles(triangles,0);hull.RecalculateNormals();Model("Wooden boat hull",boat,hull,timber);
        if(index==0)
        {
        for(int rib=0;rib<6;rib++)for(int part=0;part<8;part++)
        {
            float a=part*Mathf.PI/8,b=(part+1)*Mathf.PI/8,z=-1.1f+rib*.32f;
            Beam("Canopy woven rib",boat,new Vector3(Mathf.Cos(a)*.82f,.34f+Mathf.Sin(a)*1.12f,z),new Vector3(Mathf.Cos(b)*.82f,.34f+Mathf.Sin(b)*1.12f,z),.07f,cloth);
        }
        var coverVertices=new List<Vector3>();var coverUV=new List<Vector2>();var coverTriangles=new List<int>();
        for(int part=0;part<16;part++)
        {
            float a=part*Mathf.PI/16,b=(part+1)*Mathf.PI/16;
            Vector3 p=new Vector3(Mathf.Cos(a)*.80f,.34f+Mathf.Sin(a)*1.10f,-1.13f),q=new Vector3(Mathf.Cos(b)*.80f,.34f+Mathf.Sin(b)*1.10f,-1.13f);
            AddQuad(coverVertices,coverUV,coverTriangles,p,q,q+Vector3.forward*1.65f,p+Vector3.forward*1.65f);
        }
        var cover=new Mesh{name="Woven covered ferry canopy"};cover.SetVertices(coverVertices);cover.SetUVs(0,coverUV);cover.SetTriangles(coverTriangles,0);cover.RecalculateNormals();Model("Continuous woven ferry canopy",boat,cover,cloth);
        }
        else
        {
            for(int i=0;i<4;i++)Board(boat,new Vector3(0,.25f,-1.7f+i*.8f),new Vector3(1.4f,.10f,.22f));
            Basket(boat,new Vector3(-.28f,.12f,-1.3f),.3f,.42f);V3Net(boat,new Vector3(.2f,.6f,-.5f),.9f,.4f);
            V3Lathe(boat,new Vector3(.18f,.12f,.2f),.31f,.2f,new[]{.7f,.85f,1f},cloth);
        }
        for(int i=0;i<6;i++)Box("Deck plank",boat,new Vector3(0,.08f,-2.5f+i*.95f),new Vector3(1.4f,.1f,.09f),cloth);
        Combine(boat,"Ferry");
        var passenger=Place(residentPrefabs["Fisherman"],boat,new Vector3(0,.12f,1.6f),180);
        passenger.transform.localScale*=1.1f;
        var oar=Root("Rowing oar",boat);oar.localPosition=new Vector3(.85f,.6f,1.7f);oar.localRotation=Quaternion.Euler(30,0,22);
        Beam("Oar shaft",oar,new Vector3(0,-1.5f,0),new Vector3(0,1,0),.055f,timber);Box("Oar blade",oar,new Vector3(0,-1.35f,0),new Vector3(.24f,.65f,.055f),timber);Combine(oar,"Oar");
        var motion=boat.gameObject.AddComponent<InkboundBoat>();motion.rower=passenger.GetComponentInChildren<Animator>();motion.oar=oar;motion.speed=.65f;motion.timeOffset=index*8;motion.wakeIndex=index;
        var path=new List<Vector3>();
        for(int i=0;i<=16;i++){float z=-62+i*9.1f;path.Add(new Vector3(RiverCenter(z)+(index==0?-1.8f:1.8f),-.98f,z));}
        for(int i=16;i>=0;i--){float z=-62+i*9.1f;path.Add(new Vector3(RiverCenter(z)+(index==0?1.8f:-1.8f),-.98f,z));}
        motion.route=path.ToArray();boat.localPosition=new Vector3(RiverCenter(startZ),-.98f,startZ);
    }
    static void BuildLightingAndPlayer()
    {
        var sky=Special("UnpaintedPaperSky","Project/InkboundTown/Paper Sky");
        sky.SetColor("_Tint",new Color(.957f,.949f,.918f));EditorUtility.SetDirty(sky);AssetDatabase.SaveAssetIfDirty(sky);
        RenderSettings.skybox=sky;RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.82f,.82f,.79f);RenderSettings.ambientIntensity=.8f;
        var sun=Root("Soft afternoon daylight",town).gameObject.AddComponent<Light>();sun.type=LightType.Directional;
        sun.transform.rotation=Quaternion.Euler(46,-32,0);sun.intensity=1.2f;sun.color=new Color(1,.98f,.92f);sun.shadows=LightShadows.Soft;
        var rig=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/PlayerRig.prefab"));
        rig.name="PlayerRig — Inkbound exploration instance";
        var motor=rig.GetComponentInChildren<ThirdPersonController>();motor.transform.SetPositionAndRotation(Entrance,Quaternion.identity);
        motor.gameObject.AddComponent<InkboundDisturbanceSource>();
        motor.gameObject.AddComponent<InkboundGroundWash>().inkMaterial=plume;
        var combat=motor.GetComponent<PlayerCombat>();if(combat)combat.enabled=false;
        foreach(var skill in rig.GetComponentsInChildren<VoidRendSkillDemo>(true))
        {
            // Its existing cleanup writes a transient clock to this material, even when no skill is cast.
            // Keep that write on a town-owned asset instead of the shared combat material.
            string path=Materials+"/M_IT_UnusedSkillRefraction.mat";
            var local=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!local && skill.refractionMaterial){local=new Material(skill.refractionMaterial);AssetDatabase.CreateAsset(local,path);}
            skill.refractionMaterial=local;skill.enabled=false;
        }
        var orbit=rig.GetComponentInChildren<ThirdPersonCamera>();orbit.distance=7;orbit.maxDistance=12;orbit.startPitch=10;orbit.pitch=10;orbit.yaw=0;
        var camera=rig.GetComponentInChildren<Camera>();camera.clearFlags=CameraClearFlags.Skybox;camera.backgroundColor=new Color(.957f,.949f,.918f);camera.farClipPlane=650;
        var cameraData=camera.GetUniversalAdditionalCameraData();cameraData.renderPostProcessing=false;cameraData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var marker=Root("Player arrival point",town);marker.position=Entrance;
        var exploration=town.gameObject.AddComponent<InkboundExploration>();exploration.player=motor;exploration.entrance=marker;
        exploration.showArrivalTitle=false;
        var ambience=Root("River and distant birds",town);ambience.position=new Vector3(0,0,10);
        var sound=ambience.gameObject.AddComponent<InkboundAmbience>();
        sound.birds=new[]{"bird_sparrow_1","bird_dove_1","bird_uguisu_1"}.Select(n=>Resources.Load<AudioClip>("Audio/SFX/Gen/"+n)).Where(c=>c).ToArray();
        sound.riverLoop=BuildRiverAudio();
    }
    static AudioClip BuildRiverAudio()
    {
        const string path="Assets/_Project/VFX/InkboundTown/IT_RiverAmbience.wav";
        if(!System.IO.File.Exists(path))
        {
            int rate=22050,count=rate*8;var random=new System.Random(523);float filtered=0;
            using(var writer=new System.IO.BinaryWriter(System.IO.File.Open(path,System.IO.FileMode.Create)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
                for(int i=0;i<count;i++)
                {
                    float t=i/(float)rate;filtered=Mathf.Lerp(filtered,(float)random.NextDouble()*2-1,.12f);
                    float envelope=Mathf.SmoothStep(0,1,Mathf.Min(t,8-t)*3);
                    float sample=(filtered*.34f+Mathf.Sin(t*5.1f)*.015f)*(.85f+Mathf.Sin(t*.785398f)*.12f)*envelope;
                    writer.Write((short)(Mathf.Clamp(sample,-1,1)*32767));
                }
            }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
#endif
