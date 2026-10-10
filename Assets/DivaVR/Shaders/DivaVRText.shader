// 头显上的文字：TextMeshPro 自带的 SDF 着色器在 visionOS 的立体渲染里不画（整段文字都不显示），这里用 URP 的写法重做一个，
// 属性名和 TMP 的一样（_FaceColor、_OutlineWidth、_Underlay…），所以 DivaUi.Glow 这些照常用。边缘用屏幕导数做抗锯齿，
// 不用 TMP 那套按距离换算的粗细，所以字在任何距离都清楚。
Shader "Diva/VR Text"
{
    Properties
    {
        _MainTex ("Font atlas", 2D) = "white" {}
        _FaceColor ("Face color", Color) = (1, 1, 1, 1)
        _FaceDilate ("Face dilate", Range(-1, 1)) = 0
        _OutlineColor ("Outline color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline width", Range(0, 1)) = 0
        _OutlineSoftness ("Outline softness", Range(0, 1)) = 0
        _UnderlayColor ("Underlay color", Color) = (0, 0, 0, .5)
        _UnderlayOffsetX ("Underlay offset X", Range(-1, 1)) = 0
        _UnderlayOffsetY ("Underlay offset Y", Range(-1, 1)) = 0
        _UnderlayDilate ("Underlay dilate", Range(-1, 1)) = 0
        _UnderlaySoftness ("Underlay softness", Range(0, 1)) = 0
        // TMP 算字边留白要读这些；这里不用，但要有
        _WeightNormal ("Weight normal", Float) = 0
        _WeightBold ("Weight bold", Float) = .5
        _ScaleRatioA ("Scale ratio A", Float) = 1
        _ScaleRatioB ("Scale ratio B", Float) = 1
        _ScaleRatioC ("Scale ratio C", Float) = 1
        _GradientScale ("Gradient scale", Float) = 5
        _TextureWidth ("Texture width", Float) = 1024
        _TextureHeight ("Texture height", Float) = 1024
        _ScaleX ("Scale X", Float) = 1
        _ScaleY ("Scale Y", Float) = 1
        _PerspectiveFilter ("Perspective filter", Range(0, 1)) = .875
        _Sharpness ("Sharpness", Range(-1, 1)) = 0
        _VertexOffsetX ("Vertex offset X", Float) = 0
        _VertexOffsetY ("Vertex offset Y", Float) = 0
        _ClipRect ("Clip rect", Vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX ("Mask softness X", Float) = 0
        _MaskSoftnessY ("Mask softness Y", Float) = 0
        _StencilComp ("Stencil comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil operation", Float) = 0
        _StencilWriteMask ("Stencil write mask", Float) = 255
        _StencilReadMask ("Stencil read mask", Float) = 255
        _CullMode ("Cull mode", Float) = 0
        _ColorMask ("Color mask", Float) = 15
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Text"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local UNDERLAY_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                half4 _FaceColor, _OutlineColor, _UnderlayColor;
                float _FaceDilate, _OutlineWidth, _OutlineSoftness, _UnderlayOffsetX, _UnderlayOffsetY, _UnderlayDilate, _UnderlaySoftness;
                float _GradientScale, _ScaleRatioA, _ScaleRatioC, _VertexOffsetX, _VertexOffsetY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 p = input.positionOS; p.x += _VertexOffsetX; p.y += _VertexOffsetY;
                o.positionCS = TransformObjectToHClip(p.xyz);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // 图集里的距离：0.5 是字的边，1 在字里面。留白宽度 = _GradientScale 像素，所以 1 单位的粗细 = 留白那么宽
                float d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                float w = max(fwidth(d) * .7, .004);   // 一个像素的柔边，远处也不比一像素更糊
                float edge = .5 - _FaceDilate * _ScaleRatioA * .25;
                float faceA = smoothstep(edge - w, edge + w, d);
                float outlineW = _OutlineWidth * _ScaleRatioA * .5;
                float os = max(w, _OutlineSoftness * _ScaleRatioA * .25);
                float outlineA = smoothstep(edge - outlineW - os, edge - outlineW + os, d);
                half4 face = i.color * _FaceColor;
                half4 outline = _OutlineColor;
                half3 rgb = lerp(outline.rgb, face.rgb, faceA);
                half a = outlineA * lerp(outline.a, face.a, faceA);
                a *= i.color.a;
                half4 result = half4(rgb * a, a);   // 预乘
                #ifdef UNDERLAY_ON
                // 底层柔光 / 投影：同一张图再采样一次，向外扩、更柔，画在字后面
                float2 off = float2(_UnderlayOffsetX, _UnderlayOffsetY) * _ScaleRatioC * _GradientScale * _MainTex_TexelSize.xy;
                float d2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - off).a;
                float uDil = _UnderlayDilate * _ScaleRatioC * .25, uSoft = max(w, _UnderlaySoftness * _ScaleRatioC * .5);
                float u = smoothstep(edge - uDil - uSoft, edge - uDil + uSoft, d2) * _UnderlayColor.a * i.color.a;
                result.rgb += _UnderlayColor.rgb * u * (1 - a);
                result.a += u * (1 - a);
                #endif
                return result;
            }
            ENDHLSL
        }
    }
}
