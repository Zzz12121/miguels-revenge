using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class DojoScenicChecks
{
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Invoke(object o,string name)=>o.GetType().GetMethod(name,Flags).Invoke(o,null);
    static async Task Until(Func<bool> ok,float seconds,string message)
    {float end=Time.realtimeSinceStartup+seconds;while(!ok()){Check(Time.realtimeSinceStartup<end,message);await Task.Delay(15);}}
    static async Task Wait(float seconds){float end=Time.time+seconds;await Until(()=>Time.time>=end,Mathf.Max(10,seconds*4),"Simulation stopped");}
    static Keyboard keyboard;static Mouse mouse;
    static void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState());
        typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null).Invoke(null,new object[]{InputUpdateType.Dynamic});keyboard.MakeCurrent();mouse.MakeCurrent();
    }
    static void Shot(string name,Vector3? position=null,Vector3? look=null,float fov=55)
    {
        var source=Camera.main;var go=new GameObject("Scenic check camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(source);go.AddComponent<UniversalAdditionalCameraData>().requiresDepthTexture=true;
        camera.fieldOfView=fov;camera.transform.SetPositionAndRotation(position??source.transform.position,position.HasValue?Quaternion.LookRotation(look.Value-position.Value):source.transform.rotation);
        var rt=RenderTexture.GetTemporary(1280,800,24);var previous=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes("output/dojo-building/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
        finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(go);}
    }
    public static async Task<string> Run()
    {
        var cut=Object.FindAnyObjectByType<DojoOpeningCutscene>();var p=PlayerHealth.Find();var motor=p.GetComponent<ThirdPersonController>();var cc=p.GetComponent<CharacterController>();
        var encounter=cut.master.GetComponent<DojoCalligraphyEncounter>();var boss=cut.master.GetComponent<DojoMasterBoss>();var hp=boss.GetComponent<BossHealth>();var escape=boss.GetComponent<DojoElderEscape>();
        var oldKeyboard=Keyboard.current;var oldMouse=Mouse.current;var oldBehavior=InputSystem.settings.editorInputBehaviorInPlayMode;float oldTime=Time.timeScale;
        var metrics=new Dictionary<string,object>();
        string fogBefore=JsonUtility.ToJson(Object.FindAnyObjectByType<FogZone>());
        try
        {
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard=InputSystem.AddDevice<Keyboard>("Dojo scenic checks");mouse=InputSystem.AddDevice<Mouse>("Dojo scenic mouse");InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(mouse);Keys();
            Time.timeScale=2;await Wait(.2f);p.GetComponent<DojoTestMode>().SetActive(true);
            cc.enabled=false;p.transform.position=new Vector3(0,1,1.5f);cc.enabled=true;motor.ResetMotion();Physics.SyncTransforms();await Wait(.2f);
            Keys();Invoke(cut.gate,"Update");var prompt=Object.FindAnyObjectByType<InteractPrompt>();if(prompt)Invoke(prompt,"LateUpdate");Keys(Key.E);Invoke(cut.gate,"Update");Keys();Invoke(cut,"Update");
            await Until(()=>cut.Phase=="Exploring courtyard",10,"No scenic player walk");
            float start=p.transform.position.z,lastScenicZ=start,minYaw=100,maxYaw=-100,maxLift=0,maxOrbit=0,minCameraDistance=100;bool captured=false;
            while(cut.Phase=="Exploring courtyard")
            {
                minYaw=Mathf.Min(minYaw,cut.HeadYaw);maxYaw=Mathf.Max(maxYaw,cut.HeadYaw);maxLift=Mathf.Max(maxLift,cut.HeadLift);
                lastScenicZ=p.transform.position.z;
                maxOrbit=Mathf.Max(maxOrbit,cut.ScenicCameraOrbit);minCameraDistance=Mathf.Min(minCameraDistance,Vector3.Distance(Camera.main.transform.position,p.transform.position+Vector3.up*1.45f));
                Check(!cut.PlayerRepositioned,"Hidden road reposition must wait for tea-room shot");
                if(cut.ExplorationElapsed>1.8f&&!captured)
                {Shot("53-slow-scenic-walk");Shot("54-player-head-up-and-turn",p.transform.position+p.transform.forward*3+p.transform.right*2+Vector3.up*1.6f,p.transform.position+Vector3.up*1.4f,48);captured=true;}
                await Task.Delay(15);
            }
            float travel=lastScenicZ-start;Check(travel>7&&travel<12,"Scenic walk should be slower: "+travel);
            Check(minYaw<-45&&maxYaw>45&&maxLift>30,"Need broad head turn and head lift");metrics["scenicTravelMeters"]=travel;metrics["headYawRange"]=new[]{minYaw,maxYaw};metrics["headLiftDegrees"]=maxLift;
            Check(maxOrbit>150&&minCameraDistance>3.8f,"Scenic camera must circle to front while keeping a comfortable distance");metrics["cameraOrbitDegrees"]=maxOrbit;metrics["minimumCameraDistance"]=minCameraDistance;
            await Until(()=>cut.PlayerRepositioned,4,"Player not hidden-repositioned");
            float expected=Mathf.Lerp(cut.gate.transform.position.z,cut.terraceStop.position.z,.85f);
            Check(Mathf.Abs(p.transform.position.z-expected)<.2f,"Player must be 85% along stone road");
            // Frustum check verifies the actual cutaway cannot reveal the repositioned player.
            var frustum=GeometryUtility.CalculateFrustumPlanes(Camera.main);Check(!GeometryUtility.TestPlanesAABB(frustum,cc.bounds),"Hidden player reposition is visible in cutaway");metrics["hiddenRoadZ"]=p.transform.position.z;
            var anim=cut.master.GetComponentInChildren<Animator>();float lastTime=Time.time,lastWalk=0;Vector3 last=cut.master.position;float walkMeters=0;bool elderCapture=false;
            while(!StoryDialogue.Active)
            {
                Check(Time.time-lastTime<90,"Opening failed to reach dialogue");
                bool walking=anim.GetCurrentAnimatorStateInfo(0).IsName("Walk")&&!anim.IsInTransition(0)&&Vector3.Distance(cut.master.position,cut.terraceStop.position)>.2f;
                if(walking)
                {
                    if(lastWalk>0&&Time.time-lastWalk>.35f)
                    {float step=Vector3.Distance(last,cut.master.position);Check(step>.12f,"Elder walks in place during visible shot");walkMeters+=step;last=cut.master.position;lastWalk=Time.time;}
                    else if(lastWalk==0){lastWalk=Time.time;last=cut.master.position;}
                    if(cut.master.position.x<11&&!elderCapture){Shot("55-elder-emerges-through-door");elderCapture=true;}
                }
                await Task.Delay(15);
            }
            metrics["elderVisibleWalkMeters"]=walkMeters;metrics["hiddenWalkMeters"]=cut.HiddenWalkMeters;Check(elderCapture,"Must see elder emerge behind partition");Check(cut.HiddenWalkMeters>1,"Occluded wall sections should be shortened");Shot("56-player-return-at-three-quarter-road");
            while(StoryDialogue.Active)
            {var story=Object.FindAnyObjectByType<StoryDialogue>();Keys(Key.Enter);Invoke(story,"Update");Keys();await Task.Delay(20);}
            await Until(()=>encounter.Phase=="Opening arms",10,"No open-arms entrance");
            await Until(()=>encounter.ArmsRaised,5,"Arms did not rise");
            var form=encounter.Form;var brush=form.brush;
            float right=Vector3.Angle((brush.rightHand.position-brush.rightUpper.position).normalized,Vector3.up),left=Vector3.Angle((brush.leftHand.position-brush.leftUpper.position).normalized,Vector3.up);
            Check(Mathf.Abs(right-45)<5&&Mathf.Abs(left-45)<5,"Both arms should point 45 degrees up: "+right+","+left);
            await Wait(.35f);
            Shot("57-elder-45-degree-arms-and-brush",form.transform.position+form.transform.forward*5+Vector3.up*2,form.transform.position+Vector3.up*1.35f,52);
            metrics["armAnglesFromVertical"]=new[]{right,left};
            await Until(()=>encounter.Running&&!cut.IsPlaying,12,"No fight after brush reveal");await Until(()=>encounter.CastHistory.Count>0,5,"No first slam");Check(encounter.CastHistory[0]==6,"Slam must follow arms/brush");
            await Until(()=>encounter.EntranceComplete,7,"Entrance did not land");encounter.StopAllCoroutines();form.CancelCast();hp.SetHealth(hp.maxHealth*.5f);
            await Until(()=>escape.Cloud&&escape.Phase!="Twisting into ink",6,"No flying ink escape");
            bool rearShot=false,gateShot=false;float deadline=Time.realtimeSinceStartup+35;
            while(escape.Phase!="Player remarks on escape")
            {
                Check(Time.realtimeSinceStartup<deadline,"Escape did not finish at first torii: "+escape.Phase+" wp="+escape.ReachedWaypoint);
                if(escape.Cloud)
                {
                    var at=escape.Cloud.transform.position;
                    if(at.z>104&&at.x<45&&!rearShot){Shot("58-agile-ink-direct-over-river");rearShot=true;}
                    if(at.x>64&&!gateShot)
                    {
                        Shot("59-ink-past-first-torii");gateShot=true;
                        var block=new MaterialPropertyBlock();Object.FindAnyObjectByType<DojoMountainVeil>().volume.GetPropertyBlock(block);
                        Check(block.GetFloat("_ObserverFade")==0,"First-torii cinematic must not be hidden by exterior veil");
                    }
                }
                await Task.Delay(15);
            }
            var nav=new DojoInkFlightPath(cut.master);int collisions=0;float maxSway=0;
            for(int i=0;i<escape.FlightSamples.Count;i++)
            {
                if(!nav.Free(escape.FlightSamples[i]))collisions++;
                if(i>0&&i!=escape.IndoorCutSampleIndex&&!nav.Clear(escape.FlightSamples[i-1],escape.FlightSamples[i]))collisions++;
                var at=escape.FlightSamples[i];float closest=float.MaxValue;
                Vector3 a=form.transform.position+Vector3.up*1.25f;
                foreach(var anchor in escape.flightPath)
                {var d=anchor.position-a;float q=Mathf.Clamp01(Vector3.Dot(at-a,d)/Mathf.Max(.01f,d.sqrMagnitude));closest=Mathf.Min(closest,Vector3.Distance(at,a+d*q));a=anchor.position;}
                maxSway=Mathf.Max(maxSway,closest);
            }
            Check(collisions==0,"Ink must not clip walls / trunks / terrain: "+collisions);
            Check(maxSway>.7f,"Ink should drift clearly sideways rather than fly straight");
            var final=escape.flightPath.Last().position;Check(final.x<68&&final.z<130,"Finale should stop beyond first torii");
            metrics["flightSamples"]=escape.FlightSamples.Count;metrics["collisionViolations"]=collisions;metrics["pathDeviationMeters"]=maxSway;metrics["avoidanceCorners"]=escape.AvoidanceCorners;metrics["finalToriiPosition"]=final.ToString();
            while(StoryDialogue.Active){var story=Object.FindAnyObjectByType<StoryDialogue>();Keys(Key.Enter);Invoke(story,"Update");Keys();await Task.Delay(20);}
            await Until(()=>escape.Completed,3,"No control handoff after player remark");Check(!hp.ShowBar&&motor.inputEnabled&&!motor.ActionsLocked,"Finale must hide bar and unlock player");
            Check(fogBefore==JsonUtility.ToJson(Object.FindAnyObjectByType<FogZone>()),"Existing mountain-path fog settings changed");
            Check(!DojoMountainVeil.InsideMountain(new Vector3(71,2,53))&&DojoMountainVeil.InsideMountain(new Vector3(57,2,123)),"Exterior mist observer gating");
            metrics["existingPathFogUnchanged"]=true;metrics["success"]=true;
            string json=Newtonsoft.Json.JsonConvert.SerializeObject(metrics,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("output/dojo-building/dojo-scenic-checks.json",json);return json;
        }
        finally
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();if(oldMouse!=null&&oldMouse.added)oldMouse.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode=oldBehavior;if(!StoryDialogue.Active)Time.timeScale=oldTime;
        }
    }
}
