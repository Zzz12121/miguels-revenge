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
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   half4 Frag(Varyings i):SV_Target
   {
    float2 uv=i.texcoord;
    float t=_Clock;
    float band=floor((uv.y+uv.x*.24)*24);
    float h=hash(float2(band,floor(t*7)));
    float shard=step(.53,h);
    float2 offset=float2((h-.5)*.075, sin(uv.x*27+t*9)*.008)*_Strength*shard;
    float2 d=uv-.5;
    offset+=normalize(d+.0001)*sin(length(d)*45-t*10)*.009*_Strength;
    float2 split=float2(.004, .001)*_Strength;
    float3 c;
    c.r=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv+offset+split)).r;
    c.g=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv+offset)).g;
    c.b=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv+offset-split)).b;
    float l=dot(c,float3(.2126,.7152,.0722));
    c=lerp(c,l*float3(.72,.89,1.08),saturate(_Strength*.65));
    return half4(c,1);
   }
   ENDHLSL
  }
 }
}
