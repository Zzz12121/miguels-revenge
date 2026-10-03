Shader "Project/VFX/VoidRendSmoke"
{
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;float4 c:COLOR;};
   struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float4 c:COLOR;};
   V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.uv;o.c=i.c;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 a=floor(p),b=frac(p);b=b*b*(3-2*b);return lerp(lerp(hash(a),hash(a+float2(1,0)),b.x),lerp(hash(a+float2(0,1)),hash(a+1),b.x),b.y);}
   half4 frag(V i):SV_Target
   {
    float2 p=i.uv*2-1;
    float n=noise(i.uv*3+float2(_Time.y*.25,-_Time.y*.7));
    n+=noise(i.uv*7+_Time.y*.2)*.25;
    float a=pow(saturate(1-dot(p,p)),2)*(.25+n*.5)*i.c.a;
    return half4(lerp(i.c.rgb,float3(.4,.78,1),.65)*(1+n),a);
   }
   ENDHLSL
  }
 }
}
