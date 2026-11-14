// The tight aura on your own hands and forearms. Each pixel of a box around the hand finds how close its line of
// sight passes to the hand's bones (each a tapered capsule), so the glow outlines the whole hand as one shape, the
// way the body mask's glow outlines a person: a bright band just outside the edge, a faint fill, a soft halo.
Shader "Repulsor/TightGlow"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.75, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Edge ("Edge band (m)", Float) = 0.006
        _FadeOrigin ("Fade from (wrist)", Vector) = (0, 0, 0, 0)
        _FadeDir ("Fade toward (elbow), length in w", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_BONES 24

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Alpha;
                float _Edge;
                float4 _FadeOrigin;
                float4 _FadeDir;
            CBUFFER_END

            // Each bone's ends, with the radius at that end in w. Set per hand from the CPU.
            float4 _BoneA[MAX_BONES];
            float4 _BoneB[MAX_BONES];
            float _BoneCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
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
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 eye = GetCameraPositionWS();
                float3 ray = normalize(input.positionWS - eye);
                float best = 1e5;
                float3 nearest = 0;
                int count = (int)_BoneCount;
                for (int i = 0; i < MAX_BONES; i++)
                {
                    if (i >= count)
                        break;
                    float3 a = _BoneA[i].xyz;
                    float3 along = _BoneB[i].xyz - a;
                    float3 fromEye = a - eye;
                    float lengthSq = dot(along, along);
                    float alongRay = dot(along, ray);
                    float startRay = dot(fromEye, ray);
                    float denominator = lengthSq - alongRay * alongRay;
                    // The point on the bone nearest the line of sight, then corrected once for the bone's ends.
                    float s = denominator > 1e-9 ? saturate((alongRay * startRay - dot(fromEye, along)) / denominator) : 0.0;
                    float t = max(startRay + s * alongRay, 0.0);
                    s = lengthSq > 1e-9 ? saturate(dot(eye + t * ray - a, along) / lengthSq) : 0.0;
                    float3 onBone = a + s * along;
                    t = max(dot(onBone - eye, ray), 0.0);
                    float gap = length(eye + t * ray - onBone) - lerp(_BoneA[i].w, _BoneB[i].w, s);
                    if (gap < best)
                    {
                        best = gap;
                        nearest = onBone;
                    }
                }

                // A bright band just outside the hand's edge, a faint fill over it, and a halo beyond.
                float band = exp(-abs(best - _Edge * 0.4) / _Edge) * saturate(1.0 - best / 0.06);
                float fill = best < 0.0 ? 0.1 : 0.0;
                // The halo ends well inside the box drawn around the hand, so the box never shows.
                float halo = best > 0.0 ? exp(-best / (_Edge * 3.0)) * 0.3 * saturate(1.0 - best / 0.06) : 0.0;
                float shimmer = 0.8 + 0.2 * sin(_Time.y * 9.0 + nearest.y * 70.0 + nearest.x * 45.0)
                                    * sin(_Time.y * 5.0 - nearest.z * 60.0);
                // The forearm (no elbow joint to end it on) trails off toward the elbow.
                float along01 = _FadeDir.w > 0.0 ? max(0.0, dot(nearest - _FadeOrigin.xyz, _FadeDir.xyz)) / _FadeDir.w : 0.0;
                float trail = saturate(1.0 - along01);
                float glow = saturate((band * 0.95 + fill + halo) * shimmer) * _Alpha * trail * trail;
                if (glow < 0.003)
                    discard;
                return float4(_Color.rgb * glow * 1.5, glow * 0.7);
            }
            ENDHLSL
        }
    }
}
