using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

public static class DojoEncounterPresentationInstaller
{
    const string Dir="Assets/Piloto Studio/Super Realistic FX Bundle 02/Realistic Ying Yang/";
    static GameObject Fx(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>(Dir+name+".prefab")??throw new Exception("Missing FX "+name);
    public static string Configure(DojoOpeningCutscene cut)
    {
        if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
        if(!cut)throw new Exception("Open dojo architecture preview first");
        var p=Object.FindAnyObjectByType<PlayerHealth>();if(!p.GetComponent<DojoTestMode>())p.gameObject.AddComponent<DojoTestMode>();
        cut.explorationFirst=true;cut.explorationSeconds=5;cut.explorationStop=new Vector3(0,0,36);EditorUtility.SetDirty(cut);
        cut.scenicWalkInput=.42f;cut.hiddenRoadProgress=.85f;cut.scenicHeadYaw=65;cut.scenicHeadLift=38;
        foreach(var shot in cut.shots)
        {
            if(shot.name.Contains("走出茶室")){shot.startMeters=0;shot.transform.position=new Vector3(6.8f,2.6f,83.1f);shot.dollySpeed=0;shot.fov=54;}
            if(shot.name.Contains("露臺停步")){shot.startMeters=9.5f;shot.transform.position=new Vector3(3,2.6f,71.2f);shot.transform.rotation=Quaternion.LookRotation(new Vector3(1.2f,2.2f,77.2f)-shot.transform.position);shot.dollySpeed=0;shot.fov=52;}
            EditorUtility.SetDirty(shot);
        }
        var encounter=cut.master.GetComponent<DojoCalligraphyEncounter>();encounter.escapeAtHalfHealth=true;
        encounter.brushAssemblyMaterial=EnsureMaterial("BrushInkAssembly","Project/Dojo Brush Ink Assembly");encounter.brushAssemblySeconds=.8f;
        encounter.meleeShockwave=Fx("Ying Yang Burst Shockwave");EditorUtility.SetDirty(encounter);
        var escape=cut.master.GetComponent<DojoElderEscape>();if(!escape)escape=cut.master.gameObject.AddComponent<DojoElderEscape>();
        escape.casting=Fx("Extras/ARPG_White__Casting_Hand_attach");escape.embers=Fx("Extras/ARPG_White__Embers");
        escape.nova=Fx("Extras/ARPG_White__Hit_Nova_Heavy");escape.muzzle=Fx("Extras/ARPG_White__Projectile_Muzzle_Flash");
        var route=cut.transform.Find("HalfHealthEscapeRoute");if(!route){route=new GameObject("HalfHealthEscapeRoute").transform;route.SetParent(cut.transform,false);}
        var torii=Object.FindObjectsByType<Transform>().FirstOrDefault(t=>t.name=="WoodenTorii");
        Vector3 throughTorii=torii?torii.position+torii.forward*4+Vector3.up*1.3f:new Vector3(65.3f,2.4f,127.4f);
        var points=new[]{new Vector3(0,2.5f,79.5f),new Vector3(0,2.5f,84.5f),new Vector3(12,2.5f,84.5f),new Vector3(14,2.5f,88),new Vector3(19.1f,2.5f,88),new Vector3(19.1f,2.5f,101.7f),new Vector3(47,1.45f,123.3f),new Vector3(52,1.45f,123.3f),throughTorii};
        var names=new[]{"01 Main entrance","02 Hall to open partition","03 Tea-room portal","04 Side door alignment","05 Exit through side door","06 Clear house corner","07 Rear moon gate direct over river","08 Outside gate","09 Beyond first torii"};
        escape.flightPath=new Transform[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            string name=i<names.Length?names[i]:"Mountain bend "+i;
            var point=route.Find(name);if(!point){point=new GameObject(name).transform;point.SetParent(route,false);}
            var at=points[i];
            // Match DojoMountainPathBuilder.PathPts floor heights; raycasts would land on torii lintels.
            point.position=at;escape.flightPath[i]=point;
        }
        var beyond=route.Find("Continue beyond torii during camera return");
        if(!beyond){beyond=new GameObject("Continue beyond torii during camera return").transform;beyond.SetParent(route,false);}
        beyond.position=new Vector3(74,3.6f,132);escape.departureBeyondTorii=beyond;
        EditorUtility.SetDirty(escape);EditorSceneManager.MarkSceneDirty(cut.gameObject.scene);
        ConfigureVeil();
        DojoGateFit.Fit(cut.gate);
        return "Configured scenic walk, hidden 85% road position, moving elder and obstacle-checked flight through first torii";
    }
    static void ConfigureVeil()
    {
        var veil=Object.FindAnyObjectByType<DojoMountainVeil>();
        if(!veil)veil=new GameObject("Exterior mountain mist veil").AddComponent<DojoMountainVeil>();
        veil.minimum=new Vector3(48.5f,-3,40);veil.maximum=new Vector3(268.5f,98,260);
        if(!veil.volume)
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Bounded exterior mist";Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(veil.transform,false);veil.volume=cube.GetComponent<Renderer>();
        }
        veil.volume.transform.position=Vector3.zero;veil.volume.transform.localScale=Vector3.one;
        veil.volume.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;veil.volume.receiveShadows=false;
        var material=EnsureMaterial("ExteriorMountainVeil","Project/Dojo Exterior Mountain Veil");
        material.SetFloat("_Density",.84f);material.SetColor("_MistColor",new Color(.72f,.78f,.81f,.82f));veil.volume.sharedMaterial=material;
        var terrain=Object.FindObjectsByType<Terrain>().FirstOrDefault(t=>t.transform.position.x>40);
        if(!terrain)throw new Exception("Missing mountain terrain for surface mist");
        const int resolution=100,layers=3;int row=resolution+1,verticesPerLayer=row*row;
        var vertices=new System.Collections.Generic.List<Vector3>();var colors=new System.Collections.Generic.List<Color>();var triangles=new System.Collections.Generic.List<int>();
        for(int layer=0;layer<layers;layer++)for(int z=0;z<=resolution;z++)for(int x=0;x<=resolution;x++)
        {
            float u=x/(float)resolution,v=z/(float)resolution;
            var at=terrain.transform.position+new Vector3(u*terrain.terrainData.size.x,0,v*terrain.terrainData.size.z);
            at.y=terrain.SampleHeight(at)+terrain.transform.position.y;
            Vector3 normal=terrain.terrainData.GetInterpolatedNormal(u,v);
            at+=normal*(.35f+layer*.4f);vertices.Add(at);
            float border=Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v));colors.Add(new Color(1,1,1,Mathf.SmoothStep(0,1,border/.025f)*Mathf.Pow(.5f,layer)));
            if(x<resolution&&z<resolution)
            {int n=layer*verticesPerLayer+z*row+x;triangles.AddRange(new[]{n,n+row,n+1,n+1,n+row,n+row+1});}
        }
        string meshPath="Assets/_Project/Prefabs/DojoMountain/Meshes/Mesh_DM_ExteriorMistShell.asset";
        var generated=new Mesh{name="Mesh_DM_ExteriorMistShell",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};generated.SetVertices(vertices);generated.SetColors(colors);generated.SetTriangles(triangles,0);generated.RecalculateBounds();
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(saved){EditorUtility.CopySerialized(generated,saved);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);}else{AssetDatabase.CreateAsset(generated,meshPath);saved=generated;}
        veil.volume.GetComponent<MeshFilter>().sharedMesh=saved;veil.volume.name="Mist following exterior terrain";
        foreach(var camera in Object.FindObjectsByType<Camera>())
        {var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(data){data.requiresDepthTexture=true;EditorUtility.SetDirty(data);}}
        EditorUtility.SetDirty(veil);EditorUtility.SetDirty(material);
    }
    static Material EnsureMaterial(string name,string shader)
    {
        string path="Assets/_Project/Materials/FinalDojoArena/Garden/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);var type=Shader.Find(shader);if(!type)throw new Exception("Missing shader "+shader);
        if(!material){material=new Material(type);AssetDatabase.CreateAsset(material,path);}else material.shader=type;
        EditorUtility.SetDirty(material);return material;
    }
}
