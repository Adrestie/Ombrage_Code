using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>
    /// Builds the 3D convex hull of a point set using the incremental insertion algorithm.
    /// Robust enough for the near-spherical point clouds the ConvexHull strategy feeds it.
    /// Face winding is forced outward via a fixed interior reference point, so the result
    /// is always consistently oriented regardless of insertion order.
    /// </summary>
    public static class ConvexHullBuilder
    {
        const float Epsilon = 1e-5f;

        struct Face
        {
            public int A, B, C;
            public Vector3 Normal;   // outward-facing
        }

        public static MeshData Build(IList<Vector3> points)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (points.Count < 4)
                throw new ArgumentException("A convex hull needs at least 4 points.", nameof(points));

            // Interior reference point: the centroid is a convex combination of the inputs,
            // hence strictly inside the hull (unless the points are coplanar).
            Vector3 interior = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
                interior += points[i];
            interior /= points.Count;

            var faces = new List<Face>();
            if (!BuildInitialTetrahedron(points, interior, faces))
                throw new InvalidOperationException("Convex hull points are degenerate (coplanar).");

            for (int p = 0; p < points.Count; p++)
            {
                Vector3 pos = points[p];

                // Faces visible from this point.
                var visible = new List<int>();
                for (int f = 0; f < faces.Count; f++)
                {
                    Face face = faces[f];
                    if (Vector3.Dot(face.Normal, pos - points[face.A]) > Epsilon)
                        visible.Add(f);
                }
                if (visible.Count == 0)
                    continue; // point lies inside (or on) the current hull

                // Horizon edges = edges shared by exactly one visible face.
                var edgeCount = new Dictionary<long, int>();
                var edgeEnds = new Dictionary<long, (int a, int b)>();
                foreach (int f in visible)
                {
                    Face face = faces[f];
                    AddEdge(face.A, face.B, edgeCount, edgeEnds);
                    AddEdge(face.B, face.C, edgeCount, edgeEnds);
                    AddEdge(face.C, face.A, edgeCount, edgeEnds);
                }

                // Remove visible faces (descending order keeps indices valid).
                visible.Sort();
                for (int i = visible.Count - 1; i >= 0; i--)
                    faces.RemoveAt(visible[i]);

                // Stitch the horizon to the new point.
                foreach (var kv in edgeCount)
                {
                    if (kv.Value != 1)
                        continue; // interior edge of the visible region
                    (int a, int b) = edgeEnds[kv.Key];
                    faces.Add(MakeFace(a, b, p, points, interior));
                }
            }

            return ToMeshData(faces, points);
        }

        static bool BuildInitialTetrahedron(IList<Vector3> points, Vector3 interior, List<Face> faces)
        {
            int count = points.Count;
            int i0 = 0;

            int i1 = -1;
            for (int i = 1; i < count; i++)
                if (Vector3.Distance(points[i], points[i0]) > Epsilon) { i1 = i; break; }
            if (i1 < 0) return false;

            int i2 = -1;
            for (int i = 0; i < count; i++)
            {
                if (i == i0 || i == i1) continue;
                Vector3 cross = Vector3.Cross(points[i1] - points[i0], points[i] - points[i0]);
                if (cross.sqrMagnitude > Epsilon * Epsilon) { i2 = i; break; }
            }
            if (i2 < 0) return false;

            Vector3 planeNormal = Vector3.Cross(points[i1] - points[i0], points[i2] - points[i0]).normalized;
            int i3 = -1;
            for (int i = 0; i < count; i++)
            {
                if (i == i0 || i == i1 || i == i2) continue;
                if (Mathf.Abs(Vector3.Dot(planeNormal, points[i] - points[i0])) > Epsilon) { i3 = i; break; }
            }
            if (i3 < 0) return false;

            faces.Add(MakeFace(i0, i1, i2, points, interior));
            faces.Add(MakeFace(i0, i1, i3, points, interior));
            faces.Add(MakeFace(i0, i2, i3, points, interior));
            faces.Add(MakeFace(i1, i2, i3, points, interior));
            return true;
        }

        static Face MakeFace(int a, int b, int c, IList<Vector3> points, Vector3 interior)
        {
            Vector3 va = points[a], vb = points[b], vc = points[c];
            Vector3 normal = Vector3.Cross(vb - va, vc - va).normalized;
            Vector3 centroid = (va + vb + vc) / 3f;

            // Flip winding if the normal points toward the hull interior.
            if (Vector3.Dot(normal, centroid - interior) < 0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }
            return new Face { A = a, B = b, C = c, Normal = normal };
        }

        static void AddEdge(int u, int v, Dictionary<long, int> count,
            Dictionary<long, (int, int)> ends)
        {
            long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
            count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
            ends[key] = (u, v);
        }

        static MeshData ToMeshData(List<Face> faces, IList<Vector3> points)
        {
            var remap = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var triangles = new List<int>(faces.Count * 3);

            int Map(int original)
            {
                if (remap.TryGetValue(original, out int mapped))
                    return mapped;
                mapped = vertices.Count;
                vertices.Add(points[original]);
                remap[original] = mapped;
                return mapped;
            }

            foreach (Face face in faces)
            {
                triangles.Add(Map(face.A));
                triangles.Add(Map(face.B));
                triangles.Add(Map(face.C));
            }

            return new MeshData
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles.ToArray(),
            };
        }
    }
}
