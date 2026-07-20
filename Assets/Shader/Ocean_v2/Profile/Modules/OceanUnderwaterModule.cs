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
        // ── Visibilité (fog) & lumière, pilotées par la PROFONDEUR de la caméra sous l'eau ──
        // viewDist(prof) = lerp(viewMaxDist, viewMinDist, smoothstep(viewReduceAtDepth, minViewAtDepth, prof))
        // light(prof)    = 1 − smoothstep(lightReduceAtDepth, minLightAtDepth, prof)
        [Header("Visibilité & lumière (selon la profondeur caméra)")]
        [Tooltip("Distance de vue aquatique MINIMALE (m) — atteinte en profondeur (à partir de minViewAtDepth).")]
        public OceanFloatParameter viewMinDist = new OceanFloatParameter(8f);

        [Tooltip("Distance de vue aquatique MAXIMALE (m) — près de la surface (jusqu'à viewReduceAtDepth).")]
        public OceanFloatParameter viewMaxDist = new OceanFloatParameter(60f);

        [Tooltip("Profondeur (m) à partir de laquelle la distance de vue COMMENCE à se réduire.")]
        public OceanFloatParameter viewReduceAtDepth = new OceanFloatParameter(4f);

        [Tooltip("Profondeur (m) à partir de laquelle la distance de vue est MINIMALE (viewMinDist).")]
        public OceanFloatParameter minViewAtDepth = new OceanFloatParameter(40f);

        [Tooltip("Profondeur (m) à partir de laquelle la luminosité COMMENCE à se réduire.")]
        public OceanFloatParameter lightReduceAtDepth = new OceanFloatParameter(8f);

        [Tooltip("Profondeur (m) à partir de laquelle il n'y a PLUS de lumière (noir).")]
        public OceanFloatParameter minLightAtDepth = new OceanFloatParameter(80f);

        [Header("Fenêtre de Snell")]
        [Tooltip("Demi-angle du cône de la fenêtre de Snell (°). Physique de l'eau ≈ 48.6°. Règle la taille de la fenêtre (la réfraction en sera dérivée).")]
        public OceanFloatParameter snellCriticalAngleDeg = new OceanFloatParameter(48.6f);

        [Tooltip("Distance max (m) de recherche de la scène émergée dans la fenêtre de Snell (marche screen-space). Trop court = objets lointains coupés ; trop long = coût de marche accru.")]
        public OceanFloatParameter snellMaxReach = new OceanFloatParameter(60f);

        const string kShaderName = "Hidden/Ocean/Underwater";
        const string kShaderPath = "Assets/Shader/Ocean_v2/Shaders/OceanUnderwater.shader";
        const string kPassName   = "Underwater";

        // _OceanUnderwaterEnabled (« module actif ») est poussé par OceanSurfaceModule (toujours actif).
        static readonly int P_ViewMinDist       = Shader.PropertyToID("_OceanViewMinDist");
        static readonly int P_ViewMaxDist       = Shader.PropertyToID("_OceanViewMaxDist");
        static readonly int P_ViewReduceAtDepth = Shader.PropertyToID("_OceanViewReduceAtDepth");
        static readonly int P_MinViewAtDepth    = Shader.PropertyToID("_OceanMinViewAtDepth");
        static readonly int P_LightReduceAtDepth= Shader.PropertyToID("_OceanLightReduceAtDepth");
        static readonly int P_MinLightAtDepth   = Shader.PropertyToID("_OceanMinLightAtDepth");
        static readonly int P_SnellCosThetaC    = Shader.PropertyToID("_OceanSnellCosThetaC");
        static readonly int P_SnellMaxReach     = Shader.PropertyToID("_OceanSnellMaxReach");

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

            // _OceanUnderwaterEnabled (« module actif ») est poussé par OceanSurfaceModule ; la SUBMERSION
            // est calculée IN-SHADER par-caméra (robuste Scene view/Play, vs l'ancien Camera.main C#). Ici on
            // ne pousse que les réglages, consommés par le shader de surface (Snell) + la passe sous-marine.
            ctx.globals.SetGlobalFloat(P_ViewMinDist,        viewMinDist.Effective);
            ctx.globals.SetGlobalFloat(P_ViewMaxDist,        viewMaxDist.Effective);
            ctx.globals.SetGlobalFloat(P_ViewReduceAtDepth,  viewReduceAtDepth.Effective);
            ctx.globals.SetGlobalFloat(P_MinViewAtDepth,     minViewAtDepth.Effective);
            ctx.globals.SetGlobalFloat(P_LightReduceAtDepth, lightReduceAtDepth.Effective);
            ctx.globals.SetGlobalFloat(P_MinLightAtDepth,    minLightAtDepth.Effective);
            ctx.globals.SetGlobalFloat(P_SnellCosThetaC, Mathf.Cos(snellCriticalAngleDeg.Effective * Mathf.Deg2Rad));
            ctx.globals.SetGlobalFloat(P_SnellMaxReach, snellMaxReach.Effective);
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

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            viewMinDist.value        = Mathf.Clamp(viewMinDist.value, 0.5f, 500f);
            viewMaxDist.value        = Mathf.Clamp(viewMaxDist.value, 1f, 1000f);
            viewReduceAtDepth.value  = Mathf.Max(viewReduceAtDepth.value, 0f);
            minViewAtDepth.value     = Mathf.Max(minViewAtDepth.value, viewReduceAtDepth.value + 0.1f);
            lightReduceAtDepth.value = Mathf.Max(lightReduceAtDepth.value, 0f);
            minLightAtDepth.value    = Mathf.Max(minLightAtDepth.value, lightReduceAtDepth.value + 0.1f);
            snellCriticalAngleDeg.value = Mathf.Clamp(snellCriticalAngleDeg.value, 35f, 65f);
            snellMaxReach.value         = Mathf.Clamp(snellMaxReach.value, 10f, 300f);
        }
#endif
    }
}
