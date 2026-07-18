# MANIFEST — 10_before_P4_foam

**Rôle :** snapshot AVANT-phase P4 (écume). État = **P3 validée intacte** (identique au contenu de
`09_P3_absorption_validated/`, re-capturé au setup P4 le 2026-07-17).
**Rollback :** restaurer ce dossier si P4 échoue (boucle ROADMAP §1.2).

## Contenu
- `Profile/**`, `Shaders/**`, `Profiles/**`, `Tests/**`, `OceanProfile.asset`, `Ocean Debug.unity`.
- 81 fichiers. Empreintes : `HASHES.sha256`.

## Cadrage P4 acté (session 2026-07-17)
- Q7.1 : Jacobien pré-filtré Dupuy (μ/σ² en mips, seuil doux erf), **1 param `jacobianThreshold`**.
- Q7.2 : crêtes seules. Q7.3 : persistance RT ping-pong, **1 param `foamFadeRate`** (s⁻¹, décroissance exp(−r·dt)).
- Architecture : `OceanFoam.compute` séparé (P1 byte-à-byte intact) ; arrays moments (J, J²) RGHalf mippés
  par groupe 512/256 ; **RT persistance R16F 1024² world-locked FIXE** alignée grille P2 (toroïdal différé
  au pivot clipmap) ; consommation surface = branche uniforme `_OceanFoamEnabled` (pattern P3).
- Foam = feature du module `surface` (Q12.4) : `OceanFoamFeature.cs` détenu par le module, PAS un module.
- Validation **en deux temps** : gate intermédiaire couverture instantanée (sans persistance), puis persistance.
- Slots : `10` (avant) / `11` (validé).
