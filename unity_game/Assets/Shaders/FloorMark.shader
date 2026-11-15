// A warning painted on the floor where an attack will land: a disc, or a fan (a sector of one), faintly filled, with
// a bright rim and a second fill that grows from the middle out to the rim as the attack comes, so its edge
// reaching the rim is the moment of the hit. Drawn on a quad lying on the floor, its "up" along the fan's middle.
Shader "Repulsor/Floor Mark"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.15, 0.08, 1)
        _Arc ("Half angle (degrees, 180 = whole disc)", Float) = 180
        _Fill ("Fill (0 to 1)", Range(0, 1)) = 0
        _Alpha ("Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Arc;
                float _Fill;
                float _Alpha;
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

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = (input.uv - 0.5) * 2.0;
                float r = length(p);
                float angle = abs(degrees(atan2(p.x, p.y)));
                // Inside the disc and inside the fan, with soft edges.
                float inside = smoothstep(1.0, 0.985, r) * smoothstep(_Arc + 1.0, _Arc - 1.0, angle);
                float rim = smoothstep(0.93, 0.97, r);
                // The fan's straight edges glow like the rim.
                float edge = _Arc < 179.0 ? smoothstep(_Arc - 5.0, _Arc - 1.5, angle) * smoothstep(0.03, 0.1, r) : 0.0;
                float filled = smoothstep(_Fill + 0.01, _Fill - 0.01, r);
                float front = smoothstep(0.05, 0.0, abs(r - _Fill)) * step(0.02, _Fill);
                float strength = 0.14 + 0.24 * filled + 0.55 * max(rim, edge) + 0.5 * front;
                float alpha = saturate(_Alpha) * inside * saturate(strength);
                return float4(_Color.rgb * alpha, alpha * 0.85);
            }
            ENDHLSL
        }
    }
}
