// ============================================================================
//  OceanInput.hlsl
//  Shared uniforms, samplers, and helpers for the ocean surface shader.
//  Included by all passes (ForwardOnly, DepthForwardOnly).
//  Supports multi-cascade FFT sampling (up to 3 cascades).
// ============================================================================

#ifndef OCEAN_INPUT_INCLUDED
#define OCEAN_INPUT_INCLUDED

// ── Global textures (pushed by OceanManager.cs via Shader.SetGlobalTexture) ──

// Cascade 0 (base — longest wavelengths)
TEXTURE2D(_OceanDisplacementY);
TEXTURE2D(_OceanDisplacementX);
TEXTURE2D(_OceanDisplacementZ);
TEXTURE2D(_OceanNormalMap);
TEXTURE2D(_OceanFoamMap);

// Cascade 1 (medium wavelengths)
TEXTURE2D(_OceanDisplacementY1);
TEXTURE2D(_OceanDisplacementX1);
TEXTURE2D(_OceanDisplacementZ1);
TEXTURE2D(_OceanNormalMap1);
TEXTURE2D(_OceanFoamMap1);

// Cascade 2 (short wavelengths — detail)
TEXTURE2D(_OceanDisplacementY2);
TEXTURE2D(_OceanDisplacementX2);
TEXTURE2D(_OceanDisplacementZ2);
TEXTURE2D(_OceanNormalMap2);
TEXTURE2D(_OceanFoamMap2);

// Foam detail textures (artist-authored, assigned on material)
TEXTURE2D(_FoamTexHigh);
TEXTURE2D(_FoamTexLow);

// Wake interaction RT (pushed by OceanWakeManager)
TEXTURE2D(_OceanWakeMap);

// Planar reflection RT (pushed by OceanPlanarReflection)
TEXTURE2D(_OceanPlanarReflectionTex);

SAMPLER(sampler_linear_repeat);
SAMPLER(sampler_linear_clamp);

// ── Global floats (pushed by OceanManager.cs) ───────────────────────────────

float _OceanPatchSize;
float _OceanPatchSize1;
float _OceanPatchSize2;
int   _OceanCascadeCount;

// Wake globals (pushed by OceanWakeManager)
float  _OceanWakeCoverageSize;
float  _OceanWakeDisplacementScale;
float4 _OceanWakeCenter;
float  _OceanWakeTexelSize;

// Shore intersection map (pushed by OceanSystem)
TEXTURE2D(_OceanShoreMap);
float  _OceanShoreMapSize;
float4 _OceanShoreMapCenter;
float  _WaveShoreAttenuationDist;
float  _WaveShoreMinAmplitude;

// ── Material properties ─────────────────────────────────────────────────────

float4 _ShallowColor;
float4 _DeepColor;
float4 _SSSColor;
float4 _FoamColor;
float  _FresnelPower;
float  _SSSIntensity;
float  _SSSPower;
float  _SSSSpread;
float  _SpecularIntensity;
float  _OceanRoughness;
float  _HeightScale;
float  _AmbientStrength;
float  _WrapDiffuse;
float  _FoamTexScale;
float  _FoamTexBlend;
float  _DepthAbsorption;
float  _DepthMaxDistance;
float4 _AbsorptionColor;
float  _RefractionStrength;
float  _RefractionDepthFade;
float  _ReflectionIntensity;
float4 _HorizonColor;
float4 _ZenithColor;
float  _DebugFoam;
float  _DebugWake;
float  _UsePlanarReflection;
float  _PlanarReflectionBlend;

// ── Helpers ─────────────────────────────────────────────────────────────────

float2 OceanUV(float3 worldPos)
{
    return worldPos.xz / _OceanPatchSize;
}

// ── Multi-cascade displacement (LOD — used in domain shader) ────────────────

float3 SampleOceanDisplacement(float3 posAWS)
{
    float2 uv0 = posAWS.xz / _OceanPatchSize;
    float dy = SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY, sampler_linear_repeat, uv0, 0).r;
    float dx = SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX, sampler_linear_repeat, uv0, 0).r;
    float dz = SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ, sampler_linear_repeat, uv0, 0).r;
    float3 disp = float3(-dx, dy * _HeightScale, -dz);

    [branch] if (_OceanCascadeCount >= 2)
    {
        float2 uv1 = posAWS.xz / _OceanPatchSize1;
        disp.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY1, sampler_linear_repeat, uv1, 0).r * _HeightScale;
        disp.x -= SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX1, sampler_linear_repeat, uv1, 0).r;
        disp.z -= SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ1, sampler_linear_repeat, uv1, 0).r;
    }

    [branch] if (_OceanCascadeCount >= 3)
    {
        float2 uv2 = posAWS.xz / _OceanPatchSize2;
        disp.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY2, sampler_linear_repeat, uv2, 0).r * _HeightScale;
        disp.x -= SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX2, sampler_linear_repeat, uv2, 0).r;
        disp.z -= SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ2, sampler_linear_repeat, uv2, 0).r;
    }

    return disp;
}

// ── Multi-cascade normal blending (gradient reconstruction) ─────────────────

float3 SampleOceanNormal(float3 posAWS)
{
    float2 uv0 = posAWS.xz / _OceanPatchSize;
    float3 n0 = SAMPLE_TEXTURE2D(_OceanNormalMap, sampler_linear_repeat, uv0).rgb;
    float dhdx = -n0.x / max(n0.y, 0.001);
    float dhdz = -n0.z / max(n0.y, 0.001);

    [branch] if (_OceanCascadeCount >= 2)
    {
        float2 uv1 = posAWS.xz / _OceanPatchSize1;
        float3 n1 = SAMPLE_TEXTURE2D(_OceanNormalMap1, sampler_linear_repeat, uv1).rgb;
        dhdx += -n1.x / max(n1.y, 0.001);
        dhdz += -n1.z / max(n1.y, 0.001);
    }

    [branch] if (_OceanCascadeCount >= 3)
    {
        float2 uv2 = posAWS.xz / _OceanPatchSize2;
        float3 n2 = SAMPLE_TEXTURE2D(_OceanNormalMap2, sampler_linear_repeat, uv2).rgb;
        dhdx += -n2.x / max(n2.y, 0.001);
        dhdz += -n2.z / max(n2.y, 0.001);
    }

    return normalize(float3(-dhdx, 1.0, -dhdz));
}

float3 SampleOceanNormalLOD(float3 posAWS)
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

// ── Multi-cascade foam ──────────────────────────────────────────────────────

float SampleOceanFoam(float3 posAWS)
{
    float2 uv0 = posAWS.xz / _OceanPatchSize;
    float foam = SAMPLE_TEXTURE2D(_OceanFoamMap, sampler_linear_repeat, uv0).r;

    [branch] if (_OceanCascadeCount >= 2)
    {
        float2 uv1 = posAWS.xz / _OceanPatchSize1;
        foam = max(foam, SAMPLE_TEXTURE2D(_OceanFoamMap1, sampler_linear_repeat, uv1).r);
    }

    [branch] if (_OceanCascadeCount >= 3)
    {
        float2 uv2 = posAWS.xz / _OceanPatchSize2;
        foam = max(foam, SAMPLE_TEXTURE2D(_OceanFoamMap2, sampler_linear_repeat, uv2).r);
    }

    return foam;
}

float SampleOceanHeight(float3 posAWS)
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

    return h * _HeightScale;
}

// ── Choppiness-corrected ocean height (fixed-point iteration) ──────────────
// Inverts horizontal displacement to find the true wave height at a world XZ.

float SampleOceanHeightCorrected(float3 posAWS)
{
    float2 gridXZ = posAWS.xz;

    [unroll]
    for (int iter = 0; iter < 6; iter++)
    {
        float2 dxz = 0;
        float2 uv0 = gridXZ / _OceanPatchSize;
        dxz.x += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX, sampler_linear_repeat, uv0, 0).r;
        dxz.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ, sampler_linear_repeat, uv0, 0).r;

        [branch] if (_OceanCascadeCount >= 2)
        {
            float2 uv1 = gridXZ / _OceanPatchSize1;
            dxz.x += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX1, sampler_linear_repeat, uv1, 0).r;
            dxz.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ1, sampler_linear_repeat, uv1, 0).r;
        }

        [branch] if (_OceanCascadeCount >= 3)
        {
            float2 uv2 = gridXZ / _OceanPatchSize2;
            dxz.x += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementX2, sampler_linear_repeat, uv2, 0).r;
            dxz.y += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementZ2, sampler_linear_repeat, uv2, 0).r;
        }

        gridXZ = posAWS.xz + dxz;
    }

    float2 fuv0 = gridXZ / _OceanPatchSize;
    float h = SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY, sampler_linear_repeat, fuv0, 0).r;

    [branch] if (_OceanCascadeCount >= 2)
    {
        float2 fuv1 = gridXZ / _OceanPatchSize1;
        h += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY1, sampler_linear_repeat, fuv1, 0).r;
    }

    [branch] if (_OceanCascadeCount >= 3)
    {
        float2 fuv2 = gridXZ / _OceanPatchSize2;
        h += SAMPLE_TEXTURE2D_LOD(_OceanDisplacementY2, sampler_linear_repeat, fuv2, 0).r;
    }

    return h * _HeightScale;
}

// ── Shore intersection map sampling ─────────────────────────────────────────
// Returns float4(waterY, foam, groundY, signedDist) from the GPU intersection map.
// signedDist = waterY - groundY: positive = water above ground.

float4 SampleOceanShoreMap(float3 posAWS)
{
    float2 uv = (posAWS.xz - _OceanShoreMapCenter.xy) / _OceanShoreMapSize + 0.5;
    return SAMPLE_TEXTURE2D_LOD(_OceanShoreMap, sampler_linear_clamp, uv, 0);
}

// ── Wake interaction ────────────────────────────────────────────────────────

float2 WakeUV(float3 posAWS)
{
    return frac(posAWS.xz / _OceanWakeCoverageSize);
}

float SampleWakeDisplacement(float3 posAWS)
{
    float2 uv = WakeUV(posAWS);
    return SAMPLE_TEXTURE2D_LOD(_OceanWakeMap, sampler_linear_repeat, uv, 0).r;
}

float SampleWakeFoam(float3 posAWS)
{
    float2 uv = WakeUV(posAWS);
    float wake = SAMPLE_TEXTURE2D(_OceanWakeMap, sampler_linear_repeat, uv).r;

    float2 texelSize = float2(_OceanWakeTexelSize, _OceanWakeTexelSize);
    float wakeL = SAMPLE_TEXTURE2D(_OceanWakeMap, sampler_linear_repeat, uv - float2(texelSize.x, 0)).r;
    float wakeR = SAMPLE_TEXTURE2D(_OceanWakeMap, sampler_linear_repeat, uv + float2(texelSize.x, 0)).r;
    float wakeD = SAMPLE_TEXTURE2D(_OceanWakeMap, sampler_linear_repeat, uv - float2(0, texelSize.y)).r;
    float wakeU = SAMPLE_TEXTURE2D(_OceanWakeMap, sampler_linear_repeat, uv + float2(0, texelSize.y)).r;

    float gradient = length(float2(wakeR - wakeL, wakeU - wakeD));
    return gradient * 3.0 + wake * 0.3;
}

#endif // OCEAN_INPUT_INCLUDED
