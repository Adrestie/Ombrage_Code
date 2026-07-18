# MANIFEST — 11_P4_foam_validated

**Rôle :** snapshot APRÈS-phase P4 (écume) — **état P4 VALIDÉE utilisateur** (gate visuel 2026-07-17 ;
gate 0-bis re-confirmé 2026-07-18 : EditMode **19/19**, Console 0/0). Point de restauration d'une
étape validée (CLAUDE.md §10 / Q13.3).
**Rollback :** restaurer ce dossier pour revenir à l'état P4 validée (si P5 échoue).

> Créé le **2026-07-18** : le snapshot était référencé par STATUS/TEST_P4/OCEAN.md depuis le
> 2026-07-17 mais absent du disque (classe « backup fantôme ») — matérialisé à la confirmation
> du gate 0-bis, présence disque vérifiée.

## Contenu
- `Profile/**`, `Shaders/**`, `Profiles/**`, `Tests/**`, `OceanProfile.asset`, `Ocean Debug.unity`
  (périmètre miroir de `09`/`10` — les docs de chronique `OCEAN_*.md` restent hors rollback).
- **87 fichiers** (81 du périmètre P3 + 6 nouveaux P4 : `OceanFoam.compute`, `OceanFoamFeature.cs`,
  `OceanFoamTests.cs` + metas). Empreintes : `HASHES.sha256`.

## P4 livrée (référence — détail : `OCEAN_TEST_P4.md`, décision : `OCEAN_DECISIONS.md §A1`)
- Écume = **carte world-locked** (couverture crêtes Q7.2 + persistance Q7.3 en 1 RT RHalf ping-pong
  mippée, `foamResolution` découplée de la longueur de tuile).
- **Q7.1 AMENDÉE** (rang 1, validée au gate) : J_total ≈ 1 + Σ(Jxx+Jzz) filtré à échelle physique
  fixe 2 m, erf à σ fixe, AA distance par mips de la carte — le littéral Dupuy per-cascade/footprint
  est mesuré inapplicable en carte world-space.
- Surface : échantillonnage position NON-déplacée (q ≈ p − D(p)), LOD explicite par distance,
  rupture procédurale 2 échelles. Params : `jacobianThreshold` · `foamFadeRate` · `foamResolution`.
- Correctifs transverses P1 : signe du déplacement choppy (silhouette crêtes/creux) ; contrat `.w`
  = s = Jxx+Jzz (ex-déterminant). 7 bugs corrigés en chronique.

## Suivant
**P5 — Réflexions** (slots `12` avant / `13` validé).
