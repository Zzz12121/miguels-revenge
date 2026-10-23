Shader "Project/VFX/CalligraphyEnvironment"
{
    Properties
    {
        _BaseMap("Surface",2D)="white"{}
        _BaseColor("Original tint",Color)=(1,1,1,1)
        _Blend("Ink transition",Range(0,1))=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST,_BaseColor;
                float _Blend;
            CBUFFER_END
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
            struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 world:TEXCOORD1;float2 uv:TEXCOORD2;};
            V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.world=TransformObjectToWorld(i.p.xyz);o.n=TransformObjectToWorldNormal(i.n);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
            float noise(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
            half4 frag(V i):SV_Target
            {
                half3 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                float3 normal=normalize(i.n);Light sun=GetMainLight();float lambert=saturate(dot(normal,sun.direction));
                half3 original=base*(SampleSH(normal)+sun.color*lambert);
                float luminance=dot(base,half3(.2126,.7152,.0722));
                float edge=pow(1-saturate(abs(dot(normal,normalize(GetWorldSpaceViewDir(i.world))))),4);
                float dry=noise(floor(i.world*120));
                float wash=smoothstep(.05,.85,luminance*.5+lambert*.45+.15);
                half3 ink=lerp(half3(.015,.022,.023),half3(.91,.90,.83),wash);
                ink*=1-edge*.65;ink*=.93+dry*.07;
                return half4(lerp(original,ink,_Blend),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
