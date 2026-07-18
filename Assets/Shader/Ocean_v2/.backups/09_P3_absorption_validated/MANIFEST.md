# MANIFEST — 09_P3_absorption_validated

**Phase :** P3 — Absorption (module `absorption`)
**Statut :** ✅ VALIDÉ UTILISATEUR (rendu couleur) — 2026-07-17
**Snapshot :** état de fin de phase P3, restaurable.

## Contenu copié
- `Profile/**` (code C# + asmdefs), `Shaders/**` (HLSL/shader/compute/mat), `Profiles/**` (3 ancres Jerlov),
  `Tests/**`, `OceanProfile.asset`, `Ocean Debug.unity` (scène nettoyée).
- 81 fichiers. Empreintes : `HASHES.sha256`.

## État validé

### Modèle couleur (surface, `OceanSurfaceData.hlsl`)
- Réflectance montante `upwelling = b_b/σ × maturité(d) × kUpwellingScale` (k3 + k4).
- `maturité = 1 − exp(−2·σ_norm·kDepthRate·d)`, profondeur optique normalisée par σ̄ (knee ≈ 12 m, indépendant de la turbidité).
- `kUpwellingScale = 0.02` (**constante statique** — l'exposition en paramètre profil a été envisagée puis RETIRÉE, cf. session ; réactivable plus tard).
- `kDepthRate = 0.04`. Chromie portée UNIQUEMENT par σ.

### Module `OceanAbsorptionModule`
- Champ renommé **`perceivedDepth` → `colorBuildup`** (`[FormerlySerializedAs("perceivedDepth")]`), tooltip corrigé
  (bas = colonne peu développée → sombre ; haut = couleur pleine ; PAS la distance au fond — pas de fond en V1).
- Slider « Depth Density » (ex-`depthDensity`, exposition de `kUpwellingScale`) **RETIRÉ** → constante statique.
- Push globaux (SET pur, anti-bug n°1) : `_WaterAbsorption` (σ) + `_OceanAbsorptionDepth` (= `colorBuildup`).
- Défauts du profil : `waterType = 0`, `colorBuildup = 15`.

### Ancres Jerlov (σ en m⁻¹, absorption pure a(λ))
- **Ia = (0.36, 0.07, 0.028)** — σ_G recalibré 0.041 → **0.07** (bleu moins cyan, validé session 2026-07-17).
- II = (0.45, 0.09, 0.15) · III = (0.55, 0.20, 1.10) (inchangés k5).

### Vérifications
- Compilation : Console 0 erreur ; shader `hasError = False`.
- Test Runner EditMode `Ombrage.OceanFeatures.Tests` : **16/16 verts**.
- Anti-bug n°1 : push non cumulatif + `RestoreAll` neutre (test exécutable).
- P1 (spectre) et P2 (surface/MV) non modifiés.

## Réserves / dette
- **Calibrage colorimétrique restant dû** (Q6.1 §D) : palette qualitative, RGB écran dépendant de l'éclairage/
  tonemapping. Le « bleu profond » pleinement convaincant dépendra aussi des **réflexions (P5)** et du
  **sous-marin/réfraction (P6)** — non présents en P3. Validation faite sur cette base, look final à revoir P5/P6.
- σ = presets littérature + 1 recalibrage (Ia σ_G) ; révision fine possible dans les assets.

## Nettoyage de session
- Banc temporaire agent `__P3_GateEnv__` supprimé.
- Banc `TEMP_P3_VisualBench` (objet + `Tests/TEMP_P3_VisualBench.volumeprofile.asset`) supprimé sur demande utilisateur.
- Caméra `Ocean Debug` restaurée (0, 16.26, −10) ; soleil restauré (1, 0.957, 0.839).

## Suivant
P4 — Écume (feature `foam`, budget agrégé à `surface`). Slot snapshot P4 = `10`/`11` (ROADMAP §1.1(b)).
