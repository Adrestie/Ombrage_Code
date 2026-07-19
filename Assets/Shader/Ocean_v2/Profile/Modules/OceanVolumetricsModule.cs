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
        // Valeurs à OVERRIDE (niveau 2, cf. module Reflection). Défaut décoché = ces valeurs ; cocher
        // permet de saisir autre chose. Clamp appliqué sur .value en OnValidate.
        [Header("Volumétriques sous-marins (P6 / G4.a — fog volumétrique HDRP)")]
        [Tooltip("Couleur du glow diffus sous-marin (single-scattering albedo du fog). C'est l'IN-SCATTERING ; l'extinction reste portée par G2.")]
        public OceanColorParameter fogGlowColor = new OceanColorParameter(new Color(0.10f, 0.45f, 0.55f, 1f));

        [Tooltip("Densité du fog volumétrique = distance moyenne libre (m). PLUS GRAND = fog PLUS LÉGER. Large par défaut pour ne pas ré-éteindre G2.")]
        public OceanFloatParameter fogMeanFreePath = new OceanFloatParameter(60f);

        [Tooltip("Portée (m) sur laquelle le fog volumétrique est calculé devant la caméra.")]
        public OceanFloatParameter fogDepthExtent = new OceanFloatParameter(96f);

        [Header("God-rays (P6 / G4.b — cookie de caustics sur le soleil)")]
        [Tooltip("Texture de caustics projetée sur le soleil (cookie) → shafts/dappling dans le fog + caustiques sur les objets immergés. VIDE = placeholder procédural généré au runtime (remplaçable par les vraies caustiques Q8.1). Référence d'asset → champ simple (pas d'override).")]
        public Texture2D causticCookie;

        [Tooltip("Échelle du motif de caustics projeté sur le monde (m). Plus petit = motif plus fin/dense.")]
        public OceanFloatParameter causticScale = new OceanFloatParameter(12f);

        sealed class Runtime
        {
            public GameObject go;
            public Volume volume;
            public VolumeProfile profile;
            public Fog fog;
            // God-rays : modulation NON destructive du soleil de la scène (cookie), gatée immersion.
            public Light sun;
            public HDAdditionalLightData sunHD;
            public Texture savedCookie;   // cookie d'origine du soleil (souvent null) — restauré émergé/teardown
            public bool cookieApplied;
            public Texture2D generatedCookie;  // placeholder procédural (HideAndDontSave → non sérialisé)
            public Texture appliedTex; public float appliedScale = float.NaN;  // garde anti re-set par frame
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
                // ANTI-BUG n°1 : restaurer le cookie d'origine du soleil (on module un objet de SCÈNE) AVANT
                // de lâcher la référence, puis détruire le placeholder procédural et le Volume runtime.
                if (rt.cookieApplied) RestoreSunCookie(rt);
                if (rt.generatedCookie != null) DestroyObj(rt.generatedCookie);
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

            // GATING immersion (D3) : le Volume ne contribue QUE sous l'eau ; émergé, on le désactive →
            // le fog de la scène reprend la main (aucune écriture destructive, anti-bug n°1).
            rt.volume.enabled = submerged;

            // God-rays (G4.b) : cookie de caustics sur le soleil, APPLIQUÉ immergé / RESTAURÉ émergé.
            // Doit tourner AUSSI quand émergé (pour restaurer) → avant le return anticipé.
            UpdateGodRayCookie(rt, submerged);

            if (!submerged) return;

            // Fog volumétrique HDRP piloté (D1a / D2). baseHeight = niveau d'eau → densité PLEINE sous
            // l'eau (constante en dessous), s'estompe juste au-dessus (émergé = Volume off de toute façon).
            SetBool (rt.fog.enabled,            true);
            SetBool (rt.fog.enableVolumetricFog, true);
            SetColor(rt.fog.albedo,             fogGlowColor.Effective);
            SetFloat(rt.fog.meanFreePath,       fogMeanFreePath.Effective);
            SetFloat(rt.fog.baseHeight,         waterY);
            SetFloat(rt.fog.maximumHeight,      waterY + 2f);
            SetFloat(rt.fog.depthExtent,        fogDepthExtent.Effective);
            SetFloat(rt.fog.anisotropy,         0.6f);  // forward-scatter → renforce les shafts vers le soleil (G4.b)
        }

        // --- God-rays (G4.b) : cookie de caustics NON destructif sur le soleil de la scène ------------
        void UpdateGodRayCookie(Runtime rt, bool submerged)
        {
            ResolveSun(rt);
            if (rt.sun == null || rt.sunHD == null) return;

            if (submerged)
            {
                if (!rt.cookieApplied)
                {
                    rt.savedCookie = rt.sun.cookie;   // sauve l'original (le plus souvent null)
                    rt.cookieApplied = true;
                    rt.appliedTex = null; rt.appliedScale = float.NaN;
                }
                // Ne (re)pousse le cookie que si texture/échelle ont changé → pas de re-set (ni de dirty
                // en édition) à chaque frame ; permet quand même le réglage live de causticScale.
                Texture tex = EnsureCookie(rt);
                float scale = causticScale.Effective;
                if (rt.appliedTex != tex || rt.appliedScale != scale)
                {
                    rt.sunHD.SetCookie(tex, new Vector2(scale, scale));
                    rt.appliedTex = tex; rt.appliedScale = scale;
                }
            }
            else if (rt.cookieApplied)
            {
                RestoreSunCookie(rt);
            }
        }

        void ResolveSun(Runtime rt)
        {
            if (rt.sun != null) { if (rt.sunHD == null) rt.sunHD = rt.sun.GetComponent<HDAdditionalLightData>(); return; }
            var sun = RenderSettings.sun;
            if (sun == null)   // repli : la directionnelle la plus intense
            {
                var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                float best = -1f;
                foreach (var l in lights)
                    if (l != null && l.type == LightType.Directional && l.intensity > best) { best = l.intensity; sun = l; }
            }
            rt.sun = sun;
            rt.sunHD = sun != null ? sun.GetComponent<HDAdditionalLightData>() : null;
        }

        void RestoreSunCookie(Runtime rt)
        {
            if (rt.sunHD != null)
            {
                if (rt.savedCookie != null) rt.sunHD.SetCookie(rt.savedCookie, new Vector2(causticScale.Effective, causticScale.Effective));
                else                        rt.sunHD.SetCookie(null, Vector2.one);   // retire NOTRE cookie
            }
            rt.cookieApplied = false;
            rt.appliedTex = null; rt.appliedScale = float.NaN;
        }

        Texture EnsureCookie(Runtime rt)
        {
            if (causticCookie != null) return causticCookie;
            if (rt.generatedCookie == null) rt.generatedCookie = GenerateCausticTexture(256);
            return rt.generatedCookie;
        }

        // Placeholder procédural : réseau de caustiques (arêtes de cellules Worley) tuilable, HideAndDontSave
        // (donc jamais sérialisé → une sauvegarde de scène immergée ne persiste pas le cookie). Remplacé par
        // les vraies caustiques Q8.1 quand elles existeront (ou par le champ causticCookie).
        static Texture2D GenerateCausticTexture(int res)
        {
            // MULTI-ÉCHELLE : composante BASSE fréquence (larges bandes = shafts que la grille froxel
            // GROSSIÈRE du volumétrique peut résoudre) × composante HAUTE fréquence (réseau caustique fin,
            // visible sur les surfaces pleine résolution). Sans la basse fréquence, le fog moyenne tout.
            const int fineCells = 8, coarseCells = 2;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
            {
                name = "OceanCausticCookie (auto)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[res * res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
                float broad = CausticWorley(u * coarseCells, v * coarseCells, coarseCells);  // shafts larges
                float fine  = CausticWorley(u * fineCells,   v * fineCells,   fineCells);    // caustiques fines
                float c = Mathf.Lerp(0.25f, 1f, broad) * Mathf.Lerp(0.55f, 1f, fine);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255);
                px[y * res + x] = new Color32(b, b, b, b);
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // Worley : brillant (1) près des arêtes de cellules (F2-F1 petit) → réseau de caustiques. Tuilable
        // (indices de cellule wrappés au modulo). Contraste par puissance.
        static float CausticWorley(float px, float py, int cells)
        {
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
            float f1 = 99f, f2 = 99f;
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                Vector2 h = Hash2(wx, wy);
                float dx = px - (gx + h.x), dy = py - (gy + h.y);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) { f2 = d; }
            }
            float caustic = 1f - Mathf.SmoothStep(0f, 0.6f, f2 - f1);
            return Mathf.Pow(Mathf.Clamp01(caustic), 1.5f);
        }

        static Vector2 Hash2(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return new Vector2(((h & 0xffff) / 65535f), (((h >> 16) & 0xffff) / 65535f));
        }
        // ---------------------------------------------------------------------------------------------

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
            causticScale.value    = Mathf.Clamp(causticScale.value, 2f, 60f);
        }

#endif
    }
}
