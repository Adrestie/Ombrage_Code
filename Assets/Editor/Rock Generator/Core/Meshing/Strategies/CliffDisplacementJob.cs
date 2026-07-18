using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Ombrage.Tools.Core.Noise;

namespace Ombrage.Tools.Core.Meshing.Strategies
{
    /// <summary>
    /// Burst-compiled per-vertex displacement that turns a subdivided box into a stylized
    /// cliff: horizontal strata (terraced ledges, overhang-capable), vertical fracturing, a
    /// macro shape warp, surface detail, an irregular crest and an eroded base.
    ///
    /// The displacement is a pure function of vertex position. Because the input box is
    /// welded (shared vertices on face seams), evaluating the same function for a shared
    /// vertex always yields the same result, so the cliff stays watertight.
    /// </summary>
    [BurstCompile]
    public struct CliffDisplacementJob : IJobParallelFor
    {
        // Base vertices, already shifted so the cliff base sits at y = 0 (y in [0, H]).
        [ReadOnly] public NativeArray<float3> BaseVertices;
        [WriteOnly] public NativeArray<float3> DisplacedVertices;

        public float3 Size;            // (width, height, depth)
        public FractalNoiseConfig MacroNoise;
        public float3 SeedOffset;
        public float SeedScalar;

        public float StratumHeight;
        public float StratumStrength;
        public float StratumJitter;
        public float OverhangBias;
        public float FaceLean;
        public float FractureStrength;
        public float FractureScale;
        public float DetailStrength;
        public float CrestRoughness;
        public float ErosionStrength;

        public void Execute(int index)
        {
            float3 v = BaseVertices[index];
            float width = Size.x;
            float height = math.max(1e-4f, Size.y);
            float depth = math.max(1e-4f, Size.z);

            // Front weight: 0 at the back face, 1 at the front face. The back stays flat so
            // the cliff can be planted against terrain.
            float frontT = math.saturate((v.z + depth * 0.5f) / depth);
            float w = math.smoothstep(0f, 1f, frontT);

            // --- Horizontal strata ------------------------------------------------
            float stratum = math.floor(v.y / math.max(0.05f, StratumHeight));
            float protrusion = StratumProtrusion(stratum);          // centered around 0

            // --- Vertical fracturing (columnar jointing) --------------------------
            float fracture = noise.cnoise(
                new float3(v.x * FractureScale, v.y * 0.2f, 0f) + SeedOffset);   // [-1, 1]
            float fractureRecess = math.min(0f, fracture) * FractureStrength;

            // --- Macro warp & fine detail -----------------------------------------
            float macro = MacroNoise.Sample(v);                                  // [-1, 1]
            float detail = noise.cnoise(v * 3.7f + SeedOffset * 1.7f);           // [-1, 1]

            // --- Base erosion & top crest -----------------------------------------
            float baseErode = math.saturate(1f - v.y / math.max(1e-4f, height * 0.35f));
            float crestBand = math.max(0.05f, StratumHeight);
            float crestT = math.saturate((v.y - (height - crestBand)) / crestBand);

            // --- Outward (Z) displacement, in world units -------------------------
            float outward =
                  protrusion * StratumStrength * 0.5f * depth
                + macro * 0.22f * depth
                + fractureRecess * 0.45f * depth
                - baseErode * ErosionStrength * 0.4f * depth
                + FaceLean * (v.y / height) * depth
                + detail * DetailStrength * 0.12f * depth;

            // Never punch through the (flat) back face.
            outward = math.max(outward, -depth * 0.45f);

            float3 displacement = float3.zero;
            displacement.z = w * outward;
            displacement.x = w * fracture * FractureStrength * 0.12f * width;

            // Irregular crest along the top edge.
            float crestNoise = noise.cnoise(new float3(v.x, v.z, 0f) * 1.3f + SeedOffset);
            displacement.y -= crestT * (0.5f + 0.5f * crestNoise) * CrestRoughness * crestBand;
            displacement.z += w * crestT * crestNoise * CrestRoughness * 0.3f * depth;

            DisplacedVertices[index] = v + displacement;
        }

        /// <summary>Per-stratum protrusion, centered around 0. Upper strata can protrude
        /// more than lower ones (driven by <see cref="OverhangBias"/>), creating overhangs.</summary>
        float StratumProtrusion(float stratum)
        {
            float perLayer = Hash01(stratum, SeedScalar);
            float jittered = math.lerp(0.5f, perLayer, StratumJitter);
            float shelf = (SmoothStratum(stratum) - 0.5f) * OverhangBias * 1.6f;
            return jittered + shelf - 0.5f;
        }

        /// <summary>Low-frequency value correlated across neighbouring strata: produces
        /// multi-layer shelves rather than per-layer noise.</summary>
        float SmoothStratum(float stratum)
        {
            float scaled = stratum * 0.34f;
            float i0 = math.floor(scaled);
            float frac = scaled - i0;
            float h0 = Hash01(i0, SeedScalar + 5f);
            float h1 = Hash01(i0 + 1f, SeedScalar + 5f);
            return math.lerp(h0, h1, math.smoothstep(0f, 1f, frac));
        }

        static float Hash01(float n, float seed)
        {
            return math.frac(math.sin(n * 127.1f + seed * 311.7f) * 43758.5453f);
        }
    }
}
