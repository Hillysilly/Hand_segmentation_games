// The colour wheel: hue around the disc and white at the middle, with a ring marking the current choice.
Shader "Repulsor/ColorWheel"
{
    Properties
    {
        _Picked ("Picked Point", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Picked;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 spot : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.spot = input.uv * 2.0 - 1.0;
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float radius = length(input.spot);
                float hue = frac(atan2(input.spot.y, input.spot.x) / 6.2831853);
                float3 pure = saturate(abs(frac(hue + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                float3 color = lerp(float3(1.0, 1.0, 1.0), pure, saturate(radius));

                float distanceToPick = length(input.spot - _Picked.xy);
                float pickRing = saturate(1.0 - abs(distanceToPick - 0.09) / 0.025);
                color = lerp(color, float3(0.0, 0.0, 0.0), pickRing);

                // The picture is in display colour, so undo the conversion to linear output.
                color = pow(color, 2.2);
                return float4(color, saturate((1.0 - radius) / 0.02) * 0.96);
            }
            ENDHLSL
        }
    }
}
