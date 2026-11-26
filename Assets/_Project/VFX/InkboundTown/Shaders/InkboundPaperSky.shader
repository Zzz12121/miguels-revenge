Shader "Project/InkboundTown/Paper Sky"
{
    Properties { _Tint("Unpainted xuan paper",Color)=(.957,.949,.918,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            CBUFFER_END
            struct A{float4 p:POSITION;};
            struct V{float4 p:SV_POSITION;float3 ray:TEXCOORD0;};
            V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.ray=i.p.xyz;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            half4 frag(V i):SV_Target
            {
                float3 ray=normalize(i.ray);
                float2 uv=float2(atan2(ray.x,ray.z),asin(ray.y));
                float fibre=hash(floor(uv*float2(4800,2700)))-.5;
                float pulp=sin(uv.x*79+sin(uv.y*123))*sin(uv.y*137+sin(uv.x*51));
                return half4(_Tint.rgb+fibre*.012+pulp*.0015,1);
            }
            ENDHLSL
        }
    }
}
