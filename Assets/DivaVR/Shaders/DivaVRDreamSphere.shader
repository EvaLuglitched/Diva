// 开场的"梦境"：一个包住玩家的球（从里面看），粉彩渐变加慢慢漂的柔光斑。
// 门开了以后，从眼睛穿过门洞（圆盘）看到的那部分不画，于是只能从门里看到外面的糖果镇；
// 门开以后梦境从门的方向往四周柔和地化开（_Dissolve 0→1.2，半透明渐隐，没有碎片和白边：头显里碎片一闪一闪的）。
// 化开时 C# 把混合改成半透明（_SrcBlend/_DstBlend/_ZWrite）、队列改到透明物体那里，镇子才看得见。
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
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Dream"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
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
                float y = i.dir.y;
                half3 c = y > 0 ? lerp(_Mid.rgb, _Top.rgb, saturate(y * 1.3)) : lerp(_Mid.rgb, _Bottom.rgb, saturate(-y * 1.3));
                // 几团慢慢漂的柔光
                float t2 = _Time.y * .15;
                float a = atan2(i.dir.x, i.dir.z);
                float g = 0;
                g += pow(saturate(.5 + .5 * cos(a * 2 + t2) * cos(y * 3.1 + t2 * .7)), 6);
                g += pow(saturate(.5 + .5 * cos(a * 3 - t2 * 1.3 + 1.7) * cos(y * 2.2 - t2)), 8) * .7;
                c = lerp(c, _Glow.rgb, saturate(g) * .35);
                // 化开：从门的方向开一个越来越大的口，边缘 0.5 弧度宽的柔和过渡（带一点粉色光晕），最后整体淡掉
                half alpha = 1;
                if (_Dissolve > 0)
                {
                    float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                    float3 toPortal = normalize(_PortalCenter.xyz - centre);
                    float angle = acos(clamp(dot(normalize(i.positionWS - centre), toPortal), -1, 1));
                    // 开头慢（视野里的半个球大约 0.5 秒才完全打开），后面快
                    float k = pow(saturate(_Dissolve / 1.05), 2.2);
                    // 开始时口和门一样大、边是硬的（接得上门洞），然后边越来越柔
                    float portalAngle = atan2(_PortalRadius > 0 ? _PortalRadius : 1.35, max(.1, distance(_PortalCenter.xyz, centre)));
                    float open = lerp(portalAngle, 3.9, k);
                    float soft = lerp(.03, .6, saturate(k * 3));
                    alpha = saturate((angle - open) / soft);
                    float rim = saturate(1 - abs(angle - open - soft * .5) / (soft * .5));
                    c = lerp(c, half3(1, .82, .94), rim * .35);
                    alpha *= 1 - saturate((_Dissolve - .9) / .3);
                }
                return half4(c, alpha);
            }
            ENDHLSL
        }
    }
}
