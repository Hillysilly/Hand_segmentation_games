// One crackling arc, for a Line Renderer: a thin white-hot thread inside a soft blue glow, fading towards its
// far end. The line's uv runs (along, across) and its vertex colour's alpha fades the whole arc.
Shader "Repulsor/Lightning"
{
    Properties
    {
        _GlowColor ("Glow Color", Color) = (0.5, 0.88, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "EnvironmentDepth.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
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
                output.uv = input.uv;
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float across = input.uv.y * 2.0 - 1.0;
                float squared = across * across;
                float core = exp(-squared * 34.0);
                float glow = exp(-squared * 4.0) * (1.0 - squared);

                float fade = (1.0 - input.uv.x * input.uv.x) * input.color.a;
                fade *= Visibility(input.positionWS);

                float3 color = (float3(1.0, 1.0, 1.0) * core * 1.5 + _GlowColor.rgb * glow * 0.8) * fade;
                return float4(color, core * fade * 0.6);
            }
            ENDHLSL
        }
    }
}
