using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Ombrage.Tools.Core.Noise;

namespace Ombrage.Tools.Core.Meshing.Strategies
{
    /// <summary>
    /// Burst-compiled job that displaces each base vertex along its normal using fractal
    /// noise. One <see cref="Execute"/> call per vertex, fully parallel: every index
    /// reads/writes only its own slot, so there is no race.
    /// </summary>
    [BurstCompile]
    public struct VertexDisplacementJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> BaseVertices;
        [ReadOnly] public NativeArray<float3> BaseNormals;
        [WriteOnly] public NativeArray<float3> DisplacedVertices;

        public FractalNoiseConfig NoiseConfig;
        public float DisplacementStrength;

        public void Execute(int index)
        {
            float3 vertex = BaseVertices[index];
            float3 normal = BaseNormals[index];

            float displacement = NoiseConfig.Sample(vertex) * DisplacementStrength;
            DisplacedVertices[index] = vertex + normal * displacement;
        }
    }
}
