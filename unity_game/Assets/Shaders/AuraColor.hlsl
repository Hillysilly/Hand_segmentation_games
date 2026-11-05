// The aura's colour, chosen on the colour wheel and set by AuraView. Every style lights itself through this,
// so fire of any colour keeps a dark edge and a pale, hot centre.
#ifndef REPULSOR_AURA_COLOR
#define REPULSOR_AURA_COLOR

float4 _AuraTint;

float3 AuraFire(float heat)
{
    float3 tint = _AuraTint.rgb;
    float3 color = lerp(pow(abs(tint), 1.8), tint, smoothstep(0.0, 0.45, heat));

    // A lifted version of the colour, not the colour plus white, so orange runs to gold and not to pink.
    float3 hot = lerp(pow(abs(tint), 0.35), float3(1.0, 1.0, 1.0), 0.2);
    return lerp(color, hot, smoothstep(0.45, 0.95, heat));
}

#endif
