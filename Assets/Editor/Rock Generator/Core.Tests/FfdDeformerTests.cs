using NUnit.Framework;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Tests
{
    public sealed class FfdDeformerTests
    {
        /// <summary>A small but valid mesh: a quad (4 vertices, 2 triangles).</summary>
        static MeshData MakeQuad()
        {
            return new MeshData
            {
                Vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(1f, -1f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(-1f, 1f, 0f),
                },
                Triangles = new[] { 0, 1, 2, 0, 2, 3 },
                Normals = new Vector3[4],
                Uvs = new Vector2[4],
            };
        }

        [Test]
        public void Deform_WhenDisabled_LeavesVerticesUnchanged()
        {
            MeshData mesh = MakeQuad();
            var original = (Vector3[])mesh.Vertices.Clone();

            FfdDeformer.Deform(mesh, new FfdParameters { enabled = false });

            CollectionAssert.AreEqual(original, mesh.Vertices);
        }

        [Test]
        public void Deform_WithRestLattice_LeavesVerticesUnchanged()
        {
            MeshData mesh = MakeQuad();
            var original = (Vector3[])mesh.Vertices.Clone();

            var ffd = new FfdParameters
            {
                enabled = true,
                resolution = new Vector3Int(2, 2, 2),
                controlPointOffsets = new Vector3[8], // all zero -> identity
            };

            FfdDeformer.Deform(mesh, ffd);

            CollectionAssert.AreEqual(original, mesh.Vertices);
        }

        [Test]
        public void Deform_UniformControlPointOffset_TranslatesWholeMesh()
        {
            MeshData mesh = MakeQuad();
            var original = (Vector3[])mesh.Vertices.Clone();
            var translation = new Vector3(2f, -3f, 1.5f);

            var offsets = new Vector3[8];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = translation;

            var ffd = new FfdParameters
            {
                enabled = true,
                resolution = new Vector3Int(2, 2, 2),
                controlPointOffsets = offsets,
                boundsPadding = 0f,
            };

            FfdDeformer.Deform(mesh, ffd);

            for (int i = 0; i < original.Length; i++)
            {
                Vector3 expected = original[i] + translation;
                Assert.That(Vector3.Distance(mesh.Vertices[i], expected), Is.LessThan(1e-3f),
                    "A uniform lattice offset must translate every vertex equally.");
            }
        }

        [Test]
        public void Deform_WithMismatchedOffsetArray_LeavesVerticesUnchanged()
        {
            MeshData mesh = MakeQuad();
            var original = (Vector3[])mesh.Vertices.Clone();

            var ffd = new FfdParameters
            {
                enabled = true,
                resolution = new Vector3Int(3, 3, 3),
                controlPointOffsets = new Vector3[4], // wrong length for 3x3x3
            };

            FfdDeformer.Deform(mesh, ffd);

            CollectionAssert.AreEqual(original, mesh.Vertices);
        }

        [Test]
        public void ClampedResolution_EnforcesMinimumOfTwo()
        {
            var ffd = new FfdParameters { resolution = new Vector3Int(1, 0, 5) };
            Assert.AreEqual(new Vector3Int(2, 2, 5), ffd.ClampedResolution);
        }

        [Test]
        public void ComputeBox_FitsMeshBounds()
        {
            MeshData mesh = MakeQuad();
            Bounds box = FfdDeformer.ComputeBox(mesh, new FfdParameters { boundsPadding = 0f });

            Assert.That(box.min.x, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(box.max.y, Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
