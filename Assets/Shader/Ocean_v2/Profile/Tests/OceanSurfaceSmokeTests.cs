// OceanSurfaceSmokeTests.cs  (Ocean_v2)
// Smoke test EditMode MINIMAL pour l'assembly runtime Ombrage.OceanFeatures.
//
// OBJECTIF : capter hors éditeur les ruptures de COMPILATION
// et de LOGIQUE C# du chemin de surface — que la validation manuelle play-mode (rendu deferred / MV / GPU)
// ne peut couvrir. Ne teste PAS le rendu (impossible hors play-mode) : uniquement des invariants purs.
//
// Couverture :
//   1) Le shader de surface est trouvable par son nom canonique (détecte un renommage / une non-import).
//   2) Un matériau se crée depuis ce shader (détecte un shader cassé au point de ne plus instancier).
//   3) OceanSurfaceRuntime.GenerateUniformGrid : comptes verts/tris, clamp de résolution, format d'index,
//      normale de base vers le haut, bounds via SetBounds.
//   4) OceanSurfaceModule.SameArrayDims (durcissement) : null↔null=vrai, null↔non-null=faux,
//      dims différentes=faux, dims identiques=vrai, current non-RenderTexture=faux.
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.OceanFeatures.Tests
{
    public sealed class OceanSurfaceSmokeTests
    {
        const string k_SurfaceShaderName = "Custom/HDRP/OceanSurface";

        // ---------------------------------------------------------------- Shader / matériau

        [Test]
        public void SurfaceShader_IsFindableByCanonicalName()
        {
            var shader = Shader.Find(k_SurfaceShaderName);
            Assert.IsNotNull(shader,
                $"Shader introuvable : « {k_SurfaceShaderName} ». Renommé, non importé, ou échec de compilation ?");
        }

        [Test]
        public void SurfaceShader_InstantiatesAsMaterial()
        {
            var shader = Shader.Find(k_SurfaceShaderName);
            Assume.That(shader, Is.Not.Null, "Shader de surface absent — voir SurfaceShader_IsFindableByCanonicalName.");

            Material mat = null;
            try
            {
                mat = new Material(shader);
                Assert.IsNotNull(mat);
                Assert.AreSame(shader, mat.shader);
            }
            finally
            {
                if (mat != null) Object.DestroyImmediate(mat);
            }
        }

        // ---------------------------------------------------------------- GenerateUniformGrid

        [Test]
        public void GenerateUniformGrid_VertexAndTriangleCounts_AreConsistent()
        {
            const int res = 64;
            var mesh = OceanSurfaceRuntime.GenerateUniformGrid(res, 100f);
            try
            {
                int verts1D = res + 1;
                Assert.AreEqual(verts1D * verts1D, mesh.vertexCount, "vCount attendu = (res+1)².");
                Assert.AreEqual(res * res * 6, mesh.triangles.Length, "triangles attendus = res²·6.");
                Assert.AreEqual(mesh.vertexCount, mesh.normals.Length, "une normale par vertex.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void GenerateUniformGrid_ClampsResolution_ToValidRange()
        {
            // Sous-borne : res<2 → 2 segments.
            var low = OceanSurfaceRuntime.GenerateUniformGrid(0, 10f);
            // Sur-borne : res>254 → 254 segments (garde le vCount sous la limite UInt16→UInt32).
            var high = OceanSurfaceRuntime.GenerateUniformGrid(9999, 10f);
            try
            {
                Assert.AreEqual((2 + 1) * (2 + 1), low.vertexCount, "res<2 doit être clampé à 2.");
                Assert.AreEqual((254 + 1) * (254 + 1), high.vertexCount, "res>254 doit être clampé à 254.");
            }
            finally
            {
                Object.DestroyImmediate(low);
                Object.DestroyImmediate(high);
            }
        }

        [Test]
        public void GenerateUniformGrid_SelectsIndexFormat_ByVertexCount()
        {
            var small = OceanSurfaceRuntime.GenerateUniformGrid(100, 10f);   // 101² = 10201 < 65000
            var large = OceanSurfaceRuntime.GenerateUniformGrid(254, 10f);   // 255² = 65025 > 65000
            try
            {
                Assert.AreEqual(IndexFormat.UInt16, small.indexFormat, "<65000 verts → UInt16.");
                Assert.AreEqual(IndexFormat.UInt32, large.indexFormat, ">65000 verts → UInt32.");
            }
            finally
            {
                Object.DestroyImmediate(small);
                Object.DestroyImmediate(large);
            }
        }

        [Test]
        public void GenerateUniformGrid_BaseNormals_PointUp()
        {
            var mesh = OceanSurfaceRuntime.GenerateUniformGrid(8, 10f);
            try
            {
                foreach (var n in mesh.normals)
                    Assert.AreEqual(Vector3.up, n, "la normale de base doit pointer vers le haut (recomposée analytiquement en fragment).");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SetBounds_InflatesMeshBounds_FromAmplitude()
        {
            var rt = new OceanSurfaceRuntime { mesh = OceanSurfaceRuntime.GenerateUniformGrid(8, 50f) };
            try
            {
                rt.SetBounds(maxWaveHeight: 3f, maxHorizontalDisp: 5f, extent: 50f);
                var b = rt.mesh.bounds;
                Assert.AreEqual(50f + 5f, b.extents.x, 1e-3f, "demi-étendue XZ = extent + déplacement horizontal max.");
                Assert.AreEqual(50f + 5f, b.extents.z, 1e-3f);
                Assert.AreEqual(3f, b.extents.y, 1e-3f, "demi-hauteur Y = hauteur de vague max.");
            }
            finally
            {
                Object.DestroyImmediate(rt.mesh);
            }
        }

        // ---------------------------------------------------------------- SameArrayDims (durcissement)

        [Test]
        public void SameArrayDims_BothNull_IsCoherent()
        {
            // Groupe de cascade absent des deux côtés (jamais échantillonné) → cohérent → MV valides.
            Assert.IsTrue(OceanSurfaceModule.SameArrayDims(null, null));
        }

        [Test]
        public void SameArrayDims_OneNull_IsIncoherent()
        {
            var rt = NewRT(64, 64);
            try
            {
                // Apparition/disparition d'un groupe (ex. Low↔High) → incohérent → MV invalidés ce frame.
                Assert.IsFalse(OceanSurfaceModule.SameArrayDims(null, rt), "prev présent, current nul → incohérent.");
                Assert.IsFalse(OceanSurfaceModule.SameArrayDims(rt, null), "current présent, prev nul → incohérent.");
            }
            finally { FreeRT(rt); }
        }

        [Test]
        public void SameArrayDims_DifferentDimensions_IsIncoherent()
        {
            var a = NewRT(64, 64);
            var b = NewRT(128, 128);   // switch de preset : résolution de cascade différente
            try
            {
                Assert.IsFalse(OceanSurfaceModule.SameArrayDims(a, b),
                    "résolutions différentes (fenêtre d'un frame au switch de preset) → incohérent → MV nuls.");
            }
            finally { FreeRT(a); FreeRT(b); }
        }

        [Test]
        public void SameArrayDims_SameDimensions_IsCoherent()
        {
            var a = NewRT(64, 64);
            var b = NewRT(64, 64);
            try
            {
                Assert.IsTrue(OceanSurfaceModule.SameArrayDims(a, b),
                    "mêmes dimensions/format → cohérent → MV valides.");
            }
            finally { FreeRT(a); FreeRT(b); }
        }

        [Test]
        public void SameArrayDims_CurrentNotRenderTexture_IsIncoherent()
        {
            var prev = NewRT(64, 64);
            var tex2D = new Texture2D(64, 64, TextureFormat.RGBAFloat, false);
            try
            {
                // Type inattendu côté current (pas une RenderTexture) → prudence → MV nuls.
                Assert.IsFalse(OceanSurfaceModule.SameArrayDims(tex2D, prev));
            }
            finally
            {
                FreeRT(prev);
                Object.DestroyImmediate(tex2D);
            }
        }

        // ---------------------------------------------------------------- helpers

        static RenderTexture NewRT(int w, int h)
        {
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBFloat, 0)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 1,
                enableRandomWrite = true,
                useMipMap = false
            };
            var rt = new RenderTexture(desc);
            rt.Create();
            return rt;
        }

        static void FreeRT(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
