#ifndef UNDERWATER_INPUT_INCLUDED
#define UNDERWATER_INPUT_INCLUDED

// ── Uniforms ───────────────────────────────────────────────────────────────

float  _WaterLevel;

// Fog
float  _UnderwaterFogDensity;
float  _UnderwaterFogStartDist;
float4 _UnderwaterFogColor;
float  _UnderwaterDepthAbsorption;
float  _UnderwaterDepthDarkeningMin;

// God rays
float  _GodRayIntensity;
float  _GodRayFadeInDepth;
float4 _GodRayColor;
float  _BeamThresholdLo;
float  _BeamThresholdHi;
float  _BeamScale;
float  _BeamSunFollow;
float  _BeamDepthFade;
float  _BeamExtinction;
float  _GodRayMaxDist;
float  _GodRaySpeed;
float3 _SunDirWS;
int    _GodRaySteps;
int    _UnderwaterDebug;

// Surface from below
float  _SurfaceDistortion;
float  _SnellWindowDepth;

// Screen distortion
float  _ScreenDistStrength;
float  _ScreenDistSpeed;
float  _ScreenDistScale;

// ── Ocean FFT data (globals pushed by OceanManager) ──────────────────────

TEXTURE2D(_OceanDisplacementY);
TEXTURE2D(_OceanDisplacementY1);
TEXTURE2D(_OceanDisplacementY2);
TEXTURE2D(_OceanNormalMap);
TEXTURE2D(_OceanNormalMap1);
TEXTURE2D(_OceanNormalMap2);
SAMPLER(sampler_linear_repeat);

float  _OceanPatchSize;
float  _OceanPatchSize1;
float  _OceanPatchSize2;
int    _OceanCascadeCount;
float  _UnderwaterHeightScale;

// ── Ocean sampling helpers (subset of OceanInput.hlsl) ───────────────────

float3 _UW_SampleOceanNormal(float3 posAWS)
{
    float2 uv0 = posAWS.xz / _OceanPatchSize;
    float3 n0 = SAMPLE_TEXTURE2D_LOD(_OceanNormalMap, sampler_linear_repeat, uv0, 0).rgb;
    float dhdx = -n0.x / max(n0.y, 0.001);
    float dhdz = -n0.z / max(n0.y, 0.001);

    [branch] if (_OceanCascadeCount >= 2)
    {
        float2 uv1 = posAWS.xz / _OceanPatchSize1;
        float3 n1 = SAMPLE_TEXTURE2D_LOD(_OceanNormalMap1, sampler_linear_repeat, uv1, 0).rgb;
        dhdx += -n1.x / max(n1.y, 0.001);
        dhdz += -n1.z / max(n1.y, 0.001);
    }

    [branch] if (_OceanCascadeCount >= 3)
    {
        float2 uv2 = posAWS.xz / _OceanPatchSize2;
        float3 n2 = SAMPLE_TEXTURE2D_LOD(_OceanNormalMap2, sampler_linear_repeat, uv2, 0).rgb;
        dhdx += -n2.x / max(n2.y, 0.001);
        dhdz += -n2.z / max(n2.y, 0.001);
    }

    return normalize(float3(-dhdx, 1.0, -dhdz));
}

float _UW_SampleOceanHeight(float3 posAWS)
{
    float2 uv0 = posAWS.xz / _OceanPatchSize;
    float h = SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY, sampler_linear_repeat, uv0, 0).r;

    [branch] if (_OceanCascadeCount >= 2)
    {
        float2 uv1 = posAWS.xz / _OceanPatchSize1;
        h += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY1, sampler_linear_repeat, uv1, 0).r;
    }

    [branch] if (_OceanCascadeCount >= 3)
    {
        float2 uv2 = posAWS.xz / _OceanPatchSize2;
        h += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY2, sampler_linear_repeat, uv2, 0).r;
    }

    return h * _UnderwaterHeightScale;
}

// ── Noise (used by god rays only now) ────────────────────────────────────

float _grHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float _grNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = _grHash(i);
    float b = _grHash(i + float2(1, 0));
    float c = _grHash(i + float2(0, 1));
    float d = _grHash(i + float2(1, 1));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// ── Fog (Subnautica-style decoupled attenuation) ───────────────────────────

struct UnderwaterFogResult
{
    float3 color;
    float  fogAmount;
};

UnderwaterFogResult ComputeUnderwaterFog(float3 sceneColor, float dist,
                                          float fragDepthBelow, float camDepthBelow)
{
    UnderwaterFogResult result;

    float fogDist = max(0.0, dist - _UnderwaterFogStartDist);
    float hFog = 1.0 - exp(-_UnderwaterFogDensity * fogDist);

    float avgDepth = (camDepthBelow + fragDepthBelow) * 0.5;
    float depthDarkening = lerp(_UnderwaterDepthDarkeningMin, 1.0,
                                exp(-_UnderwaterDepthAbsorption * avgDepth));

    float3 fogColorAtDepth = _UnderwaterFogColor.rgb * depthDarkening;

    result.color = lerp(sceneColor, fogColorAtDepth, hFog);
    result.fogAmount = hFog;
    return result;
}

// ── Surface from below (real FFT data + Snell's window) ──────────────────

float3 ComputeSurfaceFromBelow(float3 rayDir, float3 camPosAWS, float camDepthBelow,
                                float2 screenUV)
{
    float cosTheta = max(rayDir.y, 0.0);

    // Intersect ray with flat water plane (first approximation)
    float tSurf = camDepthBelow / max(cosTheta, 0.001);
    tSurf = min(tSurf, 500.0);
    float3 surfHitFlat = camPosAWS + rayDir * tSurf;

    // Sample real wave height at intersection and refine
    float waveH = _UW_SampleOceanHeight(surfHitFlat);
    float actualSurfY = _WaterLevel + waveH;
    float correctedDepth = actualSurfY - camPosAWS.y;
    float tCorrected = correctedDepth / max(cosTheta, 0.001);
    tCorrected = min(tCorrected, 500.0);
    float3 surfHit = camPosAWS + rayDir * tCorrected;

    // Sample the real FFT surface normal at the hit point
    float3 surfNormal = _UW_SampleOceanNormal(surfHit);
    // Flip normal to face downward (we're looking from below)
    surfNormal.y = abs(surfNormal.y);

    // Snell's window — widens near the surface, shrinks with depth
    float cosAngle = dot(rayDir, surfNormal);
    float depthFactor = saturate(camDepthBelow / max(_SnellWindowDepth, 0.01));
    float criticalCos = lerp(0.05, 0.661, depthFactor);
    float snellMask = smoothstep(criticalCos - 0.03, criticalCos + 0.08, cosAngle);

    // Refraction distortion from real wave normals
    float2 normalOffset = surfNormal.xz;
    float depthAtten = exp(-camDepthBelow * 0.05);
    float2 distortedUV = screenUV + normalOffset * _SurfaceDistortion * depthAtten;
    distortedUV = clamp(distortedUV, 0.001, 0.999);

    // Sample actual scene through the surface
    float3 aboveColor = CustomPassSampleCameraColor(distortedUV, 0);
    aboveColor = max(aboveColor, 0.0);

    // Water column absorption: light travels camDepthBelow/cosAngle through water
    float waterPath = camDepthBelow / max(cosAngle, 0.01);
    float waterAbsorption = 1.0 - exp(-_UnderwaterFogDensity * 1.5 * waterPath);
    waterAbsorption = max(waterAbsorption, 0.15);
    aboveColor = lerp(aboveColor, _UnderwaterFogColor.rgb, waterAbsorption);

    // Total internal reflection: dark underwater tint
    float3 tirColor = _UnderwaterFogColor.rgb * 0.15;

    // Bright ring at Snell's window edge
    float ring = smoothstep(criticalCos - 0.01, criticalCos + 0.01, cosAngle)
               * smoothstep(criticalCos + 0.1, criticalCos + 0.02, cosAngle);

    float3 result = lerp(tirColor, aboveColor, snellMask);
    result += ring * float3(0.08, 0.12, 0.10);

    return result;
}

// ── God ray helpers ────────────────────────────────────────────────────────

float _InterleavedGradientNoise(float2 pixelCoord)
{
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(pixelCoord, magic.xy)));
}

// God ray beam from FFT surface curvature (3 texture fetches).
// Uses a wide smoothstep so beams fade in/out gradually as waves pass.
float _BeamHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float _OceanBeamPattern(float3 surfaceHitAWS)
{
    float eps = 1.0 / max(_BeamScale, 0.01);

    float3 nC = _UW_SampleOceanNormal(surfaceHitAWS);
    float3 nX = _UW_SampleOceanNormal(surfaceHitAWS + float3(eps, 0, 0));
    float3 nZ = _UW_SampleOceanNormal(surfaceHitAWS + float3(0, 0, eps));

    // Divergence ≈ curvature. Negative = concave = light focuses = beam.
    float divN = (nX.x - nC.x + nZ.z - nC.z) / eps;

    float lo = _BeamThresholdLo * 0.3;
    float hi = _BeamThresholdHi * 3.0;
    float beam = smoothstep(lo, hi, -divN);

    float2 cell = floor(surfaceHitAWS.xz * _BeamScale * 0.5);
    float sizeVariation = lerp(0.4, 1.0, _BeamHash(cell));
    return beam * sizeVariation;
}

// ── God rays (surface-projected volumetric march) ──────────────────────────

float3 ComputeGodRays(float2 screenUV, float3 posRWS, float3 camPosAWS_absolute,
                       float linearDist, bool isSky)
{
    float camDepthBelow = _WaterLevel - camPosAWS_absolute.y;
    if (camDepthBelow <= 0.0) return float3(0, 0, 0);

    float l = length(posRWS);
    float3 rayDir = (l > 0.0001) ? posRWS / l : float3(0, 1, 0);
    float maxDist = isSky ? _GodRayMaxDist : min(linearDist, _GodRayMaxDist);
    int   steps   = clamp(_GodRaySteps, 4, 64);
    float stepSize = maxDist / (float)steps;

    float3 sunDir = float3(-_SunDirWS.x, -abs(_SunDirWS.y), -_SunDirWS.z);
    float sunLen = length(sunDir);
    sunDir = (sunLen > 0.0001) ? sunDir / sunLen : float3(0, -1, 0);

    float3 beamDir = normalize(lerp(float3(0, -1, 0), sunDir, _BeamSunFollow));
    beamDir.y = min(beamDir.y, -0.1);
    beamDir = normalize(beamDir);

    float jitter = _InterleavedGradientNoise(screenUV * _ScreenParams.xy);

    float accum = 0.0;

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float t = stepSize * ((float)i + 0.5 + jitter);
        float3 sampleAWS = camPosAWS_absolute + rayDir * t;

        float depthBelow = _WaterLevel - sampleAWS.y;
        if (depthBelow < 0.0) break;

        float tUp = depthBelow / (-beamDir.y);
        float3 surfaceHit = sampleAWS - beamDir * tUp;

        float beam = _OceanBeamPattern(surfaceHit);

        float surfaceProximity = exp(-depthBelow * _BeamDepthFade);
        float distanceAtten = exp(-t * _BeamExtinction);

        accum += beam * surfaceProximity * distanceAtten * stepSize;
    }

    float depthFadeIn = smoothstep(0.0, _GodRayFadeInDepth, camDepthBelow);
    float horizonFade = smoothstep(0.1, 0.4, -beamDir.y);
    return accum * _GodRayColor.rgb * _GodRayIntensity * depthFadeIn * horizonFade;
}

#endif // UNDERWATER_INPUT_INCLUDED
