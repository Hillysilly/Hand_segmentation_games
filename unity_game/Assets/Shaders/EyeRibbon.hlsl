// A ribbon that always turns to face the eye, so a thin beam has no geometry to see. The mesh has x = -1..1
// across and z = 0..1 along; the transform's x scale is the half width and its z scale the length.
#ifndef REPULSOR_EYE_RIBBON
#define REPULSOR_EYE_RIBBON

float3 EyeRibbonPosition(float3 positionOS, out float alongMeters)
{
    float3 widthAxis = float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x);
    float3 lengthAxis = float3(UNITY_MATRIX_M[0].z, UNITY_MATRIX_M[1].z, UNITY_MATRIX_M[2].z);
    float metersLong = length(lengthAxis);
    float3 direction = lengthAxis / max(metersLong, 0.0001);

    float z = positionOS.z;
    float3 axisWS = TransformObjectToWorld(float3(0.0, 0.0, z));

    // Looking straight down the ribbon leaves nothing to cross with, so fall back to the transform's own side.
    float3 side = cross(direction, GetCameraPositionWS() - axisWS);
    float sideLength = length(side);
    side = sideLength > 0.0001 ? side / sideLength : normalize(widthAxis);

    alongMeters = z * metersLong;
    return axisWS + side * positionOS.x * length(widthAxis);
}

#endif
