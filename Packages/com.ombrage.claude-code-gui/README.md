# Claude Code GUI (Unity)

Interface UI Toolkit pour piloter **Claude Code** depuis l'éditeur Unity 6.4, inspirée de la GUI JetBrains, avec historique de sessions persistant.

## Installation

Le package est embarqué dans `Packages/com.ombrage.claude-code-gui`. Il dépend de
`com.unity.nuget.newtonsoft-json` (résolu automatiquement par le Package Manager).

Le binaire `claude` (Claude Code CLI) doit être installé et accessible. Sous Windows il
est recherché dans `%USERPROFILE%\.local\bin` ; sinon renseignez le chemin complet dans
**Settings → Exécutable Claude**.

## Ouverture

Menu **Window → Ombrage Tools → Claude Code (UITK)** (raccourci `Ctrl+Shift+Alt+K`).

## Fonctionnalités (Phase 1)

- Chat en streaming (texte, réflexion, appels d'outils, résultats) en bulles UI Toolkit, thèmes clair/sombre.
- Historique de sessions persistant (`Library/ClaudeCodeGUI/sessions`), avec import automatique
  des sessions de l'ancien outil (`Library/ClaudeCode/sessions`).
- Sidebar : recherche, favoris, renommage, duplication, suppression, export Markdown.
- Sélection de modèle (alias + IDs connus + récents + modèle personnalisé).
- Modes de permission (`default`, `plan`, `acceptEdits`, `bypassPermissions`).
- Fichiers de contexte (drag & drop ou sélection).
- Coût et tokens par échange, cumulés par session.
- AskUserQuestion (boîte de dialogue).

## Feuille de route

- Phase 2 : rendu Markdown + comparateur de diff.
- Phase 3 : slash commands + références `@fichier` + snippets.
- Phase 4 : AskUserQuestion inline, usage détaillé, revue du mode plan, rewind.
- Phase 5 : intégrations Unity (boucle d'erreurs de compilation, capture Scene/Game pour la
  vision, contexte automatique, création de scripts, capture console, checkpoints, panneau MCP).

## Note sur la détection des modèles

La CLI Claude Code n'expose pas de commande fiable « lister les modèles ». Le sélecteur
combine donc des alias (toujours à jour côté CLI), une liste curatée d'IDs connus, les
modèles récemment utilisés, le modèle par défaut lu dans `~/.claude.json`, et un champ libre.
