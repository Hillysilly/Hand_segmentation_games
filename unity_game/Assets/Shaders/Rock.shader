// Stone for the Earth character: brown rock lit by a simple sun, with glowing orange-red cracks from a cellular
// (Voronoi) pattern in object space, so it needs no UVs (the Kenney rocks have none worth using). _Glow brightens the
// cracks (rock orbs and stuck rocks about to blow); _Sparkle adds gold and silver flecks (the level 3 aura).
Shader "Repulsor/Rock"
{
    Properties
    {
        _BaseColor ("Stone", Color) = (0.42, 0.29, 0.19, 1)
        _CrackColor ("Cracks", Color) = (1, 0.35, 0.08, 1)
        _Scale ("Crack scale", Float) = 3.5
        _Glow ("Glow", Range(0, 4)) = 1
        _Sparkle ("Gold and silver flecks", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _CrackColor;
                float _Scale;
                float _Glow;
                float _Sparkle;
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
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
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
                // Scale-free pattern: object space times the object's size, so big and small rocks crack alike.
                float size = length(UNITY_MATRIX_M._m00_m10_m20);
                output.positionOS = input.positionOS.xyz * size;
                return output;
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            // Distance to the nearest cell edge of a 3D Voronoi pattern, 0 on an edge.
            float CellEdge(float3 p, out float cellId)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                cellId = 0;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float3 h = Hash3(i + g);
                    float3 r = g + h - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; cellId = h.x; }
                    else if (d < d2) { d2 = d; }
                }
                return sqrt(d2) - sqrt(d1);
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal = normalize(input.normalWS);
                float cell;
                float edge = CellEdge(input.positionOS * _Scale, cell);
                float crack = 1.0 - smoothstep(0.008, 0.045, edge);
                // Flat-ish lighting so it reads in passthrough: a sun from above-front plus ambient.
                float light = 0.45 + 0.55 * saturate(dot(normal, normalize(float3(0.3, 0.9, 0.35))));
                float3 stone = _BaseColor.rgb * (0.8 + 0.4 * cell) * light;
                float pulse = 0.85 + 0.15 * sin(_Time.y * 3.0 + cell * 6.0);
                float3 color = lerp(stone, _CrackColor.rgb * _Glow * pulse, crack * saturate(_Glow));
                if (_Sparkle > 0.0)
                {
                    float fleck = step(0.93, frac(cell * 17.0)) * (1.0 - crack);
                    float3 metal = frac(cell * 7.0) > 0.5 ? float3(1.0, 0.8, 0.3) : float3(0.85, 0.88, 0.95);
                    color = lerp(color, metal * (1.2 + 0.6 * sin(_Time.y * 5.0 + cell * 20.0)), fleck * _Sparkle);
                }
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
