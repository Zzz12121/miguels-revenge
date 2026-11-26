#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class InkboundTownBuilder
{
    const string ExtraCrowdName="05 Additional daily life groups";

    // Forty additional people, grouped around existing places rather than scattered across the map.
    static void BuildExtraPopulation()
    {
        Transform originalLife=life;var originalRandom=rng;
        var extra=Root(ExtraCrowdName,life);life=extra;rng=new System.Random(9927);
        try
        {
            string[] models={"VillagerWoman","Merchant","Scholar","ErrandBoy","Elder","Fisherman","Granny"};
            float[] starts={-43,-31,-9,7,23,40,66};
            for(int bank=-1;bank<=1;bank+=2)for(int i=0;i<starts.Length;i++)
            {
                float z=starts[i],end=z+12,offset=bank*(i%2==0?10.5f:11.6f);
                Vector3 a=new Vector3(RiverCenter(z)+offset,0,z),b=new Vector3(RiverCenter(end)+offset,0,end);
                if(i%2!=0){var swap=a;a=b;b=swap;}
                Resident(models[(i+(bank>0?2:0))%models.Length],"Additional quay walker "+bank+" "+i,
                    new[]{Stop(a,i%2==0?0:180,Range(.1f,1.2f),NpcRoutine.Act.Stand),Stop(b,i%2==0?0:180,Range(.6f,2),NpcRoutine.Act.Stand)},Range(.72f,1.12f));
            }

            float[] shopperX={-43.3f,-45.2f,-42.7f,-45.7f,-43.5f,-44.5f};
            float[] shopperZ={-12,-6,2,7,12,4};
            for(int i=0;i<6;i++)
            {
                float x=shopperX[i],z=shopperZ[i],yaw=x>-44?90:-90;
                Resident(models[i%models.Length],"Additional browsing customer "+i,
                    new[]{Stop(new Vector3(x,0,z),yaw,Range(3,7),NpcRoutine.Act.Stand),Stop(new Vector3(x,0,z+9),yaw,Range(2,5),NpcRoutine.Act.Stand)},Range(.68f,.92f));
            }

            Vector3[] meetingPlaces={new(-13.7f,0,-32),new(13,0,16),new(34,.08f,17)};
            for(int group=0;group<meetingPlaces.Length;group++)
            {
                Stationary(group==0?"Merchant":"Scholar","Additional conversation "+group+" A",meetingPlaces[group]+Vector3.left*.65f,90,NpcRoutine.Act.Stand,0,true);
                Stationary(group==2?"Granny":"VillagerWoman","Additional conversation "+group+" B",meetingPlaces[group]+Vector3.right*.65f,-90,NpcRoutine.Act.Stand,0,true);
            }

            // Families keep their own small loops; nobody turns toward or avoids the visitor.
            for(int family=0;family<2;family++)
            {
                Vector3 origin=family==0?new Vector3(-13,0,-38):new Vector3(37,.08f,28);
                Vector3 direction=family==0?Vector3.forward*10:Vector3.right*9;
                for(int member=0;member<3;member++)
                {
                    Vector3 side=new Vector3((member-1)*.8f,0,member==2?-.75f:0);
                    Resident(member==0?"VillagerWoman":member==1?"Merchant":"ChildGirl","Additional family "+family+" member "+member,
                        new[]{Stop(origin+side,family==0?0:90,1,NpcRoutine.Act.Stand),Stop(origin+side+direction,family==0?180:-90,1.5f,NpcRoutine.Act.Stand)},.78f);
                }
            }

            var tea=buildings.Find("Riverside open tea house");
            for(int i=0;i<2;i++)
            {
                Vector3 p=tea.TransformPoint(new Vector3(-.1f,.24f,i==0?-.5f:2.1f));
                Stationary(i==0?"Scholar":"VillagerWoman","Additional central tea guest "+i,p,tea.eulerAngles.y+(i==0?0:180),NpcRoutine.Act.Sit,.76f,true);
            }

            foreach(float z in new[]{-22f,53f})
            {
                var route=new List<NpcRoutine.Stop>();float center=RiverCenter(z);
                for(int i=16;i>=0;i--){float x=center-10+i*1.25f;route.Add(Stop(new Vector3(x,BridgeHeight(x,z)+.025f,z-.75f),-90,i==16?1:0));}
                for(int i=1;i<=16;i++){float x=center-10+i*1.25f;route.Add(Stop(new Vector3(x,BridgeHeight(x,z)+.025f,z-.75f),90,0));}
                Resident(z<0?"VillagerWoman":"Merchant","Additional bridge passerby "+z,route.ToArray(),.95f);
            }

            Vector3[] squareLoop={new(33,.08f,21),new(47,.08f,21),new(47,.08f,34),new(33,.08f,34)};
            for(int i=0;i<4;i++)
            {
                var route=new NpcRoutine.Stop[4];
                for(int k=0;k<4;k++)route[k]=Stop(squareLoop[(k+i)%4]+new Vector3(i*.38f,0,i*.25f),k*90,1.3f,NpcRoutine.Act.Stand);
                Resident(models[(i+1)%models.Length],"Additional square stroller "+i,route,Range(.70f,1));
            }
        }
        finally{life=originalLife;rng=originalRandom;}
    }

    public static void ApplyCrowdAndUnletteredSigns()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!=ScenePath || scene.isDirty)throw new InvalidOperationException("Open the saved town in Edit mode first.");
        if(scene.GetRootGameObjects().Any(g=>g.GetComponent<InkboundTownEncounter>()))throw new InvalidOperationException("Use Inkbound Town V3/Rebuild town for the V3 layout and population.");
        var loaded=new Dictionary<string,GameObject>();
        foreach(string name in new[]{"Merchant","VillagerWoman","Fisherman","Scholar","ChildGirl","Elder","Granny","ErrandBoy"})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Residents+"/PF_IT_"+name+".prefab");
            if(!prefab)throw new InvalidOperationException("Missing town resident: "+name);loaded[name]=prefab;
        }
        town=scene.GetRootGameObjects().First(g=>g.GetComponent<InkboundExploration>()).transform;
        buildings=town.Find("02 Inhabited town architecture");life=town.Find("04 Preserved lives and ink echoes");
        if(!buildings || !life)throw new InvalidOperationException("Town hierarchy was not found.");
        residentPrefabs.Clear();foreach(var entry in loaded)residentPrefabs[entry.Key]=entry.Value;
        var previous=life.Find(ExtraCrowdName);if(previous)Undo.DestroyObjectImmediate(previous.gameObject);
        BuildExtraPopulation();Undo.RegisterCreatedObjectUndo(life.Find(ExtraCrowdName).gameObject,"Add town life groups");

        // Update only the town's own prefab assets; shared NPCs and gameplay prefabs stay untouched.
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{Prefabs}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab.GetComponentsInChildren<InkboundSign>(true).Length==0)continue;
            var contents=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var sign in contents.GetComponentsInChildren<InkboundSign>(true))
                {sign.inscription=string.Empty;var text=sign.GetComponent<TextMesh>();if(text)text.text=string.Empty;}
                PrefabUtility.SaveAsPrefabAsset(contents,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(contents);}
        }
        foreach(var sign in town.GetComponentsInChildren<InkboundSign>(true))
        {
            Undo.RecordObject(sign,"Remove sign lettering");sign.inscription=string.Empty;EditorUtility.SetDirty(sign);
            var text=sign.GetComponent<TextMesh>();if(text){Undo.RecordObject(text,"Remove sign lettering");text.text=string.Empty;EditorUtility.SetDirty(text);}
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log("[InkboundTown] Added daily life groups and removed all town sign lettering.");
    }
}
#endif
