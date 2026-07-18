# OCEAN_TEST_P5.md — Protocole de validation (Phase P5 — Réflexions)

> **P5 livre le module `reflection`** (Q5.1) : le **ciel** est réfléchi automatiquement par HDRP
> (surface Lit deferred → hiérarchie de réflexion) ; ce module ajoute une **Planar Reflection Probe
> HDRP built-in** au niveau d'eau (objets locaux) + le **gating immergé**. SSR reporté V1.5 (Q5.1),
> sonde sous-marine différée (Q5.2). **✅ VALIDÉE UTILISATEUR le 2026-07-18.**

## Nature de la phase
Contrairement à P1–P4 (systèmes GPU custom), P5 est surtout de l'**intégration HDRP** : la surface
étant un Lit deferred standard, HDRP lui applique la réflexion sans shader custom. Le code = un
**contrôleur** (`OceanReflectionModule`) : sonde planar + gating. Ajout de la **référence HDRP** à
l'asmdef océan (première dépendance HDRP du code runtime).

## Architecture livrée
- **Ciel** : HDRP (ambient/sky reflection probe). Prérequis scène = Volume + Sky
  (`Ocean Debug Sky.volumeprofile` : PBS + Exposure Fixed EV14, conservé).
- **Planar Probe** : `OceanReflectionModule` crée un GameObject runtime `OceanPlanarReflection
  (runtime)` (HideAndDontSave) enfant du système, `PlanarReflectionProbe` HDRP, mode Realtime/
  EveryFrame, plan miroir = plan d'eau (position = niveau d'eau, rotation neutre), influence
  `influenceExtent × 2` en boxSize.
- **Gating immergé** : `Apply` teste `Camera.main.y < waterY` → `probe.enabled = !submerged`.
- Params exposés : `planarEnabled` (bool), `influenceExtent` (m). (Résolution/preset = P10.)

## (a) Gate 0-bis
Reimport asmdef + `OceanReflectionModule.cs` ; Console 0 erreur ; EditMode **19/19** (inchangé,
le module réflexion n'a pas de test dédié — validation par gate visuel + données).

## (b) Gates — validés 2026-07-18
1. **Ciel réfléchi** : la surface montre le ciel (fort en vue rasante par Fresnel). ✅
2. **Planar / objets locaux (dé-risque Q3.4)** : un objet-témoin au-dessus de l'eau apparaît dans la
   réflexion de la surface custom (Fresnel-correct : subtil de dessus, fort en rasant). ✅
3. **Gating immergé (§1.3)** — **par DONNÉES** : caméra émergée → `probe.enabled=True` ; caméra sous
   l'eau → `False` ; ré-émergée → `True`. ✅
4. **SSR (critère i)** : SSR global HDRP activé temporairement → **aucun artefact** (pas de z-fighting/
   auto-réflexion) sur la surface custom. Compat OK pour V1.5 ; **reste OFF en V1** (Q5.1). ✅

## (c) Décision T1 — PROVISOIRE (sous-marin reste V1.5)
Le frame émergé complet est désormais assemblé (spectre + surface+foam + absorption + reflection),
MAIS le coût dominant des réflexions = **le re-rendu de scène de la Planar Probe**, qui **dépend du
contenu de la scène**. La scène de test étant quasi-vide, une mesure build **sous-estime** ce coût et
n'est **pas décisive** pour T1. → **Décision : T1 reste provisoire, sous-marin = V1.5** ; la mesure
build reflection + la décision définitive sont **différées à une scène représentative** (ou à la
re-validation T1 post-P6, prévue ROADMAP §4). Aucun chiffre fabriqué.

## (d) AA (Q5.3)
Constaté : TAA sur eau + écume animées = risque de ghosting connu. Le système **AA-par-preset** est
livré en **P10** (`OceanQualityProfile`). Rien construit en P5.

## En cas d'échec
Rollback `.backups/12_before_P5_reflection/` (état P4 validée). Repères : pas de réflexion → pas de
ciel dans la scène (Volume+Sky requis) ; réflexion déformée → orientation du plan de la sonde ;
gating inopérant → `Camera.main` nulle ou niveau d'eau mal lu.

## Clôture
Snapshot `.backups/13_P5_reflection_validated/` + MANIFEST + SHA-256 (90 fichiers).
Suivant : **P6 — Dessus/dessous + sous-marin COMPLET** (V1.5), slots `14`/`15`.
