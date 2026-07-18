using NUnit.Framework;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;
using Ombrage.Tools.Core.Meshing.Strategies;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class ConvexHullStrategyTests
    {
        IMeshGenerationStrategy _strategy;

        [SetUp]
        public void SetUp() => _strategy = new ConvexHullStrategy();

        static RockGenerationSettings Settings()
        {
            var s = RockGenerationSettings.CreateDefault();
            s.mode = GenerationMode.Rock;
            s.algorithm = GenerationAlgorithm.ConvexHull;
            return s;
        }

        [Test]
        public void Algorithm_IsConvexHull()
        {
            Assert.AreEqual(GenerationAlgorithm.ConvexHull, _strategy.Algorithm);
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
        public void Generate_HullStaysWithinPointCloudPlusDisplacement()
        {
            var settings = Settings();
            settings.convexHull.radius = 1f;
            settings.convexHull.displacementStrength = 0.15f;

            MeshData mesh = _strategy.Generate(settings);
            foreach (Vector3 v in mesh.Vertices)
                Assert.LessOrEqual(v.magnitude, 1f + 0.15f + 0.05f);
        }

        [Test]
        public void Builder_Octahedron_ProducesClosedHull()
        {
            // Six axis points: an octahedron has no coplanar faces, so the hull is unambiguous.
            var points = new[]
            {
                new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
                new Vector3(0, 1, 0), new Vector3(0, -1, 0),
                new Vector3(0, 0, 1), new Vector3(0, 0, -1),
            };

            MeshData hull = ConvexHullBuilder.Build(points);

            Assert.AreEqual(6, hull.VertexCount, "All six extreme points must be on the hull.");
            Assert.AreEqual(8, hull.TriangleCount, "An octahedron hull has eight triangular faces.");
        }
    }
}
