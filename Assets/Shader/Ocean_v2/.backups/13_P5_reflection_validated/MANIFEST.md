# MANIFEST — 13_P5_reflection_validated

**Phase :** P5 — Réflexions (module `reflection`)
**Statut :** ✅ VALIDÉ UTILISATEUR (gate visuel) — 2026-07-18
**Snapshot :** état de fin de phase P5, restaurable. 90 fichiers + HASHES.sha256.

## Contenu
`Profile/**` (dont asmdef + réf HDRP), `Shaders/**`, `Profiles/**`, `Tests/**`,
`OceanProfile.asset`, `Ocean Debug.unity`, `Ocean Debug Sky.volumeprofile`.

## Livré
- **Ciel réfléchi** : automatique (surface Lit deferred → hiérarchie de réflexion HDRP). Prérequis
  de scène = un Volume + Sky (monté et **conservé** : `Ocean Debug Sky.volumeprofile`, PBS + Exposure Fixed EV14).
- **Planar Reflection Probe HDRP built-in** (Q5.1, Q-P5.2) : `OceanReflectionModule` crée/positionne
  une sonde runtime au niveau d'eau (realtime EveryFrame), influence `influenceExtent` (défaut 200 m).
  **Dé-risque Q3.4 LEVÉ** : compat native avec la surface custom HLSL confirmée (cube-témoin réfléchi,
  Fresnel-correct — fort en rasant, subtil de dessus).
- **Gating immergé (§1.3)** : caméra principale sous le niveau d'eau → sonde `enabled=false`
  (pas de re-rendu). **Prouvé par données** (émergé=ON, immergé=OFF, ré-émergé=ON).
- **SSR (critère i)** : SSR global HDRP testé sur la surface custom → **aucun artefact** (pas de
  z-fighting/auto-réflexion). Compat notée pour V1.5 ; **reste OFF en V1** (Q5.1).

## Décisions / réserves
- **T1 = PROVISOIRE** : sous-marin **reste V1.5**. Le coût dominant (re-rendu de scène de la Planar
  Probe) dépend du CONTENU ; la scène de test est quasi-vide → mesure build non représentative. Mesure
  reflection + décision définitive T1 **différées à une scène représentative / re-validation post-P6**
  (conforme à la clause de re-validation T1 de ROADMAP §4).
- **AA (Q5.3)** : constaté (TAA/ghosting sur eau+écume animées) ; système AA-par-preset = **P10**.
- Résolution planar par preset qualité = **P10** (non exposée en P5).

## Modifs structurantes
- `Profile/Ombrage.OceanFeatures.asmdef` : **ajout des références HDRP + Core Runtime** (le module
  réflexion utilise `PlanarReflectionProbe`). L'océan est HDRP-only → dépendance cohérente.
- `OceanReflectionModule.cs` : stub P0 → implémentation réelle.

## Vérifications
Console 0 erreur (périmètre océan) ; EditMode **19/19** ; P1–P4 non touchés.

## Cleanup de session
Objets de test retirés (`__P5_TestMarker__`, `__P5_SSR_Test__`). Le root `Cube` appartient à
l'utilisateur (conservé). Ciel conservé sur demande.

## Dette mineure notée
`Ocean Debug Sky.volumeprofile` créé via `CreateAsset` avec extension `.volumeprofile` (warning Unity :
préférer `.asset` ; fonctionne, à renommer proprement si l'exception future arrive).

## Suivant
P6 — Dessus/dessous + sous-marin COMPLET (V1.5) : double-sided + ménisque + Snell + fog + god-rays +
absorption partagée + caustiques (P7). Slots `14`/`15`.
