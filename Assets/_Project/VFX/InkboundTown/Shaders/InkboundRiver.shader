Shader "Project/InkboundTown/Painted River"
{
    Properties{_BaseColor("Paper water",Color)=(.957,.949,.918,1) _ReflectionStrength("Reflection",Range(0,1))=.32}
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10"}
        Cull Off
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;float _ReflectionStrength;
            CBUFFER_END
            TEXTURE2D(_InkTownReflection);SAMPLER(sampler_InkTownReflection);
            float4x4 _InkTownReflectionVP;
            float _InkTownReflectionReady;
            float4 _InkBoat0,_InkBoat1;
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);}
            float wake(float2 p,float4 boat)
            {
                float2 d=p-boat.xy;float back=-dot(d,boat.zw),side=abs(dot(d,float2(-boat.w,boat.z)));
                float envelope=smoothstep(0,.6,back)*(1-smoothstep(2.5,7,back))*step(.1,dot(boat.zw,boat.zw));
                float wakeLine=abs(side-(.55+back*.22+sin(back*2-_Time.y*.8)*.035));
                float broken=smoothstep(.25,.7,noise(float2(back*3,side*.8)));
                return (1-smoothstep(.025,.13,wakeLine))*envelope*broken;
            }
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float4 reflection:TEXCOORD1;float2 uv:TEXCOORD2;};
            V vert(A i){V o;o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.reflection=mul(_InkTownReflectionVP,float4(o.world,1));o.uv=i.uv;return o;}
            half4 frag(V i):SV_Target
            {
                #if defined(DEBUG_DISPLAY)
                    half4 debugColor;if(CalculateColorForDebugSceneOverride(debugColor))return debugColor;
                #endif
                float2 uv=i.reflection.xy/max(.001,i.reflection.w)*.5+.5;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y=1-uv.y;
                #endif
                float band=i.world.z*6.3+noise(i.world.xz*.7);
                float broken=noise(float2(i.world.x*.85,band+_Time.y*.08));
                uv.x+=(noise(float2(band*.17,_Time.y*.11))-.5)*.018;
                uv.y+=(noise(float2(i.world.x*.6,band*.23))-.5)*.009;
                half3 reflected=SAMPLE_TEXTURE2D(_InkTownReflection,sampler_InkTownReflection,uv).rgb;
                float ink=saturate((.90-dot(reflected,half3(.299,.587,.114)))/.90);
                float bank=pow(abs(i.uv.x-.5)*2,2.4);
                float reflectionInk=saturate(ink-.045)*(.32+.68*smoothstep(.20,.67,broken))*(.42+bank*.58);
                half3 color=_BaseColor.rgb-reflectionInk*_ReflectionStrength*_InkTownReflectionReady;
                float stroke=noise(float2(i.world.x*.29,i.world.z*8+_Time.y*.07));
                float narrow=smoothstep(.75,.92,stroke)*smoothstep(.30,.66,noise(i.world.xz*.72));
                color-=narrow*(.065+bank*.12);
                float broadWash=noise(float2(i.world.x*.11,i.world.z*.18-_Time.y*.009));
                color-=smoothstep(.16,.82,broadWash)*.075;
                float midStroke=noise(float2(i.world.x*.66,i.world.z*4.2+_Time.y*.05));
                color-=smoothstep(.68,.85,midStroke)*smoothstep(.28,.60,noise(i.world.xz*.27))*.095;
                float wetBank=pow(bank,1.35)*noise(i.world.xz*.58);
                color-=wetBank*.20;
                color-=(wake(i.world.xz,_InkBoat0)+wake(i.world.xz,_InkBoat1))*.15;
                color+=(noise(i.world.xz*125)-.5)*.008;
                float fog=smoothstep(65,175,distance(_WorldSpaceCameraPos,i.world));
                return half4(lerp(color,half3(.94,.935,.90),fog),1);
            }
            ENDHLSL
        }
    }
}
