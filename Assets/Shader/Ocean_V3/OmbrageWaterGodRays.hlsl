#ifndef OMBRAGE_WATER_GODRAYS_HLSL
#define OMBRAGE_WATER_GODRAYS_HLSL

// =============================================================================
// Ombrage — Contrôle découplé des god-rays underwater
// -----------------------------------------------------------------------------
// Injecté dans le point NATIF de HDRP : VolumetricLighting.compute, bloc
// `if (underWater)`, là où le pipeline module la lumière volumétrique du soleil
// par le motif de caustics de la surface. On remplace les deux lignes natives
// (modulation caustic + absorption en profondeur) par une modulation pilotable,
// SANS toucher aux paramètres globaux de l'eau (donc sans effet de bord sur le
// reste du rendu underwater).
// -----------------------------------------------------------------------------
// Paramètres live poussés par le C# Ombrage.Visual.Ocean (OmbrageWaterGodRaysController) :
//   .x = intensity | .y = reach | .z = contrast   (.w inutilisé)
// Non défini (aucun contrôleur en scène) => intensity 0 => FALLBACK NATIF EXACT.
// Fonction pure : toutes ses entrées sont passées en argument (aucune dépendance
// d'ordre d'include hormis la déclaration de _OmbrageGodRaysParams ci-dessous).
// =============================================================================

float4 _OmbrageGodRaysParams;

// Multiplicateur appliqué à lightColor.a. Remplace EXACTEMENT les deux lignes natives :
//     lightColor.a *= 1.0 + caustic * causticsIntensity;
//     lightColor.a *= exp(-distanceToSurface * extinction);
//   caustic           : valeur du motif de caustics (~0..5, 1 = neutre)
//   causticsIntensity : _UnderWaterCausticsIntensity (global HDRP)
//   distanceToSurface : profondeur verticale sous la surface (m)
//   extinction        : _UnderWaterScatteringExtinction.w (extinction moyenne)
float OmbrageGodRaysModulation(float caustic, float causticsIntensity, float distanceToSurface, float extinction)
{
    float intensity = _OmbrageGodRaysParams.x;
    float reach     = _OmbrageGodRaysParams.y;
    float contrast  = _OmbrageGodRaysParams.z;

    // Aucun contrôleur (ou intensité nulle) => comportement HDRP natif inchangé.
    if (intensity <= 0.0)
        return (1.0 + caustic * causticsIntensity) * exp(-distanceToSurface * extinction);

    // Contraste (punch GoT) : accentue les cœurs focalisés, creuse les gaps.
    // contrast = 1 => identique au natif ; > 1 => faisceaux plus marqués.
    float shaped = pow(max(caustic, 0.0), contrast);

    // Intensité dédiée aux shafts, découplée des caustics de surface.
    // intensity = 1 => identique au natif.
    float modulation = 1.0 + shaped * causticsIntensity * intensity;

    // Portée : réduit l'extinction perçue par les SEULS shafts (reach > 1 =>
    // faisceaux plus longs) sans éclaircir le reste de l'eau. reach = 1 => natif.
    // Plafond dur non contournable en shader : la portée du VBuffer = Fog Depth Extent.
    float absorption = exp(-distanceToSurface * extinction / max(reach, 1e-3));

    return modulation * absorption;
}

#endif // OMBRAGE_WATER_GODRAYS_HLSL
