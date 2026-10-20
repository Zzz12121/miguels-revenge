Shader "Project/VFX/CalligraphyInk"
{
    Properties
    {
        _BaseColor ("Ink pigment", Color) = (0.008,0.014,0.023,1)
        _DiluteColor ("Diluted pigment", Color) = (0.16,0.20,0.23,1)
        _Density ("Optical density", Range(0.1,6)) = 2.5
        _Flow ("Internal flow", Range(0,2)) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _DiluteColor;
                float _Density, _Flow;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float fbm(float2 p)
            {
                return noise(p)*0.55 + noise(p*2.03+13.1)*0.27 + noise(p*4.17+31.3)*0.13 + noise(p*8.3)*0.05;
            }
            V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color; return o; }
            half4 frag(V i):SV_Target
            {
                float2 p=(i.uv-0.5)*2;
                float t=_Time.y*_Flow;
                float2 warp=float2(fbm(p*2.3+float2(t,0)),fbm(p*2.3+float2(7,-t)))-0.5;
                float n=fbm(p*3.4+warp*2.7-float2(0,t));
                float edge=1-length(p+warp*0.33);
                float body=smoothstep(0.02,0.42,edge)*smoothstep(0.23,0.66,n);
                float filaments=pow(saturate(1-abs(n-0.48)*20),3)*smoothstep(0.02,0.3,edge)*0.15;
                float pigment=saturate(body+filaments);
                half alpha=(1-exp(-pigment*_Density))*i.color.a*_BaseColor.a;
                half3 col=lerp(_DiluteColor.rgb,_BaseColor.rgb,saturate(pigment*2.8))*i.color.rgb;
                return half4(col,alpha);
            }
            ENDHLSL
        }
    }
}
