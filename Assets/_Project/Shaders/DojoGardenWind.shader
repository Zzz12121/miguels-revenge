Shader "Project/Environment/Dojo Garden Wind"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = .35
        _RootY("Mesh Root Y", Float) = 0
        _Height("Mesh Height", Float) = 1
        _UseVertexMask("Use UV1.y Root Mask", Range(0,1)) = 0
        _UseVertexColor("Use Vertex Color", Range(0,1)) = 0
        _WindDirection("Wind Direction XZ", Vector) = (1,0,.35,0)
        _BendStrength("Bend Distance (World Metres)", Range(0,2)) = .15
        _FlutterStrength("Leaf Flutter (World Metres)", Range(0,.3)) = .025
        _WindSpeed("Wind Speed", Range(0,5)) = 1
        _GustStrength("Gust Strength", Range(0,1)) = .4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _WindDirection;
            float _Cutoff, _RootY, _Height, _UseVertexMask, _UseVertexColor;
            float _BendStrength, _FlutterStrength, _WindSpeed, _GustStrength;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float2 mask : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float3 WindPosition(float3 p, float2 mask)
        {
            float3 world = TransformObjectToWorld(p);
            float2 direction = _WindDirection.xz / max(length(_WindDirection.xz), .001);
            float pin = lerp(saturate((p.y-_RootY)/max(_Height,.001)), saturate(mask.y), _UseVertexMask);
            float t = _Time.y * _WindSpeed;
            float phase = dot(world.xz,float2(.16,.11));
            float wave = sin(t*1.8-phase) * .65 + sin(t*.73+phase*.47)*.35;
            float gust = 1 + _GustStrength * sin(t*.41+phase*.22);
            float bend = pin*pin * _BendStrength * (wave*.45+.55) * gust;
            float flutter = sin(t*5.1+dot(world.xz,float2(2.7,1.9))) * _FlutterStrength * pin;
            world.xz += direction*bend + float2(-direction.y,direction.x)*flutter;
            world.y -= abs(bend)*.12*pin;
            return TransformWorldToObject(world);
        }
        half4 Albedo(float2 uv, half4 color)
        {
            half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv)*_BaseColor*lerp(half4(1,1,1,1),color,_UseVertexColor);
            clip(c.a-_Cutoff); return c;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;
                half4 color:COLOR;
                half fog:TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p=WindPosition(i.positionOS.xyz,i.mask);
                VertexPositionInputs v=GetVertexPositionInputs(p);
                o.positionCS=v.positionCS; o.positionWS=v.positionWS;
                o.normalWS=TransformObjectToWorldNormal(i.normalOS);
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); o.color=i.color;
                o.fog=ComputeFogFactor(v.positionCS.z); return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target
            {
                half4 c=Albedo(i.uv,i.color);
                half3 n=normalize(i.normalWS)*IS_FRONT_VFACE(facing,1,-1);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse=saturate(dot(n,light.direction))*.8h+.2h;
                half3 ambient=max(SampleSH(n),half3(.1,.1,.1));
                half3 lit=c.rgb*(ambient+light.color*diffuse*light.distanceAttenuation*lerp(.35h,1.0h,light.shadowAttenuation));
                return half4(MixFog(lit,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings ShadowVert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                float3 world=TransformObjectToWorld(WindPosition(i.positionOS.xyz,i.mask));
                float3 n=TransformObjectToWorldNormal(i.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection=normalize(_LightPosition-world);
                #else
                    float3 lightDirection=_LightDirection;
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(world,n,lightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #else
                    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #endif
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); o.color=i.color; return o;
            }
            half4 ShadowFrag(Varyings i):SV_Target { Albedo(i.uv,i.color); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings DepthVert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i); Varyings o;
                o.positionCS=TransformObjectToHClip(WindPosition(i.positionOS.xyz,i.mask));
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); o.color=i.color; return o;
            }
            half4 DepthFrag(Varyings i):SV_Target { Albedo(i.uv,i.color); return 0; }
            ENDHLSL
        }
    }
}
