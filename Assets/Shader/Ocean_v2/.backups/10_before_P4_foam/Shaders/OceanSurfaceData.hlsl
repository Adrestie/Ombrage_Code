// OceanSurfaceData.hlsl  (Ocean_v2 / P2)
// REMPLACE HDRP/LitData.hlsl pour la surface océan : fournit GetSurfaceAndBuiltinData(), la seule
// fonction que chaque passe de shading/depth appelle. Tout le reste (encodage GBuffer, drivers de passe,
// InitBuiltinData/PostInitBuiltinData) est réutilisé VERBATIM du framework HDRP/Lit.
//
// P2 (volontairement minimal) + P3 (absorption) :
//   - normalWS  : recomposée ANALYTIQUEMENT depuis les pentes des cascades P1 (anti-bug n°2)
//   - baseColor : colonne d'eau Beer-Lambert (P3, Q6.1) = réflectance MONTANTE b_b/σ × maturité(d)
//                 sur la profondeur perçue (b_b = constante Rayleigh eau pure ; corrigé k3 — la
//                 transmittance exp(−σd) rendait turquoise ; k4 — profondeur optique normalisée par
//                 σ̄ pour que `perceivedDepth` reste discriminant à toute turbidité), σ =
//                 _WaterAbsorption (source UNIQUE) ; REPLI _BaseColor si module absorption
//                 absent/inactif (branche uniforme, 0 variant).
//                 Réflexions = P5, écume = P4, sous-marin = P6.
//   - Lit STANDARD opaque, sans diffusion profile (stencil GBuffer Ref=2 = RequiresDeferredLighting).
//
// Inclus APRÈS Material.hlsl + Lit.hlsl (SurfaceData/BuiltinData, ENCODE_INTO_GBUFFER, InitBuiltinData,
// PostInitBuiltinData, Orthonormalize déjà définis), comme GrassBRGSurface.hlsl.
#ifndef OCEAN_SURFACE_DATA_INCLUDED
#define OCEAN_SURFACE_DATA_INCLUDED

// InitBuiltinData / PostInitBuiltinData (le chemin Lit les tire via LitData→LitBuiltinData ; comme on
// remplace LitData, on l'inclut nous-mêmes, exactement comme UnlitData.hlsl / GrassBRGSurface.hlsl).
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceCascadeSampling.hlsl"

// ---- Absorption Beer-Lambert (P3, Q6.1) — GLOBAUX, HORS UnityPerMaterial (jamais dans Properties{}) ----
// _WaterAbsorption.rgb    = σ (m⁻¹) : SOURCE DE VÉRITÉ UNIQUE, poussée par OceanAbsorptionModule SEUL
//                           (SET pur non cumulatif via ctx.globals ; .w réservé, inutilisé).
// _OceanAbsorptionDepth   = profondeur perçue d (m) — consommation SURFACE V1 (pleine mer, pas de fond) ;
//                           le futur CustomPass sous-marin (P6) lira le MÊME σ avec ses distances réelles.
// _OceanAbsorptionEnabled = interrupteur de consommation (0/1, poussé par OceanSurfaceModule) :
//                           branche UNIFORME (aucun variant/keyword) ; 0 → repli _BaseColor (P2).
float4 _WaterAbsorption;
float  _OceanAbsorptionDepth;
float  _OceanAbsorptionEnabled;

// Signature IDENTIQUE au site d'appel des drivers ShaderPass*.hlsl.
void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput,
                              out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);

    // ---- Normale analytique (pentes sommées des cascades) ----
    float3 fragAbs = GetAbsolutePositionWS(posInput.positionWS);
    float3 normalWS = SampleOceanNormal(fragAbs.xz);
    // Surface single-sided (Cull Back), mais on garde l'eau face caméra robuste.
    if (dot(normalWS, V) < 0.0)
        normalWS = -normalWS;

    // ---- Matériau (Lit Standard, opaque, sans SSS) ----
    surfaceData.materialFeatures     = MATERIALFEATUREFLAGS_LIT_STANDARD;

    // ---- Couleur de la colonne d'eau : réflectance montante Beer-Lambert (P3, Q6.1 — k3 + k4) ----
    // La couleur vue de DESSUS n'est pas la transmittance exp(−σ·d) (elle rendait Ia turquoise :
    // G≈B survivent à égalité — constat gate (d) 2026-07-05) mais la réflectance MONTANTE :
    //   R(λ) ∝ b_b(λ)/σ(λ) × maturité(d)
    // où b_b = rétrodiffusion spectrale de l'EAU PURE (Rayleigh ~λ⁻⁴, normalisée bleu) — une
    // CONSTANTE physique : ce n'est NI un paramètre, NI le scattering V1.5 (aucun champ, aucune
    // variation par type d'eau — TOUTE la chromie par type vient de σ, source unique Q6.1).
    //
    // CORRECTIF k4 (cohérence du slider `perceivedDepth`, gate (d) 2026-07-06) :
    // la maturité NAÏVE (1 − exp(−2·σ·d)) indexe son « coude » sur la MAGNITUDE de σ → pour une eau
    // turbide (σ élevé, type III) tous les canaux saturent avant d≈8 m, donc `perceivedDepth`
    // n'a plus AUCUN effet visible de 8 à 200 (bug rapporté). On DÉCOUPLE le taux de réponse en
    // profondeur de la magnitude chromatique en normalisant la profondeur optique par σ̄ (moyenne
    // pondérée luminance des MÊMES σ planchés) :
    //   σ̄ = dot(σ, poids_luma) ;  σ_norm = σ / σ̄  (chroma préservée, magnitude neutralisée)
    //   maturité(λ) = 1 − exp(−2·σ_norm·kDepthRate·d)
    // Le knee du canal MOYEN devient ≈ 1/(2·kDepthRate) INDÉPENDANT de la turbidité → `perceivedDepth`
    // discrimine sur toute sa plage pour TOUS les types (les canaux à fort σ_norm — rouge Ia,
    // rouge+bleu III — saturent plus tôt ; le bleu de Ia reste dans la transition sur [0.1..50]).
    // INVARIANTS préservés : d→0 ⇒ chromie b_b (bleu, σ̄ s'annule dans b_b/σ × σ_norm) ; d→∞ ⇒
    // b_b/σ (couleur asymptotique du type = HUE, porté ENTIÈREMENT par le triplet σ de l'ancre :
    // recalibré 2026-07-06 pour Ia bleu profond · II bleu-vert côtier · III VERT sombre — σ_b>>σ_g
    // installé sur III pour supprimer le bleu asymptotique, cf. assets Profiles/WaterAbsorption_*).
    // kUpwellingScale = fraction rétrodiffusée effective (échelle d'albédo UNIQUE). NB : aucun canal
    // asymptotique b_b/σ × kUpwellingScale n'atteint 1 sur les 3 ancres (max = Ia bleu 0.71) → le
    // saturate() ci-dessous ne CLIPPE jamais : à préserver lors de tout recalibrage des σ.
    // kDepthRate = constante shader (comme kUpwellingScale) : AUCUN nouveau global/slider/push →
    // anti-bug n°1 strictement intact. Tunable au gate (d) ; couleur écran produite par le LightLoop.
    // (Exposition en paramètre profil envisagée puis RETIRÉE — statique pour l'instant, cf. session.)
    const float3 kBackscatterSpectrum = float3(0.206, 0.422, 1.0);  // (650/450)^-4.3, (550/450)^-4.3, 1
    const float  kUpwellingScale = 0.02;
    const float3 kLumaWeights    = float3(0.2126, 0.7152, 0.0722);  // Rec.709 (pondération luminance)
    // kDepthRate abaissé 0.12 → 0.04 (correctif couleur 2026-07-06, gates (d)/(e) NON validées) :
    // à 0.12 le knee du canal moyen ≈ 1/(2·0.12) ≈ 4 m → sur les eaux turbides/intermédiaires (II/III)
    // tous les canaux saturaient AVANT ~25 m, d'où le PLATEAU résiduel de `perceivedDepth` sur [25..50]
    // (lectures user : Ia 25 m (0,37,93) ≈ 50 m (0,40,108)). À 0.04 le knee ≈ 1/(2·0.04) ≈ 12 m → la
    // maturité reste en transition sur toute la plage [0.1..50] pour TOUS les types (delta RGB non nul
    // 25↔50 m ; vérif : Δmaturité bleu Ia 25→50 m 0.166→0.241, Δvert III 0.023→0.202). Effet secondaire
    // assumé : la réflectance d'upwelling ∝ kDepthRate·d → le PROCHE (d≈0.1) et le défaut sont plus
    // sombres qu'à 0.12 ; compensé par le défaut `perceivedDepth` remonté (8 → 15) côté module. Fenêtre
    // de réglage au gate ≈ 0.03–0.06 (constante shader, aucun global/slider/push — anti-bug n°1 intact).
    const float  kDepthRate      = 0.04;   // knee canal moyen ≈ 1/(2·kDepthRate) ≈ 12 m (tunable au gate, ~0.03–0.06)
    float3 sigma     = max(_WaterAbsorption.rgb, 1e-4);       // plancher anti-NaN (division b_b/σ ET normalisation)
    float  sigmaMean = max(dot(sigma, kLumaWeights), 1e-4);  // ≥ 1e-4 (σ planché, poids>0) → diviseur jamais 0
    float3 sigmaNorm = sigma / sigmaMean;                    // profondeur optique NORMALISÉE (découple knee↔turbidité)
    float3 maturity  = 1.0 - exp(-2.0 * sigmaNorm * kDepthRate * _OceanAbsorptionDepth);
    float3 upwelling = kBackscatterSpectrum / sigma * maturity * kUpwellingScale;
    surfaceData.baseColor            = lerp(_BaseColor.rgb, saturate(upwelling), _OceanAbsorptionEnabled);
    surfaceData.perceptualSmoothness = _Smoothness;
    surfaceData.metallic             = _Metallic;
    surfaceData.ambientOcclusion     = 1.0;
    surfaceData.specularOcclusion    = 1.0;
    surfaceData.normalWS             = normalWS;
    surfaceData.geomNormalWS         = normalWS;

    surfaceData.tangentWS            = normalize(input.tangentToWorld[0]);
    surfaceData.tangentWS            = Orthonormalize(surfaceData.tangentWS, surfaceData.normalWS);
    surfaceData.specularColor        = 0.0;

    // Pas de diffusion profile en P2 (transmission/absorption sous-marine = P6).
    surfaceData.diffusionProfileHash = 0;
    surfaceData.subsurfaceMask       = 0.0;
    surfaceData.thickness            = 1.0;

    float alpha = _BaseColor.a;

    // ---- Builtin (GI / APV / emissive) ----
    float3 bentNormalWS = surfaceData.normalWS;
    InitBuiltinData(posInput, alpha, bentNormalWS, -surfaceData.normalWS,
                    /*texCoord1*/ (float4)0.0, /*texCoord2*/ (float4)0.0, builtinData);
    builtinData.emissiveColor = 0.0;

    PostInitBuiltinData(V, posInput, surfaceData, builtinData);
}

#endif // OCEAN_SURFACE_DATA_INCLUDED
