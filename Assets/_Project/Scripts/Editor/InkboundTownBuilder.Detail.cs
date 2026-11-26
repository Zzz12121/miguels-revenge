#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static partial class InkboundTownBuilder
{
    static float RoofY(float u,float v,float height,float rise)
    {
        float s=Mathf.Abs(v-.5f)*2,end=Mathf.Abs(u-.5f)*2;
        return height+rise*Mathf.Pow(1-s,1.32f)+Mathf.Pow(s,7)*.38f+Mathf.Pow(end,7)*(.18f+s*.23f);
    }
    static void SculptRoof(Transform parent,float width,float depth,float height,float rise)
    {
        if(!forceSimpleRoof && width>6 && depth>5 && DetailedVillageRoof(parent,width,depth,height,rise))return;
        const int nx=20,nz=28;var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
        {
            float u=x/(float)nx,t=z/(float)nz;
            v.Add(new Vector3((u-.5f)*width,RoofY(u,t,height,rise),(t-.5f)*depth));uv.Add(new Vector2(u*width,t*depth));
            if(x<nx && z<nz){int a=z*(nx+1)+x;tr.AddRange(new[]{a,a+nx+1,a+1,a+1,a+nx+1,a+nx+2});}
        }
        // A real turned edge and dark soffit give the roof thickness from below and across the river.
        for(int edge=0;edge<4;edge++)for(int k=0;k<20;k++)
        {
            float a=k/20f,b=(k+1)/20f;
            Vector2 s=edge<2?new Vector2(a,edge):new Vector2(edge-2,a),t=edge<2?new Vector2(b,edge):new Vector2(edge-2,b);
            Vector3 p=new Vector3((s.x-.5f)*width,RoofY(s.x,s.y,height,rise),(s.y-.5f)*depth);
            Vector3 q=new Vector3((t.x-.5f)*width,RoofY(t.x,t.y,height,rise),(t.y-.5f)*depth);
            AddQuad(v,uv,tr,p,q,q-Vector3.up*.19f,p-Vector3.up*.19f);
        }
        var mesh=new Mesh{name="Concave roof with swept corners and thick eaves"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();
        Model("Concave tiled roof",parent,mesh,roof);
        var contours=new BrushGeometry();
        contours.Tube(new Vector3(-width*.5f,height+rise+.34f,0),new Vector3(-width*.25f,height+rise-.04f,0),new Vector3(width*.25f,height+rise-.04f,0),new Vector3(width*.5f,height+rise+.34f,0),.12f,.1f,20,7);
        for(int side=-1;side<=1;side+=2)
        {
            float z=side*depth*.5f;
            contours.Tube(new Vector3(-width*.5f,RoofY(0,0,height,rise)-.08f,z),new Vector3(-width*.2f,height+.27f,z),new Vector3(width*.2f,height+.27f,z),new Vector3(width*.5f,RoofY(1,0,height,rise)-.08f,z),.095f,.085f,20,6);
            int caps=Mathf.CeilToInt(width/.40f);
            for(int k=0;k<caps;k++)
            {
                float u=(k+.4f)/caps,x=(u-.5f)*width,y=RoofY(u,side<0?0:1,height,rise);
                contours.Tube(new Vector3(x,y+.055f,z-side*.48f),new Vector3(x,y+.035f,z-side*.28f),new Vector3(x,y+.05f,z),new Vector3(x,y+.04f,z+side*.06f),.065f,.09f,4,6);
            }
        }
        Model("Rounded ridge and broken tile caps",parent,contours.Mesh("Roof ridge and eave caps"),timber);
    }
    static bool DetailedVillageRoof(Transform parent,float width,float depth,float height,float rise)
    {
        const string path="Assets/Soswaewon/Meshes/Buildings/SM_JWD_Roof01a.fbx";
        var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!asset)return false;
        var filter=asset.GetComponentInChildren<MeshFilter>();if(!filter || !filter.sharedMesh)return false;
        Bounds b=filter.sharedMesh.bounds;
        // The source spans both slopes across X and half the ridge length along +Z.
        // Rotate the two ridge modules so the assembled ridge follows the house's X axis.
        for(int side=-1;side<=1;side+=2)
        {
            var slope=UnityEngine.Object.Instantiate(asset,parent);slope.name="Detailed reusable tiled roof slope";
            slope.transform.localRotation=Quaternion.Euler(0,side*90,0);
            Vector3 scale=new Vector3(depth/b.size.x,rise/b.size.y,width*.5f/b.size.z);
            slope.transform.localScale=scale;
            slope.transform.localPosition=new Vector3(side*.012f,height-b.min.y*scale.y,0);
            foreach(var renderer in slope.GetComponentsInChildren<Renderer>())
            {
                var mats=renderer.sharedMaterials;for(int k=0;k<mats.Length;k++)mats[k]=roof;renderer.sharedMaterials=mats;
            }
            foreach(var collider in slope.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(collider);
        }
        return true;
    }
    static void WornStone(Transform parent,Vector3 at,Vector3 size,Material material)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        Vector2[] outline={new(-.5f,-.32f),new(-.32f,-.5f),new(.34f,-.5f),new(.5f,-.28f),new(.5f,.31f),new(.30f,.5f),new(-.34f,.5f),new(-.5f,.3f)};
        float[] offset=new float[8];for(int i=0;i<8;i++)offset[i]=Range(.94f,1.04f);
        for(int ring=0;ring<3;ring++)for(int k=0;k<8;k++)
        {
            float scale=ring==1?1:.92f,y=ring==0?-size.y*.5f:ring==1?size.y*.3f:size.y*.5f;
            v.Add(at+new Vector3(outline[k].x*size.x*scale*offset[k],y,outline[k].y*size.z*scale*offset[k]));uv.Add(outline[k]);
            if(ring<2){int q=ring*8+k,n=ring*8+(k+1)%8;tr.AddRange(new[]{q,n,q+8,n,n+8,q+8});}
        }
        v.Add(at+Vector3.up*size.y*.5f);uv.Add(Vector2.one*.5f);
        for(int k=0;k<8;k++)tr.AddRange(new[]{24,16+(k+1)%8,16+k});
        var mesh=new Mesh{name="Worn eight corner stone"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();
        Model("Chipped layered masonry",parent,mesh,material);
    }
    static void HangingCloth(Transform parent,Vector3 top,float width,float length)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int row=0;row<=10;row++)for(int col=0;col<=6;col++)
        {
            float u=col/6f,t=row/10f;
            v.Add(top+new Vector3((u-.5f)*width,-t*length+Mathf.Pow(t,9)*Mathf.Sin(u*19)*.07f,Mathf.Sin(u*15)*.055f*t));uv.Add(new Vector2(u,1-t));
            if(row<10 && col<6){int q=row*7+col;tr.AddRange(new[]{q,q+7,q+1,q+1,q+7,q+8});}
        }
        var mesh=new Mesh{name="Hanging wrinkled linen with uneven hem"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();
        Model("Hanging cloth",parent,SaveMesh(mesh,"HangingCloth"),cloth);
        Beam("Banner crossbar",parent,top-new Vector3(width*.62f,0,0),top+new Vector3(width*.62f,0,0),.055f,timber);
    }
    static void ClothCanopy(Transform parent,Vector3 at,float width,float depth)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        const int nx=12,nz=8;
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
        {
            float u=x/(float)nx,t=z/(float)nz;
            float sag=-Mathf.Sin(u*Mathf.PI)*.16f-Mathf.Sin(t*Mathf.PI)*.16f+Mathf.Sin(u*24)*.035f;
            v.Add(at+new Vector3((u-.5f)*width,sag-t*.28f,(t-.5f)*depth));uv.Add(new Vector2(u,t));
            if(x<nx && z<nz){int q=z*(nx+1)+x;tr.AddRange(new[]{q,q+nx+1,q+1,q+1,q+nx+1,q+nx+2});}
        }
        var mesh=new Mesh{name="Sagging pleated market canopy"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();Model("Soft linen awning",parent,mesh,cloth);
    }
    static void Lantern(Transform parent,Vector3 at,float radius)
    {
        var body=Root("Unlit ribbed paper lantern",parent);body.localPosition=at;
        Pot(body,new Vector3(0,-radius*.75f,0),radius,radius*1.5f,paper);
        var ribs=new BrushGeometry();
        for(int k=0;k<8;k++)
        {
            float a=k*Mathf.PI*.25f;Vector3 r=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
            ribs.Tube(r*.56f-Vector3.up*radius*.7f,r-Vector3.up*radius*.32f,r+Vector3.up*radius*.32f,r*.56f+Vector3.up*radius*.7f,.009f,.009f,8,4);
        }
        Model("Lantern ribs",body,ribs.Mesh("Linen lantern bamboo ribs"),timber);
        Beam("Lantern tassel",body,-Vector3.up*radius*.75f,-Vector3.up*radius*1.4f,.025f,timber);
        Beam("Lantern hanger",body,Vector3.up*radius*.75f,Vector3.up*(radius*1.4f),.018f,timber);
        Combine(body,"Lantern");body.gameObject.AddComponent<InkboundHangingMotion>().phase=Range(0,1);
    }
    static void Basket(Transform parent,Vector3 at,float radius,float height,bool fruit=false)
    {
        Pot(parent,at,radius,height,cloth);var weave=new BrushGeometry();
        for(int k=0;k<7;k++)
        {
            float y=height*(.12f+k*.13f),r=radius*(.62f+Mathf.Sin(k/7f*Mathf.PI)*.34f);
            for(int a=0;a<4;a++)
            {
                float t=a*Mathf.PI*.5f;Vector3 p=new Vector3(Mathf.Cos(t),0,Mathf.Sin(t)),q=new Vector3(-Mathf.Sin(t),0,Mathf.Cos(t));
                weave.Tube(at+p*r+Vector3.up*y,at+(p+q*.55f)*r+Vector3.up*y,at+(p*.55f+q)*r+Vector3.up*y,at+q*r+Vector3.up*y,.012f,.012f,5,4);
            }
        }
        Model("Broken wicker weave",parent,weave.Mesh("Basket horizontal weave"),timber);
        if(fruit)for(int k=0;k<9;k++)
        {
            Vector3 p=at+new Vector3(Mathf.Sin(k*2.4f)*radius*.55f,height*.8f+(k%3)*.06f,Mathf.Cos(k*2.4f)*radius*.55f);
            Pot(parent,p,.10f,.16f,k%3==0?stone:ceramic);
        }
    }
    static void RiversideLifeSets()
    {
        for(int side=-1;side<=1;side+=2)for(int i=0;i<3;i++)
        {
            float z=-36+i*36+side*2;var set=Root("Waterfront living vignette "+side+" "+i,buildings);
            set.localPosition=new Vector3(RiverCenter(z)+side*12.8f,0,z);set.localRotation=Quaternion.Euler(0,side<0?-90:90,0);
            if(i==0 || i==2)
            {
                ClothCanopy(set,new Vector3(0,3,-.3f),3.5f,2.6f);
                for(int s=-1;s<=1;s+=2)Beam("Awning support",set,new Vector3(s*1.6f,0,.7f),new Vector3(s*1.6f,3.1f,.7f),.085f,timber);
                Box("Produce table",set,new Vector3(0,.85f,0),new Vector3(2.6f,.13f,.85f),timber);
                for(int s=-1;s<=1;s+=2)Box("Table trestle",set,new Vector3(s,.42f,0),new Vector3(.12f,.84f,.65f),timber);
                Basket(set,new Vector3(-.6f,.93f,0),.29f,.32f,true);Basket(set,new Vector3(.65f,.93f,0),.27f,.29f,true);
                Basket(set,new Vector3(1.6f,0,-.3f),.38f,.68f);Lantern(set,new Vector3(-1.4f,2.3f,-.7f),.24f);
            }
            else {Table(set,Vector3.zero,true);Basket(set,new Vector3(1.7f,0,.4f),.34f,.55f);}
            Combine(set,"RiversideVignette");
            if(i==0 || i==2)Stationary(i==0?"Granny":"Merchant","Riverside produce vendor "+side+" "+i,set.TransformPoint(new Vector3(0,0,.8f)),set.eulerAngles.y+180,NpcRoutine.Act.Wipe);
            else
            {
                Stationary("Scholar","Riverside tea conversation "+side,set.TransformPoint(new Vector3(0,0,-1.3f)),set.eulerAngles.y,NpcRoutine.Act.Sit,.52f,true);
                Stationary("Elder","Riverside tea elder "+side,set.TransformPoint(new Vector3(0,0,1.3f)),set.eulerAngles.y+180,NpcRoutine.Act.Sit,.52f,true);
            }
        }
        var fishing=Root("Quiet fishing landing",buildings);fishing.localPosition=new Vector3(RiverCenter(-40)-6.8f,0,-40);
        Basket(fishing,new Vector3(-1,0,.6f),.3f,.48f);var strokes=new BrushGeometry();
        strokes.Tube(new Vector3(-.8f,1,.1f),new Vector3(.2f,1.8f,.2f),new Vector3(1.6f,2,.3f),new Vector3(2.4f,1.5f,.4f),.035f,.008f,20,6);
        strokes.Tube(new Vector3(2.4f,1.5f,.4f),new Vector3(2.5f,.8f,.4f),new Vector3(2.3f,-.3f,.4f),new Vector3(2.1f,-1.2f,.4f),.005f,.003f,12,4);
        Model("Curved fishing rod and line",fishing,strokes.Mesh("Fishing tackle"),timber);Combine(fishing,"FishingLanding");
        Stationary("Fisherman","Fisherman resting at landing",fishing.TransformPoint(new Vector3(-.9f,0,0)),90,NpcRoutine.Act.Sit,.5f);
        var moorings=Root("Mooring posts ropes and sparse bank plants",scenery);var ropes=new BrushGeometry();
        foreach(float z in new[]{-61f,-36f,8f,35f,70f})for(int side=-1;side<=1;side+=2)
        {
            Vector3 a=new Vector3(RiverCenter(z)+side*6.8f,.03f,z),b=new Vector3(RiverCenter(z+3.3f)+side*6.8f,.03f,z+3.3f);
            ropes.Tube(a,a+Vector3.up*.3f,a+Vector3.up*.5f,a+Vector3.up*.75f,.06f,.055f,5,6);
            ropes.Tube(b,b+Vector3.up*.3f,b+Vector3.up*.5f,b+Vector3.up*.75f,.06f,.055f,5,6);
            ropes.Tube(a+Vector3.up*.6f,a+new Vector3(0,.18f,1),b+new Vector3(0,.18f,-1),b+Vector3.up*.6f,.025f,.022f,15,5);
        }
        Model("Slack mooring ropes",moorings,ropes.Mesh("Riverside ropes and posts"),timber);Combine(moorings,"Moorings");
    }
}
#endif
