// TerrainLitCustomHullDomain.hlsl
// Custom Hull/Domain for TerrainLit tessellation — HDRP 17.x
//
// v10 — BUILD FIX (D3D12/Vulkan):
//
//   UNITY_SETUP_INSTANCE_ID added to HullConstant and Domain.
//   Without it, DXC (D3D12/Vulkan) cannot resolve instanced CBUFFER
//   variables (_TerrainHeightmapRecipSize, _Control0_TexelSize) →
//   garbage UVs → NaN displacement → terrain vanishes.
//   FXC (D3D11/editor) implicitly propagates instance context, masking the bug.
//
// v9 — SAND MODE RIM PUSH-UP:
//
//   When _SAND_MODE is active, the Domain shader samples the deformation map
//   gradient around each vertex. At the edges of footprints/trails (high gradient),
//   vertices are pushed UP instead of down, creating the characteristic "bourrelet"
//   — the sand rim that forms around real footprints as displaced grains pile up.
//
//   Rim push-up is gated by sand weight (blend weight of sand-enabled layers at
//   this position), so non-sand areas (ice, rock) don't get rims.
//
//   The hull constant and tessellation mask logic are unchanged from v8.
// ---------------------------------------------------------------------------------

// #define TESS_DIAG_UNIFORM
// #define TESS_DIAG_VIS_DEFORM

#ifndef MAX_TESSELLATION_FACTORS
#define MAX_TESSELLATION_FACTORS 64.0
#endif

#ifndef STAMP_DETECT_THRESHOLD
#define STAMP_DETECT_THRESHOLD 0.001
#endif

struct TessellationFactors
{
    float edge[3] : SV_TessFactor;
    float inside  : SV_InsideTessFactor;
};

#ifdef _TESSELLATION_DISPLACEMENT
TEXTURE2D(_TessellationFalloffRamp);
SAMPLER(sampler_TessellationFalloffRamp);
#endif

PackedVaryingsMeshToPS InterpolatePackedMesh(
    PackedVaryingsMeshToPS a,
    PackedVaryingsMeshToPS b,
    PackedVaryingsMeshToPS c,
    float3 bary)
{
    PackedVaryingsMeshToPS o = (PackedVaryingsMeshToPS)0;
    #ifdef VARYINGS_NEED_POSITION_WS
    o.interpolators0 = a.interpolators0 * bary.x + b.interpolators0 * bary.y + c.interpolators0 * bary.z;
    #endif
    #ifdef VARYINGS_NEED_TANGENT_TO_WORLD
    o.interpolators1 = a.interpolators1 * bary.x + b.interpolators1 * bary.y + c.interpolators1 * bary.z;
    o.interpolators2 = a.interpolators2 * bary.x + b.interpolators2 * bary.y + c.interpolators2 * bary.z;
    #endif
    #ifdef VARYINGS_NEED_TEXCOORD0
    o.interpolators3 = a.interpolators3 * bary.x + b.interpolators3 * bary.y + c.interpolators3 * bary.z;
    #endif
    #ifdef VARYINGS_NEED_TEXCOORD2
    o.interpolators4 = a.interpolators4 * bary.x + b.interpolators4 * bary.y + c.interpolators4 * bary.z;
    #endif
    #ifdef VARYINGS_NEED_COLOR
    o.interpolators5 = a.interpolators5 * bary.x + b.interpolators5 * bary.y + c.interpolators5 * bary.z;
    #endif
    return o;
}

float2 GetNormalizedTerrainUV(float2 rawUV)
{
    #ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
        return rawUV * _TerrainHeightmapRecipSize.zw;
    #else
        return rawUV;
    #endif
}

// ---------------------------------------------------------------------------------
// Sample tessellation mask — world-aligned toroidal UV (same space as the
// deformation map). Caller passes frac(absWorldXZ / _BufferWorldSize).
// ---------------------------------------------------------------------------------
#ifdef _TESSELLATION_DISPLACEMENT
float SampleTessMask(float2 toroidalUV)
{
    return SAMPLE_TEXTURE2D_LOD(_TessellationMask, sampler_TessellationMask, toroidalUV, 0).r;
}

bool FrustumCullPatch(float3 p0, float3 p1, float3 p2)
{
    float4 c0 = TransformWorldToHClip(p0);
    float4 c1 = TransformWorldToHClip(p1);
    float4 c2 = TransformWorldToHClip(p2);

    float m = 1.3;

    bool allBehind = (c0.w < 0) && (c1.w < 0) && (c2.w < 0);
    bool allLeft   = (c0.x < -c0.w * m) && (c1.x < -c1.w * m) && (c2.x < -c2.w * m);
    bool allRight  = (c0.x >  c0.w * m) && (c1.x >  c1.w * m) && (c2.x >  c2.w * m);
    bool allBottom = (c0.y < -c0.w * m) && (c1.y < -c1.w * m) && (c2.y < -c2.w * m);
    bool allTop    = (c0.y >  c0.w * m) && (c1.y >  c1.w * m) && (c2.y >  c2.w * m);

    return allBehind || allLeft || allRight || allBottom || allTop;
}
#endif

// ---------------------------------------------------------------------------------
// Sand mode: rim push-up via deformation gradient
//
// Samples the deformation map at 4 neighboring texels (Sobel-like).
// Returns positive height contribution at deformation edges.
// Masked to OUTER edges only: where gradient is high but deformation is low
// (outside the footprint center), creating the characteristic bourrelet.
// ---------------------------------------------------------------------------------
#if defined(_TESSELLATION_DISPLACEMENT) && defined(_SAND_MODE)
float ComputeSandRimPushUp(float2 deformUV, float deformCenter, float texelSize, float rimScale, float sandWeight)
{
    if (sandWeight < 0.001 || rimScale < 0.0001)
        return 0;

    // Sample 4 neighbors for gradient (the RT has wrapMode=Repeat, so edge wrapping is automatic)
    float dR = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, deformUV + float2(texelSize, 0), 0).r;
    float dL = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, deformUV - float2(texelSize, 0), 0).r;
    float dU = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, deformUV + float2(0, texelSize), 0).r;
    float dD = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, deformUV - float2(0, texelSize), 0).r;

    // Central difference gradient magnitude
    float2 grad = float2(dR - dL, dU - dD) * 0.5;
    float gradMag = length(grad);

    // OUTER rim mask: push up only OUTSIDE the footprint center.
    // Where deformCenter is high (inside footprint), rimMask → 0.
    // Where deformCenter is low  (outside footprint), rimMask → 1.
    // The transition zone (edge) has both high gradient AND medium rimMask → rim.
    float rimMask = saturate(1.0 - deformCenter * 5.0);

    // Smooth the rim profile for a natural sand mound look
    float rim = gradMag * rimMask * rimScale * sandWeight;

    return rim;
}
#endif

// ---------------------------------------------------------------------------------
// Wind displacement — Domain shader
//
// global noise × detail noise → remap [min, max] → vertex displacement.
// Period pulsation modulates the combined noise over time.
// ---------------------------------------------------------------------------------
#if defined(_TESSELLATION_DISPLACEMENT) && defined(_WIND_DISPLACEMENT)
float ComputeWindVertexDisplacement(float3 worldPos, float dispWeight)
{
    float range = _WindMaxValue - _WindMinValue;
    if (dispWeight < 0.001 || abs(range) < 0.0001)
        return 0;

    float time = _WindTime;

    float2 globalUV = worldPos.xz * _WindGlobalTile + _WindGlobalOffsetDir.xy * time;
    float2 detailUV = worldPos.xz * _WindDetailTile + _WindDetailOffsetDir.xy * time;

    float global = SAMPLE_TEXTURE2D_LOD(_WindGlobalMap, sampler_WindGlobalMap, globalUV, 0).r;
    float detail = SAMPLE_TEXTURE2D_LOD(_WindDetailMap, sampler_WindDetailMap, detailUV, 0).r;

    float combined = global * detail;

    // Period pulsation
    if (_WindPeriod > 0.0001)
    {
        float pulse = sin(time * _WindPeriod * 6.2831853) * 0.5 + 0.5;
        combined *= pulse;
    }

    return lerp(_WindMinValue, _WindMaxValue, combined) * dispWeight;
}
#endif

// ---------------------------------------------------------------------------------
// Hull constant
// ---------------------------------------------------------------------------------
TessellationFactors HullConstant(InputPatch<PackedVaryingsToPS, 3> input)
{
    TessellationFactors output;

    // Required for DXC (D3D12/Vulkan): without this, instanced CBUFFER variables
    // (_TerrainHeightmapRecipSize, _Control0_TexelSize) read garbage.
    UNITY_SETUP_INSTANCE_ID(input[0].vmesh);

#ifdef _TESSELLATION_DISPLACEMENT
    float maxDist = max(_TessellationDistanceFade, 10.0);

    #ifdef VARYINGS_NEED_POSITION_WS
        float3 posRWS0 = input[0].vmesh.interpolators0;
        float3 posRWS1 = input[1].vmesh.interpolators0;
        float3 posRWS2 = input[2].vmesh.interpolators0;

        // Back-face culling
        {
            float3 faceNormal = cross(posRWS1 - posRWS0, posRWS2 - posRWS0);
            float area = length(faceNormal);
            if (area > 1e-6)
            {
                float3 center = (posRWS0 + posRWS1 + posRWS2) * (1.0 / 3.0);
                float facing = dot(faceNormal / area, normalize(center));
                if (facing > _TessellationBackFaceCullEpsilon)
                {
                    output.edge[0] = output.edge[1] = output.edge[2] = output.inside = 0;
                    return output;
                }
            }
        }

        // Frustum culling
        if (FrustumCullPatch(posRWS0, posRWS1, posRWS2))
        {
            output.edge[0] = output.edge[1] = output.edge[2] = output.inside = 0;
            return output;
        }

        float d0 = length(posRWS0);
        float d1 = length(posRWS1);
        float d2 = length(posRWS2);

        float fade0 = SAMPLE_TEXTURE2D_LOD(_TessellationFalloffRamp, sampler_TessellationFalloffRamp, float2(saturate(d0 / maxDist), 0.5), 0).r;
        float fade1 = SAMPLE_TEXTURE2D_LOD(_TessellationFalloffRamp, sampler_TessellationFalloffRamp, float2(saturate(d1 / maxDist), 0.5), 0).r;
        float fade2 = SAMPLE_TEXTURE2D_LOD(_TessellationFalloffRamp, sampler_TessellationFalloffRamp, float2(saturate(d2 / maxDist), 0.5), 0).r;

        float fullTf0 = max(1.0, _TessellationFactor * fade0);
        float fullTf1 = max(1.0, _TessellationFactor * fade1);
        float fullTf2 = max(1.0, _TessellationFactor * fade2);

        #if !defined(TESS_DIAG_UNIFORM) && !defined(TESS_DIAG_VIS_DEFORM)
        {
            // World-aligned toroidal mask UVs — terrain-agnostic.
            // Edge UVs derive from per-vertex absolute world positions, so two
            // patches sharing an edge sample identical texels → no T-junction cracks.
            float3 wp0 = GetAbsolutePositionWS(posRWS0);
            float3 wp1 = GetAbsolutePositionWS(posRWS1);
            float3 wp2 = GetAbsolutePositionWS(posRWS2);

            float2 mUV0 = frac(wp0.xz / _BufferWorldSize);
            float2 mUV1 = frac(wp1.xz / _BufferWorldSize);
            float2 mUV2 = frac(wp2.xz / _BufferWorldSize);

            float mask0 = SampleTessMask(mUV0);
            float mask1 = SampleTessMask(mUV1);
            float mask2 = SampleTessMask(mUV2);

            float act0 = step(STAMP_DETECT_THRESHOLD, mask0);
            float act1 = step(STAMP_DETECT_THRESHOLD, mask1);
            float act2 = step(STAMP_DETECT_THRESHOLD, mask2);

            float etf0 = lerp(1.0, fullTf0, act0);
            float etf1 = lerp(1.0, fullTf1, act1);
            float etf2 = lerp(1.0, fullTf2, act2);

            output.edge[0] = min(0.5 * (etf1 + etf2), MAX_TESSELLATION_FACTORS);
            output.edge[1] = min(0.5 * (etf0 + etf2), MAX_TESSELLATION_FACTORS);
            output.edge[2] = min(0.5 * (etf0 + etf1), MAX_TESSELLATION_FACTORS);

            // Centroid: average WORLD positions then frac() — averaging wrapped
            // UVs across the toroidal seam would be wrong. Affects inside factor only.
            float3 centerWS = (wp0 + wp1 + wp2) * (1.0 / 3.0);
            float2 centroidUV = frac(centerWS.xz / _BufferWorldSize);
            float maskCentroid = SampleTessMask(centroidUV);
            float actCentroid = step(STAMP_DETECT_THRESHOLD, maskCentroid);
            float insideActivate = max(max(act0, act1), max(act2, actCentroid));
            float avgFullTf = (fullTf0 + fullTf1 + fullTf2) / 3.0;
            output.inside = min(lerp(1.0, avgFullTf, insideActivate), MAX_TESSELLATION_FACTORS);
        }
        #else
        {
            output.edge[0] = min(0.5 * (fullTf1 + fullTf2), MAX_TESSELLATION_FACTORS);
            output.edge[1] = min(0.5 * (fullTf0 + fullTf2), MAX_TESSELLATION_FACTORS);
            output.edge[2] = min(0.5 * (fullTf0 + fullTf1), MAX_TESSELLATION_FACTORS);
            output.inside  = min((fullTf0 + fullTf1 + fullTf2) / 3.0, MAX_TESSELLATION_FACTORS);
        }
        #endif
    #else
        output.edge[0] = min(_TessellationFactor, MAX_TESSELLATION_FACTORS);
        output.edge[1] = min(_TessellationFactor, MAX_TESSELLATION_FACTORS);
        output.edge[2] = min(_TessellationFactor, MAX_TESSELLATION_FACTORS);
        output.inside  = min(_TessellationFactor, MAX_TESSELLATION_FACTORS);
    #endif
#else
    output.edge[0] = 1.0;
    output.edge[1] = 1.0;
    output.edge[2] = 1.0;
    output.inside  = 1.0;
#endif

    return output;
}

[maxtessfactor(MAX_TESSELLATION_FACTORS)]
[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[patchconstantfunc("HullConstant")]
[outputcontrolpoints(3)]
PackedVaryingsToPS Hull(InputPatch<PackedVaryingsToPS, 3> input, uint id : SV_OutputControlPointID)
{
    return input[id];
}

[domain("tri")]
PackedVaryingsToPS Domain(TessellationFactors tessFactors, const OutputPatch<PackedVaryingsToPS, 3> input, float3 baryCoords : SV_DomainLocation)
{
    PackedVaryingsToPS output;
    output.vmesh = InterpolatePackedMesh(input[0].vmesh, input[1].vmesh, input[2].vmesh, baryCoords);
    UNITY_TRANSFER_INSTANCE_ID(input[0].vmesh, output.vmesh);

    // Required for DXC (D3D12/Vulkan): resolve instanced terrain CBUFFER
    UNITY_SETUP_INSTANCE_ID(output.vmesh);

#ifdef _TESSELLATION_DISPLACEMENT
    #ifdef VARYINGS_NEED_POSITION_WS
        float2 rawUV = output.vmesh.interpolators3.xy;
        float2 terrainUV = GetNormalizedTerrainUV(rawUV);

        // World-aligned toroidal deformation UV — terrain-agnostic.
        // Depends only on absolute world position + the global tile size,
        // never on a per-terrain origin/size, so it is correct on every
        // terrain that shares this material.
        float3 absWP = GetAbsolutePositionWS(output.vmesh.interpolators0);
        float2 deformUV = frac(absWP.xz / _BufferWorldSize);

        #ifdef TESS_DIAG_VIS_DEFORM
            float deformation = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, deformUV, 0).r;
            float height = -deformation * 2.0;
        #else
            TessellationSampleData tessData = SampleAllTessellationData(terrainUV);
            float height = tessData.height;

            float deformation = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, deformUV, 0).r;
            height -= deformation * _DeformationStrength;

            #ifdef _SAND_MODE
            if (_SandRimPushUp > 0.0001 && _SandDeformTexelSize > 0)
            {
                float rim = ComputeSandRimPushUp(
                    deformUV, deformation,
                    _SandDeformTexelSize, _SandRimPushUp, tessData.sandWeight);
                height += rim;
            }
            #endif

            #ifdef _WIND_DISPLACEMENT
            {
                float windHeight = ComputeWindVertexDisplacement(absWP, tessData.displacementWeight);
                height += windHeight;
            }
            #endif

            height = clamp(height, 0,
                _TessellationMaxDisplacement + 
            #ifdef _SAND_MODE
                _SandRimPushUp +
            #else
                0 +
            #endif
            #ifdef _WIND_DISPLACEMENT
                max(abs(_WindMinValue), abs(_WindMaxValue))
            #else
                0
            #endif
            );
        #endif

        float3 posRWS = output.vmesh.interpolators0;
        float distToCamera = length(posRWS);
        float maxDist = max(_TessellationDistanceFade, 10.0);
        float distanceFade = SAMPLE_TEXTURE2D_LOD(_TessellationFalloffRamp, sampler_TessellationFalloffRamp,
            float2(saturate(distToCamera / maxDist), 0.5), 0).r;
        height *= distanceFade;

        #ifdef VARYINGS_NEED_TANGENT_TO_WORLD
            float3 normalWS = normalize(output.vmesh.interpolators1.xyz);
        #else
            float3 normalWS = float3(0, 1, 0);
        #endif

        output.vmesh.interpolators0 += normalWS * height;
    #endif
#endif

    #ifdef VARYINGS_NEED_POSITION_WS
        output.vmesh.positionCS = TransformWorldToHClip(output.vmesh.interpolators0);
    #else
        output.vmesh.positionCS = input[0].vmesh.positionCS * baryCoords.x
                                + input[1].vmesh.positionCS * baryCoords.y
                                + input[2].vmesh.positionCS * baryCoords.z;
    #endif

    return output;
}
