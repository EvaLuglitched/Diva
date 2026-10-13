// 冰（终点前的冰墙，DivaShowWall）：URP 自定义着色器，PC 和 Vision Pro 立体渲染都能画（带 instancing / stereo 宏）。
// 顶点色 r = 高度（底部深蓝 → 顶部亮青白），g = 雪（顶部的切面偏白）。
// 写实的几样东西：
// · 折射（_REFRACTION，PC）：透过冰看到后面被法线扭曲的街景；头显上不开（不透明纹理太贵），改用半透明。
// · 厚度感：正对着看的面看进冰里更深蓝，边上薄、更亮更透（菲涅尔）。
// · 内部纹理：按世界坐标的 3D 噪声做裂纹（噪声等值线附近的亮线）和霜（朝上的面更多，发白、更糙）。
// · 光照：小面上两层高光（细 + 宽）、天空 / 反射探针的环境反射（按菲涅尔）、边缘发光、里面慢慢呼吸的微光。
// · 投影：ShadowCaster 通道，冰柱在路上有影子。
Shader "Diva/Ice"
{
    Properties
    {
        _Deep ("Deep colour (thick ice)", Color) = (0.06, 0.36, 0.95, 1)
        _Light ("Light colour (thin ice)", Color) = (0.62, 0.92, 1.0, 1)
        _Rim ("Rim glow", Color) = (0.55, 0.92, 1.0, 1)
        _Alpha ("Opacity (no refraction)", Range(0, 1)) = 0.9
        _Glow ("Glow", Range(0, 3)) = 1
        _Refract ("Refraction strength", Range(0, 0.2)) = 0.06
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
            #pragma multi_compile_local_fragment _ _REFRACTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Light, _Rim;
                float _Alpha, _Glow, _Refract;
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

            // 便宜的 3D 值噪声（哈希 + 三线性插值），两三层叠起来
            float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float Noise(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }
            float Fbm(float3 p) { return Noise(p) * 0.5 + Noise(p * 2.07 + 7.1) * 0.3 + Noise(p * 4.3 + 3.7) * 0.2; }

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
                float nv = saturate(dot(n, v));
                float fres = pow(1.0 - nv, 3.0);
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // 内部纹理
                float3 p = i.positionWS;
                float f1 = Fbm(p * 1.4), f2 = Fbm(p * 3.1 + 11.0);
                float cracks = smoothstep(0.035, 0.0, abs(f1 - 0.5)) * 0.6 + smoothstep(0.02, 0.0, abs(f2 - 0.48)) * 0.35;   // 裂纹细一点、淡一点，别像大理石纹
                float frost = smoothstep(0.52, 0.78, Fbm(p * 2.3 + 5.0)) * (0.35 + 0.65 * saturate(n.y * 1.5)) + i.color.g * 0.9;
                frost = saturate(frost);

                // 冰体的颜色：薄处亮、厚处深；看进去（正对着）更深
                half3 body = lerp(_Deep.rgb, _Light.rgb, saturate(i.color.r * 0.55 + fres * 0.6 + 0.1));
                body = lerp(body, _Deep.rgb * 0.8, nv * 0.4 * (1.0 - i.color.r * 0.5));
                body += cracks * half3(0.75, 0.95, 1.0) * 0.55;
                body = lerp(body, half3(0.94, 0.97, 1.0), frost * 0.75);

                // 光照
                Light l = GetMainLight();
                half3 lightCol = max(l.color, 0.35);
                float ndl = saturate(dot(n, l.direction)) * 0.65 + 0.35;
                float3 h = normalize(l.direction + v);
                float nh = saturate(dot(n, h));
                float spec = pow(nh, 110.0) * 1.8 * (1.0 - frost * 0.6) + pow(nh, 10.0) * 0.22;
                float F = 0.04 + 0.96 * fres;
                half3 refl = GlossyEnvironmentReflection(reflect(-v, n), i.positionWS, lerp(0.08, 0.6, frost), 1.0, screenUV);
                float breathe = sin(_Time.y * 1.6 + p.y * 1.3 + p.x * 0.4) * 0.5 + 0.5;
                half3 col = body * ndl * lightCol + spec * lightCol + refl * F * 1.1
                          + _Rim.rgb * fres * (0.55 + 0.3 * breathe) * _Glow + body * 0.1 * breathe * (_Glow - 0.5);

                #if _REFRACTION
                // 透过冰看后面：法线把画面扭一下，厚处（正对着）扭得多；霜和裂纹处不透
                float2 off = TransformWorldToViewDir(n).xy * _Refract * (0.5 + 0.5 * nv) * (1.0 - frost * 0.8);
                half3 through = SampleSceneColor(screenUV + off);
                through *= lerp(half3(1, 1, 1), body * 1.5, 0.55);
                float see = saturate((1.0 - _Alpha) * 2.2 * (1.0 - frost) * (1.0 - fres * 0.5) * (1.0 - cracks * 0.5));
                return half4(lerp(col, through, see) + spec * lightCol * 0.4, 1.0);
                #else
                float a = saturate(_Alpha + fres * 0.3 + spec * 0.3 + frost * 0.1);
                return half4(col, a);
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowFrag(ShadowVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
