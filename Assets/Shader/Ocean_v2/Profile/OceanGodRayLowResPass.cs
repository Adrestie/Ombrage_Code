// OceanGodRayLowResPass.cs  (Ocean_v2)
// CustomPass SCRIPTÉ : rend les god-rays en DEMI-RÉSOLUTION dans une RT dédiée (Pass 0), puis les
// composite en ADDITIF plein écran (Pass 1, Blend One One). Piloté par OceanVolumetricsModule qui crée
// le CustomPassVolume (BeforePostProcess) et assigne le matériau (Hidden/Ocean/GodRaysLowRes).
//
// Perf : le raymarch ne tourne que sur ¼ des pixels (demi-res). Les god-rays sont basse fréquence →
// l'upscale bilinéaire au composite est invisible. Occlusion géométrie ABSENTE en v1 (pas de depth).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;

namespace Ombrage.OceanFeatures
{
    class OceanGodRayLowResPass : CustomPass
    {
        public Material material;
        RTHandle m_RT;
        static readonly int ID_TargetSize = Shader.PropertyToID("_OceanGRTargetSize");
        static readonly int ID_GodRayTex  = Shader.PropertyToID("_OceanGodRayTex");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            m_RT = RTHandles.Alloc(Vector2.one * 0.5f, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "OceanGodRayLowRes");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null || m_RT == null) return;

            // Taille RÉELLE de la RT demi-res pour cette caméra → NDC correct côté shader (_ScreenSize est plein écran).
            Vector2Int full = new Vector2Int(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight);
            Vector2Int rt   = m_RT.GetScaledSize(full);
            ctx.cmd.SetGlobalVector(ID_TargetSize, new Vector4(rt.x, rt.y, 1f / Mathf.Max(1, rt.x), 1f / Mathf.Max(1, rt.y)));

            // Pass 0 : god-rays dans la RT demi-res (clear noir d'abord).
            CoreUtils.SetRenderTarget(ctx.cmd, m_RT, ClearFlag.Color, Color.clear);
            CoreUtils.DrawFullScreen(ctx.cmd, material, m_RT, null, shaderPassId: 0);

            // Pass 1 : composite ADDITIF dans le color buffer plein écran.
            ctx.cmd.SetGlobalTexture(ID_GodRayTex, m_RT);
            CoreUtils.DrawFullScreen(ctx.cmd, material, ctx.cameraColorBuffer, null, shaderPassId: 1);
        }

        protected override void Cleanup()
        {
            m_RT?.Release();
            m_RT = null;
        }
    }
}
