using System;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Ombrage.Tools.Core.Noise;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Meshing.Strategies
{
    /// <summary>
    /// Dedicated cliff generator. Builds a watertight, subdivided box (base at y = 0) and
    /// sculpts it into a stylized game cliff with <see cref="CliffDisplacementJob"/>:
    /// layered strata, vertical fracturing, overhangs and an irregular crest. Meant to be
    /// planted against Unity Terrain to add vertical relief the heightmap cannot represent.
    ///
    /// This is intentionally NOT an <see cref="IMeshGenerationStrategy"/>: cliffs are a
    /// separate generation mode, not one of the Rock algorithms.
    /// </summary>
    public sealed class CliffStrategy
    {
        public MeshData Generate(RockGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            CliffParameters cliff = settings.cliff
                ?? throw new ArgumentException("cliff parameters are missing.", nameof(settings));

            Vector3 size = new Vector3(
                Mathf.Max(0.1f, cliff.size.x),
                Mathf.Max(0.1f, cliff.size.y),
                Mathf.Max(0.1f, cliff.size.z));

            int rx = Mathf.Clamp(cliff.faceResolution.x, 4, 120);
            int ry = Mathf.Clamp(cliff.faceResolution.y, 4, 120);
            const int depthResolution = 4;

            MeshData box = BoxBuilder.Build(size, new Vector3Int(rx, ry, depthResolution));

            int vertexCount = box.VertexCount;
            var baseVertices = new NativeArray<float3>(vertexCount, Allocator.TempJob);
            var displaced = new NativeArray<float3>(vertexCount, Allocator.TempJob);

            try
            {
                // BoxBuilder centers the box on the origin; shift it so the base sits at y = 0.
                float halfHeight = size.y * 0.5f;
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 v = box.Vertices[i];
                    baseVertices[i] = new float3(v.x, v.y + halfHeight, v.z);
                }

                var rng = new Unity.Mathematics.Random(unchecked((uint)settings.seed) | 1u);

                var job = new CliffDisplacementJob
                {
                    BaseVertices = baseVertices,
                    DisplacedVertices = displaced,
                    Size = new float3(size.x, size.y, size.z),
                    MacroNoise = FractalNoiseConfig.FromSettings(cliff.macroNoise, settings.seed),
                    SeedOffset = rng.NextFloat3() * 100f,
                    SeedScalar = rng.NextFloat() * 50f,
                    StratumHeight = Mathf.Max(0.05f, cliff.stratumHeight),
                    StratumStrength = Mathf.Clamp01(cliff.stratumStrength),
                    StratumJitter = Mathf.Clamp01(cliff.stratumJitter),
                    OverhangBias = Mathf.Clamp01(cliff.overhangBias),
                    FaceLean = Mathf.Clamp(cliff.faceLean, -0.6f, 0.6f),
                    FractureStrength = Mathf.Clamp01(cliff.fractureStrength),
                    FractureScale = Mathf.Max(0.1f, cliff.fractureScale),
                    DetailStrength = Mathf.Clamp01(cliff.detailStrength),
                    CrestRoughness = Mathf.Clamp01(cliff.crestRoughness),
                    ErosionStrength = Mathf.Clamp01(cliff.erosionStrength),
                };
                job.Schedule(vertexCount, 64).Complete();

                var vertices = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    vertices[i] = displaced[i];

                var result = new MeshData
                {
                    Vertices = vertices,
                    Triangles = box.Triangles,
                };
                result.RecalculateNormals();
                result.Uvs = MeshUtils.BoxProjectUvs(result.Vertices, result.Normals);
                return result;
            }
            finally
            {
                baseVertices.Dispose();
                displaced.Dispose();
            }
        }
    }
}
