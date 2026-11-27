#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.AI.Navigation;

public static partial class InkboundTownBuilder
{
    public static bool V3SliceOnly;
    static readonly List<(Transform transform,int type)> shopSites=new();
    static GameObject V3PlaceShop(int type,int variation,Vector3 p,float yaw)
    {
        var go=Place(housePrefabs[type*2+variation],buildings,p,yaw);
        shopSites.Add((go.transform,type));return go;
    }
    static void BuildV3Blocks()
    {
        shopSites.Clear();
        var slice=Root("V3 first street - 28m quality slice",streets);
        Box("Smooth street collision",slice,new Vector3(-44,.045f,1),new Vector3(7,.09f,36),stone,true);
        for(int row=0;row<4;row++)
        {
            int left=new[]{0,1,3,7}[row],right=new[]{4,2,6,5}[row];float z=-12+row*8.2f;
            V3PlaceShop(left,row%2,new Vector3(-51.1f,0,z),-90);
            V3PlaceShop(right,(row+1)%2,new Vector3(-36.9f,0,z+1.2f),90);
        }
        var streetGate=Root("Market street timber paifang",buildings);streetGate.position=new Vector3(-44,0,25);
        foreach(int s in new[]{-1,1})
        {RoundTimber(streetGate,new Vector3(s*3.7f,0,0),new Vector3(s*3.7f,6.1f,0),.19f);var column=Root("Market gate solid column",streetGate);column.localPosition=new Vector3(s*3.7f,3,0);var collider=column.gameObject.AddComponent<CapsuleCollider>();collider.height=6;collider.radius=.19f;}
        RoundTimber(streetGate,new Vector3(-4,5.4f,0),new Vector3(4,5.4f,0),.18f);Roof(streetGate,9.2f,2.5f,6.05f,1.05f);Combine(streetGate,"MarketGate");
        var tea=V3PlaceShop(1,1,new Vector3(28,0,-24),90);tea.name="Riverside open tea house";
        // Planted edges, steps and setbacks soften the transition between shop and pavement.
        for(int side=-1;side<=1;side+=2)for(int j=0;j<6;j++)
        {
            Vector3 p=new Vector3(-44+side*3.6f,0,-13+j*5.6f);
            if(j%3==0)V3Planter(p,2,.42f,.40f);
            else if(j%3==1)V3Planter(p,3,.36f,.43f);
            else Place(v3Plants[6],scenery,p,Range(0,360),.48f);
            var trim=Root("Uneven shop threshold",slice);
            for(int k=0;k<3;k++)WornStone(trim,p+new Vector3(side*.5f,.09f,k*.4f),new Vector3(.9f,.14f,.39f),bankStone);
            Combine(trim,"ShopThreshold");
        }
        if(V3SliceOnly)return;
        // Dense blocks leave distinct north/south streets and perpendicular alleys between groups.
        for(int side=-1;side<=1;side+=2)
        {
            float x=side*20.5f;
            float[] z={-43,-34,-7,2,11,20,29,38,68,77};
            for(int i=0;i<z.Length;i++)
            {
                V3PlaceShop((i+(side<0?0:3))%8,i%2,new Vector3(x+(i%3==0?side*.8f:0),0,z[i]),side<0?-90:90);
            }
            // Rear roof streets close the town's silhouette without creating a solid impassable wall.
            float[] rear=side<0?new[]{-45f,-36,-27,39,48,57,66,75}:new[]{-43f,-34,-25,-6,3,65};
            for(int i=0;i<rear.Length;i++)V3PlaceShop((i+5)%8,(i+1)%2,new Vector3(side*63,0,rear[i]),side<0?-90:90);
        }
        float[] innerEast={-44f,-7,2,11,48,67,76};
        for(int i=0;i<innerEast.Length;i++)V3PlaceShop((i+2)%8,i%2,new Vector3(36,0,innerEast[i]),90);
        for(int i=0;i<8;i++)V3PlaceShop((i+1)%8,i%2,new Vector3(i<4?-63+i*8.1f:15+(i-4)*8.2f,0,94+(i%3)*2),i%2==0?0:180);
        BuildV3Courtyard();BuildV3Docks();
        foreach(float z in new[]{-48f,-22f,49f,64f})
        {
            foreach(int s in new[]{-1,1})
            {var lane=Root("Continuous transverse alley",streets);Box("Worn paving",lane,new Vector3(s*37,.025f,z),new Vector3(49,.05f,z==-22?7:3),stone);}
        }
    }
    static void V3Planter(Vector3 p,int speciesIndex,float radius,float plantScale)
    {
        var pot=Root("Planted ceramic threshold",scenery);pot.position=p;
        Pot(pot,Vector3.zero,radius,radius*1.3f,ceramic);Ring(pot,Vector3.up*radius*1.3f,radius*.64f,.025f,cloth);
        Combine(pot,"PlantedVessel");Place(v3Plants[speciesIndex],pot,Vector3.up*radius*1.1f,Range(0,360),plantScale);
    }
    static void BuildV3Courtyard()
    {
        var court=Root("Old tree chess courtyard",streets);
        Box("Natural small square",court,new Vector3(42,.04f,29),new Vector3(25,.08f,26),stone,true);
        V3PlaceShop(7,1,new Vector3(52,0,52),0);V3PlaceShop(2,1,new Vector3(66,0,49),90);
        var wall=Root("Courtyard moon gate",court);wall.position=new Vector3(51,0,36);
        // Masonry above the arch leaves a true opening, shared with the navigable courtyard.
        const float radius=1.75f,height=2.05f;
        for(int i=0;i<26;i++)
        {
            float x=-6+i*.48f;float bottom=Mathf.Abs(x)<radius?height+Mathf.Sqrt(Mathf.Max(0,radius*radius-x*x)):0;
            Box("Moon gate masonry",wall,new Vector3(x,(4.1f+bottom)*.5f,0),new Vector3(.49f,Mathf.Max(.03f,4.1f-bottom),.34f),plaster,true);
        }
        var arch=new BrushGeometry();
        for(int i=0;i<14;i++)
        {float a=i*Mathf.PI/14,b=(i+1)*Mathf.PI/14;Vector3 p=new Vector3(Mathf.Cos(a)*radius,height+Mathf.Sin(a)*radius,-.2f),q=new Vector3(Mathf.Cos(b)*radius,height+Mathf.Sin(b)*radius,-.2f);arch.Tube(p,Vector3.Lerp(p,q,.3f),Vector3.Lerp(p,q,.7f),q,.09f,.09f,3,6);}
        Model("Moon gate stone arch",wall,arch.Mesh("Moon gate"),bankStone);
        for(int s=-1;s<=1;s+=2)RoundTimber(wall,new Vector3(s*radius,0,-.2f),new Vector3(s*radius,height,-.2f),.09f,bankStone);
        Roof(wall,13.1f,1,4.14f,.25f);Combine(wall,"MoonGate");
        Place(v3Plants[4],scenery,new Vector3(57,0,45),-23,1.13f);
        var furniture=Root("Chess under camphor canopy",court);
        furniture.gameObject.AddComponent<InkboundPassableGroup>();
        V3Table(furniture,new Vector3(54,.08f,43),true,false);
        Board(furniture,new Vector3(54,.94f,43),new Vector3(.67f,.035f,.67f),paper);
        for(int i=0;i<19;i++)
        {RoundTimber(furniture,new Vector3(53.70f,.967f,42.70f+i*.03333f),new Vector3(54.30f,.967f,42.70f+i*.03333f),.0015f);RoundTimber(furniture,new Vector3(53.70f+i*.03333f,.967f,42.70f),new Vector3(53.70f+i*.03333f,.967f,43.30f),.0015f);}
        for(int j=0;j<15;j++)Pot(furniture,new Vector3(53.76f+j%5*.10f,.97f,42.78f+j/5*.15f),.025f,.025f,j%2==0?ink:paper);
        Cup(furniture,new Vector3(53.3f,.91f,42.7f));Cup(furniture,new Vector3(54.7f,.91f,43.2f));
        for(int j=0;j<3;j++){V3Planter(new Vector3(49+j*4,0,48),j==1?1:3,.43f,j==1?.23f:.42f);Place(v3Plants[5],scenery,new Vector3(61+j*.7f,0,39),j*47,.68f);}
        Combine(furniture,"ChessFurniture");
        var garden=Root("Bounded living chess garden",buildings);
        foreach(var spec in new[]{new Vector3(64,0,40),new Vector3(64,0,53)})
        {
            var part=Root("Garden wall with open east passage",garden);part.position=spec;part.rotation=Quaternion.Euler(0,90,0);
            Box("Low limewashed garden wall",part,new Vector3(0,.80f,0),new Vector3(7,1.6f,.3f),plaster,true);Roof(part,7.3f,.85f,1.6f,.15f);Combine(part,"GardenWall");
        }
        Box("Chess court paving",court,new Vector3(54,.045f,43.7f),new Vector3(10,.09f,12),stone);
        for(int j=0;j<9;j++)
        {
            Vector3 p=new Vector3(61.4f+Range(-.45f,.45f),0,38+j*1.75f);
            Place(v3Plants[j%3==0?2:j%3==1?4:6],scenery,p,Range(0,360),j%3==0?.44f:j%3==1?.16f:.75f);
            if(j%2==0)V3Rock(scenery,p+new Vector3(-.8f,.32f,.35f),new Vector3(1.2f,.65f,.9f));
        }
        var litter=new BrushGeometry();
        for(int j=0;j<55;j++){Vector3 p=new Vector3(57+Range(-3,3),.06f,45+Range(-3,3));litter.Lance(p,p+new Vector3(Range(-.12f,.12f),.012f,Range(-.12f,.12f)),.025f,j);}
        Model("Sparse fallen leaves under old tree",scenery,SaveMesh(litter.Mesh("Leaf litter"),"V3LeafLitter"),leafWash);
        // A pavilion establishes a second landmark from the bridge without sealing the plaza.
        var pavilion=Root("Open river pavilion",buildings);pavilion.position=new Vector3(15,0,59);
        Board(pavilion,new Vector3(0,.19f,0),new Vector3(6,.3f,6),bankStone);
        foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
        {RoundTimber(pavilion,new Vector3(x*2.1f,.3f,z*2.1f),new Vector3(x*2.1f,4,z*2.1f),.13f);var c=Root("Pavilion post collision",pavilion);c.localPosition=new Vector3(x*2.1f,2,z*2.1f);c.gameObject.AddComponent<CapsuleCollider>().height=4;c.GetComponent<CapsuleCollider>().radius=.13f;}
        Roof(pavilion,6.9f,6.9f,4,2.2f);Combine(pavilion,"Pavilion");
    }
    static void BuildV3Docks()
    {
        foreach(int s in new[]{-1,1})
        {
            float z=s<0?-39:28,c=RiverCenter(z);
            var dock=Root("Timber fishing landing",buildings);dock.position=new Vector3(c+s*6.7f,-.7f,z);
            dock.gameObject.AddComponent<InkboundPassableGroup>();
            for(int j=0;j<12;j++)Board(dock,new Vector3(s*1.1f,-.05f,-2.5f+j*.44f),new Vector3(2.6f,.16f,.42f));
            foreach(float dz in new[]{-2.1f,2.1f})foreach(float x in new[]{0f,s*2.1f})
            {
                RoundTimber(dock,new Vector3(x,-1.1f,dz),new Vector3(x,.5f,dz),.13f);
                for(int k=0;k<4;k++)Ring(dock,new Vector3(x,.28f+k*.035f,dz),.15f,.022f,cloth);
            }
            V3Net(dock,new Vector3(s*2,.45f,-2.1f),1.8f,.75f);Basket(dock,new Vector3(s*.9f,.05f,1.3f),.35f,.48f);
            Combine(dock,"FishingDock");
        }
    }
    static void V3Rock(Transform parent,Vector3 at,Vector3 size)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Soswaewon/Meshes/Props/SM_Rock01a.fbx");
        var source=asset.GetComponentInChildren<MeshFilter>().sharedMesh;var mesh=UnityEngine.Object.Instantiate(source);var b=source.bounds;
        mesh.vertices=source.vertices.Select(v=>Vector3.Scale(v-b.center,new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z))+at).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
        var rock=Model("Weathered garden rock",parent,SaveMesh(mesh,"V3GardenRock"),bankStone);rock.AddComponent<InkboundPassableGroup>();
    }
    static void BuildV3Landscape()
    {
        // Species and spacing serve distinct spaces. Willows no longer line every quay at equal intervals.
        foreach(var p in new[]{new Vector3(-9,0,-43),new Vector3(9,0,-8),new Vector3(-11,0,26),new Vector3(10,0,74)})
            Place(v3Plants[p.z>0?8:0],scenery,p,Range(0,360),Range(.87f,1.10f));
        foreach(var p in new[]{new Vector3(-49,0,-20),new Vector3(-39,0,20),new Vector3(19,0,-34),new Vector3(31,0,-17)})
            Place(v3Plants[3],scenery,p,Range(0,360),Range(.85f,1.1f));
        foreach(var p in new[]{new Vector3(-55,0,-17),new Vector3(-32,0,19),new Vector3(34,0,-29)})Place(v3Plants[2],scenery,p,Range(0,360),1);
        Place(v3Plants[1],scenery,new Vector3(-34,0,-22),37,.9f);
        Place(v3Plants[4],scenery,new Vector3(-53,0,21),-43,.92f);
        Place(v3Plants[5],scenery,new Vector3(-38,0,-17),58,.85f);
        for(int i=0;i<20;i++)
        {
            float z=-51+i*6.9f,s=i%2==0?-1:1;Vector3 p=new Vector3(RiverCenter(z)+s*5.75f,-1.05f,z);
            Place(v3Plants[i%3==0?7:6],scenery,p,Range(0,360),Range(.6f,1));
        }
        if(!V3SliceOnly)for(int i=0;i<15;i++)
        {
            int k=new[]{1,3,4,5,2}[i%5];float s=i%2==0?-1:1;
            Place(v3Plants[k],scenery,new Vector3(s*Range(54,74),0,-57+i*9),Range(0,360),Range(.7f,1.12f));
        }
        // Four asymmetrical mountain groups occupy different bearings, with deliberate sky gaps.
        for(int layer=0;layer<4;layer++)for(int group=0;group<(layer==0?2:3);group++)
        {
            var r=Root("V3 brush ridge "+layer+" "+group,scenery);
            float yaw=new[]{-49f,43,119}[group]+layer*17;
            r.position=Quaternion.Euler(0,yaw,0)*new Vector3(0,-9,190+layer*83)+Vector3.forward*15;r.rotation=Quaternion.Euler(0,yaw+180,0);
            var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
            float seed=layer*11.7f+group*2.43f,width=layer==0?180:260;
            for(int z=0;z<=18;z++)for(int x=0;x<=120;x++)
            {
                float u=x/120f,t=z/18f;
                float spine=12+Mathf.PerlinNoise(u*3.4f,seed)*39+Mathf.PerlinNoise(u*8.3f,seed+4)*19;
                spine*=Mathf.SmoothStep(0,1,u*8)*Mathf.SmoothStep(0,1,(1-u)*8);
                spine+=Mathf.PerlinNoise(u*28,seed)*5;
                float shape=Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),.65f+Mathf.PerlinNoise(u*5,seed)*.8f);
                float y=spine*shape+(Mathf.PerlinNoise(u*19+t*2,seed)-.5f)*9*shape;
                v.Add(new Vector3((u-.5f)*width,y,(t-.5f)*92+Mathf.Sin(u*13+seed)*12));uv.Add(new Vector2(u,shape));
                if(x<120&&z<18){int q=z*121+x;tr.AddRange(new[]{q,q+121,q+1,q+1,q+121,q+122});}
            }
            var mesh=new Mesh{name="Asymmetric folded mountain ridges"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();
            var m=V3Material("DistantRidge"+layer,bankStone,.67f+layer*.075f);m.SetFloat("_Pattern",5);m.SetFloat("_FadeNear",160);m.SetFloat("_FadeFar",680);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
            Model("Layered ink mountain",r,SaveMesh(mesh,"V3Mountain"),m);
        }
    }
    static void BuildV3Population()
    {
        foreach(var site in shopSites)
        {
            if(site.transform.position.z>80)continue;
            var a=site.transform;int type=site.type;
            Vector3 keeper=a.TransformPoint(new Vector3(type==1?0:.8f,.18f,type==1?2.85f:-1.7f));
            var vendor=Stationary(type==5?"Fisherman":type==1?"Scholar":"Merchant",shopNames[type]+" keeper",keeper,a.eulerAngles.y+180,type==4?NpcRoutine.Act.Tend:type==0?NpcRoutine.Act.Wipe:type==3?NpcRoutine.Act.Grind:NpcRoutine.Act.Stand);
            if(type==0||type==5||type==6){var task=vendor.AddComponent<InkboundDailyLifeDetails>();task.kind=type==5?InkboundDailyLifeDetails.Kind.RepairNet:InkboundDailyLifeDetails.Kind.ArrangeGoods;task.localTaskPoint=new Vector3(0,.95f,.45f);task.phase=Range(0,1);}
            if(type==4){var steam=Root("Slow kitchen steam",a);steam.localPosition=new Vector3(-1.3f,1.42f,-3.95f);var detail=steam.gameObject.AddComponent<InkboundDailyLifeDetails>();detail.kind=InkboundDailyLifeDetails.Kind.Steam;detail.steamMaterial=steamInk;detail.phase=Range(0,1);}
            if(type==1)
            {
                for(int s=-1;s<=1;s+=2)
                {
                    var guest=Stationary(s<0?"Elder":"Granny","Tea conversation",a.TransformPoint(new Vector3(-2.55f,.18f,-1.1f+s*1.13f)),a.eulerAngles.y+(s<0?0:180),NpcRoutine.Act.Sit,.61f,true);
                    guest.GetComponent<InkboundResidentPhase>().drinkingTea=true;
                    var cup=Root("Guest held tea cup",guest.transform);Cup(cup,Vector3.zero);Combine(cup,"HeldTeaCup");guest.GetComponent<InkboundResidentPhase>().cup=cup;
                }
            }
        }
        for(int j=0;j<12;j++)
        {
            float x=-44+Range(-1.6f,1.6f),z=-16+j*2.6f;string model=new[]{"VillagerWoman","Merchant","ChildGirl","ErrandBoy","Granny","Scholar"}[j%6];
            Resident(model,"Market passing life "+j,new[]{Stop(new Vector3(x,0,z),0,Range(.2f,2)),Stop(new Vector3(x+.4f,0,19),180,1),Stop(new Vector3(x,0,-18),0,1)},Range(.65f,1.12f));
        }
        foreach(int side in new[]{-1,1})for(int j=0;j<7;j++)
        {
            float z=-44+j*17;
            var stops=new List<NpcRoutine.Stop>();
            for(int k=0;k<8;k++){float zz=z+k*3;stops.Add(Stop(new Vector3(RiverCenter(zz)+side*11.4f,0,zz)));}
            for(int k=6;k>0;k--){float zz=z+k*3;stops.Add(Stop(new Vector3(RiverCenter(zz)+side*11.4f,0,zz)));}
            Resident(j%3==0?"ChildGirl":j%2==0?"VillagerWoman":"Scholar","Quay pedestrian "+side+" "+j,stops.ToArray(),Range(.65f,1.15f));
        }
        foreach(float z in new[]{-22f,53})
        {
            var route=new List<NpcRoutine.Stop>();float c=RiverCenter(z);
            for(int j=0;j<=20;j++){float x=c-10+j;route.Add(Stop(new Vector3(x,BridgeHeight(x,z)+.05f,z+.65f)));}
            for(int j=19;j>0;j--){float x=c-10+j;route.Add(Stop(new Vector3(x,BridgeHeight(x,z)+.05f,z+.65f)));}
            Resident("Merchant","Bridge traveller",route.ToArray(),.85f);
        }
        if(!V3SliceOnly)
        {
            foreach(int s in new[]{-1,1})
            {var chess=Stationary(s<0?"Elder":"Scholar","Chess elder",new Vector3(54,.08f,43+s*1.13f),s<0?0:180,NpcRoutine.Act.Sit,.51f,true);var task=chess.AddComponent<InkboundDailyLifeDetails>();task.kind=InkboundDailyLifeDetails.Kind.Chess;task.localTaskPoint=new Vector3(.1f,.91f,.75f);task.phase=s<0?0:.6f;}
            foreach(int s in new[]{-1,1})Stationary("Fisherman","Dock net repair",new Vector3(RiverCenter(s<0?-39:28)+s*7.6f,.08f,s<0?-39:28),s<0?90:-90,NpcRoutine.Act.Crouch);
            Resident("VillagerWoman","Mother on afternoon walk",new[]{Stop(new Vector3(37,0,17),0,2),Stop(new Vector3(37,0,37),180,2)},.72f);
            Resident("ChildGirl","Child beside mother",new[]{Stop(new Vector3(38,0,17.1f),0,2),Stop(new Vector3(38,0,37.1f),180,2)},.72f);
        }
        BuildBoat(-49,0);BuildBoat(16,1);
        foreach(var material in residentPrefabs.Values.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).SelectMany(r=>r.sharedMaterials).Distinct())
        {
            material.SetColor("_BaseColor",Display(.84f,.84f,.82f));material.SetColor("_PaperColor",Display(.957f,.949f,.918f));
            material.SetFloat("_FadeNear",60);material.SetFloat("_FadeFar",220);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        }
    }
    static void BuildV3Encounter()
    {
        RenderSettings.skybox.SetColor("_Tint",Display(.957f,.949f,.918f));EditorUtility.SetDirty(RenderSettings.skybox);AssetDatabase.SaveAssetIfDirty(RenderSettings.skybox);
        RenderSettings.ambientLight=Display(.79f,.79f,.77f);
        Folder("Assets/Resources/InkboundTown");
        const string entryMatPath="Assets/Resources/InkboundTown/M_IT_EntryScroll.mat";
        var entry=AssetDatabase.LoadAssetAtPath<Material>(entryMatPath);
        if(!entry){entry=new Material(Shader.Find("Project/InkboundTown/Entry Scroll"));AssetDatabase.CreateAsset(entry,entryMatPath);}
        entry.shader=Shader.Find("Project/InkboundTown/Entry Scroll");entry.SetColor("_PaperColor",Display(.957f,.949f,.918f));
        EditorUtility.SetDirty(entry);AssetDatabase.SaveAssetIfDirty(entry);
        const string rollerPath="Assets/Resources/InkboundTown/M_IT_EntryRoller.mat";
        var roller=AssetDatabase.LoadAssetAtPath<Material>(rollerPath);if(!roller){roller=new Material(bark);AssetDatabase.CreateAsset(roller,rollerPath);}else EditorUtility.CopySerialized(bark,roller);
        EditorUtility.SetDirty(roller);AssetDatabase.SaveAssetIfDirty(roller);
        var exploration=town.GetComponent<InkboundExploration>();exploration.allowCombat=true;
        town.gameObject.AddComponent<InkboundSkillDisturbance>();
        var motor=exploration.player;motor.GetComponent<PlayerCombat>().enabled=true;
        var contactWash=Special("V3_SoftContactWash","Project/InkboundTown/Returning Pigment");contactWash.SetFloat("_UseFluidMask",0);contactWash.SetFloat("_VertexTint",0);
        EditorUtility.SetDirty(contactWash);AssetDatabase.SaveAssetIfDirty(contactWash);motor.GetComponent<InkboundGroundWash>().inkMaterial=contactWash;
        foreach(var skill in motor.GetComponentsInChildren<MonoBehaviour>(true))
            if(skill&&new[]{"VoidRendSkillDemo","EnergyTetherSkill","PhantomStrike","LeapCleaveStrike","ShieldSlideStrike","SpearArts","PlayerItems"}.Contains(skill.GetType().Name))skill.enabled=true;
        const string elderPath="Assets/_Project/Prefabs/Boss/PF_InkboundTown_Elder.prefab";
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(elderPath);
        if(!prefab)
        {
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Battle.prefab");
            var seed=(GameObject)PrefabUtility.InstantiatePrefab(original);PrefabUtility.UnpackPrefabInstance(seed,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            prefab=PrefabUtility.SaveAsPrefabAsset(seed,elderPath);UnityEngine.Object.DestroyImmediate(seed);
        }
        var elder=(GameObject)PrefabUtility.InstantiatePrefab(prefab);elder.name="V3 wandering elder - continuous city encounter";elder.transform.position=new Vector3(-44,0,16);
        var combat=elder.GetComponent<CalligraphyBossCombat>();combat.externallySequenced=true;
        elder.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
        motor.transform.root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
        var surface=town.gameObject.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.All;surface.useGeometry=UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.overrideTileSize=true;surface.tileSize=128;surface.overrideVoxelSize=true;surface.voxelSize=.13f;
        surface.BuildNavMesh();
        string navPath=Meshes+"/IT_V3_CityNavigation.asset";
        var old=AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath);
        if(old){EditorUtility.CopySerialized(surface.navMeshData,old);surface.RemoveData();surface.navMeshData=old;surface.AddData();EditorUtility.SetDirty(old);AssetDatabase.SaveAssetIfDirty(old);}
        else AssetDatabase.CreateAsset(surface.navMeshData,navPath);
        var encounter=town.gameObject.AddComponent<InkboundTownEncounter>();encounter.elder=combat;encounter.player=motor.GetComponent<PlayerHealth>();
        var agent=elder.AddComponent<UnityEngine.AI.NavMeshAgent>();agent.radius=.42f;agent.height=2;agent.speed=combat.moveSpeed;agent.stoppingDistance=2.8f;agent.angularSpeed=240;
        encounter.agent=agent;
        var body=elder.AddComponent<InkboundDisturbanceSource>();body.radius=.42f;
        if(combat.brush.brushTip){var tip=combat.brush.brushTip.gameObject.AddComponent<InkboundDisturbanceSource>();tip.shape=InkboundDisturbanceSource.Shape.SweptTip;tip.radius=.5f;tip.onlyWhileMoving=true;tip.minimumSpeed=.7f;}
        var blade=motor.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.IndexOf("blade",StringComparison.OrdinalIgnoreCase)>=0||t.name.IndexOf("saber",StringComparison.OrdinalIgnoreCase)>=0);
        if(blade){var sweep=blade.gameObject.AddComponent<InkboundDisturbanceSource>();sweep.shape=InkboundDisturbanceSource.Shape.SweptTip;sweep.onlyWhileMoving=true;sweep.minimumSpeed=1.8f;sweep.radius=.32f;}
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(town.gameObject.scene);
    }
}
#endif
