using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// Empreinte de foam autour des objets émergents, approche STAMP (comme le wake V1) :
    /// pour chaque objet d'un LayerMask, on inscrit un disque à falloff (centré sur l'objet,
    /// rayon = ses bounds) dans une RenderTexture région via un Blit accumulé en BlendOp Max.
    /// Aucune capture de mesh, aucune matrice per-objet -> fiable.
    ///
    /// Bind en global : _OmbrageFoamStampRT, _OmbrageFoamRegion (xy = centre, z = 1/taille,
    /// w = taille). Le shader d'eau sample cette RT -> collier symétrique, indépendant de la vue.
    /// L'intensité / le bruit du collier restent pilotés par l'Edge Foam Controller.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Ombrage/Visual/Ocean/Foam Stamp")]
    public sealed class OmbrageFoamHeightCapture : MonoBehaviour
    {
        public enum UpdateMode { Static, EveryFrame }

        [Tooltip("Shader Hidden/Ombrage/FoamStamp.")]
        public Shader stampShader;

        [Tooltip("Calques des objets émergents. À définir explicitement (pas le sol).")]
        public LayerMask captureLayers = 0;

        [Tooltip("Centre XZ de la région = position de ce GameObject si coché.")]
        public bool followTransform = true;
        public Vector2 regionCenter = Vector2.zero;

        [Tooltip("Taille de la région (mètres).")]
        [Min(1f)] public float regionSize = 50f;

        [Tooltip("Résolution de la texture d'empreinte.")]
        [Min(16)] public int resolution = 512;

        [Tooltip("Largeur du collier au-delà du rayon de l'objet (mètres).")]
        [Min(0.01f)] public float collarWidth = 2f;

        [Tooltip("Static : stamp une fois (objets fixes). EveryFrame : objets mobiles.")]
        public UpdateMode updateMode = UpdateMode.Static;

        private RenderTexture _rt;
        private Material _mat;
        private CommandBuffer _cmd;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private static readonly int RTId = Shader.PropertyToID("_OmbrageFoamStampRT");
        private static readonly int RegionId = Shader.PropertyToID("_OmbrageFoamRegion");
        private static readonly int StampCenterId = Shader.PropertyToID("_OmbrageStampCenter");
        private static readonly int StampRadiusId = Shader.PropertyToID("_OmbrageStampRadiusUV");
        private static readonly int StampWidthId = Shader.PropertyToID("_OmbrageStampWidthUV");

        private Vector2 Center => followTransform
            ? new Vector2(transform.position.x, transform.position.z)
            : regionCenter;

        private void OnEnable() { EnsureResources(); Stamp(); }
        private void OnDisable() { Cleanup(); }

        private void Update()
        {
            if (updateMode == UpdateMode.EveryFrame) Stamp();
            BindGlobals();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) { EnsureResources(); Stamp(); }
        }

        private void EnsureResources()
        {
            if (_mat == null)
            {
                var sh = stampShader != null ? stampShader : Shader.Find("Hidden/Ombrage/FoamStamp");
                if (sh != null) _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_rt == null || _rt.width != resolution)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.R8)
                {
                    name = "OmbrageFoamStampRT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _rt.Create();
            }
            if (_cmd == null) _cmd = new CommandBuffer { name = "Ombrage Foam Stamp" };
        }

        [ContextMenu("Restamp")]
        public void Stamp()
        {
            EnsureResources();
            if (_mat == null || _rt == null) return;

            Vector2 c = Center;
            float invSize = 1f / Mathf.Max(regionSize, 0.001f);

            _renderers.Clear();
            var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var r in all)
                if (((1 << r.gameObject.layer) & captureLayers.value) != 0 && r.enabled)
                    _renderers.Add(r);

            _cmd.Clear();
            _cmd.SetRenderTarget(_rt);
            _cmd.ClearRenderTarget(false, true, Color.clear);

            foreach (var r in _renderers)
            {
                Bounds b = r.bounds;
                Vector2 objCenter = new Vector2(b.center.x, b.center.z);
                float objRadius = Mathf.Max(b.extents.x, b.extents.z);

                Vector2 uvCenter = (objCenter - c) * invSize + new Vector2(0.5f, 0.5f);
                _cmd.SetGlobalVector(StampCenterId, uvCenter);
                _cmd.SetGlobalFloat(StampRadiusId, objRadius * invSize);
                _cmd.SetGlobalFloat(StampWidthId, collarWidth * invSize);

                // Blit plein écran accumulé (BlendOp Max) dans la RT courante.
                CoreUtils.DrawFullScreen(_cmd, _mat, shaderPassId: 0);
            }

            Graphics.ExecuteCommandBuffer(_cmd);
            BindGlobals();
        }

        private void BindGlobals()
        {
            if (_rt == null) return;
            Vector2 c = Center;
            Shader.SetGlobalTexture(RTId, _rt);
            Shader.SetGlobalVector(RegionId, new Vector4(c.x, c.y, 1f / Mathf.Max(regionSize, 0.001f), regionSize));
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
