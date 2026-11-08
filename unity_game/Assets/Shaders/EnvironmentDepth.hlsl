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

#define SURFACE_MARGIN 0.03
#define SURFACE_RAMP 0.12

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

// Where a point falls in the depth map and how far it is from the depth camera. False if it's outside the map.
bool FindInDepth(float3 positionWS, out float2 uv, out float distance)
{
    float4 clip = mul(_EnvironmentDepthProjectionMatrices[DepthEye()], float4(positionWS, 1.0));
    uv = (clip.xy / clip.w + 1.0) * 0.5;
    distance = LinearDepth(clip.z / clip.w);
    return _IsOcclusionOn != 0 && uv.x > 0.0 && uv.x < 1.0 && uv.y > 0.0 && uv.y < 1.0;
}

float2 DepthMapSize()
{
    uint width, height, layers;
    _EnvironmentDepthTexture.GetDimensions(width, height, layers);
    return float2(width, height);
}

// How much of a point at this distance can be seen, 0 to 1. Each of the four nearest depth pixels is tested on
// its own and the answers are blended, so a cut is a soft line rather than blocks. Blending the distances
// first wouldn't do that: between a hand and a far wall they still flip from hidden to clear all at once.
// Gathering reads all four for the price of one.
float VisibleThrough(float2 uv, float2 size, float distance, float margin, float ramp)
{
    // Gathers from the corner the four pixels share, so it can't land on a different four from the blend below.
    float2 place = uv * size - 0.5;
    float2 corner = floor(place);
    float2 blend = place - corner;
    float4 depths = GATHER_TEXTURE2D_ARRAY(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, (corner + 1.0) / size, DepthEye());
    float4 real = _NdcLinearConversionParameters.x / (depths * 2.0 - 1.0 + _NdcLinearConversionParameters.y);
    float4 clear = saturate((real - distance + margin) / ramp);

    // Gather's order goes round the square: top left, top right, bottom right, bottom left.
    return lerp(lerp(clear.w, clear.z, blend.x), lerp(clear.x, clear.y, blend.x), blend.y);
}

// 1 where a point is clear of the real world, 0 where something real is closer, with a wide soft edge. For small
// effects, as it reads the depth map four times.
float Visibility(float3 positionWS)
{
    float2 uv;
    float distance;
    if (!FindInDepth(positionWS, uv, distance))
        return 1.0;

    float2 size = DepthMapSize();
    float2 spread = 0.75 / size;
    return (VisibleThrough(uv + float2(-spread.x, -spread.y), size, distance, SURFACE_MARGIN, SURFACE_RAMP)
        + VisibleThrough(uv + float2(spread.x, -spread.y), size, distance, SURFACE_MARGIN, SURFACE_RAMP)
        + VisibleThrough(uv + float2(-spread.x, spread.y), size, distance, SURFACE_MARGIN, SURFACE_RAMP)
        + VisibleThrough(uv + float2(spread.x, spread.y), size, distance, SURFACE_MARGIN, SURFACE_RAMP)) * 0.25;
}

// As Visibility with a narrower soft edge, for effects that draw over many pixels many times. A margin lets
// something sink that far into what's in front of it, as a suit does into the person wearing it.
float VisibilityQuick(float3 positionWS, float margin = SURFACE_MARGIN, float ramp = SURFACE_RAMP)
{
    float2 uv;
    float distance;
    if (!FindInDepth(positionWS, uv, distance))
        return 1.0;
    return VisibleThrough(uv, DepthMapSize(), distance, margin, ramp);
}

#endif
