using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Owned additive render-time shake. Never changes the player's orbit camera state.</summary>
public sealed class CalligraphyUltimateCameraShake : MonoBehaviour
{
    Camera target;
    float started,ends,strength,frequency;
    bool applied;
    Vector3 savedPosition;
    Quaternion savedRotation;
    public bool IsShaking=>Time.time<ends && strength>0;
    void Awake(){target=GetComponent<Camera>();}
    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering+=Begin;
        RenderPipelineManager.endCameraRendering+=End;
        Camera.onPreCull+=Before;
        Camera.onPostRender+=After;
    }
    public void Pulse(CalligraphyBossCombat.AvatarShakeSettings settings)
    {
        if(settings==null || settings.strength<=0)return;
        started=Time.time;ends=Time.time+settings.duration;strength=settings.strength;frequency=settings.frequency;
    }
    void Begin(ScriptableRenderContext context,Camera camera)=>Before(camera);
    void End(ScriptableRenderContext context,Camera camera)=>After(camera);
    void Before(Camera camera)
    {
        if(camera!=target || applied || !IsShaking || Time.timeScale<=0)return;
        applied=true;savedPosition=transform.position;savedRotation=transform.rotation;
        float age=Time.time-started,fade=Mathf.Clamp01((ends-Time.time)/Mathf.Max(.01f,ends-started));
        float x=(Mathf.PerlinNoise(age*frequency,17)-.5f)*2;
        float y=(Mathf.PerlinNoise(41,age*frequency)-.5f)*2;
        transform.position+=transform.right*x*strength*fade+transform.up*y*strength*fade;
        transform.rotation*=Quaternion.Euler(y*strength*.8f,x*strength*.8f,0);
    }
    void After(Camera camera)
    {if(camera==target && applied){transform.SetPositionAndRotation(savedPosition,savedRotation);applied=false;}}
    void OnDisable()
    {
        After(target);
        RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;
        Camera.onPreCull-=Before;Camera.onPostRender-=After;
    }
}
