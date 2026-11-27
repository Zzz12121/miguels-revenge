Shader "Project/InkboundTown/Painted Surface"
{
    Properties
    {
        _BaseColor("Pigment tone",Color)=(.8,.8,.77,1)
        _BaseMap("Original painted detail",2D)="white"{}
        _TextureWeight("Texture luminance",Range(0,1))=0
        _OpacityMap("Leaf silhouette",2D)="white"{}
        _AlphaClip("Leaf cutout",Float)=0
        _Cutoff("Silhouette threshold",Range(0,1))=.35
        _BrushAtlas("Hand painted pigment atlas",2D)="gray"{}
        _BrushPanel("Atlas quadrant",Vector)=(0,0,0,0)
        _BrushWeight("Pigment atlas strength",Range(0,1))=0
        _Pattern("0 plaster 1 timber 2 tile 3 paving 4 cloth 5 mountain 6 foliage",Float)=0
        _Grain("Dry brush",Range(0,1))=.35
        _Wash("Wash variation",Range(0,1))=.18
        _Outline("Contour pigment",Range(0,1))=.2
        _PaperColor("Paper",Color)=(.94,.935,.90,1)
        _FadeNear("Atmospheric fade start",Float)=55
        _FadeFar("Atmospheric fade end",Float)=185
        _StrokeScale("Brush scale",Float)=1
        _DirectionalUV("Follow curved brush UV",Range(0,1))=0
        [HideInInspector]_MountainWorldSpace("Continuous mountain brush coordinates",Float)=0
        [HideInInspector]_EchoStrength("Echo disturbance",Float)=0
        [HideInInspector]_InkRebuild("Reassembled pigment",Float)=1
        [HideInInspector]_InkRebuildMin("Reassembly bounds minimum",Vector)=(0,0,0,0)
        [HideInInspector]_InkRebuildSize("Reassembly bounds size",Vector)=(1,1,1,0)
        [HideInInspector]_TouchRadius("Influence radius",Float)=.88
        [HideInInspector]_TouchPosition("Influence position",Vector)=(0,0,0,0)
        [HideInInspector]_TouchDirection("Advection",Vector)=(0,0,0,0)
        _ContactInk("Contact and waterline accumulation",Range(0,1.5))=.55
        [HideInInspector]_HeightMist("Mountain foot mist",Range(0,1))=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor,_PaperColor;
        float4 _BaseMap_ST,_TouchPosition,_TouchDirection,_BrushPanel;
        float4 _InkFrom[4],_InkTo[4];
        float4 _InkRebuildMin,_InkRebuildSize;
        float _InkRebuild,_MountainWorldSpace,_ContactInk,_HeightMist;
        float _TextureWeight,_Pattern,_Grain,_Wash,_Outline,_FadeNear,_FadeFar,_EchoStrength,_TouchRadius,_StrokeScale,_DirectionalUV,_BrushWeight,_AlphaClip,_Cutoff;
        CBUFFER_END
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        TEXTURE2D(_OpacityMap);SAMPLER(sampler_OpacityMap);
        TEXTURE2D(_BrushAtlas);SAMPLER(sampler_BrushAtlas);
        float _InkReflectionPass;
        float hash21(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
        float noise21(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);}
        float wash(float2 p){return noise21(p)*.58+noise21(p*2.07)*.28+noise21(p*4.13)*.14;}
        // Jittered cells make differently sized flags; the joints lose ink instead of forming a grid.
        float2 stoneCell(float2 p)
        {
            float2 cell=floor(p);float nearest=100,second=100,id=0;
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
            {
                float2 q=cell+float2(x,y),j=float2(hash21(q),hash21(q+31.7));
                float2 d=q+.5+(j-.5)*.92-p;float r=dot(d,d);
                if(r<nearest){second=nearest;nearest=r;id=hash21(q+74);}
                else second=min(second,r);
            }
            return float2((second-nearest)/max(.12,sqrt(second)+sqrt(nearest)),id);
        }
        struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
        struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float3 original:TEXCOORD3;};
        float influence(float3 p)
        {
            float result=0;
            [unroll] for(int k=0;k<4;k++)
            {
                float3 segment=_InkTo[k].xyz-_InkFrom[k].xyz;
                float t=saturate(dot(p-_InkFrom[k].xyz,segment)/max(.0001,dot(segment,segment)));
                float d=distance(p,_InkFrom[k].xyz+segment*t)/max(.01,_InkFrom[k].w);
                result=max(result,_InkTo[k].w*(1-smoothstep(.12,1,d)));
            }
            return result;
        }
        V vert(A i)
        {
            V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.original=o.world;
            o.normal=TransformObjectToWorldNormal(i.normalOS);
            float touch=influence(o.world);
            o.world+=touch*(_TouchDirection.xyz*.024+o.normal*(noise21(o.world.xz*6+_Time.y)-.5)*.18);
            if(_Pattern>5.5 && _Pattern<6.5)o.world.xz+=sin(_Time.y*.7+o.world.y*.9+o.world.z*.17)*.045;
            if(_Pattern>3.5 && _Pattern<4.5 && _TextureWeight<.1)
                o.world.z+=sin(_Time.y*.65+o.world.x*1.8+o.world.y*.7)*.035*(1-i.uv.y);
            o.positionCS=TransformWorldToHClip(o.world);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;
        }
        void echoClip(V i)
        {
            // Shared by color, depth and shadows: the whole object really disappears.
            // Return in irregular pigment patches following its current animated pose.
            if(_InkRebuild<.9999)
            {
                float3 local=(i.original-_InkRebuildMin.xyz)/max(_InkRebuildSize.xyz,float3(.01,.01,.01));
                float threshold=saturate(local.y)*.68+wash(local.xz*9+local.y*3.7)*.32;
                clip(_InkRebuild*1.04-threshold-.015);
            }
            if(_AlphaClip>.5)clip(SAMPLE_TEXTURE2D(_OpacityMap,sampler_OpacityMap,i.uv).r-_Cutoff);
            float m=influence(i.original);
            float pigment=wash(i.original.xz*11+i.original.y*3.7);
            clip(pigment-m*.94+.055);
            if(_Pattern>5.5&&_Pattern<6.5)
                clip(smoothstep(.35,1.3,distance(_WorldSpaceCameraPos,i.world))-noise21(i.world.xz*9+i.world.y)*.85);
        }
        ENDHLSL
        Pass
        {
            Name "PaintedForward"
            Tags{"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            half4 frag(V i):SV_Target
            {
                echoClip(i);
                #if defined(DEBUG_DISPLAY)
                    half4 debugColor;if(CalculateColorForDebugSceneOverride(debugColor))return debugColor;
                #endif
                float3 n=normalize(i.normal);float2 p=i.world.xz;
                // Two-sided paint meshes otherwise expose the underside of the entire land strip
                // to the submerged reflection camera and hide all reflected houses behind it.
                if(_InkReflectionPass>.5 && abs(n.y)>.7 && (_Pattern<.5 || (_Pattern>2.5 && _Pattern<3.5) || _Pattern>6.5))clip(-1);
                float2 surface=abs(n.y)>.6?i.world.xz:(abs(n.x)>.6?i.world.zy:i.world.xy);
                surface*=max(.1,_StrokeScale);
                surface=lerp(surface,float2(i.uv.x*3,i.uv.y),_DirectionalUV);
                if(_Pattern>4.5 && _Pattern<5.5)surface=lerp(float2(i.uv.x*160,i.uv.y*42),float2((i.world.x+i.world.z*.43)*.2,i.world.y*.75),_MountainWorldSpace);
                float distanceToEye=distance(_WorldSpaceCameraPos,i.world);
                float detail=1-smoothstep(18,115,distanceToEye);
                float broad=wash(surface*.67),dry=wash(surface*float2(19,6));
                float tex=dot(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb,float3(.299,.587,.114));
                // Lift the large clothing areas while keeping dark hair and facial marks legible.
                half3 color=_BaseColor.rgb*lerp(1,.16+pow(max(0,tex),.76)*.9,_TextureWeight);
                float lighting=saturate(dot(n,normalize(float3(-.35,.85,-.3))));
                color*=lerp(.76,1.025,smoothstep(.12,.8,lighting));
                Light mainLight=GetMainLight(TransformWorldToShadowCoord(i.world));
                color*=lerp(.72,1,mainLight.shadowAttenuation);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    color*=lerp(.66,1,ao.indirectAmbientOcclusion);
                #endif
                color*=1-_Wash*(.65-broad);
                float mark=0,mountainFootMist=0;
                if(_Pattern<.5)
                {
                    float foot=1-smoothstep(.15,1.65+noise21(p*.8)*.8,i.world.y);
                    float stain=wash(surface*float2(1.8,.55));
                    mark=smoothstep(.37,.73,stain)*foot*.26;
                    mark+=smoothstep(.67,.84,broad)*.045;
                }
                else if(_Pattern<1.5)
                {
                    float fibers=wash(float2(surface.x*24,surface.y*.75));
                    float dryGap=smoothstep(.67,.9,wash(float2(surface.x*38,surface.y*.42)));
                    float longGrain=wash(float2(surface.x*3.2,surface.y*.16));
                    color=lerp(color,_PaperColor.rgb,smoothstep(.68,.91,fibers)*_Grain*.11*detail);
                    color=lerp(color,_PaperColor.rgb,dryGap*_Grain*.14*detail);
                    color*=.94+longGrain*.1;
                    mark=wash(float2(surface.x*4,surface.y*.4))*.10+dryGap*.035;
                }
                else if(_Pattern<2.5)
                {
                    // A dark ink mass with sparse tile hints, broken by bristle-shaped paper gaps.
                    float2 tiles=float2(surface.x*2.8,surface.y*2.1);
                    float stroke=noise21(float2(surface.x*27,surface.y*2.5));
                    float row=abs(frac(tiles.y+noise21(surface*.4)*.18)-.5);
                    float col=abs(frac(tiles.x+floor(tiles.y)*.43)-.5);
                    float tile=(smoothstep(.41,.48,row)+smoothstep(.44,.49,col)*.4)*detail;
                    color=lerp(color,_PaperColor.rgb,tile*.035*noise21(surface*4));
                    color=lerp(color,_PaperColor.rgb,smoothstep(.72,.94,stroke)*_Grain*.085*detail);
                    float slopeWash=wash(surface*float2(.72,.28));
                    float bristle=smoothstep(.61,.86,wash(surface*float2(23,1.7)));
                    color*=.9+slopeWash*.16;
                    color=lerp(color,_PaperColor.rgb,bristle*_Grain*.12*detail);
                    mark=wash(surface*.7)*.13+bristle*.025;
                }
                else if(_Pattern<3.5)
                {
                    float2 cell=stoneCell(surface*float2(.86,.92)+(float2(noise21(surface*2),noise21(surface*2+9))-.5)*.16);
                    float seam=(1-smoothstep(.008,.038,cell.x))*smoothstep(.18,.72,wash(surface*5.7));
                    mark=seam*smoothstep(.28,.64,dry)*(.22+cell.y*.16)*detail;
                    color=lerp(color,_PaperColor.rgb,.08+cell.y*.17);
                    color*=.95+cell.y*.075;
                    mark+=smoothstep(.56,.8,broad)*.085;
                }
                else if(_Pattern<4.5)
                {
                    float thread=wash(i.uv*float2(115,155));
                    float fold=wash(surface*float2(1.7,.34));
                    color*=.96+fold*.065;
                    color=lerp(color,_PaperColor.rgb,smoothstep(.76,.94,thread)*_Grain*.055*detail);
                    mark=wash(surface*float2(17,2))*.04*detail;
                    if(_TextureWeight<.1)
                    {
                        float2 motif=frac(i.uv*float2(3,4))-.5;
                        float leaf=1-smoothstep(.05,.11,length(motif*float2(1,2.5)));
                        float stem=1-smoothstep(.008,.018,abs(motif.x-sin(motif.y*5)*.045));
                        mark+=(leaf*.11+stem*.045)*detail;
                    }
                }
                else if(_Pattern<5.5)
                {
                    float2 mountainUV=float2(lerp(i.uv.x,i.world.x*.01+i.world.z*.008,_MountainWorldSpace),i.uv.y);
                    float stroke=wash(surface*float2(.13,.19));
                    float ridge=pow(saturate(noise21(float2(surface.x*1.1+surface.y*.35,surface.y*.18))),3);
                    color=lerp(_PaperColor.rgb,color,saturate(.22+stroke*.9));
                    mark=ridge*.10;
                    color=lerp(color,_PaperColor.rgb,smoothstep(.60,.84,dry)*.24*detail);
                    float foot=smoothstep(.03,.53+noise21(float2(mountainUV.x*18,2))*.16,mountainUV.y);
                    float edgeInk=smoothstep(.91,.995,mountainUV.y)*(.2+noise21(surface*.5)*.4);
                    color=lerp(_PaperColor.rgb,color*(1-edgeInk*.14),foot);
                    if(_MountainWorldSpace>.5)
                    {
                        float2 rockUV=float2((i.world.x+i.world.z*.61)*.3,i.world.y*.095);
                        float split=wash(float2(rockUV.x*4+rockUV.y*.45,rockUV.y*.55));
                        float slope=1-saturate(abs(n.y));
                        float strata=wash(float2(rockUV.x*2.6+rockUV.y*.32,rockUV.y*7.5));
                        float crevice=smoothstep(.57,.79,strata)*(.3+slope*.7);
                        color*=1-smoothstep(.44,.77,split)*(.08+slope*.14);
                        color*=1-crevice*.13;
                        color=lerp(color,_PaperColor.rgb,smoothstep(.72,.9,noise21(rockUV*float2(12,2.2)))*.15*foot);
                    }
                    float mistHeight=18+noise21(i.world.xz*.035)*16;
                    mountainFootMist=(1-smoothstep(0,mistHeight,i.world.y))*_HeightMist;
                    color=lerp(color,_PaperColor.rgb,mountainFootMist*(.28+.24*noise21(i.world.xz*.09)));
                }
                else if(_Pattern<6.5)
                {
                    // Leaf meshes taper in geometry; pigment breaks up inside their silhouettes.
                    color=lerp(color,_PaperColor.rgb,smoothstep(.57,.84,dry)*_Grain*.58);
                    color*=.93+broad*.12;
                    mark=broad*.16+dry*.035;
                }
                else
                {
                    // Quiet soil is a broad, low frequency wash, never the foliage dry-brush pattern.
                    mark=smoothstep(.57,.80,broad)*.025;
                    color=lerp(color,_PaperColor.rgb,broad*.035);
                }
                float contact=0;
                if(_Pattern<.5)
                {
                    float dampHeight=1.25+noise21(i.world.xz*.73)*.55;
                    float damp=1-smoothstep(.06,dampHeight,i.world.y);
                    float capillary=smoothstep(.4,.79,wash(float2(surface.x*5.8,surface.y*.52)));
                    float lowerEave=exp(-pow((i.world.y-3.08)/.24,2));
                    float upperEave=exp(-pow((i.world.y-6.43)/.28,2));
                    contact=(damp*(.11+capillary*.2)+(lowerEave+upperEave)*.085)*_ContactInk;
                }
                else if(_Pattern>.5&&_Pattern<1.5)
                {
                    float damp=1-smoothstep(.04,.72+noise21(i.world.xz)*.25,i.world.y);
                    contact=damp*wash(float2(surface.x*7,surface.y*.3))*.13*_ContactInk;
                }
                else if(_Pattern>2.5&&_Pattern<3.5)
                {
                    float waterline=exp(-pow((i.world.y+.82)/.23,2));
                    float broken=smoothstep(.25,.7,wash(i.world.xz*3.7+i.world.y*4));
                    float moss=smoothstep(.58,.82,wash(i.world.xz*1.6))*(1-smoothstep(-.95,.05,i.world.y));
                    contact=(waterline*(.08+broken*.16)+moss*.08)*_ContactInk;
                }
                color*=1-contact;
                float edge=pow(1-abs(dot(n,normalize(_WorldSpaceCameraPos-i.world))),5);
                edge*=.55+noise21(surface*13)*.65;
                // Mirrored coordinates stay within one painted quadrant, including mip padding.
                float2 brushUV=abs(frac(surface*float2(.17,.13)*.5)*2-1);
                float2 atlasUV=_BrushPanel.xy+float2(.006,.006)+brushUV*.488;
                float pigment=dot(SAMPLE_TEXTURE2D(_BrushAtlas,sampler_BrushAtlas,atlasUV).rgb,float3(.299,.587,.114));
                color*=lerp(1,lerp(.62,1.18,saturate(pigment*1.7)),_BrushWeight*(.45+.55*detail));
                color*=1-mark-edge*_Outline*(.45+noise21(surface*4)*.55)*detail;
                color=lerp(color,_PaperColor.rgb,smoothstep(.69,.92,dry)*_Grain*.13*detail);
                // Fine paper fibres, softened with distance so the far scenery loses texture too.
                float fibre=noise21(surface*float2(130,67));
                color+=(fibre-.5)*.0025*detail;
                float atmosphere=smoothstep(_FadeNear,max(_FadeNear+.1,_FadeFar),distanceToEye);
                atmosphere=saturate(atmosphere+mountainFootMist*.18);
                color=lerp(color,_PaperColor.rgb,atmosphere);
                return half4(saturate(color),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode"="ShadowCaster"}
            Cull Back
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma target 3.0
            float3 _LightDirection;
            V shadowVert(A input)
            {
                V o=vert(input);o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.world,o.normal,_LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #else
                    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #endif
                return o;
            }
            half4 shadowFrag(V i):SV_Target{echoClip(i);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode"="DepthOnly"}
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment depth
            half4 depth(V i):SV_Target{echoClip(i);return 0;}
            ENDHLSL
        }
    }
}
