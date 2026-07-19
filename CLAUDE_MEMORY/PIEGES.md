# Pièges & leçons durables — Unity 6 / HDRP 17 / BRG / shaders (Ombrage)

> **mémoire technique persistante du projet concernant les problèmes sérieux d'implémentation** (décision utilisateur 2026-07-17).
> À lire avant tout code Unity / HDRP / BRG / shader. Tout nouveau piège durable s'ajoute ICI.

## BRG / DOTS Instancing
- **`BatchDrawRange.drawCommandsType` (Unity 6)** : les sorties `BatchCullingOutputDrawCommands`
  sont allouées via `UnsafeUtility.Malloc` = mémoire NON initialisée ; Unity 6 a ajouté ce champ →
  s'il contient du garbage : access violation native dans
  `ScriptableBatchRenderer::RenderMultipleMeshes` (même pour 16 instances). Fix :
  `drawRanges[i].drawCommandsType = BatchDrawCommandType.Direct`. Règle : initialiser TOUS les
  champs des structs de sortie BRG (les samples antérieurs à U6 sont incomplets). Tell-tale : code
  identique OK sur 2022.3, crash sur Unity 6. La vraie stack est dans l'`Editor.log` du dossier
  Crash (« OUTPUTTING STACK TRACE ») — le JSON de crash est du bruit de symbolisation.
- **`GraphicsBuffer.SetData` : `graphicsBufferStartIndex` en unités `sizeof(T)`, PAS en /4**
  (stride int d'un buffer Raw). Mauvaise unité → dépassement → exception AVANT `AddBatch` →
  BatchID 0 → « invalid Batch/Mesh/Material ID ».
- **`Graphics.DrawMesh` ne rend PAS un matériau custom HDRP deferred** (le camera-relative casse) ;
  `RenderMeshIndirect` ne se rend PAS dans les passes ombre/motion-vectors. **Seul le BRG obtient
  ombres + motion vectors automatiquement (via `BatchFilterSettings`).**

## SRP-Batcher (rejet dans les 2 sens)
- Une prop de `Properties{}` hors `UnityPerMaterial` = rejet → paramètres partagés en globals.
- Un membre `UnityPerMaterial` absent de `Properties{}` = rejet → toute entrée du cbuffer doit
  avoir sa ligne `Properties{}` (`[HideInInspector]` pour les per-instance).

## HDRP 17 — surface custom & géométrie procédurale
- **Surface custom** (remplacer `LitData.hlsl`) : inclure soi-même
  `Runtime/Material/BuiltinUtilities.hlsl` (sinon `undeclared identifier 'InitBuiltinData'` —
  chemin normal `LitData→LitBuiltinData→BuiltinUtilities` ; corrigé dans `GrassBRGSurface.hlsl`).
- **Camera-relative** : jamais de graine/hash sur `GetObjectToWorldMatrix()._m03/13/23`
  (relatif caméra → shimmer) → `GetAbsolutePositionWS`. L'APV exige aussi la position ABSOLUE.
- **Diffusion Profile** : DOIT être enregistré dans HDRP Global Settings → Diffusion Profile List,
  sinon transmission silencieusement absente. Recette SSS custom GBuffer : stencil Ref 2→3 +
  `MATERIALFEATUREFLAGS_LIT_TRANSMISSION` + `diffusionProfileHash`.
- **Surface custom TESSELLÉE** : le `HLSLINCLUDE` doit inclure lui-même `GeometricTools.hlsl`
  PUIS `Tessellation.hlsl` (core), entre `Common.hlsl` et `ShaderVariables.hlsl` (ordre de
  `LitTessellation.shader`). Sinon la macro `TESSELLATION_INTERPOLATE_BARY` n'est pas déployée →
  `undeclared identifier 'positionRWS'` (`VaryingMesh.hlsl`, fonction domain). ⚠ Défaut INVISIBLE
  au gate éditeur sans rendu (aucun variant tessellé compilé) et à la revue statique → ne surface
  qu'au **rendu réel** (1ᵉʳ variant tessellé = ShadowCaster).
- **Surface custom remplaçant `LitData.hlsl`** : redéclarer soi-même `int _ObjectId; int _PassValue;
  float4 _SelectionID;` **HORS `UnityPerMaterial`** (copie de `LitProperties.hlsl`), sinon
  `undeclared identifier '_ObjectId'/'_SelectionID'` dans les passes **SceneSelection/ScenePicking**.
  ⚠ Ces 2 passes ne sont compilées qu'au **build** (shader en *Always Included Shaders* force TOUTES
  les passes) ou au clic Scene View → défaut dormant, invisible en éditeur normal.
- **Intégration géométrie procédurale (vérifié sur les sources du package HDRP 17.4)** : opaque →
  préférer GBuffer/deferred (`Tags{LightMode=GBuffer}`, `SHADERPASS_GBUFFER`,
  `ShaderPassGBuffer.hlsl`, `ENCODE_INTO_GBUFFER`, stencil `_StencilRefGBuffer=2`) — moins cher que
  ForwardOnly (pas d'explosion de variants light-list) ; ForwardOnly SEULEMENT si BSDF custom/MSAA
  (squelette = passe « ForwardOnly » d'AxF.shader, driver `ShaderPassForward.hlsl`, master switch
  `HAS_LIGHTLOOP`). APV/GI : ne pas bricoler — `multi_compile_fragment _ PROBE_VOLUMES_L1
  PROBE_VOLUMES_L2`, builtinData zero-init, le `LightLoop` remplit `bakeDiffuseLighting`.
  Motion vectors (requis TAA) : `Tags{LightMode=MotionVectors}` (nom exact),
  `SHADERPASS_MOTION_VECTORS`, CS courant (`UNITY_MATRIX_UNJITTERED_VP`) + précédent
  (`UNITY_MATRIX_PREV_VP`), vent évalué à `_TimeParameters.x` ET `_LastTimeParameters.x`,
  `forceNoMotion` pour les instances instables. Prévoir aussi DepthForwardOnly
  (`WRITE_NORMAL_BUFFER`) + ShadowCaster. ⚠ Un draw procédural indirect est INVISIBLE du culling
  et des vues ombre/MV sauf à l'émettre PAR VUE ; un `multi_compile` manquant droppe
  silencieusement ombres/decals/APV (aucune erreur de compilation).
- **Lire le stencil dans un FullScreen CustomPass** : le global `_StencilTexture` n'est **PAS**
  fourni aux CustomPass → lecture = 0 partout (écran noir, même le bit `RequiresDeferredLighting`
  présent sur tout opaque éclairé). Le rebinder soi-même via une **passe scriptée enregistrée AVANT**
  le pass fullscreen : `ctx.cmd.SetGlobalTexture(Shader.PropertyToID("_StencilTexture"),
  ctx.cameraDepthBuffer, RenderTextureSubElement.Stencil)` — ressource canonique (celle que HDRP
  donne à TAA/SSR → aucun état étranger). Lecture ensuite standard : `TYPED_TEXTURE2D_X(uint2,
  _StencilTexture)` (à déclarer soi-même, absent de la chaîne CustomPass) +
  `GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, posInput.positionSS))` (`GetStencilValue` =
  core `Common.hlsl`, choisit le canal par plateforme — ne PAS coder `.x`/`.g` en dur). Bits libres
  utilisateur : `StencilUsage.UserBit0=(1<<6)=64`, `UserBit1=(1<<7)=128` (bits 0-5 = HDRPReservedBits).
  ⚠ Le test matériel `Stencil{Comp Equal}` est inutilisable si le MÊME pass doit aussi traiter les
  pixels non-taggés : il les **rejette** (perte du chemin normal). Vérifié océan v2 P6/G3.

## Herbe BRG — pièges résolus (les coûteux)
- Melt/fade : distance sur la caméra de CULL (`_GrassCullCamPos`), jamais la caméra de rendu
  (sinon height=0 → Bézier dégénérée → `normalize(0)` = NaN → GBuffer noir) + NaN guards.
- Placement impostors : distance **3D avec hauteur caméra** (comme le melt des brins), cartes à
  100 % AU DÉBUT du melt des brins — sinon trou brins↔cartes vu de haut.
- Dither de ring clipmap : sur les rings INTÉRIEURS seulement (rayons stables) ; les 2 rings
  extérieurs disparaissent par le melt distance (le rayon externe saute en puissance de 2 quand
  maxBladeDistance change de niveau de clipmap → incohérent sinon).
- Hi-Z occlusion : lecture par `Load` pixel entier (PAS `uv*_RTHandleScale`) ; VP matchée à la
  frame suivante ; capture de la depth HDRP à `endCameraRendering`.
- Toggles d'override de module : `Push(value-or-default)` sinon valeurs fantômes quand décoché
  (corrigé pour GrassTint ; latent sur les autres modules).

## VFX Graph (HDRP)
- Un output **lit OPAQUE rend en DEFERRED** (passe `LightMode=GBuffer` présente — vérifié par
  énumération des pass tags ; le forced-forward HDRP ne concerne que
  transparent/Fabric/Hair/AxF/StackLit/Unlit). Cull Mode et Double Sided y sont indépendants.
  Ne pas occuper l'éditeur pendant un domain reload.
- ⚠ **Le `.vfx` sur disque MENT** (en retard sur les édits non sauvés du graphe) → vérifier les
  settings du graphe via l'état LIVE (`execute_code`), jamais via le fichier.

## Compute shaders — pièges vérifiés (chantier écume P4)
- **`SamplerState sampler_xxx` en compute = sampler COUPLÉ à la texture `_xxx`** (préfixe réservé
  Unity) → s'il n'y a pas de texture de ce nom : sampler invalide, `SampleLevel` retourne **0
  silencieusement**. Deux conventions de nommage « inline » ont aussi échoué. Parade fiable :
  **`Load` + bilinéaire manuel** (wrap au modulo) — zéro dépendance sampler.
- **`Time.deltaTime` ≈ 0 hors Play** (`[ExecuteAlways]`) → toute intégration/décroissance par
  frame est FIGÉE en mode édition (constaté : traînée d'écume fossile indestructible). Parade :
  delta d'horloge réelle `Time.realtimeSinceStartupAsDouble` stocké, clampé (~0.1 s).
- **Jamais d'échantillonnage à dérivées implicites à travers une coordonnée DÉPLACÉE/inversée**
  (océan : `q ≈ p − D(p)`) ni dans une boucle à `break` : les ddx/ddy de D bilinéaire sont en
  marches d'escalier → le LOD hardware saute vers des mips grossiers → **paliers durs en
  parallélogramme**. Parade : **LOD explicite** (distance caméra : `dist·(2/P._m11)·_ScreenSize.w`
  / texel monde), pattern déjà appliqué partout dans les shaders océan (`_LOD`).
- **`GenerateMips()` FONCTIONNE sur RenderTexture Tex2DArray, y compris mip0 écrit en UAV**
  (vérifié empiriquement — ne pas re-soupçonner ce chemin).
- **`Graphics.CopyTexture` entre formats incompatibles échoue SILENCIEUSEMENT** (zéros) — pour un
  readback : RT temporaire au MÊME format, puis `ReadPixels` vers un Texture2D compatible.

## Builds & API
- **`using UnityEditor` hors `#if UNITY_EDITOR` = casse le build** — toujours conditionner les
  usings éditeur.
- Profilage : `MarkerFlags.SampleGPU` (casse exacte) ; `ProfilerRecorder` GPU exige
  `SumAllSamplesInFrame` ; un asmdef de test ne référence pas Assembly-CSharp.
- **Refs d'assets d'un ScriptableObject résolues sous `#if UNITY_EDITOR`** (repli
  `AssetDatabase.LoadAssetAtPath` quand le champ est nul) : NON sérialisées si le champ est encore
  nul au `SaveAssets` → l'asset cible (compute/matériau) est **strippé du build** → objet
  invisible/inactif **en build seulement** (fonctionne en éditeur car le write-back a eu lieu). Fix :
  assigner ET sérialiser les refs AVANT `SaveAssets`, puis vérifier par **round-trip disque**
  (recharger l'asset et asserter les refs non nulles) — la vérif en RAM ne détecte pas l'échec de
  sérialisation.
- **Mesure GPU par marker = seule autorité BUILD** (Development Build + fenêtre Profiler GPU,
  D3D12/Vulkan) ; l'éditeur est indicatif. Un toggle `MeshRenderer.enabled` ne coupe PAS les
  dispatchs compute → un poste GPU compute se mesure par son propre recorder, jamais déduit d'un delta
  renderer.

## Architecture / systèmes du projet
- **Risque orphelin (défensif) : objets runtime `HideAndDontSave` créés par les modules SURVIVENT au
  domain reload** alors que le `Runtime` non sérialisé perd sa réf. En pratique le teardown `OnDisable`
  détruit le volume → **pas d'accumulation observée** (0 orphelin trouvé en P6/G4). MAIS un `Volume`
  GLOBAL avec override `Fog` qui échapperait au teardown corromprait le rendu partout → parade défensive
  ajoutée : **balayer les orphelins avant création** (`Resources.FindObjectsOfTypeAll<Volume>()` voit les
  cachés ; filtrer nom runtime + `scene.IsValid()`) + menu de nettoyage manuel.
- **Ne PAS diagnostiquer un rendu volumétrique / Planar Probe depuis une vue edit-mode non rafraîchie.**
  Le volumetric fog (historique/reprojection) ET les Planar Reflection Probes se **re-render par frame** ;
  hors Play l'éditeur ne repeint pas en continu → on voit un **résultat FIGÉ/périmé** (constaté P6/G4 : un
  « fog vert persistant » et une sonde qui « n'influençait plus creux/lointain » — les deux transitoires,
  disparus au 1er repaint réel). LEÇON : forcer un repaint (bouger la caméra / recompiler / recharger la
  scène) AVANT de conclure à un bug ; ⚠ ne pas partir en chasse (2 hypothèses fausses coûtées ici).
- `TerrainDeformationManager` : RT déformation toroïdale RFloat **R-seul, PARTAGÉE terrain+herbe**
  → pour ajouter une direction de déformation : RT SÉPARÉE, ne pas changer le format de l'existante.
- Anti-modèles océan v1 (interdits, à re-vérifier à chaque phase) : soleil cumulatif, H₀
  réinitialisé par frame, normalisation IFFT couplée ; et le throwaway (2 impls / 2 fades /
  3 copies du même code).
- **Couleur de l'eau vue de dessus** : la transmittance Beer-Lambert `exp(−σ·d)` SEULE rend l'eau
  **turquoise** (avec les a(λ) réels, G≈B survivent : eau claire @10 m ≈ (0.03, 0.66, 0.76)) — jamais
  un bleu océan. La couleur montante = **rétrodiffusion Rayleigh de l'eau pure** (∝ λ⁻⁴, favorise le
  bleu). Modéliser l'albédo comme **réflectance montante** `b_b/σ·(1−exp(−2σ·d))` avec `b_b` constante
  (ni paramètre, ni scattering). Normaliser la profondeur optique par σ̄ pour que le « knee » soit
  indépendant de la turbidité. Océan v2 : source σ UNIQUE (`_WaterAbsorption`) partagée surface +
  sous-marin (Beer-Lambert Jerlov Ia/II/III).

## Diag & outillage
- Captures caméra MCP peu fiables (cadrage raté/noir) → tout jugement VISUEL = l'utilisateur ;
  tout le vérifiable en DONNÉES passe par `execute_code` sur l'état live.
- **Une capture MCP ne tick PAS `[ExecuteAlways]` quand l'éditeur est défocalisé** → forcer
  `Update()` (ou équivalent) AVANT chaque capture, sinon captures identiques sur un état périmé.
  Corollaire : `Ocean Debug.unity` n'a ni Volume ni ciel → juger une couleur exige un environnement
  d'éclairage explicite (un env de gate calibré pour un autre rig n'est pas transposable).
- **Compilation de scripts DIFFÉRÉE quand l'éditeur n'a pas le focus.** `CompilationPipeline.RequestScriptCompilation()`
  / `AssetDatabase.Refresh` déclenchés via MCP **restent en file d'attente** tant que
  `InternalEditorUtility.isApplicationActive == false` (même avec Auto Refresh ON) → la DLL de
  `Library/ScriptAssemblies/` n'est PAS reconstruite, le type chargé reste l'ancien (piège :
  `Type.GetField("nouveauChamp")==null` alors que le `.cs` sur disque est correct). **Vérifier** par le
  timestamp de la DLL vs source, et **demander à l'utilisateur de cliquer dans l'éditeur** pour forcer
  la recompile (la « recompilation = gate utilisateur » du chantier océan vient de là).
- **En mode édition, `SceneView.RepaintAll()` ne repeint PAS la Game view** → un changement piloté par
  MCP peut sembler figé. Forcer : `Resources.FindObjectsOfTypeAll(<UnityEditor.GameView>).Repaint()`
  (type interne, via réflexion) + `EditorApplication.QueuePlayerLoopUpdate()`.
- **Round-trip `ToString`/`Parse` en culture FR casse floats/couleurs.** `color.r + "," + color.g` en
  locale française produit des virgules décimales → `Split(',')` puis `float.Parse` reconstruit une
  valeur fausse (ex. sun color → `(1, 0, 957)`). Pour stash/restore une valeur : littéraux C# ou
  `CultureInfo.InvariantCulture` explicite, jamais la concaténation dépendante de la culture.
- **`execute_code` (CodeDom) : une erreur de compilation = AUCUNE exécution partielle** (tout le bloc
  échoue). Une ligne parasite en fin de script annule tous les effets voulus au-dessus.
- **On ne peut PAS observer une évolution frame-à-frame DANS un seul `execute_code`** :
  `QueuePlayerLoopUpdate` ne fait que planifier (exécution APRÈS le retour du call) et
  `Thread.Sleep` bloque le main thread → deux mesures dans le même appel voient le MÊME état.
  Mesurer l'évolution = **appels séparés** (l'éditeur vit entre les deux), comparer avec
  `Time.frameCount` pour prouver que des frames ont tourné.
- **Couleur d'une surface = matière × lumière (PBR), pas un miroir du ciel.** Test décisif pour trancher
  « matière vs réflexion » : passer le soleil/ciel au rouge → une eau à albédo bleu devient **sombre**
  (pas rouge), car son albédo n'a pas de rouge à renvoyer. À angle rasant (Fresnel→1) c'est la réflexion
  spéculaire qui domine ; à angle plongeant (Fresnel→0.02) c'est la couleur matière. En P3 océan (pas
  encore de réflexions P5), la couleur est indépendante de l'angle = bien la matière/absorption.
- MCP officiel `unity-mcp` / `com.unity.ai.assistant` = ABANDONNÉ ici (entitlement « Capacity
  Limit », cap connexions effectif 0 sur ce compte ; orphelins `relay_win.exe` à tuer au besoin).
  Ne pas re-tenter — le MCP fonctionnel est CoplayDev (cf. `CLAUDE.md`).
