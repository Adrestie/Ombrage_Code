using System.Collections.Generic;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>Granularité d'édition de la sélection (réglage global).</summary>
    public enum UVSelectionMode
    {
        Faces = 0,
        Vertices = 1
    }

    /// <summary>
    /// Sélection unifiée de l'UV Editor, propre à un mesh de travail.
    ///
    /// Vérité unique : un ensemble d'indices de VERTICES du mesh de travail.
    /// Une face (triangle) est considérée sélectionnée si ses trois vertices le
    /// sont. Tout se ramène donc aux vertices — ce qui rend la synchronisation
    /// 2D/3D triviale (les deux vues lisent le même ensemble) et prépare les
    /// transformations de la sous-étape 2.4.
    ///
    /// Les indices référencent le mesh APRÈS reconstruction (post-couture) : un
    /// point « soudé » en 3D correspond à plusieurs vertices ici, un par
    /// couture. La résolution des doublons (3D -> tous, 2D -> un seul) est faite
    /// par l'appelant, pas ici.
    /// </summary>
    public class UVSelection
    {
        readonly HashSet<int> _vertices = new HashSet<int>();
        int[] _triangles;          // copie des indices du mesh de travail
        int _vertexCount;

        public int VertexCount => _vertexCount;
        public int TriangleCount => _triangles != null ? _triangles.Length / 3 : 0;
        public int SelectedVertexCount => _vertices.Count;
        public bool IsEmpty => _vertices.Count == 0;
        public IEnumerable<int> SelectedVertices => _vertices;

        /// <summary>
        /// (Ré)initialise la sélection pour un mesh donné. La topologie (indices
        /// de triangles) est nécessaire pour les helpers face.
        /// </summary>
        public void SetMesh(int vertexCount, int[] triangles)
        {
            _vertexCount = vertexCount;
            _triangles = triangles;
            _vertices.Clear();
        }

        /// <summary>
        /// Met à jour la topologie sans toucher à la sélection si le nombre de
        /// vertices est inchangé ; sinon réinitialise (la sélection deviendrait
        /// incohérente).
        /// </summary>
        public void RefreshTopology(int vertexCount, int[] triangles)
        {
            if (vertexCount != _vertexCount)
            {
                _vertexCount = vertexCount;
                _triangles = triangles;
                _vertices.Clear();
                return;
            }
            _triangles = triangles;
        }

        // --- Édition par vertex ---

        public bool ContainsVertex(int v) => _vertices.Contains(v);

        public void AddVertex(int v)
        {
            if (v >= 0 && v < _vertexCount)
                _vertices.Add(v);
        }

        public void RemoveVertex(int v) => _vertices.Remove(v);

        public void Clear() => _vertices.Clear();

        public void SelectAllVertices()
        {
            _vertices.Clear();
            for (int i = 0; i < _vertexCount; i++)
                _vertices.Add(i);
        }

        public void InvertVertices()
        {
            var inv = new HashSet<int>();
            for (int i = 0; i < _vertexCount; i++)
            {
                if (!_vertices.Contains(i))
                    inv.Add(i);
            }
            _vertices.Clear();
            _vertices.UnionWith(inv);
        }

        /// <summary>Remplace la sélection par l'ensemble fourni.</summary>
        public void SetVertices(IEnumerable<int> vertices)
        {
            _vertices.Clear();
            if (vertices == null)
                return;
            foreach (int v in vertices)
            {
                if (v >= 0 && v < _vertexCount)
                    _vertices.Add(v);
            }
        }

        // --- Helpers face (une face = 3 vertices) ---

        /// <summary>Indices des 3 vertices d'un triangle.</summary>
        public void GetTriangle(int triangleIndex, out int v0, out int v1, out int v2)
        {
            int t = triangleIndex * 3;
            v0 = _triangles[t];
            v1 = _triangles[t + 1];
            v2 = _triangles[t + 2];
        }

        /// <summary>Vrai si les 3 vertices du triangle sont sélectionnés.</summary>
        public bool IsTriangleSelected(int triangleIndex)
        {
            if (_triangles == null)
                return false;
            int t = triangleIndex * 3;
            if (t + 2 >= _triangles.Length)
                return false;
            return _vertices.Contains(_triangles[t]) &&
                   _vertices.Contains(_triangles[t + 1]) &&
                   _vertices.Contains(_triangles[t + 2]);
        }

        /// <summary>Sélectionne les 3 vertices d'un triangle.</summary>
        public void AddTriangle(int triangleIndex)
        {
            if (_triangles == null)
                return;
            int t = triangleIndex * 3;
            if (t + 2 >= _triangles.Length)
                return;
            _vertices.Add(_triangles[t]);
            _vertices.Add(_triangles[t + 1]);
            _vertices.Add(_triangles[t + 2]);
        }

        /// <summary>Désélectionne les 3 vertices d'un triangle.</summary>
        public void RemoveTriangle(int triangleIndex)
        {
            if (_triangles == null)
                return;
            int t = triangleIndex * 3;
            if (t + 2 >= _triangles.Length)
                return;
            _vertices.Remove(_triangles[t]);
            _vertices.Remove(_triangles[t + 1]);
            _vertices.Remove(_triangles[t + 2]);
        }

        public void SelectAllTriangles() => SelectAllVertices();

        /// <summary>Nombre de faces entièrement sélectionnées.</summary>
        public int CountSelectedTriangles()
        {
            if (_triangles == null)
                return 0;
            int count = 0;
            int triCount = _triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                if (IsTriangleSelected(i))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Construit le masque de triangles pour une projection « faces
        /// sélectionnées » : un triangle est masqué si ses 3 vertices sont
        /// sélectionnés. Renvoie null si aucun triangle n'est entièrement
        /// sélectionné (aucune face à projeter).
        /// </summary>
        public bool[] BuildTriangleMask()
        {
            if (_triangles == null || _vertices.Count == 0)
                return null;

            int triCount = _triangles.Length / 3;
            var mask = new bool[triCount];
            bool any = false;
            for (int i = 0; i < triCount; i++)
            {
                if (IsTriangleSelected(i))
                {
                    mask[i] = true;
                    any = true;
                }
            }
            return any ? mask : null;
        }
    }
}
