// OceanUnderwater.shader  (Ocean_v2)
// CustomPass FULLSCREEN du sous-marin (compositing post-GBuffer, injection BeforePostProcess).
// MILIEU SOUS-MARIN (single-scattering), σ PARTAGÉ (_WaterAbsorption) + couleur d'eau (_OceanScatterColor,
// même look que la surface) : L = L_fond·T + couleurEau·(1−T), T = exp(−σ·d). La géométrie lointaine se
// FOND dans la couleur d'eau (fog), plus vers le noir. Les pixels sans géométrie (depth==far) horizontaux/
// descendants (l'horizon sous l'eau) → couleur d'eau pleine (colonne infinie). Le fog volumétrique HDRP
// (module Volumetrics) ajoute PAR-DESSUS le glow LITÉ (même teinte).
//
// FENÊTRE DE SNELL sur les pixels de surface isolés par le tag stencil UserBit0 (posé au GBuffer
// d'OceanSurface.shader) : dans le cône (θ<θc réglable) on échantillonne le ciel HDRP
// (_SkyTexture) dans la direction réfractée (loi de Snell) ; hors cône = réflexion totale interne (TIR,
// approximation « eau sombre »). Le résultat est ensuite absorbé par la colonne d'eau.
//
// Gaté par _OceanUnderwaterEnabled (1 quand la caméra est immergée, poussé par OceanUnderwaterModule).
// N'écrit que la couleur (fog + composite additif des god-rays ; pas de mutation d'état partagé — anti-bug n°1).
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
    // God-rays : RAYMARCH déporté dans une passe demi-résolution dédiée (OceanGodRayLowResPass →
    // OceanGodRaysLowRes.shader) rendue en amont ; ici on ne fait que SAMPLER + AJOUTER sa RT à la fin.

    // Globaux (poussés par les modules ; _WaterAbsorption est le MÊME que la surface).
    float4 _WaterAbsorption;         // spectre d'absorption (couleur/ORDRE, normalisé in-shader — la magnitude vient de la distance de vue)
    float4 _OceanScatterColor;       // couleur AFFICHÉE de l'eau = in-scattering du milieu (poussée par OceanAbsorptionModule)
    float  _OceanUnderwaterEnabled;  // 0/1 (caméra immergée)
    float  _OceanWaterLevel;         // Y absolu du plan d'eau (poussé par OceanSurfaceModule)
    // Visibilité (fog) & lumière pilotées par la PROFONDEUR caméra (poussées par OceanUnderwaterModule) :
    float  _OceanViewMinDist;        // distance de vue min (profond)
    float  _OceanViewMaxDist;        // distance de vue max (surface)
    float  _OceanViewReduceAtDepth;  // profondeur où la vue commence à baisser
    float  _OceanMinViewAtDepth;     // profondeur où la vue est minimale
    float  _OceanLightReduceAtDepth; // profondeur où la lumière commence à baisser
    float  _OceanMinLightAtDepth;    // profondeur où la lumière est nulle

    // GOD-RAYS : rendus en amont (AfterOpaqueDepthAndNormal) dans une RT demi-résolution par
    // OceanGodRayLowResPass, puis ÉCHANTILLONNÉS + AJOUTÉS À LA FIN de cette passe (ordre garanti,
    // plus de passe composite séparée qui courait avec ce fog). _OceanGodRaysEnabled gate la lecture.
    float       _OceanGodRaysEnabled;  // 0/1 (module Volumetrics actif — poussé par la surface)
    TEXTURE2D_X(_OceanGodRayTex);      // RT demi-res god-rays (radiance additive)

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

        // Gate : module actif ET caméra IMMERGÉE (submersion calculée in-shader par-caméra — robuste
        // Scene view/Play, contrairement à l'ancien Camera.main). Sinon la passe n'a rien à faire.
        float camAbsY = GetAbsolutePositionWS(float3(0.0, 0.0, 0.0)).y;
        if (_OceanUnderwaterEnabled < 0.5 || camAbsY >= _OceanWaterLevel)
            return color;

        // Profondeur de la CAMÉRA sous la surface — pilote la visibilité ET la lumière (constante par frame).
        float camDepth = max(_OceanWaterLevel - camAbsY, 0.0);

        // VISIBILITÉ pilotée par la profondeur : viewDist décroît quand la caméra descend.
        //   viewDist = lerp(max, min, smoothstep(viewReduceAtDepth, minViewAtDepth, camDepth))
        // σ = spectre NORMALISÉ (couleur/ordre depuis _WaterAbsorption) × (1/viewDist) → le canal dominant
        // chute à 1/e à viewDist mètres ; l'ordre spectral (A3) est préservé (canaux moins absorbés = plus loin).
        float3 spec = max(_WaterAbsorption.rgb, 1e-4);
        spec /= max(max(spec.x, max(spec.y, spec.z)), 1e-4);      // canal dominant → 1
        float  viewDist = lerp(_OceanViewMaxDist, _OceanViewMinDist,
                               smoothstep(_OceanViewReduceAtDepth, _OceanMinViewAtDepth, camDepth));
        float3 sigma    = spec / max(viewDist, 0.1);

        // LUMIÈRE pilotée par la profondeur : lightFactor 1 → 0 entre lightReduceAtDepth et minLightAtDepth.
        float  lightFactor = 1.0 - smoothstep(_OceanLightReduceAtDepth, _OceanMinLightAtDepth, camDepth);
        float3 inScatter   = _OceanScatterColor.rgb;              // teinte du fog (assombrie globalement par lightFactor)

        // ── FOG UNIFIÉ : longueur de trajet DANS l'eau jusqu'au pixel, pour TOUT (fond, surface vue de
        //    dessous, horizon), en UNE règle → raccord sans couture, la surface se fond comme le fond.
        // dExit = distance à la SORTIE de l'eau par la surface (rayon montant) ; sinon ∞ (ne ressort jamais).
        // dGeom = distance à la géométrie opaque. dPath = min(dExit, dGeom). Fenêtre de Snell (droit au-dessus)
        //         = dExit court → claire ; surface rasante / horizon = dExit énorme → fog plein ; fond = dGeom.
        float3 rayDir = normalize(posInput.positionWS);            // camera-relative = direction monde (Y = up)
        float  dExit  = (rayDir.y > 1e-3) ? (_OceanWaterLevel - camAbsY) / rayDir.y : 1e9;

        float dGeom = 1e9;
        if (depth != UNITY_RAW_FAR_CLIP_VALUE)
        {
            dGeom = length(posInput.positionWS);
            float3 pAbs = GetAbsolutePositionWS(posInput.positionWS);
            // Caustiques sur la géométrie IMMERGÉE si c'est bien elle qu'on voit (avant la sortie d'eau).
            // Le fondu par la profondeur est fait DANS ComputeOceanCaustics par la profondeur DU FOND/objet
            // (topologie-correct : hauts-fonds éclairés, creux éteints) — PAS par la profondeur caméra (qui
            // effacerait à tort les caustics d'un objet peu profond quand la caméra est profonde). L'assombrissement
            // global avec la profondeur caméra est déjà porté par lightFactor.
            if (pAbs.y < _OceanWaterLevel - 0.1 && dGeom <= dExit)
                color.rgb *= 1.0 + ComputeOceanCaustics(pAbs, _OceanWaterLevel);
        }

        float  dPath = min(min(dExit, dGeom), 400.0);
        float3 T     = exp(-sigma * dPath);
        // Milieu (extinction + in-scattering) PUIS luminosité ambiante (assombrit tout avec la profondeur).
        color.rgb = (color.rgb * T + inScatter * (1.0 - T)) * lightFactor;

        // ── GOD-RAYS (radiance in-scatterée le long du rayon de vue) : dernière opération sous-marine.
        // Rendus en amont dans _OceanGodRayTex (demi-res, OceanGodRayLowResPass) ; on échantillonne en
        // bilinéaire (upscale invisible car basse fréquence) et on AJOUTE. Faire l'ajout ICI (et non dans une
        // passe composite séparée) garantit l'ordre : le fog ne peut plus les écraser. Assombris aussi par
        // lightFactor (les rayons faiblissent en profondeur, comme le reste du milieu).
        if (_OceanGodRaysEnabled > 0.5)
        {
            float2 grUV = varyings.positionCS.xy * _ScreenSize.zw;
            float3 gr   = SAMPLE_TEXTURE2D_X_LOD(_OceanGodRayTex, s_linear_clamp_sampler, grUV * _RTHandleScale.xy, 0).rgb;
            color.rgb  += gr * lightFactor;
        }
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
