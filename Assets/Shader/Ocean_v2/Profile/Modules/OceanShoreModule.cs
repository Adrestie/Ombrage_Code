// OceanShoreModule.cs
// Module RIVAGE — intersection océan/terrain (détection GPU), atténuation et écume de rivage,
// crêtes côtières (crêtes SEULES, conformément aux décisions verrouillées).
// STUB (aucune logique). Implémentation réelle dans une phase ultérieure (cf. ROADMAP).
namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Interaction/Shore")]
    public class OceanShoreModule : OceanFeatureModule
    {
        public override void Apply(OceanApplyContext ctx)
        {
            // No-op (scaffolding). Le rivage arrive dans une phase ultérieure.
        }
    }
}
