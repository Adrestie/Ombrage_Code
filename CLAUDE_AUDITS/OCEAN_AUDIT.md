# AUDIT — Système d'Océan (Ombrage)

**Révision 2 — 2026-06-28** (remplace la révision 1 du même jour)
**Périmètre :** `Assets/Shader/Ocean/` + dépendances suivies par GUID
**Objectif :** Audit exhaustif des choix de design, pièges et solutions, en lecture seule, avant refonte from scratch. **Aucun code modifié.**

> **Pourquoi une révision 2.** La révision 1 avait été produite à partir de la liste d'exploration (incomplète) et non d'une lecture intégrale de chaque fichier. Cette révision 2 repart de l'énumération RÉELLE du dossier et de la lecture intégrale des 23 fichiers source. Elle **corrige plusieurs constats faux** de la révision 1 :
> 1. Le « JONSWAP » n'en est pas un (c'est Phillips × γ — cœur du spectre absent).
> 2. Les normales ne viennent **pas** du Jacobien mais de **différences finies** sur la hauteur.
> 3. La normalisation IFFT est `1/N` (pas `1/N²`), couplée en cachette à `_Amplitude`.
> 4. Le constat « zéro preset » est **faux** : 7 presets Beaufort complets existent et sont branchés (mais ne couvrent que ~24 % des paramètres).
> 5. La doc/tooltips ne sont **pas** « minimaux » : ~110 tooltips pour ~110 champs ; la douleur de réglage vient d'ailleurs (nombre + interdépendances + presets partiels + 0 clamp).
> 6. Plusieurs sous-systèmes sont **présents mais non câblés** en scène (voir §4 et §2.4).

---

## 1. ENVIRONNEMENT & PÉRIMÈTRE CONFIRMÉ

*(Clôt les questions ouvertes n°1 et n°2 du brief.)*

| Élément | Valeur confirmée | Source |
|---|---|---|
| Moteur | Unity 6 (`com.unity.entities.graphics 6.4.0`) | `Packages/manifest.json` |
| Pipeline | **HDRP 17.4.0** | `manifest.json:14` |
| Shader Graph / VFX Graph | 17.4.0 | `manifest.json:15,19` |
| Nature du système | **100 % custom HLSL/C#** | aucune dépendance Crest/KWS/asset tiers trouvée dans le manifest ni dans le code |

- **Aucun asset tiers** (Crest, KWS, autre océan Asset Store) n'intervient : le système est entièrement maison.
- Aucune utilisation de l'API **HDRP Water System** built-in (pas de `WaterSurface`, pas de Water Decals) : c'est une implémentation FFT indépendante posée sur HDRP via shader Lit-custom + CustomPass.
- Dépendances externes réelles : `Terrain` API (heightmap rivage), `RenderSettings.sun` (fallback soleil), framework HDRP (LightLoop/Lit includes, CustomPass, Volume), compute shaders (DX11+/4.5).
- **Config par défaut effective** (`OceanSettings.asset`) : `resolution = 512`, `cascadeCount = 3`, `waterLevel = 191.17`, toggles `enableShoreWaves/enableUnderwater/enableUnderwaterLighting/usePlanarReflection/enableShoreIntersectionMap = 1`.

---

## 2. INVENTAIRE RÉEL & GRAPHE DE DÉPENDANCES

### 2.1 Inventaire (source de vérité = glob du dossier, pas l'exploration)

**23 fichiers source** + 3 assets, tous lus intégralement.

**Compute (7)** : `OceanInitSpectrum.compute`, `OceanTimeDependentSpectrum.compute`, `OceanFFT.compute`, `OceanPostProcess.compute`, `OceanFoamBlur.compute`, `OceanWakeTrail.compute`, `OceanShoreIntersection.compute`.

**Shaders (6)** : `OceanSurface.shader`, `UnderwaterEffect.shader`, `ShoreWaveEffect.shader`, `OceanWakeStamp.shader`, `OceanWakeFade.shader`, **`OceanDebugSpectrum.shader`**.

**Includes HLSL (4)** : `OceanInput.hlsl`, `OceanCaustics.hlsl`, `OceanTessellation.hlsl`, `UnderwaterInput.hlsl`.

**C# (7)** : `OceanSystem.cs` (1648 l.), `OceanSettings.cs` (697 l.), `UnderwaterPass.cs`, `UnderwaterLightingController.cs`, `ShoreWavePass.cs`, `Editor/OceanSettingsEditor.cs`, `Editor/BeaufortPresets.cs`.

**Assets (3)** : `OceanSettings.asset`, `OceanSurface.mat`, **`UnderwaterLightinh.asset`** (faute de frappe « Lightinh » dans le nom).

> **Diff exploration → réel.** L'exploration avait **manqué 2 fichiers** : `OceanDebugSpectrum.shader` et `UnderwaterLightinh.asset`. Tous deux sont présents et ont été lus ici. Par ailleurs, l'exploration marquait `OceanWakeFade/Stamp.shader`, `BeaufortPresets.cs`, `OceanSettingsEditor.cs` comme « inférés / non relus » : ils sont désormais **lus intégralement** (aucun constat « inféré » ne subsiste).

### 2.2 Table GUID → chemin (dépendances suivies par GUID, pas par nom)

| GUID | Chemin résolu | Type | Statut |
|---|---|---|---|
| c39d6066…afda7f | OceanSurface.shader | Shader | résolu |
| 549b02de…6421e9 | OceanSurface.mat | Material | résolu |
| 57128c3d…125302 | OceanSettings.asset | ScriptableObject | résolu |
| e5a5275b…6020c0 | OceanSettings.cs (`Ocean.OceanSettings`) | Script | résolu |
| 12cfe7be…71b96 | UnderwaterLightinh.asset | VolumeProfile (HDRP standard) | résolu |
| b42cf2bb…d1e9 | OceanSystem.cs | Script | résolu |
| b2a9998c…57fac | UnderwaterPass.cs | Script | résolu |
| 6cfd77cd…04692 | ShoreWavePass.cs | Script | résolu |
| fa6fe3d1…8eac9 | UnderwaterLightingController.cs | Script | résolu |
| 7f240a8d…f5141 | UnderwaterEffect.shader | Shader | résolu |
| 7cc109ba…9ebb2 | ShoreWaveEffect.shader | Shader | résolu |
| bef975a1…59ae7 | OceanShoreIntersection.compute | Compute | résolu |
| 3e401b39…3f0d5 | OceanInitSpectrum.compute | Compute | résolu |
| 54502e60…b75b65 | OceanTimeDependentSpectrum.compute | Compute | résolu |
| 6825b7f1…23e5d0 | OceanFFT.compute | Compute | résolu |
| 3ea1a0e0…4a1489 | OceanPostProcess.compute | Compute | résolu |
| e919c385…662903 | OceanFoamBlur.compute | Compute | résolu |
| 7adef583…68b | OceanWakeTrail.compute | Compute | résolu |
| e32692fa…0aadb | OceanDebugSpectrum.shader | Shader | résolu |
| fdffd32a…adb6 | Texture2D/Noises/128/Vein/Vein_14-128.png (`_CausticsTex`) | Texture | résolu **(hors dossier Ocean)** |
| 0888fbd2…99fec | Texture2D/Noises/128/Voronoi/Voronoi_03-128.png (`_FoamTexHigh`) | Texture | résolu **(hors dossier Ocean)** |
| 8c4ec38f…8a40bc | Texture2D/Noises/256/Turbulence/Turbulence_04-256.png (`_FoamTexLow`) | Texture | résolu **(hors dossier Ocean)** |

**Aucun GUID non résolu / cassé.** Les 3 textures du matériau sont des noises génériques de `Assets/Texture2D/Noises/` (pas d'authoring foam/caustics dédié → tiling de bruit générique). Les assets sont tous en **YAML texte lisible** (Force Text actif), aucun binaire.

### 2.3 Graphe de dépendances

```
OceanSettings.asset (Ocean.OceanSettings)  ← référencé par OceanSystem / UnderwaterPass / ShoreWavePass
├─ oceanMaterial → OceanSurface.mat → OceanSurface.shader
│                     ├─ _CausticsTex → Vein_14-128 (noise générique)
│                     ├─ _FoamTexHigh → Voronoi_03-128 (noise générique)
│                     └─ _FoamTexLow  → Turbulence_04-256 (noise générique)
├─ 7 compute shaders (init/time/fft/postproc/foamblur/wake/shore)
└─ fftDebugShader → OceanDebugSpectrum.shader

Globals poussés par OceanSystem (Shader.SetGlobal*) → consommés par :
   OceanSurface.shader, ShoreWaveEffect.shader, UnderwaterEffect.shader
   (textures cascades Disp Y/X/Z + Normal + Foam ×3, wake RT, shore RT, planar RT, caustics)

UnderwaterLightinh.asset = VolumeProfile HDRP standard (Exposure/WhiteBalance/
   ColorAdjustments/Bloom/Vignette, teinte froide) — AUCUNE dépendance vers l'océan.
```

### 2.4 Câblage en scène (CustomPass / Volumes / GameObjects)

Recherche par GUID **et** par nom de classe SerializeReference (les CustomPass sont référencés en `SerializeReference {class, ns}`, invisibles à un grep GUID seul).

| Sous-système | Câblé ? | Détail |
|---|---|---|
| **OceanSystem** (MonoBehaviour) | **OUI** | `OutdoorsScene.unity` (+ `_Recovery/0.unity`). Pointe sur `OceanSettings.asset` + `sunLight`. |
| **UnderwaterPass** (CustomPass) | **OUI mais le CustomPassVolume est `m_Enabled: 0`** | `OutdoorsScene.unity`, injectionPoint `BeforePostProcess`, global. Le pass lui-même `enabled:1` mais le **Volume porteur est désactivé** → l'effet sous-marin ne tourne pas sans activation. |
| **ShoreWavePass** (CustomPass) | **NON** | GUID présent seulement dans son `.meta`. Aucune scène ne l'installe. (`enableShoreWaves:1` dans les settings → intention activée, pass absent → usage à confirmer / instanciation runtime ?) |
| **UnderwaterLightingController** (MonoBehaviour) | **NON** | Aucun GameObject dans aucune scène. |
| **UnderwaterLightinh.asset** (VolumeProfile) | **NON (orphelin)** | Référencé par aucun Volume `sharedProfile`, aucune scène, aucun autre asset. Profil mort ou assigné runtime. |

> **Constat d'usage éditeur majeur :** une partie significative du système « complet » décrit par le code n'est **pas réellement active** dans le projet tel que sérialisé : underwater désactivé au niveau du Volume, shore waves et controller d'éclairage non installés, VolumeProfile sous-marin orphelin. L'écart entre « code présent » et « système câblé/testé » est important et doit être pris en compte avant de juger la maturité réelle de l'existant.

> **Note matériau :** `OceanSurface.mat` a **tous ses keywords en `m_InvalidKeywords`** (POM, SAND_MODE, TESSELLATION_DISPLACEMENT, USEPLANARREFLECTION_ON, WIND_DISPLACEMENT) — aucun keyword *valide* actif. À vérifier en éditeur : certaines features pourraient ne pas être compilées dans la variante effectivement utilisée. `_UsePlanarReflection:1` est pourtant un float actif.

---

## 3. ARCHITECTURE & FLUX DE DONNÉES

### 3.1 Ordre d'exécution réel (extrait de `OceanSystem.cs`)

```
Update() :                          LateUpdate() :
  UpdateFFT()                         MeshGenerateIfNeeded()
  UpdateShoreIntersection()           MeshSnapToCamera()  (mode FollowCamera)
  UpdateWake()                        | MeshApplyWaterLevel() (mode Static)
  UpdateMaterialParams()

beginCameraRendering (event séparé) : OnBeginCameraRendering() → planar reflection
```

### 3.2 Pipeline FFT (cœur)

```
[ONE-SHOT] InitSpectrum  → H₀(k)            (relancé si params spectre changent)
                 │
[PER-FRAME, boucle cascade c ∈ [0..cascadeCount)] :
   TimeDependentSpectrum(c) → Dy/Dx/Dz spectra (3 RT complexes RGFloat)
   IFFT 2D ×3 (Stockham)    → champs de déplacement spatiaux Y/X/Z
   PostProcess(c)           → NormalMap (.a = Jacobien) + Foam (ping-pong temporel)
   FoamBlur(c)              → flou gaussien séparable optionnel
   → FFTPushGlobalTextures() (toutes cascades poussées en globals)
```

- **IFFT** : Stockham auto-sort (pas de bit-reversal), twiddle `+i` (= IFFT), `2·log₂(N)` passes butterfly (horizontal puis vertical) + 1 `FinalPermute` par texture, ping-pong `_fftPing/_fftPong`.
- **Coût brut (512², 3 cascades)** : `_logN = 9` → par IFFT 2D = 18 butterfly + 1 permute + 1 blit. ×3 spectres ×3 cascades ≈ **~171 dispatchs IFFT + 9 blits**, plus time/postproc/foamblur. **De l'ordre de ~180 dispatchs GPU/frame** rien que pour la simulation, **sans aucun `ProfilerMarker`/`CommandBuffer`** (impossible de profiler par passe, aucun batching).
- **Formats RT** : H₀ `ARGBFloat` ; Disp Y/X/Z et spectres `RGFloat` ; Normal `ARGBFloat` ; Foam `RFloat` (ping-pong A/B par cascade) ; shore map `ARGBHalf` ; reflection `DefaultHDR` depth24. RT mises en cache (pas de réalloc en régime stable), libérées en OnDisable.

### 3.3 Sous-systèmes périphériques

- **Wake** : RT mono-canal `RFloat`, kernels Paint/BlurH/BlurV/Fade, piloté par position/vitesse/direction d'un `wakeTarget`. Full-grid par frame.
- **Shore intersection** : compute full-grid world-space, sortie `ARGBHalf` (R=waterY, G=foam, B=groundY, A=signedDist) ; **point-fixe 6 itérations** pour inverser la choppiness avant sample terrain+FFT.
- **Planar reflection** : caméra miroir cachée, oblique projection, `GL.invertCulling`, skip de frames via `reflectionUpdateInterval`, frame settings allégées (volumetrics/MV/post off). Rendu de scène complet par update.
- **Underwater** : CustomPass `UnderwaterEffect.shader` (fog + god rays ray-march + distortion Snell) + `UnderwaterLightingController` (atténuation spectrale du soleil + poids Volume).
- **Mesh** : grille fixe `resPerTile × tiles`, snap XZ caméra (`Floor(pos/tileSize)*tileSize`), Y = waterLevel, shadow casting Off.

---

## 4. ANALYSE PAR SOUS-SYSTÈME (axes artistique / technique-perf / usage éditeur)

### 4.1 Cœur de simulation FFT

**Technique.**
- **Spectre** (`OceanInitSpectrum.compute`) : **Phillips complet** (`A·exp(-1/(kL)²)/k⁴·(k̂·ŵ)²`, `L=U²/g`, cutoff petites vagues `_SmallWaveCutoff`). Le mode « JONSWAP » (`_UseJONSWAP=1`) **n'implémente que le facteur de peak-enhancement γ** appliqué sur Phillips (`pow(γ, exp(-r²))`) — **le cœur JONSWAP `αg²/ω⁵·exp(-5/4·(ωp/ω)⁴)` est absent** (le commentaire le décrit mais le code ne le code pas). Ce n'est donc pas un vrai JONSWAP.
- **Dispersion** : deep-water `ω=√(gk)` partout (`InitSpectrum`, `TimeDependent`). **Aucun terme de profondeur finie `tanh(kh)`** → comportement en eau peu profonde / côtière non physique.
- **Cascades** : gérées côté C# (instanciation × `cascadeCount`, patch `seed+cascade*7919`). Le band-pass est un **high-pass seul** via `_MinWavelength` (pas de borne haute) → **recouvrement possible entre cascades**, pas de band-pass borné des deux côtés.
- **IFFT** : correcte, mais **n'exploite pas la propriété hermitienne 2-en-1** (Dx+i·Dz pourraient tenir dans une seule FFT complexe) → **3 IFFT complètes** au lieu de 2. Normalisation **`1/N` et non `1/N²`**, le second `1/N` étant « absorbé dans `_Amplitude` » → **couplage caché : changer la résolution N modifie l'amplitude effective des vagues** (bug latent, non testable isolément).
- **Normales** (`OceanPostProcess.compute`) : **différences finies centrales sur la hauteur Dy** (PAS le Jacobien, PAS le gradient analytique spectral `i·k·h̃`) → plus simple mais lissage/quantification de grille, n'utilise pas le déplacement horizontal dans la métrique.
- **Écume** : seuil sur **Jacobien** `_FoamThreshold`, decay `pow(_FoamDecay, dt·60)` (frame-rate independent), accumulation par **`max()`** (pas de somme → pas de renforcement par passages répétés), ping-pong, **non bornée** (aucun clamp).
- **Redondances** : `specNeg` recalculé inutilement (isotrope, pair en k) ; `expNeg` recalculé au lieu de `conj(expPos)`.

**Artistique (impact des curseurs).**
- `_Amplitude` + `_Choppiness` : hauteur vs « pointes » des vagues — mais l'amplitude est couplée à N (voir ci-dessus), donc changer la résolution déséquilibre le look.
- `_JonswapGamma` : ne fait que pointer/élargir le pic spectral, **sans le vrai caractère JONSWAP** → la mer « formée par le vent » reste proche d'un Phillips. Différence artistique attendue/promise non livrée.
- Seuil Jacobien `_FoamThreshold` : 0 = uniquement les replis (J<0), ~1 = toute crête, >1 = tout couvert → curseur très sensible, facile à rendre la mousse « partout » ou « absente ».
- Absence de `tanh(kh)` : pas de cambrure/raidissement des vagues près du rivage → l'eau côtière paraît « pleine mer » jusqu'au bord.

### 4.2 Shader de surface (`OceanSurface.shader` + includes)

**Technique.**
- **Rendu** : `RenderType=Transparent`, `Queue=Transparent-10`, **ForwardOnly uniquement** (+ `DepthForwardOnly`), `Cull Back` (**single-sided, pas double-sided**), `ZWrite On`, alpha forcé à 1 (opaque de fait, transparence faite main via `_ColorPyramidTexture`). **Pas de passe GBuffer/Deferred.**
- **Éclairage maison malgré HDRP** : `HAS_LIGHTLOOP` défini et tous les includes Lit inclus, **mais `LightLoop()` n'est jamais appelé**. Soleil unique lu manuellement (`_DirectionalLightDatas[0]`), NdotL/diffuse-wrap/SSS/GGX ad hoc. Seuls emprunts réels à HDRP : shadow directionnel + exposure. **Pas d'APV, pas de lumières ponctuelles, pas de réflexion env HDRP** → variants shadows compilés pour rien (coût de compilation gaspillé).
- **Ordre des ~15 effets dans le fragment** (fragile, ordre-dépendant) : discard underwater → sample normal/foam → atténuation shore sur foam → max wake foam → données soleil → Fresnel (F0=0.02) → refraction (`_ColorPyramidTexture`, UV distordu par N.xz) → reconstruction worldpos scène (depth) → caustics (×(1+c)) → shadow → absorption profondeur (Beer-Lambert) → shore foam → couleur underwater (`lerp(body, sceneColor, refractionMask)`) → diffuse wrap → SSS → réflexion sky/planar + GGX → combine par Fresnel → **foam appliqué en dernier** → edge-fade.
- **Réflexions** : **sky gradient procédural** (`lerp(horizon, zenith, refl.y)`) + **planar RT optionnelle** (miroir-X + distortion). **Pas de cubemap, pas de SSR.** Fresnel atténué en eau peu profonde.
- **Absorption** : Beer-Lambert `1-exp(-depth·_DepthAbsorption)`, `lerp(_ShallowColor, _AbsorptionColor, absorption)`. **`_DeepColor` déclaré mais JAMAIS lu dans le shader** (c'est `_AbsorptionColor` qui sert de couleur profonde) — alors que le `.mat` *assigne* `_DeepColor` → curseur trompeur pour l'artiste. **`_SunDirection` déclaré mais non utilisé** (code lit `_DirectionalLightDatas[0]`).
- **Cascades en shader** : normales = **somme des gradients** des 3 cascades, foam = `max`, displacement = somme — **aucun fondu LOD distance** → aliasing scintillant des cascades courtes au loin ; et changer `_OceanCascadeCount` à chaud **ajoute/retire une cascade entière brutalement** (pop).
- **Tessellation** (`OceanTessellation.hlsl`) : hull/domain, `fractional_odd`, `maxtessfactor(64)`, facteur **par distance** (pas screen-space), plage morte 0–5 m, `EdgeFactor=max` aux sommets (anti-crack T-junction). Domain et DomainDepth **doivent appliquer le même déplacement** (« MUST match », sinon Z-fighting) → couplage fragile.
- **Shore attenuation dupliquée 3×** (frag, domain, domainDepth) au lieu d'un helper.
- **~38 propriétés matériau** + bien au-delà de 60 uniforms globaux au total.

**Artistique.** Specular GGX + SSS + diffuse-wrap donnent un rendu riche, mais réflexion = simple dégradé ciel (peu crédible sans planar coûteuse), foam = bruit générique tilé (Voronoi/Turbulence), caustics dérivées de la courbure des normales FFT (pas d'authoring). `_SunColor` HDR à `130000` dans le `.mat` (intensité extrême, calibrage fragile).

**Usage éditeur.** Effets imbriqués non documentés (ordre implicite) ; modifier un effet risque d'en casser un autre.

### 4.3 Caustics / Foam blur / Debug

- `OceanCaustics.hlsl` : caustiques = `smoothstep` de la courbure (Laplacien) des normales FFT, **aberration chromatique** (R/B décalés), blend **`min`** de 2 cascades (cascade 2 ignorée), depth-fade. Réutilisable.
- `OceanFoamBlur.compute` : gaussien séparable 5-tap, wrap toroïdal, blend progressif `saturate(_BlurRadius*0.15)` par frame (dissipation façon Sea of Thieves). Sain.
- `OceanDebugSpectrum.shader` : **`Hidden/`, outil de dev** (visualise un spectre amplifié ×5000 ou la shore map). **Non branché en prod.** *(Manqué par l'exploration.)*

### 4.4 Wake (sillage)

- `OceanWakeTrail.compute` : RT `RFloat`, paint = centre circulaire + 2 splashes latéraux (façon SoT, **pas de V procédural**), blend **`max` only → jamais de creux/suction**, fade multiplicatif frame-rate-independent. Full-grid/frame.
- **Doublons / throwaway** : `OceanWakeStamp.shader` réimplémente le stamp **avec un vrai V de Kelvin** (params disjoints) ; `OceanWakeFade.shader` réimplémente le fade en **soustractif non normalisé au dt**. → **2 implémentations parallèles du paint et 2 du fade** (compute vs fragment) → odeur de code d'essai non nettoyé (contraire à la préférence « clean code, no throwaway »). À trancher : lequel est réellement câblé par le manager.
- **Découplage réussi** : le wake ne dépend que de la cible (point fort architectural confirmé).

### 4.5 Shore (rivage)

- `OceanShoreIntersection.compute` : world-space full-grid, point-fixe 6 itérations × jusqu'à 3 cascades → **coûteux par texel**. **Mais sa map de sortie n'est consommée par aucun des fichiers shore lus** : `ShoreWaveEffect.shader` **recalcule sa propre intersection en screen-space** (3ᵉ copie de la boucle choppiness). → map potentiellement **orpheline / doublon**.
- `ShoreWaveEffect.shader` : tracking RT `R32G32` ping-pong (descente de vague → washMark), foam Perlin 4 octaves + wet-sand darkening (premultiplied alpha). **Tracking screen-space sans reprojection** → ghosting/traînée au pan caméra rapide.
- `ShoreWavePass.cs` : actif seulement caméra **au-dessus** de l'eau. **Non câblé en scène** (voir §2.4).

### 4.6 Underwater (sous l'eau)

- `UnderwaterEffect.shader` (CustomPass, caméra **sous** l'eau) : 2 passes (full-res / downsamplée + upscale), fog exponentiel, **god rays ray-march** (4–64 pas, jitter IGN, beam pattern = courbure des normales FFT, extinction), distortion + **Snell's window** (anneau brillant au bord, TIR sombre).
- `UnderwaterLightingController.cs` : atténuation spectrale Beer-Lambert RGB du soleil + poids Volume. **Bug structurel** : `sun.intensity *= …` et `sun.color *= …` **cumulatifs et non restaurés** chaque LateUpdate, en `[ExecuteAlways]` → **dérive permanente / assombrissement irréversible du Directional Light**. **Non câblé en scène.**
- `UnderwaterInput.hlsl` : sample FFT height/normal **sans correction de choppiness** (≠ shore/compute qui la corrigent) → **incohérence de hauteur d'eau** entre la surface vue d'en dessous et la ligne de rivage. Fog « Subnautica » (distance + depth-darkening découplés). Snell's window paramétrée (`criticalCos` lerp).

**Points de douleur underwater confirmés** : (a) **double système d'absorption** non couplé — le controller atténue le soleil de toute la scène (coeffs RGB), le shader applique son propre fog/depth-darkening (coeffs différents) → double-comptage + désaccord de teinte ; (b) **transition surface = bascule binaire** par `camPos.y` vs `waterLevel` (shore skip si dessous, underwater skip si dessus), **aucun masque per-pixel, aucun ménisque, pas de double-sided** → caméra à moitié immergée mal gérée, waterline abrupte.

### 4.7 Orchestration C# & usage éditeur

- **`OceanSettings.cs`** : **110 champs publics sérialisés** (~101 réglables + 9 références d'assets). **~110 tooltips** (couverture quasi totale), **~70 `[Range]`**, **0 `OnValidate`** → **aucun clamp programmatique** (set par script hors plage = comportement indéfini). **4 `[Header]` dupliqués** (édition manuelle bâclée). 25 groupes thématiques.
- **`OceanSettingsEditor.cs`** : inspecteur custom propre, **8 onglets** (General/Surface/Shading/Wake/Underwater/Wind/Settings/References), onglet persisté en `EditorPrefs`. Doublon `wrapDiffuse`, typos (`Referencies`). Certains champs du SO n'apparaissent dans aucun onglet.
- **`BeaufortPresets.cs`** : **7 presets Beaufort complets** (Calm→Hurricane), `struct` 24 champs, libellés + descriptions, `GetPreset`/`ApplyToSettings`, **pleinement branchés** dans l'inspecteur (7 boutons + Undo + « Apply Selected Preset »). → **le constat « zéro preset » est FAUX.** Nuances : couvrent **24/~101 params (~24 %)** seulement (rien sur underwater, wake, cascades, résolution, JONSWAP, shore, tessellation, refraction, planar) ; **codés en dur** (pas data-driven) ; `windSpeedFactor` écrit en **vitesse absolue** → incohérent dès qu'un `WindZone` est présent (où c'est un multiplicateur).
- **Pitfall perf/correctness sévère** : si `windPulse*Factor > 0`, `FFTSpectrumParamsChanged()` compare la wind-speed **pulsée** → elle varie chaque frame → **réinitialisation complète de H₀ (release+recreate de toutes les RT FFT) à chaque frame**.
- **Aucun caching** : ~33 `material.Set*` + ~50 `Shader.SetGlobal*` poussés inconditionnellement chaque frame (aucun dirty-flag).
- **Aucune instrumentation perf** : 0 `ProfilerMarker`/`CommandBuffer`/`ProfilingScope` → bottleneck non mesurable depuis le code.

---

## 5. TRAITEMENT DES 6 POINTS DE DOULEUR

Format : **choix retenu / piège rencontré / solution appliquée / pertinence critique.**

### 5.1 Vagues complexes à régler
- **Choix** : FFT Phillips/(pseudo-)JONSWAP, 3 cascades, ~101 curseurs.
- **Piège** : amplitude couplée à N (normalisation `1/N`) ; γ sans vrai effet JONSWAP ; cascades à high-pass seul (overlap) ; presets ne couvrant que 24 % ; `windSpeedFactor` ambigu vs WindZone ; aucun clamp.
- **Solution appliquée** : presets Beaufort (7×24), tooltips quasi exhaustifs, inspecteur à onglets.
- **Pertinence critique** : la douleur **n'est pas** « zéro doc/preset » (faux), mais (1) **interdépendances cachées** (N↔amplitude, cascade overlap), (2) presets **partiels et codés en dur**, (3) absence de feedback spectral en jeu (le debug existe mais hors prod), (4) absence de validation de plage. Refonte : presets data-driven couvrant 100 % des params + découplage N/amplitude + un seul « master » + dérivés.

### 5.2 Écume peu crédible
- **Choix** : Jacobien (seuil) + decay + blur + 2 textures de bruit générique.
- **Piège** : 1 seule couche (whitecaps), textures = noises non-authored tilés, accumulation `max` (pas de renforcement), pas de roughness/lifetime/spray, pas de mousse de rivage cohérente (shore recalcule sa propre intersection).
- **Solution appliquée** : foam blur progressif (dissipation douce, façon SoT).
- **Pertinence critique** : sous l'état de l'art (Crest/SoT = **3 couches** : whitecaps Jacobien + mousse ambiante + mousse rivage, + Foam Fade Rate). Le système est mono-couche → plastique. À refondre vers multi-couches + contribution roughness + (option) particules.

### 5.3 Réflexions incohérentes
- **Choix** : sky gradient procédural + planar RT optionnelle ; rien sous l'eau (Snell distortion seule).
- **Piège** : gradient banal, planar coûteuse/peu fréquente et fragile sur vagues (normales variables), pas de cubemap ni SSR, **pas de continuité dessus↔dessous**.
- **Solution appliquée** : blend Fresnel sky↔planar, atténuation en eau peu profonde.
- **Pertinence critique** : conforme aux « leaky reflections » documentées par la communauté Crest sur sonde planaire. Manque l'hybride recommandé (cubemap ciel toujours + planar/SSR sélectif + sonde sous-marine). À refondre en système de réflexion unifié.

### 5.4 Absorption incohérente
- **Choix** : 2 systèmes — depth-coloring surface (Beer-Lambert `_DepthAbsorption`/`_AbsorptionColor`) **et** atténuation spectrale du soleil (controller, coeffs RGB) **et** fog underwater (coeffs encore différents).
- **Piège** : **3 modèles non couplés**, coefficients distincts → double-comptage + teintes en désaccord ; `_DeepColor` mort ; choppiness corrigée côté surface/shore mais pas underwater.
- **Solution appliquée** : aucune unification.
- **Pertinence critique** : loin de la référence Jerlov / rendu spectral CGF2024 (un seul modèle d'eau cohérent). À refondre : **un seul Beer-Lambert spectral** partagé surface + underwater + fog, calibré sur les types Jerlov.

### 5.5 Surabondance de paramètres
- **Choix** : tout exposé (~101 réglables), 25 groupes, 8 onglets.
- **Piège** : 4 Headers dupliqués, `wrapDiffuse` doublonné, des champs hors onglets, 0 clamp, presets partiels.
- **Solution appliquée** : onglets + tooltips + Beaufort.
- **Pertinence critique** : la quantité **n'est pas compensée par la hiérarchisation** (master + dérivés %, comme adopté pour l'herbe). Le levier n'est pas « plus de doc » mais « moins de surface réglable + presets complets ». Cohérent avec la préférence utilisateur « 1 master + % + presets ».

### 5.6 Coexistence dessus/sous-eau fragile
- **Choix** : `discard` de la surface si caméra sous l'eau + CustomPass underwater ; bascule par `camPos.y`.
- **Piège** : transition **binaire** sans crossfade, **aucun masque per-pixel**, **pas de double-sided ni de ménisque**, hauteur d'eau incohérente (choppiness non corrigée underwater), waterline glitchable à `y≈waterLevel`.
- **Solution appliquée** : `transitionDepth` (controller) lisse seulement le poids du Volume.
- **Pertinence critique** : sous la solution de référence Crest (Custom Pass + **matériau double-sided** + **ménisque** + fog underwater sur les transparents). À refondre : surface double-sided + masque per-pixel à la waterline + modèle d'eau partagé.

---

## 6. ÉVALUATION CRITIQUE COMPARATIVE (références corrigées)

> **Correction de la hiérarchie des spectres.** Le contexte résumait rtryan98 par « JONSWAP recommandé vs Phillips », ce qui efface la recommandation réelle : **TMA > JONSWAP > Phillips**. TMA = JONSWAP modifié par l'atténuation de profondeur de Kitaigorodskii, recommandé pour eaux profondes **et** peu profondes ; JONSWAP n'est qu'une approximation acceptable mais sous-optimale en eau peu profonde.

| Axe | Existant | Référence | Verdict |
|---|---|---|---|
| **Spectre** | Phillips ; « JONSWAP » = Phillips×γ (cœur absent) ; deep-water seul | TMA > JONSWAP > Phillips (rtryan98) | **Un à deux crans sous la référence.** Pas un vrai JONSWAP, pas de TMA, pas de `tanh(kh)` → faible en zones côtières/peu profondes. **Ne pas conclure « aligné ».** |
| **FFT** | Stockham auto-sort, correct, mais 3 IFFT (pas d'hermitien), norm `1/N` couplée à l'amplitude | FFT vs Gerstner (BTH) ; hermitien 2-en-1 (rtryan98) | Cœur **solide et réutilisable**, mais **optimisation hermitienne manquante** (−1 IFFT/3 possible) et **couplage N↔amplitude** à corriger. |
| **Écume** | 1 couche (Jacobien) + blur + noise tilé | 3 couches Crest/SoT + Fade Rate | **En dessous** : mono-couche, pas de mousse ambiante/rivage cohérente. |
| **Réflexions** | sky gradient + planar | cubemap + planar + SSR (Crest) | **En dessous** ; « leaky reflections » planaire connues ; pas d'hybride. |
| **Absorption** | 3 modèles non couplés | Jerlov + spectral CGF2024 | **Incohérent** ; à unifier sur un modèle Jerlov spectral. |
| **Dessus/dessous** | discard + bascule binaire | Custom Pass + double-sided + ménisque (Crest) | **Fragile** ; manque double-sided/ménisque/masque per-pixel. |
| **Normales** | différences finies (hauteur) | gradient analytique spectral `i·k·h̃` | Approximation ; le spectral serait plus précis/moins bruité. |
| **Tessellation** | distance, maxfactor 64, ForwardOnly | GBuffer-dominant (Unity Water) | Coût concentré surface ; ForwardOnly cohérent avec le shading maison mais hors deferred. |

---

## 7. RÉUTILISABLE vs À REFONDRE

**Réutilisable (solide) :**
- Cœur FFT (Stockham auto-sort, time-evolution Tessendorf) — sous réserve : ajouter hermitien 2-en-1 et corriger la normalisation `1/N`→`1/N²` (découpler de l'amplitude).
- Architecture **wake découplée** (ne dépend que de la cible) — meilleur sous-système.
- `OceanFoamBlur` (gaussien séparable propre), `OceanCaustics` (module autonome), `OceanDebugSpectrum` (outil de debug utile).
- Inspecteur à onglets + mécanisme Beaufort (à étendre/data-driver).

**À refondre :**
- **Spectre** : passer à JONSWAP réel et/ou TMA + dispersion profondeur finie `tanh(kh)`.
- **Shader de surface** : éclater le monolithe (~15 effets ordre-dépendants), décider LightLoop HDRP vs maison (aujourd'hui hybride incohérent), supprimer props mortes (`_DeepColor`, `_SunDirection`), fondu LOD des cascades.
- **Absorption** : modèle Beer-Lambert spectral **unique** partagé (surface + underwater + fog).
- **Réflexions** : système unifié (cubemap + planar/SSR + sonde sous-marine).
- **Dessus/dessous** : double-sided + ménisque + masque per-pixel + correction choppiness underwater.
- **UX** : presets data-driven couvrant 100 % des params, master + dérivés %, clamps (`OnValidate`), nettoyer Headers/doublons.
- **Nettoyage throwaway** : doublons wake (paint/fade compute vs fragment), 3 copies de la boucle choppiness, map shore orpheline.

**Bugs à corriger avant tout (même hors refonte) :**
- Mutation cumulative non restaurée du soleil (`UnderwaterLightingController`).
- Réinit H₀ par frame si `windPulse > 0`.
- Couplage N↔amplitude (normalisation FFT).

---

## 8. QUESTIONS OUVERTES POUR LA PHASE GUIDELINES

*(Questions d'intention, à trancher conjointement — pas résolubles par lecture.)*
1. **Cible artistique** : réaliste-stylisé GoT/Sea of Thieves vs réaliste physique ? (détermine spectre, foam, réflexions).
2. **Profondeur de la simulation côtière** : faut-il `tanh(kh)` / vagues déferlantes de rivage, ou océan « pleine mer » suffit ?
3. **Budget perf cible** (GPU/plateforme) : conditionne cascades (3 vs 4), god rays, planar, résolution FFT.
4. **Réutiliser l'existant** : garde-t-on le cœur FFT + wake, ou refonte 100 % libre ?
5. **Sous-systèmes à conserver câblés** : underwater (désactivé), shore waves (non câblé), planar reflection — lesquels sont réellement voulus dans le jeu ?
6. **Niveau de profondeur perf attendu de l'audit** : faut-il une passe de profiling instrumenté (hors scope actuel, exige du code) ?

---

## 9. LIMITES DE L'AUDIT

- **Audit statique en lecture seule** : aucune exécution, aucun profiling runtime (le code n'a **aucune instrumentation** GPU). Les coûts sont **structurels** (chemins lus), pas mesurés. Un profiling fiable exigerait d'instrumenter le code (violerait « pas d'implémentation »).
- **Câblage runtime non vérifiable depuis les assets** : `ShoreWavePass` / `UnderwaterLightingController` / `UnderwaterLightinh.asset` non installés en scène pourraient être instanciés runtime par `OceanSystem` (à confirmer en éditeur). Statut documenté « non câblé / à confirmer ».
- **Variantes shader** : non compilées ici ; `OceanSurface.mat` a tous ses keywords en `m_InvalidKeywords` → l'état réel des features (tess/wind/POM/planar) dans la variante effective est à valider en éditeur.
- **Managers hors périmètre** : le wake mentionne un `OceanWakeManager` non présent dans le dossier ; la cible `wakeTarget` est externe.
- L'axe artistique des compute purs est traité **sous l'angle « impact des curseurs sur la sortie visuelle »**, pas comme analyse esthétique d'un rendu live (non observé).

---

**Audit révision 2 terminé — lecture intégrale des 23 fichiers source + 3 assets, dépendances résolues par GUID, câblage scène vérifié. Prêt pour la phase de redéfinition des guidelines.**
</content>
</invoke>
