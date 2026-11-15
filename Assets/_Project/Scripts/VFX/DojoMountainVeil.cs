using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Local exterior mountain mist. Does not touch RenderSettings, FogZone or existing path particles.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class DojoMountainVeil : MonoBehaviour
{
    public Renderer volume;
    public Vector3 minimum=new Vector3(48.5f,-3,40),maximum=new Vector3(268.5f,98,260);
    MaterialPropertyBlock properties;
    void OnEnable(){RenderPipelineManager.beginCameraRendering+=ForCamera;}
    void OnDisable(){RenderPipelineManager.beginCameraRendering-=ForCamera;}
    public static bool InsideMountain(Vector3 at)=>at.x>48.6f&&at.x<222&&at.z>120&&at.z<230;
    public static bool InsideCourtyard(Vector3 at)=>at.x>=-49&&at.x<=49&&at.z>=0&&at.z<=127;
    void ForCamera(ScriptableRenderContext context,Camera camera)
    {
        if(!volume||!camera)return;
        Vector3 observer=camera.transform.position;
        // The ordinary third-person camera may hang outside the entrance while its player is inside.
        if(Application.isPlaying)
        {
            var elder=FindAnyObjectByType<DojoElderEscape>();
            bool cinematic=elder&&elder.Started&&!elder.Completed;
            if(cinematic&&elder.Cloud&&elder.Cloud.transform.position.x>44)observer=new Vector3(60,observer.y,125);
            else if(!cinematic&&camera==Camera.main){var player=PlayerHealth.Find();if(player)observer=player.transform.position;}
            else if(observer.x>43&&observer.z>110)observer=new Vector3(60,observer.y,125);
        }
        properties??=new MaterialPropertyBlock();volume.GetPropertyBlock(properties);
        properties.SetVector("_VolumeMin",minimum);properties.SetVector("_VolumeMax",maximum);
        properties.SetFloat("_ObserverFade",InsideMountain(observer)||InsideCourtyard(observer)?0:1);volume.SetPropertyBlock(properties);
    }
}
