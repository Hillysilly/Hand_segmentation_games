// A comet's tail: a ribbon facing the eye, like the laser, that narrows to nothing and wanders more the
// further it is from the head, with streaks of light racing back along it. Hidden behind real things.
Shader "Repulsor/Comet Tail"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise", 2D) = "gray" {}
        _CoreColor ("Core Color", Color) = (0.55, 0.9, 1, 1)
        _OuterColor ("Outer Color", Color) = (0.1, 0.4, 1, 1)
        _FlowSpeed ("Flow Speed", Float) = 3
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
            #include "EyeRibbon.hlsl"

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _OuterColor;
                float _FlowSpeed;
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
                float across : TEXCOORD0;
                float alongMeters : TEXCOORD1;
                float along01 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = EyeRibbonPosition(input.positionOS.xyz, output.alongMeters);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.across = input.positionOS.x;
                output.along01 = input.positionOS.z;
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

                float along = input.along01;
                float flow = _Time.y * _FlowSpeed;

                // The narrowing is done here rather than in the mesh, for the reason given in the laser's shader.
                float wander = (Noise(float2(input.alongMeters * 0.9 + flow, 0.21)) - 0.5) * along * 0.7;
                float narrow = max(pow(1.0 - along, 1.3), 0.03);
                float x = (input.across - wander) / narrow;

                float streak = Noise(float2(input.alongMeters * 0.8 + flow * 0.8, x * 0.9 + 0.37));
                float squared = x * x;
                float core = exp(-squared * 45.0);
                float inner = exp(-squared * 8.0);
                float outer = exp(-squared * 2.2);

                // It starts inside the ball, so its wide glow never shows a cut-off end there.
                float fade = (1.0 - along) * (1.0 - along) * saturate(input.alongMeters / 0.1);
                float edge = saturate(1.0 - input.across * input.across);
                fade *= edge * edge;
                fade *= Visibility(input.positionWS);

                float3 color = float3(1.0, 1.0, 1.0) * core * 1.2
                    + _CoreColor.rgb * inner * (0.3 + 1.2 * streak)
                    + _OuterColor.rgb * outer * (0.3 + 1.1 * streak);

                float alpha = saturate(core + inner * 0.5) * fade * max(_Cover, core * 0.9);
                return float4(color * fade, alpha);
            }
            ENDHLSL
        }
    }
}
