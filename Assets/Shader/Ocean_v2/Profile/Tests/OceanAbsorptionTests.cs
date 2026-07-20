// OceanAbsorptionTests.cs  (Ocean_v2)
// Smoke tests EditMode du module absorption — invariants PURS, hors rendu (rendu = validation visuelle).
// Modèle art-directed A3 : waterColor (look) + absorptionColor (ordre) → σ NORMALISÉ (la magnitude/distance
// de vue est pilotée par la profondeur côté Underwater).
//   1) Défaut physique : le rouge est absorbé en premier pour une eau bleue (σ_r ≥ σ_g ≥ σ_b).
//   2) σ normalisé : le canal dominant vaut 1.
//   3) Ordre art-directed : override → le canal le plus vif de absorptionColor devient le σ dominant.
//   4) Push : _WaterAbsorption + _OceanScatterColor + _OceanAbsorptionDepth via OceanGlobalCache =
//      SET PUR NON CUMULATIF (Apply ×2 = identique) et RESTAURABLE (RestoreAll → neutre). Anti-bug n°1.
using NUnit.Framework;
using UnityEngine;

namespace Ombrage.OceanFeatures.Tests
{
    public sealed class OceanAbsorptionTests
    {
        const float kEps = 1e-4f;

        static void AssertVec3(Vector3 expected, Vector3 actual, string msg)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(kEps), msg + " (x)");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(kEps), msg + " (y)");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(kEps), msg + " (z)");
        }

        [Test]
        public void DefaultSpectrum_BlueWater_AbsorbsRedFirst()
        {
            // Eau bleue (peu de rouge affiché) → absorbe le rouge en premier → σ_r le plus grand.
            Vector3 s = OceanAbsorptionModule.DeriveSigma(new Color(0.06f, 0.30f, 0.42f), false, Color.white);
            Assert.That(s.x, Is.GreaterThanOrEqualTo(s.y), "σ_r ≥ σ_g (rouge absorbé avant le vert)");
            Assert.That(s.y, Is.GreaterThanOrEqualTo(s.z), "σ_g ≥ σ_b (vert absorbé avant le bleu)");
        }

        [Test]
        public void DeriveSigma_IsNormalized()
        {
            // σ est le spectre NORMALISÉ (canal dominant = 1) ; la magnitude (distance) vient d'Underwater.
            Vector3 s = OceanAbsorptionModule.DeriveSigma(new Color(0.06f, 0.30f, 0.42f), false, Color.white);
            float maxCh = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            Assert.That(maxCh, Is.EqualTo(1f).Within(kEps), "canal dominant σ = 1 (normalisé)");
        }

        [Test]
        public void AbsorptionOrder_Override_TwistsOrder()
        {
            // Override vert → le vert devient le canal absorbé en premier (σ_g dominant).
            Vector3 s = OceanAbsorptionModule.DeriveSigma(new Color(0.06f, 0.30f, 0.42f), true, new Color(0.2f, 1f, 0.2f));
            Assert.That(s.y, Is.GreaterThan(s.x), "σ_g > σ_r (vert absorbé avant le rouge)");
            Assert.That(s.y, Is.GreaterThan(s.z), "σ_g > σ_b (vert absorbé avant le bleu)");
        }

        [Test]
        public void LookFromSigma_IsNormalized()
        {
            // La conversion σ→couleur (presets) est normalisée au canal dominant (∈ [0..1], max = 1).
            Color c = OceanAbsorptionModule.LookFromSigma(new Vector3(0.36f, 0.041f, 0.028f));  // Ia
            float maxCh = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            Assert.That(maxCh, Is.EqualTo(1f).Within(kEps), "look normalisé (canal dominant = 1)");
            Assert.That(c.b, Is.GreaterThan(c.r), "Ia (rouge absorbé) → bleu dominant");
        }

        [Test]
        public void Apply_PushesColorGlobals_NonCumulative_AndRestores()
        {
            OceanAbsorptionModule module = null;
            var cache = new OceanGlobalCache();
            try
            {
                module = ScriptableObject.CreateInstance<OceanAbsorptionModule>();
                module.waterColor = new Color(0.10f, 0.30f, 0.50f, 1f);
                module.colorBuildup.overridden = true; module.colorBuildup.value = 12.5f;
                module.absorptionColor.overridden = false;

                Vector3 expSigma = OceanAbsorptionModule.DeriveSigma(module.waterColor, false, module.absorptionColor.value);
                var ctx = new OceanApplyContext { globals = cache };

                module.Apply(ctx);
                Vector4 sig1  = Shader.GetGlobalVector("_WaterAbsorption");
                Vector4 scat1 = Shader.GetGlobalVector("_OceanScatterColor");
                module.Apply(ctx);                 // 2ᵉ Apply : SET pur → valeur IDENTIQUE
                Vector4 sig2  = Shader.GetGlobalVector("_WaterAbsorption");

                AssertVec3(expSigma, new Vector3(sig1.x, sig1.y, sig1.z), "σ poussé = DeriveSigma");
                AssertVec3(new Vector3(0.10f, 0.30f, 0.50f), new Vector3(scat1.x, scat1.y, scat1.z), "scatter = waterColor");
                AssertVec3(new Vector3(sig1.x, sig1.y, sig1.z), new Vector3(sig2.x, sig2.y, sig2.z), "Apply ×2 = même σ (non cumulatif)");
                Assert.That(Shader.GetGlobalFloat("_OceanAbsorptionDepth"), Is.EqualTo(12.5f).Within(kEps), "colorBuildup poussé");

                cache.RestoreAll();                // teardown → globals neutres (anti-bug n°1)
                Vector4 sig3  = Shader.GetGlobalVector("_WaterAbsorption");
                Vector4 scat3 = Shader.GetGlobalVector("_OceanScatterColor");
                AssertVec3(Vector3.zero, new Vector3(sig3.x, sig3.y, sig3.z), "RestoreAll → σ neutre (0)");
                AssertVec3(Vector3.zero, new Vector3(scat3.x, scat3.y, scat3.z), "RestoreAll → scatter neutre (0)");
            }
            finally
            {
                cache.RestoreAll();
                if (module != null) Object.DestroyImmediate(module);
            }
        }
    }
}
