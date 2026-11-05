// Halo auras and rune circles: a glowing pattern on the floor, a curtain of thin beams rising around the body, and a ring over
// the head, all real shapes with real positions. The mesh's uv holds (around 0..1, across or up 0..1, unused,
// part), where part is 0 for the curtain, 1 for the floor and 2 for the head ring.
Shader "Repulsor/AuraHalo"
{
    Properties
    {
        [NoScaleOffset] _Noise ("Noise", 2D) = "gray" {}
        _Pattern ("Floor Pattern: 0 Ring, 1 Sun, 2 Runes", Float) = 0
        _CurtainHeight ("Curtain Height (m)", Float) = 1.8
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

            #include "EnvironmentDepth.hlsl"
            #include "AuraColor.hlsl"

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            float _AuraPower;     // Set by AuraView: 0 when out, 1 when fully lit.

            CBUFFER_START(UnityPerMaterial)
                float _Pattern;
                float _CurtainHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 details : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 details : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 position = input.positionOS.xyz;
                if (input.details.w < 0.5)
                    position.y *= _CurtainHeight;

                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.details = input.details;
                return output;
            }

            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).r;
            }

            float Band(float x, float centre, float width)
            {
                float d = (x - centre) / width;
                return exp(-d * d);
            }

            float Random(float a, float b)
            {
                return frac(sin(a * 12.9898 + b * 78.233) * 43758.5453);
            }

            float LineTo(float2 p, float2 a, float2 b)
            {
                float2 along = b - a;
                float t = saturate(dot(p - a, along) / max(dot(along, along), 0.0001));
                return length(p - a - along * t);
            }

            // One rune, drawn in a -1..1 square: a few strokes between points of a 3x3 grid, chosen by the
            // rune's number, so each is different but all share a hand.
            float Rune(float2 p, float number)
            {
                float nearest = 10.0;
                for (int stroke = 0; stroke < 4; stroke++)
                {
                    float2 from = floor(float2(Random(number, stroke), Random(number, stroke + 7.0)) * 3.0) - 1.0;
                    float2 to = floor(float2(Random(number, stroke + 13.0), Random(number, stroke + 19.0)) * 3.0) - 1.0;
                    nearest = min(nearest, LineTo(p, from * 0.8, to * 0.8));
                }
                return smoothstep(0.17, 0.07, nearest);
            }

            // Thin rings, a turning six-pointed star, and a band of runes turning the other way.
            float RuneCircle(float around, float out01, float time)
            {
                const int RuneCount = 18;
                float rings = Band(out01, 0.93, 0.008) + Band(out01, 0.9, 0.004)
                    + Band(out01, 0.64, 0.008) + Band(out01, 0.61, 0.004) + Band(out01, 0.08, 0.008);

                float light = rings * 1.2 + Band(out01, 0.77, 0.2) * 0.12;

                float turned = frac(around + time * 0.012) * RuneCount;
                float number = floor(turned);
                float2 glyph = float2((turned - number - 0.5) * 2.4, (out01 - 0.77) / 0.082);
                if (abs(glyph.y) < 1.15)
                    light += Rune(glyph, number) * 1.3;

                float2 spot = out01 * float2(cos(around * 6.2831853), sin(around * 6.2831853));
                float star = 0.0;
                for (int side = 0; side < 6; side++)
                {
                    float angle = side * 1.0471976 - time * 0.09;
                    star += Band(dot(spot, float2(cos(angle), sin(angle))), 0.3, 0.005);
                }
                return light + star * step(out01, 0.61) * 0.9;
            }

            float FloorLight(float around, float out01, float time)
            {
                if (_Pattern > 1.5)
                    return RuneCircle(around, out01, time);

                float angle = around * 6.2831853;
                float light = Band(out01, 0.62, 0.05) * 1.1 + Band(out01, 0.62, 0.2) * 0.3;
                if (_Pattern < 0.5)
                {
                    float swirl = Noise(float2(around * 6.0 + time * 0.1, out01 * 0.4 - time * 0.08));
                    return light * (0.5 + 3.0 * swirl);
                }

                // Twelve petals turning one way and a ring of marks turning the other.
                float petals = abs(cos(angle * 6.0 + time * 0.35));
                float outline = Band(out01, 0.28 + 0.5 * petals, 0.016);
                float marks = smoothstep(0.82, 0.92, abs(cos(angle * 36.0 - time * 0.5))) * Band(out01, 0.84, 0.035);
                float inner = Band(out01, 0.2, 0.02) + Band(out01, 0.92, 0.012);
                return light * 0.8 + outline * 1.2 + marks * 1.1 + inner * 0.9;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float around = input.details.x;
                float along = input.details.y;
                float part = input.details.w;
                float time = _Time.y;
                float power = saturate(_AuraPower * 2.0);
                float visible = VisibleAt(DistanceToRealQuick(input.positionWS), DistanceToPoint(input.positionWS));

                float light;
                if (part > 1.5)
                {
                    light = Band(along, 0.5, 0.2) * (1.0 + 0.2 * sin(time * 2.5 + around * 25.1327412));
                }
                else if (part > 0.5)
                {
                    light = FloorLight(around, along, time);
                }
                else
                {
                    // Noise squeezed thin around the ring and long upwards makes separate beams.
                    float beams = Noise(float2(around * 9.0, along * 0.08 - time * 0.12));
                    float finer = Noise(float2(around * 17.0 + 0.3, along * 0.12 - time * 0.2));
                    light = pow(saturate(beams * finer * 12.0), 3.0) * pow(1.0 - along, 1.6) * smoothstep(0.0, 0.05, along);
                }

                light = saturate(light) * visible * power;

                // Held below the hottest part of the fire colour, so bright lines keep the chosen colour.
                return float4(AuraFire(light * 0.5) * light * 1.3, light * 0.35);
            }
            ENDHLSL
        }
    }
}
