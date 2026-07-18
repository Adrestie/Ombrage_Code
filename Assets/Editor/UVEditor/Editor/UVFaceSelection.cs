using System.Collections.Generic;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Sélection persistante de faces (ensemble d'indices de triangles), propre à
    /// un mesh. Définit la portée d'une projection en mode "Selected Faces" :
    /// seules les faces sélectionnées sont reprojetées.
    ///
    /// La logique d'édition (clic, rectangle, modificateurs) est reprise du
    /// Vertex Color Editor pour offrir une expérience cohérente entre les outils.
    /// </summary>
    public class UVFaceSelection
    {
        readonly HashSet<int> _selected = new HashSet<int>();
        int _triangleCount;

        public int Count => _selected.Count;
        public bool IsEmpty => _selected.Count == 0;
        public int TriangleCount => _triangleCount;
        public IEnumerable<int> Indices => _selected;

        /// <summary>
        /// Réinitialise la sélection pour un mesh donné (les indices de triangles
        /// sont spécifiques à un mesh).
        /// </summary>
        public void SetTriangleCount(int count)
        {
            _triangleCount = count;
            _selected.Clear();
        }

        public bool Contains(int triangleIndex) => _selected.Contains(triangleIndex);

        public void Add(int triangleIndex)
        {
            if (triangleIndex >= 0 && triangleIndex < _triangleCount)
                _selected.Add(triangleIndex);
        }

        public void Remove(int triangleIndex) => _selected.Remove(triangleIndex);

        public void Clear() => _selected.Clear();

        public void SelectAll()
        {
            _selected.Clear();
            for (int i = 0; i < _triangleCount; i++)
                _selected.Add(i);
        }

        public void Invert()
        {
            var inverted = new HashSet<int>();
            for (int i = 0; i < _triangleCount; i++)
            {
                if (!_selected.Contains(i))
                    inverted.Add(i);
            }
            _selected.Clear();
            _selected.UnionWith(inverted);
        }

        /// <summary>
        /// Construit le masque par triangle (longueur = nombre de triangles).
        /// Renvoie null si la sélection est vide (aucun masque = portée libre).
        /// </summary>
        public bool[] BuildTriangleMask()
        {
            if (_selected.Count == 0 || _triangleCount == 0)
                return null;

            var mask = new bool[_triangleCount];
            foreach (int t in _selected)
            {
                if (t >= 0 && t < _triangleCount)
                    mask[t] = true;
            }
            return mask;
        }
    }
}
