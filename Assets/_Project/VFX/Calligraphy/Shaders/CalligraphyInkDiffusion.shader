Shader "Project/VFX/CalligraphyInkDiffusion"
{
    Properties
    {
        _Opacity("Airborne ink opacity",Range(0,1))=.62
        _Branching("Fractal feathering",Range(0,1))=.8
        _Age("Diffusion clock",Float)=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5"}
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Opacity,_Branching,_Age;
            CBUFFER_END
            struct Attributes{float4 positionOS:POSITION;float2 uv:TEXCOORD0;float2 phase:TEXCOORD1;half4 colour:COLOR;};
            struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float2 phase:TEXCOORD1;half alpha:TEXCOORD2;half fog:TEXCOORD3;};
            float Hash(float2 p)
            {
                float3 q=frac(float3(p.xyx)*.1031);q+=dot(q,q.yzx+33.33);
                return frac((q.x+q.y)*q.z);
            }
            float Noise(float2 p)
            {
                float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);
            }
            float Fbm(float2 p)
            {
                float n=Noise(p)*.57;
                p=float2(p.x*.8-p.y*.6,p.x*.6+p.y*.8)*2.13+11.7;n+=Noise(p)*.28;
                p=float2(p.x*.8-p.y*.6,p.x*.6+p.y*.8)*2.07+19.3;n+=Noise(p)*.15;
                return n;
            }
            Varyings Vert(Attributes a)
            {
                Varyings o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;o.phase=a.phase;
                o.alpha=a.colour.a;o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 uv=i.uv;float seed=i.phase.x,life=i.phase.y;
                float2 p=float2(uv.x*4.4,uv.y*6.5)+float2(seed,seed*.37);
                // Slow advection along the ribbon and smaller nested curls at its edge.
                float clock=_Age*.28;
                float2 warp=float2(Fbm(p*.66+float2(0,-clock)),Fbm(p*.83+float2(8.2,-clock*.7)))-.5;
                float n=Fbm(p+warp*2.8+float2(0,-clock*.8));
                float fine=Noise(p*6.3+warp*3.2);
                float x=uv.x*2-1+(warp.x*.7)*lerp(.35,1,uv.y);
                float feather=lerp(.15,.48,_Branching);
                float width=lerp(.86,.58,uv.y)+(n-.5)*feather;
                float body=1-smoothstep(width*.32,width,abs(x));
                float filament=pow(saturate(1-abs(n*.8+fine*.2-.54)*5.4),2);
                float holes=smoothstep(.23,.67,n+fine*.16+(.5-uv.y)*.15);
                float tip=1-smoothstep(.65,1,uv.y+(n-.5)*.24);
                float root=smoothstep(0,.08,uv.y+.02);
                float detached=lerp(1,smoothstep(.02,.4,uv.y),smoothstep(.45,.85,life));
                half alpha=_Opacity*i.alpha*body*tip*root*detached*lerp(holes,holes*(.35+filament),_Branching*.72);
                // Charcoal-black cores dissolve to a pale ink wash; no glow or wet specular highlights.
                half wash=saturate((1-holes)*.5+life*.15);
                half3 colour=lerp(half3(.006,.009,.013),half3(.065,.075,.085),wash);
                clip(alpha-.003);
                return half4(MixFog(colour,i.fog),alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
