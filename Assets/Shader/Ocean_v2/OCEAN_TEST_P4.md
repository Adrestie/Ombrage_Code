# OCEAN_TEST_P4.md — Protocole de validation (Phase P4 — Écume)

> **P4 livre la feature `foam`** (Q7.1 amendé / Q7.2 / Q7.3) : carte d'écume **WORLD-LOCKED**
> (couverture crêtes + persistance dans une seule RT ping-pong mippée), résolution **découplée**
> de la longueur de tuile, consommée par la surface en décal (LOD explicite par distance).
> **✅ VALIDÉE UTILISATEUR le 2026-07-17** (gate visuel : crêtes suivies, bords organiques,
> traînée qui se dissout). Budget : agrégé à `surface` (marker `Ocean.Foam`) — pas de gate build
> à P4 ; re-contrôle total-frame à T1 (P5/P6).

## Architecture livrée (référence)

```
P1 (intact, .w = s = Jxx+Jzz) ─► [1] ExtractMoments : (s, s²) par cascade, arrays RGHalf mippés
                                  [2] FoamAccumulate : J_total ≈ 1 + Σᵢ E[sᵢ]@2m (filtre physique),
                                      erf doux (σ fixe), + persistance max(prev·exp(−fade·dt_réel), cov)
                                      → carte _OceanFoam (RHalf, ping-pong, mips)
Surface : échantillonne _OceanFoam à la position NON-DÉPLACÉE (q ≈ p − D(p)), LOD par distance
          caméra ; rupture procédurale 2 échelles (anti-aplat) ; albédo/rugosité constantes.
```

**Paramètres exposés (module Surface)** : `foamEnabled` · `jacobianThreshold` [0.5..1.05]
(≈0.5 = plis rares → ≈0.85 = généreux ; à mer formée, **0.7 ≈ 4 % de couverture**) ·
`foamFadeRate` (s⁻¹) · `foamResolution` [256..2048].

## Amendement Q7.1 (décision de rang 1 — validé au gate)

Le **littéral** Q7.1 (couverture par cascade + variance Dupuy au footprint) a été **mesuré
inapplicable** dans l'architecture carte world-locked validée :
- déterminant PAR cascade : la cascade fine a J∈[0.08..2.6] → union ≈ 1 partout (mesuré) ;
- variance au footprint d'un texel de carte world-space : dégénérée (texel fin → var=0 →
  binaire/speckle ; texel gros → var≫ → gris ~40 % uniforme — mesuré aux deux régimes).

**Retenu (état de l'art jeu, esprit Dupuy conservé)** : moments de la **divergence linéaire**
`s=Jxx+Jzz` (sommable inter-cascades, mippable = pré-filtrage linéaire), **J_total** filtré à une
**échelle physique fixe** (`kFoamFilterScale = 2 m`), seuil doux erf à σ fixe, AA distance par les
**mips de la carte**. Consigné dans `OCEAN_DECISIONS.md §Amendements`.

## Bugs corrigés pendant la phase (chronique d'audit — tout mesuré)

| # | Bug | Correctif |
|---|---|---|
| 1 | **Signe du déplacement choppy P1** : creux affûtés au lieu des crêtes (silhouette inversée, J bas aux creux — corr(J,h)=+0.99) | `Dx/Dz = +i·k̂·h` + Jacobien suivi (`OceanSpectrum.compute`) → corr = −0.99, silhouette validée |
| 2 | Persistance jamais dissipée hors Play (`Time.deltaTime`≈0 en édition → fossile d'écume) | horloge réelle interne à la feature (`realtimeSinceStartupAsDouble`, clamp 0.1 s) |
| 3 | Sampler compute invalide (préfixe `sampler_` réservé par Unity) → moments lus = 0 → couverture 1 partout | lecture `Load` + **bilinéaire manuel** (zéro sampler) |
| 4 | Déterminant par cascade + variance world-space (voir Amendement) | J_total filtré 2 m, σ fixe |
| 5 | Écume décalée des crêtes (« ne suit pas les vagues ») : carte indexée non-déplacé, lue au déplacé | inversion 1ᵉʳ ordre `q ≈ p − D(p)` à l'échantillonnage |
| 6 | Paliers durs en parallélogramme : LOD implicite pollué par les dérivées de la coordonnée inversée | **LOD explicite par distance caméra** (`SampleOceanFoamCoverage(worldXZ, viewDist)`) |
| 7 | Aplats blancs uniformes | rupture procédurale 2 échelles (0.6 m + 0.17 m), monde non-déplacé, constantes |

## (a) Gate 0-bis — recompilation
Reimport `OceanSpectrum.compute` + `OceanFoam.compute` + `OceanSurfaceCascadeSampling.hlsl` +
`OceanSurfaceData.hlsl` + `OceanSurface.shader` ; Console **0/0** ; EditMode **19/19**
(16 + 3 `OceanFoamTests` : push non-cumulatif/restore, no-op sûr, Dispose idempotent).

## (b) Gates visuels (✅ passés 2026-07-17)
1. **Position** : écume **sur les crêtes**, qui les **suit** (mer formée `oceanState`≈0.6+).
2. **Aspect** : bords organiques déchiquetés, pas d'aplat uniforme, pas de paliers géométriques.
3. **Seuil live** : ~0.5 = plis rares · 0.7 = crêtes (≈4 %) · >0.9 = nappe (trop).
4. **Traînée** : `foamFadeRate` 0.3 ↔ 2 → traînée longue ↔ brève ; **se dissipe hors Play aussi**.
5. **Découplage** : changer `Master Tile Length` ne change PAS le grain de l'écume ;
   `Foam Resolution` 256↔2048 le change.
6. **Mer calme** (`oceanState` bas) : PAS d'écume (crêtes seules, Q7.2 — pas de voile ambiant).

## (c) Repli / toggle
`foamEnabled` OFF → surface sans écume dès la frame suivante (branche uniforme, 0 variant) ;
OceanSystem OFF/ON → globals restaurés neutres puis re-poussés (anti-bug n°1, test exécutable).

## (d) Non-régression
P1 (spectre) : contrat `.w` **CHANGÉ** (s = Jxx+Jzz remplace le déterminant — consommateur unique :
l'écume ; le debug Mode=3 affiche désormais s). MV/tessellation P2 et absorption P3 non touchés.
Aller-retour presets `Ultra→Low→Ultra` : Console vide, pas de traînée MV.

## En cas d'échec
Rollback `.backups/10_before_P4_foam/` (état P3 validée). Repères : écume partout → seuil trop
haut OU moments à zéro (vérifier extraction) ; écume figée → dt (fossile) ; écume décalée →
inversion de déplacement ; paliers durs → LOD implicite réintroduit.

## Clôture
Snapshot `.backups/11_P4_foam_validated/` + MANIFEST (validé utilisateur 2026-07-17).
**✅ CLÔTURE MATÉRIALISÉE le 2026-07-18** : gate (a) re-confirmé (EditMode **19/19** utilisateur,
Console 0/0 vérifiée MCP) ; snapshot **créé + vérifié disque** (87 fichiers + MANIFEST + SHA-256 —
il était référencé depuis le 2026-07-17 mais absent du disque : backup fantôme corrigé).
Suivant : **P5 — Réflexions** (slots `12`/`13`).
