using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Graphe d'adjacence des vertices, soudé par position : les vertices
    /// coïncidents (dédoublés aux seams d'UV / de normales) sont fusionnés en un
    /// même "noeud". Le lissage (Smooth) traverse ainsi correctement les seams.
    /// </summary>
    public class VertexMeshTopology
    {
        int[] _weld;            // index vertex brut -> index de noeud soudé
        int[] _nodeRepRaw;      // noeud -> un vertex brut représentatif
        int[] _neighborStart;   // CSR : offset de début des voisins du noeud i
        int[] _neighborList;    // CSR : liste plate des noeuds voisins
        int _nodeCount;

        public bool IsReady => _weld != null && _neighborStart != null;

        public void Build(Vector3[] vertices, int[] triangles)
        {
            if (vertices == null || triangles == null)
            {
                _weld = null;
                _neighborStart = null;
                _neighborList = null;
                _nodeRepRaw = null;
                _nodeCount = 0;
                return;
            }

            int vertexCount = vertices.Length;
            _weld = new int[vertexCount];

            // Soudure par position (quantification au 1/10000 d'unité).
            var map = new Dictionary<(int, int, int), int>();
            _nodeCount = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 v = vertices[i];
                var key = (Mathf.RoundToInt(v.x * 10000f),
                           Mathf.RoundToInt(v.y * 10000f),
                           Mathf.RoundToInt(v.z * 10000f));
                if (!map.TryGetValue(key, out int id))
                {
                    id = _nodeCount++;
                    map[key] = id;
                }
                _weld[i] = id;
            }

            _nodeRepRaw = new int[_nodeCount];
            for (int i = 0; i < vertexCount; i++)
                _nodeRepRaw[_weld[i]] = i;

            // Adjacence soudée à partir des arêtes de triangles.
            var sets = new HashSet<int>[_nodeCount];
            for (int i = 0; i < _nodeCount; i++)
                sets[i] = new HashSet<int>();

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = _weld[triangles[t]];
                int b = _weld[triangles[t + 1]];
                int c = _weld[triangles[t + 2]];
                if (a != b) { sets[a].Add(b); sets[b].Add(a); }
                if (b != c) { sets[b].Add(c); sets[c].Add(b); }
                if (a != c) { sets[a].Add(c); sets[c].Add(a); }
            }

            // Conversion en structure CSR (compacte, sans allocation par requête).
            _neighborStart = new int[_nodeCount + 1];
            for (int i = 0; i < _nodeCount; i++)
                _neighborStart[i + 1] = _neighborStart[i] + sets[i].Count;

            _neighborList = new int[_neighborStart[_nodeCount]];
            for (int i = 0; i < _nodeCount; i++)
            {
                int offset = _neighborStart[i];
                foreach (int n in sets[i])
                    _neighborList[offset++] = n;
            }
        }

        /// <summary>
        /// Couleur moyenne des voisins d'un vertex, en espace soudé. Les couleurs
        /// sont lues dans sourceColors via le vertex représentatif de chaque noeud
        /// voisin. Renvoie la couleur d'origine du vertex si aucun voisin.
        /// </summary>
        public Color GetNeighborAverage(int rawVertex, Color[] sourceColors)
        {
            if (!IsReady || rawVertex < 0 || rawVertex >= _weld.Length)
                return sourceColors[rawVertex];

            int node = _weld[rawVertex];
            int start = _neighborStart[node];
            int end = _neighborStart[node + 1];
            if (end <= start)
                return sourceColors[rawVertex];

            float r = 0f, g = 0f, b = 0f, a = 0f;
            for (int k = start; k < end; k++)
            {
                Color c = sourceColors[_nodeRepRaw[_neighborList[k]]];
                r += c.r; g += c.g; b += c.b; a += c.a;
            }
            int count = end - start;
            return new Color(r / count, g / count, b / count, a / count);
        }
    }
}
