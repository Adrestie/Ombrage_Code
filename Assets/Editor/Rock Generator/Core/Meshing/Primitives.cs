using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>Builds a subdivided icosphere centered on the origin.</summary>
    public static class IcosphereBuilder
    {
        public static MeshData Build(int subdivisions, float radius)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 6);
            radius = Mathf.Max(0.0001f, radius);

            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var positions = new List<Vector3>
            {
                new Vector3(-1f,  t, 0f), new Vector3( 1f,  t, 0f),
                new Vector3(-1f, -t, 0f), new Vector3( 1f, -t, 0f),
                new Vector3(0f, -1f,  t), new Vector3(0f,  1f,  t),
                new Vector3(0f, -1f, -t), new Vector3(0f,  1f, -t),
                new Vector3( t, 0f, -1f), new Vector3( t, 0f,  1f),
                new Vector3(-t, 0f, -1f), new Vector3(-t, 0f,  1f),
            };
            for (int i = 0; i < positions.Count; i++)
                positions[i] = positions[i].normalized;

            var indices = new List<int>
            {
                0, 11, 5,  0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
                1, 5, 9,   5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
                3, 9, 4,   3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
                4, 9, 5,   2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1
            };

            var midpointCache = new Dictionary<long, int>();
            for (int s = 0; s < subdivisions; s++)
            {
                var refined = new List<int>(indices.Count * 4);
                for (int f = 0; f < indices.Count; f += 3)
                {
                    int a = indices[f];
                    int b = indices[f + 1];
                    int c = indices[f + 2];
                    int ab = Midpoint(a, b, positions, midpointCache);
                    int bc = Midpoint(b, c, positions, midpointCache);
                    int ca = Midpoint(c, a, positions, midpointCache);

                    refined.Add(a);  refined.Add(ab); refined.Add(ca);
                    refined.Add(b);  refined.Add(bc); refined.Add(ab);
                    refined.Add(c);  refined.Add(ca); refined.Add(bc);
                    refined.Add(ab); refined.Add(bc); refined.Add(ca);
                }
                indices = refined;
            }

            var vertices = new Vector3[positions.Count];
            var normals = new Vector3[positions.Count];
            var uvs = new Vector2[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 n = positions[i].normalized;
                normals[i] = n;
                vertices[i] = n * radius;
                // Equirectangular UVs. A seam exists where the azimuth wraps; acceptable for
                // procedural rock, which is typically shaded with triplanar materials anyway.
                uvs[i] = new Vector2(
                    0.5f + Mathf.Atan2(n.z, n.x) / (2f * Mathf.PI),
                    0.5f + Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) / Mathf.PI);
            }

            return new MeshData
            {
                Vertices = vertices,
                Triangles = indices.ToArray(),
                Normals = normals,
                Uvs = uvs
            };
        }

        static int Midpoint(int a, int b, List<Vector3> positions, Dictionary<long, int> cache)
        {
            long key = a < b
                ? ((long)a << 32) | (uint)b
                : ((long)b << 32) | (uint)a;

            if (cache.TryGetValue(key, out int existing))
                return existing;

            Vector3 mid = ((positions[a] + positions[b]) * 0.5f).normalized;
            int index = positions.Count;
            positions.Add(mid);
            cache[key] = index;
            return index;
        }
    }

    /// <summary>Builds a subdivided, watertight box centered on the origin (welded vertices).</summary>
    public static class BoxBuilder
    {
        public static MeshData Build(Vector3 size, Vector3Int resolution)
        {
            size = new Vector3(
                Mathf.Max(0.0001f, size.x),
                Mathf.Max(0.0001f, size.y),
                Mathf.Max(0.0001f, size.z));
            int rx = Mathf.Clamp(resolution.x, 1, 128);
            int ry = Mathf.Clamp(resolution.y, 1, 128);
            int rz = Mathf.Clamp(resolution.z, 1, 128);
            Vector3 h = size * 0.5f;

            var positions = new List<Vector3>();
            var weld = new Dictionary<Vector3Int, int>();
            var triangles = new List<int>();

            int Weld(Vector3 p)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(p.x * 1000f),
                    Mathf.RoundToInt(p.y * 1000f),
                    Mathf.RoundToInt(p.z * 1000f));
                if (weld.TryGetValue(key, out int existing))
                    return existing;
                int index = positions.Count;
                positions.Add(p);
                weld[key] = index;
                return index;
            }

            void Face(Vector3 origin, Vector3 uDir, Vector3 vDir, int uRes, int vRes)
            {
                for (int v = 0; v < vRes; v++)
                for (int u = 0; u < uRes; u++)
                {
                    Vector3 p00 = origin + uDir * (u / (float)uRes)       + vDir * (v / (float)vRes);
                    Vector3 p10 = origin + uDir * ((u + 1) / (float)uRes) + vDir * (v / (float)vRes);
                    Vector3 p11 = origin + uDir * ((u + 1) / (float)uRes) + vDir * ((v + 1) / (float)vRes);
                    Vector3 p01 = origin + uDir * (u / (float)uRes)       + vDir * ((v + 1) / (float)vRes);

                    int i00 = Weld(p00), i10 = Weld(p10), i11 = Weld(p11), i01 = Weld(p01);
                    triangles.Add(i00); triangles.Add(i10); triangles.Add(i11);
                    triangles.Add(i00); triangles.Add(i11); triangles.Add(i01);
                }
            }

            Face(new Vector3( h.x, -h.y, -h.z), new Vector3(0f, 0f, size.z), new Vector3(0f, size.y, 0f), rz, ry);
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(0f, 0f, size.z), new Vector3(0f, size.y, 0f), rz, ry);
            Face(new Vector3(-h.x,  h.y, -h.z), new Vector3(size.x, 0f, 0f), new Vector3(0f, 0f, size.z), rx, rz);
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(size.x, 0f, 0f), new Vector3(0f, 0f, size.z), rx, rz);
            Face(new Vector3(-h.x, -h.y,  h.z), new Vector3(size.x, 0f, 0f), new Vector3(0f, size.y, 0f), rx, ry);
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(size.x, 0f, 0f), new Vector3(0f, size.y, 0f), rx, ry);

            var vertices = positions.ToArray();
            var indices = triangles.ToArray();

            // The base box is convex and centered on the origin: flip any inward-facing triangle
            // so winding is outward regardless of how each face grid was emitted.
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 v0 = vertices[indices[i]];
                Vector3 v1 = vertices[indices[i + 1]];
                Vector3 v2 = vertices[indices[i + 2]];
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
                Vector3 centroid = (v0 + v1 + v2) / 3f;
                if (Vector3.Dot(faceNormal, centroid) < 0f)
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            }

            // Box-projected UVs based on each vertex's dominant axis.
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y), az = Mathf.Abs(p.z);
                if (ax >= ay && ax >= az)
                    uvs[i] = new Vector2((p.z + h.z) / size.z, (p.y + h.y) / size.y);
                else if (ay >= ax && ay >= az)
                    uvs[i] = new Vector2((p.x + h.x) / size.x, (p.z + h.z) / size.z);
                else
                    uvs[i] = new Vector2((p.x + h.x) / size.x, (p.y + h.y) / size.y);
            }

            return new MeshData
            {
                Vertices = vertices,
                Triangles = indices,
                Uvs = uvs,
                Normals = null
            };
        }
    }
}
