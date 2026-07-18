// GrassBRGProperties.hlsl
// ---------------------------------------------------------------------------------
// PHASE 1c.2a — Our own material properties + DOTS-instancing setup, REPLACING HDRP's
// LitProperties.hlsl. Verified safe: Lit.hlsl itself dereferences none of the Lit material
// properties (they all lived in LitData.hlsl, which we already replace), so this minimal set
// is enough.
//
// Why our own file: to add a CUSTOM per-instance property (_GrassParams) we need our own
// MaterialPropertyMetadata DOTS block — we can't extend the one inside the package's
// LitProperties.hlsl. This is the foundation of the compute feed (1c.2b+): the compute writes
// per-blade data into the BRG instance buffer, the shader reads it via this DOTS property.
//
// Pattern mirrors UnlitProperties.hlsl / LitProperties.hlsl exactly (cbuffer + DOTS block +
// SetupDOTS* + #define remap). UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() is auto-called from
// UNITY_SETUP_INSTANCE_ID (incl. in the vertex stage, BEFORE ApplyMeshModification).
// ---------------------------------------------------------------------------------

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float  _Metallic;
    float  _Smoothness;
    float4 _DoubleSidedConstants;
    float4 _DiffusionProfileAsset;
    float  _DiffusionProfileHash;

    // Per-blade data: x = height (m), y = bend, z = tilt, w = wind phase (0..1).
    // Overridden per instance by the BRG instance buffer (CPU in 1c.2a, compute in 1c.2b+).
    // The cbuffer value is only the non-instanced default (material preview, which we don't use).
    float4 _GrassParams;
    // Per-blade data 2 (multi-species): x = width (m), y = species kind, z = species index.
    float4 _GrassParams2;
    // Per-blade transform (compression B): xyz = ABSOLUTE world position, w = yaw. Replaces the
    // per-instance unity_ObjectToWorld (which is now a shared identity); the vertex hook rebuilds the
    // transform from this and outputs camera-relative world positions + world normals.
    float4 _GrassXform;

    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// Editor scene-selection/picking globals (referenced by some shared paths).
int _ObjectId;
int _PassValue;

#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _DiffusionProfileHash)
    UNITY_DOTS_INSTANCED_PROP(float4, _GrassParams)
    UNITY_DOTS_INSTANCED_PROP(float4, _GrassParams2)
    UNITY_DOTS_INSTANCED_PROP(float4, _GrassXform)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

static float4 unity_DOTS_Sampled_BaseColor;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_DiffusionProfileHash;
static float4 unity_DOTS_Sampled_GrassParams;
static float4 unity_DOTS_Sampled_GrassParams2;
static float4 unity_DOTS_Sampled_GrassXform;

void SetupDOTSGrassPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor   = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_Metallic    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_Smoothness  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_DiffusionProfileHash = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DiffusionProfileHash);
    unity_DOTS_Sampled_GrassParams = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _GrassParams);
    unity_DOTS_Sampled_GrassParams2 = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _GrassParams2);
    unity_DOTS_Sampled_GrassXform  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _GrassXform);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSGrassPropertyCaches()

#define _BaseColor   unity_DOTS_Sampled_BaseColor
#define _Metallic    unity_DOTS_Sampled_Metallic
#define _Smoothness  unity_DOTS_Sampled_Smoothness
#define _DiffusionProfileHash unity_DOTS_Sampled_DiffusionProfileHash
#define _GrassParams unity_DOTS_Sampled_GrassParams
#define _GrassParams2 unity_DOTS_Sampled_GrassParams2
#define _GrassXform  unity_DOTS_Sampled_GrassXform

#endif
