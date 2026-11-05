// The headset's live depth, for shaders that hide behind real things. AR Foundation's AR Shader Occlusion
// component sets these each frame. Distances are along the depth camera's view, in metres.
#ifndef REPULSOR_ENVIRONMENT_DEPTH
#define REPULSOR_ENVIRONMENT_DEPTH

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_ARRAY(_EnvironmentDepthTexture);
SAMPLER(sampler_EnvironmentDepthTexture);
float4 _EnvironmentDepthTexture_TexelSize;
float4x4 _EnvironmentDepthProjectionMatrices[2];
float4 _NdcLinearConversionParameters;
int _IsOcclusionOn;

#define NOTHING_REAL 100000.0

float LinearDepth(float ndc)
{
    return _NdcLinearConversionParameters.x / (ndc + _NdcLinearConversionParameters.y);
}

uint DepthEye()
{
#if defined(USING_STEREO_MATRICES)
    return unity_StereoEyeIndex;
#else
    return 0;
#endif
}

// How far this point is from the depth camera.
float DistanceToPoint(float3 positionWS)
{
    float4 clip = mul(_EnvironmentDepthProjectionMatrices[DepthEye()], float4(positionWS, 1.0));
    return LinearDepth(clip.z / clip.w);
}

// How far the nearest real thing is along the line to this point.
float DistanceToReal(float3 positionWS)
{
    if (_IsOcclusionOn == 0)
        return NOTHING_REAL;

    uint eye = DepthEye();
    float4 clip = mul(_EnvironmentDepthProjectionMatrices[eye], float4(positionWS, 1.0));
    float2 uv = (clip.xy / clip.w + 1.0) * 0.5;
    if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0)
        return NOTHING_REAL;

    // Depth is coarse, so average a few samples to soften the edge of the cut-out.
    float2 offset = _EnvironmentDepthTexture_TexelSize.xy * 1.5;
    float depth = 0.0;
    depth += SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv + float2(-offset.x, -offset.y), eye, 0.0).r;
    depth += SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv + float2(offset.x, -offset.y), eye, 0.0).r;
    depth += SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv + float2(-offset.x, offset.y), eye, 0.0).r;
    depth += SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv + float2(offset.x, offset.y), eye, 0.0).r;
    return LinearDepth(depth * 0.5 - 1.0);
}

// As DistanceToReal with a single sample, for effects that draw over many pixels many times.
float DistanceToRealQuick(float3 positionWS)
{
    if (_IsOcclusionOn == 0)
        return NOTHING_REAL;

    uint eye = DepthEye();
    float4 clip = mul(_EnvironmentDepthProjectionMatrices[eye], float4(positionWS, 1.0));
    float2 uv = (clip.xy / clip.w + 1.0) * 0.5;
    if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0)
        return NOTHING_REAL;

    float depth = SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv, eye, 0.0).r;
    return LinearDepth(depth * 2.0 - 1.0);
}

// 1 where a point is clear of the real world, 0 where something real is closer.
float VisibleAt(float real, float distance)
{
    return saturate((real - distance + 0.03) / 0.12);
}

#endif
