Shader "Project/VFX/VoidRendRefraction"
{
 Properties { _Strength("Strength",Range(0,1))=0 _Clock("Clock",Float)=0 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   ZWrite Off ZTest Always Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float _Strength, _Clock;
   CBUFFER_END
   float _SpatialStrength, _SpatialClock;
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   half4 Frag(Varyings i):SV_Target
   {
    float2 uv=i.texcoord;
    float strength=saturate(_SpatialStrength);
    if(strength<.001) return SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv);
    // A small set of irregular seeds partitions the screen into LARGE polygons.
    // A rigid translation per polygon breaks straight lines at sharp boundaries.
    float aspect=_ScreenParams.x/_ScreenParams.y;
    float2 p=uv*float2(aspect,1);
    float nearest=100, second=100, id=0;
    float epoch=floor(_SpatialClock*4);
    [unroll] for(int k=0;k<10;k++)
    {
      float2 seed=float2(hash(float2(k+3,epoch+11)),hash(float2(k+41,epoch+7)));
      float2 site=seed*float2(aspect,1);
      float d=dot(p-site,p-site);
      if(d<nearest){second=nearest;nearest=d;id=k;}
      else second=min(second,d);
    }
    float angle=hash(float2(id+9,epoch+4))*6.283185;
    float2 axis=float2(cos(angle)/aspect,sin(angle));
    float pulse=.7+.3*(1-smoothstep(.35,.95,frac(_SpatialClock*4)));
    float enabled=step(.45,hash(float2(id+71,epoch)));
    float2 offset=axis*(.022+.036*hash(float2(id+29,epoch)))*strength*pulse*enabled;
    float2 split=axis*.0008*strength*pulse*enabled;
    float3 c;
    c.r=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv+offset+split)).r;
    c.g=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv+offset)).g;
    c.b=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv+offset-split)).b;
    // No polygon outlines: the only boundary cue is the image discontinuity itself.
    return half4(c,1);
   }
   ENDHLSL
  }
 }
}
