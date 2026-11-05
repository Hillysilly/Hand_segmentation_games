// A jagged, flickering burst of light on a quad. Noise around the circle pushes rays out by different
// amounts, so the shape is never a clean disc. Lies on a surface, or faces the eye with _Billboard on.
Shader "Repulsor/Laser Burst"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise", 2D) = "gray" {}
        _CoreColor ("Core Color", Color) = (0.55, 0.9, 1, 1)
        _OuterColor ("Outer Color", Color) = (0.1, 0.4, 1, 1)
        _Billboard ("Face The Eye", Float) = 0
        _RayCount ("Ray Count", Float) = 7
        _RayStrength ("Ray Strength", Range(0, 1)) = 0.8
        _FlickerSpeed ("Flicker Speed", Float) = 3
        _Softness ("Glow Tightness", Float) = 6
        _Brightness ("Brightness", Float) = 1
        _Cover ("Cover Behind", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

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

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _OuterColor;
                float _Billboard;
                float _RayCount;
                float _RayStrength;
                float _FlickerSpeed;
                float _Softness;
                float _Brightness;
                float _Cover;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
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

                float3 positionWS;
                if (_Billboard > 0.5)
                {
                    float3 centre = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                    float size = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                    float3 view = normalize(GetCameraPositionWS() - centre);
                    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), view));
                    float3 up = cross(view, right);
                    positionWS = centre + (right * input.positionOS.x + up * input.positionOS.y) * size;
                }
                else
                {
                    positionWS = TransformObjectToWorld(input.positionOS.xyz);
                }

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.positionOS.xy * 2.0;
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float radius = length(input.uv);
                float angle = atan2(input.uv.y, input.uv.x) / 6.28318 + 0.5;
                float time = _Time.y * _FlickerSpeed;

                // Whole-number multiples of the angle, so the noise joins up all the way round.
                float wide = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(angle * round(_RayCount), time * 0.31)).r;
                float thin = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(angle * round(_RayCount * 3.0), -time * 0.47)).r;
                float rays = saturate((wide * 0.6 + thin * 0.5 - 0.2) * 1.5);
                float reach = lerp(1.0, 0.18 + 0.82 * rays * rays, _RayStrength);

                float body = saturate(1.0 - radius / max(reach, 0.01));
                body *= body;
                float glow = exp(-radius * radius * _Softness);
                float core = exp(-radius * radius * 45.0);

                // The quad's edge must stay invisible whatever the rays are doing.
                float edge = saturate(1.0 - radius);
                edge *= edge;

                float3 color = float3(1.0, 1.0, 1.0) * core * 1.6
                    + _CoreColor.rgb * body * 0.8
                    + _OuterColor.rgb * glow * 0.5;
                color *= edge * _Brightness;

                float alpha = saturate(core + body * 0.4) * edge * max(_Cover, core * 0.9);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
