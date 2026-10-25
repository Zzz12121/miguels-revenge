Shader "Project/VFX/CalligraphyInkDomain"
{
    Properties
    {
        _BaseColor("Dense ink",Color)=(.006,.012,.018,1)
        _Opacity("Opacity",Range(0,1))=1
        _Reveal("Expanding radius",Range(0,1))=1
        _Density("Ink concentration",Range(.1,2))=1
        _Sea("Sea surface (otherwise rock)",Float)=1
        _Seed("Ink variation",Float)=0
        _WaveAmplitude("Wave height",Float)=2.4
        _ChurnSpeed("Churning speed",Float)=.85
        _VortexDensity("Vortex density",Float)=1
        _VortexStrength("Vortex strength",Float)=1.3
        _EdgeStrength("Broken ink traces",Float)=.8
        _Age("Impact clock",Float)=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10"}
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Opacity,_Reveal,_Density,_Sea,_Seed;
                float _WaveAmplitude,_ChurnSpeed,_VortexDensity,_VortexStrength,_EdgeStrength,_Age;
            CBUFFER_END
            struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
            struct Varyings{float4 positionCS:SV_POSITION;float3 ws:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;};
            float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float Fracture(float2 p)
            {
                float2 cell=floor(p),f=frac(p);float first=8,second=8;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 q=float2(x,y),j=cell+q;
                    float2 d=q+float2(Hash(j),Hash(j+17.7))-f;float d2=dot(d,d);
                    second=min(second,max(first,d2));first=min(first,d2);
                }
                return 1-smoothstep(.008,.065,second-first);
            }
            Varyings Vert(Attributes a)
            {
                Varyings o;o.ws=TransformObjectToWorld(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normalOS);
                if(_Sea>.5 && _Sea<1.5)
                {
                    float2 p=a.positionOS.xz;float t=_Age*_ChurnSpeed;
                    float r=length(p),warp=Noise(p*.07+float2(t*.08,-t*.05))*3;
                    float a1=dot(p,float2(.27,.16))+t*1.4+warp;
                    float a2=dot(p,float2(-.19,.34))-t*1.13+warp*.5;
                    float a3=r*.39-t*2.3+Noise(p*.13)*2.7;
                    float wave=.43*sin(a1)+.3*sin(a2)+.22*sin(a3)*(.55+exp(-r*.035));
                    float crest=pow(saturate(.5+.5*sin(a1+a2*.4)),3)*.7;
                    float boundary=smoothstep(0,.12,_Reveal-length(a.uv*2-1)+.08);
                    o.ws.y+=_WaveAmplitude*max(.08,.85+wave+crest)*boundary;
                    o.ws.xz+=_WaveAmplitude*.32*float2(cos(a1),cos(a2))*boundary;
                }
                o.positionCS=TransformWorldToHClip(o.ws);o.uv=a.uv;o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float sea=step(.5,_Sea),t=_Age*_ChurnSpeed;
                float2 p=lerp(i.ws.xz*.17+i.ws.y*.04,i.ws.xz*.13,sea)+_Seed;
                float2 curl=float2(Noise(p*_VortexDensity+float2(t*.12,0)),Noise(p*_VortexDensity+float2(9,-t*.09)));
                p+=(curl-.5)*_VortexStrength*2*sea;
                float n=Noise(p+float2(t*.14,-t*.08)*sea);
                float bleed=Noise(p*3.7+float2(n,-t*.22*sea));
                float radial=length(i.uv*2-1);
                float edge=_Reveal*1.12-radial+(.5-n)*.11;
                float coverage=_Sea>.5 && _Sea<1.5?smoothstep(-.025,.035,edge):1;
                float rim=_Sea>.5 && _Sea<1.5?exp(-abs(edge)*52):0;
                float3 geom=normalize(cross(ddy(i.ws),ddx(i.ws)));
                geom=geom.y<0 && sea>.5?-geom:geom;
                float3 normal=sea>.5?geom:normalize(i.normal);
                float light=.2+.8*saturate(dot(normal,normalize(float3(.35,.8,-.4))));
                float layers=Noise(float2(i.ws.y*.9+n*2,i.ws.x*.24+i.ws.z*.24));
                float top=saturate(normal.y*normal.y);
                float2 rockUV=lerp(float2(i.ws.x*.6+i.ws.z*.7,i.ws.y*.28),i.ws.xz*.7,top)+_Seed;
                float cracks=Fracture(rockUV)*smoothstep(.2,.6,Noise(rockUV*2.2));
                float grainRock=Noise(rockUV*8)*.55+Noise(rockUV*23)*.3+Noise(rockUV*67)*.15;
                float brushgrain=Noise(float2(i.ws.x*2+i.ws.z*1.7,i.ws.y*.28));
                half3 rock=lerp(half3(.024,.034,.043),half3(.3,.33,.34),layers*.25+grainRock*.5+brushgrain*.25)*light;
                rock*=1-cracks*.72;
                rock+=top*half3(.065,.074,.08)*grainRock;
                float grain=Noise(p*45)*.08;
                half3 ink=lerp(half3(.004,.007,.011),half3(.075,.092,.1),n*n)*light;
                float fresnel=pow(1-saturate(dot(normal,GetWorldSpaceNormalizeViewDir(i.ws))),3);
                ink+=half3(.08,.09,.085)*fresnel*(.2+.8*bleed);
                float streak=pow(saturate(bleed*.7+n*.3-.52)*2.1,3)*saturate(normal.y)*_EdgeStrength;
                ink+=half3(.3,.31,.28)*(streak*.85+rim*.65)+grain*.04;
                half3 colour=lerp(rock,ink,sea);
                if(_Sea>1.5)coverage*=smoothstep(0,.08,i.uv.x)*smoothstep(0,.08,1-i.uv.x)*smoothstep(0,.1,1-i.uv.y);
                half alpha=_Opacity*coverage*saturate(_Density*(.85+bleed*.3));
                clip(alpha-.012);return half4(MixFog(colour,i.fog),alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
