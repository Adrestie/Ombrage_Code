// OceanReflectionModule.cs
// Module RÉFLEXIONS — système unifié (ciel/cubemap + planar/SSR + sonde sous-marine).
// Le SSR ajoutera le bit stencil TraceReflectionRay (8) en P5 (Ref -> 10) — PAS en P0.
// P0 : STUB (aucune logique). Implémentation réelle en P5 (décision définitive du gating immergé).
namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Rendering/Reflection")]
    public class OceanReflectionModule : OceanFeatureModule
    {
        public override void Apply(OceanApplyContext ctx)
        {
            // P0 : no-op (scaffolding). Les réflexions arrivent en P5.
        }
    }
}
