using UnityEngine;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// Pilote les god-rays underwater d'Ombrage, injectés dans le point natif HDRP
    /// (VolumetricLighting.compute, bloc underwater) via la variable globale
    /// <c>_OmbrageGodRaysParams</c> (x = intensity, y = reach, z = contrast).
    ///
    /// Sans ce composant actif dans la scène, la globale reste à zéro et le rendu
    /// retombe sur le comportement HDRP natif exact (opt-in, aucune régression).
    ///
    /// Note : le nombre de slices / la résolution du VBuffer (donc la portée dure
    /// et la netteté) restent pilotés par le Fog volumétrique HDRP, pas ici.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Ombrage/Visual/Ocean/God Rays Controller")]
    public sealed class OmbrageWaterGodRaysController : MonoBehaviour
    {
        [Tooltip("Intensité des god-rays (visibilité). 0 = désactivé (rendu HDRP natif). 1 = équivalent natif.")]
        [Min(0f)]
        [SerializeField] private float intensity = 2.0f;

        [Header("Avancé")]
        [Tooltip("Portée : allonge les faisceaux en profondeur en réduisant l'absorption perçue par les shafts. " +
                 "1 = natif. Plafond dur : Fog > Depth Extent (le VBuffer ne va pas au-delà).")]
        [Range(0.1f, 8f)]
        [SerializeField] private float reach = 2.0f;

        [Tooltip("Contraste (punch) : creuse les gaps sombres entre les faisceaux. 1 = natif, >1 = plus marqué.")]
        [Range(0.5f, 4f)]
        [SerializeField] private float contrast = 1.5f;

        private static readonly int ParamsId = Shader.PropertyToID("_OmbrageGodRaysParams");

        private void OnEnable() => Apply();
        private void Update() => Apply();
        private void OnValidate() => Apply();

        // Coupe proprement l'injection quand le contrôleur est désactivé/détruit
        // => retour au rendu natif.
        private void OnDisable() => Shader.SetGlobalVector(ParamsId, Vector4.zero);

        private void Apply()
        {
            Shader.SetGlobalVector(ParamsId, new Vector4(intensity, reach, contrast, 0f));
        }
    }
}
