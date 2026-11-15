Shader "Project/Dojo Brush Ink Assembly"
{
 Properties{_BaseMap("Original brush texture",2D)="white"{} _BaseColor("Original brush color",Color)=(1,1,1,1) _Assembly("Assembly",Range(0,1))=0 _Reach("Brush half length",Float)=2}
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout"}
  Pass
  {
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A{float4 positionOS:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;};
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST;half4 _BaseColor;float _Assembly,_Reach;float4x4 _BrushWorldToLocal;
   CBUFFER_END
   V Vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.normal);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);return o;}
   half4 Frag(V i):SV_Target
   {
    float3 p=mul(_BrushWorldToLocal,float4(i.world,1)).xyz;
    float grain=sin(p.x*85+sin(p.z*43))*sin(p.y*69-p.z*39)*.035;
    float threshold=clamp(abs(p.z)/max(.01,_Reach)+grain,.015,.97);
    clip(_Assembly-threshold);
    half4 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;clip(base.a-.05);
    float edge=1-smoothstep(0,.12,_Assembly-threshold);
    Light light=GetMainLight();half shade=.3+.7*saturate(dot(normalize(i.normal),light.direction));
    half3 real=base.rgb*(shade*light.color+SampleSH(normalize(i.normal))*.25);
    half3 ink=lerp(half3(.005,.008,.012),half3(.2,.24,.26),sin(p.z*65+_Time.y*12)*.5+.5);
    return half4(lerp(real,ink,edge),1);
   }
   ENDHLSL
  }
 }
}
