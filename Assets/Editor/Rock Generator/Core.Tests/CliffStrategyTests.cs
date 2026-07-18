using NUnit.Framework;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;
using Ombrage.Tools.Core.Meshing.Strategies;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class CliffStrategyTests
    {
        CliffStrategy _strategy;

        [SetUp]
        public void SetUp() => _strategy = new CliffStrategy();

        static RockGenerationSettings Settings()
        {
            var s = RockGenerationSettings.CreateDefault();
            s.mode = GenerationMode.Cliff;
            s.cliff.size = new Vector3(4f, 6f, 2.5f);
            s.cliff.faceResolution = new Vector2Int(24, 32);
            return s;
        }

        [Test]
        public void Generate_NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _strategy.Generate(null));
        }

        [Test]
        public void Generate_ProducesNonEmptyMesh()
        {
            MeshData mesh = _strategy.Generate(Settings());
            Assert.Greater(mesh.VertexCount, 3);
            Assert.Greater(mesh.TriangleCount, 0);
            Assert.AreEqual(mesh.VertexCount, mesh.Normals.Length);
            Assert.AreEqual(mesh.VertexCount, mesh.Uvs.Length);
        }

        [Test]
        public void Generate_IsDeterministicForSameSeed()
        {
            MeshData first = _strategy.Generate(Settings());
            MeshData second = _strategy.Generate(Settings());

            Assert.AreEqual(first.VertexCount, second.VertexCount);
            for (int i = 0; i < first.VertexCount; i++)
                Assert.Less(Vector3.Distance(first.Vertices[i], second.Vertices[i]), 1e-5f);
        }

        [Test]
        public void Generate_DifferentSeedsDiffer()
        {
            var a = Settings(); a.seed = 100;
            var b = Settings(); b.seed = 200;

            MeshData meshA = _strategy.Generate(a);
            MeshData meshB = _strategy.Generate(b);

            bool anyDifferent = false;
            int shared = Mathf.Min(meshA.VertexCount, meshB.VertexCount);
            for (int i = 0; i < shared && !anyDifferent; i++)
                anyDifferent = Vector3.Distance(meshA.Vertices[i], meshB.Vertices[i]) > 1e-4f;
            Assert.IsTrue(anyDifferent, "Different seeds produced identical cliffs.");
        }

        [Test]
        public void Generate_BaseSitsAtGroundLevel()
        {
            MeshData mesh = _strategy.Generate(Settings());

            float minY = float.MaxValue;
            foreach (Vector3 v in mesh.Vertices)
                minY = Mathf.Min(minY, v.y);

            // The base face receives no vertical displacement: it stays on the y = 0 plane.
            Assert.Less(Mathf.Abs(minY), 1e-2f, "Cliff base should rest at y = 0.");
        }

        [Test]
        public void Generate_HeightRoughlyMatchesSize()
        {
            var settings = Settings();
            MeshData mesh = _strategy.Generate(settings);

            float maxY = float.MinValue;
            foreach (Vector3 v in mesh.Vertices)
                maxY = Mathf.Max(maxY, v.y);

            // The crest is only ever carved downward, so the top stays at or below size.y.
            Assert.LessOrEqual(maxY, settings.cliff.size.y + 1e-2f);
            Assert.Greater(maxY, settings.cliff.size.y - 1.5f);
        }
    }
}
