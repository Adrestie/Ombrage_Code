// GrassImpostorProperties.hlsl
// ---------------------------------------------------------------------------------
// Impostor material properties + DOTS-instancing setup, REPLACING HDRP's LitProperties.hlsl.
// A near-verbatim copy of GrassBRGProperties.hlsl (same per-instance layout so the impostor's
// BRG fills the same instance-buffer shape) with ONE addition: _AlphaCutoff — needed because the
// impostor shader defines _ALPHATEST_ON (so HDRP interpolates the texture UV in the depth/shadow
// passes, where the silhouette cutout must also run). _AlphaCutoff is per-MATERIAL (not per-
// instance), so it lives in the cbuffer only, not the DOTS block.
// ---------------------------------------------------------------------------------

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float  _Metallic;
    float  _Smoothness;
    float  _AlphaCutoff;            // silhouette clip threshold (impostor; _ALPHATEST_ON path)
    float4 _DoubleSidedConstants;
    float4 _DiffusionProfileAsset;
    float  _DiffusionProfileHash;

    // Per-instance (same layout as the blades): x = height (m) [_GrassParams], x = width (m) [_GrassParams2],
    // xyz = ABSOLUTE root world pos + w = yaw [_GrassXform]. unity_ObjectToWorld is a shared identity.
    float4 _GrassParams;
    float4 _GrassParams2;
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

void SetupDOTSGrassImpostorPropertyCaches()
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
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSGrassImpostorPropertyCaches()

#define _BaseColor   unity_DOTS_Sampled_BaseColor
#define _Metallic    unity_DOTS_Sampled_Metallic
#define _Smoothness  unity_DOTS_Sampled_Smoothness
#define _DiffusionProfileHash unity_DOTS_Sampled_DiffusionProfileHash
#define _GrassParams unity_DOTS_Sampled_GrassParams
#define _GrassParams2 unity_DOTS_Sampled_GrassParams2
#define _GrassXform  unity_DOTS_Sampled_GrassXform

#endif
