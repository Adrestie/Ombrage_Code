using NUnit.Framework;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class MeshDataTests
    {
        // A flat unit quad in the XY plane, wound so the geometric normal points to +Z.
        static MeshData BuildQuad() => new MeshData
        {
            Vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(0f, 1f, 0f),
            },
            Triangles = new[] { 0, 1, 2, 0, 2, 3 },
            Uvs = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            },
        };

        [Test]
        public void RecalculateNormals_ProducesUnitLengthNormals()
        {
            var data = BuildQuad();
            data.RecalculateNormals();

            Assert.AreEqual(4, data.Normals.Length);
            foreach (var n in data.Normals)
                Assert.AreEqual(1f, n.magnitude, 1e-4f);
        }

        [Test]
        public void RecalculateNormals_FlatQuadFacesForward()
        {
            var data = BuildQuad();
            data.RecalculateNormals();
            foreach (var n in data.Normals)
                Assert.Less(Vector3.Distance(n, Vector3.forward), 1e-3f);
        }

        [Test]
        public void ToMesh_SetsExpectedCounts()
        {
            var data = BuildQuad();
            Mesh mesh = null;
            try
            {
                mesh = data.ToMesh("QuadTest");
                Assert.AreEqual(4, mesh.vertexCount);
                Assert.AreEqual(6, mesh.triangles.Length);
                Assert.AreEqual("QuadTest", mesh.name);
            }
            finally
            {
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ToMesh_WithoutGeometry_Throws()
        {
            var data = new MeshData();
            Assert.Throws<System.InvalidOperationException>(() => data.ToMesh("Empty"));
        }
    }
}
