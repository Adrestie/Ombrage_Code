// TerrainLitCustom_Splatmap.hlsl
// Fork of TerrainLit_Splatmap.hlsl with:
//   - POM injection per layer (Phase 3)
//   - Blended height output for tessellation displacement (Phase 5)
//   - viewDirTS passed through for POM (Phase 4)
//   - SAND MODE: per-layer sand weight accumulation + procedural ripple normals (Phase 7)
//
// FIXES v2:
//   - Forced bilinear filtering on control maps (fixes blocky layer transitions)
//   - Clean displacement sampling with correct UV space
//   - POM distance fade integration
// ---------------------------------------------------------------------------------

#if defined(_NORMALMAP) && defined(SURFACE_GRADIENT)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/NormalSurfaceGradient.hlsl"
#endif

TEXTURE2D(_Control0);

#define DECLARE_TERRAIN_LAYER_TEXS(n)   \
    TEXTURE2D(_Splat##n);               \
    TEXTURE2D(_Normal##n);              \
    TEXTURE2D(_Mask##n)

DECLARE_TERRAIN_LAYER_TEXS(0);
DECLARE_TERRAIN_LAYER_TEXS(1);
DECLARE_TERRAIN_LAYER_TEXS(2);
DECLARE_TERRAIN_LAYER_TEXS(3);
#ifdef _TERRAIN_8_LAYERS
    DECLARE_TERRAIN_LAYER_TEXS(4);
    DECLARE_TERRAIN_LAYER_TEXS(5);
    DECLARE_TERRAIN_LAYER_TEXS(6);
    DECLARE_TERRAIN_LAYER_TEXS(7);
    TEXTURE2D(_Control1);
#endif

#undef DECLARE_TERRAIN_LAYER_TEXS

SAMPLER(sampler_Splat0);

// ---------------------------------------------------------------------------------
// FIX: Force bilinear filtering + clamp on control maps
// ---------------------------------------------------------------------------------
SAMPLER(sampler_linear_clamp);
SAMPLER(sampler_Control0);

float _TerrainBasemapDistance;

// ---------------------------------------------------------------------------------
// Custom uniforms — OUTSIDE the UnityTerrain CBUFFER
// ---------------------------------------------------------------------------------

// Per-layer POM parameters
#define DECLARE_CUSTOM_LAYER_PROPS(n)   \
    float _EnablePOMLayer##n;           \
    float _HeightScale##n;              \
    float _POMMinSteps##n;              \
    float _POMMaxSteps##n;              \
    float _EnableDisplacementLayer##n;  \
    float _DisplacementScale##n;        \
    float _EnableSandMode##n;           \
    float _EnableGrassTintLayer##n;

DECLARE_CUSTOM_LAYER_PROPS(0)
DECLARE_CUSTOM_LAYER_PROPS(1)
DECLARE_CUSTOM_LAYER_PROPS(2)
DECLARE_CUSTOM_LAYER_PROPS(3)
#ifdef _TERRAIN_8_LAYERS
    DECLARE_CUSTOM_LAYER_PROPS(4)
    DECLARE_CUSTOM_LAYER_PROPS(5)
    DECLARE_CUSTOM_LAYER_PROPS(6)
    DECLARE_CUSTOM_LAYER_PROPS(7)
#endif
#undef DECLARE_CUSTOM_LAYER_PROPS

// Global custom parameters
float _POMDistanceFade;
float _TessellationFactor;
float _TessellationBackFaceCullEpsilon;
float _TessellationMaxDisplacement;
float _TessellationDistanceFade;
float _DeformationStrength;
float _BufferWorldSize;   // world-space size of one toroidal tile (meters) — identical for ALL terrains

// ---------------------------------------------------------------------------------
// Sand mode global parameters — declared in TerrainLitCustomData.hlsl
// (included BEFORE this file in each pass). NOT declared here to avoid
// redefinition errors. See Data.hlsl for explanation of why they live there.
// ---------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------
// Parallax Occlusion Mapping — Phase 3 implementation
// ---------------------------------------------------------------------------------
#ifdef _PARALLAX_OCCLUSION_MAPPING

float SampleHeightPOM(TEXTURE2D_PARAM(heightMap, heightSampler), float2 uv, float2 dxuv, float2 dyuv)
{
    return SAMPLE_TEXTURE2D_GRAD(heightMap, heightSampler, uv, dxuv, dyuv).b;
}

float2 ApplyPOM(
    TEXTURE2D_PARAM(heightMap, heightSampler),
    float2 uv,
    float2 dxuv,
    float2 dyuv,
    float3 viewDirTS,
    float  heightScale,
    float  minSteps,
    float  maxSteps)
{
    float viewAngle = abs(viewDirTS.z);
    float numSteps = lerp(maxSteps, minSteps, viewAngle);
    numSteps = max(numSteps, 2);

    float3 viewDir = viewDirTS;
    viewDir.z = abs(viewDir.z) + 1e-5;

    float2 uvOffsetPerStep = -(viewDir.xy / viewDir.z) * heightScale / numSteps;
    float  heightStepSize  = 1.0 / numSteps;

    float2 currentUV     = uv;
    float  currentRayH   = 1.0;
    float  currentTexH   = SampleHeightPOM(TEXTURE2D_ARGS(heightMap, heightSampler), currentUV, dxuv, dyuv);

    float  prevRayH  = currentRayH;
    float  prevTexH  = currentTexH;
    float2 prevUV    = currentUV;

    int    iNumSteps = (int)numSteps;

    UNITY_LOOP
    for (int step = 0; step < iNumSteps; step++)
    {
        if (currentRayH <= currentTexH)
            break;

        prevRayH = currentRayH;
        prevTexH = currentTexH;
        prevUV   = currentUV;

        currentUV   += uvOffsetPerStep;
        currentRayH -= heightStepSize;
        currentTexH  = SampleHeightPOM(TEXTURE2D_ARGS(heightMap, heightSampler), currentUV, dxuv, dyuv);
    }

    float2 searchUV   = currentUV;
    float  searchRayH = currentRayH;

    UNITY_UNROLL
    for (int j = 0; j < 5; j++)
    {
        float2 midUV   = (prevUV + searchUV) * 0.5;
        float  midRayH = (prevRayH + searchRayH) * 0.5;
        float  midTexH = SampleHeightPOM(TEXTURE2D_ARGS(heightMap, heightSampler), midUV, dxuv, dyuv);

        if (midRayH <= midTexH)
        {
            searchUV   = midUV;
            searchRayH = midRayH;
        }
        else
        {
            prevUV   = midUV;
            prevRayH = midRayH;
        }
    }

    float2 pomUV = (prevUV + searchUV) * 0.5;
    return pomUV;
}

#endif // _PARALLAX_OCCLUSION_MAPPING

// ---------------------------------------------------------------------------------
// Helpers (identical to stock)
// ---------------------------------------------------------------------------------

float GetSumHeight(float4 heights0, float4 heights1)
{
    float sumHeight = heights0.x;
    sumHeight += heights0.y;
    sumHeight += heights0.z;
    sumHeight += heights0.w;
    #ifdef _TERRAIN_8_LAYERS
        sumHeight += heights1.x;
        sumHeight += heights1.y;
        sumHeight += heights1.z;
        sumHeight += heights1.w;
    #endif
    return sumHeight;
}

#ifdef _NORMALMAP
float3 SampleNormalGrad(TEXTURE2D_PARAM(textureName, samplerName), float2 uv, float2 dxuv, float2 dyuv, float scale)
{
    float4 nrm = SAMPLE_TEXTURE2D_GRAD(textureName, samplerName, uv, dxuv, dyuv);
#ifdef SURFACE_GRADIENT
    #ifdef UNITY_NO_DXT5nm
        return float3(UnpackDerivativeNormalRGB(nrm, scale), 0);
    #else
        return float3(UnpackDerivativeNormalRGorAG(nrm, scale), 0);
    #endif
#else
    #ifdef UNITY_NO_DXT5nm
        return UnpackNormalRGB(nrm, scale);
    #else
        return UnpackNormalMapRGorAG(nrm, scale);
    #endif
#endif
}
#endif

float4 RemapMasks(float4 masks, float blendMask, float4 remapOffset, float4 remapScale)
{
    float4 ret = masks;
    ret.b *= blendMask;
    ret = ret * remapScale + remapOffset;
    return ret;
}

#ifdef OVERRIDE_SPLAT_SAMPLER_NAME
    #define sampler_Splat0 OVERRIDE_SPLAT_SAMPLER_NAME
    SAMPLER(OVERRIDE_SPLAT_SAMPLER_NAME);
#endif

// ---------------------------------------------------------------------------------
// Sand Mode: Procedural ripple normal perturbation
//
// Based on Journey's approach (GDC John Edwards / Alan Zucconi):
// Wind-aligned ripple pattern projected onto the slope. The ripple
// direction is perpendicular to the terrain slope (like real aeolian dunes).
// ---------------------------------------------------------------------------------
#ifdef _SAND_MODE
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
float3 ComputeSandRippleNormal(float3 worldPos, float3 normalWS, float rippleScale, float rippleStrength)
{
    // Project world position onto the horizontal plane for ripple UVs
    float2 rippleUV = worldPos.xz * rippleScale;

    // Compute slope direction — ripples align perpendicular to downhill
    float3 slopeDir = normalWS - float3(0, 1, 0) * dot(normalWS, float3(0, 1, 0));
    float slopeLen = length(slopeDir);

    // Only generate ripples on slopes — flat areas get no ripples
    if (slopeLen < 0.01)
        return float3(0, 0, 0);

    slopeDir /= slopeLen;

    // Ripple wave aligned perpendicular to slope direction
    float2 rippleDir = normalize(float2(-slopeDir.z, slopeDir.x));
    float ripplePhase = dot(rippleUV, rippleDir);

    // Multi-octave ripple: primary + secondary for visual complexity
    float wave1 = sin(ripplePhase * 6.2831853);
    float wave2 = sin(ripplePhase * 2.0 * 6.2831853 + 1.37) * 0.3;
    float ripple = (wave1 + wave2) * rippleStrength * slopeLen;

    // Convert to tangent-space normal perturbation
    // The ripple displaces along the slope direction
    float3 perturbation = slopeDir * ripple;
    return perturbation;
}
#endif
#endif

// ---------------------------------------------------------------------------------
// Main splatmap blend — now accepts viewDirTS for POM
// ---------------------------------------------------------------------------------
void TerrainSplatBlend(float2 controlUV, float2 splatBaseUV, float3 viewDirTS, float cameraDist, inout TerrainLitSurfaceData surfaceData)
{
    float4 albedo[_LAYER_COUNT];
    float3 normal[_LAYER_COUNT];
    float4 masks[_LAYER_COUNT];

#ifdef _NORMALMAP
    #define SampleNormal(i) SampleNormalGrad(_Normal##i, sampler_Splat0, splatuv, splatdxuv, splatdyuv, _NormalScale##i)
#else
    #define SampleNormal(i) float3(0, 0, 0)
#endif

#define DefaultMask(i) float4(_Metallic##i, _MaskMapRemapOffset##i.y + _MaskMapRemapScale##i.y, _MaskMapRemapOffset##i.z + 0.5 * _MaskMapRemapScale##i.z, albedo[i].a * _Smoothness##i)

#ifdef _MASKMAP
    #define MaskModeMasks(i, blendMask) RemapMasks(SAMPLE_TEXTURE2D_GRAD(_Mask##i, sampler_Splat0, splatuv, splatdxuv, splatdyuv), blendMask, _MaskMapRemapOffset##i, _MaskMapRemapScale##i)
    #define SampleMasks(i, blendMask) lerp(DefaultMask(i), MaskModeMasks(i, blendMask), _LayerHasMask##i)
    #define NullMask(i)               float4(0, 1, _MaskMapRemapOffset##i.z, 0)
#else
    #define SampleMasks(i, blendMask) DefaultMask(i)
    #define NullMask(i)               float4(0, 1, 0, 0)
#endif

// ---------------------------------------------------------------------------------
// SampleResults macro — POM injection point per layer
// ---------------------------------------------------------------------------------
#define SampleResults(i, mask)                                                                                  \
    UNITY_BRANCH if (mask > 0)                                                                                  \
    {                                                                                                           \
        float2 splatuv = splatBaseUV * _Splat##i##_ST.xy + _Splat##i##_ST.zw;                                   \
        float2 splatdxuv = dxuv * _Splat##i##_ST.x;                                                             \
        float2 splatdyuv = dyuv * _Splat##i##_ST.y;                                                             \
        /* --- POM INJECTION (Phase 3) --- */                                                                    \
        POM_APPLY(i, splatuv, splatdxuv, splatdyuv, viewDirTS, cameraDist)                                      \
        /* --- End POM --- */                                                                                    \
        albedo[i] = SAMPLE_TEXTURE2D_GRAD(_Splat##i, sampler_Splat0, splatuv, splatdxuv, splatdyuv);            \
        albedo[i].rgb *= _DiffuseRemapScale##i.xyz;                                                             \
        normal[i] = SampleNormal(i);                                                                            \
        masks[i] = SampleMasks(i, mask);                                                                        \
    }                                                                                                           \
    else                                                                                                        \
    {                                                                                                           \
        albedo[i] = float4(0, 0, 0, 0);                                                                         \
        normal[i] = float3(0, 0, 0);                                                                            \
        masks[i] = NullMask(i);                                                                                 \
    }

// ---------------------------------------------------------------------------------
// POM_APPLY macro
// ---------------------------------------------------------------------------------
#ifdef _PARALLAX_OCCLUSION_MAPPING
    #if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
        #ifdef _MASKMAP
            #define POM_APPLY(i, splatuv, splatdxuv, splatdyuv, viewDirTS, cameraDist)                          \
                if (_EnablePOMLayer##i > 0 && _HeightScale##i > 0 && _LayerHasMask##i > 0)                       \
                {                                                                                               \
                    float pomFade##i = 1.0 - saturate(cameraDist / max(_POMDistanceFade, 1.0));                  \
                    float fadedScale##i = _HeightScale##i * pomFade##i;                                          \
                    if (fadedScale##i > 0.0001)                                                                  \
                    {                                                                                           \
                        splatuv = ApplyPOM(TEXTURE2D_ARGS(_Mask##i, sampler_Splat0),                            \
                            splatuv, splatdxuv, splatdyuv, viewDirTS,                                           \
                            fadedScale##i, _POMMinSteps##i, _POMMaxSteps##i);                                   \
                    }                                                                                           \
                }
        #else
            #define POM_APPLY(i, splatuv, splatdxuv, splatdyuv, viewDirTS, cameraDist)
        #endif
    #else
        #define POM_APPLY(i, splatuv, splatdxuv, splatdyuv, viewDirTS, cameraDist)
    #endif
#else
    #define POM_APPLY(i, splatuv, splatdxuv, splatdyuv, viewDirTS, cameraDist)
#endif

    // Derivatives
#if defined(SHADER_STAGE_RAY_TRACING)
    float2 dxuv = 0;
    float2 dyuv = 0;
#else
    float2 dxuv = ddx(splatBaseUV);
    float2 dyuv = ddy(splatBaseUV);
#endif

    // ---------------------------------------------------------------------------------
    // Control map sampling with forced bilinear filtering
    // ---------------------------------------------------------------------------------
    float2 blendUV0 = (controlUV.xy * (_Control0_TexelSize.zw - 1.0f) + 0.5f) * _Control0_TexelSize.xy;
    float4 blendMasks0 = SAMPLE_TEXTURE2D(_Control0, sampler_linear_clamp, blendUV0);
    #ifdef _TERRAIN_8_LAYERS
        float2 blendUV1 = (controlUV.xy * (_Control1_TexelSize.zw - 1.0f) + 0.5f) * _Control1_TexelSize.xy;
        float4 blendMasks1 = SAMPLE_TEXTURE2D(_Control1, sampler_linear_clamp, blendUV1);
    #else
        float4 blendMasks1 = float4(0, 0, 0, 0);
    #endif

    SampleResults(0, blendMasks0.x);
    SampleResults(1, blendMasks0.y);
    SampleResults(2, blendMasks0.z);
    SampleResults(3, blendMasks0.w);
    #ifdef _TERRAIN_8_LAYERS
        SampleResults(4, blendMasks1.x);
        SampleResults(5, blendMasks1.y);
        SampleResults(6, blendMasks1.z);
        SampleResults(7, blendMasks1.w);
    #endif

#undef SampleNormal
#undef SampleMasks
#undef SampleResults
#undef POM_APPLY

    // ---------------------------------------------------------------------------------
    // Weight computation (height blend / density blend) — identical to stock
    // ---------------------------------------------------------------------------------

    float weights[_LAYER_COUNT];
    ZERO_INITIALIZE_ARRAY(float, weights, _LAYER_COUNT);

    #ifdef _MASKMAP
        #if defined(_TERRAIN_BLEND_HEIGHT)
            float maxHeight = masks[0].z;
            maxHeight = max(maxHeight, masks[1].z);
            maxHeight = max(maxHeight, masks[2].z);
            maxHeight = max(maxHeight, masks[3].z);
            #ifdef _TERRAIN_8_LAYERS
                maxHeight = max(maxHeight, masks[4].z);
                maxHeight = max(maxHeight, masks[5].z);
                maxHeight = max(maxHeight, masks[6].z);
                maxHeight = max(maxHeight, masks[7].z);
            #endif

            float transition = max(_HeightTransition, 1e-5);

            float4 weightedHeights0 = { masks[0].z, masks[1].z, masks[2].z, masks[3].z };
            weightedHeights0 = weightedHeights0 - maxHeight.xxxx;
            weightedHeights0 = (max(0, weightedHeights0 + transition) + 1e-6) * blendMasks0;

            #ifdef _TERRAIN_8_LAYERS
                float4 weightedHeights1 = { masks[4].z, masks[5].z, masks[6].z, masks[7].z };
                weightedHeights1 = weightedHeights1 - maxHeight.xxxx;
                weightedHeights1 = (max(0, weightedHeights1 + transition) + 1e-6) * blendMasks1;
            #else
                float4 weightedHeights1 = { 0, 0, 0, 0 };
            #endif

            float sumHeight = GetSumHeight(weightedHeights0, weightedHeights1);
            blendMasks0 = weightedHeights0 / sumHeight.xxxx;
            #ifdef _TERRAIN_8_LAYERS
                blendMasks1 = weightedHeights1 / sumHeight.xxxx;
            #endif
        #elif defined(_TERRAIN_BLEND_DENSITY)
            float4 opacityAsDensity0 = saturate((float4(albedo[0].a, albedo[1].a, albedo[2].a, albedo[3].a) - (float4(1.0, 1.0, 1.0, 1.0) - blendMasks0)) * 20.0);
            opacityAsDensity0 += 0.001f * blendMasks0;
            float4 useOpacityAsDensityParam0 = { _DiffuseRemapScale0.w, _DiffuseRemapScale1.w, _DiffuseRemapScale2.w, _DiffuseRemapScale3.w };
            blendMasks0 = lerp(opacityAsDensity0, blendMasks0, useOpacityAsDensityParam0);
            #ifdef _TERRAIN_8_LAYERS
                float4 opacityAsDensity1 = saturate((float4(albedo[4].a, albedo[5].a, albedo[6].a, albedo[7].a) - (float4(1.0, 1.0, 1.0, 1.0) - blendMasks1)) * 20.0);
                opacityAsDensity1 += 0.001f * blendMasks1;
                float4 useOpacityAsDensityParam1 = { _DiffuseRemapScale4.w, _DiffuseRemapScale5.w, _DiffuseRemapScale6.w, _DiffuseRemapScale7.w };
                blendMasks1 = lerp(opacityAsDensity1, blendMasks1, useOpacityAsDensityParam1);
            #endif

            float sumHeight = GetSumHeight(blendMasks0, blendMasks1);
            blendMasks0 = blendMasks0 / sumHeight.xxxx;
            #ifdef _TERRAIN_8_LAYERS
                blendMasks1 = blendMasks1 / sumHeight.xxxx;
            #endif
        #endif
    #endif

    weights[0] = blendMasks0.x;
    weights[1] = blendMasks0.y;
    weights[2] = blendMasks0.z;
    weights[3] = blendMasks0.w;
    #ifdef _TERRAIN_8_LAYERS
        weights[4] = blendMasks1.x;
        weights[5] = blendMasks1.y;
        weights[6] = blendMasks1.z;
        weights[7] = blendMasks1.w;
    #endif

    // ---------------------------------------------------------------------------------
    // Accumulate final surface data
    // ---------------------------------------------------------------------------------

    surfaceData.albedo = 0;
    surfaceData.normalData = 0;
    float3 outMasks = 0;
    float  blendedHeight = 0;

    UNITY_UNROLL for (int i = 0; i < _LAYER_COUNT; ++i)
    {
        surfaceData.albedo += albedo[i].rgb * weights[i];
        surfaceData.normalData += normal[i].rgb * weights[i];
        outMasks += masks[i].xyw * weights[i];
        blendedHeight += masks[i].z * weights[i];
    }
    surfaceData.smoothness = outMasks.z;
    surfaceData.metallic   = outMasks.x;
    surfaceData.ao         = outMasks.y;
    surfaceData.height     = blendedHeight;

    // ---------------------------------------------------------------------------------
    // Sand mode: accumulate sand weight from enabled layers
    // ---------------------------------------------------------------------------------
    surfaceData.sandWeight = 0;
#ifdef _SAND_MODE
    #if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    {
        surfaceData.sandWeight += weights[0] * step(0.5, _EnableSandMode0);
        surfaceData.sandWeight += weights[1] * step(0.5, _EnableSandMode1);
        surfaceData.sandWeight += weights[2] * step(0.5, _EnableSandMode2);
        surfaceData.sandWeight += weights[3] * step(0.5, _EnableSandMode3);
        #ifdef _TERRAIN_8_LAYERS
            surfaceData.sandWeight += weights[4] * step(0.5, _EnableSandMode4);
            surfaceData.sandWeight += weights[5] * step(0.5, _EnableSandMode5);
            surfaceData.sandWeight += weights[6] * step(0.5, _EnableSandMode6);
            surfaceData.sandWeight += weights[7] * step(0.5, _EnableSandMode7);
        #endif
    }
    #endif
#endif

    // ---------------------------------------------------------------------------------
    // Wind mode: accumulate displacement weight from displacement-enabled layers
    // Used to gate wind normal perturbation in the fragment shader.
    // ---------------------------------------------------------------------------------
    surfaceData.displacementWeight = 0;
#ifdef _WIND_DISPLACEMENT
    #if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    {
        surfaceData.displacementWeight += weights[0] * step(0.5, _EnableDisplacementLayer0);
        surfaceData.displacementWeight += weights[1] * step(0.5, _EnableDisplacementLayer1);
        surfaceData.displacementWeight += weights[2] * step(0.5, _EnableDisplacementLayer2);
        surfaceData.displacementWeight += weights[3] * step(0.5, _EnableDisplacementLayer3);
        #ifdef _TERRAIN_8_LAYERS
            surfaceData.displacementWeight += weights[4] * step(0.5, _EnableDisplacementLayer4);
            surfaceData.displacementWeight += weights[5] * step(0.5, _EnableDisplacementLayer5);
            surfaceData.displacementWeight += weights[6] * step(0.5, _EnableDisplacementLayer6);
            surfaceData.displacementWeight += weights[7] * step(0.5, _EnableDisplacementLayer7);
        #endif
    }
    #endif
#endif

    // ---------------------------------------------------------------------------------
    // Grass tint (L2): accumulate weight from grass-tint-enabled layers.
    // Consumed in Data.hlsl to fade the terrain toward the grass color at distance.
    // ---------------------------------------------------------------------------------
    surfaceData.grassTintWeight = 0;
#ifdef _GRASS_TINT
    #if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    {
        surfaceData.grassTintWeight += weights[0] * step(0.5, _EnableGrassTintLayer0);
        surfaceData.grassTintWeight += weights[1] * step(0.5, _EnableGrassTintLayer1);
        surfaceData.grassTintWeight += weights[2] * step(0.5, _EnableGrassTintLayer2);
        surfaceData.grassTintWeight += weights[3] * step(0.5, _EnableGrassTintLayer3);
        #ifdef _TERRAIN_8_LAYERS
            surfaceData.grassTintWeight += weights[4] * step(0.5, _EnableGrassTintLayer4);
            surfaceData.grassTintWeight += weights[5] * step(0.5, _EnableGrassTintLayer5);
            surfaceData.grassTintWeight += weights[6] * step(0.5, _EnableGrassTintLayer6);
            surfaceData.grassTintWeight += weights[7] * step(0.5, _EnableGrassTintLayer7);
        #endif
    }
    #endif
#endif
}

// ---------------------------------------------------------------------------------
// Entry point called by TerrainLitData
// ---------------------------------------------------------------------------------
void TerrainLitShade(float2 uv, float3 viewDirTS, float cameraDist, inout TerrainLitSurfaceData surfaceData)
{
    TerrainSplatBlend(uv, uv, viewDirTS, cameraDist, surfaceData);
}

// ---------------------------------------------------------------------------------
// Debug display (identical to stock)
// ---------------------------------------------------------------------------------
void TerrainLitDebug(float2 uv, uint2 screenSpaceCoords, out float3 baseColor)
{
#ifdef DEBUG_DISPLAY
    if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_CONTROL)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv, _Control0);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER0)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat0_ST.xy + _Splat0_ST.zw, _Splat0);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER1)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat1_ST.xy + _Splat1_ST.zw, _Splat1);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER2)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat2_ST.xy + _Splat2_ST.zw, _Splat2);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER3)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat3_ST.xy + _Splat3_ST.zw, _Splat3);
    }
    #ifdef _TERRAIN_8_LAYERS
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER4)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat4_ST.xy + _Splat4_ST.zw, _Splat4);
        }
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER5)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat5_ST.xy + _Splat5_ST.zw, _Splat5);
        }
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER6)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat6_ST.xy + _Splat6_ST.zw, _Splat6);
        }
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER7)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat7_ST.xy + _Splat7_ST.zw, _Splat7);
        }
    #else
        else
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_NO_TEX(screenSpaceCoords, uv);
        }
    #endif
#endif
}

// ---------------------------------------------------------------------------------
// Tessellation displacement height sampling
// ---------------------------------------------------------------------------------
#ifdef _TESSELLATION_DISPLACEMENT

float SampleDisplacementHeight(float2 controlUV)
{
    float2 blendUV0 = (controlUV * (_Control0_TexelSize.zw - 1.0) + 0.5) * _Control0_TexelSize.xy;
    float4 blendMasks0 = SAMPLE_TEXTURE2D_LOD(_Control0, sampler_linear_clamp, blendUV0, 0);
    #ifdef _TERRAIN_8_LAYERS
        float2 blendUV1 = (controlUV * (_Control1_TexelSize.zw - 1.0) + 0.5) * _Control1_TexelSize.xy;
        float4 blendMasks1 = SAMPLE_TEXTURE2D_LOD(_Control1, sampler_linear_clamp, blendUV1, 0);
    #endif

    float displacement = 0;
    displacement += blendMasks0.x * _DisplacementScale0;
    displacement += blendMasks0.y * _DisplacementScale1;
    displacement += blendMasks0.z * _DisplacementScale2;
    displacement += blendMasks0.w * _DisplacementScale3;
    #ifdef _TERRAIN_8_LAYERS
        displacement += blendMasks1.x * _DisplacementScale4;
        displacement += blendMasks1.y * _DisplacementScale5;
        displacement += blendMasks1.z * _DisplacementScale6;
        displacement += blendMasks1.w * _DisplacementScale7;
    #endif

    return displacement;
}

// ---------------------------------------------------------------------------------
// Sand mode: sample sand weight at a given control UV for rim push-up gating
// Returns combined blend weight of all sand-enabled layers at this position.
// ---------------------------------------------------------------------------------
#ifdef _SAND_MODE
float SampleSandWeight(float2 controlUV)
{
    float2 blendUV0 = (controlUV * (_Control0_TexelSize.zw - 1.0) + 0.5) * _Control0_TexelSize.xy;
    float4 blendMasks0 = SAMPLE_TEXTURE2D_LOD(_Control0, sampler_linear_clamp, blendUV0, 0);

    float sandW = 0;
    sandW += blendMasks0.x * step(0.5, _EnableSandMode0);
    sandW += blendMasks0.y * step(0.5, _EnableSandMode1);
    sandW += blendMasks0.z * step(0.5, _EnableSandMode2);
    sandW += blendMasks0.w * step(0.5, _EnableSandMode3);
    #ifdef _TERRAIN_8_LAYERS
        float2 blendUV1 = (controlUV * (_Control1_TexelSize.zw - 1.0) + 0.5) * _Control1_TexelSize.xy;
        float4 blendMasks1 = SAMPLE_TEXTURE2D_LOD(_Control1, sampler_linear_clamp, blendUV1, 0);
        sandW += blendMasks1.x * step(0.5, _EnableSandMode4);
        sandW += blendMasks1.y * step(0.5, _EnableSandMode5);
        sandW += blendMasks1.z * step(0.5, _EnableSandMode6);
        sandW += blendMasks1.w * step(0.5, _EnableSandMode7);
    #endif
    return sandW;
}
#endif

// ---------------------------------------------------------------------------------
// Wind displacement: sample displacement-enabled layer blend weight
// Returns combined blend weight of all displacement-enabled layers [0-1].
// Used by the Domain shader to gate wind vertex displacement per position.
// ---------------------------------------------------------------------------------
#ifdef _WIND_DISPLACEMENT
float SampleDisplacementWeight(float2 controlUV)
{
    float2 blendUV0 = (controlUV * (_Control0_TexelSize.zw - 1.0) + 0.5) * _Control0_TexelSize.xy;
    float4 blendMasks0 = SAMPLE_TEXTURE2D_LOD(_Control0, sampler_linear_clamp, blendUV0, 0);

    float dispW = 0;
    dispW += blendMasks0.x * step(0.5, _EnableDisplacementLayer0);
    dispW += blendMasks0.y * step(0.5, _EnableDisplacementLayer1);
    dispW += blendMasks0.z * step(0.5, _EnableDisplacementLayer2);
    dispW += blendMasks0.w * step(0.5, _EnableDisplacementLayer3);
    #ifdef _TERRAIN_8_LAYERS
        float2 blendUV1 = (controlUV * (_Control1_TexelSize.zw - 1.0) + 0.5) * _Control1_TexelSize.xy;
        float4 blendMasks1 = SAMPLE_TEXTURE2D_LOD(_Control1, sampler_linear_clamp, blendUV1, 0);
        dispW += blendMasks1.x * step(0.5, _EnableDisplacementLayer4);
        dispW += blendMasks1.y * step(0.5, _EnableDisplacementLayer5);
        dispW += blendMasks1.z * step(0.5, _EnableDisplacementLayer6);
        dispW += blendMasks1.w * step(0.5, _EnableDisplacementLayer7);
    #endif
    return dispW;
}
#endif

// ---------------------------------------------------------------------------------
// Combined tessellation data — single control map pass for the Domain shader
// ---------------------------------------------------------------------------------
struct TessellationSampleData
{
    float height;
    float sandWeight;
    float displacementWeight;
};

TessellationSampleData SampleAllTessellationData(float2 controlUV)
{
    TessellationSampleData data;
    data.height = 0;
    data.sandWeight = 0;
    data.displacementWeight = 0;

    float2 blendUV0 = (controlUV * (_Control0_TexelSize.zw - 1.0) + 0.5) * _Control0_TexelSize.xy;
    float4 m0 = SAMPLE_TEXTURE2D_LOD(_Control0, sampler_linear_clamp, blendUV0, 0);
    #ifdef _TERRAIN_8_LAYERS
        float2 blendUV1 = (controlUV * (_Control1_TexelSize.zw - 1.0) + 0.5) * _Control1_TexelSize.xy;
        float4 m1 = SAMPLE_TEXTURE2D_LOD(_Control1, sampler_linear_clamp, blendUV1, 0);
    #endif

    data.height += m0.x * _DisplacementScale0;
    data.height += m0.y * _DisplacementScale1;
    data.height += m0.z * _DisplacementScale2;
    data.height += m0.w * _DisplacementScale3;
    #ifdef _TERRAIN_8_LAYERS
        data.height += m1.x * _DisplacementScale4;
        data.height += m1.y * _DisplacementScale5;
        data.height += m1.z * _DisplacementScale6;
        data.height += m1.w * _DisplacementScale7;
    #endif

    #ifdef _SAND_MODE
        data.sandWeight += m0.x * step(0.5, _EnableSandMode0);
        data.sandWeight += m0.y * step(0.5, _EnableSandMode1);
        data.sandWeight += m0.z * step(0.5, _EnableSandMode2);
        data.sandWeight += m0.w * step(0.5, _EnableSandMode3);
        #ifdef _TERRAIN_8_LAYERS
            data.sandWeight += m1.x * step(0.5, _EnableSandMode4);
            data.sandWeight += m1.y * step(0.5, _EnableSandMode5);
            data.sandWeight += m1.z * step(0.5, _EnableSandMode6);
            data.sandWeight += m1.w * step(0.5, _EnableSandMode7);
        #endif
    #endif

    #ifdef _WIND_DISPLACEMENT
        data.displacementWeight += m0.x * step(0.5, _EnableDisplacementLayer0);
        data.displacementWeight += m0.y * step(0.5, _EnableDisplacementLayer1);
        data.displacementWeight += m0.z * step(0.5, _EnableDisplacementLayer2);
        data.displacementWeight += m0.w * step(0.5, _EnableDisplacementLayer3);
        #ifdef _TERRAIN_8_LAYERS
            data.displacementWeight += m1.x * step(0.5, _EnableDisplacementLayer4);
            data.displacementWeight += m1.y * step(0.5, _EnableDisplacementLayer5);
            data.displacementWeight += m1.z * step(0.5, _EnableDisplacementLayer6);
            data.displacementWeight += m1.w * step(0.5, _EnableDisplacementLayer7);
        #endif
    #endif

    return data;
}

#endif // _TESSELLATION_DISPLACEMENT
