using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using Object=UnityEngine.Object;
public static class InkboundTownV3TeaPathCheck
{
    public static async Task<string> Run()
    {
        var d=Object.FindAnyObjectByType<InkboundTownEncounter>();var p=PlayerHealth.Find();var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=new Vector3(28,1.23f,-24);cc.enabled=true;p.GetComponent<ThirdPersonController>().ResetMotion();
        UnityEngine.AI.NavMesh.SamplePosition(new Vector3(11,0,-30),out var ground,2,UnityEngine.AI.NavMesh.AllAreas);d.agent.Warp(ground.position);d.elder.transform.position=ground.position;d.BeginFight();
        typeof(InkboundTownEncounter).GetField("decision",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(d,Time.time+1000);
        var rows=new List<object>();
        for(int i=0;i<22;i++)
        {
            await Task.Delay(1000);Vector3 direction=Vector3.ProjectOnPlane(d.agent.steeringTarget-d.elder.transform.position,Vector3.up).normalized;
            rows.Add(new{seconds=i,position=d.elder.transform.position.ToString(),steering=d.agent.steeringTarget.ToString(),remaining=d.agent.remainingDistance,path=d.agent.pathStatus.ToString(),hits=Physics.SphereCastAll(d.elder.transform.position+Vector3.up,.42f,direction,.16f,~0,QueryTriggerInteraction.Ignore).Where(h=>!h.transform.IsChildOf(d.elder.transform)).Select(h=>new{name=h.collider.name,h.distance}).ToArray()});
        }
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/inkbound-town/v3/tea-path-debug.json",json);return json;
    }
}
