Shader "Project/VFX/CalligraphyAvatar"
{
    Properties
    {
        _BaseMap("Original face and robe",2D)="white"{}
        _BaseColor("Ink",Color)=(0.012,0.02,0.026,1)
        _Opacity("Opacity",Range(0,1))=.78
        _Visibility("Summon visibility",Range(0,1))=1
        _FlowStrength("Flow",Range(0,2))=.65
        _FaceClarity("Face clarity",Range(0,1))=.86
        _CutY("Upper body cut height",Float)=-1000
        _FaceY("Face world height",Float)=45
        _FaceRange("Face band",Float)=15
        _AvatarScale("Size factor",Float)=1
        _FlowSpeed("Ink speed",Float)=1
        _BodyDeform("Deform clothing only",Float)=0
        _RobeAmplitude("Robe motion",Float)=2.8
        _RobeSpeed("Robe speed",Float)=.65
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Opacity,_Visibility,_FlowStrength,_FaceClarity,_CutY,_FaceY,_FaceRange;
                float _AvatarScale,_FlowSpeed,_BodyDeform,_RobeAmplitude,_RobeSpeed;
                float4 _HeadCenter,_HeadRight,_HeadUp,_HeadForward,_ClothTop,_MotionLag,_RightHand,_LeftHand;
            CBUFFER_END
            struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
            struct Varyings{float4 positionCS:SV_POSITION;float3 ws:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;};
            float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            Varyings Vert(Attributes a)
            {
                Varyings o;o.ws=TransformObjectToWorld(a.positionOS.xyz);
                o.normal=TransformObjectToWorldNormal(a.normalOS);
                float scale=max(.1,_AvatarScale);
                float3 relative=(o.ws-_HeadCenter.xyz)/scale;
                float3 h=float3(dot(relative,_HeadRight.xyz),dot(relative,_HeadUp.xyz),dot(relative,_HeadForward.xyz));
                // This rig has no separate hair geometry/bones. Keep the entire head and neck rigid.
                // The posed chest plane gates every displacement, including inertial movement.
                float clothOnly=smoothstep(0,4,dot(_ClothTop.xyz-o.ws,_HeadUp.xyz)/scale);
                float robe=smoothstep(9,23,-h.y)*smoothstep(2,10,abs(h.x))+.35*smoothstep(26,42,-h.y);
                float handDistance=min(distance(o.ws,_RightHand.xyz),distance(o.ws,_LeftHand.xyz))/scale;
                float stableHands=smoothstep(1,3,handDistance);
                float t=_Time.y;
                float cloth=sin(t*_RobeSpeed+h.x*.3-h.y*.18)+.38*sin(t*_RobeSpeed*1.91+h.z*.4+h.y*.31);
                float3 flow=(_HeadRight.xyz+_HeadForward.xyz*.3)*robe*cloth*_RobeAmplitude;
                flow+=_MotionLag.xyz*saturate(robe)*smoothstep(0,3,abs(h.y));
                o.ws+=flow*_BodyDeform*stableHands*clothOnly;
                o.positionCS=TransformWorldToHClip(o.ws);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);
                o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float face=saturate(1-abs(i.ws.y-_FaceY)/max(1,_FaceRange))*_FaceClarity;
                float2 flowUV=i.ws.xz*.095+float2(i.ws.y*.065,-_Time.y*.14*_FlowSpeed);
                float wet=Noise(flowUV+Noise(flowUV*.7)*(_FlowStrength*2.76923));
                float fibre=Noise(i.ws.xy*.85+float2(_Time.y*.11,0));
                float reveal=Noise(i.ws.xy*.11+float2(0,-_Time.y*.09));
                float visibility=smoothstep(reveal-.16,reveal+.16,_Visibility*1.35-.12);
                float cut=smoothstep(_CutY-.4,_CutY+2.8+wet*2,i.ws.y);
                float3 n=normalize(i.normal);
                float rim=pow(1-saturate(abs(dot(n,GetWorldSpaceNormalizeViewDir(i.ws)))),2);
                float erosion=lerp(smoothstep(.12,.45,wet+fibre*.3),1,face*.8);
                half3 original=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half detail=dot(original,half3(.299,.587,.114));
                // Retain the original mesh normals and albedo detail; the face is not replaced by black fog.
                half light=.52+.48*saturate(dot(n,normalize(float3(.35,.65,-.55))));
                half wash=(.035+pow(detail,1.25)*lerp(.6,1.12,face))*light;
                half3 colour=lerp(_BaseColor.rgb,half3(wash, wash*.99, wash*.94),.64+face*.32);
                float inkVein=smoothstep(.35,.69,wet+fibre*.18);
                colour*=lerp(lerp(.2,.7,face),1,inkVein);
                colour+=rim*half3(.34,.35,.33)*(.18+face*.08);
                half alpha=_Opacity*_BaseColor.a*visibility*cut*lerp(.48,1,erosion);
                alpha=lerp(alpha,visibility*cut*max(_Opacity,.93),face*.75);
                clip(alpha-.008);
                return half4(MixFog(colour,i.fog),alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
