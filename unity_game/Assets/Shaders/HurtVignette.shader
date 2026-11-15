// The red flash when you're hit: drawn on the inside of a small sphere round the eyes, clear in the middle of
// the view and red toward the edges and beyond, so the red reaches past the edge of the view wherever you look.
Shader "Repulsor/Hurt Vignette"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.08, 0.05, 1)
        _Amount ("Amount", Range(0, 1)) = 0
        _Inner ("Clear within (degrees)", Float) = 18
        _Outer ("Full red from (degrees)", Float) = 55
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+400" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Amount;
                float _Inner;
                float _Outer;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
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
                // The sphere sits on the head, its +Z along the view.
                output.direction = input.positionOS.xyz;
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float angle = degrees(acos(clamp(normalize(input.direction).z, -1.0, 1.0)));
                float edge = saturate((angle - _Inner) / max(1.0, _Outer - _Inner));
                float alpha = saturate(_Amount) * edge * edge * (3.0 - 2.0 * edge);
                return float4(_Color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
