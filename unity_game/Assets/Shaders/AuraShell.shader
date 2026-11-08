// Fire painted on real curved surfaces that wrap the body, the way mesh effects in games are made. Because
// the surfaces have a real shape and position, both eyes see their depth, and they're brightest where they
// turn edge-on, so the person stays visible through the middle. Flat parts of the mesh are a ring on the floor.
// The mesh's uv holds (around 0..1, up 0..1, shell seed, 1 on the floor ring). A second uv holds (ridge, phase)
// on meshes torn into flame tips: ridge is 1 on a tip's centre line, and each tip has its own phase.
Shader "Repulsor/AuraShell"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise", 2D) = "gray" {}
        _RiseSpeed ("Rise Speed", Float) = 1
        _FadeStart ("Fade Start", Range(0, 1.2)) = 0.58
        _FadeEnd ("Fade End", Range(0, 1.2)) = 0.9
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
            #include "AuraColor.hlsl"

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            float _AuraPower;     // Set by AuraView: 0 when out, 1 when fully lit.

            CBUFFER_START(UnityPerMaterial)
                float _RiseSpeed;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 details : TEXCOORD0;
                float2 spike : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 details : TEXCOORD2;
                float ridge : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Rippling in and out as it rises, so the shells aren't smooth cones.
                float3 position = input.positionOS.xyz;
                float around = input.details.x * 6.2831853;
                float up = input.details.y;
                float time = _Time.y * _RiseSpeed;
                float ripple = sin(around * 3.0 + up * 7.0 - time * 3.0 + input.details.z * 9.0) * 0.05
                    + sin(around * 7.0 - up * 5.0 - time * 4.3) * 0.025;
                position.xz *= 1.0 + ripple * up * (1.0 - input.details.w);

                // Flame tips stretch and whip about, each in its own time. Their bases stay still.
                float tip = input.spike.x * up * up;
                position.y *= 1.0 + tip * sin(time * 3.7 + input.spike.y * 2.0) * 0.13;
                position.xz += position.zx * float2(1.0, -1.0) * tip * sin(time * 2.3 + input.spike.y) * 0.16;

                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.details = input.details;
                output.ridge = input.spike.x;
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

                float visible = VisibilityQuick(input.positionWS);
                float power = saturate(_AuraPower * 2.0);
                float time = _Time.y * _RiseSpeed;
                float around = input.details.x;
                float up = input.details.y;
                float seed = input.details.z;

                // The floor ring: a soft band of light that breaks up as it turns.
                if (input.details.w > 0.5)
                {
                    float band = exp(-pow((up - 0.5) / 0.22, 2.0));
                    float swirl = Noise(float2(around * 4.0 - time * 0.12, up * 0.6 + time * 0.1));
                    float ring = band * (0.25 + 1.3 * swirl * swirl) * visible * power;
                    return float4(AuraFire(ring) * ring, ring * 0.25);
                }

                // Whole-number repeats around, so the noise meets itself with no seam.
                // The noise is built on a square grid, so its rows are bent to stop them showing as flat bands.
                float bend = sin(around * 12.5663706 + seed * 5.0) * 0.07 + sin(around * 31.4159265) * 0.03;
                float slow = Noise(float2(around * 3.0 + seed, up * 0.55 + bend - time * 0.42));
                float fast = Noise(float2(around * 5.0 - seed * 2.0, up * 0.9 - bend - time * 0.75));
                float wisps = slow * 0.6 + fast * 0.4;

                // Tall licks: the surface burns away higher up, leaving flame-shaped tips with no clean top.
                // Ridges burn away less higher up, so the tips keep their points and the middle stays see-through.
                float burn = saturate((wisps - 0.2 - up * 0.17 + input.ridge * up * 0.3) * 9.0);
                float base = smoothstep(0.0, 0.1, up) * (1.0 - smoothstep(_FadeStart, _FadeEnd, up));

                // Bright where the surface turns edge-on, nearly clear where it faces the viewer.
                float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
                float edgeOn = 1.0 - abs(dot(normalize(input.normalWS), toEye));
                float shell = 0.12 + 0.95 * pow(edgeOn, 0.7);

                float heat = burn * (1.05 - up * 0.65);
                float fade = burn * base * shell * visible * power;
                return float4(AuraFire(saturate(heat)) * fade * 1.4, fade * 0.55);
            }
            ENDHLSL
        }
    }
}
