// OceanFoamFeature.cs  (Ocean_v2)
// FEATURE écume du module surface. Pipeline CANONIQUE dans une carte world-locked :
//
//   Spectre (intact) ─► ExtractMoments ─► arrays MOMENTS (J, J²) mippés (μ, E[J²] par mip — Dupuy)
//                                        └─► FoamAccumulate : erf(ε, μ, σ²) au footprint du texel
//                                            de carte + persistance → carte _OceanFoam
//
// La carte (ping-pong RHalf, mippée) a résolution/étendue PROPRES → écume découplée de la longueur
// de tuile. La surface l'échantillonne en décal. Cette classe ne pousse AUCUN global (les binds
// passent par OceanSurfaceModule.BindFoam via ctx.globals — anti-bug n°1).
// Cycle de vie symétrique strict (anti-fuite [ExecuteAlways]) : Dispose au OnModuleDisable.
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    public sealed class OceanFoamFeature
    {
        // Bindings kernels (OceanFoam.compute).
        static readonly int SP_DispIn     = Shader.PropertyToID("_FoamDispIn");
        static readonly int SP_MomentsOut = Shader.PropertyToID("_FoamMomentsOut");
        static readonly int SP_Moments512 = Shader.PropertyToID("_FoamMoments512");
        static readonly int SP_Moments256 = Shader.PropertyToID("_FoamMoments256");
        static readonly int SP_Prev       = Shader.PropertyToID("_FoamPrev");
        static readonly int SP_Out        = Shader.PropertyToID("_FoamOut");
        static readonly int SP_C0         = Shader.PropertyToID("_FoamCascade0");
        static readonly int SP_C1         = Shader.PropertyToID("_FoamCascade1");
        static readonly int SP_C2         = Shader.PropertyToID("_FoamCascade2");
        static readonly int SP_C3         = Shader.PropertyToID("_FoamCascade3");
        static readonly int SP_Count      = Shader.PropertyToID("_FoamCascadeCount");
        static readonly int SP_Extent     = Shader.PropertyToID("_FoamExtent");
        static readonly int SP_Res        = Shader.PropertyToID("_FoamResolution");
        static readonly int SP_Threshold  = Shader.PropertyToID("_FoamThreshold");
        static readonly int SP_Softness   = Shader.PropertyToID("_FoamSoftness");
        static readonly int SP_Fade       = Shader.PropertyToID("_FoamFadeRate");
        static readonly int SP_Dt         = Shader.PropertyToID("_FoamDeltaTime");

        // Moments (J, J²) par groupe de résolution — miroir strict des arrays du spectre, mippés.
        RenderTexture m_Moments512, m_Moments256;
        // Carte d'écume ping-pong.
        RenderTexture m_A, m_B;
        bool m_AisCurrent = true;
        int m_Res;
        int m_KExtract = -1, m_KAccum = -1;
        ComputeShader m_Owner;
        // Horloge RÉELLE pour la décroissance : Time.deltaTime vaut ~0 hors Play ([ExecuteAlways])
        // → la traînée ne se dissipait JAMAIS en mode édition (fossile d'écume — bug corrigé).
        double m_LastTime = -1.0;

        /// Carte d'accumulation la plus récente (lue par la surface). null tant qu'aucune dispatch.
        public RenderTexture Current => m_A == null ? null : (m_AisCurrent ? m_A : m_B);

        public void Dispatch(ComputeShader cs, int res, Texture disp512, Texture disp256,
                             Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3, float count,
                             float extent, float threshold, float softness, float fade)
        {
            if (cs == null || (disp512 == null && disp256 == null)) return;

            // dt RÉEL (édition + play) : delta d'horloge monotone, clampé (pauses/domain reload).
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = (m_LastTime < 0.0) ? 0f : Mathf.Clamp((float)(now - m_LastTime), 0f, 0.1f);
            m_LastTime = now;
            EnsureTargets(res);
            MirrorOne(disp512 as RenderTexture, ref m_Moments512);
            MirrorOne(disp256 as RenderTexture, ref m_Moments256);
            if (m_Owner != cs) { m_KExtract = cs.FindKernel("ExtractMoments"); m_KAccum = cs.FindKernel("FoamAccumulate"); m_Owner = cs; }

            using (OceanProfiler.Foam.Auto())
            {
                // [1] Moments (J, J²) + mips (μ / E[J²] par mip — pré-filtrage Dupuy).
                ExtractOne(cs, m_KExtract, disp512 as RenderTexture, m_Moments512);
                ExtractOne(cs, m_KExtract, disp256 as RenderTexture, m_Moments256);

                // [2] Couverture erf + persistance dans la carte (ping-pong).
                RenderTexture prev  = m_AisCurrent ? m_A : m_B;
                RenderTexture outRT = m_AisCurrent ? m_B : m_A;
                // Groupe absent (ex. Low sans 512²) : jamais échantillonné (aucune cascade ne le
                // pointe) mais DOIT être bindé → bouche-trou avec le groupe présent.
                RenderTexture mo512 = m_Moments512 != null ? m_Moments512 : m_Moments256;
                RenderTexture mo256 = m_Moments256 != null ? m_Moments256 : m_Moments512;
                cs.SetTexture(m_KAccum, SP_Moments512, mo512);
                cs.SetTexture(m_KAccum, SP_Moments256, mo256);
                cs.SetTexture(m_KAccum, SP_Prev, prev);
                cs.SetTexture(m_KAccum, SP_Out, outRT);
                cs.SetVector(SP_C0, c0); cs.SetVector(SP_C1, c1); cs.SetVector(SP_C2, c2); cs.SetVector(SP_C3, c3);
                cs.SetFloat(SP_Count, count);
                cs.SetFloat(SP_Extent, extent);
                cs.SetFloat(SP_Res, res);
                cs.SetFloat(SP_Threshold, threshold);
                cs.SetFloat(SP_Softness, Mathf.Max(1e-4f, softness));
                cs.SetFloat(SP_Fade, Mathf.Max(0f, fade));
                cs.SetFloat(SP_Dt, dt);
                int g = (res + 7) / 8;
                cs.Dispatch(m_KAccum, g, g, 1);
                outRT.GenerateMips();        // AA distance côté surface (décal mippé)
                m_AisCurrent = !m_AisCurrent;   // outRT devient Current
            }
        }

        void ExtractOne(ComputeShader cs, int kernel, RenderTexture src, RenderTexture dst)
        {
            if (src == null || dst == null) return;
            cs.SetTexture(kernel, SP_DispIn, src);
            cs.SetTexture(kernel, SP_MomentsOut, dst);
            cs.Dispatch(kernel, src.width / 8, src.height / 8, Mathf.Max(1, dst.volumeDepth));
            dst.GenerateMips();   // vérifié fonctionnel sur RT Tex2DArray (dé-risque, 2026-07-17)
        }

        static void MirrorOne(RenderTexture src, ref RenderTexture dst)
        {
            if (src == null) { ReleaseOne(ref dst); return; }
            if (dst != null && dst.width == src.width && dst.volumeDepth == src.volumeDepth) return;
            ReleaseOne(ref dst);
            dst = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.RGHalf)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = Mathf.Max(1, src.volumeDepth),
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                name = "OceanFoamMoments"
            };
            dst.Create();
        }

        void EnsureTargets(int res)
        {
            if (m_A != null && m_Res == res) return;
            ReleaseOne(ref m_A);
            ReleaseOne(ref m_B);
            m_Res = res;
            m_A = NewFoamRT(res);
            m_B = NewFoamRT(res);
            ClearZero(m_A);
            ClearZero(m_B);   // départ sans écume → aucun flash
            m_AisCurrent = true;
        }

        static RenderTexture NewFoamRT(int res)
        {
            var rt = new RenderTexture(res, res, 0, RenderTextureFormat.RHalf)
            {
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,     // world-locked : hors carte = bord (pas de tuilage)
                filterMode = FilterMode.Trilinear,
                name = "OceanFoam"
            };
            rt.Create();
            return rt;
        }

        static void ClearZero(RenderTexture rt)
        {
            var active = RenderTexture.active;
            Graphics.SetRenderTarget(rt);
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;
        }

        public void Dispose()
        {
            ReleaseOne(ref m_Moments512);
            ReleaseOne(ref m_Moments256);
            ReleaseOne(ref m_A);
            ReleaseOne(ref m_B);
            m_KExtract = m_KAccum = -1;
            m_Owner = null;
        }

        static void ReleaseOne(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Object.Destroy(rt);
            else Object.DestroyImmediate(rt);
            rt = null;
        }
    }
}
