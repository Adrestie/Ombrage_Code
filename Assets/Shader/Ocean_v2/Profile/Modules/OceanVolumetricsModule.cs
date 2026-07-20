// OceanVolumetricsModule.cs  (Ocean_v2)
// Module VOLUMÉTRIQUES sous-marins — MOITIÉ FOG de l'hybride G4 (cf. OCEAN_DECISIONS.md §Amendements A2).
//   - FOG volumétrique = HDRP NATIF : Volume runtime dédié + override Fog, in-scattering/glow LITÉ (réagit
//     aux lumières, remplit le volume, fogue les particules) que le custom ne sait pas faire. ACTIF
//     uniquement en immersion, NON destructif (notre propre Volume + VolumeProfile runtime, détruits au
//     teardown ; on ne touche JAMAIS au fog de la scène — anti-bug n°1).
//   - GOD-RAYS = passe CUSTOM additive pilotée par la courbure FFT (portage V1) → gérée SÉPARÉMENT (G4.2),
//     PAS ici. Ce module ne touche donc PLUS au soleil de la scène (l'ancien cookie a été retiré).
//
// RÉPARTITION DES RÔLES (résout la « bouillie 2 couleurs » du test) :
//   - EXTINCTION SPECTRALE (rouge éteint avant le bleu) = passe custom G2 (σ) — HDRP ne sait pas (extinction
//     MONOCHROME, un seul meanFreePath). C'est la décision cœur Q6.1, on la garde.
//   - IN-SCATTERING / GLOW LITÉ = ce fog HDRP, meanFreePath LARGE (extinction propre négligeable → ne
//     re-éteint pas) ; son ALBEDO (couleur du glow) = la couleur AFFICHÉE de l'eau `_OceanScatterColor`
//     (waterColor art-directed, poussé par OceanAbsorptionModule — source unique du look, amendement A3),
//     normalisée → glow cohérent avec le dessus. Aucun « fogGlowColor » libre (c'était la 2ᵉ couleur incohérente).
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
        // NB : PLUS de couleur de glow ici — l'albedo du fog est DÉRIVÉ de σ (source unique) au runtime.
        [Header("Fog volumétrique sous-marin (HDRP natif, glow lité)")]
        [Tooltip("Densité du fog volumétrique = distance moyenne libre (m). PLUS GRAND = fog PLUS LÉGER. LARGE par défaut : ce fog ne porte QUE le glow lité, l'extinction (spectrale) reste à l'absorption custom → ne pas descendre trop bas sous peine de re-éteindre en double.")]
        public OceanFloatParameter fogMeanFreePath = new OceanFloatParameter(60f);

        [Tooltip("Portée (m) sur laquelle le fog volumétrique est calculé devant la caméra.")]
        public OceanFloatParameter fogDepthExtent = new OceanFloatParameter(96f);

        // ── GOD-RAYS (rayons volumétriques custom, courbure FFT — consommés par la passe underwater) ──
        // COMPARAISON TEMPORAIRE : dropdown pour juger le LOOK. À retirer une fois le mode choisi.
        public enum GodRayLook { Net_Raymarch = 0, Doux = 1, Marque = 2 }

        [Header("God-rays")]
        [Tooltip("MODE (comparaison temporaire) : Net = raymarch tranché/lourd · Doux = faisceaux larges diffus (look cookie/HDRP) · Marqué = faisceaux contrastés medium.")]
        public GodRayLook godRayMode = GodRayLook.Net_Raymarch;

        [Tooltip("Force globale des rayons (0 = éteints). Curseur maître.")]
        public OceanFloatParameter godRayIntensity = new OceanFloatParameter(1.5f);

        [Tooltip("Teinte des rayons (idéalement cohérente avec la couleur d'eau).")]
        public OceanColorParameter godRayColor = new OceanColorParameter(new Color(0.15f, 0.55f, 0.5f, 1f));

        [Tooltip("Portée (m) du raymarch le long du rayon de vue : jusqu'où DEVANT la caméra les rayons sont dessinés (et coût).")]
        public OceanFloatParameter godRayMaxDist = new OceanFloatParameter(50f);

        [Tooltip("Netteté des faisceaux : 0 = glow diffus (larges/doux), 1 = faisceaux nets et contrastés.")]
        public OceanFloatParameter godRaySharpness = new OceanFloatParameter(0.6f);

        [Header("God-rays — Advanced")]
        [Tooltip("Échelle du voisinage de courbure (m) : règle l'épaisseur/finesse des faisceaux.")]
        public OceanFloatParameter godRayBeamScale = new OceanFloatParameter(0.5f);

        [Tooltip("Inclinaison : 0 = faisceaux verticaux, 1 = alignés sur le soleil (obliques).")]
        public OceanFloatParameter godRaySunFollow = new OceanFloatParameter(0.3f);

        [Tooltip("Fondu VERTICAL : haut = rayons seulement près de la surface, bas = plongent profond.")]
        public OceanFloatParameter godRayDepthFade = new OceanFloatParameter(0.15f);

        [Tooltip("Fondu le long du RAYON DE VUE : haut = rayons proches caméra seulement, bas = visibles plus loin.")]
        public OceanFloatParameter godRayExtinction = new OceanFloatParameter(0.06f);

        [Tooltip("Profondeur caméra d'APPARITION des rayons (évite qu'ils poppent pile à la surface).")]
        public OceanFloatParameter godRayFadeInDepth = new OceanFloatParameter(2f);

        [Tooltip("Nombre de pas de raymarch (perf). 16 souvent suffisant, 8 = cheap, 32 = plus net.")]
        public OceanFloatParameter godRaySteps = new OceanFloatParameter(16f);

        // L'albedo du glow = la couleur AFFICHÉE de l'eau (_OceanScatterColor, waterColor art-directed poussé
        // par OceanAbsorptionModule) normalisée → glow du fog cohérent avec la couleur du dessus, source unique.
        static readonly int ID_ScatterColor = Shader.PropertyToID("_OceanScatterColor");
        // God-rays (poussés inconditionnellement ; interrupteur _OceanGodRaysEnabled poussé par la surface).
        static readonly int ID_GRColor       = Shader.PropertyToID("_OceanGodRayColor");
        static readonly int ID_GRIntensity   = Shader.PropertyToID("_OceanGodRayIntensity");
        static readonly int ID_GRMaxDist     = Shader.PropertyToID("_OceanGodRayMaxDist");
        static readonly int ID_GRThreshLo    = Shader.PropertyToID("_OceanGodRayBeamThresholdLo");
        static readonly int ID_GRThreshHi    = Shader.PropertyToID("_OceanGodRayBeamThresholdHi");
        static readonly int ID_GRBeamScale   = Shader.PropertyToID("_OceanGodRayBeamScale");
        static readonly int ID_GRSunFollow   = Shader.PropertyToID("_OceanGodRaySunFollow");
        static readonly int ID_GRDepthFade   = Shader.PropertyToID("_OceanGodRayDepthFade");
        static readonly int ID_GRExtinction  = Shader.PropertyToID("_OceanGodRayExtinction");
        static readonly int ID_GRFadeInDepth = Shader.PropertyToID("_OceanGodRayFadeInDepth");
        static readonly int ID_GRSteps       = Shader.PropertyToID("_OceanGodRaySteps");
        static readonly int ID_GRMode        = Shader.PropertyToID("_OceanGodRayMode");   // comparaison temporaire

        sealed class Runtime
        {
            public GameObject go;
            public Volume volume;
            public VolumeProfile profile;
            public Fog fog;
            // God-rays : CustomPass scripté demi-résolution (sur le MÊME GameObject runtime).
            public CustomPassVolume grVolume;
            public OceanGodRayLowResPass grPass;
            public Material grMaterial;
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
                // Volume + VolumeProfile + matériau god-ray runtime (le CustomPassVolume part avec le GameObject).
                if (rt.grMaterial != null) DestroyObj(rt.grMaterial);
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

            // God-rays : poussés INCONDITIONNELLEMENT (valeurs globales) — l'effet est gaté par la submersion
            // in-shader + l'interrupteur _OceanGodRaysEnabled (surface), pas par le Camera.main du fog HDRP.
            PushGodRays(ctx);

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
            SetColor(rt.fog.albedo,              GlowAlbedoFromScatter());
            SetFloat(rt.fog.meanFreePath,        fogMeanFreePath.Effective);
            SetFloat(rt.fog.baseHeight,          waterY);
            SetFloat(rt.fog.maximumHeight,       waterY + 2f);
            SetFloat(rt.fog.depthExtent,         fogDepthExtent.Effective);
            SetFloat(rt.fog.anisotropy,          0.6f);  // forward-scatter → renforce le glow vers le soleil
        }

        // Pousse les globals god-rays. Les seuils de courbure lo/hi sont DÉRIVÉS de la netteté (sharpness)
        // — même mapping que V1 : lo = lerp(0.35,0.60,s)·0.3 ; hi = (lo_base + lerp(0.25,0.12,s))·3.0.
        void PushGodRays(OceanApplyContext ctx)
        {
            float sharp  = Mathf.Clamp01(godRaySharpness.Effective);
            float loBase = Mathf.Lerp(0.35f, 0.60f, sharp);
            float hiBase = loBase + Mathf.Lerp(0.25f, 0.12f, sharp);
            ctx.globals.SetGlobalColor(ID_GRColor,       godRayColor.Effective);
            ctx.globals.SetGlobalFloat(ID_GRIntensity,   godRayIntensity.Effective);
            ctx.globals.SetGlobalFloat(ID_GRMaxDist,     godRayMaxDist.Effective);
            ctx.globals.SetGlobalFloat(ID_GRThreshLo,    loBase * 0.3f);
            ctx.globals.SetGlobalFloat(ID_GRThreshHi,    hiBase * 3.0f);
            ctx.globals.SetGlobalFloat(ID_GRBeamScale,   godRayBeamScale.Effective);
            ctx.globals.SetGlobalFloat(ID_GRSunFollow,   godRaySunFollow.Effective);
            ctx.globals.SetGlobalFloat(ID_GRDepthFade,   godRayDepthFade.Effective);
            ctx.globals.SetGlobalFloat(ID_GRExtinction,  godRayExtinction.Effective);
            ctx.globals.SetGlobalFloat(ID_GRFadeInDepth, godRayFadeInDepth.Effective);
            ctx.globals.SetGlobalFloat(ID_GRSteps,       godRaySteps.Effective);
            ctx.globals.SetGlobalFloat(ID_GRMode,        (float)(int)godRayMode);   // comparaison temporaire
        }

        // Albedo (single-scattering) du glow = la couleur AFFICHÉE de l'eau (_OceanScatterColor, poussée par
        // le module Absorption — source unique du look), normalisée au canal dominant (couleur vive). → glow
        // du dessous cohérent avec le dessus. Repli bleu si scatter absent/nul (module Absorption off).
        static Color GlowAlbedoFromScatter()
        {
            Vector4 c = Shader.GetGlobalVector(ID_ScatterColor);
            Vector3 v = new Vector3(Mathf.Max(c.x, 0f), Mathf.Max(c.y, 0f), Mathf.Max(c.z, 0f));
            float m = Mathf.Max(v.x, Mathf.Max(v.y, v.z));
            if (m > 1e-6f) v /= m;                       // normalise le canal dominant à 1
            else v = new Vector3(0.10f, 0.45f, 0.55f);   // repli (module Absorption absent)
            return new Color(v.x, v.y, v.z, 1f);
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

                // God-rays : CustomPass scripté demi-résolution sur le même GameObject (BeforePostProcess).
                var grSh = Shader.Find("Hidden/Ocean/GodRaysLowRes");
#if UNITY_EDITOR
                if (grSh == null) grSh = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(
                    "Assets/Shader/Ocean_v2/Shaders/OceanGodRaysLowRes.shader");
#endif
                if (grSh != null)
                {
                    rt.grMaterial = new Material(grSh) { name = "OceanGodRaysLowRes (auto)", hideFlags = HideFlags.HideAndDontSave };
                    rt.grVolume = rt.go.AddComponent<CustomPassVolume>();
                    rt.grVolume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
                    rt.grVolume.isGlobal = true;
                    rt.grPass = new OceanGodRayLowResPass { name = "OceanGodRays", material = rt.grMaterial, enabled = true };
                    rt.grVolume.customPasses.Add(rt.grPass);
                }
                else Debug.LogWarning("[Ocean] Shader 'Hidden/Ocean/GodRaysLowRes' introuvable — god-rays inactifs.");
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
            godRayIntensity.value   = Mathf.Max(0f, godRayIntensity.value);
            godRayMaxDist.value     = Mathf.Clamp(godRayMaxDist.value, 1f, 400f);
            godRaySharpness.value   = Mathf.Clamp01(godRaySharpness.value);
            godRayBeamScale.value   = Mathf.Clamp(godRayBeamScale.value, 0.05f, 10f);
            godRaySunFollow.value   = Mathf.Clamp01(godRaySunFollow.value);
            godRayDepthFade.value   = Mathf.Max(0f, godRayDepthFade.value);
            godRayExtinction.value  = Mathf.Max(0f, godRayExtinction.value);
            godRayFadeInDepth.value = Mathf.Max(0.01f, godRayFadeInDepth.value);
            godRaySteps.value       = Mathf.Clamp(godRaySteps.value, 4f, 64f);
        }
#endif
    }
}
