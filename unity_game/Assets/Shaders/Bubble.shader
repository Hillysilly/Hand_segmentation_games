// Shield bubbles and the transformation shockwave sphere. One shader with a style number, so the Variant Lab can
// flip between looks: 0 hex, 1 flame, 2 glass, 3 thin, 4 web, 5 energy shell, 6 smoke, 7 wind, 8 rock. Drawn from both sides, since the
// player usually sees their own shield from inside, and hidden behind real things by live depth.
Shader "Repulsor/Bubble"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.75, 1, 1)
        _Style ("Style", Float) = 0
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Hit ("Hit Flash", Range(0, 1)) = 0
        _Occlude ("Hide behind real things", Range(0, 1)) = 1
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Style;
                float _Alpha;
                float _Hit;
                float _Occlude;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
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
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.uv = input.uv;
                return output;
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
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // Distance to the nearest edge of a hexagon grid, 0 on an edge.
            float HexEdge(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 centers = floor(float4(p, p - float2(0.5, 1.0)) / s.xyxy) + 0.5;
                float4 offsets = float4(p - centers.xy * s, p - (centers.zw + 0.5) * s);
                float2 local = dot(offsets.xy, offsets.xy) < dot(offsets.zw, offsets.zw) ? offsets.xy : offsets.zw;
                local = abs(local);
                return 0.5 - max(dot(local, s * 0.5), local.x);
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normal = normalize(input.normalWS);
                float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
                float facing = abs(dot(normal, toEye));
                float rim = pow(1.0 - facing, 2.5);
                float t = _Time.y;
                float3 p = normalize(input.positionOS);
                int style = (int)round(_Style);

                float glow = 0.0;
                float3 color = _Color.rgb;
                if (style == 0)
                {
                    // Hex panels: bright edges, a faint fill, and a band that sweeps upward.
                    float2 hexUv = float2(input.uv.x * 24.0, input.uv.y * 9.0);
                    float edge = 1.0 - smoothstep(0.0, 0.06, HexEdge(hexUv));
                    float sweep = smoothstep(0.15, 0.0, abs(frac(input.uv.y - t * 0.35) - 0.5) - 0.35);
                    glow = rim * 0.8 + edge * (0.35 + 0.5 * sweep) + 0.05;
                }
                else if (style == 1)
                {
                    // Tongues of flame licking upward: four octaves of noise, squeezed sideways and stretched tall,
                    // scrolling up and bent by a slower noise so the tongues sway. Thin wisps at the edge, a hot core.
                    float3 q = float3(p.x * 4.0, p.y * 1.4 - t * 1.9, p.z * 4.0);
                    q.xz += (Noise(q * 0.7 + 3.1) - 0.5) * 1.2;
                    float n = Noise(q) * 0.5 + Noise(q * 2.03 + 1.7) * 0.25 + Noise(q * 4.01 + 4.3) * 0.15 + Noise(q * 8.02 + 9.1) * 0.1;
                    float height = p.y * 0.5 + 0.5;
                    float lick = smoothstep(0.5, 0.78, n + height * 0.22 - 0.06);
                    float core = smoothstep(0.7, 0.95, n + height * 0.22 - 0.06);
                    glow = rim * 0.55 + lick * (0.3 + 0.45 * height) + core * 0.5 + 0.01;
                    color = lerp(_Color.rgb, float3(1.0, 0.95, 0.75), core * 0.8);
                }
                else if (style == 2)
                {
                    // Glass: almost clear in the middle, a crisp rim and a moving sheen.
                    float sheen = pow(saturate(dot(p, normalize(float3(sin(t * 0.7), 0.6, cos(t * 0.7))))), 24.0);
                    glow = pow(1.0 - facing, 4.0) * 1.1 + sheen * 0.7 + 0.02;
                    color = lerp(_Color.rgb, float3(1, 1, 1), 0.5);
                }
                else if (style == 3)
                {
                    // Thin: a faint skin with drifting sparkles.
                    float sparkle = step(0.985, Noise(p * 40.0 + t * 0.6));
                    glow = rim * 0.5 + sparkle * 0.9 + 0.015;
                }
                else if (style == 4)
                {
                    // Web: lines of longitude meeting at the top, rings around, slightly wavy.
                    float around = abs(frac(input.uv.x * 16.0) - 0.5);
                    float rings = abs(frac(input.uv.y * 10.0 + sin(input.uv.x * 100.53) * 0.08) - 0.5);
                    float lines = max(1.0 - smoothstep(0.0, 0.05, 0.5 - around), 1.0 - smoothstep(0.0, 0.05, 0.5 - rings));
                    glow = lines * 0.9 + rim * 0.25;
                    color = lerp(_Color.rgb, float3(0.95, 0.95, 0.95), 0.7);
                }
                else if (style == 6)
                {
                    // Smoke: a soft grey mass, thicker in the middle, slowly churning. Greys out what's behind it.
                    float churn = Noise(p * 3.0 + float3(t * 0.3, t * 0.5, 0)) * 0.6 + Noise(p * 7.0 - t * 0.4) * 0.4;
                    glow = pow(facing, 1.6) * (0.5 + 0.7 * churn) * 1.1;
                    color = _Color.rgb;
                }
                else if (style == 7)
                {
                    // Wind: pale streaks whirling sideways around the bubble, faster near the top, with gaps.
                    float angle = atan2(p.z, p.x);
                    float swirl = angle * 3.0 + p.y * 4.0 - t * (5.0 + p.y * 2.0);
                    float streaks = pow(saturate(sin(swirl) * 0.5 + 0.5), 6.0);
                    float gusts = Noise(float3(angle * 2.0 - t * 2.0, p.y * 5.0, t * 0.5));
                    glow = streaks * (0.4 + 0.8 * gusts) + rim * 0.45 + 0.01;
                    color = lerp(_Color.rgb, float3(0.95, 0.98, 1.0), 0.6);
                }
                else if (style == 8)
                {
                    // Rock: a wall of brown stone plates with glowing orange cracks between them; nearly solid.
                    float3 q = p * 5.0;
                    float plates = Noise(q) * 0.6 + Noise(q * 2.3 + 4.1) * 0.4;
                    float crack = 1.0 - smoothstep(0.0, 0.05, abs(plates - 0.5));
                    float3 stone = float3(0.42, 0.29, 0.19) * (0.6 + 0.6 * Noise(q * 4.0));
                    color = lerp(stone, float3(1.0, 0.4, 0.1) * 1.6, crack);
                    glow = 0.75 + crack * 0.8 + rim * 0.2;
                }
                else
                {
                    // Energy shell for the transformation shockwave: bright rim, streaky interior.
                    float streak = Noise(float3(p.x * 6.0, p.y * 2.0 - t * 3.0, p.z * 6.0));
                    glow = rim * 1.4 + streak * 0.35 + 0.1;
                }

                glow += _Hit * (0.6 + rim);

                // From inside (your own shield) it's faint, so you can still see to fight.
                float3 centre = UNITY_MATRIX_M._m03_m13_m23;
                float radius = length(UNITY_MATRIX_M._m00_m10_m20) * 0.5;
                if (distance(GetCameraPositionWS(), centre) < radius && style != 5 && style != 6)
                    glow *= 0.6 + 0.4 * _Hit;
                // The quick depth test: bubbles can cover the whole view, so the 4-sample one costs too much.
                float shown = _Alpha * lerp(1.0, VisibilityQuick(input.positionWS), _Occlude);
                return float4(color * glow * shown, saturate(glow) * shown * 0.85);
            }
            ENDHLSL
        }
    }
}
