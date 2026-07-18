Shader "Custom/HDRP/TerrainLitCustom"
{
    Properties
    {
        // ---------------------------------------------------------------------------------
        // Stock TerrainLit properties
        // ---------------------------------------------------------------------------------
        [HideInInspector] [ToggleUI] _EnableHeightBlend("EnableHeightBlend", Float) = 0.0
        _HeightTransition("Height Transition", Range(0, 1.0)) = 0.0
        [HideInInspector] [Enum(Off, 0, From Ambient Occlusion, 1)] _SpecularOcclusionMode("Specular Occlusion Mode", Int) = 1

        // Stencil state — Forward
        [HideInInspector] _StencilRef("_StencilRef", Int) = 0
        [HideInInspector] _StencilWriteMask("_StencilWriteMask", Int) = 3
        // GBuffer
        [HideInInspector] _StencilRefGBuffer("_StencilRefGBuffer", Int) = 2
        [HideInInspector] _StencilWriteMaskGBuffer("_StencilWriteMaskGBuffer", Int) = 3
        // Depth prepass
        [HideInInspector] _StencilRefDepth("_StencilRefDepth", Int) = 0
        [HideInInspector] _StencilWriteMaskDepth("_StencilWriteMaskDepth", Int) = 8

        // Blending state
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0
        [HideInInspector][ToggleUI] _TransparentZWrite("_TransparentZWrite", Float) = 0.0
        [HideInInspector] _CullMode("__cullmode", Float) = 2.0
        [HideInInspector] _ZTestDepthEqualForOpaque("_ZTestDepthEqualForOpaque", Int) = 4
        [HideInInspector] _ZTestGBuffer("_ZTestGBuffer", Int) = 4

        [ToggleUI] _EnableInstancedPerPixelNormal("Instanced per pixel normal", Float) = 1.0

        [HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}

        [HideInInspector] _EmissionColor("Color", Color) = (1, 1, 1)

        [HideInInspector] _MainTex("Albedo", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1,1,1,1)

        [HideInInspector] [ToggleUI] _SupportDecals("Support Decals", Float) = 1.0
        [HideInInspector] [ToggleUI] _ReceivesSSR("Receives SSR", Float) = 1.0
        [HideInInspector] [ToggleUI] _AddPrecomputedVelocity("AddPrecomputedVelocity", Float) = 0.0

        // ---------------------------------------------------------------------------------
        // CUSTOM: Parallax Occlusion Mapping — global controls
        // ---------------------------------------------------------------------------------
        [Header(Parallax Occlusion Mapping)]
        [ToggleUI] _EnablePOM("Enable Parallax Occlusion Mapping", Float) = 0.0
        _POMDistanceFade("POM Distance Fade (meters)", Float) = 50.0

        // ---------------------------------------------------------------------------------
        // CUSTOM: Tessellation / Displacement — global controls
        // ---------------------------------------------------------------------------------
        [Header(Tessellation Displacement)]
        [ToggleUI] _EnableDisplacement("Enable Tessellation Displacement", Float) = 0.0
        _TessellationFactor("Tessellation Factor", Range(1, 64)) = 15.0
        [HideInInspector] _TessellationFalloffRamp("_", 2D) = "white" {}
        _TessellationBackFaceCullEpsilon("Back Face Cull Epsilon", Range(-1, 0)) = -0.25
        _TessellationMaxDisplacement("Max Displacement (m)", Float) = 1.0
        _TessellationDistanceFade("Tessellation Distance Fade (meters)", Float) = 80.0

        // ---------------------------------------------------------------------------------
        // CUSTOM: Deformation map for runtime footprints
        // ---------------------------------------------------------------------------------
        [Header(Runtime Deformation)]
        _DeformationMap("Deformation Map (R16 RenderTexture)", 2D) = "black" {}
        _DeformationStrength("Deformation Strength", Range(0, 2)) = 1.0
        [HideInInspector] _BufferWorldSize("_", Float) = 40
        [HideInInspector] _TessellationMask("_", 2D) = "black" {}

        // ---------------------------------------------------------------------------------
        // CUSTOM: Per-layer POM & Displacement parameters
        // Set at runtime by TerrainLitCustomSetup.cs via material.SetFloat.
        // Must be in Properties block so the SRP Batcher includes them in
        // UnityPerMaterial CBUFFER (Shader.SetGlobalFloat writes to _Globals
        // which is a DIFFERENT buffer — the shader never reads it for these).
        // ---------------------------------------------------------------------------------
        [HideInInspector] _EnablePOMLayer0("_", Float) = 0
        [HideInInspector] _EnablePOMLayer1("_", Float) = 0
        [HideInInspector] _EnablePOMLayer2("_", Float) = 0
        [HideInInspector] _EnablePOMLayer3("_", Float) = 0
        [HideInInspector] _EnablePOMLayer4("_", Float) = 0
        [HideInInspector] _EnablePOMLayer5("_", Float) = 0
        [HideInInspector] _EnablePOMLayer6("_", Float) = 0
        [HideInInspector] _EnablePOMLayer7("_", Float) = 0
        [HideInInspector] _HeightScale0("_", Float) = 0
        [HideInInspector] _HeightScale1("_", Float) = 0
        [HideInInspector] _HeightScale2("_", Float) = 0
        [HideInInspector] _HeightScale3("_", Float) = 0
        [HideInInspector] _HeightScale4("_", Float) = 0
        [HideInInspector] _HeightScale5("_", Float) = 0
        [HideInInspector] _HeightScale6("_", Float) = 0
        [HideInInspector] _HeightScale7("_", Float) = 0
        [HideInInspector] _POMMinSteps0("_", Float) = 4
        [HideInInspector] _POMMinSteps1("_", Float) = 4
        [HideInInspector] _POMMinSteps2("_", Float) = 4
        [HideInInspector] _POMMinSteps3("_", Float) = 4
        [HideInInspector] _POMMinSteps4("_", Float) = 4
        [HideInInspector] _POMMinSteps5("_", Float) = 4
        [HideInInspector] _POMMinSteps6("_", Float) = 4
        [HideInInspector] _POMMinSteps7("_", Float) = 4
        [HideInInspector] _POMMaxSteps0("_", Float) = 32
        [HideInInspector] _POMMaxSteps1("_", Float) = 32
        [HideInInspector] _POMMaxSteps2("_", Float) = 32
        [HideInInspector] _POMMaxSteps3("_", Float) = 32
        [HideInInspector] _POMMaxSteps4("_", Float) = 32
        [HideInInspector] _POMMaxSteps5("_", Float) = 32
        [HideInInspector] _POMMaxSteps6("_", Float) = 32
        [HideInInspector] _POMMaxSteps7("_", Float) = 32
        [HideInInspector] _EnableDisplacementLayer0("_", Float) = 0
        [HideInInspector] _EnableDisplacementLayer1("_", Float) = 0
        [HideInInspector] _EnableDisplacementLayer2("_", Float) = 0
        [HideInInspector] _EnableDisplacementLayer3("_", Float) = 0
        [HideInInspector] _EnableDisplacementLayer4("_", Float) = 0
        [HideInInspector] _EnableDisplacementLayer5("_", Float) = 0
        [HideInInspector] _EnableDisplacementLayer6("_", Float) = 0
        [HideInInspector] _EnableDisplacementLayer7("_", Float) = 0
        [HideInInspector] _DisplacementScale0("_", Float) = 0
        [HideInInspector] _DisplacementScale1("_", Float) = 0
        [HideInInspector] _DisplacementScale2("_", Float) = 0
        [HideInInspector] _DisplacementScale3("_", Float) = 0
        [HideInInspector] _DisplacementScale4("_", Float) = 0
        [HideInInspector] _DisplacementScale5("_", Float) = 0
        [HideInInspector] _DisplacementScale6("_", Float) = 0
        [HideInInspector] _DisplacementScale7("_", Float) = 0

        // ---------------------------------------------------------------------------------
        // CUSTOM: Sand Mode — per-layer enables + global sand parameters
        // Glitter, rim push-up, ocean specular, fresnel, ripples.
        // All set at runtime by TerrainLitCustomSetup.cs via material.SetFloat.
        // ---------------------------------------------------------------------------------
        [Header(Sand Mode)]
        [ToggleUI] _EnableSandMode("Enable Sand Mode", Float) = 0.0

        // Per-layer sand enable (set by C# — which layers are "sand")
        [HideInInspector] _EnableSandMode0("_", Float) = 0
        [HideInInspector] _EnableSandMode1("_", Float) = 0
        [HideInInspector] _EnableSandMode2("_", Float) = 0
        [HideInInspector] _EnableSandMode3("_", Float) = 0
        [HideInInspector] _EnableSandMode4("_", Float) = 0
        [HideInInspector] _EnableSandMode5("_", Float) = 0
        [HideInInspector] _EnableSandMode6("_", Float) = 0
        [HideInInspector] _EnableSandMode7("_", Float) = 0

        // Glitter
        [HideInInspector] _SandGlitterIntensity("_", Float) = 0.8
        [HideInInspector] _SandGlitterThreshold("_", Float) = 0.97
        [HideInInspector] _SandGlitterScale("_", Float) = 200.0
        [HideInInspector] _SandGlitterColor("_", Color) = (1, 0.95, 0.8, 1)
        [HideInInspector] _SandGlitterMaxDistance("_", Float) = 80.0
        [HideInInspector] _SandGlitterFalloffRamp("_", 2D) = "white" {}

        // Rim push-up (bourrelets)
        [HideInInspector] _SandRimPushUp("_", Float) = 0.03
        [HideInInspector] _SandDeformTexelSize("_", Float) = 0.000976563

        // Sun direction (set by C# from main directional light)
        [HideInInspector] _SandSunDirection("_", Vector) = (0.3, 0.8, 0.5, 0)

        // Ripples
        [HideInInspector] _SandRippleScale("_", Float) = 3.0
        [HideInInspector] _SandRippleStrength("_", Float) = 0.15

        // Ocean specular + Fresnel
        [HideInInspector] _SandOceanSpecPower("_", Float) = 16.0
        [HideInInspector] _SandOceanSpecIntensity("_", Float) = 0.3
        [HideInInspector] _SandFresnelPower("_", Float) = 4.0
        [HideInInspector] _SandFresnelIntensity("_", Float) = 0.3

        // ---------------------------------------------------------------------------------
        // CUSTOM: Wind Displacement — driven by WindZone component
        // Produces gentle procedural undulations on displacement-enabled layers.
        // All values set at runtime by TerrainLitCustomSetup.cs.
        // ---------------------------------------------------------------------------------
        [Header(Wind Displacement)]
        [HideInInspector] _WindGlobalMap("_", 2D) = "gray" {}
        [HideInInspector] _WindDetailMap("_", 2D) = "white" {}
        [HideInInspector] _WindGlobalTile("_", Float) = 0.01
        [HideInInspector] _WindDetailTile("_", Float) = 0.05
        [HideInInspector] _WindMinValue("_", Float) = -0.01
        [HideInInspector] _WindMaxValue("_", Float) = 0.02
        [HideInInspector] _WindGlobalOffsetDir("_", Vector) = (0.3, 0.15, 0, 0)
        [HideInInspector] _WindDetailOffsetDir("_", Vector) = (-0.1, 0.2, 0, 0)
        [HideInInspector] _WindPeriod("_", Float) = 0.08
        [HideInInspector] _WindTime("_", Float) = 0
        [HideInInspector] _WindDebugMode("_", Float) = 0

        // ---------------------------------------------------------------------------------
        // CUSTOM: Grass Tint (L2) — distant color-match toward the grass color.
        // Per-layer enables + tint params. Set at runtime by TerrainLitCustomSetup.cs.
        // ---------------------------------------------------------------------------------
        [Header(Grass Tint)]
        [ToggleUI] _EnableGrassTint("Enable Grass Tint", Float) = 0.0
        [HideInInspector] _EnableGrassTintLayer0("_", Float) = 0
        [HideInInspector] _EnableGrassTintLayer1("_", Float) = 0
        [HideInInspector] _EnableGrassTintLayer2("_", Float) = 0
        [HideInInspector] _EnableGrassTintLayer3("_", Float) = 0
        [HideInInspector] _EnableGrassTintLayer4("_", Float) = 0
        [HideInInspector] _EnableGrassTintLayer5("_", Float) = 0
        [HideInInspector] _EnableGrassTintLayer6("_", Float) = 0
        [HideInInspector] _EnableGrassTintLayer7("_", Float) = 0
        [HideInInspector] _GrassTintColor("_", Color) = (0.25, 0.4, 0.15, 1)
        [HideInInspector] _GrassTintStrength("_", Float) = 1.0
        [HideInInspector] _GrassTintSmoothness("_", Float) = 0.25
        [HideInInspector] _GrassSmoothnessBlend("_", Float) = 1.0
        [HideInInspector] _GrassNormalBlend("_", Float) = 1.0
        [HideInInspector] _GrassTintDistanceStart("_", Float) = 30.0
        [HideInInspector] _GrassTintDistanceFull("_", Float) = 120.0
        [HideInInspector] _GrassWaveNormalStrength("_", Float) = 0.5
        [HideInInspector] _GrassWaveLumStrength("_", Float) = 0.15
    }

    HLSLINCLUDE
    #pragma target 4.5

    // ---- Stock terrain keywords ----
    #pragma shader_feature_local _TERRAIN_8_LAYERS
    #pragma shader_feature_local _NORMALMAP
    #pragma shader_feature_local _MASKMAP
    #pragma shader_feature_local _SPECULAR_OCCLUSION_NONE

    #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
    #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL

    #pragma shader_feature_local _DISABLE_DECALS
    #pragma shader_feature_local _ADD_PRECOMPUTED_VELOCITY

    #pragma multi_compile _ _ALPHATEST_ON

    // ---- CUSTOM keywords ----
    // multi_compile_local forces both ON/OFF variants to compile in builds.
    // shader_feature_local only compiles variants referenced by materials at build time,
    // but Unity terrain's materialTemplate doesn't always serialize runtime-set keywords.
    //
    // They are declared PER-PASS (below), NOT here, so passes where a feature is inert do
    // not compile duplicate (byte-identical) variants. Where each keyword actually does work:
    //   _PARALLAX_OCCLUSION_MAPPING : GBuffer, Forward                          (fragment, #if FORWARD||GBUFFER)
    //   _GRASS_TINT                 : GBuffer, Forward                          (fragment, #if FORWARD||GBUFFER)
    //   _TESSELLATION_DISPLACEMENT  : GBuffer, Forward, ShadowCaster, DepthOnly (Hull/Domain vertex)
    //   _SAND_MODE                  : GBuffer, Forward, ShadowCaster, DepthOnly (fragment + Domain rim push-up)
    //   _WIND_DISPLACEMENT          : GBuffer, Forward, ShadowCaster, DepthOnly (fragment + Domain displacement)
    // META and SceneSelectionPass declare NONE (no Hull/Domain; all feature code compiled out).

    #define _DEFERRED_CAPABLE_MATERIAL
    #define SUPPORT_GLOBAL_MIP_BIAS

    // ---- CUSTOM: use our includes instead of stock ----
    #include "TerrainLitCustom_Splatmap_Includes.hlsl"

    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "SplatCount" = "8"
            "MaskMapR" = "Metallic"
            "MaskMapG" = "AO"
            "MaskMapB" = "Height"
            "MaskMapA" = "Smoothness"
            "DiffuseA" = "Smoothness (becomes Density when Mask map is assigned)"
            "DiffuseA_MaskMapUsed" = "Density"
            "TerrainCompatible" = "True"
        }

        // =====================================================================
        // Pass: GBuffer
        // =====================================================================
        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "GBuffer" }

            Cull [_CullMode]
            ZTest [_ZTestGBuffer]

            Stencil
            {
                WriteMask [_StencilWriteMaskGBuffer]
                Ref [_StencilRefGBuffer]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment DECALS_OFF DECALS_3RT DECALS_4RT
            #pragma multi_compile_fragment _ DECAL_SURFACE_GRADIENT
            #pragma multi_compile_fragment _ RENDERING_LAYERS

            // CUSTOM feature keywords — scoped to this pass (see HLSLINCLUDE mapping)
            #pragma multi_compile_local _ _PARALLAX_OCCLUSION_MAPPING
            #pragma multi_compile_local _ _TESSELLATION_DISPLACEMENT
            #pragma multi_compile_local _ _SAND_MODE
            #pragma multi_compile_local _ _WIND_DISPLACEMENT
            #pragma multi_compile_local _ _GRASS_TINT

            #define SHADERPASS SHADERPASS_GBUFFER
            #include "TerrainLitCustomTemplate.hlsl"
            #include "TerrainLitCustom_Splatmap.hlsl"
            #include "TerrainLitCustomHullDomain.hlsl"

            ENDHLSL
        }

        // =====================================================================
        // Pass: META
        // =====================================================================
        Pass
        {
            Name "META"
            Tags{ "LightMode" = "META" }

            Cull Off

            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #define SHADERPASS SHADERPASS_LIGHT_TRANSPORT
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "TerrainLitCustomTemplate.hlsl"
            #include "TerrainLitCustom_Splatmap.hlsl"

            ENDHLSL
        }

        // =====================================================================
        // Pass: ShadowCaster
        // =====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags{ "LightMode" = "ShadowCaster" }

            Cull[_CullMode]

            ZClip [_ZClip]
            ZWrite On
            ZTest LEqual

            ColorMask 0

            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            // CUSTOM feature keywords — only the vertex-affecting features matter in shadows
            #pragma multi_compile_local _ _TESSELLATION_DISPLACEMENT
            #pragma multi_compile_local _ _SAND_MODE
            #pragma multi_compile_local _ _WIND_DISPLACEMENT

            #define SHADERPASS SHADERPASS_SHADOWS
            #include "TerrainLitCustomTemplate.hlsl"
            #include "TerrainLitCustom_Splatmap.hlsl"
            #include "TerrainLitCustomHullDomain.hlsl"

            ENDHLSL
        }

        // =====================================================================
        // Pass: DepthOnly
        // =====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags{ "LightMode" = "DepthOnly" }

            Cull[_CullMode]

            Stencil
            {
                WriteMask [_StencilWriteMaskDepth]
                Ref [_StencilRefDepth]
                Comp Always
                Pass Replace
            }

            ZWrite On

            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile _ WRITE_DECAL_BUFFER WRITE_RENDERING_LAYER
            #pragma multi_compile _ WRITE_MSAA_DEPTH

            // CUSTOM feature keywords — only the vertex-affecting features matter in depth
            #pragma multi_compile_local _ _TESSELLATION_DISPLACEMENT
            #pragma multi_compile_local _ _SAND_MODE
            #pragma multi_compile_local _ _WIND_DISPLACEMENT

            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "TerrainLitCustomTemplate.hlsl"
            #ifdef WRITE_NORMAL_BUFFER
                #if defined(_NORMALMAP)
                    #define OVERRIDE_SPLAT_SAMPLER_NAME sampler_Normal0
                #elif defined(_MASKMAP)
                    #define OVERRIDE_SPLAT_SAMPLER_NAME sampler_Mask0
                #endif
            #endif
            #include "TerrainLitCustom_Splatmap.hlsl"
            #include "TerrainLitCustomHullDomain.hlsl"

            ENDHLSL
        }

        // =====================================================================
        // Pass: Forward
        // =====================================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "Forward" }

            Stencil
            {
                WriteMask [_StencilWriteMask]
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }

            ZTest [_ZTestDepthEqualForOpaque]
            ZWrite [_ZWrite]
            Cull [_CullMode]

            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment SCREEN_SPACE_SHADOWS_OFF SCREEN_SPACE_SHADOWS_ON
            #pragma multi_compile_fragment DECALS_OFF DECALS_3RT DECALS_4RT
            #pragma multi_compile_fragment _ DECAL_SURFACE_GRADIENT

            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

            #pragma multi_compile USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST

            // CUSTOM feature keywords — scoped to this pass (see HLSLINCLUDE mapping)
            #pragma multi_compile_local _ _PARALLAX_OCCLUSION_MAPPING
            #pragma multi_compile_local _ _TESSELLATION_DISPLACEMENT
            #pragma multi_compile_local _ _SAND_MODE
            #pragma multi_compile_local _ _WIND_DISPLACEMENT
            #pragma multi_compile_local _ _GRASS_TINT

            #define SHADERPASS SHADERPASS_FORWARD
            #include "TerrainLitCustomTemplate.hlsl"
            #include "TerrainLitCustom_Splatmap.hlsl"
            #include "TerrainLitCustomHullDomain.hlsl"

            ENDHLSL
        }

        // =====================================================================
        // Pass: SceneSelectionPass
        // =====================================================================
        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }

            Cull Off

            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma editor_sync_compilation
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENESELECTIONPASS
            #include "TerrainLitCustomTemplate.hlsl"
            #include "TerrainLitCustom_Splatmap.hlsl"

            ENDHLSL
        }

        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }

    // No SubShader 2 (DXR) — ray tracing not used

    Dependency "BaseMapShader" = "Hidden/HDRP/TerrainLit_Basemap"
    Dependency "BaseMapGenShader" = "Hidden/HDRP/TerrainLit_BasemapGen"
    FallBack "Hidden/HDRP/FallbackError"
    CustomEditor "UnityEditor.Rendering.HighDefinition.TerrainLitGUI"
}
