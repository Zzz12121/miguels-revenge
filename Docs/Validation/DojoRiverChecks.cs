using System; using System.IO; using System.Threading.Tasks; using UnityEngine;
public static class DojoRiverChecks {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 public static async Task<string> Run(){
 Check(Application.isPlaying,"Play mode required");var water=UnityEngine.Object.FindAnyObjectByType<DojoRiverWater>();var p=PlayerHealth.Find();var cc=p.GetComponent<CharacterController>();var move=p.GetComponent<ThirdPersonController>();var start=p.transform.position;var fixture=new GameObject("Temporary water attack contact check");
 Action<Vector3> warp=v=>{cc.enabled=false;p.transform.position=v;cc.enabled=true;move.ResetMotion();Physics.SyncTransforms();};
 try{
 float x=-5,z=DojoRiverWater.Center(x);warp(new Vector3(x,1,z));await Task.Delay(1600);Check(cc.isGrounded,"Player must settle on riverbed");Check(p.FeetY<-.65f,"Water must have a lowered riverbed: "+p.FeetY);Check(water.ActorContactCount>0,"Wading must emit water contacts");float bed=p.FeetY;int contacts=water.ActorContactCount;
 warp(new Vector3(12,1,DojoRiverWater.Center(12)));await Task.Delay(1000);Check(cc.isGrounded&&p.FeetY>0,"Stone bridge must support player above water: "+p.FeetY);float bridge=p.FeetY;
 fixture.transform.position=new Vector3(x,DojoRiverWater.WaterY,z);var ps=fixture.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.simulationSpace=ParticleSystemSimulationSpace.World;m.startSpeed=0;m.startLifetime=2;var sh=ps.shape;sh.enabled=false;var em=ps.emission;em.enabled=false;ps.Play();await Task.Delay(1200);ps.Emit(new ParticleSystem.EmitParams{position=new Vector3(x,DojoRiverWater.WaterY,z),velocity=Vector3.zero},1);await Task.Delay(1500);Check(water.ParticleContactCount>0,"Attack particles touching water must emit splash/ripple");Check(!water.EmitImpact(new Vector3(0,0,60)),"Dry land must not emit water splashes");
 string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{success=true,riverbedFeet=bed,bridgeFeet=bridge,actorContacts=contacts,particleContacts=water.ParticleContactCount,pooledImpacts=water.ImpactCount},Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/river-contact-checks.json",json);return json;
 }finally{warp(start);UnityEngine.Object.Destroy(fixture);}
 }
}
