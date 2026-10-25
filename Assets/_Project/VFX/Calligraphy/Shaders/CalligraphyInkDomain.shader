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
        _WaveAmplitude("Small surface ripples",Float)=.18
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
            float Fbm(float2 p)
            {
                float n=Noise(p)*.57;
                p=float2(p.x*.8-p.y*.6,p.x*.6+p.y*.8)*2.11+13.7;n+=Noise(p)*.28;
                p=float2(p.x*.8-p.y*.6,p.x*.6+p.y*.8)*2.07+7.3;n+=Noise(p)*.15;
                return n;
            }
            float2 SurfaceFlow(float2 world,float time,out float spiral,out float feather)
            {
                float2 grid=world*(.055*max(.25,_VortexDensity));
                float2 cell=floor(grid),advection=0;
                spiral=0;feather=0;
                // Jittered local vortices rotate at different speeds and in opposite directions.
                // Smooth finite influence prevents seams as the cell neighbourhood changes.
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 id=cell+float2(x,y);
                    float seed=Hash(id+23.4),other=Hash(id+61.8);
                    float2 centre=id+.18+float2(seed,other)*.64;
                    float2 stretch=float2(lerp(.8,1.22,other),lerp(.8,1.2,seed));
                    float2 delta=(grid-centre)*stretch;
                    float r=length(delta),radius=lerp(.55,.79,seed);
                    float influence=1-smoothstep(.06,radius,r);
                    float direction=seed>.5?1:-1;
                    float rotation=direction*(time*(.38+other*.35)+r*7)*influence*_VortexStrength;
                    float sine,cosine;sincos(rotation,sine,cosine);
                    float2 rotated=float2(delta.x*cosine-delta.y*sine,delta.x*sine+delta.y*cosine);
                    advection+=(rotated-delta)/stretch*influence;
                    float angle=atan2(delta.y+.0001,delta.x+.0001);
                    float warp=Noise(delta*3.6+seed*17+float2(time*.08,-time*.06));
                    float phase=angle*(1+floor(other*2))+r*(18+seed*9)-direction*time*(.8+other*.5)+warp*2.4;
                    float band=.5+.5*sin(phase);
                    float breakup=smoothstep(.2,.66,Noise(rotated*7+seed*31+time*.07));
                    spiral+=pow(saturate(band),7)*influence*breakup;
                    feather+=pow(saturate(.5+.5*sin(phase+.7)),11)*influence*breakup;
                }
                return (grid+advection*1.8)*3.2+float2(time*.09,-time*.075);
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
                    float boundary=smoothstep(0,.12,_Reveal-length(a.uv*2-1)+.08);
                    o.ws.y+=min(_WaveAmplitude,.35)*wave*.45*boundary;
                }
                o.positionCS=TransformWorldToHClip(o.ws);o.uv=a.uv;o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                if(_Sea>.5)
                {
                    float t=_Age*_ChurnSpeed,spiral,feather;
                    float2 p=SurfaceFlow(i.ws.xz,t,spiral,feather);
                    float2 curl=float2(Fbm(p*.62+float2(t*.04,0)),Fbm(p*.73+float2(19,-t*.05)))-.5;
                    float blotch=Fbm(p+curl*1.9);
                    float bleed=Fbm(p*2.6+curl*3.5+float2(-t*.11,t*.08));
                    float fibre=Noise(p*11+curl*4);
                    // Broad asymmetric ink islands keep stretching while finer pigment follows the spirals.
                    float pools=smoothstep(.28,.68,blotch+(.5-bleed)*.22);
                    float veins=pow(saturate(1-abs(bleed-.49)*7),3);
                    float radial=length(i.uv*2-1);
                    float border=Fbm(i.ws.xz*.12+float2(t*.09,-t*.065));
                    float edge=_Reveal*(.94+(border-.5)*.13)-radial;
                    float coverage=smoothstep(-.035,.035,edge);
                    // A diluted wet fringe remains just inside the sea's original footprint.
                    coverage=max(coverage,smoothstep(0,.035,_Reveal-radial)*.2);
                    float rim=exp(-abs(edge)*55);
                    half3 core=max(_BaseColor.rgb*.45,half3(.002,.003,.005));
                    half3 wash=half3(.055,.066,.074);
                    half3 ink=lerp(core,wash,pools*.72);
                    ink*=1-saturate(spiral*_VortexStrength*.72)*.78;
                    ink+=half3(.04,.047,.053)*(saturate(feather)*.6+veins*.18+rim*.1)*_EdgeStrength;
                    ink+=fibre*.0015;
                    half alpha=_Opacity*coverage*saturate(_Density*(.88+bleed*.24));
                    clip(alpha-.012);
                    return half4(MixFog(ink,i.fog),alpha);
                }
                // Rocks retain their original shading and do not evaluate the surface vortex field.
                float2 p=i.ws.xz*.17+i.ws.y*.04+_Seed;
                float n=Noise(p),bleed=Noise(p*3.7+float2(n,0));
                float3 normal=normalize(i.normal);
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
                half alpha=_Opacity*saturate(_Density*(.85+bleed*.3));
                clip(alpha-.012);return half4(MixFog(rock,i.fog),alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
