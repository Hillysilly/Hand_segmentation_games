// The tight aura on a person the camera sees: their body mask from the pose model, grown a little and drawn as a
// glow, brightest at the edge and faint over the body. Each mask is a flat square standing where the person was;
// new masks arrive a few times a second and the old ones fade, which leaves afterimages behind a moving body.
Shader "Repulsor/BodyGlow"
{
    Properties
    {
        _MaskTex ("Mask", 2D) = "black" {}
        _Color ("Color", Color) = (0.3, 0.75, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Grow ("Grow (of the mask's width)", Float) = 0.022
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+15" "RenderPipeline" = "UniversalPipeline" }

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                float4 _Color;
                float _Alpha;
                float _Grow;
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
                float2 uv : TEXCOORD0;
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
                output.uv = input.uv;
                return output;
            }

            float Mask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_MaskTex, uv, 0).r;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;
                float body = Mask(uv);
                // Grow the mask: the most of it within reach (two rings of samples), and a softer average for a halo.
                float grown = 0.0;
                float soft = 0.0;
                [unroll]
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * 0.5235988;
                    float2 dir = float2(cos(angle), sin(angle));
                    float a = Mask(uv + dir * _Grow * 0.5);
                    float b = Mask(uv + dir * _Grow);
                    grown = max(grown, max(a, b));
                    soft += a + b;
                }
                soft /= 24.0;

                float shell = smoothstep(0.3, 0.75, grown);
                float inside = smoothstep(0.35, 0.85, body);
                float edge = shell * (1.0 - inside * 0.88);
                float halo = smoothstep(0.02, 0.5, soft) * 0.35 * (1.0 - inside * 0.8);
                // Flicker that licks upward, like heat.
                float t = _Time.y;
                float lick = 0.75 + 0.25 * sin(uv.x * 90.0 + t * 4.0) * sin(uv.y * 55.0 + t * 9.0);
                float glow = saturate((edge + halo) * lick) * _Alpha;
                return float4(_Color.rgb * glow * 1.6, glow * 0.75);
            }
            ENDHLSL
        }
    }
}
