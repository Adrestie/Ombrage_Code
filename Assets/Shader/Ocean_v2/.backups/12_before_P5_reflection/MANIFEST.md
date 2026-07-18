# MANIFEST — 12_before_P5_reflection

**Rôle :** snapshot AVANT-phase P5 (réflexions). État = **P4 validée intacte**.
**Rollback :** restaurer ce dossier si P5 échoue (boucle ROADMAP §1.2).

## Contenu
- `Profile/**`, `Shaders/**`, `Profiles/**`, `Tests/**`, `OceanProfile.asset`, `Ocean Debug.unity`.
- 88 fichiers (+ `HASHES.sha256`).

## Cadrage P5 acté (session 2026-07-18)
- Q5.1 : ciel (HDRP) + **Planar Reflection Probe HDRP built-in** en V1 ; SSR reporté V1.5+.
- Q5.2 : sonde sous-marine différée.
- Q5.3 : AA constaté en P5, système de presets AA = P10.
- Décisions session : (Q-P5.1) ciel monté dans la scène et **conservé** (pas de suppression) ;
  (Q-P5.2) Planar Probe **built-in HDRP** (plus optimisée qu'une RT maison) pilotée par le module
  `reflection` ; (Q-P5.3) SSR global **OFF** sur l'océan en V1, seulement testé ; (Q-P5.4) **gating
  immergé** implémenté dès P5 (planar OFF sous l'eau, prérequis modèle budget §1.3) ; (Q-P5.5)
  **mesure build reflection** (méthode gate 4 P2) avant décision définitive **T1**.
- Slots : `12` (avant) / `13` (validé).

## État de scène (validé P4, conservé)
oceanState 0.606 · choppiness 1.2 · masterTileLength 193 · gridExtent 100 · useTMA OFF ·
foam : jacobianThreshold 0.7 · foamFadeRate 1.0 · foamResolution 1024. Surface smoothness 0.948.
