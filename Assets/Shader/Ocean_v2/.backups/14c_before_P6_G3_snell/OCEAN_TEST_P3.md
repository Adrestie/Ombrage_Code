# OCEAN_TEST_P3.md — Protocole de validation (Phase P3 — Absorption)

> **P3 livre le module `absorption`** (Q6.1/Q6.2/Q6.3) : Beer-Lambert spectral `I = I₀·exp(−σ·d)` par
> canal, 3 ancres Jerlov Ia/II/III (assets `WaterAbsorptionProfile`), master `waterType [0..1]`
> (interpolation par segments), push NON cumulatif du global **UNIQUE** `_WaterAbsorption`,
> consommation surface = albédo `exp(−σ·d)` sur la profondeur perçue (repli `_BaseColor`).
>
> **Budget cible (ROADMAP §2.2)** : 0.10–0.18 ms Low→Ultra. Coût réel ≈ ε (1 `exp` + 1 `lerp` par
> pixel + 3 uniforms) — **pas de gate build à P3** ; le cumul sera re-contrôlé au prochain relevé
> build (T1, P5/P6).

## (a) Pré-requis

- P2 validée intégralement (gates 0-bis + 1–4, budget verrouillé 2.564 ms — `OCEAN_TEST_P2.md` §(l)).
- Filets de rollback en place : `.backups/08_before_P3_absorption/` (état P2, 99 fichiers), 
  `.backups/08b_before_P3_k4_fix/` (état k3 avant le correctif k4 de cohérence des sliders) **et**
  `.backups/08c_before_P3_color_fix/` (miroir complet 117 fichiers avant le correctif COULEUR
  2026-07-06 : `kDepthRate` 0.12→0.04 + recalibrage des ancres Jerlov ; MANIFEST + HASHES.sha256).
  Le slot **09** reste réservé à l'état **validé utilisateur**.

## (b) GATE 0-bis — recompilation (code C# **et** shader touchés)

1. **Réimport** : `Shaders/OceanSurfaceData.hlsl` (et `OceanSurface.shader` par dépendance) → clic
   droit **Reimport** ; laisser le domain reload se terminer.
2. **Console VIDE** : 0 erreur / 0 warning (C# et shader).
3. **Test Runner EditMode** `Ombrage.OceanFeatures.Tests` → **16/16 verts** (12 tests P2 + 4 nouveaux
   `OceanAbsorptionTests` : ancres exactes / linéarité de segment / clamp / **push non-cumulatif +
   restore**).
   > Le test `Apply_PushesSingleGlobal_NonCumulative_AndRestores` est la **vérification exécutable**
   > du critère P3 « push vérifié non cumulatif » (anti-bug n°1), en complément de la revue de code.

> **✅ (b) PRÉ-VALIDÉ PAR L'AGENT (2026-07-05, via MCP Unity)** : recompilation forcée → **Console
> 0 erreur / 0 warning périmètre océan** (2 correctifs appliqués au passage : consts de chemins
> d'ancres `internal`→`public` — CS0117 depuis l'assembly éditeur — et `GetInstanceID`→`GetEntityId`
> dans `OceanProfileEditor.cs` — dépréciation Unity 6000.4) ; **EditMode 16/16 verts** (Test Runner
> piloté par MCP ; une 1ʳᵉ run pré-domain-reload n'avait vu que 12 tests — relancée → 16/16).
> Il te reste : un coup d'œil de confirmation (Console propre) puis (c)2 → (f).
> NB : 9 warnings CS0618 subsistent côté `TerrainLitCustom` (hors périmètre océan, ticket consigné).

## (c) Mise en place (one-shot)

1. Menu **`Ombrage/Ocean/Create Water Absorption Anchors (Ia, II, III)`** → 3 assets sous
   `Assets/Shader/Ocean_v2/Profiles/` (log vert). **Create-if-missing** : relancer le menu n'écrase
   JAMAIS un asset (protège un futur calibrage).
   > **✅ FAIT PAR L'AGENT (2026-07-05, via MCP)** : menu exécuté, 3 assets créés et **vérifiés sur
   > disque** (`WaterAbsorption_{Ia,II,III}.asset` + `.meta`), logs verts conformes.
2. Dans le **profil océan de la scène de test** : vérifier que le module **`Underwater/Absorption`**
   est présent (sinon Add Module) et **`active` coché**. Les 3 ancres s'**auto-résolvent** (éditeur) —
   **sauver le profil** (Ctrl+S) pour **sérialiser les références** (exigence build, même caveat que le
   correctif (f) de P2).
3. Modules actifs requis : `Spectrum` + `Surface` + `Absorption`.

## (d) GATE VISUEL — couleur cohérente & commutation LIVE (critère principal)

Caméra au-dessus de l'eau, environnement de la scène de gate (ciel + soleil).

> **⚠ Deux axes INDÉPENDANTS (clarification 2026-07-06).** La formule est une réflectance montante
> `b_b/σ × maturité(d)`. Le **HUE** (teinte du type d'eau) est porté **par `waterType`** (donc par le
> triplet σ de l'ancre, via `b_b/σ`) ; le slider **`perceivedDepth` ne change PAS le hue** à `waterType`
> fixe — il fait mûrir la couleur du **bleu de surface (proche) vers la couleur asymptotique du type
> (profond)**, en gagnant en saturation/en s'assombrissant. La progression « bleu → vert-brun » est
> donc l'axe **`waterType`**, pas l'axe `perceivedDepth`. Juger chaque axe séparément :

**Axe 1 — `waterType` (le HUE), à `perceivedDepth` élevé (≈ 40–50) :**
1. `waterType = 0` (Ia) → eau **bleu profond** (pas bleu ciel, pas émeraude). Le bleu domine.
2. Tirer **`waterType` 0 → 0.5 → 1 en continu** → glissement **bleu profond → bleu-vert côtier →
   vert sombre**, **LIVE**, sans à-coup ni recompilation (interpolation par segments, ancre II à 0.5).
   À `waterType = 1`, `perceivedDepth = 50` : **vert sombre** (le canal bleu est écrasé par σ_B fort
   des ancres côtières — pas d'émeraude ni de cyan sombre).
3. **Vérifier la continuité fine** : balayer aussi `waterType = 0.25` et `0.75` (pas seulement
   0/0.5/1) → aucun **pic** parasite de teinte ou de brillance entre les ancres (interpolation σ
   monotone → hue attendu monotone).

**Axe 2 — `perceivedDepth` (la MATURITÉ, plus de plateau), à `waterType` fixe = 0, 0.5 **et** 1 :**
4. Tirer **`perceivedDepth` 0.1 → 50** : changement de couleur **VISIBLE et PROGRESSIF sur TOUTE la
   plage**, pour les 3 types — **delta RGB non négligeable entre 25 et 50 m** (plus AUCUN plateau ;
   le bug k4 résiduel saturait dès d≈25 m pour II/III). `kDepthRate` abaissé 0.12→0.04 (knee ≈ 12 m).
5. **Contrôle de la borne basse** : à `perceivedDepth = 0.1`, la surface doit rester **lisible**
   (colonne immature, teinte bleutée pâle) et **NON quasi-noire**. La baisse de `kDepthRate`
   assombrit le proche → si l'ouverture de scène paraît trop sombre, remonter d'abord le défaut
   `perceivedDepth` (déjà porté à 15) puis, si besoin, `kUpwellingScale` (voir note de tuning ci-dessous).
   > ⚠ **Reclampage** : tout profil sérialisé avec `perceivedDepth > 50` est ramené à **50** dès la
   > prochaine ouverture (OnValidate) — volontaire, la plage 50–200 était optiquement saturée.

**Axe 3 — commutation de profil :**
6. **Profil utilisateur** : `Create > Ombrage/Ocean/Water Absorption Profile`, modifier ses σ,
   l'assigner à la place d'une ancre → la couleur suit (profils commutables, Q6.2).

**Critère : (Axe 1) hue bleu→bleu-vert→vert sombre monotone sur `waterType` ; (Axe 2) graduation
continue sans plateau sur `perceivedDepth` pour CHAQUE type, borne basse lisible ; (Axe 3) live.**

> **🔧 Note de tuning au gate (2026-07-06).** `kDepthRate` est une **constante de compilation HLSL**
> (`OceanSurfaceData.hlsl`) : chaque ajustement = édition du `.hlsl` → **save → attendre la recompile
> shader → observer** (pas de tuning « live » sans ajouter un `SetGlobalFloat` de debug). Si plusieurs
> passes sont nécessaires, balayer 3 candidats (**0.03 / 0.04 / 0.06**) en autant de recompiles puis
> figer. Le **hue** se règle en éditant les σ des 3 ancres (`Profiles/WaterAbsorption_*.asset`, live,
> pas de recompile) ; après édition d'un `.asset`, **forcer un réimport** et vérifier les σ chargés
> dans l'inspecteur du module avant de juger (éviter de calibrer sur des valeurs périmées). **Invariant
> à préserver sous tout recalibrage :** `max(b_b/σ × kUpwellingScale) < 1` sur chaque canal des 3
> ancres (sinon `saturate()` clippe et fige le canal) — baisser `kUpwellingScale` plutôt que laisser clipper.

> **⚠ Banc + correctif k3 (2026-07-05).** Le 1ᵉʳ passage de ce gate a ÉCHOUÉ (eau turquoise, Ia≈II) :
> la transmittance `exp(−σ·d)` ne produit pas de bleu océan → **modèle corrigé** en réflectance
> MONTANTE `b_b_pure/σ × (1−exp(−2σ·d))` (constantes physiques, cf. `OceanSurfaceData.hlsl`) + ancres
> II/III recalibrées. **Attendu vérifié par captures MCP** (`Assets/Screenshots/P3_v4_{Ia,II,III}_*.png`) :
> **Ia bleu océan franc · II bleu-vert côtier · III vert sombre**. NB : `Ocean Debug.unity` n'a **ni
> Volume ni ciel** — juge la couleur sous un environnement (ciel + exposition adaptée à TON soleil ;
> l'env de gate Fixed EV10 est calibré pour la scène de GATE, pas un soleil 100 000 lux).

## (e) GATE REPLI / TOGGLE (branche uniforme, zéro variant)

1. **Décocher `active`** du module Absorption → la surface **revient à `_BaseColor`** (bleu-gris
   provisoire P2) dès la frame suivante — aucun flash, aucun recompile (interrupteur
   `_OceanAbsorptionEnabled` poussé par la surface).
2. Recocher → couleur absorption de retour.
3. **Désactiver puis réactiver l'`OceanSystem`** (GameObject) → même état au retour (globals
   restaurés neutres au Teardown, re-poussés au Setup — anti-bug n°1).

## (f) NON-RÉGRESSION P2 (contrôle de fumée rapide)

- **MV** : caméra fixe + vagues animées → MV non nuls ; en translation/rotation → pas de ghosting
  TAA (aucun code MV touché).
- **Aller-retour presets** `Ultra → Low → Ultra` : Console vide, pas de traînée MV persistante.
- Tessellation/déplacement inchangés à l'œil.

## (g) Revue anti-bug n°1 (faite par l'agent au livrable)

- **UN SEUL point de push** de `_WaterAbsorption` dans tout le projet = `OceanAbsorptionModule.Apply`
  (via `ctx.globals`, SET pur) ; `_OceanAbsorptionDepth` idem ; `_OceanAbsorptionEnabled` poussé par
  `OceanSurfaceModule.BindAbsorption` (via `ctx.globals` aussi) ; **aucun σ en dur** hors assets-ancres ;
  Teardown → `RestoreAll()` → neutres. Doublé par le test EditMode de (b).

## (k4) Banc — découplage profondeur (audit de cohérence + correctif, 2026-07-06)

**Livrable 1 — audit de cohérence P3.** Revue du module d'absorption (shader + module + profil + câblage) :

- **(a) Modèle** — Beer-Lambert / réflectance montante `b_b/σ × maturité(d)` : **correct** (constantes
  Rayleigh `kBackscatterSpectrum`, `kUpwellingScale` ; aucune régression du modèle k3).
- **(b) Anti-bug n°1** — push **UNIQUE** non cumulatif confirmé par revue : `_WaterAbsorption` **et**
  `_OceanAbsorptionDepth` poussés SEULEMENT par `OceanAbsorptionModule.Apply` (via `ctx.globals`, SET
  pur, jamais `+=`/`*=`) ; `_OceanAbsorptionEnabled` poussé SEULEMENT par `OceanSurfaceModule.BindAbsorption` ;
  `RestoreAll()` → neutres. Doublé par le test EditMode `Apply_PushesSingleGlobal_NonCumulative_AndRestores`.
  **Le correctif k4 n'ajoute AUCUN global, slider ni push** (`kDepthRate` = constante shader) → anti-bug n°1 intact.
- **(c) σ jamais en dur** — les 3 ancres `WaterAbsorption_*.asset` restent la seule source des σ ;
  `EvaluateSigma` inchangé.
- **(d) σ = 0 → pas de NaN** — le shader planche `sigma = max(_WaterAbsorption.rgb, 1e-4)` AVANT de
  dériver `sigmaMean` (dot avec poids Rec.709 > 0 → ≥ 1e-4, jamais 0) et `sigmaNorm` → aucune division
  par zéro même pour un profil éditable à σ≈0 ([Min(0f)] côté `WaterAbsorptionProfile`).
- **(e) CAUSE RACINE quantifiée** — la maturité naïve `1 − exp(−2·σ·d)` indexe son knee (≈ 1/(2σ))
  sur la **magnitude** de σ, donc sur la turbidité :

  | Type | σ (R,G,B) m⁻¹ | knee bleu ≈ 1/(2σ_B) | 2·σ_B·d à d=8 | 2·σ_B·d à d=200 |
  |---|---|---|---|---|
  | Ia | (0.36, 0.041, 0.028) | ≈ 18 m | 0.45 | 11.2 |
  | II | (0.45, 0.09, 0.12) | ≈ 4 m | 1.9 | 48 |
  | III | (0.6, 0.2, 0.65) | ≈ 0.8 m | **10.4** (déjà 1−e⁻¹⁰·⁴ ≈ 1) | 260 |

  → pour II/III, tous les canaux saturent **avant d≈8 m** : `perceivedDepth` n'a plus aucun effet de 8 à
  200 (bug rapporté). **Conclusion : correction ciblée, pas de refonte.**

**Correctif k4 (livrables 2 & 3).**

- **Shader** (`OceanSurfaceData.hlsl`) : profondeur optique **NORMALISÉE** par σ̄ (moyenne luminance
  Rec.709 des mêmes σ planchés) → `sigmaNorm = σ/σ̄`, `maturité = 1 − exp(−2·σ_norm·kDepthRate·d)`.
  Le knee du canal **moyen** devient ≈ 1/(2·kDepthRate) **indépendant de la turbidité**. Invariants
  préservés : d→0 ⇒ chromie `b_b` (bleu) ; d→∞ ⇒ `b_b/σ` (couleur asymptotique du type **inchangée**
  vs captures k3 : Ia bleu · II bleu-vert · III vert sombre). `kDepthRate = 0.12` (knee moyen ≈ 4 m) —
  **⚠ SUPERSÉDÉ par (k5) : abaissé à 0.04** (knee ≈ 12 m) car 4 m saturait encore II/III avant 25 m —
  **constante shader tunable au gate**, pas de nouveau paramètre. NB : le knee « uniforme » vaut pour
  le canal moyen ; les canaux à fort σ_norm (rouge Ia, rouge+bleu III) saturent plus tôt, le bleu de Ia
  (σ_norm≈0.26) reste en transition sur toute la plage — c'est ce spread qui donne la richesse de teinte.
- **Bornes slider** (`OceanAbsorptionModule.cs`) : `perceivedDepth` [0.1..200] → **[0.1..50]** (`Range` +
  clamp `OnValidate` + tooltip). `waterType` [0..1] inchangé.

## (k5) Banc — correctif COULEUR (plateau résiduel + palette, 2026-07-06)

Les gates (d)/(e) étaient restées **NON validées** après k4 : plateau persistant sur `perceivedDepth`
[25..50] (lectures user : Ia 25 m `(0,37,93)` ≈ 50 m `(0,40,108)`) et palette hors cible (Ia trop clair,
III `(1,18,10)` = vert-bleu trop faible au lieu d'un vert sombre franc). **Diagnostic recadré : problème
de CALIBRAGE, pas de structure** — la structure d'upwelling `b_b/σ × maturité × kUpwellingScale` est saine
(aucun clip : max canal asymptotique = Ia bleu 0.71 < 1) et le remplacement par `R_inf = b_b/(a+b_b)`
est **ÉCARTÉ** (donnerait un cyan-turquoise ≈ (0.36,0.91,0.97), unités incohérentes). **K4 CONSERVÉ**
(le retirer réintroduit le plateau qu'il corrigeait sur II/III). Deux leviers seulement :

- **Plateau → `kDepthRate` 0.12 → 0.04** (`OceanSurfaceData.hlsl`) : le knee du canal moyen passe de
  ≈ 4 m à ≈ 12 m → la maturité reste en transition jusqu'à 50 m sur TOUS les types (Δmaturité bleu Ia
  25→50 m 0.166→0.241 ; Δvert III 0.023→0.202). Constante shader, **aucun global/slider/push** (anti-bug
  n°1 intact). Effet secondaire assumé : proche + défaut plus sombres → défaut `perceivedDepth` **8 → 15**
  (compensation, `OceanAbsorptionModule.cs`). Fenêtre de réglage au gate ≈ 0.03–0.06.
- **Palette (hue) → recalibrage des ancres** (`Profiles/WaterAbsorption_*.asset` + défauts du builder) :
  le hue asymptotique = `b_b/σ`, donc réglé par σ. **Renforcement de l'inversion σ_B > σ_G** sur les
  eaux côtières (CDOM/sédiments absorbent le BLEU en premier — Ocean Optics Web Book / Akkaynak 2017)
  pour écraser le bleu asymptotique et obtenir un vrai vert sombre :

  | Ancre | σ (R,G,B) m⁻¹ | Δ vs k3 | asymptote b_b/σ×0.02 (hue) |
  |---|---|---|---|
  | Ia  | (0.36, 0.041, 0.028) | inchangé | (0.011, 0.206, 0.714) — bleu profond |
  | II  | (0.45, 0.09, **0.15**) | σ_B 0.12→0.15 | (0.009, 0.094, 0.133) — bleu-vert côtier |
  | III | (**0.55**, 0.20, **1.10**) | σ_R 0.60→0.55, σ_B 0.65→1.10 | (0.0075, 0.042, 0.018) — **vert sombre** (G/B ≈ 2.3) |

  σ_B monotone croissant Ia→II→III (0.028 < 0.15 < 1.10) → hue monotone, aucun pic entre ancres.
  Aucun canal asymptotique n'atteint 1 → `saturate()` ne clippe pas (invariant préservé). Ia **non
  touché** : baisser son σ_B=0.028 clipperait le bleu (0.02/0.028=0.71 ; à σ_B=0.02 → clip).

**Calibrage QUALITATIF (l'utilisateur est « les yeux ») :** pas de LUT RGB figée (le RGB écran final
passe par l'exposition/white-balance/tonemapping du LightLoop HDRP). Les lectures RGB user servent de
diagnostic RELATIF (plateau / hue), pas de cible absolue. Réglages fins au gate : `kDepthRate`
(0.03–0.06, recompile), σ des ancres (live), défaut `perceivedDepth`, `kUpwellingScale` en dernier recours.

## En cas d'échec

- **Rollback** `.backups/08c_before_P3_color_fix/` (état k4 avant ce correctif couleur) ou, plus loin,
  `.backups/08_before_P3_absorption/` (état P2 validée intact) + diagnostic.
- Repères : **eau blanche** → ancres à σ≈0 ou `perceivedDepth`≈0 ; **eau inchangée** → module inactif /
  ancres manquantes (warning `[Ocean P3]` en Console) / profil édité ≠ profil actif de la scène ;
  **eau turquoise uniforme** → régression du modèle upwelling (vérifier le bloc k3 d'`OceanSurfaceData.hlsl`) ;
  **teinte incohérente** → vérifier les σ des ancres (R > G > B pour une eau claire ; B qui monte =
  eau côtière) ; **tout sombre/cramé** → environnement/exposition de la scène, pas l'absorption.

## Clôture (après validation utilisateur)

- Snapshot **`.backups/09_P3_absorption_validated/`** + MANIFEST (σ de départ consignés ; **calibrage
  colorimétrique restant dû** — Q6.1 §D, Solonenko & Mobley 2015).
- Passage **P4 — Écume** bloqué tant que (d)/(e) ne sont pas validés (Q13.3).
