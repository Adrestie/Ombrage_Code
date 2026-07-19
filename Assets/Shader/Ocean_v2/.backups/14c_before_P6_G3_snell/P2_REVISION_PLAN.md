# P2_REVISION_PLAN.md — Révision du banc de validation P2 (point d'entrée unique de reprise)

> **Objet :** lever les objections du gardien du scope (verdict « revision_requise ») et des passes de
> revue successives **avant** de reprendre l'exécution des gates 1–4 de P2 (surface deferred).
> **Périmètre strict :** fiabilité du **banc de validation**. Aucune feature océan. Aucune décision clipmap
> T2 (seule la fiabilité des chiffres qui l'alimentent est en jeu). **Code de rendu figé (snapshot 05) intact.**

---

## Cible & environnement

- Cible matérielle de référence : **RTX 2060** (dev = RTX 3080 Ti).
- **Unity 6 / HDRP 17.4.0**, 100 % HLSL custom. Chemin HDRP résolu **dynamiquement** (`PackageInfo`).

---

## Mapping blocage → correctif → fichier

### A. Cinq points du verdict « revision_requise » (gardien du scope)

| # | Blocage | Correctif | Fichier(s) |
|---|---|---|---|
| 1 (bloquant) | **Scène `.unity` construite en YAML manuel** (fileID/GUID) — fragile | Fabrication par **editor-script one-shot** (`NewScene`+`SaveScene`), MCP CoplayDev en repli, **jamais** de YAML | `Assets/Editor/Ocean/OceanP2GateSceneBuilder.cs` → `Tests/Scenes/OceanP2Gate.unity` |
| 2 | **Mesure budget non annotée** éditeur vs build ; risque de trancher T2 sur une valeur éditeur seule | Protocole annoté **delta `MeshRenderer.enabled` ON/OFF**, table **éditeur (Play, GPU) + build (RTX 2060)**, consigne « jamais sur une seule valeur éditeur » | `OCEAN_TEST_P2.md` §(i)/(i-bis)/(j) + table budget |
| 3 | **Convention de snapshots** floue (risque d'écraser le slot 05) | **05 immuable** ; **CRÉER** `06_before_P2_gates` / `07_P2_gates_validated` ; jamais de renommage | `OCEAN_IMPLEMENTATION_STATUS.md` §Convention ; `.backups/06_before_P2_gates/MANIFEST.md` |
| 4 | **Recompilation shader** non formalisée avant les gates | **Gate 0-bis** (réimport 4 shaders + domain reload + Console vide + EditMode 12/12) avant tout gate play-mode | `OCEAN_TEST_P2.md` §(a-bis) |
| 5 | **Chemin HDRP** non résolu dynamiquement | Helper `PackageInfo.FindForPackageName` (repli glob + `package.json`), vérif version 17.4.0 | `Assets/Editor/Ocean/OceanHdrpPath.cs` |

### B. Deux blocages de la revue antérieure

| Blocage | Correctif | Fichier(s) |
|---|---|---|
| **Profil Spectrum-only = faux négatif** (surface jamais créée) / profil racine 7 modules = delta pollué | **Profil de gate dédié** = EXACTEMENT 2 modules actifs (Spectrum+Surface) | `OceanP2GateProfileBuilder.cs` → `Tests/OceanP2Gate.profile.asset` |
| **`Setup()` avale silencieusement un module absent** | **Assertion POSITIVE** au build (profil) ET au démarrage (`Verify Surface Runtime Present`) | `OceanP2GateProfileBuilder.cs`, `OceanP2GateSceneBuilder.cs`, `OceanSurfaceRendererToggle.cs` |

### C. Deux blocages edge-case Rév.3

| Blocage | Correctif | Fichier(s) |
|---|---|---|
| **Mesure via flag `active`** → surface reste dans le GBuffer → delta ~0 **FAUX** | Delta via **`MeshRenderer.enabled`** + condition binaire FrameDebugger (draw call disparu) | `OceanSurfaceRendererToggle.cs`, `OCEAN_TEST_P2.md` §(i) |
| **`NewScene` destructif** (remplace la scène ouverte sans prompt) | **`SaveCurrentModifiedScenesIfUserWantsTo()`** + log `isDirty` **avant** `NewScene` ; abort si annulé | `OceanP2GateSceneBuilder.cs` |

### D. Deux blocages Rév.4

| Blocage | Correctif | Fichier(s) |
|---|---|---|
| **Environnement de lumière non déterministe** : Volume sans profil + APV non bakée → ambient/réflexion **noirs** → gate 1 « eau noire » faux négatif + Deferred Lighting non déterministe (auto-exposition) | **VolumeProfile de gate** : **GradientSky** actif + ambient **Dynamic** + **Exposure Fixed** + Fog off ; **APV retirée** ; Volume **global** câblé + assertion | `OceanP2GateEnvBuilder.cs`, `OceanP2GateSceneBuilder.cs`, `OCEAN_TEST_P2.md` §(d) |
| **Toggle/vérif child runtime inopérants** : nom réel `OceanSurface (runtime)` (≠ `OceanSurface`) + `HideAndDontSave` (invisible/non sélectionnable) → `Transform.Find` = null + repli manuel impossible | **`GetComponentsInChildren<MeshRenderer>(includeInactive:true)`** + filtre nom exact ; **`Transform.Find` banni** ; repli = **corroboration 2 profils** + FrameDebugger | `OceanSurfaceRendererToggle.cs`, `OCEAN_TEST_P2.md` §(i) |

### E. Recommandations du panel intégrées (non bloquantes)

- **GradientSky** (vs PhysicallyBasedSky) : déterministe, indépendant de l'angle solaire, **sans prérequis
  HDRP Asset** → ferme le sous-cas « soleil sous l'horizon = ciel noir ».
- **`skyAmbientMode = Dynamic`** : ambient du ciel appliqué sans bake.
- **`CreateAsset` avant `Add<T>()`** : les VolumeComponent sont persistés comme sous-assets.
- **Caméra TAA + Post-processing** : sinon le critère « pas de ghosting » du gate 2 est **vacuous**.
- **Vérif `HDRenderPipelineAsset.currentPlatformRenderPipelineSettings.supportMotionVectors`** : LogError explicite sinon.
- **Exposition calibrée** : critère gate 1 reformulé (« réflexion de ciel plausible + gradient visible »,
  ni noir ni blanc saturé) ; `fixedExposure` documenté et tunable une fois.
- **Alignement des chaînes de mesure internes** (`OceanProfiler.Readout` / `OceanSystem`) sur
  `MeshRenderer.enabled` — supprime la discordance doc interne/externe.

---

## Décisions actées (questions ouvertes du brief)

- **Snapshots :** créer **06_before_P2_gates** + **07_P2_gates_validated** ; **05 préservé** (ni renommé ni écrasé).
- **Mesure budget :** **delta `MeshRenderer.enabled` (GPU éditeur)** en primaire + **corroboration 2 profils** +
  **au moins une lecture build (RTX 2060)** ; T2 jamais tranché sur une seule valeur éditeur.
- **Scène / profil / env :** **editor-script prioritaire** (déterministe, versionné), MCP en repli.
- **Environnement :** **GradientSky déterministe** (ambient Dynamic, Exposure Fixed), **sans APV**.
- **Point 1 (bloquant) traité en priorité** : la fiabilité de la scène conditionne tout le banc.

---

## Séquence de reprise (point d'entrée unique)

1. **Compiler** l'outillage (`Assets/Editor/Ocean/`) → Console 0 erreur.
2. **Capturer** le snapshot `06_before_P2_gates/` (copie `Profile/**` + `Shaders/**` + `Assets/Editor/Ocean/**`
   + `Tests/**` + SHA-1) — cf. son MANIFEST.
3. **`Ombrage/Ocean/Build P2 Gate Scene`** → assets env + profil + scène ; confirmation utilisateur
   **0 « Missing Script »** + log `Spectrum+Surface actifs ; Sky+ExposureFixed`.
4. **Gate 0-bis** (`OCEAN_TEST_P2.md` §a-bis) : recompile shaders + EditMode **12/12** + `Verify Surface Runtime Present`.
5. **Gate 1** rendu deferred/éclairage (§d, env chargé) → **Gate 2** MV (§f) → **Gate 3** picking/bounds (§g3/h)
   → **Gate 4** budget (§i–k : delta `MeshRenderer.enabled`, draw call confirmé disparu, corroboration 2 profils,
   exposition Fixed, table éditeur+build).
6. **Snapshot `07_P2_gates_validated/`** + table budget RTX 2060 remplie. Aucune décision clipmap ici.

---

## Hors scope (rappel)

- Nouvelles features océan au-delà de la validation P2 (surface deferred).
- Le **pivot clipmap T2** lui-même (non décidé ici) — seule la **fiabilité des chiffres** est en jeu.
