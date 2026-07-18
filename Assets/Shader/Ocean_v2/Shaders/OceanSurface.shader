// OceanSurface.shader  (Ocean_v2 / P2)
// ---------------------------------------------------------------------------------
// Première surface OPAQUE de l'océan rendue en DEFERRED (GBuffer), tessellation adaptative gatée
// distance, passe MotionVectors native (TAA correct sur l'eau animée GPU).
//
// Architecture (plus bas risque) : on réutilise l'INTÉGRALITÉ du framework HDRP/Lit — encodage GBuffer,
// drivers de passe (ShaderPass*.hlsl), hull/domain (TessellationShare.hlsl), chemin MotionVectors
// tessellé (MotionVectorTessellation) — et on ne fournit en propre QUE :
//   (a) GetSurfaceAndBuiltinData          → OceanSurfaceData.hlsl
//   (b) les 3 hooks de tessellation       → OceanSurfaceTessellation.hlsl
//       (GetMaxDisplacement / GetTessellationFactor / ApplyTessellationModification)
//   (c) l'échantillonnage des cascades P1 → OceanSurfaceCascadeSampling.hlsl
//
// Structurellement = un HDRP/Lit allégé (non-layered, opaque, deferred-capable, single-sided Cull Back,
// tessellation + déplacement). Les cartes d'absorption/réflexion/écume arrivent en P3/P5/P6.
//
// STENCIL (vérifié vs HDRP 17.4 Lit.shader, opaque SANS diffusion profile) :
//   GBuffer       Ref=66 WriteMask=67  // RequiresDeferredLighting (=2) | UserBit0 (=64) — cf. G3.0 ci-dessous
//   DepthOnly     Ref=0  WriteMask=8   // StencilUsage.TraceReflectionRay
//   MotionVectors Ref=32 WriteMask=32  // StencilUsage.ObjectMotionVector
//   ShadowCaster  (aucun stencil)
// Justification : un masque erroné ferait écrire la surface au GBuffer mais la ferait SAUTER par le
// LightLoop → eau noire. Gate éclairage explicite au protocole P2 (distinct de « compile sans erreur »).
//
// P6 / G3.0 — TAG STENCIL surface (préparatoire à la fenêtre de Snell, gate anti-« eau noire ») :
// la logique Snell (G3.a+) discriminera les pixels de surface vus de DESSOUS via ce tag dédié (approche
// précise, pas écran-espace). G3.0 pose le tag et RIEN d'autre — il n'est encore lu par personne.
//   Bit utilisateur retenu : StencilUsage.UserBit0 = (1 << 6) = 64.
//   Provenance : HDRP HDStencilUsage.cs — bits 6-7 = UserBit0/UserBit1, SEULS bits libres
//   (HDRPReservedBits = 255 & ~(UserBit0|UserBit1) = 63 → HDRP réserve les bits 0-5). Aucun autre
//   système du projet (herbe, terrain, decals, CustomPass OceanUnderwater) n'écrit ni ne teste ce bit.
//   Le bit s'AJOUTE au masque système sans le remplacer : Ref/WriteMask GBuffer |= 64. Le comportement
//   RequiresDeferredLighting (éclairage deferred) reste identique bit-à-bit → pas de régression LightLoop.
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
    #define _DEFERRED_CAPABLE_MATERIAL          // opaque → éligible au chemin GBuffer deferred
    #define SUPPORT_GLOBAL_MIP_BIAS
    #define PREFER_HALF 0                       // Lit conseille la pleine précision (banding GBuffer)

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
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDLitShader" }

        // =====================================================================
        // GBuffer (lighting deferred opaque)
        // =====================================================================
        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "GBuffer" }

            // P6 : DOUBLE-SIDED (Cull Off) → surface visible de DESSOUS (raccord dessus/dessous, Q4.1).
            // La normale est retournée vers la caméra dans OceanSurfaceData.hlsl (dot(normalWS,V)<0)
            // → les 2 faces s'éclairent de façon cohérente (normale toujours face-caméra). Vue de dessus,
            // le front-face gagne le ZTest (même géométrie) → aucune régression P2/P5.
            Cull Off
            ZTest LEqual
            ZWrite On

            Stencil
            {
                // Système (inchangé) : RequiresDeferredLighting=2, masque 3 (bit SSS écrit à 0) — gate LightLoop.
                // G3.0 : on AJOUTE UserBit0 (=64) SANS toucher les bits système (cf. en-tête). Le tag n'est
                // pas encore lu (G3.a+) ; il prouve seulement que tagger la surface ne casse ni LightLoop ni MV.
                WriteMask 67  // 3 (RequiresDeferredLighting | SSS, bit SSS écrit à 0) | 64 (UserBit0)
                Ref 66        // 2 (RequiresDeferredLighting) | 64 (UserBit0)
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma only_renderers d3d11 d3d12 vulkan metal
            #pragma editor_sync_compilation

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #pragma multi_compile_instancing

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
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceTessellation.hlsl"
            #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassGBuffer.hlsl"
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
