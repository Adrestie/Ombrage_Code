// OceanSystem.cs
// Composant UNIQUE de scène qui applique un OceanProfile (équivalent du TerrainProfileController).
// - Construit le contexte, énumère les modules, les Apply/Tick chaque frame en [ExecuteAlways].
// - Détient le cache de push global NON CUMULATIF/restaurable (contrat anti-bug n°1) et le
//   restaure à neutre au Teardown (OnDisable).
// - Porte l'état runtime des modules (un SO ne sérialise pas d'état de scène) + l'instrumentation.
//
// P0 : aucune donnée métier poussée (modules = stubs). Seuls le cycle et le HARNAIS sont posés.
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.OceanFeatures
{
    [ExecuteAlways]
    [AddComponentMenu("Ombrage/Ocean/Ocean System")]
    public class OceanSystem : MonoBehaviour
    {
        [Tooltip("Profil de features océan à appliquer.")]
        public OceanProfile profile;

        [Tooltip("Matériau de surface cible (alimenté à partir de P2 — non requis en P0).")]
        public Material surfaceMaterial;

        OceanApplyContext m_Ctx;
        readonly OceanGlobalCache m_Globals = new OceanGlobalCache();
        readonly Dictionary<OceanFeatureModule, object> m_Runtime = new Dictionary<OceanFeatureModule, object>();
        readonly List<OceanFeatureModule> m_Enabled = new List<OceanFeatureModule>();

        // --- État runtime par module (RT spectre/cascade, double-buffer MV, etc.) ---
        public object GetRuntime(OceanFeatureModule m) => (m != null && m_Runtime.TryGetValue(m, out var s)) ? s : null;
        public void SetRuntime(OceanFeatureModule m, object state) { if (m != null) m_Runtime[m] = state; }

        // ── Instrumentation perf P2 (ProfilerRecorder GPU) ──────────────────
        // Recorders GPU des 3 postes budget T2 : (a) GBuffer total (la surface océan en est un DELTA, lu
        // EN ÉDITEUR par toggle du MeshRenderer.enabled du child runtime — PAS du flag `active`),
        // (b) FFT/spectre (marker englobant Ocean.Spectrum, SampleGPU), (c) Ocean.MotionVector (SampleGPU).
        // Les recorders tournent aussi en Development Build (Start() sous OnEnable). Voir OceanProfiler /
        // menu Ombrage/Ocean/Toggle Surface Renderer.
        OceanPerfRecorders m_Perf;
        // NOTE : ce booléen est réservé à un futur overlay/inspecteur ; AUCUN affichage n'y est câblé
        // aujourd'hui (pas de CustomEditor ni d'OnGUI). La mesure BUILD de référence se lit dans la
        // fenêtre Profiler GPU connectée à un Development Build (OCEAN_TEST_P2.md §(i-build)) — NE PAS
        // s'appuyer sur un « readout inspecteur ». Le champ est conservé (et non supprimé) pour ne pas
        // casser la sérialisation des scènes/prefabs de gate existants.
        [Tooltip("Réservé (futur overlay des coûts GPU) — non câblé à ce jour. Lire le budget dans la " +
                 "fenêtre Profiler GPU sur Development Build, cf. OCEAN_TEST_P2.md §(i-build).")]
        public bool showPerfReadout = true;

        void OnEnable() { Setup(); m_Perf = new OceanPerfRecorders(); m_Perf.Start(); }
        void OnDisable() { Teardown(); if (m_Perf != null) { m_Perf.Dispose(); m_Perf = null; } }

        void OnValidate()
        {
#if UNITY_EDITOR
            // Re-setup différé (le profil a pu changer dans l'inspecteur).
            EditorApplication.delayCall += DeferredResetup;
#endif
        }

#if UNITY_EDITOR
        void DeferredResetup()
        {
            if (this == null) return;
            if (!isActiveAndEnabled) return;
            Teardown();
            Setup();
        }
#endif

        void Setup()
        {
            BuildContext();
            m_Globals.Clear();        // repart d'un état de tracking propre
            m_Enabled.Clear();
            ReconcileEnabled();       // n'active QUE les modules actifs (module inactif = comme absent)
            ApplyAll();
        }

        void Teardown()
        {
            if (m_Ctx != null)
            {
                foreach (var m in m_Enabled)
                    if (m != null) m.OnModuleDisable(m_Ctx);
            }
            m_Enabled.Clear();
            m_Runtime.Clear();

            // CONTRAT ANTI-BUG n°1 : restaurer tous les globaux poussés à une valeur neutre.
            // Aucune écriture océan ne doit subsister après désactivation (jamais cumulatif).
            m_Globals.RestoreAll();
        }

        // Réconcilie l'ensemble des modules « vivants » (ressources créées) avec leur flag `active`.
        // Un module devenu actif reçoit OnModuleEnable ; un module devenu inactif — ou retiré du profil —
        // reçoit OnModuleDisable (qui libère/détruit ses ressources). C'est CE qui donne son sens à
        // « module désactivé = comme absent » : sans ça, un effet créé une fois (ex. sonde de réflexion)
        // survit au décochage puisque Apply cesse d'être appelé sans jamais éteindre la ressource.
        // Idempotent : n'agit que sur les transitions. Appelé au Setup et en tête de chaque Update.
        void ReconcileEnabled()
        {
            if (profile == null || m_Ctx == null) return;

            // (a) Activation : modules actifs et présents, pas encore vivants.
            foreach (var m in profile.modules)
            {
                if (m == null || !m.active) continue;
                if (!m_Enabled.Contains(m)) { m.OnModuleEnable(m_Ctx); m_Enabled.Add(m); }
            }

            // (b) Désactivation : modules vivants devenus inactifs, nuls, ou retirés du profil.
            for (int i = m_Enabled.Count - 1; i >= 0; i--)
            {
                var m = m_Enabled[i];
                if (m == null || !m.active || !profile.modules.Contains(m))
                {
                    if (m != null) m.OnModuleDisable(m_Ctx);
                    m_Enabled.RemoveAt(i);
                }
            }
        }

        // Nettoyage EXPLICITE d'un module qu'on s'apprête à RETIRER du profil (appelé par l'éditeur AVANT
        // la destruction de l'instance). Indispensable : au retrait, l'éditeur détruit le ScriptableObject
        // immédiatement → à l'Update suivant ReconcileEnabled le verrait déjà `null` et ne pourrait PLUS
        // appeler son OnModuleDisable (l'instance est requise) → ses ressources de scène fuient (cookie
        // soleil, Volume runtime… = effet fantôme persistant). Sans effet si le module n'était pas vivant.
        public void DisableAndForget(OceanFeatureModule m)
        {
            if (m == null || m_Ctx == null) return;
            if (m_Enabled.Remove(m)) m.OnModuleDisable(m_Ctx);
        }

        void BuildContext()
        {
            m_Ctx = new OceanApplyContext
            {
                material = surfaceMaterial,
                profile = profile,
                system = this,
                globals = m_Globals,
                editMode = !Application.isPlaying
            };
        }

        void Update()
        {
            if (m_Ctx == null) BuildContext();

            m_Ctx.material = surfaceMaterial;
            m_Ctx.profile = profile;
            m_Ctx.editMode = !Application.isPlaying;
            m_Ctx.time = Application.isPlaying ? Time.time : EditorTime();
            m_Ctx.deltaTime = Time.deltaTime;

            // PHASE 0 — RÉCONCILIATION : aligne l'ensemble « vivant » sur les flags `active` (décoché =
            // désactivé = comme absent ; coché = (ré)activé) AVANT toute passe, pour que l'effet d'un
            // module suive réellement son interrupteur.
            ReconcileEnabled();

            // PHASE 1 — PRÉ-SIMULATION : invoquée sur TOUS les modules actifs AVANT toute passe Apply
            // ET Tick (= avant l'évolution du spectre P1, qui s'effectue dans Tick). INVARIANT D'ORDRE
            // critique pour les Motion Vectors de la surface : la copie _OceanDisp(=D[N-1])→_OceanDispPrev
            // faite ici, étant globalement antérieure à l'évolution (qui écrira D[N]), garantit que tous
            // les contextes de rendu de la frame lisent prev=D[N-1] de façon identique (race éliminée).
            // Deux balayages DISTINCTS (PreSimulate puis Apply) → indépendant de l'ordre des modules.
            PreSimulateAll();

            // PHASE 2 — APPLY (props statiques + globaux) puis TICK (dynamique : évolution FFT P1).
            ApplyAll();
            Tick();

            if (m_Perf != null) m_Perf.Sample();

#if UNITY_EDITOR
            // Ne forcer le repaint continu de la SceneView que si au moins un module ACTIF anime la
            // surface. En P0 (stubs inertes), aucun module ne le demande : la SceneView reste au repos.
            if (!Application.isPlaying && NeedsContinuousRepaint()) SceneView.RepaintAll();
#endif
        }

#if UNITY_EDITOR
        bool NeedsContinuousRepaint()
        {
            if (profile == null) return false;
            foreach (var m in profile.modules)
                if (m != null && m.active && m.WantsContinuousRepaint) return true;
            return false;
        }
#endif

        static float EditorTime()
        {
#if UNITY_EDITOR
            return (float)EditorApplication.timeSinceStartup;
#else
            return Time.time;
#endif
        }

        // Balayage de pré-simulation : DOIT précéder globalement ApplyAll()/Tick() (cf. Update).
        void PreSimulateAll()
        {
            if (profile == null || m_Ctx == null) return;
            foreach (var m in profile.modules)
                if (m != null && m.active) m.PreSimulate(m_Ctx);
        }

        void ApplyAll()
        {
            if (profile == null || m_Ctx == null) return;
            foreach (var m in profile.modules)
            {
                if (m == null) continue;
                // Keyword de surface (à partir de P2) : géré sur le matériau quand il existe.
                if (surfaceMaterial != null && !string.IsNullOrEmpty(m.Keyword))
                    SetKeyword(surfaceMaterial, m.Keyword, m.KeywordEnabled(m_Ctx));
                if (m.active) m.Apply(m_Ctx);
            }
        }

        void Tick()
        {
            if (profile == null || m_Ctx == null) return;
            foreach (var m in profile.modules)
                if (m != null && m.active) m.Tick(m_Ctx);
        }

        void OnDrawGizmosSelected()
        {
            if (profile == null || m_Ctx == null) return;
            foreach (var m in profile.modules)
                if (m != null && m.active) m.DrawGizmos(m_Ctx);
        }

        static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw);
            else m.DisableKeyword(kw);
        }
    }
}
