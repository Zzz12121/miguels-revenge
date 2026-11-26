Shader "Project/InkboundTown/Entry Scroll"
{
    Properties{_PaperColor("Paper",Color)=(.957,.949,.918,1)}
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _PaperColor;
            CBUFFER_END
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
            V vert(A i){V o;i.p.z+=sin(i.uv.x*13+_Time.y*.7)*.008*sin(i.uv.y*3.14159);o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.uv;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float n(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
            half4 frag(V i):SV_Target
            {
                float2 p=i.uv;float ink=0;
                for(int layer=0;layer<3;layer++)
                {
                    float h=.72+layer*.07+(n(float2(p.x*(5+layer*3),layer+3))-.5)*.23;
                    float mountain=(1-smoothstep(h-.025,h+.01,p.y))*smoothstep(.50+layer*.055,.70,p.y);
                    ink=max(ink,mountain*(.10-layer*.025));
                }
                float depth=saturate((.67-p.y)/.55),bankWidth=.045+depth*.35;
                float bank=step(.5+bankWidth,abs(p.x-.5)+.5);
                ink=max(ink,bank*.05);
                for(int j=0;j<7;j++)for(int s=-1;s<=1;s+=2)
                {
                    float d=j/7.0,y=.14+d*.47,w=.16*(1-d)+.025,x=.5+s*(.14+(1-d)*.27),height=.09*(1-d)+.025;
                    float wall=step(abs(p.x-x),w*.5)*step(abs(p.y-y),height*.5);
                    float roof=step(abs(p.x-x),w*.64)*step(abs(p.y-(y+height*.65)+abs(p.x-x)*.22),.009+.014*(1-d));
                    float windows=step(abs(p.x-x),w*.35)*step(abs(p.y-y),height*.18)*(1-smoothstep(.35,.42,abs(frac((p.x-x)/w*6)-.5)));
                    ink=max(ink,wall*.10+roof*.55+windows*.18);
                }
                float arch=sqrt(max(0,1-pow((p.x-.5)/.13,2)))*.065+.43;
                ink=max(ink,(1-smoothstep(.003,.009,abs(p.y-arch)))*step(abs(p.x-.5),.13)*.38);
                ink+=smoothstep(.72,.88,n(float2(p.x*7,p.y*135+_Time.y*.10)))*step(p.y,.43)*.035;
                ink*=.85+n(p*23)*.15;
                return half4(_PaperColor.rgb*(1-ink),1);
            }
            ENDHLSL
        }
    }
}
