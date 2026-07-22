using UnityEngine;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// Pilote les god-rays underwater injectés dans le Water System HDRP forké
    /// (kernel WaterFogIndirect via OmbrageWaterGodRays.hlsl).
    ///
    /// Les réglages sont poussés au shader par une variable globale
    /// <c>_OmbrageGodRaysParams</c> (x = intensité, y = anisotropy, z = maxDistance).
    /// Sans ce composant actif dans la scène, la variable reste à zéro et les
    /// god-rays sont désactivés (intensité 0) — comportement opt-in volontaire.
    ///
    /// Note : le nombre de pas de raymarch est un preset qualité côté HLSL
    /// (constante de compilation requise par l'unroll), il n'est donc pas exposé ici.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Ombrage/Visual/Ocean/God Rays Controller")]
    public sealed class OmbrageWaterGodRaysController : MonoBehaviour
    {
        [Tooltip("Intensité maîtresse des god-rays. 0 = désactivé.")]
        [Min(0f)]
        [SerializeField] private float intensity = 2.0f;

        [Header("Avancé")]
        [Tooltip("Concentration du halo vers le soleil (phase de Cornette-Shanks). " +
                 "0 = diffus, ~0.9 = faisceaux serrés.")]
        [Range(0f, 0.95f)]
        [SerializeField] private float anisotropy = 0.6f;

        [Tooltip("Distance maximale marchée le long du rayon (mètres). Borne le coût.")]
        [Min(1f)]
        [SerializeField] private float maxDistance = 60f;

        private static readonly int ParamsId = Shader.PropertyToID("_OmbrageGodRaysParams");

        private void OnEnable() => Apply();
        private void Update() => Apply();
        private void OnValidate() => Apply();

        // Coupe proprement les god-rays quand le contrôleur est désactivé/détruit.
        private void OnDisable() => Shader.SetGlobalVector(ParamsId, Vector4.zero);

        private void Apply()
        {
            Shader.SetGlobalVector(ParamsId, new Vector4(intensity, anisotropy, maxDistance, 0f));
        }
    }
}
