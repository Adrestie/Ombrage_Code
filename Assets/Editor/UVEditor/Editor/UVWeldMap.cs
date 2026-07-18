using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Regroupe les vertices du mesh de travail qui occupent la même position.
    ///
    /// Les projections (box, et plus généralement toute couture) dédoublent un
    /// vertex en plusieurs copies de même position 3D mais d'UV différentes.
    /// Cette carte permet, à partir d'un vertex, de retrouver tous ses
    /// « doublons » — utile pour la sélection en 3D : viser un point soudé doit
    /// sélectionner tous ses vertices, puisqu'ils sont indistinguables dans
    /// l'espace 3D. Le viewport 2D, lui, manipule chaque doublon séparément et
    /// n'utilise pas cette carte.
    /// </summary>
    public class UVWeldMap
    {
        // groupId par vertex.
        int[] _groupOf;
        // Pour chaque groupe, la liste des vertices qui le composent.
        readonly List<List<int>> _groups = new List<List<int>>();

        public bool IsBuilt => _groupOf != null;

        /// <summary>(Re)construit la carte à partir des positions des vertices.</summary>
        public void Build(Vector3[] positions)
        {
            _groups.Clear();

            if (positions == null || positions.Length == 0)
            {
                _groupOf = null;
                return;
            }

            _groupOf = new int[positions.Length];

            // Quantification des positions pour regrouper les coïncidences à
            // une tolérance près (les doublons de couture ont des positions
            // strictement égales, mais on reste robuste aux imprécisions).
            var byKey = new Dictionary<long, int>(positions.Length);

            for (int i = 0; i < positions.Length; i++)
            {
                long key = QuantizeKey(positions[i]);
                if (!byKey.TryGetValue(key, out int groupId))
                {
                    groupId = _groups.Count;
                    byKey.Add(key, groupId);
                    _groups.Add(new List<int>(2));
                }
                _groupOf[i] = groupId;
                _groups[groupId].Add(i);
            }
        }

        /// <summary>
        /// Tous les vertices partageant la position du vertex donné (lui inclus).
        /// Retourne une liste à un seul élément si le vertex n'a pas de doublon.
        /// </summary>
        public List<int> GetCoincident(int vertex)
        {
            if (_groupOf == null || vertex < 0 || vertex >= _groupOf.Length)
                return null;
            return _groups[_groupOf[vertex]];
        }

        // Position -> clé entière. Quantum volontairement fin : on veut séparer
        // des vertices réellement distincts et ne fusionner que les coïncidents.
        static long QuantizeKey(Vector3 p)
        {
            const float Quantum = 100000f;
            long x = (long)Mathf.Round(p.x * Quantum);
            long y = (long)Mathf.Round(p.y * Quantum);
            long z = (long)Mathf.Round(p.z * Quantum);
            // Mélange simple des trois composantes.
            unchecked
            {
                long h = 1469598103934665603L;
                h = (h ^ x) * 1099511628211L;
                h = (h ^ y) * 1099511628211L;
                h = (h ^ z) * 1099511628211L;
                return h;
            }
        }
    }
}
