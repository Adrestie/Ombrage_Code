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

            // God-rays dans la RT demi-res (clear noir d'abord), puis BIND en global : le composite additif est
            // fait à la FIN du shader underwater (ordre garanti car cette passe est injectée plus tôt).
            CoreUtils.SetRenderTarget(ctx.cmd, m_RT, ClearFlag.Color, Color.clear);
            CoreUtils.DrawFullScreen(ctx.cmd, material, m_RT, null, shaderPassId: 0);
            ctx.cmd.SetGlobalTexture(ID_GodRayTex, m_RT);
        }

        protected override void Cleanup()
        {
            m_RT?.Release();
            m_RT = null;
        }
    }
}
