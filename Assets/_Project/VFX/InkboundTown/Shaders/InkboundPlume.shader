Shader "Project/InkboundTown/Returning Pigment"
{
    Properties{_BaseColor("Ink",Color)=(.025,.03,.035,1) _VertexTint("Use vertex pigment",Range(0,1))=0 _FluidMask("Fluid density atlas",2D)="white"{} _UseFluidMask("Fluid density",Range(0,1))=0}
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            Tags{"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;float _VertexTint,_UseFluidMask;
            CBUFFER_END
            TEXTURE2D(_FluidMask);SAMPLER(sampler_FluidMask);
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;float2 brush:TEXCOORD1;half4 color:COLOR;};
            struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float2 brush:TEXCOORD1;float3 world:TEXCOORD2;};
            V vert(A i){V o;o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.uv=i.uv;o.color=i.color;o.brush=i.brush;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
            half4 frag(V i):SV_Target
            {
                #if defined(DEBUG_DISPLAY)
                    half4 debugColor;if(CalculateColorForDebugSceneOverride(debugColor))return debugColor;
                #endif
                float2 flow=float2(_Time.y*.19,-_Time.y*.14)+i.brush.y;
                float n=noise(i.uv*3.7+flow)*.65+noise(i.uv*9.1+flow*.6)*.35;
                float2 p=(i.uv-.5)*2;
                float density;
                if(i.brush.x>.5 && i.brush.x<1.5)
                    density=(1-smoothstep(.2,.96,abs(p.x)+(.5-n)*.32))*smoothstep(0,.12,i.uv.y)*smoothstep(1,.82,i.uv.y);
                else
                {
                    float edge=length(p+float2(sin(p.y*4+flow.x),cos(p.x*3+flow.y))*.085)+(.5-n)*.40;
                    density=(1-smoothstep(.40,1.03,edge))*(.48+n*.52);
                    density*=1-smoothstep(.58,.78,noise(i.uv*float2(12,3)+flow*.5))*.3;
                }
                float fade=1-smoothstep(35,140,distance(_WorldSpaceCameraPos,i.world));
                float2 panel=i.brush.x>1.5?float2(.5,0):i.brush.x>.5?float2(0,0):float2(frac(i.brush.y)>.5?.5:0,.5);
                float2 warp=float2(sin(i.uv.y*7+flow.x),sin(i.uv.x*6+flow.y))*.012;
                float fluid=1-SAMPLE_TEXTURE2D(_FluidMask,sampler_FluidMask,panel+.006+saturate(i.uv+warp)*.488).r;
                float boundary=(1-smoothstep(.85,1,abs(p.x)))*(1-smoothstep(.85,1,abs(p.y)));
                density=lerp(density,saturate(fluid*1.7)*boundary,_UseFluidMask);
                half3 pigment=lerp(_BaseColor.rgb,half3(.21,.21,.20),saturate(1-density)*.3);
                pigment=lerp(pigment,i.color.rgb,_VertexTint);
                return half4(pigment,density*i.color.a*fade);
            }
            ENDHLSL
        }
    }
}
