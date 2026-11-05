// 修羅場天空：夜空漸層＋星空（會閃爍）＋赤月（圓盤、表面斑紋、光暈）＋飄過的薄雲帶。
// 月亮方向由 _MoonDir 指定（ShuraArenaBuilder 會依月光方向自動設定）。
Shader "Shura/BloodMoonSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.05, 0.012, 0.018, 1)
        _HorizonColor ("Horizon", Color) = (0.42, 0.08, 0.05, 1)
        _GroundColor ("Below Horizon", Color) = (0.08, 0.02, 0.015, 1)
        _HorizonFalloff ("Horizon Falloff", Range(0.1, 2)) = 0.45

        [HDR] _MoonColor ("Moon", Color) = (2.2, 0.55, 0.35, 1)
        _MoonDir ("Moon Direction (world)", Vector) = (0.27, 0.34, 0.9, 0)
        _MoonSize ("Moon Angular Radius", Range(0.005, 0.2)) = 0.055
        _MoonMottle ("Moon Mottling", Range(0, 1)) = 0.45
        [HDR] _HaloColor ("Halo", Color) = (1.0, 0.16, 0.08, 1)
        _HaloFalloff ("Halo Falloff", Range(1, 40)) = 9
        _HaloIntensity ("Halo Intensity", Range(0, 3)) = 0.9

        [HDR] _StarColor ("Stars", Color) = (1.4, 1.3, 1.5, 1)
        _StarDensity ("Star Density", Range(0, 0.2)) = 0.06
        _StarScale ("Star Grid Scale", Float) = 260
        _StarSize ("Star Size", Range(0.02, 0.5)) = 0.16
        _StarTwinkle ("Star Twinkle", Range(0, 1)) = 0.6
        _StarHorizonFade ("Star Horizon Fade", Range(0.01, 1)) = 0.25

        _CloudColor ("Cloud", Color) = (0.07, 0.015, 0.015, 1)
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.75
        _CloudScale ("Cloud Scale", Float) = 1.6
        _CloudSpeed ("Cloud Speed", Float) = 0.02
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _ZenithColor, _HorizonColor, _GroundColor, _MoonColor, _HaloColor, _CloudColor, _StarColor;
            float _StarDensity, _StarScale, _StarSize, _StarTwinkle, _StarHorizonFade;
            float4 _MoonDir;
            float _HorizonFalloff, _MoonSize, _MoonMottle, _HaloFalloff, _HaloIntensity;
            float _CloudOpacity, _CloudScale, _CloudSpeed;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash3(i + float3(0, 0, 0)), hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += a * noise3(p); p *= 2.03; a *= 0.5; }
                return s;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = saturate(d.y);
                float down = saturate(-d.y);

                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, _HorizonFalloff));
                col = lerp(col, _GroundColor.rgb, saturate(down * 6.0));

                // 星空：把方向切成 3D 格子，每格隨機決定有沒有星星、星星在格子裡的位置與亮度
                float3 sp = d * _StarScale;
                float3 cell = floor(sp);
                float pick = hash3(cell);
                float3 starPos = float3(hash3(cell + 17.1), hash3(cell + 31.7), hash3(cell + 53.3));
                float starDist = length(frac(sp) - starPos);
                float starMask = step(1.0 - _StarDensity, pick);
                float twinkle = lerp(1.0, 0.5 + 0.5 * sin(_Time.y * (2.0 + 4.0 * hash3(cell + 7.7)) + pick * 40.0), _StarTwinkle);
                float star = starMask * (1.0 - smoothstep(0.0, _StarSize, starDist)) * twinkle * (0.4 + 0.6 * hash3(cell + 91.3));
                star *= saturate(d.y / _StarHorizonFade);

                // 赤月與光暈
                float3 m = normalize(_MoonDir.xyz);
                float ang = acos(clamp(dot(d, m), -1.0, 1.0));
                float disk = 1.0 - smoothstep(_MoonSize * 0.9, _MoonSize, ang);
                float mottle = fbm(d * 90.0);
                float3 moon = _MoonColor.rgb * lerp(1.0, 0.55 + 0.7 * mottle, _MoonMottle);
                float halo = exp(-ang * _HaloFalloff) * _HaloIntensity + exp(-ang * 2.2) * 0.25 * _HaloIntensity;
                col += _HaloColor.rgb * halo;
                col += _StarColor.rgb * star * (1.0 - saturate(halo * 1.5)); // 月亮附近的星星被光暈蓋掉
                col = lerp(col, moon, disk);

                // 薄雲帶（地平線附近被拉長），會遮住部分月亮
                float2 uv = d.xz / (up + 0.18);
                float t = _Time.y * _CloudSpeed;
                float c = fbm(float3(uv.x * _CloudScale * 0.6 + t, uv.y * _CloudScale * 2.4, t * 0.3));
                float cloud = smoothstep(0.48, 0.78, c) * saturate(d.y * 5.0) * saturate(1.2 - up);
                col = lerp(col, _CloudColor.rgb + _HaloColor.rgb * halo * 0.25, cloud * _CloudOpacity);

                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
