#ifndef OMBRAGE_WATER_GODRAYS_HLSL
#define OMBRAGE_WATER_GODRAYS_HLSL

// =============================================================================
// Ombrage — God-rays underwater
// Injection DA dans le Water System HDRP forké (look Ghost of Tsushima).
// -----------------------------------------------------------------------------
// ÉTAPE 2 — RAYMARCH.
// Pour chaque pixel d'eau sous la surface, on marche le long du rayon
// caméra -> pixel. À chaque pas : (1) le point est-il éclairé par le soleil
// (shadow map) ? (2) l'eau y diffuse de la lumière vers la caméra, concentrée
// vers le soleil (phase function), (3) atténuée par l'absorption sur le trajet
// (transmittance). La somme = l'in-scatter = le faisceau. Les ombres creusent
// les gaps entre faisceaux ; l'absorption fait le fondu (pas de couture dure).
// -----------------------------------------------------------------------------
// Dépendances (fournies par WaterLighting.compute, qui inclut ce fichier EN
// DERNIER) : IsUnderWater (UnderWaterUtilities), _DirectionalLightDatas /
// GetDirectionalShadowAttenuation / InitShadowContext (LightLoop + HDShadow),
// _UnderWaterScatteringExtinction / _DirectionalShadowIndex (globals HDRP),
// CornetteShanksPhasePartVarying / InterleavedGradientNoise (core).
// Fichier volontairement couplé au fork, pas un include partagé générique.
// -----------------------------------------------------------------------------
// Paramètres : codés en #define pour cette étape (itération rapide du look).
// Câblage propre via le contrôleur C# Ombrage.Visual.Ocean à l'étape suivante.
// =============================================================================

#define OMBRAGE_GODRAYS_INTENSITY     2.0    // valeur maîtresse (multiplie l'in-scatter)
#define OMBRAGE_GODRAYS_STEP_COUNT    16     // pas de raymarch (plancher RTX 2060)
#define OMBRAGE_GODRAYS_MAX_DISTANCE  60.0   // borne du trajet marché (mètres) — coût
#define OMBRAGE_GODRAYS_ANISOTROPY    0.6    // g de la phase : concentration vers le soleil

// In-scatter des god-rays pour le pixel courant.
//   posInput : infos du pixel d'eau (positionWS en RWS, positionSS).
//   V        : direction vue normalisée (pixel -> caméra), déjà calculée par le kernel.
float3 OmbrageEvaluateGodRays(PositionInputs posInput, float3 V)
{
    uint2 coord = (uint2)posInput.positionSS.xy;

    // Early-out : hors de l'eau, ou pas de soleil ombré exploitable.
    if (!IsUnderWater(coord) || _DirectionalShadowIndex < 0)
        return float3(0.0, 0.0, 0.0);

    // --- Soleil ---
    DirectionalLightData light = _DirectionalLightDatas[_DirectionalShadowIndex];
    float3 L = -light.forward;   // direction vers le soleil

    // --- Rayon caméra (origine RWS = 0) -> pixel d'eau ---
    float3 rayEnd = posInput.positionWS;            // RWS
    float  rayLen = length(rayEnd);
    if (rayLen < 1e-3)
        return float3(0.0, 0.0, 0.0);
    float3 rayDir = rayEnd / rayLen;                // = -V
    rayLen = min(rayLen, OMBRAGE_GODRAYS_MAX_DISTANCE);

    // --- Absorption de l'eau (moyenne scalaire ; version colorée à l'étape grading) ---
    float extinction = _UnderWaterScatteringExtinction.w;

    // --- Contexte d'ombre + dither temporel (anti-banding, résolu par le TAA) ---
    HDShadowContext shadowContext = InitShadowContext();
    float jitter = InterleavedGradientNoise((float2)coord, _FrameCount);
    float3 biasNormal = float3(0.0, 1.0, 0.0);      // normale neutre pour le biais d'ombre

    float stepLen = rayLen / OMBRAGE_GODRAYS_STEP_COUNT;
    float accum = 0.0;

    // [unroll] requis : GetDirectionalShadowAttenuation utilise des samples à
    // dérivées implicites qui interdisent une vraie boucle dynamique. STEP_COUNT
    // étant une constante de compilation, le déroulage est possible.
    [unroll]
    for (int i = 0; i < OMBRAGE_GODRAYS_STEP_COUNT; ++i)
    {
        // Position du pas le long du rayon (RWS), décalée par le jitter.
        float  dist = (i + jitter) * stepLen;
        float3 P = rayDir * dist;

        // 1. Le point est-il éclairé par le soleil ? (1 = éclairé, 0 = ombre)
        float shadow = GetDirectionalShadowAttenuation(shadowContext, (float2)posInput.positionSS.xy,
                                                       P, biasNormal, light.shadowIndex, L);

        // 2. Atténuation caméra -> P par l'absorption de l'eau.
        float transmittance = exp(-dist * extinction);

        accum += shadow * transmittance;
    }
    accum /= OMBRAGE_GODRAYS_STEP_COUNT;   // moyenne -> fraction éclairée bornée [0,1]

    // Phase : concentre l'in-scatter vers le soleil (halo / faisceaux marqués).
    float phase = CornetteShanksPhasePartVarying(OMBRAGE_GODRAYS_ANISOTROPY, dot(rayDir, L));

    // Teinte du soleil normalisée : on garde SA COULEUR, pas sa magnitude physique
    // (des milliers de nits) qui ferait exploser le rendu. La brillance vient de
    // l'intensité artistique + l'exposition de la scène (espace du buffer couleur).
    float3 sunTint = light.color / max(Max3(light.color.r, light.color.g, light.color.b), 1e-3);

    return accum * phase * sunTint * OMBRAGE_GODRAYS_INTENSITY * GetCurrentExposureMultiplier();
}

#endif // OMBRAGE_WATER_GODRAYS_HLSL
