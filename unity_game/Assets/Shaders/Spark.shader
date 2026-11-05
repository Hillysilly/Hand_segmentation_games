// A glowing streak for stretched particles: a thin hot core inside a soft halo. Adds light only.
Shader "Repulsor/Spark"
{
    Properties
    {
        [Toggle] _DepthTest ("Hide Behind Real Things", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "EnvironmentDepth.hlsl"
            #include "AuraColor.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _DepthTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float radius = saturate(1.0 - dot(input.uv, input.uv));
                float halo = radius * radius * 0.45;
                float core = radius * radius * radius * radius * radius * radius * radius * radius * 2.4;

                // The core runs hotter than the particle's colour, so it reads as white-hot.
                // Aura sparks, the ones that test depth, take the aura's colour.
                float3 tint = _DepthTest > 0.5 ? AuraFire(0.6) : input.color.rgb;
                float3 color = tint * (halo + core) + float3(1.0, 1.0, 1.0) * core * 0.5;
                float fade = input.color.a;
                if (_DepthTest > 0.5)
                    fade *= VisibleAt(DistanceToReal(input.positionWS), DistanceToPoint(input.positionWS));
                return float4(color * fade, core * fade * 0.15);
            }
            ENDHLSL
        }
    }
}
