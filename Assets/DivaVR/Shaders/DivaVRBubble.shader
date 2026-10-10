// 载着玩家飞的大泡泡（从里面看）：边上一圈彩虹薄膜，正对视线的地方几乎透明。
Shader "Diva/VR Bubble"
{
    Properties
    {
        _Alpha ("Rim alpha", Range(0, 1)) = .55
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Shift ("Hue drift", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Bubble"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Alpha, _Shift;
                half4 _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half3 Hue(float h)
            {
                float3 p = abs(frac(h + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3);
                return saturate(p - 1);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                float f = 1 - abs(dot(normalize(i.normalWS), v));   // 边上大、正对小
                float rim = pow(f, 2.2);
                // 薄膜干涉：颜色随视角和位置慢慢变
                float h = f * 1.6 + i.positionWS.y * .12 + _Shift + _Time.y * .05;
                half3 c = lerp(half3(1, 1, 1), Hue(h), .65) * _Tint.rgb;
                half a = saturate(rim * _Alpha + .03 * _Alpha);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
