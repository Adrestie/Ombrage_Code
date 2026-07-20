// OceanVolumetricsModule.cs  (Ocean_v2)
// Module VOLUMÉTRIQUES sous-marins — MOITIÉ FOG de l'hybride G4 (cf. OCEAN_DECISIONS.md §Amendements A2).
//   - FOG volumétrique = HDRP NATIF : Volume runtime dédié + override Fog, in-scattering/glow piloté par σ,
//     ACTIF uniquement en immersion, NON destructif (notre propre Volume + VolumeProfile runtime, détruits
//     au teardown ; on ne touche JAMAIS au fog de la scène — anti-bug n°1).
//   - GOD-RAYS = passe CUSTOM additive pilotée par la courbure FFT (portage V1) → gérée SÉPARÉMENT (G4.2),
//     PAS ici. Ce module ne touche donc PLUS au soleil de la scène (l'ancien cookie a été retiré).
//
// Réconciliation avec l'absorption (fog = in-scattering/glow, absorption = extinction) : le fog apporte le
// glow bleu-vert diffus (single-scattering) que l'absorption pure n'a pas ; l'EXTINCTION reste portée par
// l'absorption (σ unique, CustomPass G2). meanFreePath LARGE par défaut (extinction propre du fog faible)
// pour ne pas ré-éteindre — calibrage fin à la validation.
//
// LIMITE ASSUMÉE (A2) : un Volume HDRP est GLOBAL → le gating immersion est piloté par la caméra de JEU
// (Camera.main) ; en Scene view avec caméras de part et d'autre de l'eau, le fog peut être incohérent. Les
// god-rays custom (G4.2), eux, sont per-caméra exacts.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Volumetrics")]
    public class OceanVolumetricsModule : OceanFeatureModule
    {
        // Valeurs à OVERRIDE (niveau 2, cf. module Reflection). Défaut décoché = ces valeurs ; cocher
        // permet de saisir autre chose. Clamp appliqué sur .value en OnValidate.
        [Header("Fog volumétrique sous-marin (HDRP natif)")]
        [Tooltip("Couleur du glow diffus sous-marin (single-scattering albedo du fog). C'est l'IN-SCATTERING ; l'extinction reste portée par l'absorption (σ partagé).")]
        public OceanColorParameter fogGlowColor = new OceanColorParameter(new Color(0.10f, 0.45f, 0.55f, 1f));

        [Tooltip("Densité du fog volumétrique = distance moyenne libre (m). PLUS GRAND = fog PLUS LÉGER. Large par défaut pour ne pas ré-éteindre l'absorption.")]
        public OceanFloatParameter fogMeanFreePath = new OceanFloatParameter(60f);

        [Tooltip("Portée (m) sur laquelle le fog volumétrique est calculé devant la caméra.")]
        public OceanFloatParameter fogDepthExtent = new OceanFloatParameter(96f);

        sealed class Runtime
        {
            public GameObject go;
            public Volume volume;
            public VolumeProfile profile;
            public Fog fog;
        }

        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            var rt = new Runtime();
            EnsureVolume(rt);
            ctx.SetRuntime(this, rt);
        }

        public override void OnModuleDisable(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt != null)
            {
                // Volume + VolumeProfile runtime uniquement (le module ne module plus aucun objet de scène).
                if (rt.go != null) DestroyObj(rt.go);
                if (rt.profile != null) DestroyObj(rt.profile);
            }
            ctx.SetRuntime(this, null);
        }

        public override void Apply(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt == null) { OnModuleEnable(ctx); rt = ctx.GetRuntime(this) as Runtime; }
            if (rt == null || rt.fog == null) return;
            EnsureVolume(rt);

            float waterY = ctx.system != null ? ctx.system.transform.position.y : 0f;
            bool submerged = PrimaryCameraSubmerged(waterY);

            // GATING immersion : le Volume ne contribue QUE sous l'eau ; émergé, on le désactive →
            // le fog de la scène reprend la main (aucune écriture destructive, anti-bug n°1).
            rt.volume.enabled = submerged;
            if (!submerged) return;

            // Fog volumétrique HDRP piloté. baseHeight = niveau d'eau → densité PLEINE sous
            // l'eau (constante en dessous), s'estompe juste au-dessus (émergé = Volume off de toute façon).
            SetBool (rt.fog.enabled,             true);
            SetBool (rt.fog.enableVolumetricFog, true);
            SetColor(rt.fog.albedo,              fogGlowColor.Effective);
            SetFloat(rt.fog.meanFreePath,        fogMeanFreePath.Effective);
            SetFloat(rt.fog.baseHeight,          waterY);
            SetFloat(rt.fog.maximumHeight,       waterY + 2f);
            SetFloat(rt.fog.depthExtent,         fogDepthExtent.Effective);
            SetFloat(rt.fog.anisotropy,          0.6f);  // forward-scatter → renforce le glow vers le soleil
        }

        void EnsureVolume(Runtime rt)
        {
            if (rt.go != null && rt.fog != null) return;

            if (rt.profile == null)
            {
                rt.profile = ScriptableObject.CreateInstance<VolumeProfile>();
                rt.profile.hideFlags = HideFlags.HideAndDontSave;
                rt.fog = rt.profile.Add<Fog>(overrides: true);
            }

            if (rt.go == null)
            {
                // ANTI-ORPHELIN (anti-bug n°1) : nos objets HideAndDontSave SURVIVENT au domain reload,
                // mais le Runtime (non sérialisé) perd sa réf → l'ancien Volume devient orphelin, invisible
                // ET toujours GLOBAL (override Fog actif) = fog fantôme partout. On balaie donc tout Volume
                // océan orphelin AVANT d'en créer un neuf, pour empêcher l'accumulation à chaque recompile.
                DestroyOrphanVolumes();

                rt.go = new GameObject(kRuntimeName) { hideFlags = HideFlags.HideAndDontSave };
                rt.volume = rt.go.AddComponent<Volume>();
                rt.volume.isGlobal = true;
                rt.volume.priority = 100f;    // au-dessus des volumes de scène → gagne en immersion
                rt.volume.profile  = rt.profile;
                rt.volume.enabled  = false;   // activé par Apply selon l'immersion
            }
        }

        static bool PrimaryCameraSubmerged(float waterY)
        {
            var cam = Camera.main;
            return cam != null && cam.transform.position.y < waterY;
        }

        // MinFloatParameter / ClampedFloatParameter dérivent de FloatParameter → le setter surchargé
        // (clamp) s'applique via la référence de base. overrideState=true pour que l'override prenne.
        static void SetBool (BoolParameter  p, bool  v) { p.overrideState = true; p.value = v; }
        static void SetFloat(FloatParameter p, float v) { p.overrideState = true; p.value = v; }
        static void SetColor(ColorParameter p, Color v) { p.overrideState = true; p.value = v; }

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        const string kRuntimeName = "OceanVolumetrics (runtime)";

        // Détruit tout GameObject de Volume runtime océan qui traîne dans une scène chargée (orphelins de
        // domain reload / de suppression de module). FindObjectsOfTypeAll voit AUSSI les objets cachés
        // (HideAndDontSave). On ignore les assets/prefabs (scene invalide). Sûr : le module recrée un
        // Volume propre au besoin au prochain Apply.
        static void DestroyOrphanVolumes()
        {
            var all = Resources.FindObjectsOfTypeAll<Volume>();
            foreach (var v in all)
            {
                if (v == null || v.gameObject == null) continue;
                if (v.gameObject.name != kRuntimeName) continue;
                if (!v.gameObject.scene.IsValid()) continue;   // ignore assets/prefabs hors scène
                DestroyObj(v.gameObject);
            }
        }

#if UNITY_EDITOR
        // Filet de sécurité manuel : si un module a été retiré sans teardown (l'orphelin n'est alors balayé
        // par aucun Apply), ce menu nettoie les Volumes de fog fantômes qui corrompent le rendu.
        [UnityEditor.MenuItem("Ombrage/Ocean/Nettoyer les Volumes océan orphelins")]
        static void CleanupOrphanVolumesMenu()
        {
            var all = Resources.FindObjectsOfTypeAll<Volume>();
            int n = 0;
            foreach (var v in all)
            {
                if (v == null || v.gameObject == null) continue;
                if (v.gameObject.name != kRuntimeName) continue;
                if (!v.gameObject.scene.IsValid()) continue;
                Object.DestroyImmediate(v.gameObject);
                n++;
            }
            Debug.Log($"[Ocean] Nettoyage : {n} Volume(s) océan orphelin(s) détruit(s).");
        }

        void OnValidate()
        {
            fogMeanFreePath.value = Mathf.Clamp(fogMeanFreePath.value, 5f, 200f);
            fogDepthExtent.value  = Mathf.Clamp(fogDepthExtent.value, 16f, 256f);
        }
#endif
    }
}
