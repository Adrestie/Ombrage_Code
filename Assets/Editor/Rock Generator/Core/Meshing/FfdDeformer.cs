using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>
    /// Applies a free-form deformation (<see cref="FfdParameters"/>) to mesh data. The
    /// lattice box is fit to the mesh's axis-aligned bounds; the deformation itself runs
    /// as a Burst-compiled job and surface normals are recalculated afterwards.
    /// </summary>
    public static class FfdDeformer
    {
        const float MinAxisSize = 1e-4f;

        /// <summary>
        /// Computes the lattice box for a mesh: its axis-aligned bounding box, expanded by
        /// the configured padding. Returned even when FFD is disabled so editor tooling
        /// can position the lattice consistently.
        /// </summary>
        public static Bounds ComputeBox(MeshData mesh, FfdParameters ffd)
        {
            if (mesh == null || mesh.Vertices == null || mesh.Vertices.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Vector3 min = mesh.Vertices[0];
            Vector3 max = min;
            for (int i = 1; i < mesh.Vertices.Length; i++)
            {
                min = Vector3.Min(min, mesh.Vertices[i]);
                max = Vector3.Max(max, mesh.Vertices[i]);
            }

            float padding = ffd != null ? Mathf.Max(0f, ffd.boundsPadding) : 0f;
            Vector3 pad = (max - min) * padding;
            min -= pad;
            max += pad;

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>Deforms the mesh in place, fitting the lattice box to the mesh itself.</summary>
        public static void Deform(MeshData mesh, FfdParameters ffd)
            => Deform(mesh, ffd, ComputeBox(mesh, ffd));

        /// <summary>
        /// Deforms <paramref name="mesh"/> in place using the supplied lattice box. Does
        /// nothing when the FFD parameters describe no actual deformation.
        /// </summary>
        public static void Deform(MeshData mesh, FfdParameters ffd, Bounds box)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            if (ffd == null || !ffd.HasDeformation
                || mesh.Vertices == null || mesh.Vertices.Length == 0)
                return;

            Vector3Int res = ffd.ClampedResolution;
            int controlPointCount = res.x * res.y * res.z;

            Vector3 boxMin = box.min;
            Vector3 boxSize = new Vector3(
                Mathf.Max(MinAxisSize, box.size.x),
                Mathf.Max(MinAxisSize, box.size.y),
                Mathf.Max(MinAxisSize, box.size.z));

            int vertexCount = mesh.Vertices.Length;

            var baseVertices = new NativeArray<float3>(vertexCount, Allocator.TempJob);
            var controlPoints = new NativeArray<float3>(controlPointCount, Allocator.TempJob);
            var deformed = new NativeArray<float3>(vertexCount, Allocator.TempJob);

            try
            {
                for (int i = 0; i < vertexCount; i++)
                    baseVertices[i] = mesh.Vertices[i];

                FillControlPoints(controlPoints, res, boxMin, boxSize, ffd.controlPointOffsets);

                var job = new FfdDeformationJob
                {
                    BaseVertices = baseVertices,
                    ControlPoints = controlPoints,
                    Resolution = new int3(res.x, res.y, res.z),
                    BoxMin = boxMin,
                    BoxSize = boxSize,
                    Deformed = deformed,
                };
                job.Schedule(vertexCount, 64).Complete();

                var result = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    result[i] = deformed[i];
                mesh.Vertices = result;
            }
            finally
            {
                baseVertices.Dispose();
                controlPoints.Dispose();
                deformed.Dispose();
            }

            // The deformation changes surface orientation; normals must be rebuilt.
            mesh.RecalculateNormals();
        }

        /// <summary>Fills the control-point grid: regular rest lattice plus per-point offsets.</summary>
        static void FillControlPoints(
            NativeArray<float3> controlPoints, Vector3Int res,
            Vector3 boxMin, Vector3 boxSize, Vector3[] offsets)
        {
            for (int z = 0; z < res.z; z++)
            for (int y = 0; y < res.y; y++)
            for (int x = 0; x < res.x; x++)
            {
                int index = x + y * res.x + z * res.x * res.y;
                var t = new Vector3(
                    x / (float)(res.x - 1),
                    y / (float)(res.y - 1),
                    z / (float)(res.z - 1));
                Vector3 rest = boxMin + Vector3.Scale(t, boxSize);
                controlPoints[index] = rest + offsets[index];
            }
        }
    }
}
