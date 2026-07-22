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

// Valeur de foam texturée façon V1.
//   foamAmount : quantité de foam en entrée (surfaceFoam + customFoam)
//   posXZ      : position (object space) pour l'UV du motif
float OmbrageFoamValue(float foamAmount, float2 posXZ)
{
    float m = saturate(foamAmount);
    float2 uv = posXZ * _OmbrageFoamTiling;

    float hi = SAMPLE_TEXTURE2D(_OmbrageFoamTexHigh, s_linear_repeat_sampler, uv).r;
    float lo = SAMPLE_TEXTURE2D(_OmbrageFoamTexLow,  s_linear_repeat_sampler, uv).r;

    // Dissipation -> crêtes selon la quantité de foam.
    float pattern = lerp(lo, hi, saturate(m * _OmbrageFoamBlend));
    float detail  = lerp(0.3, 1.0, pattern);
    return saturate(m * detail);
}

#endif // OMBRAGE_WATER_FOAM_HLSL
