// OceanUnderwaterModule.cs
// Module SOUS-MARIN — vue immergée : fog volumétrique, god-rays, fenêtre de Snell, caustiques
// (les caustiques sont une feature d'underwater, PAS un module dédié). Gating immergé ancré P6.
// P0 : STUB (aucune logique). Implémentation réelle en P6.
namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Underwater/Underwater")]
    public class OceanUnderwaterModule : OceanFeatureModule
    {
        public override void Apply(OceanApplyContext ctx)
        {
            // P0 : no-op (scaffolding). Le sous-marin arrive en P6.
        }
    }
}
