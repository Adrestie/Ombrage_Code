// OceanVolumetricsModule.cs  (Ocean_v2 / P6)
// Module VOLUMÉTRIQUES sous-marins (G4) — fog volumétrique HDRP NATIF, injecté via un Volume DÉDIÉ géré
// runtime (D1a), ACTIF uniquement en immersion (D3), NON destructif : on ne touche JAMAIS aux réglages de
// fog de la scène — c'est notre propre Volume + VolumeProfile runtime, détruits au teardown (anti-bug n°1).
//
// Réconciliation avec G2 (D2 : fog = in-scattering/glow, G2 = extinction) : le fog apporte le « glow »
// bleu-vert diffus (single-scattering) que l'absorption pure de G2 n'a pas ; l'EXTINCTION reste portée par
// G2 (σ unique, Q6.1). Le meanFreePath est volontairement LARGE (extinction propre du fog faible) pour ne
// pas ré-éteindre — calibrage fin à la validation. Les god-rays = G4.b (contribution volumétrique du soleil).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Volumetrics")]
    public class OceanVolumetricsModule : OceanFeatureModule
    {
        [Header("Volumétriques sous-marins (P6 / G4.a — fog volumétrique HDRP)")]
        [Tooltip("Couleur du glow diffus sous-marin (single-scattering albedo du fog). C'est l'IN-SCATTERING ; l'extinction reste portée par G2.")]
        public Color fogGlowColor = new Color(0.10f, 0.45f, 0.55f, 1f);

        [Tooltip("Densité du fog volumétrique = distance moyenne libre (m). PLUS GRAND = fog PLUS LÉGER. Large par défaut pour ne pas ré-éteindre G2.")]
        [Range(5f, 200f)] public float fogMeanFreePath = 60f;

        [Tooltip("Portée (m) sur laquelle le fog volumétrique est calculé devant la caméra.")]
        [Range(16f, 256f)] public float fogDepthExtent = 96f;

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
                if (rt.go != null) DestroyObj(rt.go);
                if (rt.profile != null) DestroyObj(rt.profile);
            }
            ctx.SetRuntime(this, null);
            // Aucun global poussé, aucun réglage de scène modifié → rien à restaurer (anti-bug n°1).
        }

        public override void Apply(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt == null) { OnModuleEnable(ctx); rt = ctx.GetRuntime(this) as Runtime; }
            if (rt == null || rt.fog == null) return;
            EnsureVolume(rt);

            float waterY = ctx.system != null ? ctx.system.transform.position.y : 0f;
            bool submerged = PrimaryCameraSubmerged(waterY);

            // GATING immersion (D3) : le Volume ne contribue QUE sous l'eau ; émergé, on le désactive →
            // le fog de la scène reprend la main (aucune écriture destructive, anti-bug n°1).
            rt.volume.enabled = submerged;
            if (!submerged) return;

            // Fog volumétrique HDRP piloté (D1a / D2). baseHeight = niveau d'eau → densité PLEINE sous
            // l'eau (constante en dessous), s'estompe juste au-dessus (émergé = Volume off de toute façon).
            SetBool (rt.fog.enabled,            true);
            SetBool (rt.fog.enableVolumetricFog, true);
            SetColor(rt.fog.albedo,             fogGlowColor);
            SetFloat(rt.fog.meanFreePath,       fogMeanFreePath);
            SetFloat(rt.fog.baseHeight,         waterY);
            SetFloat(rt.fog.maximumHeight,      waterY + 2f);
            SetFloat(rt.fog.depthExtent,        fogDepthExtent);
            SetFloat(rt.fog.anisotropy,         0f);   // isotrope en G4.a ; forward-scatter (god-rays) = G4.b
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

#endif
    }
}
