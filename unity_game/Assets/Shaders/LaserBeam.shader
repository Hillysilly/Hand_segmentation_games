// The laser: a ribbon that always faces the eye, painted with soft bands of light and flowing noise, so
// there is no geometry to see. Bloom can't do this job because passthrough needs post-processing off.
Shader "Repulsor/Laser Beam"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise", 2D) = "gray" {}
        _CoreColor ("Core Color", Color) = (0.55, 0.9, 1, 1)
        _OuterColor ("Outer Color", Color) = (0.1, 0.4, 1, 1)
        _NoiseScale ("Noise Scale (per m)", Float) = 0.9
        _FlowSpeed ("Flow Speed", Float) = 2.2
        _Cover ("Cover Behind", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // Premultiplied. Low alpha means this mostly adds light rather than hiding the room.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "EyeRibbon.hlsl"

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _OuterColor;
                float _NoiseScale;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = EyeRibbonPosition(input.positionOS.xyz, output.alongMeters);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.across = input.positionOS.x;
                output.along01 = input.positionOS.z;
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Starts narrow in the palm and opens out. This belongs here, not in the vertex shader:
                // tapering the mesh would bend the interpolation at every segment into a zigzag.
                float opening = lerp(0.3, 1.0, saturate(input.alongMeters / 0.5));
                float x = input.across / opening;
                float flow = _Time.y * _FlowSpeed;

                // The beam swells slowly along its length. This noise doesn't vary across the beam, so the
                // core stays straight. Noise across it would make the bright area follow the noise's grid.
                float swell = lerp(0.85, 1.2, SAMPLE_TEXTURE2D(_Noise, sampler_Noise,
                    float2(input.alongMeters * _NoiseScale - flow, 0.13)).r);

                // Long streaks running with the beam, only in the softer light around the core.
                float streak = SAMPLE_TEXTURE2D(_Noise, sampler_Noise,
                    float2(input.alongMeters * _NoiseScale * 0.4 - flow * 0.6, x * 2.2 + 0.37)).r;

                float distanceFromCentre = abs(x) / swell;
                float squared = distanceFromCentre * distanceFromCentre;

                float core = exp(-squared * 200.0);
                float inner = exp(-squared * 26.0);
                float outer = exp(-squared * 4.0);

                // Light dies away before the ribbon ends, so its edge never shows.
                float edge = saturate(1.0 - x * x);
                outer *= edge * edge;

                float3 color = float3(1.0, 1.0, 1.0) * core * 1.5
                    + _CoreColor.rgb * inner * (0.35 + 0.45 * streak)
                    + _OuterColor.rgb * outer * (0.2 + 0.5 * streak);

                float ends = saturate(input.alongMeters / 0.06) * saturate((1.0 - input.along01) / 0.1);
                color *= ends;

                float alpha = saturate(core + inner * 0.5) * ends * max(_Cover, core * 0.9);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
