Shader "Project/InkboundTown/Calligraphy Sign"
{
    Properties { _MainTex("Font atlas",2D)="white"{} }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off ZTest LEqual Cull Off
        Pass
        {
            Tags{"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            CBUFFER_END
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float3 world:TEXCOORD1;};
            V vert(A i){V o;o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.uv=i.uv;o.color=i.color;return o;}
            half4 frag(V i):SV_Target
            {
                half alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;
                float fade=1-smoothstep(40,150,distance(i.world,_WorldSpaceCameraPos));
                return half4(i.color.rgb,alpha*i.color.a*fade);
            }
            ENDHLSL
        }
    }
}
