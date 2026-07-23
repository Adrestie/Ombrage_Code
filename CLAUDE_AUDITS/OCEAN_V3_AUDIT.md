# AUDIT — Océan V3 (Water System HDRP customisé)

**Date :** 2026-07-23
**Périmètre :** modifications réelles vs HDRP 17.4.0 vanilla (commit d'embarquement `948b4f0`) + `Assets/Shader/Ocean_V3/`
**Objectif :** état des lieux exact + optimisations possibles sans dénaturer le rendu.
**Aucun code modifié par cet audit** (palier « modification de shader » = validation explicite requise, CLAUDE.md §1).

---

## 1. Périmètre constaté (source : diff Git, pas l'historique des intentions)

L'empreinte custom **actuelle** est très petite et bien contenue :

| Élément | Fichier | Nature |
|---|---|---|
| Fork HDRP (1 seul fichier de code modifié) | `Packages/.../Runtime/Lighting/VolumetricLighting/VolumetricLighting.compute:226-238` | Débranche les caustics froxel sous-marins (le cookie caustics projeté dans le volumétrique) ; **conserve** l'absorption en profondeur. Remplacé par le pass screen-space V3. |
| God-rays V3 | `Assets/Shader/Ocean_V3/OmbrageUnderwaterGodRays.shader` (214 l.) | Fullscreen raymarch : beam = courbure (Laplacien par différences finies) du gradient `_WaterAdditionalDataBuffer` du Water System. |
| Pass C# | `Assets/Shader/Ocean_V3/OmbrageUnderwaterGodRaysPass.cs` (152 l.) | CustomPass scripté ; `WaterSurface.SetGlobalTextures()` + push params + `DrawFullScreen`. |

**Vérifié :** le chantier foam V3 (`OmbrageWaterFoam.hlsl`, capture de hauteur, stamp, modifications de `SampleWaterSurface.hlsl` / `WaterLighting.compute` / `WaterUtilities.hlsl`) a été **intégralement reverté** au commit `19fb74f` — le diff net du package ne contient plus que `VolumetricLighting.compute`. Aucun code mort en arbre.

**Verdict global :** code propre, minimal, bien commenté (marqueur « Ombrage — » dans le fork = retrouvable par grep). Le point faible n'est pas la qualité mais le **coût GPU du raymarch** et deux détails de robustesse.

---

## 2. Analyse de coût du raymarch (le poste dominant)

Par pixel plein écran, par step (`steps` défaut 24, max 64) :
- `OceanBeamPattern` = 3 × `SampleWaterGradient` (différences finies X/Z) ;
- chaque `SampleWaterGradient` = 1 à 3 samples de `_WaterAdditionalDataBuffer` (1 par bande active).

Soit **jusqu'à 9 samples texture / step → ~216 samples / pixel** avec 24 steps et 3 bandes.
À 1440p (~3,7 M pixels) : **~800 M samples texture par frame**, plus le fait que le pass tourne **même caméra hors de l'eau** (le early-out est dans le fragment, après le draw fullscreen). Sur RTX 2060 c'est plusieurs ms — incompatible avec le budget océan 2–4 ms.

Redondance clé : `OceanBeamPattern(surfXZ)` est une **fonction 2D pure du plan XZ**, indépendante du pixel. Aujourd'hui chaque pixel de chaque step la recalcule par différences finies — des millions d'évaluations d'un champ qui pourrait être calculé **une fois par frame**.

---

## 3. Constats de robustesse / correction

### C1 — Le pass coûte plein pot au-dessus de l'eau
`Execute()` (`OmbrageUnderwaterGodRaysPass.cs:85-125`) fait `SetGlobalTextures()` + `DrawFullScreen` inconditionnellement ; le passthrough (`shader:169-170`) est décidé **par pixel**. Un jeu qui passe 95 % du temps émergé paie un fullscreen pass + une lecture pyramid pour rien.
→ Gate CPU : `if (ctx.hdCamera.camera.transform.position.y > waterSurface.transform.position.y) return;` — strictement équivalent au test du shader (même référence `_WaterLevel` plate), donc **zéro changement de rendu**.

### C2 — Composite par relecture de la color pyramid (risque de « scène périmée »)
`Frag` lit la scène via `CustomPassSampleCameraColor` (`shader:166`) puis écrit `scene + godRays` en `Blend Off`. En injection Before Post Process, cette fonction échantillonne `_ColorPyramidTexture` (`CustomPassSampling.hlsl:24`) — une **copie** de la couleur caméra dont la fraîcheur dépend du moment de génération de la pyramide : tout ce qui est rendu après (transparents tardifs, autres custom passes) serait écrasé par la copie.
→ Passer en **blend additif** (`Blend One One`, sortir `float4(godRays, 1)`) : supprime la lecture pyramid (perf) **et** la classe entière de bugs de composition. Le mode debug `showGodRaysOnly` a besoin de `Blend Off` → 2ᵉ passe dans le SubShader, sélectionnée par `shaderPassId`.

### C3 — Coordonnée pixel reconstruite à la main (casse en dynamic resolution)
`pixelCoord = uint2(uv * _UnderwaterCamPixelSize.xy)` (`shader:161`) avec une taille poussée depuis C# (`cam.pixelWidth/Height`). Sous RTHandles/dynamic res, la taille effective du buffer diffère → `LOAD_TEXTURE2D_X(_CameraDepthTexture, …)` peut lire à côté.
→ Utiliser `IN.positionCS.xy` (SV_Position = coordonnée pixel directe) et `_ScreenSize` (déjà fourni par HDRP). Supprime l'uniform `_UnderwaterCamPixelSize` et un aller-retour CPU. Zéro changement de rendu à résolution native, correction en dynamic res.

### C4 — Risque build : référence shader
`EnsureMaterial()` ne fait `Shader.Find` que sous `UNITY_EDITOR` (`Pass.cs:78-80`). En build, si `godRaysShader` n'est pas sérialisé dans la scène/volume → pass silencieusement mort (piège « strippé du build » déjà documenté dans PIEGES.md §Builds). À vérifier une fois dans la scène (le champ a bien un tooltip qui le dit — c'est un rappel, pas un bug).

### C5 — `FindSun()` rescanne la hiérarchie chaque frame en cas d'absence
`FindObjectsByType<Light>` (`Pass.cs:135`) est appelé à chaque `Execute` tant qu'aucun soleil n'est trouvé (le cache ne mémorise que le succès). Scène sans directionnelle = scan par frame.
→ Mémoriser aussi l'échec (re-scan périodique ou sur événement), ou exiger la référence.

### C6 — Fork HDRP : maintenance
Un seul fichier modifié, marqué par un commentaire « Ombrage — » : bon état. Deux rappels :
- un upgrade du package **écrasera** la modification silencieusement (les caustics froxel reviendraient en **double** avec le pass screen-space) ;
- garder la règle : toute modification du fork porte le marqueur « Ombrage — » (retrouvable par `grep -rn "Ombrage —" Packages/com.unity.render-pipelines.high-definition`), et la liste des fichiers forkés vit dans la mémoire océan.

---

## 4. Optimisations proposées (classées gain / effort, iso-rendu sauf mention)

### O1 — Gate CPU au-dessus de l'eau — *gain : ~100 % du coût émergé, effort : trivial*
Cf. C1. Aucun changement de rendu. **À faire en premier.**

### O2 — Précompute du beam pattern dans une RT world-locked — *gain : ~9× sur les samples du march, effort : moyen* ⭐ la « maligne »
Le champ `OceanBeamPattern(xz)` est 2D et partagé par tous les pixels/steps. Le calculer **une fois par frame** dans une petite RT (R16F, 256–512², couvrant `camera.xz ± (maxDistance + marge sunFollow)`, world-locked) via un blit/compute qui fait exactement le code actuel (3 taps de gradient + hash de cellule). Le march passe alors à **1 sample bilinéaire par step** au lieu de ~9.
Bonus rendu (amélioration, pas dénaturation) : générer les mips de cette RT et échantillonner avec un LOD croissant avec `t` → filtrage propre du pattern au loin = **moins d'aliasing/scintillement** des faisceaux distants que la version actuelle (qui sample toujours mip 0).
Points d'attention : ancrer la RT sur une grille monde (snap au texel) pour éviter tout « swimming » lors des déplacements caméra ; recalculée chaque frame donc pas de rémanence.

### O3 — March en demi-résolution + upsample depth-aware — *gain : ~4× sur les pixels, effort : moyen+*
Les god-rays sont basse fréquence ; c'est l'optimisation standard des light shafts. Rendre l'accumulation dans une RT ½ res (CustomPass buffer), upsampler en respectant les bords de profondeur, composer en additif. Combiné au jitter IGN existant, la qualité perçue est identique voire plus douce. À ne faire **qu'après mesure** de O1+O2 : si le budget est déjà tenu, éviter la complexité.

### O4 — Pas de march adaptatif — *gain : variable (fort sur géométrie proche), effort : faible*
Aujourd'hui `stepSize = maxDist / steps` : un pixel dont l'opaque est à 2 m paie autant de steps qu'un pixel ciel à 60 m, avec des steps minuscules sur-résolus. Passer à un **pas constant en mètres** (`stepSize = _GodRayMaxDist / _GodRaySteps`, nombre de steps = `ceil(maxDist / stepSize)` clampé) : même échantillonnage physique, gros raccourci sur les rayons courts. Ajouter deux early-outs :
- rayon plongeant depuis une caméra profonde : si `exp(-camDepthBelow * _BeamDepthFade)` est déjà négligeable et `rayDir.y <= 0`, la contribution totale est bornée par ce facteur → skip ;
- `distanceAtten` sous un epsilon → break.
Iso-rendu à epsilon près (choisir ~1e-3 de la dynamique accumulée).

### O5 — Blend additif (= C2) — *gain : 1 fetch pyramid en moins + robustesse, effort : faible*

### O6 — Micro (optionnel, après mesure)
- C5 (FindSun) ;
- ne pousser les 15 uniforms que quand ils changent (négligeable, ne le faire que si le profiler CPU le montre).

### E1 — Expérience (changement de rendu possible → gate visuel utilisateur)
`_WaterAdditionalDataBuffer.z` contient déjà le **Jacobien** par bande (`WaterSimulation.compute:205` : `float4(surfaceGradient, jacobian, jacobian)`). Le beam pourrait se sourcer sur `1 − J` (pincement des crêtes) au lieu du Laplacien par différences finies : 1 tap au lieu de 3 par échantillon, et un signal physiquement lié aux crêtes. **Mais** c'est une quantité différente (compression horizontale ≠ courbure de hauteur) → le look changerait. À tester uniquement comme variante visuelle, pas comme optimisation iso-rendu. Devient inutile si O2 est retenu (le précompute absorbe déjà le coût).

**Projection combinée (sous l'eau, 1440p, 24 steps, 3 bandes) :** O2 ≈ ÷9 sur les samples du march ; O2+O3 ≈ ÷30 ; O4 en plus sur les scènes avec géométrie proche. Émergé : O1 = coût nul. Ces chiffres sont des bornes théoriques — la seule autorité reste la **mesure GPU par marker en build** (PIEGES.md §Builds).

---

## 5. Ce qui n'a PAS besoin d'optimisation

- **`VolumetricLighting.compute` (fork)** : la modification est déjà une *réduction* de coût (suppression d'un sample caustics + refract/cross par voxel froxel sous l'eau). Propre, minimale, bien commentée. Ne pas y toucher.
- **`SetGlobalTextures()` par frame** : nécessaire (buffers de simulation re-créés/re-bindés par HDRP) et bon marché (bindings, pas de copie). Le seul gain est de ne pas l'appeler émergé (couvert par O1).
- **L'IGN jitter, le `SafeColor`, le clamp de steps** : garde-fous corrects, coût négligeable.

---

## 6. Plan d'implémentation proposé (cycle CLAUDE.md : une étape = un gate)

| Étape | Contenu | Risque | Validation |
|---|---|---|---|
| 1 | O1 + C3 + C5 (gate CPU, SV_Position, cache FindSun) | Quasi nul, iso-rendu | Compile + rendu identique sous l'eau, marker GPU ≈ 0 émergé |
| 2 | O5/C2 (blend additif, passe debug séparée) | Faible | Rendu identique (comparaison A/B), debug OK |
| 3 | O2 (RT beam pattern world-locked + mips) | Moyen (snap texel, couverture) | A/B visuel + marker GPU (attendu : ÷5 à ÷9 sur le pass) |
| 4 | O4 (pas constant + early-outs) | Faible | A/B visuel, marker |
| 5 | O3 (½ res) — **seulement si** la mesure post-étape 3 dépasse le budget | Moyen | Gate visuel utilisateur (upsample) + marker |

Chaque étape est indépendante et revertable ; aucune ne touche `VolumetricLighting.compute` ni le rendu de surface du Water System.
