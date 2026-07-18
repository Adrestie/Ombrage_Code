using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>
    /// Engine-agnostic mesh container. Kept as a plain object (rather than a <see cref="Mesh"/>)
    /// so generation logic stays trivially unit-testable.
    /// </summary>
    public sealed class MeshData
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        public Vector3[] Normals;
        public Vector2[] Uvs;

        public int VertexCount => Vertices?.Length ?? 0;
        public int TriangleCount => (Triangles?.Length ?? 0) / 3;

        /// <summary>Recomputes smooth, area-weighted vertex normals from the triangle list.</summary>
        public void RecalculateNormals()
        {
            if (Vertices == null || Triangles == null)
                throw new InvalidOperationException("MeshData has no geometry to compute normals from.");

            var normals = new Vector3[Vertices.Length];
            for (int t = 0; t < Triangles.Length; t += 3)
            {
                int i0 = Triangles[t];
                int i1 = Triangles[t + 1];
                int i2 = Triangles[t + 2];

                // Cross-product magnitude is proportional to triangle area => area weighting.
                Vector3 faceNormal = Vector3.Cross(
                    Vertices[i1] - Vertices[i0],
                    Vertices[i2] - Vertices[i0]);

                normals[i0] += faceNormal;
                normals[i1] += faceNormal;
                normals[i2] += faceNormal;
            }

            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].sqrMagnitude > 1e-12f
                    ? normals[i].normalized
                    : Vector3.up;
            }

            Normals = normals;
        }

        /// <summary>
        /// Builds a <see cref="Mesh"/> from this data. Tangents are always recomputed because
        /// HDRP normal mapping requires them.
        /// </summary>
        public Mesh ToMesh(string meshName)
        {
            if (Vertices == null || Triangles == null)
                throw new InvalidOperationException("MeshData must have vertices and triangles before conversion.");

            var mesh = new Mesh
            {
                name = string.IsNullOrEmpty(meshName) ? "GeneratedMesh" : meshName
            };

            if (Vertices.Length > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(Vertices);
            mesh.SetTriangles(Triangles, 0);

            if (Uvs != null && Uvs.Length == Vertices.Length)
                mesh.SetUVs(0, Uvs);

            if (Normals != null && Normals.Length == Vertices.Length)
                mesh.SetNormals(Normals);
            else
                mesh.RecalculateNormals();

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
