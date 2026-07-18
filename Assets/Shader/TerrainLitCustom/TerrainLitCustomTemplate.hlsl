// TerrainLitCustomTemplate.hlsl
// Fork of TerrainLitTemplate.hlsl — points to custom Data and SurfaceData files.
// ---------------------------------------------------------------------------------

#define HAVE_MESH_MODIFICATION

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

#if SHADERPASS == SHADERPASS_GBUFFER && !defined(DEBUG_DISPLAY)
    #define SHADERPASS_GBUFFER_BYPASS_ALPHA_TEST
#endif

#if SHADERPASS == SHADERPASS_FORWARD && !defined(_SURFACE_TYPE_TRANSPARENT) && !defined(DEBUG_DISPLAY)
    #define SHADERPASS_FORWARD_BYPASS_ALPHA_TEST
#endif

#if defined(_ALPHATEST_ON)
    #define ATTRIBUTES_NEED_TEXCOORD0
    #define VARYINGS_NEED_TEXCOORD0
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#ifdef DEBUG_DISPLAY
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
#endif
#ifdef SCENESELECTIONPASS
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
#elif SHADERPASS == SHADERPASS_LIGHT_TRANSPORT
    #define SCENEPICKINGPASS
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
#endif
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"

#if SHADERPASS == SHADERPASS_FORWARD
    #define HAS_LIGHTLOOP
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
#else
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
#endif

#if defined(WRITE_DECAL_BUFFER) || (defined(WRITE_RENDERING_LAYER) && !defined(_DISABLE_DECALS))
#define OUTPUT_DECAL_BUFER
#endif

#if SHADERPASS != SHADERPASS_DEPTH_ONLY || defined(WRITE_NORMAL_BUFFER) || defined(OUTPUT_DECAL_BUFER)
    #define ATTRIBUTES_NEED_NORMAL
    #define ATTRIBUTES_NEED_TEXCOORD0
    #define ATTRIBUTES_NEED_TANGENT
    #if SHADERPASS == SHADERPASS_LIGHT_TRANSPORT
        #define ATTRIBUTES_NEED_TEXCOORD1
        #define ATTRIBUTES_NEED_TEXCOORD2
        #ifdef EDITOR_VISUALIZATION
        #define ATTRIBUTES_NEED_TEXCOORD3
        #define VARYINGS_NEED_TEXCOORD0
        #define VARYINGS_NEED_TEXCOORD1
        #define VARYINGS_NEED_TEXCOORD2
        #define VARYINGS_NEED_TEXCOORD3
        #endif
    #endif
    #define VARYINGS_NEED_POSITION_WS
    #define VARYINGS_NEED_TANGENT_TO_WORLD
    #define VARYINGS_NEED_TEXCOORD0
#endif

#if defined(UNITY_INSTANCING_ENABLED) && defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL)
    #define ENABLE_TERRAIN_PERPIXEL_NORMAL
#endif

#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
    #undef ATTRIBUTES_NEED_NORMAL
    #undef ATTRIBUTES_NEED_TANGENT
    #undef VARYINGS_NEED_TANGENT_TO_WORLD
#endif

// ---- CUSTOM: Tessellation setup ----
// We do NOT define TESSELLATION_ON here. Instead we write custom Hull/Domain
// in TerrainLitCustomHullDomain.hlsl using PackedVaryingsToPS directly.
// This avoids dependency on TessellationShare.hlsl and PackedVaryingsToDS.
#ifdef _TESSELLATION_DISPLACEMENT
    // Force position, normal, tangent, texcoord into varyings for tessellation
    #ifndef ATTRIBUTES_NEED_NORMAL
        #define ATTRIBUTES_NEED_NORMAL
    #endif
    #ifndef ATTRIBUTES_NEED_TANGENT
        #define ATTRIBUTES_NEED_TANGENT
    #endif
    #ifndef VARYINGS_NEED_TANGENT_TO_WORLD
        #define VARYINGS_NEED_TANGENT_TO_WORLD
    #endif
    #ifndef ATTRIBUTES_NEED_TEXCOORD0
        #define ATTRIBUTES_NEED_TEXCOORD0
    #endif
    #ifndef VARYINGS_NEED_TEXCOORD0
        #define VARYINGS_NEED_TEXCOORD0
    #endif
    #ifndef VARYINGS_NEED_POSITION_WS
        #define VARYINGS_NEED_POSITION_WS
    #endif
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"

// ---- CUSTOM: use our own Data file instead of the built-in one ----
#include "TerrainLitCustomData.hlsl"

#if SHADERPASS == SHADERPASS_GBUFFER
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassGBuffer.hlsl"
#elif SHADERPASS == SHADERPASS_LIGHT_TRANSPORT
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassLightTransport.hlsl"
#elif SHADERPASS == SHADERPASS_SHADOWS || SHADERPASS == SHADERPASS_DEPTH_ONLY
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
#elif SHADERPASS == SHADERPASS_FORWARD
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
#endif
