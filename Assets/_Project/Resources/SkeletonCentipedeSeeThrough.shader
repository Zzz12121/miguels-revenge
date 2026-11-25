// 百足駭骨擋住鏡頭看玩家的視線時用的半透明材質：顏色變淡（往灰白靠）、可以看穿後面的玩家。
// 由 SkeletonCentipedeSeeThrough 在執行時換上（複製原本材質的貼圖 / 顏色），不擋的時候換回原本的材質。
//   _Alpha 1 = 不透明，越小越透明
//   _Pale  0 = 原本的顏色，1 = 完全變成 _PaleColor
// 先畫一次只寫深度（不畫顏色），再畫半透明的顏色：骨頭重疊的地方只看得到最前面那層，不會一團亂。
Shader "Project/VFX/SkeletonCentipedeSeeThrough"
{
    Properties
    {
        _BaseMap("Base", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Alpha("Alpha", Range(0,1)) = 0.35
        _Pale("Pale", Range(0,1)) = 0.5
        _PaleColor("Pale Color", Color) = (0.86, 0.9, 0.95, 1)
        _RimColor("Rim Color", Color) = (0.75, 0.85, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor, _PaleColor, _RimColor;
            float _Alpha, _Pale;
        CBUFFER_END
        ENDHLSL

        // 1. 只寫深度
        Pass
        {
            Name "DepthPrepass"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS:POSITION; };
            float4 vert(A a) : SV_POSITION { return TransformObjectToHClip(a.positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // 2. 半透明顏色（淡化 + 邊緣一點冷光，看得出輪廓）
        Pass
        {
            Name "ForwardTransparent"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back
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
                float3 n = normalize(i.normalWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                Light l = GetMainLight();
                half3 lit = albedo * (SampleSH(n) + l.color * saturate(dot(n, l.direction)));
                lit = lerp(lit, _PaleColor.rgb * (0.65 + 0.35 * saturate(dot(n, l.direction))), _Pale);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 2.5);
                lit += _RimColor.rgb * rim * 0.35 * _Pale;
                return half4(lit, saturate(_Alpha + rim * 0.25 * _Alpha));
            }
            ENDHLSL
        }

        // 3. 影子照舊（透明的時候地上的影子不要不見）
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
            float4 vert(A a) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(a.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(a.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 dir = normalize(_LightPosition - p);
            #else
                float3 dir = _LightDirection;
            #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, nWS, dir));
            #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
            #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return c;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
