// A ball of plasma on a quad that faces the eye: a white-hot core, veins of light crawling over a surface that
// looks curved, a bright rim, and a soft glow that pulses. The ball fills the middle of the quad and the glow
// fades out before the quad's edge. It hides behind real things, such as the hand holding it.
Shader "Repulsor/Energy Ball"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise", 2D) = "gray" {}
        _CoreColor ("Core Color", Color) = (0.55, 0.9, 1, 1)
        _OuterColor ("Outer Color", Color) = (0.1, 0.4, 1, 1)
        _BallSize ("Ball Radius On Quad", Range(0.1, 0.6)) = 0.3
        _PulseSpeed ("Pulse Speed", Float) = 9
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
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "EnvironmentDepth.hlsl"

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _OuterColor;
                float _BallSize;
                float _PulseSpeed;
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

                float3 centre = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float size = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 view = normalize(GetCameraPositionWS() - centre);
                float3 right = normalize(cross(float3(0.0, 1.0, 0.0), view));
                float3 up = cross(view, right);

                output.positionWS = centre + (right * input.positionOS.x + up * input.positionOS.y) * size;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.positionOS.xy * 2.0;
                return output;
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).r;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float time = _Time.y;
                float radius = length(input.uv);
                float ball = radius / _BallSize;

                // Two beats at once, so the pulse never settles into a plain throb.
                float pulse = 0.5 + 0.3 * sin(time * _PulseSpeed) + 0.2 * sin(time * _PulseSpeed * 0.37 + 1.0);

                // Noise pushed outward near the rim, so the pattern seems to wrap round a curved surface.
                float height = sqrt(saturate(1.0 - ball * ball));
                float2 surface = input.uv / (_BallSize * (1.0 + height));
                float first = Noise(surface * 0.45 + float2(time * 0.31, -time * 0.23));
                float second = Noise(surface * 0.7 + float2(-time * 0.43, time * 0.37));

                // Where two moving noises cross, they draw thin, wandering veins.
                float veins = saturate(1.0 - abs(first - second) * 9.0);
                veins *= veins;

                float inside = 1.0 - smoothstep(0.88, 1.04, ball);
                float rimOffset = ball - 1.0;
                float rim = exp(-rimOffset * rimOffset * 28.0);
                float core = exp(-ball * ball * (3.4 - pulse));
                float glow = exp(-radius * radius * lerp(10.0, 6.5, pulse));

                // The quad's edge must stay invisible however far the glow reaches.
                float edge = saturate(1.0 - radius);
                edge *= edge;

                float visible = Visibility(input.positionWS);

                float3 color = float3(1.0, 1.0, 1.0) * core * 1.3
                    + _CoreColor.rgb * inside * (0.3 + veins * 1.1)
                    + _CoreColor.rgb * rim * (0.35 + 0.35 * pulse)
                    + _OuterColor.rgb * glow * (0.55 + 0.5 * pulse);
                color *= edge * visible;

                float alpha = saturate(core + inside * 0.6) * edge * visible * max(_Cover, core * 0.9);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
