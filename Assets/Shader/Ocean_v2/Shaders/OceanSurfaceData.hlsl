// OceanSurfaceData.hlsl  (Ocean_v2)
// REMPLACE HDRP/LitData.hlsl pour la surface océan : fournit GetSurfaceAndBuiltinData(), la seule
// fonction que chaque passe de shading/depth appelle. Tout le reste (encodage GBuffer, drivers de passe,
// InitBuiltinData/PostInitBuiltinData) est réutilisé VERBATIM du framework HDRP/Lit.
//
// Volontairement minimal, avec absorption :
//   - normalWS  : recomposée ANALYTIQUEMENT depuis les pentes des cascades (anti-bug n°2)
//   - baseColor : colonne d'eau Beer-Lambert = réflectance MONTANTE b_b/σ × maturité(d)
//                 sur la profondeur perçue (b_b = constante Rayleigh eau pure ; corrigé k3 — la
//                 transmittance exp(−σd) rendait turquoise ; k4 — profondeur optique normalisée par
//                 σ̄ pour que `perceivedDepth` reste discriminant à toute turbidité), σ =
//                 _WaterAbsorption (source UNIQUE) ; REPLI _BaseColor si module absorption
//                 absent/inactif (branche uniforme, 0 variant).
//                 Réflexions, écume et sous-marin viendront plus tard.
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
// Caustiques (module Caustics) : APRÈS le cascade sampling (utilise SampleOceanNormal). Module le fond
// réfracté dans le bloc réfraction ci-dessous ; inerte tant que _OceanCausticsEnabled = 0.
#include "Assets/Shader/Ocean_v2/Shaders/OceanCaustics.hlsl"

// ---- Absorption Beer-Lambert — GLOBAUX, HORS UnityPerMaterial (jamais dans Properties{}) ----
// _WaterAbsorption.rgb    = σ (m⁻¹) : SOURCE DE VÉRITÉ UNIQUE, poussée par OceanAbsorptionModule SEUL
//                           (SET pur non cumulatif via ctx.globals ; .w réservé, inutilisé).
// _OceanAbsorptionDepth   = profondeur perçue d (m) — consommation SURFACE en pleine mer (pas de fond) ;
//                           le futur CustomPass sous-marin lira le MÊME σ avec ses distances réelles.
// _OceanAbsorptionEnabled = interrupteur de consommation (0/1, poussé par OceanSurfaceModule) :
//                           branche UNIFORME (aucun variant/keyword) ; 0 → repli _BaseColor.
float4 _WaterAbsorption;
float  _OceanAbsorptionDepth;
float  _OceanAbsorptionEnabled;
// Interrupteur écume (0/1, poussé par OceanSurfaceModule.BindFoam) : branche uniforme, 0 variant.
// (Les moments + le seuil sont déclarés dans OceanSurfaceCascadeSampling.hlsl, avec les cascades.)
float  _OceanFoamEnabled;
// ── Réfraction (see-through du fond) — GLOBAUX, branche uniforme (0 variant) ────────────────────
// _OceanRefractionEnabled     = interrupteur (0/1), poussé par OceanSurfaceModule (comme l'absorption) :
//                               0 → pas de see-through, l'eau reste OPAQUE colorée (repli _BaseColor.a).
// _OceanRefractionClarityDist = distance de clarté (m) : trajet 3D dans l'eau au-delà duquel c'est opaque.
// _OceanRefractionDistort     = force de distorsion du fond par la normale des vagues (fraction d'écran).
// Les deux valeurs sont poussées par OceanRefractionModule (SEULE source ; périmées mais inertes si off).
float  _OceanRefractionEnabled;
float  _OceanRefractionClarityDist;
float  _OceanRefractionDistort;
// ── Sous-marin / fenêtre de Snell (poussés par OceanUnderwaterModule) ───────────────────────────
// _OceanUnderwaterEnabled = 1 quand la caméra est IMMERGÉE (et le module actif). Vue de dessous : la
//                           surface affiche la FENÊTRE DE SNELL au lieu du see-through (rendu ICI, plus
//                           de CustomPass/stencil). 0 → branche see-through normale.
// _OceanSnellCosThetaC    = cos(demi-angle du cône de Snell) — taille de la fenêtre (θc≈48.6° physique).
// _OceanWaterLevel        = Y absolu du plan d'eau (poussé par OceanSurfaceModule) — réservé/partagé.
float  _OceanUnderwaterEnabled;
float  _OceanSnellCosThetaC;
float  _OceanWaterLevel;

// Bruit de valeur procédural (monde non-déplacé) — casse les APLATS de couverture d'écume.
float OceanFoamHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float OceanFoamNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = OceanFoamHash(i);
    float b = OceanFoamHash(i + float2(1.0, 0.0));
    float c = OceanFoamHash(i + float2(0.0, 1.0));
    float d = OceanFoamHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Signature IDENTIQUE au site d'appel des drivers ShaderPass*.hlsl.
void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput,
                              out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);

    // ---- Normale analytique (pentes sommées des cascades) ----
    float3 fragAbs = GetAbsolutePositionWS(posInput.positionWS);
    float3 normalUp = SampleOceanNormal(fragAbs.xz);   // normale GÉOMÉTRIQUE montante (+Y) — pour Snell
    float3 normalWS = normalUp;
    // Surface double-face (Cull Off) : on garde la normale de SHADING face caméra (robuste des 2 côtés).
    if (dot(normalWS, V) < 0.0)
        normalWS = -normalWS;

    // ---- Matériau (Lit Standard, opaque, sans SSS) ----
    surfaceData.materialFeatures     = MATERIALFEATUREFLAGS_LIT_STANDARD;

    // ---- Couleur de la colonne d'eau : réflectance montante Beer-Lambert (k3 + k4) ----
    // La couleur vue de DESSUS n'est pas la transmittance exp(−σ·d) (elle rendait Ia turquoise :
    // G≈B survivent à égalité — constaté le 2026-07-05) mais la réflectance MONTANTE :
    //   R(λ) ∝ b_b(λ)/σ(λ) × maturité(d)
    // où b_b = rétrodiffusion spectrale de l'EAU PURE (Rayleigh ~λ⁻⁴, normalisée bleu) — une
    // CONSTANTE physique : ce n'est NI un paramètre, NI un scattering additionnel (aucun champ, aucune
    // variation par type d'eau — TOUTE la chromie par type vient de σ, source unique).
    //
    // CORRECTIF k4 (cohérence du slider `perceivedDepth`, 2026-07-06) :
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
    // anti-bug n°1 strictement intact. Tunable ; couleur écran produite par le LightLoop.
    // (Exposition en paramètre profil envisagée puis RETIRÉE — statique pour l'instant, cf. session.)
    const float3 kBackscatterSpectrum = float3(0.206, 0.422, 1.0);  // (650/450)^-4.3, (550/450)^-4.3, 1
    const float  kUpwellingScale = 0.02;
    const float3 kLumaWeights    = float3(0.2126, 0.7152, 0.0722);  // Rec.709 (pondération luminance)
    // kDepthRate abaissé 0.12 → 0.04 (correctif couleur 2026-07-06) :
    // à 0.12 le knee du canal moyen ≈ 1/(2·0.12) ≈ 4 m → sur les eaux turbides/intermédiaires (II/III)
    // tous les canaux saturaient AVANT ~25 m, d'où le PLATEAU résiduel de `perceivedDepth` sur [25..50]
    // (lectures user : Ia 25 m (0,37,93) ≈ 50 m (0,40,108)). À 0.04 le knee ≈ 1/(2·0.04) ≈ 12 m → la
    // maturité reste en transition sur toute la plage [0.1..50] pour TOUS les types (delta RGB non nul
    // 25↔50 m ; vérif : Δmaturité bleu Ia 25→50 m 0.166→0.241, Δvert III 0.023→0.202). Effet secondaire
    // assumé : la réflectance d'upwelling ∝ kDepthRate·d → le PROCHE (d≈0.1) et le défaut sont plus
    // sombres qu'à 0.12 ; compensé par le défaut `perceivedDepth` remonté (8 → 15) côté module. Fenêtre
    // de réglage ≈ 0.03–0.06 (constante shader, aucun global/slider/push — anti-bug n°1 intact).
    const float  kDepthRate      = 0.04;   // knee canal moyen ≈ 1/(2·kDepthRate) ≈ 12 m (tunable, ~0.03–0.06)
    float3 sigma     = max(_WaterAbsorption.rgb, 1e-4);       // plancher anti-NaN (division b_b/σ ET normalisation)
    float  sigmaMean = max(dot(sigma, kLumaWeights), 1e-4);  // ≥ 1e-4 (σ planché, poids>0) → diviseur jamais 0
    float3 sigmaNorm = sigma / sigmaMean;                    // profondeur optique NORMALISÉE (découple knee↔turbidité)
    float3 maturity  = 1.0 - exp(-2.0 * sigmaNorm * kDepthRate * _OceanAbsorptionDepth);
    float3 upwelling = kBackscatterSpectrum / sigma * maturity * kUpwellingScale;
    surfaceData.baseColor            = lerp(_BaseColor.rgb, saturate(upwelling), _OceanAbsorptionEnabled);
    surfaceData.perceptualSmoothness = _Smoothness;

    // ── Écume (crêtes seules) : couverture Dupuy instantanée, branche UNIFORME (0 variant).
    // kFoamAlbedo/kFoamSmoothness = CONSTANTES (blanc légèrement cassé, diffus/rugueux) — pas de
    // nouveaux sliders ; le seul réglage artistique est le seuil ε. La persistance/
    // traînée est un étage ultérieur : elle s'ajoutera en max() sans rien casser.
    float foamMask = 0.0;   // hoisté : réutilisé dans le composite see-through pour garder l'écume OPAQUE
    if (_OceanFoamEnabled > 0.5)
    {
        const float3 kFoamAlbedo     = float3(0.85, 0.88, 0.90);
        const float  kFoamSmoothness = 0.35;
        // L'écume vit dans le référentiel NON-DÉPLACÉ (le champ J est indexé par la position
        // d'ORIGINE des éléments de surface, comme la carte). Le fragment est à p = q + D(q) ;
        // inversion au 1ᵉʳ ordre : q ≈ p − D(p) (erreur O(D·∇D), invisible). Sans elle, l'écume
        // est décalée du déplacement horizontal (mètres) et « ne suit pas les vagues ».
        float2 undispXZ = fragAbs.xz - SampleOceanDisplacement(fragAbs.xz, false).xz;
        float cov = saturate(SampleOceanFoamCoverage(undispXZ, length(posInput.positionWS)));
        // Rupture procédurale (anti-aplat) : la couverture sert de SEUIL sur un motif monde à
        // 2 échelles (≈0.6 m + ≈0.17 m), échantillonné dans le référentiel des vagues → cœurs
        // pleins texturés aux fortes couvertures, franges déchiquetées aux bords.
        float n = 0.65 * OceanFoamNoise(undispXZ * 1.7) + 0.35 * OceanFoamNoise(undispXZ * 5.9);
        float foam = smoothstep(1.0 - cov, 1.0 - cov + 0.35, n);
        foamMask = foam;
        surfaceData.baseColor            = lerp(surfaceData.baseColor, kFoamAlbedo, foam);
        surfaceData.perceptualSmoothness = lerp(surfaceData.perceptualSmoothness, kFoamSmoothness, foam);
    }
    surfaceData.metallic             = _Metallic;
    surfaceData.ambientOcclusion     = 1.0;
    surfaceData.specularOcclusion    = 1.0;
    surfaceData.normalWS             = normalWS;
    surfaceData.geomNormalWS         = normalWS;

    surfaceData.tangentWS            = normalize(input.tangentToWorld[0]);
    surfaceData.tangentWS            = Orthonormalize(surfaceData.tangentWS, surfaceData.normalWS);
    surfaceData.specularColor        = 0.0;

    // Pas de diffusion profile ici (transmission/absorption sous-marine ultérieures).
    surfaceData.diffusionProfileHash = 0;
    surfaceData.subsurfaceMask       = 0.0;
    surfaceData.thickness            = 1.0;

    // See-through / RÉFRACTION CUSTOM : on compositie NOUS-MÊMES le fond réfracté dans la passe Forward,
    // plutôt que de laisser l'alpha-blend HDRP (qui ne pourrait pas DISTORDRE le fond). Modèle :
    //   opacité t = absorption de Beer-Lambert selon la LONGUEUR DU TRAJET 3D de la lumière DANS l'eau
    //   (distance surface→fond le long du rayon — vu du dessus d'un haut-fond : trajet court → transparent ;
    //   de loin/rasant : trajet long → opaque). Le fond opaque (terrain ET meshes) est lu dans le color
    //   pyramid à un UV DISTORDU par la normale des vagues → il ondule. Composite :
    //     couleur d'eau (éclairée) × t   +   fond réfracté (déjà éclairé, canal émissif) × (1−t)
    //   L'écume force t=1 (reste blanche/opaque) ; le spéculaire de surface (F0 diélectrique) reste par-dessus.
    //   Sortie OPAQUE (alpha=1, on a composité) ; on reste dans la file Transparent pour que le color pyramid
    //   soit disponible. GARDÉ à la passe Forward (les autres passes n'ont pas de scène/color cohérents ici).
    float alpha = _BaseColor.a;
    float3 refractTransmit = 0.0;   // fond réfracté transmis (émissif), nul hors Forward
#if (SHADERPASS == SHADERPASS_FORWARD)
    if (_OceanUnderwaterEnabled > 0.5)
    {
        // ══ FENÊTRE DE SNELL ══ caméra IMMERGÉE, on regarde la surface DE DESSOUS. Toute la voûte
        // émergée est comprimée dans un cône de demi-angle θc (≈48.6°) autour de la normale locale
        // (donc la fenêtre ONDULE avec les vagues) ; hors cône = réflexion totale interne (eau sombre).
        // Rendu ICI (surface forward double-face) → plus besoin du tag stencil GBuffer (cassé par le flip).
        float3 Vray      = normalize(posInput.positionWS);       // rayon caméra→fragment (camera-relative)
        float  cosInc    = dot(Vray, normalUp);                  // cos(angle vs normale montante)
        float  cosThetaC = _OceanSnellCosThetaC;
        float  sinThetaC = sqrt(saturate(1.0 - cosThetaC * cosThetaC));
        float  eta       = 1.0 / max(sinThetaC, 1e-3);           // n_eau = 1/sin(θc)  (n_air = 1)
        float3 refr      = refract(Vray, -normalUp, eta);        // eau→air ; renvoie 0 en TIR
        bool   isTIR     = dot(refr, refr) < 1e-6;

        // Contenu de la fenêtre : CIEL PROCÉDURAL (gradient horizon→zénith selon la direction réfractée)
        // + SOLEIL suivant la vraie directionnelle. Approximation crédible sans dépendance HDRP incertaine ;
        // le vrai ciel HDRP (_SkyTexture) pourra remplacer le gradient ultérieurement si besoin.
        const float3 kZenith = float3(0.20, 0.42, 0.75);
        const float3 kHoriz  = float3(0.55, 0.70, 0.85);
        float3 skyApprox = lerp(kHoriz, kZenith, saturate(refr.y));

        // Soleil dans la fenêtre : disque brillant + halo doux dans la direction du soleil (vers le HAUT =
        // −direction de propagation). Le rayon réfracté alignés au soleil → glare. Suit la rotation du soleil.
        float3 toSun = -_OceanSunDirection.xyz;
        float  toSunLen = length(toSun);
        if (toSunLen > 1e-3 && !isTIR)
        {
            float sunAlign = saturate(dot(refr, toSun / toSunLen));
            float sunDisc  = pow(sunAlign, 350.0) * 4.0;    // cœur brillant
            float sunHalo  = pow(sunAlign, 8.0)   * 0.25;   // halo doux
            skyApprox += (sunDisc + sunHalo) * float3(1.0, 0.96, 0.88);
        }

        const float3 colTIR = float3(0.004, 0.020, 0.030);       // TIR : approximation « eau sombre »
        float  inWindow = isTIR ? 0.0 : smoothstep(cosThetaC - 0.03, cosThetaC + 0.03, cosInc);
        float3 snell = lerp(colTIR, skyApprox, inWindow);

        // Absorption de la colonne d'eau traversée caméra→surface (σ PARTAGÉ) : plus la caméra est
        // profonde, plus la fenêtre s'assombrit. Distance = |positionWS| (camera-relative).
        float camDist = min(length(posInput.positionWS), 400.0);
        snell *= exp(-max(_WaterAbsorption.rgb, 0.0) * camDist);

        surfaceData.baseColor = 0.0;    // pas de diffuse de surface : on montre la fenêtre (émissif)
        refractTransmit = snell;        // lumière transmise (non ré-éclairée par le LightLoop)
        alpha = 1.0;
        // ═══ DEBUG TEMPORAIRE (à retirer) : MAGENTA = la branche Snell est bien atteinte. Si tu ne vois
        //     PAS de magenta en regardant vers le haut sous l'eau → _OceanUnderwaterEnabled n'atteint pas 1
        //     (immersion via Camera.main / module Underwater absent) OU la surface ne se rend pas de dessous.
        refractTransmit = float3(1.0, 0.0, 1.0);
    }
    else
    {
    float sceneDeviceDepth = LoadCameraDepth(posInput.positionSS);
    // Gate : module Refraction présent+actif (_OceanRefractionEnabled=1) ET un fond opaque derrière l'eau.
    // Sinon → pas de see-through, l'eau reste OPAQUE colorée (repli alpha = _BaseColor.a).
    if (_OceanRefractionEnabled > 0.5 && sceneDeviceDepth != UNITY_RAW_FAR_CLIP_VALUE)
    {
        float3 seabedWS  = ComputeWorldSpacePosition(posInput.positionNDC, sceneDeviceDepth, UNITY_MATRIX_I_VP);
        float  waterPath = distance(posInput.positionWS, seabedWS);   // trajet 3D DANS l'eau (m)
        float t = saturate(waterPath / max(_OceanRefractionClarityDist, 1e-3));   // opacité par trajet
        t = max(t, foamMask);             // l'écume reste OPAQUE

        // Fond réfracté : color pyramid échantillonné à un UV décalé par la pente des vagues (distorsion
        // plus forte en eau claire, nulle quand opaque). UV clampé pour ne pas sortir de l'écran.
        // ── Exposition : le color pyramid est stocké PRÉ-EXPOSÉ (×E), et HDRP RE-multiplie l'émissif
        //    par E → double exposition (fond quasi noir en extérieur). On annule une passe avec 1/E :
        //    (P·1/E) puis ×E côté framework = P (valeur correctement pré-exposée). Vérifié par le debug 3 bandes.
        float2 refrUV = saturate(posInput.positionNDC + normalWS.xz * _OceanRefractionDistort * (1.0 - t));
        float3 bg = SampleCameraColor(refrUV, 0.0) * GetInverseCurrentExposureMultiplier();

        // Caustiques : lumière focalisée par la surface sur le FOND réfracté (modulation multiplicative,
        // comme V1). Projetées le long du rayon SOLAIRE (suivi du soleil + correct sur surfaces verticales),
        // fondu par la profondeur verticale eau→fond. Inertes si module Caustics absent/inactif.
        float3 seabedAbs  = GetAbsolutePositionWS(seabedWS);
        float  surfaceAbsY = GetAbsolutePositionWS(posInput.positionWS).y;
        bg *= 1.0 + ComputeOceanCaustics(seabedAbs, surfaceAbsY);

        surfaceData.baseColor *= t;              // couleur d'eau proportionnelle à l'opacité
        refractTransmit = bg * (1.0 - t);        // fond transmis au complément (déjà éclairé)
        alpha = 1.0;                             // composite fait → sortie opaque
    }
    }   // fin else (see-through au-dessus de l'eau)
#endif

    // ---- Builtin (GI / APV / emissive) ----
    float3 bentNormalWS = surfaceData.normalWS;
    InitBuiltinData(posInput, alpha, bentNormalWS, -surfaceData.normalWS,
                    /*texCoord1*/ (float4)0.0, /*texCoord2*/ (float4)0.0, builtinData);
    // Fond réfracté injecté via l'émissif : la lumière du fond (déjà éclairée dans le color pyramid)
    // est TRANSMISE à travers l'eau, elle ne doit pas être réatténuée par l'éclairage de surface.
    builtinData.emissiveColor = refractTransmit;

    PostInitBuiltinData(V, posInput, surfaceData, builtinData);
}

#endif // OCEAN_SURFACE_DATA_INCLUDED
