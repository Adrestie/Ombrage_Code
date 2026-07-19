// OceanCaustics.hlsl  (Ocean_v2)
// Caustiques = focalisation de la lumière par la surface, appliquée en MODULATION MULTIPLICATIVE du
// fond réfracté (comme la V1 : sceneColor *= 1 + caustics). Motif = LAPLACIEN du champ de hauteur
// (divergence de la pente) : une surface concave (Laplacien < 0) fait CONVERGER la lumière → caustique
// brillante. La V1 échantillonnait des normal-maps FFT ; ici on porte l'algo sur la normale ANALYTIQUE
// de V2 (SampleOceanNormal = pente sommée des cascades), par différences finies aux points voisins.
//
// L'animation vient GRATUITEMENT de l'évolution des cascades chaque frame (pas de scroll UV / vitesse).
//
// Inclus APRÈS OceanSurfaceCascadeSampling.hlsl (SampleOceanNormal déjà défini).
#ifndef OCEAN_CAUSTICS_INCLUDED
#define OCEAN_CAUSTICS_INCLUDED

// ── Globaux ─────────────────────────────────────────────────────────────────
// _OceanCausticsEnabled = interrupteur (0/1), poussé par OceanSurfaceModule (comme absorption/réfraction).
// Les 3 valeurs sont poussées par OceanCausticsModule (SEULE source ; périmées mais inertes si off).
float _OceanCausticsEnabled;
float _OceanCausticsScale;       // échelle spatiale (m) du voisinage d'échantillonnage
float _OceanCausticsIntensity;   // force globale (0 = éteint)
float _OceanCausticsMaxDepth;    // profondeur (m) de fondu
float _OceanCausticsChroma;      // dispersion chromatique (m) — 0 = monochrome

// Luminosité caustique en un point : convergence = −divergence de la pente ≈ −Laplacien de la hauteur.
// On prend la normale analytique en 3 points voisins (centre, +x, +z) et on estime la divergence.
float _OceanCausticSample(float2 worldXZ, float eps)
{
    float3 nC = SampleOceanNormal(worldXZ);
    float3 nX = SampleOceanNormal(worldXZ + float2(eps, 0.0));
    float3 nZ = SampleOceanNormal(worldXZ + float2(0.0, eps));
    // n.x = −slopeX/‖·‖, n.z = −slopeZ/‖·‖ → (Δn.x + Δn.z)/eps ≈ −(∂slopeX/∂x + ∂slopeZ/∂z) = −Laplacien(h).
    float curvature = ((nX.x - nC.x) + (nZ.z - nC.z)) / eps;
    return smoothstep(0.0, 2.0, -curvature);   // seuil 2.0 = calage V1 (ajustable au gate visuel)
}

// worldXZ            = position ABSOLUE monde du FOND (là où la lumière se focalise).
// depthBelowSurface  = distance VERTICALE (m) fond→surface (positive).
// Retour : valeur RGB additive (dispersion chromatique) destinée à  bg *= 1 + c.
float3 ComputeOceanCaustics(float2 worldXZ, float depthBelowSurface)
{
    if (_OceanCausticsEnabled < 0.5 || _OceanCausticsIntensity < 1e-3 || depthBelowSurface <= 0.0)
        return 0.0;

    float eps = max(_OceanCausticsScale, 1e-3);   // voisinage en MÈTRES (SampleOceanNormal prend du monde)

    // Décalages chromatiques : DIRECTIONS constantes (reprises de V1), amplitude = chroma (m).
    const float2 kOffR = float2( 1.0, 0.0);
    const float2 kOffB = float2(-0.5, 0.7);
    float chroma = _OceanCausticsChroma;

    float3 c;
    c.r = _OceanCausticSample(worldXZ + kOffR * chroma, eps);
    c.g = _OceanCausticSample(worldXZ,                  eps);
    c.b = _OceanCausticSample(worldXZ + kOffB * chroma, eps);

    // Fondu profondeur : au-delà de maxDepth, la lumière est dispersée → plus de caustiques.
    float depthFade = 1.0 - smoothstep(0.0, max(_OceanCausticsMaxDepth, 1e-3), depthBelowSurface);
    return c * depthFade * _OceanCausticsIntensity;
}

#endif // OCEAN_CAUSTICS_INCLUDED
