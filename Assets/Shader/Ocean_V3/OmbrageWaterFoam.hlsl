#ifndef OMBRAGE_WATER_FOAM_HLSL
#define OMBRAGE_WATER_FOAM_HLSL

// =============================================================================
// Ombrage — Aspect foam custom (portage du look V1)
// -----------------------------------------------------------------------------
// Remplace l'aspect de FoamErosion natif par un blend de DEUX textures foam
// (crêtes haute-fréquence + dissipation basse-fréquence), piloté par la quantité
// de foam — exactement le foamAlpha de la V1 (OceanSurface.shader:368-374).
// Appliqué à TOUTE la foam (crêtes simulation + edge foam Ombrage), via le
// branchement dans EvaluateFoamData (WaterUtilities.hlsl).
//
// Opt-in : sans contrôleur en scène, _OmbrageFoamEnabled = 0 => fallback natif.
// Textures + params poussés en global par OmbrageWaterFoamController (Assets).
// Les textures foam ne sont pas dans git : assignées dans l'inspector.
// =============================================================================

TEXTURE2D(_OmbrageFoamTexHigh);   // crêtes (haute fréquence), canal R
TEXTURE2D(_OmbrageFoamTexLow);    // dissipation (basse fréquence), canal R

float _OmbrageFoamEnabled;
float _OmbrageFoamTiling;
float _OmbrageFoamBlend;

// -----------------------------------------------------------------------------
// Ombrage — edge foam d'empreinte (collier autour des objets émergents).
// Déclaré ICI (et non dans SampleWaterSurface) car EvaluateFoamData qui le
// consomme se compile aussi côté compute (WaterSimulation/WaterDeformation),
// hors du garde fragment-only de SampleWaterSurface.
//   - Intensity/Noise/NoiseScale : poussés par OmbrageEdgeFoamController.
//   - StampRT + Region : poussés par OmbrageFoamHeightCapture
//     (Region : xy = centre monde, z = 1/taille, w = taille).
// -----------------------------------------------------------------------------
float _OmbrageEdgeFoamIntensity;
float _OmbrageEdgeFoamWidth;       // (réservé étape 2)
float _OmbrageEdgeFoamNoise;       // casse le bord (organique)
float _OmbrageEdgeFoamNoiseScale;  // échelle du bruit

TEXTURE2D(_OmbrageFoamStampRT);
float4 _OmbrageFoamRegion;

// DEBUG (temporaire) : force la foam sur TOUTE la surface, en court-circuitant la
// capture/région/binding. Sert à isoler « la foam se rend-elle du tout ? » vs
// « la capture est-elle vide ? ». Piloté par OmbrageEdgeFoamController (Debug Flood).
float _OmbrageEdgeFoamDebug;

float _OmbrageEdgeHash(float2 p)
{
    p = frac(p * float2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return frac(p.x * p.y);
}

// Value noise lissé (pour perturber le bord de l'edge foam).
float _OmbrageEdgeNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = _OmbrageEdgeHash(i);
    float b = _OmbrageEdgeHash(i + float2(1, 0));
    float c = _OmbrageEdgeHash(i + float2(0, 1));
    float d = _OmbrageEdgeHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Valeur de foam texturée façon V1.
//   foamAmount : quantité de foam en entrée (surfaceFoam + customFoam)
//   posXZ      : position (object space) pour l'UV du motif
float OmbrageFoamValue(float foamAmount, float2 posXZ)
{
    float m = saturate(foamAmount);

    // Scroll par le courant (repris de FoamErosion natif). Dual-sample décalé dans
    // le temps + blend -> l'écume dérive avec le courant sans étirement.
    float2 currentDirection = OrientationToDirection(_PatchOrientation[0]);
#if defined(WATER_LOCAL_CURRENT)
    currentDirection = SampleWaterGroup0CurrentMap(posXZ);
    float sinC, cosC;
    sincos(_GroupOrientation[0], sinC, cosC);
    currentDirection = float2(cosC * currentDirection.x - sinC * currentDirection.y,
                              sinC * currentDirection.x + cosC * currentDirection.y);
#endif
    currentDirection *= 3.0;
    float2 lerpF = frac(_SimulationTime * 0.5 * _FoamCurrentInfluence + float2(0.0, 0.5));
    float2 uvA = (posXZ - currentDirection * lerpF.x) * _OmbrageFoamTiling;
    float2 uvB = (posXZ - currentDirection * lerpF.y) * _OmbrageFoamTiling;
    float  lf  = (_FoamCurrentInfluence > 0.0) ? pow(cos(lerpF.x * PI), 2.0) : 0.0;

    float hi = lerp(SAMPLE_TEXTURE2D(_OmbrageFoamTexHigh, s_linear_repeat_sampler, uvA).r,
                    SAMPLE_TEXTURE2D(_OmbrageFoamTexHigh, s_linear_repeat_sampler, uvB).r, lf);
    float lo = lerp(SAMPLE_TEXTURE2D(_OmbrageFoamTexLow,  s_linear_repeat_sampler, uvA).r,
                    SAMPLE_TEXTURE2D(_OmbrageFoamTexLow,  s_linear_repeat_sampler, uvB).r, lf);

    // Dissipation -> crêtes selon la quantité de foam.
    float pattern = lerp(lo, hi, saturate(m * _OmbrageFoamBlend));
    float detail  = lerp(0.3, 1.0, pattern);
    return saturate(m * detail);
}

#endif // OMBRAGE_WATER_FOAM_HLSL
