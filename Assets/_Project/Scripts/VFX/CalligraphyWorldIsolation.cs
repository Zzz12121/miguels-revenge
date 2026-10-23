using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

/// <summary>Temporary presentation boundary. Keeps original ground physics and restores every owned change.</summary>
[DefaultExecutionOrder(10000)]
public sealed class CalligraphyWorldIsolation : MonoBehaviour
{
    static readonly HashSet<CalligraphyWorldIsolation> active=new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetActive()=>active.Clear();
    public static bool Allows(Transform subject)
    {
        active.RemoveWhere(x=>!x || !x.isActiveAndEnabled);
        foreach(var world in active)if(subject && !world.Inside(subject))return false;
        return true;
    }
    readonly Dictionary<Renderer,bool> renderers=new();
    readonly Dictionary<Terrain,(bool heightmap,bool foliage)> terrains=new();
    readonly Dictionary<Canvas,bool> canvases=new();
    readonly List<(Collider player,Collider other,bool ignored)> pairs=new();
    readonly HashSet<Collider> handled=new();
    PlayerHealth player;
    Collider[] playerColliders;
    float scan;
    Camera revealCamera;
    RenderTexture outside;
    RawImage reveal;
    public float RevealProgress {get;set;}
    public bool FullyRevealed {get;private set;}
    public int CapturedFrames {get;private set;}
    public int HiddenCount=>renderers.Count;
    public void Initialize(PlayerHealth target)
    {
        player=target;playerColliders=target.GetComponentsInChildren<Collider>();active.Add(this);Scan();
    }
    bool Inside(Transform t)=>t.IsChildOf(transform) || (player && t.IsChildOf(player.transform));
    void Scan()
    {
        foreach(var r in FindObjectsByType<Renderer>())
        {
            if(Inside(r.transform) || renderers.ContainsKey(r))continue;
            renderers.Add(r,r.forceRenderingOff);r.forceRenderingOff=true;
        }
        foreach(var t in Terrain.activeTerrains)if(!Inside(t.transform) && !terrains.ContainsKey(t))
        {terrains.Add(t,(t.drawHeightmap,t.drawTreesAndFoliage));t.drawHeightmap=false;t.drawTreesAndFoliage=false;}
        foreach(var c in FindObjectsByType<Canvas>())
            if((c.renderMode==RenderMode.WorldSpace || c.GetComponentInParent<IDamageable>()!=null) && !Inside(c.transform) && !canvases.ContainsKey(c)){canvases.Add(c,c.enabled);c.enabled=false;}
        foreach(var c in FindObjectsByType<Collider>())
        {
            if(Inside(c.transform) || handled.Contains(c))continue;
            // The world owns its invisible support and enlarged bounds. Only the player's pairs
            // ignore outside geometry: outside actors keep their original floor and physics.
            handled.Add(c);
            foreach(var p in playerColliders)if(p && c && p!=c){bool old=Physics.GetIgnoreCollision(p,c);pairs.Add((p,c,old));Physics.IgnoreCollision(p,c,true);}
            foreach(var support in GetComponentsInChildren<Collider>())
                if(support && support!=c){bool old=Physics.GetIgnoreCollision(support,c);pairs.Add((support,c,old));Physics.IgnoreCollision(support,c,true);}
        }
    }
    void LateUpdate()
    {
        scan-=Time.deltaTime;if(scan<=0){scan=.25f;Scan();}
        if(RevealProgress<=0 || !Camera.main)return;
        var source=Camera.main;
        if(!revealCamera)
        {
            var go=new GameObject("Painting exit outside camera");go.transform.SetParent(transform,false);
            revealCamera=go.AddComponent<Camera>();revealCamera.enabled=false;
            var canvasObject=new GameObject("Painting exit crossfade",typeof(RectTransform),typeof(Canvas));
            canvasObject.transform.SetParent(transform,false);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=-100;
            var imageObject=new GameObject("Live outside view",typeof(RectTransform),typeof(RawImage));imageObject.transform.SetParent(canvasObject.transform,false);
            reveal=imageObject.GetComponent<RawImage>();reveal.raycastTarget=false;
            reveal.rectTransform.anchorMin=Vector2.zero;reveal.rectTransform.anchorMax=Vector2.one;
            reveal.rectTransform.offsetMin=reveal.rectTransform.offsetMax=Vector2.zero;
        }
        int width=Mathf.Max(1,source.pixelWidth),height=Mathf.Max(1,source.pixelHeight);
        if(!outside || outside.width!=width || outside.height!=height)
        {
            if(outside){outside.Release();Destroy(outside);}
            outside=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name="Painting exit live outside"};outside.Create();reveal.texture=outside;
        }
        revealCamera.CopyFrom(source);revealCamera.enabled=false;revealCamera.targetTexture=outside;
        revealCamera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
        var data=revealCamera.GetUniversalAdditionalCameraData();var sourceData=source.GetUniversalAdditionalCameraData();
        data.renderPostProcessing=sourceData.renderPostProcessing;data.volumeLayerMask=sourceData.volumeLayerMask;
        data.volumeTrigger=sourceData.volumeTrigger;data.antialiasing=sourceData.antialiasing;
        var local=GetComponentsInChildren<Renderer>();var old=new bool[local.Length];
        try
        {
            for(int i=0;i<local.Length;i++){old[i]=local[i].forceRenderingOff;local[i].forceRenderingOff=true;}
            foreach(var p in renderers)if(p.Key)p.Key.forceRenderingOff=p.Value;
            foreach(var p in terrains)if(p.Key){p.Key.drawHeightmap=p.Value.heightmap;p.Key.drawTreesAndFoliage=p.Value.foliage;}
            foreach(var p in canvases)if(p.Key)p.Key.enabled=p.Value;
            revealCamera.Render();CapturedFrames++;
        }
        finally
        {
            for(int i=0;i<local.Length;i++)if(local[i])local[i].forceRenderingOff=old[i];
            foreach(var p in renderers)if(p.Key)p.Key.forceRenderingOff=true;
            foreach(var p in terrains)if(p.Key){p.Key.drawHeightmap=false;p.Key.drawTreesAndFoliage=false;}
            foreach(var p in canvases)if(p.Key)p.Key.enabled=false;
        }
        reveal.color=new Color(1,1,1,Mathf.SmoothStep(0,1,RevealProgress));
        FullyRevealed=RevealProgress>=1;
    }
    void OnDisable()
    {
        active.Remove(this);
        if(reveal)reveal.gameObject.SetActive(false);
        if(revealCamera)revealCamera.targetTexture=null;
        if(outside){outside.Release();Destroy(outside);outside=null;}
        foreach(var p in renderers)if(p.Key)p.Key.forceRenderingOff=p.Value;
        foreach(var p in terrains)if(p.Key){p.Key.drawHeightmap=p.Value.heightmap;p.Key.drawTreesAndFoliage=p.Value.foliage;}
        foreach(var p in canvases)if(p.Key)p.Key.enabled=p.Value;
        foreach(var p in pairs)if(p.player && p.other)Physics.IgnoreCollision(p.player,p.other,p.ignored);
        renderers.Clear();terrains.Clear();canvases.Clear();pairs.Clear();handled.Clear();
    }
}
