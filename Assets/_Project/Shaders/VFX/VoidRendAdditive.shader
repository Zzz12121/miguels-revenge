Shader "Project/VFX/VoidRendAdditive"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (0.35, 0.9, 1, 1)
        _Intensity ("Intensity", Range(0, 12)) = 4
        _Softness ("Line Softness", Range(0.5, 8)) = 2
        _UseLineMask ("Use Line Mask", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VoidRendAdditive"
            Blend SrcAlpha One
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
                half _Intensity;
                half _Softness;
                half _UseLineMask;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half across = saturate(1.0h - abs(input.uv.y * 2.0h - 1.0h));
                half softCore = pow(across, _Softness);
                half ends = smoothstep(0.0h, 0.075h, input.uv.x) *
                            smoothstep(0.0h, 0.075h, 1.0h - input.uv.x);
                half lineMask = softCore * ends;
                half mask = lerp(1.0h, lineMask, _UseLineMask);
                half shimmer = 0.82h + 0.18h * sin(input.uv.x * 52.0h - _Time.y * 24.0h);
                half alpha = saturate(_BaseColor.a * input.color.a * mask);
                half3 rgb = _BaseColor.rgb * input.color.rgb * _Intensity * shimmer;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
