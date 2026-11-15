using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

public static class DojoCalligraphySetup
{
    const string BossDir="Assets/_Project/Prefabs/Boss/";
    const string FlaskPath="Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoWineFlask.prefab";
    public static string SaveForm()
    {
        var source=Object.FindAnyObjectByType<CalligraphyBossCombat>();
        if(!source||source.gameObject.scene.name!="VFX_VoidRend_Bocchi")throw new Exception("Open saved calligraphy range first");
        var clone=Object.Instantiate(source.gameObject);clone.name="PF_DojoCalligraphyForm";
        try
        {
            clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one;
            var combat=clone.GetComponent<CalligraphyBossCombat>();combat.externallySequenced=true;combat.cycleDemonstration=false;combat.dodgeChance=0;
            combat.GetComponent<CalligraphyBrushPresentation>().showcase=false;
            var display=clone.GetComponent<CalligraphyInkPresentation>();if(display)display.enabled=false;
            foreach(var c in clone.GetComponents<CalligraphyPracticeTarget>())Object.DestroyImmediate(c);
            PrefabUtility.SaveAsPrefabAsset(clone,BossDir+"PF_DojoCalligraphyForm.prefab");
            AssetDatabase.SaveAssets();return "Saved production form using current range actor and all ten configured skills";
        }
        finally{Object.DestroyImmediate(clone);}
    }
    static Material Material(string name,Color color)
    {
        string p="Assets/_Project/Materials/FinalDojoArena/Garden/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,p);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.35f);EditorUtility.SetDirty(m);return m;
    }
    static GameObject Flask()
    {
        var root=new GameObject("PF_DojoWineFlask");
        var v=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        // Turned ceramic wine gourd: round belly, narrow waist, small neck and open lip.
        float[] y={0,.015f,.04f,.085f,.14f,.18f,.215f,.245f,.275f,.305f,.33f};
        float[] radius={.045f,.075f,.095f,.105f,.087f,.045f,.06f,.068f,.049f,.025f,.027f};
        const int sides=28;
        for(int row=0;row<y.Length;row++)for(int k=0;k<=sides;k++)
        {
            float a=k*Mathf.PI*2/sides;v.Add(new Vector3(Mathf.Cos(a)*radius[row],y[row],Mathf.Sin(a)*radius[row]));uv.Add(new Vector2(k/(float)sides,row/(float)(y.Length-1)));
            if(row>0&&k<sides){int n=row*(sides+1)+k;triangles.AddRange(new[]{n,n+1,n-sides-1,n+1,n-sides,n-sides-1});}
        }
        var mesh=new Mesh{name="SM_DojoWineFlask"};mesh.SetVertices(v);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path="Assets/_Project/Models/Environment/FinalDojoArena/Garden/SM_DojoWineFlask.asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved){EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);mesh=saved;}else AssetDatabase.CreateAsset(mesh,path);
        root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=Material("WineFlask_Ceramic",new Color(.32f,.09f,.045f));
        var band=GameObject.CreatePrimitive(PrimitiveType.Cylinder);band.name="Dark woven neck binding";band.transform.SetParent(root.transform,false);band.transform.localPosition=new Vector3(0,.185f,0);band.transform.localScale=new Vector3(.095f,.009f,.095f);Object.DestroyImmediate(band.GetComponent<Collider>());band.GetComponent<Renderer>().sharedMaterial=Material("WineFlask_Cord",new Color(.12f,.065f,.025f));
        var opening=GameObject.CreatePrimitive(PrimitiveType.Cylinder);opening.name="Dark bottle opening";opening.transform.SetParent(root.transform,false);opening.transform.localPosition=new Vector3(0,.3302f,0);opening.transform.localScale=new Vector3(.043f,.0005f,.043f);Object.DestroyImmediate(opening.GetComponent<Collider>());opening.GetComponent<Renderer>().sharedMaterial=Material("WineFlask_Opening",new Color(.012f,.007f,.005f));
        return root;
    }
    public static string Install()
    {
        if(EditorApplication.isPlaying)throw new Exception("Use Edit mode");
        var cut=Object.FindAnyObjectByType<DojoOpeningCutscene>();if(!cut||!cut.master)throw new Exception("Open dojo scene with opening cutscene");
        var form=AssetDatabase.LoadAssetAtPath<GameObject>(BossDir+"PF_DojoCalligraphyForm.prefab").GetComponent<CalligraphyBossCombat>();
        var boss=cut.master.GetComponent<DojoMasterBoss>();if(!boss)boss=cut.master.gameObject.AddComponent<DojoMasterBoss>();
        var encounter=cut.master.GetComponent<DojoCalligraphyEncounter>();if(!encounter)encounter=cut.master.gameObject.AddComponent<DojoCalligraphyEncounter>();encounter.formPrefab=form;
        var stop=cut.transform.Find("TerraceStop");if(!stop){stop=new GameObject("TerraceStop").transform;stop.SetParent(cut.transform,false);}stop.SetPositionAndRotation(new Vector3(0,1.2f,76.6f),Quaternion.identity);cut.terraceStop=stop;
        if(cut.meetPoint)cut.meetPoint.position=new Vector3(0,0,76.6f-cut.meetingGap*.5f);
        cut.bossArenaRadius=36;boss.arenaCenter=new Vector3(0,0,55);boss.arenaRadius=36;
        var flask=Flask();try{PrefabUtility.SaveAsPrefabAsset(flask,FlaskPath);}finally{Object.DestroyImmediate(flask);}
        cut.wineFlaskPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(FlaskPath);cut.holdTeaCup=true;
        foreach(var shot in cut.shots)
        {
            if(shot.name.Contains("庭院石板路"))shot.gameObject.SetActive(false);
            if(shot.name.Contains("走下台階")){shot.gameObject.name="Shot_04_露臺停步";shot.startMeters=19.5f;}
        }
        foreach(var line in cut.lines)line.text=line.text.Replace("喝茶、看門","飲酒、看門");
        EditorUtility.SetDirty(cut);EditorUtility.SetDirty(boss);EditorUtility.SetDirty(encounter);AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(cut.gameObject.scene);EditorSceneManager.SaveScene(cut.gameObject.scene);
        return "Saved terrace stop, ceramic wine gourd, calligraphy transformation and sequential encounter in dojo scene";
    }
}
