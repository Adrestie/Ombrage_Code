using NUnit.Framework;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;
using Ombrage.Tools.Core.Meshing.Strategies;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class MarchingCubesStrategyTests
    {
        static RockGenerationSettings Settings()
        {
            var s = RockGenerationSettings.CreateDefault();
            s.mode = GenerationMode.Rock;
            s.algorithm = GenerationAlgorithm.MarchingCubes;
            s.marchingCubes.gridResolution = new Vector3Int(16, 16, 16);
            s.marchingCubes.bounds = Vector3.one * 2f;
            return s;
        }

        [Test]
        public void Algorithm_IsMarchingCubes()
        {
            Assert.AreEqual(GenerationAlgorithm.MarchingCubes, new MarchingCubesStrategy().Algorithm);
        }

        [Test]
        public void Generate_NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new MarchingCubesStrategy().Generate(null));
        }

        [Test]
        public void Generate_ProducesNonEmptyMesh()
        {
            MeshData mesh = new MarchingCubesStrategy().Generate(Settings());
            Assert.Greater(mesh.VertexCount, 3);
            Assert.Greater(mesh.TriangleCount, 0);
            Assert.AreEqual(mesh.VertexCount, mesh.Normals.Length);
            Assert.AreEqual(mesh.VertexCount, mesh.Uvs.Length);
        }

        [Test]
        public void Generate_IsDeterministicForSameSeed()
        {
            MeshData first = new MarchingCubesStrategy().Generate(Settings());
            MeshData second = new MarchingCubesStrategy().Generate(Settings());

            Assert.AreEqual(first.VertexCount, second.VertexCount);
            for (int i = 0; i < first.VertexCount; i++)
                Assert.Less(Vector3.Distance(first.Vertices[i], second.Vertices[i]), 1e-5f);
        }

        [Test]
        public void Generate_VerticesStayWithinBounds()
        {
            var settings = Settings();
            MeshData mesh = new MarchingCubesStrategy().Generate(settings);

            Vector3 half = settings.marchingCubes.bounds * 0.5f;
            foreach (Vector3 v in mesh.Vertices)
            {
                Assert.LessOrEqual(Mathf.Abs(v.x), half.x + 1e-3f);
                Assert.LessOrEqual(Mathf.Abs(v.y), half.y + 1e-3f);
                Assert.LessOrEqual(Mathf.Abs(v.z), half.z + 1e-3f);
            }
        }

        [Test]
        public void Generate_NoNaNVertices()
        {
            MeshData mesh = new MarchingCubesStrategy().Generate(Settings());
            foreach (Vector3 v in mesh.Vertices)
                Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z));
        }
    }
}
