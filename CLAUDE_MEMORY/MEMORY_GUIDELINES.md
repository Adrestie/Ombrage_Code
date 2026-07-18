# MEMORY_GUIDELINES — Règles de gestion de la mémoire

Ce fichier décrit **comment gérer la mémoire du projet Ombrage**. Il est référencé par `CLAUDE.md` (section 11) et ne doit être consulté que lors d'une création ou mise à jour de mémoire — il n'a pas besoin d'être relu à chaque interaction.

---

## 1. Architecture mémoire

Le système mémoire est volontairement minimal. **Quatre emplacements autorisés** :

1. `CLAUDE.md`
2. `MEMORY.md`
3. `CLAUDE_MEMORY/`
4. Mémoire de session GUI

**Règle** : aucune autre mémoire persistante ne doit être créée.

---

## 2. `MEMORY.md` global

**Rôle** : table des matières uniquement.

**Fonctions** :
- Référencer les sujets existants.
- Pointer vers les mémoires spécifiques.

**Exemple** :
- Rendering → `CLAUDE_MEMORY/RENDERING.md`
- Grass System → `CLAUDE_MEMORY/GRASS_SYSTEM.md`
- Ocean → `CLAUDE_MEMORY/OCEAN.md`

**`MEMORY.md` ne contient jamais** : décisions techniques, architecture, historique, résumé de session, TODO, état d'avancement.

Il doit rester minimal et concis. **Interdiction** d'écrire directement des informations de projet dans `MEMORY.md`.

---

## 3. `CLAUDE_MEMORY/`

**Rôle** : connaissances durables du projet.

**Structure** : chaque sujet important possède son propre fichier.

**Exemple** :
```
CLAUDE_MEMORY/
├── RENDERING.md
├── GRASS_SYSTEM.md
├── OCEAN.md
├── BRG.md
└── PIEGES.md
```

**Convention de nommage** (obligatoire) : majuscules, underscore, extension `.md`.

| Acceptés | Refusés |
|----------|---------|
| `GRASS_SYSTEM.md` | `GrassSystem.md` |
| `RENDER_PIPELINE.md` | `grass_notes.md` |
| `OCEAN_SIMULATION.md` | `Grass.md` |

---

## 4. Contenu des mémoires spécifiques

**Les fichiers dans `CLAUDE_MEMORY/` contiennent uniquement** :
- Décisions d'architecture
- Conventions
- Contraintes
- Choix techniques
- Informations importantes à conserver
- Raisons des choix effectués

**Ils ne contiennent jamais** :
- Journal de développement
- Résumé de session
- Historique complet
- Conversations
- TODO temporaires
- Essais abandonnés

**Principe** : une mémoire décrit l'état actuel validé du système. Elle n'est **pas** un changelog.

---

## 5. Création ou mise à jour d'une mémoire

**Processus lorsqu'une connaissance durable apparaît** :

1. Identifier le sujet concerné.
2. Chercher une mémoire existante.
3. Mettre à jour cette mémoire si elle existe.
4. Créer un nouveau fichier uniquement si nécessaire.
5. Ajouter l'entrée correspondante dans `MEMORY.md`.
6. Garder uniquement le contexte temporaire dans la mémoire de session.

**Avant de créer une nouvelle mémoire** :
- vérifier les doublons ;
- vérifier les sujets proches ;
- préférer compléter un fichier existant.

---

## 6. Gestion des contradictions

**Principe** : une mémoire ne doit jamais contenir deux vérités concurrentes.

**Si une nouvelle décision remplace une ancienne** :
- mettre à jour la mémoire ;
- supprimer l'information obsolète ;
- conserver l'ancien choix uniquement s'il explique une contrainte actuelle.

**Règle** : la mémoire doit toujours représenter l'état actuel du projet.

---

## 7. Mémoire de session

**Statut** : appartient exclusivement à l'environnement d'exécution.

**Emplacement** : `Library/ClaudeCodeGUI/memory/<session_id>/MEMORY.md`

**Lorsqu'une session est reprise après une réouverture d'Unity**, lire dans cet ordre :
1. `MEMORY.md` global
2. `Library/ClaudeCodeGUI/memory/<session_id>/MEMORY.md`

**Contenu autorisé** :
- Résumé synthétique de la session
- Contexte immédiat
- Travail effectué
- Problèmes en cours
- Prochaines actions

**Contenu interdit** :
- Décisions durables
- Architecture validée
- Conventions permanentes
- Documentation technique définitive

**Règle** : toute information durable doit être transférée dans `CLAUDE_MEMORY/`.

---

## 8. Structure obligatoire des mémoires spécifiques

**Chaque fichier dans `CLAUDE_MEMORY/`** doit rester concis et suivre cette structure :

```markdown
# NOM_DU_SYSTEME

Création : YYYY-MM-DD
Dernière modification : YYYY-MM-DD

## État actuel
Description courte du système actuel.

## Décisions d'architecture
### Décision : Nom
Date : YYYY-MM-DD
Choix : Description de la décision.
Raison : Pourquoi cette décision a été retenue.

## Contraintes
Contraintes techniques, architecture, production ou performance.

## Choix techniques
### Technologie ou approche
Utilisation : Pourquoi cette technologie est utilisée.
Justification : Pourquoi cette solution a été choisie plutôt qu'une autre.

## Interfaces
Relations avec les autres systèmes du projet.

## Versions
Historique minimal des changements structurants uniquement.
Format : [YYYY-MM-DD] Modification majeure par @auteur

## Liens
Références internes ou externes.
```

---

## 9. Gestion de la taille des mémoires

**Principe** : les mémoires doivent rester lisibles et exploitables par un agent. Elles ne doivent jamais devenir des documents exhaustifs contenant toute l'histoire du développement.

**Si une mémoire devient trop grande** :
- découper par sous-système ;
- créer des mémoires spécialisées ;
- mettre à jour `MEMORY.md`.

**Exemple** :

| Avant | Après |
|-------|-------|
| `GRASS_SYSTEM.md` | `GRASS_SYSTEM.md`<br>`GRASS_RENDERING.md`<br>`GRASS_SIMULATION.md` |

---

## 10. Fichier `PIEGES.md`

**Fichier** : `CLAUDE_MEMORY/PIEGES.md` — exception autorisée.

**Contenu** : uniquement les pièges techniques durables.

**Exemples** :
- Comportement inattendu Unity
- Limitations HDRP
- Problèmes Entities Graphics
- Problèmes BRG
- Problèmes shaders
- Incompatibilités connues
- Erreurs difficiles à diagnostiquer

**Règle** : tout nouveau piège technique confirmé doit être ajouté dans `CLAUDE_MEMORY/PIEGES.md`.

**`PIEGES.md` ne contient jamais** :
- Journal de debugging
- Historique complet d'un bug
- Compte rendu de session
- Hypothèses non vérifiées

Il contient uniquement la **connaissance réutilisable**.

---

## 11. Règles de documentation

**Privilégier** : l'état actuel, les décisions, les contraintes, les raisons importantes.

**Éviter** : les explications historiques longues, les discussions, les essais abandonnés, les détails sans valeur future.

**Une bonne documentation répond aux questions** :
- Qu'est-ce qui existe ?
- Pourquoi ce choix ?
- Quelles contraintes doivent être respectées ?
- Comment éviter les erreurs futures ?
