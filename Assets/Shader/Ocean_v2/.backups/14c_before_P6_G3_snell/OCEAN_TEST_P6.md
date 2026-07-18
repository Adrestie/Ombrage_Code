# OCEAN_TEST_P6.md — Phase P6 (Dessus/dessous + sous-marin) — EN COURS

> **Statut : P6 EN COURS.** G1 (double-sided) + G2 (absorption immergée) **validés utilisateur (2026-07-18)**.
> G3 (Snell) cadré ci-dessous, à démarrer en session dédiée. Checkpoint : `.backups/14b_P6_G1G2_checkpoint/`.
> Rollback amont : `.backups/14_before_P6_underwater/`.

## Cadrage acté (session 2026-07-18) — « tes recos »
- **Q3.1** : surface opaque deferred + **CustomPass sous-marin séparé** (post-GBuffer, `BeforePostProcess`).
- **Q4.1** : transition + ménisque en V1 ; **Snell en V1.5 = ce P6**. **Q4.2** : approche tranchée au proto.
- **Q9.1** : effet complet (fog + god-rays + Snell + absorption partagée + single-scattering). Seul ajout réel = god-rays. Éclairage = modulation NON destructive (anti-bug n°1).
- **Q6.3** : single-scattering = **volumetric HDRP natif** (pas de froxels custom). **Q-P6.2 validé**.
- Caustiques = **P7** (hors P6). **T1** re-validée après P6.
- **Staging 5 gates** : G1 double-sided → G2 absorption → G3 Snell → G4 fog/god-rays → G5 éclairage non destructif + mesure underwater → re-valid T1.

## Architecture (5 sous-systèmes)
| | Sous-système | Livré ? |
|---|---|---|
| A | Raccord dessus/dessous : surface **double-sided** (Cull Off) + normale face-caméra | **✅ G1** |
| B | Compositing immergé : CustomPass `BeforePostProcess`, absorption Beer-Lambert σ partagé | **✅ G2** |
| C | **Fenêtre de Snell** (θc≈48.6° + réflexion totale interne) | 🔜 **G3** |
| D | Fog volumétrique + god-rays = **volumetrics HDRP** pilotés par σ | 🔜 G4 |
| E | Éclairage sous-marin = modulation non destructive du soleil | 🔜 G5 |

## G1 — double-sided ✅
`OceanSurface.shader` : `Cull Back → Cull Off` sur **GBuffer, DepthOnly, MotionVectors** (ShadowCaster/Picking restent Back). La normale est déjà retournée vers la caméra dans `OceanSurfaceData.hlsl` (`dot(normalWS,V)<0`) → éclairage cohérent des 2 faces, aucune régression vue de dessus (le front-face gagne le ZTest). **Gate** : caméra sous l'eau → surface visible de dessous (pas de trou).

## G2 — absorption immergée ✅
`Shaders/OceanUnderwater.shader` (fullscreen CustomPass, template HDRP `CustomPassCommon.hlsl`) :
`color.rgb *= exp(−σ·d)` avec `d = length(posInput.positionWS)` (clampé 400 m), `σ = _WaterAbsorption.rgb`
(**MÊME global que la surface**, Q6.1). Gaté `_OceanUnderwaterEnabled` (1 en immersion). `_OceanUnderwaterDistScale`
= `underwaterDensity` [0.1..4]. `OceanUnderwaterModule` crée `CustomPassVolume` (BeforePostProcess, global) +
`FullScreenCustomPass` (`fetchColorBuffer=true`), gate `Camera.main.y < waterY`, push SET pur (anti-bug n°1).
**Gate** : vue immergée s'assombrit + teinte bleu-vert (rouge absorbé en premier). Le lointain vire sombre
(absorption pure ; le glow bleu = G4). Validé.

---

## 🔜 CADRAGE G3 — Fenêtre de Snell (à démarrer en session dédiée)

### Le défi (tension d'architecture)
La surface est **OPAQUE** (Q3.1, pour l'éclairage deferred). Or la fenêtre de Snell = **voir le ciel À TRAVERS
la surface** depuis dessous : tout l'hémisphère émergé se compresse dans un cône de demi-angle
**θc = arcsin(1/1.33) ≈ 48.6°** autour de la verticale ; **hors du cône = réflexion totale interne** (on voit
l'environnement sous-marin réfléchi sur la sous-face). Avec une surface opaque, on ne voit PAS à travers
nativement → le **CustomPass doit reconstruire** le ciel réfracté (dans le cône) et la TIR (hors cône).

### Deux approches (à trancher au démarrage G3)
1. **Approximation écran-espace (pragmatique, reco de départ)** : dans le CustomPass immergé, pour chaque
   pixel, calculer l'angle du rayon de vue vs la verticale (`up`). Dans le cône (θ<θc) → échantillonner le
   **ciel HDRP** (cubemap / `_SkyTexture` ou la reflection probe) dans la direction réfractée (loi de Snell) ;
   hors cône → **TIR** = réflexion de l'environnement sous-marin (au minimum : teinte d'absorption + réflexion
   du ciel atténuée, ou simplement l'eau sombre). Falloff doux au bord du cône. **Pas de tag surface requis.**
   Rapide, lisible ; imperfection : ne distingue pas finement « pixel = surface » vs « pixel = objet immergé ».
2. **Précis, surface taggée au stencil** : ajouter un **bit stencil dédié à la surface océan** (au GBuffer) ;
   le CustomPass ne fait la logique Snell QUE sur les pixels de surface vus de dessous (réfraction exacte du
   rayon + échantillonnage sky). Plus juste, mais **invasif** (touche le stencil de la surface P2, risque de
   régression LightLoop) et plus coûteux à valider.

**Reco : démarrer par (1) l'approximation écran-espace** (le rendu « fenêtre de Snell » est surtout un effet
de composition ; l'approximation lit bien) ; passer à (2) seulement si le gate visuel l'exige.

### Points d'attention G3
- Le **`up` du plan d'eau** = monde +Y (grille horizontale). La normale de réfraction = +Y.
- Le ciel HDRP est accessible en CustomPass : préférer la **reflection probe ambiante / `_SkyTexture`** (le
  cubemap de ciel), pas un re-rendu.
- **Ordre avec G2** : Snell s'applique à la vue immergée regardant vers le haut ; l'absorption G2 s'applique
  ensuite (ou avant) — décider l'ordre (physiquement : la lumière du ciel réfractée traverse ensuite la colonne
  d'eau → absorption APRÈS le sample sky ; donc G3 remplace la couleur « ciel » puis G2 l'absorbe, OU on fait
  les deux dans le même pass dans le bon ordre).
- **Gating immersion** déjà en place (`_OceanUnderwaterEnabled`).
- **Anti-bug n°1** : aucune mutation d'état partagé (le soleil, c'est G5).

### Questions ouvertes G3 (à poser au démarrage)
- Q-G3.1 : approche (1) écran-espace vs (2) stencil ? (reco = 1)
- Q-G3.2 : hors-cône (TIR) — réflexion réelle de l'environnement sous-marin, ou approximation (eau sombre +
  ciel atténué) ? (reco = approximation d'abord)
- Q-G3.3 : le ciel réfracté doit-il être **absorbé** par la colonne d'eau au-dessus de la caméra (plus la
  caméra est profonde, plus la fenêtre est sombre) ? (reco = oui, cohérent Q6.1)

## État de reprise (pour une nouvelle session)
- **Caméra** restaurée à (0, 9.02, −10) rot 0 ; pour tester en immersion : la descendre sous y=0 (ex. (0,−6,0)).
- **Scène** : `Ocean Debug` (ciel PBS conservé, root `Cube` = utilisateur). Profil : modules Spectrum/Surface/
  Absorption/Reflection/Underwater actifs.
- **Réglages validés** : oceanState 0.606, chop 1.2, tile 193, extent 100, useTMA OFF ; foam seuil 0.7 fade 1.0
  res 1024 ; absorption waterType/colorBuildup 15 ; reflection influenceExtent 200 ; underwaterDensity 1.
- **Checkpoint** : `.backups/14b_P6_G1G2_checkpoint/` (92 fichiers + SHA-256).
