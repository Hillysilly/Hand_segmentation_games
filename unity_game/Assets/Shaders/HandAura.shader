// The level 3 hand aura: drawn on the XR Hands sample hand, posed like your tracked hand. Two shells, both pushed
// out along the normals (world metres) and added onto the view, so your real hand shows through the middle:
// - a close shell glowing only at its silhouette, outlining the hand and each finger;
// - a looser shell of flickering, rising energy, also strongest at the silhouette.
// Both fade out just past the wrist (_WristPos, _WristFwd: where the wrist is and the way the fingers point).
Shader "Repulsor/HandAura"
{
    Properties
    {
        _Color ("Color", Color) = (0.4, 0.8, 1, 1)
        _Strength ("Strength", Range(0, 2)) = 1
        _Inner ("Inner Shell (m)", Float) = 0.003
        _Outer ("Outer Shell (m)", Float) = 0.011
        _WristPos ("Wrist", Vector) = (0, 0, 0, 0)
        _WristFwd ("Wrist Forward", Vector) = (0, 0, 1, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float _Strength;
        float _Inner;
        float _Outer;
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

    Varyings Shell(Attributes input, float push)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * push;
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

    // How edge-on the surface is to the eye: 0 face-on, 1 at the silhouette.
    float Edge(Varyings input)
    {
        float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
        return 1.0 - saturate(abs(dot(normalize(input.normalWS), toEye)));
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
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        // The outline.
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            Varyings Vertex(Attributes input) { return Shell(input, _Inner); }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float edge = Edge(input);
                float glow = pow(edge, 2.2) * 1.1 + 0.05;
                return float4(_Color.rgb * glow * _Strength * WristFade(input.positionWS), 0);
            }
            ENDHLSL
        }

        // The energy around it.
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            Varyings Vertex(Attributes input) { return Shell(input, _Outer); }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float edge = Edge(input);
                float3 p = input.positionWS * 55.0 - float3(0, _Time.y * 2.2, 0);
                float n = Noise(p) * 0.65 + Noise(p * 2.3 + 7.0) * 0.35;
                float flicker = smoothstep(0.35, 0.8, n);
                float glow = pow(edge, 1.4) * (0.25 + flicker * 0.9);
                return float4(_Color.rgb * glow * 0.75 * _Strength * WristFade(input.positionWS), 0);
            }
            ENDHLSL
        }
    }
}
