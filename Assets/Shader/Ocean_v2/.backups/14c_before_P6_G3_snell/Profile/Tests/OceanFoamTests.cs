// OceanFoamTests.cs  (Ocean_v2 / P4)
// Smoke tests EditMode de la feature écume — invariants PURS, hors rendu (le rendu = gate visuel P4) :
//   1) Push des globaux écume via OceanGlobalCache = SET pur non cumulatif + RestoreAll neutre
//      (anti-bug n°1 — même vérification exécutable que l'absorption P3).
//   2) Dispatch sans compute / sans arrays = no-op sûr (aucune alloc, Current null).
//   3) Dispose idempotent (cycle de vie symétrique [ExecuteAlways], anti-fuite).
using NUnit.Framework;
using UnityEngine;

namespace Ombrage.OceanFeatures.Tests
{
    public sealed class OceanFoamTests
    {
        static readonly int ID_Foam    = Shader.PropertyToID("_OceanFoam");
        static readonly int ID_Extent  = Shader.PropertyToID("_OceanFoamExtent");
        static readonly int ID_Enabled = Shader.PropertyToID("_OceanFoamEnabled");

        [Test]
        public void FoamGlobals_Push_NonCumulative_AndRestore()
        {
            var cache = new OceanGlobalCache();
            try
            {
                cache.SetGlobalFloat(ID_Extent, 100f);
                cache.SetGlobalFloat(ID_Extent, 100f);   // push ×2 : SET pur → valeur identique, jamais cumulée
                Assert.That(Shader.GetGlobalFloat(ID_Extent), Is.EqualTo(100f).Within(1e-5f), "extent poussé");
                cache.SetGlobalFloat(ID_Enabled, 1f);
                cache.SetGlobalTexture(ID_Foam, Texture2D.blackTexture);

                cache.RestoreAll();                       // teardown → neutres (anti-bug n°1)
                Assert.That(Shader.GetGlobalFloat(ID_Extent), Is.EqualTo(0f).Within(1e-5f), "RestoreAll → extent neutre");
                Assert.That(Shader.GetGlobalFloat(ID_Enabled), Is.EqualTo(0f).Within(1e-5f), "RestoreAll → enabled neutre");
                Assert.That(Shader.GetGlobalTexture(ID_Foam), Is.Null, "RestoreAll → texture neutre");
            }
            finally { cache.RestoreAll(); }
        }

        [Test]
        public void Dispatch_WithoutComputeOrArrays_IsSafeNoOp()
        {
            var f = new OceanFoamFeature();
            // compute null → return AVANT toute allocation
            f.Dispatch(null, 512, null, null,
                       Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero,
                       0f, 100f, 0.7f, 0.03f, 1f);
            Assert.That(f.Current, Is.Null, "aucune carte allouée sans compute");
            f.Dispose();
        }

        [Test]
        public void Dispose_IsIdempotent_AndClearsCurrent()
        {
            var f = new OceanFoamFeature();
            f.Dispose();
            f.Dispose();   // double Dispose = sûr
            Assert.That(f.Current, Is.Null);
        }
    }
}
