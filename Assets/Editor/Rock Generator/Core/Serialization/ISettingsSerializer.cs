using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Serialization
{
    /// <summary>Serializes and deserializes <see cref="RockGenerationSettings"/> presets.</summary>
    public interface ISettingsSerializer
    {
        /// <summary>Serializes settings to a string payload.</summary>
        string Serialize(RockGenerationSettings settings);

        /// <summary>Deserializes a payload back into settings. Throws on malformed input.</summary>
        RockGenerationSettings Deserialize(string payload);
    }
}
