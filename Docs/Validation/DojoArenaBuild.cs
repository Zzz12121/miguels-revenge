// Run with: unity command run_script --file Docs/Validation/DojoArenaBuild.cs --entry DojoArenaBuild.Build
// Static architecture only: no player, combat, AI, boss-door or arena gameplay scripts.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class DojoArenaBuild
{
    const string Models = "Assets/_Project/Models/Environment/FinalDojoArena";
    const string Mats = "Assets/_Project/Materials/FinalDojoArena";
    const string Prefabs = "Assets/_Project/Prefabs/Environment/FinalDojoArena";
    public const string ScenePath = "Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity";
    const string Vendor = "Assets/_Project/ThirdParty/Mega Fantasy Props Pack/Textures/";
    static Material wood, floor, dark, stone, paper, plaster, red, brass, lamp, foliage, earth, gravel, matting, ink;
    static Material roofMaterial;
    static Mesh roof;
    static Transform architecture;

    static Transform Group(Transform parent, string name, Vector3 position = default(Vector3))
    {
        var go = new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;return go.transform;
    }
    static GameObject Part(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision=false, PrimitiveType primitive=PrimitiveType.Cube)
    {
        var go=GameObject.CreatePrimitive(primitive);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;
        var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
        if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic=true;return go;
    }
    static void Box(Transform p,string name, float x,float y,float z,float w,float h,float d,Material m,bool collision=false)
        {Part(p,name,new Vector3(x,y,z),new Vector3(w,h,d),m,collision);}
    static void Beam(Transform p,string name,Vector3 a,Vector3 b,float thickness,Material m)
    {
        var go=Part(p,name,(a+b)*.5f,new Vector3(thickness,Vector3.Distance(a,b),thickness),m);
        go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
    }
    static Material MaterialAsset(string name,Color tint,string texture=null,Vector2 tiling=default(Vector2),float smoothness=.18f)
    {
        var path=Mats+"/M_Dojo_"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",tint);m.SetFloat("_Metallic",0);m.SetFloat("_Smoothness",smoothness);m.enableInstancing=true;
        if(texture!=null){m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));m.SetTextureScale("_BaseMap",tiling==Vector2.zero?Vector2.one:tiling);}
        EditorUtility.SetDirty(m);return m;
    }
    static Mesh SaveMesh(Mesh mesh,string name)
    {
        string path=Models+"/"+name+".asset";mesh.name=name;
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);return existing;
    }
    static Mesh LoadRoof()
    {
        using(var reader=new BinaryReader(File.OpenRead(Models+"/MainDojoRoof.geometry.bytes")))
        {
            if(new string(reader.ReadChars(4))!="DJR1")throw new InvalidDataException("Unexpected roof data.");
            int count=reader.ReadInt32(),indexCount=reader.ReadInt32();var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];var indices=new int[indexCount];
            for(int i=0;i<count;i++)v[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            for(int i=0;i<count;i++)n[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            for(int i=0;i<count;i++)uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());
            for(int i=0;i<indexCount;i++)indices[i]=reader.ReadInt32();
            var mesh=new Mesh();mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateTangents();mesh.RecalculateBounds();
            return SaveMesh(mesh,"SM_MainDojoRoof");
        }
    }
    static void Roof(Transform parent,string name,Vector3 position,float scale,bool longAxisX=true)
    {
        var t=Group(parent,name,position);t.localScale=Vector3.one*scale;t.localRotation=Quaternion.Euler(0,longAxisX?90:0,0);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh=roof;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=roofMaterial;t.gameObject.isStatic=true;
        t.gameObject.AddComponent<MeshCollider>().sharedMesh=roof;
    }
    static void Shoji(Transform p,string name,Vector3 position,float width,float height,float yaw=0)
    {
        var g=Group(p,name,position);g.localRotation=Quaternion.Euler(0,yaw,0);
        Box(g,"WoodenLowerPanel",0,.38f,0,width,.76f,.14f,wood,true);
        Box(g,"PaperPanel",0,(height+.76f)*.5f,0,width-.1f,height-.76f,.075f,paper,true);
        foreach(float x in new[]{-width*.5f,width*.5f})Box(g,"DoorStile",x,height*.5f,0,.11f,height,.18f,dark);
        foreach(float y in new[]{.03f,.76f,height-.03f})Box(g,"DoorRail",0,y,0,width,.09f,.18f,dark);
        int cols=Mathf.Max(3,Mathf.RoundToInt(width/.47f));
        for(int i=1;i<cols;i++)Box(g,"PaperLatticeVertical",-width*.5f+width*i/cols,(height+.76f)*.5f,-.065f,.035f,height-.76f,.04f,wood);
        for(float y=1.38f;y<height-.1f;y+=.64f)Box(g,"PaperLatticeHorizontal",0,y,-.065f,width,.038f,.04f,wood);
    }
    static void Column(Transform p,float x,float z,float top=5.9f)
    {
        Box(p,"StoneColumnFoot",x,1.28f,z,.60f,.16f,.60f,stone);
        Box(p,"CedarColumn",x,(top+1.2f)*.5f,z,.36f,top-1.2f,.36f,wood,true);
        Box(p,"CarvedCapital",x,top-.24f,z,.68f,.24f,.60f,wood);
    }
    static void Ceiling(Transform parent,float width,float depth,float height)
    {
        var g=Group(parent,"RemovableCeiling");Box(g,"CeilingBoardLining",0,height,0,width,.16f,depth,floor,true);
        for(float x=-width*.5f;x<=width*.5f+.01f;x+=2.8f)Box(g,"CeilingCofferAcross",x,height-.16f,0,.16f,.23f,depth,dark);
        for(float z=-depth*.5f;z<=depth*.5f+.01f;z+=2.5f)Box(g,"CeilingCofferLength",0,height-.16f,z,width,.23f,.16f,dark);
    }
    static void Steps(Transform p,float width,float floorFront=-13)
    {
        var g=Group(p,"FrontStoneStairs");
        for(int i=0;i<6;i++){float h=(i+1)*.2f;Box(g,"Step_"+(i+1),0,h*.5f,floorFront-2.2f+i*.4f,width,h,.4f,stone,true);}
    }
    static void Lantern(Transform p,Vector3 position,bool crimson=true,float scale=1)
    {
        var g=Group(p,"HangingLantern",position);g.localScale=Vector3.one*scale;
        Beam(g,"HangingCord",new Vector3(0,.5f,0),new Vector3(0,1.1f,0),.035f,dark);
        Part(g,"PaperShade",Vector3.zero,new Vector3(.55f,.77f,.55f),crimson?red:lamp,false,PrimitiveType.Sphere);
        foreach(float y in new[]{-.37f,.37f})Part(g,"LanternCap",new Vector3(0,y,0),new Vector3(.35f,.05f,.35f),dark,false,PrimitiveType.Cylinder);
        for(int i=0;i<9;i++){float y=-.32f+i*.08f;float r=Mathf.Sqrt(Mathf.Max(.01f,1-(y/.42f)*(y/.42f)))*.55f;Part(g,"LanternRib",new Vector3(0,y,0),new Vector3(r,.009f,r),wood,false,PrimitiveType.Cylinder);}
    }
    static void SwordRack(Transform p,Vector3 position)
    {
        var g=Group(p,"ThreeSwordDisplay",position);
        foreach(float x in new[]{-1.1f,1.1f}){Box(g,"StandPost",x,1,0,.14f,2,.16f,dark);Box(g,"StandFoot",x,.08f,0,.5f,.16f,.7f,dark);}
        for(int k=0;k<3;k++)
        {
            float y=.55f+k*.55f;Box(g,"WeaponSupport",0,y,0,2.5f,.1f,.12f,wood);
            Beam(g,"SwordGrip",new Vector3(-1.1f,y+.15f,-.16f),new Vector3(-.55f,y+.15f,-.16f),.11f,dark);
            Box(g,"SwordGuard",-.53f,y+.15f,-.16f,.07f,.27f,.24f,brass);
            Vector3 a=new Vector3(-.48f,y+.15f,-.16f);
            for(int i=1;i<=6;i++){Vector3 b=new Vector3(-.48f+i*.30f,y+.15f+i*i*.0027f,-.16f);Beam(g,"CurvedBlade",a,b,.065f,brass);a=b;}
        }
    }
    static void TeaSet(Transform p,Vector3 position)
    {
        var g=Group(p,"TeaTableAndCushions",position);
        Box(g,"LowTeaTable",0,.59f,0,2.8f,.14f,1.6f,wood,true);
        foreach(float x in new[]{-1.14f,1.14f})foreach(float z in new[]{-.57f,.57f})Box(g,"TableLeg",x,.28f,z,.16f,.56f,.16f,dark);
        Part(g,"TeaPot",new Vector3(.2f,.86f,0),new Vector3(.38f,.36f,.38f),brass,false,PrimitiveType.Sphere);
        for(int i=0;i<3;i++)Part(g,"TeaCup",new Vector3(-.65f+i*.5f,.75f,-.46f),new Vector3(.18f,.065f,.18f),dark,false,PrimitiveType.Cylinder);
        foreach(float x in new[]{-.86f,.86f})foreach(float z in new[]{-1.65f,1.65f})Box(g,"FloorCushion",x,.10f,z,.87f,.18f,.78f,red);
    }
    static void MainBuilding()
    {
        var g=Group(architecture,"MainDojo",new Vector3(0,0,88));
        var body=Group(g,"Structure");
        Box(body,"StoneFoundation",0,.52f,0,36,1.04f,24,stone,true);
        Box(body,"RaisedWoodFloorAndVeranda",0,1.12f,0,38,.16f,26,floor,true);
        Box(body,"FrontVerandaEdge",0,1.08f,-13,38,.32f,.22f,dark);
        Steps(body,8.5f);
        float[] posts={-17,-12.75f,-8.5f,-4.25f,4.25f,8.5f,12.75f,17};
        foreach(float x in posts){Column(body,x,-10);Column(body,x,10);}
        Column(body,0,10);
        foreach(float z in new[]{-5f,-1.9f,1.9f,5f}){Column(body,-17,z);Column(body,17,z);}
        foreach(float x in new[]{-14.875f,-10.625f,-6.375f,6.375f,10.625f,14.875f})Shoji(body,"FrontShoji",new Vector3(x,1.2f,-10),4.10f,4.55f);
        // The central 8.1m doorway is genuinely open, with parked door leaves.
        Box(body,"EntranceLintel",0,5.56f,-10,8.5f,.7f,.35f,wood);
        foreach(float x in new[]{-4.48f,4.48f})Shoji(body,"ParkedEntranceDoor",new Vector3(x,1.2f,-10.21f),.62f,4.2f);
        for(int side=-1;side<=1;side+=2)
        {
            foreach(float z in new[]{-7.5f,7.5f})Shoji(body,"SideShoji",new Vector3(side*17,1.2f,z),4.75f,4.55f,90);
            foreach(float z in new[]{-3.375f,3.375f})Shoji(body,"SideShojiByPortal",new Vector3(side*17,1.2f,z),3.25f,4.55f,90);
            Box(body,"AnnexPortalHeader",side*17,5.62f,0,.30f,.50f,3.50f,wood);
            if(side==-1)for(int i=0;i<4;i++)Shoji(body,"RearShoji",new Vector3(-12.75f+i*8.5f,1.2f,10),8.2f,4.55f);
            if(side==-1){Box(body,"FrontContinuousBeam",0,5.86f,-10,34.4f,.27f,.4f,wood);Box(body,"RearContinuousBeam",0,5.86f,10,34.4f,.27f,.4f,wood);}
            Box(body,"SideContinuousBeam",side*17,5.86f,0,.4f,.27f,20,wood);
            // Side partitions leave an unobstructed 3m portal into each room.
            Shoji(body,"SideRoomPartitionFront",new Vector3(side*11,1.2f,-7.5f),5,3.8f,90);
            Shoji(body,"SideRoomPartitionRear",new Vector3(side*11,1.2f,4),12,3.8f,90);
            Box(body,"SideRoomPortalHeader",side*11,5.05f,-3.5f,.22f,.3f,3,wood);
            foreach(float x in new[]{11.75f,16.25f})Shoji(body,"RearStoragePartition",new Vector3(side*x,1.2f,4),1.5f,3.8f);
            Box(body,"RearStorageDoorHeader",side*14,5.05f,4,3,.3f,.22f,wood);
        }
        foreach(float x in new[]{-7f,7f})Shoji(body,"RearChamberPartition",new Vector3(x,1.2f,4),8,4.0f);
        Box(body,"RearChamberPortalHeader",0,5.3f,4,6,.3f,.2f,wood);
        Box(body,"InnerSanctumDais",0,1.31f,7.25f,9,.22f,4.5f,dark,true);
        // Close the attic wall under the higher roof eaves; this is real geometry,
        // not an opaque plane spanning any doorway or the playable room.
        foreach(float z in new[]{-10f,10f})
        {
            Box(body,"AtticTimberFrieze",0,6.43f,z,34.4f,1.05f,.20f,dark);
            Box(body,"AtticHeaderBeam",0,6.99f,z,34.7f,.19f,.34f,wood);
            for(float x=-16.8f;x<17;x+=.60f)Box(body,"AtticVentBattens",x,6.43f,z-.12f,.065f,.85f,.06f,wood);
        }
        foreach(float x in new[]{-17f,17f})
        {
            Box(body,"SideAtticFrieze",x,6.43f,0,.20f,1.05f,20,dark);
            Box(body,"SideAtticHeader",x,6.99f,0,.34f,.19f,20,wood);
        }
        var props=Group(g,"InteriorFurnishings");
        SwordRack(props,new Vector3(-14.0f,1.2f,6.8f));SwordRack(props,new Vector3(-14.0f,1.2f,-7.8f));
        for(int i=0;i<3;i++)Box(props,"TatamiMat",14,1.215f,-5+i*3.7f,5.6f,.025f,3.5f,matting);
        TeaSet(props,new Vector3(14,1.24f,2));
        foreach(int side in new[]{-1,1})for(int i=0;i<2;i++)Box(props,"RearStorageChest",side*(12.5f+i*2.5f),1.73f,8.4f,1.8f,1.06f,1.0f,wood,true);
        foreach(float x in new[]{-6f,6f}){Box(props,"SanctumScrollBacking",x,3.6f,9.85f,1.7f,3.0f,.05f,paper);Box(props,"ScrollInkStroke",x,3.6f,9.81f,.17f,1.6f,.03f,ink);}
        for(int i=0;i<5;i++)Lantern(props,new Vector3(-13.6f+i*6.8f,4.85f,-11.7f),true,.9f);
        for(int i=0;i<3;i++)Lantern(props,new Vector3(-7+i*7,4.5f,0),false,.9f);
        Ceiling(g,34,20,5.90f);
        var removable=Group(g,"RemovableRoofs");Roof(removable,"UserApprovedTripoRoof",new Vector3(0,5.95f,0),40);
        Bake(body,"MainStructure");Bake(props,"MainFurnishings");Bake(g.Find("RemovableCeiling"),"MainCeiling");
        PrefabUtility.SaveAsPrefabAsset(g.gameObject,Prefabs+"/PF_MainDojo_Complete.prefab");
    }
    static void Annex(int side)
    {
        var g=Group(architecture,side<0?"WestWeaponsAnnex":"EastTeaAnnex",new Vector3(side*27,0,88));var body=Group(g,"Structure");
        Box(body,"Foundation",0,.52f,0,15,1.04f,11,stone,true);Box(body,"VerandaFloor",0,1.12f,0,16,.16f,12,floor,true);
        Steps(body,4.5f,-6);
        foreach(float x in new[]{-7f,7f})foreach(float z in new[]{-4.5f,4.5f})Column(body,x,z,5.1f);
        foreach(float x in new[]{-4.6f,4.6f})Shoji(body,"FrontShoji",new Vector3(x,1.2f,-4.5f),4.4f,3.8f);
        Box(body,"AnnexFrontHeader",0,4.94f,-4.5f,14,.26f,.3f,wood);
        foreach(float z in new[]{-4.5f,4.5f})Box(body,"AnnexAtticTimber",0,5.42f,z,14.2f,.64f,.18f,dark);
        foreach(float x in new[]{-7f,7f})Box(body,"AnnexAtticTimberSide",x,5.42f,0,.18f,.64f,9,dark);
        Shoji(body,"RearShoji",new Vector3(0,1.2f,4.5f),14,3.8f);
        Shoji(body,"OuterSideShoji",new Vector3(side*7,1.2f,0),9,3.8f,90);
        Shoji(body,"InnerSideFront",new Vector3(-side*7,1.2f,-3.15f),2.7f,3.8f,90);
        Shoji(body,"InnerSideRear",new Vector3(-side*7,1.2f,3.15f),2.7f,3.8f,90);
        // Raised connector to the main veranda, with no wall across the passage.
        Box(body,"MainBuildingBridge",-side*9,1.12f,0,4,.16f,3.4f,floor,true);
        var props=Group(g,"Furnishings");
        if(side<0){SwordRack(props,new Vector3(-3,1.2f,2.8f));SwordRack(props,new Vector3(3,1.2f,2.8f));}
        else{Box(props,"Tatami",0,1.22f,0,12,.03f,7,matting);TeaSet(props,new Vector3(1.5f,1.24f,1.6f));}
        foreach(float x in new[]{-5f,0,5f})Lantern(props,new Vector3(x,4.28f,-5.0f),true,.75f);
        Ceiling(g,14,9,5.06f);var roofs=Group(g,"RemovableRoofs");Roof(roofs,"AnnexRoof",new Vector3(0,5.12f,0),18);
        Bake(body,side<0?"WestAnnex":"EastAnnex");Bake(props,side<0?"WestProps":"EastProps");Bake(g.Find("RemovableCeiling"),side<0?"WestCeiling":"EastCeiling");
    }
    static void Corridors()
    {
        var g=Group(architecture,"CoveredSideWalkways");var body=Group(g,"Structure");var roofs=Group(g,"RemovableRoofs");
        foreach(int side in new[]{-1,1})
        {
            float x=side*39;
            Box(body,"WalkwayStoneBase",x,.21f,51,5.7f,.42f,65,stone,true);Box(body,"WalkwayTimberDeck",x,.49f,51,5.2f,.14f,65,floor,true);
            // A shallow ramp at each end joins the deck without an invisible step.
            for(int i=0;i<3;i++)Box(body,"WalkwayEntryStep",x,(i+1)*.09f,17.6f+i*.38f,5.3f,(i+1)*.18f,.38f,stone,true);
            for(float z=20;z<=80;z+=5)
            {
                foreach(int edge in new[]{-1,1}){Box(body,"WalkwayColumn",x+edge*2.35f,2.11f,z,.25f,3.10f,.25f,wood,true);Box(body,"WalkwayCapital",x+edge*2.35f,3.57f,z,.55f,.22f,.55f,wood);}
                Box(body,"WalkwayCrossBeam",x,3.63f,z,5.1f,.25f,.28f,wood);
            }
            foreach(int edge in new[]{-1,1})Box(body,"WalkwayLengthBeam",x+edge*2.35f,3.64f,50,.30f,.26f,60,wood);
            for(int i=0;i<7;i++)Roof(roofs,"WalkwayRoof_"+side+"_"+i,new Vector3(x,3.75f,21+i*9),9.8f,false);
            Roof(roofs,"WalkwayNorthRoof_"+side,new Vector3(x,3.75f,82),9.8f,false);
            for(int i=0;i<6;i++)Lantern(body,new Vector3(x,2.9f,24+i*10),true,.62f);
            Box(body,"AnnexBridge",side*36.5f,.91f,81.5f,7.0f,.58f,3.0f,stone,true);
            for(int i=0;i<3;i++){float top=.56f+(i+1)*(.64f/3);Box(body,"AnnexBridgeSteps",x,(top+.56f)*.5f,79.2f+i*.38f,5.2f,top-.56f,.38f,stone,true);}
        }
        Bake(body,"Walkways");
    }
    static void CompoundBoundary()
    {
        var g=Group(architecture,"OuterWallsAndGate");
        foreach(int side in new[]{-1,1})
        {
            Box(g,"WallStoneBase",side*48,1.0f,60,1.1f,2,110,stone,true);Box(g,"WhitePlasterWall",side*48,3.0f,60,.75f,2.0f,110,plaster,true);
            Box(g,"DarkWallCoping",side*48,4.08f,60,1.25f,.22f,110,dark);
            Box(g,"EntranceWallBase",side*28,1,5,40,2,1.1f,stone,true);Box(g,"EntranceWallPlaster",side*28,3,5,40,2,.75f,plaster,true);Box(g,"EntranceWallCoping",side*28,4.08f,5,40,.22f,1.25f,dark);
        }
        Box(g,"RearWallBase",0,1,115,97,2,1.1f,stone,true);Box(g,"RearWallPlaster",0,3,115,97,2,.75f,plaster,true);Box(g,"RearWallCoping",0,4.08f,115,97,.22f,1.25f,dark);
        foreach(float x in new[]{-6f,6f}){Box(g,"GateStoneFoot",x,1.0f,5,1.2f,2,1.2f,stone,true);Box(g,"GateTimberPost",x,3.1f,5,.6f,6.2f,.6f,dark,true);}
        Box(g,"GateLintel",0,6.1f,5,13,.55f,.6f,wood);
        var roofs=Group(g,"RemovableRoofs");Roof(roofs,"EntranceGateRoof",new Vector3(0,6.3f,5),17);
        for(int side=-1;side<=1;side+=2)
        {
            var door=Group(g,"OpenGateLeaf",new Vector3(side*5.6f,0,5));door.localRotation=Quaternion.Euler(0,side*76,0);
            Box(door,"HeavyTimberGate",-side*2.6f,2.65f,0,5.2f,5.3f,.22f,wood,true);
            foreach(float y in new[]{.9f,4.4f})Box(door,"IronGateStrap",-side*2.6f,y,-.14f,5,.18f,.055f,dark);
        }
        var torii=Group(g,"VermilionTorii",new Vector3(0,0,15));
        foreach(float x in new[]{-5.5f,5.5f}){Part(torii,"ToriiPost",new Vector3(x,3.6f,0),new Vector3(.68f,3.6f,.68f),red,true,PrimitiveType.Cylinder);Box(torii,"ToriiStonePlinth",x,.25f,0,1,.5f,1,stone,true);}
        Box(torii,"ToriiLowerBeam",0,5.6f,0,13.5f,.40f,.48f,red);Box(torii,"ToriiUpperBeam",0,7.05f,0,15,.48f,.7f,red);
        Beam(torii,"ToriiCurvedCopingLeft",new Vector3(-7.6f,7.62f,0),new Vector3(0,7.27f,0),.43f,dark);
        Beam(torii,"ToriiCurvedCopingRight",new Vector3(0,7.27f,0),new Vector3(7.6f,7.62f,0),.43f,dark);
        Box(torii,"ToriiCentreBlock",0,6.35f,0,.35f,1.0f,.45f,red);
        Bake(g,"CompoundBoundary");
    }
    static void StoneLantern(Transform parent,Vector3 position)
    {
        var g=Group(parent,"StoneGardenLantern",position);
        Box(g,"LanternPlinth",0,.10f,0,1.05f,.20f,1.05f,stone);Box(g,"LanternFoot",0,.27f,0,.8f,.16f,.8f,stone);
        Part(g,"StoneShaft",new Vector3(0,.95f,0),new Vector3(.30f,.62f,.30f),stone,false,PrimitiveType.Cylinder);
        Box(g,"LanternHousingBase",0,1.58f,0,.85f,.16f,.85f,stone);
        foreach(float x in new[]{-.32f,.32f})foreach(float z in new[]{-.32f,.32f})Box(g,"LanternCorner",x,1.94f,z,.10f,.6f,.10f,stone);
        Box(g,"WarmLanternCore",0,1.91f,0,.25f,.38f,.25f,lamp);
        Part(g,"PagodaCap",new Vector3(0,2.31f,0),new Vector3(1.2f,.26f,1.2f),stone,false,PrimitiveType.Sphere);
        Part(g,"StoneFinial",new Vector3(0,2.53f,0),new Vector3(.2f,.25f,.2f),stone,false,PrimitiveType.Sphere);
    }
    static void Pine(Transform parent,Vector3 pos,float scale=1)
    {
        var p=Group(parent,"GardenPine",pos);p.localScale=Vector3.one*scale;
        Beam(p,"PineTrunk",Vector3.zero,new Vector3(.25f,3.8f,.12f),.22f,wood);
        for(int i=0;i<7;i++)
        {
            float angle=i*2.4f;Vector3 tip=new Vector3(Mathf.Cos(angle)*1.25f,1.7f+i*.37f,Mathf.Sin(angle)*.8f);
            Beam(p,"PineBranch",new Vector3(.12f,tip.y-.35f,0),tip,.09f,wood);
            Part(p,"PineNeedleCanopy",tip,new Vector3(1.8f-i*.13f,.38f,1.30f-i*.07f),foliage,false,PrimitiveType.Sphere);
        }
    }
    static void Garden()
    {
        var g=Group(architecture,"CourtyardAndGarden");
        Box(g,"FlatCourtyardGround",0,-.15f,60,98,.3f,122,gravel,true);
        // Continuous flat fight area: x +/-26, z19..71. All props remain outside it.
        foreach(int side in new[]{-1,1})
        {
            Box(g,"GardenBorder",side*28.2f,.045f,45,.22f,.09f,56,stone);
            foreach(float z in new[]{24f,38f,52f,66f})StoneLantern(g,new Vector3(side*30,.0f,z));
            foreach(float z in new[]{29f,57f,75f})Pine(g,new Vector3(side*33,0,z),1.15f);
            Pine(g,new Vector3(side*24,1.2f,81),.48f);
        }
        var rng=new System.Random(82);
        for(int i=0;i<20;i++)
        {
            int side=i%2==0?-1:1;float z=22+(float)rng.NextDouble()*53;
            var rock=Part(g,"GardenStone",new Vector3(side*(31+(float)rng.NextDouble()*3),.28f,z),new Vector3(1.0f+(float)rng.NextDouble()*1.5f,.65f,1.1f),stone,false,PrimitiveType.Sphere);
            rock.transform.localRotation=Quaternion.Euler(0,i*37,0);
        }
        for(int side=-1;side<=1;side+=2)Box(g,"CentralApproachPavingEdge",side*4.4f,.012f,13,.15f,.024f,16,stone);
        Bake(g,"CourtyardGarden");
    }
    static void Bake(Transform group,string name)
    {
        var filters=group.GetComponentsInChildren<MeshFilter>().Where(f=>!f.transform.GetComponentsInParent<Transform>().Any(t=>t.name=="RemovableRoofs") && f.GetComponent<MeshRenderer>()!=null).ToArray();
        var byMaterial=filters.GroupBy(f=>f.GetComponent<MeshRenderer>().sharedMaterial).ToArray();
        var visual=Group(group,"BakedVisuals");
        foreach(var entry in byMaterial)
        {
            if(entry.Key==null)continue;
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};
            var combine=entry.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=group.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray();
            mesh.CombineMeshes(combine,true,true);mesh.RecalculateBounds();
            var asset=SaveMesh(mesh,"SM_"+name+"_"+entry.Key.name);var go=Group(visual,entry.Key.name);
            go.gameObject.AddComponent<MeshFilter>().sharedMesh=asset;go.gameObject.AddComponent<MeshRenderer>().sharedMaterial=entry.Key;go.gameObject.isStatic=true;
        }
        foreach(var filter in filters){Object.DestroyImmediate(filter.GetComponent<MeshRenderer>());Object.DestroyImmediate(filter);}
    }
    static void Lighting()
    {
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.62f,.69f,.79f);RenderSettings.ambientEquatorColor=new Color(.44f,.46f,.48f);RenderSettings.ambientGroundColor=new Color(.28f,.25f,.21f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Exponential;RenderSettings.fogDensity=.0019f;RenderSettings.fogColor=new Color(.54f,.62f,.70f);
        var sky=MaterialAsset("PreviewSky",Color.white);sky.shader=Shader.Find("Skybox/Procedural");sky.SetFloat("_SunSize",.045f);sky.SetColor("_SkyTint",new Color(.58f,.62f,.68f));sky.SetColor("_GroundColor",new Color(.38f,.33f,.28f));sky.SetFloat("_Exposure",1.05f);RenderSettings.skybox=sky;
        var preview=Group(null,"PreviewLightingAndCameras");
        var sun=Group(preview,"LateAfternoonSun");sun.localRotation=Quaternion.Euler(37,-34,0);var light=sun.gameObject.AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.86f,.68f);light.intensity=2.1f;light.shadows=LightShadows.Soft;light.shadowStrength=.75f;RenderSettings.sun=light;
        var fill=Group(preview,"CoolSkyFill");fill.localRotation=Quaternion.Euler(42,125,0);var fl=fill.gameObject.AddComponent<Light>();fl.type=LightType.Directional;fl.intensity=.38f;fl.color=new Color(.68f,.79f,1);fl.shadows=LightShadows.None;
        foreach(float x in new[]{-6f,6f})
        {
            var t=Group(preview,"WarmHallLight",new Vector3(x,4.9f,86));var l=t.gameObject.AddComponent<Light>();l.type=LightType.Point;l.range=15;l.intensity=5;l.color=new Color(1,.76f,.43f);l.shadows=LightShadows.None;
        }
        var cam=Group(preview,"Main Camera").gameObject.AddComponent<Camera>();cam.tag="MainCamera";cam.transform.position=new Vector3(64,32,38);cam.transform.LookAt(new Vector3(0,5.5f,87));cam.fieldOfView=48;cam.nearClipPlane=.15f;cam.farClipPlane=350;cam.allowHDR=true;cam.backgroundColor=new Color(.5f,.6f,.7f);cam.clearFlags=CameraClearFlags.Skybox;
        cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        cam.gameObject.AddComponent<AudioListener>();
        cam.gameObject.AddComponent<DojoArchitectureTour>();
    }
    public static string Build()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before building architecture.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("An open scene has unsaved changes; preserving it.");
        foreach(string directory in new[]{Models,Mats,Prefabs})Directory.CreateDirectory(directory);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var ti=(TextureImporter)AssetImporter.GetAtPath(Models+"/T_MainDojoRoof_BaseColor.jpg");ti.maxTextureSize=4096;ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.sRGBTexture=true;ti.SaveAndReimport();
        roof=LoadRoof();
        roofMaterial=MaterialAsset("TripoRoof",Color.white,Models+"/T_MainDojoRoof_BaseColor.jpg");roofMaterial.SetFloat("_Cull",0);
        wood=MaterialAsset("CedarTimber",new Color(.48f,.30f,.18f),Vendor+"wooden-boards-texture_d.png",new Vector2(1,2));
        floor=MaterialAsset("Floorboards",new Color(.72f,.56f,.39f),Vendor+"wooden-boards-texture_d.png",new Vector2(9,6),.26f);
        dark=MaterialAsset("DarkJoinery",new Color(.16f,.095f,.055f),Vendor+"wooden-boards-texture_d.png");
        stone=MaterialAsset("FoundationStone",new Color(.57f,.55f,.50f),Vendor+"stone_wall_2_d.png",new Vector2(8,2));
        paper=MaterialAsset("ShojiPaper",new Color(.88f,.82f,.67f));plaster=MaterialAsset("Plaster",new Color(.81f,.79f,.70f));
        red=MaterialAsset("Vermilion",new Color(.49f,.065f,.028f),null,default(Vector2),.23f);
        brass=MaterialAsset("Bronze",new Color(.39f,.29f,.15f),null,default(Vector2),.65f);brass.SetFloat("_Metallic",.65f);
        lamp=MaterialAsset("WarmPaperLight",new Color(1,.88f,.58f));lamp.EnableKeyword("_EMISSION");lamp.SetColor("_EmissionColor",new Color(1,.57f,.18f)*1.4f);
        foliage=MaterialAsset("PineNeedles",new Color(.12f,.19f,.09f));earth=MaterialAsset("Earth",new Color(.24f,.20f,.14f));
        gravel=MaterialAsset("CourtyardSand",new Color(.56f,.49f,.38f));matting=MaterialAsset("Tatami",new Color(.53f,.51f,.30f));ink=MaterialAsset("ScrollInk",new Color(.025f,.02f,.018f));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=Group(null,"FinalDojoArena");architecture=Group(root,"Architecture");
        MainBuilding();Annex(-1);Annex(1);Corridors();CompoundBoundary();Garden();Lighting();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
        PrefabUtility.SaveAsPrefabAsset(root.gameObject,Prefabs+"/PF_FinalDojoArena_Architecture.prefab");
        // Retire only this task's first-pass numeric bake assets after both prefabs
        // and the scene have been rebuilt with stable group/material mesh names.
        foreach(string guid in AssetDatabase.FindAssets("t:Mesh",new[]{Models}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            if(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path),@"^SM_(MainStructure|MainFurnishings|MainCeiling|WestAnnex|EastAnnex|WestProps|EastProps|WestCeiling|EastCeiling|Walkways|CompoundBoundary|CourtyardGarden)_\d+$"))AssetDatabase.DeleteAsset(path);
        }
        Selection.activeGameObject=architecture.Find("MainDojo").gameObject;
        var summary=new {scene=ScenePath,mainPrefab=Prefabs+"/PF_MainDojo_Complete.prefab",compoundPrefab=Prefabs+"/PF_FinalDojoArena_Architecture.prefab",renderers=root.GetComponentsInChildren<MeshRenderer>().Length,triangles=root.GetComponentsInChildren<MeshFilter>().Sum(f=>(long)f.sharedMesh.triangles.Length/3),colliders=root.GetComponentsInChildren<Collider>().Length,mainRoofWidth=40,clearFightArea="52 x 52m",rooms="main hall, rear chamber, left weapons room, right tea room, west/east annexes"};
        Directory.CreateDirectory("output/dojo-building");File.WriteAllText("output/dojo-building/build-summary.json",JsonUtility.ToJson(new Summary{scene=ScenePath,renderers=summary.renderers,triangles=summary.triangles,colliders=summary.colliders},true));
        return Newtonsoft.Json.JsonConvert.SerializeObject(summary);
    }
    [Serializable]class Summary{public string scene;public int renderers;public long triangles;public int colliders;}
    static void Snapshot(Camera cam,string name,Vector3 from,Vector3 target,bool ortho=false,float size=40)
    {
        cam.transform.position=from;cam.transform.LookAt(target);cam.orthographic=ortho;cam.orthographicSize=size;
        var rt=RenderTexture.GetTemporary(1800,1125,24,RenderTextureFormat.ARGB32);cam.targetTexture=rt;
        var previous=RenderTexture.active;
        try{cam.Render();RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes("output/dojo-building/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);}
        finally{cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
    }
    public static string Capture()
    {
        var cam=Camera.main;var root=GameObject.Find("FinalDojoArena");if(cam==null||root==null)throw new Exception("Open the dojo architecture scene first.");
        Directory.CreateDirectory("output/dojo-building");
        Vector3 saved=cam.transform.position;Quaternion rotation=cam.transform.rotation;bool ortho=cam.orthographic;
        string savedQualityPipelinePath=AssetDatabase.GetAssetPath(QualitySettings.renderPipeline);
        var pipeline=QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
        var capturePipeline=Object.Instantiate((UniversalRenderPipelineAsset)pipeline);capturePipeline.shadowDistance=240;capturePipeline.shadowCascadeCount=4;
        // Scoped render-only override. The project pipeline asset/settings are restored below.
        QualitySettings.renderPipeline=capturePipeline;
        var disabled=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="RemovableRoofs"||t.name=="RemovableCeiling").Select(t=>t.gameObject).ToArray();
        try
        {
            Snapshot(cam,"01-main-building-exterior",new Vector3(62,28,39),new Vector3(0,6,87));
            Snapshot(cam,"02-complete-courtyard",new Vector3(112,83,-24),new Vector3(0,2,60),true,67);
            Snapshot(cam,"03-main-hall-interior",new Vector3(0,3.25f,78.7f),new Vector3(0,3.0f,96));
            foreach(var go in disabled)go.SetActive(false);Snapshot(cam,"04-building-cutaway",new Vector3(48,52,46),new Vector3(0,1.3f,88),true,31);
            foreach(var go in disabled)go.SetActive(true);
            Snapshot(cam,"05-courtyard-eye-level",new Vector3(0,2.0f,40),new Vector3(0,6.0f,88));
        }
        finally{foreach(var go in disabled)go.SetActive(true);cam.transform.SetPositionAndRotation(saved,rotation);cam.orthographic=ortho;QualitySettings.renderPipeline=string.IsNullOrEmpty(savedQualityPipelinePath)?null:AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(savedQualityPipelinePath);Object.DestroyImmediate(capturePipeline);}
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "Captured 5 actual Unity camera renders in output/dojo-building; project render pipeline restored.";
    }
    public static string Package()
    {
        var arena=GameObject.Find("FinalDojoArena");if(arena==null)throw new Exception("Open the dojo preview scene first.");
        var building=new GameObject("DojoBuildings_Complete");
        Vector3 origin=arena.transform.Find("Architecture/MainDojo").position;
        try
        {
            foreach(string name in new[]{"MainDojo","WestWeaponsAnnex","EastTeaAnnex"})
            {
                var source=arena.transform.Find("Architecture/"+name);
                var clone=Object.Instantiate(source.gameObject,building.transform);clone.name=name;
                clone.transform.localPosition=source.position-origin;clone.transform.localRotation=source.rotation;clone.transform.localScale=source.lossyScale;
            }
            PrefabUtility.SaveAsPrefabAsset(building,Prefabs+"/PF_DojoBuildings_Complete.prefab");
        }
        finally{Object.DestroyImmediate(building);}
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        var entries=AssetDatabase.FindAssets("t:Prefab",new[]{Prefabs}).Select(AssetDatabase.GUIDToAssetPath).Concat(new[]{ScenePath}).ToArray();
        var dependencies=AssetDatabase.GetDependencies(entries,true).Where(path=>path.StartsWith("Assets/",StringComparison.Ordinal)&&!AssetDatabase.IsValidFolder(path)).Distinct().ToArray();
        // URP is the receiving project's package dependency, not writable content
        // in this handoff. Never bundle PackageCache shader or C# files.
        UnityEditor.AssetPackage.Package.Export(new UnityEditor.AssetPackage.ExportPackageParameters(dependencies,"output/dojo-building/FinalDojoArena_Architecture.unitypackage",null,ExportPackageOptions.Default));
        return "Saved combined main-house/annex prefab and standalone Unity architecture package.";
    }
}
