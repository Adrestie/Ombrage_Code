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
    // Décodage du normal buffer GBuffer (relief des vagues pour la fenêtre de Snell). Déclare
    // _NormalBufferTexture + DecodeFromNormalBuffer derrière son propre include-guard (pas de conflit).
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
    // Caustiques immergées : SampleOceanNormal (cascades) + ComputeOceanCaustics, partagés avec la
    // surface (globals _OceanCaustics*/_OceanSunDirection poussés par le module Caustics). Include-guards
    // propres, aucune redéclaration de _WaterAbsorption. Cascade sampling AVANT caustics (dépendance).
    #include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceCascadeSampling.hlsl"
    #include "Assets/Shader/Ocean_v2/Shaders/OceanCaustics.hlsl"

    // Globaux (poussés par le module ; _WaterAbsorption est le MÊME que la surface).
    float4 _WaterAbsorption;         // σ (m⁻¹) en .rgb
    float  _OceanUnderwaterEnabled;  // 0/1
    float  _OceanUnderwaterDistScale;// échelle artistique de densité (défaut 1)
    float  _OceanSnellCosThetaC;     // cos(demi-angle du cône de Snell), poussé par le module (réglable)
    float  _OceanWaterLevel;         // Y absolu du plan d'eau (poussé par OceanCausticsModule) — caustiques immergées

    // ---------------------------------------------------------------------------------
    // Ressources lues par la fenêtre de Snell :
    //  • _StencilTexture : stencil caméra HDRP. ABSENT de la chaîne d'includes d'un FullScreen CustomPass
    //    (CustomPassSampling.hlsl ne l'expose pas) → on le DÉCLARE ici, et il est rebindé par
    //    BindCameraStencilPass (OceanUnderwaterModule.cs) AVANT ce pass, gaté immersion. Isole la surface
    //    (tag UserBit0=64 posé au GBuffer d'OceanSurface.shader). Lu via GetStencilValue (core Common.hlsl)
    //    qui choisit le bon canal selon la plateforme (D3D12 inclus).
    //  • _SkyTexture : cubemap de ciel HDRP, DÉJÀ déclaré par la chaîne d'includes (ne pas redéclarer) ;
    //    global de frame bindé en UpdateEnvironment → dispo à BeforePostProcess. Échantillonné dans la
    //    direction réfractée pour remplir la fenêtre.
    TYPED_TEXTURE2D_X(uint2, _StencilTexture);
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

        // Absorption Beer-Lambert de la colonne d'eau traversée (σ partagé). Distance caméra→pixel :
        // en camera-relative HDRP, |positionWS| EST la distance. Skybox (depth ~ far) clampé (pas d'exp(−∞)).
        float  d = min(length(posInput.positionWS) * _OceanUnderwaterDistScale, 400.0);
        float3 transmittance = exp(-max(_WaterAbsorption.rgb, 0.0) * d);

        // FENÊTRE DE SNELL — uniquement sur les pixels de surface océan vus de dessous (tag UserBit0).
        // On REMPLACE la couleur de surface par la lumière venant du dessus ; elle est ensuite absorbée par
        // la colonne d'eau (color.rgb *= transmittance plus bas → fenêtre d'autant plus sombre que la caméra
        // est profonde).
        uint stencil = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, posInput.positionSS));
        if ((stencil & 64u) != 0u)                        // 64 = StencilUsage.UserBit0 (surface)
        {
            // V = rayon de vue caméra→pixel (camera-relative : les axes monde sont conservés).
            float3 V = normalize(posInput.positionWS);

            // Normale de surface AVEC le relief des vagues (au lieu de +Y plat) → la fenêtre ONDULE.
            // Lue dans le normal buffer GBuffer. OceanSurfaceData retourne la normale FACE-CAMÉRA :
            // vue de dessous elle pointe vers le bas → on la force MONTANTE pour retrouver la normale
            // géométrique de l'eau (les vagues ne surplombent pas → hémisphère supérieur).
            NormalData nd;
            DecodeFromNormalBuffer(posInput.positionSS, nd);
            float3 N = (nd.normalWS.y < 0.0) ? -nd.normalWS : nd.normalWS;

            // cosθ = angle d'incidence vs la normale LOCALE (ondule avec les vagues). θc = demi-angle
            // réglable ; on en dérive l'indice de l'eau pour une réfraction cohérente avec le bord.
            float  cosTheta  = dot(V, N);
            float  cosThetaC = _OceanSnellCosThetaC;
            float  sinThetaC = sqrt(saturate(1.0 - cosThetaC * cosThetaC));
            float  eta       = 1.0 / max(sinThetaC, 1e-3); // n_eau (n_air=1), = 1/sin(θc)

            // Réfraction eau→air autour de la normale ondulée (normale face à l'eau = -N). refract()
            // renvoie 0 en réflexion totale interne (θ > θc) → détection TIR gratuite ; la direction
            // réfractée dit où lire le ciel émergé.
            float3 refr   = refract(V, -N, eta);
            bool   isTIR  = dot(refr, refr) < 1e-6;
            float3 skyDir = isTIR ? normalize(float3(V.x, 1e-3, V.z)) : refr;   // repli horizon près du bord
            // _SkyTexture stocke la radiance ABSOLUE (non pré-exposée) ; le color buffer à BeforePostProcess
            // est PRÉ-EXPOSÉ → sans ce facteur, le ciel HDR est écrasé en blanc. GetCurrentExposureMultiplier
            // (ShaderVariables.hlsl, déjà inclus) ramène le cubemap dans l'espace pré-exposé du buffer.
            float3 skyCol = SAMPLE_TEXTURECUBE_ARRAY_LOD(_SkyTexture, s_trilinear_clamp_sampler, skyDir, 0, 0).rgb
                          * GetCurrentExposureMultiplier();

            // TIR (hors cône) : approximation « eau sombre » — la réflexion réelle de l'environnement
            // sous-marin viendra plus tard. Bord adouci (smoothstep) entre fenêtre (ciel) et TIR.
            const float3 colTIR = float3(0.004, 0.020, 0.030);
            const float  edge   = 0.03;
            float  inWindow = smoothstep(cosThetaC - edge, cosThetaC + edge, cosTheta);
            color.rgb = lerp(colTIR, skyCol, inWindow);
        }

        // CAUSTIQUES sur la géométrie OPAQUE immergée (fond + objets), consommant les globals partagés du
        // module Caustics. Détection « sous l'eau » GÉOMÉTRIQUE (worldY < niveau d'eau), indépendante du
        // tag stencil (cassé par le flip forward). Skybox exclue (depth == far). Petite marge anti-surface
        // (0.1 m sous le plan) pour ne pas éclairer les pixels de surface / juste au niveau d'eau.
        // Appliquées AVANT l'absorption ci-dessous → plus profond = caustiques aussi atténuées (correct).
        float3 pAbs = GetAbsolutePositionWS(posInput.positionWS);
        if (depth != UNITY_RAW_FAR_CLIP_VALUE && pAbs.y < _OceanWaterLevel - 0.1)
            color.rgb *= 1.0 + ComputeOceanCaustics(pAbs, _OceanWaterLevel);

        color.rgb *= transmittance;   // absorption colonne (surface : ciel réfracté / TIR ; sinon : scène immergée)
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
