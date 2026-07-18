using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ocean
{
    [System.Serializable]
    public class UnderwaterPass : CustomPass
    {
        [Header("References")]

        [Tooltip("Assign Hidden/Ocean/UnderwaterEffect shader. Required for builds (Shader.Find fails for unreferenced Hidden shaders).")]
        public Shader underwaterShader;

        [Tooltip("Ocean settings asset. Underwater parameters are read from here.")]
        public OceanSettings settings;

        [Tooltip("Override sun light. If null, uses RenderSettings.sun or first directional light.")]
        public Light sunLight;

        private Material _underwaterMaterial;

        // Fog
        private static readonly int ID_WaterLevel              = Shader.PropertyToID("_WaterLevel");
        private static readonly int ID_FogDensity              = Shader.PropertyToID("_UnderwaterFogDensity");
        private static readonly int ID_FogStartDist            = Shader.PropertyToID("_UnderwaterFogStartDist");
        private static readonly int ID_FogColor                = Shader.PropertyToID("_UnderwaterFogColor");
        private static readonly int ID_DepthAbsorption         = Shader.PropertyToID("_UnderwaterDepthAbsorption");
        private static readonly int ID_DepthDarkeningMin       = Shader.PropertyToID("_UnderwaterDepthDarkeningMin");

        // God rays
        private static readonly int ID_GodRayIntensity         = Shader.PropertyToID("_GodRayIntensity");
        private static readonly int ID_GodRayColor             = Shader.PropertyToID("_GodRayColor");
        private static readonly int ID_BeamThresholdLo         = Shader.PropertyToID("_BeamThresholdLo");
        private static readonly int ID_BeamThresholdHi         = Shader.PropertyToID("_BeamThresholdHi");
        private static readonly int ID_BeamScale               = Shader.PropertyToID("_BeamScale");
        private static readonly int ID_BeamSunFollow           = Shader.PropertyToID("_BeamSunFollow");
        private static readonly int ID_BeamDepthFade           = Shader.PropertyToID("_BeamDepthFade");
        private static readonly int ID_BeamExtinction          = Shader.PropertyToID("_BeamExtinction");
        private static readonly int ID_GodRayMaxDist           = Shader.PropertyToID("_GodRayMaxDist");
        private static readonly int ID_GodRaySpeed             = Shader.PropertyToID("_GodRaySpeed");
        private static readonly int ID_GodRayFadeInDepth      = Shader.PropertyToID("_GodRayFadeInDepth");
        private static readonly int ID_SunDirWS                = Shader.PropertyToID("_SunDirWS");
        private static readonly int ID_UnderwaterDebug          = Shader.PropertyToID("_UnderwaterDebug");
        private static readonly int ID_SurfaceDistortion       = Shader.PropertyToID("_SurfaceDistortion");
        private static readonly int ID_SnellWindowDepth       = Shader.PropertyToID("_SnellWindowDepth");
        private static readonly int ID_ScreenDistStrength     = Shader.PropertyToID("_ScreenDistStrength");
        private static readonly int ID_ScreenDistSpeed        = Shader.PropertyToID("_ScreenDistSpeed");
        private static readonly int ID_ScreenDistScale        = Shader.PropertyToID("_ScreenDistScale");
        private static readonly int ID_UnderwaterHeightScale  = Shader.PropertyToID("_UnderwaterHeightScale");
        private static readonly int ID_GodRaySteps            = Shader.PropertyToID("_GodRaySteps");
        private static readonly int ID_UnderwaterCamPixelSize = Shader.PropertyToID("_UnderwaterCamPixelSize");
        private static readonly int ID_UnderwaterTempRT       = Shader.PropertyToID("_UnderwaterTempRT");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = underwaterShader;
            #if UNITY_EDITOR
            if (shader == null) shader = Shader.Find("Hidden/Ocean/UnderwaterEffect");
            #endif
            if (shader == null)
            {
                Debug.LogError("[UnderwaterPass] Cannot find 'Hidden/Ocean/UnderwaterEffect' shader. Assign it in the Custom Pass Volume Inspector.");
                return;
            }

            _underwaterMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureMaterial()
        {
            if (_underwaterMaterial != null && _underwaterMaterial.shader != null
                && _underwaterMaterial.shader.isSupported)
                return;

            CoreUtils.Destroy(_underwaterMaterial);
            var shader = underwaterShader;
            #if UNITY_EDITOR
            if (shader == null) shader = Shader.Find("Hidden/Ocean/UnderwaterEffect");
            #endif
            if (shader != null)
                _underwaterMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            EnsureMaterial();
            if (_underwaterMaterial == null || settings == null || !settings.enableUnderwater)
                return;

            Camera cam = ctx.hdCamera.camera;
            if (cam.name == "_OceanReflectionCam")
                return;

            bool isSolidRedTest = settings.underwaterDebugMode == UnderwaterDebugMode.SolidRed;

            if (!isSolidRedTest && cam.transform.position.y >= settings.waterLevel)
                return;

            var cmd = ctx.cmd;

            _underwaterMaterial.SetFloat(ID_WaterLevel, settings.waterLevel);
            _underwaterMaterial.SetFloat(ID_FogDensity, settings.underwaterFogDensity);
            _underwaterMaterial.SetFloat(ID_FogStartDist, settings.underwaterFogStartDistance);
            _underwaterMaterial.SetColor(ID_FogColor, settings.underwaterFogColor);
            _underwaterMaterial.SetFloat(ID_DepthAbsorption, settings.underwaterDepthAbsorption);
            _underwaterMaterial.SetFloat(ID_DepthDarkeningMin, settings.underwaterDepthDarkeningMin);

            float sharpness = settings.godRaySharpness;
            float lo = Mathf.Lerp(0.35f, 0.60f, sharpness);
            float hi = lo + Mathf.Lerp(0.25f, 0.12f, sharpness);

            _underwaterMaterial.SetFloat(ID_GodRayIntensity, settings.godRayIntensity);
            _underwaterMaterial.SetColor(ID_GodRayColor, settings.godRayColor);
            _underwaterMaterial.SetFloat(ID_BeamThresholdLo, lo);
            _underwaterMaterial.SetFloat(ID_BeamThresholdHi, hi);
            _underwaterMaterial.SetFloat(ID_BeamScale, settings.godRayBeamScale);
            _underwaterMaterial.SetFloat(ID_BeamSunFollow, settings.godRaySunFollow);
            _underwaterMaterial.SetFloat(ID_BeamDepthFade, settings.godRayDepthFade);
            _underwaterMaterial.SetFloat(ID_BeamExtinction, settings.godRayExtinction);
            _underwaterMaterial.SetFloat(ID_GodRayMaxDist, settings.godRayMaxDist);
            _underwaterMaterial.SetFloat(ID_GodRaySpeed, settings.godRaySpeed);
            _underwaterMaterial.SetFloat(ID_GodRayFadeInDepth, settings.godRayFadeInDepth);
            _underwaterMaterial.SetInt(ID_UnderwaterDebug, (int)settings.underwaterDebugMode);

            Light sun = FindSun();
            Vector3 sunDir = sun != null ? -sun.transform.forward : Vector3.up;
            sunDir.Normalize();
            _underwaterMaterial.SetVector(ID_SunDirWS, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));

            _underwaterMaterial.SetFloat(ID_SurfaceDistortion, settings.surfaceFromBelowDistortion);
            _underwaterMaterial.SetFloat(ID_SnellWindowDepth, settings.snellWindowDepth);
            _underwaterMaterial.SetFloat(ID_ScreenDistStrength, settings.underwaterDistortionStrength);
            _underwaterMaterial.SetFloat(ID_ScreenDistSpeed, settings.underwaterDistortionSpeed);
            _underwaterMaterial.SetFloat(ID_ScreenDistScale, settings.underwaterDistortionScale);
            _underwaterMaterial.SetFloat(ID_UnderwaterHeightScale, settings.heightScale);
            _underwaterMaterial.SetInt(ID_GodRaySteps, settings.godRaySteps);

            int camW = cam.pixelWidth;
            int camH = cam.pixelHeight;
            _underwaterMaterial.SetVector(ID_UnderwaterCamPixelSize, new Vector4(camW, camH, 0, 0));

            float scale = Mathf.Clamp(settings.underwaterResolutionScale, 0.25f, 1f);
            if (scale < 0.999f)
            {
                int w = Mathf.Max(1, (int)(camW * scale));
                int h = Mathf.Max(1, (int)(camH * scale));
                cmd.GetTemporaryRT(ID_UnderwaterTempRT, w, h, 0, FilterMode.Bilinear,
                                   UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
                CoreUtils.SetRenderTarget(cmd, ID_UnderwaterTempRT);
                CoreUtils.DrawFullScreen(cmd, _underwaterMaterial, shaderPassId: 0);

                CoreUtils.SetRenderTarget(cmd, ctx.cameraColorBuffer);
                CoreUtils.DrawFullScreen(cmd, _underwaterMaterial, shaderPassId: 1);
                cmd.ReleaseTemporaryRT(ID_UnderwaterTempRT);
            }
            else
            {
                CoreUtils.SetRenderTarget(cmd, ctx.cameraColorBuffer);
                CoreUtils.DrawFullScreen(cmd, _underwaterMaterial, shaderPassId: 0);
            }
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_underwaterMaterial);
        }

        private Light _cachedSun;

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
