using UnityEngine;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// Edge foam Ombrage : collier d'écume additif autour des objets qui émergent de
    /// l'eau (et léger effet shoreline sur les hauts-fonds). Calculé dans la passe
    /// GBuffer du Water System HDRP (SampleWaterSurface.hlsl), en plus de la foam
    /// de crête built-in — il n'en remplace rien.
    ///
    /// Pilote deux globales lues par le shader de surface :
    ///   _OmbrageEdgeFoamIntensity (0 = désactivé => aucun effet, opt-in)
    ///   _OmbrageEdgeFoamWidth     (largeur du collier, en mètres)
    /// Sans ce composant actif, l'intensité reste à 0 => comportement natif inchangé.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Ombrage/Visual/Ocean/Edge Foam Controller")]
    public sealed class OmbrageEdgeFoamController : MonoBehaviour
    {
        [Tooltip("Intensité du collier d'écume. 0 = désactivé.")]
        [Min(0f)]
        [SerializeField] private float intensity = 1.0f;

        [Tooltip("Largeur du collier : distance (m) sous la surface jusqu'à laquelle la géométrie génère de l'écume.")]
        [Min(0.001f)]
        [SerializeField] private float width = 0.5f;

        private static readonly int IntensityId = Shader.PropertyToID("_OmbrageEdgeFoamIntensity");
        private static readonly int WidthId = Shader.PropertyToID("_OmbrageEdgeFoamWidth");

        private void OnEnable() => Apply();
        private void Update() => Apply();
        private void OnValidate() => Apply();

        // Coupe proprement l'effet quand le composant est désactivé/détruit.
        private void OnDisable() => Shader.SetGlobalFloat(IntensityId, 0f);

        private void Apply()
        {
            Shader.SetGlobalFloat(IntensityId, intensity);
            Shader.SetGlobalFloat(WidthId, Mathf.Max(width, 0.001f));
        }
    }
}
