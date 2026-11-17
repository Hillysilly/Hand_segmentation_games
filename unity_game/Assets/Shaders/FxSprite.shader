// Glowing effect sprites (energy ball looks: rings, flames, stars, sparks). Additive, so black is see-through and
// overlapping sprites build up to white. The texture's alpha is the shape; the colour comes from _Color times the
// vertex colour (particles set that), and the brightest middle of each sprite runs white-hot by _Hot.
Shader "Repulsor/FxSprite"
{
    Properties
    {
        _MainTex ("Shape (alpha)", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Hot ("White-hot core", Range(0, 2)) = 0.6
        _Scroll ("Scroll V per second (beams)", Float) = 0
        _FadeEnds ("Fade the top and bottom edges", Range(0, 1)) = 0
        _Occlude ("Hide behind real things (robot eyes)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "EnvironmentDepth.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Hot;
                float _Scroll;
                float _FadeEnds;
                float _Occlude;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 raw : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex) - float2(0.0, _Time.y * _Scroll);
                output.raw = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half shape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half ends = smoothstep(0.0, 0.12, input.raw.y) * smoothstep(1.0, 0.7, input.raw.y);
                half strength = shape * input.color.a * _Color.a * lerp(1.0, ends, _FadeEnds);
                if (_Occlude > 0.0)
                    strength *= lerp(1.0, VisibilityQuick(input.positionWS, 0.1, 0.1), _Occlude);
                half3 color = _Color.rgb * input.color.rgb * strength + strength * strength * strength * _Hot;
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
