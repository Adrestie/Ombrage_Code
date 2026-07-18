using System;
using UnityEngine;

namespace Ombrage.Tools.Core.Settings
{
    /// <summary>
    /// Complete, serializable description of a rock/cliff to generate. This is the object
    /// persisted to JSON so a preset can be reloaded and tweaked later.
    /// </summary>
    [Serializable]
    public sealed class RockGenerationSettings
    {
        /// <summary>Schema version of the serialized payload. Increment on breaking changes.</summary>
        public const int CurrentFormatVersion = 1;

        public int formatVersion = CurrentFormatVersion;
        public string presetName = "New Rock";

        [Tooltip("Seed driving every deterministic part of the generation.")]
        public int seed = 12345;

        public GenerationMode mode = GenerationMode.Rock;
        public GenerationAlgorithm algorithm = GenerationAlgorithm.PrimitiveNoise;

        public PrimitiveNoiseParameters primitiveNoise = new PrimitiveNoiseParameters();
        public ConvexHullParameters convexHull = new ConvexHullParameters();
        public MarchingCubesParameters marchingCubes = new MarchingCubesParameters();
        public CliffParameters cliff = new CliffParameters();
        public FfdParameters ffd = new FfdParameters();

        /// <summary>Returns a fresh settings instance populated with sensible defaults.</summary>
        public static RockGenerationSettings CreateDefault() => new RockGenerationSettings();

        /// <summary>Returns a deep, independent copy of these settings.</summary>
        public RockGenerationSettings Clone()
            => JsonUtility.FromJson<RockGenerationSettings>(JsonUtility.ToJson(this));
    }
}
