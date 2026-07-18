# OCEAN

Création : 2026-07-17
Dernière modification : 2026-07-18 (P4 clôturée)

> Mémoire durable (digest agent). Le **détail canonique** vit in-tree dans
> `Assets/Shader/Ocean_v2/` — voir §Liens. Les pièges d'implémentation vont dans `PIEGES.md`.

## État actuel
Réécriture **from-scratch** de l'océan (dossier `Assets/Shader/Ocean_v2/`), 100 % HLSL custom +
compute, HDRP 17.4. Périmètre V1 = **pleine mer seule**, direction réaliste-stylisé (réf. *Sea of
Thieves* / *AC Black Flag*). Coexiste avec l'ancien `Assets/Shader/Ocean/` laissé **intact** jusqu'à
migration des scènes. Phases P0 (scaffolding), P1 (spectre FFT), P2 (surface deferred), P3
(absorption), P4 (écume), **P5 (réflexions)** livrées et validées = **V1 fonctionnelle complète**.
**P6 (dessus/dessous + sous-marin, V1.5) EN COURS** : G1 double-sided + G2 absorption immergée validés ;
reste G3 Snell (cadré `OCEAN_TEST_P6.md`) / G4 fog+god-rays / G5 éclairage. Roadmap P0→P10.
Note assembly : `Ombrage.OceanFeatures.asmdef` référence désormais **HDRP Runtime + Core Runtime**
(depuis P5, pour `PlanarReflectionProbe`).

Architecture code : `OceanProfile` (SO données) + `OceanFeatureModule` (abstrait, **7 modules** :
spectre/surface/underwater/reflection/absorption/shore/wake) + `OceanSystem` (`[ExecuteAlways]`).
Namespace `Ombrage.OceanFeatures` = **assembly dédié** (`Ombrage.OceanFeatures.asmdef` + `.Editor`),
PAS `Assembly-CSharp`. Pattern 1:1 avec `TerrainProfile`/`TerrainFeatureModule`.
Scène de test : `Assets/Shader/Ocean_v2/Ocean Debug.unity`.

## Décisions d'architecture
### Décision : Chemin de rendu = surface opaque deferred + CustomPass sous-marin
Date : 2026-06-28
Choix : Surface opaque en GBuffer/DeferredOnly (éclairage LightLoop+ombres+APV) ; la vue à travers/
sous l'eau est reconstruite par un CustomPass séparé post-GBuffer (`BeforePostProcess`).
Raison : cohérence avec herbe/terrain (deferred) ; une eau réellement transparente forcerait le
Forward. Sépare rendu de surface et compositing immergé.

### Décision : Sous-marin = surface double-sided + CustomPass BeforePostProcess — P6 EN COURS
Date : 2026-06-28 · G1/G2 livrés 2026-07-18
Choix (Q3.1) : surface **opaque deferred** rendue **double-sided** (`Cull Off` sur GBuffer/DepthOnly/
MotionVectors ; normale retournée face-caméra) pour la voir de dessous ; **CustomPass `BeforePostProcess`**
(`OceanUnderwater.shader` fullscreen) pour le compositing immergé. **G1** (double-sided) et **G2**
(absorption `color*=exp(−σ·d)`, σ = `_WaterAbsorption` PARTAGÉ avec la surface, Q6.1) validés.
Reste **G3 Snell** (θc≈48.6° + TIR — défi : surface opaque → refraction/TIR reconstruites dans le
CustomPass ; 2 approches, reco = approximation écran-espace ; cadrage `OCEAN_TEST_P6.md`), **G4** fog +
god-rays = **volumetrics HDRP natifs** pilotés par σ (single-scattering Q6.3 obtenu nativement),
**G5** éclairage sous-marin = modulation NON destructive (anti-bug n°1). Caustiques = P7. T1 re-validée après P6.
`OceanUnderwaterModule` : CustomPassVolume + FullScreenCustomPass, gate immersion, push non destructif.

### Décision : Réflexions = HDRP natif (ciel + Planar Probe built-in) — P5 validée
Date : 2026-06-28 · livrée/validée 2026-07-18
Choix : la surface étant un **Lit deferred standard**, HDRP applique la **hiérarchie de réflexion**
automatiquement (ciel/cubemap → planar probe → SSR). P5 n'ajoute pas de shader : `OceanReflectionModule`
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
- Anti-transparence : la surface reste opaque (voir décision rendu).

## Choix techniques
### 100 % HLSL custom + compute (pas de Shader Graph)
Utilisation : contrôle total du pipeline océan, cohérence avec herbe/terrain.
Justification : calibre AAA/AA visé, découplage numérique fin (hermitien, dérivées analytiques)
impossible proprement en Shader Graph.

## Interfaces
- **WindZone de scène** partagé avec herbe/terrain (un seul vent pilote tout).
- **Globals partagés** : `_OceanWind*`, `_OceanDisp*/_OceanDeriv*/_OceanCascade*` (cascades P1),
  `_WaterAbsorption` (surface + sous-marin), `_OceanDispPrev*/_OceanMVValid` (motion vectors T-1).
- Module `shore` **dormant en V1** (activé V1.5 avec le côtier).

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

## Liens
- `Assets/Shader/Ocean_v2/OCEAN_DECISIONS.md` — table canonique 41/41 décisions (**rang 1**).
- `Assets/Shader/Ocean_v2/OCEAN_ROADMAP.md` — plan exécutif, phases P0→P10, budget, tensions.
- `Assets/Shader/Ocean_v2/OCEAN_IMPLEMENTATION_STATUS.md` — avancement détaillé.
- `Assets/Shader/Ocean_v2/OCEAN_GUIDELINES.md` — cahier des charges + specs A–D.
- `CLAUDE_MEMORY/PIEGES.md` — pièges HDRP/shader/build (dont modèle couleur eau, tessellation).
