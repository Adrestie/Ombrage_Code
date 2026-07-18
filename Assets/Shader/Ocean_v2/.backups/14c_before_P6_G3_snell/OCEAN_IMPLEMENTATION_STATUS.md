# OCEAN_IMPLEMENTATION_STATUS.md — Suivi d'Implémentation (Océan v2)

> **Statut de l'implémentation en temps réel** — document de suivi des phases P0→P10,
> mis à jour à chaque phase validée. Complément actif du plan exécutif OCEAN_ROADMAP.md.

**Dernière mise à jour :** 2026-07-18 (l) — **🏁 P4 CLÔTURÉE — GATE 0-bis RE-CONFIRMÉ + SNAPSHOT 11 MATÉRIALISÉ.** Utilisateur : **EditMode 19/19 verts** (après réouverture d'Unity) ; Console sans erreur/warning (vérifiée MCP, seuls logs du test runner). Constat de clôture : `.backups/11_P4_foam_validated/` était **référencé par STATUS/TEST_P4/OCEAN.md mais ABSENT du disque** (classe « backup fantôme », leçon herbe) → **snapshot CRÉÉ + VÉRIFIÉ le 2026-07-18** (87 fichiers = 81 du périmètre P3 + 6 nouveaux P4, + MANIFEST + HASHES.sha256 ; périmètre miroir 09/10 ; spot-check hash source↔copie OK). Aucun code/shader/test modifié ce run (clôture documentaire + snapshot). **Prochaine étape : setup P5 — Réflexions (slots `12`/`13`).** *(Précédent : 2026-07-06 (k4) — **CORRECTIF COHÉRENCE SLIDER `perceivedDepth` — PROFONDEUR OPTIQUE NORMALISÉE.** Bug : dès d≈8 m aucun effet visible de `perceivedDepth` jusqu'à 200 m (saturation naïve). Cause : le knee de `1−exp(−2σd)` indexé sur |σ| (turbidité). **Correctif shader** : profondeur optique NORMALISÉE par σ̄ (moyenne Rec.709 des σ planchés) + `kDepthRate=0.12` → knee moyen ≈4 m **INDÉPENDANT de la turbidité**. Bornes slider : [0.1..200]→**[0.1..50]** (plage 50–200 saturée). Invariants préservés (d→0 bleu, d→∞ asymptote type inchangée vs k3). Anti-bug n°1 intact (constante shader, zéro nouveau push). Snapshot `08b_before_P3_k4_fix/` créé. — **Prérequis : gate 0-bis (Console 0/0 + EditMode 16/16) puis gates visuels utilisateur (d)/(e) → snapshot 09.** Détail : `OCEAN_TEST_P3.md` §(k4). *(Précédent k3 : 2026-07-05 — **RÉ-SPÉC GATE (d) P3 : COULEURS CORRIGÉES (transmittance → réflectance MONTANTE) + 1ᵉʳ CALIBRAGE ANCRES — vérifié par captures MCP.** Échec gate (d) rapporté (eau **turquoise**, Ia≈II) → diagnostic instrumenté MCP : pipeline σ/globals/shader **sain** (piège `_BaseColor` rouge non rendu) ; cause = **modèle** — `exp(−σ·d)` ne produit jamais un bleu océan (G≈B survivants avec les a(λ) réels). **Correctif `OceanSurfaceData.hlsl`** : albédo = **`b_b_pure/σ × (1−exp(−2σ·d)) × 0.02`** (b_b = constante Rayleigh eau pure (0.206, 0.422, 1.0) — NI paramètre NI scattering V1.5 ; σ reste la source unique). **Calibrage ancres (1ᵉʳ passage, Q6.1 §D)** : II **(0.45, 0.09, 0.12)** · III **(0.60, 0.20, 0.65)** (CDOM côtier → σ_B ↑ ; Ia inchangée) — assets sauvés + defaults builder alignés. **Vérifié captures MCP** (banc temporaire ciel PBR + EV14, intégralement démonté, soleil restauré) : **Ia bleu océan · II bleu-vert · III vert sombre** ✓ (`Assets/Screenshots/P3_v4_*.png`). **Gates visuels (d)/(e) utilisateur RE-DUS sur le nouveau rendu** ; recompile builder (2 littéraux) en file au focus éditeur. Détail : §Ré-spéc (k3) sous P3. *(Précédent : 2026-07-05 (k) — **P3 — ABSORPTION LIVRÉE (code + tests + outillage + protocole) — EN ATTENTE GATE 0-bis + VALIDATION UTILISATEUR.** Setup P3 exécuté : **renumérotation des slots ACTÉE** (`Pn → (2n+2)/(2n+3)`, P3=`08`/`09` … P10=`22`/`23`, locus ROADMAP §1.1(b)) + **snapshot `08_before_P3_absorption/` créé et vérifié** (99 fichiers, SHA-256). Livrables : **`WaterAbsorptionProfile`** (SO, 3 σ a(λ) pur clampés + tooltips, menu Create) ; **`OceanAbsorptionModule` réel** (stub P0 remplacé — 3 ancres Jerlov auto-résolues éditeur, master `waterType` [0..1] par segments avec ancre II=0.5, `perceivedDepth` [0.1..200] m, `Apply` = UNIQUE point de push de `_WaterAbsorption` + `_OceanAbsorptionDepth` via ctx.globals SET pur, ZÉRO σ en dur — sans ancres : aucun push + warning) ; **consommation surface** (`OceanSurfaceModule.BindAbsorption` → `_OceanAbsorptionEnabled`, branche UNIFORME zéro variant ; `OceanSurfaceData.hlsl` : albédo = `lerp(_BaseColor, exp(−σ·d), enabled)`, 3 globaux HORS UnityPerMaterial, collision de noms HDRP 17.4 vérifiée NULLE) ; **`OceanAbsorptionAnchorsBuilder`** (menu create-if-missing, jamais d'écrasement — Ia (0.36, 0.041, 0.028) / II (0.42, 0.065, 0.070) / III (0.50, 0.110, 0.200) m⁻¹, littérature, calibrage dû Q6.1 §D) ; **+4 tests EditMode** (`OceanAbsorptionTests` → **16 attendus**, dont « push non-cumulatif + restore » exécutable) ; **`OCEAN_TEST_P3.md`** (protocole complet). P1 byte-à-byte intact ; MV/tessellation P2 non touchés. **Prochain : gate 0-bis (Console 0/0 + EditMode 16/16) puis gates visuels (d)/(e) de TEST_P3 → snapshot 09.** *(Précédent : 2026-07-05 (j) — **🏁 GATE 4 CLOS — BUDGET OCÉAN P2 VERROUILLÉ — VERDICT T2 : TESSELLATION CONSERVÉE.** Relevé utilisateur des 3 postes GPU dans le module GPU du Profiler (capture build dev 2026-07-05, preset **Ultra** forcé par le profil de gate, API **D3D12**, machine du protocole RTX 2060) : **(a) GBuffer = 0.169 ms · (b) Ocean.Spectrum = 1.252 ms · (c) Ocean.MotionVector = 1.143 ms → SOMME = 2.564 ms ∈ [2–4 ms]** (plafond dur 4 ms), poste (a) très en-dessous de sa part cible ~1.00 ms → **conservation de la tessellation ~200k tris, pivot clipmap Q3.4 NON déclenché, aucune optimisation requise à P2**. Marge résiduelle sous plafond = **1.436 ms** pour P3/P5/P6 (la somme P2 ne couvre pas depth-prepass/MV-draw → recontrôle total-frame à T1/P5-P6). **⚠ Poste (c) (copie T-1, ~21 Mo ≈ 0.06 ms théoriques) = 45 % de la somme → candidat n°1 d'optimisation, DIFFÉRÉ** tant que le budget tient (ping-pong de binding sans copie / slices réduites). Corroboration CPU saine (~0.5 ms cumulés). **Snapshot `07_P2_gates_validated/` CRÉÉ** (MANIFEST + SHA-256, présence disque vérifiée). **P2 = INTÉGRALEMENT VALIDÉE ; prochaine étape = setup P3 — Absorption** (renumérotation slots P3→P10 à y acter, ROADMAP §1.1(b)). Détail : `OCEAN_TEST_P2.md` §(l) + table budget. *(Précédent : 2026-07-05 (i) — **Build fonctionnel + océan VISIBLE en build (retour utilisateur) → pré-conditions 0.a/0.b du gate 4 LEVÉES.** L'utilisateur rapporte « le build fonctionne maintenant et l'océan est bien visible » et fournit une capture Profiler : `C:\Users\Arthe\Ombrage\ProfilerCaptures\Ombrage_2026-07-05_13-15-17.{data,png,highlights}`. **Preuves exploitées par l'agent** (qui ne peut lancer Unity) : (1) le **PNG** montre une surface océan rendue (visibilité confirmée) ; (2) un scan du `.data` confirme que les markers `Ocean.Spectrum`/`Ocean.FFT`/`Ocean.Surface`/`Ocean.MotionVector` **et** `RenderGBuffer` sont **enregistrés dans le build** → l'instrumentation GPU (correctif (b)) est **active en build** et le backend graphique du build supporte le GpuRecorder (pré-condition edge-case D3D11 franchie de fait). Les correctifs compile-BUILD (e) + visibilité (f)/(g)/(h) sont donc **effectifs** : pré-condition **0.a (compile toutes passes)** ✅ et **0.b (océan visible)** ✅. **BLOQUANT RÉSIDUEL du gate 4 = lecture numérique.** Le `.data` (213 Mo, format binaire Profiler non documenté) **ne peut PAS être décodé par l'agent** pour extraire le coût GPU par marker ; les 3 postes budget **(a) `RenderGBuffer` / (b) `Ocean.Spectrum` / (c) `Ocean.MotionVector` + la SOMME** doivent être **relevés par l'utilisateur** dans **Window ▸ Analysis ▸ Profiler → module GPU** (capture ouverte), preset **Ultra**, sur une frame animée, chacun **non nul**. **Aucune valeur inventée : tant que les 3 ms + somme ne sont pas fournis, gate 4 reste OUVERT, verdict T2 non tranché, snapshot 07 NON créé.** Aucun code/shader modifié ce run (l'instrumentation (b) et les correctifs (c)/(e)/(f)/(g)/(h) sont déjà en place et validés en build). (Précédent : 2026-07-05 (h) — **Durcissement visibilité surface en BUILD (verdict Réviseur, bloquant #2).** `OceanP2GateProfileBuilder.cs` : le profil de gate **sérialise désormais un matériau de surface** (`Tests/OceanP2GateSurface.mat`, fabriqué depuis `OceanSurface.shader`) assigné à `OceanSurfaceModule.surfaceMaterialOverride` → chemin surface **build-déterministe** (aucun `Shader.Find` runtime, variants du matériau tirés dans le build) ; assertion étendue au round-trip disque du matériau (log vert « compute + matériau de surface SÉRIALISÉS ET RE-VÉRIFIÉS SUR DISQUE »). Complète le correctif (f) (compute/déplacement) côté SURFACE (rendu). **RE-CONSTRUIRE la scène de gate avant build. Gate 0-bis + re-vérif compilation BUILD + gate 4 restent OUVERTS ; PAS de snapshot 07 ce run** (efficacité build non prouvable par revue de code). (Précédent : 2026-07-05 (e) — **Correctif compilation shader en BUILD (passes SceneSelection/ScenePicking).** `OceanSurface.shader` : déclaration des 3 variables `int _ObjectId; int _PassValue; float4 _SelectionID;` HORS du cbuffer `UnityPerMaterial` (copie verbatim de HDRP/Lit `LitProperties.hlsl` L294-298), dans le `HLSLINCLUDE`. Corrige `undeclared identifier '_ObjectId'` (`ShaderPassDepthOnly.hlsl:112`, passe **SceneSelectionPass**) et `'_SelectionID'` (`:114`, passe **ScenePickingPass**, via la macro `unity_SelectionID`→`_SelectionID` de `ShaderVariables.hlsl`), remontés par l'utilisateur au **build** après ajout de `OceanSurface` aux *Always Included Shaders* (le build force la compilation de TOUTES les passes, y compris les 2 passes éditeur ; l'éditeur ne les compilait pas → gate 0-bis vert malgré le défaut). La surface océan remplace `LitData.hlsl` par `OceanSurfaceData.hlsl` et n'incluait donc jamais ces déclarations. **Même classe de défaut que le `positionRWS` (c) : visible seulement à la compilation réelle de toutes les passes, ici en build. → Re-vérif compilation BUILD requise (pas seulement gate 0-bis éditeur) avant reprise du gate 4 ; gates restent OUVERTS ; PAS de snapshot 07 ce run. Note : gate 4 (mesure build) reste par ailleurs bloqué en amont par « l'océan n'est pas visible en build » — à diagnostiquer côté rendu/stripping, distinct de ce correctif de compilation.** (Précédent : 2026-07-05 (c) — **Correctif régression compilation shader (gate P2).** `OceanSurface.shader` : ajout des includes core `GeometricTools.hlsl` + `Tessellation.hlsl` dans le `HLSLINCLUDE` (entre `Common.hlsl` et `ShaderVariables.hlsl`), calqué sur `LitTessellation.shader`. Corrige `undeclared identifier 'positionRWS'` (`VaryingMesh.hlsl:322`, macro `TESSELLATION_INTERPOLATE_BARY` non définie) remonté par l'utilisateur au gate P2 1–3 sur la passe ShadowCaster — défaut présent dès le snapshot 05, invisible à gate 0-bis (aucun rendu) et à la revue statique. **→ Gate 0-bis à RE-passer (code shader modifié) avant reprise des gates P1/P2 ; gates restent OUVERTS ; PAS de snapshot 07 ce run.** (Précédent : 2026-07-05 (b) — **Instrumentation du poste budget (b) FFT/spectre + méthodologie de mesure BUILD.** Correctif ciblé `OceanProfiler.cs` : markers `Ocean.Spectrum`/`Ocean.FFT` passés `SampleGPU`, 3ᵉ `ProfilerRecorder` GPU sur le marker **englobant** `Ocean.Spectrum` (poste b = évolution+IFFT+assemblage ; `Ocean.FFT` = sous-mesure, NON additionnée), exposé dans `Readout()`. `OCEAN_TEST_P2.md` : table budget restructurée en **3 postes GPU + SOMME**, colonne **BUILD RTX 2060 = seule autorité**, chemin build explicité (Development Build + Profiler GPU, `showPerfReadout` NON câblé écarté), delta `MeshRenderer.enabled`/FrameDebugger reclassés **corroboration éditeur**, preset porteur = **Ultra**. **→ Gate 0-bis ROUVERT (changement de code) ; gates P1/P2/gate 4 restent OUVERTS (mesures utilisateur non fournies) ; PAS de snapshot 07 ce run.** *(Précédent : 2026-07-05 (a) — consolidation « Révision du plan V1 » : 5 points tracés ROADMAP+STATUS, réconciliation table de slots, 06/07 = banc de gates P2.)*))*)*)*)*

---

## Synthèse Rapide

| Phase | Statut | Snapshot | Notes |
|---|---|---|---|
| **P0** | ✅ **IMPLÉMENTÉ + CORRECTIFS** | `.backups/01_after_P0_scaffolding/` | Framework + 7 modules stubs ; correction #3 (repaint) appliquée ; #1/#2 documentés |
| **P1** | ✅ **IMPLÉMENTÉ — EN ATTENTE VALIDATION ÉDITEUR** | `.backups/02_before_P1_spectre/` (avant) · `.backups/03_P1_spectre_validated/` (après) | Spectre FFT complet : JONSWAP γ≈3.3 + TMA dormant + dérivées analytiques + 4 cascades golden-ratio mixtes ; IFFT hermitienne 2-en-1 (P1.a) ; bugs #2/#3 corrigés à la source. **Compilation OK après correction OceanProfiler.cs (MarkerFlags.SampleGPU).** |
| **P2** | ✅ **VALIDÉE — GATES 0-bis + 1–4 VERTS, BUDGET VERROUILLÉ, T2 TRANCHÉ (tessellation conservée)** | `.backups/04_before_P2_surface_deferred/` (avant) · `.backups/05_P2_surface_deferred_validated/` (après) · **`.backups/07_P2_gates_validated/` (gates + budget, 2026-07-05)** | Surface opaque deferred (GBuffer) + tessellation gatée distance (~200k tris) + passe MotionVectors native (tampon T-1) + instrumentation ProfilerRecorder GPU des 3 postes budget. P1 byte-à-byte intact. **Budget BUILD RTX 2060 (Ultra, D3D12, dev build = borne haute) : (a) GBuffer 0.169 + (b) Ocean.Spectrum 1.252 + (c) Ocean.MotionVector 1.143 = SOMME 2.564 ms ∈ [2–4 ms]** → tessellation conservée, pivot clipmap Q3.4 non déclenché, marge 1.436 ms pour P3/P5/P6. ⚠ poste (c) copie T-1 = candidat n°1 d'optimisation (différé). Détail : `OCEAN_TEST_P2.md` §(l) |
| **P3** | ✅ **VALIDÉE UTILISATEUR (rendu couleur) — 2026-07-17 — 16/16** | `.backups/08_before_P3_absorption/` (avant) · `.backups/08b_before_P3_k4_fix/` · `.backups/08c_before_P3_color_fix/` · **`.backups/09_P3_absorption_validated/` ✅ (81 fichiers + MANIFEST + SHA-256)** | Module `absorption` réel (Q6.1/Q6.2) : Beer-Lambert spectral, 3 ancres Jerlov, master `waterType` par segments, **`colorBuildup`** [0.1..50] (ex-`perceivedDepth`, renommé 2026-07-17 + tooltip corrigé), push SET pur de l'UNIQUE `_WaterAbsorption` (+`_OceanAbsorptionDepth`) ; surface : albédo = **réflectance montante `b_b/σ·(1−e^(−2σ_norm·kDepthRate·d))·kUpwellingScale`**, `kUpwellingScale=0.02` + `kDepthRate=0.04` = **constantes shader statiques** (exposition en paramètre profil envisagée puis RETIRÉE), interrupteur `_OceanAbsorptionEnabled` (branche uniforme, 0 variant), repli `_BaseColor`. Ancres : **Ia (0.36, 0.07, 0.028)** (σ_G 0.041→0.07) · II (0.45, 0.09, 0.15) · III (0.55, 0.20, 1.10). Défauts profil : `waterType=0`, `colorBuildup=15`. Anti-bug n°1 intact. ⚠ Calibrage qualitatif ; « bleu profond » final dépendra de P5 (réflexions) + P6 (sous-marin). Protocole : `OCEAN_TEST_P3.md`. |
| **P4** | ✅ **VALIDÉE UTILISATEUR (gate visuel) — 2026-07-17 — 19/19** | `.backups/10_before_P4_foam/` (avant) · **`.backups/11_P4_foam_validated/` ✅ (87 fichiers + MANIFEST + SHA-256, créé/vérifié 2026-07-18)** (après) | Écume = **carte world-locked** (couverture + persistance Q7.3 en 1 RT ping-pong RHalf mippée, résolution découplée de la tuile), **Q7.1 AMENDÉE** (J_total = 1+Σ(Jxx+Jzz) filtré à 2 m, erf σ fixe — voir `OCEAN_DECISIONS.md §Amendements A1`, mesures dans `OCEAN_TEST_P4.md`). Consommation surface : position NON-déplacée (q≈p−D(p)), LOD par distance, rupture procédurale 2 échelles. Params : `jacobianThreshold` (0.7≈4 % à mer formée), `foamFadeRate`, `foamResolution`. **+2 correctifs transverses** : signe déplacement choppy P1 (silhouette crêtes/creux, corr(J,h) +0.99→−0.99) ; contrat P1 `.w` = s=Jxx+Jzz (ex-déterminant). 7 bugs corrigés en chronique (`OCEAN_TEST_P4.md §Bugs`). Marker `Ocean.Foam` (budget agrégé `surface`, re-contrôle à T1). |
| **P5** | ✅ **VALIDÉE UTILISATEUR (gate visuel) — 2026-07-18 — 19/19** | `.backups/12_before_P5_reflection/` (avant) · `.backups/13_P5_reflection_validated/` (après) | Module `reflection` (Q5.1) : **ciel** réfléchi automatiquement par HDRP (surface Lit deferred) ; **Planar Reflection Probe HDRP built-in** au niveau d'eau (realtime, `influenceExtent`), **dé-risque Q3.4 LEVÉ** (compat surface custom, Fresnel-correct) ; **gating immergé** (§1.3) prouvé par données (planar OFF sous l'eau) ; **SSR** testé (critère i) = aucun artefact, reste OFF en V1 (Q5.1). Ciel monté+conservé (`Ocean Debug Sky.volumeprofile`, PBS+EV14). **asmdef océan : ajout réf HDRP+Core**. **T1 = PROVISOIRE** (sous-marin V1.5 ; mesure reflection non représentative sur scène vide → différée scène réelle/post-P6). AA (Q5.3)=constaté, système P10. Détail : `OCEAN_TEST_P5.md`. |
| **P6** | 🚧 **EN COURS** — G1+G2 validés (2026-07-18), G3 cadré | `.backups/14_before_P6_underwater/` (avant) · **`.backups/14b_P6_G1G2_checkpoint/`** (checkpoint mi-phase) · `15` = fin de phase (à venir) | Sous-marin (V1.5). **G1** double-sided (`OceanSurface.shader` Cull Off) ✅ · **G2** absorption immergée (CustomPass `BeforePostProcess` `OceanUnderwater.shader`, σ partagé Q6.1, `OceanUnderwaterModule` réel) ✅. Reste : **G3 Snell** (cadré — surface opaque → refraction/TIR reconstruites dans le CustomPass, 2 approches, cf. `OCEAN_TEST_P6.md`), G4 fog/god-rays (volumetrics HDRP), G5 éclairage non destructif + **re-valid T1**. Protocole + cadrage G3 : `OCEAN_TEST_P6.md`. |
| P7–P10 | 🗓 Planifié | slots **ACTÉS** : P7=`16`/`17` … P10=`22`/`23` (cascade `(2n+2)/(2n+3)`, ROADMAP §1.1(b)) | Voir OCEAN_ROADMAP.md §3 |

---

## P0 — Scaffolding (Framework du projet) — ✅ LIVRÉ & CORRIGÉ

**Livraison :** 2026-06-29
**Snapshot de fin :** `.backups/01_after_P0_scaffolding/MANIFEST.md`

### Livrables

#### Cœur du framework
- ✅ `OceanProfile.cs` — ScriptableObject central (pattern TerrainProfile)
- ✅ `OceanFeatureModule.cs` — Base abstraite (Active, DisplayName, Keyword, OnModuleEnable/Disable, Apply, Tick, **WantsContinuousRepaint**)
- ✅ `OceanSystem.cs` — MonoBehaviour `[ExecuteAlways]` (Setup/Update/Teardown, **NeedsContinuousRepaint()** helper)
- ✅ `OceanApplyContext.cs` — Contexte Apply/Tick (matériau, profil, système, temps, cache globaux)
- ✅ `OceanGlobalCache.cs` — **Push global NON CUMULATIF/restaurable** (anti-bug #1) : assignation pure, caching, `RestoreAll()` Teardown
- ✅ Harnais d'instrumentation : `OceanProfiler.cs` (ProfilerMarker/CommandBuffer squelette), `OceanMotionVectorPass.cs` (stub MotionVector custom)
- ✅ Infrastructure : `OceanModuleMenuAttribute.cs`, éditeur `OceanProfileEditor.cs`

#### 7 modules-stubs (Q12.4)
- ✅ `OceanSpectrumModule.cs` — stub
- ✅ `OceanSurfaceModule.cs` — stub
- ✅ `OceanUnderwaterModule.cs` — stub
- ✅ `OceanReflectionModule.cs` — stub
- ✅ `OceanAbsorptionModule.cs` — stub
- ✅ `OceanShoreModule.cs` — stub (dormant V1)
- ✅ `OceanWakeModule.cs` — stub

### Critères de Validation Atteints

- ✅ **Compilation** : sans erreur (correction #3 syntaxiquement cohérente)
- ✅ **Instanciation** : `OceanProfile` créable en éditeur (`Create > Ombrage/Ocean/Ocean Profile`)
- ✅ **Inspecteur** : 7 modules listés, ajoutables/supprimables via `OceanProfileEditor`
- ✅ **Composant** : `OceanSystem [ExecuteAlways]` s'attache sans exception (`Add Component > Ombrage/Ocean/Ocean System`)
- ✅ **Cycle** : Setup/Teardown/ApplyAll/Tick fonctionnels, aucune exception runtime
- ✅ **Repaint éditeur** : conditionné correctement (correction #3 appliquée), stubs inertes = aucun repaint forcé

### Correctifs Post-Revue (Réviseur P0)

#### Problème #3 (CORRIGÉ) — Repaint SceneView inconditionnel
**Statut :** ✅ **CORRIGÉ**

**Description :** `SceneView.RepaintAll()` était appelé chaque frame en mode édition, forçant un rafraîchissement continu même quand les modules étaient inactifs (stubs). Coût : rechargement de la SceneView à 60 Hz hors Play.

**Solution appliquée :**
- Ajout de propriété virtuelle `OceanFeatureModule.WantsContinuousRepaint` (défaut `false`)
- Ajout de helper `OceanSystem.NeedsContinuousRepaint()` (éditeur seulement, blocs `#if UNITY_EDITOR`)
- Modification `Update()` : `SceneView.RepaintAll()` conditionné via le helper
- Résultat : SceneView ne rafraîchit que si ≥ 1 module ACTIF signale `WantsContinuousRepaint=true`
- En P0 (stubs inertes) : aucun module ne le demande → SceneView au repos, zéro repaint forcé

**Fichiers affectés :**
- `OceanFeatureModule.cs` (ligne 35) : propriété ajoutée
- `OceanSystem.cs` (lignes 115–129) : Update + helper ajoutés

**Vérification :** ✅ Correction syntaxiquement cohérente, blocs compilateurs fermés proprement, pas de régression

#### Problème #2 (DIFFÉRÉ) — Smoke test EditMode
**Statut :** 🗓 **REPORTÉ à P1+**

**Description :** Un test EditMode automatisé (Unity Test Framework) exige un asmdef de test, lequel ne peut référencer Assembly-CSharp (où vit le code Ocean_v2 par décision verrouillée). L'automatiser imposerait un asmdef de production = hors-scope P0 et contraire à la décision « pas d'asmdef ».

**Décision :** Différé à P1+ quand de la logique testable existera (spectre, surface) ; l'introduction d'un asmdef + tests y sera plus naturelle.

**Raison du report :** P0 est pur scaffolding (stubs no-op) = peu d'intérêt à automatiser ; P1 aura des modules avec logique réelle à tester.

#### Problème #1 (GATE UTILISATEUR) — Compilation éditeur
**Statut :** 👤 **EXÉCUTION UTILISATEUR** (non exécutable hors Unity)

**Description :** La validation de compilation ne peut être exécutée hors Unity (UnityLockfile présent). Impossible d'automatiser avec un simple appel batch.

**Solution :** Gate utilisateur — l'utilisateur valide dans l'éditeur Unity avant de passer à P1.

**Protocole de validation (pour l'utilisateur) :**
1. Laisser recompiler l'éditeur Unity (Ctrl+R ou rechargement domaine).
2. Console Unity doit être VIDE (0 erreur, 0 warning bloquant).
3. Créer un `OceanProfile` : menu `Create > Ombrage/Ocean/Ocean Profile` → asset créé sans erreur.
4. Inspecter l'asset nouvellement créé → bouton « Add Module » présent et fonctionnel.
5. Cliquer « Add Module » ; les 7 stubs doivent s'afficher comme options et s'ajouter en sous-assets.
6. Attacher `OceanSystem` à un GameObject : menu `Add Component > Ombrage/Ocean/Ocean System` → s'attache sans exception.
7. Le composant doit exposer les champs (profile, surfaceMaterial) et doit être activable/désactivable sans crash.

**Si l'une de ces étapes échoue :** problème #1 = NON validé, rollback à `.backups/00_before_P0_scaffolding/` et diagnostic du compilateur.

### Invariants Vérifiés (3 bugs interdits)

1. ✅ **Bug #1 (soleil cumulatif non restauré)** — Structurellement empêché par `OceanGlobalCache` (assignation pure, jamais `*=`, + `RestoreAll()` Teardown). Le harnais est mis en place en P0 ; sera exercé en P3 (absorption) quand des globaux seront réellement poussés.

2. ✅ **Bug #2 (H₀ réinit complète par frame si windPulse actif)** — Sans objet en P0 (pas de spectre/simulation). Prérequis P1 : recalcul de H₀ **uniquement sur changement réel de paramètre**, pas chaque frame.

3. ✅ **Bug #3 (normalisation IFFT couplée à l'amplitude)** — Sans objet en P0 (pas d'IFFT). Prérequis P1 : normalisation `1/N` **strictement découplée de l'amplitude**.

### Snapshot de Fin

**Localisation :** `.backups/01_after_P0_scaffolding/`

**Contenu :**
- Arborescence `Profile/` complète (10 fichiers C# + Editor + Modules)
- `MANIFEST.md` annoté avec les correctifs post-revue et l'état des 3 bugs

**Invariant :** `snapshot/Profile/** == production/Profile/**` (résynchronisé après correction #3)

---

## P1 — Spectre (Simulation FFT) — ✅ LIVRÉ — EN ATTENTE VALIDATION

**Livraison :** 2026-06-29
**Snapshots :** `.backups/02_before_P1_spectre/` (avant) · `.backups/03_P1_spectre_validated/` (après)

### Livrables

#### Shaders (nouveau dossier `Shaders/`)
- ✅ `OceanFFTCommon.hlsl` — arithmétique complexe, symétrie hermitienne, **packing 2-en-1** (`OceanPack2`), indexation fréquentielle DC-au-coin, dispersion deep-water + TMA.
- ✅ `OceanFFT.compute` — IFFT/FFT 2D **Stockham auto-sort** (réécrit from-scratch) : butterfly paramétré (stage/axe/sens), passe de scale `_NormScale` **découplée de l'amplitude**.
- ✅ `OceanSpectrum.compute` — H0 (JONSWAP + TMA dormant), évolution temporelle hermitienne, **dérivées analytiques** (déplacement/pentes/Jacobien), 4 paquets hermitiens (P1.b), kernels P1.a (hermitien + naïf), assemblage vers arrays.
- ✅ `OceanSpectrumDebug.shader` — visualisation des slices (hauteur / déplacement / normale / Jacobien) sur quad, pattern HDRP ForwardOnly.

#### C#
- ✅ `OceanSpectrumModule.cs` — module data-driven : 4 cascades golden-ratio, résolution mixte (Ultra 2×512²+2×256² / High 1×512²+3×256² / Low tout-256²), état runtime via `SetRuntime`, push globaux via `ctx.globals`, mesure go/no-go P1.a, recalcul H0 **uniquement sur changement réel** de paramètre.

### P1.a — Dé-risque IFFT hermitienne 2-en-1 (PERF) — go/no-go
- **Mesure** : ratio de passes IFFT **hermitien vs naïf** sur les 2 mêmes signaux (height + choppiness_x).
  - Hermitien = `OceanPack2(h, Dx)` → **1 IFFT complexe**.
  - Naïf = 2 IFFT séparées.
  - Ratio attendu ≈ **0.50** (gain 50 %), loggé en Console : `[Ocean P1.a] … ratio=0.50`.
- **Critère = constat documenté** (PAS un seuil ms : le budget ms RTX 2060 est renvoyé au proto P2, Q11.2/Q3.4).
- **Repli sanctionné** : si l'hermitien s'avérait rédhibitoire, repli automatique sur IFFT séparées (cœur prouvé), documenté au manifeste — sans arrêt mi-parcours (P1.a/P1.b = jalon unique).

### P1.b — Simulation complète
- ✅ **Vrai JONSWAP** : `S(ω)=αg²/ω⁵·exp(−1.25(ωp/ω)⁴)·γ^r`, α/ωp dérivés du vent+fetch, γ≈3.3, σ=0.07/0.09.
- ✅ **Branche TMA dormante** : `×Φ(ωh)` Kitaigorodskii + dispersion `tanh(kh)`, gatée par `useTMA` (deep-water 191 m → Φ→1, OFF en V1).
- ✅ **Dérivées analytiques** (anti-bug #2) : déplacement `-i·k̂·h`, pentes `i·k·h`, Jacobien `k⊗k/|k|·h` — **aucune différence finie**.
- ✅ **Normalisation découplée** (anti-bug #3) : `_NormScale` constante mathématique isolée ; les coefficients portent Δk=2π/L (indépendant de N) → changer N ne change PAS la hauteur.
- ✅ **4 cascades golden-ratio** (longueurs `L₀/φⁱ`) + **résolution mixte** + arrays **groupés par résolution** (512² / 256² homogènes — contrainte slices uniformes).
- ✅ **Packing 2 textures** (déplacement xyz+J / dérivées) + **emballage hermitien généralisé** (4 paquets = 8 champs réels).

### Anti-bug — invariants respectés
1. ✅ **Bug #1** : tous les globaux (`_OceanDisp*`, `_OceanDeriv*`, `_OceanCascade*`) poussés via `ctx.globals` (assignation pure), restaurés par `OceanSystem.Teardown → RestoreAll()`.
2. ✅ **Bug #2** : dérivées/normales/Jacobien **analytiques en domaine spectral** (`OceanBuildSpectra`).
3. ✅ **Bug #3** : normalisation `1/N` traitée comme `_NormScale` (1.0 en sim), **jamais** liée à `_Amplitude` ; + recalcul H0 conditionné par `ComputeParamHash` (ni le temps ni la choppiness n'y entrent → pas de réinit par frame, anti-piège windPulse).

---

## P2 — Surface deferred (GBuffer) — ✅ VALIDÉE (gates 0-bis + 1–4 VERTS — budget verrouillé 2.564 ms build Ultra RTX 2060 — T2 : tessellation conservée)

**Livraison (code) :** 2026-06-30
**Snapshots :** `.backups/04_before_P2_surface_deferred/` (avant, état P1 intact) · `.backups/05_P2_surface_deferred_validated/` (après)
**Définition de « P2 terminée » ici :** code + instrumentation + protocole **ÉCRITS et revus**. La mesure
GBuffer réelle sur RTX 2060 et le **verrouillage de la table budget** restent un **gate utilisateur** (cases
« budget mesuré » laissées vides dans les manifestes).
**→ Gate utilisateur REMPLI le 2026-07-05 (j) : budget mesuré et verrouillé (2.564 ms build Ultra), T2 tranché —
voir §« Relevé budget BUILD + clôture du gate 4 » ci-dessous et `OCEAN_TEST_P2.md` §(l).**

### Correctif régression compilation shader — 2026-07-05 (c) — `TESSELLATION_INTERPOLATE_BARY` manquant
**Symptôme (remonté par l'utilisateur au gate P2 1–3, d3d11) :**
`Shader error in 'Custom/HDRP/OceanSurface': undeclared identifier 'positionRWS' at .../ShaderPass/VaryingMesh.hlsl(322)`,
`Pass: ShadowCaster, Vertex program`.
**Cause racine :** `OceanSurface.shader` est un shader **tessellé** mais son `HLSLINCLUDE` n'incluait
PAS le core `Tessellation.hlsl`, qui définit la macro `TESSELLATION_INTERPOLATE_BARY`. Cette macro est
utilisée par `InterpolateWithBaryCoordsMeshToDS` (`VaryingMesh.hlsl:322`), fonction du **domain** compilée
dans **toutes** les passes géométriques. Macro non déployée → `positionRWS` reste un token nu → identifiant
non déclaré. Le variant **ShadowCaster** est simplement le 1ᵉʳ variant tessellé effectivement compilé par le
renderer ; le défaut touchait à l'identique GBuffer/DepthOnly/MotionVectors/Picking/SceneSelection.
**Pourquoi non capté avant :** gate 0-bis (reimport + domain reload + EditMode 12/12) ne **rend** aucune
scène → aucun variant tessellé compilé ; la revue statique n'avait pas relevé l'include manquant. Le défaut
ne surface qu'au **rendu réel** (gate P2). Présent dès le snapshot 05 (vérifié).
**Fix (chirurgical, calqué sur `LitTessellation.shader` HDRP 17.4 L342-345) :** ajout dans le `HLSLINCLUDE`,
entre `Common.hlsl` et `ShaderVariables.hlsl`, de `GeometricTools.hlsl` PUIS `Tessellation.hlsl` (core). Le
premier est requis car `Tessellation.hlsl` référence `ProjectPointOnPlane`/`ComputeNormalizedDeviceCoordinates`.
Include guards ⇒ la ré-inclusion transitive ultérieure (via `Material.hlsl`) est inoffensive.
**Conséquence process :** changement de **code shader** → **gate 0-bis à re-passer** (recompile + Console 0/0 +
EditMode 12/12) AVANT toute reprise des gates P1/P2. Aucun autre fichier touché ; instrumentation `OceanProfiler.cs`
(2026-07-05 b) inchangée. Fait partie du code à sceller au snapshot 07 (avec l'instrumentation).

### Correctif visibilité océan en BUILD — 2026-07-05 (f) — compute shaders non sérialisés dans le profil de gate
**Symptôme (remonté par l'utilisateur, réponse au gate 4) :** « l'océan n'est pas visible en build » → poste
(a)_build (GBuffer total ≈ surface) non mesurable, **gate 4 bloqué en amont**, verdict T2 et snapshot 07
inatteignables.
**Diagnostic (revue de code, l'agent ne peut lancer Unity) :** le shader `OceanSurface` est bien inclus au
build (présent dans *Always Included Shaders*, GUID `0fef2101cc76b6b46a9b7c2e8fc896e3` — vérifié dans
`ProjectSettings/GraphicsSettings.asset`) : l'invisibilité **ne vient PAS d'un stripping du shader de surface**.
**Cause racine :** `OceanP2GateProfileBuilder.BuildProfile` créait l'`OceanSpectrumModule` **sans assigner
`fftShader`/`spectrumShader`**. Le repli `AssetDatabase.LoadAssetAtPath` de `OceanSpectrumModule.ResolveShaders`
(L200-206) est sous `#if UNITY_EDITOR` et n'écrit les champs sérialisés qu'en éditeur ; or le profil de gate est
**sauvegardé** (SetDirty+SaveAssets) alors que ces champs sont encore **nuls**. En **build** : (1) les `.compute`
qu'aucun asset buildé ne référence sont **strippés** ; (2) `ResolveShaders` laisse `rt.fft`/`rt.spectrum` nuls →
`OnModuleEnable` (L141-146) logue « [Ocean P1] Compute shaders FFT/Spectrum introuvables — module spectre
inactif » et sort → **aucun `_OceanDisp*` poussé** → surface **non déplacée / invisible**. Le profil racine
`OceanProfile.asset` (scène « Ocean Debug ») a, lui, les refs sérialisées (write-back éditeur passé), d'où le
fonctionnement correct EN ÉDITEUR qui masquait le défaut.
**Fix (chirurgical, `OceanP2GateProfileBuilder.cs` — outillage éditeur, hors code de rendu) :** charge les 2
compute via `AssetDatabase.LoadAssetAtPath` et **les assigne** à `spectrum.fftShader`/`spectrum.spectrumShader`
AVANT `SaveAssets` (abort BRUYANT + suppression de l'asset incomplet si l'un manque) ; l'assertion finale exige
`computeSérialisés=true`. Les refs étant désormais **sérialisées dans le profil de gate**, les `.compute`
survivent au build et se résolvent **non nuls** dans le player.
**Conséquence process :** avant le prochain build, **RE-CONSTRUIRE la scène de gate** (*Ombrage/Ocean/Build P2
Gate Scene*, qui régénère le profil) — sinon l'ancien profil (refs nulles) persiste. Aucun code de rendu ni test
EditMode touché (modif dans un `.cs` d'Editor de banc). **Vérification humaine requise** (l'agent ne peut lancer
Unity) : après re-build, l'océan est VISIBLE dans le player ET le Player.log ne contient plus « module spectre
inactif ». Tant que non confirmé, **gate 4 reste OUVERT** — pas de valeur budget, pas de snapshot 07. Si l'océan
reste invisible après ce correctif, suspecter un stripping de **variant** de la surface (matériau runtime non
scanné par le stripper HDRP), cf. `OCEAN_TEST_P2.md` §(i-build) 0.b.

### Durcissement de l'assertion du builder de gate — 2026-07-05 (g) — round-trip disque des refs compute
**Origine :** verdict Réviseur (problème mineur #3) — l'assertion finale du correctif (f) contrôlait
`spectrum.fftShader != null` sur l'objet EN MÉMOIRE, ce qui ne détecte PAS un échec de **sérialisation** (les refs
vivent en RAM même si `SaveAssets` ne les persiste pas) ; or c'est justement la sérialisation des `.compute` DANS
le profil qui conditionne la visibilité en build.
**Fix (`OceanP2GateProfileBuilder.cs`, outillage éditeur) :** après `SaveAssets`+`ImportAsset`, l'asset et son
sous-module `OceanSpectrumModule` sont **rechargés depuis le disque** (`LoadAssetAtPath` + `LoadAllAssetsAtPath`)
et l'assertion exige désormais `computeMémoire=true` **ET** `computeDisque=true` (refs sérialisées non nulles). Log
vert : « compute FFT/Spectrum **SÉRIALISÉS ET RE-VÉRIFIÉS SUR DISQUE** (build-safe) » ; log rouge détaillant
`computeMémoire`/`computeDisque` et si le sous-module a été rechargé. **Chaîne scène→profil→compute :** confirmé que
`OceanP2GateSceneBuilder.BuildScene` appelle `BuildProfile()` puis assigne le profil régénéré à `OceanSystem.profile`
(scène et profil reconstruits d'un bloc) → pas de risque d'ancien profil orphelin quand on passe par *Build P2 Gate
Scene*. Aucun code de rendu ni test EditMode touché. **Le bloquant du verdict reste entier** : l'efficacité en BUILD
(visibilité + `.compute` non strippés) n'est PAS prouvable par revue de code — elle exige que l'utilisateur déroule
le `protocole_test` (Development Build → océan visible + Player.log sans « module spectre inactif »). Tant que non
confirmé : **gate 4 OUVERT, pas de verdict T2, pas de snapshot 07**.

### Durcissement visibilité surface en BUILD — 2026-07-05 (h) — matériau de surface sérialisé dans le profil de gate
**Origine :** verdict Réviseur (problème **bloquant #2**) — le correctif (f) sérialise les `.compute` (chemin du
**déplacement**), mais la **visibilité de la SURFACE** restait tributaire de `OceanSurfaceModule.EnsureMaterial`
(L245-261) qui, si `surfaceMaterialOverride` est nul, résout le matériau en runtime via
`Shader.Find("Custom/HDRP/OceanSurface")` (repli `AssetDatabase` **sous `#if UNITY_EDITOR`**). En build, même avec
le shader forcé dans les *Always Included Shaders* (inclusion du shader **entier**, GUID `0fef2101…` — vérifié), (1)
`Shader.Find` dans le player reste un chemin fragile et (2) la reachability des **variants** de rendu effectivement
échantillonnés dépend du stripper HDRP — précisément le « stripping de variant surface » que le Réviseur pointait
comme risque résiduel. Le profil de gate ne sérialisait **aucune** référence matériau/shader de surface → rien ne
tirait déterministiquement le matériau (et ses variants) dans le build.
**Fix (chirurgical, `OceanP2GateProfileBuilder.cs` — outillage éditeur, hors code de rendu) :** charge
`OceanSurface.shader` via `AssetDatabase.LoadAssetAtPath<Shader>` (abort BRUYANT + suppression de l'asset incomplet
s'il manque), **fabrique un matériau d'asset** `Assets/Shader/Ocean_v2/Tests/OceanP2GateSurface.mat` à partir de ce
shader, et l'**assigne à `surface.surfaceMaterialOverride`** AVANT `SaveAssets`. Le matériau étant un **asset
référencé par le profil de gate** (donc par le build via la scène), le shader **ET les variants que le matériau
utilise** sont inclus déterministiquement ; `EnsureMaterial` emploie le matériau directement (`surfaceMaterialOverride
!= null` → **aucun `Shader.Find` en build**). Les propriétés (couleur, tessellation…) restent poussées chaque frame
par `PushMaterialProps` → un matériau nu issu du shader suffit. L'assertion finale du builder est étendue au
**round-trip disque** du matériau (comme (g) pour les compute) : recharge `OceanSurfaceModule` + le `.mat` depuis le
disque et exige `reloadedSurface.surfaceMaterialOverride != null` **ET** `reloadedMat.shader != null` ; le log vert
mentionne désormais « compute FFT/Spectrum **+ matériau de surface** SÉRIALISÉS ET RE-VÉRIFIÉS SUR DISQUE (build-safe) ».
**Conséquence process :** avant le prochain build, **RE-CONSTRUIRE la scène de gate** (*Ombrage/Ocean/Build P2 Gate
Scene* → régénère profil + matériau) — sinon l'ancien profil (sans matériau) persiste. Aucun code de rendu ni test
EditMode touché (modif dans un `.cs` d'Editor de banc). **Vérification humaine requise** (l'agent ne peut lancer
Unity) : après re-build, l'océan est VISIBLE dans le player. **Le bloquant du verdict reste entier** : l'efficacité
BUILD n'est pas prouvable par revue de code. Tant que non confirmé : **gate 4 OUVERT, pas de verdict T2, pas de
snapshot 07**.

### Relevé budget BUILD + clôture du gate 4 + verdict T2 — 2026-07-05 (j) — 🏁 P2 INTÉGRALEMENT VALIDÉE
**Relevé utilisateur (module GPU du Profiler, capture build dev, preset Ultra forcé par le profil de gate —
`kCascadeQuality = Ultra` —, API D3D12, machine du protocole RTX 2060) :** (a) `GBuffer` = **0.169 ms** ·
(b) `Ocean.Spectrum` (englobant) = **1.252 ms** · (c) `Ocean.MotionVector` = **1.143 ms** → **SOMME = 2.564 ms
∈ [2–4 ms]** (plafond dur 4 ms). Poste (a) ≪ part cible ~1.00 ms Ultra (ROADMAP §2.2).
**Verdict (application stricte d'`OCEAN_TEST_P2.md` §(j)) : CONSERVATION de la tessellation (~200k tris) ;
pivot clipmap Q3.4 NON déclenché ; aucune passe d'optimisation requise à P2 ; BUDGET OCÉAN P2 VERROUILLÉ.**
Marge résiduelle sous plafond = **1.436 ms** pour les postes à venir (P3/P5/P6) ; la somme P2 ne couvre pas
les passes HDRP depth-prepass/MV-draw de la surface (marqueur (c) = copie T-1 seule) → **recontrôle
total-frame à T1 (P5/P6)**, conforme §(i-build) 6.
**Consignes :** dev build instrumenté = **borne HAUTE** ; nom réel du poste (a) en capture = **«GBuffer»**
(couvert par `kGBufferMarkerCandidates`) ; corroboration CPU saine (~0.5 ms cumulés tous threads, dispatchs
bien batchés). ⚠ **Poste (c) = 45 % de la somme pour une copie T-1 (~21 Mo RGBAFloat ≈ 0.06 ms théoriques
sur RTX 2060)** → surcoût dominé par transitions de layout/synchronisations D3D12, pas par la bande passante ;
**candidat n°1 d'optimisation, DIFFÉRÉ** (pistes : ping-pong de binding sans copie — demanderait une retouche
du contrat P1, à cadrer —, copie des seules slices échantillonnées par la passe MV, format réduit du tampon
prev) : le budget tient, on ne touche pas au code validé.
**Snapshot `07_P2_gates_validated/` CRÉÉ** (MANIFEST + SHA-256 + vérification de présence disque réelle —
leçon des backups herbe fantômes). **Prochaine étape : setup P3 — Absorption** (y acter la renumérotation
des slots P3→P10, ROADMAP §1.1(b)). Aucun code/shader/test modifié ce run (clôture documentaire + snapshot).

### Build fonctionnel + océan VISIBLE + capture Profiler fournie — 2026-07-05 (i) — pré-conditions 0.a/0.b du gate 4 LEVÉES
**Retour utilisateur :** « le build fonctionne maintenant et l'océan est bien visible » + capture Profiler dans
`C:\Users\Arthe\Ombrage\ProfilerCaptures\Ombrage_2026-07-05_13-15-17.{data,png,highlights}`.
**Ce que cela prouve (revue par l'agent, qui ne peut lancer Unity) :**
- **Compilation BUILD OK (pré-condition 0.a ✅)** : aucune nouvelle erreur shader rapportée après ajout d'`OceanSurface`
  aux *Always Included Shaders* → les correctifs (c) `positionRWS`/`Tessellation.hlsl` et (e) `_ObjectId`/`_SelectionID`
  compilent **toutes** les passes en build.
- **Océan VISIBLE (pré-condition 0.b ✅)** : le **PNG** de la capture montre une surface océan rendue et déplacée →
  les correctifs (f) compute sérialisés, (g) round-trip disque et (h) matériau de surface sérialisé sont **effectifs
  en build** (le module spectre s'active, `_OceanDisp*` poussé, surface tirée).
- **Instrumentation GPU active en build** : un scan du `.data` confirme la présence des markers `Ocean.Spectrum`,
  `Ocean.FFT`, `Ocean.Surface`, `Ocean.MotionVector` **et** `RenderGBuffer` → les 5 markers sont **enregistrés dans le
  player**, et le **backend graphique du build supporte le GpuRecorder** (pré-condition edge-case D3D11 franchie de fait ;
  API graphique du build à consigner formellement au relevé).
**Ce qui reste OUVERT (bloquant résiduel du gate 4) :** la **lecture numérique** des 3 postes budget. Le `.data`
(213 Mo, format binaire Profiler non documenté) **n'est pas décodable par l'agent** pour extraire le coût GPU par
marker. Les valeurs **(a) `RenderGBuffer` / (b) `Ocean.Spectrum` (englobant) / (c) `Ocean.MotionVector` + la SOMME**
doivent être **relevées par l'utilisateur** dans **Window ▸ Analysis ▸ Profiler → module GPU** (capture ouverte, ou
Development Build reconnecté), preset **Ultra**, sur une frame animée, chacun **non nul** (cf. `OCEAN_TEST_P2.md`
§(i-build) 4 et §(j)). **Aucune valeur inventée** : tant que ces 3 ms + somme ne sont pas fournis, **gate 4 reste
OUVERT, verdict T2 non tranché, snapshot 07 NON créé.** Aucun code/shader/test modifié ce run (instrumentation (b) +
correctifs (c)/(e)/(f)/(g)/(h) déjà en place, désormais **confirmés fonctionnels en build**).

> **✅ RÉSOLU (2026-07-05 j)** : les 3 postes + SOMME relevés par l'utilisateur, non nuls → **gate 4 CLOS,
> T2 tranché (tessellation conservée)** — voir §« Relevé budget BUILD + clôture du gate 4 » ci-dessus.

### Correctif compilation shader en BUILD — 2026-07-05 (e) — passes SceneSelection/ScenePicking (`_ObjectId`/`_SelectionID`)
**Symptôme (remonté par l'utilisateur, BUILD d3d11, après ajout d'`OceanSurface` aux *Always Included Shaders*) :**
`undeclared identifier '_ObjectId'` (`.../ShaderPass/ShaderPassDepthOnly.hlsl(112)`, `Pass: SceneSelectionPass`) et
`undeclared identifier '_SelectionID'` (`:114`, `Pass: ScenePickingPass`).
**Cause racine :** `ShaderPassDepthOnly.hlsl` référence, dans ses branches éditeur, `_ObjectId`/`_PassValue`
(sortie de sélection, L112) et `unity_SelectionID` (sortie de picking, L114). Or `unity_SelectionID` est une
**macro** de `ShaderVariables.hlsl` (que le shader inclut) qui se déploie en `_SelectionID`. Ces 3 variables
sont alimentées par l'éditeur C++ et, dans un Lit standard, déclarées par `LitProperties.hlsl`. La surface océan
**remplace `LitData.hlsl` par `OceanSurfaceData.hlsl`** et n'inclut donc **jamais** `LitProperties.hlsl` →
les 3 identifiants restent non déclarés. Les passes `SceneSelectionPass`/`ScenePickingPass` ne sont normalement
**pas compilées** en build (LightMode éditeur, strippés) ; les ajouter aux *Always Included Shaders* **force la
compilation de TOUTES les passes** → le défaut, dormant, surface.
**Pourquoi non capté avant :** l'éditeur ne compile ces 2 passes qu'au clic Scene View / sélection ; gate 0-bis
(reimport + domain reload + EditMode 12/12) ne les déclenche pas. Même classe de défaut que `positionRWS` (c) :
invisible tant que la passe n'est pas réellement compilée. Présent dès le snapshot 05.
**Fix (chirurgical, calqué sur HDRP/Lit `LitProperties.hlsl` L294-298) :** déclaration, dans le `HLSLINCLUDE`
juste après `CBUFFER_END(UnityPerMaterial)`, de `int _ObjectId; int _PassValue; float4 _SelectionID;` — **HORS**
du cbuffer `UnityPerMaterial` (exigence SRP Batcher, exactement comme LitProperties). Déclarées globalement (comme
LitProperties, inclus par toutes les passes Lit) : inertes dans les passes non-sélection (GBuffer/Depth/MV/Shadow),
aucun changement de rendu.
**Conséquence process :** changement de **code shader** → au-delà du gate 0-bis éditeur, une **re-vérification de
compilation BUILD** (rebuild avec `OceanSurface` en *Always Included Shaders*) est requise pour attester la
disparition des 2 erreurs. Aucun autre fichier touché ; instrumentation `OceanProfiler.cs` (b) et correctif (c)
inchangés. À sceller au snapshot 07 avec le reste. **Distinct** du blocage amont du gate 4 « l'océan n'est pas
visible en build » (problème de rendu/simulation, non de compilation) — **diagnostiqué et corrigé au correctif
(f) ci-dessus** (compute shaders non sérialisés dans le profil de gate).

### Alignement documentaire du chemin de mesure — 2026-07-05 (d) — `showPerfReadout` honnêtement décrit
**Constat :** `OceanProfiler.Readout()` et le tooltip de `OceanSystem.showPerfReadout` prétendaient un
« readout inspecteur » des coûts GPU, alors qu'**aucun affichage n'est câblé** (ni `CustomEditor`, ni
`OnGUI` ; `Readout()` n'est appelé nulle part — vérifié). `OCEAN_TEST_P2.md` §(i-build) avait déjà acté
le bon chemin (fenêtre **Profiler GPU** sur **Development Build**, seule autorité T2) et écarté
`showPerfReadout` ; le **code** restait en retard et affirmait le contraire.
**Fix (chirurgical, commentaires/tooltip SEULS — zéro changement de comportement) :** commentaire de
`Readout()` et tooltip/commentaire de `showPerfReadout` reformulés → helper NON câblé, réservé à un futur
overlay, la mesure BUILD passe par la fenêtre Profiler GPU (renvoi §(i-build)). Le booléen public est
**conservé** (non supprimé) pour ne pas casser la sérialisation des scènes/prefabs de gate. Décision de ne
PAS ajouter de HUD `OnGUI` : contredirait la décision déjà documentée dans `OCEAN_TEST_P2.md` (« pas de HUD
in-build ») et sortirait du périmètre.
**Conséquence process :** deux `.cs` de production touchés (commentaires seuls) → **gate 0-bis** déjà rouvert
ce run les recouvre (aucun test EditMode affecté). À sceller au snapshot 07 avec le reste.

### Livrables

#### Shaders (`Shaders/`)
- ✅ `OceanSurface.shader` — surface opaque deferred, **4 passes tessellées** (GBuffer/DeferredOnly, DepthOnly,
  ShadowCaster, MotionVectors) + META non tessellée. **Cull Back**. Réutilise l'INTÉGRALITÉ du framework HDRP/Lit
  (encodage GBuffer, drivers de passe, `TessellationShare.hlsl` hull/domain, `MotionVectorTessellation`). Stencil
  **vérifié vs HDRP 17.4 `Lit.shader`** : GBuffer Ref=2/WriteMask=3 (RequiresDeferredLighting, sans SSS),
  DepthOnly Ref=0/WriteMask=8, MotionVectors Ref=32/WriteMask=32.
- ✅ `OceanSurfaceData.hlsl` — `GetSurfaceAndBuiltinData` (remplace `LitData.hlsl`) : normales **analytiques**
  recomposées depuis les pentes des cascades (anti-bug #2), couleur de base provisoire, Lit STANDARD opaque.
- ✅ `OceanSurfaceTessellation.hlsl` — hooks du contrat HDRP : `GetMaxDisplacement` (auto-dérivé),
  `GetTessellationFactor` (gaté distance, **quantifié** + caméra de référence **snappée** côté shader → stabilité MV),
  `ApplyTessellationModification` (déplacement xyz ; échantillonne le tampon **N-1** lors du rejeu MV via `_LastTimeParameters`).
- ✅ `OceanSurfaceCascadeSampling.hlsl` — échantillonnage partagé des cascades P1 (déplacement / pentes / N-1),
  lecture **pure** des globaux (`_OceanDisp512/256`, `_OceanDeriv512/256`, `_OceanCascade*`, `_OceanDispPrev512/256`, `_OceanMVValid`).

#### C#
- ✅ `OceanSurfaceModule.cs` — module data-driven : grille **UNIFORME FIXE world-locked**, push des props matériau
  (tessellation, couleur), **bounds XYZ recalculés à chaud** (hash d'amplitude), **maxHorizontalDisplacement AUTO-DÉRIVÉ**
  des cascades (seul `boundsSafetyScale` exposé), `OnValidate` anti-NaN (`tessMax>tessMin`, `maxTessFactor∈[1,64]`,
  extents>0), binding `_OceanDispPrev*`/`_OceanMVValid` **EXCLUSIVEMENT via `ctx.globals`** (anti-bug #1).
- ✅ `OceanSurfaceRuntime.cs` — état runtime non sérialisé : GameObject enfant (MeshRenderer + Per Object Motion),
  mesh grille uniforme (CLIPMAP_READY), bounds, détention du coordinator MV.
- ✅ `OceanMotionVectorPass.cs` — **coordinator T-1** (n'est PAS un CustomPass) : `SnapshotPrevious()` fait la
  `CopyTexture` `_OceanDisp*`→`_OceanDispPrev*` dans **`PreSimulate` (avant l'évolution du spectre)**, gatée par
  **`Time.frameCount`** (au plus une copie/frame, aucun champ lu sur P1), miroir **strict** des arrays P1
  (format+résolution+slices, **toutes les slices**), `init prev=current` (RT==null/realloc → MV nuls, pas de flash),
  cycle de vie symétrique anti-fuite. **Aucun abonnement RenderPipelineManager.**
- ✅ `OceanFeatureModule.cs` — hook **`PreSimulate(ctx)`** virtuel **no-op** (additif, P1 ne l'override pas).
- ✅ `OceanSystem.cs` — **`PreSimulateAll()`** : balayage distinct AVANT `ApplyAll()`/`Tick()` (invariant d'ordre
  PreSimulate→évolution garanti, indépendant de l'ordre des modules) ; instrumentation `OceanPerfRecorders`.
- ✅ `OceanProfiler.cs` — markers `Ocean.Surface`/`Ocean.MotionVector` **puis (2026-07-05b) `Ocean.Spectrum`/`Ocean.FFT`**
  passés en **`MarkerFlags.SampleGPU`** (édition des initialiseurs) ; `OceanPerfRecorders` lance **3 ProfilerRecorder GPU**
  — (a) passe GBuffer, **(b) marker ENGLOBANT `Ocean.Spectrum`** (évolution+IFFT+assemblage ; `Ocean.FFT` = sous-mesure
  NON additionnée pour éviter le double comptage — nesting `using(Spectrum)⊃using(FFT)`), (c) `Ocean.MotionVector` —
  garde `Valid && Count>0` → « n/a » jamais 0 trompeur ; `Readout()` expose les 3 postes + la SOMME. Readout fusionné
  (pas de `OceanPerfReadout.cs` séparé), **non surfacé dans un HUD** (lecture = fenêtre Profiler GPU, cf. `OCEAN_TEST_P2.md §(c)`).

### Décisions clés de cette phase (révision approuvée)
- **MV / tampon T-1** : copie **découplée du rendu**, dans le tick via `PreSimulate` AVANT l'évolution du spectre →
  **race intra-frame inter-contextes éliminée par construction** (tous les contextes Scene+Game+sondes lisent
  prev=D[N-1] à l'identique), **aucune détection de « contexte principal »**, **aucune famine de copie**.
- **Cadence = `Time.frameCount`** (proxy global monotone), **PAS un champ de `OceanSpectrumModule`** (inexistant ; l'ajouter
  violerait « P1 byte-à-byte intact ») → **P1 réellement intact** (hook `PreSimulate` no-op non override par P1).
- **Tessellation** : gating distance HDRP natif désactivé, gating **quantifié + snappé** calculé dans `GetTessellationFactor`
  (réutilise quand même hull/domain/MV-tessellation de HDRP) → facteur constant frame-à-frame entre paliers (stabilité MV).
- **Grille UNIFORME FIXE** world-locked ; suivi caméra/extension monde = **pivot clipmap Q3.4 différé** (CLIPMAP_READY).

### Limites & réserves
- **Résidu MV au franchissement de palier** de quantification (atténué, non éliminé ; limite HDRP documentée :
  « motion vectors incorrects si le facteur de tessellation diffère entre 2 frames »). Contrôle caméra EN MOUVEMENT au protocole.
- **Mesure GBuffer RTX 2060** et **verrouillage table budget** = HORS environnement (gate utilisateur).
- **Nom du marker GBuffer** HDRP à confirmer dans le Profiler GPU (candidats essayés : `RenderGBuffer`/`GBuffer`).
- **Question P1** (arrêt vs repli « 3 IFFT séparées ») et **pivot clipmap** restent dépendants de la première mesure ms concrète (P2).

### Critères de validation (gate utilisateur éditeur)
Voir **`OCEAN_TEST_P2.md`** (protocole complet : gate P1, compilation, gate **éclairage** (eau NON noire),
tessellation ~200k tris ±20%, **pré-check d'avancée de la simulation**, MV vagues fixe + **multi-vues Scene+Game+sonde** +
mouvement, premier frame/preset, bounds à chaud, delta GBuffer, smoke-check non-régression P1).

### Périmètre P2 respecté
- ✅ P1 (`OceanSpectrumModule.cs` + shaders compute) **NON modifié**.
- ✅ Ancien `Assets/Shader/Ocean/` intact.
- ✅ Aucun `OceanPerfReadout.cs`, aucun `foamJacobianThreshold`, aucun `maxHorizontalDisplacement` manuel introduits.
- ✅ Aucune logique de suivi caméra/recentrage (différée au pivot clipmap Q3.4).
- ✅ Renumérotation des slots `.backups` P3→P10 **différée au début de P3** (annotation ciblée du ROADMAP).

---

## P3 — Absorption (module `absorption`) — ✅ CODE/TESTS/OUTILLAGE/PROTOCOLE ÉCRITS — CORRECTIF COULEUR (k5) APPLIQUÉ — EN ATTENTE GATES (d)/(e)/(f)

**Livraison (code) :** 2026-07-05 (k) · **correctif couleur k5 : 2026-07-06**
**Snapshots :** `.backups/08_before_P3_absorption/` (avant — état P2 validée intact, 99 fichiers vérifiés,
1ᵉʳ slot de la cascade actée `(2n+2)/(2n+3)`) · `.backups/08b_before_P3_k4_fix/` (avant k4) ·
`.backups/08c_before_P3_color_fix/` (**avant k5**, miroir complet 117 fichiers, MANIFEST + HASHES, vérifié
disque) · `.backups/09_P3_absorption_validated/` (après, **à créer à la validation utilisateur**).
**Contrat (ROADMAP §3 P3 — Q6.1/Q6.2/Q6.3) :** Beer-Lambert spectral `I = I₀·exp(−σ·d)` par canal, 3 profils
Jerlov Ia/II/III, master `waterType [0..1]`, push global NON cumulatif, **source de vérité UNIQUE** partagée
surface + (futur) sous-marin P6.

### Ré-spéc gate (d) — 2026-07-05 (k3) — transmittance → réflectance MONTANTE + 1ᵉʳ calibrage ancres
**Échec constaté (utilisateur)** : eau **turquoise** à Ia, Ia≈II indiscernables. **Diagnostic instrumenté
(MCP : sliders pilotés, captures, relevés de globals, piège `_BaseColor` rouge)** : pipeline SAIN (σ poussé,
`enabled=1`, rouge non rendu → le chemin absorption est bien affiché) ; cause = le **modèle** : la
transmittance `exp(−σ·d)` ne peut pas produire un bleu océan — avec les a(λ) réels de l'eau claire, G et B
survivent à quasi-égalité (Ia@10 m = (0.03, 0.66, 0.76) = turquoise). La couleur vue de dessus est la
**réflectance montante**, dominée par la rétrodiffusion Rayleigh de l'eau pure (∝ λ⁻⁴) qui favorise le bleu.
**Correctif (`OceanSurfaceData.hlsl`)** : albédo = `b_b_pure/σ × (1 − exp(−2σ·d)) × kUpwellingScale`, avec
`b_b_pure = (0.206, 0.422, 1.0)` (Rayleigh λ⁻⁴·³ par bande, normalisé bleu) et `kUpwellingScale = 0.02` =
**CONSTANTES** (ni paramètres, ni scattering V1.5 : aucun champ, aucune variation par type d'eau — toute la
chromie vient de σ, source unique Q6.1 ; `(1−exp(−2σd))` = Beer-Lambert canonique sur l'aller-retour de la
colonne perçue).
**Calibrage ancres (Q6.1 §D — 1ᵉʳ passage FAIT)** : II **(0.45, 0.09, 0.12)** · III **(0.60, 0.20, 0.65)**
(CDOM côtier = absorption du bleu → σ_B ↑ ; Ia inchangée). Assets sauvés + defaults builder alignés.
**Vérification (captures MCP — banc temporaire ciel PBR + soleil 50° + Exposure Fixed EV14, monté puis
INTÉGRALEMENT démonté : soleil restauré (68.4, 36.8, 143.5), 0 volume résiduel, scène non sauvée)** :
**Ia = bleu océan franc · II = bleu-vert côtier · III = vert sombre**, progression continue ✓.
Références : `Assets/Screenshots/P3_v4_{Ia,II,III}_*.png` (avant-correctif : `P3_absorption_*.png`).
**Leçons de banc** : (1) `Ocean Debug.unity` n'a NI Volume NI ciel → juger la couleur sous un environnement
(l'env de gate Fixed EV10 est calibré pour le rig de la scène de GATE, pas un soleil 100 000 lux) ; (2) une
capture MCP ne tick PAS `[ExecuteAlways]` quand l'éditeur est défocalisé → **forcer `Update` avant chaque
capture** (sinon captures identiques sur état périmé) ; (3) l'aliasing spéculaire orange au loin en capture
one-shot sans TAA est attendu (hors périmètre P3).
**Conséquence : gates visuels (d)/(e) utilisateur RE-DUS sur le nouveau rendu.**

### Correctif COULEUR — 2026-07-06 (k5) — plateau résiduel + palette (gates d/e restées NON validées)
**Échec constaté (utilisateur, post-k4)** : plateau persistant sur `perceivedDepth` [25..50] (Ia 25 m
`(0,37,93)` ≈ 50 m `(0,40,108)`) + palette hors cible (Ia trop clair ; III `(1,18,10)` = vert-bleu faible
au lieu d'un vert sombre franc). **Diagnostic recadré : CALIBRAGE, pas structure.** La structure d'upwelling
`b_b/σ × maturité × kUpwellingScale` est saine (aucun clip : max canal asymptotique = Ia bleu 0.71 < 1) →
le remplacement par `R_inf = b_b/(a+b_b)` est **ÉCARTÉ** (donnerait un cyan-turquoise, unités incohérentes).
**K4 CONSERVÉ** (le retirer réintroduit le plateau qu'il corrigeait sur II/III). Deux leviers :
- **`kDepthRate` 0.12 → 0.04** (`OceanSurfaceData.hlsl`) : knee canal moyen 4 m → 12 m → maturité en
  transition jusqu'à 50 m sur TOUS les types (Δmaturité bleu Ia 25→50 m 0.166→0.241 ; Δvert III 0.023→0.202).
  Constante shader, **aucun global/slider/push** (anti-bug n°1 intact). Effet secondaire : proche + défaut
  plus sombres → défaut `perceivedDepth` **8 → 15** (`OceanAbsorptionModule.cs`, compensation).
- **Recalibrage ancres** (assets + défauts builder), renforcement de l'inversion σ_B>σ_G (CDOM absorbe le
  bleu) : **Ia (0.36,0.041,0.028) inchangé** · **II (0.45,0.09,0.15)** (σ_B 0.12→0.15) · **III
  (0.55,0.20,1.10)** (σ_R 0.60→0.55, σ_B 0.65→1.10 → bleu asymptotique écrasé, G/B≈2.3 = vert sombre).
  σ_B monotone Ia→II→III → hue monotone, pas de pic ; aucun canal asymptotique ≥ 1 → `saturate()` ne clippe pas.
**Clarification gate (d)** : le HUE (bleu→bleu-vert→vert) est porté par `waterType` ; `perceivedDepth` fait
mûrir proche→profond SANS changer le hue à `waterType` fixe (correction d'attente documentée dans OCEAN_TEST_P3.md).
**Calibrage QUALITATIF** (tone-mapping HDRP non quantifié) : lectures RGB user = diagnostic RELATIF, pas cible absolue.
Fichiers : `OceanSurfaceData.hlsl`, `WaterAbsorption_{II,III}.asset`, `OceanAbsorptionAnchorsBuilder.cs`,
`OceanAbsorptionModule.cs`, `OceanAbsorptionTests.cs` (commentaire), `OCEAN_TEST_P3.md` (§k5). Tests 16/16 inchangés
(logique non touchée). **Conséquence : gates (d)/(e)/(f) utilisateur RE-DUS ; snapshot 09 après validation.**

### Livrables
- **`Profile/WaterAbsorptionProfile.cs`** (NOUVEAU) : SO exposant STRICTEMENT les 3 σ (m⁻¹, absorption pure
  a(λ) en 3 bandes RGB — **jamais Kd**, Q6.3 §C) + label informatif ; clamps `OnValidate` [0..10] + tooltips ;
  menu `Create > Ombrage/Ocean/Water Absorption Profile` (profils utilisateur libres = variantes d'ancre, Q6.2).
- **`Profile/Modules/OceanAbsorptionModule.cs`** (stub P0 → RÉEL) : 3 refs d'ancres (auto-résolution
  EDITOR-ONLY depuis `Profiles/` — même caveat build que ResolveShaders (f) P2 : **références à sérialiser
  dans le profil**) ; master `waterType` [0..1] — interpolation **PAR SEGMENTS**, ancre II = 0.5 (`kAnchorII`,
  convention artistique, question ouverte Q6.1 §D) ; `perceivedDepth` [0.1..200] m (« profondeur perçue »
  Q6.1 §A, consommation surface V1) ; **`Apply` = l'UNIQUE point de push** de **`_WaterAbsorption`** (vec4,
  .rgb = σ) + **`_OceanAbsorptionDepth`**, via `ctx.globals` (SET pur, anti-bug n°1) ; **ZÉRO σ en dur**
  (Q6.2 §C) — sans ancres : aucun push + warning unique, la surface reste sur `_BaseColor`.
- **`Profile/Modules/OceanSurfaceModule.cs`** (+ `BindAbsorption`) : pousse **`_OceanAbsorptionEnabled`**
  (0/1, global via ctx.globals) = module absorption **présent + actif + ancré**. Couvre le toggle runtime de
  `active` (un module désactivé n'Apply plus → son σ périmé est rendu inerte par l'interrupteur). Branche
  **UNIFORME** : zéro variant/keyword (leçon explosion de variants terrain).
- **`Shaders/OceanSurfaceData.hlsl`** : 3 globaux déclarés **HORS `UnityPerMaterial`** (aucune entrée
  `Properties{}` — règle SRP Batcher sens n°1) ; albédo = **réflectance montante**
  `lerp(_BaseColor.rgb, b_b_pure/σ·(1−exp(−2σ·d))·kScale, enabled)` **(corrigé k3 — la transmittance
  exp(−σ·d) rendait turquoise)** — Ia = bleu océan (Rayleigh bleu / rouge absorbé), III = vert sombre
  (CDOM absorbe le bleu), éclairé ensuite par le LightLoop. **Collision de noms vérifiée dans le package
  HDRP 17.4 : AUCUNE** (`_WaterAbsorption` / `_OceanAbsorptionDepth` / `_OceanAbsorptionEnabled`).
- **`Assets/Editor/Ocean/OceanAbsorptionAnchorsBuilder.cs`** (NOUVEAU) : menu `Ombrage/Ocean/Create Water
  Absorption Anchors (Ia, II, III)` — **create-if-missing, ne JAMAIS écraser** (protège le calibrage) ;
  valeurs de départ littérature (ancrage type IB ≈ (0.37, 0.044, 0.035) m⁻¹, Akkaynak 2017), **révisées
  au 1ᵉʳ passage de calibrage (k3)** : **Ia (0.36, 0.041, 0.028) · II (0.45, 0.09, 0.12) · III
  (0.60, 0.20, 0.65)** — révision fine toujours possible dans les assets (Q6.1 §D).
- **`Profile/Tests/OceanAbsorptionTests.cs`** (NOUVEAU, +4 tests → **16 EditMode attendus**) : ancres
  restituées exactes / linéarité intra-segment / clamp hors [0..1] / **push non-cumulatif + RestoreAll
  neutre** (le critère « push vérifié non cumulatif » rendu EXÉCUTABLE, en plus de la revue).
- **`OCEAN_TEST_P3.md`** (NOUVEAU) : protocole complet (0-bis 16/16 → ancres → gate visuel LIVE →
  gate repli/toggle → non-régression P2 → clôture snapshot 09).

### Critères de validation (gate utilisateur)
**Couleur cohérente en surface** (Ia bleu profond → II bleu-vert → III vert-brun, slider LIVE) ; **profils
commutables** ; **un seul `_WaterAbsorption` poussé** ; **push vérifié non cumulatif** (revue de code faite +
test EditMode). Protocole détaillé : `OCEAN_TEST_P3.md`.

### Périmètre P3 respecté
- ✅ P1 (spectre) **byte-à-byte intact** ; MV/tessellation P2 non touchés (seuls ajouts surface :
  `BindAbsorption` + lerp d'albédo).
- ✅ **Aucun scattering/turbidité** (V1 = absorption pure, Q6.3 — extension V1.5 non-breaking, aucun champ mort).
- ✅ Aucun σ en dur hors des 3 assets-ancres ; **pas de 2ᵉ modèle d'absorption** (anti « absorption dupliquée
  sur 3 systèmes » de l'audit).
- ✅ Budget : +1 `exp` + 1 `lerp` par pixel + 3 uniforms ≈ ε ≪ cible 0.18 ms Ultra (cumul re-contrôlé au
  prochain relevé build / T1).

---

## Convention de snapshots — banc de gates P2 (préservation du slot 05)

> Réponse au point 4 du verdict « revision_requise » (convention de numérotation clarifiée pour **ne pas
> écraser** le slot 05).

- **`05_P2_surface_deferred_validated/` = IMMUABLE.** État **code pré-gates** (durcissements P2 + infra de
  test EditMode + revue de conformité). On **ne le renomme PAS**, on **ne l'écrase PAS**. Point de
  référence SHA-1 inchangé.
- **`06_before_P2_gates/`** = capture du **code LIVE** avant l'exécution des gates : correction post-05
  `ProfilerRecorder.SumAllSamplesInFrame` + alignement des chaînes de mesure (`OceanProfiler.Readout` /
  `OceanSystem` : « toggle `MeshRenderer.enabled` », plus « toggle module ») + **nouvel outillage éditeur**
  (`Assets/Editor/Ocean/` : profil de gate, VolumeProfile de gate, builder de scène, toggle renderer,
  résolution HDRP). MANIFEST annoté (type de Sky = **GradientSky**, `fixedExposure` = **EV 10**, décisions).
- **`07_P2_gates_validated/`** = **✅ CRÉÉ le 2026-07-05 (j)** après les 4 gates + table budget **BUILD**
  RTX 2060 remplie (colonnes éditeur non relevées = corroborantes, non requises — le verrou a tenu sur build ;
  MANIFEST + SHA-256 + présence disque vérifiée).
- **Règle générale :** décimal **croissant**, **jamais d'écrasement**, chaque slot porte un `MANIFEST.md`
  annoté (SHA-1, verdict gates, corrections post-snapshot, Sky + exposition du banc). On **CRÉE** 06/07,
  on **ne RENOMME PAS** 05.

> ⚠️ **Cohérence croisée avec OCEAN_ROADMAP.md (2026-07-05).** La cascade `Pn → 2n/2n+1` du ROADMAP —
> présente à **3 loci** : formule §1.1(b), restatement de la section P2, et les 8 lignes « Snapshot de fin »
> P3→P10 du §3 — a été marquée **SUPERSEDED / PROVISOIRE** et **alignée sur la présente convention** :
> `06`/`07` appartiennent au **banc de gates P2** (`06_before_P2_gates/` sur disque, `07_P2_gates_validated/`
> réservé) ; la **renumérotation P3→P10 est à acter au setup P3** ; **ne pas présumer `06`/`07` libres**. Les
> deux documents portent désormais la **même** affectation `06`/`07`.
> **→ ✅ ACTÉE le 2026-07-05 (setup P3) : `Pn → (2n+2)/(2n+3)`** — P3=`08`/`09`, P4=`10`/`11`,
> P5=`12`/`13`, P6=`14`/`15`, P7=`16`/`17`, P8=`18`/`19`, P9=`20`/`21`, P10=`22`/`23` — miroir exact du
> locus canonique ROADMAP §1.1(b). `08_before_P3_absorption/` **CRÉÉ** (99 fichiers, SHA-256, vérifié disque).

---

## Révision du plan V1 — 5 points (traçabilité)

> Consolidation 2026-07-05 : les 5 points du gardien du scope sont **tracés** ici et dans
> `OCEAN_ROADMAP.md §« Révision du plan V1 »`. **Document de détail unique = `P2_REVISION_PLAN.md`**
> (point d'entrée de reprise). On **renvoie**, on ne duplique pas. Numérotation = celle du **brief**
> (`P2_REVISION_PLAN.md §A` ordonne différemment : son #2 = mesure budget, son #3 = snapshots).

| # (brief) | Point | Statut | Artefact porteur (détail) |
|---|---|---|---|
| **1** | Aucune scène `.unity` en YAML manuel (editor-script one-shot + Missing Script = 0) | ✅ acté / outillé | `P2_REVISION_PLAN.md §A(1)` · `Assets/Editor/Ocean/OceanP2GateSceneBuilder.cs` |
| **2** | Convention de numérotation des snapshots (05 immuable ; 06/07 = banc de gates P2) | ✅ acté | §« Convention de snapshots — banc de gates P2 » ci-dessus · `OCEAN_ROADMAP.md §1.1(b)` |
| **3** | Mesure du budget GPU (Game view seule, delta `MeshRenderer.enabled`, table éditeur/build, pas de pivot T2 sur une valeur éditeur) | ✅ protocole écrit | `OCEAN_TEST_P2.md §(i)/(i-bis)/(j)` · `P2_REVISION_PLAN.md §A(2)` |
| **4** | Recompilation utilisateur après tout changement shader (Console vide + EditMode 12/12) | ✅ prérequis inscrit | `OCEAN_TEST_P2.md §(a-bis)` · `P2_REVISION_PLAN.md §A(4)` |
| **5** | Résolution dynamique du chemin HDRP (glob PackageCache + fallback Packages/, version via package.json) | ✅ outillé | `Assets/Editor/Ocean/OceanHdrpPath.cs` · `P2_REVISION_PLAN.md §A(5)` |

---

## Protocole de test éditeur P1 (pour l'utilisateur)

> **Smoke test EditMode toujours DIFFÉRÉ** (asmdef de test incompatible avec la décision « pas d'asmdef
> production », roadmap §différé #2). Validation par **debug visuel + logs + test d'identité in-shader**.

1. **Compilation** : laisser Unity recompiler → Console **VIDE** (0 erreur). Les 3 nouveaux assets
   (`OceanFFT.compute`, `OceanSpectrum.compute`, `OceanSpectrumDebug.shader`) doivent s'importer sans erreur.
2. **Activation** : ouvrir l'`OceanProfile` → `Add Module > Simulation/Spectrum`. Ajouter un `Ocean System`
   à un GameObject avec ce profil. Vérifier qu'aucune exception n'apparaît (cycle Setup/Teardown propre).
3. **Lecture go/no-go P1.a** : dans la Console, repérer le log `[Ocean P1.a] … ratio≈0.50` (gain 50 %).
4. **Visualisation** : créer un Quad, lui assigner un matériau utilisant `Hidden/Ocean_v2/SpectrumDebug`.
   - `Mode=0` (hauteur) → champ de vagues animé qui défile.
   - `Mode=2` (normale) → normales **lisses, sans seams/escaliers** (preuve anti-bug #2, dérivées analytiques).
     Le mode normale **auto-force l'array Deriv** du groupe de résolution sélectionné (un array Disp donne
     sinon des pentes erronées) — toute valeur de `_DebugArray` reste donc valide en `Mode=2`.
   - `Mode=3` (Jacobien) → liserés blancs sur les crêtes (préparation écume P4).
   - Faire varier `_DebugArray`/`_DebugSlice` pour inspecter chaque cascade.
5. **Preuve anti-bug #3 (découplage N)** : passer `cascadeQuality` de **High** à **Low** (512²→256²).
   La **hauteur des vagues doit rester INCHANGÉE** (seul le détail fin disparaît). Inversement Ultra
   ajoute du détail sans gonfler l'amplitude. (Optionnel : cocher `runIdentityTest` pour le log de convention.)
6. **Anti-répétition (golden-ratio)** : agrandir le Quad / panoramiquer → **aucune répétition franche** visible.
7. **Cycle propre** : désactiver le `Ocean System` → aucune fuite (globaux restaurés), réactiver → reprise normale.

**Si une étape échoue** → P1 NON validée : rollback `.backups/02_before_P1_spectre/` + diagnostic.

**Critères de validation (rappel ROADMAP §P1) :** spectre stable 4 cascades ; amplitude découplée de N ;
normales analytiques ; zéro répétition visible ; budget `spectre` (mesure ms reportée au proto P2).

---

## Notes Globales

### Périmètre P0 Respecté
- ✅ Aucun shader GBuffer
- ✅ Aucun FFT, aucune vague
- ✅ Aucune valeur métier poussée (harnais existe, vide)
- ✅ Ancien `Assets/Shader/Ocean/` + `OutdoorsScene.unity` : strictement INCHANGÉS

### Décisions Implicites P0
- **Namespace :** `Ombrage.OceanFeatures` (cohérence projet)
- **Pattern :** 1:1 `TerrainProfile`/`TerrainFeatureModule` (rodé en production)
- **Pas d'asmdef :** code en `Assembly-CSharp` (décision verrouillée Q13.1)
- **Snapshots Write/Copy-Item :** pattern herbe/terrain réutilisé

### Correction Compilation (2026-07-04) — Reprise Session Océan v2

**Problème A** : Build bloquée 4 erreurs CS0117 + CS1503 (enum `MarkerFlags.SampleGpu` non existent).

**Cause racine A** : Faute de casse dans les 2 initialiseurs ProfilerMarker 
- Ligne 29 : `MarkerFlags.SampleGpu` → corrigé `MarkerFlags.SampleGPU` (Ocean.Surface)
- Ligne 30 : `MarkerFlags.SampleGpu` → corrigé `MarkerFlags.SampleGPU` (Ocean.MotionVector)
- Fichiers affectés : `OceanProfiler.cs` (2 corrections de code + 3 alignements commentaires), `OceanSystem.cs` (1 alignement commentaire l.39)

**Problème B** : Warning CS0618 sur `OceanSurfaceRendererToggle.cs` L50 — appel d'overload déprécié.

**Cause racine B** : La surcharge `FindObjectsByType<T>(FindObjectsInactive, FindObjectsSortMode)` à 2 arguments est dépréciée en Unity 6000.x. Le message du compilateur indique l'overload **non déprécié** : `FindObjectsByType<T>(FindObjectsInactive)` à 1 seul argument. Le commentaire L47-48 présentait le 2-args comme valide (trompeur).

**Correction appliquée** :
- Ligne 51 : `FindObjectsByType<OceanSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` → `FindObjectsByType<OceanSystem>(FindObjectsInactive.Exclude)` (suppression arg `FindObjectsSortMode`)
- Lignes 47-50 : commentaire corrigé pour documenter que l'overload à 2 args EST lui aussi déprécié
- Raison : pattern `Exclude` (ne cherche que les objets actifs) répond au besoin du module ; tri n'apporte rien au cas d'usage (un seul `OceanSystem` attendu)
- **Conformité** : cette surcharge à 1 argument est déjà le pattern de l'équipe ailleurs (OceanSystem.cs L1249, UnderwaterPass.cs L182, UnderwaterLightingController.cs L102)

**Statut** : ✅ Compilation rétablie (Console Unity 0 erreur). Diff chirurgical, instrumentation P2 intégralement préservée. CS0618 supprimé, convention de code consolidée.

**Prochaines étapes (gate utilisateur éditeur)** :
1. **Gate 0-bis récompilation** (par l'utilisateur en Unity) : laisser l'éditeur recompiler (Ctrl+R / Reimport shaders), vérifier Console **VIDE** (CS0618 disparu), lancer Test Runner EditMode **12/12 verts** (cf. `OCEAN_TEST_P2.md` §a-bis).
2. **P1 validation** (par l'utilisateur en Unity) : exécuter le protocole OCEAN_TEST_P2.md §«Protocole de test éditeur P1» (smoke test P1.a/P1.b).
3. **P2 validation** (par l'utilisateur en Unity) : exécuter le protocole complet OCEAN_TEST_P2.md (GBuffer illuminé, tessellation ~200k tris, MV, mesure RTX 2060).

### Branchements Ouverts (À Trancher au Proto P2)
- T1 : Hiérarchie V1/V1.5 underwater (Q1.5↔Q8.1/Q9.1) — reconfirmation partielle P2, décision définitive P5
- **T2 : Budget tessellation (Q11.2↔Q3.2/Q3.3) — ✅ TRANCHÉ le 2026-07-05 (j) : TESSELLATION CONSERVÉE.**
  SOMME build RTX 2060 (Ultra, D3D12) = 0.169 + 1.252 + 1.143 = **2.564 ms ∈ [2–4 ms]**, poste (a) ≪ part
  ~1.00 ms → **pivot clipmap Q3.4 NON déclenché**, budget verrouillé (borne haute dev build). Marge 1.436 ms
  pour P3/P5/P6 ; recontrôle total-frame à T1 (P5/P6). ⚠ poste (c) copie T-1 = candidat d'optimisation différé
  (cf. §« Relevé budget BUILD + clôture du gate 4 »).
- T3 : Crédibilité eau côtière trouble (Q6.3) — type III vs photo référence, si inacceptable sans diffusion → single-scattering V1

---

## Historique

| Date | Événement | Phase | Snapshot |
|---|---|---|---|
| 2026-06-28 | Cadrage complet (41/41 Q) + plan exécutif OCEAN_ROADMAP.md livré | pré-P0 | `.backups/00_before_P0_scaffolding/` |
| 2026-06-29 | P0 implémenté + révision Réviseur P0 : 3 problèmes identifiés | P0 | — |
| 2026-06-29 | Correctif #3 appliqué (repaint éditeur) ; #1/#2 documentés/différés | P0 | `.backups/01_after_P0_scaffolding/` |
| 2026-06-29 | Mise à jour OCEAN_ROADMAP.md + mémoire session + présent fichier OCEAN_IMPLEMENTATION_STATUS.md | doc | — |
| 2026-06-29 | P0 validé utilisateur (éditeur) → snapshot avant-P1 | P1 | `.backups/02_before_P1_spectre/` |
| 2026-06-29 | **P1 implémenté** : FFT Stockham + JONSWAP/TMA dormant + dérivées analytiques + 4 cascades mixtes + IFFT hermitienne 2-en-1 ; anti-bugs #2/#3 à la source. EN ATTENTE validation. | P1 | `.backups/03_P1_spectre_validated/` |
| 2026-06-30 | **P2 implémenté (code+instrumentation+protocole écrits et revus)** : surface deferred GBuffer (4 passes tessellées + META), tessellation gatée distance quantifiée, passe MotionVectors native + coordinator T-1 (copie en PreSimulate avant l'évolution, cadence Time.frameCount), bounds XYZ à chaud + maxHorizontalDisplacement auto-dérivé, instrumentation ProfilerRecorder GPU. **P1 byte-à-byte intact.** EN ATTENTE validation éditeur + mesure RTX 2060. | P2 | `.backups/04_before_P2_surface_deferred/` (avant) · `.backups/05_P2_surface_deferred_validated/` (après) |
| 2026-07-04a | **Reprise session océan v2 — Correction compilation bloquante A (OceanProfiler.cs)** : faute de casse `MarkerFlags.SampleGpu` → `MarkerFlags.SampleGPU` (2 initialiseurs ProfilerMarker, lignes 29–30, Surface + MotionVector) + alignement commentaires (6 refs). Console Unity → 0 erreur. | compilation | — |
| 2026-07-04b | **Reprise session océan v2 — Correction compilation bloquante B (OceanSurfaceRendererToggle.cs)** : warning CS0618 L51, overload `FindObjectsByType` dépréciée (2 args) → surcharge non dépréciée (1 arg). `FindObjectsByType<OceanSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` → `FindObjectsByType<OceanSystem>(FindObjectsInactive.Exclude)`. Commentaire L47-50 corrigé (2-args est aussi déprécié). Conformité vérifiée : pattern 1-arg utilisé partout (OceanSystem.cs L1249, UnderwaterPass.cs L182, UnderwaterLightingController.cs L102). | compilation | — |
| 2026-07-04c | **Révision du banc de validation P2** (levée verdict « revision_requise » + edge-cases) : outillage éditeur `Assets/Editor/Ocean/` (profil de gate Spectrum+Surface, VolumeProfile déterministe GradientSky+ExposureFixed+Fog off — **APV retirée**, builder de scène avec garde anti-perte + assertions bruyantes + caméra TAA/MV + vérif HDRP Asset, toggle `MeshRenderer.enabled` via `GetComponentsInChildren`, résolution HDRP `PackageInfo`). Docs : `OCEAN_TEST_P2.md` (gate 0-bis recompile, mesure delta corrigée A+D, table éditeur/build), `P2_REVISION_PLAN.md`, convention snapshots 05 immuable / 06 / 07. Alignement chaînes `OceanProfiler.Readout`/`OceanSystem`. **P1/shaders/modules de rendu intacts.** | banc P2 | `.backups/06_before_P2_gates/` (capturé) |
| 2026-07-05b | **Instrumentation poste budget (b) FFT/spectre + méthodologie mesure BUILD** (2 critiques bloquantes du plan). `OceanProfiler.cs` : `Ocean.Spectrum`/`Ocean.FFT` → `SampleGPU` ; 3ᵉ recorder GPU sur `Ocean.Spectrum` **englobant** (poste b = évolution+IFFT+assemblage ; `Ocean.FFT` sous-mesure NON sommée — nesting vérifié dans `EvolveAndTransform`) ; `Readout()` = 3 postes + SOMME. `OCEAN_TEST_P2.md` : table budget **3 postes GPU × colonnes éditeur/build**, **BUILD RTX 2060 = seule autorité**, §(i-build) chemin build (Development Build + Profiler GPU + D3D12/Vulkan ; `showPerfReadout` non câblé **écarté**), delta `MeshRenderer.enabled`/FrameDebugger **reclassés corroboration éditeur**, preset porteur **Ultra**, pré-condition GpuRecorder (edge-case D3D11). `OceanP2GateSceneBuilder.cs` : log du nombre de MeshRenderer actifs (proxy build poste a). `OCEAN_ROADMAP.md` §2.4/§4-T2 alignés. **Gate 0-bis ROUVERT (code modifié) ; gates P1/P2/gate 4 OUVERTS (mesures utilisateur non fournies) ; snapshot 07 NON créé.** P1/shaders/modules de rendu intacts. | banc P2 / instrumentation | — (07 réservé, non créé) |
| 2026-07-05c | **Correctif régression compilation shader (gate P2)** : `OceanSurface.shader` — ajout dans le `HLSLINCLUDE` des includes core `GeometricTools.hlsl` + `Tessellation.hlsl` entre `Common.hlsl` et `ShaderVariables.hlsl` (ordre calqué sur `LitTessellation.shader` HDRP 17.4 L342-345). Corrige `undeclared identifier 'positionRWS'` (`VaryingMesh.hlsl:322`) : la macro `TESSELLATION_INTERPOLATE_BARY` (définie par le core `Tessellation.hlsl`, utilisée par le domain `InterpolateWithBaryCoordsMeshToDS`) n'était pas déployée → token nu. Défaut présent dès le snapshot 05, touchant toutes les passes tessellées (ShadowCaster = 1ᵉʳ variant compilé au rendu) ; invisible à gate 0-bis (aucun rendu) et à la revue statique. **Gate 0-bis à RE-passer avant reprise gates P1/P2 ; gates OUVERTS ; snapshot 07 NON créé.** Aucun autre fichier touché ; instrumentation `OceanProfiler.cs` (b) inchangée. | compilation / shader | — (07 réservé, non créé) |
| 2026-07-05d/e | **Verdicts utilisateur (éditeur) + correctif compile BUILD.** Gates rapportés par l'utilisateur sur le code (c) : **gate 0-bis VERT** (Console 0/0, EditMode 12/12, « Verify Surface Runtime Present » OK, `positionRWS` disparu), **gate P1 VERT** (log `[Ocean P1.a] ratio≈0.50`, normales lisses Mode=2, 3 bugs interdits non reproduits), **gate P2 1–3 VERT** (eau non noire + réflexion ciel + gradient ; MV non-nuls cohérents, pas de ghosting TAA ; ~200k tris ±20 %, pas de NaN, bounds anti-pop, aller-retour presets stable). **Gate 4 (mesure budget BUILD) NON franchi** : l'utilisateur rapporte « l'océan n'est pas visible en build », puis — après ajout d'`OceanSurface` aux *Always Included Shaders* — 2 erreurs de compilation shader en build (`_ObjectId`/`_SelectionID`, passes SceneSelection/ScenePicking). **Correctif (e)** appliqué : déclaration `int _ObjectId; int _PassValue; float4 _SelectionID;` hors `UnityPerMaterial` (mirror `LitProperties.hlsl`). **Gate 0-bis + re-vérif compilation BUILD à re-passer sur le code (e) ; gate 4 reste OUVERT (visibilité build à diagnostiquer, puis 3 postes + somme non nuls sur BUILD RTX 2060) ; snapshot 07 NON créé.** | banc P2 / compilation / gates | — (07 réservé, non créé) |
| 2026-07-05h | **Durcissement visibilité surface en BUILD** (verdict Réviseur, bloquant #2). `OceanP2GateProfileBuilder.cs` : fabrique un matériau d'asset `Tests/OceanP2GateSurface.mat` depuis `OceanSurface.shader` et l'assigne à `OceanSurfaceModule.surfaceMaterialOverride` dans le profil de gate → chemin surface **build-déterministe** (matériau référencé par le build → shader **+ variants** tirés ; `EnsureMaterial` n'appelle plus `Shader.Find` en build). Assertion étendue au **round-trip disque** du matériau (recharge module + `.mat`, exige `surfaceMaterialOverride`≠null ET `mat.shader`≠null ; log vert « compute + **matériau de surface** SÉRIALISÉS ET RE-VÉRIFIÉS SUR DISQUE »). Complète (f) (compute/déplacement) côté rendu. **RE-CONSTRUIRE la scène de gate avant build.** Aucun code de rendu ni test EditMode touché (`.cs` d'Editor de banc). **Gate 0-bis + re-vérif compilation BUILD + gate 4 restent OUVERTS ; verdict T2 non tranché ; snapshot 07 NON créé** (efficacité build non prouvable par revue de code). | banc P2 / build-safety | — (07 réservé, non créé) |
| 2026-07-05i | **Build fonctionnel + océan VISIBLE en build + capture Profiler fournie → pré-conditions 0.a/0.b du gate 4 LEVÉES.** Retour utilisateur (« le build fonctionne, l'océan est visible ») + capture `ProfilerCaptures/Ombrage_2026-07-05_13-15-17.{data,png,highlights}`. Preuves exploitées par l'agent (ne peut lancer Unity) : PNG = surface rendue/déplacée (visibilité, donc correctifs (f)/(g)/(h) effectifs en build) ; scan `.data` = markers `Ocean.Spectrum/FFT/Surface/MotionVector` + `RenderGBuffer` **enregistrés en build** (instrumentation (b) active, GpuRecorder supporté par le backend). Correctifs compile-BUILD (c)/(e) confirmés (aucune erreur shader). **Reste OUVERT = lecture numérique** : le `.data` binaire (213 Mo) n'est pas décodable par l'agent → les 3 postes (a)+(b)+(c)+SOMME doivent être relevés par l'utilisateur dans le module GPU du Profiler (preset Ultra, non nuls). **Aucune valeur inventée ; gate 4 OUVERT, T2 non tranché, snapshot 07 NON créé.** Aucun code/shader/test touché. | gates / build | — (07 réservé, non créé) |
| 2026-07-05 | **Consolidation « Révision du plan V1 »** : intégration traçable des 5 points dans `OCEAN_ROADMAP.md` (nouvelle sous-section §P2 + notes §1.1 et §2.4) et `OCEAN_IMPLEMENTATION_STATUS.md` (bloc de traçabilité + note de cohérence croisée) par **RENVOI** vers `P2_REVISION_PLAN.md`/`OCEAN_TEST_P2.md` (zéro duplication). **Réconciliation de la table de slots aux 3 loci du ROADMAP** (formule §1.1(b), restatement §P2, 8 lignes « Snapshot de fin » 07→21) : cascade P3→P10 marquée **SUPERSEDED/PROVISOIRE**, `06`/`07` = banc de gates P2, renumérotation P3→P10 différée au setup P3 ; **ROADMAP↔STATUS rendus identiques sur 06/07**. `OCEAN_DECISIONS.md` (rang 1) et code C#/shader **non touchés**. Backup documentaire hors schéma de slots (`.backups/docs_before_V1revision_consolidation_2026-07-05/`). | doc | `.backups/docs_before_V1revision_consolidation_2026-07-05/` |
| 2026-07-05j | **🏁 Relevé budget BUILD + GATE 4 CLOS + VERDICT T2 = TESSELLATION CONSERVÉE.** 3 postes GPU relevés par l'utilisateur (module GPU du Profiler, capture build dev, Ultra, D3D12, RTX 2060) : (a) GBuffer **0.169 ms** · (b) Ocean.Spectrum **1.252 ms** · (c) Ocean.MotionVector **1.143 ms** → **SOMME 2.564 ms ∈ [2–4 ms]** ; poste (a) ≪ part ~1.00 ms ; **pivot clipmap Q3.4 non déclenché** ; budget verrouillé (borne haute dev build) ; marge 1.436 ms pour P3/P5/P6 (total-frame recontrôlé à T1). ⚠ poste (c) copie T-1 = candidat n°1 d'optimisation (différé). Table budget + relevé final §(l) remplis dans `OCEAN_TEST_P2.md`. **P2 intégralement validée ; prochaine étape = setup P3 — Absorption** (renumérotation slots à acter). Aucun code touché (clôture documentaire + snapshot). | gates / budget / T2 | `.backups/07_P2_gates_validated/` **(CRÉÉ)** |
| 2026-07-05k | **P3 — Absorption LIVRÉE (code + tests + outillage + protocole).** Renumérotation slots **ACTÉE** (`(2n+2)/(2n+3)`, P3=08/09 … P10=22/23, locus §1.1(b)) ; snapshot avant-phase créé+vérifié (99 fichiers, SHA-256). Livrables : `WaterAbsorptionProfile` (SO 3 σ clampés) ; `OceanAbsorptionModule` réel (3 ancres Jerlov auto-résolues, master `waterType` par segments — ancre II=0.5 —, `perceivedDepth`, **push SET pur** de `_WaterAbsorption`+`_OceanAbsorptionDepth`, zéro σ en dur) ; surface : `BindAbsorption` → `_OceanAbsorptionEnabled` (branche uniforme, 0 variant) + albédo `exp(−σ·d)` repli `_BaseColor` (`OceanSurfaceData.hlsl`, collision HDRP vérifiée nulle) ; builder ancres create-if-missing (Ia/II/III littérature, calibrage dû Q6.1 §D) ; +4 tests EditMode (**16 attendus**, dont non-cumul exécutable) ; `OCEAN_TEST_P3.md`. P1 intact ; MV non touchés. **EN ATTENTE : gate 0-bis (Console 0/0 + 16/16) + gates visuels (d)/(e) → snapshot 09.** | P3 | `.backups/08_before_P3_absorption/` **(CRÉÉ)** |
| 2026-07-05k2 | **Correctifs compile + pré-vérifications machine du gate P3 (MCP Unity).** (1) **CS0117** : consts `kAnchor*Path` `internal`→`public` (le builder vit dans `Assembly-CSharp-Editor`, sans `InternalsVisibleTo`). (2) **CS0618** : `OceanProfileEditor.cs` migré `GetInstanceID`→`GetEntityId` (clé de dictionnaire → `EntityId`, dépréciation Unity 6000.4). Recompilation forcée : **Console 0 erreur / 0 warning périmètre océan** ; **EditMode 16/16 verts** (1ʳᵉ run pré-domain-reload = 12 tests → relancée : 16/16) ; **3 ancres Jerlov créées via le menu et vérifiées disque** (`Profiles/WaterAbsorption_{Ia,II,III}.asset`). ⚠ Ticket hors périmètre : **famille CS0618 `TerrainLitCustom`** (GetInstanceID / FindFirstObjectByType / drawCommandPickingInstanceIDs — 9 warnings, dépréciations Unity 6000.4), chantier terrain/herbe. **Reste dû (utilisateur) : (c)2 module Absorption actif dans le profil de scène + SAUVER le profil (sérialise les refs), puis gates visuels (d)/(e)/(f) → snapshot 09.** | P3 / pré-gate | — |
| 2026-07-05k3 | **Ré-spéc gate (d) — couleurs corrigées + 1ᵉʳ calibrage ancres (diagnostic 100 % MCP).** Échec utilisateur (turquoise, Ia≈II) → sliders pilotés + captures + piège `_BaseColor` rouge (non rendu → pipeline sain, modèle en cause) : `exp(−σ·d)` garde G≈B (Ia@10 = (0.03, 0.66, 0.76)). **Fix `OceanSurfaceData.hlsl`** : albédo = réflectance montante `b_b_pure/σ·(1−exp(−2σd))·0.02` (b_b = constante Rayleigh eau pure — ni paramètre ni scattering V1.5). **Ancres recalibrées** II (0.45, 0.09, 0.12) / III (0.60, 0.20, 0.65) (assets + defaults builder). **Vérifié captures MCP** (ciel PBR + EV14 temporaires, démontés, soleil restauré) : Ia **bleu océan** · II **bleu-vert** · III **vert sombre** (`Assets/Screenshots/P3_v4_*.png`). Leçons banc : scène debug sans Volume/ciel ; captures MCP ne tickent pas `[ExecuteAlways]` défocalisé → `Update` forcé ; env de gate EV10 non transposable. **Gates visuels (d)/(e) RE-DUS sur le nouveau rendu → snapshot 09 à la validation.** | P3 / ré-spéc | — |
| 2026-07-06k5 | **Correctif COULEUR — plateau résiduel sur `perceivedDepth` [25..50] + palette hors cible (gates d/e restées NON validées post-k4).** Diagnostic recadré : **CALIBRAGE, pas structure** ; la structure d'upwelling `b_b/σ × maturité × kUpwellingScale` saine (aucun clip : max Ia bleu 0.71 < 1) ; **K4 CONSERVÉ** (retirer réintroduirait le plateau). Deux leviers : **(1) `kDepthRate` 0.12 → 0.04** (`OceanSurfaceData.hlsl`) : knee du canal moyen passe 4 m → 12 m → maturité en transition jusqu'à 50 m sur TOUS types (Δ Ia bleu 25→50 m 0.166→0.241 ; Δ III vert 0.023→0.202) ; constante shader, aucun global/slider/push (anti-bug n°1 intact) ; effet secondaire : défaut `perceivedDepth` **8 → 15** (compensation proche + sombre). **(2) Recalibrage ancres** : renforcement inversion σ_B>σ_G (CDOM absorbe bleu) : **Ia (0.36,0.041,0.028) inchangé** (bleu profond) ; **II (0.45,0.09,0.15)** (σ_B 0.12→0.15, bleu-vert côtier) ; **III (0.55,0.20,1.10)** (σ_R 0.60→0.55, σ_B 0.65→1.10, vert sombre avec G/B≈2.3). σ_B monotone Ia→II→III → hue monotone sans pic ; aucun canal asymptotique ≥ 1 → saturate() ne clippe. **Clarification gate (d)** : HUE porté par `waterType` (bleu→bleu-vert→vert) ; `perceivedDepth` fait mûrir proche→profond SANS changer hue à `waterType` fixe (correction d'attente). **Calibrage QUALITATIF** : lectures RGB user = diagnostic RELATIF, pas cible absolue (tone-mapping HDRP non quantifié). Snapshot `.backups/08c_before_P3_color_fix/` créé (117 fichiers, MANIFEST + HASHES.sha256 vérifiés disque). Fichiers modifiés : `OceanSurfaceData.hlsl` (kDepthRate, K4 conservé, commentaires justificatifs) ; `WaterAbsorption_{Ia,II,III}.asset` (σ recalibrés) ; `OceanAbsorptionAnchorsBuilder.cs` (défauts alignés) ; `OceanAbsorptionModule.cs` (perceivedDepth 8→15) ; `OceanAbsorptionTests.cs` (commentaire) ; `OCEAN_TEST_P3.md` (§k5 gate recadré, note tuning) ; `OCEAN_IMPLEMENTATION_STATUS.md` (P3 diagnostic recadré). Tests 16/16 inchangés (logique non touchée). **Gates visuels (d)/(e)/(f) utilisateur RE-DUS ; snapshot 09 après validation LIVE.** | P3 / correctif-couleur | `.backups/08c_before_P3_color_fix/` **(CRÉÉ)** |
| 2026-07-18b | **🚧 P6 DÉMARRÉE — G1+G2 validés, checkpoint mi-phase.** Cadrage P6 acté (« tes recos ») : double-sided + CustomPass BeforePostProcess + volumetrics HDRP ; caustiques=P7 ; Snell complet ; 5 gates. Snapshot avant-phase `14_before_P6_underwater/`. **G1 double-sided** (`OceanSurface.shader` Cull Off sur GBuffer/DepthOnly/MotionVectors ; normale déjà face-caméra) → surface visible de dessous ✅. **G2 absorption immergée** : `OceanUnderwater.shader` (fullscreen CustomPass, template HDRP) `color*=exp(−σ·d)` σ partagé (`_WaterAbsorption`, Q6.1), gaté immersion ; `OceanUnderwaterModule` réel (CustomPassVolume+FullScreenCustomPass, push non destructif) → vue immergée s'assombrit/teinte bleu-vert ✅. **Checkpoint `14b_P6_G1G2_checkpoint/`** (92 fichiers + SHA-256), caméra restaurée, scène sauvée. **Cadrage G3 (Snell) écrit** dans `OCEAN_TEST_P6.md` (défi surface opaque : refraction/TIR reconstruites ; 2 approches ; reco = approximation écran-espace). Reste G3→G5 + re-valid T1. Session compactée pour reprise dédiée G3. | P6 / G1-G2 | `.backups/14b_P6_G1G2_checkpoint/` **(CRÉÉ)** |
| 2026-07-18 | **🏁 P5 RÉFLEXIONS VALIDÉE UTILISATEUR + CLÔTURE.** Cadrage validé (ciel HDRP + Planar Probe built-in + gating immergé + SSR reporté). Phase surtout INTÉGRATION HDRP (peu de HLSL) : surface Lit deferred → réflexion appliquée par HDRP. Livré : ciel monté+conservé (`Ocean Debug Sky.volumeprofile`) ; `OceanReflectionModule` réel (Planar Probe realtime au niveau d'eau + gating immergé prouvé par données) ; **asmdef océan → réf HDRP+Core ajoutée** (1ʳᵉ dépendance HDRP du runtime). Gates : ciel ✅, planar/objets locaux ✅ (**Q3.4 levé**, Fresnel-correct), gating immergé ✅ (données), SSR ✅ (aucun artefact, OFF en V1). **T1 laissé PROVISOIRE** (sous-marin V1.5) : coût planar = re-rendu de scène dépendant du contenu → mesure build non représentative sur scène vide, différée. AA (Q5.3) constaté, système P10. EditMode **19/19**. Piège récurrent reconfirmé : changement d'asmdef → compile différée (attente focus). Snapshot 13. Suivant = **P6 sous-marin**. | P5 / clôture | `.backups/13_P5_reflection_validated/` **(CRÉÉ)** |
| 2026-07-17b | **🏁 P4 ÉCUME VALIDÉE UTILISATEUR + CLÔTURE.** Cadrage validé (carte world-locked, 2 params, gates en 2 temps) → implémentation en 3 itérations majeures pilotées PAR LES DONNÉES : (1) correctif **P1 signe déplacement choppy** (silhouette inversée « bosses », J aux creux — autorisé & validé) ; (2) pivot **carte world-locked** (résolution découplée de Master Tile Length — demande utilisateur) fusionnant couverture + persistance ; (3) **amendement Q7.1** (J_total=1+Σs filtré 2 m, erf σ fixe — le littéral par-cascade/variance mesuré inapplicable en world-space, cf. `OCEAN_DECISIONS.md §A1`). 7 bugs corrigés (chronique + repères diagnostic : `OCEAN_TEST_P4.md`) dont 3 pièges Unity durables (sampler `sampler_`, dt hors Play, LOD implicite sur coordonnée déplacée) → `CLAUDE_MEMORY/PIEGES.md`. Tests **19/19** (+3 `OceanFoamTests`). État de scène validé conservé (mer formée 0.606, chop 1.2, seuil 0.7, fade 1.0, carte 1024²). Snapshot 11. Suivant = **P5 Réflexions**. | P4 / clôture | `.backups/11_P4_foam_validated/` **(CRÉÉ)** |
| 2026-07-17 | **🏁 P3 VALIDÉE UTILISATEUR (rendu couleur) + CLÔTURE.** Gate 0-bis re-passé via MCP (shader OK, EditMode **16/16**). Gates visuels (d)/(e) tenus sur banc temporaire (PBS + EV14, caméra vue de haut). Décisions session : (1) **rename `perceivedDepth`→`colorBuildup`** (+ `FormerlySerializedAs`, tooltip corrigé — l'ancien décrivait l'inverse) ; (2) exposition de `kUpwellingScale` en paramètre profil (`depthDensity`/« Depth Density ») **codée puis RETIRÉE** à la demande — reste constante statique 0.02 ; (3) **ancre Ia σ_G 0.041→0.07** (bleu moins cyan) sauvegardée ; (4) défauts profil `waterType=0`, `colorBuildup=15`. Nettoyage : bancs `__P3_GateEnv__` + `TEMP_P3_VisualBench` (objet + `.volumeprofile.asset`) supprimés, caméra/soleil restaurés, scène sauvée. **Snapshot `09_P3_absorption_validated/` (81 fichiers + MANIFEST + SHA-256).** Suivant = P4 (écume). ⚠ Look « bleu profond » final tributaire de P5/P6. | P3 / clôture | `.backups/09_P3_absorption_validated/` **(CRÉÉ)** |

---

## Documents Associés

- **OCEAN_DECISIONS.md** ⭐ : **table canonique 41/41 = source de RANG 1 des décisions** (dans le projet)
- **OCEAN_GUIDELINES.md** : cadrage (questionnaire + specs A–D Q1.1→Q5.3)
- **OCEAN_ROADMAP.md** : plan exécutif (phases P0→P10 détaillées, budget, tensions, arbitrages)
- **OCEAN_CADRAGE_STATUS.md** : **réduit à un pointeur** (2026-07-04) vers OCEAN_DECISIONS.md + OCEAN_ROADMAP.md
- **MEMORY.md** (session 4208ed27a3e8, `Library/…`) : miroir de travail — specs A–D Q6.1→Q13.3
