using UnityEngine;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Paramètres du brush 3D sphérique (Phase 1).
    /// Classe sérialisable : persiste avec la fenêtre éditeur.
    /// </summary>
    [System.Serializable]
    public class VertexColorBrush
    {
        [Tooltip("Rayon du brush, exprimé en unités monde.")]
        public float radius = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("Force d'application (0 = aucun effet, 1 = effet maximum).")]
        public float strength = 0.5f;

        [Tooltip("Couleur cible appliquée par le brush (canaux RGB).")]
        public Color color = Color.white;

        [Range(0f, 1f)]
        [Tooltip("Valeur cible appliquée au canal Alpha (utilisée quand le canal A est peint).")]
        public float alphaValue = 0f;

        [Tooltip("Falloff : poids en fonction de la distance normalisée au centre du brush.")]
        public AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        /// <summary>
        /// Poids du brush en fonction de la distance normalisée [0..1].
        /// 0 = centre du brush, 1 = bord du rayon.
        /// </summary>
        public float EvaluateFalloff(float normalizedDistance)
        {
            float t = Mathf.Clamp01(normalizedDistance);
            return Mathf.Clamp01(falloff.Evaluate(t));
        }

        /// <summary>
        /// Garantit que les champs sont dans un état valide après désérialisation.
        /// </summary>
        public void Validate()
        {
            if (radius < 0.001f)
                radius = 0.001f;
            alphaValue = Mathf.Clamp01(alphaValue);
            if (falloff == null || falloff.length == 0)
                falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        }
    }
}
