// OceanUnderwaterModule.cs  (Ocean_v2 / P6)
// Module SOUS-MARIN (Q3.1/Q9.1) — vue immergée reconstruite par un CustomPass HDRP séparé
// (post-GBuffer, injection BeforePostProcess). Étages :
//   G2 (ce commit) : ABSORPTION Beer-Lambert de la colonne d'eau, σ PARTAGÉ (_WaterAbsorption, P3/Q6.1).
//   G3 : fenêtre de Snell (θc≈48.6°) — à venir dans le shader du pass.
//   G4 : fog volumétrique + god-rays = VOLUMETRICS HDRP natifs pilotés par σ.
//   G5 : éclairage sous-marin = modulation NON destructive (anti-bug n°1).
//
// Le gating immergé (§1.3) : le pass est ACTIF (via un global) uniquement quand la caméra principale
// est SOUS le niveau d'eau. Aucune mutation destructive d'état partagé (les god-rays/soleil = G5).
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Underwater")]
    public class OceanUnderwaterModule : OceanFeatureModule
    {
        [Header("Sous-marin (P6 — CustomPass BeforePostProcess)")]
        [Tooltip("Densité artistique de l'absorption immergée (× la profondeur perçue par la vue). 1 = physique.")]
        [Range(0.1f, 4f)] public float underwaterDensity = 1f;

        const string kShaderName = "Hidden/Ocean/Underwater";
        const string kShaderPath = "Assets/Shader/Ocean_v2/Shaders/OceanUnderwater.shader";
        const string kPassName   = "Underwater";

        static readonly int P_UnderwaterEnabled = Shader.PropertyToID("_OceanUnderwaterEnabled");
        static readonly int P_UnderwaterDist    = Shader.PropertyToID("_OceanUnderwaterDistScale");

        sealed class Runtime
        {
            public GameObject go;
            public CustomPassVolume volume;
            public FullScreenCustomPass pass;
            public Material material;
        }

        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            var rt = new Runtime();
            EnsurePass(ctx, rt);
            ctx.SetRuntime(this, rt);
        }

        public override void OnModuleDisable(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt != null)
            {
                if (rt.go != null) DestroyObj(rt.go);
                if (rt.material != null) DestroyObj(rt.material);
            }
            ctx.SetRuntime(this, null);
            // Les globaux (_OceanUnderwater*) sont restaurés neutres par OceanSystem.Teardown (anti-bug n°1).
        }

        public override void Apply(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt == null) { OnModuleEnable(ctx); rt = ctx.GetRuntime(this) as Runtime; }
            if (rt == null) return;
            EnsurePass(ctx, rt);

            float waterY = ctx.system != null ? ctx.system.transform.position.y : 0f;
            bool submerged = PrimaryCameraSubmerged(waterY);

            // GATING : le pass ne fait effet qu'en immersion (le CustomPass tourne toujours mais court-circuite
            // via _OceanUnderwaterEnabled=0 → coût négligeable émergé). Push SET pur (anti-bug n°1).
            ctx.globals.SetGlobalFloat(P_UnderwaterEnabled, submerged ? 1f : 0f);
            ctx.globals.SetGlobalFloat(P_UnderwaterDist, underwaterDensity);
        }

        void EnsurePass(OceanApplyContext ctx, Runtime rt)
        {
            if (rt.go != null && rt.pass != null) return;

            if (rt.material == null)
            {
                var sh = Shader.Find(kShaderName);
#if UNITY_EDITOR
                if (sh == null) sh = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(kShaderPath);
#endif
                if (sh == null) { Debug.LogWarning("[Ocean P6] Shader '" + kShaderName + "' introuvable — sous-marin inactif."); return; }
                rt.material = new Material(sh) { name = "OceanUnderwater (auto)", hideFlags = HideFlags.DontSave };
            }

            if (rt.go == null)
            {
                rt.go = new GameObject("OceanUnderwater (runtime)") { hideFlags = HideFlags.HideAndDontSave };
                if (ctx.system != null) rt.go.transform.SetParent(ctx.system.transform, worldPositionStays: false);
                rt.volume = rt.go.AddComponent<CustomPassVolume>();
                rt.volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
                rt.volume.isGlobal = true;

                rt.pass = new FullScreenCustomPass
                {
                    name = "OceanUnderwater",
                    fullscreenPassMaterial = rt.material,
                    materialPassName = kPassName,
                    fetchColorBuffer = true   // le shader échantillonne la couleur caméra
                };
                rt.volume.customPasses.Add(rt.pass);
            }
        }

        static bool PrimaryCameraSubmerged(float waterY)
        {
            var cam = Camera.main;
            return cam != null && cam.transform.position.y < waterY;
        }

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
