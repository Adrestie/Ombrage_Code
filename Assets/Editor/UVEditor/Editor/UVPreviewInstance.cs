using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Gère le GameObject temporaire instancié dans la SceneView pour visualiser
    /// le mesh en cours d'édition.
    ///
    /// Marqué <see cref="HideFlags.DontSave"/> : il n'est jamais enregistré dans
    /// la scène et doit être détruit explicitement à la fermeture du tool, au
    /// rechargement des scripts et à l'entrée en Play Mode.
    ///
    /// Le MeshRenderer est désactivé par défaut : la preview "checker UV" est
    /// rendue en GL immediate-mode par le <see cref="UVEditorSceneController"/>.
    /// Il n'est activé que pour le mode "Show Material" (rendu via un material
    /// réel et le pipeline).
    /// </summary>
    public class UVPreviewInstance
    {
        const string InstanceName = "[UV Editor] Preview (temp)";

        const HideFlags TransientFlags = HideFlags.DontSave;

        GameObject _instance;
        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;

        public bool Exists => _instance != null;

        public Transform Transform => _instance != null ? _instance.transform : null;

        public Matrix4x4 LocalToWorld =>
            _instance != null ? _instance.transform.localToWorldMatrix : Matrix4x4.identity;

        /// <summary>Crée (ou recrée) l'instance de preview à l'origine du monde.</summary>
        public void Create(Mesh workingMesh)
        {
            Destroy();
            // Filet de sécurité : détruit toute instance orpheline restée dans
            // la scène (ex. _preview remis à null par un reload de scripts alors
            // que le GameObject DontSave a survécu).
            DestroyAllOrphans();

            _instance = new GameObject(InstanceName) { hideFlags = TransientFlags };
            _instance.transform.position = Vector3.zero;
            _instance.transform.rotation = Quaternion.identity;
            _instance.transform.localScale = Vector3.one;

            _meshFilter = _instance.AddComponent<MeshFilter>();
            _meshFilter.sharedMesh = workingMesh;

            _meshRenderer = _instance.AddComponent<MeshRenderer>();
            _meshRenderer.enabled = false;
        }

        /// <summary>
        /// Détruit toutes les instances de preview présentes dans la scène,
        /// y compris celles dont la référence C# a été perdue (orphelins).
        /// </summary>
        public static void DestroyAllOrphans()
        {
            // Inclut les objets HideFlags.DontSave, absents de la hiérarchie
            // standard mais bien présents en mémoire.
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go == null || go.name != InstanceName)
                    continue;
                // Ignore les prefabs / assets : on ne détruit que des objets de scène.
                if (EditorUtility.IsPersistent(go))
                    continue;
                Object.DestroyImmediate(go);
            }
        }

        public void SetMesh(Mesh workingMesh)
        {
            if (_meshFilter != null)
                _meshFilter.sharedMesh = workingMesh;
        }

        /// <summary>
        /// Active/désactive l'affichage via un material réel (MeshRenderer).
        /// Quand actif, le SceneController n'effectue pas le rendu GL du checker.
        /// </summary>
        public void SetMaterialPreview(bool enabled, Material material)
        {
            if (_meshRenderer == null)
                return;
            _meshRenderer.sharedMaterial = enabled ? material : null;
            _meshRenderer.enabled = enabled && material != null;
        }

        /// <summary>Vrai si le mesh est actuellement affiché via un material réel.</summary>
        public bool IsMaterialPreviewActive =>
            _meshRenderer != null && _meshRenderer.enabled;

        /// <summary>Détruit l'instance si elle existe.</summary>
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
