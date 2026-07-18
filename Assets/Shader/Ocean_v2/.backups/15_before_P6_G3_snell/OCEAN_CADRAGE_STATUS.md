# Cadrage Océan v2 — POINTEUR (document historique réduit)

> **⚠️ CE DOCUMENT N'EST PLUS UNE SOURCE DE VÉRITÉ.**
> Réduit à un pointeur le **2026-07-04** (revue documentaire). Son ancien résumé de cadrage
> **divergeait** des documents canoniques sur trois points et devenait un vecteur de confusion
> (notamment en pipeline multi-agents) :
>
> - **ordre des phases** (il donnait P3 = rendu surface ; l'ordre exécutif est P3 = absorption) ;
> - **résolution FFT** (« 256→512 » ; la décision Q2.3 est la **résolution mixte 512²/256² par cascade**) ;
> - **statut S9** (« V1.5 » présenté isolément ; Q1.5 a acté le **renforcement de la V1**, tension T1).
>
> Plutôt que de laisser circuler un résumé obsolète, tout le fond a été retiré. **Se référer
> exclusivement aux sources canoniques ci-dessous.**

## Où sont les décisions et le plan

| Besoin | Document canonique |
|---|---|
| **Décisions actées (41/41, Q1.1→Q13.3)** | **`OCEAN_DECISIONS.md`** ⭐ (source de RANG 1, dans le projet) |
| **Ordre & contenu des phases P0→P10** | **`OCEAN_ROADMAP.md`** (fait foi) |
| **Specs détaillées A–D** | `OCEAN_GUIDELINES.md` (Q1.1→Q5.3) + `MEMORY.md` session (Q6.1→Q13.3, miroir de travail) |
| **Audit antécédent** | `OCEAN_AUDIT.md` (Rév. 2) |
| **État d'avancement de l'implémentation** | `OCEAN_IMPLEMENTATION_STATUS.md` |

## Hiérarchie de vérité (rappel)

1. `OCEAN_DECISIONS.md` — décisions actées (rang 1).
2. `OCEAN_GUIDELINES.md` / `MEMORY.md` session — specs A–D.
3. `OCEAN_ROADMAP.md` — plan exécutif (ordre/contenu des phases).
4. `OCEAN_AUDIT.md` — audit.

⚠️ **JAMAIS** sourcer une décision depuis l'annexe « Défaut proposé » d'`OCEAN_GUIDELINES.md`
(recommandations PRÉ-décision, plusieurs contredisent les choix effectifs).
