// OceanUnderwater.shader  (Ocean_v2 / P6)
// CustomPass FULLSCREEN du sous-marin (Q3.1 : compositing post-GBuffer, injection BeforePostProcess).
// G2 = ABSORPTION Beer-Lambert de la colonne d'eau traversée, avec le σ PARTAGÉ (_WaterAbsorption,
// source unique P3/Q6.1) : color *= exp(−σ·d). Le « glow » de single-scattering (bleu-vert) et les
// god-rays viendront du VOLUMETRIC HDRP natif (G4) ; la fenêtre de Snell = G3. Ici, transmittance pure.
//
// Gaté par _OceanUnderwaterEnabled (1 quand la caméra est immergée, poussé par OceanUnderwaterModule).
// N'écrit RIEN d'autre que la couleur (pas de mutation d'état partagé — anti-bug n°1 respecté côté C#).
//
// G3.a (TEMPORAIRE) : un masque debug magenta isole la surface vue de dessous via le tag stencil
// UserBit0 posé en G3.0. Encadré par OCEAN_G3A_STENCIL_DEBUG, à retirer en G3.d. Cf. bloc dédié plus bas.
Shader "Hidden/Ocean/Underwater"
{
    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 d3d12 vulkan metal

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    // Globaux (poussés par le module ; _WaterAbsorption est le MÊME que la surface, Q6.1).
    float4 _WaterAbsorption;         // σ (m⁻¹) en .rgb
    float  _OceanUnderwaterEnabled;  // 0/1
    float  _OceanUnderwaterDistScale;// échelle artistique de densité (défaut 1)

    // ---------------------------------------------------------------------------------
    // G3.a — MASQUE DEBUG D'ISOLATION (préparatoire à la fenêtre de Snell, retiré en G3.d).
    // But UNIQUE : prouver qu'on isole EXACTEMENT les pixels de surface océan vus de dessous
    // via le tag stencil UserBit0 (=64) posé en G3.0 sur la passe GBuffer d'OceanSurface.shader
    // — et RIEN d'autre (ni fond, ni objets immergés, ni ciel). AUCUNE physique Snell ici.
    //
    // Lecture du stencil : CustomPassSampling.hlsl n'expose PAS le stencil caméra. On lit donc
    // le global HDRP _StencilTexture (copie du stencil générée après le prepass, consommée par
    // SSR/SSAO — SSR étant actif via le module réflexion P5, la copie est bindée et à jour à
    // l'injection BeforePostProcess). GetStencilValue (core Common.hlsl, déjà inclus via
    // CustomPassCommon.hlsl) extrait le bon canal selon la plateforme (D3D12 inclus).
    //
    // Choix (b) lecture manuelle vs (a) test stencil matériel : (a) Stencil{Comp Equal} sur cette
    // passe REJETTERAIT les fragments non-surface → ils perdraient l'absorption G2 (chemin unique
    // de cette FullScreenPass). Préserver G2 sur les non-surface imposerait 2 passes → modif C#,
    // interdite en G3.a. La lecture manuelle branche dans le MÊME fragment : surface→debug, sinon→G2.
    #define OCEAN_G3A_STENCIL_DEBUG 1
    #if OCEAN_G3A_STENCIL_DEBUG
    TYPED_TEXTURE2D_X(uint2, _StencilTexture);   // stencil caméra HDRP (déclaré ici : absent de la chaîne CustomPass)
    #endif
    // ---------------------------------------------------------------------------------

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth,
                                                   UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float4 color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1.0);

        if (_OceanUnderwaterEnabled < 0.5)
            return color;

#if OCEAN_G3A_STENCIL_DEBUG
        // G3.a — isole le tag de surface (UserBit0=64, STENCILUSAGE_USER_BIT0) posé en G3.0.
        // Surface océan vue de dessous → aplat magenta (masque de preuve, laid et ATTENDU ;
        // remplacé par la physique de Snell en G3.b). Tout autre pixel (fond, objets immergés,
        // ciel) poursuit le chemin normal → absorption G2 ci-dessous, INCHANGÉE.
        uint stencil = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, posInput.positionSS));
        if ((stencil & 64u) != 0u)                 // 64 = StencilUsage.UserBit0 (bit posé par la surface)
            return float4(1.0, 0.0, 1.0, 1.0);     // magenta debug — isolation G3.a
#endif

        // Distance caméra → pixel. En rendu camera-relative HDRP, positionWS est relatif caméra →
        // sa longueur EST la distance. Le skybox (depth ~ far) est clampé pour éviter un exp(−∞) exact
        // (on garde une teinte d'horizon plutôt qu'un noir dur ; le fog volumétrique G4 le comblera).
        float d = min(length(posInput.positionWS) * _OceanUnderwaterDistScale, 400.0);
        float3 sigma = max(_WaterAbsorption.rgb, 0.0);
        float3 transmittance = exp(-sigma * d);

        color.rgb *= transmittance;   // Beer-Lambert (absorption pure ; scatter = volumetric HDRP G4)
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
