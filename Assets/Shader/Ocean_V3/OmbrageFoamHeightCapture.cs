using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Visual.Ocean
{
    /// <summary>
    /// Empreinte d'écume autour des objets émergents — capture de la TRANCHE au niveau
    /// de l'eau (et non la silhouette projetée). Une projection ortho top-down avec un
    /// near/far serré autour du niveau d'eau ne rasterise que la coupe de la géométrie
    /// au plan de l'eau : un objet plus large en haut est clippé par le near plane et
    /// n'occulte rien -> le contour capturé colle à la jonction réelle avec l'eau.
    ///
    /// Rendu via CommandBuffer (SetViewProjectionMatrices + DrawMesh) : les matrices
    /// sont bindées par le moteur, pas à la main -> fiable en édition comme en play
    /// (l'immédiat GL, sans contexte de rendu valide hors boucle, donnait une RT noire).
    ///
    /// Sortie : R8 coverage (0/1) bindée en global _OmbrageFoamStampRT + _OmbrageFoamRegion
    /// (xy = centre monde, z = 1/taille, w = taille). Le shader d'eau (SampleWaterSurface)
    /// sample déjà cette RT. Intensité / bruit du collier pilotés par l'Edge Foam Controller.
    ///
    /// NB (étape 1 / spike) : pas encore de dilatation. La RT contient la coupe brute ;
    /// pour un objet vertical le collier visible hors silhouette est donc quasi nul —
    /// le test ici est que l'empreinte colle à la base (objet évasé -> pas de débord au sommet).
    /// La largeur du collier arrive à l'étape 2 (flou).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Ombrage/Visual/Ocean/Foam Footprint Capture")]
    public sealed class OmbrageFoamHeightCapture : MonoBehaviour
    {
        public enum UpdateMode { Static, EveryFrame }

        [Tooltip("Shader Hidden/Ombrage/FoamFootprint (auto-trouvé si vide).")]
        public Shader footprintShader;

        [Tooltip("Calques des objets émergents. À définir explicitement (pas le sol).")]
        public LayerMask captureLayers = 0;

        [Tooltip("Centre XZ de la région = position de ce GameObject si coché.")]
        public bool followTransform = true;
        public Vector2 regionCenter = Vector2.zero;

        [Tooltip("Taille de la région capturée (mètres).")]
        [Min(1f)] public float regionSize = 50f;

        [Tooltip("Résolution de la texture d'empreinte.")]
        [Min(16)] public int resolution = 512;

        [Tooltip("Niveau de l'eau (Y monde) autour duquel la tranche est prise.")]
        public float waterLevel = 0f;

        [Tooltip("Épaisseur de la tranche capturée autour du niveau d'eau (mètres). ~ amplitude des vagues.")]
        [Min(0.02f)] public float slabThickness = 2f;

        [Tooltip("Static : capture une fois (objets fixes). EveryFrame : objets mobiles.")]
        public UpdateMode updateMode = UpdateMode.Static;

        private RenderTexture _rt;
        private Material _mat;
        private CommandBuffer _cmd;
        private readonly List<MeshFilter> _meshes = new List<MeshFilter>();

        private static readonly int RTId = Shader.PropertyToID("_OmbrageFoamStampRT");
        private static readonly int RegionId = Shader.PropertyToID("_OmbrageFoamRegion");

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
                var sh = footprintShader != null ? footprintShader : Shader.Find("Hidden/Ombrage/FoamFootprint");
                if (sh != null) _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_rt == null || _rt.width != resolution)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.R8)
                {
                    name = "OmbrageFoamFootprintRT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _rt.Create();
            }
            if (_cmd == null) _cmd = new CommandBuffer { name = "Ombrage Foam Footprint" };
        }

        [ContextMenu("Recapture")]
        public void Capture()
        {
            EnsureResources();
            if (_mat == null || _rt == null) return;

            Vector2 c = Center;
            float half = Mathf.Max(regionSize, 0.001f) * 0.5f;
            float t = Mathf.Max(slabThickness, 0.02f) * 0.5f;
            const float nearPad = 1f;

            // Caméra ortho top-down : œil juste au-dessus de la dalle, regard vers -Y.
            // near/far encadrent [waterLevel - t, waterLevel + t] -> seule la tranche est rendue.
            Vector3 eye = new Vector3(c.x, waterLevel + t + nearPad, c.y);
            Quaternion rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            Matrix4x4 camLTW = Matrix4x4.TRS(eye, rot, Vector3.one);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * camLTW.inverse;
            Matrix4x4 proj = Matrix4x4.Ortho(-half, half, -half, half, nearPad, nearPad + 2f * t);

            CollectMeshes();

            _cmd.Clear();
            _cmd.SetRenderTarget(_rt);
            _cmd.ClearRenderTarget(false, true, Color.clear);
            _cmd.SetViewProjectionMatrices(view, proj);

            foreach (var mf in _meshes)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount; s++)
                    _cmd.DrawMesh(mesh, m, _mat, s, 0);
            }

            Graphics.ExecuteCommandBuffer(_cmd);
            BindGlobals();
        }

        private void CollectMeshes()
        {
            _meshes.Clear();
            var all = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var mf in all)
            {
                if (mf.sharedMesh == null) continue;
                if (((1 << mf.gameObject.layer) & captureLayers.value) == 0) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                _meshes.Add(mf);
            }
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
