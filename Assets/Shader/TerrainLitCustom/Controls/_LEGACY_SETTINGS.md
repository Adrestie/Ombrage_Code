# Sauvegarde des réglages legacy — `TerrainLitCustomSetup` + `TerrainDeformationManager`

> Généré avant la refonte modulaire (profil/contrôleur façon Volume HDRP).
> **But** : permettre la re-saisie manuelle des valeurs dans le nouveau `TerrainProfile`.
> Trois colonnes : **Défaut code** (valeur du champ dans le script), **Scènes** (valeur réellement
> sérialisée dans `OutdoorsScene.unity` ET `GrassTest.unity` — identiques sauf mention),
> **Preset DayNight** (`Assets/Scripts/DayNight/TerrainLitCustomSetup.preset`).
>
> Les tableaux per-layer utilisent l'indexation 0–7. Les `bool[8]` du fichier scène sont
> encodés en octets hex (ex. `0100010000000000` = `[1,0,1,0,0,0,0,0]`).

---

## Références d'assets (guids — à re-lier dans le nouveau système)

| Asset | guid | Note |
|---|---|---|
| Script `TerrainLitCustomSetup` | `5f482a4d48511184697d79ea69aafadb` | monolithe refondu |
| Script `TerrainDeformationManager` | `1ce159c7b3cc32d41b89beb1608d8fd2` | intégré comme module |
| Texture bruit vent (global ET détail) | `ba4c07d0b5dda824684282cc8dfdacbf` | probable `clouds-noise_base_4096.png` |
| `stampShader` | `6a7f5f8f74741184fb8be0d4076f74de` | `Hidden/TerrainStamp` |
| `fadeShader` | `b15ea24a39b1c764d8abd89244bfd4e0` | `Hidden/TerrainDeformFade` |

---

## Module POM (`_PARALLAX_OCCLUSION_MAPPING`)

| Champ | Défaut code | Scènes | Preset DayNight |
|---|---|---|---|
| `enablePOM` | false | **1** | 1 |
| `pomHeightScale` | 0.05 | **0.0352** | 0.0577 |
| `pomMinSteps` | 4 | **64** | 64 |
| `pomMaxSteps` | 32 | **128** | 128 |
| `pomDistanceFade` | 50 | **166.4** | 166.4 |
| `layerPOMEnabled[8]` | tous false | **[1,0,1,0,0,0,0,0]** | [1,0,1,0,0,0,0,0] |

## Module Tessellation / Displacement (`_TESSELLATION_DISPLACEMENT`)

| Champ | Défaut code | Scènes | Preset DayNight |
|---|---|---|---|
| `enableDisplacement` | false | **1** | 1 |
| `tessellationFactor` | 15 | **8** | 8 |
| `tessellationFalloff` | EaseInOut (0,1)→(1,0) | (0,1)→(1,0), tangentes 0, Pre/Post=2 | idem |
| `layerDisplacementEnabled[8]` | tous false | **[0,1,0,0,0,0,0,0]** | [0,1,0,0,0,0,0,0] |
| `displacementScale` | 0.1 | **0.5** | 0.5 |

## Runtime Deformation (intégré au module Deformation)

| Champ | Défaut code | Scènes | Preset DayNight |
|---|---|---|---|
| `deformationStrength` | 1.0 | **2** | 2 |

## Module Sand (`_SAND_MODE`)

| Champ | Défaut code | Scènes | Preset DayNight |
|---|---|---|---|
| `enableSandMode` | false | **1** | 1 |
| `layerSandEnabled[8]` | tous false | **[0,1,0,0,0,0,0,0]** | [0,1,0,0,0,0,0,0] |
| `sandGlitterIntensity` | 0.8 | **0.605** | 1 |
| `sandGlitterThreshold` | 0.97 | **0.9** | 0.99 |
| `sandGlitterScale` | 200 | **59** | 30 |
| `sandGlitterColor` | (1, 0.95, 0.8, 1) | (1,1,1,1) | (1,1,1,1) |
| `sandGlitterMaxDistance` | 80 | **100** | (absent → 80) |
| `sandGlitterFalloff` | EaseInOut (0,1)→(1,0) | (0,1)→(1,0) | (absent → défaut) |
| `sandRimPushUp` | 0.03 | **0.2** | 0.2 |
| `sandRippleScale` | 3.0 | **2.57** | 2.57 |
| `sandRippleStrength` | 0.15 | **0.095** | 0.095 |
| `sandOceanSpecPower` | 16 | **17.1** | 17.1 |
| `sandOceanSpecIntensity` | 0.3 | **0.171** | 0.643 |
| `sandFresnelPower` | 4 | **4.07** | 4.07 |
| `sandFresnelIntensity` | 0.3 | **0.674** | 0.674 |

## Module Wind (`_WIND_DISPLACEMENT`, gated sur Displacement)

| Champ | Défaut code | Scènes | Preset DayNight |
|---|---|---|---|
| `enableWindDisplacement` | false | **1** | (absent → false) |
| `windNoiseGlobalMap` | null | guid `ba4c07d0…` | (absent) |
| `windNoiseDetailMap` | null | guid `ba4c07d0…` | (absent) |
| `windNoiseGlobalTile` | 0.01 | **0.0025** | (absent → 0.01) |
| `windNoiseDetailTile` | 0.05 | **0.0025** | (absent → 0.05) |
| `windMinValue` | -0.01 | **0** | (absent → -0.01) |
| `windMaxValue` | 0.02 | **0.25** | (absent → 0.02) |
| `windGlobalOffsetDirection` | (0.3, 0.15) | (0.01, 0.01) | (absent) |
| `windDetailOffsetDirection` | (-0.1, 0.2) | (0.02, -0.01) | (absent) |
| `windPeriod` | 0.08 | **0.025** | (absent → 0.08) |
| `windDebugMode` | Off (0) | Off (0) | (absent → Off) |

## Module Grass Tint (`_GRASS_TINT`)

> **Non sérialisé dans les scènes ni le preset** (instances antérieures à l'ajout de la feature)
> → toutes les valeurs sont aux **défauts code** ci-dessous, feature **désactivée**.

| Champ | Défaut code |
|---|---|
| `enableGrassTint` | false |
| `layerGrassTintEnabled[8]` | tous false |
| `grassTintColor` | (0.25, 0.4, 0.15, 1) |
| `grassTintStrength` | 1.0 |
| `grassTintSmoothness` | 0.25 |
| `grassTintDistanceStart` | 30 |
| `grassTintDistanceFull` | 120 |
| `grassWaveNormalStrength` | 0.5 |
| `grassWaveLumStrength` | 0.15 |

## Debug

| Champ | Défaut code | Scènes | Preset DayNight |
|---|---|---|---|
| `debugLogDisplacement` | false | 0 | 1 |

---

## `TerrainDeformationManager` (→ module Deformation : tuning dans le SO, refs de scène sur le contrôleur)

| Champ | Défaut code | OutdoorsScene | GrassTest | Destination |
|---|---|---|---|---|
| `vehicleBody` | null | (réf véhicule) | null | **contrôleur** (ref scène) |
| `wheels[]` | 6× null | 5 roues | 5× null | **contrôleur** (ref scène) |
| `bufferWorldSize` | 40 | **150** | 150 | SO |
| `resolution` | 1024 | **2048** | 2048 | SO |
| `stepDistance` | 0.5 | 0.5 | 0.5 | SO |
| `maxStepDistance` | 2 | 2 | 2 | SO |
| `curveAngleThreshold` | 18 | 18 | 18 | SO |
| `wheelStampIntensity` | 0.15 | **1** | 1 | SO |
| `wheelStampRadiusMeters` | 0.15 | **0.212** | 0.212 | SO |
| `wheelContactDistance` | 0.3 | **1** | 1 | SO |
| `groundLayer` | ~0 (Everything) | Everything | Everything | SO |
| `raycastOriginOffset` | 0.5 | 0.5 | 0.5 | SO |
| `flipStampIntensity` | 0.4 | **1** | 1 | SO |
| `flipStampRadiusMeters` | 2 | **5** | 5 | SO |
| `flipStepDistance` | 0.5 | **0.1** | 0.1 | SO |
| `flipThreshold` | 0.1 | 0.1 | 0.1 | SO |
| `fadeSpeed` | 0.0 | **0.08** | 0.08 | SO |
| `diffusionStrength` | 0.4 | **0.05** | 0.05 | SO |
| `diffusionIterations` | 3 | **0** | 0 | SO |
| `maskResolution` | 512 | **1024** | 1024 | SO |
| `tessellationMaskPadding` | 2.5 | 2.5 | 2.5 | SO |
| `showDebugGizmos` | false | **1** | 1 | SO (ou contrôleur) |
| `stampShader` | null | `Hidden/TerrainStamp` | idem | **contrôleur** (ref asset) |
| `fadeShader` | null | `Hidden/TerrainDeformFade` | idem | **contrôleur** (ref asset) |

> NB : `GrassController` (scène GrassTest/OutdoorsScene) référence `deformManager` (le composant
> DeformationManager) via `deformManager: {fileID: ...}`. Après refonte, ce lien pointera vers le
> nouveau contrôleur (ou une API équivalente) — à re-câbler manuellement lors de la migration.
