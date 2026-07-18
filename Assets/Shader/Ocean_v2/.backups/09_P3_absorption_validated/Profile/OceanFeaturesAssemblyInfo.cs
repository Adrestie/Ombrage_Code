// OceanFeaturesAssemblyInfo.cs  (Ocean_v2)
// Expose les membres `internal` de l'assembly runtime Ombrage.OceanFeatures au SEUL assembly de test
// EditMode (Ombrage.OceanFeatures.Tests). Permet au smoke test de couvrir la logique interne du
// durcissement 3C (OceanSurfaceModule.SameArrayDims) sans élargir la surface publique de production.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ombrage.OceanFeatures.Tests")]
