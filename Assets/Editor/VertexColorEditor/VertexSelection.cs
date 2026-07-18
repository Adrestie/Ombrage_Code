using System.Collections.Generic;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Sélection persistante de vertices (ensemble d'indices), propre à un mesh.
    /// Éditée via le mode Selected Vertices ; consommée comme masque par les modes
    /// Brush et Face, et comme cible par Fill / Gradient / Smooth (Phase 3).
    /// </summary>
    public class VertexSelection
    {
        readonly HashSet<int> _selected = new HashSet<int>();
        int _vertexCount;

        public int Count => _selected.Count;
        public bool IsEmpty => _selected.Count == 0;
        public int VertexCount => _vertexCount;
        public IEnumerable<int> Indices => _selected;

        /// <summary>
        /// Réinitialise la sélection pour un mesh donné (les indices sont
        /// spécifiques à un mesh).
        /// </summary>
        public void SetVertexCount(int count)
        {
            _vertexCount = count;
            _selected.Clear();
        }

        public bool Contains(int index) => _selected.Contains(index);

        public void Add(int index)
        {
            if (index >= 0 && index < _vertexCount)
                _selected.Add(index);
        }

        public void Remove(int index) => _selected.Remove(index);

        public void Clear() => _selected.Clear();

        public void SelectAll()
        {
            _selected.Clear();
            for (int i = 0; i < _vertexCount; i++)
                _selected.Add(i);
        }

        public void Invert()
        {
            var inverted = new HashSet<int>();
            for (int i = 0; i < _vertexCount; i++)
            {
                if (!_selected.Contains(i))
                    inverted.Add(i);
            }
            _selected.Clear();
            _selected.UnionWith(inverted);
        }

        /// <summary>
        /// Construit le masque de vertices correspondant. Renvoie null si vide
        /// (aucun masque = peinture libre).
        /// </summary>
        public bool[] BuildVertexMask()
        {
            if (_selected.Count == 0)
                return null;

            bool[] mask = new bool[_vertexCount];
            foreach (int i in _selected)
            {
                if (i >= 0 && i < _vertexCount)
                    mask[i] = true;
            }
            return mask;
        }
    }
}
