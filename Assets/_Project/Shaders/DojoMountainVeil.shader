Shader "Project/Dojo Exterior Mountain Veil"
{
 Properties{_MistColor("Terrain mist",Color)=(0.72,0.78,0.81,0.78) _Density("Opacity",Range(0,1))=0.82}
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent"}
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   ZTest LEqual
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A{float4 positionOS:POSITION;float4 color:COLOR;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;half4 color:COLOR;};
   CBUFFER_START(UnityPerMaterial)
   half4 _MistColor;float _Density;float _ObserverFade;
   CBUFFER_END
   V Vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.color=a.color;return o;}
   float Field(float3 p){return sin(p.x*.34+sin(p.z*.19))*sin(p.z*.27+p.y*.21)*.5+.5;}
   half4 Frag(V i):SV_Target
   {
    if(_ObserverFade<.001)discard;
    float3 p=i.world+float3(_Time.y*.22,0,_Time.y*.13);
    float n=Field(p)*.65+Field(p*2.13)*.35;
    float edge=i.color.a;
    float alpha=_MistColor.a*_Density*lerp(.65,1,n)*edge*_ObserverFade;
    return half4(_MistColor.rgb+(.5-n)*.06,alpha);
   }
   ENDHLSL
  }
 }
}
