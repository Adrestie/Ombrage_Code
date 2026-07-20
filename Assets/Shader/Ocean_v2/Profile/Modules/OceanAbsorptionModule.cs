// OceanAbsorptionModule.cs  (Ocean_v2)
// Module ABSORPTION & COULEUR — Beer-Lambert spectral, RE-PARAMÉTRÉ art-directed (amendement A3).
//
// MODÈLE (découplé look / extinction) :
//   - waterColor (MAÎTRE)  = la couleur que l'eau AFFICHE (surface + glow du fog). Poussée telle quelle
//                            dans _OceanScatterColor → le shader de surface l'utilise comme couleur de la
//                            colonne (développée en profondeur), le module Volumetrics en tire le glow.
//   - absorptionColor      = l'ORDRE d'absorption (quelle couleur s'éteint en premier = le spectre σ).
//                            DÉCOCHÉ = physique (dérivé de waterColor : σ ∝ b_b/couleur → rouge d'abord).
//                            COCHÉ = art-directed (on tord l'ordre : vert/bleu d'abord…), SANS changer le
//                            look affiché → « garder le comportement normal tout en changeant l'ordre ».
//   - clarity              = magnitude de σ (distance de visibilité), SÉPARÉE de la teinte.
//   - colorBuildup         = développement de la couleur en profondeur (inchangé).
//
// σ (= _WaterAbsorption, extinction spectrale) et _OceanScatterColor (= look) sont les DEUX globaux
// poussés ICI (source unique de la couleur d'eau, consommée par surface + underwater + fog).
//
// ANTI-BUG n°1 : push via ctx.globals UNIQUEMENT (assignation pure, restaurée neutre au Teardown).
// Les ancres Jerlov (Ia/II/III) ne sont plus la source runtime : elles servent de PRESETS ÉDITEUR
// (boutons « couleur réaliste ») — voir OceanAbsorptionInspector.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Absorption")]
    public class OceanAbsorptionModule : OceanFeatureModule
    {
        // Hue de rétrodiffusion de l'eau pure (Rayleigh ~λ⁻⁴), IDENTIQUE à la surface/fog. Sert au DÉFAUT
        // PHYSIQUE du spectre d'absorption (σ ∝ b_b/couleur) et à la conversion des presets Jerlov (σ→couleur).
        internal static readonly Vector3 kBackscatterSpectrum = new Vector3(0.206f, 0.422f, 1.0f);

        // Chemins des 3 ancres Jerlov — SOURCE des presets éditeur (plus du runtime).
        public const string kAnchorIaPath  = "Assets/Shader/Ocean_v2/Profiles/WaterAbsorption_Ia.asset";
        public const string kAnchorIIPath  = "Assets/Shader/Ocean_v2/Profiles/WaterAbsorption_II.asset";
        public const string kAnchorIIIPath = "Assets/Shader/Ocean_v2/Profiles/WaterAbsorption_III.asset";

        [Header("Couleur de l'eau (maître)")]
        [Tooltip("Couleur que l'eau AFFICHE (surface + glow du fog + teinte des objets immergés). Ta couleur d'DA, ensuite modulée par la lumière. Pilote toute la chaîne dessus/dessous.")]
        public Color waterColor = new Color(0.06f, 0.30f, 0.42f, 1f);

        [Header("Absorption")]
        [Tooltip("Clarté (m) : distance à laquelle la couleur absorbée en premier chute à 1/e. GRAND = eau claire (visibilité lointaine, σ faible) ; petit = trouble. Contrôle la magnitude, PAS la teinte.")]
        public OceanFloatParameter clarity = new OceanFloatParameter(4f);

        [Tooltip("Développement de la couleur de la colonne d'eau vue de dessus (épaisseur optique perçue). BAS = colonne peu développée (sombre) ; HAUT = couleur pleine. Ce n'est PAS la distance au fond.")]
        public OceanFloatParameter colorBuildup = new OceanFloatParameter(15f);

        [Tooltip("ORDRE d'absorption = quelle couleur est absorbée EN PREMIER (le canal le plus vif). DÉCOCHÉ = physique (dérivé de la couleur d'eau : le rouge en premier). COCHÉ = tu tords l'ordre librement (vert/bleu en premier…) SANS changer la couleur affichée.")]
        public OceanColorParameter absorptionColor = new OceanColorParameter(new Color(1f, 0.45f, 0.30f, 1f));

        // Ancres (presets éditeur uniquement) — auto-résolues, cachées de l'inspecteur générique.
        [HideInInspector] public WaterAbsorptionProfile anchorIa;
        [HideInInspector] public WaterAbsorptionProfile anchorII;
        [HideInInspector] public WaterAbsorptionProfile anchorIII;

        static readonly int ID_WaterAbsorption = Shader.PropertyToID("_WaterAbsorption");
        static readonly int ID_ScatterColor    = Shader.PropertyToID("_OceanScatterColor");
        static readonly int ID_AbsorptionDepth = Shader.PropertyToID("_OceanAbsorptionDepth");

        // ── Dérivation σ (PURE, testable) ─────────────────────────────────────────────────────────
        /// Spectre d'absorption PHYSIQUE par défaut = complément de la couleur affichée (σ ∝ b_b/couleur) :
        /// l'eau claire absorbe le rouge en premier. Non normalisé.
        public static Vector3 DefaultAbsorptionSpectrum(Color look)
        {
            Vector3 l = new Vector3(Mathf.Max(look.r, 1e-3f), Mathf.Max(look.g, 1e-3f), Mathf.Max(look.b, 1e-3f));
            return new Vector3(kBackscatterSpectrum.x / l.x, kBackscatterSpectrum.y / l.y, kBackscatterSpectrum.z / l.z);
        }

        /// σ final : spectre (physique OU art-directed), normalisé au canal max, × magnitude(clarté).
        /// clarté = distance (m) où le canal DOMINANT chute à 1/e → σ_dominant = 1/clarté.
        public static Vector3 DeriveSigma(Color waterColor, bool overrideOrder, Color orderColor, float clarity)
        {
            Vector3 spectrum = overrideOrder
                ? new Vector3(Mathf.Max(orderColor.r, 0f), Mathf.Max(orderColor.g, 0f), Mathf.Max(orderColor.b, 0f))
                : DefaultAbsorptionSpectrum(waterColor);
            float m = Mathf.Max(spectrum.x, Mathf.Max(spectrum.y, spectrum.z));
            spectrum = m > 1e-6f ? spectrum / m : Vector3.one;   // canal dominant → 1
            return spectrum * (1f / Mathf.Max(clarity, 1e-2f));
        }

        /// Couleur AFFICHÉE que produit un σ Jerlov (inverse de la dérivation) = normalize(b_b/σ).
        /// Sert aux presets éditeur (convertit une ancre réaliste en waterColor de départ).
        public static Color LookFromSigma(Vector3 sigma)
        {
            Vector3 s = new Vector3(Mathf.Max(sigma.x, 1e-4f), Mathf.Max(sigma.y, 1e-4f), Mathf.Max(sigma.z, 1e-4f));
            Vector3 look = new Vector3(kBackscatterSpectrum.x / s.x, kBackscatterSpectrum.y / s.y, kBackscatterSpectrum.z / s.z);
            float m = Mathf.Max(look.x, Mathf.Max(look.y, look.z));
            if (m > 1e-6f) look /= m;
            return new Color(look.x, look.y, look.z, 1f);
        }

        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            ResolveAnchorsEditorOnly();
        }

        public override void Apply(OceanApplyContext ctx)
        {
            Vector3 sigma = DeriveSigma(waterColor, absorptionColor.overridden, absorptionColor.value, clarity.Effective);

            // Les DEUX globaux couleur d'eau (SET pur, anti-bug n°1) — source unique consommée par
            // surface (look + extinction) + underwater (extinction) + fog (glow).
            ctx.globals.SetGlobalVector(ID_WaterAbsorption, new Vector4(sigma.x, sigma.y, sigma.z, 0f));
            ctx.globals.SetGlobalVector(ID_ScatterColor,    new Vector4(waterColor.r, waterColor.g, waterColor.b, 1f));
            ctx.globals.SetGlobalFloat (ID_AbsorptionDepth, colorBuildup.Effective);
        }

        void ResolveAnchorsEditorOnly()
        {
#if UNITY_EDITOR
            if (anchorIa == null)  anchorIa  = AssetDatabase.LoadAssetAtPath<WaterAbsorptionProfile>(kAnchorIaPath);
            if (anchorII == null)  anchorII  = AssetDatabase.LoadAssetAtPath<WaterAbsorptionProfile>(kAnchorIIPath);
            if (anchorIII == null) anchorIII = AssetDatabase.LoadAssetAtPath<WaterAbsorptionProfile>(kAnchorIIIPath);
#endif
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            clarity.value      = Mathf.Clamp(clarity.value, 0.3f, 60f);
            colorBuildup.value = Mathf.Clamp(colorBuildup.value, 0.1f, 50f);
            ResolveAnchorsEditorOnly();
        }
#endif
    }
}
