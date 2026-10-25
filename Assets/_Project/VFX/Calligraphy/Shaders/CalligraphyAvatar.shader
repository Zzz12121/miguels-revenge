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
        _EyeVisibility("Gaze definition",Float)=.9
        _EyeContrast("Gaze contrast",Float)=1
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
                float _AvatarScale,_FlowSpeed,_BodyDeform,_EyeVisibility,_EyeContrast,_RobeSpeed;
                float4 _HeadCenter,_HeadRight,_HeadUp,_HeadForward,_EyeLeft,_EyeRight,_Gaze,_HairMotion,_MotionLag,_RightHand,_LeftHand;
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
                float hair=smoothstep(.8,2.3,abs(h.x))*(1-smoothstep(5,13,abs(h.y)))+smoothstep(.2,2,-h.z)*(1-smoothstep(3,11,abs(h.y)));
                hair=saturate(hair);
                float stableFace=(1-smoothstep(1.1,2.3,abs(h.x)))*(1-smoothstep(1.5,3.8,abs(h.y)))*smoothstep(-1,.4,h.z);
                hair*=1-stableFace;
                float beard=smoothstep(.3,3,-h.y)*(1-smoothstep(6,12,-h.y))*(1-smoothstep(1.5,4,abs(h.x)))*smoothstep(-.6,1,h.z);
                float robe=smoothstep(9,23,-h.y)*smoothstep(2,10,abs(h.x))+.35*smoothstep(26,42,-h.y);
                float handDistance=min(distance(o.ws,_RightHand.xyz),distance(o.ws,_LeftHand.xyz))/scale;
                float stableHands=smoothstep(1,3,handDistance);
                float t=_Time.y,phase=h.y*.21+h.x*.37;
                float flutter=sin(t*_HairMotion.y+phase)+.32*sin(t*_HairMotion.y*1.73+h.x*.8-h.y*.14);
                float heavy=sin(t*_HairMotion.y*.63+h.y*.26+.7);
                float cloth=sin(t*_RobeSpeed+h.x*.3-h.y*.18)+.38*sin(t*_RobeSpeed*1.91+h.z*.4+h.y*.31);
                float3 flow=_HeadRight.xyz*(hair*flutter*_HairMotion.x+beard*heavy*_HairMotion.z+robe*cloth*_HairMotion.w);
                flow+=_HeadForward.xyz*(hair*.5*flutter*_HairMotion.x+beard*abs(heavy)*_HairMotion.z+robe*.3*cloth*_HairMotion.w);
                flow+=_MotionLag.xyz*saturate(hair+beard+robe)*smoothstep(0,3,abs(h.y));
                o.ws+=flow*_BodyDeform*stableHands*(1-stableFace);
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
                float scale=max(.1,_AvatarScale);
                float3 eyeDelta=i.ws-(distance(i.ws,_EyeLeft.xyz)<distance(i.ws,_EyeRight.xyz)?_EyeLeft.xyz:_EyeRight.xyz);
                float2 eye=float2(dot(eyeDelta,_HeadRight.xyz),dot(eyeDelta,_HeadUp.xyz))/scale;
                float front=smoothstep(.12,.6,dot(n,_HeadForward.xyz))*smoothstep(-.7,.1,dot(eyeDelta,_HeadForward.xyz)/scale);
                float socket=1-smoothstep(.8,1.3,length(eye/float2(.68,.32)));
                float opening=1-smoothstep(.82,1.08,length(eye/float2(.5,.105)));
                float pupil=1-smoothstep(.8,1.1,length((eye-_Gaze.xy)/float2(.11,.14)));
                float lid=exp(-abs(eye.y-.13+abs(eye.x)*.13)*27)*(1-smoothstep(.4,.68,abs(eye.x)));
                half3 eyeInk=lerp(colour*.32,half3(.7,.71,.66)*_EyeContrast,opening);
                eyeInk=lerp(eyeInk,half3(.008,.009,.01),saturate(pupil*opening+lid*.9));
                colour=lerp(colour,eyeInk,socket*front*_EyeVisibility*_BodyDeform);
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
