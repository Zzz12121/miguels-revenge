// 百足駭骨分身倒下後「消散」用的材質：骨頭從上往下、一塊一塊碎成灰，邊緣帶一圈暗紅餘燼光。
// 由 SkeletonCentipedeDissolveVfx 在執行時換上（複製原本材質的貼圖 / 顏色），不用手動指定。
//   _Amount       0 = 完整，1 = 全部消失
//   _HeightMin/Max 模型在世界座標的最低 / 最高點（由腳本每幀更新），越高的地方越早散掉
Shader "Project/VFX/SkeletonCentipedeDissolve"
{
    Properties
    {
        _BaseMap("Base", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Amount("Dissolve", Range(0,1)) = 0
        [HDR] _EdgeColor("Edge Color", Color) = (2.4, 0.55, 0.18, 1)
        _EdgeWidth("Edge Width", Range(0.01, 0.3)) = 0.09
        _NoiseScale("Noise Scale (per m)", Float) = 2.2
        _HeightBias("Top-first Bias", Range(0,1)) = 0.45
        _HeightMin("Height Min", Float) = 0
        _HeightMax("Height Max", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor, _EdgeColor;
            float _Amount, _EdgeWidth, _NoiseScale, _HeightBias, _HeightMin, _HeightMax;
        CBUFFER_END

        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }
        float ValueNoise(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                             lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                             lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
        }
        // > 0 還留著，< 0 已經散掉；越接近 0 越靠近消散的邊緣
        float DissolveEdge(float3 positionWS)
        {
            float3 q = positionWS * _NoiseScale;
            float n = ValueNoise(q) * 0.65 + ValueNoise(q * 2.7 + 13.1) * 0.35;
            float h = saturate((positionWS.y - _HeightMin) / max(0.001, _HeightMax - _HeightMin));
            float v = lerp(n, 1.0 - h, _HeightBias);          // 越高越早散
            return v - lerp(-0.05, 1.05, _Amount);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; float3 normalWS:TEXCOORD2; };
            V vert(A a)
            {
                V o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float edge = DissolveEdge(i.positionWS);
                clip(edge);
                float3 n = normalize(i.normalWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                Light l = GetMainLight();
                half3 lit = albedo * (SampleSH(n) + l.color * saturate(dot(n, l.direction)));
                // 邊緣：先變焦黑，再亮成餘燼
                float glow = 1.0 - smoothstep(0.0, _EdgeWidth, edge);
                float char_ = 1.0 - smoothstep(0.0, _EdgeWidth * 2.5, edge);
                glow *= smoothstep(0.0, 0.04, _Amount);
                char_ *= smoothstep(0.0, 0.04, _Amount);
                lit *= lerp(1.0, 0.25, char_);
                return half4(lit + _EdgeColor.rgb * glow * glow, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            V vert(A a)
            {
                V o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(a.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 dir = normalize(_LightPosition - o.positionWS);
            #else
                float3 dir = _LightDirection;
            #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, nWS, dir));
            #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return o;
            }
            half4 frag(V i) : SV_Target { clip(DissolveEdge(i.positionWS)); return 0; }
            ENDHLSL
        }
    }
}
