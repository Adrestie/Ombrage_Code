// ============================================================================
//  OceanCaustics.hlsl
//  Caustics from FFT normal-map divergence — two-cascade layers blended
//  via min, with per-channel chromatic dispersion.
//  Include AFTER OceanInput.hlsl or UnderwaterInput.hlsl so that
//  _OceanNormalMap*, _OceanPatchSize*, _OceanCascadeCount and
//  sampler_linear_repeat are already declared.
// ============================================================================

#ifndef OCEAN_CAUSTICS_INCLUDED
#define OCEAN_CAUSTICS_INCLUDED

// ── Uniforms (pushed as globals by OceanSystem.cs) ─────────────────────────

float _CausticsScale;
float _CausticsIntensity;
float _CausticsMaxDepth;
float _CausticsChromaSpread;
float4 _CausticsChromaOffsetR;  // .xy = direction du décalage canal rouge
float4 _CausticsChromaOffsetB;  // .xy = direction du décalage canal bleu

// ── Single-point caustic brightness from normal divergence ─────────────────
//
//  Samples the normal map at three neighbouring points and computes the
//  Laplacian of the height field (= divergence of the gradient).
//  Negative Laplacian ⇒ concave surface ⇒ light convergence ⇒ bright caustic.

float _CausticSample(float2 uv, float texelEps,
                     TEXTURE2D_PARAM(normalMap, normalSampler))
{
    float3 nC = SAMPLE_TEXTURE2D_LOD(normalMap, normalSampler, uv,                          0).rgb;
    float3 nX = SAMPLE_TEXTURE2D_LOD(normalMap, normalSampler, uv + float2(texelEps, 0),    0).rgb;
    float3 nZ = SAMPLE_TEXTURE2D_LOD(normalMap, normalSampler, uv + float2(0, texelEps),    0).rgb;

    float curvature = ((nX.x - nC.x) + (nZ.z - nC.z)) / texelEps;
    return smoothstep(0.0, 2.0, -curvature);
}

// ── Full caustic computation ───────────────────────────────────────────────
//
//  surfaceAWS        : absolute world position on the water surface directly
//                      above the shaded fragment (for UV / normal sampling).
//  depthBelowSurface : positive vertical distance of the fragment below the
//                      actual wave surface (NOT below _WaterLevel).
//
//  Returns an RGB additive caustic value (chromatic dispersion):
//  finalColor *= (1 + c).

float3 ComputeOceanCaustics(float3 surfaceAWS, float depthBelowSurface)
{
    if (_CausticsIntensity < 0.001 || depthBelowSurface <= 0.0)
        return 0.0;

    // ── Layer 1 — cascade 0 (longest wavelengths) ──────────────────────

    float2 uv1     = surfaceAWS.xz / _OceanPatchSize;
    float  eps1    = _CausticsScale / _OceanPatchSize;
    float  chroma1 = _CausticsChromaSpread / _OceanPatchSize;

    float3 c1;
    c1.r = _CausticSample(uv1 + _CausticsChromaOffsetR.xy * chroma1, eps1,
                           TEXTURE2D_ARGS(_OceanNormalMap, sampler_linear_repeat));
    c1.g = _CausticSample(uv1,                                        eps1,
                           TEXTURE2D_ARGS(_OceanNormalMap, sampler_linear_repeat));
    c1.b = _CausticSample(uv1 + _CausticsChromaOffsetB.xy * chroma1, eps1,
                           TEXTURE2D_ARGS(_OceanNormalMap, sampler_linear_repeat));

    float3 caustics = c1;

    // ── Layer 2 — cascade 1 (medium wavelengths, blended via min) ──────

    [branch] if (_OceanCascadeCount >= 2)
    {
        float2 uv2     = surfaceAWS.xz / _OceanPatchSize1;
        float  eps2    = _CausticsScale / _OceanPatchSize1;
        float  chroma2 = _CausticsChromaSpread / _OceanPatchSize1;

        float3 c2;
        c2.r = _CausticSample(uv2 + _CausticsChromaOffsetR.xy * chroma2, eps2,
                               TEXTURE2D_ARGS(_OceanNormalMap1, sampler_linear_repeat));
        c2.g = _CausticSample(uv2,                                        eps2,
                               TEXTURE2D_ARGS(_OceanNormalMap1, sampler_linear_repeat));
        c2.b = _CausticSample(uv2 + _CausticsChromaOffsetB.xy * chroma2, eps2,
                               TEXTURE2D_ARGS(_OceanNormalMap1, sampler_linear_repeat));

        caustics = min(c1, c2);
    }

    // ── Depth fade ─────────────────────────────────────────────────────

    float depthFade = 1.0 - smoothstep(0.0, _CausticsMaxDepth, depthBelowSurface);

    return caustics * depthFade * _CausticsIntensity;
}

#endif // OCEAN_CAUSTICS_INCLUDED
