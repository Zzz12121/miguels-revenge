// Body-only live Editor eval: required integration checks, no gameplay rebalance or test runner setup.
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var roots=scene.GetRootGameObjects();
var objects=roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
var nodes=new[]{
    new{name="Market",p=new Vector3(-44,0,0)},new{name="West quay",p=new Vector3(-11,0,-30)},
    new{name="South bridge",p=new Vector3(-1.3f,2.5f,-22)},new{name="Tea interior",p=new Vector3(28,.18f,-24)},
    new{name="East quay",p=new Vector3(11,0,0)},new{name="Chess court",p=new Vector3(54,.08f,43)},
    new{name="North bridge",p=new Vector3(3,2.5f,53)},new{name="West alley",p=new Vector3(-57,0,-22)}
};
var nav=new System.Collections.Generic.List<object>();
for(int j=0;j<nodes.Length;j++)
{
    var a=nodes[j];var b=nodes[(j+1)%nodes.Length];
    foreach(bool reverse in new[]{false,true})
    {
        Vector3 from=reverse?b.p:a.p,to=reverse?a.p:b.p;
        bool begin=UnityEngine.AI.NavMesh.SamplePosition(from,out var start,2,UnityEngine.AI.NavMesh.AllAreas);
        bool end=UnityEngine.AI.NavMesh.SamplePosition(to,out var finish,2,UnityEngine.AI.NavMesh.AllAreas);
        var path=new UnityEngine.AI.NavMeshPath();
        bool found=begin&&end&&UnityEngine.AI.NavMesh.CalculatePath(start.position,finish.position,UnityEngine.AI.NavMesh.AllAreas,path);
        nav.Add(new{from=reverse?b.name:a.name,to=reverse?a.name:b.name,begin,end,found,status=path.status.ToString(),corners=path.corners.Length});
    }
}
var routines=UnityEngine.Object.FindObjectsByType<NpcRoutine>(UnityEngine.FindObjectsInactive.Include);
var shaders=UnityEditor.AssetDatabase.FindAssets("t:Shader",new[]{"Assets/_Project/VFX/InkboundTown"}).Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Select(p=>UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(p));
var report=new{
    scene=scene.path,playing=Application.isPlaying,hardware=new{SystemInfo.processorType,SystemInfo.graphicsDeviceName},
    pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name:"Built in",
    colorSpace=QualitySettings.activeColorSpace.ToString(),fov=Camera.main.fieldOfView,
    shaderErrors=shaders.SelectMany(s=>UnityEditor.ShaderUtil.GetShaderMessages(s)).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m=>m.message).ToArray(),
    missingScripts=roots.Sum(r=>r.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c==null)),
    invalidMeshes=objects.Select(t=>t.GetComponent<MeshFilter>()).Where(f=>f&&f.sharedMesh&&f.sharedMesh.isReadable).Where(f=>f.sharedMesh.vertexCount==0||f.sharedMesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))).Select(f=>f.name).ToArray(),
    npcRoutines=routines.Length,boats=UnityEngine.Object.FindObjectsByType<InkboundBoat>().Length,
    npcColliders=routines.Sum(r=>r.GetComponentsInChildren<Collider>(true).Count(c=>c.enabled)),allIgnorePlayer=routines.All(r=>r.ignorePlayer),
    visibleLettering=objects.Select(t=>t.GetComponent<TextMesh>()).Where(t=>t&&!string.IsNullOrWhiteSpace(t.text)).Select(t=>t.text).ToArray(),
    playerCombat=PlayerHealth.Find().GetComponent<PlayerCombat>().enabled,
    nav,elder=UnityEngine.Object.FindAnyObjectByType<InkboundTownEncounter>().State,
    timestamp=DateTimeOffset.Now.ToString("o")
};
string json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);
System.IO.File.WriteAllText("output/inkbound-town/v3/required-checks.json",json);return json;
