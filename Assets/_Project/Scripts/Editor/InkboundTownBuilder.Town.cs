#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class InkboundTownBuilder
{
    static void BuildTerrain()
    {
        for(int side=-1;side<=1;side+=2)
        {
            var verts=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            for(int i=0;i<=52;i++)
            {
                float z=-78+i*3.2f,edge=RiverCenter(z)+side*6.2f;
                verts.Add(new Vector3(edge,0,z));verts.Add(new Vector3(side*82,0,z));
                uv.Add(new Vector2(edge,z));uv.Add(new Vector2(side*82,z));
                if(i<52){int q=i*2;indices.AddRange(side<0?new[]{q,q+1,q+2,q+1,q+3,q+2}:new[]{q,q+2,q+1,q+1,q+2,q+3});}
            }
            var mesh=new Mesh{name="Riverbank land strip"};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();
            Model(side<0?"West bank — solid ground":"East bank — solid ground",streets,mesh,earth,true);
            var quay=Root("Masonry quay "+side,streets);
            for(int i=0;i<52;i++)
            {
                float z=-78+i*3.2f;Vector3 a=new Vector3(RiverCenter(z)+side*6.2f,-.72f,z),b=new Vector3(RiverCenter(z+3.2f)+side*6.2f,-.72f,z+3.2f);
                var wall=Box("Quay wall",quay,(a+b)*.5f,new Vector3(.35f,1.55f,Vector3.Distance(a,b)+.06f),stone,true);
                wall.transform.localRotation=Quaternion.LookRotation(b-a);
                Object.DestroyImmediate(wall.GetComponent<MeshRenderer>());Object.DestroyImmediate(wall.GetComponent<MeshFilter>());
                var cap=Root("Uneven coping stones",quay);cap.localPosition=(a+b)*.5f+Vector3.up*.84f;cap.localRotation=wall.transform.localRotation;
                WornStone(cap,new Vector3(0,0,-.8f),new Vector3(.68f,.19f,1.65f),stone);
                WornStone(cap,new Vector3(.025f,-.015f,.8f),new Vector3(.63f,.18f,1.56f),stone);
                var paving=Box("Waterside stone street",quay,(a+b)*.5f+new Vector3(side*3.2f,.745f,0),new Vector3(6,.05f,Vector3.Distance(a,b)+.07f),stone);
                paving.transform.localRotation=wall.transform.localRotation;
            }
            for(int course=0;course<3;course++)
            {
                float z=-78-Range(0,1.2f);
                while(z<88)
                {
                    float length=Range(.75f,2.1f),middle=z+length*.5f;
                    WornStone(quay,new Vector3(RiverCenter(middle)+side*6.17f,-.23f-course*.46f,middle),new Vector3(Range(.43f,.58f),Range(.40f,.46f),length-.025f),course==2?bankStone:stone);
                    z+=length;
                }
            }
            Combine(quay,"Quay");
        }
        var lanes=Root("Market lanes connecting the quays",streets);
        foreach(float z in new[]{-22f,15.5f,53f})Box("Cross-town stone lane",lanes,new Vector3(-43,.025f,z),new Vector3(64,.05f,z==15.5f?4.2f:5.3f),stone);
        Box("Market pedestrian street",lanes,new Vector3(-44,.025f,4),new Vector3(6,.05f,53),stone);
        Box("East lane to square",lanes,new Vector3(30,.025f,5),new Vector3(31,.05f,5.8f),stone);
        Combine(lanes,"TownLanes");
        var waterVerts=new List<Vector3>();var waterUV=new List<Vector2>();var waterTriangles=new List<int>();
        for(int i=0;i<=64;i++)
        {
            float z=-88+i*3.05f,x=RiverCenter(z);
            waterVerts.Add(new Vector3(x-6.3f,-1.25f,z));waterVerts.Add(new Vector3(x+6.3f,-1.25f,z));
            waterUV.Add(new Vector2(0,i));waterUV.Add(new Vector2(1,i));
            if(i<64){int a=i*2;waterTriangles.AddRange(new[]{a,a+1,a+2,a+1,a+3,a+2});}
        }
        var waterMesh=new Mesh{name="Meandering paper river"};waterMesh.SetVertices(waterVerts);waterMesh.SetUVs(0,waterUV);waterMesh.SetTriangles(waterTriangles,0);waterMesh.RecalculateNormals();
        var water=Model("River — reflected ink",streets,SaveMesh(waterMesh,"River"),river);water.layer=4;
        water.AddComponent<InkboundWaterReflection>();
        foreach(float z in new[]{-41f,28f,70f})for(int side=-1;side<=1;side+=2)
        {
            var stair=Root("River landing stone steps",streets);
            for(int step=0;step<6;step++)
            {
                float y=-1.25f+step*.23f,x=RiverCenter(z)+side*(4.6f+step*.29f);
                Box("Landing step",stair,new Vector3(x,y-.17f,z),new Vector3(.65f,.34f,3.2f),stone,true);
            }
            Combine(stair,"WaterSteps");
        }
        for(int side=-1;side<=1;side+=2)
        {
            var border=Root("Quiet outer edge "+side,streets);
            foreach(var position in new[]{new Vector3(side*79,4,0),new Vector3(0,4,side<0?-75:86)})
            {
                var go=new GameObject("Exploration boundary");go.transform.SetParent(border,false);go.transform.localPosition=position;
                go.AddComponent<BoxCollider>().size=position.x!=0?new Vector3(1,12,164):new Vector3(164,12,1);
            }
        }
    }
    static void BuildBridges()
    {
        foreach(float z in new[]{-22f,53f})
        {
            float center=RiverCenter(z);var bridge=Root("Stone arch bridge "+z,streets);bridge.localPosition=new Vector3(center,0,z);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int i=0;i<32;i++)
            {
                float a=-10+i*20f/32,b=-10+(i+1)*20f/32;
                float ya=2.5f*Mathf.Cos(Mathf.Abs(a)/10*Mathf.PI*.5f),yb=2.5f*Mathf.Cos(Mathf.Abs(b)/10*Mathf.PI*.5f);
                AddQuad(vertices,uv,triangles,new Vector3(a,ya,-2.6f),new Vector3(a,ya,2.6f),new Vector3(b,yb,2.6f),new Vector3(b,yb,-2.6f));
                for(int side=-1;side<=1;side+=2)
                    AddQuad(vertices,uv,triangles,new Vector3(a,ya,side*2.6f),new Vector3(b,yb,side*2.6f),new Vector3(b,yb-.95f,side*2.6f),new Vector3(a,ya-.95f,side*2.6f));
            }
            var mesh=new Mesh{name="Walkable stone arch"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            Model("Arch deck with clear waterway",bridge,mesh,stone,true);
            for(int side=-1;side<=1;side+=2)for(int piece=0;piece<28;piece++)
            {
                float x=-9.8f+piece*.72f,y=2.5f*Mathf.Cos(Mathf.Abs(x)/10*Mathf.PI*.5f);
                WornStone(bridge,new Vector3(x,y-.44f,side*2.65f),new Vector3(.70f,.86f,.22f),bankStone);
            }
            foreach(int side in new[]{-1,1})Box("Arch abutment",bridge,new Vector3(side*8.8f,-.37f,0),new Vector3(2.1f,1.75f,5.6f),bankStone);
            for(int side=-1;side<=1;side+=2)for(int p=0;p<10;p++)
            {
                float x=-9.4f+p*18.8f/9,y=2.5f*Mathf.Cos(Mathf.Abs(x)/10*Mathf.PI*.5f);
                Box("Bridge parapet post",bridge,new Vector3(x,y+.55f,side*2.45f),new Vector3(.26f,1.1f,.32f),stone,true);
                if(p<9)
                {
                    float nx=x+18.8f/9,ny=2.5f*Mathf.Cos(Mathf.Abs(nx)/10*Mathf.PI*.5f);
                    Beam("Stone parapet rail",bridge,new Vector3(x,y+.8f,side*2.45f),new Vector3(nx,ny+.8f,side*2.45f),.25f,stone,true);
                    Beam("Parapet ink cap",bridge,new Vector3(x,y+.95f,side*2.45f),new Vector3(nx,ny+.95f,side*2.45f),.055f,ink);
                }
            }
            Combine(bridge,"ArchBridge");
        }
    }
    static void AddQuad(List<Vector3> vertices,List<Vector2> uv,List<int> triangles,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
    {
        int q=vertices.Count;vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});
        triangles.AddRange(new[]{q,q+1,q+2,q,q+2,q+3});
    }
    static void BuildTownBlocks()
    {
        // Two irregular waterfront streets, linked by bridges and the market lane.
        var placements=new[]{
            new Vector4(-24,-43,0,0),new Vector4(-34,-41,-4,1),new Vector4(-47,-39,5,2),
            new Vector4(-24,-8,90,2),new Vector4(-24,8,90,1),new Vector4(-25,24,90,3),
            new Vector4(-27,40,90,0),new Vector4(-28,67,90,2),new Vector4(-42,71,180,1),
            new Vector4(-55,62,174,3),new Vector4(-56,36,90,2),new Vector4(-59,18,94,0),
            new Vector4(-58,-6,88,1),new Vector4(-45,-29,0,0),new Vector4(-60,-29,8,2),
            new Vector4(24,-44,0,2),new Vector4(38,-43,-5,1),new Vector4(54,-43,4,3),
            new Vector4(24,-5,-90,3),new Vector4(55,-21,0,0),new Vector4(64,-7,-88,1),
            new Vector4(65,16,-90,2),new Vector4(65,40,-87,3),new Vector4(59,65,180,2),
            new Vector4(44,70,180,1),new Vector4(24,71,180,3),new Vector4(22,54,-90,0)
        };
        foreach(var spec in placements)
        {
            int variant=(int)spec.w;float yaw=spec.z;
            if(Mathf.Abs(yaw)>60 && Mathf.Abs(yaw)<120)yaw=-yaw;
            else if(spec.y>60 && Mathf.Abs(yaw)>150)yaw=0;
            float x=spec.x;
            if(Mathf.Abs(x)<29 && spec.y>-15 && spec.y<73)x-=Mathf.Sign(x)*Range(2,4);
            var house=Place(housePrefabs[variant],buildings,new Vector3(x,0,spec.y+Range(-.6f,.6f)),yaw+Range(-2.5f,2.5f),Range(.93f,1.07f));
            house.name="River dwelling "+spec.x+" "+spec.y;
        }
        // Pale roof silhouettes beyond the accessible streets retain true parallax.
        for(int i=0;i<14;i++)
        {
            float x=-72+i*10.6f,z=Range(94,110);
            Place(housePrefabs[i%4],buildings,new Vector3(x,Range(-.2f,1.5f),z),Range(-12,12),Range(.8f,1.12f));
        }
    }
    static void BuildEntrance()
    {
        var entrance=Root("Entering the completed painting",streets);entrance.localPosition=new Vector3(-14,0,-52);
        for(int i=0;i<4;i++)Box("Broad worn stone entry",entrance,new Vector3(0,.04f-i*.02f,-3-i*.9f),new Vector3(9,.12f,1),stone,true);
        for(int side=-1;side<=1;side+=2)
        {
            Box("Paifang stone pillar",entrance,new Vector3(side*4.2f,2.7f,0),new Vector3(.65f,5.4f,.65f),stone,true);
            Box("Pillar foot",entrance,new Vector3(side*4.2f,.4f,0),new Vector3(1.1f,.8f,1.1f),stone,true);
            if(side<0)
            {
                Box("Side garden wall",entrance,new Vector3(side*10,1.5f,0),new Vector3(10.9f,3,.4f),plaster,true);
                Box("Wall ink coping",entrance,new Vector3(side*10,3.05f,0),new Vector3(11.1f,.19f,.65f),roof);
            }
        }
        Box("Paifang crossbeam",entrance,new Vector3(0,4.7f,0),new Vector3(9.2f,.5f,.7f),timber,true);
        Roof(entrance,10.8f,2.7f,5.15f,.8f);
        Box("Town name tablet",entrance,new Vector3(0,4.45f,-.44f),new Vector3(3.6f,.95f,.12f),paper);
        Combine(entrance,"Entrance");
        Place(pinePrefab,scenery,new Vector3(-27,0,-61),23,.9f);
        Place(bambooPrefab,scenery,new Vector3(-3,0,-56),-20,.8f);
        var road=Root("Stone road from the painting threshold",streets);
        for(int i=0;i<10;i++)Box("Entry paving inset",road,new Vector3(-14,.025f,-70+i*2.3f),new Vector3(5,.05f,2.24f),stone);
        Combine(road,"EntryRoad");
    }
    static void BuildMarket()
    {
        var market=Root("West market — cloth ceramics and baskets",buildings);
        for(int i=0;i<6;i++)
        {
            var stall=Root("Preserved stall "+i,market);
            stall.localPosition=new Vector3(i<3?-40:-49,0,-9+(i%3)*10);stall.localRotation=Quaternion.Euler(0,i<3?90:-90,0);
            for(int side=-1;side<=1;side+=2)Beam("Stall timber upright",stall,new Vector3(side*1.65f,0,0),new Vector3(side*1.65f,2.8f,0),.11f,timber);
            ClothCanopy(stall,new Vector3(0,2.9f,-.7f),3.8f,2.8f);
            Box("Market counter",stall,new Vector3(0,.9f,-.1f),new Vector3(3.3f,.13f,1.15f),timber);
            for(int p=0;p<4;p++)Box("Counter leg",stall,new Vector3((p%2==0?-1:1)*1.35f,.45f,p<2?-.5f:.35f),new Vector3(.1f,.9f,.1f),timber);
            for(int p=0;p<5;p++)
            {
                Vector3 at=new Vector3(-1.25f+p*.58f,1,-.2f);
                Pot(stall,at,Range(.15f,.26f),Range(.3f,.55f),i%2==0?ceramic:stone);
            }
            Box("Hanging woven cloth",stall,new Vector3(-1.15f,1.9f,.65f),new Vector3(.8f,1.1f,.035f),cloth);
            Basket(stall,new Vector3(1.15f,0,-1.05f),.36f,.65f,i%3==2);
            Box("Paper merchant tablet",stall,new Vector3(0,2.2f,-1.7f),new Vector3(.95f,.55f,.04f),paper);
            Beam("Sign suspension",stall,new Vector3(0,2.5f,-1.7f),new Vector3(0,2.75f,-1.7f),.025f,timber);
            Combine(stall,"MarketStall");Sign(stall,i%3==0?"布莊":i%3==1?"陶器":"山貨",new Vector3(0,2.2f,-1.74f),0,.095f);
        }
        var goods=Root("Ink goods beside the market",life);goods.localPosition=new Vector3(-37,0,15);
        for(int i=0;i<5;i++)Pot(goods,new Vector3((i%3)*.55f,0,(i/3)*.65f),.25f,.55f,ceramic);
        Combine(goods,"EchoGoods");goods.gameObject.AddComponent<InkboundEcho>().plumeMaterial=plume;
    }
    static void Pot(Transform parent,Vector3 position,float radius,float height,Material material)
    {
        bool unit=BeginPassableUnit(ref parent,"Complete clay vessel");
        int sides=28,rings=12;var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        float[] profile={.5f,.64f,.78f,.90f,.98f,1,.99f,.92f,.8f,.62f,.59f,.64f};
        for(int r=0;r<rings;r++)for(int k=0;k<=sides;k++)
        {
            float a=k*Mathf.PI*2/sides;verts.Add(position+new Vector3(Mathf.Cos(a)*radius*profile[r],r*height/(rings-1),Mathf.Sin(a)*radius*profile[r]));uv.Add(new Vector2(k/(float)sides,r/(float)(rings-1)));
            if(r<rings-1 && k<sides){int q=r*(sides+1)+k;tris.AddRange(new[]{q,q+sides+1,q+1,q+1,q+sides+1,q+sides+2});}
        }
        var mesh=new Mesh{name="Hand-thrown river town vessel"};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();Model("Clay vessel",parent,mesh,material);
        EndPassableUnit(parent,unit);
    }
    static void BuildTeaHouse()
    {
        var tea=Root("Riverside open tea house",buildings);tea.localPosition=new Vector3(28,0,-24);tea.localRotation=Quaternion.Euler(0,90,0);
        Box("Tea floor",tea,new Vector3(0,.12f,0),new Vector3(12,.24f,9),stone,true);
        Box("Tea back wall",tea,new Vector3(0,2.1f,4.4f),new Vector3(12,4.2f,.25f),plaster,true);
        foreach(int side in new[]{-1,1})
        {
            Box("Tea side wall",tea,new Vector3(side*5.9f,2.1f,1),new Vector3(.2f,4.2f,6.7f),plaster,true);
            for(int i=0;i<3;i++)Box("Tea veranda post",tea,new Vector3(side*5.4f,2.1f,-3.9f+i*3.8f),new Vector3(.24f,4.2f,.24f),timber,true);
        }
        Roof(tea,14.2f,11,4.2f,1.6f);
        for(int t=0;t<3;t++)Table(tea,new Vector3(-3.8f+t*3.7f,.24f,.8f),true);
        Box("Tea shop counter",tea,new Vector3(0,.85f,3.3f),new Vector3(6,1.25f,.9f),timber);
        for(int i=0;i<4;i++)Pot(tea,new Vector3(-2+i*1.3f,1.5f,3.3f),.16f,.28f,ceramic);
        Box("Tea sign board",tea,new Vector3(0,3.6f,-4.9f),new Vector3(3.3f,.85f,.12f),paper);
        Combine(tea,"TeaHouse");Sign(tea,"聽雨茶肆",new Vector3(0,3.6f,-5),0,.12f);
    }
    static void Table(Transform parent,Vector3 position,bool stools)
    {
        Box("Tea table top",parent,position+Vector3.up*.85f,new Vector3(1.8f,.12f,1.3f),timber);
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box("Table leg",parent,position+new Vector3(x*.7f,.42f,z*.45f),new Vector3(.1f,.84f,.1f),timber);
        for(int i=0;i<3;i++)Pot(parent,position+new Vector3((i-1)*.35f,.91f,0),i==1?.13f:.07f,i==1?.21f:.11f,ceramic);
        if(stools)foreach(int side in new[]{-1,1})
        {
            Box("Low wooden stool",parent,position+new Vector3(0,.46f,side*1.3f),new Vector3(.85f,.12f,.56f),timber);
            for(int x=-1;x<=1;x+=2)Box("Stool legs",parent,position+new Vector3(x*.29f,.21f,side*1.3f),new Vector3(.09f,.42f,.38f),timber);
        }
    }
    static void BuildPlaza()
    {
        var plaza=Root("Old tree square — open exploration space",streets);
        Box("Forty metre stone square",plaza,new Vector3(41,.04f,29),new Vector3(40,.08f,38),stone,true);
        Place(pinePrefab,scenery,new Vector3(57,0,45),-30,1.45f);
        var furniture=Root("Chess tables and resting benches",plaza);
        Table(furniture,new Vector3(54,.08f,43),true);
        Box("Chess board",furniture,new Vector3(54,.995f,43),new Vector3(.75f,.025f,.75f),paper);
        for(int i=0;i<9;i++)
        {
            Box("Chess grid horizontal",furniture,new Vector3(54,1.012f,42.67f+i*.082f),new Vector3(.68f,.008f,.006f),timber);
            Box("Chess grid vertical",furniture,new Vector3(53.67f+i*.082f,1.012f,43),new Vector3(.006f,.008f,.68f),timber);
        }
        for(int i=0;i<13;i++)Pot(furniture,new Vector3(53.75f+(i%5)*.12f,1.02f,42.8f+(i/5)*.16f),.035f,.03f,i%2==0?ink:paper);
        for(int i=0;i<3;i++)
        {
            Box("Square bench",furniture,new Vector3(59,.53f,17+i*7),new Vector3(.65f,.15f,2.8f),timber);
            Box("Bench stone foot",furniture,new Vector3(59,.25f,16.2f+i*7),new Vector3(.5f,.5f,.35f),stone);
            Box("Bench stone foot",furniture,new Vector3(59,.25f,17.8f+i*7),new Vector3(.5f,.5f,.35f),stone);
        }
        Combine(furniture,"SquareFurniture");
    }
}
#endif
