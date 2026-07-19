// OceanAbsorptionTests.cs  (Ocean_v2 / P3)
// Smoke tests EditMode du module absorption — invariants PURS, hors rendu (le rendu = gate visuel P3) :
//   1) Interpolation par segments : les 3 ancres sont restituées EXACTEMENT à t = 0 / kAnchorII / 1.
//   2) Linéarité intra-segment (t = 0.25 → mi-chemin Ia↔II).
//   3) Clamp du master hors [0..1].
//   4) Push : UN SEUL global _WaterAbsorption (+ _OceanAbsorptionDepth), via OceanGlobalCache =
//      SET PUR NON CUMULATIF (Apply ×2 → valeur identique, jamais doublée) et RESTAURABLE
//      (RestoreAll → neutre). C'est la vérification exécutable du critère P3 « push vérifié non
//      cumulatif » (anti-bug n°1), en complément de la revue de code.
using NUnit.Framework;
using UnityEngine;

namespace Ombrage.OceanFeatures.Tests
{
    public sealed class OceanAbsorptionTests
    {
        // Valeurs SYNTHÉTIQUES (kII/kIII ≠ assets réels WaterAbsorption_II/III recalibrés 2026-07-06 :
        // II=(0.45,0.09,0.15), III=(0.55,0.20,1.10)) — intentionnellement distinctes : ces tests
        // vérifient la LOGIQUE d'EvaluateSigma (interpolation par segments, clamp, push non-cumulatif),
        // pas la couleur réelle des ancres (rendu = gate visuel (d)). Ne pas les prendre pour référence.
        static readonly Vector3 kIa  = new Vector3(0.36f, 0.041f, 0.028f);
        static readonly Vector3 kII  = new Vector3(0.42f, 0.065f, 0.070f);
        static readonly Vector3 kIII = new Vector3(0.50f, 0.110f, 0.200f);
        const float kEps = 1e-5f;

        static void AssertVec3(Vector3 expected, Vector3 actual, string msg)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(kEps), msg + " (x)");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(kEps), msg + " (y)");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(kEps), msg + " (z)");
        }

        [Test]
        public void EvaluateSigma_AtAnchors_ReturnsAnchorValues()
        {
            AssertVec3(kIa,  OceanAbsorptionModule.EvaluateSigma(kIa, kII, kIII, 0f), "t=0 doit rendre Ia");
            AssertVec3(kII,  OceanAbsorptionModule.EvaluateSigma(kIa, kII, kIII, OceanAbsorptionModule.kAnchorII), "t=ancre II doit rendre II");
            AssertVec3(kIII, OceanAbsorptionModule.EvaluateSigma(kIa, kII, kIII, 1f), "t=1 doit rendre III");
        }

        [Test]
        public void EvaluateSigma_MidSegment_IsLinear()
        {
            float tMid = OceanAbsorptionModule.kAnchorII * 0.5f;   // milieu du segment Ia→II
            AssertVec3(Vector3.Lerp(kIa, kII, 0.5f),
                       OceanAbsorptionModule.EvaluateSigma(kIa, kII, kIII, tMid),
                       "milieu du segment Ia→II = lerp 0.5");
        }

        [Test]
        public void EvaluateSigma_ClampsOutOfRange()
        {
            AssertVec3(kIa,  OceanAbsorptionModule.EvaluateSigma(kIa, kII, kIII, -5f), "t<0 clampé sur Ia");
            AssertVec3(kIII, OceanAbsorptionModule.EvaluateSigma(kIa, kII, kIII, 5f),  "t>1 clampé sur III");
        }

        [Test]
        public void Apply_PushesSingleGlobal_NonCumulative_AndRestores()
        {
            WaterAbsorptionProfile ia = null, ii = null, iii = null;
            OceanAbsorptionModule module = null;
            var cache = new OceanGlobalCache();
            try
            {
                ia  = MakeProfile(kIa);
                ii  = MakeProfile(kII);
                iii = MakeProfile(kIII);

                module = ScriptableObject.CreateInstance<OceanAbsorptionModule>();
                module.anchorIa = ia; module.anchorII = ii; module.anchorIII = iii;
                // Override activé pour que la valeur saisie prime sur le défaut (sinon .Effective = défaut).
                module.waterType.overridden = true;    module.waterType.value = 0f;      // → σ = Ia exactement
                module.colorBuildup.overridden = true; module.colorBuildup.value = 12.5f;

                var ctx = new OceanApplyContext { globals = cache };

                module.Apply(ctx);
                Vector4 v1 = Shader.GetGlobalVector("_WaterAbsorption");
                module.Apply(ctx);                 // 2ᵉ Apply : SET pur → valeur IDENTIQUE, jamais cumulée
                Vector4 v2 = Shader.GetGlobalVector("_WaterAbsorption");

                AssertVec3(kIa, new Vector3(v1.x, v1.y, v1.z), "push initial = σ(Ia)");
                AssertVec3(new Vector3(v1.x, v1.y, v1.z), new Vector3(v2.x, v2.y, v2.z),
                           "Apply ×2 = même valeur (non cumulatif)");
                Assert.That(Shader.GetGlobalFloat("_OceanAbsorptionDepth"), Is.EqualTo(12.5f).Within(kEps),
                            "colorBuildup poussé");

                cache.RestoreAll();                // teardown → globals neutres (anti-bug n°1)
                Vector4 v3 = Shader.GetGlobalVector("_WaterAbsorption");
                AssertVec3(Vector3.zero, new Vector3(v3.x, v3.y, v3.z), "RestoreAll → σ neutre (0)");
                Assert.That(Shader.GetGlobalFloat("_OceanAbsorptionDepth"), Is.EqualTo(0f).Within(kEps),
                            "RestoreAll → profondeur neutre (0)");
            }
            finally
            {
                cache.RestoreAll();                // hygiène : ne jamais fuir des globaux entre tests
                if (module != null) Object.DestroyImmediate(module);
                if (ia != null) Object.DestroyImmediate(ia);
                if (ii != null) Object.DestroyImmediate(ii);
                if (iii != null) Object.DestroyImmediate(iii);
            }
        }

        static WaterAbsorptionProfile MakeProfile(Vector3 sigma)
        {
            var p = ScriptableObject.CreateInstance<WaterAbsorptionProfile>();
            p.sigmaR = sigma.x; p.sigmaG = sigma.y; p.sigmaB = sigma.z;
            return p;
        }
    }
}
