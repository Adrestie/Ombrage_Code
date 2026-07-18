# Le système utilise-t-il un workflow agentique ? — Réponse

> Tâche d'introspection sur la configuration agentique du harness Claude Code qui
> orchestre le développement d'Ombrage (et non sur le code C#/Unity du jeu).
> Chaque entrée est tracée à sa **source** (system-reminder de la session courante
> vs script lu sur disque) et à son **statut** (capacité disponible maintenant vs
> exécution passée trouvée sur disque ; observé vs inféré).
>
> ⚠️ *Snapshot de la session courante* : l'inventaire des types d'agents et des
> skills est un instantané, susceptible de varier selon la version du harness.

---

## 1. Réponse oui / non (qualifiée)

**OUI — mais il faut distinguer deux niveaux :**

| Niveau | Workflow agentique ? | Détail |
|---|---|---|
| **Code applicatif Ombrage** (C# / Unity, le jeu) | **NON** | Aucun système d'agents ni de workflow agentique n'est embarqué dans la base de code du jeu. Pas de répertoire `.claude/` dans le projet lui-même. *(Source : exploration du dépôt.)* |
| **Harness Claude Code** (ce qui orchestre le développement) | **OUI** | Système agentique actif : types d'agents spécialisés + skills invocables + outil `Workflow` d'orchestration multi-agents. *(Source : system-reminder courant + scripts sur disque.)* |

C'est la distinction principale à poser : le **jeu** ne contient pas d'IA agentique ;
l'**outil de développement** qui aide à le construire, lui, en est un.

---

## 2. Types d'agents disponibles (source faisant foi : system-reminder courant)

Snapshot de la session courante. Ces agents sont lançables via l'outil `Agent`.

| Type d'agent | Rôle |
|---|---|
| **claude** | Agent fourre-tout (catch-all) pour toute tâche ne relevant pas d'un agent plus spécifique ; accès à tous les outils. |
| **Explore** | Agent de recherche **read-only** pour balayages larges multi-fichiers : localise du code (lit des extraits) sans l'auditer. Profondeur paramétrable (« medium » / « very thorough »). |
| **general-purpose** | Agent généraliste pour questions complexes, recherche de code et tâches multi-étapes ; accès à tous les outils. |
| **Plan** | Architecte logiciel : conçoit des plans d'implémentation, identifie les fichiers critiques, pèse les arbitrages. |
| **statusline-setup** | Configure la status line de l'utilisateur (outils Read, Edit uniquement). |

> *Remarque : ces types sont génériques au harness. À l'intérieur d'un workflow,
> les sous-agents sont tous de type « workflow-subagent » au runtime ; leur rôle
> effectif est défini par le prompt qui leur est passé (voir §6).*

---

## 3. Skills disponibles + lien skill ↔ workflow (source : system-reminder courant)

Les **skills ne sont pas des « agents »** au sens strict : ce sont des capacités
invocables. **Point clé :** certaines skills *s'exécutent comme des workflows
agentiques* — c'est le cas de `deep-research` (fan-out de recherche web +
vérification adversariale + synthèse). Cette section prépare donc le §5.

| Skill | Rôle (fonctionnel) |
|---|---|
| **deep-research** | **Est un workflow agentique** : éclate une question en angles, lance des recherches web parallèles, vérifie les affirmations de façon adversariale (3 votants), synthétise un rapport sourcé. |
| **update-config** | Configure le harness via `settings.json` (permissions, hooks, variables d'env). |
| **keybindings-help** | Personnalise les raccourcis clavier (`keybindings.json`). |
| **verify** | Vérifie qu'un changement de code fait bien ce qu'il doit, en lançant l'app et en observant le comportement. |
| **code-review** | Relit le diff courant (bugs, simplifications) ; en mode « ultra », revue multi-agents dans le cloud. |
| **simplify** | Nettoie le code modifié (réutilisation, simplification, efficacité) — qualité seulement, pas de chasse aux bugs. |
| **fewer-permission-prompts** | Génère une allowlist de permissions à partir des transcripts pour réduire les prompts. |
| **loop** | Exécute un prompt / une slash-command à intervalle récurrent. |
| **schedule** | Crée/gère des agents cloud planifiés (cron / routines). |
| **claude-api** | Référence API Claude / SDK Anthropic (modèles, pricing, params). |
| **run** | Lance et pilote l'app du projet pour voir un changement fonctionner. |
| **init** | Initialise un fichier `CLAUDE.md` de documentation du dépôt. |
| **review** | Relit une pull request GitHub. |
| **security-review** | Revue de sécurité des changements en cours sur la branche. |

---

## 4. Capacité « workflow » disponible MAINTENANT (source : system-reminder courant)

Statut : **disponible maintenant**, lançable à la volée.

- **Outil `Workflow`** — orchestration multi-agents **déterministe** (fan-out,
  pipeline, parallèle, barrières) ; les sous-agents tournent en parallèle sous un
  plafond de concurrence.
- **Skill `deep-research`** — workflow agentique pré-packagé invocable directement.

> Aucun fichier `.claude/workflows/*.js` pré-défini dans le projet Ombrage : les
> workflows custom sont écrits **ad-hoc** et persistés sous le répertoire de session.

---

## 5. Instances de workflows OBSERVÉES sur disque (statut : exécutions passées)

Statut distinct : **exécutions historiques** trouvées dans des sessions antérieures
sur disque — **pas** des capacités configurées garanties relançables. Identités
**vérifiées par lecture directe des scripts** (`meta.name` authentique).

Il y a **2 exécutions de la même skill `deep-research` + 1 workflow custom**, et
**non** 3 finalités distinctes :

| Run (id) | Identité réelle (`meta.name`) | Nature |
|---|---|---|
| `wf_8bd61643-e45` | **`deep-research`** | 1er run de la skill deep-research (question différente). |
| `wf_51101727-f92` | **`deep-research`** | 2e run de la **même** skill (même script, args/question différents). |
| `wf_1d0548ab-bf8` | **`grass-games-targeted`** | Workflow **custom** : recherche web ciblée sur la végétation/le rendu d'herbe de 10 jeux pondérés par importance (`stars`), puis vérification adversariale par jeu. |

**Effectifs — bornes issues des scripts** (et non les « 200/100 agents » de
l'exploration, qui étaient un artefact de comptage de fichiers ×2) :

- **`deep-research`** : `1` (scope) + `≤6` (search, 1/angle) + `≤15` (fetch/extract)
  + `≤25 × 3` (verify, 3 votants/claim) + `1` (synthèse) ≈ **~98 agents au maximum**.
  *(Confirmé par le code : `agentCalls = 1 + angles + sources + voted*3 + 1`, avec
  `MAX_FETCH=15`, `MAX_VERIFY_CLAIMS=25`, `VOTES_PER_CLAIM=3`.)*
- **`grass-games-targeted`** : `10` (recherche, 1/jeu) + `≤10` (verify, 1/jeu) ≈
  **~20 agents au maximum**.

> Ce sont des **bornes du script**, pas des effectifs réellement consommés par run.
> Un chiffre exact nécessiterait de recompter les `agent-*.meta.json` uniques de
> chaque exécution.

---

## 6. Rôles des sous-agents — OBSERVÉS (les scripts sont lisibles)

Les scripts documentant explicitement rôles (corps du script) et ordre
(`meta.phases`), ce qui suit est **observé, pas inféré**. L'ordre d'orchestration
est donné **en contexte** des rôles (le livrable demandé reste « liste des agents +
rôle »).

### `deep-research` — pipeline : Scope → Search → Fetch+Extract → Verify (3 votes) → Synthesize

| Sous-agent (rôle) | Ce qu'il fait |
|---|---|
| **Scoper** | Décompose la question en 3–6 angles de recherche complémentaires. |
| **Web Searcher** (1 par angle, ≤6) | Lance une `WebSearch` sur son angle, renvoie les 4–6 résultats les plus pertinents. |
| **Source Extractor** (Fetch, top 15) | `WebFetch` de la page, évalue la qualité de source, extrait 2–5 **affirmations falsifiables** avec citation directe. |
| **Adversarial Claim Verifier** (3 votants : voter 1/3, 2/3, 3/3) | Chaque votant tente de **réfuter** la claim ; **2 réfutations sur 3 tuent l'affirmation** (`REFUTATIONS_REQUIRED=2`). Défaut = réfuté en cas d'incertitude. |
| **Synthesizer** | Déduplique sémantiquement, regroupe en findings, assigne une confiance, cite les sources, rédige résumé + caveats + questions ouvertes. |

*Détails de pipeline observés : dédup d'URL + budget de fetch en cours de route ;
barrière intentionnelle avant la phase Verify (le pool de claims doit être complet
avant le classement). Les votes nuls (skip/erreur) comptent comme abstention.*

### `grass-games-targeted` — pipeline : Research (1/jeu) → Verify adversarial (1/jeu)

| Sous-agent (rôle) | Ce qu'il fait |
|---|---|
| **Graphics-tech Researcher** (1 par jeu) | `WebSearch`/`WebFetch` sur le rendu végétation/herbe du jeu (scatter, LOD/impostors, caméra aérienne/aliasing, GPU-driven, mémoire). Profondeur de recherche scalée par `stars` (5–6 recherches pour ★★★, jusqu'à 2–3 pour ☆). Interdiction de fabriquer : `foundInfo=false` si rien de crédible. |
| **Adversarial Verifier** (1 par jeu, claims centrales) | Tente de réfuter chaque claim centrale (≤8) : verdict `solid` / `weak` / `refuted`, défaut `weak` faute de corroboration indépendante. |

---

## 7. Synthèse — limites et traçabilité

- **Réponse :** OUI pour le harness de développement (agents typés + skills + outil
  `Workflow`) ; NON pour le code du jeu Ombrage lui-même.
- **Sources tracées :** types d'agents et skills = **system-reminder courant**
  (faisant foi) ; identités/rôles/pipeline des workflows = **scripts lus sur disque**
  (observé). Les workflows passés sont des **exécutions historiques**, distinctes des
  **capacités disponibles maintenant** (outil `Workflow` + skill `deep-research`).
- **Limite résiduelle :** les **effectifs réels** par exécution ne sont pas reportés
  ici (seules les bornes du script le sont) — un recomptage des `agent-*.meta.json`
  serait nécessaire pour un chiffre exact. L'inventaire types d'agents/skills est un
  **snapshot** de la session courante.
- **Zéro agent inventé** : toutes les identités sont tirées des `meta.name` réels
  des scripts ou de la liste système courante.
