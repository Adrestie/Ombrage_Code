// OceanReflectionModule.cs  (Ocean_v2)
// Module RÉFLEXIONS : le CIEL est réfléchi automatiquement par HDRP (surface Lit deferred) ;
// ce module ajoute une **Planar Reflection Probe HDRP built-in** au niveau d'eau pour les réflexions
// d'objets locaux (terrain, bateaux…). SSR reporté ultérieurement ; sonde sous-marine différée.
//
// GATING IMMERGÉ (prérequis du modèle budget) : la sonde planar (coûteuse = re-rendu de scène)
// est ÉTEINTE quand la caméra principale passe SOUS le niveau d'eau — le monde émergé n'est alors
// plus visible (fenêtre de Snell). Sans ce gating, les deux états caméra se cumuleraient.
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
        // Valeurs à OVERRIDE (niveau 2). Défaut décoché = ces valeurs validées ;
        // cocher permet de saisir une valeur différente. Clamp appliqué sur .value en OnValidate.
        [Header("Planar Reflection Probe (HDRP built-in)")]
        [Tooltip("Active la Planar Reflection Probe (réflexions d'OBJETS locaux : terrain, bateaux). Le CIEL est réfléchi de toute façon par HDRP, indépendamment de ce réglage.")]
        public OceanBoolParameter planarEnabled = new OceanBoolParameter(true);

        [Tooltip("Demi-étendue XZ de la zone d'influence de la sonde (m) — doit couvrir la surface visible. (La résolution par preset qualité viendra plus tard.)")]
        public OceanFloatParameter influenceExtent = new OceanFloatParameter(200f);

        [Tooltip("Hauteur (m) de la zone d'influence, centrée au niveau d'eau. DOIT couvrir la plage verticale des vagues (crêtes + creux, ~2×_OceanMaxDisplacement) : sinon les fragments déplacés hors de la boîte ne reçoivent PAS la réflexion (creux/crêtes retombent sur le ciel). Trop grand = risque de contaminer des objets proches du plan d'eau.")]
        public OceanFloatParameter influenceHeight = new OceanFloatParameter(20f);

        [Tooltip("Distance de fondu (m) de l'influence vers les bords XZ (Blend Distance) : la réflexion s'atténue sur les derniers mètres de la boîte au lieu d'une COUPURE NETTE. 0 = coupure franche. Clampé à l'étendue.")]
        public OceanFloatParameter edgeFade = new OceanFloatParameter(40f);

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

            // Valeurs effectives (override ou défaut) — lues UNE fois, jamais .value directement.
            bool  planar = planarEnabled.Effective;
            float ext    = influenceExtent.Effective;
            float h      = influenceHeight.Effective;
            float fade   = edgeFade.Effective;

            Vector3 sysPos = ctx.system != null ? ctx.system.transform.position : Vector3.zero;
            float waterY = sysPos.y;

            // Plan miroir = plan d'eau horizontal (sonde au niveau d'eau, up = monde). La sonde SUIT la
            // caméra principale en XZ : la boîte d'influence étant de taille bornée (résolution/perf), la
            // centrer sur la caméra garde l'eau REGARDÉE toujours couverte, plutôt que de l'ancrer au centre
            // de l'océan (le lointain décrocherait dès qu'on s'éloigne). Repli sur le système si pas de caméra.
            var cam = Camera.main;
            Vector3 center = cam != null ? cam.transform.position : sysPos;

            // Rotation Y (yaw) suivie de la caméra : la boîte présente toujours une FACE devant → on ne voit
            // plus le COIN de la boîte apparaître quand on tourne. SÛR pour le miroir : une planar probe se
            // réfléchit selon son UP (= +Y) ; tourner autour de Y garde up = +Y → plan miroir horizontal intact.
            Quaternion rot = Quaternion.identity;
            if (cam != null)
            {
                Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-6f) rot = Quaternion.LookRotation(fwd, Vector3.up);
            }
            rt.go.transform.SetPositionAndRotation(new Vector3(center.x, waterY, center.z), rot);
            // Boîte centrée au niveau d'eau : sa HAUTEUR doit couvrir crêtes+creux, sinon les fragments
            // de vagues déplacés hors de la boîte ne reçoivent pas la réflexion (retombent sur le ciel).
            rt.probe.influenceVolume.boxSize = new Vector3(ext * 2f, h, ext * 2f);

            // Fondu progressif vers les bords XZ (Blend Distance) → plus de coupure nette au bord de la
            // boîte. 0 en Y : on ne fond PAS dans la bande de vagues (réflexion pleine sur crêtes/creux).
            float blendDist = Mathf.Min(fade, ext);
            var blend = new Vector3(blendDist, 0f, blendDist);
            rt.probe.influenceVolume.boxBlendDistancePositive = blend;
            rt.probe.influenceVolume.boxBlendDistanceNegative = blend;

            // GATING IMMERGÉ : caméra principale sous l'eau → sonde OFF (pas de re-rendu inutile).
            bool submerged = PrimaryCameraSubmerged(waterY);
            bool on = planar && !submerged;
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
        // fin par-vue (Scene vs Game) est un détail de rendu hors périmètre.
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
            influenceExtent.value = Mathf.Max(1f, influenceExtent.value);
            influenceHeight.value = Mathf.Max(1f, influenceHeight.value);
            edgeFade.value = Mathf.Max(0f, edgeFade.value);
        }
#endif
    }
}
