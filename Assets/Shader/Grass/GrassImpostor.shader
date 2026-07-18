// GrassImpostor.shader
// ---------------------------------------------------------------------------------
// Horizon impostor backbone (Step 2) — a DEDICATED HDRP shader, SEPARATE from the blade
// shader (GrassBRG.shader). Structurally an exact clone of the blade backbone (same 4 passes,
// stencils, defines, include order, reused GrassBRGProperties.hlsl DOTS block) so it inherits
// the proven deferred/shadow/depth/MV plumbing — only TWO includes differ:
//   GrassBRGVertex.hlsl  -> GrassImpostorVertex.hlsl  (camera-facing billboard, not a Bézier blade)
//   GrassBRGSurface.hlsl -> GrassImpostorSurface.hlsl (sample baked card + alpha cutout, not per-vert colour)
//
// Per-instance data is the SAME layout as the blades (reused properties): _GrassXform.xyz = card
// root world pos, _GrassParams.x = height, _GrassParams2.x = width, _BaseColor = tint. So the
// impostor's own BRG (Step 3) fills the same instance buffer shape; the Step 2 test draws set them
// per-card via a MaterialPropertyBlock.
// ---------------------------------------------------------------------------------

Shader "Custom/HDRP/GrassImpostor"
{
    Properties
    {
        [MainColor] _BaseColor("BaseColor", Color) = (0.30, 0.45, 0.10, 1)
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.25
        _AlphaCutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        // Kept so the Lit framework paths that reference them resolve (cf. GrassBRG.shader).
        [HideInInspector] _DoubleSidedConstants("_DoubleSidedConstants", Vector) = (1, 1, -1, 0)
        [HideInInspector] _DiffusionProfileAsset("Diffusion Profile Asset", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DiffusionProfileHash("Diffusion Profile Hash", Float) = 0

        // Per-instance (BRG instance buffer / Step 2 MPB). Same layout as the blades — declared here
        // only because every UnityPerMaterial member must also appear in Properties{} for SRP-Batcher/BRG.
        [HideInInspector] _GrassParams("Grass Params (per-instance)", Vector) = (0,0,0,0)        // x = height m
        [HideInInspector] _GrassParams2("Grass Params 2 (per-instance)", Vector) = (0.5,0,0,0)   // x = width m
        [HideInInspector] _GrassXform("Grass Xform (per-instance)", Vector) = (0,0,0,0)           // xyz = root pos, w = yaw (unused)

        // NOTE: card texture/cutoff/normal-up/size are GLOBAL uniforms (Shader.SetGlobal*), NOT material
        // properties — kept out of Properties{} for SRP-Batcher/BRG, exactly like the blade's global tuning.
    }

    HLSLINCLUDE

    #pragma target 4.5

    //-------------------------------------------------------------------------------------
    // Material configuration (identical to GrassBRG.shader — keep the deferred path proven).
    //-------------------------------------------------------------------------------------
    #define _DOUBLESIDED_ON
    // Alpha-tested cutout: makes HDRP interpolate the texture UV (and need it) in the DEPTH/SHADOW passes
    // too, so the silhouette clip in the surface runs consistently across ALL passes — not just GBuffer.
    #define _ALPHATEST_ON
    #define _DEFERRED_CAPABLE_MATERIAL
    #define _MATERIAL_FEATURE_TRANSMISSION
    // Billboard built in the vertex hook ApplyMeshModification() (GrassImpostorVertex.hlsl).
    #define HAVE_MESH_MODIFICATION
    #define PREFER_HALF 0

    //-------------------------------------------------------------------------------------
    // Includes (mirror HDRP/Lit HLSLINCLUDE order)
    //-------------------------------------------------------------------------------------
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

    // Impostor material properties + DOTS-instancing caches (blade layout + _AlphaCutoff for the cutout).
    #include "Assets/Shader/Grass/GrassImpostorProperties.hlsl"

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
                WriteMask 3
                Ref 3
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

            #define SHADERPASS SHADERPASS_GBUFFER
            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "Assets/Shader/Grass/GrassImpostorVertex.hlsl"
            #include "Assets/Shader/Grass/GrassImpostorSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassGBuffer.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // =====================================================================
        // ShadowCaster (real impostor shadows)
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
            #include "Assets/Shader/Grass/GrassImpostorVertex.hlsl"
            #include "Assets/Shader/Grass/GrassImpostorSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // =====================================================================
        // DepthOnly (prepass — fog/SSAO/contact shadows see the impostors)
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
                WriteMask 8
                Ref 0
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma multi_compile _ WRITE_DECAL_BUFFER WRITE_RENDERING_LAYER

            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "Assets/Shader/Grass/GrassImpostorVertex.hlsl"
            #include "Assets/Shader/Grass/GrassImpostorSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // =====================================================================
        // MotionVectors (TAA correctness)
        // =====================================================================
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Cull Off
            ZWrite On

            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma multi_compile _ WRITE_DECAL_BUFFER_AND_RENDERING_LAYER

            #ifdef WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #define WRITE_DECAL_BUFFER
            #endif

            #define SHADERPASS SHADERPASS_MOTION_VECTORS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitMotionVectorPass.hlsl"
            #include "Assets/Shader/Grass/GrassImpostorVertex.hlsl"
            #include "Assets/Shader/Grass/GrassImpostorSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassMotionVectors.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
