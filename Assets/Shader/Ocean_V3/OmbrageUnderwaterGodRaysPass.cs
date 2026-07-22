using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// God-rays underwater V3 — portage de l'approche V1 (screen-space, beam depuis
    /// la courbure de la surface), décorrélé du brouillard volumétrique HDRP.
    ///
    /// Le pass appelle l'API publique WaterSurface.SetGlobalTextures() pour exposer
    /// globalement le buffer de gradient + le CB de bande, puis marche les god-rays
    /// en plein écran et les compose sur la couleur caméra.
    ///
    /// À placer dans un Custom Pass Volume. Injection conseillée : "Before Post Process".
    /// </summary>
    [System.Serializable]
    public class OmbrageUnderwaterGodRaysPass : CustomPass
    {
        [Header("Références")]
        [Tooltip("Shader Hidden/Ombrage/UnderwaterGodRays (requis pour les builds).")]
        public Shader godRaysShader;

        [Tooltip("La WaterSurface océan : expose ses buffers en global et donne le niveau d'eau.")]
        public WaterSurface waterSurface;

        [Tooltip("Soleil. Si null : RenderSettings.sun ou la première directionnelle.")]
        public Light sunLight;

        [Header("God rays")]
        [Min(0f)] public float intensity = 1.0f;
        [ColorUsage(false, true)] public Color color = Color.white;
        [Range(0f, 1f)] public float sunFollow = 0.5f;
        [Min(1f)] public float maxDistance = 60f;
        [Range(4, 64)] public int steps = 24;
        [Min(0f)] public float fadeInDepth = 2f;

        [Header("Beam (avancé)")]
        [Min(0.01f)] public float beamScale = 1.0f;
        [Tooltip("Amplifie la courbure de la surface (le gradient HDRP est de faible magnitude). Monter jusqu'à voir des beams.")]
        [Range(1f, 200f)] public float beamGain = 30f;
        [Range(0f, 1f)] public float sharpness = 0.5f;
        [Min(0f)] public float depthFade = 0.05f;
        [Min(0f)] public float extinction = 0.02f;

        [Header("Debug")]
        [Tooltip("Affiche les god-rays seuls (sans la scène).")]
        public bool showGodRaysOnly = false;

        private Material _material;

        private static readonly int ID_WaterLevel   = Shader.PropertyToID("_WaterLevel");
        private static readonly int ID_BeamScale    = Shader.PropertyToID("_BeamScale");
        private static readonly int ID_BeamGain     = Shader.PropertyToID("_BeamGain");
        private static readonly int ID_BeamLo       = Shader.PropertyToID("_BeamThresholdLo");
        private static readonly int ID_BeamHi       = Shader.PropertyToID("_BeamThresholdHi");
        private static readonly int ID_BeamSunFollow= Shader.PropertyToID("_BeamSunFollow");
        private static readonly int ID_BeamDepthFade= Shader.PropertyToID("_BeamDepthFade");
        private static readonly int ID_BeamExtinct  = Shader.PropertyToID("_BeamExtinction");
        private static readonly int ID_Intensity    = Shader.PropertyToID("_GodRayIntensity");
        private static readonly int ID_Color        = Shader.PropertyToID("_GodRayColor");
        private static readonly int ID_MaxDist      = Shader.PropertyToID("_GodRayMaxDist");
        private static readonly int ID_FadeInDepth   = Shader.PropertyToID("_GodRayFadeInDepth");
        private static readonly int ID_Steps        = Shader.PropertyToID("_GodRaySteps");
        private static readonly int ID_Debug        = Shader.PropertyToID("_GodRayDebug");
        private static readonly int ID_SunDir       = Shader.PropertyToID("_SunDirWS");
        private static readonly int ID_CamPixelSize = Shader.PropertyToID("_UnderwaterCamPixelSize");

        private Light _cachedSun;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            EnsureMaterial();
        }

        private void EnsureMaterial()
        {
            if (_material != null && _material.shader != null && _material.shader.isSupported)
                return;
            CoreUtils.Destroy(_material);
            var shader = godRaysShader;
#if UNITY_EDITOR
            if (shader == null) shader = Shader.Find("Hidden/Ombrage/UnderwaterGodRays");
#endif
            if (shader != null)
                _material = CoreUtils.CreateEngineMaterial(shader);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            EnsureMaterial();
            if (_material == null || waterSurface == null)
                return;

            // Expose buffer de gradient + CB de bande en global (API publique HDRP).
            if (!waterSurface.SetGlobalTextures())
                return;

            var cam = ctx.hdCamera.camera;

            _material.SetFloat(ID_WaterLevel, waterSurface.transform.position.y);
            _material.SetFloat(ID_BeamScale, beamScale);
            _material.SetFloat(ID_BeamGain, beamGain);

            float lo = Mathf.Lerp(0.35f, 0.60f, sharpness);
            float hi = lo + Mathf.Lerp(0.25f, 0.12f, sharpness);
            _material.SetFloat(ID_BeamLo, lo);
            _material.SetFloat(ID_BeamHi, hi);

            _material.SetFloat(ID_BeamSunFollow, sunFollow);
            _material.SetFloat(ID_BeamDepthFade, depthFade);
            _material.SetFloat(ID_BeamExtinct, extinction);
            _material.SetFloat(ID_Intensity, intensity);
            _material.SetColor(ID_Color, color);
            _material.SetFloat(ID_MaxDist, maxDistance);
            _material.SetFloat(ID_FadeInDepth, fadeInDepth);
            _material.SetInt(ID_Steps, steps);
            _material.SetInt(ID_Debug, showGodRaysOnly ? 1 : 0);

            Light sun = FindSun();
            Vector3 sunDir = sun != null ? -sun.transform.forward : Vector3.up;
            sunDir.Normalize();
            _material.SetVector(ID_SunDir, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));

            _material.SetVector(ID_CamPixelSize, new Vector4(cam.pixelWidth, cam.pixelHeight, 0f, 0f));

            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer);
            CoreUtils.DrawFullScreen(ctx.cmd, _material, shaderPassId: 0);
        }

        private Light FindSun()
        {
            if (sunLight != null && sunLight.isActiveAndEnabled)
                return sunLight;
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
            if (_cachedSun != null && _cachedSun.isActiveAndEnabled)
                return _cachedSun;
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
        }
    }
}
