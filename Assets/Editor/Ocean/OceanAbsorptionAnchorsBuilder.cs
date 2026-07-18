// OceanAbsorptionAnchorsBuilder.cs  (banc P3 — outillage éditeur)
// Crée les 3 assets-ancres Jerlov du module absorption (Q6.2 : EXACTEMENT 3 profils préchargés,
// alignés sur les 3 ancres du master waterType) dans Assets/Shader/Ocean_v2/Profiles/ :
//   Ia  (waterType = 0)   — océanique très claire (bleu profond)
//   II  (waterType ≈ 0.5) — côtier bleuté
//   III (waterType = 1)   — côtier vert-brun
//
// Valeurs de DÉPART = littérature (ancrage : type IB ≈ (0.37, 0.044, 0.035) m⁻¹, Akkaynak 2017 ;
// Ia légèrement plus clair), RÉVISÉES au calibrage colorimétrique. La surface consomme la réflectance
// MONTANTE b_b/σ (cf. OceanSurfaceData.hlsl) → le HUE asymptotique par type est porté ENTIÈREMENT par
// le triplet σ. Passe 2026-07-06 (échec gates (d)/(e) k4) : renforcement de l'inversion σ_B > σ_G sur
// les eaux côtières (CDOM/sédiments absorbent le BLEU en premier — Ocean Optics Web Book / Akkaynak
// 2017), pour SUPPRIMER le bleu asymptotique et obtenir un vrai vert sombre en type III :
//   Ia (0.36, 0.041, 0.028) inchangé — bleu profond, b_b/σ×0.02 ≈ (0.011, 0.206, 0.714) ;
//   II (0.45, 0.09, 0.15) — bleu-vert côtier, ≈ (0.009, 0.094, 0.133) ;
//   III (0.55, 0.20, 1.10) — vert sombre, ≈ (0.0075, 0.042, 0.018) (σ_B fort → bleu écrasé, G/B ≈ 2.3).
// Aucun canal asymptotique n'atteint 1 (max = Ia bleu 0.71) → le saturate() shader ne clippe pas ;
// contrainte à préserver si l'on baisse un σ. Révision fine toujours possible (Q6.1 §D — a(λ)
// Solonenko & Mobley 2015), directement dans les ASSETS (jamais écrasés par ce menu).
//
// ⚠️ NE JAMAIS ÉCRASER un asset existant : l'utilisateur peut avoir calibré les σ — le menu est
// create-if-missing (contrairement au profil de gate P2, volontairement recréé déterministe).
using UnityEditor;
using UnityEngine;

namespace Ombrage.OceanFeatures.GateTools
{
    public static class OceanAbsorptionAnchorsBuilder
    {
        const string kFolder = "Assets/Shader/Ocean_v2/Profiles";

        [MenuItem("Ombrage/Ocean/Create Water Absorption Anchors (Ia, II, III)")]
        public static void CreateAnchors()
        {
            OceanEditorIO.EnsureFolder(kFolder);

            var ia  = EnsureAnchor(OceanAbsorptionModule.kAnchorIaPath,
                "Jerlov Ia — océanique très claire", 0.36f, 0.041f, 0.028f);
            var ii  = EnsureAnchor(OceanAbsorptionModule.kAnchorIIPath,
                "Jerlov II — côtier bleuté", 0.45f, 0.09f, 0.15f);
            var iii = EnsureAnchor(OceanAbsorptionModule.kAnchorIIIPath,
                "Jerlov III — côtier vert-brun", 0.55f, 0.20f, 1.10f);

            AssetDatabase.SaveAssets();

            bool ok = ia != null && ii != null && iii != null;
            if (ok)
                Debug.Log("[Ocean P3] Ancres d'absorption prêtes : Ia / II / III sous " + kFolder +
                          " (create-if-missing, jamais écrasées). Valeurs littérature de départ — calibrage " +
                          "colorimétrique dû (Q6.1 §D). Le module Absorption les auto-résout en éditeur ; " +
                          "SAUVER le profil pour sérialiser les références (exigence build, cf. correctif (f) P2).");
            else
                Debug.LogError("[Ocean P3] Échec de création d'au moins une ancre d'absorption (voir logs).");

            Selection.activeObject = ia;
        }

        /// Charge l'ancre si présente, sinon la crée avec les σ de départ. Ne modifie JAMAIS un asset existant.
        static WaterAbsorptionProfile EnsureAnchor(string path, string label, float r, float g, float b)
        {
            var existing = AssetDatabase.LoadAssetAtPath<WaterAbsorptionProfile>(path);
            if (existing != null)
            {
                Debug.Log($"[Ocean P3] Ancre déjà présente (inchangée) : {path}");
                return existing;
            }

            var p = ScriptableObject.CreateInstance<WaterAbsorptionProfile>();
            p.label = label;
            p.sigmaR = r;
            p.sigmaG = g;
            p.sigmaB = b;
            AssetDatabase.CreateAsset(p, path);
            Debug.Log($"[Ocean P3] Ancre créée : {path}  σ=({r}, {g}, {b}) m⁻¹ — « {label} »");
            return p;
        }
    }
}
