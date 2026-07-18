using NUnit.Framework;
using Ombrage.Tools.Core.Serialization;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class JsonSettingsSerializerTests
    {
        ISettingsSerializer _serializer;

        [SetUp]
        public void SetUp() => _serializer = new JsonSettingsSerializer();

        [Test]
        public void RoundTrip_PreservesValues()
        {
            var original = RockGenerationSettings.CreateDefault();
            original.presetName = "Granite Boulder";
            original.seed = 9981;
            original.mode = GenerationMode.Cliff;
            original.algorithm = GenerationAlgorithm.MarchingCubes;
            original.primitiveNoise.displacementStrength = 0.62f;
            original.primitiveNoise.noise.octaves = 6;
            original.cliff.stratumStrength = 0.33f;

            string payload = _serializer.Serialize(original);
            RockGenerationSettings restored = _serializer.Deserialize(payload);

            Assert.AreEqual(original.presetName, restored.presetName);
            Assert.AreEqual(original.seed, restored.seed);
            Assert.AreEqual(original.mode, restored.mode);
            Assert.AreEqual(original.algorithm, restored.algorithm);
            Assert.AreEqual(original.primitiveNoise.displacementStrength,
                restored.primitiveNoise.displacementStrength, 1e-6f);
            Assert.AreEqual(original.primitiveNoise.noise.octaves, restored.primitiveNoise.noise.octaves);
            Assert.AreEqual(original.cliff.stratumStrength, restored.cliff.stratumStrength, 1e-6f);
        }

        [Test]
        public void Deserialize_NullOrEmpty_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => _serializer.Deserialize(null));
            Assert.Throws<System.ArgumentException>(() => _serializer.Deserialize("   "));
        }

        [Test]
        public void Deserialize_FutureFormatVersion_Throws()
        {
            var settings = RockGenerationSettings.CreateDefault();
            settings.formatVersion = RockGenerationSettings.CurrentFormatVersion + 1;
            string payload = _serializer.Serialize(settings);

            Assert.Throws<SettingsDeserializationException>(() => _serializer.Deserialize(payload));
        }

        [Test]
        public void Serialize_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _serializer.Serialize(null));
        }

        [Test]
        public void Clone_ProducesIndependentCopy()
        {
            var original = RockGenerationSettings.CreateDefault();
            RockGenerationSettings clone = original.Clone();

            clone.seed = original.seed + 1;
            clone.primitiveNoise.displacementStrength += 1f;

            Assert.AreNotEqual(original.seed, clone.seed);
            Assert.AreNotEqual(original.primitiveNoise.displacementStrength,
                clone.primitiveNoise.displacementStrength);
        }
    }
}
