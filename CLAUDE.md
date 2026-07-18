# Ombrage — Instructions projet — SOURCE DE VÉRITÉ UNIQUE

Ce fichier est la **source de vérité** du projet : règles de travail, conventions, contraintes permanentes, préférences techniques, processus.

**Principe transversal** : rester **concis, propre, maintenable**. Ne jamais privilégier une solution rapide au détriment de l'architecture. Ce principe s'applique à tout ce qui suit et n'est pas répété ailleurs.

**Gouvernance** : toutes les instructions durables vivent ici. Ne jamais créer d'autres canaux d'instructions permanentes, sauf les emplacements mémoire décrits en section 11.

---

## 1. Règles de travail — CRITIQUES

### Autorisation avant modification (modèle à paliers)

| Palier | Périmètre | Règle |
|--------|-----------|-------|
| **Autonome** | typo, commentaire, formatage, renommage local sans impact fonctionnel | pas besoin d'autorisation |
| **Autorisation explicite requise** | logique, architecture, nouveau script, changement de comportement, modification de shader | **ne jamais** agir sans validation explicite de l'utilisateur |

En cas de doute sur le palier → demander.

### Développement par étapes

Cycle obligatoire : **Cadrage → Plan détaillé → Implémentation → Validation utilisateur → Étape suivante**

- Une seule étape à la fois.
- Chaque étape doit être testable (voir section 6).
- Attendre la validation utilisateur avant de continuer.

### Réflexion et interaction

- Identifier les ambiguïtés **avant** d'agir ; poser suffisamment de questions ; utiliser AskUserQuestion si nécessaire.
- Ne jamais supposer silencieusement un besoin important.
- **Ne pas être un « yes man »** : signaler clairement tout risque architectural, dette technique, incohérence ou mauvaise pratique.
- Une fois un plan validé : le suivre. Ne pas multiplier les pivots ni les alternatives. Changer de direction uniquement en cas de blocage réel, problème architectural majeur ou problème de correction — et alors expliquer le problème + proposer **une** recommandation principale (pas une liste infinie d'options).

---

## 2. Contexte projet

**Type** : Jeu PC AAA/AA d'aventure et exploration.
- Level design contrôlé, **pas** d'open world.
- Pipeline simple et maintenable ; équipe solo ou duo.
- Priorité : qualité visuelle et technique avec une architecture raisonnable pour une petite équipe.

**Direction artistique** : Stylisé réaliste.
- Références générales : God of War, Horizon, Final Fantasy, Elden Ring.
- Référence herbe : Ghost of Tsushima, Breath of the Wild.

**Phase actuelle** : Pré-production — R&D — prototypage visuel — construction du pipeline.
- Priorités : 1. Shaders custom — 2. LookDev — 3. Post-processing — 4. VFX.

**Philosophie de production** : viser une qualité AAA tout en restant compréhensible, maintenable, itérable rapidement. Éviter les architectures inutilement complexes adaptées aux grandes équipes.

---

## 3. Cibles techniques

| Niveau | Configuration |
|--------|---------------|
| Recommandée | RTX 4070+ |
| Plancher acceptable | RTX 2060 |

Systèmes concernés par ces cibles : Océan, Herbe, Rendu avancé, Simulation GPU.

---

## 4. Environnement technique

### Stack

| Composant | Version |
|-----------|---------|
| Unity | 6000.4.1f1 |
| HDRP | 17.4.0 |
| SRP Core | 17.4 |
| Entities Graphics | 6.4.0 |
| Direct3D | 12 |

### Machine de développement

| Composant | Spécification |
|-----------|---------------|
| GPU | RTX 3080 Ti |
| CPU | Intel i9-13900K |
| RAM | 32 Go DDR5 6400 MT/s |

---

## 5. Conventions de code

### Arborescence & emplacement des scripts

- **Chaque système possède son propre path.** L'emplacement où créer les scripts est **défini par l'utilisateur en début de session**.
- **Ne jamais déduire un path.** Si l'utilisateur ne l'a pas fourni, **le demander explicitement** avant de créer quoi que ce soit.

### Namespaces

- Tous les namespaces commencent par **`Ombrage`**, puis se déclinent selon le script.
- Le namespace est **défini par l'utilisateur en début de session**, comme le path.
- Si l'utilisateur l'oublie : **le demander**, et vérifier qu'on utilise un **namespace existant** ou qu'un **nouveau namespace est réellement nécessaire**.

### Nommage

- **PascalCase, sans préfixe** (scripts, shaders, ScriptableObjects, `.hlsl`).

### HLSL

- Les fichiers `.hlsl` sont rangés **par thématique** dans `Assets/Shaders`.
- Pas de fichiers partagés connus à ce jour. *(Si un include partagé apparaît, le signaler pour définir une convention.)*

---

## 6. Validation technique

Les décisions doivent être validées par des **données réelles**, pas par une impression, une capture ou une supposition.

**Priorité des sources** : État live → Tests → Fichiers → Mesures → Hypothèses.

### Méthode de validation du projet

- Validation par un **mix** : rendu visuel dans l'éditeur, Unity Test Framework, `execute_code` via MCP — selon le cas.
- Travail systématique dans une **scène de test dédiée** lors de l'implémentation.
- **Tout passe exclusivement par l'éditeur Unity** (pas de CLI de build/validation).

### Critère « une étape est testable / validée »

Une étape est considérée validée quand :
1. elle **compile** ;
2. l'**objectif de l'étape est atteint**, visuellement ou mécaniquement.

**Outils temporaires** (ex. harnais de test) : autorisés uniquement s'ils sont isolés, clairement identifiés, et **supprimés après usage**.

---

## 7. MCP Unity

**Outil** : CoplayDev « MCP for Unity » — serveur UnityMCP, outils `mcp__UnityMCP__*`, ressources `mcpforunity://`.

**Règle** : avant toute modification de l'état de l'éditeur, lire les ressources MCP disponibles et vérifier l'état réel de l'éditeur. Ne jamais supposer l'état courant.

**Attention** : les captures caméra MCP ne sont **pas** une source fiable pour juger un rendu. Pour toute validation esthétique, **l'utilisateur est la référence visuelle**.

---

## 8. Préférences de rendu

Pour Terrain, Herbe, Océan, effets GPU et pipeline de rendu avancé :

- **Utiliser** : HLSL pur, Custom Pass, Compute Shader, shaders personnalisés.
- **Ne pas utiliser** : Shader Graph.

**Niveau d'explication HLSL** : l'utilisateur apprend le HLSL. Explications pédagogiques, progressives, détaillées, orientées compréhension, niveau visé **avancé**. Ne pas masquer les concepts importants par une simplification excessive.

---

## 9. Architecture des paramètres

Philosophie : **Valeur maîtresse → Valeurs dérivées en pourcentage → Presets qualité**.

- Une valeur principale, des ratios dérivés validés, des presets via ScriptableObjects.
- Paramètres avancés : regroupés, cachés derrière un foldout « Advanced », utilisés uniquement si nécessaire.
- **Interdits** : multiplication de sliders interdépendants, paramètres redondants, exposition excessive de détails internes.

---

## 10. Versionnage et sauvegardes

Le projet **n'utilise pas Git**. Versionnage exclusivement par **snapshots**.

- Structure de travail : `.backups/<NN_nom>/`
- Avant et après chaque phase importante → créer un snapshot.
- Une étape validée doit pouvoir être restaurée.
- Après validation complète → `backups/Systems/<Système>/`.

Objectifs : comparer les états, restaurer une version fonctionnelle, identifier clairement les changements.

---

## 11. Mémoire

Le système mémoire est volontairement minimal. **Quatre emplacements autorisés, aucun autre** :

1. `CLAUDE.md` — ce fichier (source de vérité).
2. `MEMORY.md` — **table des matières uniquement** (pointe vers les mémoires spécifiques).
3. `CLAUDE_MEMORY/` — connaissances durables du projet (un fichier par sujet).
4. Mémoire de session — `Library/ClaudeCodeGUI/memory/<session_id>/MEMORY.md` (temporaire, jamais source de vérité).

**Les règles complètes de gestion de la mémoire** (rôles précis, structure des fichiers, conventions de nommage, gestion des contradictions et de la taille, `PIEGES.md`) vivent dans :

➡️ **`CLAUDE_MEMORY/MEMORY_GUIDELINES.md`**

Consulter ce fichier avant toute création ou mise à jour de mémoire.

À la reprise d'une session après réouverture d'Unity, lire dans l'ordre : 1. `MEMORY.md` global — 2. `Library/ClaudeCodeGUI/memory/<session_id>/MEMORY.md`.

---

## 12. Priorité des informations

En cas de contradiction entre sources :

| Priorité | Source |
|----------|--------|
| 1 | Code réel du projet |
| 2 | État réel Unity |
| 3 | `CLAUDE.md` |
| 4 | `CLAUDE_MEMORY/` |
| 5 | Mémoire de session |
| 6 | Historique ancien |

La mémoire aide à comprendre le projet ; elle ne remplace **jamais** la réalité du code.

---

## Règle finale

Avant toute action importante : comprendre le contexte, vérifier les informations disponibles, demander confirmation si nécessaire, privilégier une solution propre et maintenable.

Le but n'est pas seulement de faire fonctionner le système, mais de construire un **pipeline durable, compréhensible et évolutif**.
