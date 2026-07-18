namespace Ombrage.Systems.DayNight
{
	using Ombrage.StaticSystem;
	using UnityEngine;
	using UnityEngine.Rendering.HighDefinition;
	/// <summary>
	/// Composant scène qui pilote les astres (soleils/lunes) et met à jour le ciel HDRP
	/// en fonction du DayNightManager.
	/// </summary>
	public class DayNightController : MonoBehaviour
    {
        [Header("Références")]
        [Tooltip("Référence au DayNightManager (ScriptableObject)")]
        public DayNightManager dayNightManager;

        [Header("Corps célestes")]
        [Tooltip("Liste des soleils et lunes à faire progresser dans le ciel")]
        public CelestialBody[] celestialBodies;

        private void Update()
        {
            if (dayNightManager == null || dayNightManager.status != StaticSystemBase.Status.READY)
                return;

            // Avancer le temps directement (StaticUpdate n'est pas appelé automatiquement)
            dayNightManager.StaticUpdate();

            float time = dayNightManager.currentTimeNormalized;

            UpdateCelestialBodies(time);
        }

        private void UpdateCelestialBodies(float time)
        {
            if (celestialBodies == null) return;

            // Trouver l'astre le plus haut dans le ciel pour lui donner les shadows
            int highestIndex = -1;
            float highestElevation = float.MinValue;

            // Premier pass : rotation + calcul d'élévation
            for (int i = 0; i < celestialBodies.Length; i++)
            {
                CelestialBody body = celestialBodies[i];
                if (body.light == null) continue;

                float angle = dayNightManager.GetCelestialAngle(body.phaseOffset);
                body.currentAngle = angle;

                body.light.transform.rotation = Quaternion.Euler(angle - 90f, 170f, body.orbitTilt);

                float elevationAngle = GetElevationAngle(body.light.transform);

                if (elevationAngle > body.disableBelowAngle && elevationAngle > highestElevation)
                {
                    highestElevation = elevationAngle;
                    highestIndex = i;
                }
            }

            // Deuxième pass : appliquer intensité, couleur, et shadows
            for (int i = 0; i < celestialBodies.Length; i++)
            {
                CelestialBody body = celestialBodies[i];
                if (body.light == null) continue;

                HDAdditionalLightData hdLight = body.light.GetComponent<HDAdditionalLightData>();
                if (hdLight == null) continue;

                float elevationAngle = GetElevationAngle(body.light.transform);
                bool aboveHorizon = elevationAngle > body.disableBelowAngle;

                body.light.enabled = aboveHorizon;

                if (!aboveHorizon) continue;

                float heightFactor = Mathf.Clamp01(elevationAngle / 90f);

                // Intensité via HDRP (en lux pour directional)
                float intensityCurve = Mathf.Sin(heightFactor * Mathf.PI * 0.5f);
                body.light.lightUnit = UnityEngine.Rendering.LightUnit.Lux;
                body.light.intensity = Mathf.Lerp(0f, body.maxIntensity, body.intensityProgression.Evaluate(heightFactor));

                // Couleur via température de couleur uniquement
                hdLight.EnableColorTemperature(true);
                body.light.colorTemperature = Mathf.Lerp(body.horizonColorTemperature, body.zenithColorTemperature, heightFactor);
                body.light.color = Color.white;

                // Shadows : une seule directional light peut cast des shadows en HDRP
                hdLight.EnableShadows(i == highestIndex);

                if (i == highestIndex)
                    hdLight.SetShadowUpdateMode(ShadowUpdateMode.EveryFrame);
            }
        }

        private float GetElevationAngle(Transform lightTransform)
        {
            // Le forward d'une directional light pointe vers le sol quand elle éclaire
            // L'angle d'élévation = angle entre le forward et l'horizon
            float dotDown = Vector3.Dot(lightTransform.forward, Vector3.down);
            return Mathf.Asin(dotDown) * Mathf.Rad2Deg;
        }

    }
}
