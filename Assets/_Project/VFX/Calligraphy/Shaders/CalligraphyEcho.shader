Shader "Project/VFX/CalligraphyEcho"
{
    Properties
    {
        _BaseColor ("Ink",Color)=(0.012,0.021,0.028,1)
        _PaperColor ("Wash highlights",Color)=(0.55,0.64,0.66,1)
        _Fade ("Dissolve",Range(0,1))=0
        _Opacity ("Opacity",Range(0,1))=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor,_PaperColor;
                float _Fade,_Opacity;
            CBUFFER_END
            struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL;};
            struct V {float4 positionCS:SV_POSITION; float3 pos:TEXCOORD0; float3 normal:TEXCOORD1;};
            float hash(float3 p) {return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
            float noise(float3 p)
            {
                float3 a=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash(a),hash(a+float3(1,0,0)),f.x),lerp(hash(a+float3(0,1,0)),hash(a+float3(1,1,0)),f.x),f.y),
                lerp(lerp(hash(a+float3(0,0,1)),hash(a+float3(1,0,1)),f.x),lerp(hash(a+float3(0,1,1)),hash(a+1),f.x),f.y),f.z);
            }
            V vert(A i)
            {
                V o; o.pos=TransformObjectToWorld(i.positionOS.xyz);
                o.normal=TransformObjectToWorldNormal(i.normalOS);
                o.positionCS=TransformWorldToHClip(o.pos); return o;
            }
            half4 frag(V i):SV_Target
            {
                float3 flow=i.pos*5+float3(sin(i.pos.y*2+_Time.y*.5)*.35,_Time.y*.24,cos(i.pos.x*3+_Time.y*.4)*.3);
                float n=noise(flow)*0.7+noise(flow*3.6)*0.3;
                float mask=smoothstep(_Fade,_Fade+0.12,n);
                float light=saturate(dot(normalize(i.normal),normalize(float3(-0.4,0.8,0.3))));
                half3 c=lerp(_BaseColor.rgb,_PaperColor.rgb,smoothstep(0.35,0.9,light)*0.28);
                float edge=abs(dot(normalize(i.normal),normalize(GetWorldSpaceViewDir(i.pos))));
                float wash=lerp(.35,.95,smoothstep(.15,.7,n))*smoothstep(0,.23,edge+n*.15);
                return half4(c,mask*(1-_Fade)*_BaseColor.a*_Opacity*wash);
            }
            ENDHLSL
        }
    }
}
