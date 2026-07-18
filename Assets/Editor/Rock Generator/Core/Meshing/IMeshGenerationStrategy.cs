using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>
    /// A mesh generation algorithm. One implementation per <see cref="GenerationAlgorithm"/>.
    /// Adding a new algorithm must not require modifying existing strategies (Open/Closed).
    /// </summary>
    public interface IMeshGenerationStrategy
    {
        /// <summary>The algorithm this strategy implements.</summary>
        GenerationAlgorithm Algorithm { get; }

        /// <summary>
        /// Generates mesh data from the given settings. Implementations must be deterministic
        /// for a fixed <see cref="RockGenerationSettings.seed"/>.
        /// </summary>
        MeshData Generate(RockGenerationSettings settings);
    }
}
