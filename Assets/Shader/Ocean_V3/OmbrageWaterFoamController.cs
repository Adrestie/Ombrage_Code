using UnityEngine;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// Aspect foam custom Ombrage (look dual-texture V1) : remplace l'aspect de
    /// FoamErosion natif par un blend de deux textures (crêtes + dissipation),
    /// appliqué à toute la foam de la surface (crêtes de simulation + edge foam).
    ///
    /// Pilote les globales lues par SampleWaterSurface/WaterUtilities via
    /// EvaluateFoamData. Opt-in : sans ce composant (ou textures manquantes),
    /// _OmbrageFoamEnabled reste 0 => aspect natif inchangé, aucune régression.
    ///
    /// Les textures foam ne sont pas versionnées (git ignore les images) : à
    /// assigner ici (tu peux réutiliser celles de la V1, crêtes + dissipation).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Ombrage/Visual/Ocean/Water Foam Controller")]
    public sealed class OmbrageWaterFoamController : MonoBehaviour
    {
        [Tooltip("Active l'aspect foam custom (look V1). Décoché => FoamErosion natif.")]
        [SerializeField] private bool useCustomFoam = true;

        [Tooltip("Texture foam crêtes (haute fréquence). Canal R utilisé.")]
        [SerializeField] private Texture2D foamCrests;

        [Tooltip("Texture foam dissipation (basse fréquence). Canal R utilisé.")]
        [SerializeField] private Texture2D foamDissipating;

        [Tooltip("Tiling du motif de foam.")]
        [Min(0.1f)]
        [SerializeField] private float tiling = 8f;

        [Tooltip("Netteté du passage dissipation -> crêtes.")]
        [Range(0.1f, 5f)]
        [SerializeField] private float blend = 1.5f;

        private static readonly int EnabledId = Shader.PropertyToID("_OmbrageFoamEnabled");
        private static readonly int HighId = Shader.PropertyToID("_OmbrageFoamTexHigh");
        private static readonly int LowId = Shader.PropertyToID("_OmbrageFoamTexLow");
        private static readonly int TilingId = Shader.PropertyToID("_OmbrageFoamTiling");
        private static readonly int BlendId = Shader.PropertyToID("_OmbrageFoamBlend");

        private void OnEnable() => Apply();
        private void Update() => Apply();
        private void OnValidate() => Apply();

        private void OnDisable() => Shader.SetGlobalFloat(EnabledId, 0f);

        private void Apply()
        {
            // Actif seulement si les deux textures sont assignées (sinon fallback natif).
            bool on = useCustomFoam && foamCrests != null && foamDissipating != null;
            Shader.SetGlobalFloat(EnabledId, on ? 1f : 0f);

            if (foamCrests != null) Shader.SetGlobalTexture(HighId, foamCrests);
            if (foamDissipating != null) Shader.SetGlobalTexture(LowId, foamDissipating);
            Shader.SetGlobalFloat(TilingId, tiling);
            Shader.SetGlobalFloat(BlendId, blend);
        }
    }
}
