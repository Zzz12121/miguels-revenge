using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class DojoGardenChecks
{
    public static string Run()
    {
        var a=GameObject.Find("FinalDojoArena/Architecture");
        if(!a)throw new Exception("Open dojo scene");
        var garden=a.transform.Find("LivingGarden");if(!garden)throw new Exception("LivingGarden missing");
        var all=garden.GetComponentsInChildren<Transform>(true);
        string[] categories={"CherryTree_","BlackPine_","Bamboo_","JapaneseMaple_","Boxwood_","Fern_","StoneLantern_","SteppingStone_"};
        var counts=categories.ToDictionary(k=>k,k=>all.Count(t=>t.name.StartsWith(k)));
        foreach(var c in counts)if(c.Value==0)throw new Exception("Required art missing: "+c.Key);
        var renderers=garden.GetComponentsInChildren<MeshRenderer>(true);
        foreach(var r in renderers)
        {
            if(!r.GetComponent<MeshFilter>()?.sharedMesh)throw new Exception("Mesh missing: "+r.name);
            foreach(var m in r.sharedMaterials)if(!m||!m.shader||!m.shader.isSupported)throw new Exception("Material unsupported: "+r.name);
        }
        var wind=Shader.Find("Project/Environment/Dojo Garden Wind");
        if(ShaderUtil.GetShaderMessages(wind).Any(m=>m.severity.ToString()=="Error"))throw new Exception("Wind shader compile error");
        if(a.transform.Find("CourtyardAndGarden/BakedVisuals").gameObject.activeSelf)throw new Exception("Old garden visuals still visible");
        if(a.transform.Find("OuterWallsAndGate/VermilionTorii").gameObject.activeSelf)throw new Exception("Old torii collision still active");
        Physics.SyncTransforms();
        int pathSamples=0;
        // Ray heights detect raised stones and accidental tall barriers along the open approach.
        for(float z=8;z<73;z+=.5f)
        {
            var hits=Physics.RaycastAll(new Vector3(0,3,z),Vector3.down,5,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.GetComponentInParent<PlayerHealth>()==null).OrderBy(h=>h.distance).ToArray();
            if(hits.Length==0)throw new Exception("No path floor z="+z);
            var hit=hits[0];
            if(hit.point.y>.32f)throw new Exception("Path obstacle at z="+z+" "+hit.collider.name+" y="+hit.point.y);
            pathSamples++;
        }
        var systems=garden.GetComponentsInChildren<ParticleSystem>();
        if(systems.Length<6||systems.Any(ps=>!ps.main.loop||ps.main.maxParticles>150))throw new Exception("Bounded looping leaf effects missing");
        if(Application.isPlaying&&systems.Sum(p=>p.particleCount)==0)throw new Exception("Leaf systems are not emitting");
        var summary=new {success=true,counts,pathSamples,renderers=renderers.Length,particleSystems=systems.Length,liveParticles=systems.Sum(p=>p.particleCount),scene=SceneManager.GetActiveScene().path};
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(summary,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/garden-checks.json",json);return json;
    }
}
