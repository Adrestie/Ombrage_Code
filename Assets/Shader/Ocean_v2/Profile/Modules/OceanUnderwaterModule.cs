// OceanUnderwaterModule.cs  (Ocean_v2)
// Module SOUS-MARIN — vue immergée. Répartition post-flip forward :
//   - FENÊTRE DE SNELL (θc≈48.6°) : rendue par le SHADER DE SURFACE (forward double-face), gated par
//     _OceanUnderwaterEnabled. Ce module ne fait que POUSSER l'angle (_OceanSnellCosThetaC) + l'interrupteur.
//   - COLONNE D'EAU sur la géométrie immergée (absorption Beer-Lambert, σ PARTAGÉ ; caustiques) : CustomPass
//     FullScreen BeforePostProcess (OceanUnderwater.shader), séparation surface/immergé GÉOMÉTRIQUE.
//   - fog volumétrique + god-rays = VOLUMETRICS HDRP natifs (ultérieur).
//
// Le gating immergé : effets ACTIFS (via global) uniquement quand la caméra principale est SOUS le niveau
// d'eau. Aucune mutation destructive d'état partagé (anti-bug n°1). PLUS de tag stencil (retiré au re-cadrage
// forward : la surface étant transparente, elle n'écrit plus le GBuffer — le Snell est passé côté surface).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Underwater")]
    public class OceanUnderwaterModule : OceanFeatureModule
    {
        // Valeurs à OVERRIDE (niveau 2, cf. Reflection). Décoché = défaut ; cocher = saisie. Clamp en OnValidate.
        [Header("Sous-marin (CustomPass BeforePostProcess)")]
        [Tooltip("Densité artistique de l'absorption immergée (× la profondeur perçue par la vue). 1 = physique.")]
        public OceanFloatParameter underwaterDensity = new OceanFloatParameter(1f);

        [Tooltip("Demi-angle du cône de la fenêtre de Snell (°). Physique de l'eau ≈ 48.6°. Règle la taille de la fenêtre (la réfraction en sera dérivée).")]
        public OceanFloatParameter snellCriticalAngleDeg = new OceanFloatParameter(48.6f);

        const string kShaderName = "Hidden/Ocean/Underwater";
        const string kShaderPath = "Assets/Shader/Ocean_v2/Shaders/OceanUnderwater.shader";
        const string kPassName   = "Underwater";

        static readonly int P_UnderwaterEnabled = Shader.PropertyToID("_OceanUnderwaterEnabled");
        static readonly int P_UnderwaterDist    = Shader.PropertyToID("_OceanUnderwaterDistScale");
        static readonly int P_SnellCosThetaC    = Shader.PropertyToID("_OceanSnellCosThetaC");

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
            // _OceanSnellCosThetaC + _OceanUnderwaterEnabled sont AUSSI consommés par le shader de surface
            // (fenêtre de Snell rendue là-bas). La passe FullScreen ne fait plus que la colonne d'eau immergée.
            ctx.globals.SetGlobalFloat(P_UnderwaterEnabled, submerged ? 1f : 0f);
            ctx.globals.SetGlobalFloat(P_UnderwaterDist, underwaterDensity.Effective);
            ctx.globals.SetGlobalFloat(P_SnellCosThetaC, Mathf.Cos(snellCriticalAngleDeg.Effective * Mathf.Deg2Rad));
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
                if (sh == null) { Debug.LogWarning("[Ocean] Shader '" + kShaderName + "' introuvable — sous-marin inactif."); return; }
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

#if UNITY_EDITOR
        void OnValidate()
        {
            underwaterDensity.value     = Mathf.Clamp(underwaterDensity.value, 0.1f, 4f);
            snellCriticalAngleDeg.value = Mathf.Clamp(snellCriticalAngleDeg.value, 35f, 65f);
        }
#endif
    }
}
