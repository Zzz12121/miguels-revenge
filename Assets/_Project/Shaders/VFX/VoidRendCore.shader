Shader "Project/VFX/VoidRendCore"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.005, 0.008, 0.02, 0.95)
        _Softness ("Line Softness", Range(0.5, 8)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VoidRendCore"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Softness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half across = saturate(1.0h - abs(input.uv.y * 2.0h - 1.0h));
                half mask = pow(across, _Softness);
                half ends = smoothstep(0.0h, 0.1h, input.uv.x) *
                            smoothstep(0.0h, 0.1h, 1.0h - input.uv.x);
                return half4(_BaseColor.rgb, _BaseColor.a * input.color.a * mask * ends);
            }
            ENDHLSL
        }
    }
}
