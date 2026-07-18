using System;
using UnityEngine;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Serialization
{
    /// <summary>Thrown when a settings payload cannot be deserialized.</summary>
    public sealed class SettingsDeserializationException : Exception
    {
        public SettingsDeserializationException(string message) : base(message) { }
        public SettingsDeserializationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// <see cref="ISettingsSerializer"/> backed by Unity's <see cref="JsonUtility"/>. Chosen
    /// over Newtonsoft to keep the Core assembly free of external dependencies.
    /// </summary>
    public sealed class JsonSettingsSerializer : ISettingsSerializer
    {
        public string Serialize(RockGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return JsonUtility.ToJson(settings, prettyPrint: true);
        }

        public RockGenerationSettings Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                throw new ArgumentException("Settings payload is null or empty.", nameof(payload));

            RockGenerationSettings settings;
            try
            {
                settings = JsonUtility.FromJson<RockGenerationSettings>(payload);
            }
            catch (Exception inner)
            {
                throw new SettingsDeserializationException("Settings payload is not valid JSON.", inner);
            }

            if (settings == null)
                throw new SettingsDeserializationException("Settings payload deserialized to null.");

            if (settings.formatVersion > RockGenerationSettings.CurrentFormatVersion)
            {
                throw new SettingsDeserializationException(
                    $"Settings format version {settings.formatVersion} is newer than supported " +
                    $"({RockGenerationSettings.CurrentFormatVersion}). Update the tool.");
            }

            return settings;
        }
    }
}
