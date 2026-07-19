// OceanUnderwater.shader  (Ocean_v2 / P6)
// CustomPass FULLSCREEN du sous-marin (Q3.1 : compositing post-GBuffer, injection BeforePostProcess).
// G2 = ABSORPTION Beer-Lambert de la colonne d'eau traversée, avec le σ PARTAGÉ (_WaterAbsorption,
// source unique P3/Q6.1) : color *= exp(−σ·d). Le « glow » de single-scattering (bleu-vert) et les
// god-rays viendront du VOLUMETRIC HDRP natif (G4) ; la fenêtre de Snell = G3. Ici, transmittance pure.
//
// Gaté par _OceanUnderwaterEnabled (1 quand la caméra est immergée, poussé par OceanUnderwaterModule).
// N'écrit RIEN d'autre que la couleur (pas de mutation d'état partagé — anti-bug n°1 respecté côté C#).
//
// G3 (TEMPORAIRE) : bloc debug bâtissant la fenêtre de Snell par paliers testables sur la surface
// taggée en G3.0 — isolation (G3.a) → géométrie du cône (G3.b, actif) → contenu ciel/TIR (G3.c).
// Encadré par OCEAN_G3A_STENCIL_DEBUG, à retirer en G3.d. Cf. bloc dédié plus bas.
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
    // G3 — BLOC DEBUG fenêtre de Snell (préparatoire, retiré en G3.d). Construit par paliers sur les
    // pixels de surface isolés par le tag stencil UserBit0 (=64) posé en G3.0 (passe GBuffer d'Ocean
    // Surface.shader) : G3.a isolation → G3.b géométrie du cône → G3.c contenu réel (ciel réfracté/TIR).
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
    // Mode : 0 = off (retrait G3.d) ; 1 = magenta sur surface (isolation G3.a) ; 2 = DIAGNOSTIC bits
    //        stencil (bypasse G2) ; 3 = G3.b GÉOMÉTRIE fenêtre de Snell (2 zones debug) — ACTIF.
    // NB : _StencilTexture n'est PAS fournie par défaut à un FullScreen CustomPass (constaté G3.a :
    // lecture = 0). Elle est désormais rebindée par BindCameraStencilPass (OceanUnderwaterModule.cs),
    // enregistrée AVANT ce pass et gatée sur l'immersion → la lecture ci-dessous est valide immergé.
    #define OCEAN_G3A_STENCIL_DEBUG 3
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

#if OCEAN_G3A_STENCIL_DEBUG == 3
        // G3.b — GÉOMÉTRIE de la fenêtre de Snell (debug 2 zones ; le CONTENU réel = G3.c).
        // Sur les pixels de surface isolés (tag UserBit0, G3.0), on classe le rayon de vue par rapport
        // à l'angle critique θc = asin(1/n_eau) ≈ 48.6° (n_eau = 1.333), autour de la NORMALE DU PLAN
        // D'EAU = +Y monde (approximation plan, cf. cadrage — la normale par vague viendra si le gate
        // l'exige). But : valider le disque de Snell (centré au zénith, rayon ~48.6°, bord stable).
        //   θ < θc → DANS la fenêtre (ciel réfracté en G3.c)   → BLEU CIEL
        //   θ > θc → réflexion totale interne (TIR, G3.c)      → ROUGE SOMBRE
        // AUCUN échantillonnage ciel/réflexion ici. Les non-surface poursuivent → absorption G2 inchangée.
        uint stencil = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, posInput.positionSS));
        if ((stencil & 64u) != 0u)                     // 64 = StencilUsage.UserBit0 (surface, G3.0)
        {
            // positionWS est camera-relative (translation pure : les axes monde sont conservés) → sa
            // direction EST le rayon de vue caméra→pixel, et V.y = cos(angle avec la verticale +Y).
            float3 V        = normalize(posInput.positionWS);
            float  cosTheta = V.y;
            // n_eau = 1.333 → sinθc = 1/n ≈ 0.7502 → cosθc = sqrt(1 − (1/n)²) ≈ 0.6612.
            const float cosThetaC = 0.6612;
            const float edge      = 0.03;              // largeur du falloff (en cos) ≈ 2–3° au bord
            float  inWindow = smoothstep(cosThetaC - edge, cosThetaC + edge, cosTheta);
            float3 colWindow = float3(0.25, 0.60, 1.00); // fenêtre (ciel réfracté à venir en G3.c)
            float3 colTIR    = float3(0.45, 0.05, 0.05); // TIR (réflexion sous-marine à venir en G3.c)
            return float4(lerp(colTIR, colWindow, inWindow), 1.0);
        }
#elif OCEAN_G3A_STENCIL_DEBUG == 2
        // G3.a — DIAGNOSTIC TEMPORAIRE (mode 2) : visualise 3 bits du stencil caméra pour savoir
        // ce que _StencilTexture délivre réellement dans un FullScreen CustomPass. Bypasse G2 le
        // temps du diagnostic. Repli en mode 1 (magenta + G2) une fois la lecture confirmée.
        //   R = bit 1  (=2,  RequiresDeferredLighting) → 1 sur tout opaque éclairé (dont la surface)
        //   V = bit 6  (=64, UserBit0)                 → 1 UNIQUEMENT sur la surface (NOTRE tag G3.0)
        //   B = bit 5  (=32, ObjectMotionVector)       → 1 sur la surface animée
        // Lecture attendue immergé, regard vers le haut :
        //   • surface  → BLANC/jaune (R+V, ± B) ; le canal VERT prouve l'isolation du tag.
        //   • fond/objets → ROUGE (R seul).   • ciel → NOIR.   • tout NOIR → _StencilTexture non bindée.
        uint stencil = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, posInput.positionSS));
        float r = ((stencil & 2u)  != 0u) ? 1.0 : 0.0;
        float g = ((stencil & 64u) != 0u) ? 1.0 : 0.0;
        float b = ((stencil & 32u) != 0u) ? 1.0 : 0.0;
        return float4(r, g, b, 1.0);
#elif OCEAN_G3A_STENCIL_DEBUG == 1
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
