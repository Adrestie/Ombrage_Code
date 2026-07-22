using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// God-rays underwater V3 — portage de l'approche V1 (screen-space, beam depuis
    /// la courbure de la surface), décorrélé du brouillard volumétrique HDRP.
    ///
    /// ÉTAPE 1 — SPIKE : valide seulement la source du beam (gradient du Water System
    /// HDRP échantillonné dans un CustomPass). Le pass appelle l'API publique
    /// WaterSurface.SetGlobalTextures() pour exposer globalement le buffer de gradient
    /// + le CB de bande, puis dessine en plein écran le signal de beam (niveaux de gris).
    ///
    /// À placer dans un Custom Pass Volume. Injection conseillée : "Before Post Process".
    /// </summary>
    [System.Serializable]
    public class OmbrageUnderwaterGodRaysPass : CustomPass
    {
        [Tooltip("Shader Hidden/Ombrage/UnderwaterGodRays (requis pour les builds).")]
        public Shader godRaysShader;

        [Tooltip("La WaterSurface océan : sert à exposer ses buffers en global et à lire le niveau d'eau.")]
        public WaterSurface waterSurface;

        [Tooltip("Échelle du motif de beam (contrôle l'epsilon de courbure).")]
        [Min(0.01f)] public float beamScale = 1.0f;

        [Tooltip("Netteté des beams (mappée sur les seuils du smoothstep).")]
        [Range(0f, 1f)] public float sharpness = 0.5f;

        private Material _material;

        private static readonly int ID_WaterLevel   = Shader.PropertyToID("_WaterLevel");
        private static readonly int ID_BeamScale     = Shader.PropertyToID("_BeamScale");
        private static readonly int ID_BeamLo        = Shader.PropertyToID("_BeamThresholdLo");
        private static readonly int ID_BeamHi        = Shader.PropertyToID("_BeamThresholdHi");
        private static readonly int ID_CamPixelSize  = Shader.PropertyToID("_UnderwaterCamPixelSize");

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

            // Expose le buffer de gradient + le CB de bande en global (API publique HDRP).
            if (!waterSurface.SetGlobalTextures())
                return;

            var cam = ctx.hdCamera.camera;

            _material.SetFloat(ID_WaterLevel, waterSurface.transform.position.y);
            _material.SetFloat(ID_BeamScale, beamScale);

            float lo = Mathf.Lerp(0.35f, 0.60f, sharpness);
            float hi = lo + Mathf.Lerp(0.25f, 0.12f, sharpness);
            _material.SetFloat(ID_BeamLo, lo);
            _material.SetFloat(ID_BeamHi, hi);

            _material.SetVector(ID_CamPixelSize, new Vector4(cam.pixelWidth, cam.pixelHeight, 0f, 0f));

            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer);
            CoreUtils.DrawFullScreen(ctx.cmd, _material, shaderPassId: 0);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
        }
    }
}
