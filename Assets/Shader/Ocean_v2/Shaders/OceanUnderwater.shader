// OceanUnderwater.shader  (Ocean_v2)
// CustomPass FULLSCREEN du sous-marin (compositing post-GBuffer, injection BeforePostProcess).
// ABSORPTION Beer-Lambert de la colonne d'eau traversée, avec le σ PARTAGÉ (_WaterAbsorption,
// source unique) : color *= exp(−σ·d). Le « glow » de single-scattering (bleu-vert) et les
// god-rays viendront du VOLUMETRIC HDRP natif.
//
// FENÊTRE DE SNELL sur les pixels de surface isolés par le tag stencil UserBit0 (posé au GBuffer
// d'OceanSurface.shader) : dans le cône (θ<θc réglable) on échantillonne le ciel HDRP
// (_SkyTexture) dans la direction réfractée (loi de Snell) ; hors cône = réflexion totale interne (TIR,
// approximation « eau sombre »). Le résultat est ensuite absorbé par la colonne d'eau.
//
// Gaté par _OceanUnderwaterEnabled (1 quand la caméra est immergée, poussé par OceanUnderwaterModule).
// N'écrit RIEN d'autre que la couleur (pas de mutation d'état partagé — anti-bug n°1 respecté côté C#).
Shader "Hidden/Ocean/Underwater"
{
    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 d3d12 vulkan metal

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    // Caustiques immergées : SampleOceanNormal (cascades) + ComputeOceanCaustics, partagés avec la
    // surface (globals _OceanCaustics*/_OceanSunDirection poussés par le module Caustics). Include-guards
    // propres, aucune redéclaration de _WaterAbsorption. Cascade sampling AVANT caustics (dépendance).
    #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceCascadeSampling.hlsl"
    #include "Assets/Shader/Ocean_v2/Shaders/OceanCaustics.hlsl"

    // Globaux (poussés par les modules ; _WaterAbsorption est le MÊME que la surface).
    float4 _WaterAbsorption;         // σ (m⁻¹) en .rgb
    float  _OceanUnderwaterEnabled;  // 0/1 (caméra immergée)
    float  _OceanUnderwaterDistScale;// échelle artistique de densité (défaut 1)
    float  _OceanWaterLevel;         // Y absolu du plan d'eau (poussé par OceanSurfaceModule)

    // Rôle de cette passe (post-flip forward) : effets de COLONNE D'EAU sur la géométrie OPAQUE immergée
    // (fond + objets) — absorption Beer-Lambert + caustiques. La FENÊTRE DE SNELL (surface vue de dessous)
    // est désormais rendue par le SHADER DE SURFACE (forward double-face) : plus de tag stencil ni de lecture
    // du normal/sky buffer ici. La séparation « surface vs géométrie immergée » est GÉOMÉTRIQUE (worldY vs
    // niveau d'eau), robuste quel que soit le comportement de depth des transparents.
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth,
                                                   UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float4 color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1.0);

        if (_OceanUnderwaterEnabled < 0.5)
            return color;   // caméra émergée → cette passe n'a rien à faire

        // On ne traite QUE la géométrie OPAQUE immergée (worldY < niveau d'eau). Les pixels de surface / ciel
        // (worldY ≥ niveau d'eau, ou depth == far) sont possédés par le shader de surface (fenêtre de Snell +
        // sa propre absorption de colonne) → on les laisse intacts pour ne pas double-absorber.
        float3 pAbs = GetAbsolutePositionWS(posInput.positionWS);
        bool submergedGeom = (depth != UNITY_RAW_FAR_CLIP_VALUE) && (pAbs.y < _OceanWaterLevel);
        if (!submergedGeom)
            return color;

        // CAUSTIQUES sur la géométrie immergée (globals partagés du module Caustics). AVANT l'absorption →
        // plus profond = caustiques aussi atténuées. Marge 0.1 m pour ne pas frôler le plan d'eau.
        if (pAbs.y < _OceanWaterLevel - 0.1)
            color.rgb *= 1.0 + ComputeOceanCaustics(pAbs, _OceanWaterLevel);

        // Absorption Beer-Lambert de la colonne caméra→pixel (σ partagé). |positionWS| = distance (camera-relative).
        float  d = min(length(posInput.positionWS) * _OceanUnderwaterDistScale, 400.0);
        color.rgb *= exp(-max(_WaterAbsorption.rgb, 0.0) * d);
        return color;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Underwater"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
