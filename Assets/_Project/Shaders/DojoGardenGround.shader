Shader "Project/Environment/Dojo Garden Ground"
{
    Properties
    {
        _BaseColor("Base Color",Color)=(.42,.45,.43,1)
        _PebbleScale("Pebbles Per Metre",Float)=28
        _Grass("Grass Soil Mode",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;float _PebbleScale,_Grass;
            CBUFFER_END
            float2 Hash(float2 p){return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453);}
            struct A { float4 p:POSITION;float3 n:NORMAL; };
            struct V { float4 p:SV_POSITION;float3 world:TEXCOORD0;half fog:TEXCOORD1; };
            V Vert(A i){V o;VertexPositionInputs v=GetVertexPositionInputs(i.p.xyz);o.p=v.positionCS;o.world=v.positionWS;o.fog=ComputeFogFactor(v.positionCS.z);return o;}
            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz*_PebbleScale;float2 cell=floor(p),f=frac(p);float closest=10;float2 seed=0;
                for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                {float2 c=float2(x,y);float2 h=Hash(cell+c);float d=length(c+h*.76+.12-f);if(d<closest){closest=d;seed=h;}}
                half grain=lerp(.58h,1.14h,smoothstep(.56,.12,closest));
                half variation=.72h+seed.x*.48h;
                float aa=saturate(length(fwidth(p))*.6);
                half gravel=lerp(grain*variation,.94h,aa);
                half soil=.80h+.17h*sin(i.world.x*2.8+sin(i.world.z*3.2))+.10h*sin(i.world.z*15.7+i.world.x*12.3);
                half3 albedo=_BaseColor.rgb*lerp(gravel,soil,_Grass);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                half3 lighting=SampleSH(half3(0,1,0))+light.color*(saturate(light.direction.y)*.72h+.12h)*lerp(.3h,1.0h,light.shadowAttenuation);
                return half4(MixFog(albedo*lighting,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;float _PebbleScale,_Grass;
            CBUFFER_END
            float4 Vert(float4 p:POSITION):SV_POSITION{return TransformObjectToHClip(p.xyz);}
            half4 Frag():SV_Target{return 0;}
            ENDHLSL
        }
    }
}
