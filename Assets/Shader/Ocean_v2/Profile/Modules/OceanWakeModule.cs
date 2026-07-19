// OceanWakeModule.cs
// Module SILLAGE — interaction dynamique (stamp RT de Kelvin / wind-packet FFT), UN SEUL chemin
// d'implémentation (l'ancien système avait 2 impls de wake = throwaway à NE PAS reproduire).
// STUB (aucune logique). Implémentation réelle dans une phase ultérieure (cf. ROADMAP).
namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Interaction/Wake")]
    public class OceanWakeModule : OceanFeatureModule
    {
        public override void Apply(OceanApplyContext ctx)
        {
            // No-op (scaffolding). Le sillage arrive dans une phase ultérieure.
        }
    }
}
