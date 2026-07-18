# Analyse du systeme Ocean Custom

Date: 2026-05-16  
Projet: `C:\Users\Arthe\Ombrage`

## Objectif

Analyser le systeme d'ocean custom et identifier pourquoi la detection de l'intersection entre les vagues de l'ocean et le terrain/mesh 3D est difficile, puis proposer une solution robuste.

## Fichiers principaux

Systeme actif:

- `Assets/Shader/Ocean/OceanSystem.cs`
- `Assets/Shader/Ocean/OceanSettings.cs`
- `Assets/Shader/Ocean/OceanSettings.asset`
- `Assets/Shader/Ocean/OceanInput.hlsl`
- `Assets/Shader/Ocean/OceanTessellation.hlsl`
- `Assets/Shader/Ocean/OceanSurface.shader`
- `Assets/Shader/Ocean/OceanInitSpectrum.compute`
- `Assets/Shader/Ocean/OceanTimeDependentSpectrum.compute`
- `Assets/Shader/Ocean/OceanFFT.compute`
- `Assets/Shader/Ocean/OceanPostProcess.compute`
- `Assets/Shader/Ocean/ShoreWavePass.cs`
- `Assets/Shader/Ocean/ShoreWaveEffect.shader`
- `Assets/Shader/Ocean/UnderwaterPass.cs`
- `Assets/Shader/Ocean/UnderwaterInput.hlsl`
- `Assets/Shader/Ocean/UnderwaterEffect.shader`

Ancienne version utile pour comparaison:

- `backups/Ocean/Ocean_BACKUP_2026-05-14/`
- Ancien decoupage en `OceanManager.cs`, `OceanPlane.cs`, `OceanWakeManager.cs`, `OceanSetup.cs`.
- La version active semble avoir fusionne ces responsabilites dans `OceanSystem.cs`.

Systeme terrain pertinent:

- `Assets/Shader/TerrainLitCustom/Controls/TerrainDeformationManager.cs`
- `Assets/Shader/TerrainLitCustom/TerrainLitCustomHullDomain.hlsl`
- `Assets/Shader/TerrainLitCustom/TerrainLitCustomData.hlsl`

Scene inspectee:

- `Assets/Scenes/Debug/OutdoorsScene.unity`

## Architecture actuelle de l'ocean

`OceanSystem.cs` est le chef d'orchestre.

Il fait quatre choses principales:

1. Genere un mesh plan pour l'ocean.
2. Lance le pipeline FFT sur GPU.
3. Pousse les textures globales consommees par les shaders.
4. Met a jour le materiau, la reflection planaire et le wake.

Points importants:

- `OceanSystem.Update()` lance `UpdateFFT()`, `UpdateWake()`, puis `UpdateMaterialParams()`.
- `UpdateFFT()` calcule les cascades FFT, fait les IFFT, genere normal/foam, puis pousse les textures globales.
- `FFTPushGlobalTextures()` expose:
  - `_OceanDisplacementY`, `_OceanDisplacementY1`, `_OceanDisplacementY2`
  - `_OceanDisplacementX`, `_OceanDisplacementX1`, `_OceanDisplacementX2`
  - `_OceanDisplacementZ`, `_OceanDisplacementZ1`, `_OceanDisplacementZ2`
  - `_OceanNormalMap`, `_OceanNormalMap1`, `_OceanNormalMap2`
  - `_OceanFoamMap`, `_OceanFoamMap1`, `_OceanFoamMap2`
  - `_OceanPatchSize`, `_OceanPatchSize1`, `_OceanPatchSize2`
  - `_OceanCascadeCount`
  - `_OceanWaterLevel`

Le mesh ocean est un plan plat. Le vrai mouvement des vagues est applique dans le shader, pas dans un mesh CPU.

## Generation des vagues

Le pipeline FFT est classique:

- `OceanInitSpectrum.compute` genere le spectre initial `H0(k)`.
- `OceanTimeDependentSpectrum.compute` anime le spectre dans le temps.
- `OceanFFT.compute` transforme le spectre vers le domaine spatial.
- `OceanPostProcess.compute` calcule:
  - la normal map depuis `DisplacementY`;
  - le jacobien depuis les deplacements horizontaux;
  - la foam map a partir des cretes.

Les deplacements finaux sont:

- hauteur: `Dy`
- deplacement horizontal X: `Dx`
- deplacement horizontal Z: `Dz`

Dans `OceanInput.hlsl`, `SampleOceanDisplacement()` retourne:

```hlsl
float3 disp = float3(-dx, dy * _HeightScale, -dz);
```

Puis `OceanTessellation.hlsl` applique ce deplacement dans le domain shader:

```hlsl
float3 disp = SampleOceanDisplacement(posAWS);
posRWS += disp;
```

Donc la surface rendue n'est pas seulement:

```text
y = waterLevel + height(x, z)
```

Elle est aussi deplacee horizontalement:

```text
finalXZ = gridXZ + (-Dx, -Dz)
finalY  = waterLevel + Dy * heightScale
```

C'est le point central du probleme.

## Systeme de foam existant

Il y a deux types de foam:

1. Foam de crete, generee par le jacobien dans `OceanPostProcess.compute`.
2. Foam d'intersection/shore, estimee dans le shader de surface ou dans le pass shore.

La foam de crete fonctionne sans connaitre le terrain.

La foam de shore cherche a savoir si une geometrie visible est sous ou pres de la surface de l'eau.

## Detection shore actuelle dans OceanSurface.shader

`OceanSurface.shader` fait une detection screen-space:

1. Lit `_CameraDepthTexture`.
2. Reconstruit la position monde visible derriere le pixel d'eau.
3. Echantillonne la hauteur de vague a cette position.
4. Calcule:

```hlsl
float surfaceY = _OceanWaterLevel + waveH;
depthBelowSurface = max(0, surfaceY - sceneAWS.y);
```

Puis il transforme `depthBelowSurface` en foam:

```hlsl
shoreFoamAmount = 1.0 - saturate(depthBelowSurface / _ShoreFoamDistance);
shoreFoamAmount = pow(shoreFoamAmount, _ShoreFoamFalloff) * _ShoreFoamStrength;
foam = max(foam, shoreFoamAmount);
```

Limites:

- C'est screen-space: cela ne voit que ce que la camera voit.
- Cela depend de la depth buffer.
- Cela ne produit pas une carte stable en world-space.
- Cela ne detecte pas une vraie ligne d'intersection geometrique.
- Cela utilise `SampleOceanHeight(sceneAWS)`, qui ne corrige pas le deplacement horizontal des vagues choppy.

## ShoreWaveEffect.shader: la meilleure piste actuelle

`ShoreWaveEffect.shader` contient une idee importante: `_ShoreOceanSample()`.

Cette fonction corrige le sampling pour les vagues choppy:

```hlsl
float2 gridXZ = posAWS.xz;

for (int iter = 0; iter < 3; iter++)
{
    float2 dx_total = 0;
    dx_total.x += sample DisplacementX;
    dx_total.y += sample DisplacementZ;

    gridXZ = posAWS.xz + dx_total;
}
```

Pourquoi c'est important:

- La surface ocean est deplacee horizontalement.
- Si on veut connaitre la hauteur de vague au point monde final `(x,z)`, il faut retrouver le point de grille FFT qui s'est deplace vers ce `(x,z)`.
- Cette inversion approximative par iteration est la bonne approche pour les vagues choppy.

Ensuite la fonction lit `DisplacementY` et `FoamMap` au `gridXZ` converge.

Limite:

- Cette correction existe seulement dans `ShoreWaveEffect.shader`.
- Elle n'est pas partagee dans `OceanInput.hlsl`.
- `OceanSurface.shader` et `UnderwaterInput.hlsl` utilisent encore des samples de hauteur simples.
- Le pass shore n'est pas branche dans la scene inspectee.

## ShoreWavePass dans la scene

`ShoreWavePass.cs` existe et dessine `ShoreWaveEffect.shader` en plein ecran.

Mais dans `Assets/Scenes/Debug/OutdoorsScene.unity`, le `CustomPassVolume` inspecte ne contient que:

- `UnderwaterPass`

Je n'ai pas vu `ShoreWavePass` reference dans la scene.

Donc meme si `enableShoreWaves` existe dans `OceanSettings`, le pass visuel shore peut ne jamais tourner.

## Underwater

`UnderwaterInput.hlsl` possede son propre helper:

```hlsl
float _UW_SampleOceanHeight(float3 posAWS)
```

Il additionne les `DisplacementY` des cascades, mais ne corrige pas non plus le deplacement horizontal X/Z.

Donc caustics, surface from below et underwater peuvent eux aussi utiliser une hauteur legerement fausse quand `choppiness` est fort.

## Terrain

Le terrain a deja une architecture utile dans `TerrainDeformationManager.cs`:

- un `RenderTexture` toroidal pour deformation runtime;
- un `RenderTexture` en UV terrain pour mask de tessellation;
- des fonctions de mapping world -> terrain UV:

```csharp
Vector2 WorldToTerrainUV(Vector3 worldPos)
{
    return new Vector2(
        (worldPos.x - cachedTerrainOrigin.x) / cachedTerrainSize.x,
        (worldPos.z - cachedTerrainOrigin.y) / cachedTerrainSize.y);
}
```

C'est un precedent important: le projet sait deja utiliser des RT monde/terrain pour piloter des effets geometriques.

## Cause probable du blocage

Claude cherche probablement une intersection en comparant directement:

```text
oceanHeight(x,z) vs terrainHeight(x,z)
```

Mais cette comparaison est incomplete pour cet ocean.

A cause de la choppiness:

```text
la vague visible au point monde XZ ne vient pas du meme XZ dans la grille FFT
```

Il faut resoudre approximativement:

```text
worldXZ = gridXZ + horizontalDisplacement(gridXZ)
```

Dans ce projet:

```text
worldXZ = gridXZ + (-Dx(gridXZ), -Dz(gridXZ))
```

Donc:

```text
gridXZ ~= worldXZ + (Dx(gridXZ), Dz(gridXZ))
```

C'est exactement ce que fait `_ShoreOceanSample()`.

## Conclusion courte

Le systeme a deja presque toutes les briques:

- les textures FFT globales;
- une fonction de sampling des vagues;
- une correction choppy dans `ShoreWaveEffect.shader`;
- un systeme terrain qui sait mapper monde -> UV/RT;
- un CustomPass shore, mais pas encore branche dans la scene inspectee.

Le manque est une representation stable de l'intersection en world-space.

La solution actuelle screen-space marche pour un effet de foam visible, mais pas pour detecter de facon robuste ou les vagues intersectent vraiment le terrain/mesh.

## Recommandation 1: fix rapide

Extraire `_ShoreOceanSample()` de `ShoreWaveEffect.shader` vers un fichier partage, idealement `OceanInput.hlsl`.

Ajouter une fonction du style:

```hlsl
void SampleOceanSurfaceAtWorldXZ(float3 posAWS, out float height, out float foam)
{
    // Fixed-point iteration to compensate choppy horizontal displacement.
}
```

Puis remplacer les usages simples de `SampleOceanHeight()` dans:

- `OceanSurface.shader`
- `UnderwaterInput.hlsl`
- `UnderwaterEffect.shader`
- eventuellement `OceanCaustics.hlsl` via les appels qui lui fournissent `surfaceAWS`

Ensuite brancher `ShoreWavePass` dans le `CustomPassVolume` de la scene.

Effet attendu:

- shore foam plus coherent avec les cretes choppy;
- moins de decalage visuel entre vague et terrain;
- solution rapide, peu intrusive.

Limite:

- cela reste screen-space si on garde `OceanSurface.shader`/`ShoreWaveEffect.shader` tels quels.

## Recommandation 2: vraie solution robuste

Creer une carte GPU stable d'intersection, par exemple:

```text
_OceanShoreIntersectionMap
```

Pipeline propose:

1. Creer une RT top-down autour de la zone utile, en world-space.
2. Remplir cette RT avec `groundY`:
   - pour Unity Terrain: sample de la heightmap terrain;
   - pour meshes 3D: rendu orthographique top-down dans une RT `RFloat` qui encode la hauteur monde.
3. Lancer un compute shader qui, pour chaque texel:
   - convertit texel -> worldXZ;
   - sample l'ocean avec correction choppy;
   - calcule:

```text
waterY = waterLevel + correctedWaveHeight
delta = waterY - groundY
intersection = 1 - saturate(abs(delta) / foamWidth)
wash = smoothstep(shoreWashHeight, 0, groundY - waterY)
```

4. Utiliser cette map dans:
   - ocean shader pour shore foam;
   - terrain shader pour wet sand/foam;
   - VFX/eventuels particles;
   - debug view.

Avantages:

- stable en world-space;
- independant de la camera;
- fonctionne pour terrain et meshes;
- reutilisable pour gameplay, VFX et rendu.

Inconvenients:

- demande un petit systeme de RT supplementaire;
- pour les meshes, il faut choisir quelles couches sont incluses dans le rendu top-down.

## Recommandation 3: cote CPU, uniquement pour debug/gameplay

Pour des points ponctuels, on peut faire cote CPU:

1. Obtenir `groundY` par `Terrain.SampleHeight`, `TerrainCollider.Raycast`, ou `Physics.Raycast`.
2. Obtenir `waterY` par AsyncGPUReadback ou par une approximation CPU du spectre.

Mais pour une ligne de rivage dense, ce n'est pas ideal.

Le GPU est le bon endroit pour cette detection, car les vagues vivent deja dans des textures GPU.

## Points de vigilance

- `OceanSettings.asset` a `choppiness: 2`, donc la correction horizontale est indispensable.
- `OceanSettings.asset` a `wakeTrailShader: {fileID: 0}`, donc le wake semble actuellement non assigne.
- `ShoreWavePass` existe, mais n'est pas reference dans la scene inspectee.
- Le shader de surface utilise `SampleOceanHeight()` simple, pas la correction choppy.
- Le systeme underwater a un helper de hauteur separe, ce qui risque de creer des divergences.

## Decision conseillee

Court terme:

- Factoriser `_ShoreOceanSample()` dans un helper partage.
- L'utiliser partout ou l'on a besoin de la hauteur de la surface ocean au point monde final.
- Ajouter `ShoreWavePass` au CustomPassVolume.

Moyen terme:

- Implementer une `ShoreIntersectionMap` GPU.
- Alimenter cette map avec une height map top-down terrain/mesh.
- Utiliser cette map pour foam, wetness et VFX.

La solution robuste n'est pas de chercher une intersection entre deux meshes triangules. Dans ce projet, l'ocean est une hauteur FFT deplacee horizontalement sur GPU. Il faut donc comparer deux champs de hauteur en world-space, avec inversion du deplacement horizontal de l'ocean.
