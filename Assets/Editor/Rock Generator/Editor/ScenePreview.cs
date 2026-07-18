using System;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.Editor
{
    /// <summary>
    /// A transient rock GameObject placed in the active scene, so the generated mesh can be
    /// inspected directly in the Scene and Game views under the project's real environment
    /// (sky, lighting, post-processing).
    ///
    /// The object uses DontSave hide flags: it is never written to the scene file. It is
    /// destroyed by <see cref="Dispose"/> (called when the generator window closes) and is
    /// transparently recreated after a domain reload by the window.
    /// </summary>
    public sealed class ScenePreview : IDisposable
    {
        const string ObjectName = "[Rock Generator Preview]";

        const HideFlags TransientFlags =
            HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        GameObject _go;
        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;
        Mesh _mesh;
        Material _material;
        bool _disposed;

        /// <summary>True once the preview GameObject exists in the scene.</summary>
        public bool Exists => _go != null;

        /// <summary>The transient preview GameObject, or null if not created.</summary>
        public GameObject Root => _go;

        /// <summary>Creates the preview GameObject in the active scene if it does not exist.</summary>
        public void EnsureCreated()
        {
            if (_disposed || _go != null)
                return;

            _go = new GameObject(ObjectName) { hideFlags = TransientFlags };
            _go.transform.position = ComputeSpawnPosition();
            _meshFilter = _go.AddComponent<MeshFilter>();
            _meshRenderer = _go.AddComponent<MeshRenderer>();

            _material = CreateDefaultMaterial();
            _meshRenderer.sharedMaterial = _material;

            if (_mesh != null)
                _meshFilter.sharedMesh = _mesh;
        }

        /// <summary>
        /// Assigns a freshly generated mesh, taking ownership of it. The previously held
        /// mesh is destroyed. Safe to call before <see cref="EnsureCreated"/>; the mesh is
        /// then applied when the GameObject is created.
        /// </summary>
        public void SetMesh(Mesh mesh)
        {
            if (_mesh != null && _mesh != mesh)
                UnityEngine.Object.DestroyImmediate(_mesh);

            _mesh = mesh;
            if (_meshFilter != null)
                _meshFilter.sharedMesh = _mesh;
        }

        /// <summary>
        /// Picks a spawn position so the rock appears in front of the editor camera.
        /// Uses the active Scene view's pivot (the point it looks at and orbits around);
        /// falls back to the world origin when no Scene view is available.
        /// </summary>
        static Vector3 ComputeSpawnPosition()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            return sceneView != null ? sceneView.pivot : Vector3.zero;
        }

        static Material CreateDefaultMaterial()
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
                shader = Shader.Find("Standard"); // defensive fallback outside HDRP

            return new Material(shader)
            {
                name = "Rock Generator Preview Material",
                hideFlags = TransientFlags,
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);
            if (_mesh != null)
                UnityEngine.Object.DestroyImmediate(_mesh);
            if (_material != null)
                UnityEngine.Object.DestroyImmediate(_material);

            _go = null;
            _meshFilter = null;
            _meshRenderer = null;
            _mesh = null;
            _material = null;
        }
    }
}
