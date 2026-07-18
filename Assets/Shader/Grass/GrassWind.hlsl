// GrassWind.hlsl
// ---------------------------------------------------------------------------------
// SHARED wind field — the single source of motion for the whole grass system.
//
//   - L0 blades (GrassBRGVertex.hlsl, wired in Phase 2): bend amount  = signal
//   - L2 tinted terrain (TerrainLitCustomData.hlsl):     normal tilt + luminance bands = signal
//
// Both consumers evaluate the SAME GrassGustSignal() at their world XZ, so the gust
// you see rolling across the terrain from altitude is exactly the one that bends the
// blades when you descend (motion-match → seamless L0<->L2 hand-off in Phase 4).
//
// Globals are pushed by GrassWindController (Shader.SetGlobalX). Time is passed by the
// caller, NOT read here: the terrain passes _GrassWindTime; the blades will pass
// timeParameters.x (and _LastTimeParameters.x in the MotionVectors pass) so object
// motion vectors stay correct. Same wall-clock value at render => gusts align.
// ---------------------------------------------------------------------------------

#ifndef GRASS_WIND_INCLUDED
#define GRASS_WIND_INCLUDED

float4 _GrassWindDir;            // xy = world XZ wind direction (normalized in-shader)
float  _GrassWindMain;           // overall wind strength (0 = no wind => no waves)
float  _GrassWindTurbulence;     // small-scale chaotic strength
float  _GrassWindPulseMagnitude; // gust envelope amplitude
float  _GrassWindPulseFrequency; // gust speed
float  _GrassWindTime;           // seconds — terrain (fragment) time source
float  _GrassWindDirectionality; // 0 = organic/isotropic blobs, 1 = directional fronts (across wind)
float  _GrassWindEvolve;         // 0 = rigid directional travel (pan), 1 = morph in place (boil)

// Phase 4 — SHARED L0<->L2 crossfade band (pushed by GrassBladesModule). Over [Start, End] the L0
// blades melt out (height -> 0) while the L2 tint rises in, complementary, so the hand-off is
// seamless. End = maxBladeDistance. If End <= Start (no blades / module off), the tint falls back to
// its own _GrassTintDistance* params and the blades don't melt.
float  _GrassCrossfadeStart;
float  _GrassCrossfadeEnd;

// FAKE far shadow (pushed by GrassBladesModule). Real cast shadows only reach _GrassShadowDist; beyond
// that this cheap albedo darkening fakes the grass canopy self-shadowing, ramping IN over the same
// band where the real shadows ramp OUT (complementary -> no visible shadow-disc edge). Read by BOTH
// the blades (GrassBRGSurface) and the L2 tint (TerrainLitCustomData).
float  _GrassShadowDist;
float  _GrassShadowFadeBand;
float  _GrassFakeShadowStrength; // 0 = off

// World-space downwind direction (horizontal) — the lean axis for blades / normal tilt.
float3 GrassWindDirWS()
{
    float2 d = normalize(_GrassWindDir.xy + float2(1e-4, 0.0));
    return float3(d.x, 0.0, d.y);
}

// --- Cheap 2D value noise (organic gust fronts, NOT 1D sine lines) ---
float GW_hash(float2 p)
{
    p = 50.0 * frac(p * 0.3183099 + float2(0.71, 0.113));
    return frac(p.x * p.y * (p.x + p.y));
}
float GW_vnoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);          // smoothstep interp
    float a = GW_hash(i + float2(0.0, 0.0));
    float b = GW_hash(i + float2(1.0, 0.0));
    float c = GW_hash(i + float2(0.0, 1.0));
    float d = GW_hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);   // [0,1]
}
float GW_fbm(float2 p)
{
    float v = 0.0, amp = 0.5;
    // Rotate each octave (~37 deg) so features never align to the world axes (anti-tiling).
    float2x2 rot = float2x2(0.80, -0.60, 0.60, 0.80);
    [unroll] for (int i = 0; i < 4; i++) { v += amp * GW_vnoise(p); p = mul(rot, p) * 2.03 + 1.7; amp *= 0.5; }
    return v;                                     // ~[0, 0.94]
}

// --- 3D value noise: time as the 3rd axis, so the field EVOLVES/boils over time instead of
// just translating (kills the "texture panning on the ground" look). ---
float GW_hash3(float3 p)
{
    // iq's 3D value-noise hash — well distributed on the integer lattice (same family as the 2D
    // GW_hash above). The previous Dave-Hoskins variant clustered on integer inputs, which showed
    // as hard cell boundaries + temporal "cuts" when crossing the time (z) cells.
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float GW_vnoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = GW_hash3(i + float3(0,0,0)), n100 = GW_hash3(i + float3(1,0,0));
    float n010 = GW_hash3(i + float3(0,1,0)), n110 = GW_hash3(i + float3(1,1,0));
    float n001 = GW_hash3(i + float3(0,0,1)), n101 = GW_hash3(i + float3(1,0,1));
    float n011 = GW_hash3(i + float3(0,1,1)), n111 = GW_hash3(i + float3(1,1,1));
    float nx00 = lerp(n000, n100, u.x), nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x), nx11 = lerp(n011, n111, u.x);
    return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z); // [0,1]
}
float GW_fbm3(float3 p)
{
    float v = 0.0, amp = 0.5;
    float2x2 rot = float2x2(0.80, -0.60, 0.60, 0.80);
    [unroll] for (int i = 0; i < 3; i++)
    {
        v += amp * GW_vnoise3(p);
        p.xy = mul(rot, p.xy) * 2.03 + 1.7;
        p.z += 0.7;                 // decorrelate octaves in time, morph rate stays slow
        amp *= 0.5;
    }
    return v;                       // ~[0, 0.875]
}

// Rolling gust signal at a world XZ position and time. Output ~[-1.8, 1.8] * windMain.
// 2D flow noise sampled in a WIND-ALIGNED frame (along/across the wind): the pattern rotates
// RIGIDLY with the wind direction (gust fronts perpendicular to the wind), and the temporal
// advection is a SCALAR phase along the wind axis — NOT a dir*time vector offset. So rotating
// the WindZone rotates the whole visual by the same angle, continuously, with no resample/jump.
// (Rigid rotation is about the world origin, so terrain very far from it swirls more per degree;
//  the world anchor is what keeps the field stable — no sliding — as the camera moves.)
// Low spatial frequency so deferred+TAA stays stable on the static terrain (zero motion vectors).
float GrassGustSignal(float2 worldXZ, float time)
{
    float2 dir   = normalize(_GrassWindDir.xy + float2(1e-4, 0.0));
    float2 perp  = float2(-dir.y, dir.x);
    float  along = dot(worldXZ, dir);    // distance along the wind
    float  across= dot(worldXZ, perp);   // distance across the wind
    float  speed = 0.5 + _GrassWindPulseFrequency;

    // Two knobs (module): Directionality (0 = organic/isotropic blobs, 1 = fronts elongated ACROSS
    // the wind) and Evolve (0 = rigid directional travel = risk of "panning texture", 1 = morph in
    // place = risk of "boiling swamp"). The middle = gust fronts that roll downwind while slowly
    // deforming.
    float aniso = lerp(1.0, 4.0, saturate(_GrassWindDirectionality));
    float ev    = saturate(_GrassWindEvolve);
    float drift = time * speed * lerp(0.6, 0.18, ev);    // directional travel
    float evo   = time * speed * lerp(0.04, 0.55, ev);   // in-place morph

    float fa = 0.03;            // along-wind frequency
    float fc = fa / aniso;      // across-wind frequency (lower = wider fronts = more directional)

    // Broad gust fronts, travelling downwind, morphing subtly. Warp scales with Evolve (clean &
    // directional when low, organic when high).
    float2 q1   = float2(along * fa - drift, across * fc);
    float2 warp = float2(GW_vnoise(q1 * 1.3 + 5.2), GW_vnoise(q1 * 1.3 + 19.3)) - 0.5;
    q1 += warp * lerp(0.2, 0.9, ev);
    float  broad = GW_fbm3(float3(q1, evo * 0.5));

    // Finer along-wind streaks.
    float  fine = GW_vnoise3(float3(along * (fa * 2.6) - drift * 1.6, across * (fc * 2.6), evo));

    float g = (broad - 0.47) * 2.0 + (fine - 0.5) * 0.4;   // centered

    // Gust envelope: fronts swell as they pass.
    g *= 1.0 + _GrassWindPulseMagnitude * broad;

    // Flutter: fast, small temporal shimmer (grass trembling). Low spatial frequency for TAA.
    if (_GrassWindTurbulence > 0.0)
        g += _GrassWindTurbulence * sin(time * (2.0 + speed * 2.0) + along * 0.08 + across * 0.05) * 0.15;

    return clamp(g, -1.8, 1.8) * _GrassWindMain;
}

// Fake canopy shadow multiplier (albedo) for grass beyond the real shadow sphere. Ramps IN as the
// real cast shadows ramp OUT across [_GrassShadowDist - band, _GrassShadowDist], modulated by static
// low-frequency clump patches so the field reads as self-shadowed grass, not flat-lit. Returns a
// multiplier in [1 - strength, 1]. camDist = distance to camera.
float GrassFakeShadowMul(float2 worldXZ, float camDist)
{
    if (_GrassFakeShadowStrength <= 0.0) return 1.0;
    float rampIn = (_GrassShadowFadeBand > 0.0)
        ? smoothstep(_GrassShadowDist - _GrassShadowFadeBand, _GrassShadowDist, camDist)
        : step(_GrassShadowDist, camDist);
    float patch  = GW_fbm(worldXZ * 0.08);                 // clump-scale shadow blobs (static)
    float darken = _GrassFakeShadowStrength * rampIn * (0.35 + 0.65 * patch);
    return saturate(1.0 - darken);
}

#endif // GRASS_WIND_INCLUDED
