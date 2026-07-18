using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ocean
{
    public class ShoreWavePass : CustomPass
    {
        [Header("References")]

        [Tooltip("Assign Hidden/Ocean/ShoreWaveEffect shader. Required for builds.")]
        public Shader shoreWaveShader;

        [Tooltip("Ocean settings asset. Shore wave parameters are read from here.")]
        public OceanSettings settings;

        private Material _material;
        private RTHandle _trackingA;
        private RTHandle _trackingB;
        private bool _pingPong;
        private bool _needsClear = true;

        private static readonly int ID_WaterLevel         = Shader.PropertyToID("_WaterLevel");
        private static readonly int ID_ShoreHeightScale   = Shader.PropertyToID("_ShoreHeightScale");
        private static readonly int ID_ShoreWashHeight    = Shader.PropertyToID("_ShoreWashHeight");
        private static readonly int ID_ShoreWashFoamWidth = Shader.PropertyToID("_ShoreWashFoamWidth");
        private static readonly int ID_ShoreWetDarkening  = Shader.PropertyToID("_ShoreWetDarkening");
        private static readonly int ID_ShoreFoamScale     = Shader.PropertyToID("_ShoreFoamScale");
        private static readonly int ID_ShoreFoamColor     = Shader.PropertyToID("_ShoreFoamColor");
        private static readonly int ID_ShoreWashPower     = Shader.PropertyToID("_ShoreWashPower");
        private static readonly int ID_ShoreWashFadeTime  = Shader.PropertyToID("_ShoreWashFadeTime");
        private static readonly int ID_ShoreTrackingPrev  = Shader.PropertyToID("_ShoreTrackingPrev");
        private static readonly int ID_ShoreTrackingCurr  = Shader.PropertyToID("_ShoreTrackingCurr");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = shoreWaveShader;
            #if UNITY_EDITOR
            if (shader == null) shader = Shader.Find("Hidden/Ocean/ShoreWaveEffect");
            #endif
            if (shader == null)
            {
                Debug.LogError("[ShoreWavePass] Cannot find 'Hidden/Ocean/ShoreWaveEffect' shader. Assign it in the Custom Pass Volume Inspector.");
                return;
            }
            _material = CoreUtils.CreateEngineMaterial(shader);

            _trackingA = RTHandles.Alloc(
                Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32_SFloat,
                enableRandomWrite: false, useDynamicScale: true,
                name: "ShoreTrackingA"
            );
            _trackingB = RTHandles.Alloc(
                Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32_SFloat,
                enableRandomWrite: false, useDynamicScale: true,
                name: "ShoreTrackingB"
            );
        }

        private void EnsureMaterial()
        {
            if (_material != null && _material.shader != null && _material.shader.isSupported)
                return;

            CoreUtils.Destroy(_material);
            var shader = shoreWaveShader;
            #if UNITY_EDITOR
            if (shader == null) shader = Shader.Find("Hidden/Ocean/ShoreWaveEffect");
            #endif
            if (shader != null)
                _material = CoreUtils.CreateEngineMaterial(shader);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            EnsureMaterial();
            if (_material == null || settings == null || !settings.enableShoreWaves)
                return;
            if (_trackingA == null || _trackingB == null)
                return;

            Camera cam = ctx.hdCamera.camera;
            if (cam.name == "_OceanReflectionCam")
                return;

            if (cam.transform.position.y < settings.waterLevel)
                return;

            var cmd = ctx.cmd;

            _material.SetFloat(ID_WaterLevel,         settings.waterLevel);
            _material.SetFloat(ID_ShoreHeightScale,   settings.heightScale);
            _material.SetFloat(ID_ShoreWashHeight,    settings.shoreWashHeight);
            _material.SetFloat(ID_ShoreWashFoamWidth, settings.shoreWashFoamWidth);
            _material.SetFloat(ID_ShoreWetDarkening,  settings.shoreWetDarkening);
            _material.SetFloat(ID_ShoreFoamScale,     settings.shoreFoamNoiseScale);
            _material.SetColor(ID_ShoreFoamColor,     settings.foamColor);
            _material.SetFloat(ID_ShoreWashPower,     settings.shoreWashPower);
            _material.SetFloat(ID_ShoreWashFadeTime,  Mathf.Max(settings.shoreWashFadeTime, 1f));

            var prev = _pingPong ? _trackingA : _trackingB;
            var curr = _pingPong ? _trackingB : _trackingA;

            if (_needsClear)
            {
                CoreUtils.SetRenderTarget(cmd, _trackingA);
                cmd.ClearRenderTarget(false, true, Color.clear);
                CoreUtils.SetRenderTarget(cmd, _trackingB);
                cmd.ClearRenderTarget(false, true, Color.clear);
                _needsClear = false;
            }

            // Pass 0: Update tracking RT
            cmd.SetGlobalTexture(ID_ShoreTrackingPrev, prev);
            CoreUtils.SetRenderTarget(cmd, curr);
            CoreUtils.DrawFullScreen(cmd, _material, shaderPassId: 0);

            // Pass 1: Render foam to screen
            cmd.SetGlobalTexture(ID_ShoreTrackingCurr, curr);
            CoreUtils.SetRenderTarget(cmd, ctx.cameraColorBuffer);
            CoreUtils.DrawFullScreen(cmd, _material, shaderPassId: 1);

            _pingPong = !_pingPong;
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            _trackingA?.Release();
            _trackingB?.Release();
        }
    }
}
