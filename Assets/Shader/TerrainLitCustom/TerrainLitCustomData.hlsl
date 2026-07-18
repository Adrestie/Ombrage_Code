// TerrainLitCustomData.hlsl
// Fork of TerrainLitData.hlsl — CLEAN REGENERATION
// ---------------------------------------------------------------------------------

#ifndef SHADER_STAGE_RAY_TRACING
#define SURFACE_GRADIENT
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/SampleUVMapping.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

#ifndef UNITY_TERRAIN_CB_VARS
    #define UNITY_TERRAIN_CB_VARS
#endif
#ifndef UNITY_TERRAIN_CB_DEBUG_VARS
    #define UNITY_TERRAIN_CB_DEBUG_VARS
#endif

CBUFFER_START(UnityTerrain)
    UNITY_TERRAIN_CB_VARS
#ifdef UNITY_INSTANCING_ENABLED
    float4 _TerrainHeightmapRecipSize;
#endif
    float4 _TerrainHeightmapScale;
#ifdef DEBUG_DISPLAY
    UNITY_TERRAIN_CB_DEBUG_VARS
#endif
#ifdef SCENESELECTIONPASS
    int _ObjectId;
    int _PassValue;
#endif
CBUFFER_END

#ifdef UNITY_INSTANCING_ENABLED
    TEXTURE2D(_TerrainHeightmapTexture);
    TEXTURE2D(_TerrainNormalmapTexture);
    #ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
        SAMPLER(sampler_TerrainNormalmapTexture);
    #endif
#endif

#ifdef _ALPHATEST_ON
TEXTURE2D(_TerrainHolesTexture);
SAMPLER(sampler_TerrainHolesTexture);
#endif

#ifdef _TESSELLATION_DISPLACEMENT
TEXTURE2D(_DeformationMap);
SAMPLER(sampler_DeformationMap);
TEXTURE2D(_TessellationMask);
SAMPLER(sampler_TessellationMask);
#endif

// ---------------------------------------------------------------------------------
// Sand mode uniforms — unconditional for SRP Batcher CBUFFER consistency.
// Declared here (Data.hlsl) because this file is included FIRST in each pass.
// ---------------------------------------------------------------------------------
float  _SandGlitterIntensity;
float  _SandGlitterThreshold;
float  _SandGlitterScale;
float4 _SandGlitterColor;
float  _SandRimPushUp;
float  _SandDeformTexelSize;
float4 _SandSunDirection;
float  _SandRippleScale;
float  _SandRippleStrength;
float  _SandOceanSpecPower;
float  _SandOceanSpecIntensity;
float  _SandFresnelPower;
float  _SandFresnelIntensity;
float  _SandGlitterMaxDistance;

// Glitter distance falloff ramp (baked from AnimationCurve, same pattern as tessellation)
TEXTURE2D(_SandGlitterFalloffRamp);
SAMPLER(sampler_SandGlitterFalloffRamp);

// ---------------------------------------------------------------------------------
// Wind displacement uniforms — unconditional for SRP Batcher CBUFFER consistency.
// Global map × detail map, scrolling independently, remapped to [min, max].
// ---------------------------------------------------------------------------------
float  _WindGlobalTile;
float  _WindDetailTile;
float  _WindMinValue;
float  _WindMaxValue;
float4 _WindGlobalOffsetDir;    // xy = scroll direction × speed (world units/sec)
float4 _WindDetailOffsetDir;    // xy = scroll direction × speed
float  _WindPeriod;             // temporal pulsation frequency
float  _WindTime;
float  _WindDebugMode;          // 0=off, 1=global, 2=detail, 3=combined, 4=displacement

TEXTURE2D(_WindGlobalMap);
SAMPLER(sampler_WindGlobalMap);
TEXTURE2D(_WindDetailMap);
SAMPLER(sampler_WindDetailMap);

// ---------------------------------------------------------------------------------
// Grass tint (L2) uniforms — unconditional for SRP Batcher CBUFFER consistency.
// Per-material for now (set by TerrainLitCustomSetup). _GrassTintColor becomes a
// shared global at unification (Phase 4) so it matches the grass blade color.
// ---------------------------------------------------------------------------------
float4 _GrassTintColor;
float  _GrassTintStrength;
float  _GrassTintSmoothness;
float  _GrassTintDistanceStart;
float  _GrassTintDistanceFull;
float  _GrassWaveNormalStrength;   // L2 wind-wave normal tilt amount (the rolling bands)
float  _GrassWaveLumStrength;      // L2 wind-wave albedo luminance amount (subtle)
float  _GrassSmoothnessBlend;      // 0 = keep layer smoothness, 1 = full grass tint smoothness
float  _GrassNormalBlend;          // 0 = keep layer normal (rock bumps), 1 = smooth terrain normal only

// Shared wind field (globals + GrassGustSignal) — same source the blades use (Phase 2+).
#include "../Grass/GrassWind.hlsl"

// ---------------------------------------------------------------------------------
// Terrain vertex modification
// ---------------------------------------------------------------------------------
#if !defined(SHADER_STAGE_RAY_TRACING)
#ifdef HAVE_MESH_MODIFICATION

UNITY_INSTANCING_BUFFER_START(Terrain)
UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
UNITY_INSTANCING_BUFFER_END(Terrain)

float4 ConstructTerrainTangent(float3 normal, float3 positiveZ)
{
    float3 tangent = cross(normal, positiveZ);
    return float4(tangent, -1);
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
#ifdef UNITY_INSTANCING_ENABLED
    float2 patchVertex = input.positionOS.xy;
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));
    input.positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    input.positionOS.y = height * _TerrainHeightmapScale.y;
    #ifdef ATTRIBUTES_NEED_NORMAL
        input.normalOS = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2 - 1;
    #endif
    #if defined(VARYINGS_NEED_TEXCOORD0) || defined(VARYINGS_DS_NEED_TEXCOORD0)
        #ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
            input.uv0 = sampleCoords;
        #else
            input.uv0 = sampleCoords * _TerrainHeightmapRecipSize.zw;
        #endif
    #endif
#endif
#ifdef ATTRIBUTES_NEED_TANGENT
    input.tangentOS = ConstructTerrainTangent(input.normalOS, float3(0, 0, 1));
#endif
    return input;
}
#endif
#endif

#define _EmissiveColor float3(0,0,0)
#define _AlbedoAffectEmissive 0
#define _EmissiveExposureWeight 0
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitBuiltinData.hlsl"
#undef _EmissiveColor
#undef _AlbedoAffectEmissive
#undef _EmissiveExposureWeight

#if !defined(SHADER_STAGE_RAY_TRACING) || defined(PATH_TRACING_CLUSTERED_DECALS)
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Decal/DecalUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitDecalData.hlsl"
#endif

#include "TerrainLitCustomSurfaceData.hlsl"

void TerrainLitShade(float2 uv, float3 viewDirTS, float cameraDist, inout TerrainLitSurfaceData surfaceData);
void TerrainLitDebug(float2 uv, uint2 screenSpaceCoords, out float3 baseColor);

float3 ConvertToNormalTS(float3 normalData, float3 tangentWS, float3 bitangentWS)
{
#ifdef _NORMALMAP
    #ifdef SURFACE_GRADIENT
        return SurfaceGradientFromTBN(normalData.xy, tangentWS, bitangentWS);
    #else
        return normalData;
    #endif
#else
    #ifdef SURFACE_GRADIENT
        return float3(0.0, 0.0, 0.0);
    #else
        return float3(0.0, 0.0, 1.0);
    #endif
#endif
}

float3 ComputeViewDirTS(float3 V, float3x3 tangentToWorld)
{
    return normalize(mul(tangentToWorld, V));
}

// =========================================================================
// SAND MODE FUNCTIONS
// =========================================================================
#ifdef _SAND_MODE
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)

uint3 pcg3d(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

float3 HashToRandomNormal(float3 worldPos, float scale)
{
    int3 cell = int3(floor(worldPos * scale));
    uint3 h = pcg3d(uint3(
        uint(cell.x + 32768),
        uint(cell.y + 32768),
        uint(cell.z + 32768)));
    float3 rn = float3(h) / float(0xFFFFFFFFu) * 2.0 - 1.0;
    return normalize(rn);
}

// ---------------------------------------------------------------------------------
// ComputeSandGlitter — 7 params, returns float [0,1].
// reflect(-L, G) → dot(R, V): Phong specular on random grain normals.
// ---------------------------------------------------------------------------------
float ComputeSandGlitter(
    float3 worldPos,
    float3 N, float3 V, float3 L,
    float sandWeight, float scale, float threshold)
{
    if (sandWeight < 0.001) return 0;

    float upFacing = saturate(dot(N, float3(0, 1, 0)));
    if (upFacing < 0.1) return 0;

    float NdotL = saturate(dot(N, L));
    if (NdotL < 0.01) return 0;

    float3 G1 = HashToRandomNormal(worldPos, scale);
    float3 R1 = reflect(-L, G1);
    float sparkle1 = smoothstep(threshold, 1.0, saturate(dot(R1, V)));

    float3 G2 = HashToRandomNormal(worldPos, scale * 2.37);
    float3 R2 = reflect(-L, G2);
    float sparkle2 = smoothstep(threshold * 0.995, 1.0, saturate(dot(R2, V))) * 0.35;

    return saturate((sparkle1 + sparkle2) * upFacing * NdotL * sandWeight);
}

float3 ComputeSandOceanSpecular(
    float3 N, float3 V, float3 L,
    float sandWeight,
    float specPower, float specIntensity,
    float fresnelPower, float fresnelIntensity)
{
    if (sandWeight < 0.001) return float3(0, 0, 0);

    float3 H = normalize(V + L);
    float NdotH = saturate(dot(N, H));
    float spec = pow(NdotH, specPower) * specIntensity;

    float NdotV = saturate(dot(N, V));
    float fresnel = pow(1.0 - NdotV, fresnelPower) * fresnelIntensity;
    float NdotL = dot(N, L);
    fresnel *= saturate(-NdotL * 0.5 + 0.5);

    return float3(1, 1, 1) * (spec + fresnel) * sandWeight;
}

float3 ComputeSandRippleNormalWS(
    float3 worldPos, float3 normalWS,
    float sandWeight, float rippleScale, float rippleStrength)
{
    if (sandWeight < 0.001 || rippleStrength < 0.001) return float3(0, 0, 0);

    float3 up = float3(0, 1, 0);
    float3 slopeDir = normalWS - up * dot(normalWS, up);
    float slopeLen = length(slopeDir);
    if (slopeLen < 0.01) return float3(0, 0, 0);
    slopeDir /= slopeLen;

    float2 rippleDir2D = normalize(float2(-slopeDir.z, slopeDir.x));
    float ripplePhase = dot(worldPos.xz * rippleScale, rippleDir2D);
    float wave1 = sin(ripplePhase * 6.2831853);
    float wave2 = sin(ripplePhase * 2.17 * 6.2831853 + 1.37) * 0.35;

    return slopeDir * (wave1 + wave2) * rippleStrength * slopeLen * sandWeight;
}

#endif
#endif

// =========================================================================
// WIND DISPLACEMENT — Fragment-side normal perturbation
//
// Samples global × detail noise at 3 points (center, +X, +Z),
// remaps to [min, max], computes gradient via finite differences.
// =========================================================================
#ifdef _WIND_DISPLACEMENT
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)

float SampleWindHeight(float2 worldXZ)
{
    float time = _WindTime;

    float2 globalUV = worldXZ * _WindGlobalTile + _WindGlobalOffsetDir.xy * time;
    float2 detailUV = worldXZ * _WindDetailTile + _WindDetailOffsetDir.xy * time;

    float global = SAMPLE_TEXTURE2D_LOD(_WindGlobalMap, sampler_WindGlobalMap, globalUV, 0).r;
    float detail = SAMPLE_TEXTURE2D_LOD(_WindDetailMap, sampler_WindDetailMap, detailUV, 0).r;

    float combined = global * detail;

    // Period pulsation: 0 = always on, >0 = fades in/out
    if (_WindPeriod > 0.0001)
    {
        float pulse = sin(time * _WindPeriod * 6.2831853) * 0.5 + 0.5;
        combined *= pulse;
    }

    return lerp(_WindMinValue, _WindMaxValue, combined);
}

float3 ComputeWindNormalPerturbation(float3 worldPos, float dispWeight)
{
    float range = _WindMaxValue - _WindMinValue;
    if (dispWeight < 0.001 || abs(range) < 0.0001)
        return float3(0, 0, 0);

    float eps = 0.5;  // world-space step (meters)
    float hC = SampleWindHeight(worldPos.xz);
    float hR = SampleWindHeight(worldPos.xz + float2(eps, 0));
    float hU = SampleWindHeight(worldPos.xz + float2(0, eps));

    float dHdx = (hR - hC) / eps;
    float dHdz = (hU - hC) / eps;

    return float3(-dHdx * dispWeight, 0, -dHdz * dispWeight);
}

#endif
#endif

// =========================================================================
// GRASS TINT (L2) — distance color-match toward the grass color.
// 1a: static color only. 1b adds the shared wind gust (normal tilt + luminance)
// via GrassWind.hlsl so the field "ondulates" like tall grass seen from above.
// =========================================================================
#ifdef _GRASS_TINT
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
void ApplyGrassTint(float cameraDist, float grassWeight, float3 worldPos, float time,
                    inout float3 baseColor, inout float3 normalWS, float3 geomNormalWS,
                    inout float perceptualSmoothness, inout float metallic)
{
    if (grassWeight < 0.001) return;
    // Tint rises with camera distance: far grass / high camera reads as terrain-colored grass, near
    // grass stays untouched. The distance range is governed SOLELY by Grass Tint's own Distance Start /
    // Distance Full — independent of the blades (changing a blade setting no longer moves the tint).
    float distFull = max(_GrassTintDistanceFull, _GrassTintDistanceStart + 1e-3);
    float distFade = smoothstep(_GrassTintDistanceStart, distFull, cameraDist);
    float amount = saturate(grassWeight * distFade * _GrassTintStrength);
    if (amount < 0.001) return;

    // --- Colour: NOT a flat fill. Static patch variation (clumps of lighter / drier grass) plus
    //     the terrain's OWN albedo detail kept as value, so real structure shows through. ---
    float patch = GW_fbm(worldPos.xz * 0.05);                              // ~20 m clumps (static)
    float3 grassCol = _GrassTintColor.rgb * lerp(0.78, 1.18, patch);       // value variation
    grassCol = lerp(grassCol, grassCol * float3(1.15, 1.05, 0.72), patch * 0.6); // drier/yellower patches
    float terrLum = dot(saturate(baseColor), float3(0.299, 0.587, 0.114));
    grassCol *= 0.7 + 0.6 * terrLum;                                       // terrain texture detail -> value
    baseColor = lerp(baseColor, grassCol, amount);

    metallic = lerp(metallic, 0.0, amount);
    // _GrassSmoothnessBlend: 0 = keep the terrain LAYER's smoothness (rocks etc. keep showing in
    // the specular), 1 = fully the grass tint smoothness (uniform -> layer surface detail hidden).
    perceptualSmoothness = lerp(perceptualSmoothness, _GrassTintSmoothness, amount * _GrassSmoothnessBlend);

    // --- Normal: suppress the layer's normal-map detail (rock bumps) toward the SMOOTH terrain
    //     normal by _GrassNormalBlend, then apply the wind wave lean. The rolling bright/dark
    //     bands come from LIGHTING on this moving surface (not a brightness multiply). ---
    float3 baseN = normalize(lerp(normalWS, geomNormalWS, saturate(amount * _GrassNormalBlend)));
    float g = GrassGustSignal(worldPos.xz, time);                          // ~[-1.8,1.8] * windMain
    float3 lean = GrassWindDirWS() * (g * _GrassWaveNormalStrength * amount);
    float3 n = baseN + lean;
    float nl = dot(n, n);
    if (nl > 1e-6) normalWS = n * rsqrt(nl);
    baseColor *= 1.0 - _GrassWaveLumStrength * amount * saturate(-g) * 0.5; // grass bent away = a touch darker

    // Fake far shadow: beyond the real shadow sphere the grass canopy self-shadowing is faked by an
    // albedo darkening (ramps in as the real cast shadows ramp out -> continuous across the edge).
    // NOTE: still tied to the grass-tint LAYER coverage (grassTintWeight), not the real per-species
    // density — see TODO 0c (feed _GrassLayerCoverage to the terrain) for the proper "only where grass is".
    baseColor *= GrassFakeShadowMul(worldPos.xz, cameraDist);
}
#endif
#endif

// =========================================================================
// GetSurfaceAndBuiltinData
// =========================================================================
void GetSurfaceAndBuiltinData(inout FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    ZERO_INITIALIZE(BuiltinData, builtinData);

#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
    float2 terrainNormalMapUV = (input.texCoord0.xy + 0.5f) * _TerrainHeightmapRecipSize.xy;
    input.texCoord0.xy *= _TerrainHeightmapRecipSize.zw;
#endif

#ifdef _ALPHATEST_ON
    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, input.texCoord0.xy).r;
    GENERIC_ALPHA_TEST(hole, 0.5);
#endif

#ifndef EDITOR_VISUALIZATION
    input.texCoord1 = input.texCoord2 = input.texCoord0;
#endif

    float3 viewDirTS = float3(0, 0, 1);
    float cameraDist = length(input.positionRWS);

    TerrainLitSurfaceData terrainLitSurfaceData;
    InitializeTerrainLitSurfaceData(terrainLitSurfaceData);

#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
    #ifdef TERRAIN_PERPIXEL_NORMAL_OVERRIDE
        float3 normalWS = float3(0, 1, 0);
    #else
        float3 normalOS = SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, terrainNormalMapUV).rgb * 2 - 1;
        float3 normalWS = mul((float3x3)GetObjectToWorldMatrix(), normalOS);
    #endif
    float4 tangentWS = ConstructTerrainTangent(normalWS, GetObjectToWorldMatrix()._13_23_33);
    input.tangentToWorld = BuildTangentToWorld(tangentWS, normalWS);
    viewDirTS = ComputeViewDirTS(V, input.tangentToWorld);
    TerrainLitShade(input.texCoord0.xy, viewDirTS, cameraDist, terrainLitSurfaceData);
    #ifdef TERRAIN_PERPIXEL_NORMAL_OVERRIDE
        normalWS = terrainLitSurfaceData.normalData.xyz;
        tangentWS = ConstructTerrainTangent(normalWS, GetObjectToWorldMatrix()._13_23_33);
        input.tangentToWorld = BuildTangentToWorld(tangentWS, normalWS);
    #endif
    surfaceData.normalWS = normalWS;
#else
    viewDirTS = ComputeViewDirTS(V, input.tangentToWorld);
    TerrainLitShade(input.texCoord0.xy, viewDirTS, cameraDist, terrainLitSurfaceData);
    surfaceData.normalWS = float3(0, 0, 0);
#endif

    surfaceData.tangentWS = normalize(input.tangentToWorld[0].xyz);
    surfaceData.geomNormalWS = input.tangentToWorld[2];
    surfaceData.baseColor = terrainLitSurfaceData.albedo;
    surfaceData.perceptualSmoothness = terrainLitSurfaceData.smoothness;
    surfaceData.metallic = terrainLitSurfaceData.metallic;
    surfaceData.ambientOcclusion = terrainLitSurfaceData.ao;
    surfaceData.subsurfaceMask = 0;
    surfaceData.transmissionMask = 0;
    surfaceData.thickness = 1;
    surfaceData.diffusionProfileHash = 0;
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
    surfaceData.anisotropy = 0.0;
    surfaceData.specularColor = float3(0, 0, 0);
    surfaceData.coatMask = 0.0;
    surfaceData.iridescenceThickness = 0.0;
    surfaceData.iridescenceMask = 0.0;
    surfaceData.ior = 1.0;
    surfaceData.transmittanceColor = float3(1, 1, 1);
    surfaceData.atDistance = 1000000.0;
    surfaceData.transmittanceMask = 0.0;
    surfaceData.specularOcclusion = 1.0;

    // -----------------------------------------------------------------
    // Normal pipeline (splatmap normals, decals)
    // -----------------------------------------------------------------
#if !defined(ENABLE_TERRAIN_PERPIXEL_NORMAL) || !defined(TERRAIN_PERPIXEL_NORMAL_OVERRIDE)
    float3 normalTS = ConvertToNormalTS(terrainLitSurfaceData.normalData, input.tangentToWorld[0], input.tangentToWorld[1]);
    #ifdef DECAL_NORMAL_BLENDING
    if (_EnableDecals)
    {
        #ifndef SURFACE_GRADIENT
        normalTS = SurfaceGradientFromTangentSpaceNormalAndFromTBN(normalTS, input.tangentToWorld[0], input.tangentToWorld[1]);
        #endif
        float alpha = 1.0;
        DecalSurfaceData decalSurfaceData = GetDecalSurfaceData(posInput, input, alpha);
        ApplyDecalToSurfaceData(decalSurfaceData, input.tangentToWorld[2], surfaceData, normalTS);
    }
    GetNormalWS_SG(input, normalTS, surfaceData.normalWS, float3(1, 1, 1));
    #else
    GetNormalWS(input, normalTS, surfaceData.normalWS, float3(1, 1, 1));
    #if HAVE_DECALS
    if (_EnableDecals)
    {
        float alpha = 1.0;
        DecalSurfaceData decalSurfaceData = GetDecalSurfaceData(posInput, input, alpha);
        ApplyDecalToSurfaceData(decalSurfaceData, input.tangentToWorld[2], surfaceData);
    }
    #endif
    #endif
#elif HAVE_DECALS
    if (_EnableDecals)
    {
        float alpha = 1.0;
        DecalSurfaceData decalSurfaceData = GetDecalSurfaceData(posInput, input, alpha);
        #ifdef DECAL_NORMAL_BLENDING
        float3 normalTS = SurfaceGradientFromPerturbedNormal(input.tangentToWorld[2], surfaceData.normalWS);
        ApplyDecalToSurfaceData(decalSurfaceData, input.tangentToWorld[2], surfaceData, normalTS);
        GetNormalWS_SG(input, normalTS, surfaceData.normalWS, float3(1, 1, 1));
        #else
        ApplyDecalToSurfaceData(decalSurfaceData, input.tangentToWorld[2], surfaceData);
        #endif
    }
#endif

    // -----------------------------------------------------------------
    // SAND: Ripples (after normal pipeline, before everything else)
    // -----------------------------------------------------------------
#ifdef _SAND_MODE
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    if (terrainLitSurfaceData.sandWeight > 0.001 && _SandRippleStrength > 0.001)
    {
        float3 absWP = GetAbsolutePositionWS(input.positionRWS);
        float3 ripple = ComputeSandRippleNormalWS(absWP, surfaceData.normalWS,
            terrainLitSurfaceData.sandWeight, _SandRippleScale, _SandRippleStrength);
        float3 pertN = surfaceData.normalWS + ripple;
        float lenSq = dot(pertN, pertN);
        if (lenSq > 1e-6)
            surfaceData.normalWS = pertN * rsqrt(lenSq);
    }
#endif
#endif

    // -----------------------------------------------------------------
    // WIND: Normal perturbation (after decals + sand ripples)
    //
    // Global × detail noise, finite differences for gradient.
    // Gated by displacementWeight: only displacement-enabled layers
    // (e.g. snow) get wind normals. POM-only layers (e.g. ice) unaffected.
    // -----------------------------------------------------------------
#ifdef _WIND_DISPLACEMENT
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    if (terrainLitSurfaceData.displacementWeight > 0.001 && abs(_WindMaxValue - _WindMinValue) > 0.0001)
    {
        float3 absWP_wind = GetAbsolutePositionWS(input.positionRWS);
        float3 windPert = ComputeWindNormalPerturbation(
            absWP_wind,
            terrainLitSurfaceData.displacementWeight);
        float3 windN = surfaceData.normalWS + windPert;
        float windLenSq = dot(windN, windN);
        if (windLenSq > 1e-6)
            surfaceData.normalWS = windN * rsqrt(windLenSq);
    }
#endif
#endif

    // -----------------------------------------------------------------
    // WIND: Debug visualization
    //
    // Overrides baseColor to show the noise maps on the terrain surface.
    //   1 = Global only
    //   2 = Detail only
    //   3 = Combined (global × detail)
    //   4 = Final displacement (remapped, with period pulse)
    // -----------------------------------------------------------------
#ifdef _WIND_DISPLACEMENT
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    if (_WindDebugMode > 0.5)
    {
        float3 absWP_dbg = GetAbsolutePositionWS(input.positionRWS);
        float time_dbg = _WindTime;

        float2 globalUV_dbg = absWP_dbg.xz * _WindGlobalTile + _WindGlobalOffsetDir.xy * time_dbg;
        float2 detailUV_dbg = absWP_dbg.xz * _WindDetailTile + _WindDetailOffsetDir.xy * time_dbg;

        float global_dbg = SAMPLE_TEXTURE2D(_WindGlobalMap, sampler_WindGlobalMap, globalUV_dbg).r;
        float detail_dbg = SAMPLE_TEXTURE2D(_WindDetailMap, sampler_WindDetailMap, detailUV_dbg).r;
        float combined_dbg = global_dbg * detail_dbg;

        float vis = 0;
        if (_WindDebugMode < 1.5)        vis = global_dbg;       // 1 = Global
        else if (_WindDebugMode < 2.5)   vis = detail_dbg;       // 2 = Detail
        else if (_WindDebugMode < 3.5)   vis = combined_dbg;     // 3 = Combined
        else                                                      // 4 = Displacement
        {
            float pulse_dbg = 1.0;
            if (_WindPeriod > 0.0001)
                pulse_dbg = sin(time_dbg * _WindPeriod * 6.2831853) * 0.5 + 0.5;
            float disp = lerp(_WindMinValue, _WindMaxValue, combined_dbg * pulse_dbg);
            // Remap displacement to [0,1] for visualization: green = positive, red = negative
            float range_dbg = max(abs(_WindMinValue), abs(_WindMaxValue));
            if (range_dbg > 0.0001)
            {
                float norm = disp / range_dbg; // [-1, 1]
                surfaceData.baseColor = float3(saturate(-norm), saturate(norm), 0);
            }
            else
            {
                surfaceData.baseColor = float3(0.5, 0.5, 0.5);
            }
            surfaceData.metallic = 0;
            surfaceData.perceptualSmoothness = 0.1;
        }

        if (_WindDebugMode < 3.5)
        {
            surfaceData.baseColor = float3(vis, vis, vis);
            surfaceData.metallic = 0;
            surfaceData.perceptualSmoothness = 0.1;
        }
    }
#endif
#endif

    // -----------------------------------------------------------------
    // SAND: Glitter + Ocean Specular
    //
    // v8 — PBR push with moderate metallic, shadow-aware:
    //
    // GBuffer constraints:
    //   GBuffer0 (baseColor) = R8G8B8A8_SRGB → clamped [0,1] → no HDR
    //   GBuffer3 (emission)  = float HDR but bypasses shadow maps
    //   PBR specular = computed in deferred lighting × shadowAtt ✓
    //
    // The deferred lighting evaluates per light:
    //   F(F0, LdotH) × D(NdotH, roughness) × G(NdotV, NdotL) × lightColor × shadowAtt
    //
    // By pushing normalWS → H: NdotH → 1, D-term spikes.
    // By pushing smoothness → 0.97: roughness → 0.03, D peak is very sharp.
    // By pushing metallic → 0.25: F0 goes from 0.04 to ~0.23.
    //
    //   Sun (130k lux, shadowAtt=1):
    //     0.23 × D(~200) × 0.8 × 130000 = 4.8M nits → bloom ✓
    //
    //   Shadow (shadowAtt=0, ambient only):
    //     0.23 × envBRDF(~0.5) × probeLight(~3000) = 345 nits
    //     vs diffuse: 0.8 × 0.75 × 3000/π = 573 nits
    //     Ratio: +12% brightness → barely visible ✓
    //
    //   Compare metallic=1.0 in shadow:
    //     F0=0.8, diffuse=0 (fully metallic) → sparkle IS the only light
    //     → very visible ✗ (this was the bug in v3)
    //
    // _SandGlitterIntensity [0,1]: lerp strength toward mirror normal.
    // -----------------------------------------------------------------
#ifdef _SAND_MODE
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    if (terrainLitSurfaceData.sandWeight > 0.001)
    {
        float3 absWorldPos = GetAbsolutePositionWS(input.positionRWS);
        float3 N = surfaceData.normalWS;
        float3 L = normalize(_SandSunDirection.xyz);

        // Glitter: Journey reflect(-L,G) → dot(R,V), NdotL gated, returns [0,1]
        float sparkle = ComputeSandGlitter(
            absWorldPos, N, V, L,
            terrainLitSurfaceData.sandWeight,
            _SandGlitterScale,
            _SandGlitterThreshold);

        // Distance falloff: sample ramp texture with normalized camera distance
        float cameraDist = length(input.positionRWS);
        float glitterFade = SAMPLE_TEXTURE2D_LOD(_SandGlitterFalloffRamp, sampler_SandGlitterFalloffRamp,
            float2(saturate(cameraDist / max(_SandGlitterMaxDistance, 1.0)), 0.5), 0).r;
        sparkle *= glitterFade;

        float push = saturate(sparkle * _SandGlitterIntensity);
        if (push > 0.001)
        {
            float3 H = normalize(V + L);

            // Push normal toward H → NdotH → 1 → GGX D-term spike
            surfaceData.normalWS = normalize(lerp(surfaceData.normalWS, H, push));

            // Sharper specular peak: smoothness 0.97 → roughness 0.03
            // D ≈ 200 at NdotH=1 with this roughness
            surfaceData.perceptualSmoothness = lerp(surfaceData.perceptualSmoothness, 0.97, push);

            // Moderate metallic push: F0 goes from 0.04 → ~0.23
            // This is the CRITICAL balance:
            //   - Enough F0 for the D-term spike to produce bloom-level nits
            //   - Low enough that ambient reflection in shadow stays negligible
            //   - Diffuse is reduced by (1 - 0.25) = 75% — barely noticeable
            surfaceData.metallic = lerp(surfaceData.metallic, 0.25, push);
        }

        // Ocean specular + Fresnel: broad effects, add to baseColor
        float3 oceanSpec = ComputeSandOceanSpecular(
            N, V, L,
            terrainLitSurfaceData.sandWeight,
            _SandOceanSpecPower,
            _SandOceanSpecIntensity,
            _SandFresnelPower,
            _SandFresnelIntensity);
        surfaceData.baseColor = saturate(surfaceData.baseColor + oceanSpec);
    }
#endif
#endif

    // -----------------------------------------------------------------
    // GRASS TINT (L2): fade the terrain toward the grass color at distance.
    // -----------------------------------------------------------------
#ifdef _GRASS_TINT
#if (SHADERPASS == SHADERPASS_FORWARD) || (SHADERPASS == SHADERPASS_GBUFFER)
    if (terrainLitSurfaceData.grassTintWeight > 0.001)
    {
        float3 grassWP = GetAbsolutePositionWS(input.positionRWS);
        ApplyGrassTint(cameraDist, terrainLitSurfaceData.grassTintWeight, grassWP, _GrassWindTime,
                       surfaceData.baseColor, surfaceData.normalWS, surfaceData.geomNormalWS,
                       surfaceData.perceptualSmoothness, surfaceData.metallic);
    }
#endif
#endif

    float3 bentNormalWS = surfaceData.normalWS;

#if defined(DEBUG_DISPLAY)
#if !defined(SHADER_STAGE_RAY_TRACING)
    if (_DebugMipMapMode != DEBUGMIPMAPMODE_NONE)
    {
        TerrainLitDebug(input.texCoord0.xy, posInput.positionSS, surfaceData.baseColor);
        surfaceData.metallic = 0;
    }
#endif
    ApplyDebugToSurfaceData(input.tangentToWorld, surfaceData);
#endif

#if defined(_MASKMAP) && !defined(_SPECULAR_OCCLUSION_NONE)
    surfaceData.specularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(dot(surfaceData.normalWS, V)), surfaceData.ambientOcclusion, PerceptualSmoothnessToRoughness(surfaceData.perceptualSmoothness));
#endif

    GetBuiltinData(input, V, posInput, surfaceData, 1, bentNormalWS, 0, builtinData);

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}
