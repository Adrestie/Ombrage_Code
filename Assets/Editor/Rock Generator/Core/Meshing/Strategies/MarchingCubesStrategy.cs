using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Ombrage.Tools.Core.Noise;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Meshing.Strategies
{
    /// <summary>
    /// Generates a rock by extracting an iso-surface from a voxel density field. Density
    /// sampling runs as a Burst job (<see cref="DensitySamplingJob"/>); the surface is then
    /// triangulated on the managed side using Marching Tetrahedra (each cube is split into
    /// six tetrahedra). The tetrahedral variant is preferred over classic Marching Cubes
    /// because it has no ambiguous cases and is always watertight, at the cost of a few more
    /// triangles. Every emitted triangle is oriented outward from the "inside" corners, so
    /// winding is correct regardless of the case.
    /// </summary>
    public sealed class MarchingCubesStrategy : IMeshGenerationStrategy
    {
        public GenerationAlgorithm Algorithm => GenerationAlgorithm.MarchingCubes;

        // Cube corner offsets, indexed 0..7.
        static readonly int[,] CornerOffset =
        {
            { 0, 0, 0 }, { 1, 0, 0 }, { 1, 1, 0 }, { 0, 1, 0 },
            { 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 }
        };

        // Six tetrahedra sharing the cube's 0-6 main diagonal.
        static readonly int[,] Tetrahedra =
        {
            { 0, 1, 2, 6 }, { 0, 2, 3, 6 }, { 0, 3, 7, 6 },
            { 0, 7, 4, 6 }, { 0, 4, 5, 6 }, { 0, 5, 1, 6 }
        };

        // Per-generation scratch + output. The strategy is single-use per Generate call.
        readonly Vector3[] _p = new Vector3[4];
        readonly float[] _v = new float[4];
        List<Vector3> _vertices;
        List<int> _triangles;
        Dictionary<Vector3Int, int> _weld;
        float _iso;

        public MeshData Generate(RockGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            MarchingCubesParameters parameters = settings.marchingCubes
                ?? throw new ArgumentException("marchingCubes parameters are missing.", nameof(settings));

            int rx = Mathf.Clamp(parameters.gridResolution.x, 2, 72);
            int ry = Mathf.Clamp(parameters.gridResolution.y, 2, 72);
            int rz = Mathf.Clamp(parameters.gridResolution.z, 2, 72);

            Vector3 bounds = new Vector3(
                Mathf.Max(0.01f, parameters.bounds.x),
                Mathf.Max(0.01f, parameters.bounds.y),
                Mathf.Max(0.01f, parameters.bounds.z));

            int gx = rx + 1, gy = ry + 1, gz = rz + 1;
            var cellSize = new float3(bounds.x / rx, bounds.y / ry, bounds.z / rz);
            var boundsMin = new float3(-bounds.x * 0.5f, -bounds.y * 0.5f, -bounds.z * 0.5f);
            var halfExtent = new float3(bounds.x * 0.5f, bounds.y * 0.5f, bounds.z * 0.5f);

            float[] density = SampleDensity(parameters, settings.seed,
                gx, gy, gz, boundsMin, cellSize, halfExtent);

            _iso = Mathf.Clamp01(parameters.isoLevel);
            _vertices = new List<Vector3>();
            _triangles = new List<int>();
            _weld = new Dictionary<Vector3Int, int>();

            var cubePos = new Vector3[8];
            var cubeVal = new float[8];

            for (int cz = 0; cz < rz; cz++)
            for (int cy = 0; cy < ry; cy++)
            for (int cx = 0; cx < rx; cx++)
            {
                for (int k = 0; k < 8; k++)
                {
                    int x = cx + CornerOffset[k, 0];
                    int y = cy + CornerOffset[k, 1];
                    int z = cz + CornerOffset[k, 2];
                    cubePos[k] = (Vector3)(boundsMin + new float3(x, y, z) * cellSize);
                    cubeVal[k] = density[x + y * gx + z * gx * gy];
                }

                for (int t = 0; t < 6; t++)
                {
                    MarchTetrahedron(
                        cubePos[Tetrahedra[t, 0]], cubePos[Tetrahedra[t, 1]],
                        cubePos[Tetrahedra[t, 2]], cubePos[Tetrahedra[t, 3]],
                        cubeVal[Tetrahedra[t, 0]], cubeVal[Tetrahedra[t, 1]],
                        cubeVal[Tetrahedra[t, 2]], cubeVal[Tetrahedra[t, 3]]);
                }
            }

            if (_triangles.Count == 0)
            {
                throw new InvalidOperationException(
                    "Marching Cubes produced an empty mesh. Lower the iso level or raise the "
                    + "noise amplitude so the density field crosses the threshold.");
            }

            var result = new MeshData
            {
                Vertices = _vertices.ToArray(),
                Triangles = _triangles.ToArray(),
            };
            result.RecalculateNormals();
            result.Uvs = MeshUtils.BoxProjectUvs(result.Vertices, result.Normals);
            return result;
        }

        static float[] SampleDensity(MarchingCubesParameters parameters, int seed,
            int gx, int gy, int gz, float3 boundsMin, float3 cellSize, float3 halfExtent)
        {
            int total = gx * gy * gz;
            var density = new NativeArray<float>(total, Allocator.TempJob);
            try
            {
                var job = new DensitySamplingJob
                {
                    GridPoints = new int3(gx, gy, gz),
                    BoundsMin = boundsMin,
                    CellSize = cellSize,
                    BoundsCenter = boundsMin + halfExtent,
                    HalfExtent = math.max(halfExtent, new float3(1e-4f)),
                    NoiseConfig = FractalNoiseConfig.FromSettings(parameters.noise, seed),
                    NoiseAmplitude = parameters.noiseAmplitude,
                    Density = density,
                };
                job.Schedule(total, 64).Complete();

                var managed = new float[total];
                density.CopyTo(managed);
                return managed;
            }
            finally
            {
                density.Dispose();
            }
        }

        void MarchTetrahedron(
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float v0, float v1, float v2, float v3)
        {
            _p[0] = p0; _p[1] = p1; _p[2] = p2; _p[3] = p3;
            _v[0] = v0; _v[1] = v1; _v[2] = v2; _v[3] = v3;

            // Partition the four corners into "inside" (density above iso) and "outside".
            int h0 = 0, h1 = 0, h2 = 0, l0 = 0, l1 = 0, l2 = 0;
            int hc = 0, lc = 0;
            for (int i = 0; i < 4; i++)
            {
                if (_v[i] > _iso)
                {
                    if (hc == 0) h0 = i; else if (hc == 1) h1 = i; else h2 = i;
                    hc++;
                }
                else
                {
                    if (lc == 0) l0 = i; else if (lc == 1) l1 = i; else l2 = i;
                    lc++;
                }
            }
            if (hc == 0 || hc == 4)
                return;

            // Centroid of the inside corners: reference for outward triangle orientation.
            Vector3 insideRef;
            if (hc == 1) insideRef = _p[h0];
            else if (hc == 2) insideRef = (_p[h0] + _p[h1]) * 0.5f;
            else insideRef = (_p[h0] + _p[h1] + _p[h2]) / 3f;

            if (hc == 1)
            {
                EmitTriangle(Edge(h0, l0), Edge(h0, l1), Edge(h0, l2), insideRef);
            }
            else if (hc == 3)
            {
                EmitTriangle(Edge(l0, h0), Edge(l0, h1), Edge(l0, h2), insideRef);
            }
            else // hc == 2 -> the surface crosses four edges, forming a quad
            {
                Vector3 e0 = Edge(h0, l0);
                Vector3 e1 = Edge(h1, l0);
                Vector3 e2 = Edge(h1, l1);
                Vector3 e3 = Edge(h0, l1);
                EmitTriangle(e0, e1, e2, insideRef);
                EmitTriangle(e0, e2, e3, insideRef);
            }
        }

        Vector3 Edge(int a, int b)
        {
            float denom = _v[b] - _v[a];
            float t = Mathf.Abs(denom) > 1e-8f ? (_iso - _v[a]) / denom : 0.5f;
            return Vector3.Lerp(_p[a], _p[b], Mathf.Clamp01(t));
        }

        void EmitTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 insideRef)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-14f)
                return; // degenerate sliver

            // Orient so the normal points away from the inside corners.
            Vector3 triCentroid = (a + b + c) / 3f;
            if (Vector3.Dot(normal, insideRef - triCentroid) > 0f)
                (b, c) = (c, b);

            _triangles.Add(Weld(a));
            _triangles.Add(Weld(b));
            _triangles.Add(Weld(c));
        }

        int Weld(Vector3 p)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(p.x * 10000f),
                Mathf.RoundToInt(p.y * 10000f),
                Mathf.RoundToInt(p.z * 10000f));
            if (_weld.TryGetValue(key, out int existing))
                return existing;
            int index = _vertices.Count;
            _vertices.Add(p);
            _weld[key] = index;
            return index;
        }
    }
}
