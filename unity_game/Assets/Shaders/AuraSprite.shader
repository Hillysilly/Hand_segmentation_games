// One animated flame sprite for the aura's particle styles. Each particle has a real place in the room, so
// both eyes agree on its depth. Sprites hide behind real things, and those between the viewer and the body
// are faint, so the person stays visible. The particle renderer must send the Center vertex stream.
Shader "Repulsor/AuraSprite"
{
    Properties
    {
        _MainTex ("Flipbook", 2D) = "white" {}
        [Toggle] _UseAlpha ("Texture Has Alpha", Float) = 1
        _Brightness ("Brightness", Float) = 1.2
        _FrontStrength ("Strength In Front Of Body", Range(0, 1)) = 0.2
        _Tiles ("Flipbook Columns and Rows", Vector) = (1, 1, 0, 0)
        _BaseFade ("Fade Over Bottom Of Frame", Range(0.01, 1)) = 0.3
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _AuraFoot;     // Set by AuraView: the floor point between the feet.
            float _AuraPower;     // Set by AuraView: 0 when out, 1 when fully lit.

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _UseAlpha;
                float _Brightness;
                float _FrontStrength;
                float4 _Tiles;
                float _BaseFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uvAndCentre : TEXCOORD0;   // xy uv, zw centre x and y.
                float4 moreCentre : TEXCOORD1;    // x centre z.
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float strength : TEXCOORD2;
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
                output.color = input.color;
                output.uv = input.uvAndCentre.xy;

                float3 centre = float3(input.uvAndCentre.zw, input.moreCentre.x);
                float2 fromBody = centre.xz - _AuraFoot.xz;
                float2 toEye = GetCameraPositionWS().xz - centre.xz;
                float facing = dot(fromBody, toEye) / max(length(fromBody) * length(toEye), 0.0001);
                float inFront = smoothstep(0.15, 0.75, facing) * (1.0 - saturate((centre.y - _AuraFoot.y - 1.5) / 0.2));
                output.strength = lerp(1.0, _FrontStrength, inFront) * saturate(_AuraPower * 2.0);
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 flame = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float shape = _UseAlpha > 0.5 ? flame.a : max(flame.r, max(flame.g, flame.b));

                // Flames in these flipbooks run up to the edges of their frames, which would show as
                // straight cuts, and many have a flat base. Fading the frame's edges hides both.
                float2 cell = frac(input.uv * _Tiles.xy);
                float edges = smoothstep(0.0, 0.08, cell.x) * smoothstep(0.0, 0.08, 1.0 - cell.x)
                    * smoothstep(0.0, 0.08, 1.0 - cell.y) * smoothstep(0.0, _BaseFade, cell.y);
                shape *= edges;
                if (shape < 0.01)
                    return 0.0;

                float visible = VisibleAt(DistanceToRealQuick(input.positionWS), DistanceToPoint(input.positionWS));

                // The flipbook gives the flame's shape and brightness, and the wheel gives its colour.
                // Brightness, not the strongest channel: red is at full in most of an orange flame.
                float heat = saturate(dot(flame.rgb, float3(0.3, 0.59, 0.11)) * 1.15);
                float3 color = AuraFire(heat) * (_UseAlpha > 0.5 ? flame.a : max(flame.r, max(flame.g, flame.b)));
                float fade = input.color.a * input.strength * visible * edges;
                return float4(color * _Brightness * fade, shape * 0.6 * fade);
            }
            ENDHLSL
        }
    }
}
