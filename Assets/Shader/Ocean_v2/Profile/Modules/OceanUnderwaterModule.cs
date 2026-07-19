// OceanUnderwaterModule.cs  (Ocean_v2)
// Module SOUS-MARIN — vue immergée reconstruite par un CustomPass HDRP séparé
// (post-GBuffer, injection BeforePostProcess). Étages :
//   - ABSORPTION Beer-Lambert de la colonne d'eau, σ PARTAGÉ (_WaterAbsorption).
//   - fenêtre de Snell (θc≈48.6°) — à venir dans le shader du pass.
//   - fog volumétrique + god-rays = VOLUMETRICS HDRP natifs pilotés par σ.
//   - éclairage sous-marin = modulation NON destructive (anti-bug n°1).
//
// Le gating immergé : le pass est ACTIF (via un global) uniquement quand la caméra principale
// est SOUS le niveau d'eau. Aucune mutation destructive d'état partagé (les god-rays/soleil).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Underwater")]
    public class OceanUnderwaterModule : OceanFeatureModule
    {
        // Valeurs à OVERRIDE (niveau 2, cf. Reflection). Décoché = défaut ; cocher = saisie. Clamp en OnValidate.
        [Header("Sous-marin (CustomPass BeforePostProcess)")]
        [Tooltip("Densité artistique de l'absorption immergée (× la profondeur perçue par la vue). 1 = physique.")]
        public OceanFloatParameter underwaterDensity = new OceanFloatParameter(1f);

        [Tooltip("Demi-angle du cône de la fenêtre de Snell (°). Physique de l'eau ≈ 48.6°. Règle la taille de la fenêtre (la réfraction en sera dérivée).")]
        public OceanFloatParameter snellCriticalAngleDeg = new OceanFloatParameter(48.6f);

        const string kShaderName = "Hidden/Ocean/Underwater";
        const string kShaderPath = "Assets/Shader/Ocean_v2/Shaders/OceanUnderwater.shader";
        const string kPassName   = "Underwater";

        static readonly int P_UnderwaterEnabled = Shader.PropertyToID("_OceanUnderwaterEnabled");
        static readonly int P_UnderwaterDist    = Shader.PropertyToID("_OceanUnderwaterDistScale");
        static readonly int P_SnellCosThetaC    = Shader.PropertyToID("_OceanSnellCosThetaC");

        // Passe utilitaire : rebinde le stencil de la depth caméra sur _StencilTexture afin que la
        // FullScreenPass immergée puisse LIRE le tag de surface (UserBit0, posé sur le GBuffer).
        // Un FullScreen CustomPass HDRP ne reçoit PAS _StencilTexture (constaté : lecture = 0 →
        // écran noir en diagnostic). On rebinde la ressource CANONIQUE — le MÊME stencil caméra que HDRP
        // fournit à TAA/SSR — donc aucun état étranger introduit (anti-bug n°1). Gatée (enabled) par Apply.
        sealed class BindCameraStencilPass : CustomPass
        {
            static readonly int s_StencilTexture = Shader.PropertyToID("_StencilTexture");
            protected override void Execute(CustomPassContext ctx)
                => ctx.cmd.SetGlobalTexture(s_StencilTexture, ctx.cameraDepthBuffer, RenderTextureSubElement.Stencil);
        }

        sealed class Runtime
        {
            public GameObject go;
            public CustomPassVolume volume;
            public BindCameraStencilPass bindPass;
            public FullScreenCustomPass pass;
            public Material material;
        }

        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            var rt = new Runtime();
            EnsurePass(ctx, rt);
            ctx.SetRuntime(this, rt);
        }

        public override void OnModuleDisable(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt != null)
            {
                if (rt.go != null) DestroyObj(rt.go);
                if (rt.material != null) DestroyObj(rt.material);
            }
            ctx.SetRuntime(this, null);
            // Les globaux (_OceanUnderwater*) sont restaurés neutres par OceanSystem.Teardown (anti-bug n°1).
        }

        public override void Apply(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as Runtime;
            if (rt == null) { OnModuleEnable(ctx); rt = ctx.GetRuntime(this) as Runtime; }
            if (rt == null) return;
            EnsurePass(ctx, rt);

            float waterY = ctx.system != null ? ctx.system.transform.position.y : 0f;
            bool submerged = PrimaryCameraSubmerged(waterY);

            // GATING : le pass ne fait effet qu'en immersion (le CustomPass tourne toujours mais court-circuite
            // via _OceanUnderwaterEnabled=0 → coût négligeable émergé). Push SET pur (anti-bug n°1).
            ctx.globals.SetGlobalFloat(P_UnderwaterEnabled, submerged ? 1f : 0f);
            ctx.globals.SetGlobalFloat(P_UnderwaterDist, underwaterDensity.Effective);
            ctx.globals.SetGlobalFloat(P_SnellCosThetaC, Mathf.Cos(snellCriticalAngleDeg.Effective * Mathf.Deg2Rad));
            if (rt.bindPass != null) rt.bindPass.enabled = submerged;   // ne rebinde _StencilTexture qu'immergé (anti-bug n°1)
        }

        void EnsurePass(OceanApplyContext ctx, Runtime rt)
        {
            if (rt.go != null && rt.pass != null) return;

            if (rt.material == null)
            {
                var sh = Shader.Find(kShaderName);
#if UNITY_EDITOR
                if (sh == null) sh = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(kShaderPath);
#endif
                if (sh == null) { Debug.LogWarning("[Ocean] Shader '" + kShaderName + "' introuvable — sous-marin inactif."); return; }
                rt.material = new Material(sh) { name = "OceanUnderwater (auto)", hideFlags = HideFlags.DontSave };
            }

            if (rt.go == null)
            {
                rt.go = new GameObject("OceanUnderwater (runtime)") { hideFlags = HideFlags.HideAndDontSave };
                if (ctx.system != null) rt.go.transform.SetParent(ctx.system.transform, worldPositionStays: false);
                rt.volume = rt.go.AddComponent<CustomPassVolume>();
                rt.volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
                rt.volume.isGlobal = true;

                // AVANT la FullScreenPass : le SetGlobalTexture de la passe de bind est enregistré sur le
                // command buffer de l'injection et s'exécute donc avant le blit immergé qui lit _StencilTexture.
                rt.bindPass = new BindCameraStencilPass { name = "OceanBindStencil", enabled = false };
                rt.volume.customPasses.Add(rt.bindPass);

                rt.pass = new FullScreenCustomPass
                {
                    name = "OceanUnderwater",
                    fullscreenPassMaterial = rt.material,
                    materialPassName = kPassName,
                    fetchColorBuffer = true   // le shader échantillonne la couleur caméra
                };
                rt.volume.customPasses.Add(rt.pass);
            }
        }

        static bool PrimaryCameraSubmerged(float waterY)
        {
            var cam = Camera.main;
            return cam != null && cam.transform.position.y < waterY;
        }

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            underwaterDensity.value     = Mathf.Clamp(underwaterDensity.value, 0.1f, 4f);
            snellCriticalAngleDeg.value = Mathf.Clamp(snellCriticalAngleDeg.value, 35f, 65f);
        }
#endif
    }
}
