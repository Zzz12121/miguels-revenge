// 打擊的空氣波動：一圈往外擴散的透明環，把它後面的畫面往外推（像熱浪 / 衝擊波）。
// 由 ShockwaveFx.cs 產生（面向鏡頭的方形面片），不要直接拿去貼在模型上。
// 需要 URP 的 Opaque Texture（PC_RPAsset 已經開著）；沒開的話 ShockwaveFx 不會產生它。
Shader "GeminiGrowUp/ShockwaveDistortion"
{
    Properties
    {
        _Progress ("Progress (環的半徑 0~1)", Range(0, 1)) = 0.3
        _Width ("Ring Width", Range(0.02, 0.6)) = 0.22
        _Strength ("Strength (推開多少，跟環的大小成比例)", Range(0, 1)) = 0.25
        _Fade ("Fade", Range(0, 1)) = 1
        _Tint ("Tint (環上的一點點亮光)", Color) = (1, 1, 1, 0.015)
        _Opacity ("Opacity (整體透明度：越小越淡，只剩扭曲的感覺)", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Shockwave"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Progress;
                float _Width;
                float _Strength;
                float _Fade;
                float4 _Tint;
                float _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float radiusNdc : TEXCOORD2;   // 面片半徑在畫面上有多大（NDC 的 x 單位）
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                float4 c = TransformObjectToHClip(float3(0, 0, 0));
                float4 e = TransformObjectToHClip(float3(0.5, 0, 0));
                o.radiusNdc = length(e.xy / e.w - c.xy / c.w);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;            // -1..1，中心 = 打中的點
                float r = length(c);
                float d = (r - _Progress) / max(_Width, 0.001);
                float ring = exp(-d * d * 3.0);          // 環的中間最強
                ring *= saturate((1.0 - r) / 0.12);      // 面片邊緣歸零
                float strength = ring * _Fade;
                float2 dir = r > 1e-4 ? c / r : float2(0, 0);

                // 環的半徑換成畫面 uv 的位移（x、y 依畫面長寬比換算，畫面上才是圓的）
                float2 off = dir * strength * _Strength * i.radiusNdc * 0.5;
                off.y *= _ScreenParams.x / _ScreenParams.y;
                float2 uv = i.screenPos.xy / i.screenPos.w;
                half3 col = SampleSceneColor(uv - off);
                col += _Tint.rgb * (_Tint.a * strength);
                return half4(col, saturate(strength * 1.6) * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
