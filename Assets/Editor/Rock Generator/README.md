# Rock Generator (Ombrage.Tools) — Générateur procédural de rochers & falaises

Outil Unity 6 (HDRP 17) de génération procédurale de meshes. Deux générateurs distincts :
des **rochers** (3 algorithmes) et des **falaises** dédiées. Prévisualisation live dans la
scène active, déformation free-form, export FBX + préset JSON, rechargement de préset.

## Architecture (découpage par couche)

| Assembly                    | Type     | Rôle |
|-----------------------------|----------|------|
| `Ombrage.Tools.Core`        | Runtime  | Logique pure : réglages, génération de mesh, bruit, sérialisation. Aucune dépendance Editor. |
| `Ombrage.Tools.Editor`      | Editor   | EditorWindow, preview en scène, export FBX/JSON, IO fichier. |
| `Ombrage.Tools.Core.Tests`  | EditMode | Tests unitaires de la couche Core. |

Sens des dépendances : `Editor → Core`, jamais l'inverse.

## Modes de génération

Le **mode** est l'aiguillage principal.

### Mode Rock — 3 algorithmes

- **PrimitiveNoise** — une primitive (icosphère ou boîte) déplacée le long de ses normales
  par du bruit fractal. Déplacement exécuté en job `[BurstCompile]` parallèle.
- **ConvexHull** — nuage de points semé déterministiquement sur une sphère perturbée, puis
  enveloppe convexe 3D (insertion incrémentale). Rendu facetté, façon rocher anguleux.
- **MarchingCubes** — extraction d'iso-surface depuis un champ de densité voxelisé.
  L'échantillonnage de densité (bruit) tourne en job Burst ; la triangulation utilise des
  **marching tetrahedra** (cube découpé en 6 tétraèdres) — sans cas ambigus, toujours
  étanche.

### Mode Cliff — générateur de falaise dédié

Génère un bloc de falaise étanche (base à `y = 0`), pensé pour être plaqué contre le
Terrain Unity afin d'ajouter du relief vertical que la heightmap ne sait pas représenter.
Une boîte subdivisée est sculptée par un job Burst dont le déplacement est une fonction
pure de la position (la boîte soudée reste donc étanche) : **strates horizontales**
(corniches en gradins, surplombs possibles), **fracturation verticale** (jointoiement
colonnaire), gauchissement macro, détail de surface, crête irrégulière et base érodée.

## Déformation free-form (FFD)

Une grille de points de contrôle déforme le mesh généré, indépendamment du générateur.
Les points se manipulent directement dans la vue Scene (poignées jaunes).

## Export & présets

- **Generate (FBX + JSON)** — une boîte de dialogue ; écrit `nom.fbx` et `nom.json` côte à
  côte. Le mesh exporté est le résultat final (algorithme + FFD), reconstruit à l'origine.
  Le FBX passe par le package `com.unity.formats.fbx`, appelé **par réflexion** : si le
  package est absent, l'outil compile et tourne quand même — l'export se réduit au JSON.
- **Load Preset (JSON)** — réimporte un préset `.json` dans l'outil pour reprendre l'édition.

## Dépendances de packages

- `com.unity.burst` — compilation native du chemin chaud de génération (couche Core).
- `com.unity.mathematics` — `float3`, bruit (`cnoise`, `cellular`), RNG (couche Core).
- `com.unity.formats.fbx` — *optionnel* : requis uniquement pour l'export FBX. Absent, le
  JSON reste fonctionnel.

À installer via le Package Manager (versions vérifiées pour Unity 6).

## Installation

Glisser-déposer le dossier `Rock Generator/` dans `Assets/Editor/` du projet Unity ;
l'emplacement final doit être `Assets/Editor/Rock Generator/`.

Note : les `.asmdef` pilotent la compilation, pas le nom des dossiers. `Ombrage.Tools.Core`
conserve un `includePlatforms` vide et reste donc un assembly *runtime* malgré sa présence
sous `Assets/Editor/` — la règle Unity du « dossier Editor » est neutralisée dès qu'un
assembly definition est présent. Unity génère les `.meta` au premier import.

## Utilisation

`Window ▸ Ombrage Tools ▸ Rock Generator`. La fenêtre crée un GameObject transitoire dans
la scène active qui se met à jour à chaque changement de paramètre. Fermer la fenêtre
retire ce GameObject.

## Tests

`Window ▸ General ▸ Test Runner ▸ onglet EditMode ▸ Run All`.

## Statut

Outil complet : 3 algorithmes de rocher + générateur de falaise dédié, FFD, export
FBX + JSON, rechargement de préset, prévisualisation en scène, suite de tests EditMode.
