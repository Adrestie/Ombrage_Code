using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>
    /// Burst-compiled trivariate Bezier (Bernstein basis) free-form deformation. Each
    /// vertex is expressed in normalized lattice coordinates and recombined from the
    /// control-point grid. A rest lattice (control points on a regular grid) reproduces
    /// the input vertices exactly.
    /// </summary>
    [BurstCompile]
    public struct FfdDeformationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> BaseVertices;

        /// <summary>Flattened control points, length = Resolution.x * y * z.</summary>
        [ReadOnly] public NativeArray<float3> ControlPoints;

        public int3 Resolution;
        public float3 BoxMin;
        public float3 BoxSize;

        [WriteOnly] public NativeArray<float3> Deformed;

        public void Execute(int index)
        {
            // Normalized lattice coordinates (s, t, u) in [0, 1].
            float3 local = math.saturate((BaseVertices[index] - BoxMin) / BoxSize);

            int lx = Resolution.x;
            int ly = Resolution.y;
            int lz = Resolution.z;

            float3 acc = float3.zero;
            for (int i = 0; i < lx; i++)
            {
                float bx = Bernstein(i, lx - 1, local.x);
                for (int j = 0; j < ly; j++)
                {
                    float bxy = bx * Bernstein(j, ly - 1, local.y);
                    for (int k = 0; k < lz; k++)
                    {
                        float weight = bxy * Bernstein(k, lz - 1, local.z);
                        acc += weight * ControlPoints[i + j * lx + k * lx * ly];
                    }
                }
            }

            Deformed[index] = acc;
        }

        /// <summary>The i-th Bernstein basis polynomial of degree n, evaluated at t.</summary>
        static float Bernstein(int i, int n, float t)
        {
            return Binomial(n, i) * math.pow(t, (float)i) * math.pow(1f - t, (float)(n - i));
        }

        /// <summary>Binomial coefficient C(n, k). n is small (lattice resolution - 1).</summary>
        static float Binomial(int n, int k)
        {
            if (k < 0 || k > n)
                return 0f;

            float result = 1f;
            for (int j = 0; j < k; j++)
                result = result * (n - j) / (j + 1);
            return result;
        }
    }
}
