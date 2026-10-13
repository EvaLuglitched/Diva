// 冰（终点前的冰墙，DivaShowWall）：URP 自定义着色器，PC 和 Vision Pro 立体渲染都能画（带 instancing / stereo 宏）。
// 顶点色 r = 高度（底部深蓝 → 顶部亮青白），g = 雪（顶部的切面偏白）。
// 看起来像冰的三样东西：菲涅尔边缘发光（侧着看的面更亮、更透）、硬边小面上的高光、里面轻轻呼吸的微光。
Shader "Diva/Ice"
{
    Properties
    {
        _Deep ("Deep colour (bottom)", Color) = (0.08, 0.42, 1.0, 1)
        _Light ("Light colour (top)", Color) = (0.55, 0.9, 1.0, 1)
        _Rim ("Rim glow", Color) = (0.55, 0.92, 1.0, 1)
        _Alpha ("Alpha", Range(0, 1)) = 0.9
        _Glow ("Glow", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Light, _Rim;
                float _Alpha, _Glow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
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
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light l = GetMainLight();
                float3 lightCol = max(l.color, 0.35);              // 没有主光的场景也别全黑
                float ndl = saturate(dot(n, l.direction)) * 0.7 + 0.3;   // 小面之间明暗差大一点，棱角才看得出来
                float3 h = normalize(l.direction + v);
                float spec = pow(saturate(dot(n, h)), 56) * 1.3;
                float rim = pow(1 - saturate(dot(n, v)), 2.4);
                half3 base = lerp(_Deep.rgb, _Light.rgb, i.color.r);
                base = lerp(base, half3(0.96, 0.99, 1.0), i.color.g * 0.8);   // 雪顶
                // 里面的微光：按高度和时间慢慢呼吸
                float breathe = sin(_Time.y * 1.6 + i.positionWS.y * 1.3 + i.positionWS.x * 0.4) * 0.5 + 0.5;
                half3 col = base * ndl * lightCol + spec * lightCol + rim * _Rim.rgb * (1.0 + 0.5 * breathe) * _Glow + base * 0.12 * breathe * _Glow;
                col = lerp(col, half3(0.97, 0.99, 1.0), i.color.g * 0.35);   // 雪顶再提亮一点
                float a = saturate(_Alpha + rim * 0.3 + spec * 0.4);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
