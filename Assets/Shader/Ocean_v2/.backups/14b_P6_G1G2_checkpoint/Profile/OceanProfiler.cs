// OceanProfiler.cs
// INSTRUMENTATION (orientation de conception §1.4 OCEAN_ROADMAP.md).
// Fournit :
//   - des ProfilerMarker nommés, un par compute/passe (spectre, surface, MV, ...) ;
//     ceux des étages GPU réellement mesurés en P2 (Spectrum/FFT [poste budget b],
//     Surface [poste a], MotionVector [poste c]) sont créés avec MarkerFlags.SampleGPU
//     pour que ProfilerRecorder(GpuRecorder) renvoie un coût GPU non nul ;
//   - un wrapper CommandBuffer réutilisable pour batcher les dispatchs (P1/P2) ;
//   - OceanPerfRecorders : la lecture GPU des TROIS postes budget du verrou T2 —
//     (a) GBuffer total, (b) FFT/spectre (marker englobant Ocean.Spectrum), (c) Ocean.MotionVector —
//     consommée par OceanSystem, avec table budget par preset (cases « mesuré » à remplir = gate utilisateur).
//
// IMPORTANT (édition des initialiseurs, PAS de réassignation runtime) : un ProfilerMarker
// `static readonly` est immuable ; pour activer SampleGPU il faut éditer l'EXPRESSION
// d'initialisation (new ProfilerMarker(ProfilerCategory.Render, "...", MarkerFlags.SampleGPU)),
// ce qui est fait ci-dessous. Le .Auto() CPU reste valide et rétrocompatible P1.
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.OceanFeatures
{
    /// Marqueurs de profilage statiques (un par étage GPU prévu). À envelopper autour des dispatchs :
    /// using (OceanProfiler.Surface.Auto()) { cmd.DispatchCompute(...); }
    public static class OceanProfiler
    {
        // P1 — poste budget (b) « FFT/spectre » : SampleGPU obligatoire (sinon la valeur GPU du recorder = 0).
        // NESTING : dans OceanSpectrumModule.EvolveAndTransform, le `using (Spectrum.Auto())` ENVELOPPE les
        // appels Inverse2D (eux-mêmes en `using (FFT.Auto())`). Ocean.Spectrum est donc le marker ENGLOBANT
        // qui porte le coût TOTAL du poste (b) = évolution (EvolvePack) + IFFT (butterfly) + assemblage.
        // Ocean.FFT n'est qu'une SOUS-MESURE de l'IFFT (utile au Profiler pour ventiler), déjà comprise dans
        // Ocean.Spectrum → NE JAMAIS additionner les deux recorders (double comptage). Le poste (b) = Spectrum seul.
        public static readonly ProfilerMarker Spectrum     = new ProfilerMarker(ProfilerCategory.Render, "Ocean.Spectrum", MarkerFlags.SampleGPU); // P1 — poste (b), ENGLOBANT
        public static readonly ProfilerMarker FFT          = new ProfilerMarker(ProfilerCategory.Render, "Ocean.FFT",      MarkerFlags.SampleGPU); // P1.a — sous-mesure IFFT (incluse dans Spectrum)
        // P2 — étages GPU mesurés : SampleGPU obligatoire (sinon la valeur GPU du recorder = 0).
        public static readonly ProfilerMarker Surface      = new ProfilerMarker(ProfilerCategory.Render, "Ocean.Surface",      MarkerFlags.SampleGPU); // P2
        public static readonly ProfilerMarker MotionVector = new ProfilerMarker(ProfilerCategory.Render, "Ocean.MotionVector", MarkerFlags.SampleGPU); // P2
        // P4 — extraction moments d'écume + GenerateMips (budget AGRÉGÉ au poste `surface`, ventilable au Profiler).
        public static readonly ProfilerMarker Foam         = new ProfilerMarker(ProfilerCategory.Render, "Ocean.Foam",         MarkerFlags.SampleGPU); // P4
        public static readonly ProfilerMarker Reflection   = new ProfilerMarker("Ocean.Reflection");    // P5
        public static readonly ProfilerMarker Underwater   = new ProfilerMarker("Ocean.Underwater");    // P6
        public static readonly ProfilerMarker Absorption   = new ProfilerMarker("Ocean.Absorption");    // P6
        public static readonly ProfilerMarker Shore        = new ProfilerMarker("Ocean.Shore");         // P7+
        public static readonly ProfilerMarker Wake         = new ProfilerMarker("Ocean.Wake");          // P7+
    }

    /// Wrapper d'un CommandBuffer unique réutilisé chaque frame (évite l'allocation par frame).
    public sealed class OceanCommandRecorder : System.IDisposable
    {
        public CommandBuffer Cmd { get; private set; }
        public OceanCommandRecorder(string name = "Ocean") { Cmd = new CommandBuffer { name = name }; }
        public void Begin() { Cmd.Clear(); }
        public void Dispose() { if (Cmd != null) { Cmd.Release(); Cmd = null; } }
    }

    /// Lecture de perf GPU P2 (éditeur + player) — les TROIS postes budget du verrou T2. Lance des
    /// ProfilerRecorder GPU sur :
    ///   - (a) le marker HDRP de la passe GBuffer (coût total ; la surface océan en est un DELTA lu, EN
    ///     ÉDITEUR, par toggle du MeshRenderer.enabled du child runtime, PAS du flag `active`. En BUILD, la
    ///     scène de gate ne contenant QUE Spectrum+Surface et le GradientSky n'écrivant pas le GBuffer, le
    ///     GBuffer total ≈ surface seule). NOM À CONFIRMER au protocole : probable "RenderGBuffer" en
    ///     HDRP 17.4 (cf. Profiler GPU / FrameDebugger) — sans nom correct recorder.Valid=false.
    ///   - (b) le marker ENGLOBANT "Ocean.Spectrum" (SampleGPU) = FFT/spectre (évolution + IFFT + assemblage).
    ///     CRITIQUE : les dispatchs compute tournent que la surface soit visible ou non → ce poste est
    ///     INVISIBLE au delta (a) et doit avoir son PROPRE recorder. Ne pas additionner Ocean.FFT (sous-mesure
    ///     déjà comprise dans Ocean.Spectrum → double comptage).
    ///   - (c) le marker custom "Ocean.MotionVector" (SampleGPU) qui enveloppe la copie/binding T-1.
    /// Garde de validité stricte : on n'écrit une valeur que si recorder.Valid && Count>0, sinon "n/a"
    /// (jamais 0 trompeur — un poste à 0/n-a = donnée MANQUANTE, gate ouvert, jamais injecté dans la somme).
    /// ms = LastValue * 1e-6f (LastValue est en nanosecondes).
    public sealed class OceanPerfRecorders : System.IDisposable
    {
        // Candidats de nom pour la passe GBuffer HDRP (le 1er valide est retenu). Le protocole P2
        // confirme le nom réel ; on en essaie quelques-uns pour ne pas dépendre d'une seule supposition.
        static readonly string[] kGBufferMarkerCandidates = { "RenderGBuffer", "GBuffer", "Render GBuffer" };

        // Options du recorder GPU. GpuRecorder SEUL est refusé par Unity (NotSupportedException) sur un
        // marqueur agrégeant plusieurs échantillons/threads par frame (cas du GBuffer HDRP et de tout
        // marqueur SampleGPU multi-dispatch) : il faut y adjoindre SumAllSamplesInFrame (somme des
        // échantillons de la frame — sémantique correcte pour un coût de passe agrégé) ou
        // CollectOnlyOnCurrentThread. On retient SumAllSamplesInFrame car le GBuffer et Ocean.MotionVector
        // sont des coûts agrégés par frame, pas mono-échantillon mono-thread.
        const ProfilerRecorderOptions kGpuFrameOptions =
            ProfilerRecorderOptions.GpuRecorder | ProfilerRecorderOptions.SumAllSamplesInFrame;

        ProfilerRecorder m_GBuffer;     // (a) GPU, ns
        ProfilerRecorder m_SpectrumFFT; // (b) GPU, ns — marker englobant Ocean.Spectrum
        ProfilerRecorder m_MotionVec;   // (c) GPU, ns
        string m_GBufferMarkerUsed = "(non identifié)";

        public bool GBufferValid => m_GBuffer.Valid && m_GBuffer.Count > 0;
        public bool SpectrumFftValid => m_SpectrumFFT.Valid && m_SpectrumFFT.Count > 0;
        public bool MotionVecValid => m_MotionVec.Valid && m_MotionVec.Count > 0;
        public float GBufferMs => GBufferValid ? m_GBuffer.LastValue * 1e-6f : -1f;
        public float SpectrumFftMs => SpectrumFftValid ? m_SpectrumFFT.LastValue * 1e-6f : -1f;
        public float MotionVecMs => MotionVecValid ? m_MotionVec.LastValue * 1e-6f : -1f;
        public string GBufferMarkerUsed => m_GBufferMarkerUsed;

        public void Start()
        {
            // Essaye chaque candidat de nom ; conserve le premier qui produit un recorder valide.
            foreach (var name in kGBufferMarkerCandidates)
            {
                var r = ProfilerRecorder.StartNew(ProfilerCategory.Render, name, 1, kGpuFrameOptions);
                if (r.Valid)
                {
                    m_GBuffer = r;
                    m_GBufferMarkerUsed = name;
                    break;
                }
                r.Dispose();
            }
            // (b) Poste FFT/spectre : marker ENGLOBANT Ocean.Spectrum (évolution + IFFT + assemblage). Le toggle
            // MeshRenderer.enabled du gate ne coupe PAS ces dispatchs compute → poste mesuré par SON PROPRE
            // recorder GPU, indépendamment du delta surface, en éditeur ET en build.
            m_SpectrumFFT = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Ocean.Spectrum", 1, kGpuFrameOptions);

            m_MotionVec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Ocean.MotionVector", 1, kGpuFrameOptions);
        }

        /// À appeler une fois par frame (rafraîchit LastValue côté recorder ; no-op explicite ici car
        /// ProfilerRecorder accumule seul, mais on garde le point d'appel pour symétrie/extension).
        public void Sample() { }

        /// Ligne de lecture humaine des 3 postes budget. **N'est câblée à AUCUN affichage aujourd'hui**
        /// (ni CustomEditor, ni OnGUI/HUD) — helper programmatique laissé pour un futur overlay/inspecteur.
        /// La mesure BUILD de référence NE passe PAS par cette chaîne : elle se lit dans la **fenêtre
        /// Profiler GPU** (Window ▸ Analysis ▸ Profiler → GPU) connectée à un Development Build, seule
        /// autorité du verrou T2 (cf. OCEAN_TEST_P2.md §(i-build)). "n/a" si le recorder GPU est
        /// indisponible (ex. D3D11 sans GpuRecorder) → fallback FrameDebugger documenté au protocole.
        public string Readout()
        {
            string gb = GBufferValid ? $"{GBufferMs:0.000} ms" : "n/a";
            string sp = SpectrumFftValid ? $"{SpectrumFftMs:0.000} ms" : "n/a";
            string mv = MotionVecValid ? $"{MotionVecMs:0.000} ms" : "n/a";
            return $"(a) GBuffer total = {gb}  (marker: {m_GBufferMarkerUsed})\n" +
                   $"(b) FFT/spectre (Ocean.Spectrum englobant) = {sp}\n" +
                   $"(c) Ocean.MotionVector = {mv}\n" +
                   "Budget océan T2 = SOMME (a)+(b)+(c) sur la colonne BUILD RTX 2060 (voir OCEAN_TEST_P2.md).\n" +
                   "Coût surface isolé (poste a en éditeur) = DELTA GBuffer (toggle MeshRenderer.enabled via menu " +
                   "Ombrage/Ocean/Toggle Surface Renderer) — CORROBORATION ÉDITEUR uniquement, pas mesure build.";
        }

        public void Dispose()
        {
            if (m_GBuffer.Valid) m_GBuffer.Dispose();
            if (m_SpectrumFFT.Valid) m_SpectrumFFT.Dispose();
            if (m_MotionVec.Valid) m_MotionVec.Dispose();
        }
    }
}
