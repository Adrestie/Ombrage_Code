// OceanSurfaceTessellation.hlsl  (Ocean_v2)
// Hooks de TESSELLATION du contrat HDRP (réutilisation INTÉGRALE de TessellationShare.hlsl : HullConstant
// / Hull / Domain / culling / chemin MotionVectors sont ceux de HDRP — on ne fournit QUE les 3 fonctions
// que le framework appelle). Inclus PAR PASSE, après Lit*Pass.hlsl (qui définit AttributesMesh /
// VaryingsMeshToDS) et AVANT le driver ShaderPass*.hlsl (qui inclut TessellationShare).
//
//   GetMaxDisplacement()            : enveloppe de déplacement (cull frustum + bounds). Auto-dérivée (C#).
//   GetTessellationFactor()         : facteur PAR SOMMET, gaté distance, QUANTIFIÉ, caméra de référence
//                                     SNAPPÉE → constant frame-à-frame entre deux paliers (STABILITÉ MV).
//   ApplyTessellationModification() : déplacement xyz dans le domain ; échantillonne le tampon N-1 lors du
//                                     rejeu par la passe MV (timeParameters = _LastTimeParameters).
//
// Gating distance NATIF HDRP désactivé côté C# (_TessellationFactorMaxDistance/TriangleSize = 0) : tout le
// gating vit ici, dans GetTessellationFactor, pour pouvoir QUANTIFIER + SNAPPER (la voie native utilise
// GetPrimaryCameraPosition() en continu → facteur variant chaque frame → MV instables au sous-palier).
#ifndef OCEAN_SURFACE_TESSELLATION_INCLUDED
#define OCEAN_SURFACE_TESSELLATION_INCLUDED

#include "Assets/Shader/Ocean_v2/Shaders/OceanSurfaceCascadeSampling.hlsl"

// NOTE : _OceanTessMinDist / _OceanTessMaxDist / _OceanTessQuantLevels / _OceanRefCamSnap /
// _OceanMaxDisplacement ET _TessellationFactor sont des propriétés MATÉRIAU déclarées dans le cbuffer
// UnityPerMaterial d'OceanSurface.shader (poussées par OceanSurfaceModule via material.SetFloat).

// Enveloppe de déplacement (m) — frustum cull epsilon + extension de bounds. Auto-dérivée des cascades (C#).
float GetMaxDisplacement()
{
    return max(_OceanMaxDisplacement, 0.01);
}

// Facteur de tessellation PAR SOMMET (HDRP en fait la moyenne par arête dans HullConstant).
// Gating distance STRICT : retombe à 1.0 au-delà de _OceanTessMaxDist (la tessellation s'éteint, le coût
// hull/domain fixe demeure — doc HDRP 17). Quantifié en paliers + caméra de référence snappée → stabilité MV.
float GetTessellationFactor(AttributesMesh input)
{
    float3 posRWS = TransformObjectToWorld(input.positionOS);
    float3 absVert = GetAbsolutePositionWS(posRWS);
    // Origine camera-relative → position absolue de la caméra (rendu camera-relative HDRP).
    float3 absCam  = GetAbsolutePositionWS(float3(0.0, 0.0, 0.0));

    // SNAP de la caméra de référence sur une grille quantifiée → la distance de gating ne varie pas en
    // continu entre deux frames (condition de stabilité MV avec tessellation, doc HDRP 17).
    float snap = max(_OceanRefCamSnap, 0.0);
    float3 camRef = (snap > 1e-4) ? floor(absCam / snap) * snap : absCam;

    // Distance horizontale (océan ~planaire vu de dessus).
    float d = distance(absVert.xz, camRef.xz);

    float denom = max(_OceanTessMaxDist - _OceanTessMinDist, 1e-3);  // > 0 garanti par OnValidate (anti-NaN)
    float t = saturate(1.0 - (d - _OceanTessMinDist) / denom);

    // QUANTIFICATION en paliers discrets (floor(x+0.5) = round déterministe inter-GPU).
    float levels = max(_OceanTessQuantLevels, 1.0);
    float q = floor(t * levels + 0.5) / levels;

    // _TessellationFactor = facteur MAX (poussé par C#). Hors portée → 1.0 (tessellation OFF).
    return max(1.0, lerp(1.0, _TessellationFactor, q));
}

// Déplacement xyz complet dans le domain (Gerstner horizontal + hauteur), lu des cascades.
// Le framework HDRP appelle cette fonction avec _TimeParameters (position courante) dans les passes
// normales, et la REJOUE avec _LastTimeParameters dans MotionVectorTessellation pour la position N-1.
// On détecte le rejeu N-1 et on échantillonne alors le tampon _OceanDispPrev* (cohérence T/T-1, pattern PR #4418).
VaryingsMeshToDS ApplyTessellationModification(VaryingsMeshToDS input, float3 timeParameters)
{
    float3 absPos = GetAbsolutePositionWS(input.positionRWS);

    // Rejeu de la passe MV ⇔ timeParameters = _LastTimeParameters ⇔ x diffère du temps courant.
    bool usePrev = (timeParameters.x != _TimeParameters.x);
    // prev=current (⇒ MV nuls, pas de flash/smear) quand _OceanMVValid=0. Le C# (OceanSurfaceModule.
    // BindMotionVectors) met _OceanMVValid=0 dans TOUS les cas de discontinuité : (ré)allocation du
    // tampon prev (1er frame / toggle / domain reload), incohérence de structure current↔prev au switch
    // de preset (fenêtre d'un frame), et saut réel du champ de vagues (slider LookDev état de mer/ampli).
    if (usePrev && _OceanMVValid < 0.5) usePrev = false;

    float3 disp = SampleOceanDisplacement(absPos.xz, usePrev);
    input.positionRWS += disp;   // disp est un vecteur de déplacement (invariant d'espace) → RWS reste correct
    return input;
}

#endif // OCEAN_SURFACE_TESSELLATION_INCLUDED
