// The level 3 hand aura: drawn on the XR Hands sample hand, posed like your tracked hand. One shell pushed out along
// the normals (world metres), added onto the view so your real hand shows through the middle: a bright outline at its
// silhouette and flickering, rising energy toward its edges. It fades out just past the wrist (_WristPos, _WristFwd:
// where the wrist is and the way the fingers point).
// Only the front of the shell is drawn (11/15): a first pass writes its depth and nothing else, so in a fist the
// fingers behind don't glow through the ones in front. It draws after other see-through effects, so that depth
// hides nothing but the aura itself.
Shader "Repulsor/HandAura"
{
    Properties
    {
        _Color ("Color", Color) = (0.4, 0.8, 1, 1)
        _Strength ("Strength", Range(0, 2)) = 1
        _Shell ("Shell (m)", Float) = 0.008
        _WristPos ("Wrist", Vector) = (0, 0, 0, 0)
        _WristFwd ("Wrist Forward", Vector) = (0, 0, 1, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float _Strength;
        float _Shell;
        float4 _WristPos;
        float4 _WristFwd;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vertex(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * _Shell;
        output.positionWS = positionWS;
        output.positionCS = TransformWorldToHClip(positionWS);
        output.normalWS = normalWS;
        return output;
    }

    // 0 behind the wrist, 1 from 2.5 cm in front of it.
    float WristFade(float3 positionWS)
    {
        return smoothstep(-0.005, 0.025, dot(positionWS - _WristPos.xyz, normalize(_WristFwd.xyz)));
    }

    float Hash(float3 p)
    {
        p = frac(p * 0.3183099 + 0.1);
        p *= 17.0;
        return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
    }

    float Noise(float3 p)
    {
        float3 i = floor(p);
        float3 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }

        // Depth only: the nearest surface of the shell, so the glow below draws once per pixel.
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }

        // The glow, on that nearest surface only.
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
                float edge = 1.0 - saturate(abs(dot(normalize(input.normalWS), toEye)));
                float3 p = input.positionWS * 55.0 - float3(0, _Time.y * 2.2, 0);
                float n = Noise(p) * 0.65 + Noise(p * 2.3 + 7.0) * 0.35;
                float flicker = smoothstep(0.35, 0.8, n);
                float outline = pow(edge, 2.6) * 1.2;
                float energy = pow(edge, 1.3) * (0.15 + flicker * 0.7);
                float glow = (outline + energy + 0.04) * _Strength * WristFade(input.positionWS);
                return float4(_Color.rgb * glow, 0);
            }
            ENDHLSL
        }
    }
}
