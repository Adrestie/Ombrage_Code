// GrassBRG.shader
// ---------------------------------------------------------------------------------
// PHASE 1b — Custom HDRP shader for the BRG grass backbone.
//
// Goal: replace the stock HDRP/Lit material used in Phase 1a with OUR OWN shader,
// rendered through the BatchRendererGroup, that participates in every pass the grass
// needs: GBuffer (deferred lighting), DepthOnly, ShadowCaster, MotionVectors —
// and consumes a per-instance DOTS property (_BaseColor) to de-risk the Phase 1c feed.
//
// Architecture (lowest-risk): we reuse the ENTIRE HDRP Lit framework — properties,
// DOTS-instancing caches (LitProperties.hlsl), pass interpolators (Lit*Pass.hlsl) and
// the pass drivers (ShaderPass*.hlsl) — and replace ONLY LitData.hlsl with our own
// GrassBRGSurface.hlsl (the single hand-authored file: GetSurfaceAndBuiltinData).
//
// This is structurally a stripped HDRP/Lit (non-layered, opaque, deferred-capable,
// double-sided, no displacement/tessellation/raytracing). Phase 1c adds the vertex
// hook (Bézier blade) and extends the surface (translucency, AO, rounded normals).
// ---------------------------------------------------------------------------------

Shader "Custom/HDRP/GrassBRG"
{
    Properties
    {
        [MainColor] _BaseColor("BaseColor", Color) = (0.30, 0.45, 0.10, 1)
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.25

        // Kept so the Lit framework code paths that reference them resolve (we never call
        // GetDoubleSidedConstants(), but the property must exist for LitProperties.hlsl).
        [HideInInspector] _DoubleSidedConstants("_DoubleSidedConstants", Vector) = (1, 1, -1, 0)

        // Diffusion profile (unused in 1b, wired in 1c for translucency).
        [HideInInspector] _DiffusionProfileAsset("Diffusion Profile Asset", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DiffusionProfileHash("Diffusion Profile Hash", Float) = 0

        // Per-instance, fed by the BRG instance buffer (x=height, y=bend, z=tilt, w=phase).
        // Declared here ONLY because SRP-Batcher/BRG requires every UnityPerMaterial member to
        // also appear in Properties{}.
        [HideInInspector] _GrassParams("Grass Params (per-instance)", Vector) = (0,0,0,0)
        // Per-instance, fed by the BRG instance buffer (x=width m, y=species kind, z=species index).
        [HideInInspector] _GrassParams2("Grass Params 2 (per-instance)", Vector) = (0.05,0,0,0)
        // Per-instance transform (xyz=abs world pos, w=yaw) — replaces per-instance unity_ObjectToWorld.
        [HideInInspector] _GrassXform("Grass Xform (per-instance)", Vector) = (0,0,0,0)

        // NOTE: blade shape params (_GrassBladeHeight/Width/Bend/Tilt/HeightRandom) are GLOBAL
        // shader uniforms set via Shader.SetGlobalFloat (see GrassBRGTest) — NOT material
        // properties. Keeping them out of Properties{} is required for SRP-Batcher/BRG.
    }

    HLSLINCLUDE

    #pragma target 4.5

    //-------------------------------------------------------------------------------------
    // Material configuration
    //-------------------------------------------------------------------------------------
    // Double-sided: gives us VARYINGS_NEED_CULLFACE -> input.isFrontFace (normal flip).
    #define _DOUBLESIDED_ON
    // Opaque -> eligible for the deferred GBuffer path.
    #define _DEFERRED_CAPABLE_MATERIAL

    // Translucent grass: compile the transmission GBuffer/lighting path (Phase 1c.4b).
    #define _MATERIAL_FEATURE_TRANSMISSION

    // Phase 1c: procedural Bézier blade built in the vertex hook ApplyMeshModification()
    // (defined in GrassBRGVertex.hlsl). VertMesh + the MotionVectors pass call it in every
    // pass; the MV pass replays it with _LastTimeParameters for correct object motion.
    #define HAVE_MESH_MODIFICATION

    // NOTE: still NOT defined (would change the contract):
    //   HAVE_VERTEX_MODIFICATION   -> a separate post-transform hook we don't use
    //   HAVE_RECURSIVE_RENDERING   -> raytracing only

    // Lit advises disabling half precision to avoid GBuffer banding.
    #define PREFER_HALF 0

    //-------------------------------------------------------------------------------------
    // Includes (mirror HDRP/Lit HLSLINCLUDE order)
    //-------------------------------------------------------------------------------------
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

    // Our OWN material properties + DOTS-instancing caches (replaces LitProperties.hlsl).
    // Declares the custom per-instance _GrassParams property fed through the BRG instance
    // buffer, and redefines UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES (auto-called from
    // UNITY_SETUP_INSTANCE_ID, including in the vertex stage before ApplyMeshModification).
    #include "Assets/Shader/Grass/GrassBRGProperties.hlsl"

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDLitShader" }

        // =====================================================================
        // GBuffer (deferred opaque lighting)
        // =====================================================================
        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "GBuffer" }

            Cull Off
            ZTest LEqual
            ZWrite On

            Stencil
            {
                WriteMask 3   // RequiresDeferredLighting | SubsurfaceScattering
                Ref 3         // RequiresDeferredLighting | SubsurfaceScattering (SSS/transmission pass)
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal
            #pragma editor_sync_compilation

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment _ RENDERING_LAYERS

            // Stripped vs HDRP/Lit (procedural BRG grass, no LODGroup, no lightmaps, and our
            // surface never samples the DBuffer): LOD_FADE_CROSSFADE, LIGHTMAP_* /
            // DIRLIGHTMAP / DYNAMICLIGHTMAP / USE_LEGACY_LIGHTMAPS, SHADOWS_SHADOWMASK,
            // DECALS_* / DECAL_SURFACE_GRADIENT.

            #define SHADERPASS SHADERPASS_GBUFFER
            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "Assets/Shader/Grass/GrassBRGVertex.hlsl"
            #include "Assets/Shader/Grass/GrassBRGSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassGBuffer.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // NOTE: the opaque "Forward" pass (Route B, Forward+MSAA experiment) was REMOVED —
        // the grass is deferred-only again (GBuffer). Restore from .backups/09_route_b_forward
        // if forward rendering is ever needed back.

        // =====================================================================
        // ShadowCaster (the whole point of the BRG backbone — real grass shadows)
        // =====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "Assets/Shader/Grass/GrassBRGVertex.hlsl"
            #include "Assets/Shader/Grass/GrassBRGSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // =====================================================================
        // DepthOnly (prepass — fog/SSAO/contact shadows see the grass)
        // =====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask 0

            Stencil
            {
                WriteMask 8   // TraceReflectionRay
                Ref 0
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            // Deferred-only: the prepass never writes the normal buffer (forward materials
            // only) and MSAA is impossible -> WRITE_NORMAL_BUFFER / WRITE_MSAA_DEPTH stripped.
            #pragma multi_compile _ WRITE_DECAL_BUFFER WRITE_RENDERING_LAYER

            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "Assets/Shader/Grass/GrassBRGVertex.hlsl"
            #include "Assets/Shader/Grass/GrassBRGSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // =====================================================================
        // MotionVectors (TAA correctness — no smearing of static grass in 1b;
        // object motion from wind animation is validated in 1c)
        // =====================================================================
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Cull Off
            ZWrite On

            Stencil
            {
                WriteMask 32  // ObjectMotionVector
                Ref 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            // Deferred-only: WRITE_NORMAL_BUFFER / WRITE_MSAA_DEPTH stripped (cf. DepthOnly).
            #pragma multi_compile _ WRITE_DECAL_BUFFER_AND_RENDERING_LAYER

            #ifdef WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #define WRITE_DECAL_BUFFER
            #endif

            #define SHADERPASS SHADERPASS_MOTION_VECTORS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitMotionVectorPass.hlsl"
            #include "Assets/Shader/Grass/GrassBRGVertex.hlsl"
            #include "Assets/Shader/Grass/GrassBRGSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassMotionVectors.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
