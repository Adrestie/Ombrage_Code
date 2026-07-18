using System;
using UnityEngine;
using Unity.Mathematics;
using Ombrage.Tools.Core.Noise;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Meshing.Strategies
{
    /// <summary>
    /// Generates a faceted boulder: a deterministic, seed-driven point cloud is scattered on
    /// a jittered sphere, wrapped in its 3D convex hull, then its corners are nudged by
    /// fractal noise. Lower point counts read as sharp, low-poly rock.
    /// </summary>
    public sealed class ConvexHullStrategy : IMeshGenerationStrategy
    {
        public GenerationAlgorithm Algorithm => GenerationAlgorithm.ConvexHull;

        public MeshData Generate(RockGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ConvexHullParameters parameters = settings.convexHull
                ?? throw new ArgumentException("convexHull parameters are missing.", nameof(settings));

            int pointCount = Mathf.Clamp(parameters.pointCount, 4, 512);
            float radius = Mathf.Max(0.0001f, parameters.radius);

            // Deterministic point cloud. Random needs a non-zero seed.
            var rng = new Unity.Mathematics.Random(unchecked((uint)settings.seed) | 1u);
            var points = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                float3 direction = math.normalize(rng.NextFloat3Direction());
                float r = radius * (0.78f + 0.22f * rng.NextFloat());
                points[i] = (Vector3)(direction * r);
            }

            MeshData hull = ConvexHullBuilder.Build(points);
            hull.RecalculateNormals();

            if (parameters.displacementStrength > 0f)
            {
                FractalNoiseConfig noiseConfig = FractalNoiseConfig.FromSettings(parameters.noise, settings.seed);
                for (int i = 0; i < hull.Vertices.Length; i++)
                {
                    float displacement = noiseConfig.Sample(hull.Vertices[i]) * parameters.displacementStrength;
                    hull.Vertices[i] += hull.Normals[i] * displacement;
                }
                hull.RecalculateNormals();
            }

            hull.Uvs = MeshUtils.BoxProjectUvs(hull.Vertices, hull.Normals);
            return hull;
        }
    }
}
