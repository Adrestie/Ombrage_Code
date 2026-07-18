using UnityEngine;

namespace Ombrage.Systems.DayNight
{
    public enum CelestialBodyType { SUN, MOON }

    [System.Serializable]
    public class CelestialBody
    {
        public string name = "Sun";
        public CelestialBodyType type = CelestialBodyType.SUN;

        [Tooltip("Lumière directionnelle associée à cet astre")]
        public Light light;

        [Tooltip("Axe d'inclinaison de l'orbite (en degrés)")]
        [Range(-90f, 90f)]
        public float orbitTilt = 0f;

        [Tooltip("Décalage de phase dans le cycle (0-1). Permet de décaler plusieurs soleils/lunes entre eux")]
        [Range(0f, 1f)]
        public float phaseOffset = 0f;

        [Tooltip("Intensité maximale de la lumière (en lux pour le soleil, en lux pour la lune)")]
        public float maxIntensity = 130000f;

        [Tooltip("Intensité en fonction de la phase")]
        public AnimationCurve intensityProgression = new AnimationCurve();

        [Tooltip("Couleur de la lumière au zénith")]
        public Color zenithColor = Color.white;

        [Tooltip("Couleur de la lumière au lever/coucher")]
        public Color horizonColor = new Color(1f, 0.5f, 0.2f);

        [Tooltip("Température de couleur au zénith (en Kelvin)")]
        public float zenithColorTemperature = 6500f;

        [Tooltip("Température de couleur à l'horizon (en Kelvin)")]
        public float horizonColorTemperature = 2500f;

        [Tooltip("Sous cet angle par rapport à l'horizon, la lumière est désactivée (en degrés)")]
        public float disableBelowAngle = -5f;

        /// <summary>
        /// Angle courant de l'astre dans le ciel (0 = horizon est, 90 = zénith, 180 = horizon ouest)
        /// </summary>
        [HideInInspector] public float currentAngle;
    }
}
