Shader "Project/VFX/WeaponLightningDissolve"
{
 Properties { _BaseMap("Base",2D)="white"{} _BaseColor("Color",Color)=(1,1,1,1) _Amount("Dissolve",Range(0,1))=0 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST, _BaseColor;
   float _Amount;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION; float3 pos:TEXCOORD0; float2 uv:TEXCOORD1; float3 normal:TEXCOORD2; };
   V vert(A a) { V o; o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.pos=a.positionOS.xyz;
    o.uv=TRANSFORM_TEX(a.uv,_BaseMap); o.normal=TransformObjectToWorldNormal(a.normalOS); return o; }
   half4 frag(V i):SV_Target
   {
    float n=.5+.22*sin(dot(i.pos,float3(17,31,23)))+.16*sin(dot(i.pos,float3(53,11,37)))+.10*sin(dot(i.pos,float3(89,59,13)));
    float edge=n-lerp(-.12,1.12,_Amount); clip(edge);
    half3 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
    Light l=GetMainLight(); base*=SampleSH(normalize(i.normal)) + l.color*saturate(dot(normalize(i.normal),l.direction));
    float rim=(1-smoothstep(0,.13,edge))*smoothstep(0,.08,_Amount);
    return half4(lerp(base,float3(.035,.16,.32),rim*.45),1);
   }
   ENDHLSL
  }
 }
}
