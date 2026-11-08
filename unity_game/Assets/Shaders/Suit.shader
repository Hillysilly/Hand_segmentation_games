// A suit, drawn cheaply enough for the headset: one light and a simple room reflection in place of full
// physically based lighting. It hides behind live depth, but only behind things well in front of it, because
// the person wearing it is in the depth too, and would otherwise cut holes in their own suit.
Shader "Repulsor/Suit"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Base Color", 2D) = "white" {}
        _BaseColor ("Base Color Factor", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _MetalRoughMap ("Metallic (B), Roughness (G)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Roughness ("Roughness", Range(0, 1)) = 0.5
        [NoScaleOffset] _NormalMap ("Normal Map", 2D) = "bump" {}
        [NoScaleOffset] _EmissionMap ("Emission", 2D) = "white" {}
        _Emission ("Emission Factor", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "EnvironmentDepth.hlsl"

            #define HIDE_MARGIN 0.3

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetalRoughMap);
            SAMPLER(sampler_MetalRoughMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Metallic;
                float _Roughness;
                float4 _Emission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
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
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.uv = input.uv;
                return output;
            }

            // A stand-in for the room: a bright ceiling, a dim floor and a band of light around the horizon.
            float3 RoomLight(float3 direction, float blur)
            {
                float up = direction.y;
                float3 sky = lerp(float3(0.1, 0.09, 0.09), float3(0.95, 0.92, 0.88), smoothstep(-0.2, 0.8, up));
                float band = exp(-up * up / (0.008 + blur * 0.3));
                return sky + band * (0.75 - blur * 0.55);
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float visible = VisibilityQuick(input.positionWS, HIDE_MARGIN, 0.08);
                if (visible <= 0.0)
                    discard;

                float4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float4 metalRough = SAMPLE_TEXTURE2D(_MetalRoughMap, sampler_MetalRoughMap, input.uv);
                float metallic = saturate(_Metallic * metalRough.b);
                float roughness = saturate(_Roughness * metalRough.g);

                float3 normal = normalize(input.normalWS);
                float3 tangent = normalize(input.tangentWS.xyz);
                float3 bitangent = cross(normal, tangent) * input.tangentWS.w;
                float3 detail = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));
                normal = normalize(tangent * detail.x + bitangent * detail.y + normal * detail.z);

                float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
                float3 toLight = normalize(float3(0.3, 0.8, -0.5));
                float3 halfway = normalize(toLight + toEye);
                float facing = saturate(dot(normal, toEye));

                float3 mirror = reflect(-toEye, normal);
                float3 room = RoomLight(mirror, roughness);
                float fresnel = pow(1.0 - facing, 5.0);

                float shine = pow(saturate(dot(normal, halfway)), lerp(180.0, 12.0, roughness)) * (1.6 - roughness);
                float3 diffuse = base.rgb * (1.0 - metallic) * (RoomLight(normal, 1.0) * 0.55 + saturate(dot(normal, toLight)) * 0.6);
                float3 tintedMirror = lerp(float3(0.04, 0.04, 0.04), base.rgb, metallic);
                float3 reflection = (room + shine) * lerp(tintedMirror, float3(1.0, 1.0, 1.0), fresnel * (1.0 - roughness));

                float3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _Emission.rgb;
                return float4((diffuse + reflection + emission) * visible, visible);
            }
            ENDHLSL
        }
    }
}
