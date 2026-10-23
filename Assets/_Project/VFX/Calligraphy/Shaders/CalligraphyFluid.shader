Shader "Project/VFX/CalligraphyFluid"
{
    Properties
    {
        _BaseColor("Ink / paper",Color)=(0.012,0.018,0.022,1)
        _Fade("Local erosion",Range(0,1))=0
        _Opacity("Opacity",Range(0,1))=1
        _Mode("0 ribbon, 1 sculpture, 2 paper, 3 yin yang",Float)=0
        _Flow("Flow speed",Float)=0.6
        _Seed("Seed",Float)=0
        _FaceTex("Elder visage albedo",2D)="white" {}
        _EyeVisibility("Sky eye ink definition",Range(0,1))=1
        _EyeCenter("Eye half-spacing / height",Vector)=(.035,.07,0,0)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Fade,_Opacity,_Mode,_Flow,_Seed;
            float _EyeVisibility;
            float4 _EyeCenter;
            CBUFFER_END
            TEXTURE2D(_FaceTex); SAMPLER(sampler_FaceTex);
            struct A {float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR;};
            struct V {float4 p:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; float3 n:TEXCOORD2; float3 local:TEXCOORD3; half4 color:COLOR;};
            float hash(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p)
            {
                float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float fbm(float2 p) {return noise(p)*.57+noise(p*2.13)*.28+noise(p*4.1)*.15;}
            V vert(A i)
            {
                V o; o.world=TransformObjectToWorld(i.p.xyz);
                o.p=TransformWorldToHClip(o.world); o.n=TransformObjectToWorldNormal(i.n);
                o.uv=i.uv; o.color=i.color;o.local=i.p.xyz; return o;
            }
            half4 frag(V i,bool frontFace:SV_IsFrontFace):SV_Target
            {
                float2 flow=i.world.xz*2.4+i.world.y*.7;
                flow+=float2(_Time.y*_Flow*.22,_Seed);
                float n=fbm(flow+float2(fbm(flow+3),fbm(flow-4))*1.7);
                float erosion=smoothstep(_Fade*.98,_Fade*.98+.14,n);
                float alpha=erosion*_Opacity*_BaseColor.a;
                half3 color=_BaseColor.rgb;
                if(_Mode<.5)
                {
                    float2 uv=i.uv;
                    float warp=fbm(float2(uv.x*7-_Time.y*_Flow,uv.y*4)+_Seed);
                    float edge=abs(uv.y-.5)*2;
                    float fibers=fbm(float2(uv.x*4-_Time.y*_Flow*.3,uv.y*38)+warp);
                    alpha*=smoothstep(1, .22, edge+(warp-.5)*.55)*lerp(.25,1,fibers);
                    alpha*=smoothstep(0,.08,uv.x)*smoothstep(1,.7,uv.x);
                    color=lerp(color,color+.12,saturate(fibers-.55));
                }
                else if(_Mode<1.5)
                {
                    float light=saturate(dot(normalize(i.n),normalize(float3(-.4,.8,.3))));
                    // Pigment absorbs light; never turn the dragon into a grey plastic tube.
                    color=lerp(color,half3(.035,.04,.045),light*.16+n*.1);
                    alpha*=smoothstep(.13,.36,n)*(.78+.22*noise(flow*3));
                    alpha*=smoothstep(.5,2.0,distance(_WorldSpaceCameraPos,i.world));
                }
                else if(_Mode<2.5)
                {
                    float2 cloudUV=(i.world.xz+i.world.y*.73)*.12;
                    float cloud=fbm(cloudUV+float2(_Time.y*.012,0)+fbm(cloudUV*1.8)*2.5);
                    // Quiet paper/cloud values leave the giant face and dark foot ripples readable.
                    color*=.88+smoothstep(.22,.72,cloud)*.10+noise(i.uv*500)*.012;
                    alpha=_Opacity*_BaseColor.a*smoothstep(_Fade*.98,_Fade*.98+.08,n);
                }
                else if(_Mode<3.5)
                {
                    float2 p=(i.uv-.5)*2;
                    float a=_Time.y*.27, c=cos(a),s=sin(a);
                    p=mul(float2x2(c,-s,s,c),p);
                    float radius=length(p);
                    float white=step(0,p.x);
                    white=lerp(white,1,1-smoothstep(.47,.51,length(p-float2(0,.5))));
                    white=lerp(white,0,1-smoothstep(.47,.51,length(p+float2(0,.5))));
                    white=lerp(white,0,1-smoothstep(.12,.14,length(p-float2(0,.5))));
                    white=lerp(white,1,1-smoothstep(.12,.14,length(p+float2(0,.5))));
                    color=lerp(half3(.006,.011,.013),half3(.94,.93,.87),white);
                    alpha*=smoothstep(1,.87,radius+(n-.5)*.1);
                }
                else if(_Mode<4.5)
                {
                    float grain=fbm(i.uv*float2(70,18)+float2(n*2,0));
                    float veins=pow(noise(i.uv*float2(200,9)+n),3);
                    color=lerp(color*.45,half3(.82,.82,.77),grain*.65);
                    color*=1-veins*.3;
                    alpha*=smoothstep(0,.18,i.uv.y)*(0.7+grain*.3);
                }
                else if(_Mode<5.5)
                {
                    // Hanging ink in water: connected dark pigment lobes with diffuse, torn edges.
                    // Unlike the sword wake, this uses low-frequency cross-flow, not parallel fibres.
                    float2 p=float2(i.uv.x*11,i.uv.y*3.5);
                    float2 warp=float2(fbm(p+_Time.y*.045),fbm(p+8-_Time.y*.06))-.5;
                    float pigment=fbm(p+warp*3.6);
                    float edge=abs(i.uv.y-.5)*2+(pigment-.5)*.75;
                    float wash=smoothstep(1.1,.2,edge)*smoothstep(.2,.62,pigment);
                    float tendril=pow(saturate(1-abs(pigment-.48)*16),3)*.22;
                    alpha*=saturate(wash*1.6+tendril*smoothstep(1,.35,edge));
                    alpha*=smoothstep(0,.06,i.uv.x)*smoothstep(1,.88,i.uv.x);
                }
                else
                {
                    clip(frontFace?1:-1);
                    // Broad ink-wash lighting preserves the elder's nose, eye sockets and beard.
                    float light=saturate(dot(normalize(i.n),normalize(float3(-.45,.65,-.6))));
                    float facing=abs(dot(normalize(i.n),normalize(_WorldSpaceCameraPos-i.world)));
                    half3 albedo=SAMPLE_TEXTURE2D(_FaceTex,sampler_FaceTex,i.uv).rgb;
                    float pigment=dot(albedo,half3(.3,.59,.11));
                    float broadWash=fbm(i.world.xy*.035+float2(_Time.y*.015,0));
                    color=lerp(half3(.012,.018,.022),half3(.49,.51,.53),pigment)*(.65+light*.35);
                    color*=.9+broadWash*.15;
                    // Ink-defined lids/iris on the existing facial surface, not floating spheres.
                    // Soften the original model's circular dark eye patches into narrow sockets.
                    float2 eye=float2(abs(i.local.x)-_EyeCenter.x,i.local.y-_EyeCenter.y);
                    float socket=(1-smoothstep(.8,1.25,length(eye/float2(.023,.022))))*smoothstep(.055,.08,i.local.z);
                    float opening=1-smoothstep(.85,1.05,length(eye/float2(.016,.005)));
                    float pupil=1-smoothstep(.8,1.15,length((eye-float2(-.001,-.0007))/float2(.0035,.0035)));
                    float lid=(1-smoothstep(.0008,.0025,abs(eye.y-(.0055-pow(eye.x/.018,2)*.004))))*(1-smoothstep(.015,.021,abs(eye.x)));
                    half3 skinWash=half3(.49,.51,.53)*.8*(.65+light*.35)*(.9+broadWash*.15);
                    half3 eyeInk=lerp(skinWash,skinWash+.065,opening);
                    eyeInk=lerp(eyeInk,half3(.045,.055,.062),saturate(pupil*opening+lid*.75));
                    color=lerp(color,eyeInk,socket*_EyeVisibility);
                    alpha*=smoothstep(.02,.3,facing)*lerp(.65,1,broadWash);
                }
                return half4(color,alpha*i.color.a);
            }
            ENDHLSL
        }
    }
}
