# OCEAN_ROADMAP.md — Plan exécutif d'implémentation (Océan v2)

> **Statut : PLAN EXÉCUTIF — document de planification, zéro ligne de code de production.**
> Transforme le squelette de roadmap P0→P10 (`OCEAN_GUIDELINES.md` §13.2) en plan
> directement actionnable : sous-tâches concrètes, critères de validation mesurables,
> snapshots de fin de phase, budget perf par module × preset, arbitrage des tensions
> résiduelles. **L'implémentation de P0 vient ENSUITE** ; ce document ne l'exécute pas.

> ### Sources de vérité (hiérarchie)
> 1. **Réponses de cadrage (décisions actées)** → **table canonique** de
>    **`Assets/Shader/Ocean_v2/OCEAN_DECISIONS.md`** (table 41/41 : `# | sujet | réponse retenue | type | horodatage`),
>    **rapatriée dans le projet** (source de rang 1, à l'abri de la purge de `Library/`).
>    C'est la **seule** source des décisions Q1.1→Q13.3. Le `MEMORY.md` de session
>    (`Library/ClaudeCodeGUI/memory/4208ed27a3e8/MEMORY.md`) n'en est plus qu'un **miroir de travail**.
>    ⚠️ **JAMAIS** l'annexe « Défaut proposé » d'`OCEAN_GUIDELINES.md` (= recommandations PRÉ-décision,
>    plusieurs contredisent les choix effectifs).
> 2. **Spécifications détaillées de cadrage (gabarit A–D)** → `OCEAN_GUIDELINES.md`
>    (Q1.1→Q5.3, ajoutées en pré-P0) + `MEMORY.md` session « sous la table » (Q6.1→Q13.3, miroir de travail).
> 3. **Plan exécutif / ordre & contenu des phases** → **CE document** (`OCEAN_ROADMAP.md`)
>    **fait foi**. En cas de divergence d'ordre/contenu de phase avec `OCEAN_GUIDELINES.md`
>    §13.2 ou `OCEAN_CADRAGE_STATUS.md`, **OCEAN_ROADMAP.md prévaut**.
> 4. **Audit antécédent** → `OCEAN_AUDIT.md` (Rév. 2).

> ### Contraintes verrouillées (rappel — non négociables)
> - **Moteur/pipeline** : Unity 6 (6000.4.1f1) / **HDRP 17.4.0** (+ SRP Core 17.4).
> - **100 % HLSL custom** + compute (target 4.5+) ; **zéro dépendance externe**
>   (pas de Crest, KWS, ni HDRP Water).
> - **Ancien océan** (`Assets/Shader/Ocean/`) laissé **INTACT** jusqu'à migration des scènes.
> - **Dossier de travail** : `Assets/Shader/Ocean_v2/` (Q13.2).
> - **Les 3 bugs interdits** (jamais reproduits — re-vérifiés à chaque boucle de phase) :
>   1. Soleil cumulatif non restauré en `[ExecuteAlways]` (`sun.intensity/color *= …`).
>   2. Réinit H₀ complète par frame (wind-pulse fait varier les params spectraux).
>   3. Normalisation IFFT `1/N` couplée à l'amplitude (changer N change la hauteur).

> ### Règle de jalonnage
> **AUCUNE date calendaire ni estimation de durée nulle part.** Les dé-risques sont
> « datés » uniquement par **ancrage à une phase/jalon** (ex. « levé au proto P2 »).
> Le **proto P2 = la phase P2 elle-même** (première surface deferred réellement rendue
> = premier GBuffer mesurable sur RTX 2060 = premier budget perf concret = point de
> reconfirmation des tensions).

---

## Table des matières

1. [Pipeline de développement récurrent](#1-pipeline-de-développement-récurrent)
2. [Budget de performance par module × preset](#2-budget-de-performance-par-module--preset)
3. [Phases P0→P10 détaillées](#3-phases-p0p10-détaillées)
4. [Arbitrage des 3 tensions résiduelles](#4-arbitrage-des-3-tensions-résiduelles)
5. [Références croisées & cohérence inter-documents](#5-références-croisées--cohérence-inter-documents)

---

## 1. Pipeline de développement récurrent

> Section **dédiée et distincte** du contenu phase-par-phase (réponse OUI à la question
> ouverte n°1 du brief). Décrit la **boucle appliquée à CHAQUE phase Pn** ; le §3 ne
> répète pas ce process, il ne donne que le contenu spécifique de chaque phase.

### 1.1 Boucle nominale (par phase Pn)

| Étape | Nom | Description |
|---|---|---|
| **(a)** | **SPEC** | Figer la sous-spec de Pn depuis la décision actée (**table canonique** `OCEAN_DECISIONS.md`) + le détail (**spec A–D** GUIDELINES/MEMORY). Aucune réouverture de décision. |
| **(b)** | **SNAPSHOT AVANT** | Copie de `Assets/Shader/Ocean_v2/` → `.backups/NN_before_<phase>_<tag>/`. **Mécanique : `Copy-Item` (copie shell) — admis et rapide, le sandbox NE bloque plus les copies shell** (note session 0e9e640173af). On ne motive donc PLUS le choix par un blocage sandbox inexistant. **⭐ SCHÉMA DE SLOTS `Pn → 2n/2n+1`** — pair = `before`, impair = `validated`. Toute sous-phase (ex. P1.a) réutilise le slot de sa phase-mère (pas de slot de premier rang). **✅ VALIDE ET RESPECTÉ POUR P0–P2 UNIQUEMENT :** P0=`00`/`01`, P1=`02`/`03`, P2=`04`/`05` — slots `00`→`05` conformes et inchangés sur disque (collision `04`/`05` historique purgée). **⚓ RÉCONCILIATION DES SLOTS (2026-07-05) — LOCUS CANONIQUE :** l'ancienne cascade **P3→P10 (`P3=06/07 … P10=20/21`) est SUPERSEDED / PROVISOIRE** — le **banc de gates P2** (bench *hors* formule de phase) a **consommé `06` ET `07`** (`06_before_P2_gates/` + `07_P2_gates_validated/` **CRÉÉ le 2026-07-05**, gate 4 clos, budget verrouillé 2.564 ms) ; en conséquence **P3 ne doit PAS présumer `06`/`07` libres**. **✅ RENUMÉROTATION ACTÉE AU SETUP P3 (2026-07-05) : P3→P10 = `Pn → (2n+2)/(2n+3)`** (décalage +2 absorbant le banc de gates P2) — **P3=`08`/`09`, P4=`10`/`11`, P5=`12`/`13`, P6=`14`/`15`, P7=`16`/`17`, P8=`18`/`19`, P9=`20`/`21`, P10=`22`/`23`** ; slots P3 = `08_before_P3_absorption/` · `09_P3_absorption_validated/` (convention miroir dans `OCEAN_IMPLEMENTATION_STATUS.md` §« Convention de snapshots — banc de gates P2 »). Les **2 autres loci** de cette cascade — le restatement de la section P2 et les **8 lignes « Snapshot de fin » P3→P10** du §3 — portent la même mention SUPERSEDED et **renvoient ICI**. |
| **(c)** | **IMPLÉMENTATION** | **Code de production SEUL** (clean code, no throwaway) ; tout harnais de test temporaire est **isolé puis supprimé**. **Instrumentation dès J0** (voir §1.4, cadrée comme *orientations de conception*). |
| **(d)** | **PROTOCOLE DE TEST** | Validation **par données** : `execute_code`, inspection `.vfx`/`.shader`/FrameDebugger, ProfilerMarkers. **L'utilisateur = les yeux** pour le rendu visuel (les captures MCP ne sont **pas** fiables). |
| **(e)** | **SNAPSHOT DE FIN** | `.backups/NN_<phase>_validated_<tag>/` + **manifeste** (liste fichiers, hash/horodatage, budget mesuré, critères atteints). |
| **(f)** | **VALIDATION UTILISATEUR** | Jalon obligatoire à **CHAQUE** phase (Q13.3). |
| **(g)** | **PASSAGE Pn+1** | **Strictement bloqué** tant que le critère de sortie de Pn n'est pas atteint ET validé. |

> **Prérequis shader (point 4 de la révision du plan V1) :** dès que l'étape **(c)** touche un
> `.shader`/`.hlsl`/`.compute`, l'étape **(d)** — et **tout gate play-mode** — ne démarre qu'après une
> **recompilation côté utilisateur** : réimport shaders + domain reload, **Console vide** (0 err/warn),
> **Test Runner EditMode TOUS verts** (12 tests en P2 ; **16 attendus dès P3**, ajout `OceanAbsorptionTests`). C'est le **gate 0-bis** (`OCEAN_TEST_P2.md §(a-bis)`).

### 1.2 Branche d'échec (rollback + ré-spéc)

Si **(f)** échoue **OU** si l'implémentation casse à mi-parcours :
1. **Rollback** sur `.backups/NN_before_<phase>_<tag>/` (restauration du dossier).
2. **Ré-spéc** : analyse cause racine (logs, données, FrameDebugger), correction de la sous-spec.
3. **Ré-itération** de la boucle (a)→(g).
4. **Aucun avancement vers Pn+1** tant que la phase n'est pas re-validée.

### 1.3 Décision d'implémentation transverse — gating actif de l'état caméra

Le modèle de budget (§2) repose sur une **exclusion mutuelle** des passes selon l'état
caméra. Cela **REQUIERT un gating actif** à implémenter (prérequis du modèle) :
- Détecter la **position caméra vs niveau d'eau** (émergé / immergé / transition).
- **DÉSACTIVER les passes de réflexion ocean-spécifiques** (planar RT, éventuel SSR ocean)
  quand la caméra est **immergée** (la fenêtre de Snell occulte le monde émergé).
- Le **SSR HDRP GLOBAL** (autres surfaces) reste **indépendant** et **hors** de ce budget.
- **Sans ce gating, les deux états seraient mesurés simultanément** → le modèle est invalide.
  Si le hardware n'autorise pas la troncature des passes plein-écran, **mesurer le coût
  additionné au proto P2/P5 et revoir le modèle** (parade (b) du §2.4).
- **Implémentation du gating ancrée à P5 (réflexions) + P6 (sous-marin)** (cf. §3).

### 1.4 Instrumentation — orientations de conception

> Ces points sont des **orientations de conception** de l'instrumentation, **pas** du code
> de production livré par ce plan. Ils rendent les phases actionnables sans franchir le
> hors-scope « écriture de code ».

- **ProfilerMarker** par compute / passe shader.
- **CommandBuffer** pour batcher les dispatchs.
- **Caching des `Set/SetGlobal`** : ne pousser que les valeurs changées (push **non cumulatif**).
- **Push NON cumulatif restaurable** (anti-bug n°1 soleil) : `SetGlobal*` = assignation pure,
  jamais `*=`/`+=` ; teardown remet à neutre.
- H₀ recalculé **uniquement sur changement réel de paramètre** (anti-bug n°2).
- Normalisation `1/N` **découplée de l'amplitude** (anti-bug n°3).

### 1.5 Invariant de chaque boucle

Les **3 bugs interdits** sont **re-vérifiés à chaque boucle** (nominale comme reprise),
ainsi que le **garde-fou de non-régression du budget** (§2 : le cumul de la phase ne doit
pas franchir le plafond du preset).

---

## 2. Budget de performance par module × preset

> **Plage cible 2–4 ms** sur le **plancher RTX 2060** (Q11.1/Q11.2).
> **Interprétation (DÉCISION PROVISOIRE, reconfirmable au proto P2)** : la plage est un
> **plafond de conception sur le TOTAL-FRAME**. **4 ms = plafond dur** ; **descendre sous
> 2 ms est acceptable** (marge gagnée, conservateur).
>
> ⚠️ **Tous les chiffres sont PROVISOIRES, à RÉCONCILIER/VERROUILLER au proto P2** (premier
> GBuffer réellement mesuré). Ce sont des **CIBLES**, pas des mesures.

### 2.1 Conventions

- **7 modules** (Q12.4) : `spectre`, `surface`, `underwater`, `reflection`, `absorption`,
  `shore`, `wake`. **`foam` (Q7) → agrégé à `surface`** ; **`caustics` (Q8) → agrégé à `underwater`** (ce sont des *features*, pas des modules).
- Chaque module est **rattaché à la phase qui le livre** (budget **cumulé par phase** =
  garde-fou de non-régression à chaque jalon).
- **État de mer (Beaufort)** : les ms sont exprimées au **pire cas raisonnable d'état de
  mer ciblé (ex. B6 « mer agitée »)** — dimension **orthogonale** aux 4 presets qualité
  (Q1.3 B0→B12 ; Q12.2 6 presets Beaufort). `foam`/`surface` portent la **variance Beaufort**
  comme garde-fou de non-régression ; les **états extrêmes (B11–B12) sont à reconfirmer au proto P2**.

### 2.2 Table module × preset (ms, RTX 2060)

| Module (phase de livraison) | Low | Medium | High | Ultra |
|---|---:|---:|---:|---:|
| `spectre` / FFT (**P1**) | 0.45 | 0.60 | 0.80 | 1.10 |
| `surface` + foam (**P2/P4**) | 0.55 | 0.70 | 0.85 | 1.00 |
| `absorption` (**P3**) | 0.10 | 0.12 | 0.15 | 0.18 |
| `reflection` *above-water* (**P5**) | 0.50 | 0.80 | 1.00 | 1.20 |
| `underwater` + caustics (**P6/P7**) | 0.30 | 0.35 | 0.40 | 0.50 |
| `wake` (**P8**) | 0.00 | 0.05 | 0.10 | 0.15 |
| `shore` (**P9**, dormant V1) | 0.00 | 0.00 | 0.00 | 0.00 |

**Résolution FFT mixte par preset (Q2.3)** — reflétée dans le coût `spectre` :
- **Ultra** : 2 × 512² (porteuses, silhouette) + 2 × 256² (détail).
- **High** : 1 × 512² + 3 × 256².
- **Medium** : 4 × 256².
- **Low** : 4 → 2 cascades × 256².

### 2.3 Trois lignes de cumul par état caméra

Le coût total dépend de l'**état caméra**, gouverné par le **gating actif** (§1.3).

| État caméra | Composition | Low | Medium | High | Ultra |
|---|---|---:|---:|---:|---:|
| **(1) FRAME ÉMERGÉ** | spectre + surface+foam + absorption + reflection + wake (underwater = 0) | 1.60 | 2.27 | 2.90 | **3.63** |
| **(2) FRAME IMMERGÉ** | émergé − reflection + underwater *(gating actif : la fenêtre de Snell occulte le monde émergé)* | 1.40 | 1.82 | 2.30 | **2.93** |
| **(3) FRAME TRANSITION** *(ligne de flottaison)* | émergé + underwater *(reflection above-water ET underwater coexistent quand la waterline traverse l'écran — cas emblématique : nager façon Sea of Thieves, Q4.1/Q4.2)* | 1.90 | 2.62 | 3.30 | **4.13** |
| **PLAFOND = total-frame = max(émergé, immergé, transition)** | = ligne transition | **1.90** | **2.62** | **3.30** | **4.13** |

**Vérification arithmétique (Ultra)** :
- Émergé = 1.10 + 1.00 + 0.18 + 1.20 + 0.15 = **3.63** ✓
- Immergé = 3.63 − 1.20 + 0.50 = **2.93** ✓
- Transition = 3.63 + 0.50 = **4.13** ✓

### 2.4 Constat honnête + dé-risque proto P2

> **Le pic TRANSITION Ultra (4.13 ms) DÉPASSE 4 ms.** L'affirmation « aucun scénario ne
> dépasse le budget » serait **fausse** et est **retirée**.

C'est un **dé-risque OUVERT, à mesurer au proto P2**, avec **deux parades chiffrées
cohérentes** (modèle de partage à mesurer) :

- **Parade (a) — clamp DEMI-ÉCRAN des passes ÉCRAN-ESPACE** via scissor / stencil / depth-bounds
  pendant le crossing. **⚠️ Portée corrigée (revue documentaire 2026-07-04)** : le scissor ne réduit
  que le coût **écran-espace** (composition/resolve). Il agit donc pleinement sur le **compositing
  underwater** (CustomPass `BeforePostProcess`, purement écran-espace → clampé à la portion immergée :
  `underwater 0.50 → ~0.25` **défendable**). **MAIS** le coût **dominant** d'une **Planar Reflection
  Probe** est le **re-rendu de la scène dans la RT planar**, **indépendant de la portion d'écran** où
  le résultat est échantillonné : le scissor ne réduit que la **composition** de la réflexion, pas ce
  re-rendu. L'ancienne estimation `reflection 1.20 → ~0.60 par scissor` était donc **optimiste/fausse**
  et est **retirée**. La vraie réduction du coût planar pendant la transition passe par la **dégradation
  de la RT planar** (résolution abaissée et/ou culling range resserré), ce qui **converge vers la parade
  (b)**. → Estimation de mitigation à **RE-CHIFFRER au proto P2** (le `3.28 ms` autrefois annoncé n'est
  plus valide car il reposait sur la réduction erronée de `reflection`).
- **Parade (b) — dégradation `SSR → planar → ciel` (+ RT planar en résolution/culling réduits)**
  pendant la transition à Ultra. **Reste valide et sera vraisemblablement la bonne** (c'est elle qui
  attaque réellement le re-rendu de la RT planar).
- Si le hardware **n'autorise pas** la troncature des passes écran-espace : **mesurer le coût plein
  des deux passes au proto P2 et acter la parade (b)**.

**Marquage** : *« pic transition Ultra > 4 ms = dé-risque ouvert ; le clamp scissor n'agit QUE sur
l'écran-espace (underwater réduit réellement, PAS le re-rendu de la RT planar) ; mitigation réelle =
parade (b) dégradation planar → ciel + RT réso/culling réduits, à chiffrer au proto P2 »*.

> **Discipline de mesure (point 3 de la révision du plan V1) — condition de validité de tout chiffre
> qui alimente T2 :** le budget = **SOMME de 3 postes GPU** (a) surface/GBuffer, (b) FFT/spectre
> (`Ocean.Spectrum` englobant, recorder GPU dédié), (c) `Ocean.MotionVector` — sur la **colonne BUILD
> RTX 2060 (SEULE autorité)**, lue via **Development Build + fenêtre Profiler GPU** (D3D12/Vulkan). L'**éditeur
> est indicatif** : le poste (a) y est isolé par **DELTA `MeshRenderer.enabled` ON/OFF** (draw call confirmé
> disparu au FrameDebugger) — **corroboration, pas verdict**. ⚠️ Le delta ne voit PAS le poste (b) (dispatchs
> FFT non gatés par le renderer) → poste (b) sur recorder propre, **non nul** exigé, jamais 0. Le **pivot T2 ne
> se tranche JAMAIS sur une seule valeur éditeur** (exiger SOMME build + corroboration 2 profils). Preset
> porteur = **Ultra**. Protocole complet : `OCEAN_TEST_P2.md §(c)/(i)/(i-bis)/(i-build)/(j)`.

### 2.5 Ce que chaque preset coupe

| Levier (réponse de cadrage) | Low | Medium | High | Ultra |
|---|---|---|---|---|
| Cascades (Q2.2) | 2 | 4 | 4 | 4 |
| Réso FFT (Q2.3) | tout-256² | 4×256² | 1×512²+3×256² | 2×512²+2×256² |
| Réflexions (Q5.1) | ciel seul (planar OFF) | ciel + planar | ciel + planar | ciel + planar (+SSR V1.5+) |
| Tessellation/LOD (Q3.2/Q3.3) | plafonné bas | médian | haut | haut (~200k tris) |
| Caustiques (Q8.1) | OFF | OFF/léger | ON | ON |
| Single-scattering (Q6.3, V1.5) | OFF | OFF | OFF/léger | ON (V1.5) |

> **Note** : tout overhead / marge éventuel doit apparaître en **ligne explicite** dans la
> table lorsqu'il sera mesuré à P2 (pas de coût caché). Le cumul par phase sert de
> **garde-fou de non-régression** : à chaque jalon, le total mesuré du preset ne doit pas
> franchir son plafond.

---

## 3. Phases P0→P10 détaillées

> **Ordre ancré sur `OCEAN_GUIDELINES.md` §13.2** (P2 = première surface deferred = proto P2).
> Marquage version : **V1 = P0–P5** ; **V1.5/V2 = P6–P9**. Chaque phase = **≥3 sous-tâches
> ancrées aux fichiers réels** + **≥1 critère de validation mesurable** + **snapshot de fin**.
> La **boucle de développement du §1** s'applique à chacune (non répétée ici).

---

### P0 — Scaffolding (livre le *framework*) — V1

✅ **IMPLÉMENTÉ & VALIDÉ (2026-06-29)**

**Sous-tâches réalisées :**
1. ✅ Arborescence `Assets/Shader/Ocean_v2/` + `Assets/Shader/Ocean_v2/.backups/` créée (Q13.1/Q13.2).
2. ✅ Types centraux implémentés :
   - `OceanProfile.cs` — ScriptableObject `Create > Ombrage/Ocean/Ocean Profile`.
   - `OceanFeatureModule.cs` — base abstraite, propriété virtuelle `WantsContinuousRepaint` (défaut `false`).
   - `OceanSystem.cs` — MonoBehaviour `[ExecuteAlways]` `Add Component > Ombrage/Ocean/Ocean System`.
   - `OceanApplyContext.cs` — contexte Apply/Tick.
   - `OceanGlobalCache.cs` — **push global NON CUMULATIF/restaurable (anti-bug n°1)**.
   - **Harnais d'instrumentation** : `OceanProfiler.cs` (ProfilerMarker/CommandBuffer squelette), `OceanMotionVectorPass.cs` (stub custom MotionVector).
   - Infrastructure menu/attributes (`OceanModuleMenuAttribute.cs`, éditeur `OceanProfileEditor.cs`).
3. ✅ **7 modules-stubs** listés en sous-assets (spectre/surface/underwater/reflection/absorption/shore/wake) — chacun `Apply()` no-op en P0.

**Critères de validation vérifiés :**
- ✅ Compile sans erreur.
- ✅ `OceanProfile` instanciable en éditeur (`Create > Ombrage/Ocean/Ocean Profile`).
- ✅ 7 modules listés dans l'inspecteur, ajoutables/supprimables via `OceanProfileEditor`.
- ✅ `OceanSystem` s'attache au GameObject sans exception (`Add Component > Ombrage/Ocean/Ocean System`).

**Corrections post-revue appliquées :**
- **#3 (repaint éditeur, CORRIGÉ)** : `SceneView.RepaintAll()` n'est plus appelé inconditionnellement chaque frame. Ajout de `OceanFeatureModule.WantsContinuousRepaint` (défaut `false`) + helper `OceanSystem.NeedsContinuousRepaint()`. La SceneView ne se rafraîchit en continu que si ≥ 1 module ACTIF animate la surface. En P0 (stubs inertes) → aucun repaint forcé.
- **#2 (smoke test EditMode, DIFFÉRÉ à P1+)** : test EditMode requiert un asmdef de test incompatible avec la décision « pas d'asmdef production ». À revisiter en P1 quand de la logique réellement testable existera.
- **#1 (compilation éditeur, GATE UTILISATEUR)** : la validation en éditeur reste à charge de l'utilisateur (UnityLockfile présent, pas d'exécution batch possible). Protocole : laisser recompiler, inspecteur du profil → Add Module → ajouter/lister stubs, OceanSystem sur GameObject.

**Snapshot de fin :** `.backups/01_after_P0_scaffolding/` + `MANIFEST.md` annotant les correctifs #1/#2/#3.

---

### P1 — Spectre (livre le module `spectre`) — V1

> **Décomposé en 2 SOUS-phases internes** P1.a / P1.b. **Renvoi externe = « P1 »** (P1.a/P1.b
> ne sont PAS des phases de premier rang ; aucun document tiers ne doit créer P1a/P1b).

#### P1.a — Dé-risque IFFT hermitienne 2-en-1 (**PERF UNIQUEMENT**, 1 cascade)

**Sous-tâches :**
1. Implémenter l'IFFT hermitienne 2-en-1 sur **1 SEULE cascade**, signaux emballés =
   **`height` + `choppiness_x`** (2 signaux réels → **1 IFFT complexe** = 1 dispatch).
2. Instrumenter le **ratio dispatches hermitiens / naïfs** pour ces signaux.
3. Snapshot intermédiaire (sous-phase).

**Critère de sortie (baseline EXPLICITE par signaux) :**
- **Ratio structurel de dispatches = 0.50** : `height + choppiness_x` packagés en 1 IFFT complexe
  (2 signaux réels → 1 dispatch) **vs** 2 dispatches séparés sur **ces 2 mêmes signaux**.
- Mesure faite sur le **RATIO hermitien/naïf des SEULS signaux calculés en P1.a**, **PAS**
  vs l'ancien système 3-IFFT.
- + **spectre stable sur 1 cascade**.
- ⚠️ **NE PAS inscrire « corrige bug n°3 » ici** (P1.a est perf-only).

> **⚠️ Nature du ratio 0.50 — précision (revue documentaire 2026-07-04).** Le ratio « 2 signaux réels
> dans 1 IFFT complexe vs 2 dispatches » est **vrai PAR CONSTRUCTION** — c'est la **définition** de
> l'astuce hermitienne, **pas une mesure**. Il valide seulement le **go/no-go structurel** de la
> sous-phase (le packing est correctement en place). Le **VRAI dé-risque perf de P1.a = le gain NET
> en millisecondes**, une fois **déduits** les surcoûts de **packing/dépacking** (`OceanPack2`) et de
> **symétrisation hermitienne** `h̃(−k)=h̃*(k)`. Ce gain net **n'est mesurable qu'au proto P2**
> (premiers ProfilerMarkers sur RTX 2060). **La roadmap reporte déjà cette mesure ms à P2** (cf. §1.4
> et Étape 5 MEMORY) : P1.a n'est donc PAS re-validé sur le ratio seul — le **verdict perf définitif
> (net ms) reste dû au proto P2**, où le repli « 3 IFFT séparées » serait sanctionné si le net s'avérait
> rédhibitoire.

#### P1.b — Simulation complète (livre le module `spectre` ; **porte la correction des bugs n°2/n°3**)

**Sous-tâches :**
1. **JONSWAP** (pic γ ≈ 3.3) + correction **TMA `tanh(kh)`** activable par profondeur, **branche TMA INACTIVE en V1** (deep-water 191 m, Q2.1).
2. **Dérivées analytiques en domaine spectral** (`∂η/∂x = i·h̃·k̂x`, Jacobien analytique) → **corrige le bug n°2** (normales par différences finies → analytiques).
3. **Normalisation `1/N` strictement découplée de l'amplitude** → **corrige le bug n°3** (Q2.4).
4. **4 cascades golden-ratio** (Q2.2) + **résolution mixte par cascade** (Q2.3) + **packing 2 textures** (déplacement + dérivées).
5. **Emballage hermitien généralisé** : 5 signaux (`height`, `Hx`, `Hy`, `Jxx`, `Jyy`) par paires → gain global ~ pair-wise.

**Critère de validation (mesurable) :** spectre stable sur 4 cascades ; **amplitude découplée
de N** (changer la résolution ne change PAS la hauteur des vagues — anti-bug n°3 vérifié) ;
normales analytiques (anti-bug n°2 vérifié) ; **pas de répétition visible** (golden-ratio) ;
budget `spectre` dans la cible du preset (§2.2).

**Snapshot de fin :** `.backups/01_P1_spectre_validated/` + manifeste (cascades, réso mixte, gain hermitien mesuré, preuve découplage N).

---

### P2 — Surface deferred = **PROTO P2** (livre le module `surface`) — V1

> **GATE EXPLICITE au démarrage de P2** :
> 1. **Convergence OBLIGATOIRE du rattrapage pré-P0** (homogénéité A–D Q1.1→Q5.3, voir §5)
>    avant de démarrer P2 — point de convergence non bloquant pour P0/P1, **bloquant pour P2**.
> 2. **Géométrie Q3.2 VERROUILLÉE** sur **tessellation hardware gatée strictement par
>    distance** ; P2 **MESURE** si le GBuffer tessellé (~200k tris, Q3.3) tient le budget.
>    Sinon, le **pivot mesh clipmap est DÉJÀ sanctionné par Q3.4** et exercé **sans nouvel aval**.

**Sous-tâches :**
1. Surface **GBuffer / DeferredOnly minimale** : déplacement (depuis cascades P1) + normales analytiques (Q3.1).
2. **Passe MotionVector** custom (position précédente → TAA/motion blur corrects sur surface animée).
3. **Instrumentation complète** (ProfilerMarker/CommandBuffer) → **MESURE du budget GBuffer réel RTX 2060**.

**Critère de validation (mesurable) :** la surface **s'éclaire/s'ombre comme l'herbe**
(LightLoop + ombres + APV) ; **budget GBuffer mesuré** ; **table module×preset (§2)
VERROUILLÉE/réconciliée** sur la mesure ; **gate T2 tranché** (tessellation tient OU pivot
clipmap exercé) ; **reconfirmation des tensions** : **T2 décidée**, **T1 et T3 pré-évaluées** (§4).

**Snapshot de fin :** ~~`.backups/02_P2_surface_deferred_validated/`~~ → **CORRIGÉ : slots `04`/`05`**
(les slots `02`/`03` étaient déjà pris par P1 : `02_before_P1_spectre`, `03_P1_spectre_validated`).

> **RÈGLE DE SLOTS RETENUE POUR P2 :** `.backups/04_before_P2_surface_deferred/` (AVANT, état P1 intact)
> et `.backups/05_P2_surface_deferred_validated/` (APRÈS), chacun + `MANIFEST.md` (budget GBuffer mesuré
> à remplir, décision géométrie, table réconciliée).
>
> **⚓ RÉCONCILIATION DES SLOTS (mise à jour 2026-07-05) — voir le LOCUS CANONIQUE en §1.1(b).** La
> collision historique `04`/`05` est purgée : le schéma **Pn → `2n` (before) / `2n+1` (validated)** est
> **valide et respecté pour P0–P2** (slots `00`→`05` conformes et inchangés sur disque). **⚠️ En revanche,
> l'ancienne renumérotation P3→P10 (`P3=06/07 … P10=20/21`) est SUPERSEDED / PROVISOIRE** et **n'est plus
> présentée comme acquise** : le **banc de gates P2** (hors formule de phase) a **consommé `06`**
> (`06_before_P2_gates/`, présent sur disque) et **réserve `07`** (futur `07_P2_gates_validated/`). **État
> disque réel : `00`→`06` présents ; `07` réservé au banc de gates P2.** En conséquence **P3 ne doit PAS
> présumer `06`/`07` libres** ; la **renumérotation P3→P10 est différée au setup P3** (strictement identique
> à `OCEAN_IMPLEMENTATION_STATUS.md` §« Convention de snapshots — banc de gates P2 »). Les 8 lignes
> « Snapshot de fin » P3→P10 ci-dessous portent la mention SUPERSEDED et renvoient au locus canonique §1.1(b).

> **État P2 (2026-07-04) — code + durcissement + correction runtime complets, MESURE = gate utilisateur restant.**
> Code, shaders, instrumentation et **durcissement de la stabilité MV** (garde dimensionnelle au bind +
> rebind noir compatible array au switch de preset + invalidation MV sur saut de slider LookDev + passe
> `SceneSelectionPass` tessellée) sont en place et revus (cf. `.backups/05_P2_surface_deferred_validated/MANIFEST.md`).
> 
> **Correction runtime appliquée (2026-07-04)** : `OceanProfiler.cs` l.66–67 — ajout du flag
> `SumAllSamplesInFrame` aux appels `ProfilerRecorder.StartNew` (résout NotSupportedException sur marqueurs GPU
> agrégeant plusieurs échantillons/threads par frame). Const `kGpuFrameOptions = GpuRecorder | SumAllSamplesInFrame`
> factorisée ; applies aux deux recorders GBuffer + Ocean.MotionVector. Gate étape 0 PASSÉ : compilation **0 erreur**,
> Test Runner **12/12 verts** ✓.
>
> **Restent un gate UTILISATEUR** (non productibles par l'agent : GPU Profiler = play-mode + Graphics Jobs OFF,
> RenderDoc « calculate timings ») : le **budget GBuffer réel RTX 2060** (cases « mesuré » de §2.2 encore
> vides) et donc la **décision T2** (tessellation ~200k tris tient le plafond **OU** pivot clipmap Q3.4
> sanctionné). **T2 = À TRANCHER sur mesure**, aucun chiffre fabriqué ici. **T1** reconfirmée partiellement
> (spectre+surface, décision définitive P5), **T3** pré-évaluée (note qualitative au MANIFEST ; décision P3).

#### Révision du plan V1 — fiabilité du banc de validation P2 (5 points du gardien du scope)

> **Engagements de niveau plan** conditionnant l'exécution des gates 1–4. Le **détail fait
> autorité dans les satellites** (`P2_REVISION_PLAN.md` = point d'entrée unique, `OCEAN_TEST_P2.md` =
> protocole) ; on **renvoie** ici, on ne duplique pas. Ces 5 points sont des **règles de fiabilité du
> banc** (process), PAS des décisions de cadrage → `OCEAN_DECISIONS.md` (rang 1) **inchangé**.
> ⚠️ La numérotation ci-dessous suit le **brief** ; `P2_REVISION_PLAN.md §A` ordonne les mêmes points
> différemment (son #2 = mesure budget, son #3 = snapshots) — c'est le **sujet** qui fait foi, pas le rang.

| # (brief) | Point | Statut | Renvoi canonique (détail) |
|---|---|---|---|
| **1** | **Aucune scène `.unity` en YAML manuel** — fabrication par **editor-script one-shot** (`NewScene`+`SaveScene`), recompilation + contrôle **Missing Script = 0**, MCP en repli | ✅ acté / outillé | `P2_REVISION_PLAN.md §A(1)` · `Assets/Editor/Ocean/OceanP2GateSceneBuilder.cs` · `OCEAN_TEST_P2.md §« Mise en place… »` |
| **2** | **Convention de numérotation des snapshots** — **05 immuable**, `06`/`07` = banc de gates P2, jamais d'écrasement ; réconciliation de la cascade P3→P10 (voir §1.1(b)) | ✅ acté | `OCEAN_IMPLEMENTATION_STATUS.md §« Convention de snapshots »` · §1.1(b) ci-dessus (LOCUS CANONIQUE) |
| **3** | **Mesure du budget GPU** — **Game view seule**, delta **ON/OFF via `MeshRenderer.enabled`**, table annotée **« éditeur » vs « build »**, **jamais** de pivot T2 sur une seule valeur éditeur | ✅ protocole écrit | `OCEAN_TEST_P2.md §(i)/(i-bis)/(j)` · discipline reprise au §2.4 |
| **4** | **Recompilation utilisateur obligatoire après tout changement shader** — Console vide + **EditMode 12/12** avant tout gate play-mode | ✅ prérequis inscrit | `OCEAN_TEST_P2.md §(a-bis)` (gate 0-bis) · boucle §1.1(c)/(d) |
| **5** | **Résolution dynamique du chemin HDRP** — glob `Library/PackageCache/com.unity.render-pipelines.high-definition@*` + fallback `Packages/`, version vérifiée via `package.json` | ✅ outillé | `P2_REVISION_PLAN.md §A(5)` · `Assets/Editor/Ocean/OceanHdrpPath.cs` |

---

### P3 — Absorption (livre le module `absorption`) — V1

**Sous-tâches :**
1. **Beer-Lambert spectral** `I = I₀·exp(−σ·d)` par canal, **3 profils Jerlov Ia/II/III** (Q6.1).
2. Master `waterType [0..1]` (interpolation 3 ancres) + `WaterAbsorptionProfile` (ScriptableObject) (Q6.2).
3. **Push global NON cumulatif** (`SetGlobalVector` = assignation, anti-bug n°1) — **source de vérité UNIQUE** partagée surface + (futur) sous-marin (Q6.1).

**Critère de validation (mesurable) :** couleur cohérente en surface ; profils commutables
live ; un seul global `_WaterAbsorption` poussé ; push vérifié non cumulatif (revue de code).

**Snapshot de fin :** `.backups/09_P3_absorption_validated/` + manifeste (snapshot AVANT = `.backups/08_before_P3_absorption/`, créé au setup). ✅ **NUMÉROTATION ACTÉE au setup P3 (2026-07-05)** — cascade `(2n+2)/(2n+3)`, locus canonique §1.1(b).

---

### P4 — Écume (feature `foam` → budget agrégé à `surface`) — V1

**Sous-tâches :**
1. **Jacobien pré-filtré (Dupuy/erf)** multi-échelles, seuil doux `erf` (μ/σ² en mipmaps), **1 seul paramètre** `jacobian_scale` (Q7.1).
2. **Crêtes SEULES** (Q7.2) — pas d'ambiant ni rivage en V1.
3. **Foam RT persistante** (ping-pong + décroissance/frame), **1 paramètre** `foamFadeRate` (Q7.3), accumulation toroïdale (suivi caméra pleine mer).

**Critère de validation (mesurable) :** écume **anti-aliasée** à toutes distances (zéro
shimmer au loin) ; crêtes crédibles ; traînée qui se dissipe ; budget agrégé à `surface`
dans la cible du preset.

**Snapshot de fin :** `.backups/09_P4_foam_validated/` + manifeste. ⚠️ **NUMÉRO SUPERSEDED / PROVISOIRE** — renumérotation P3→P10 différée au setup P3, **ne pas présumer `06`/`07` libres** (banc de gates P2 ; voir §1.1(b)).

---

### P5 — Réflexions (livre le module `reflection`) — V1

**Sous-tâches :**
1. **Ciel (cubemap)** toujours actif → **Planar Reflection Probe** en V1 (Q5.1) ; **SSR reporté V1.5+**.
2. **AA** : exposé/configurable par preset de qualité (SMAA signal fort, verrou tranché au proto — Q5.3).
3. **IMPLÉMENTER le gating actif** des passes de réflexion ocean quand la caméra est **immergée** (prérequis du modèle d'exclusion mutuelle, §1.3).

**Critères de sortie (mesurables) :**
- **(i)** Test **SSR sur surface ocean custom HLSL non-WaterStack** : **absence de z-fighting /
  auto-réflexion**, cohérence vs planar (FrameDebugger).
- **(ii)** **Gating immergé implémenté et vérifié** (les passes reflection ocean s'éteignent en immersion).
- **Jalon de DÉCISION DÉFINITIVE T1** (frame émergé complet désormais connu, §4).

**Snapshot de fin :** `.backups/11_P5_reflection_validated/` + manifeste (compat planar confirmée, test SSR mesh custom, gating immergé). ⚠️ **NUMÉRO SUPERSEDED / PROVISOIRE** — renumérotation P3→P10 différée au setup P3, **ne pas présumer `06`/`07` libres** (banc de gates P2 ; voir §1.1(b)).

---

### P6 — Dessus/dessous + sous-marin COMPLET (livre le module `underwater`) — V1.5

**Sous-tâches :**
1. **Double-sided + masque per-pixel + ménisque** à la ligne d'eau (Q4.1, V1).
2. **Fenêtre de Snell** (θc ≈ 48.6°) + **fog volumétrique** + **god-rays** (Q9.1).
3. **CustomPass post-GBuffer** (`BeforePostProcess`) consommant le `_WaterAbsorption` partagé (Q3.1/Q6.1) ; éclairage sous-marin = **modulation NON destructive** restaurée (anti-bug n°1).

**Critère de validation (mesurable) :** transition propre dessus/dessous (pas de `discard`
abrupt) ; absorption partagée identique surface/sous-marin ; budget `underwater` mesuré.

**JALON DE RE-VALIDATION T1 :** confronter la **mesure réelle underwater P6** à l'**estimation
table** (underwater+caustics 0.30/0.35/0.40/**0.50**). **Si écart > 0.2 ms sur un preset →
T1 ré-arbitrée** à la lumière de la mesure (§4).

**Snapshot de fin :** `.backups/13_P6_underwater_validated/` + manifeste (mesure underwater réelle vs estimation, verdict re-validation T1). ⚠️ **NUMÉRO SUPERSEDED / PROVISOIRE** — renumérotation P3→P10 différée au setup P3, **ne pas présumer `06`/`07` libres** (banc de gates P2 ; voir §1.1(b)).

---

### P7 — Caustiques (feature `caustics` → budget agrégé à `underwater`) — V1.5

**Sous-tâches :**
1. **Rayons réfractés projetés en espace texture**, résolution fixe (256²/512²) → **coût GPU constant** (Q8.1).
2. **Monochromes** (intensité scalaire ; teinte venant gratuitement de l'absorption Q6.1) (Q8.2).
3. Blend **décal multiplicatif** sur fond + objets immergés.

**Critère de validation (mesurable) :** caustiques sur fond/objets, **coût indépendant** du nombre de pixels visibles ; budget agrégé à `underwater` tenu.

**Snapshot de fin :** `.backups/15_P7_caustics_validated/` + manifeste. ⚠️ **NUMÉRO SUPERSEDED / PROVISOIRE** — renumérotation P3→P10 différée au setup P3, **ne pas présumer `06`/`07` libres** (banc de gates P2 ; voir §1.1(b)).

---

### P8 — Sillages (livre le module `wake`) — V1.5+

**Sous-tâches :**
1. **Stamp RT consolidé** — **1 SEUL chemin wake** (élimine le throwaway 2 impls + 2 fades, Q10.1).
2. **Technique tranchée au proto** (stamp RT Kelvin vs hybride WP-FFT, sur mesure réelle — Q10.2).
3. Fade unique (1 seul modèle de dissipation).

**Critère de validation (mesurable) :** sillage crédible ; **une seule** implémentation et **un seul** fade ; budget `wake` tenu.

**Snapshot de fin :** `.backups/17_P8_wake_validated/` + manifeste. ⚠️ **NUMÉRO SUPERSEDED / PROVISOIRE** — renumérotation P3→P10 différée au setup P3, **ne pas présumer `06`/`07` libres** (banc de gates P2 ; voir §1.1(b)).

---

### P9 — Rivage / shore (module **DORMANT V1**) — V1.5/V2

**Sous-tâches :**
1. **Squelette `shore` inactif** (présent, désactivé en V1 — Q12.4/Q1.4).
2. Aucune RT toroïdale terrain↔eau, aucune succion en V1 (Q10.3).
3. Déformation/interaction rivage **différée V2** (amorçable V1.5 avec le côtier).

**Critère de validation (mesurable) :** le module `shore` existe, est listé, et **n'a aucun coût runtime** (0.00 ms, §2.2) ; aucun couplage eau↔terrain actif.

**Snapshot de fin :** `.backups/19_P9_shore_dormant_validated/` + manifeste. ⚠️ **NUMÉRO SUPERSEDED / PROVISOIRE** — renumérotation P3→P10 différée au setup P3, **ne pas présumer `06`/`07` libres** (banc de gates P2 ; voir §1.1(b)).

---

### P10 — UX / presets / scalabilité (finalise les budgets) — V1

**Sous-tâches :**
1. Master `oceanState` + valeurs **dérivées en %** + **6 presets Beaufort** `OceanSeaStatePreset` (Q12.2).
2. **Clamps `OnValidate`** sur tous les champs ; **4 niveaux** Low/Med/High/Ultra `OceanQualityProfile` (Q11.3).
3. **Vent WindZone partagé** en facteur relatif (`_OceanWind*`, Q12.3) ; **finalise les budgets tous presets** (§2).

**Critère de validation (mesurable) :** presets commutables ; clamps actifs (aucune valeur
hors borne possible) ; les 4 niveaux qualité respectent leur plafond budget (§2) sur RTX 2060.

**Snapshot de fin :** `.backups/21_P10_ux_presets_validated/` + manifeste (budgets finalisés tous presets). ⚠️ **NUMÉRO SUPERSEDED / PROVISOIRE** — renumérotation P3→P10 différée au setup P3, **ne pas présumer `06`/`07` libres** (banc de gates P2 ; voir §1.1(b)).

---

## 4. Arbitrage des 3 tensions résiduelles

> Chaque arbitrage est une **décision PROVISOIRE** marquée **« à reconfirmer au proto P2 »**,
> assortie du **critère de données** qui la validerait/invaliderait. **Aucune décision de
> cadrage n'est rouverte.**

### T1 — Q1.5 ↔ Q8.1/Q9.1 (hiérarchie V1/V1.5 ↔ ambition underwater/caustiques)

**Décision EN DEUX TEMPS + RE-VALIDATION :**
- **(a) Au proto P2** — données partielles (spectre + surface) → **RECONFIRMATION PARTIELLE
  non décisionnelle** (P2 EST le point de reconfirmation partielle de T1).
- **(b) À P5** (frame émergé complet connu) — **DÉCISION DÉFINITIVE**, arrimée au TOTAL-FRAME :
  promouvoir underwater en V1 **seulement si** le **frame IMMERGÉ projeté** ET le **frame
  TRANSITION projeté** restent **≤ 4 ms AVEC marge ≥ 1.0 ms** ET immersion fréquente (Q9.2).
- **(c) RE-VALIDATION après P6** — la projection P5 utilise l'**estimation table underwater =
  0.50 (Ultra)**. **Si la mesure réelle P6 dépasse l'estimation de > 0.2 ms sur un preset,
  T1 est ré-arbitrée** à la lumière de la mesure.

**Critère d'invalidation :** frame immergé OU transition projeté > 4 ms (ou marge < 1.0 ms) → underwater reste V1.5.

### T2 — Q11.2 ↔ Q3.2/Q3.3 (budget ↔ géométrie) — REFORMULÉE

**Q3.2 est VERROUILLÉE** sur « tessellation hardware gatée strictement par distance » +
Q3.3 « ~200k tris ». La prose §3 GUIDELINES « mesh clipmap préféré » n'est qu'un **DÉFAUT
pré-décision** (à aligner sur le verrouillé, cf. §5 — liste fond pour aval).

T2 **n'est donc PAS** « choisir la géométrie » (déjà fait) **mais** : *« le GBuffer tessellé
~200k tris tient-il le budget 2–4 ms ? »*.

**Décision PROVISOIRE :** avancer avec **tessellation gatée + ~200k tris** ; **MESURER au proto
P2** le coût GBuffer tessellé vs surface clipmap équivalente. **Si dépassement → exercer le
PIVOT mesh clipmap, DÉJÀ SANCTIONNÉ par Q3.4 → AUCUN nouvel aval requis.** (Gate au démarrage de P2, §3.)

**Protocole de mesure (verrouillé) :** le budget se lit comme **SOMME de 3 postes GPU** — (a) surface/GBuffer,
(b) FFT/spectre (marker **englobant `Ocean.Spectrum`**, instrumenté `SampleGPU` + recorder dédié depuis
2026-07-05), (c) `Ocean.MotionVector` — sur la **colonne BUILD RTX 2060 (seule autorité)** ; l'éditeur est
indicatif. ⚠️ Le toggle `MeshRenderer.enabled` ne coupe PAS les dispatchs FFT → poste (b) mesuré par son
propre recorder, jamais 0 injecté. Preset porteur = **Ultra**. Détail : `OCEAN_TEST_P2.md §(c)/(i)/(i-build)/(j)`.

**Critère d'invalidation :** budget GBuffer tessellé mesuré > plafond preset au proto P2 → une optimisation
bornée **re-validée** (gate 0-bis + gates 1–3 + re-mesure build) ; si toujours hors budget → pivot clipmap.

**STATUT (2026-07-05) : T2 NON TRANCHÉ — gate 4 OUVERT.** Instrumentation des 3 postes complétée (poste (b)
rendu mesurable), méthodologie build définie ; **en attente des relevés build RTX 2060 fournis par l'utilisateur**
(les 3 postes + somme, non nuls). Aucun chiffre fabriqué. Verdict à écrire ici quand la SOMME build Ultra sera renseignée.

### T3 — Q6.3 ↔ type III trouble (Beer-Lambert pur a(λ) ↔ eau côtière turbide)

**Décision PROVISOIRE :** **absorption pure a(λ) SEULE en V1** ; single-scattering (froxels) en V1.5 (Q6.3).

**Critère (proto P2/P3) :** rendre le profil **type III** et le comparer à une **photographie
de référence**. Si le rendu vert/jaune turbide est **inacceptable sans diffusion** → soit
**(a)** acter une **fidélité type III réduite en V1**, soit **(b)** anticiper une **approximation
single-scattering bon marché**.

**Critère d'invalidation :** type III jugé non crédible vs photo sans diffusion → fidélité réduite V1 ou single-scattering anticipé.

---

## 5. Références croisées & cohérence inter-documents

### 5.1 Statut des documents

- **`OCEAN_DECISIONS.md`** (dans le projet) : **⭐ table canonique 41/41 = source de RANG 1 des
  décisions actées** (rapatriée hors de `Library/` le 2026-07-04). C'est ELLE qu'on cite pour toute
  décision Q1.1→Q13.3.
- **`OCEAN_ROADMAP.md`** (ce fichier) : **plan exécutif — fait foi pour l'ordre/contenu des phases**.
- **`OCEAN_GUIDELINES.md`** : cadrage (questionnaire ①②③ + specs A–D Q1.1→Q5.3 ajoutées pré-P0).
  **Source de vérité des specs A–D Q1→Q5**, PAS des décisions (→ `OCEAN_DECISIONS.md`) ni du plan exécutif.
- **`MEMORY.md`** (session, `Library/…`) : **miroir de travail** — conserve les specs A–D Q6.1→Q13.3
  « sous la table ». **N'est plus la source de rang 1** des décisions (dossier `Library/` régénérable).
- **`OCEAN_CADRAGE_STATUS.md`** : **réduit à un POINTEUR** (revue documentaire 2026-07-04) — son
  ancien résumé divergeait (ordre de phases, réso FFT, statut S9). Il ne contient plus que des renvois
  vers `OCEAN_DECISIONS.md` (décisions) et `OCEAN_ROADMAP.md` (phases). Plus aucun contenu de fond
  susceptible de diverger.

### 5.2 Renvois par phase → specs de cadrage

| Phase | Specs de cadrage sources |
|---|---|
| P0 | Q12.1, Q12.4, Q13.1, Q13.2 |
| P1 | Q2.1, Q2.2, Q2.3, Q2.4 |
| P2 | Q3.1, Q3.2, Q3.3, Q3.4 |
| P3 | Q6.1, Q6.2 |
| P4 | Q7.1, Q7.2, Q7.3 |
| P5 | Q5.1, Q5.2, Q5.3 |
| P6 | Q4.1, Q4.2, Q9.1, Q9.2, Q6.1 |
| P7 | Q8.1, Q8.2 |
| P8 | Q10.1, Q10.2 |
| P9 | Q10.3, Q12.4, Q1.4 |
| P10 | Q11.1, Q11.2, Q11.3, Q12.1, Q12.2, Q12.3 |

### 5.3 Sources externes (état de l'art)

Renvoi aux **sources [1]–[12]** d'`OCEAN_GUIDELINES.md` §14 (Ryan 2025 [1], Tessendorf 2004 [2],
Dupuy/Inria [3], WP-FFT arXiv 2025 [4], Sea of Thieves [5], Jerlov [6], Premoze & Ashikhmin [7],
GPU Gems caustiques [8], Monzon CGF 2024 [9], HDRP docs [10], Crest [11], Beaufort [12]).

### 5.4 Cohérence entretenue

- `OCEAN_GUIDELINES.md` §13.2 porte désormais un renvoi « plan exécutif détaillé → OCEAN_ROADMAP.md ».
- `OCEAN_CADRAGE_STATUS.md` a été **réduit à un pointeur** (2026-07-04) : son ancien résumé divergent
  (ordre de phases, réso FFT, statut S9) est retiré ; il ne fait plus que renvoyer vers
  `OCEAN_DECISIONS.md` (décisions) et `OCEAN_ROADMAP.md` (phases). Plus de contenu de fond divergent.
- **Table canonique rapatriée** : les décisions 41/41 vivent désormais dans `OCEAN_DECISIONS.md`
  (rang 1, dans le projet) ; le `MEMORY.md` session en est un miroir de travail.
- **« Ne pas polluer »** : les décisions vivent dans `OCEAN_DECISIONS.md` ; le plan exécutif ICI ;
  les specs A–D dans GUIDELINES/MEMORY.
