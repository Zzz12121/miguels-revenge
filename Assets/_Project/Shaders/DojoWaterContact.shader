Shader "Project/Environment/Dojo Water Contact"
{
 Properties{_BaseMap("SUIMONO particle texture",2D)="white"{} _BaseColor("Tint",Color)=(.75,.88,.87,.7)}
 SubShader
 {
 Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Pass
 {
 Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor;float4 _BaseMap_ST;
 CBUFFER_END
 struct A{float4 p:POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;};
 struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;half fog:TEXCOORD1;};
 V Vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.c=i.c*_BaseColor;o.fog=ComputeFogFactor(o.p.z);return o;}
 half4 Frag(V i):SV_Target{half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);return half4(MixFog(i.c.rgb,i.fog),i.c.a*tex.a*tex.r);}
 ENDHLSL
 }
 }
}
