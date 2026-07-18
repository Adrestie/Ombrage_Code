// WaterAbsorptionProfile.cs  (Ocean_v2 / P3)
// ScriptableObject pilote de l'ABSORPTION (Q6.1/Q6.2) : expose STRICTEMENT les 3 σ d'absorption
// pure a(λ) intégrée en 3 bandes RGB (R = [600–700] nm, G = [500–600] nm, B = [400–500] nm —
// méthode Akkaynak 2017), en m⁻¹. AUCUN terme de diffusion/turbidité : V1 = absorption pure
// (Q6.3 ; le single-scattering V1.5 sera un ajout NON-breaking, jamais un champ ici en V1).
//
// Les 3 assets-ancres Jerlov Ia/II/III sont créés par le menu Ombrage/Ocean/Create Water Absorption
// Anchors (valeurs littérature de DÉPART — ancrage type IB ≈ (0.37, 0.044, 0.035) m⁻¹, Akkaynak
// 2017 ; calibrage colorimétrique dû, Q6.1 §D / Solonenko & Mobley 2015). L'utilisateur crée
// librement d'autres profils = variantes d'ancre (Q6.2) — PAS de 4ᵉ position sur le master 3-points.
//
// ⚠️ Ne JAMAIS saisir Kd (atténuation diffuse = absorption + diffusion) à la place de a(λ) pur :
// Kd embarquerait un demi-scattering implicite incohérent (Q6.3 §C).
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    [CreateAssetMenu(fileName = "WaterAbsorptionProfile", menuName = "Ombrage/Ocean/Water Absorption Profile", order = 10)]
    public class WaterAbsorptionProfile : ScriptableObject
    {
        // Garde-fou large : les eaux naturelles même très turbides restent ≲ 3 m⁻¹ par bande.
        const float kSigmaMax = 10f;

        [Tooltip("Étiquette informative du type d'eau (ex. « Jerlov Ia — océanique très claire »). Sans effet runtime.")]
        public string label = "";

        [Tooltip("σ ROUGE (m⁻¹) — absorption pure a(λ) moyennée sur [600–700] nm. Le rouge est absorbé le plus vite (eau claire ≈ 0.3–0.5 m⁻¹).")]
        [Min(0f)] public float sigmaR = 0.36f;

        [Tooltip("σ VERT (m⁻¹) — absorption pure a(λ) moyennée sur [500–600] nm.")]
        [Min(0f)] public float sigmaG = 0.041f;

        [Tooltip("σ BLEU (m⁻¹) — absorption pure a(λ) moyennée sur [400–500] nm. Monte avec la matière organique dissoute (CDOM) : eaux côtières vertes/brunes.")]
        [Min(0f)] public float sigmaB = 0.028f;

        public Vector3 Sigma => new Vector3(sigmaR, sigmaG, sigmaB);

        void OnValidate()
        {
            sigmaR = Mathf.Clamp(sigmaR, 0f, kSigmaMax);
            sigmaG = Mathf.Clamp(sigmaG, 0f, kSigmaMax);
            sigmaB = Mathf.Clamp(sigmaB, 0f, kSigmaMax);
        }
    }
}
