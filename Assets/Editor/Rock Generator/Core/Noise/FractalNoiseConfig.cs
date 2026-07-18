using System;
using Unity.Mathematics;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Noise
{
    /// <summary>
    /// Blittable, Burst-compatible fractal (fBm) noise configuration.
    ///
    /// This replaces the previous managed <c>INoiseField</c> hierarchy: a Burst job cannot
    /// call managed virtual dispatch, so the noise is expressed as a self-contained value
    /// type sampled by a plain method. Base noise comes from <see cref="Unity.Mathematics"/>
    /// (<c>cnoise</c> / <c>cellular</c>), which is Burst-friendly and true 3D.
    /// </summary>
    public struct FractalNoiseConfig
    {
        public NoiseType type;
        public float frequency;
        public int octaves;
        public float persistence;
        public float lacunarity;

        /// <summary>Domain offset derived from the seed. Two seeds give decorrelated fields.</summary>
        public float3 seedOffset;

        /// <summary>
        /// Builds a job-ready config from serialized settings. The seed is resolved to a
        /// domain offset here, on the managed thread, so the job itself stays deterministic
        /// and free of any RNG.
        /// </summary>
        public static FractalNoiseConfig FromSettings(NoiseSettings settings, int seed)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            return new FractalNoiseConfig
            {
                type = settings.type,
                frequency = math.max(0.0001f, settings.frequency),
                octaves = math.clamp(settings.octaves, 1, 16),
                persistence = math.saturate(settings.persistence),
                lacunarity = math.max(1f, settings.lacunarity),
                seedOffset = SeedToOffset(seed),
            };
        }

        /// <summary>Samples fractal noise at a position. The result lies within [-1, 1].</summary>
        public float Sample(float3 position)
        {
            float amplitude = 1f;
            float freq = frequency;
            float sum = 0f;
            float normalization = 0f;

            for (int octave = 0; octave < octaves; octave++)
            {
                sum += SampleBase(position * freq + seedOffset, type) * amplitude;
                normalization += amplitude;
                amplitude *= persistence;
                freq *= lacunarity;
            }

            return normalization > 0f ? sum / normalization : 0f;
        }

        /// <summary>Single-octave base noise sample. The result lies within [-1, 1].</summary>
        public static float SampleBase(float3 position, NoiseType noiseType)
        {
            switch (noiseType)
            {
                case NoiseType.Voronoi:
                    // cellular() returns (F1, F2); F1 is the distance to the nearest point.
                    float f1 = noise.cellular(position).x;
                    return math.clamp(1f - f1 * 2f, -1f, 1f);
                default:
                    return math.clamp(noise.cnoise(position), -1f, 1f);
            }
        }

        static float3 SeedToOffset(int seed)
        {
            uint state = unchecked((uint)seed);
            return new float3(
                NextUnit(ref state),
                NextUnit(ref state),
                NextUnit(ref state)) * 1000f;
        }

        // Deterministic PCG-style hash producing a value in [0, 1).
        static float NextUnit(ref uint state)
        {
            unchecked
            {
                state = state * 747796405u + 2891336453u;
                uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
                word = (word >> 22) ^ word;
                return (word & 0xFFFFFFu) / (float)0x1000000;
            }
        }
    }
}
