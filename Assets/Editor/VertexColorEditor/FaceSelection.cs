using System.Collections.Generic;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Sélection persistante de faces (ensemble d'indices de triangles), propre à un
    /// mesh. Sert de masque à la peinture : convertie en masque de vertices via
    /// l'ensemble des vertices appartenant aux faces sélectionnées.
    /// </summary>
    public class FaceSelection
    {
        readonly HashSet<int> _selected = new HashSet<int>();
        int _triangleCount;

        public int Count => _selected.Count;
        public bool IsEmpty => _selected.Count == 0;
        public int TriangleCount => _triangleCount;
        public IEnumerable<int> Indices => _selected;

        /// <summary>
        /// Réinitialise la sélection pour un mesh donné.
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
        /// Construit le masque de vertices correspondant : tout vertex appartenant
        /// à au moins une face sélectionnée est marqué true. Renvoie null si vide.
        /// </summary>
        public bool[] BuildVertexMask(int[] triangles, int vertexCount)
        {
            if (_selected.Count == 0 || triangles == null)
                return null;

            bool[] mask = new bool[vertexCount];
            foreach (int tri in _selected)
            {
                int t = tri * 3;
                if (t + 2 >= triangles.Length)
                    continue;
                mask[triangles[t]] = true;
                mask[triangles[t + 1]] = true;
                mask[triangles[t + 2]] = true;
            }
            return mask;
        }
    }
}
