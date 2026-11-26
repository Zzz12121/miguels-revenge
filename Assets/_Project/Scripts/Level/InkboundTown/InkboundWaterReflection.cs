using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>One bounded, low-rate planar reflection for this town's river.</summary>
[DefaultExecutionOrder(18000)]
public sealed class InkboundWaterReflection : MonoBehaviour
{
    [Range(128,1024)] public int resolution = 512;
    [Range(1,30)] public float updatesPerSecond = 8;
    public float waterHeight = -1.25f;
    Camera reflection;
    RenderTexture texture;
    float next;
    bool rendering;
    void LateUpdate()
    {
        Camera source=Camera.main;
        if(!source || rendering || Time.unscaledTime<next)return;
        next=Time.unscaledTime+1/updatesPerSecond;
        RefreshFor(source);
    }
    public void RefreshFor(Camera source)
    {
        if(!source || rendering)return;
        if(!reflection)
        {
            var go=new GameObject("Ink river reflection camera"){hideFlags=HideFlags.HideAndDontSave};
            reflection=go.AddComponent<Camera>();reflection.enabled=false;
            var data=reflection.GetUniversalAdditionalCameraData();data.renderShadows=false;data.renderPostProcessing=false;
            data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
            texture=new RenderTexture(resolution,resolution,16,RenderTextureFormat.ARGB32){name="Ink river reflected town"};texture.Create();
        }
        reflection.CopyFrom(source);reflection.enabled=false;reflection.targetTexture=texture;
        reflection.cullingMask=source.cullingMask & ~(1<<4);reflection.farClipPlane=210;reflection.clearFlags=CameraClearFlags.SolidColor;
        reflection.backgroundColor=new Color(.957f,.949f,.918f);
        Vector3 eye=source.transform.position;eye.y=waterHeight*2-eye.y;
        reflection.transform.position=eye;
        var mirror=Matrix4x4.identity;mirror.m11=-1;mirror.m13=waterHeight*2;
        reflection.worldToCameraMatrix=source.worldToCameraMatrix*mirror;
        Vector3 point=reflection.worldToCameraMatrix.MultiplyPoint(new Vector3(0,waterHeight+.025f,0));
        Vector3 normal=reflection.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
        reflection.projectionMatrix=source.CalculateObliqueMatrix(new Vector4(normal.x,normal.y,normal.z,-Vector3.Dot(point,normal)));
        bool previous=GL.invertCulling;
        try
        {
            rendering=true;GL.invertCulling=!previous;Shader.SetGlobalFloat("_InkReflectionPass",1);reflection.Render();
            Shader.SetGlobalTexture("_InkTownReflection",texture);
            Shader.SetGlobalMatrix("_InkTownReflectionVP",GL.GetGPUProjectionMatrix(reflection.projectionMatrix,true)*reflection.worldToCameraMatrix);
            Shader.SetGlobalFloat("_InkTownReflectionReady",1);
        }
        finally{Shader.SetGlobalFloat("_InkReflectionPass",0);GL.invertCulling=previous;rendering=false;}
    }
    void OnDisable()
    {
        Shader.SetGlobalFloat("_InkTownReflectionReady",0);
        if(reflection)Destroy(reflection.gameObject);
        if(texture){texture.Release();Destroy(texture);}
    }
}
