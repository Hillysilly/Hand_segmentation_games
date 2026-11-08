// A blast front on a real cone wall: a torn, unevenly burning leading edge, and streaks of energy thrown back
// along the wall. Real geometry gives it true depth, it's brightest where it turns
// edge-on, as the aura is, and real things cut it off. The mesh's uv holds (around 0..1, back from the front 0..1).
Shader "Repulsor/Shock Wave"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise", 2D) = "gray" {}
        _CoreColor ("Core Color", Color) = (0.55, 0.9, 1, 1)
        _OuterColor ("Outer Color", Color) = (0.1, 0.4, 1, 1)
        _Life ("Life", Range(0, 1)) = 0
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
                float _Life;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 details : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 place : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Bulging in and out as it travels, so the front is never a clean circle.
                float3 position = input.positionOS.xyz;
                float around = input.details.x * 6.2831853;
                float time = _Time.y;
                position.xz *= 1.0 + sin(around * 3.0 + time * 5.0) * 0.022 + sin(around * 7.0 - time * 7.0) * 0.014
                    + sin(around * 13.0 + time * 9.0) * 0.008;

                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.place = input.details.xy;
                return output;
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).r;
            }

            float Band(float position, float width)
            {
                return exp(-position * position / (width * width));
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float around = input.place.x;
                float back = input.place.y;
                float time = _Time.y;

                // Whole-number repeats around, so the noise meets itself with no seam. It tears the front so
                // it isn't a perfect circle, and it sits just inside the mesh's edge so that edge never shows.
                float tear = Noise(float2(around * 7.0, time * 0.5)) * 0.6 + Noise(float2(around * 17.0, -time * 0.7)) * 0.4;
                float front = back - (0.06 + tear * 0.2);
                float rim = Band(front, 0.03 + _Life * 0.05);
                float halo = Band(front, 0.13) * smoothstep(0.0, 0.05, back);

                // The rim burns unevenly, brighter in some places and nearly gone in others.
                float strength = saturate(Noise(float2(around * 5.0, -time * 0.9)) * 3.0 - 0.25);
                rim *= 0.25 + strength;

                // Streaks thrown back along the wall. The noise averages well under a half.
                float streaks = Noise(float2(around * 19.0, back * 0.5 + time * 1.7));
                float fine = Noise(float2(around * 43.0, back * 0.9 + time * 2.3));
                float trail = (1.0 - back) * saturate((streaks * 0.65 + fine * 0.5 - 0.2) * 3.5);

                float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
                float edgeOn = 1.0 - abs(dot(normalize(input.normalWS), toEye));
                float shell = 0.3 + 0.9 * pow(edgeOn, 1.2);

                // Off the mesh's far end nothing shows. It comes up fast and dies away over its range.
                float tailOff = 1.0 - smoothstep(0.7, 1.0, back);
                float life = saturate(_Life / 0.04) * (1.0 - _Life * _Life);
                float fade = tailOff * life
                    * VisibilityQuick(input.positionWS);

                float3 color = float3(1.0, 1.0, 1.0) * rim * 0.9
                    + _CoreColor.rgb * (rim * 0.7 + halo * 0.5 * (0.4 + trail)) * shell
                    + lerp(_OuterColor.rgb, _CoreColor.rgb, trail * 0.5) * trail * shell * 1.1;

                float alpha = saturate(rim * 0.6 + (halo + trail) * 0.14);
                return float4(color * fade, alpha * fade);
            }
            ENDHLSL
        }
    }
}
