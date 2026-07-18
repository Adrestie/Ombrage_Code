using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Ombrage.Tools.Core.Noise;

namespace Ombrage.Tools.Core.Meshing.Strategies
{
    /// <summary>
    /// Burst-compiled job that evaluates the scalar density field at every grid point of the
    /// Marching Cubes voxel grid. The heavy part of iso-surface extraction (fractal noise
    /// evaluation) runs here, parallel and native; the cheap triangulation stays managed.
    ///
    /// Density combines a radial base field (high at the center, zero at the bounds) with
    /// fractal noise. The outermost grid layer is forced "outside" so the extracted surface
    /// is always sealed (watertight).
    /// </summary>
    [BurstCompile]
    public struct DensitySamplingJob : IJobParallelFor
    {
        public int3 GridPoints;     // grid-point count per axis (cells + 1)
        public float3 BoundsMin;
        public float3 CellSize;
        public float3 BoundsCenter;
        public float3 HalfExtent;

        public FractalNoiseConfig NoiseConfig;
        public float NoiseAmplitude;

        [WriteOnly] public NativeArray<float> Density;

        public void Execute(int index)
        {
            int gx = GridPoints.x;
            int gy = GridPoints.y;
            int gz = GridPoints.z;

            int x = index % gx;
            int y = (index / gx) % gy;
            int z = index / (gx * gy);

            float3 position = BoundsMin + new float3(x, y, z) * CellSize;

            // Radial base field: 1 at the center, 0 at the bounds radius, negative beyond.
            float3 relative = (position - BoundsCenter) / HalfExtent;
            float baseField = 1f - math.length(relative);

            float density = baseField + NoiseConfig.Sample(position) * NoiseAmplitude;

            // Seal the mesh: clamp the outer grid layer firmly to "outside".
            bool boundary = x == 0 || y == 0 || z == 0
                || x == gx - 1 || y == gy - 1 || z == gz - 1;
            if (boundary)
                density = -1f;

            Density[index] = density;
        }
    }
}
