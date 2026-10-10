// 开场的"梦境"：一个包住玩家的球（从里面看），粉彩渐变加慢慢漂的柔光斑。
// 门开了以后，从眼睛穿过门洞（圆盘）看到的那部分不画，于是只能从门里看到外面的糖果镇；
// 穿过门的时候把 _PortalRadius 调到很大，整个球就都不画了。
Shader "Diva/VR Dream Sphere"
{
    Properties
    {
        _Top ("Top color", Color) = (1, .82, .92, 1)
        _Mid ("Horizon color", Color) = (.82, .78, 1, 1)
        _Bottom ("Bottom color", Color) = (.76, .95, .92, 1)
        _Glow ("Glow spots color", Color) = (1, 1, 1, 1)
        _PortalCenter ("Portal center (world)", Vector) = (0, 0, 0, 0)
        _PortalNormal ("Portal normal (world, towards the player)", Vector) = (0, 0, 1, 0)
        _PortalRadius ("Portal radius (m)", Float) = 0
        _Dissolve ("Dissolve (0-1)", Range(0, 1.2)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Dream"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Mid, _Bottom, _Glow;
                float4 _PortalCenter, _PortalNormal;
                float _PortalRadius, _Dissolve;
            CBUFFER_END

            float Hash(float3 p)
            {
                p = frac(p * .3183099 + .1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 dir : TEXCOORD1;
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
                o.dir = normalize(input.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // 门洞：从眼睛到这一点的线穿过圆盘就不画
                float3 eye = GetCameraPositionWS();
                float3 n = _PortalNormal.xyz;
                float se = dot(eye - _PortalCenter.xyz, n);
                float sx = dot(i.positionWS - _PortalCenter.xyz, n);
                float denom = se - sx;
                if (_PortalRadius > 0 && abs(denom) > 1e-5)
                {
                    float t = se / denom;
                    float3 q = eye + (i.positionWS - eye) * t;
                    if (t >= 0 && t <= 1.002 && distance(q, _PortalCenter.xyz) < _PortalRadius) clip(-1);
                }
                // 穿过门以后：像泡泡一样一片片碎掉散开（碎片边缘发亮）
                float noise = Hash(floor(i.dir * 28)) * .9 + Hash(floor(i.dir * 7)) * .1;
                float edge = noise - _Dissolve;
                clip(edge);
                float y = i.dir.y;
                half3 c = y > 0 ? lerp(_Mid.rgb, _Top.rgb, saturate(y * 1.3)) : lerp(_Mid.rgb, _Bottom.rgb, saturate(-y * 1.3));
                // 几团慢慢漂的柔光
                float t2 = _Time.y * .15;
                float a = atan2(i.dir.x, i.dir.z);
                float g = 0;
                g += pow(saturate(.5 + .5 * cos(a * 2 + t2) * cos(y * 3.1 + t2 * .7)), 6);
                g += pow(saturate(.5 + .5 * cos(a * 3 - t2 * 1.3 + 1.7) * cos(y * 2.2 - t2)), 8) * .7;
                c = lerp(c, _Glow.rgb, saturate(g) * .35);
                if (_Dissolve > 0) c = lerp(half3(1, 1, 1), c, saturate(edge * 12));   // 正在碎的那一片发白
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
