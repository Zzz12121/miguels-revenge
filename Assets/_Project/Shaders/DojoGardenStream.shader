Shader "Project/Environment/Dojo Garden Stream"
{
 Properties
 {
  _ShallowColor("Shallow Water",Color)=(.20,.37,.32,.25)
  _DeepColor("Deep Water",Color)=(.045,.17,.16,.62)
  [Normal] _NormalMap("SUIMONO calm normal",2D)="bump"{}
  [Normal] _DetailNormal("SUIMONO rolling normal",2D)="bump"{}
  _FoamMap("SUIMONO foam mask",2D)="white"{}
  _FlowSpeed("Flow Speed",Range(0,2))=.24
  _RippleStrength("Ripple Strength",Range(0,.5))=.13
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent"}
  Pass
  {
   Tags{"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);
   TEXTURE2D(_DetailNormal);SAMPLER(sampler_DetailNormal);
   TEXTURE2D(_FoamMap);SAMPLER(sampler_FoamMap);
   CBUFFER_START(UnityPerMaterial)
    half4 _ShallowColor,_DeepColor;float _FlowSpeed,_RippleStrength;
   CBUFFER_END
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;float4 tangent:TANGENT;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;float3 tangent:TEXCOORD2;half fog:TEXCOORD3;};
   V Vert(A i){V o;VertexPositionInputs p=GetVertexPositionInputs(i.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.uv=i.uv;o.tangent=TransformObjectToWorldDir(i.tangent.xyz);o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 Frag(V i):SV_Target
   {
    float time=_Time.y*_FlowSpeed;
    float2 uv=float2(i.uv.x*.3-time,i.uv.y*1.4);
    half3 a=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv));
    half3 b=UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormal,sampler_DetailNormal,uv*1.73+float2(time*.32,time*.08)));
    half3 tangent=normalize(i.tangent),across=normalize(cross(tangent,half3(0,1,0)));
    half2 slope=(a.xy+b.xy*.55)*_RippleStrength;
    half3 n=normalize(half3(0,1,0)+tangent*slope.x+across*slope.y);
    half3 v=SafeNormalize(GetWorldSpaceViewDir(i.world));
    half fresnel=.04+.85*pow(1-saturate(dot(n,v)),4);
    half edge=saturate(min(i.uv.y,1-i.uv.y)*7);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
    half4 water=lerp(_ShallowColor,_DeepColor,edge);
    half3 reflected=GlossyEnvironmentReflection(reflect(-v,n),.12,1);
    half3 color=lerp(water.rgb*(SampleSH(n)+light.color*.4*light.shadowAttenuation),reflected,fresnel);
    half spec=pow(saturate(dot(n,SafeNormalize(v+light.direction))),160)*.65;
    half mask=SAMPLE_TEXTURE2D(_FoamMap,sampler_FoamMap,uv*1.9).r;
    half foam=smoothstep(.52,.88,mask)*(1-smoothstep(.03,.18,min(i.uv.y,1-i.uv.y)))*.32;
    color+=light.color*spec*light.shadowAttenuation+foam*.5;
    return half4(MixFog(color,i.fog),saturate(water.a+fresnel*.3+foam)*smoothstep(0,.025,min(i.uv.y,1-i.uv.y)));
   }
   ENDHLSL
  }
 }
}
