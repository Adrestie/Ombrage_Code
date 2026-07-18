# OCEAN_GUIDELINES.md — Cahier des charges & questionnaire de cadrage

> **Statut : PHASE DE CADRAGE — aucun code de production.**
> Ce document est à la fois le **référentiel de conception** du nouveau système
> d'océan *et* le **questionnaire de décision** adressé à l'utilisateur. Tant
> que les questions ne sont pas tranchées, **rien n'est implémenté**.
>
> **Comment lire ce document :** chaque sous-système est présenté en **triplet**
> &nbsp;**`① Constat (audit Rév. 2)` → `② Recommandation par défaut (justifiée + sourcée)` → `③ Question(s) à trancher`**.
> Le but : vous faites trancher **par exception** sur des défauts argumentés
> plutôt que de répondre dans le vide. Répondez section par section ; chaque
> sous-système est autonome. Les réponses alimenteront la **roadmap finale**
> (§13), aujourd'hui volontairement non verrouillée.

---

<!-- OCEAN-CHANGELOG 2026-06-29 -->

## Changelog

### 2026-06-29 — Jalon « plan exécutif & homogénéité A–D »

- **(i) Reformatage mécanique A–D Q1.1→Q5.3** : ajout d'une section
  *« Spécifications de cadrage Q1.1→Q5.3 (gabarit A–D) »* (en fin de document, sentinelle
  `<!-- OCEAN-AD-REFORMAT v1 complete -->`). Le **slot A** de chaque spec est sourcé
  **EXCLUSIVEMENT** depuis la **table canonique** `OCEAN_DECISIONS.md` (source de rang 1 dans le
  projet ; `memory/4208ed27a3e8/MEMORY.md` en est un miroir de travail) — réponses EFFECTIVES
  verrouillées, **jamais** depuis l'annexe « Défaut proposé » ci-dessous
  (recommandations PRÉ-décision). Reformatage **iso-fond** : aucun contenu décisionnel nouveau,
  seul un placement A–D de réponses déjà verrouillées ; les slots sans contenu = « — néant au cadrage ».
- **(ii) Renvoi §13.2 → `OCEAN_ROADMAP.md`** : le plan exécutif détaillé (sous-tâches,
  budget perf, snapshots, tensions) vit désormais dans `OCEAN_ROADMAP.md`, qui **fait foi
  pour l'ordre/contenu des phases**.
- **(iii) Révisions de FOND (alignement prose défaut §1/§3/§7/§11 sur le verrouillé)** :
  ✅ **APPLIQUÉES le 2026-06-29.** Les 6 incohérences de prose listées ci-dessous (« ② Recommandation
  par défaut » + table d'environnement + annexe récap contredisant des réponses verrouillées) ont été
  alignées sur les décisions Q1.1/Q1.4/Q3.2-3.3/Q7.2/Q11.1/Q12.2. **Aucune réponse verrouillée n'a été
  rouverte** (édition iso-décision). Les **assertions descriptives au présent** (« ① Constat », recommandations
  « ② », environnement, annexe) sont alignées sur le verrouillé ; en revanche le questionnaire (« ③ Questions »)
  et les spécifications A–D sont laissés intacts À DESSEIN (mémoire historique du cadrage : ils citent l'ancien
  défaut pour tracer l'écart décidé, p. ex. Q1.1/Q7.2/Q11.1 et spec A).

> **Liste de fond — RÉSOLUE (appliquée le 2026-06-29)** (prose « ② Recommandation par défaut » /
> environnement / annexe, désormais alignée sur le verrouillé) :
> - ✅ **§0/§1** « strictement aligné GoT × BotW » → **Q1.1** : *Sea of Thieves / AC Black Flag* (plus réaliste).
> - ✅ **§1** « pleine mer + côtier basique » → **Q1.4** : **pleine mer SEULE** en V1.
> - ✅ **§3** « mesh clipmap géométrique préféré » → **Q3.2** : **tessellation hardware gatée par distance** + **Q3.3** ~200k tris (pivot clipmap = repli Q3.4 seulement).
> - ✅ **§7** « crêtes + ambiant + rivage » → **Q7.2** : **crêtes SEULES** en V1.
> - ✅ **§11 / Annexe** « plancher GTX 1060 » → **Q11.1** : **plancher RTX 2060**.
> - ✅ **§11 / §12 / Annexe** « 5–7 presets » → **Q12.2** : **6 presets Beaufort** nommés.

---

## 0. But, périmètre, environnement

### But
Réécrire **intégralement, from scratch**, le système d'océan du projet dans un
**nouveau dossier dédié**, en capitalisant l'**audit Rév. 2** (`OCEAN_AUDIT.md`)
et l'**état de l'art** (12 sources, §14), pour atteindre le calibre **AAA/AA**
déjà établi par les systèmes d'herbe et de terrain.

### Périmètre
- **DANS le périmètre :** définir les guidelines (artistique / technique / perf /
  UX / process) + sous-systèmes optique/effets, puis (après réponses) un plan
  d'implémentation phasé et testable.
- **HORS périmètre (pour l'instant) :** l'implémentation elle-même ; toute
  modification de l'ancien océan (`Assets/Shader/Ocean/`) qui **reste intact**
  jusqu'à migration des scènes ; l'audit (considéré terminé, Rév. 2 validée).

### Environnement verrouillé (confirmé par l'audit)
| Élément | Valeur |
|---|---|
| Moteur | **Unity 6 — 6000.4.1f1** |
| Pipeline | **HDRP 17.4.0** (+ SRP Core 17.4) |
| Langage | **100 % HLSL custom** + compute (target 4.5+) |
| Dépendances externes | **AUCUNE** — pas de Crest, KWS, ni HDRP Water |
| Cible de direction artistique | **Réaliste stylisé, AAA/AA, *Sea of Thieves* / *AC Black Flag*** (Q1.1), caméra PC sol ↔ ciel |

### Principe « from scratch »
**Aucune réutilisation directe de code ancien.** L'ancien océan sert uniquement
de **référence d'effet** (foam, caustics, tessellation, choppiness…) — on s'en
inspire, on ne copie pas. Nouveau dossier proposé : **`Assets/Shader/Ocean_v2/`**
(cohérent avec `Assets/Shader/{Grass, TerrainLitCustom, Ocean}`) — *à confirmer, §12*.

### Les 3 bugs à NE PAS reproduire (acquis du projet)
1. **Soleil cumulatif** : `sun.intensity/color *= …` non restauré en `[ExecuteAlways]`
   → assombrissement irréversible. **→ jamais de mutation destructive d'état partagé non restaurée.**
2. **Réinit H₀ par frame** : wind-pulse fait varier les params spectraux →
   release+recreate des RT chaque frame. **→ H₀ recalculé uniquement sur changement *réel* de paramètre, jamais par l'animation temporelle.**
3. **Normalisation IFFT couplée à l'amplitude** : `1/N` mêlé à `_Amplitude` →
   changer la résolution change la hauteur des vagues. **→ normalisation 1/N strictement découplée de l'amplitude physique.**

### Throwaway documentés à consolider (clean code, no throwaway)
- **Wake** : 2 implémentations (compute sans-V vs `OceanWakeStamp.shader` avec V de Kelvin) → **1 seule**.
- **Fade** : 2 modèles → **1 seul**.
- **Choppiness** : boucle copiée en **3 exemplaires** → **1 seule**.
- **Shore** : map de `OceanShoreIntersection.compute` orpheline (recalculée ailleurs) → **1 seule source**.

---

## Table des matières (sous-systèmes)

1. [Artistique & direction](#1-artistique--direction)
2. [Simulation — spectre / FFT / cascades](#2-simulation--spectre--fft--cascades)
3. [Rendu — chemin, géométrie, tessellation](#3-rendu--chemin-géométrie-tessellation)
4. [Dessus / dessous de l'eau — transition & Snell](#4-dessus--dessous-de-leau--transition--snell)
5. [Réflexions](#5-réflexions)
6. [Absorption & couleur de l'eau](#6-absorption--couleur-de-leau)
7. [Écume (foam)](#7-écume-foam)
8. [Caustiques](#8-caustiques)
9. [Sous-marin](#9-sous-marin)
10. [Sillages & interaction](#10-sillages--interaction)
11. [Performance & instrumentation](#11-performance--instrumentation)
12. [UX / paramétrage / architecture SO](#12-ux--paramétrage--architecture-so)
13. [Process & roadmap](#13-process--roadmap)
14. [Sources](#14-sources)

---

## 1. Artistique & direction

**① Constat.** L'ancien système n'a pas de référence visuelle explicite ; les
réflexions sont un gradient ciel banal, l'écume est plastique (Jacobien seul +
tiling), l'absorption est incohérente. Le reste du projet (herbe 4 couches,
terrain modulaire) est déjà calibré **réaliste stylisé** (référence projet : *Sea of Thieves* / *AC Black Flag*, verrouillé Q1.1).

**② Recommandation par défaut.**
- **Curseur** : **réaliste stylisé**, aligné sur la direction des autres systèmes
  (priorité à la lisibilité artistique sur la physique exacte — cf. *Sea of Thieves*,
  source [5]) ; physique correcte là où elle est gratuite (spectre, absorption).
- **États de mer** : couverture **Calme plat → Ouragan** via l'**échelle de Beaufort**
  (source [12]) ; **6 presets Beaufort nommés** *(verrouillé Q12.2)*.
- **Périmètre spatial** : **V1 = pleine mer SEULE** *(verrouillé Q1.4)* ; tout traitement
  côtier (littoral, écume de rivage, déferlement) reporté en V1.5+.
- **Hiérarchie des features** (à confirmer) :
  - **V1 (prioritaire)** : surface FFT + déplacement/normales, **écume** (crêtes),
    **réflexions** (ciel + planar), **absorption/couleur** Beer-Lambert.
  - **V1.5** : sous-marin (fog/god-rays/absorption), **caustiques**, écume de rivage.
  - **V2+** : sillages bateaux avancés (WP-FFT), interaction/déformation rivage,
    caustiques chromatiques.

**③ Questions.**
- **Q1.1** — Référence(s) visuelle(s) cible précise(s) (jeux/films/photos) ? Reste-t-on
  strictement aligné **GoT × BotW**, ou viser plus réaliste (*Sea of Thieves*,
  *AC Black Flag*, *Uncharted 4*, *Subnautica* pour le sous-marin) ?
- **Q1.2** — Curseur **stylisation ↔ réalisme** : 0 (très stylisé) … 10 (photoréaliste) ?
- **Q1.3** — États de mer à couvrir : confirme-t-on **Calme → Ouragan** complet,
  ou borne-t-on (ex. pas d'ouragan) ?
- **Q1.4** — **Côtier** (déferlement, écume de rivage) dès V1, ou **pleine mer seule** en V1 ?
- **Q1.5** — Validez/ajustez la **hiérarchie V1 / V1.5 / V2+** ci-dessus.

---

## 2. Simulation — spectre / FFT / cascades

**① Constat.** « JONSWAP » de l'ancien = **Phillips × facteur γ** seul (cœur
`αg²/ω⁵·exp(...)` absent), **dispersion deep-water seule** (pas de `tanh(kh)`) →
faible en côtier. **3 IFFT séparées** (pas d'astuce hermitienne). **Normales par
différences finies** (pas analytiques). **Normalisation `1/N` couplée à l'amplitude**
(bug n°3). Config réelle : **512 / 3 cascades / waterLevel 191 m**.

**② Recommandation par défaut.**
- **Spectre** : **vrai JONSWAP** (facteur de pic γ ≈ 3.3) + **correction TMA**
  `ω(k) = √(g·k·tanh(k·h))` **activable par profondeur** (sources [1], [2]). TMA
  n'apporte un gain visible qu'en eau peu profonde (< ~30 m) ; le waterLevel ancien
  (191 m) ≈ deep-water → TMA utile seulement pour zones littorales.
- **IFFT hermitienne 2-en-1** : emballer 2 signaux réels dans real/imag d'une seule
  IFFT complexe (symétrie `h̃(−k) = h̃*(k)`) → **divise par 2 le nombre d'IFFT** vs les 3 actuelles (sources [1], [2]).
- **Dérivées analytiques en domaine spectral** : `∂η/∂x = i·h̃·k̂x`, Jacobien d'écume
  idem → **supprime les différences finies** ET **découple la normalisation de l'amplitude** (corrige le bug n°3) (source [1]).
- **Normalisation** : `1/N` **strictement séparée** de l'amplitude physique du spectre.
- **Cascades** : conserver le **multi-cascade**, **3 → 4 cascades**, longueurs en
  **rapport nombre d'or** (anti-répétition), bandes fréquentielles distinctes,
  **vérifier la transition inter-cascade** (pas de flash — piège ancien) (source [1]).
- **Résolution** : défaut **256** (équilibre perf/qualité), **512** si budget le permet.
- **Packing** : déplacement + dérivées partielles emballés en **2 textures** (source [1]).

**③ Questions.**
- **Q2.1** — **JONSWAP seul** (deep-water) ou **JONSWAP + TMA** (`tanh(kh)`) ? Si TMA :
  y a-t-il des **zones littorales < 30 m** justifiant le coût, et quelle est la
  profondeur réelle d'eau jouable (le waterLevel de 191 m est-il représentatif) ?
- **Q2.2** — Nombre de **cascades** : **3** ou **4** ?
- **Q2.3** — **Résolution** FFT : **256** (défaut) ou **512** ? (impacte directement le budget GPU — voir §11)
- **Q2.4** — Validez l'**IFFT hermitienne** + **dérivées analytiques** comme socle numérique (corrige bugs n°2/n°3).

---

## 3. Rendu — chemin, géométrie, tessellation

**① Constat.** L'ancien océan est **ForwardOnly** — **inverse** du reste du projet
(herbe + terrain en **opaque deferred/GBuffer**), d'où une incohérence
lumière/ombres. Tessellation adaptative pouvant générer **65k+ triangles**.
Keywords matériau tous en `m_InvalidKeywords` (impossible de vérifier la compilation).

**② Recommandation par défaut.**
- **Chemin** : surface opaque en **DeferredOnly / GBuffer** (cohérence herbe + terrain,
  pattern herbe 1b validé ; LightLoop + ombres + APV gratuits) (source [10]).
  - **Acter** : l'**eau transparente forcerait le Forward** en HDRP → on choisit
    une **surface opaque** (rendu par-dessus le niveau d'eau) pour rester en deferred.
  - **Éviter Shader Mode « Both »** (double compilation Forward+Deferred) → **DeferredOnly**.
  - Accepter le léger artefact de **compression GBuffer** des normales/tangentes.
- **Géométrie** : **tessellation hardware gatée STRICTEMENT par distance** *(verrouillé Q3.2)*,
  budget triangles plafonné **~200k tris/frame** *(verrouillé Q3.3)*. Le **mesh clipmap géométrique**
  (pattern herbe/clip-map) reste le **repli sanctionné par Q3.4** si le GBuffer tessellé dépasse le
  budget mesuré au proto P2 (la tessellation amplifie le fillrate GBuffer ; sources [3], [10]).
- **Keyword-gating propre** : features (tess, wind, POM, planar…) en `#if/#endif`
  HLSL **valides** (corrige les `m_InvalidKeywords`).

**Incertitudes à dé-risquer en prototype (non décidées ici) :**
- Coût réel **GBuffer d'une surface océan** (tessellée ou clipmap) en HDRP 17.4.
- Compatibilité **Planar Reflection Probe HDRP** avec une **surface HLSL custom** (non-WaterStack).

**③ Questions.**
- **Q3.1** — Confirme-t-on **surface opaque DeferredOnly/GBuffer** (vs ForwardOnly ancien) ?
- **Q3.2** — Géométrie : **mesh clipmap géométrique** (défaut, comme l'herbe) ou
  **tessellation hardware** gatée par distance ?
- **Q3.3** — Budget triangles cible pour l'océan (ordre de grandeur) ?
- **Q3.4** — Acceptez-vous que ces deux incertitudes (coût GBuffer, planar probe custom)
  soient **mesurées en proto P1/P2** avant verrouillage, plutôt que tranchées maintenant ?

---

## 4. Dessus / dessous de l'eau — transition & Snell

**① Constat.** Ancien : `discard` au waterLevel, transition potentiellement abrupte,
fenêtre de Snell « UI-heavy ». `UnderwaterPass` présent mais **CustomPassVolume désactivé**.

**② Recommandation par défaut.**
- **Double-sided + masque per-pixel** : surface visible des deux faces, blend
  dessus/dessous géré par masque per-pixel selon la position caméra/pixel vs niveau d'eau.
- **Ménisque** : bande de transition (raccord doux) à la ligne d'eau pour éviter la coupure franche.
- **Fenêtre de Snell** : test **angle de vue vs angle critique θc = arcsin(n_air/n_eau) ≈ 48.6°**
  → blend absorption intérieure / réflexion totale interne (source [9]).
- **Ambition V1** : transition propre + ménisque ; Snell complet possible en **V1.5**
  avec le sous-marin (§9).

**Incertitude à dé-risquer :** technique optimale pour le double-sided HDRP
(double-pass / stencil / CustomPass) — détails d'implémentation à valider en proto
(le papier CGF 2024 [9] est payant).

**③ Questions.**
- **Q4.1** — Ambition **V1** du dessus/dessous : **transition + ménisque** seulement,
  ou **fenêtre de Snell complète** dès V1 ?
- **Q4.2** — Préférence d'approche (si vous en avez une) : **double-sided shader**
  unique vs **CustomPass sous-marin** séparé ? (défaut : à trancher en proto)

---

## 5. Réflexions

**① Constat.** Point faible majeur de l'audit : **gradient ciel banal** + **planar RT
coûteux et peu fréquent** + **aucune réflexion sous l'eau**.

**② Recommandation par défaut.**
- **Combinaison blendée** (source [11], Crest) :
  - **Cubemap ciel** (rapide, toujours actif) — base.
  - **Planar Reflection Probe** (objets locaux dynamiques : bateaux, terrain).
  - **SSR** optionnel (détails proches) — **goulot principal**, gaté par qualité.
  - **Sonde sous-marine** dédiée pour les réflexions internes (V1.5+).
- **Compromis coût/qualité** exposé via presets de scalabilité (§11) : ciel seul
  (low) → +planar (medium) → +SSR (high).
- **Note TAA** : incompatibilité connue eau + TAA en HDRP → préférer **SMAA**
  (même conclusion que l'herbe) (source [11]).

**③ Questions.**
- **Q5.1** — Combinaison cible **V1** : **ciel + planar** (défaut) ? **SSR** dès V1 ou V1.5+ ?
- **Q5.2** — **Sonde de réflexion sous-marine** : nécessaire (ambition sous-marin forte)
  ou différée V2 ?
- **Q5.3** — Confirme-t-on **SMAA** plutôt que TAA pour les scènes avec océan ?

---

## 6. Absorption & couleur de l'eau

**① Constat.** **Deux modèles indépendants jamais unifiés** : coloration par
profondeur côté surface vs atténuation spectrale côté sous-marin → incohérence visuelle.

**② Recommandation par défaut.**
- **UN seul modèle Beer-Lambert spectral** partagé **surface + sous-marin** :
  `I(z) = I₀·exp(−Kd·z)`, coefficients d'atténuation par longueur d'onde **mappés en RGB**
  (sources [6], [7], [9]).
- **Paramétrage par types d'eau Jerlov** : **5 océaniques (I → III)** + **9 côtiers (1 → 9)**,
  exposés en **profils d'eau ScriptableObject** (ex. tropical I, Méditerranée IA,
  mer du Nord II, côtier turbide…) (source [6]).
- Séparer **absorption** (perte d'énergie) et **scattering** (changement de direction)
  si budget le permet — sinon absorption seule en V1 (source [7]).

**③ Questions.**
- **Q6.1** — Confirme-t-on le **Beer-Lambert spectral Jerlov unifié** (surface = sous-marin) ?
- **Q6.2** — Combien de **profils d'eau** préchargés, et lesquels (types Jerlov visés) ?
- **Q6.3** — **Scattering** (Mie/Rayleigh) dès V1 ou absorption seule d'abord ?

---

## 7. Écume (foam)

**① Constat.** Écume peu crédible : **Jacobien seul**, texture plastique/tiling,
pas de lifetime. **Boucle choppiness en 3 copies** (throwaway).

**② Recommandation par défaut.**
- **Couverture d'écume = fonction du Jacobien pré-filtrée multi-échelles**
  (Dupuy/Inria, source [3]) : `det < seuil` → surface auto-intersectante = déferlante ;
  pré-filtrage linéaire → **anti-aliasé à toutes échelles** via mip-mapping standard.
- **Seuil Jacobien = point de déferlante** réglable (plus bas = écume plus tôt).
- **Crêtes SEULES en V1** *(verrouillé Q7.2)* : écume = **crêtes déferlantes** détectées par le
  Jacobien (source [11], Crest), sans particules. Les sources **ambiant** (voile diffus global) et
  **rivage** (déjà hors V1 par Q1.4) sont ajoutables plus tard sans dette, mais **hors V1**.
- **Consolidation** : **1 seule** boucle choppiness (vs 3), Jacobien calculé
  analytiquement en domaine spectral (§2, source [1]).
- Chaque cascade contribue **sa propre couverture** d'écume (source [3]).

**③ Questions.**
- **Q7.1** — Confirme-t-on l'écume **Jacobien pré-filtré multi-échelles** (Dupuy) ?
- **Q7.2** — Quelles **sources** activer en V1 : crêtes seules, ou crêtes + ambiant + rivage ?
- **Q7.3** — Besoin d'un **lifetime/persistance** d'écume (traînée qui se dissipe) en V1, ou différé ?

---

## 8. Caustiques

**① Constat.** Caustics procedural existant **réutilisable**. Intégration en
deferred non triviale.

**② Recommandation par défaut.**
- **Projection de rayons réfractés en espace texture** (résolution fixe → **coût constant**,
  filtrage anisotropique) : par pixel du fond, ray → surface → normale → **loi de Snell**
  → direction réfractée → échantillonne la carte d'irradiance soleil (source [8]).
- Résultat = **texture de caustiques** appliquée en **decal multiplicatif** sur fond + objets sous-marins.
- **Compute HLSL** : 1 thread = 1 pixel cible (kernel 2D 256² ou 512²).
- **V1.5** (pas V1) — dépend de l'ambition sous-marin.

**Incertitude :** intégration HDRP deferred (CustomPass post-GBuffer vs inject GBuffer
vs overlay Forward) — à confirmer en proto. Caustiques **chromatiques** = V2+.

**③ Questions.**
- **Q8.1** — Caustiques nécessaires en **V1.5** (défaut) ou plus tôt/tard ?
- **Q8.2** — **Monochromes** (défaut, coût constant) ou **chromatiques** (V2+) ?

---

## 9. Sous-marin

**① Constat.** `UnderwaterPass` (fog, god-rays, distorsion) **présent mais désactivé** ;
`UnderwaterLightingController` **orphelin** et porteur du **bug soleil cumulatif**.

**② Recommandation par défaut.**
- **Effets** : fog volumétrique + god-rays + distorsion + **absorption Beer-Lambert
  partagée** (§6) + **fenêtre de Snell** (§4) (sources [6], [9]).
- **Éclairage sous-marin** : **jamais** de mutation destructive de la lumière soleil
  partagée (corrige bug n°1) → modulation locale **non destructive** restaurée proprement.
- **Intégration** : **CustomPass post-GBuffer** vs **double-sided** intégré — à trancher
  en proto (incertitude §4).
- **V1.5** (dépend de l'ambition sous-marin déclarée en §1).

**③ Questions.**
- **Q9.1** — Ambition **sous-marin** : effet **complet** (fog + god-rays + caustics +
  Snell, type *Subnautica*) ou **minimal** (fog + teinte) en V1.5 ?
- **Q9.2** — Le sous-marin est-il un **gameplay clé** (plongée) ou **incident** (on tombe à l'eau) ?
  (conditionne le budget alloué)

---

## 10. Sillages & interaction

**① Constat.** Wake en **2 implémentations** + **2 modèles de fade** (throwaway).
Shore compute **orphelin**. Pas d'interaction terrain (one-way), pas de déformation.

**② Recommandation par défaut.**
- **Sillages V1** : **stamp RT Kelvin pré-calculé** (pattern Kelvin en V, UV dérivés
  de la vélocité de l'objet) — **consolider** les 2 wake + 2 fade en **1 seul** chemin
  (source [4], conclusion V1).
- **Hybride Wave-Particle + FFT** (cohérence spectrale, état de l'art 2025) = **V2+**
  (86 FPS RTX 4080 à 512² — justifiable seulement si sillages requis dès le départ) (source [4]).
- **Rivage / interaction terrain** : écume de rivage simple en V1 (§7) ; **déformation
  interactive** (terrain ↔ eau, RT toroïdale) = **différée V2** sauf besoin explicite.

**③ Questions.**
- **Q10.1** — **Sillages de bateaux/objets** nécessaires en **V1**, ou différés V1.5/V2 ?
- **Q10.2** — Si V1 : **stamp RT Kelvin** (défaut) suffit-il, ou besoin de l'hybride WP-FFT (V2+) ?
- **Q10.3** — **Interaction/déformation rivage** (RT toroïdale, suction) : V1 ou différée ?

---

## 11. Performance & instrumentation

**① Constat.** **Zéro instrumentation** : ~33 `material.Set*` + ~50 `SetGlobal*` /frame
**sans caching**, ~180 dispatchs GPU/frame (512/3-cascades) **sans ProfilerMarker ni
CommandBuffer**. Aucun budget chiffré. Aucun preset de scalabilité.

**② Recommandation par défaut.**
- **Matériel cible** : **plage RTX 2060 → RTX 4080** *(verrouillé Q11.1 — plancher remonté de la
  GTX 1060 de l'herbe à la RTX 2060)*.
- **Budget GPU/frame** : à **fixer chiffré** (ms/frame océan) — proposition de point
  de départ : **≤ 2–3 ms** sur cible moyenne, à valider par proto.
- **Presets de scalabilité ScriptableObject** : pilotent **cascade count**, **résolution FFT**,
  **qualité réflexions** (ciel → planar → SSR), **tessellation/LOD**, **caustiques on/off**.
- **Instrumentation dès J0** (corrige l'absence totale) :
  - **ProfilerMarker** par compute/shader.
  - **CommandBuffer** pour le batching des dispatchs.
  - **Caching** des `Set/SetGlobal` (ne pousser que les valeurs changées).
  - **Async readback** si une lecture CPU est nécessaire (pattern herbe phase 2b).

**③ Questions.**
- **Q11.1** — **Cible matérielle** : confirme-t-on **GTX 1060 → RTX 4080** ? Plancher/plafond différent ?
- **Q11.2** — **Budget ms/frame** alloué à l'océan sur cible moyenne ?
- **Q11.3** — Niveaux de **qualité** à exposer (ex. Low / Medium / High / Ultra) et ce que chacun coupe ?

---

## 12. UX / paramétrage / architecture SO

**① Constat.** **110 champs sérialisés**, **0 OnValidate (aucun clamp)**, **7 presets
Beaufort en dur** couvrant ~24 % des params, `windSpeedFactor` en **vitesse absolue**
(incohérent avec le WindZone partagé). Inspecteur 8 onglets monolithique.

**② Recommandation par défaut.**
- **Modèle UX du projet** (acté herbe/terrain) :
  - **1 master** : enum `oceanState` (Calm → Hurricane).
  - **Valeurs dérivées en %** d'une échelle maître.
  - **Presets 100 % data-driven** en **ScriptableObject** (fini le code en dur).
  - **Clamps `OnValidate`** + **tooltips** dès le départ.
- **Architecture modulaire** (transposée de `TerrainProfile` / `TerrainFeatureModule`) :
  - **`OceanProfile` (SO)** : données sérialisées centrales.
  - **`OceanFeatureModule` (abstrait)** : 1 module par feature — **spectre / surface /
    underwater / reflection / absorption / shore / wake** — stockés en **sous-assets** du profile.
  - **`OceanSystem` (runtime, MonoBehaviour)** : applique le profile, callbacks d'événement.
- **Vent** : **piloté par le WindZone partagé** (cohérence herbe/terrain), push de
  globals `_OceanWind*` (partagées surface + sous-marin) ; **windSpeed en facteur
  relatif** (corrige l'incohérence absolue). Pattern `GrassWindController`.
- **Presets nommés** : **échelle Beaufort**, **6 presets Beaufort nommés** (ex. Calme B0-1, Ripple B2-3,
  Houle légère B4-5, Agitée B6-7, Tempête B8-10, Ouragan B11-12) (source [12]).

**③ Questions.**
- **Q12.1** — Confirme-t-on le modèle **« 1 master + % + presets SO data-driven + clamps + tooltips »** ?
- **Q12.2** — **Nombre et noms** des presets (les **5–7 Beaufort** proposés conviennent-ils, ou autre échelle/nommage) ?
- **Q12.3** — **Vent** : **WindZone partagé** (défaut, cohérence projet) ou **autonome** à l'océan ?
- **Q12.4** — **Granularité des modules** : la liste (spectre/surface/underwater/reflection/
  absorption/shore/wake) vous convient-elle, ou regroupement/découpage différent ?

---

## 13. Process & roadmap

### 13.1 Process

**① Constat.** **Projet NON sous git** → chantier fragile, non diffable (constat audit).
Pas de stratégie de snapshot pour l'océan.

**② Recommandation par défaut.**
- **Dossier** : **`Assets/Shader/Ocean_v2/`** (cohérent avec l'arborescence existante).
- **Versionnage** : **mettre le chantier sous git** (recommandé). À défaut :
  **snapshots `.backups/` via Write/Copy-Item** avant chaque phase testable (pattern herbe/terrain).
- **Coexistence** : **ancien océan conservé intact** jusqu'à migration des scènes
  (comme le terrain modulaire a coexisté avec le monolithe).
- **Méthode** : **phases testables** avec, pour chaque phase, **un protocole de test
  fourni** + **un snapshot avant** + **un jalon de validation utilisateur** (modèle herbe/terrain).

**③ Questions.**
- **Q13.1** — Met-on le chantier **sous git** (oui/non) ? Si non, valide-t-on les **snapshots `.backups/`** ?
- **Q13.2** — Confirme-t-on le **nom/emplacement** `Assets/Shader/Ocean_v2/` ?
- **Q13.3** — Jalons de **validation utilisateur** : à chaque phase, ou par regroupements ?

### 13.2 Roadmap d'implémentation phasée (SQUELETTE — non verrouillé)

> 📌 **Plan exécutif détaillé → `OCEAN_ROADMAP.md`** (fichier dédié, à côté de ce document).
> Le squelette ci-dessous reste indicatif ; pour l'ordre/contenu des phases, le budget perf,
> les snapshots et l'arbitrage des tensions, **`OCEAN_ROADMAP.md` fait foi**.

> ⚠️ **Non verrouillé.** L'ordre et le contenu seront **finalisés et chiffrés APRÈS
> réponses** (les priorités artistiques/perf/ambition conditionnent le phasage).
> Aucun code n'est produit ici. Chaque phase = **critère de validation** + **snapshot avant**.

| Phase | Contenu | Critère de validation |
|---|---|---|
| **P0 — Squelette** | Dossier + `OceanProfile`/`OceanFeatureModule`/`OceanSystem` (vides) + git/backup | Compile, profile instanciable, modules listés |
| **P1 — Simulation** | JONSWAP(+TMA) + IFFT hermitien 2-en-1 + dérivées analytiques + cascades golden-ratio. **Dé-risque numérique.** | Spectre stable, amplitude découplée de N, pas de répétition visible |
| **P2 — Surface deferred** | Surface GBuffer/DeferredOnly minimale (déplacement + normales), **instrumentée** | S'éclaire/s'ombre comme l'herbe ; budget GBuffer mesuré |
| **P3 — Absorption** | Beer-Lambert Jerlov + couleur (profils SO) | Couleur cohérente surface, profils commutables |
| **P4 — Écume** | Jacobien pré-filtré multi-échelles (3 sources) | Écume anti-aliasée, crêtes crédibles, seuil réglable |
| **P5 — Réflexions** | Ciel → planar → SSR (gatées qualité) | Réflexions cohérentes, compat planar probe confirmée |
| **P6 — Dessus/dessous** | Double-sided + ménisque + Snell + sous-marin | Transition propre, absorption partagée surface/sous-marin |
| **P7 — Caustiques** | Projection rayons réfractés, espace texture | Caustiques sur fond/objets, coût constant |
| **P8 — Sillages** | Stamp RT Kelvin consolidé (1 impl) | Sillage en V crédible, fade unique |
| **P9 — Rivage/interaction** | Écume de rivage + (option) déformation | Raccord terrain propre |
| **P10 — UX / presets / scalabilité** | Master + % + presets Beaufort SO + clamps + niveaux qualité | Presets commutables, clamps actifs, niveaux qualité fonctionnels |

**Ordre indicatif.** P1 dé-risque le numérique avant tout rendu. P2 valide le chemin
deferred (incertitude majeure) tôt. Les phases V1.5/V2 (P6–P9) sont conditionnées
aux réponses artistiques (§1, §9, §10).

---

## 14. Sources

| # | Référence | Usage principal |
|---|---|---|
| [1] | **Ocean Rendering Part 1 — R. Ryan (2025)** — `rtryan98.github.io/2025/10/04/ocean-rendering-part-1.html` | TMA, hermitien 2-en-1, dérivées analytiques, cascades golden-ratio, packing 2 textures |
| [2] | **Simulating Ocean Water — J. Tessendorf (2004)** | Phillips, dispersion, choppiness, symétrie hermitienne (fondateur) |
| [3] | **Real-time Ocean Whitecaps — Dupuy et al., Inria/SIGGRAPH 2014** | Écume Jacobien pré-filtrée multi-échelles, anti-aliasing par mip |
| [4] | **Hybrid Ocean WP-FFT — arXiv 2025 (2511.02852)** | Sillages état de l'art (V2+) ; valide stamp RT Kelvin pour V1 |
| [5] | **Technical Art of Sea of Thieves — Rare/SIGGRAPH 2018** | FFT stylisé AAA scalable, lisibilité > physique exacte |
| [6] | **Jerlov Water Types — Ocean Optics Web Book** | Coefficients Kd par type d'eau (profils absorption SO) |
| [7] | **Rendering Natural Waters — Premoze & Ashikhmin, Stanford 2001** | Beer-Lambert spectral, absorption vs scattering, modèle unifié |
| [8] | **GPU Gems Ch.2 — Water Caustics (NVIDIA)** | Caustiques par rayons réfractés en espace texture (coût constant) |
| [9] | **Real-Time Underwater Spectral Rendering — Monzon et al., CGF 2024** | Fenêtre de Snell temps réel (θc ≈ 48.6°), absorption spectrale |
| [10] | **HDRP Forward/Deferred — Unity Docs 17.x** | Choix deferred/GBuffer, transparent = forced forward, coût tessellation |
| [11] | **Crest Ocean System — Docs** | Réflexions (cubemap+planar+SSR), 3 sources d'écume, SMAA vs TAA |
| [12] | **Échelle de Beaufort — Wikipedia** | 13 niveaux standardisés vent/hauteur, presets nommés Calme→Ouragan |

> **Bases de connaissance internes :** `OCEAN_AUDIT.md` (Rév. 2) + mémoire de session
> (`memory/4208ed27a3e8/MEMORY.md`).

---

## Annexe — Récapitulatif des questions (réponse rapide)

| # | Sujet | Défaut proposé |
|---|---|---|
| Q1.1–1.5 | Référence visuelle, curseur, états de mer, côtier, hiérarchie features | Réaliste stylisé *Sea of Thieves*/*AC Black Flag* (Q1.1), Calme→Ouragan, **pleine mer SEULE** (Q1.4), V1/V1.5/V2+ |
| Q2.1–2.4 | Spectre, cascades, résolution, socle numérique | JONSWAP+TMA optionnel, 3–4 cascades, 256, hermitien+analytique |
| Q3.1–3.4 | Chemin, géométrie, budget tris, incertitudes | DeferredOnly/GBuffer, **tessellation gatée par distance** (Q3.2) **~200k tris** (Q3.3) — clipmap = repli Q3.4, à dé-risquer en proto |
| Q4.1–4.2 | Ambition dessus/dessous, approche | Transition+ménisque V1, Snell V1.5, à trancher en proto |
| Q5.1–5.3 | Réflexions V1, sonde sous-marine, AA | Ciel+planar, SSR V1.5+, SMAA |
| Q6.1–6.3 | Beer-Lambert unifié, profils, scattering | Jerlov unifié, profils SO, absorption seule V1 |
| Q7.1–7.3 | Écume, sources, lifetime | Jacobien pré-filtré, **crêtes SEULES** en V1 (Q7.2), persistance légère V1 (Q7.3) |
| Q8.1–8.2 | Caustiques, chromatiques | V1.5, monochromes |
| Q9.1–9.2 | Ambition sous-marin, gameplay | Selon §1, conditionne budget |
| Q10.1–10.3 | Sillages, technique, déformation | Stamp RT Kelvin V1, WP-FFT V2+, déformation V2 |
| Q11.1–11.3 | Cible HW, budget ms, niveaux qualité | **RTX 2060→4080** (Q11.1), 2–4 ms (verrou proto P2), Low/Med/High/Ultra |
| Q12.1–12.4 | Modèle UX, presets, vent, modules | Master+%+SO, **6 presets Beaufort** (Q12.2), WindZone partagé, 7 modules |
| Q13.1–13.3 | Git, dossier, jalons | git recommandé (sinon .backups), Ocean_v2/, validation par phase |
```

---

<!-- OCEAN-AD-REFORMAT v1 complete -->

## Spécifications de cadrage Q1.1→Q5.3 (gabarit A–D)

> **Rattrapage d'homogénéité (jalon 2026-06-29).** Les 18 premières réponses de cadrage
> avaient été consignées **avant** l'introduction du gabarit détaillé A–D (présent pour
> Q6.1→Q13.3 « sous la table » de `MEMORY.md`). Cette section les met au **même gabarit
> A–D** = **A. Décision actée** · **B. Spécification technique / paramètres** ·
> **C. Pièges / contraintes / bugs interdits** · **D. Questions ouvertes / différé**.
>
> ⚠️ **Source du slot A = table canonique `OCEAN_DECISIONS.md`** (source de RANG 1, dans le projet ;
> le `memory/4208ed27a3e8/MEMORY.md` en est désormais un simple miroir de travail) — réponses
> EFFECTIVES verrouillées, **JAMAIS** l'annexe « Défaut proposé » ci-dessus. Reformatage
> **iso-fond** : aucune décision rouverte ; un slot sans contenu verrouillé = « — néant au cadrage ».
> Les contradictions de la prose défaut §1/§3/§7/§11 sont listées dans le *Changelog*
> et **désormais APPLIQUÉES le 2026-06-29** ; corrigées dans la prose défaut, désormais alignée sur le verrouillé.

### Spécification de cadrage Q1.1 — Référence(s) visuelle(s) cible

- **A. Décision actée.** Viser **plus réaliste façon *Sea of Thieves* / *AC Black Flag*** — FFT stylisé AAA scalable, vagues très crédibles tout en restant lisibles. *(Choix imposé, écart au défaut « strictement aligné GoT × BotW ».)*
- **B. Spécification technique.** Direction = réaliste stylisé AAA/AA scalable ; lisibilité préservée.
- **C. Pièges / contraintes.** Léger risque de décrochage stylistique vs herbe/terrain (plus stylisés), **assumé**.
- **D. Questions ouvertes.** — néant au cadrage.

### Spécification de cadrage Q1.2 — Curseur stylisation ↔ réalisme

- **A. Décision actée.** **7–8 (réaliste affirmé)** sur l'échelle 0 (très stylisé) → 10 (photoréaliste). *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Vagues FFT très crédibles, dérivées analytiques, écume/reflets physiquement plausibles, tout en restant lisibles et stylisables.
- **C. Pièges / contraintes.** Budget GPU plus élevé et léger décrochage stylistique vs herbe/terrain **assumés**. Cohérent avec la réf *Sea of Thieves / AC Black Flag* (Q1.1).
- **D. Questions ouvertes.** — néant au cadrage.

### Spécification de cadrage Q1.3 — États de mer à couvrir

- **A. Décision actée.** **Spectre complet Calme plat → Ouragan (Beaufort B0 → B12)**, 5–7 presets nommés (resserrés à **6** en Q12.2). *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Un seul système gère mer d'huile comme tempête extrême ; échelle Beaufort standard.
- **C. Pièges / contraintes.** Les états extrêmes (ouragan) sont **coûteux à régler/valider** et rarement vus en jeu (effort de calibrage **assumé**).
- **D. Questions ouvertes.** Calibrage fin des états extrêmes B11–B12 renvoyé au proto P2 (cohérence budget, cf. `OCEAN_ROADMAP.md` §2).

### Spécification de cadrage Q1.4 — Périmètre spatial V1

- **A. Décision actée.** **Pleine mer SEULE en V1** — aucun traitement côtier ; littoral et écume de rivage reportés V1.5+. *(Choix imposé, écart au défaut « pleine mer + côtier basique ».)*
- **B. Spécification technique.** Effort concentré sur la simulation FFT + le rendu haute mer.
- **C. Pièges / contraintes.** Raccord eau/terrain pauvre (coupure franche, pas d'écume de rivage) **assumé** jusqu'à la V1.5. Conditionne le statut **dormant** du module `shore` (Q12.4).
- **D. Questions ouvertes.** — néant au cadrage (côtier = V1.5+).

### Spécification de cadrage Q1.5 — Hiérarchie de priorisation V1 / V1.5 / V2+

- **A. Décision actée.** **Renforcer la V1** — remonter le sous-marin (fog/absorption) et/ou les caustiques de V1.5 vers V1. *(Choix imposé, écart au défaut « sous-marin + caustiques en V1.5 ».)*
- **B. Spécification technique.** Océan plus complet et spectaculaire dès la première livraison (immersion sous l'eau, fonds éclairés).
- **C. Pièges / contraintes.** V1 nettement plus lourde/longue à livrer ; **tension assumée** avec la « V1 resserrée » induite par Q1.4. → **Tension T1** arbitrée dans `OCEAN_ROADMAP.md` §4 (décision deux temps P2/P5 + re-validation P6).
- **D. Questions ouvertes.** Promotion effective de l'underwater en V1 = décidée à P5 sur total-frame (cf. T1).

### Spécification de cadrage Q2.1 — Modèle spectral & profondeur

- **A. Décision actée.** **Vrai JONSWAP (pic γ ≈ 3.3) + correction TMA `tanh(kh)` ACTIVABLE par profondeur, branche TMA INACTIVE en V1.** *(Défaut recommandé accepté.)*
- **B. Spécification technique.** `ω(k) = √(g·k·tanh(k·h))` ; socle physiquement correct, prêt pour le côtier < 30 m (V1.5) sans refonte.
- **C. Pièges / contraintes.** La branche TMA n'apporte rien en pleine mer profonde (191 m) où la V1 est cantonnée (Q1.4) → **code/coût présent mais dormant en V1**.
- **D. Questions ouvertes.** Activation TMA = V1.5 avec le côtier.

### Spécification de cadrage Q2.2 — Nombre de cascades FFT

- **A. Décision actée.** **4 cascades** — bande de fréquence supplémentaire + longueurs en **rapport nombre d'or** (anti-répétition). *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Détail multi-échelle ; transitions inter-cascade à régler (pas de flash).
- **C. Pièges / contraintes.** Coût **assumé** : +1 jeu d'IFFT/PostProcess par frame (~+33 % de dispatchs vs 3) + une transition inter-cascade de plus.
- **D. Questions ouvertes.** Réglage fin des transitions inter-cascade renvoyé à P1.

### Spécification de cadrage Q2.3 — Résolution FFT par cascade

- **A. Décision actée.** **Mixte / par cascade** : **512²** sur les grandes cascades porteuses, **256²** sur les cascades de détail. *(Choix imposé, écart au défaut homogène 256²/512².)*
- **B. Spécification technique.** Concentre la résolution là où elle se voit ; mapping par preset détaillé dans `OCEAN_ROADMAP.md` §2.2.
- **C. Pièges / contraintes.** Pipeline FFT + transitions inter-cascade **plus complexes** (tailles hétérogènes), s'écarte du socle homogène.
- **D. Questions ouvertes.** Verrouillage du mix exact par preset au proto P2 (budget perf).

### Spécification de cadrage Q2.4 — Socle numérique FFT

- **A. Décision actée.** **Adopter les DEUX — IFFT hermitienne 2-en-1 + dérivées analytiques en domaine spectral.** *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Moitié moins d'IFFT (perf, précieux avec 4 cascades) ; normales et Jacobien d'écume exacts ; `∂η/∂x = i·h̃·k̂x`.
- **C. Pièges / contraintes / bugs interdits.** Corrige le **bug n°2** (normales différences finies → analytiques) ET le **bug n°3** (`1/N` strictement découplée de l'amplitude → changer N ne change plus la hauteur). Coût **assumé** : packing real/imag + symétrie hermitienne `h̃(−k)=h̃*(k)` délicats. *(NB : la correction effective des bugs est portée par P1.b, cf. `OCEAN_ROADMAP.md` §3.)*
- **D. Questions ouvertes.** — néant au cadrage.

### Spécification de cadrage Q3.1 — Chemin de rendu de la surface

- **A. Décision actée.** **Hybride — surface opaque deferred (GBuffer) pour l'éclairage + CustomPass sous-marin séparé (post-GBuffer) pour le compositing de la vue à travers/sous l'eau.** *(Choix imposé, variante du défaut DeferredOnly enrichi.)*
- **B. Spécification technique.** Surface opaque/deferred (LightLoop + ombres + APV gratuits, cohérence herbe+terrain) ; CustomPass `BeforePostProcess` (absorption, distorsion, god-rays).
- **C. Pièges / contraintes.** L'eau réellement transparente forcerait le Forward (incohérence ancien océan) ; coût = **deux passes à coordonner**, intégration plus complexe à régler/instrumenter.
- **D. Questions ouvertes.** Technique exacte du raccord (double-sided / stencil / CustomPass) tranchée au proto (Q4.2).

### Spécification de cadrage Q3.2 — Géométrie de la surface

- **A. Décision actée.** **Tessellation hardware gatée STRICTEMENT par distance** — subdivision GPU locale, plafonnée par budget de triangles + gating sévère. *(Choix imposé, écart au défaut « mesh clipmap ».)*
- **B. Spécification technique.** Densité géométrique très élevée près caméra sans lattice CPU ; raffinement local automatique.
- **C. Pièges / contraintes.** Amplifie le **fillrate GBuffer** (coûteux en deferred, Q3.1) ; risque de pics de triangles (ancien : 65k+) ; s'écarte du pattern clipmap herbe/terrain ; plus dur à borner/instrumenter.
- **D. Questions ouvertes.** **Repli mesh clipmap sanctionné par Q3.4** si le GBuffer tessellé dépasse le budget (mesuré au proto P2). → **Tension T2**, `OCEAN_ROADMAP.md` §4.

### Spécification de cadrage Q3.3 — Budget triangles cible

- **A. Décision actée.** **Budget large ~200k+ tris/frame** — plafond haut, raffinement dense près caméra. *(Choix imposé ; aucun chiffre par défaut, calibrage renvoyé au proto.)*
- **B. Spécification technique.** Géométrie de crêtes maximale, rendu spectaculaire en haute mer agitée/tempête (Q1.3) ; devient le garde-fou central contre les pics de la tessellation (Q3.2).
- **C. Pièges / contraintes.** Amplifie nettement le fillrate GBuffer ; **tension forte** avec un budget ms/frame raisonnable (Q11.2).
- **D. Questions ouvertes.** Verrouillage chiffré au proto P2 (Q3.4/Q11.2).

### Spécification de cadrage Q3.4 — Dé-risque des incertitudes de rendu

- **A. Décision actée.** **Mesurer en proto P1/P2 avant verrouillage** — surface deferred instrumentée dès P2 (ProfilerMarker/CommandBuffer) ; coût GBuffer + compat planar probe chiffrés sur cible réelle. *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Décisions fondées sur mesures réelles, pas hypothèses ; dé-risque tôt les 2 points les plus incertains (Q3.1–Q3.3).
- **C. Pièges / contraintes.** Le verrouillage du chemin de rendu **reste ouvert jusqu'à P2** (pivots possibles : repli mesh clipmap si GBuffer tessellé trop cher ; réflexions sans planar probe si incompatible).
- **D. Questions ouvertes.** Tranchées au proto P2 (cf. `OCEAN_ROADMAP.md` §3 gate P2, §4 T2).

### Spécification de cadrage Q4.1 — Ambition V1 du raccord dessus/dessous

- **A. Décision actée.** **Transition + ménisque seulement en V1** (double-sided + masque per-pixel + bande de ménisque à la ligne d'eau) ; **fenêtre de Snell complète (θc ≈ 48.6°) reportée en V1.5**. *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Raccord propre dessus/dessous, nettement meilleur que le `discard` abrupt de l'ancien océan.
- **C. Pièges / contraintes.** Pas de réflexion totale interne réaliste vu de dessous en V1 (effet « miroir » + fenêtre de Snell seulement en V1.5) — **assumé**.
- **D. Questions ouvertes.** Approche technique du raccord tranchée au proto (Q4.2).

### Spécification de cadrage Q4.2 — Approche du raccord dessus/dessous

- **A. Décision actée.** **Laisser l'approche se trancher en proto** — fixer seulement l'objectif (transition propre + ménisque per-pixel) ; choisir entre **double-sided / stencil / CustomPass** après mesure en P2/P6. *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Décision fondée sur mesures réelles HDRP 17.4 (cohérent Q3.4).
- **C. Pièges / contraintes.** L'approche du raccord **reste ouverte jusqu'au proto** ; pas de cible technique figée tout de suite.
- **D. Questions ouvertes.** Double-sided vs stencil vs CustomPass = tranché P2/P6.

### Spécification de cadrage Q5.1 — Combinaison de réflexions V1 & SSR

- **A. Décision actée.** **Ciel (cubemap) + Planar Reflection Probe en V1 ; SSR reporté en V1.5+.** *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Base toujours active (cubemap ciel) + réflexions d'objets locaux dynamiques (planar probe) ; scalabilité par presets (§11).
- **C. Pièges / contraintes.** Pas de réflexions fines des détails proches (vagues sur vagues, mousse) tant que le SSR n'arrive pas (V1.5+).
- **D. Questions ouvertes.** Compat planar × surface HLSL custom mesurée au proto (Q3.4) ; test SSR mesh custom = critère de sortie P5 (`OCEAN_ROADMAP.md` §3).

### Spécification de cadrage Q5.2 — Sonde de réflexion sous-marine

- **A. Décision actée.** **Sonde de réflexion sous-marine différée en V1.5+/V2.** *(Défaut recommandé accepté.)*
- **B. Spécification technique.** Base V1 = ciel (cubemap) + Planar Probe au-dessus de l'eau (Q5.1) ; réflexions internes vues de dessous avec le sous-marin + Snell (V1.5).
- **C. Pièges / contraintes.** Pas de réflexion interne crédible vue de dessous en V1 (sous-face sans réflexions internes jusqu'en V1.5).
- **D. Questions ouvertes.** — néant au cadrage (arrive avec le sous-marin V1.5).

### Spécification de cadrage Q5.3 — Anti-aliasing (SMAA vs TAA)

- **A. Décision actée.** **Laisser le choix d'AA se trancher en proto / le rendre configurable par preset de qualité (§11).** *(Choix imposé, écart au défaut « confirmer SMAA ».)*
- **B. Spécification technique.** AA exposé par niveau de qualité (`OceanQualityProfile`, Q11.3) ; décision fondée sur mesure réelle HDRP 17.4.
- **C. Pièges / contraintes.** Incompatibilité connue eau + TAA (ghosting sur vagues/écume animées) = **signal fort en faveur de SMAA**, mais verrou repoussé au proto ; surface de réglage supplémentaire à valider.
- **D. Questions ouvertes.** Cible AA figée au proto (cohérent avec la passe MotionVector de P2/P3 pour un TAA correct si retenu).
