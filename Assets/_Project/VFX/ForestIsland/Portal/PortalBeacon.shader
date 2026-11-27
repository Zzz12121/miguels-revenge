Shader "Project/VFX/Forest Portal Beacon"
{
    Properties
    {
        [HDR]_BaseColor("Gold light",Color)=(4,2.5,.35,1)
        _Intensity("Beacon visibility",Range(0,2))=1
        _Sparkle("Sparkle sprite",Float)=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha One
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;float _Intensity,_Sparkle;
            CBUFFER_END
            struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
            half4 frag(V i):SV_Target
            {
                float alpha;
                if(_Sparkle>.5)
                {
                    float2 p=(i.uv-.5)*2;
                    float core=pow(saturate(1-length(p)),4);
                    float star=pow(saturate(1-abs(p.x)),28)*pow(saturate(1-abs(p.y)),2)+pow(saturate(1-abs(p.y)),28)*pow(saturate(1-abs(p.x)),2);
                    alpha=saturate(core+star*.6);
                }
                else
                {
                    float t=_Time.y;
                    float wisps=pow(saturate(sin(i.uv.x*6.28318*7+i.uv.y*5-t*1.5)*.5+.5),5);
                    float shimmer=.82+.12*sin(t*4+i.uv.y*16)+.06*sin(t*13-i.uv.y*31);
                    alpha=(.05+wisps*.19)*pow(1-i.uv.y,1.3)*smoothstep(0,.035,i.uv.y)*shimmer;
                }
                return half4(_BaseColor.rgb*i.color.rgb,alpha*i.color.a*_BaseColor.a*_Intensity);
            }
            ENDHLSL
        }
    }
}
