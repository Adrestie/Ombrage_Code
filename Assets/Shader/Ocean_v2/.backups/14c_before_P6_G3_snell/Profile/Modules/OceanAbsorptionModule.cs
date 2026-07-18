// OceanAbsorptionModule.cs  (Ocean_v2 / P3)
// Module ABSORPTION & COULEUR — l'UNIQUE modèle Beer-Lambert spectral du système (Q6.1) :
// I = I₀ · exp(−σ·d) par canal, σ = (σ_r, σ_g, σ_b) en m⁻¹ (absorption pure a(λ), zéro diffusion V1).
//
// SOURCE DE VÉRITÉ UNIQUE : le global _WaterAbsorption (vec3 σ en .rgb), poussé ICI et seulement ici,
// consommé (a) par la surface deferred — couleur de la colonne d'eau vue de dessus, sur la profondeur
// perçue _OceanAbsorptionDepth — et (b) par le futur CustomPass sous-marin (P6), qui lira le MÊME σ
// avec ses distances réelles. Élimine la « double absorption » incohérente de l'ancien système.
//
// 3 ancres Jerlov préchargées (Q6.2) : Ia (waterType = 0), II (≈ 0.5), III (1) — interpolation PAR
// SEGMENTS entre les 3 assets WaterAbsorptionProfile. Position de l'ancre II = convention artistique
// 0.5 (question ouverte Q6.1 §D, à réviser au calibrage — constante kAnchorII, pas un slider).
//
// ANTI-BUG n°1 : push via ctx.globals UNIQUEMENT (assignation pure, jamais *=/+=, restauré neutre au
// Teardown par OceanSystem). AUCUNE valeur σ codée en dur ici (les ancres sont la SEULE source des
// valeurs — Q6.2 §C) : sans ancres assignées, le module ne pousse RIEN et la surface retombe sur
// _BaseColor (l'interrupteur _OceanAbsorptionEnabled, poussé par OceanSurfaceModule, reste à 0).
//
// ⚠️ BUILD : l'auto-résolution des ancres est EDITOR-ONLY (même piège que ResolveShaders, correctif
// (f) du gate 4 P2) — pour survivre au build, les références DOIVENT être sérialisées dans le profil
// (sauver l'asset après l'auto-résolution ; un futur profil de gate devra les assigner explicitement).
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Absorption")]
    public class OceanAbsorptionModule : OceanFeatureModule
    {
        // Position artistique de l'ancre II sur [0..1] (Q6.1 §D : convention, pas physique).
        internal const float kAnchorII = 0.5f;

        // Chemins canoniques des 3 ancres (créées par OceanAbsorptionAnchorsBuilder, jamais écrasées).
        // PUBLICS (et non internal) : le builder vit dans l'assembly ÉDITEUR (Assembly-CSharp-Editor),
        // qui n'a pas d'InternalsVisibleTo — un internal y produirait CS0117 (constaté à la compile).
        public const string kAnchorIaPath  = "Assets/Shader/Ocean_v2/Profiles/WaterAbsorption_Ia.asset";
        public const string kAnchorIIPath  = "Assets/Shader/Ocean_v2/Profiles/WaterAbsorption_II.asset";
        public const string kAnchorIIIPath = "Assets/Shader/Ocean_v2/Profiles/WaterAbsorption_III.asset";

        [Header("Ancres Jerlov (Q6.2 — seules sources des σ, jamais de valeurs en dur)")]
        [Tooltip("Ancre waterType = 0 — Jerlov Ia, océanique très claire (bleu profond). Auto-résolue depuis Profiles/ si vide (éditeur seulement).")]
        public WaterAbsorptionProfile anchorIa;

        [Tooltip("Ancre waterType ≈ 0.5 — Jerlov II, côtier bleuté. Auto-résolue depuis Profiles/ si vide (éditeur seulement).")]
        public WaterAbsorptionProfile anchorII;

        [Tooltip("Ancre waterType = 1 — Jerlov III, côtier vert-brun. Auto-résolue depuis Profiles/ si vide (éditeur seulement).")]
        public WaterAbsorptionProfile anchorIII;

        [Header("Master (Q6.1)")]
        [Tooltip("Type d'eau [0..1] : 0 = Ia (très claire), 0.5 = II (côtier bleuté), 1 = III (côtier vert-brun). Interpole par segments entre les 3 ancres — réglable LIVE.")]
        [Range(0f, 1f)] public float waterType = 0f;

        [Header("Consommation surface (V1 — pleine mer, pas de fond visible)")]
        [Tooltip("Développement de la couleur de la colonne d'eau (épaisseur optique perçue — ex « Perceived Depth »). BAS = colonne peu développée → SOMBRE (tend vers le noir) ; HAUT = couleur PLEINE du type d'eau. ⚠ Ce n'est PAS la distance au fond : en V1 pleine mer il n'y a pas de fond, donc monter ce réglage ajoute de la couleur (pas de l'éclaircissement). L'effet « bas-fond = turquoise sur le sable » viendra avec le fond + réfraction (P6). Plage [0.1..50] ; au-delà, la couleur est optiquement saturée.")]
        [FormerlySerializedAs("perceivedDepth")]
        [Range(0.1f, 50f)] public float colorBuildup = 15f;

        // Globaux (déclarés côté shader dans OceanSurfaceData.hlsl, HORS UnityPerMaterial — jamais dans Properties{}).
        static readonly int ID_WaterAbsorption      = Shader.PropertyToID("_WaterAbsorption");
        static readonly int ID_OceanAbsorptionDepth = Shader.PropertyToID("_OceanAbsorptionDepth");

        [System.NonSerialized] bool m_WarnedMissingAnchors;

        public bool HasAnchors => anchorIa != null && anchorII != null && anchorIII != null;

        /// σ interpolé par segments entre les 3 ancres (t clampé [0..1]). Statique et pur → smoke-testé
        /// en EditMode (OceanAbsorptionTests).
        internal static Vector3 EvaluateSigma(Vector3 ia, Vector3 ii, Vector3 iii, float t)
        {
            t = Mathf.Clamp01(t);
            return t <= kAnchorII
                ? Vector3.Lerp(ia, ii, t / kAnchorII)
                : Vector3.Lerp(ii, iii, (t - kAnchorII) / (1f - kAnchorII));
        }

        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            ResolveAnchorsEditorOnly();
        }

        public override void Apply(OceanApplyContext ctx)
        {
            if (!HasAnchors)
            {
                ResolveAnchorsEditorOnly();
                if (!HasAnchors)
                {
                    if (!m_WarnedMissingAnchors)
                    {
                        m_WarnedMissingAnchors = true;
                        Debug.LogWarning("[Ocean P3] Ancres d'absorption manquantes (Ia/II/III) — menu " +
                                         "Ombrage/Ocean/Create Water Absorption Anchors (Ia, II, III), puis vérifier " +
                                         "le module Absorption du profil. Aucun σ poussé : la surface retombe sur _BaseColor.");
                    }
                    return;   // pas d'ancres → pas de push (aucune valeur en dur, Q6.2 §C)
                }
            }
            m_WarnedMissingAnchors = false;

            Vector3 sigma = EvaluateSigma(anchorIa.Sigma, anchorII.Sigma, anchorIII.Sigma, waterType);

            // SET pur non cumulatif (anti-bug n°1) — l'UNIQUE point de push de _WaterAbsorption du projet.
            ctx.globals.SetGlobalVector(ID_WaterAbsorption, new Vector4(sigma.x, sigma.y, sigma.z, 0f));
            ctx.globals.SetGlobalFloat(ID_OceanAbsorptionDepth, colorBuildup);
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
            waterType = Mathf.Clamp01(waterType);
            // [0.1..50] : au-delà, le terme de maturité était optiquement saturé (bug k3 → k4).
            // ⚠ Tout profil sérialisé avec colorBuildup > 50 sera ramené à 50 dès cette validation
            // (comportement volontaire du resserrement — plage 50–200 sans effet visible).
            colorBuildup = Mathf.Clamp(colorBuildup, 0.1f, 50f);
            ResolveAnchorsEditorOnly();
        }
#endif
    }
}
