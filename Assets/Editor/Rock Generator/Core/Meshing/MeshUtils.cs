using UnityEngine;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>Shared mesh helpers used by the generation strategies.</summary>
    public static class MeshUtils
    {
        /// <summary>
        /// Computes triplanar-style box-projected UVs: each vertex is projected onto the
        /// plane perpendicular to the dominant axis of its normal. World-scaled, which
        /// suits procedural rock surfaces (typically shaded with triplanar materials).
        /// </summary>
        public static Vector2[] BoxProjectUvs(Vector3[] vertices, Vector3[] normals)
        {
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                Vector3 n = (normals != null && normals.Length == vertices.Length)
                    ? normals[i]
                    : p;

                float ax = Mathf.Abs(n.x);
                float ay = Mathf.Abs(n.y);
                float az = Mathf.Abs(n.z);

                if (ax >= ay && ax >= az)
                    uvs[i] = new Vector2(p.z, p.y);
                else if (ay >= ax && ay >= az)
                    uvs[i] = new Vector2(p.x, p.z);
                else
                    uvs[i] = new Vector2(p.x, p.y);
            }
            return uvs;
        }
    }
}
