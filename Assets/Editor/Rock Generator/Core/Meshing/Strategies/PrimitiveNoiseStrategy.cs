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
    /// Generates a rock by displacing a base primitive (icosphere or box) along its surface
    /// normals using fractal noise. The per-vertex displacement runs as a Burst-compiled,
    /// parallel job (<see cref="VertexDisplacementJob"/>).
    /// </summary>
    public sealed class PrimitiveNoiseStrategy : IMeshGenerationStrategy
    {
        public GenerationAlgorithm Algorithm => GenerationAlgorithm.PrimitiveNoise;

        public MeshData Generate(RockGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            PrimitiveNoiseParameters parameters = settings.primitiveNoise
                ?? throw new ArgumentException("primitiveNoise parameters are missing.", nameof(settings));

            MeshData baseMesh = BuildBasePrimitive(parameters);
            if (baseMesh.Normals == null || baseMesh.Normals.Length != baseMesh.VertexCount)
                baseMesh.RecalculateNormals();

            int vertexCount = baseMesh.VertexCount;
            var baseVertices = new NativeArray<float3>(vertexCount, Allocator.TempJob);
            var baseNormals = new NativeArray<float3>(vertexCount, Allocator.TempJob);
            var displaced = new NativeArray<float3>(vertexCount, Allocator.TempJob);

            try
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    baseVertices[i] = baseMesh.Vertices[i];
                    baseNormals[i] = baseMesh.Normals[i];
                }

                var job = new VertexDisplacementJob
                {
                    BaseVertices = baseVertices,
                    BaseNormals = baseNormals,
                    DisplacedVertices = displaced,
                    NoiseConfig = FractalNoiseConfig.FromSettings(parameters.noise, settings.seed),
                    DisplacementStrength = parameters.displacementStrength,
                };

                // Schedule + Complete: synchronous from the caller's point of view, but the
                // work itself runs Burst-compiled and spread across worker threads.
                job.Schedule(vertexCount, 64).Complete();

                var resultVertices = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    resultVertices[i] = displaced[i];

                var result = new MeshData
                {
                    Vertices = resultVertices,
                    Triangles = baseMesh.Triangles,
                    Uvs = baseMesh.Uvs
                };
                result.RecalculateNormals();
                return result;
            }
            finally
            {
                baseVertices.Dispose();
                baseNormals.Dispose();
                displaced.Dispose();
            }
        }

        static MeshData BuildBasePrimitive(PrimitiveNoiseParameters parameters)
        {
            return parameters.primitive == BasePrimitive.Box
                ? BoxBuilder.Build(parameters.boxSize, parameters.boxResolution)
                : IcosphereBuilder.Build(parameters.subdivisions, parameters.radius);
        }
    }
}
