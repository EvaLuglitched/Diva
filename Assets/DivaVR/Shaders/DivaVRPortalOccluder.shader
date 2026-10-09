// 传送门开场的"房间"：一个包住玩家的盒子，画成透明（alpha 0，Vision Pro 上就露出真实房间），但写深度，挡住后面的虚拟世界。
// 盒子正面就是传送门所在的平面：眼睛穿过门洞（圆盘）看到的那部分不画，于是只能从门洞里看到糖果镇。
// 眼睛在门的另一边时（上了大象之后）反过来：只有穿过门洞看到的地方画成透明（回头看得见自己的房间），其余都是糖果镇。
// 电脑上没有透视：_DivaRoomPreview 不为 0 时画成一个带网格的灰色"房间"，方便在编辑器和截图里看。
Shader "Diva/VR Portal Occluder"
{
    Properties
    {
        _PortalCenter ("Portal center (world)", Vector) = (0, 0, 0, 0)
        _PortalNormal ("Portal normal (world, towards the room)", Vector) = (0, 0, 1, 0)
        _PortalRadius ("Portal radius (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _PortalCenter;
            float4 _PortalNormal;
            float _PortalRadius;
        CBUFFER_END
        float4 _DivaRoomPreview;

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            return output;
        }

        // 从眼睛到这一点的线穿过门洞了吗；按眼睛在哪一边决定这一点要不要画
        void PortalClip(float3 positionWS)
        {
            float3 eye = GetCameraPositionWS();
            float3 n = _PortalNormal.xyz;
            float se = dot(eye - _PortalCenter.xyz, n);
            float sx = dot(positionWS - _PortalCenter.xyz, n);
            bool through = false;
            float denom = se - sx;
            if (_PortalRadius > 0 && abs(denom) > 1e-5)
            {
                float t = se / denom;
                float3 q = eye + (positionWS - eye) * t;
                through = t >= 0 && t <= 1.002 && distance(q, _PortalCenter.xyz) < _PortalRadius;
            }
            bool eyeInRoom = se > 0;
            clip(through == eyeInRoom ? -1 : 1);
        }

        half4 Room(float3 positionWS)
        {
            if (_DivaRoomPreview.r + _DivaRoomPreview.g + _DivaRoomPreview.b <= 0) return half4(0, 0, 0, 0);
            float3 g = abs(frac(positionWS * 2) - .5);
            float grid = step(min(min(g.x, g.y), g.z), .02);   // 注意 line 是 HLSL 保留字
            return half4(_DivaRoomPreview.rgb * (1 + grid * .6), 0);
        }
        ENDHLSL

        Pass
        {
            Name "Room"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                PortalClip(input.positionWS);
                return Room(input.positionWS);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                PortalClip(input.positionWS);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                PortalClip(input.positionWS);
                return half4(0, 0, 1, 0);
            }
            ENDHLSL
        }
    }
}
