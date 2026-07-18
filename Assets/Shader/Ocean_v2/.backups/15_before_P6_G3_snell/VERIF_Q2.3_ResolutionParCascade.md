# Compte rendu de vérification — Conformité Q2.3 (résolution FFT par cascade)

> **Type :** analyse statique passive (lecture seule, aucune exécution, aucune modification de code).
> **Date :** 2026-07-04 · **Phase :** P1 (Ocean_v2) · **Critère unique :** conformité à la décision canonique Q2.3.
> **Verdict global : ✅ CONFORME** (sur la dimension « résolution par cascade », qui est le périmètre du brief).

---

## 1. Référentiel canonique (source de rang 1)

Fichier : `Assets/Shader/Ocean_v2/OCEAN_DECISIONS.md` — table 41/41, auto-déclarée **source de vérité de rang 1**.

- **Q2.2 (L46)** — Nombre de cascades : **« 4 cascades »** (fixe le compte de cascades à confronter).
- **Q2.3 (L47)** — Résolution FFT par cascade, verbatim :
  > « **Mixte / par cascade** (ex. **512²** sur les grandes cascades porteuses de la silhouette, **256²** sur les cascades de détail fin) — concentre la résolution là où elle se voit le plus. »
- **Q11.3 (L73)** — Déclinaison par niveau de qualité, verbatim :
  > « **Low** … réso FFT **tout-256²** (Q2.3) … **Ultra** … réso **mixte pleine 512²/256²** (Q2.3) … **Medium/High interpolent.** »
  > (Q11.3 énonce **4 niveaux** Low/Medium/High/Ultra.)

**Conséquence pour le critère :** le succès n'est PAS « ne pas être un mixte ». C'est **être précisément le mixte par cascade prescrit**, décliné Low = tout-256² et Ultra = mixte plein 512²/256², les niveaux intermédiaires interpolant.

---

## 2. Assignation réelle des résolutions (code)

Fichier : `Assets/Shader/Ocean_v2/Profile/Modules/OceanSpectrumModule.cs`
Méthode `BuildCascades`, tableau `resByQuality` — **L226-231** :

```csharp
switch (cascadeQuality)
{
    case CascadeQuality.Ultra: resByQuality = new[] { 512, 512, 256, 256 }; break; // L229
    case CascadeQuality.Low:   resByQuality = new[] { 256, 256, 256, 256 }; break; // L230
    default:                   resByQuality = new[] { 512, 256, 256, 256 }; break; // L231 (High)
}
```

- `c[i].res = resByQuality[i]` (**L246**) → une **résolution par cascade** indexée sur les 4 cascades (`new CascadeDesc[4]`, L235 ; boucle `i < 4`, L243).
- Groupage/allocation cohérents (corroboration, non décisionnels) : `group = (res==512)?0:1` (L247), `count512/count256` (L258-259), allocation `NewArray(512, count512, …)` vs `NewArray(256, count256, …)` (L317/L322), push `_OceanCascade[i] = (length, group, slice, res)` (L603-604). Aucune résolution divergente n'est réintroduite ailleurs.

### Tableau résolutions-par-cascade réellement codées

| Niveau (`CascadeQuality`) | Cascade 0 | Cascade 1 | Cascade 2 | Cascade 3 | Réf. code |
|---|---|---|---|---|---|
| **Ultra** | 512 | 512 | 256 | 256 | L229 |
| **High** (= `default`) | 512 | 256 | 256 | 256 | L231 |
| **Low** | 256 | 256 | 256 | 256 | L230 |

Le tooltip L29 (`Ultra = 2×512²+2×256², High = 1×512²+3×256², Low = tout-256²`) décrit la **même** répartition (notation « 2×512² » = 2 cascades en 512², pas 2 textures juxtaposées).

---

## 3. La cible « presets Beaufort » ne porte AUCUNE résolution par cascade

Le brief cible « les presets Beaufort ». Vérifié par lecture — **l'assignation n'y est pas** :

- `Assets/Shader/Ocean/Editor/BeaufortPresets.cs` (**Ocean v1**) : `BeaufortData` (L16-49) ne contient QUE des paramètres physiques/shading (windSpeedFactor, amplitude, choppiness, foam*, couleurs, depth…). **Zéro champ de résolution**, aucune notion de cascade.
- `Assets/Shader/Ocean/OceanSettings.cs` (**Ocean v1**) : résolution **UNIQUE** pour toutes les cascades — `OceanResolution resolution = _256` (L111), exposée via `ResolutionInt => (int)resolution` (L688). Pas de mixte par cascade.

**Localisation correcte de l'assignation :** elle vit **exclusivement** dans `CascadeQuality` / `resByQuality` de `OceanSpectrumModule.cs` (Ocean_v2), conformément à l'orthogonalité actée : presets d'état de mer (Q12.2, *apparence*) ≠ niveaux de qualité `OceanQualityProfile`/`CascadeQuality` (Q11.3, *coût*).

---

## 4. Confrontation code ↔ canonique — verdict par niveau

| Niveau | Code | Prescription Q11.3 / Q2.3 | Verdict (dimension résolution) |
|---|---|---|---|
| **Low** | {256,256,256,256} | « tout-256² » | ✅ **CONFORME** |
| **Ultra** | {512,512,256,256} | « mixte pleine 512²/256² » (512 sur silhouette, 256 sur détail) | ✅ **CONFORME** |
| **High** | {512,256,256,256} | « Medium/High interpolent » (pas de valeurs littérales) | ✅ **CONFORME** au *principe* d'interpolation Low↔Ultra |

**Verdict global : ✅ CONFORME** à Q2.3 (+ Q11.3) sur la dimension résolution par cascade.

### Levée d'ambiguïté (point pivot du brief)

Le brief oppose « Ultra = 512 + 256 » à « une répartition mixte 512²/256² ». **Ce n'est pas une opposition : ce sont deux descriptions du MÊME choix canonique.**
- « Ultra = 512 + 256 » = la cascade Ultra porte **du 512² ET du 256²** = exactement le « mixte / par cascade » de Q2.3 (silhouette en 512², détail fin en 256²).
- « Low = 256 seul » = le « tout-256² » de Q11.3.

Il n'existe donc **aucune « répartition mixte saisie à la main » non conforme** : le mixte présent dans le code **EST** la décision canonique. Le faux dilemme du brief provient vraisemblablement d'une lecture de « mixte » comme un défaut à rejeter, alors que la source de rang 1 le **prescrit** explicitement.

---

## 5. Notes d'attention (hors critère, signalées pour complétude)

1. **Niveau « Medium » absent de l'enum.** Q11.3 énonce 4 niveaux (Low/Medium/High/Ultra), mais `CascadeQuality` (L26) n'en expose que **3** (Ultra/High/Low). Aucune résolution-par-cascade n'est donc assignée à un « Medium ». Hors critère strict du brief (qui ne cite qu'Ultra/High/Low), mais à noter : le niveau canonique Medium n'a pas de mapping résolution propre dans le code.
2. **`default` = fourre-tout.** Le cas `default` (L231) mappe sur High et absorbe **toute** valeur autre qu'Ultra/Low (futur Medium, valeur sérialisée invalide, cast hors bornes). La conformité de « High » vaut donc *par défaut* ; toute extension de l'enum non explicitement câblée recevrait silencieusement {512,256,256,256}.
3. **Valeurs High inférées, non littérales.** Q11.3 ne prescrit pas de valeurs numériques pour High (« Medium/High interpolent »). Le {512,256,256,256} du code est jugé conforme au *principe* d'interpolation Low↔Ultra, non à une prescription littérale.
4. **Volet « nombre de cascades » de Q11.3 non audité.** Q11.3 couple pour Low « 4→2 cascades ET tout-256² ». Le code construit **toujours 4 cascades** (`new CascadeDesc[4]`, L235). La conformité « Low » ci-dessus porte **uniquement sur la résolution** (tout-256² ✅) ; le volet réduction du nombre de cascades est **hors périmètre** (le brief cible la résolution) et **n'a pas été évalué**.
5. **Chaîne d'écriture de `cascadeQuality` non inspectée.** La vérification porte sur le `switch` (branche d'assignation). Le renseignement en amont du champ `cascadeQuality` (mapping `OceanQualityProfile` → `CascadeQuality`, éventuel clamp plateforme) **n'a pas été tracé** : il sort du critère unique « assignation des résolutions par cascade » et relèverait d'un audit séparé.

---

## 6. Emplacements inspectés (traçabilité)

| Fichier | Lignes | Rôle |
|---|---|---|
| `Ocean_v2/OCEAN_DECISIONS.md` | L46 (Q2.2), L47 (Q2.3), L73 (Q11.3) | Référentiel canonique |
| `Ocean_v2/Profile/Modules/OceanSpectrumModule.cs` | L26 (enum), L29 (tooltip), L226-231 (`resByQuality`), L235-260 (BuildCascades), L317/322 (alloc), L603-604 (push globals) | Assignation réelle |
| `Ocean/Editor/BeaufortPresets.cs` | L16-49, L79-277 | Presets v1 — absence de résolution (fausse piste écartée) |
| `Ocean/OceanSettings.cs` | L111, L688 | v1 — résolution unique (pas de mixte par cascade) |

**Aucune modification de fichier de code n'a été effectuée** (contrainte passive respectée ; ce document est le seul artefact produit).
