# AUDIT — Océan V3 (Water System HDRP customisé)

**Révision 2 — 2026-07-23** (remplace la révision 1 du même jour)
**Périmètre :** modifications réelles vs HDRP 17.4.0 vanilla (commit d'embarquement `948b4f0`) + `Assets/Shader/Ocean_V3/`
**Objectif :** état des lieux exact + optimisations possibles sans dénaturer le rendu.
**Aucun code modifié par cet audit** (palier « modification de shader » = validation explicite requise, CLAUDE.md §1).

> **Pourquoi une révision 2.** Contre-vérification ligne à ligne de chaque proposition contre les
> sources HDRP (`HDRenderPipeline.RenderGraph.cs`, `WaterSimulation.compute`,
> `HDRenderPipeline.WaterSystem.Simulation.cs`, `SampleWaterSurface.hlsl`, `CustomPassSampling.hlsl`).
> Résultat : **O1 et O5 sont renforcés** (le composite actuel a un bug latent prouvé par l'ordre de
> frame, pas seulement un coût), **O4 est rétrogradé** (ses early-outs sont quasi inertes avec les
> valeurs par défaut actuelles — chiffres en §4), **E1 gagne une précondition** (Jacobien écrit
> seulement si la foam de simulation est active), et **O2 est confirmé** avec la géométrie exacte de
> l'empreinte à couvrir (elle est cisaillée par la direction de beam — détail en §4/O2).

---

## 1. Périmètre constaté (source : diff Git, pas l'historique des intentions)

L'empreinte custom **actuelle** est très petite et bien contenue :

| Élément | Fichier | Nature |
|---|---|---|
| Fork HDRP (1 seul fichier de code modifié) | `Packages/.../Runtime/Lighting/VolumetricLighting/VolumetricLighting.compute:226-238` | Débranche les caustics froxel sous-marins (le cookie caustics projeté dans le volumétrique) ; **conserve** l'absorption en profondeur. Remplacé par le pass screen-space V3. |
| God-rays V3 | `Assets/Shader/Ocean_V3/OmbrageUnderwaterGodRays.shader` (214 l.) | Fullscreen raymarch : beam = courbure (Laplacien par différences finies) du gradient `_WaterAdditionalDataBuffer` du Water System. |
| Pass C# | `Assets/Shader/Ocean_V3/OmbrageUnderwaterGodRaysPass.cs` (152 l.) | CustomPass scripté ; `WaterSurface.SetGlobalTextures()` + push params + `DrawFullScreen`. |

**Vérifié :** le chantier foam V3 (`OmbrageWaterFoam.hlsl`, capture de hauteur, stamp, modifications de `SampleWaterSurface.hlsl` / `WaterLighting.compute` / `WaterUtilities.hlsl`) a été **intégralement reverté** au commit `19fb74f` — le diff net du package ne contient plus que `VolumetricLighting.compute`. Aucun code mort en arbre. Marqueur « Ombrage — » présent dans le fork (grep-able), aucun autre fichier du package marqué.

**Vérifié (formule d'échantillonnage) :** `SampleBandGradient` (`shader:67-71` : `uv = posXZ * scaleOffset.x - scaleOffset.yz`) est **identique** au `TransformWaterUV` officiel (`SampleWaterSurface.hlsl:149-153`) avec `GetBandPatchData(bandIdx) = _BandN_ScaleOffset_AmplitudeMultiplier`, CB bindé en global par `SetGlobalTextures()` (`WaterSurface.cs:515-536`). Le portage est correct.

**Verdict global :** code propre, minimal, bien commenté. Les points faibles : le **coût GPU du raymarch**, et **un bug latent de composition** (§3/C2) que la révision 1 avait sous-estimé.

---

## 2. Analyse de coût du raymarch (le poste dominant)

Par pixel plein écran, par step (`steps` défaut 24, max 64) :
- `OceanBeamPattern` = 3 × `SampleWaterGradient` (différences finies X/Z) ;
- chaque `SampleWaterGradient` = 1 à 3 samples de `_WaterAdditionalDataBuffer` (1 par bande active).

Soit **jusqu'à 9 samples texture / step → ~216 samples / pixel** avec 24 steps et 3 bandes.
À 1440p (~3,7 M pixels) : **~800 M samples texture par frame**, plus le fait que le pass tourne **même caméra hors de l'eau** (le early-out est dans le fragment, après le draw fullscreen — `Execute()` ne teste jamais la hauteur caméra, `Pass.cs:85-125`). Sur RTX 2060 c'est plusieurs ms — incompatible avec le budget océan 2–4 ms.

Redondance clé : `OceanBeamPattern(surfXZ)` est une **fonction 2D pure du plan XZ** (vérifié : toutes ses entrées sont des uniforms ou `surfXZ` — gradient de bandes, `_BeamScale/_BeamGain/_BeamThreshold*`, hash de cellule `floor(surfXZ*_BeamScale*0.5)`). Aujourd'hui chaque pixel de chaque step la recalcule — des millions d'évaluations d'un champ calculable **une fois par frame**.

---

## 3. Constats de robustesse / correction

### C1 — Le pass coûte plein pot au-dessus de l'eau — ET y écrase l'image (voir C2)
`Execute()` fait `SetGlobalTextures()` + `DrawFullScreen` inconditionnellement ; le passthrough (`shader:169-170`) est décidé **par pixel** et **n'est pas un no-op** : il réécrit tout l'écran avec `float4(scene, 1.0)` où `scene` est la copie pyramid (voir C2). Gate CPU proposé : `if (ctx.hdCamera.camera.transform.position.y > waterSurface.transform.position.y) return;` — même référence plate que `_WaterLevel` (`Pass.cs:97`) et même source de position (`_WorldSpaceCameraPos` = position absolue caméra), donc décision **identique** au test du shader. Rien d'autre dans le projet ne dépend des globals poussés par `SetGlobalTextures()` (vérifié par grep sur `Assets/`), le skip est donc sans effet de bord.

### C2 — Composite par relecture de `_ColorPyramidTexture` : bug latent prouvé par l'ordre de frame
`Frag` lit la scène via `CustomPassSampleCameraColor` (`shader:166`), qui en injection Before Post Process échantillonne `_ColorPyramidTexture` (`CustomPassSampling.hlsl:24`), puis écrit `scene + godRays` en `Blend Off` → **remplace** le color buffer par la copie.

Ordre de frame vérifié (`HDRenderPipeline.RenderGraph.cs`) — ce que contient `_ColorPyramidTexture` au moment du pass (`:360`) dépend des frame settings :

1. **Distortion + Rough Distortion actifs** (`:301-314`) : une pyramide « distortion » est régénérée depuis le color buffer **après tous les transparents** → copie fraîche. Seul reste le risque d'écraser un autre CustomPass BeforePostProcess exécuté avant le nôtre.
2. **Sinon, si Refraction / SSR / SSGI actifs** (`:1826-1834`) : la dernière pyramide générée date de **l'intérieur** de la passe transparence — après le lighting de l'eau (`:1817`, la surface y est donc bien) mais **avant** les transparents réfractifs (`:1844`), les low-res transparents et les CustomPass BeforeTransparent. → le composite **efface ces éléments de l'image**, au-dessus comme en dessous de l'eau.
3. **Sinon** : `_ColorPyramidTexture` pointe la pyramide d'historique non régénérée (`:1806`) → contenu de la frame précédente = ghosting.

Le cas 2 est le plus probable en config courante. Conséquence concrète : tout transparent réfractif ou low-res disparaît de l'image dès que le pass est actif — **y compris caméra émergée** (via le passthrough C1). Si ce n'a pas encore été remarqué, c'est que la scène de test contient peu de transparents tardifs.
→ Correctif = **O5** (blend additif) : on n'a alors plus jamais besoin de lire la scène ; le problème disparaît structurellement, et C1 cesse d'écraser quoi que ce soit émergé.

### C3 — Coordonnée pixel reconstruite à la main (casse en dynamic resolution)
`pixelCoord = uint2(uv * _UnderwaterCamPixelSize.xy)` (`shader:161`) avec une taille poussée depuis C# (`cam.pixelWidth/Height`). Sous RTHandles/dynamic res, la taille effective du buffer diffère → `LOAD_TEXTURE2D_X(_CameraDepthTexture, …)` (`shader:173`) peut lire à côté. Deux usages seulement : le load de depth et le jitter IGN (`shader:126`).
→ Utiliser `IN.positionCS.xy` (SV_Position = coordonnée pixel directe) et `_ScreenSize` (fourni par `ShaderVariables.hlsl`, déjà inclus). Supprime l'uniform `_UnderwaterCamPixelSize`. Iso-rendu à résolution native, correction en dynamic res.

### C4 — Risque build : référence shader
`EnsureMaterial()` ne fait `Shader.Find` que sous `UNITY_EDITOR` (`Pass.cs:78-80`). En build, si `godRaysShader` n'est pas sérialisé dans la scène/volume → pass silencieusement mort (piège « strippé du build », PIEGES.md §Builds). Rappel de vérification, pas un bug.

### C5 — `FindSun()` rescanne la hiérarchie chaque frame en cas d'absence
`FindObjectsByType<Light>` (`Pass.cs:135`) est appelé à chaque `Execute` tant qu'aucun soleil n'est trouvé (le cache `_cachedSun` ne mémorise que le succès). → mémoriser aussi l'échec, ou exiger la référence.

### C6 — Fork HDRP : maintenance
Un seul fichier modifié, marqué « Ombrage — ». Deux rappels :
- un upgrade du package **écrasera** la modification silencieusement → les caustics froxel reviendraient **en superposition** des god-rays screen-space ;
- garder la règle : toute modification du fork porte le marqueur (retrouvable par `grep -rn "Ombrage" Packages/com.unity.render-pipelines.high-definition`), liste des fichiers forkés dans la mémoire océan.

---

## 4. Optimisations (classées gain / effort, iso-rendu sauf mention)

### O1 — Gate CPU au-dessus de l'eau — *gain : ~100 % du coût émergé, effort : trivial* — **confirmé, renforcé**
Cf. C1/C2 : non seulement le coût tombe à zéro émergé, mais on supprime la réécriture de l'écran depuis la pyramid (iso-rendu dans le cas 1 de C2, **correction de rendu** dans les cas 2/3). **À faire en premier.**

### O2 — Précompute du beam pattern dans une RT world-locked — *gain : ~9× sur les samples du march, effort : moyen* ⭐ — **confirmé, avec géométrie précisée**
`OceanBeamPattern(xz)` est bakeable tel quel (fonction 2D pure, §2 ; formule UV identique au natif, §1). Le baker une fois par frame dans une RT R16F et faire du march **1 sample bilinéaire par step** au lieu de ~9.

**Géométrie de couverture (le point délicat, vérifié dans le shader) :** le pattern est lu en `surfaceHit.xz = sampleAWS.xz − (beamDir.xz / −beamDir.y) · depthBelow` (`shader:135-139`). L'empreinte à couvrir n'est donc pas `caméra ± maxDistance` mais ce rectangle **décalé le long d'une direction fixe par frame** (beamDir est uniforme). Bornes exactes calculables CPU :
- cisaillement `k = |beamDir.xz| / (−beamDir.y)` ; `beamDir.y` clampé ≤ −0.1 (`shader:123`) → k ≤ ~9.95, **mais** `horizonFade = smoothstep(0.1, 0.4, −beamDir.y)` (`shader:149`) annule la contribution sous −0.1 et ne devient pleine qu'à −0.4 (k ≤ 2.29) ;
- profondeur utile bornée par `surfaceProximity = exp(−depthBelow·_BeamDepthFade)` : avec le défaut 0.05, négligeable (<1e-3) au-delà de ~138 m, et par construction `depthBelow ≤ camDepth + maxDist`.
→ Empreinte réaliste ≈ rect 2·maxDist + bande de ~2.3 × min(camDepth+maxDist, 138) m dans la direction du beam (≈ 300 m en scénario profond). RT 512–1024², texel visé ≈ 0.5 m (eps de différences finies = 1/`_BeamScale` = 1 m au défaut ; cellules de hash = 2 m). Ancrage snappé au texel monde (anti-swimming). Coût du bake : ~9 taps × ≤1 M texels, soit ~2 ordres de grandeur sous le coût actuel du march plein écran.

**Bonus rendu** (amélioration, pas dénaturation) : mips sur la RT + `SampleLevel` avec LOD croissant en `t` → filtrage propre des faisceaux lointains (le code actuel sample toujours mip 0 → shimmer). Écart résiduel vs actuel : le hash de cellule devient filtré bilinéairement aux frontières — plus doux, gate visuel utilisateur.

### O3 — March en demi-résolution + upsample depth-aware — *gain : ~4× sur les pixels, effort : moyen+* — **confirmé, conditionnel**
Standard light-shafts, qualité perçue équivalente (signal basse fréquence + jitter IGN existant). À ne faire **qu'après mesure** de O1+O2 : si le budget est tenu, éviter la complexité.

### O4 — Pas de march adaptatif — *gain : dépendant de la scène, effort : faible* — **rétrogradé après vérification chiffrée**
Le vrai gain : `stepSize = maxDist/steps` (`shader:116`) sur-résout les rayons courts — un pixel avec géométrie à 2 m paie 24 steps comme un pixel ciel à 60 m. Passer à un **pas constant en mètres** (nombre de steps = `ceil(maxDist/stepSize)` clampé) : même somme de Riemann jitterée, iso-rendu statistique, gain réel seulement sur les pixels à géométrie proche (récifs, coques — rien en pleine eau).
**Honnêteté sur les early-outs proposés en rév. 1, aux défauts actuels** (`Pass.cs:32-41` : extinction 0.02, depthFade 0.05, maxDistance 60) :
- break sur `distanceAtten < ε` : `exp(−60×0.02) = 0.30` → **ne se déclenche jamais** dans la portée ;
- skip du pixel si caméra profonde et rayon plongeant (`exp(−camDepth×0.05) < ε`) : exige ~138 m de fond → **quasi jamais**.
Les garder comme garde-fous ne coûte rien, mais **ne pas en attendre de gain** avec ce tuning. O4 passe derrière O5 en priorité.

### O5 — Blend additif — *gain : 1 fetch pyramid en moins + correction du bug C2, effort : faible* — **confirmé, promu (c'est un correctif autant qu'une optimisation)**
`Blend One One`, sortir `float4(godRays, 1)` : plus aucune lecture de scène → les cas 2/3 de C2 deviennent sans objet, les transparents tardifs sont préservés, et le passthrough émergé devient inutile. Le mode debug `showGodRaysOnly` exige `Blend Off` → 2ᵉ passe dans le SubShader, sélectionnée par `shaderPassId` (déjà paramétrable dans `DrawFullScreen`, `Pass.cs:124`).

### O6 — Micro (optionnel, après mesure)
C5 (FindSun) ; push d'uniforms conditionnel (négligeable, seulement si le profiler CPU le montre).

### E1 — Expérience (changement de rendu possible → gate visuel utilisateur) — **précondition découverte**
`_WaterAdditionalDataBuffer.z` contient le **Jacobien** par bande (`WaterSimulation.compute:205`), utilisable comme source de beam (pincement des crêtes) en 1 tap au lieu de 3. **Mais** vérification du dispatch (`HDRenderPipeline.WaterSystem.Simulation.cs:488`) : le kernel `EvaluateNormalsJacobian` n'est choisi **que si `HasSimulationFoam()`** ; sinon `EvaluateNormals` laisse `.z = 0` → beam noir. E1 exige donc la foam de simulation active, produit un look différent (compression horizontale ≠ courbure de hauteur), et devient inutile si O2 est retenu. À classer curiosité, pas recommandation.

**Projection combinée (sous l'eau, 1440p, 24 steps, 3 bandes) :** O2 ≈ ÷9 sur les samples du march ; O2+O3 ≈ ÷30. Émergé : O1 = coût nul. Bornes théoriques — la seule autorité reste la **mesure GPU par marker en build** (PIEGES.md §Builds).

---

## 5. Ce qui n'a PAS besoin d'optimisation

- **`VolumetricLighting.compute` (fork)** : la modification est déjà une *réduction* de coût (suppression d'un sample caustics + refract/cross par voxel froxel sous l'eau) ; la variable `prof` conservée reste utilisée (absorption). Ne pas y toucher.
- **`SetGlobalTextures()` par frame** : nécessaire et bon marché (bindings, pas de copie — `WaterSurface.cs:515-536`). Seul gain : ne pas l'appeler émergé (couvert par O1).
- **L'IGN jitter, le `SafeColor`, le clamp de steps** : garde-fous corrects, coût négligeable.
- Note : `additionalDataBuffer` possède déjà des **mips générés par HDRP** (`Simulation.cs:495-496`) — inutilisables proprement avec les différences finies actuelles, mais cohérent avec la stratégie mips de O2.

---

## 6. Plan d'implémentation proposé (cycle CLAUDE.md : une étape = un gate)

| Étape | Contenu | Risque | Validation |
|---|---|---|---|
| 1 | O1 + C3 + C5 (gate CPU, SV_Position, cache FindSun) | Quasi nul | Compile + rendu identique sous l'eau, marker GPU ≈ 0 émergé |
| 2 | O5/C2 (blend additif, passe debug séparée) | Faible | A/B sous l'eau ; vérifier que les transparents tardifs réapparaissent |
| 3 | O2 (RT beam pattern world-locked + mips) | Moyen (couverture cisaillée, snap texel) | A/B visuel + marker GPU (attendu : ÷5 à ÷9 sur le pass) |
| 4 | O4 (pas constant en mètres) | Faible | A/B visuel, marker (gain visible seulement avec géométrie proche) |
| 5 | O3 (½ res) — **seulement si** la mesure post-étape 3 dépasse le budget | Moyen | Gate visuel utilisateur (upsample) + marker |

Chaque étape est indépendante et revertable ; aucune ne touche `VolumetricLighting.compute` ni le rendu de surface du Water System.
