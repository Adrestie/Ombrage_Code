// ============================================================================
//  OceanTessellation.hlsl
//  Hull + Domain shader for distance-based ocean tessellation.
//  Displacement from FFT maps is applied in the Domain shader.
//
//  Include AFTER OceanInput.hlsl in each pass.
//
//  Lessons from TerrainLitCustom:
//    - Edge factors: vertex samples only (no centroid) → prevents T-junction cracks
//    - Centroid only affects 'inside' factor
//    - UNITY_SETUP_INSTANCE_ID in HullConstant + Domain for DXC (D3D12/Vulkan)
//    - Hull/Domain must always be defined (no #ifdef around #pragma hull/domain)
// ============================================================================

#ifndef OCEAN_TESSELLATION_INCLUDED
#define OCEAN_TESSELLATION_INCLUDED

// ── Tessellation parameters (pushed by material or globals) ─────────────────

float _TessMaxFactor;
float _TessMaxDistance;

// ── Structures ──────────────────────────────────────────────────────────────

// Vertex → Hull (control point)
struct TessControlPoint
{
    float3 positionRWS : TEXCOORD0;   // camera-relative world space (undisplaced)
    float3 positionAWS : TEXCOORD1;   // absolute world space (for stable UVs)
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Patch constant data
struct TessPatchConstant
{
    float edgeFactor[3]  : SV_TessFactor;
    float insideFactor   : SV_InsideTessFactor;
};

// ── Distance-based tessellation factor ──────────────────────────────────────

float DistanceFactor(float3 posRWS)
{
    // posRWS is camera-relative, so length = distance to camera
    float dist = length(posRWS);
    float factor = 1.0 - saturate((dist - 5.0) / _TessMaxDistance);
    return max(1.0, factor * _TessMaxFactor);
}

// Edge factor from two vertices (vertex samples only — no centroid!)
float EdgeFactor(float3 posRWS_A, float3 posRWS_B)
{
    float fA = DistanceFactor(posRWS_A);
    float fB = DistanceFactor(posRWS_B);
    return max(fA, fB);
}

// ── Vertex shader (pass-through, no displacement) ───────────────────────────

TessControlPoint OceanVertTess(Attributes IN)
{
    TessControlPoint OUT;
    UNITY_SETUP_INSTANCE_ID(IN);
    UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

    // Camera-relative world position (no displacement yet — applied in Domain)
    OUT.positionRWS = TransformObjectToWorld(IN.positionOS.xyz);

    // Absolute world position for stable UVs
    OUT.positionAWS = GetAbsolutePositionWS(OUT.positionRWS);

    return OUT;
}

// ── Hull constant function ──────────────────────────────────────────────────

TessPatchConstant HullConstant(
    InputPatch<TessControlPoint, 3> patch,
    uint patchID : SV_PrimitiveID)
{
    // Required for DXC instanced CBUFFER access
    UNITY_SETUP_INSTANCE_ID(patch[0]);

    TessPatchConstant OUT;

    // Edge factors: use vertex positions (not centroid) → no T-junction cracks
    OUT.edgeFactor[0] = EdgeFactor(patch[1].positionRWS, patch[2].positionRWS);
    OUT.edgeFactor[1] = EdgeFactor(patch[2].positionRWS, patch[0].positionRWS);
    OUT.edgeFactor[2] = EdgeFactor(patch[0].positionRWS, patch[1].positionRWS);

    // Inside factor: average of edges (centroid would be acceptable here)
    OUT.insideFactor = (OUT.edgeFactor[0] + OUT.edgeFactor[1] + OUT.edgeFactor[2]) / 3.0;

    return OUT;
}

// ── Hull shader ─────────────────────────────────────────────────────────────

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[outputcontrolpoints(3)]
[patchconstantfunc("HullConstant")]
[maxtessfactor(64.0)]
TessControlPoint OceanHull(
    InputPatch<TessControlPoint, 3> patch,
    uint id : SV_OutputControlPointID)
{
    return patch[id];
}

// ── Domain shader (ForwardOnly variant — full output) ───────────────────────

[domain("tri")]
Varyings OceanDomain(
    TessPatchConstant patchData,
    float3 bary : SV_DomainLocation,
    OutputPatch<TessControlPoint, 3> patch)
{
    // Required for DXC
    UNITY_SETUP_INSTANCE_ID(patch[0]);

    Varyings OUT;
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

    // Barycentric interpolation of control points
    float3 posRWS = bary.x * patch[0].positionRWS
                  + bary.y * patch[1].positionRWS
                  + bary.z * patch[2].positionRWS;

    float3 posAWS = bary.x * patch[0].positionAWS
                  + bary.y * patch[1].positionAWS
                  + bary.z * patch[2].positionAWS;

    // Ocean UV from absolute world position (stable)
    float2 uv = OceanUV(posAWS);
    OUT.oceanUV = uv;

    // Apply FFT displacement (all cascades)
    float3 disp = SampleOceanDisplacement(posAWS);

    // Attenuate waves near shore/meshes using signed distance from shore map
    float shoreAtten = 1.0;
    if (_OceanShoreMapSize > 0.0)
    {
        float2 shoreUV = (posAWS.xz - _OceanShoreMapCenter.xy) / _OceanShoreMapSize + 0.5;
        if (all(shoreUV > 0.01) && all(shoreUV < 0.99))
        {
            float4 shoreData = SampleOceanShoreMap(posAWS);
            if (shoreData.b > -9000.0)
            {
                float shoreDist = shoreData.a;
                shoreAtten = smoothstep(0.0, _WaveShoreAttenuationDist, shoreDist);
                shoreAtten = lerp(_WaveShoreMinAmplitude, 1.0, shoreAtten);
            }
        }
    }
    disp *= shoreAtten;

    posRWS += disp;

    // Wake displacement: trail paints positive values, subtract for depression
    float wakeValue = SampleWakeDisplacement(posAWS);
    posRWS.y -= wakeValue * _OceanWakeDisplacementScale;

    OUT.positionRWS = posRWS;
    OUT.positionCS  = TransformWorldToHClip(posRWS);
    OUT.normalWS    = SampleOceanNormalLOD(posAWS);
    OUT.posAWS      = posAWS;
    OUT.waveHeight  = disp.y;
    OUT.choppiness  = length(float2(disp.x, disp.z));

    return OUT;
}

// ── Domain shader (DepthForwardOnly variant — minimal output) ───────────────

[domain("tri")]
VaryingsDepth OceanDomainDepth(
    TessPatchConstant patchData,
    float3 bary : SV_DomainLocation,
    OutputPatch<TessControlPoint, 3> patch)
{
    UNITY_SETUP_INSTANCE_ID(patch[0]);

    VaryingsDepth OUT;
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

    float3 posRWS = bary.x * patch[0].positionRWS
                  + bary.y * patch[1].positionRWS
                  + bary.z * patch[2].positionRWS;

    float3 posAWS = bary.x * patch[0].positionAWS
                  + bary.y * patch[1].positionAWS
                  + bary.z * patch[2].positionAWS;

    float3 disp = SampleOceanDisplacement(posAWS);

    float shoreAttenD = 1.0;
    if (_OceanShoreMapSize > 0.0)
    {
        float2 shoreUVD = (posAWS.xz - _OceanShoreMapCenter.xy) / _OceanShoreMapSize + 0.5;
        if (all(shoreUVD > 0.01) && all(shoreUVD < 0.99))
        {
            float4 shoreDataD = SampleOceanShoreMap(posAWS);
            if (shoreDataD.b > -9000.0)
            {
                float shoreDistD = shoreDataD.a;
                shoreAttenD = smoothstep(0.0, _WaveShoreAttenuationDist, shoreDistD);
                shoreAttenD = lerp(_WaveShoreMinAmplitude, 1.0, shoreAttenD);
            }
        }
    }
    disp *= shoreAttenD;

    posRWS += disp;

    // Same wake displacement as ForwardOnly — MUST match
    float wakeValue = SampleWakeDisplacement(posAWS);
    posRWS.y -= wakeValue * _OceanWakeDisplacementScale;

    OUT.positionCS = TransformWorldToHClip(posRWS);

    return OUT;
}

#endif // OCEAN_TESSELLATION_INCLUDED
