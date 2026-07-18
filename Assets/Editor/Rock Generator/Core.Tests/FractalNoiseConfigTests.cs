using NUnit.Framework;
using Unity.Mathematics;
using Ombrage.Tools.Core.Noise;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class FractalNoiseConfigTests
    {
        static readonly float3[] SamplePoints =
        {
            new float3(0.1f, 0.2f, 0.3f),
            new float3(3.7f, -1.2f, 5.5f),
            new float3(-8.4f, 2.9f, -0.6f),
            new float3(12.3f, 7.1f, -4.8f),
        };

        static FractalNoiseConfig Config(NoiseType type, int seed)
            => FractalNoiseConfig.FromSettings(
                new NoiseSettings
                {
                    type = type, frequency = 1.5f, octaves = 4,
                    persistence = 0.5f, lacunarity = 2f
                }, seed);

        [Test]
        public void FromSettings_NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => FractalNoiseConfig.FromSettings(null, 0));
        }

        [Test]
        public void Sample_IsDeterministicForSameSeed()
        {
            var a = Config(NoiseType.Perlin, 404);
            var b = Config(NoiseType.Perlin, 404);
            foreach (var p in SamplePoints)
                Assert.AreEqual(a.Sample(p), b.Sample(p), 1e-6f);
        }

        [Test]
        public void Sample_StaysWithinSignedUnitRange()
        {
            foreach (var type in new[] { NoiseType.Perlin, NoiseType.Voronoi })
            {
                var config = Config(type, 7);
                foreach (var p in SamplePoints)
                {
                    float v = config.Sample(p);
                    Assert.GreaterOrEqual(v, -1.0001f);
                    Assert.LessOrEqual(v, 1.0001f);
                }
            }
        }

        [Test]
        public void Sample_DifferentSeedsProduceDifferentFields()
        {
            var a = Config(NoiseType.Perlin, 1);
            var b = Config(NoiseType.Perlin, 2);
            bool anyDifferent = false;
            foreach (var p in SamplePoints)
                anyDifferent |= math.abs(a.Sample(p) - b.Sample(p)) > 1e-5f;
            Assert.IsTrue(anyDifferent, "Two distinct seeds produced identical samples.");
        }

        [Test]
        public void Sample_SingleOctave_EqualsScaledBaseNoise()
        {
            var config = new FractalNoiseConfig
            {
                type = NoiseType.Perlin,
                frequency = 2.5f,
                octaves = 1,
                persistence = 0.5f,
                lacunarity = 2f,
                seedOffset = float3.zero,
            };

            var point = new float3(0.3f, -0.7f, 1.1f);
            float expected = FractalNoiseConfig.SampleBase(point * config.frequency, NoiseType.Perlin);
            Assert.AreEqual(expected, config.Sample(point), 1e-5f);
        }

        [Test]
        public void SampleBase_BothNoiseTypes_StayWithinRange()
        {
            foreach (var p in SamplePoints)
            {
                float perlin = FractalNoiseConfig.SampleBase(p, NoiseType.Perlin);
                float voronoi = FractalNoiseConfig.SampleBase(p, NoiseType.Voronoi);

                Assert.GreaterOrEqual(perlin, -1f);
                Assert.LessOrEqual(perlin, 1f);
                Assert.GreaterOrEqual(voronoi, -1f);
                Assert.LessOrEqual(voronoi, 1f);
            }
        }
    }
}
