// OceanGodRayLowResPass.cs  (Ocean_v2)
// CustomPass SCRIPTÉ : rend les god-rays en DEMI-RÉSOLUTION dans une RT dédiée, puis la BIND en global
// (_OceanGodRayTex). Le COMPOSITE (échantillonnage + ajout additif) est fait À LA FIN du shader underwater
// (OceanUnderwater.shader) — PAS ici — pour garantir l'ordre : injecté à AfterOpaqueDepthAndNormal, la RT
// est prête avant que le fog underwater (BeforePostProcess) ne la lise. Piloté par OceanVolumetricsModule
// qui crée le CustomPassVolume et assigne le matériau (Hidden/Ocean/GodRaysLowRes).
//
// Perf : le raymarch ne tourne que sur ¼ des pixels (demi-res). Les god-rays sont basse fréquence →
// l'upscale bilinéaire au composite est invisible. Occlusion géométrie ABSENTE en v1 (pas de depth).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;

namespace Ombrage.OceanFeatures
{
    // [Serializable] REQUIS : HDRP n'exécute une sous-classe CustomPass que si elle est sérialisable
    // (comme FullScreenCustomPass). Sans ça, la passe ajoutée au CustomPassVolume est ignorée.
    [System.Serializable]
    class OceanGodRayLowResPass : CustomPass
    {
        public Material material;
        RTHandle m_RT;       // god-rays (draw), puis résultat FLOUTÉ final (après blur V)
        RTHandle m_RTBlur;   // ping-pong intermédiaire (après blur H)
        static readonly int ID_TargetSize = Shader.PropertyToID("_OceanGRTargetSize");
        static readonly int ID_GodRayTex  = Shader.PropertyToID("_OceanGodRayTex");
        static readonly int ID_BlurSource = Shader.PropertyToID("_OceanGRSource");
        static readonly int ID_BlurDir    = Shader.PropertyToID("_OceanGRBlurDir");

        const int kPassGodRays = 0;
        const int kPassBlur    = 1;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            m_RT = RTHandles.Alloc(Vector2.one * 0.5f, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "OceanGodRayLowRes");
            m_RTBlur = RTHandles.Alloc(Vector2.one * 0.5f, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "OceanGodRayLowResBlur");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null || m_RT == null || m_RTBlur == null) return;

            // Taille RÉELLE de la RT demi-res pour cette caméra → NDC correct côté shader (_ScreenSize est plein écran).
            Vector2Int full = new Vector2Int(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight);
            Vector2Int rt   = m_RT.GetScaledSize(full);
            ctx.cmd.SetGlobalVector(ID_TargetSize, new Vector4(rt.x, rt.y, 1f / Mathf.Max(1, rt.x), 1f / Mathf.Max(1, rt.y)));

            // 1) God-rays dans la RT demi-res (clear noir d'abord).
            CoreUtils.SetRenderTarget(ctx.cmd, m_RT, ClearFlag.Color, Color.clear);
            CoreUtils.DrawFullScreen(ctx.cmd, material, m_RT, null, shaderPassId: kPassGodRays);

            // 2) Flou séparable (ping-pong) : H (m_RT → m_RTBlur) puis V (m_RTBlur → m_RT). Lisse le grain HF.
            ctx.cmd.SetGlobalTexture(ID_BlurSource, m_RT);
            ctx.cmd.SetGlobalVector(ID_BlurDir, new Vector4(1f, 0f, 0f, 0f));
            CoreUtils.SetRenderTarget(ctx.cmd, m_RTBlur, ClearFlag.Color, Color.clear);
            CoreUtils.DrawFullScreen(ctx.cmd, material, m_RTBlur, null, shaderPassId: kPassBlur);

            ctx.cmd.SetGlobalTexture(ID_BlurSource, m_RTBlur);
            ctx.cmd.SetGlobalVector(ID_BlurDir, new Vector4(0f, 1f, 0f, 0f));
            CoreUtils.SetRenderTarget(ctx.cmd, m_RT, ClearFlag.Color, Color.clear);
            CoreUtils.DrawFullScreen(ctx.cmd, material, m_RT, null, shaderPassId: kPassBlur);

            // 3) BIND le résultat flouté en global : le composite additif est fait à la FIN du shader underwater
            // (ordre garanti car cette passe est injectée plus tôt : AfterOpaqueDepthAndNormal).
            ctx.cmd.SetGlobalTexture(ID_GodRayTex, m_RT);
        }

        protected override void Cleanup()
        {
            m_RT?.Release();
            m_RTBlur?.Release();
            m_RT = null;
            m_RTBlur = null;
        }
    }
}
