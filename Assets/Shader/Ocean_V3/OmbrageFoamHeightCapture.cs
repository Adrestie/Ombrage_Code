using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// Capture top-down de la hauteur du décor (empreinte) dans une RenderTexture,
    /// pour l'edge foam d'empreinte (collier symétrique, indépendant de la vue).
    ///
    /// Rendu via CommandBuffer.DrawRenderer + VP orthographique manuelle (léger,
    /// RP-agnostique). Bind en global : _OmbrageFoamHeightRT, _OmbrageFoamRegion
    /// (xy = centre, z = 1/taille, w = taille), _OmbrageFoamWaterLevel.
    ///
    /// L'intensité / largeur / bruit du collier restent pilotés par le composant
    /// Edge Foam Controller (globales _OmbrageEdgeFoam*).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Ombrage/Visual/Ocean/Foam Height Capture")]
    public sealed class OmbrageFoamHeightCapture : MonoBehaviour
    {
        public enum UpdateMode { Static, EveryFrame }

        [Tooltip("Shader Hidden/Ombrage/FoamHeight.")]
        public Shader captureShader;

        [Tooltip("Calques des objets émergents à capturer. À définir explicitement " +
                 "(ne PAS mettre le sol/terrain, sinon foam partout).")]
        public LayerMask captureLayers = 0;

        [Tooltip("Centre XZ de la région = position de ce GameObject si coché.")]
        public bool followTransform = true;
        public Vector2 regionCenter = Vector2.zero;

        [Tooltip("Taille de la région capturée (mètres).")]
        [Min(1f)] public float regionSize = 50f;

        [Tooltip("Résolution de la texture de capture.")]
        [Min(16)] public int resolution = 512;

        [Tooltip("Niveau de l'eau (Y monde).")]
        public float waterLevel = 0f;

        [Tooltip("Bornes verticales capturées (Y monde) : min = sous le fond, max = au-dessus des objets.")]
        public float heightMin = -20f;
        public float heightMax = 50f;

        [Tooltip("Static : capture une fois (objets fixes). EveryFrame : objets mobiles.")]
        public UpdateMode updateMode = UpdateMode.Static;

        private RenderTexture _rt;
        private Material _mat;
        private CommandBuffer _cmd;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private static readonly int RTId = Shader.PropertyToID("_OmbrageFoamHeightRT");
        private static readonly int RegionId = Shader.PropertyToID("_OmbrageFoamRegion");
        private static readonly int WaterLevelId = Shader.PropertyToID("_OmbrageFoamWaterLevel");

        private Vector2 Center => followTransform
            ? new Vector2(transform.position.x, transform.position.z)
            : regionCenter;

        private void OnEnable() { EnsureResources(); Capture(); }
        private void OnDisable() { Cleanup(); }

        private void Update()
        {
            if (updateMode == UpdateMode.EveryFrame) Capture();
            BindGlobals();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) { EnsureResources(); Capture(); }
        }

        private void EnsureResources()
        {
            if (_mat == null)
            {
                var sh = captureShader != null ? captureShader : Shader.Find("Hidden/Ombrage/FoamHeight");
                if (sh != null) _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_rt == null || _rt.width != resolution)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.RFloat)
                {
                    name = "OmbrageFoamHeightRT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _rt.Create();
            }
            if (_cmd == null) _cmd = new CommandBuffer { name = "Ombrage Foam Height Capture" };
        }

        [ContextMenu("Recapture")]
        public void Capture()
        {
            EnsureResources();
            if (_mat == null || _rt == null) return;

            Vector2 c = Center;

            // Collecte des renderers sur les calques choisis.
            _renderers.Clear();
            var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var r in all)
                if (((1 << r.gameObject.layer) & captureLayers.value) != 0 && r.enabled)
                    _renderers.Add(r);

            // VP orthographique top-down (caméra virtuelle regardant -Y).
            Vector3 eye = new Vector3(c.x, heightMax, c.y);
            Matrix4x4 view = Matrix4x4.TRS(eye, Quaternion.LookRotation(Vector3.down, Vector3.forward), Vector3.one).inverse;
            view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * view; // convention GL : caméra regarde -Z
            float half = regionSize * 0.5f;
            // Projection convention Unity (SetViewProjectionMatrices applique la conversion GPU).
            Matrix4x4 proj = Matrix4x4.Ortho(-half, half, -half, half, 0.01f, Mathf.Max(0.02f, heightMax - heightMin));

            _cmd.Clear();
            _cmd.SetRenderTarget(_rt);
            _cmd.ClearRenderTarget(true, true, new Color(heightMin, heightMin, heightMin, heightMin));
            _cmd.SetViewProjectionMatrices(view, proj);
            foreach (var r in _renderers)
                _cmd.DrawRenderer(r, _mat, 0, 0);
            Graphics.ExecuteCommandBuffer(_cmd);

            BindGlobals();
        }

        private void BindGlobals()
        {
            if (_rt == null) return;
            Vector2 c = Center;
            Shader.SetGlobalTexture(RTId, _rt);
            Shader.SetGlobalVector(RegionId, new Vector4(c.x, c.y, 1f / Mathf.Max(regionSize, 0.001f), regionSize));
            Shader.SetGlobalFloat(WaterLevelId, waterLevel);
        }

        private void Cleanup()
        {
            if (_rt != null) { _rt.Release(); _rt = null; }
            if (_mat != null)
            {
                if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat);
                _mat = null;
            }
            _cmd?.Release();
            _cmd = null;
        }
    }
}
