using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ocean
{
    [ExecuteAlways]
    public class UnderwaterLightingController : MonoBehaviour
    {
        [Header("References")]

        [Tooltip("Ocean settings asset. Reads waterLevel and underwater lighting parameters.")]
        public OceanSettings settings;

        [Tooltip("Camera to track. If null, uses Camera.main.")]
        public Camera targetCamera;

        [Tooltip("Override sun light. If null, uses RenderSettings.sun or first directional light.")]
        public Light sunLight;

        [Header("Volume")]

        [Tooltip("HDRP Volume with the underwater profile. Weight is driven by this script.")]
        public Volume underwaterVolume;

        private Light _cachedSun;
        private float _blendFactor;

        private void LateUpdate()
        {
            if (settings == null || !settings.enableUnderwaterLighting)
            {
                if (underwaterVolume != null)
                    underwaterVolume.weight = 0f;
                return;
            }

            Camera cam = targetCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                if (underwaterVolume != null)
                    underwaterVolume.weight = 0f;
                return;
            }

            float depth = settings.waterLevel - cam.transform.position.y;
            float transitionDepth = Mathf.Max(settings.transitionDepth, 0.01f);

            _blendFactor = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(depth / transitionDepth));

            if (_blendFactor < 0.001f)
            {
                if (underwaterVolume != null)
                    underwaterVolume.weight = 0f;
                return;
            }

            ApplyLightAttenuation(depth);
            ApplyVolumeBlend();
        }

        private void ApplyLightAttenuation(float depth)
        {
            Light sun = FindSun();
            if (sun == null) return;

            Vector3 coeff = settings.absorptionCoefficients;
            float scaledDepth = depth * settings.attenuationDepthScale;

            float attR = Mathf.Exp(-coeff.x * scaledDepth);
            float attG = Mathf.Exp(-coeff.y * scaledDepth);
            float attB = Mathf.Exp(-coeff.z * scaledDepth);

            float intensityMult = Mathf.Max(
                settings.lightIntensityFloor,
                Mathf.Exp(-settings.lightIntensityDecay * scaledDepth));

            sun.intensity *= Mathf.Lerp(1f, intensityMult, _blendFactor);

            Color spectralTint = new Color(attR, attG, attB, 1f);
            sun.color *= Color.Lerp(Color.white, spectralTint, _blendFactor);
        }

        private void ApplyVolumeBlend()
        {
            if (underwaterVolume != null)
                underwaterVolume.weight = _blendFactor;
        }

        private Light FindSun()
        {
            if (sunLight != null && sunLight.isActiveAndEnabled)
                return sunLight;

            if (RenderSettings.sun != null)
                return RenderSettings.sun;

            if (_cachedSun != null && _cachedSun.isActiveAndEnabled)
                return _cachedSun;

            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional && l.isActiveAndEnabled)
                {
                    _cachedSun = l;
                    return l;
                }
            }
            return null;
        }
    }
}
