// OceanSurface.shader  (Ocean_v2 / P1a — bascule fondation : surface TRANSPARENTE / forward)
// ---------------------------------------------------------------------------------
// Surface d'eau rendue en FORWARD dans la file TRANSPARENTE. Tessellation adaptative gatée distance,
// passe MotionVectors (TAA). Tout le framework HDRP/Lit est réutilisé ; on ne fournit en propre QUE :
//   (a) GetSurfaceAndBuiltinData          → OceanSurfaceData.hlsl  (partagé forward/GBuffer, réutilisé tel quel)
//   (b) les 3 hooks de tessellation       → OceanSurfaceTessellation.hlsl
//       (GetMaxDisplacement / GetTessellationFactor / ApplyTessellationModification)
//   (c) l'échantillonnage des cascades P1 → OceanSurfaceCascadeSampling.hlsl
//
// FONDATION (P1a) : la surface était OPAQUE/DEFERRED (GBuffer) jusqu'en P6. Le see-through (clarté des
// hauts-fonds, fond réfracté, caustiques sur le fond) exige une surface TRANSPARENTE (réfraction native
// au chemin forward, via _ColorPyramidTexture, branchée en P1b). On bascule donc en _SURFACE_TYPE_TRANSPARENT
// + passe `Forward` (LightMode "Forward"), file Transparent — passes mirrorées sur LitTessellation.shader
// (HDRP 17.4). Tant que la réfraction (P1b) n'est pas branchée, l'eau reste d'ASPECT OPAQUE
// (opacity = _BaseColor.a = 1, ZWrite On) : équivalent du rendu V1 (opaque dans la file transparente).
//
// G3.0 (tag stencil deferred) RETIRÉ : plus de GBuffer où l'écrire. La fenêtre de Snell se refera dans le
// pass sous-marin (lecture directe hauteur/normale FFT, approche V1), indépendamment de la fondation.
// ---------------------------------------------------------------------------------

Shader "Custom/HDRP/OceanSurface"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color (provisoire P2)", Color) = (0.03, 0.10, 0.16, 1)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.92
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0

        // Tessellation HDRP (lues par TessellationShare.GetTessellationFactors). Le gating distance
        // NATIF est désactivé par C# (Min/Max/TriangleSize = 0) : tout le gating vit dans notre
        // GetTessellationFactor (quantifié + caméra de référence snappée, pour la stabilité MV).
        [HideInInspector] _TessellationFactor("Tessellation Factor (max)", Range(1, 64)) = 32
        [HideInInspector] _TessellationFactorMinDistance("_", Float) = 0
        [HideInInspector] _TessellationFactorMaxDistance("_", Float) = 0
        [HideInInspector] _TessellationFactorTriangleSize("_", Float) = 0
        [HideInInspector] _TessellationShapeFactor("_", Float) = 0
        [HideInInspector] _TessellationBackFaceCullEpsilon("_", Float) = -1

        // Tessellation OCÉAN (lues par OceanSurfaceTessellation.hlsl). Poussées par OceanSurfaceModule.
        [HideInInspector] _OceanTessMinDist("_", Float) = 20
        [HideInInspector] _OceanTessMaxDist("_", Float) = 250
        [HideInInspector] _OceanTessQuantLevels("_", Float) = 8
        [HideInInspector] _OceanRefCamSnap("_", Float) = 2
        [HideInInspector] _OceanMaxDisplacement("_", Float) = 8
    }

    HLSLINCLUDE

    #pragma target 5.0   // hull/domain (tessellation) → Shader Model 5

    //-------------------------------------------------------------------------------------
    // Configuration matériau
    //-------------------------------------------------------------------------------------
    // P1a — surface TRANSPARENTE / forward. Défini GLOBALEMENT (shader mono-usage : route TOUT le
    // framework HDRP sur le chemin transparent). Mutuellement exclusif avec _DEFERRED_CAPABLE_MATERIAL
    // (cf. LitTessellation.shader réf. : #ifndef _SURFACE_TYPE_TRANSPARENT → _DEFERRED_CAPABLE_MATERIAL).
    #define _SURFACE_TYPE_TRANSPARENT
    #define SUPPORT_GLOBAL_MIP_BIAS
    #define PREFER_HALF 0                       // pleine précision (banding)

    // Tessellation + hook de déplacement dans le domain (rejoué avec _LastTimeParameters par la passe MV).
    #define TESSELLATION_ON
    #define HAVE_TESSELLATION_MODIFICATION
    #define _TESSELLATION_DISPLACEMENT
    // PAS de _TESSELLATION_PHONG (déplacement seul), PAS de HAVE_VERTEX_MODIFICATION, PAS de DOTS/BRG.

    //-------------------------------------------------------------------------------------
    // Includes de base (ordre HDRP/Lit) + cbuffer UnityPerMaterial
    //-------------------------------------------------------------------------------------
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    // GeometricTools + Tessellation (core) : OBLIGATOIRES pour un shader tessellé. Tessellation.hlsl
    // définit TESSELLATION_INTERPOLATE_BARY (utilisé par InterpolateWithBaryCoordsMeshToDS dans
    // VaryingMesh.hlsl:322, appelé par le domain de TOUTES les passes géométriques) et dépend de
    // ProjectPointOnPlane / ComputeNormalizedDeviceCoordinates (GeometricTools.hlsl). Sans ces deux
    // includes, la macro ne se déploie pas → `positionRWS` reste un token nu → "undeclared identifier
    // 'positionRWS'" au 1er variant tessellé compilé (ShadowCaster observé). Ordre calqué EXACTEMENT
    // sur LitTessellation.shader (HDRP 17.4, L342-345) : Common → GeometricTools → Tessellation → ShaderVariables.
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Tessellation.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

    // UnityPerMaterial : TOUTES les propriétés matériau (SRP Batcher). Les globaux océan (_OceanDisp*,
    // _OceanCascade*, _OceanMVValid, _OceanDispPrev*) sont HORS cbuffer (Shader.SetGlobal*) — voir
    // OceanSurfaceCascadeSampling.hlsl.
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float  _Smoothness;
        float  _Metallic;
        // Tessellation HDRP (framework)
        float  _TessellationFactor;
        float  _TessellationFactorMinDistance;
        float  _TessellationFactorMaxDistance;
        float  _TessellationFactorTriangleSize;
        float  _TessellationShapeFactor;
        float  _TessellationBackFaceCullEpsilon;
        // Tessellation océan (custom)
        float  _OceanTessMinDist;
        float  _OceanTessMaxDist;
        float  _OceanTessQuantLevels;
        float  _OceanRefCamSnap;
        float  _OceanMaxDisplacement;
        // État TRANSPARENT HDRP (P1a) : référencé par Material.hlsl (ApplyBlendMode) et le chemin forward
        // dès que _SURFACE_TYPE_TRANSPARENT est défini ; normalement fourni par LitProperties.hlsl (non
        // inclus par la surface océan). Défauts 0 = blend Alpha, preserve-specular OFF — cohérent avec
        // opacity = _BaseColor.a = 1 (rendu d'aspect opaque). Déclarés dans UnityPerMaterial (SRP Batcher).
        float  _BlendMode;
        float  _EnableBlendModePreserveSpecularLighting;
    CBUFFER_END

    // Variables alimentées par l'éditeur C++ pour la SÉLECTION (SceneSelectionPass) et le PICKING
    // (ScenePickingPass) en Scene View. Copie VERBATIM de HDRP/Lit LitProperties.hlsl (L294-298) : elles
    // DOIVENT rester HORS du cbuffer UnityPerMaterial pour la compatibilité SRP Batcher. Comme la surface
    // océan remplace LitData.hlsl par OceanSurfaceData.hlsl, elle n'inclut jamais LitProperties.hlsl et
    // doit donc les déclarer elle-même — sinon ShaderPassDepthOnly.hlsl référence `_ObjectId`/`_PassValue`
    // (L112) et `unity_SelectionID`→`_SelectionID` (L114, macro de ShaderVariables.hlsl) non déclarés →
    // "undeclared identifier '_ObjectId' / '_SelectionID'" au build (ces 2 passes n'étant compilées qu'en
    // build forcé via Always Included Shaders / clic Scene View). Déclarées globalement, à l'identique de
    // LitProperties (inclus par toutes les passes Lit) : inertes dans les passes non-sélection.
    int    _ObjectId;
    int    _PassValue;
    float4 _SelectionID;

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDLitShader" "Queue" = "Transparent" }

        // =====================================================================
        // Forward (éclairage forward TRANSPARENT — mirroré sur LitTessellation.shader, HDRP 17.4).
        // Remplace le GBuffer : plus de chemin deferred (see-through impossible en opaque).
        // =====================================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "Forward" }

            // DOUBLE-SIDED (Cull Off) : surface visible de DESSOUS (raccord dessus/dessous, Q4.1) ; la
            // normale est retournée face-caméra dans OceanSurfaceData.hlsl. Blend alpha standard, mais
            // opacity = _BaseColor.a = 1 (P1a) ⇒ rendu d'ASPECT OPAQUE ; ZWrite On (l'eau écrit la depth,
            // comme V1). La réfraction (see-through réel via _ColorPyramidTexture) se branche en P1b.
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest LEqual
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal
            #pragma editor_sync_compilation

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing

            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment SCREEN_SPACE_SHADOWS_OFF SCREEN_SPACE_SHADOWS_ON
            #pragma multi_compile_fragment DECALS_OFF DECALS_3RT DECALS_4RT
            #pragma multi_compile_fragment _ DECAL_SURFACE_GRADIENT
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile_fragment USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST

            #ifndef SHADER_STAGE_FRAGMENT
            #define SHADOW_LOW
            #define USE_FPTL_LIGHTLIST
            #endif

            #define SHADERPASS SHADERPASS_FORWARD
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
        #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
        #endif
            #define HAS_LIGHTLOOP
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceTessellation.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            ENDHLSL
        }

        // =====================================================================
        // DepthOnly (prepass : fog / SSAO / contact shadows voient l'eau)
        // =====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off   // P6 double-sided : la depth de l'eau vue de dessous doit exister (fog/prepass)
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

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing

            // Deferred : pas d'écriture du normal buffer (forward only) ni MSAA → WRITE_NORMAL_BUFFER /
            // WRITE_MSAA_DEPTH retirés.
            #pragma multi_compile _ WRITE_DECAL_BUFFER WRITE_RENDERING_LAYER

            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceTessellation.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            ENDHLSL
        }

        // =====================================================================
        // ShadowCaster (l'eau projette des ombres cohérentes)
        // =====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing

            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceTessellation.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            ENDHLSL
        }

        // =====================================================================
        // MotionVectors (TAA / motion-blur corrects sur l'eau animée GPU)
        // Le déplacement N-1 est rejoué dans le domain via ApplyTessellationModification
        // (MotionVectorTessellation, _LastTimeParameters) → échantillonne _OceanDispPrev*.
        // =====================================================================
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Cull Off   // P6 double-sided : MV cohérents sur la face vue de dessous
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

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing

            // Deferred : WRITE_NORMAL_BUFFER / WRITE_MSAA_DEPTH retirés (cf. DepthOnly).
            #pragma multi_compile _ WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #ifdef WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #define WRITE_DECAL_BUFFER
            #endif

            #define SHADERPASS SHADERPASS_MOTION_VECTORS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitMotionVectorPass.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceTessellation.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassMotionVectors.hlsl"
            ENDHLSL
        }

        // =====================================================================
        // ScenePickingPass (clic-pour-sélectionner en Scene View, LightMode "Picking")
        // Distincte de SceneSelectionPass (contour de l'objet DÉJÀ sélectionné) : ce passe pilote le
        // TEST DE CLIC (_SelectionID). Sans lui, le picking retombe sur le maillage de base non tessellé
        // (plan à Y=0) → on ne peut pas cliquer les crêtes déplacées en vue rasante.
        // Miroir EXACT du LightMode "Picking" de LitTessellation.shader (HDRP 17.4) : driver depth
        // (SHADERPASS_DEPTH_ONLY + SCENEPICKINGPASS) AVEC le hull/domain tessellé — même tessellation
        // que tous les passes géométriques (GBuffer/DepthOnly/ShadowCaster/MotionVectors/SceneSelection).
        // =====================================================================
        Pass
        {
            Name "ScenePickingPass"
            Tags { "LightMode" = "Picking" }

            Cull Back

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal
            #pragma editor_sync_compilation

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing

            // Note : requiert _SelectionID (fixé par le pipeline de picking éditeur).
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENEPICKINGPASS   // pilote la sortie d'ID de picking du driver depth
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceTessellation.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            ENDHLSL
        }

        // =====================================================================
        // SceneSelectionPass (contour de sélection en Scene View de la surface DÉPLACÉE)
        // Sans ce passe, la sélection éditeur retomberait sur le maillage de base non tessellé
        // (plan à Y=0), décalé jusqu'à ~maxWaveHeight en vue rasante → contour peu fiable sur l'eau.
        // Réutilise le driver depth (SHADERPASS_DEPTH_ONLY + SCENESELECTIONPASS) AVEC le hull/domain
        // tessellé (cohérent avec GBuffer/DepthOnly/ShadowCaster/MotionVectors : tous les passes
        // géométriques partagent la même tessellation, cf. src [2] Vashenkov/TerrainLit).
        // Tessellation confirmée STANDARD : le LightMode "SceneSelectionPass" de LitTessellation.shader
        // (HDRP 17.4) déclare bien #pragma hull/domain (contrairement au Lit.shader NON tessellé).
        // =====================================================================
        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }

            Cull Off

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal
            #pragma editor_sync_compilation

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing

            // Note : requiert _ObjectId / _PassValue (fixés par le pipeline de sélection éditeur).
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENESELECTIONPASS   // pilote la sortie d'ID de sélection du driver depth
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceTessellation.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            ENDHLSL
        }

        // =====================================================================
        // META (light transport / lightmapper) — AUCUN hull/domain ni tessellation.
        // =====================================================================
        Pass
        {
            Name "META"
            Tags { "LightMode" = "META" }

            Cull Off

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal

            // La surface océan n'est pas tessellée hors géométrie : on retire la tessellation pour la META.
            #undef TESSELLATION_ON
            #undef HAVE_TESSELLATION_MODIFICATION

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing

            #define SHADERPASS SHADERPASS_LIGHT_TRANSPORT
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassLightTransport.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
