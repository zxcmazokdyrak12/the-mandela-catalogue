#ifndef MANDELA_PSX_COMMON
#define MANDELA_PSX_COMMON
// PS1-style vertex snapping: clip-space positions are snapped to a coarse virtual pixel grid,
// so geometry wobbles and 'swims' as the camera moves. Driven by the global _PSXSnap set from PSXMode
// (virtual vertical resolution of the grid, 0 = off).
float _PSXSnap;

float4 PSXSnap(float4 cs)
{
    if (_PSXSnap <= 0.0 || cs.w <= 0.0001) return cs;
    float2 res = float2(_PSXSnap * (_ScreenParams.x / _ScreenParams.y), _PSXSnap) * 0.5;
    float2 ndc = cs.xy / cs.w;
    ndc = floor(ndc * res + 0.5) / res;
    cs.xy = ndc * cs.w;
    return cs;
}
#endif
