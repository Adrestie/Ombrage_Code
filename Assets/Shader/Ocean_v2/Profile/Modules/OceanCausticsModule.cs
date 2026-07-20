// OceanCausticsModule.cs  (Ocean_v2)
// Module CAUSTIQUES — motifs de lumière focalisée sur le fond vu à travers l'eau. Le calcul vit dans
// le shader (OceanCaustics.hlsl) et MODULE le fond réfracté (bg *= 1 + caustics), à l'intérieur du bloc
// réfraction de OceanSurfaceData.hlsl. Ce module ne fait que POUSSER ses réglages en globaux (SET pur
// via ctx.globals, anti-bug n°1) ; l'INTERRUPTEUR de consommation (_OceanCausticsEnabled) est poussé
// par le module de SURFACE (le consommateur), comme pour l'absorption/la réfraction.
//
// DÉPENDANCE ASSUMÉE : les caustiques ne sont visibles QUE si le module Refraction est actif (sans
// see-through, pas de fond visible → rien à éclairer). Le code caustique vit dans le bloc réfraction
// du shader : Refraction OFF ⇒ caustiques inertes automatiquement. Physiquement correct.
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Rendering/Caustics")]
    public class OceanCausticsModule : OceanFeatureModule
    {
        // Valeurs à OVERRIDE (niveau 2, cf. Reflection/Refraction). Décoché = défaut validé ; cocher =
        // saisie d'une valeur différente. Clamp appliqué sur .value en OnValidate. Défauts = réglages V1.
        [Header("Caustiques (lumière focalisée sur le fond réfracté)")]
        [Tooltip("Force globale des caustiques. 0 = éteint. (Défaut V1 = 1.5.)")]
        public OceanFloatParameter intensity = new OceanFloatParameter(1.5f);

        [Tooltip("Échelle spatiale (m) du voisinage d'échantillonnage : PETIT = motif fin et serré ; GRAND = cellules larges et douces. (Défaut V1 = 1.)")]
        public OceanFloatParameter scale = new OceanFloatParameter(1f);

        [Tooltip("Profondeur (m) de fondu : au-delà, les caustiques disparaissent (lumière dispersée en eau profonde). (Défaut V1 = 10.)")]
        public OceanFloatParameter maxDepth = new OceanFloatParameter(10f);

        [Header("Advanced")]
        [Tooltip("Dispersion chromatique (m) : sépare légèrement les canaux R/B pour un liseré arc-en-ciel sur les caustiques. 0 = monochrome. (Défaut V1 = 2.)")]
        public OceanFloatParameter chromaSpread = new OceanFloatParameter(2f);

        // Globaux consommés côté shader dans OceanCaustics.hlsl (HORS UnityPerMaterial).
        static readonly int ID_Scale     = Shader.PropertyToID("_OceanCausticsScale");
        static readonly int ID_Intensity = Shader.PropertyToID("_OceanCausticsIntensity");
        static readonly int ID_MaxDepth  = Shader.PropertyToID("_OceanCausticsMaxDepth");
        static readonly int ID_Chroma    = Shader.PropertyToID("_OceanCausticsChroma");
        // Direction soleil (partageable) : sert à PROJETER le motif le long du rayon solaire côté shader.
        static readonly int ID_SunDir    = Shader.PropertyToID("_OceanSunDirection");
        // NB : _OceanWaterLevel est poussé par OceanSurfaceModule (toujours actif), pas ici (le module
        // Caustics peut être absent alors que la passe sous-marine en a besoin).

        // Réf soleil cachée (non sérialisée) — même résolution que OceanVolumetricsModule.
        [System.NonSerialized] Light m_Sun;

        public override void Apply(OceanApplyContext ctx)
        {
            // SET pur non cumulatif (anti-bug n°1). L'interrupteur enable est poussé par la surface.
            ctx.globals.SetGlobalFloat(ID_Scale, scale.Effective);
            ctx.globals.SetGlobalFloat(ID_Intensity, intensity.Effective);
            ctx.globals.SetGlobalFloat(ID_MaxDepth, maxDepth.Effective);
            ctx.globals.SetGlobalFloat(ID_Chroma, chromaSpread.Effective);

            // Direction de propagation du soleil (transform.forward = sens de la lumière, vers le bas).
            // Résolue à la volée (RenderSettings.sun, repli directionnelle la plus intense), lue LIVE
            // chaque frame pour suivre la rotation du soleil en LookDev. Repli (0,-1,0) = vertical.
            m_Sun = ResolveSun(m_Sun);
            Vector3 L = m_Sun != null ? m_Sun.transform.forward : Vector3.down;
            ctx.globals.SetGlobalVector(ID_SunDir, new Vector4(L.x, L.y, L.z, 0f));
        }

        static Light ResolveSun(Light cached)
        {
            if (cached != null) return cached;
            var sun = RenderSettings.sun;
            if (sun == null)   // repli : la directionnelle la plus intense de la scène
            {
                float best = -1f;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l != null && l.type == LightType.Directional && l.intensity > best) { best = l.intensity; sun = l; }
            }
            return sun;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            intensity.value    = Mathf.Max(0f, intensity.value);
            scale.value        = Mathf.Max(0.01f, scale.value);
            maxDepth.value     = Mathf.Max(0.1f, maxDepth.value);
            chromaSpread.value = Mathf.Max(0f, chromaSpread.value);
        }
#endif
    }
}
