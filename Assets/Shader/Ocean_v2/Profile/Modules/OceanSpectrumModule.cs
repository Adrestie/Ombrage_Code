// OceanSpectrumModule.cs  (Ocean_v2 / P1)
// Module SPECTRE — simulation FFT complète (JONSWAP/TMA dormant + dérivées analytiques),
// 4 cascades golden-ratio en résolution mixte 512²/256², IFFT hermitienne 2-en-1.
//
// Architecture (pattern herbe/terrain) : ce ScriptableObject est PUR DATA ; tout l'état
// runtime (RenderTextures, ping-pong, mesure) est détenu par OceanSystem via SetRuntime.
//
// Contrats anti-bug :
//  n°1 : tous les globaux poussés via ctx.globals (assignation pure, restaurés au Teardown).
//  n°2 : dérivées/normales/Jacobien ANALYTIQUES en domaine spectral (i·k·h) — voir OceanSpectrum.compute.
//  n°3 : normalisation 1/N STRICTEMENT découplée de l'amplitude — voir OceanFFT.compute (_NormScale).
//        + recalcul de H0 UNIQUEMENT sur changement réel de paramètre (jamais chaque frame).
using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Simulation/Spectrum")]
    public class OceanSpectrumModule : OceanFeatureModule
    {
        // ── Cascades / résolution (Q2.2 / Q2.3 / Q11.3) ─────────────────────
        public enum CascadeQuality { Ultra, High, Low }

        [Header("Cascades & résolution")]
        [Tooltip("Répartition de résolution des 4 cascades.\nUltra = 2×512²+2×256², High = 1×512²+3×256², Low = tout-256². Enum structurel → champ simple.")]
        public CascadeQuality cascadeQuality = CascadeQuality.High;

        // Valeurs à OVERRIDE (niveau 2, cf. Reflection). Décoché = défaut ; cocher = saisie. Clamp en OnValidate.
        [Tooltip("Longueur de tuile de la plus GRANDE cascade (m). Les 3 autres en sont dérivées par le nombre d'or (anti-répétition).")]
        public OceanFloatParameter masterTileLength = new OceanFloatParameter(360f);

        [Tooltip("Facteur de croisement des bandes entre cascades (anti-recouvrement).")]
        public OceanFloatParameter bandBoundary = new OceanFloatParameter(6.0f);

        // ── État de mer (master + dérivées, pattern Q12.1) ──────────────────
        [Header("État de mer")]
        [Tooltip("Master d'état de mer [0..1] : 0 = calme, 1 = tempête. Dérive amplitude et vent.")]
        public OceanFloatParameter oceanState = new OceanFloatParameter(0.5f);

        [Tooltip("Vitesse de vent de référence (m/s) à oceanState=1. La valeur effective est dérivée du master.")]
        public OceanFloatParameter windSpeedAtMax = new OceanFloatParameter(18f);

        [Tooltip("Direction du vent (degrés). En V1, asservie au master ; le WindZone partagé arrive en P2+.")]
        public OceanFloatParameter windDirectionDeg = new OceanFloatParameter(30f);

        [Tooltip("Fetch (m) — distance sur laquelle le vent a soufflé. Plus grand = houle plus développée.")]
        public OceanFloatParameter fetch = new OceanFloatParameter(120000f);

        [Tooltip("Pic JONSWAP γ (≈3.3 standard).")]
        public OceanFloatParameter gamma = new OceanFloatParameter(3.3f);

        [Tooltip("Échelle artistique de l'amplitude du spectre (JAMAIS la normalisation IFFT — anti-bug n°3).")]
        public OceanFloatParameter amplitude = new OceanFloatParameter(1.0f);

        [Tooltip("Échelle du déplacement horizontal (choppiness des crêtes).")]
        public OceanFloatParameter choppiness = new OceanFloatParameter(1.0f);

        // ── Profondeur / TMA (branche dormante en V1) ───────────────────────
        [Header("Profondeur (TMA dormant en V1)")]
        [Tooltip("Profondeur d'eau (m). V1 = pleine mer ~191 m.")]
        public OceanFloatParameter depth = new OceanFloatParameter(191f);

        [Tooltip("Active la branche TMA tanh(kh)/Kitaigorodskii (eau finie). DORMANTE en V1 (deep-water → Φ→1).")]
        public OceanBoolParameter useTMA = new OceanBoolParameter(false);

        // ── Compute shaders ─────────────────────────────────────────────────
        // ⚠️ REQUIS EN BUILD : le repli auto (ResolveShaders → AssetDatabase) est sous #if UNITY_EDITOR.
        // Un profil sauvegardé avec ces champs VIDES → refs nulles en build → « module spectre inactif »
        // (OnModuleEnable) → surface non déplacée/invisible. Les profils construits par script DOIVENT les
        // assigner explicitement (cf. OceanP2GateProfileBuilder). Ne jamais compter sur le repli éditeur pour
        // un asset destiné au build.
        [Header("Compute (auto-résolus en éditeur si vides ; DOIVENT être assignés pour le BUILD)")]
        public ComputeShader fftShader;
        public ComputeShader spectrumShader;

        // ── Debug ───────────────────────────────────────────────────────────
        [Header("Debug")]
        [Tooltip("Lance une fois le test d'identité IFFT(FFT(x))==x au prochain rechargement (valide la convention de normalisation).")]
        public bool runIdentityTest = false;

        public override bool WantsContinuousRepaint => true;   // surface animée chaque frame

        const string kFftPath = "Assets/Shader/Ocean_v2/Shaders/OceanFFT.compute";
        const string kSpectrumPath = "Assets/Shader/Ocean_v2/Shaders/OceanSpectrum.compute";

        // ── Identifiants de globaux (anti-bug n°1 : poussés via ctx.globals) ─
        static readonly int ID_Disp512   = Shader.PropertyToID("_OceanDisp512");
        static readonly int ID_Deriv512  = Shader.PropertyToID("_OceanDeriv512");
        static readonly int ID_Disp256   = Shader.PropertyToID("_OceanDisp256");
        static readonly int ID_Deriv256  = Shader.PropertyToID("_OceanDeriv256");
        static readonly int ID_Count512  = Shader.PropertyToID("_OceanCount512");
        static readonly int ID_Count256  = Shader.PropertyToID("_OceanCount256");
        static readonly int ID_CascadeCount = Shader.PropertyToID("_OceanCascadeCount");
        static readonly int[] ID_CascadeData = {
            Shader.PropertyToID("_OceanCascade0"),
            Shader.PropertyToID("_OceanCascade1"),
            Shader.PropertyToID("_OceanCascade2"),
            Shader.PropertyToID("_OceanCascade3"),
        };

        // =====================================================================
        //  État runtime (détenu par OceanSystem, JAMAIS sérialisé dans le SO)
        // =====================================================================
        class Runtime
        {
            public ComputeShader fft, spectrum;
            public int kInit, kEvolve, kAssemble, kHerm, kHeight, kChoppy;       // spectrum kernels
            public int kButterfly, kScale;                                       // fft kernels

            public CascadeDesc[] cascades;
            public RenderTexture[] h0;          // par cascade (RGBAFloat)
            public RenderTexture pack0, pack1, pack2, pack3, scratch;            // RGFloat @ maxRes
            public RenderTexture disp512, deriv512, disp256, deriv256;           // Tex2DArray RGBAFloat
            public int count512, count256, maxRes;

            public int paramHash = int.MinValue;
            public bool h0Ready;

            public bool measured;
            public float hermRatio;
            public int hermPasses, naivePasses;

            public bool identityDone;
        }

        struct CascadeDesc
        {
            public float length;     // L (m)
            public int res;          // 256 ou 512
            public int group;        // 0 = groupe 512, 1 = groupe 256
            public int slice;        // index de slice dans l'array du groupe
            public float bandLow, bandHigh;
            public uint seed;
        }

        // =====================================================================
        //  Cycle
        // =====================================================================
        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            var rt = new Runtime();
            ResolveShaders(rt);
            if (rt.fft == null || rt.spectrum == null)
            {
                Debug.LogWarning("[Ocean P1] Compute shaders FFT/Spectrum introuvables — module spectre inactif.");
                ctx.SetRuntime(this, rt);
                return;
            }
            CacheKernels(rt);
            BuildCascades(rt);
            AllocateTextures(rt);
            InitSpectrum(rt);
            rt.paramHash = ComputeParamHash();
            ctx.SetRuntime(this, rt);

            if (runIdentityTest) RunIdentityTest(rt);
        }

        public override void OnModuleDisable(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            ReleaseTextures(rt);
            ctx.SetRuntime(this, null);
            // La restauration des globaux est assurée par OceanSystem.Teardown -> RestoreAll().
        }

        public override void Apply(OceanApplyContext ctx)
        {
            // Le spectre ne pousse pas de propriété de matériau statique en P1 :
            // tout son output (textures + scalaires cascades) est poussé en globaux dans Tick().
        }

        public override void Tick(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt == null || rt.fft == null || rt.spectrum == null) return;

            // Anti-bug n°3 (volet H0) : recalcul de H0 UNIQUEMENT sur changement RÉEL de paramètre,
            // jamais à cause de l'évolution temporelle par frame.
            int h = ComputeParamHash();
            if (h != rt.paramHash || !rt.h0Ready)
            {
                BuildCascades(rt);
                InitSpectrum(rt);
                rt.paramHash = h;
            }

            // Mesure go/no-go P1.a (une seule fois, sur la 1ʳᵉ cascade) AVANT le run nominal.
            if (!rt.measured) MeasureHermitianRatio(ctx, rt);

            EvolveAndTransform(ctx, rt);
            PushGlobals(ctx, rt);
        }

        // =====================================================================
        //  Résolution des shaders & kernels
        // =====================================================================
        void ResolveShaders(Runtime rt)
        {
            rt.fft = fftShader;
            rt.spectrum = spectrumShader;
#if UNITY_EDITOR
            if (rt.fft == null) rt.fft = AssetDatabase.LoadAssetAtPath<ComputeShader>(kFftPath);
            if (rt.spectrum == null) rt.spectrum = AssetDatabase.LoadAssetAtPath<ComputeShader>(kSpectrumPath);
            // mémorise pour éviter une recherche à chaque enable
            if (fftShader == null) fftShader = rt.fft;
            if (spectrumShader == null) spectrumShader = rt.spectrum;
#endif
        }

        void CacheKernels(Runtime rt)
        {
            rt.kInit     = rt.spectrum.FindKernel("InitSpectrum");
            rt.kEvolve   = rt.spectrum.FindKernel("EvolvePack");
            rt.kAssemble = rt.spectrum.FindKernel("Assemble");
            rt.kHerm     = rt.spectrum.FindKernel("P1a_PackHermitian");
            rt.kHeight   = rt.spectrum.FindKernel("P1a_Height");
            rt.kChoppy   = rt.spectrum.FindKernel("P1a_Choppy");
            rt.kButterfly = rt.fft.FindKernel("FFTButterfly");
            rt.kScale     = rt.fft.FindKernel("FFTScale");
        }

        // =====================================================================
        //  Cascades golden-ratio + résolution mixte
        // =====================================================================
        void BuildCascades(Runtime rt)
        {
            int[] resByQuality;
            switch (cascadeQuality)
            {
                case CascadeQuality.Ultra: resByQuality = new[] { 512, 512, 256, 256 }; break;
                case CascadeQuality.Low:   resByQuality = new[] { 256, 256, 256, 256 }; break;
                default:                   resByQuality = new[] { 512, 256, 256, 256 }; break; // High
            }

            const float PHI = 1.61803398875f;
            var c = new CascadeDesc[4];
            int s512 = 0, s256 = 0;
            float master = masterTileLength.Effective;
            float band   = bandBoundary.Effective;

            // Longueurs décroissantes par le nombre d'or → pas de facteurs communs (anti-répétition).
            float[] lengths = new float[4];
            for (int i = 0; i < 4; i++)
                lengths[i] = master / Mathf.Pow(PHI, i);

            for (int i = 0; i < 4; i++)
            {
                c[i].length = lengths[i];
                c[i].res = resByQuality[i];
                c[i].group = (c[i].res == 512) ? 0 : 1;
                c[i].slice = (c[i].group == 0) ? s512++ : s256++;
                c[i].seed = (uint)(i * 977 + 1);

                // Bande [low, high) tilée en k : croisement = band·2π/L (L décroît → k croît).
                float kCross = band * (2f * Mathf.PI / lengths[i]);
                c[i].bandHigh = (i == 3) ? 1e9f : kCross;
                c[i].bandLow  = (i == 0) ? 0f : band * (2f * Mathf.PI / lengths[i - 1]);
            }

            rt.cascades = c;
            rt.count512 = s512;
            rt.count256 = s256;
            rt.maxRes = 512; // les buffers ping-pong sont alloués au max ; les 256² utilisent la sous-région
        }

        // =====================================================================
        //  Allocation / libération
        // =====================================================================
        static RenderTexture NewRT(int n, RenderTextureFormat fmt)
        {
            var rt = new RenderTexture(n, n, 0, fmt)
            {
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "OceanRT"
            };
            rt.Create();
            return rt;
        }

        static RenderTexture NewArray(int n, int depth, RenderTextureFormat fmt)
        {
            int d = Mathf.Max(1, depth);
            var rt = new RenderTexture(n, n, 0, fmt)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = d,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "OceanArray"
            };
            rt.Create();
            return rt;
        }

        void AllocateTextures(Runtime rt)
        {
            ReleaseTextures(rt);
            int max = rt.maxRes;

            rt.h0 = new RenderTexture[4];
            for (int i = 0; i < 4; i++)
                rt.h0[i] = NewRT(rt.cascades[i].res, RenderTextureFormat.ARGBFloat);

            rt.pack0 = NewRT(max, RenderTextureFormat.RGFloat);
            rt.pack1 = NewRT(max, RenderTextureFormat.RGFloat);
            rt.pack2 = NewRT(max, RenderTextureFormat.RGFloat);
            rt.pack3 = NewRT(max, RenderTextureFormat.RGFloat);
            rt.scratch = NewRT(max, RenderTextureFormat.RGFloat);

            // Arrays groupés par résolution (slices uniformes obligatoires).
            if (rt.count512 > 0)
            {
                rt.disp512  = NewArray(512, rt.count512, RenderTextureFormat.ARGBFloat);
                rt.deriv512 = NewArray(512, rt.count512, RenderTextureFormat.ARGBFloat);
            }
            if (rt.count256 > 0)
            {
                rt.disp256  = NewArray(256, rt.count256, RenderTextureFormat.ARGBFloat);
                rt.deriv256 = NewArray(256, rt.count256, RenderTextureFormat.ARGBFloat);
            }
        }

        static void Free(ref RenderTexture rt) { if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); rt = null; } }

        void ReleaseTextures(Runtime rt)
        {
            if (rt == null) return;
            if (rt.h0 != null) for (int i = 0; i < rt.h0.Length; i++) Free(ref rt.h0[i]);
            Free(ref rt.pack0); Free(ref rt.pack1); Free(ref rt.pack2); Free(ref rt.pack3); Free(ref rt.scratch);
            Free(ref rt.disp512); Free(ref rt.deriv512); Free(ref rt.disp256); Free(ref rt.deriv256);
            rt.h0Ready = false;
        }

        // =====================================================================
        //  Paramètres communs poussés au compute spectre (par cascade)
        // =====================================================================
        void SetSpectrumParams(ComputeShader cs, in CascadeDesc c, float time)
        {
            float state = oceanState.Effective;
            float windRad = windDirectionDeg.Effective * Mathf.Deg2Rad;
            // Master → vitesse de vent effective (dérivée, pattern Q12.1).
            float windSpeed = Mathf.Max(0.5f, windSpeedAtMax.Effective * Mathf.Lerp(0.15f, 1f, state));
            float amp = amplitude.Effective * Mathf.Lerp(0.1f, 1f, state);

            cs.SetInt("_N", c.res);
            cs.SetFloat("_L", c.length);
            cs.SetFloat("_Depth", depth.Effective);
            cs.SetInt("_UseTMA", useTMA.Effective ? 1 : 0);
            cs.SetVector("_WindDir", new Vector4(Mathf.Cos(windRad), Mathf.Sin(windRad), 0, 0));
            cs.SetFloat("_WindSpeed", windSpeed);
            cs.SetFloat("_Fetch", fetch.Effective);
            cs.SetFloat("_Gamma", gamma.Effective);
            cs.SetFloat("_Amplitude", amp);
            cs.SetFloat("_Choppiness", choppiness.Effective);
            cs.SetFloat("_BandLow", c.bandLow);
            cs.SetFloat("_BandHigh", c.bandHigh);
            cs.SetFloat("_Time", time);
            cs.SetInt("_Seed", (int)c.seed);
            cs.SetInt("_Slice", c.slice);
        }

        // =====================================================================
        //  H0 (one-shot par changement de paramètre)
        // =====================================================================
        void InitSpectrum(Runtime rt)
        {
            using (OceanProfiler.Spectrum.Auto())
            {
                for (int i = 0; i < 4; i++)
                {
                    var c = rt.cascades[i];
                    SetSpectrumParams(rt.spectrum, c, 0f);
                    rt.spectrum.SetTexture(rt.kInit, "_H0", rt.h0[i]);
                    int g = Mathf.Max(1, c.res / 8);
                    rt.spectrum.Dispatch(rt.kInit, g, g, 1);
                }
            }
            rt.h0Ready = true;
        }

        // =====================================================================
        //  Évolution + IFFT + assemblage (chaque frame)
        // =====================================================================
        void EvolveAndTransform(OceanApplyContext ctx, Runtime rt)
        {
            float t = ctx.time;
            using (OceanProfiler.Spectrum.Auto())
            {
                for (int i = 0; i < 4; i++)
                {
                    var c = rt.cascades[i];
                    SetSpectrumParams(rt.spectrum, c, t);
                    rt.spectrum.SetTexture(rt.kEvolve, "_H0", rt.h0[i]);
                    rt.spectrum.SetTexture(rt.kEvolve, "_Pack0", rt.pack0);
                    rt.spectrum.SetTexture(rt.kEvolve, "_Pack1", rt.pack1);
                    rt.spectrum.SetTexture(rt.kEvolve, "_Pack2", rt.pack2);
                    rt.spectrum.SetTexture(rt.kEvolve, "_Pack3", rt.pack3);
                    int g = Mathf.Max(1, c.res / 8);
                    rt.spectrum.Dispatch(rt.kEvolve, g, g, 1);

                    // 4 IFFT hermitiennes 2-en-1 (1 par paquet) — anti-bug n°3 : _NormScale=1 (sim).
                    Inverse2D(rt, rt.pack0, c.res);
                    Inverse2D(rt, rt.pack1, c.res);
                    Inverse2D(rt, rt.pack2, c.res);
                    Inverse2D(rt, rt.pack3, c.res);

                    // Assemblage vers l'array du bon groupe de résolution.
                    SetSpectrumParams(rt.spectrum, c, t);
                    rt.spectrum.SetTexture(rt.kAssemble, "_Pack0", rt.pack0);
                    rt.spectrum.SetTexture(rt.kAssemble, "_Pack1", rt.pack1);
                    rt.spectrum.SetTexture(rt.kAssemble, "_Pack2", rt.pack2);
                    rt.spectrum.SetTexture(rt.kAssemble, "_Pack3", rt.pack3);
                    rt.spectrum.SetTexture(rt.kAssemble, "_DispArray",  (c.group == 0) ? rt.disp512  : rt.disp256);
                    rt.spectrum.SetTexture(rt.kAssemble, "_DerivArray", (c.group == 0) ? rt.deriv512 : rt.deriv256);
                    rt.spectrum.Dispatch(rt.kAssemble, g, g, 1);
                }
            }
        }

        // IFFT 2D in-place (simulation) : ping-pong target<->scratch interne, _NormScale=1
        // (la somme butterfly brute EST η — anti-bug n°3, normalisation découplée de l'amplitude).
        void Inverse2D(Runtime rt, RenderTexture target, int n)
        {
            using (OceanProfiler.FFT.Auto())
                Transform2DInto(rt, target, rt.scratch, n, forward: false, normScale: 1.0f);
        }

        // Transformée 2D générique (séparable horizontal puis vertical), in-place sur `target`,
        // via ping-pong `target`<->`scratch`. log2(N)·2 passes butterfly (nombre pair -> résultat
        // dans `target`), suivies d'une passe de scale UNIQUEMENT si normScale != 1.
        //   forward=true  -> FFT directe (e^{-i})       ; forward=false -> IFFT (e^{+i})
        //   normScale     -> 1.0 (sim) ou 1/N² (round-trip d'identité).
        void Transform2DInto(Runtime rt, RenderTexture target, RenderTexture scratch, int n,
                             bool forward, float normScale)
        {
            int log2 = 0; for (int v = n; v > 1; v >>= 1) log2++;
            int g = Mathf.Max(1, n / 8);

            rt.fft.SetInt("_Resolution", n);
            rt.fft.SetInt("_Forward", forward ? 1 : 0);
            rt.fft.SetFloat("_NormScale", 1.0f);     // les butterfly n'appliquent jamais d'échelle

            RenderTexture src = target, dst = scratch;
            for (int axis = 0; axis < 2; axis++)
            {
                rt.fft.SetInt("_Axis", axis);
                for (int s = 0; s < log2; s++)
                {
                    rt.fft.SetInt("_Stage", s);
                    rt.fft.SetTexture(rt.kButterfly, "_Src", src);
                    rt.fft.SetTexture(rt.kButterfly, "_Dst", dst);
                    rt.fft.Dispatch(rt.kButterfly, g, g, 1);
                    var tmp = src; src = dst; dst = tmp;   // swap
                }
            }
            // 2·log2(N) passes (pair) -> src == target. Sécurité si jamais impair.
            if (src != target) Graphics.CopyTexture(src, target);

            // Passe de scale séparée (convention mathématique isolée de l'amplitude).
            if (normScale != 1.0f)
            {
                rt.fft.SetInt("_Resolution", n);
                rt.fft.SetFloat("_NormScale", normScale);
                rt.fft.SetTexture(rt.kScale, "_Src", target);
                rt.fft.SetTexture(rt.kScale, "_Dst", scratch);
                rt.fft.Dispatch(rt.kScale, g, g, 1);
                Graphics.CopyTexture(scratch, target);
            }
        }

        // =====================================================================
        //  P1.a — mesure go/no-go du ratio hermitien/naïf (1 cascade)
        // =====================================================================
        void MeasureHermitianRatio(OceanApplyContext ctx, Runtime rt)
        {
            var c = rt.cascades[0];
            int n = c.res;
            int passesPerIFFT;
            { int log2 = 0; for (int v = n; v > 1; v >>= 1) log2++; passesPerIFFT = 2 * log2; }

            // Chemin HERMITIEN 2-en-1 : 1 paquet (height + i·Dx) -> 1 IFFT.
            SetSpectrumParams(rt.spectrum, c, ctx.time);
            rt.spectrum.SetTexture(rt.kHerm, "_H0", rt.h0[0]);
            rt.spectrum.SetTexture(rt.kHerm, "_Pack0", rt.pack0);
            int g = Mathf.Max(1, n / 8);
            using (OceanProfiler.Spectrum.Auto()) rt.spectrum.Dispatch(rt.kHerm, g, g, 1);
            Inverse2D(rt, rt.pack0, n);
            rt.hermPasses = passesPerIFFT;          // 1 IFFT

            // Chemin NAÏF : 2 spectres séparés -> 2 IFFT.
            SetSpectrumParams(rt.spectrum, c, ctx.time);
            rt.spectrum.SetTexture(rt.kHeight, "_H0", rt.h0[0]);
            rt.spectrum.SetTexture(rt.kHeight, "_Pack0", rt.pack0);
            rt.spectrum.SetTexture(rt.kChoppy, "_H0", rt.h0[0]);
            rt.spectrum.SetTexture(rt.kChoppy, "_Pack1", rt.pack1);
            using (OceanProfiler.Spectrum.Auto())
            {
                rt.spectrum.Dispatch(rt.kHeight, g, g, 1);
                rt.spectrum.Dispatch(rt.kChoppy, g, g, 1);
            }
            Inverse2D(rt, rt.pack0, n);
            Inverse2D(rt, rt.pack1, n);
            rt.naivePasses = passesPerIFFT * 2;     // 2 IFFT

            rt.hermRatio = (float)rt.hermPasses / Mathf.Max(1, rt.naivePasses);
            rt.measured = true;

            Debug.Log($"[Ocean P1.a] Dé-risque IFFT hermitienne 2-en-1 (cascade {n}²) : " +
                      $"passes hermitien={rt.hermPasses}, naïf={rt.naivePasses}, ratio={rt.hermRatio:0.00} " +
                      $"(cible ≈0.50, gain 50%). Critère go/no-go = constat documenté (PAS un seuil ms — renvoyé au proto P2).");
        }

        // =====================================================================
        //  Test d'identité IFFT(FFT(x)) == x (valide la convention de normalisation)
        // =====================================================================
        //  Round-trip OUTILLÉ : signal connu -> FFT directe (no scale) -> IFFT (scale 1/N²)
        //  -> AsyncGPUReadback des deux -> erreur max -> log PASS/FAIL. Prouve que la
        //  convention de normalisation est correcte ET découplée de l'amplitude (anti-bug n°3) :
        //  le facteur 1/N² est purement mathématique, jamais lié à _Amplitude.
        void RunIdentityTest(Runtime rt)
        {
            const float kTol = 1e-2f;   // seuil d'erreur max (accumulation flottante sur 2·log2(N) passes)
            int n = 256;
            int g = Mathf.Max(1, n / 8);

            var signal = NewRT(n, RenderTextureFormat.RGFloat);   // signal d'origine (référence)
            var work   = NewRT(n, RenderTextureFormat.RGFloat);   // signal transformé (round-trip)
            var scr    = NewRT(n, RenderTextureFormat.RGFloat);   // ping-pong dédié

            try
            {
                // 1) Remplir le signal de test connu.
                int kFill = rt.fft.FindKernel("FFTFillTest");
                rt.fft.SetInt("_Resolution", n);
                rt.fft.SetTexture(kFill, "_Dst", signal);
                rt.fft.Dispatch(kFill, g, g, 1);

                // 2) Round-trip sur une copie : FFT directe (no scale) puis IFFT (scale 1/N²).
                Graphics.CopyTexture(signal, work);
                Transform2DInto(rt, work, scr, n, forward: true,  normScale: 1.0f);
                Transform2DInto(rt, work, scr, n, forward: false, normScale: 1.0f / ((float)n * (float)n));

                // 3) Readback synchrone (éditeur) et comparaison erreur max.
                var reqA = AsyncGPUReadback.Request(signal);
                var reqB = AsyncGPUReadback.Request(work);
                reqA.WaitForCompletion();
                reqB.WaitForCompletion();

                if (reqA.hasError || reqB.hasError)
                {
                    Debug.LogWarning("[Ocean P1] Test d'identité : AsyncGPUReadback en erreur — test non concluant.");
                    rt.identityDone = true;
                    return;
                }

                var a = reqA.GetData<float>();   // RGFloat -> 2 floats / texel
                var b = reqB.GetData<float>();
                int count = Mathf.Min(a.Length, b.Length);
                float maxErr = 0f; bool nan = false;
                for (int i = 0; i < count; i++)
                {
                    float d = Mathf.Abs(a[i] - b[i]);
                    if (float.IsNaN(d) || float.IsInfinity(d)) { nan = true; break; }
                    if (d > maxErr) maxErr = d;
                }

                bool pass = !nan && maxErr <= kTol;
                Debug.Log($"[Ocean P1] Test d'identité IFFT(FFT(x))==x (N={n}) : " +
                          $"{(pass ? "PASS" : "FAIL")} — erreur max = {(nan ? "NaN/Inf" : maxErr.ToString("0.000000"))} " +
                          $"(seuil {kTol}). _NormScale inverse = 1/N² (convention découplée de l'amplitude, anti-bug n°3).");
                if (!pass)
                    Debug.LogWarning("[Ocean P1] Test d'identité ÉCHOUÉ : la convention de normalisation FFT/IFFT " +
                                     "n'est pas un round-trip exact — à corriger AVANT de revendiquer l'anti-bug n°3.");
            }
            finally
            {
                Free(ref signal); Free(ref work); Free(ref scr);
                rt.identityDone = true;
            }
        }

        // =====================================================================
        //  Push des globaux (anti-bug n°1 : via ctx.globals, restaurables)
        // =====================================================================
        void PushGlobals(OceanApplyContext ctx, Runtime rt)
        {
            if (rt.disp512 != null)  ctx.globals.SetGlobalTexture(ID_Disp512,  rt.disp512);
            if (rt.deriv512 != null) ctx.globals.SetGlobalTexture(ID_Deriv512, rt.deriv512);
            if (rt.disp256 != null)  ctx.globals.SetGlobalTexture(ID_Disp256,  rt.disp256);
            if (rt.deriv256 != null) ctx.globals.SetGlobalTexture(ID_Deriv256, rt.deriv256);

            ctx.globals.SetGlobalFloat(ID_Count512, rt.count512);
            ctx.globals.SetGlobalFloat(ID_Count256, rt.count256);
            ctx.globals.SetGlobalFloat(ID_CascadeCount, 4);

            for (int i = 0; i < 4; i++)
            {
                var c = rt.cascades[i];
                // (longueur de tuile, groupe res, slice dans le groupe, résolution)
                ctx.globals.SetGlobalVector(ID_CascadeData[i],
                    new Vector4(c.length, c.group, c.slice, c.res));
            }
        }

        // =====================================================================
        //  Hash des paramètres spectraux (déclenche la réinit H0 uniquement
        //  sur changement RÉEL — jamais sur l'évolution temporelle, anti-bug n°2/windPulse)
        // =====================================================================
        int ComputeParamHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (int)cascadeQuality;
                h = h * 31 + masterTileLength.Effective.GetHashCode();
                h = h * 31 + bandBoundary.Effective.GetHashCode();
                h = h * 31 + oceanState.Effective.GetHashCode();
                h = h * 31 + windSpeedAtMax.Effective.GetHashCode();
                h = h * 31 + windDirectionDeg.Effective.GetHashCode();
                h = h * 31 + fetch.Effective.GetHashCode();
                h = h * 31 + gamma.Effective.GetHashCode();
                h = h * 31 + amplitude.Effective.GetHashCode();
                h = h * 31 + depth.Effective.GetHashCode();
                h = h * 31 + (useTMA.Effective ? 1 : 0);
                // NOTE : ni le temps, ni la choppiness (post-évolution) n'entrent ici :
                // ils ne doivent JAMAIS provoquer de recalcul de H0.
                return h;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            masterTileLength.value = Mathf.Clamp(masterTileLength.value, 50f, 2000f);
            bandBoundary.value     = Mathf.Clamp(bandBoundary.value, 2f, 12f);
            oceanState.value       = Mathf.Clamp01(oceanState.value);
            windSpeedAtMax.value   = Mathf.Clamp(windSpeedAtMax.value, 1f, 40f);
            windDirectionDeg.value = Mathf.Clamp(windDirectionDeg.value, 0f, 360f);
            fetch.value            = Mathf.Clamp(fetch.value, 1000f, 500000f);
            gamma.value            = Mathf.Clamp(gamma.value, 1f, 7f);
            amplitude.value        = Mathf.Clamp(amplitude.value, 0f, 4f);
            choppiness.value       = Mathf.Clamp(choppiness.value, 0f, 2f);
            depth.value            = Mathf.Clamp(depth.value, 1f, 500f);
        }
#endif
    }
}
