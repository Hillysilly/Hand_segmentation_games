// A glowing orb: rounded by its own light, and hidden wherever live depth says something real, such as a
// hand, is closer. Alpha lets passthrough show where it's hidden.
Shader "Repulsor/Orb"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 0.42, 0.1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "EnvironmentDepth.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
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
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normal = normalize(input.normalWS);
                float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
                float facing = saturate(dot(normal, toEye));

                // Darker towards the edge with a hot centre, so it reads as a ball and not a disc.
                float3 color = _BaseColor.rgb * (0.3 + 0.75 * facing);
                color += float3(1.0, 0.8, 0.5) * pow(facing, 6.0) * 0.35;

                float visible = VisibleAt(DistanceToReal(input.positionWS), DistanceToPoint(input.positionWS));
                return float4(color * visible, visible);
            }
            ENDHLSL
        }
    }
}
