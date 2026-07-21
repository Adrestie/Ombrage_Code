// OceanGodRays.hlsl  (Ocean_v2)
// GOD-RAYS sous-marins = rayons de lumière volumétriques, portage de la technique V1 dans l'archi V2.
// Raymarch le long du RAYON DE VUE ; à chaque échantillon, reprojection à la SURFACE le long de la
// direction du faisceau (entre vertical et soleil) où l'intensité = COURBURE de la surface FFT (divergence
// des normales via SampleOceanNormal — MÊME machinerie que les caustics → les rayons suivent les vagues
// GRATUITEMENT). PAS de shadow map (comme V1). Dither IGN pour casser le banding.
//
// Inclus APRÈS OceanSurfaceCascadeSampling.hlsl (SampleOceanNormal) + OceanCaustics.hlsl
// (_OceanSunDirection, _OceanWaterLevel déjà déclarés). Additif dans la passe underwater.
#ifndef OCEAN_GODRAYS_INCLUDED
#define OCEAN_GODRAYS_INCLUDED

// ── Globaux (poussés par OceanVolumetricsModule ; interrupteur par OceanSurfaceModule) ──
float  _OceanGodRaysEnabled;          // 0/1 (module Volumetrics actif)
float4 _OceanGodRayColor;             // teinte des rayons
float  _OceanGodRayIntensity;         // force globale (0 = éteint)
float  _OceanGodRayMaxDist;           // portée du raymarch le long du rayon de vue (m)
float  _OceanGodRayBeamThresholdLo;   // seuils de courbure (dérivés de sharpness côté C#)
float  _OceanGodRayBeamThresholdHi;
float  _OceanGodRayBeamScale;         // échelle du voisinage de courbure (m)
float  _OceanGodRaySunFollow;         // 0 = vertical, 1 = aligné soleil
float  _OceanGodRayDepthFade;         // fondu selon la profondeur VERTICALE du sample
float  _OceanGodRayExtinction;        // fondu le long du rayon de vue
float  _OceanGodRayFadeInDepth;       // profondeur caméra d'apparition
float  _OceanGodRaySteps;             // nombre de pas de raymarch

// Bruit à gradient entrelacé (IGN) — dither du point de départ des pas (anti-banding), sur les pixels.
float _OceanGodRayIGN(float2 pix)
{
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
}

// Hash de valeur (port V1) : bruit stable par cellule pour varier la taille des faisceaux.
float _OceanBeamHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Motif de faisceau au point d'entrée en surface : convergence = −divergence des normales ≈ −Laplacien(h)
// (surface concave → focalisation → rayon brillant). Seuillé par la netteté (lo/hi dérivés de sharpness).
float _OceanGodRayBeam(float2 surfaceXZ)
{
    float  eps = max(_OceanGodRayBeamScale, 1e-3);
    // Normale COMPLÈTE (toutes cascades) : la COURBURE (dérivée 2de) est dominée par les hautes fréquences
    // → les cascades fines (256²) créent le pic de courbure qui DÉCLENCHE le faisceau. Les retirer effondre
    // la courbure sous le seuil = plus de god-rays. (Le flou efface le détail spatial, pas ce qui le génère.)
    float3 nC = SampleOceanNormal(surfaceXZ);
    float3 nX = SampleOceanNormal(surfaceXZ + float2(eps, 0.0));
    float3 nZ = SampleOceanNormal(surfaceXZ + float2(0.0, eps));
    float  divN = ((nX.x - nC.x) + (nZ.z - nC.z)) / eps;
    float  beam = smoothstep(_OceanGodRayBeamThresholdLo, _OceanGodRayBeamThresholdHi, -divN);

    // VARIATION DE TAILLE par cellule (port V1) : casse l'uniformité → faisceaux d'épaisseurs variées,
    // certains plus fins/discrets. La cellule suit la surface (échelle liée à _OceanGodRayBeamScale).
    float2 cell          = floor(surfaceXZ * _OceanGodRayBeamScale * 0.5);
    float  sizeVariation = lerp(0.4, 1.0, _OceanBeamHash(cell));
    return beam * sizeVariation;
}

// camAbsPos     = position ABSOLUE monde de la caméra.
// viewDirWS     = direction du rayon de vue (monde, normalisée).
// marchDist     = distance max dans l'eau (min géométrie / sortie de surface).
// camDepthBelow = profondeur de la caméra sous la surface (m).
// waterLevel    = Y absolu du plan d'eau (passé en param : _OceanWaterLevel est déclaré APRÈS cet include).
// pix           = coordonnées pixel (dither).
// Retour : contribution ADDITIVE (radiance) des rayons.
float3 ComputeOceanGodRays(float3 camAbsPos, float3 viewDirWS, float marchDist, float camDepthBelow, float waterLevel, float2 pix)
{
    if (_OceanGodRaysEnabled < 0.5 || _OceanGodRayIntensity < 1e-3)
        return 0.0;

    // Direction du faisceau : entre vertical (bas) et le soleil, forcée vers le bas (anti-explosion rasant).
    float3 beamDir = normalize(lerp(float3(0.0, -1.0, 0.0), _OceanSunDirection.xyz, saturate(_OceanGodRaySunFollow)));
    beamDir.y = min(beamDir.y, -0.1);

    // Look « MARQUÉ » (verrouillé après R&D) : faisceaux contrastés, nombre de pas medium (≤12). La FORME
    // vient d'un exposant sur le faisceau : beamShape > 1 resserre/contraste les rayons.
    int   steps     = (int)clamp(_OceanGodRaySteps, 4.0, 12.0);
    float beamShape = 1.6;

    float maxDist  = min(marchDist, _OceanGodRayMaxDist);
    float stepSize = maxDist / steps;
    float jitter   = _OceanGodRayIGN(pix);

    float accum = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float  t          = stepSize * (i + 0.5 + jitter);
        float3 sampleAWS  = camAbsPos + viewDirWS * t;
        float  depthBelow = waterLevel - sampleAWS.y;
        if (depthBelow < 0.0) break;                                  // sample sorti de l'eau
        float  tUp        = depthBelow / (-beamDir.y);
        float2 surfaceXZ  = sampleAWS.xz - beamDir.xz * tUp;          // remontée à la surface le long du faisceau
        float  beam       = pow(saturate(_OceanGodRayBeam(surfaceXZ)), beamShape);
        float  proximity  = exp(-depthBelow * _OceanGodRayDepthFade); // brillant près surface, faible profond
        float  atten      = exp(-t * _OceanGodRayExtinction);         // fondu le long du rayon de vue
        accum += beam * proximity * atten * stepSize;
    }

    // ANTI-BLOWOUT (compression DOUCE au-dessus d'un genou) : accum est une intégrale (Σ·stepSize) → un rayon
    // rasant reste longtemps peu profond (beam+proximity forts) et l'intégrale EXPLOSE en bande blanche. Diviser
    // par la distance éteindrait TOUT (beam déjà faible). On laisse donc le rendu normal INCHANGÉ sous le genou,
    // et on écrase en douceur (Reinhard) seulement l'excès → la bande surexposée fond, les faisceaux normaux restent.
    const float knee = 2.0;
    float over = max(accum - knee, 0.0);
    accum = min(accum, knee) + over / (1.0 + over * 0.5);

    float depthFadeIn = smoothstep(0.0, max(_OceanGodRayFadeInDepth, 1e-3), camDepthBelow); // apparition en descendant
    float horizonFade = smoothstep(0.1, 0.4, -beamDir.y);                                   // coupe si soleil rasant
    return accum * _OceanGodRayColor.rgb * _OceanGodRayIntensity * depthFadeIn * horizonFade;
}

#endif // OCEAN_GODRAYS_INCLUDED
