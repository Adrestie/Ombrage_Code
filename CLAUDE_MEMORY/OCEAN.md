# OCEAN

Création : 2026-07-17
Dernière modification : 2026-07-20 (flip forward + réfraction/caustiques + Snell ray march)

> Mémoire durable (digest agent). Le **détail canonique** vit in-tree dans
> `Assets/Shader/Ocean_v2/` — voir §Liens. Les pièges d'implémentation vont dans `PIEGES.md`.

## État actuel
Réécriture **from-scratch** de l'océan (dossier `Assets/Shader/Ocean_v2/`), 100 % HLSL custom +
compute, HDRP 17.4. Périmètre V1 = **pleine mer seule**, direction réaliste-stylisé (réf. *Sea of
Thieves* / *AC Black Flag*). Coexiste avec l'ancien `Assets/Shader/Ocean/` (= « V1 » historique) laissé
**intact** jusqu'à migration des scènes. Spectre FFT, surface, tessellation, absorption, écume,
réflexions, **see-through/réfraction**, **caustiques** (au-dessus ET sous l'eau) et **fenêtre de Snell**
livrés. Reste : fog/god-rays sous-marins, shore, wake. La nomenclature « P2/P6/G3… » a été **retirée**
des scripts (ne veut rien dire pour l'utilisateur) — les docs `OCEAN_*.md` en gardent encore.

**FONDATION (changement majeur) : surface passée de OPAQUE DEFERRED → TRANSPARENT FORWARD** pour
permettre une vraie transparence / réfraction (voir décision). Conséquence : le tag stencil GBuffer
n'existe plus → le sous-marin/Snell a été **re-cadré** (voir décision).

**Architecture à paliers (OceanParameter)** : chaque concept = un module ; chaque valeur = un
`OceanParameter<T>` avec override (décoché = valeur par défaut validée ; coché = valeur personnalisée).
Module désactivé = concept absent ; interrupteur poussé par un module TOUJOURS actif (surface) pour
éviter les états périmés (cf. absorption/réfraction/caustiques/underwater).

Architecture code : `OceanProfile` (SO données) + `OceanFeatureModule` (abstrait ; modules :
spectre/surface/absorption/reflection/**refraction**/**caustics**/underwater/volumetrics/shore/wake) +
`OceanSystem` (`[ExecuteAlways]`, `ReconcileEnabled()` + `DisableAndForget()` : `active` pilote vraiment
le cycle de vie). Namespace `Ombrage.OceanFeatures` = **assembly dédié** (`.asmdef` + `.Editor`, réf.
HDRP + Core Runtime), PAS `Assembly-CSharp`. Découverte des modules par `TypeCache` + attribut
`[OceanModuleMenu("Categorie/NomAffiché")]`. Scène de test : `Assets/Shader/Ocean_v2/Ocean Debug.unity`.

## Décisions d'architecture
### Décision : Chemin de rendu = surface TRANSPARENTE FORWARD (ex-opaque deferred)
Date : 2026-06-28 (deferred) · **flippé forward 2026-07** (validé utilisateur)
Choix : la surface est un Lit **transparent forward** (`_SURFACE_TYPE_TRANSPARENT`, pass `LightMode
"Forward"`, file Transparent, `Blend SrcAlpha OneMinusSrcAlpha`, `Cull Off` double-face, `ZWrite On`).
Raison : une vraie transparence / **réfraction du fond** exige le Forward (accès au color pyramid) —
impossible en deferred opaque. Le choix deferred initial (cohérence herbe/terrain) est abandonné.
Conséquence : plus de GBuffer pour la surface → tout effet qui lisait le GBuffer (stencil, normal
buffer) doit être re-cadré côté forward (cf. Snell).

### Décision : See-through / RÉFRACTION du fond = composite custom (color pyramid)
Date : 2026-07 (validé)
Choix : `OceanRefractionModule` (affiché « Refraction »). Dans la passe forward de la surface
(`OceanSurfaceData.hlsl`) on composite NOUS-MÊMES le fond : opacité `t` = Beer-Lambert sur la
**longueur de trajet 3D** dans l'eau (distance surface→fond, vue-dépendante) ; le fond est lu dans le
color pyramid (`SampleCameraColor`) à un UV **distordu par la normale des vagues** ; sortie opaque
(alpha=1) via l'émissif. Paramètres : `clarityDistance`, `distortionStrength`.
Raison : HDRP Lit refraction (objets solides, épaisseur constante) inadapté à l'eau ; le composite
custom permet la distorsion et le contrôle du mélange. **Prérequis : « Rough Refraction » activé**
(HDRP Asset + Frame Settings) sinon le color pyramid est noir.

### Décision : Caustiques = modulation du fond réfracté (portage V1)
Date : 2026-07 (validé, au-dessus + sous l'eau)
Choix : `OceanCausticsModule` (affiché « Caustics »). Motif = Laplacien du champ de hauteur via
`SampleOceanNormal` (normale analytique), dispersion chromatique, fondu profondeur ; **projeté le long
du rayon solaire** (`_OceanSunDirection`) → suit le soleil + correct sur surfaces verticales.
Appliqué en `bg *= 1+caustics` DANS le bloc réfraction (au-dessus) ET dans `OceanUnderwater.shader`
sur la géométrie immergée (sous l'eau). Paramètres : intensity/scale/maxDepth/chromaSpread.
Raison : concept possédé par son module, consommé aux deux endroits (comme l'absorption).

### Décision : Sous-marin = fenêtre de Snell DANS le shader de surface + colonne d'eau en CustomPass
Date : 2026-06-28 · re-cadré 2026-07 (post-flip forward)
Choix : la **fenêtre de Snell** (surface vue de dessous : voûte émergée comprimée dans un cône θc≈48.6°,
TIR au-delà) est rendue **dans le shader de surface** (forward double-face s'exécute déjà de dessous) —
**plus de tag stencil** (mort depuis le flip). Contenu de la fenêtre = **scène réelle émergée** lue dans
le color pyramid par échantillonnage dans la **direction réfractée** via une **marche screen-space** (type
SSR) le long du rayon réfracté `P+refr·s` : vraie 1ʳᵉ intersection avec le depth buffer (croisement des
profondeurs eye) + dichotomie → `windowUV = project(impact)` (N=24/K=6 constantes shader ; distance max
exposée = `_OceanSnellMaxReach`, paramètre `OceanParameter` du module Underwater, défaut 60 m). Corrige le décalage des objets (l'ancienne correction 1-passe recalait sur
le rayon caméra, pas le rayon réfracté → biais de parallaxe ; cf. `PIEGES.md`). Replis screen-space : rayon
hors écran → échantillon droit distordu ; ciel → direction du rayon. Le `CustomPass BeforePostProcess`
(`OceanUnderwater.shader`) ne fait plus que la **colonne
d'eau** (absorption + caustiques) sur la **géométrie immergée** (`worldY < niveau d'eau` — séparation
GÉOMÉTRIQUE, robuste sans stencil). Submersion caméra calculée **in-shader par-caméra** (pas Camera.main).
`OceanUnderwaterModule` pousse angle de Snell + densité + portée max (`_OceanSnellMaxReach`) ; `OceanSurfaceModule` pousse « module actif » +
`_OceanWaterLevel` + `_OceanSunDirection`. Reste : fog/god-rays (volumetrics HDRP), éclairage sous-marin.

### Décision : Réflexions = HDRP natif (ciel + Planar Probe built-in) — P5 validée
Date : 2026-06-28 · livrée/validée 2026-07-18
Choix : la surface étant un **Lit deferred standard**, HDRP applique la **hiérarchie de réflexion**
automatiquement (ciel/cubemap → planar probe → SSR) — désormais en **forward** (voir flip). `OceanReflectionModule`
crée une **Planar Reflection Probe HDRP built-in** (plus optimisée qu'une RT maison) au niveau d'eau
(realtime), avec **gating immergé** (§1.3 : sonde OFF quand la caméra passe sous l'eau). **SSR OFF en
V1** (Q5.1, testé sans artefact = compat V1.5 OK). Sonde sous-marine différée (Q5.2).
Prérequis : un **Volume + Sky dans la scène** (l'océan ne fournit pas de ciel).
Dé-risque **Q3.4 LEVÉ** : la planar probe HDRP réfléchit correctement sur la surface HLSL custom.
**T1 provisoire** : sous-marin reste V1.5 (coût planar = re-rendu de scène, non mesurable sur scène vide).

### Décision : Géométrie = tessellation hardware gatée distance (~200k tris)
Date : 2026-06-28 · confirmée par mesure 2026-07-05
Choix : subdivision GPU gatée strictement par distance, budget ~200k tris. Pivot **mesh clipmap**
conservé en repli (Q3.4) mais NON déclenché — la mesure build P2 a validé la tessellation.
Raison : densité de crêtes élevée près caméra sans lattice CPU. Grille runtime marquée CLIPMAP_READY.

### Décision : Simulation FFT
Date : 2026-06-28
Choix : vrai JONSWAP (γ≈3.3) + branche TMA `tanh(kh)` **dormante** en V1 (deep-water 191 m) ;
**4 cascades** golden-ratio ; **résolution mixte** par cascade (512² porteuses / 256² détail) ;
**IFFT hermitienne 2-en-1** + **dérivées analytiques** en domaine spectral (normales/Jacobien exacts).
Raison : réalisme, anti-répétition, perf (moitié moins d'IFFT), et corrige à la source 2 des 3 bugs
interdits (normales analytiques ; normalisation 1/N découplée de l'amplitude).

### Décision : Absorption = Beer-Lambert Jerlov unifié (P3 validée)
Date : 2026-06-28 · calibré/validé 2026-07-17
Choix : UN seul modèle, 3 σ (m⁻¹, absorption pure a(λ)), source de vérité UNIQUE (`_WaterAbsorption`)
partagée surface + sous-marin. 3 ancres Jerlov Ia/II/III, master `waterType [0..1]` par segments.
Scattering = V1.5 (froxels), hors V1.
- **Couleur vue de dessus = réflectance MONTANTE** `b_b/σ × maturité(d) × kUpwellingScale` (b_b =
  rétrodiffusion Rayleigh eau pure, constante) — PAS la transmittance `exp(−σd)` (qui rend turquoise).
  `maturité = 1−exp(−2·σ_norm·kDepthRate·d)`, profondeur optique normalisée par σ̄. `kUpwellingScale=0.02`
  et `kDepthRate=0.04` = **constantes shader statiques** (`OceanSurfaceData.hlsl`).
- Paramètres profil (`OceanAbsorptionModule`) : `waterType` [0..1] (défaut 0) + **`colorBuildup`** [0.1..50]
  (défaut 15 ; ex-`perceivedDepth` — développement de la couleur de la colonne, PAS la distance au fond).
- Ancres σ : **Ia (0.36, 0.07, 0.028)** · II (0.45, 0.09, 0.15) · III (0.55, 0.20, 1.10).
Raison : met fin à l'incohérence « 2 modèles d'absorption indépendants » de l'ancien océan.
Réserve : calibrage colorimétrique qualitatif (RGB écran dépend de l'éclairage) ; look « bleu profond »
dépendra aussi des réflexions (P5) et du sous-marin/réfraction (P6). Snapshot `.backups/09_P3_absorption_validated/`.

### Décision : Écume = carte world-locked, Q7.1 amendée (P4 validée)
Date : 2026-07-17 (validée au gate visuel)
Choix : écume (crêtes seules Q7.2 + persistance Q7.3) calculée dans **UNE carte world-locked**
(RHalf ping-pong mippée, `foamResolution` 256..2048 — résolution **découplée** de la longueur de
tuile, exigence utilisateur). Couverture = seuil doux erf sur **J_total ≈ 1 + Σ(Jxx+Jzz)** filtré à
une **échelle physique fixe 2 m** (P1 stocke la divergence `s=Jxx+Jzz` en `.w`, linéaire → sommable/
mippable). Persistance = `max(prev·exp(−fade·dt_réel), cov)`. Surface : échantillonnage à la position
**NON-déplacée** (`q ≈ p − D(p)`), **LOD par distance caméra**, rupture procédurale 2 échelles.
Params : `jacobianThreshold` (0.7 ≈ 4 % à mer formée), `foamFadeRate`, `foamResolution`.
Raison : le littéral Q7.1 (déterminant par cascade + variance Dupuy au footprint) est **mesuré
inapplicable** en carte world-space (cascade fine J∈[0.08..2.6] → union saturée ; variance dégénérée
→ binaire ou gris 40 %). Amendement documenté : `OCEAN_DECISIONS.md §A1` ; chronique des 7 bugs :
`OCEAN_TEST_P4.md`. NB : correctif P1 au passage — **signe du déplacement choppy inversé**
(silhouette crêtes/creux) ; contrat P1 `.w` = s (ex-déterminant).

### Décision : UX data-driven + WindZone partagé
Date : 2026-06-28
Choix : 1 master (`oceanState`) + valeurs dérivées en % + presets SO data-driven (6 presets Beaufort)
+ 4 presets qualité (`OceanQualityProfile`) + clamps `OnValidate`. Vent = **WindZone de scène
partagé** (comme herbe/terrain), `windSpeed` en facteur relatif, globals `_OceanWind*`.
Raison : cohérence projet, corrige les douleurs d'audit (110 champs nus, 0 clamp, presets en dur).

## Contraintes
- Cible matérielle **RTX 2060 → RTX 4080** (plancher 2060, pas GTX 1060).
- Budget GPU océan **2–4 ms** (4 ms = plafond dur). Mesure P2 build RTX 2060 (Ultra, D3D12) =
  **2.564 ms** (GBuffer 0.169 + Ocean.Spectrum 1.252 + MotionVector 1.143). Marge ~1.4 ms pour
  P3/P5/P6. ⚠ Poste MotionVector (copie T-1) = candidat d'optimisation n°1, différé.
- **Zéro dépendance externe** (pas de Crest, KWS, HDRP Water).
- **Pas de git** : versionnage par snapshots `.backups/<NN_nom>/` ; validation utilisateur à chaque phase.
- **3 bugs interdits** re-vérifiés à chaque phase : soleil cumulatif non restauré · H₀ réinit par
  frame · normalisation IFFT 1/N couplée à l'amplitude. Push globals = assignation pure restaurable
  (`OceanGlobalCache`, `RestoreAll()` au Teardown).
- **Exposition de l'émissif** : toute valeur mise dans `builtinData.emissiveColor` (fond réfracté,
  fenêtre de Snell) DOIT être en radiance brute (× `GetInverseCurrentExposureMultiplier()`), sinon
  écrasée en noir en extérieur (HDRP re-multiplie l'émissif par l'exposition). Cf. `PIEGES.md`.

## Choix techniques
### 100 % HLSL custom + compute (pas de Shader Graph)
Utilisation : contrôle total du pipeline océan, cohérence avec herbe/terrain.
Justification : calibre AAA/AA visé, découplage numérique fin (hermitien, dérivées analytiques)
impossible proprement en Shader Graph.

## Interfaces
- **WindZone de scène** partagé avec herbe/terrain (un seul vent pilote tout).
- **Globals partagés** : `_OceanWind*`, `_OceanDisp*/_OceanDeriv*/_OceanCascade*` (cascades),
  `_WaterAbsorption` (σ extinction, surface + sous-marin + fog) + `_OceanScatterColor` (couleur affichée art-directed A3), `_OceanDispPrev*/_OceanMVValid` (motion vectors T-1),
  `_OceanWaterLevel` + `_OceanSunDirection` (poussés par la surface, fondamentaux partagés),
  `_OceanRefraction*` / `_OceanCaustics*` (réfraction/caustiques), `_OceanUnderwaterEnabled` (module actif).
- Modules `shore` / `wake` **stubs** (non implémentés).

## Versions
[2026-07-17] Création de la mémoire (audit du corpus de plan + code).
[2026-07-17] P3 (absorption) validée : rename `perceivedDepth`→`colorBuildup`, ancre Ia σ_G 0.041→0.07,
`kUpwellingScale` gardé statique. Snapshot `.backups/09_P3_absorption_validated/`.
[2026-07-17] P4 (écume) validée : carte world-locked, Q7.1 amendée (§A1), correctif signe choppy P1,
7 bugs (chronique `OCEAN_TEST_P4.md`), 5 pièges Unity → PIEGES.md. Snapshot `.backups/11_P4_foam_validated/`.
[2026-07-18] P5 (réflexions) validée : ciel HDRP + Planar Probe built-in + gating immergé (données) +
SSR compat OK (OFF en V1). asmdef → réf HDRP. T1 provisoire (sous-marin V1.5). Snapshot `13_P5_reflection_validated/`.
[2026-07-18] P6 démarrée : G1 double-sided (Cull Off) + G2 absorption immergée (CustomPass BeforePostProcess,
σ partagé) validés. G3 Snell cadré (`OCEAN_TEST_P6.md`). Checkpoint `14b_P6_G1G2_checkpoint/`.
[2026-07-20] **Refonte majeure** (branche `claude/ocean-g3-stencil-tag-vz72t9`) : (1) architecture
OceanParameter à paliers (override enable/valeur) sur tous les modules ; (2) surface **flippée opaque
deferred → transparent forward** ; (3) **see-through/réfraction** custom (color pyramid) = module
Refraction ; (4) **caustiques** portées V1→V2 (au-dessus + sous l'eau) = module Caustics ; (5) **Snell**
re-cadré dans le shader de surface (stencil supprimé), submersion in-shader par-caméra ; (6) nomenclature
de plan retirée des scripts. Piège exposition émissive → `PIEGES.md`.
[2026-07-20] Sous-marin G4 (hybride) : fog = HDRP natif (glow lité, albedo dérivé de la couleur d'eau) +
god-rays = passe custom courbure FFT (à venir G4.2). Extinction spectrale reste custom (σ, HDRP monochrome
ne sait pas). Amendement A2 (`OCEAN_DECISIONS.md`).
[2026-07-20] Couleur de l'eau ART-DIRECTED (amendement A3) : `waterColor` maître (global `_OceanScatterColor`,
look découplé de σ) + `absorptionColor` (ordre d'absorption, override défaut physique) + `clarity` (magnitude)
+ `colorBuildup`. σ dérivé de la couleur (`σ ∝ b_b/waterColor` par défaut). Ancres Jerlov → presets éditeur.
Modèle b_b/σ d'upwelling (k3/k4) retiré du shader. Calage visuel magnitude/gradient dû.
[2026-07-20] Fog sous-marin UNIFIÉ (passe underwater, par longueur de trajet d'eau `min(dExit,dGeom)`,
surface vue de dessous incluse) + **visibilité & lumière pilotées par la PROFONDEUR** (module Underwater,
6 params : viewMinDist/viewMaxDist/viewReduceAtDepth/minViewAtDepth + lightReduceAtDepth/minLightAtDepth).
Absorption ne pousse qu'un σ **normalisé** (couleur/ordre) ; magnitude = 1/viewDist(profondeur). `clarity`/
`underwaterDensity` retirés. Globals `_OceanScatterColor` + `_OceanView*`/`_Ocean*LightAtDepth`.
[2026-07-20] Fenêtre de Snell : placement des objets corrigé — passage de la correction de profondeur
1-passe (biais de parallaxe) à une **marche screen-space** (vraie intersection du rayon réfracté). Piège
« reprojection 1-passe ≠ marche le long du rayon » → `PIEGES.md`.

## Liens
- `Assets/Shader/Ocean_v2/OCEAN_DECISIONS.md` — table canonique 41/41 décisions (**rang 1**).
- `Assets/Shader/Ocean_v2/OCEAN_ROADMAP.md` — plan exécutif, phases P0→P10, budget, tensions.
- `Assets/Shader/Ocean_v2/OCEAN_IMPLEMENTATION_STATUS.md` — avancement détaillé.
- `Assets/Shader/Ocean_v2/OCEAN_GUIDELINES.md` — cahier des charges + specs A–D.
- `CLAUDE_MEMORY/PIEGES.md` — pièges HDRP/shader/build (dont modèle couleur eau, tessellation).
