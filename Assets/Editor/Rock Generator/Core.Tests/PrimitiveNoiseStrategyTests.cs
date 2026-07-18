using NUnit.Framework;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;
using Ombrage.Tools.Core.Meshing.Strategies;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class PrimitiveNoiseStrategyTests
    {
        IMeshGenerationStrategy _strategy;

        [SetUp]
        public void SetUp() => _strategy = new PrimitiveNoiseStrategy();

        static RockGenerationSettings RockSettings()
        {
            var s = RockGenerationSettings.CreateDefault();
            s.mode = GenerationMode.Rock;
            s.primitiveNoise.primitive = BasePrimitive.Icosphere;
            s.primitiveNoise.subdivisions = 2;
            return s;
        }

        [Test]
        public void Algorithm_IsPrimitiveNoise()
        {
            Assert.AreEqual(GenerationAlgorithm.PrimitiveNoise, _strategy.Algorithm);
        }

        [Test]
        public void Generate_NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _strategy.Generate(null));
        }

        [Test]
        public void Generate_Rock_ProducesNonEmptyMesh()
        {
            MeshData mesh = _strategy.Generate(RockSettings());
            Assert.Greater(mesh.VertexCount, 0);
            Assert.Greater(mesh.TriangleCount, 0);
            Assert.AreEqual(mesh.VertexCount, mesh.Normals.Length);
        }

        [Test]
        public void Generate_IsDeterministicForSameSeed()
        {
            var settings = RockSettings();
            MeshData first = _strategy.Generate(settings);
            MeshData second = _strategy.Generate(settings);

            Assert.AreEqual(first.VertexCount, second.VertexCount);
            for (int i = 0; i < first.VertexCount; i++)
            {
                Assert.AreEqual(first.Vertices[i].x, second.Vertices[i].x);
                Assert.AreEqual(first.Vertices[i].y, second.Vertices[i].y);
                Assert.AreEqual(first.Vertices[i].z, second.Vertices[i].z);
            }
        }

        [Test]
        public void Generate_DifferentSeedsProduceDifferentMeshes()
        {
            var a = RockSettings();
            a.seed = 1000;
            var b = RockSettings();
            b.seed = 2000;

            MeshData meshA = _strategy.Generate(a);
            MeshData meshB = _strategy.Generate(b);

            bool anyDifferent = false;
            for (int i = 0; i < meshA.VertexCount && !anyDifferent; i++)
                anyDifferent = Vector3.Distance(meshA.Vertices[i], meshB.Vertices[i]) > 1e-4f;
            Assert.IsTrue(anyDifferent, "Different seeds produced identical meshes.");
        }

        [Test]
        public void Generate_ZeroDisplacement_ReturnsBasePrimitive()
        {
            var settings = RockSettings();
            settings.primitiveNoise.displacementStrength = 0f;

            MeshData generated = _strategy.Generate(settings);
            MeshData baseMesh = IcosphereBuilder.Build(
                settings.primitiveNoise.subdivisions, settings.primitiveNoise.radius);

            Assert.AreEqual(baseMesh.VertexCount, generated.VertexCount);
            for (int i = 0; i < baseMesh.VertexCount; i++)
                Assert.Less(Vector3.Distance(baseMesh.Vertices[i], generated.Vertices[i]), 1e-5f);
        }

        [Test]
        public void Generate_Cliff_WithBoxPrimitive_ProducesValidMesh()
        {
            var settings = RockGenerationSettings.CreateDefault();
            settings.mode = GenerationMode.Cliff;
            settings.primitiveNoise.primitive = BasePrimitive.Box;
            settings.primitiveNoise.boxResolution = new Vector3Int(6, 6, 6);

            MeshData mesh = _strategy.Generate(settings);

            Assert.Greater(mesh.VertexCount, 0);
            Assert.Greater(mesh.TriangleCount, 0);
            foreach (var v in mesh.Vertices)
            {
                Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z),
                    "Generated mesh contains NaN vertices.");
            }
        }
    }
}
