# OCEAN_TEST_P2.md — Protocole de validation éditeur (Phase P2 — Surface deferred)

> **Définition de « P2 terminée » dans cet environnement** = code + instrumentation + protocole
> **ÉCRITS et revus**. La **mesure GBuffer réelle sur RTX 2060** et le **verrouillage de la table
> budget** restent un **gate utilisateur** (cases « budget mesuré » vides dans les manifestes).
>
> Cible matérielle minimale = **RTX 2060** (dev = RTX 3080 Ti). Plafond évoqué : pic budget ~4 ms.

---

## (a) Pré-check — gate P1 (prérequis)

P2 lit les sorties de simulation P1. **Avant tout test P2**, confirmer que P1 est validé :

1. Protocole P1 exécuté (cf. `OCEAN_IMPLEMENTATION_STATUS.md` → « Protocole de test éditeur P1 ») :
   Console vide + log `[Ocean P1.a] … ratio≈0.50` + normales lisses (Mode=2) + contrôle visuel OK.
2. P1 marqué validé.

> P1 n'est **PAS modifié** en P2 (le hook `PreSimulate` ajouté est virtuel no-op, non override par
> `OceanSpectrumModule`). Ce gate ne porte donc que sur la **base de lecture** des globaux P1.

## Mise en place de la scène de test — FABRICATION PAR EDITOR-SCRIPT (jamais de YAML manuel)

> **Méthode PRIMAIRE : editor-script one-shot** (déterministe, versionné, aucune écriture de
> `fileID`/`GUID` à la main). MCP CoplayDev = repli. La `.unity` n'est **jamais** rédigée en YAML.

1. Menu **`Ombrage/Ocean/Build P2 Gate Scene`** (`OceanP2GateSceneBuilder`). Il enchaîne :
   - **(garde anti-perte)** `SaveCurrentModifiedScenesIfUserWantsTo()` AVANT tout `NewScene` — si tu
     annules, le build s'interrompt et **aucune scène n'est détruite**.
   - construction du **profil de gate** (`OceanP2Gate.profile.asset`) = **EXACTEMENT 2 modules actifs**
     `Simulation/Spectrum` (P1) → `Rendering/Surface` (P2), aucun stub contaminant (via
     `OceanP2GateProfileBuilder`) ;
   - construction du **VolumeProfile de gate DÉTERMINISTE** (`OceanP2GateEnv.volumeprofile.asset` via
     `OceanP2GateEnvBuilder`) : **GradientSky actif** (ambient **Dynamic**, sans bake) + **Exposure = Fixed**
     (EV connu, cf. MANIFEST 06) + **Fog off**. **L'APV est RETIRÉE du banc** (bake non déterministe) ;
     le seul Sky fournit ambient **et** réflexion, reproductibles ;
   - **caméra HDRP** au-dessus de l'eau (dans `gridExtent`), **TAA + Post-processing + Motion Vectors +
     Object Motion Vectors** forcés dans les Frame Settings (sans TAA le critère « pas de ghosting » du
     gate 2 serait vacuous) ;
   - **lumière directionnelle** épinglée (rotation figée = déterminisme de l'éclairage direct) ;
   - **Volume GLOBAL** (`isGlobal`) câblé sur le VolumeProfile de gate (un Volume **sans** profil ne fait rien) ;
   - **vérification HDRP Asset** : support Motion Vectors ON (sinon LogError explicite) ;
   - **assertions de fabrication BRUYANTES** (abort si non satisfaites) : profil Spectrum+Surface actifs,
     env Sky+ExposureFixed → log `[P2Gate] profil OK: Spectrum+Surface actifs ; env OK: Sky+ExposureFixed`.
2. **GATE UTILISATEUR post-build (0 « Missing Script »)** : ouvrir `OceanP2Gate.unity`, confirmer
   **0 script manquant** sur l'`OceanSystem` et **Console 0 erreur** AVANT tout gate.
3. Chemin HDRP/version : menu **`Ombrage/Ocean/Log HDRP Package Path + Version`** (`OceanHdrpPath`)
   logue `resolvedPath` + version (attendue **17.4.0**, warning sinon) — résolution dynamique, aucun chemin codé en dur.

> **Pourquoi un environnement déterministe (correctif bloquant C) :** la surface est un Lit **très lisse**
> dominé par la **réflexion d'environnement**. Sans Sky, l'ambient/réflexion par défaut sont **noirs** →
> l'eau paraît noire par **ciel manquant**, ce qui ferait du gate 1 (« eau non noire ») un **faux négatif**
> et rendrait la colonne Deferred Lighting du gate 4 **non déterministe** (auto-exposition). GradientSky
> est choisi car **indépendant de l'angle solaire** et **sans prérequis** sur le HDRP Asset.

---

## (a-bis) GATE 0-bis — Recompilation shader + EditMode (APRÈS toute édition shader/HLSL, AVANT tout gate play-mode)

> **Aucun gate 1–4 ne démarre tant que 0-bis n'est pas vert.** À rejouer après **chaque** édition de
> shader/HLSL (l'édition d'un `.shader`/`.hlsl` n'invalide pas toujours le domaine C# → forcer le réimport).

1. **Réimport shaders** : sélectionner les 4 fichiers (`OceanSurface.shader`, `OceanSurfaceData.hlsl`,
   `OceanSurfaceTessellation.hlsl`, `OceanSurfaceCascadeSampling.hlsl`) → clic droit **Reimport** (ou **Ctrl+R**).
2. **Domain reload** terminé (barre de progression Unity retombée).
3. **Console VIDE** : 0 erreur **et** 0 warning de compilation shader.
4. **Test Runner EditMode** : `Ombrage.OceanFeatures.Tests` → **12/12 verts**.
5. **Vérif child runtime** (correctif D) : menu **`Ombrage/Ocean/Verify Surface Runtime Present`** →
   il retrouve le MeshRenderer via `GetComponentsInChildren<MeshRenderer>(includeInactive:true)` (le GO
   réel s'appelle **`OceanSurface (runtime)`** et porte `HideAndDontSave` → **jamais** `Transform.Find`) ;
   le renderer doit être **présent et `enabled`**. Absence = **gate d'entrée EN ÉCHEC**.

> **Checklist copiable :** ☐ Reimport 4 shaders ☐ domain reload ☐ Console 0 err/warn ☐ EditMode 12/12
> ☐ `Verify Surface Runtime Present` = OK.

> ⚠️ **LIMITE de gate 0-bis — les passes ÉDITEUR ne sont PAS compilées ici.** Le domain reload / Reimport
> ne compile que les variants réellement demandés par l'éditeur. Les passes `SceneSelectionPass` (LightMode
> `SceneSelectionPass`) et `ScenePickingPass` (LightMode `Picking`) ne sont compilées **qu'au clic Scene View**
> (sélection/picking) **ou en BUILD** si le shader est en *Always Included Shaders*. Un défaut de compilation
> **localisé à ces 2 passes** (ex. identifiants `_ObjectId`/`_PassValue`/`_SelectionID` non déclarés, corrigé
> le 2026-07-05e) **passe gate 0-bis au vert** tout en cassant le build. **Après toute édition shader touchant
> le préambule `HLSLINCLUDE` ou une passe, ajouter à 0-bis :** (i) cliquer la surface océan dans la Scene View
> (déclenche picking/selection → compile ces 2 passes en éditeur), **et** (ii) une **compilation BUILD** de
> contrôle (cf. §(i-build), pré-condition 0) — Console de build **0 erreur shader**. Ce n'est qu'à ce prix que
> « compile » signifie « compile toutes les passes ».

---

## (b) Compilation

- Laisser Unity recompiler. **Console VIDE** (0 erreur). Les 4 nouveaux fichiers shader
  (`OceanSurface.shader`, `OceanSurfaceData.hlsl`, `OceanSurfaceTessellation.hlsl`,
  `OceanSurfaceCascadeSampling.hlsl`) doivent s'importer/compiler sans erreur.
- **Restructuration en assembly (round de correction) :** Ocean_v2/Profile est désormais compilé dans
  l'assembly `Ombrage.OceanFeatures` (+ `.Editor` pour `OceanProfileEditor.cs`). Confirmer que la
  recompilation post-import des `.asmdef` laisse la **Console VIDE** (0 erreur C#) — c'est la première
  compilation réelle de cette frontière d'assembly.
- **Test EditMode (filet anti-régression C#) :** ouvrir **Window ▸ General ▸ Test Runner ▸ EditMode**,
  lancer `Ombrage.OceanFeatures.Tests` → **12/12 verts** attendus (11 tests P2 smoke + 1 bonus). Ils vérifient : shader de surface
  trouvable + instanciable, `GenerateUniformGrid` (comptes/clamp/format d'index/normale/bounds), et la
  logique `SameArrayDims` du durcissement 3C. Un échec ici pointe une rupture de compilation ou de logique
  **avant** même le gate play-mode.
- Le `OceanSurfaceModule` actif crée un GameObject runtime `OceanSurface (runtime)` (caché,
  `HideAndDontSave`) enfant de l'`OceanSystem`, avec MeshFilter + MeshRenderer (Per Object Motion).

## (c) Identification des 3 markers GPU du budget (instrumentation)

> **Le budget océan T2 = SOMME de TROIS postes GPU**, chacun mesuré par son PROPRE `ProfilerRecorder`
> (`GpuRecorder | SumAllSamplesInFrame`), jamais un seul :
> - **(a) SURFACE / GBuffer** — marker HDRP de la passe GBuffer. Nom **réel** à confirmer (candidats
>   `OceanPerfRecorders` : `RenderGBuffer`, `GBuffer`, `Render GBuffer`).
> - **(b) FFT / spectre** — marker custom **englobant `Ocean.Spectrum`** (`MarkerFlags.SampleGPU`), qui
>   couvre **évolution + IFFT + assemblage**. ⚠️ `Ocean.Spectrum` **enveloppe** `Ocean.FFT` dans
>   `EvolveAndTransform` → **ne PAS additionner** les deux (double comptage) ; le poste (b) = `Ocean.Spectrum`
>   **seul**. `Ocean.FFT` reste une **sous-mesure** de l'IFFT, visible au Profiler pour ventiler.
> - **(c) MOTION VECTOR** — marker custom `Ocean.MotionVector` (`SampleGPU`).

- **Lecture des markers = fenêtre Unity Profiler (module GPU), PAS un HUD in-build.** Ouvrir
  **Window ▸ Analysis ▸ Profiler → GPU** (en Play éditeur, ou **connectée à un Development Build** via
  **AutoConnect Profiler**) : les markers `RenderGBuffer`, `Ocean.Spectrum`, `Ocean.MotionVector`
  apparaissent dans la timeline GPU avec leur coût ms. Le **Rendering Debugger** (Frame/Detailed Stats)
  corrobore GBuffer / Deferred Lighting / Motion Vectors.
- **NB mécanisme :** `OceanPerfRecorders.Readout()` produit une chaîne des 3 postes, mais **aucun HUD/inspecteur
  ne l'affiche aujourd'hui** (pas de CustomEditor ni d'`OnGUI` câblés ; `showPerfReadout` est un booléen sans
  affichage associé). **Ne PAS s'appuyer sur un « readout inspecteur » pour la mesure** — la source de vérité
  runtime est la **fenêtre Profiler GPU** ci-dessus. Les recorders restent le chemin programmatique (utile si
  un HUD est ajouté plus tard).
- Si le poste (a) affiche « (non identifié) »/`n/a` dans le Profiler : le nom de passe diffère → le consigner
  et ajuster `kGBufferMarkerCandidates` dans `OceanProfiler.cs`. **Fallback (a)** : delta GPU via FrameDebugger
  > GPU Usage sur la passe GBuffer **avec/sans** la surface (corroboration éditeur, cf. §(i)).
- **Pré-condition d'API graphique (edge-case D3D11) :** `GpuRecorder` peut renvoyer 0/`n-a` selon le backend
  (D3D11 sur config Windows par défaut le refuse parfois — cf. commentaire `OceanProfiler.cs`). **Vérifier en
  Development Build sur RTX 2060 que les 3 recorders renvoient du NON-NUL** ; si D3D11 les refuse, **forcer
  D3D12 (ou Vulkan)** dans *Project Settings ▸ Player ▸ Graphics APIs* pour le build de mesure, et **consigner
  l'API utilisée** comme paramètre du relevé.

## (d) GATE ÉCLAIRAGE EXPLICITE (≠ « compile sans erreur »)

> Un stencil GBuffer erroné fait **écrire** la surface mais la fait **sauter** par le LightLoop → **eau noire**.
> **Prérequis : le VolumeProfile de gate est chargé** (GradientSky + Exposure Fixed). L'environnement étant
> déterministe, un rendu noir signale bien la **SURFACE** (stencil/LightLoop) et **non l'absence de ciel**.

- La surface **reçoit la lumière directionnelle directe + l'ambient/réflexion du GradientSky** (LightLoop deferred).
- **Critère (reformulé, pas le simple binaire « non noir ») :** la **réflexion de ciel est plausible et le
  gradient de surface est visible** — ni tout-noir (surface cassée / ciel absent), ni tout-blanc (glint
  spéculaire saturé masquant une surface cassée sur ce Lit quasi-miroir). Si l'image est saturée d'un côté,
  ajuster **une seule fois** `fixedExposure` (`OceanP2GateEnvBuilder.FixedExposureEV`) et documenter au MANIFEST 06 ;
  la valeur étant réutilisée à l'identique ON/OFF, elle **n'affecte pas** le delta du gate 4.
- Valide le couple stencil répliqué (GBuffer Ref=2/WriteMask=3, vérifié vs `Lit.shader` 17.4).
- **Prérequis du proxy build poste (a) — confirmer 1 SEUL écriveur GBuffer :** au build de la scène de gate,
  `OceanP2GateSceneBuilder` logue `[P2Gate] MeshRenderer actifs … = N (ATTENDU = 1)`. Confirmer **N = 1**
  (« OceanSurface (runtime) ») ; corroborer au **FrameDebugger** qu'un **seul** draw call écrit le GBuffer.
  Si N > 1, la mesure build `(a) = GBuffer total ≈ surface` est **invalidée** → retirer l'écriveur parasite.

## (e) Tessellation ~200k triangles (±20 %)

- **Stats** (overlay) ou **FrameDebugger** sur la passe GBuffer : compter les triangles rendus de la surface.
- Cible **~200 000 tris** (tolérance **±20 %**) près de la caméra. Ajuster par les paramètres du module :
  `maxTessFactor`, `tessMinDist`, `tessMaxDist`, `baseResolution`, `gridExtent`.
- **Gating distance** : la densité **décroît avec la distance** et **retombe à 1.0** au-delà de `tessMaxDist`
  (la tessellation s'éteint ; le coût hull/domain fixe demeure — comportement attendu, doc HDRP 17).
- **Aucune géométrie explosée / NaN** (l'`OnValidate` garantit `tessMaxDist > tessMinDist`).

## (f-PRÉCHECK) Avancée RÉELLE de la simulation (avant tout test MV)

> Distinguer une **sim figée légitime** d'une **copie cassée** AVANT de conclure quoi que ce soit sur les MV.

- Caméra **fixe**, vérifier que le **déplacement évolue effectivement** entre deux frames (la surface bouge ;
  ou via une slice de debug P1). En edit-mode, le repaint continu (P1 `WantsContinuousRepaint=true`) doit
  faire avancer le temps.
- **Si D est constant** (temps non avancé) → MV vagues nuls **NORMAUX** (pas un bug). **NE PAS conclure
  « off-by-one »** tant que la simulation n'avance pas.

## (f1) Motion Vectors — caméra STRICTEMENT FIXE + vagues animées

- Visualiser le buffer Motion Vectors (FrameDebugger / Debug HDRP « Motion Vectors »).
- La surface doit produire des **MV NON NULS** (les vagues bougent alors que la caméra est fixe).
- **Si MV nuls ALORS QUE la sim avance** ⇒ `prev == current` ⇒ **anomalie de copie** (revoir le coordinator :
  ordre `PreSimulate` → évolution, garde `Time.frameCount`, `_OceanMVValid`).

## (f1-MULTI) Test MULTI-CONTEXTE OBLIGATOIRE (la preuve anti-race)

- Ouvrir **Scene view ET Game view SIMULTANÉMENT** + placer **une sonde de réflexion** dans la scène.
- Refaire (f1) : les **MV vagues doivent être NON NULS et COHÉRENTS dans LES DEUX vues**.
- Justification : la copie étant faite dans `PreSimulate` **avant** l'évolution, tous les contextes d'une
  même frame lisent `prev=D[N-1]`. **Un échec ici** signalerait une régression de l'invariant d'ordre
  `PreSimulate → évolution` (les deux balayages d'`OceanSystem`).

## (f2) Motion Vectors — caméra EN TRANSLATION ET ROTATION

- Déplacer/tourner la caméra : **pas de ghosting/smearing TAA** notable sur l'eau.
- Inspecter les **bandes de transition de LOD** (paliers de quantification) : un léger résidu au
  **franchissement de palier** est attendu/connu (limite HDRP). S'il est visible, **élargir le pas de
  quantification** (réduire `tessQuantLevels`) ou augmenter `refCamSnap`.

## (g) Premier frame / toggle module / changement de preset (ALLER-RETOUR)

- **Toggle** du module surface (ON/OFF) et **switch** `Simulation/Spectrum.cascadeQuality`.
- **Test d'ALLER-RETOUR obligatoire** : `Ultra → Low → High → Ultra` (chaque étape change la
  structure d'arrays/slices : Ultra=2×512²+2×256², High=1×512²+3×256², Low=tout-256²).
  - **Console vide** : aucune erreur de *dimension mismatch* ni de RT détruite.
  - **Aucun flash / traînée MV** persistant au-delà du frame de bascule : MV **nuls le seul frame
    du switch**, puis stables. Trois protections cumulées ferment la fenêtre d'un frame :
    1. le coordinator réalloue le tampon prev en **miroir strict** et force `_OceanMVValid=0` le
       frame de (ré)allocation (`OceanMotionVectorPass.EnsureMirror`) ;
    2. **garde dimensionnelle au bind** (`OceanSurfaceModule.BindMotionVectors`) : si le
       `_OceanDisp512/256` courant diffère en structure du tampon prev (ou apparition/disparition
       d'un groupe, ex. Low sans 512²), `_OceanMVValid=0` ce frame ;
    3. quand un groupe prev est **nul** (Low sans 512²), `_OceanDispPrev*` est rebindé sur un
       **noir compatible array** (`OceanMotionVectorPass.BlackArray`), jamais une RT détruite ni un
       `Texture2D` simple (qui provoquerait un mismatch de sampler sur le `TEXTURE2D_ARRAY`).

## (g2) LookDev — saut de slider d'état de mer / amplitude / choppiness

- Caméra fixe, **tirer brusquement** `oceanState`, `amplitude` ou `choppiness` (module Spectrum) :
  le champ de vagues se déplace d'un coup (à dimensions d'arrays inchangées).
- **Aucun smear TAA persistant** : `BindMotionVectors` détecte le changement de hash de paramètres
  de déplacement (`DisplacementParamHash`) et force `_OceanMVValid=0` **le seul frame** du saut →
  MV nuls ce frame, puis stables. (Complément de (h) : les bounds sont aussi recalculés à chaud.)

## (g3) Picking / sélection Scene View sur l'eau déplacée

- En **vue rasante** avec vagues hautes, **cliquer** sur une crête dans la Scene View : le clic doit
  accrocher la crête déplacée, pas le plan de base à Y=0 (passe `ScenePickingPass` **tessellée**,
  LightMode `"Picking"`).
- Une fois la surface sélectionnée, son **contour de sélection** doit épouser les vagues déplacées
  (passe `SceneSelectionPass` **tessellée**), pas un rectangle plat à Y=0.
- Les deux passes partagent le hull/domain tessellé des passes géométriques (miroir de
  `LitTessellation.shader`). Confirmer **0 erreur de compilation shader** dans la Console au premier
  Reimport (surtout `ScenePickingPass` / `SceneSelectionPass`). Repli documenté en cas d'échec :
  `#undef TESSELLATION_ON` local sur ces deux passes (picking/contour retombent alors sur le plan de base).

## (h) Bounds à chaud (anti-pop en vue rasante)

- Augmenter `amplitude` / `choppiness` (module Spectrum) et/ou `maxWaveHeight` (module Surface).
- **Aucun pop / clipping des crêtes** en vue rasante : les bounds XYZ sont **recalculés** sur changement de
  hash, et `maxHorizontalDisplacement` est **auto-dérivé** des cascades (× `boundsSafetyScale`).

## (i) CORROBORATION ÉDITEUR du poste (a) — DELTA via `MeshRenderer.enabled`, jamais le flag `active`

> **RECLASSEMENT (autorité de mesure) :** le delta `MeshRenderer.enabled` ON/OFF et le FrameDebugger sont
> **EDITOR-ONLY** (`OceanSurfaceRendererToggle` est un `[MenuItem]` dans `Assets/Editor/`, le FrameDebugger
> n'existe pas en build). Ils servent donc à **ISOLER FINEMENT le poste (a) surface sur la machine de dev
> (RTX 3080 Ti) — CORROBORATION, pas verdict.** L'**autorité du verrou T2 = la colonne BUILD RTX 2060**
> (§(i-build) ci-dessous), où le poste (a) est lu comme **GBuffer total** (la scène de gate isolée n'a pas
> d'autre écriveur GBuffer). Le delta reste précieux : il prouve que le GBuffer total ≈ surface seule dans
> cette scène (les deux doivent concorder à l'ordre de grandeur), validant le proxy build.

> **CORRECTIF A (bloquant) :** le flag `active` du module ne gate QUE `PreSimulate`/`Apply`/`Tick`. Le
> MeshRenderer de la surface vit de `OnModuleEnable` à `Teardown` → décocher `active` **laisse la surface
> DANS le GBuffer** → delta **~0 FAUX**. Le mode **OFF réel** = `MeshRenderer.enabled = false`.
>
> **CORRECTIF D (bloquant) :** le child runtime réel s'appelle **`OceanSurface (runtime)`** (espaces inclus)
> et porte `HideAndDontSave` → **invisible et non sélectionnable** en hiérarchie. `Transform.Find("OceanSurface")`
> renvoie **null** et **aucune manipulation manuelle n'est possible**.

- **Mode OFF réel** : menu **`Ombrage/Ocean/Toggle Surface Renderer`** (`OceanSurfaceRendererToggle`) —
  retrouve le renderer par `GetComponentsInChildren<MeshRenderer>(includeInactive:true)` (filtre de secours
  nom exact `OceanSurface (runtime)`), **jamais** `Transform.Find`.
- **CONDITION BINAIRE (obligatoire avant de valider tout delta)** : au **FrameDebugger**, confirmer que le
  **draw call `OceanSurface` DISPARAÎT** en OFF. Sinon le delta est **rejeté** (mesure invalide).
- **(a) Coût GBuffer surface (corroboration éditeur) = DELTA** = GBuffer(renderer ON) − GBuffer(renderer OFF) ;
  **jamais** une valeur absolue **en éditeur**. (En build, poste (a) = GBuffer total, cf. §(i-build).)
- **(b) FFT/spectre** : lu par le recorder GPU **`Ocean.Spectrum` (englobant)**. ⚠️ Le toggle `MeshRenderer.enabled`
  **ne coupe PAS** les dispatchs compute FFT → ce poste est **invisible au delta (a)** et se lit sur son PROPRE
  recorder, en éditeur ET en build. **Vérifier qu'il renvoie une valeur NON NULLE sur une frame animée** (sim
  qui avance) — « le marker existe » ne suffit pas.
- **(c) Ocean.MotionVector** (ms) lu par le ProfilerRecorder GPU (`SampleGPU`).
- Garde de validité : valeur écrite seulement si `recorder.Valid && Count>0`, sinon **« n/a »** (jamais 0 trompeur).
  Un poste à `n/a`/0 = **donnée MANQUANTE → gate ouvert**, jamais injecté comme 0 dans la somme.
- **REPLI OFFICIEL si le menu-item échoue (correctif D) — PAS de manipulation manuelle (objet caché) :**
  **méthode de corroboration à 2 profils** (deux Play distincts) — un `OceanProfile` **Spectrum-only** (surface
  jamais créée) vs le **profil de gate** Spectrum+Surface — avec **confirmation FrameDebugger** que le draw call
  `OceanSurface` est présent dans l'un et absent dans l'autre. Le delta secours = différence entre ces deux Play.

## (i-bis) Conditions de mesure (répétabilité)

- **Game view MAXIMISÉE seule** (pas de double rendu Scene+Game), fenêtres inutiles fermées.
- **VSync OFF** (Project Settings > Quality > VSync Count = Don't Sync) pour éviter « Present Limited ».
- **~30 frames de stabilisation** avant relevé (moyenne glissante du Rendering Debugger).
- **VolumeProfile de gate chargé** (Exposure **Fixed**) → colonne Deferred Lighting **déterministe**.
- **Deux sources croisées** : (a) **Rendering Debugger** (Window > Analysis > Rendering Debugger, Play mode)
  → Frame Stats (GPU Frame ms) + Detailed Stats (GBuffer / Deferred Lighting / Motion Vectors) ;
  (b) **fenêtre Profiler → module GPU** (Window > Analysis > Profiler), qui affiche directement les markers
  `RenderGBuffer`, `Ocean.Spectrum`, `Ocean.MotionVector` (`GpuRecorder | SumAllSamplesInFrame`). *(Les
  `OceanPerfRecorders` alimentent ces markers ; ils ne sont PAS surfacés dans un HUD/inspecteur — cf. §(c).)*
- **Annotation « éditeur » vs « build » :** le module **GPU** de l'éditeur est **quasi sans overhead** (le
  delta ON/OFF y est probant) ; le **CPU** éditeur a un overhead → pour toute lecture, préférer un
  **Development Build** (AutoConnect Profiler). Chaque cellule de la table est annotée de sa provenance.

## (i-build) CHEMIN DE MESURE BUILD RTX 2060 — seule AUTORITÉ du verrou T2

> **Bloquant : la colonne BUILD ne se mesure NI par le delta `MeshRenderer.enabled` NI par le FrameDebugger**
> (tous deux editor-only). Chemin build défini explicitement :

0. **PRÉ-CONDITION build (compilation complète + visibilité) — à lever AVANT toute mesure :**
   - **(0.a) Compilation BUILD de toutes les passes = 0 erreur shader.** Le shader `OceanSurface` doit être
     inclus au build (matériau référencé par une scène buildée **ou** ajouté à *Project Settings ▸ Graphics ▸
     Always Included Shaders*). Ce faisant, le build compile **TOUTES** les passes, y compris les passes
     éditeur `SceneSelectionPass`/`ScenePickingPass` (non compilées par le gate 0-bis éditeur). La Console de
     build doit être **0 erreur shader** (rappel du défaut corrigé 2026-07-05e : `_ObjectId`/`_PassValue`/
     `_SelectionID` non déclarés → build cassé alors que 0-bis était vert). Un build qui échoue à compiler =
     **gate 4 non atteignable**, corriger d'abord.
   - **(0.b) Océan effectivement VISIBLE dans le build.** Vérifier à l'œil (la surface se rend dans le player).
     Un océan **absent en build** (shader/variant strippé, matériau non référencé, layer/volume manquant,
     **simulation non exécutée**) invalide la mesure : le recorder `RenderGBuffer` ne compterait pas la surface
     (ou compterait une surface plate non déplacée) → poste (a)_build faux.
     - **CAUSE RACINE identifiée (2026-07-05f) — compute shaders non sérialisés dans le profil de gate.**
       Le shader `OceanSurface` est bien inclus (présent dans *Always Included Shaders*, GUID
       `0fef2101cc76b6b46a9b7c2e8fc896e3`), donc l'invisibilité ne venait PAS d'un stripping du shader de
       surface. Le vrai défaut : `OceanP2GateProfileBuilder` créait l'`OceanSpectrumModule` **sans assigner
       `fftShader`/`spectrumShader`**. Le repli `AssetDatabase.LoadAssetAtPath` de
       `OceanSpectrumModule.ResolveShaders` est sous `#if UNITY_EDITOR` → en **éditeur** ça marche, mais le
       profil de gate est SAUVEGARDÉ avec des références **nulles**. En **build** : (1) les `.compute`
       qu'aucun asset buildé ne référence sont **strippés** ; (2) `ResolveShaders` laisse les refs nulles →
       `OnModuleEnable` logue « [Ocean P1] Compute shaders FFT/Spectrum introuvables — module spectre
       inactif » → **aucun `_OceanDisp*` poussé** → surface **non déplacée / invisible**.
     - **CORRECTIF appliqué (2026-07-05f) :** `OceanP2GateProfileBuilder.BuildProfile` charge et **assigne
       explicitement** `spectrum.fftShader`/`spectrum.spectrumShader` (abort BRUYANT + suppression de l'asset
       incomplet si l'un manque). **Durcissement (2026-07-05g, verdict Réviseur) :** l'assertion finale ne se
       contente plus de vérifier les refs EN MÉMOIRE — elle **recharge l'asset et le sous-module Spectrum
       DEPUIS LE DISQUE** (après `SaveAssets`+`ImportAsset`) et exige `computeMémoire=true` **ET**
       `computeDisque=true`, prouvant que les refs ont survécu au round-trip de sérialisation (un simple
       contrôle RAM ne détecterait pas un échec de `SaveAssets`). Le log vert attendu devient « compute
       FFT/Spectrum **SÉRIALISÉS ET RE-VÉRIFIÉS SUR DISQUE** (build-safe) ». Ces refs étant désormais
       sérialisées dans le profil de gate, les `.compute` **survivent au build** et se résolvent **non nuls**
       dans le player. → **RE-CONSTRUIRE la scène de gate** (menu *Ombrage/Ocean/Build P2 Gate Scene*, qui
       régénère le profil ET l'assigne à l'`OceanSystem` de la scène — la chaîne scène→profil→compute est
       reconstruite d'un bloc) AVANT le prochain build, sinon l'ancien profil (refs nulles) persiste.
     - **DURCISSEMENT côté SURFACE (2026-07-05h, verdict Réviseur bloquant #2) :** le correctif (f)/(g) traite
       le chemin du **déplacement** (compute) ; le chemin de la **surface** (rendu) était encore tributaire de
       `OceanSurfaceModule.EnsureMaterial` → `Shader.Find("Custom/HDRP/OceanSurface")` en build (repli
       `AssetDatabase` sous `#if UNITY_EDITOR`), avec reachability des **variants** dépendante du stripper HDRP.
       `OceanP2GateProfileBuilder` **sérialise désormais un matériau d'asset** (`Tests/OceanP2GateSurface.mat`,
       fabriqué depuis `OceanSurface.shader`) assigné à `surface.surfaceMaterialOverride` → le matériau étant
       **référencé par le profil de gate** (donc le build), le shader **et ses variants utilisés** sont tirés
       déterministiquement et `EnsureMaterial` n'appelle **plus** `Shader.Find` en build. L'assertion du builder
       exige le **round-trip disque** du matériau (`surfaceMaterialOverride`≠null ET `mat.shader`≠null). Log vert
       attendu : « compute FFT/Spectrum **+ matériau de surface** SÉRIALISÉS ET RE-VÉRIFIÉS SUR DISQUE (build-safe) ».
     - **Après re-build, si l'océan reste invisible :** vérifier dans le **Player.log** l'absence du warning
       « module spectre inactif » (chemin déplacement) ; le chemin surface étant désormais couvert par le
       matériau sérialisé (durcissement h), suspecter sinon un problème layer/volume/caméra ou un variant de
       rendu HDRP encore strippé malgré le matériau référencé (dernier recours : ajouter le/les variant(s) aux
       *Always Included* / Shader Variant Collection). Tant que l'océan n'est pas rendu dans le player,
       **gate 4 reste OUVERT** (donnée manquante, jamais un 0). Problème de **rendu/stripping/simulation**,
       distinct de la compilation (0.a).
   - **✅ CONFIRMÉ LEVÉ (2026-07-05 i) — retour utilisateur.** « Le build fonctionne, l'océan est bien visible » +
     capture Profiler `C:\Users\Arthe\Ombrage\ProfilerCaptures\Ombrage_2026-07-05_13-15-17.{data,png,highlights}`.
     Le PNG montre la surface rendue/déplacée (0.b ✅) ; aucune erreur shader build (0.a ✅) ; le scan du `.data`
     confirme les markers `Ocean.Spectrum`/`Ocean.FFT`/`Ocean.Surface`/`Ocean.MotionVector` + `RenderGBuffer`
     **enregistrés en build** (instrumentation active, GpuRecorder supporté). **Reste dû (point 4 ci-dessous) :**
     la **lecture numérique** des 3 postes dans le **module GPU** du Profiler (le `.data` binaire n'est pas
     décodable hors Unity) → gate 4 encore OUVERT tant que (a)+(b)+(c)+somme ne sont pas relevés (Ultra, non nuls).

1. **DEVELOPMENT BUILD obligatoire** (Build Settings ▸ *Development Build* + *Autoconnect Profiler*). En
   **release**, les `ProfilerRecorder` GPU et les markers sont **strippés** → aucune donnée (un readout `n/a`
   en release = mesure absente, PAS un coût de 0).
2. **API graphique D3D12 ou Vulkan** (cf. §(c) pré-condition) : confirmer que les 3 recorders GPU renvoient du
   **non-nul** sur RTX 2060 ; si D3D11 les refuse, forcer D3D12/Vulkan et le consigner.
3. **Lecture** via la **fenêtre Unity Profiler (module GPU)** connectée au build (AutoConnect) — timeline GPU
   des 3 markers. (Pas de HUD in-build ; ne pas s'appuyer sur `showPerfReadout`.)
4. **Postes lus en build :**
   - **(a)_build = GBuffer total** (`RenderGBuffer`). **Proxy valide de la surface seule** car la scène de
     gate ne contient QUE `Spectrum + Surface` et le **GradientSky n'écrit pas le GBuffer** (passe de fond) →
     GBuffer total ≈ surface. **Prérequis vérifié au gate 1** (§(d) : exactement 1 MeshRenderer actif).
   - **(b)_build = `Ocean.Spectrum`** (englobant : évolution + IFFT + assemblage).
   - **(c)_build = `Ocean.MotionVector`**.
5. **BUDGET OCÉAN VERROUILLÉ = (a)+(b)+(c) de la colonne BUILD**, comparé à la plage **2–4 ms** (plafond dur
   4 ms, cf. ROADMAP §2). **Biais de mesure :** le development build est **instrumenté** (borne HAUTE — léger
   surcoût de sampling). Si la somme **dépasse 4 ms de peu**, distinguer l'overhead d'instrumentation du coût
   réel (corroborer avec le poste isolé éditeur) **avant** de sanctionner le pivot clipmap.
6. **Preset porteur du verrou = `Ultra`** (pire cas : ROADMAP §2.3 transition Ultra projetée 4.13 ms ;
   tessellation ~200k tris = High/Ultra). `Low` est relevé comme **plancher/sanity**, pas comme verrou.
   > ⚠️ **Portée du verdict T2 à P2 :** la scène de gate ne contient que `spectre + surface + MV`
   > (réflexion/absorption/underwater = P3/P5/P6, **pas encore construits**). La SOMME P2 (a)+(b)+(c) **n'est
   > donc PAS le total-frame** des 4.13 ms Ultra. Le verdict T2 à P2 statue sur : **le poste (a) surface tient-il
   > sa part `surface` (~1.00 ms Ultra, §2.2)** en laissant assez de marge sous 4 ms pour les postes à venir ?
   > Le **total-frame définitif** se reconfirme à P5/P6 quand réflexion+underwater existent (T1).

## (j) Gating budget T2 — verdict sur la SOMME des 3 postes BUILD

- **CONDITION DE CLÔTURE DU GATE 4 :** les **3 postes (a)+(b)+(c) + la SOMME** doivent être **complètement
  renseignés sur la colonne BUILD RTX 2060** pour le preset porteur (**Ultra**), chacun issu d'un recorder GPU
  **non nul**. Si la colonne build est incomplète, ou un poste est `n/a`/0, ou seule la colonne éditeur est
  fournie → **gate 4 reste OUVERT, aucun verdict T2** (cases vides = gate ouvert, jamais de valeur inventée).
- **VERDICT :** si la SOMME build Ultra **tient dans 2–4 ms** (le poste (a) surface restant dans sa part
  ~1.00 ms, cf. §(i-build) point 6) → **CONSERVATION de la tessellation** ; verrouiller le budget mesuré. Si
  **DÉPASSEMENT** (y compris « éditeur tient mais build dépasse » = dépassement) → **ne PAS basculer
  automatiquement** : appliquer **UNE** passe d'optimisation **bornée** (baisse `maxTessFactor` / durcissement
  gating distance / réduction résolution cascade du preset visé).
- **RE-VALIDATION OBLIGATOIRE après optimisation** (elle modifie code/presets) : re-passer **dans l'ordre**
  (1) **gate 0-bis** (recompile + Console 0/0 + EditMode 12/12), (2) **gates 1–3** (réflexion, MV, ~200k tris
  ±20 % — une baisse de tess factor **change le compte de tris** —, bounds anti-pop), (3) **puis** re-mesurer
  les 3 postes du gate 4 sur build. Une re-mesure budget seule (ou éditeur seul) est **INSUFFISANTE**.
- **CONSIGNE VERROUILLÉE :** le pivot clipmap **Q3.4** ne se déclenche **QUE** si, **après** cette tentative
  unique et re-validée, la SOMME build Ultra reste hors budget. Il est **sanctionné** (aucun nouvel aval), mais
  jamais tranché sur une seule valeur éditeur ni sur un delta dont la **disparition du draw call** n'a pas été
  confirmée au FrameDebugger (corroboration éditeur). Exiger : SOMME build (a)+(b)+(c) **+** corroboration
  éditeur (delta `MeshRenderer.enabled` + 2 profils).

## (k) Non-régression P1 (allégée, structurelle)

- Smoke-check après activation de la surface : **Console vide**, log `[Ocean P1.a] … ratio≈0.50`
  toujours présent, **normales lisses** (debug P1). P1 n'étant pas modifié, la non-régression est structurelle.

---

## Table budget RTX 2060 — 3 postes GPU + SOMME (à remplir lors de la mesure réelle — gate utilisateur)

> **AUTORITÉ DES COLONNES.** Colonne **« BUILD RTX 2060 » = SEULE autorité** du verrou 2–4 ms et du verdict T2
> (Development Build, AutoConnect Profiler, D3D12/Vulkan, fenêtre Profiler GPU ; cf. §(i-build)). Colonne
> **« éditeur (RTX 3080 Ti) » = INDICATIVE / corroborante** (détecte une incohérence grossière, valide la
> méthodo) — **ne clôture AUCUN gate**. Le poste (a) éditeur s'isole finement par **delta `MeshRenderer.enabled`
> ON/OFF** (draw call FrameDebugger confirmé disparu) ; le poste (a) build = **GBuffer total** (scène isolée).
> **Chaque cellule est annotée de sa provenance.** Cases vides = **gate ouvert** ; **aucune valeur inventée**.
>
> **Preset porteur du verrou = Ultra** (pire cas). Low = plancher/sanity.

| Poste GPU | éditeur — Low | éditeur — Ultra | **BUILD — Low** | **BUILD — Ultra (verrou)** | Notes |
|---|---|---|---|---|---|
| **(a) SURFACE / GBuffer** | non relevé *(corroborant, non requis — verrou tenu)* | non relevé *(corroborant, non requis — verrou tenu)* | non relevé *(plancher/sanity, non bloquant)* | **0.169 ms** ✅ *(marker «GBuffer», module GPU, capture build 2026-07-05)* | build = GBuffer total ≈ surface (scène isolée, cf. §(i-build)) ; part cible ~1.00 ms Ultra (§2.2) → **tenue avec large marge** |
| **(b) FFT / spectre** (`Ocean.Spectrum` englobant) | non relevé | non relevé | non relevé | **1.252 ms** ✅ *(non nul, frame animée)* | recorder propre ; **invisible au delta (a)** ; NE PAS sommer `Ocean.FFT` |
| **(c) Ocean.MotionVector** | non relevé | non relevé | non relevé | **1.143 ms** ✅ *(⚠ 45 % de la somme pour la copie T-1 — candidat n°1 d'optimisation, cf. §(l))* | `GpuRecorder \| SumAllSamplesInFrame` |
| **SOMME (a)+(b)+(c) = BUDGET OCÉAN** | non relevé | non relevé | non relevé | **2.564 ms** ✅ **DANS [2–4 ms] → T2 : TESSELLATION CONSERVÉE** | **verdict T2 rendu sur cette cellule (2026-07-05 j)** |
| Triangles surface (≈) — contrôle gate 3 | — | — | — | acté au gate 3 (2026-07-05 d/e : ~200k ±20 % vérifié) — chiffre exact non re-consigné | cible ~200k ±20% (non additionné à la somme ms) |
| Corroboration 2 profils (Spectrum-only vs gate) | non requis *(verrou tenu au build)* | non requis *(verrou tenu au build)* | — | — | repli si le toggle échoue (corroboration éditeur) |

> **API graphique du relevé build :** **D3D12** (1ʳᵉ API de la liste Windows du projet — `0x12` —, D3D11 en
> repli ; les recorders GPU actifs corroborent D3D12 effectif). **Development Build :** **oui** (recorders/markers
> présents ⇒ relevé = **borne HAUTE**, léger surcoût d'instrumentation). **Preset :** **Ultra** — forcé en dur par
> `OceanP2GateProfileBuilder` (`kCascadeQuality = CascadeQuality.Ultra`), pas de réglage manuel possible.
> **Machine :** RTX 2060 (machine du protocole §(i-build) — si le relevé provenait d'une autre machine, le refaire
> sur RTX 2060 : le verrou l'exige).
>
> **✅ Capture DÉPOUILLÉE (2026-07-05 j) :** `ProfilerCaptures/Ombrage_2026-07-05_13-15-17.{data,png,highlights}`.
> Relevé utilisateur des 3 postes dans le module GPU (frame animée, chacun non nul) : voir table ci-dessus et
> relevé final §(l). NB : un premier dépouillement n'avait produit que les temps **CPU** (Main/Render Thread) ;
> le relevé **GPU** a ensuite abouti dans le module GPU. Le nom réel du poste (a) dans la capture est **«GBuffer»**
> (déjà couvert par `kGBufferMarkerCandidates` d'`OceanProfiler.cs` — harmonisation libellé « RenderGBuffer » faite).
>
> **Rappel plafond (ROADMAP §2) :** 4 ms = plafond dur total-frame ; < 2 ms acceptable. **À P2, la SOMME ne
> couvre que spectre+surface+MV** (réflexion/underwater = P5/P6, pas encore construits) → le verdict T2 statue
> sur la **part surface** et la **marge résiduelle**, pas sur le total-frame définitif (reconfirmé P5/P6, cf.
> §(i-build) point 6 et T1). Si la part surface (poste a) déborde sa cible ou ne laisse pas de marge sous 4 ms →
> optimisation bornée re-validée, sinon **pivot clipmap Q3.4 sanctionné**. **T2 ne se tranche jamais sur une
> seule valeur éditeur.**

## (l) RELEVÉ FINAL BUILD RTX 2060 & CLÔTURE DU GATE 4 — 2026-07-05 (j)

> **GATE 4 = CLOS. VERDICT T2 = TESSELLATION CONSERVÉE (~200k tris). Budget océan P2 verrouillé.**

### Relevé GPU (module GPU du Profiler, capture build dev, preset Ultra, D3D12)

| Poste | Valeur GPU | Lecture |
|---|---|---|
| (a) GBuffer (surface tessellée) | **0.169 ms** | ≪ part cible ~1.00 ms Ultra (§2.2) — très large marge |
| (b) `Ocean.Spectrum` (englobant : évolution + IFFT + assemblage) | **1.252 ms** | ordre de grandeur attendu pour 2×512² + 2×256² |
| (c) `Ocean.MotionVector` (copie T-1) | **1.143 ms** | ⚠ anomalie relative — voir Notes (1) |
| **SOMME = BUDGET OCÉAN P2** | **2.564 ms** | **∈ [2–4 ms]** → plafond dur 4 ms respecté, **marge résiduelle 1.436 ms** pour P3/P5/P6 |

**Application du §(j) :** 3 postes + SOMME renseignés sur la colonne BUILD, preset porteur Ultra, chacun non nul →
condition de clôture satisfaite. SOMME dans 2–4 ms **et** poste (a) très en-dessous de sa part → **CONSERVATION de
la tessellation ; pivot clipmap Q3.4 NON déclenché ; aucune passe d'optimisation requise à P2.**

### Corroboration CPU (même capture — coûts de soumission, tous sains)

| Marker | Main Thread (moy/méd) | Render Thread (moy/méd) |
|---|---|---|
| GBuffer | 0.008 / 0.007 ms | 0.022 / 0.018 ms |
| Ocean.Spectrum | 0.118 / 0.116 ms | 0.318 / 0.298 ms |
| Ocean.MotionVector | 0.007 / 0.007 ms | 0.019 / 0.017 ms |

≈ 0.5 ms CPU cumulés tous threads : aucun signal d'alarme côté soumission (dispatchs FFT bien batchés).

### Notes & réserves consignées

1. **Poste (c) = candidat n°1 d'optimisation (rien à faire maintenant).** `Ocean.MotionVector` n'enveloppe que
   `OceanMotionVectorPass.SnapshotPrevious()` = 2 `Graphics.CopyTexture` full-array (Ultra : 2×512² + 2×256²
   RGBAFloat ≈ 21 Mo de trafic ≈ 0.06 ms théoriques sur RTX 2060). 1.143 ms mesuré ⇒ surcoût dominé par les
   transitions de layout / synchronisations D3D12 autour des copies, pas par la bande passante. Pistes si T1
   (P5/P6) manque de marge : **ping-pong de binding sans copie** (échanger les cibles lues/écrites plutôt que
   copier — demande une retouche du contrat P1, à cadrer), copie des seules slices échantillonnées par la passe
   MV, format réduit du tampon prev. **Décision : différé** — le budget tient, on ne touche pas au code validé.
2. **Périmètre de la somme :** les passes HDRP depth-prepass / MotionVectors-draw de la surface ne sont pas
   couvertes par les 3 marqueurs (le marqueur (c) ne mesure que la copie T-1). Conforme au cadrage §(i-build) 6 :
   la somme P2 n'est PAS le total-frame ; recontrôle total-frame à **T1 (P5/P6)** quand réflexion + underwater
   existeront.
3. **Biais :** Development Build instrumenté = **borne haute** ; le coût réel release est légèrement inférieur.
4. **Snapshot :** `.backups/07_P2_gates_validated/` créé à cette clôture (MANIFEST + SHA-256, présence disque
   vérifiée). Prochaine étape = **setup P3 — Absorption** (y acter la renumérotation des slots P3→P10, ROADMAP §1.1(b)).

## En cas d'échec

- Rollback `.backups/04_before_P2_surface_deferred/` + diagnostic (le snapshot avant-P2 est l'état P1 intact).
- Repères de diagnostic : eau noire → stencil (d) ; MV nuls → (f-PRÉCHECK) puis coordinator (f1) ;
  pop crêtes → bounds (h) ; table vide → nom de marker GBuffer (c).
