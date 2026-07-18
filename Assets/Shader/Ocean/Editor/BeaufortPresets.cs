using UnityEngine;

namespace Ocean
{
    public enum BeaufortLevel
    {
        Calm,       // 0
        Light,      // 1-2
        Moderate,   // 3-4
        Fresh,      // 5-6
        Gale,       // 7-8
        Storm,      // 9-10
        Hurricane   // 11-12
    }

    public struct BeaufortData
    {
        // Spectrum / Wind
        public float windSpeedFactor;
        public float amplitude;
        public float choppiness;
        public float smallWaveCutoff;
        public float timeScale;

        // Foam
        public float foamThreshold;
        public float foamDecay;
        public float foamStrength;

        // Shading
        public Color shallowColor;
        public Color deepColor;
        public Color sssColor;
        public float oceanRoughness;
        public float reflectionIntensity;
        public float fresnelPower;
        public float sssIntensity;
        public float sssPower;
        public float sssSpread;
        public float ambientStrength;
        public float wrapDiffuse;
        public float specularIntensity;
        public float heightScale;

        // Depth
        public float depthAbsorption;
        public float depthMaxDistance;
        public Color absorptionColor;
    }

    public static class BeaufortPresets
    {
        public static readonly string[] Labels =
        {
            "0 – Calm",
            "1-2 – Light",
            "3-4 – Moderate",
            "5-6 – Fresh",
            "7-8 – Gale",
            "9-10 – Storm",
            "11-12 – Hurricane"
        };

        public static readonly string[] Descriptions =
        {
            "Sea like a mirror. No wind.",
            "Ripples, light air. Small wavelets.",
            "Small waves, some whitecaps beginning to form.",
            "Moderate waves, frequent whitecaps, some spray.",
            "Large waves, foam blown in streaks, spray.",
            "Very high waves, dense foam, reduced visibility.",
            "Phenomenal seas, air filled with foam and spray."
        };

        public static BeaufortData GetPreset(BeaufortLevel level)
        {
            return level switch
            {
                BeaufortLevel.Calm => new BeaufortData
                {
                    windSpeedFactor    = 1f,
                    amplitude          = 0.05f,
                    choppiness         = 0.3f,
                    smallWaveCutoff    = 0.005f,
                    timeScale          = 0.6f,
                    foamThreshold      = 1.5f,
                    foamDecay          = 0.5f,
                    foamStrength       = 0.2f,
                    shallowColor       = new Color(0.15f, 0.80f, 0.70f),
                    deepColor          = new Color(0.03f, 0.15f, 0.30f),
                    sssColor           = new Color(0.10f, 0.65f, 0.45f),
                    oceanRoughness     = 0.02f,
                    reflectionIntensity = 1.0f,
                    fresnelPower       = 5f,
                    sssIntensity       = 1.0f,
                    sssPower           = 3f,
                    sssSpread          = 0.8f,
                    ambientStrength    = 0.20f,
                    wrapDiffuse        = 0.3f,
                    specularIntensity  = 1.2f,
                    heightScale        = 1f,
                    depthAbsorption    = 0.20f,
                    depthMaxDistance    = 25f,
                    absorptionColor    = new Color(0.01f, 0.05f, 0.10f),
                },

                BeaufortLevel.Light => new BeaufortData
                {
                    windSpeedFactor    = 3f,
                    amplitude          = 0.15f,
                    choppiness         = 0.6f,
                    smallWaveCutoff    = 0.003f,
                    timeScale          = 0.8f,
                    foamThreshold      = 1.0f,
                    foamDecay          = 0.7f,
                    foamStrength       = 0.5f,
                    shallowColor       = new Color(0.12f, 0.78f, 0.68f),
                    deepColor          = new Color(0.03f, 0.14f, 0.28f),
                    sssColor           = new Color(0.10f, 0.62f, 0.43f),
                    oceanRoughness     = 0.03f,
                    reflectionIntensity = 0.95f,
                    fresnelPower       = 5f,
                    sssIntensity       = 1.2f,
                    sssPower           = 3f,
                    sssSpread          = 0.9f,
                    ambientStrength    = 0.18f,
                    wrapDiffuse        = 0.3f,
                    specularIntensity  = 1.1f,
                    heightScale        = 1f,
                    depthAbsorption    = 0.25f,
                    depthMaxDistance    = 22f,
                    absorptionColor    = new Color(0.01f, 0.05f, 0.09f),
                },

                BeaufortLevel.Moderate => new BeaufortData
                {
                    windSpeedFactor    = 7f,
                    amplitude          = 0.4f,
                    choppiness         = 1.0f,
                    smallWaveCutoff    = 0.002f,
                    timeScale          = 1.0f,
                    foamThreshold      = 0.6f,
                    foamDecay          = 0.8f,
                    foamStrength       = 1.0f,
                    shallowColor       = new Color(0.10f, 0.75f, 0.65f),
                    deepColor          = new Color(0.02f, 0.12f, 0.25f),
                    sssColor           = new Color(0.10f, 0.60f, 0.40f),
                    oceanRoughness     = 0.05f,
                    reflectionIntensity = 0.85f,
                    fresnelPower       = 5f,
                    sssIntensity       = 1.5f,
                    sssPower           = 3f,
                    sssSpread          = 1.0f,
                    ambientStrength    = 0.15f,
                    wrapDiffuse        = 0.3f,
                    specularIntensity  = 1.0f,
                    heightScale        = 1f,
                    depthAbsorption    = 0.30f,
                    depthMaxDistance    = 20f,
                    absorptionColor    = new Color(0.01f, 0.04f, 0.08f),
                },

                BeaufortLevel.Fresh => new BeaufortData
                {
                    windSpeedFactor    = 12f,
                    amplitude          = 0.8f,
                    choppiness         = 1.2f,
                    smallWaveCutoff    = 0.001f,
                    timeScale          = 1.0f,
                    foamThreshold      = 0.4f,
                    foamDecay          = 0.85f,
                    foamStrength       = 1.5f,
                    shallowColor       = new Color(0.08f, 0.60f, 0.50f),
                    deepColor          = new Color(0.02f, 0.10f, 0.22f),
                    sssColor           = new Color(0.08f, 0.50f, 0.35f),
                    oceanRoughness     = 0.08f,
                    reflectionIntensity = 0.75f,
                    fresnelPower       = 4.5f,
                    sssIntensity       = 1.8f,
                    sssPower           = 2.5f,
                    sssSpread          = 1.2f,
                    ambientStrength    = 0.13f,
                    wrapDiffuse        = 0.35f,
                    specularIntensity  = 0.9f,
                    heightScale        = 1f,
                    depthAbsorption    = 0.35f,
                    depthMaxDistance    = 18f,
                    absorptionColor    = new Color(0.01f, 0.03f, 0.07f),
                },

                BeaufortLevel.Gale => new BeaufortData
                {
                    windSpeedFactor    = 20f,
                    amplitude          = 1.5f,
                    choppiness         = 1.4f,
                    smallWaveCutoff    = 0.0008f,
                    timeScale          = 1.1f,
                    foamThreshold      = 0.25f,
                    foamDecay          = 0.90f,
                    foamStrength       = 2.5f,
                    shallowColor       = new Color(0.06f, 0.45f, 0.40f),
                    deepColor          = new Color(0.02f, 0.08f, 0.18f),
                    sssColor           = new Color(0.06f, 0.40f, 0.30f),
                    oceanRoughness     = 0.12f,
                    reflectionIntensity = 0.65f,
                    fresnelPower       = 4f,
                    sssIntensity       = 2.0f,
                    sssPower           = 2f,
                    sssSpread          = 1.5f,
                    ambientStrength    = 0.12f,
                    wrapDiffuse        = 0.4f,
                    specularIntensity  = 0.7f,
                    heightScale        = 1.2f,
                    depthAbsorption    = 0.45f,
                    depthMaxDistance    = 15f,
                    absorptionColor    = new Color(0.01f, 0.03f, 0.06f),
                },

                BeaufortLevel.Storm => new BeaufortData
                {
                    windSpeedFactor    = 28f,
                    amplitude          = 3.0f,
                    choppiness         = 1.6f,
                    smallWaveCutoff    = 0.0005f,
                    timeScale          = 1.2f,
                    foamThreshold      = 0.15f,
                    foamDecay          = 0.92f,
                    foamStrength       = 3.5f,
                    shallowColor       = new Color(0.04f, 0.30f, 0.28f),
                    deepColor          = new Color(0.01f, 0.06f, 0.14f),
                    sssColor           = new Color(0.05f, 0.30f, 0.22f),
                    oceanRoughness     = 0.20f,
                    reflectionIntensity = 0.50f,
                    fresnelPower       = 3.5f,
                    sssIntensity       = 2.2f,
                    sssPower           = 2f,
                    sssSpread          = 2.0f,
                    ambientStrength    = 0.10f,
                    wrapDiffuse        = 0.45f,
                    specularIntensity  = 0.5f,
                    heightScale        = 1.5f,
                    depthAbsorption    = 0.60f,
                    depthMaxDistance    = 12f,
                    absorptionColor    = new Color(0.01f, 0.02f, 0.05f),
                },

                BeaufortLevel.Hurricane => new BeaufortData
                {
                    windSpeedFactor    = 38f,
                    amplitude          = 5.0f,
                    choppiness         = 1.8f,
                    smallWaveCutoff    = 0.0003f,
                    timeScale          = 1.3f,
                    foamThreshold      = 0.08f,
                    foamDecay          = 0.95f,
                    foamStrength       = 5.0f,
                    shallowColor       = new Color(0.03f, 0.20f, 0.18f),
                    deepColor          = new Color(0.01f, 0.04f, 0.10f),
                    sssColor           = new Color(0.04f, 0.22f, 0.16f),
                    oceanRoughness     = 0.35f,
                    reflectionIntensity = 0.40f,
                    fresnelPower       = 3f,
                    sssIntensity       = 2.5f,
                    sssPower           = 1.5f,
                    sssSpread          = 3.0f,
                    ambientStrength    = 0.08f,
                    wrapDiffuse        = 0.5f,
                    specularIntensity  = 0.3f,
                    heightScale        = 2.0f,
                    depthAbsorption    = 0.80f,
                    depthMaxDistance    = 8f,
                    absorptionColor    = new Color(0.01f, 0.02f, 0.04f),
                },

                _ => GetPreset(BeaufortLevel.Moderate)
            };
        }

        public static void ApplyToSettings(OceanSettings settings, BeaufortData data)
        {
            settings.windSpeedFactor    = data.windSpeedFactor;
            settings.amplitude          = data.amplitude;
            settings.choppiness         = data.choppiness;
            settings.smallWaveCutoff    = data.smallWaveCutoff;
            settings.timeScale          = data.timeScale;

            settings.foamThreshold      = data.foamThreshold;
            settings.foamDecay          = data.foamDecay;
            settings.foamStrength       = data.foamStrength;

            settings.shallowColor       = data.shallowColor;
            settings.deepColor          = data.deepColor;
            settings.sssColor           = data.sssColor;
            settings.oceanRoughness     = data.oceanRoughness;
            settings.reflectionIntensity = data.reflectionIntensity;
            settings.fresnelPower       = data.fresnelPower;
            settings.sssIntensity       = data.sssIntensity;
            settings.sssPower           = data.sssPower;
            settings.sssSpread          = data.sssSpread;
            settings.ambientStrength    = data.ambientStrength;
            settings.wrapDiffuse        = data.wrapDiffuse;
            settings.specularIntensity  = data.specularIntensity;
            settings.heightScale        = data.heightScale;

            settings.depthAbsorption    = data.depthAbsorption;
            settings.depthMaxDistance    = data.depthMaxDistance;
            settings.absorptionColor    = data.absorptionColor;
        }
    }
}
