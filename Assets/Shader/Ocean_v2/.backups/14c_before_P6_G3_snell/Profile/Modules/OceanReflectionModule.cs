// OceanReflectionModule.cs  (Ocean_v2 / P5)
// Module RÉFLEXIONS (Q5.1) : le CIEL est réfléchi automatiquement par HDRP (surface Lit deferred) ;
// ce module ajoute une **Planar Reflection Probe HDRP built-in** au niveau d'eau pour les réflexions
// d'objets locaux (terrain, bateaux…). SSR reporté V1.5+ (Q5.1) ; sonde sous-marine différée (Q5.2).
//
// GATING IMMERGÉ (§1.3, prérequis du modèle budget) : la sonde planar (coûteuse = re-rendu de scène)
// est ÉTEINTE quand la caméra principale passe SOUS le niveau d'eau — le monde émergé n'est alors
// plus visible (fenêtre de Snell, P6). Sans ce gating, les deux états caméra se cumuleraient.
//
// Architecture (pattern surface/absorption) : ce SO est PUR DATA ; l'objet runtime (GameObject +
// PlanarReflectionProbe, non sérialisé) est détenu par OceanSystem via SetRuntime. Cycle de vie
// symétrique strict (anti-fuite [ExecuteAlways]).
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Rendering/Reflection")]
    public class OceanReflectionModule : OceanFeatureModule
    {
        [Header("Planar Reflection Probe (HDRP built-in, Q5.1)")]
        [Tooltip("Active la Planar Reflection Probe (réflexions d'OBJETS locaux : terrain, bateaux). Le CIEL est réfléchi de toute façon par HDRP, indépendamment de ce réglage.")]
        public bool planarEnabled = true;

        [Tooltip("Demi-étendue XZ de la zone d'influence de la sonde (m) — doit couvrir la surface visible. (La résolution par preset qualité viendra en P10.)")]
        [Min(1f)] public float influenceExtent = 200f;

        sealed class Runtime
        {
            public GameObject go;
            public PlanarReflectionProbe probe;
            public bool lastOn;
        }

        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            var rt = new Runtime();
            EnsureProbe(ctx, rt);
            ctx.SetRuntime(this, rt);
        }

        public override void OnModuleDisable(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt != null && rt.go != null) DestroyObj(rt.go);
            ctx.SetRuntime(this, null);
        }

        public override void Apply(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt == null) { OnModuleEnable(ctx); rt = ctx.GetRuntime(this) as Runtime; }
            if (rt == null) return;
            EnsureProbe(ctx, rt);
            if (rt.probe == null) return;

            Vector3 sysPos = ctx.system != null ? ctx.system.transform.position : Vector3.zero;
            float waterY = sysPos.y;

            // Plan miroir = plan d'eau horizontal : sonde au niveau d'eau, orientation neutre (up = monde).
            rt.go.transform.SetPositionAndRotation(new Vector3(sysPos.x, waterY, sysPos.z), Quaternion.identity);
            rt.probe.influenceVolume.boxSize = new Vector3(influenceExtent * 2f, 1f, influenceExtent * 2f);

            // GATING IMMERGÉ : caméra principale sous l'eau → sonde OFF (pas de re-rendu inutile).
            bool submerged = PrimaryCameraSubmerged(waterY);
            bool on = planarEnabled && !submerged;
            if (rt.probe.enabled != on) rt.probe.enabled = on;
            rt.lastOn = on;
        }

        void EnsureProbe(OceanApplyContext ctx, Runtime rt)
        {
            if (rt.go != null) return;
            rt.go = new GameObject("OceanPlanarReflection (runtime)") { hideFlags = HideFlags.HideAndDontSave };
            if (ctx.system != null) rt.go.transform.SetParent(ctx.system.transform, worldPositionStays: false);
            rt.probe = rt.go.AddComponent<PlanarReflectionProbe>();
            rt.probe.mode = ProbeSettings.Mode.Realtime;             // dynamique (vagues + objets)
            rt.probe.realtimeMode = ProbeSettings.RealtimeMode.EveryFrame;
        }

        // Caméra de RÉFÉRENCE = la caméra principale (le budget/model raisonne sur elle). Le gating
        // fin par-vue (Scene vs Game) est un détail de rendu hors périmètre P5.
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
            influenceExtent = Mathf.Max(1f, influenceExtent);
        }
#endif
    }
}
