using UnityEngine;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Gère le GameObject temporaire instancié dans la SceneView.
    /// Marqué DontSave : il n'est jamais enregistré dans la scène et doit être
    /// détruit explicitement à la fermeture du tool / reload / entrée en Play Mode.
    /// </summary>
    public class VertexColorPreviewInstance
    {
        const string InstanceName = "[VertexColorEditor] Preview (temp)";

        GameObject _instance;
        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;

        public bool Exists => _instance != null;

        public Transform Transform => _instance != null ? _instance.transform : null;

        public Matrix4x4 LocalToWorld =>
            _instance != null ? _instance.transform.localToWorldMatrix : Matrix4x4.identity;

        /// <summary>
        /// Crée (ou recrée) l'instance de preview à l'origine du monde.
        /// </summary>
        public void Create(Mesh workingMesh)
        {
            Destroy();

            _instance = new GameObject(InstanceName)
            {
                // DontSave : visible dans la hiérarchie comme objet temporaire,
                // mais jamais sérialisé dans la scène.
                hideFlags = HideFlags.DontSave
            };
            _instance.transform.position = Vector3.zero;
            _instance.transform.rotation = Quaternion.identity;
            _instance.transform.localScale = Vector3.one;

            _meshFilter = _instance.AddComponent<MeshFilter>();
            _meshFilter.sharedMesh = workingMesh;

            // MeshRenderer désactivé par défaut : utilisé uniquement en mode
            // "Show Material" pour afficher le mesh avec un vrai material. Sinon
            // la preview est rendue en GL immediate-mode par le SceneController.
            _meshRenderer = _instance.AddComponent<MeshRenderer>();
            _meshRenderer.enabled = false;
        }

        public void SetMesh(Mesh workingMesh)
        {
            if (_meshFilter != null)
                _meshFilter.sharedMesh = workingMesh;
        }

        /// <summary>
        /// Active/désactive l'affichage du mesh via un material réel (MeshRenderer).
        /// Quand actif, le SceneController n'effectue pas le rendu GL de la preview.
        /// </summary>
        public void SetMaterialPreview(bool enabled, Material material)
        {
            if (_meshRenderer == null)
                return;
            _meshRenderer.sharedMaterial = enabled ? material : null;
            _meshRenderer.enabled = enabled && material != null;
        }

        /// <summary>
        /// Vrai si le mesh est actuellement affiché via un material réel.
        /// </summary>
        public bool IsMaterialPreviewActive =>
            _meshRenderer != null && _meshRenderer.enabled;

        /// <summary>
        /// Détruit l'instance si elle existe.
        /// </summary>
        public void Destroy()
        {
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
                _instance = null;
                _meshFilter = null;
                _meshRenderer = null;
            }
        }
    }
}
