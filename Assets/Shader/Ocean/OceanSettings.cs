using UnityEngine;

namespace Ocean
{
    // ──────────────────────────────────────────────
    //  Enums — dropdown-friendly discrete choices
    // ──────────────────────────────────────────────

    public enum OceanResolution
    {
        [InspectorName("64 × 64   (fast, low detail)")]
        _64 = 64,
        [InspectorName("128 × 128 (balanced)")]
        _128 = 128,
        [InspectorName("256 × 256 (recommended)")]
        _256 = 256,
        [InspectorName("512 × 512 (high detail, costly)")]
        _512 = 512
    }

    public enum SpectrumType
    {
        [InspectorName("Phillips (classic, sharp peaks)")]
        Phillips,
        [InspectorName("JONSWAP (fetch-limited, realistic)")]
        JONSWAP
    }


    public enum PlanarReflectionResolution
    {
        [InspectorName("256 × 256 (fast, soft)")]
        _256 = 256,
        [InspectorName("512 × 512 (balanced)")]
        _512 = 512,
        [InspectorName("1024 × 1024 (sharp, costly)")]
        _1024 = 1024
    }

    public enum UnderwaterDebugMode
    {
        Off,
        [InspectorName("God Rays Only")]
        GodRaysOnly,
        [InspectorName("Scene Color (raw input)")]
        SceneColor,
        [InspectorName("Fog Amount")]
        FogAmount,
        [InspectorName("Depth Below Surface")]
        DepthBelow,
        [InspectorName("Solid Red (pipeline test)")]
        SolidRed
    }

    public enum PlaneResolution
    {
        [InspectorName("32 × 32   (low, 1K verts)")]
        _32 = 32,
        [InspectorName("64 × 64   (medium, 4K verts)")]
        _64 = 64,
        [InspectorName("128 × 128 (high, 16K verts)")]
        _128 = 128,
        [InspectorName("256 × 256 (ultra, 65K verts)")]
        _256 = 256
    }

    public enum FollowMode
    {
        [InspectorName("Static (stays at placed position)")]
        Static,
        [InspectorName("Follow camera (infinite ocean)")]
        FollowCamera
    }

    public enum OceanShadingStyle
    {
        [InspectorName("Stylized (Sea of Thieves)")]
        Stylized,
        [InspectorName("Semi-realistic (AC Black Flag)")]
        SemiRealistic
    }

    // ──────────────────────────────────────────────
    //  ScriptableObject
    // ──────────────────────────────────────────────

    [CreateAssetMenu(fileName = "OceanSettings", menuName = "Ocean/Ocean Settings")]
    public class OceanSettings : ScriptableObject
    {
        // ── General ─────────────────────────────────

        [Header("General")]

        [Tooltip("Y height of the ocean surface in world space. All components (mesh, reflections, underwater, wake) read from this single value.")]
        public float waterLevel = 0f;

        [Tooltip("Ocean surface material. Pushed automatically by OceanSystem each frame.")]
        public Material oceanMaterial;

        [Tooltip("Subdivisions per axis PER TILE. Tessellation adds detail on top of this.")]
        public PlaneResolution meshResolution = PlaneResolution._64;

        [Tooltip("Static: plane stays where you placed it. Follow Camera: plane tracks the camera XZ, creating infinite ocean.")]
        public FollowMode followMode = FollowMode.FollowCamera;

        // ── Spectrum ────────────────────────────────

        [Header("Spectrum")]

        [Tooltip("FFT grid resolution. Higher = finer wave detail but more GPU cost. 256 is the sweet spot for most cases.")]
        public OceanResolution resolution = OceanResolution._256;

        [Tooltip("Spectrum model. Phillips gives classic sharp peaks. JONSWAP adds a fetch parameter for more realistic wind-sea development.")]
        public SpectrumType spectrumType = SpectrumType.Phillips;

        [Tooltip("Physical size of one ocean tile in world units (meters). Larger = longer wavelengths visible, but each tile repeats more.")]
        [Range(20f, 2000f)]
        public float patchSize = 250f;

        [Tooltip("Random seed for wave pattern. Same seed = same wave layout. Change to get a different ocean.")]
        [Range(0, 9999)]
        public int seed = 42;

        // ── Wind ────────────────────────────────────
        // With a WindZone assigned: these act as offset/multipliers.
        // Without a WindZone: these are used directly as fixed values.

        [Header("Wind")]

        [Tooltip("With WindZone: offset added to the WindZone direction (degrees). Without: fixed wind angle.")]
        [Range(-180f, 360f)]
        public float windAngleOffset = 90f;

        [Tooltip("With WindZone: multiplier on Main speed. Without: fixed wind speed (m/s).")]
        [Range(0f, 40f)]
        public float windSpeedFactor = 12f;

        [Tooltip("With WindZone: multiplier on Turbulence. Without: fixed turbulence value.")]
        [Range(0f, 5f)]
        public float windTurbulenceFactor = 0f;

        [Tooltip("With WindZone: multiplier on Pulse Magnitude. Without: fixed pulse magnitude.")]
        [Range(0f, 5f)]
        public float windPulseMagnitudeFactor = 0f;

        [Tooltip("With WindZone: multiplier on Pulse Frequency. Without: fixed pulse frequency.")]
        [Range(0f, 5f)]
        public float windPulseFrequencyFactor = 0f;

        // ── Wave shape ──────────────────────────────

        [Header("Wave Shape")]

        [Tooltip("Global amplitude multiplier. Scales all wave heights. Start at 0.5 for moderate waves, 2-5 for stormy seas.")]
        [Range(0.0001f, 10f)]
        public float amplitude = 0.5f;

        [Tooltip("Choppiness (horizontal displacement strength). 0 = smooth rolling waves, 1 = sharp choppy peaks. Values > 1 cause self-intersection.")]
        [Range(0f, 2f)]
        public float choppiness = 1.2f;

        [Tooltip("Suppresses very small waves below this fraction of the largest wavelength. Reduces high-frequency noise.")]
        [Range(0f, 0.01f)]
        public float smallWaveCutoff = 0.001f;

        [Tooltip("Time scale for wave animation. 1 = real-time, 0.5 = half speed, 2 = double speed.")]
        [Range(0.1f, 4f)]
        public float timeScale = 1f;

        // ── JONSWAP specific ────────────────────────

        [Header("JONSWAP (only used with JONSWAP spectrum)")]

        [Tooltip("Fetch length in km. Distance over which wind has blown. Longer fetch = more developed sea state. 100-500 for open ocean.")]
        [Range(10f, 1000f)]
        public float fetchLength = 300f;

        [Tooltip("Peak enhancement factor (gamma). Higher = sharper spectral peak = more dominant swell. 3.3 is the standard JONSWAP value.")]
        [Range(1f, 10f)]
        public float jonswapGamma = 3.3f;

        // ── Cascades ────────────────────────────────

        [Header("Cascades")]

        [Tooltip("Number of FFT cascades. 1 = single patch (visible tiling). 3 = industry standard (minimal tiling).")]
        [Range(1, 3)]
        public int cascadeCount = 3;

        [Tooltip("Scale factor between cascade patch sizes. Cascade i covers patchSize / scale^i meters. 4 = each cascade is 4× smaller.")]
        [Range(2f, 8f)]
        public float cascadeScale = 4f;

        // ── Foam ────────────────────────────────────

        [Header("Foam")]

        [Tooltip("Foam generation bias. 0 = foam only where waves fold over. 0.5 = moderate crests get foam. 1.0 = any visible crest. Above 1 = foam even on flat water (uniform blanket).")]
        [Range(0f, 2f)]
        public float foamThreshold = 0.5f;

        [Tooltip("How quickly foam fades over time. 0 = instant disappear, 1 = lingers for several seconds.")]
        [Range(0f, 1f)]
        public float foamDecay = 0.85f;

        [Tooltip("Foam intensity multiplier.")]
        [Range(0f, 5f)]
        public float foamStrength = 1.5f;

        [Tooltip("Progressive blur radius on foam. 0 = raw pixels (off). 1 = standard dissipation. 3+ = very soft, dreamy foam. SoT uses progressive blur for organic foam.")]
        [Range(0f, 5f)]
        public float foamBlurRadius = 1f;

        [Tooltip("Tiling scale for foam detail textures. Higher = smaller foam patterns.")]
        [Range(0.5f, 20f)]
        public float foamTexScale = 8f;

        [Tooltip("Blend sharpness between high-freq (crest) and low-freq (dissipating) foam textures. Higher = sharper transition.")]
        [Range(0.1f, 5f)]
        public float foamTexBlend = 1.5f;

        [Tooltip("Distance in meters from intersecting geometry at which shore foam appears. Higher = wider foam band.")]
        [Range(0.1f, 20f)]
        public float shoreFoamDistance = 3f;

        [Tooltip("Shore foam intensity. 0 = off, 1 = full white foam at intersection.")]
        [Range(0f, 3f)]
        public float shoreFoamStrength = 1.2f;

        [Tooltip("Falloff exponent for shore foam. Higher = sharper edge, lower = smoother gradient.")]
        [Range(0.5f, 5f)]
        public float shoreFoamFalloff = 1.5f;

        [Tooltip("Distance in meters at which wave amplitude begins to fade near geometry. E.g. 15 = waves start reducing at 15m from shore.")]
        [Range(1f, 50f)]
        public float waveShoreAttenuationDist = 15f;

        [Tooltip("Minimum wave amplitude multiplier at the shore (0 = fully flat, 0.5 = half amplitude).")]
        [Range(0f, 1f)]
        public float waveShoreMinAmplitude = 0.5f;

        // ── Shore Waves ─────────────────────────────

        [Header("Shore Waves")]

        [Tooltip("Enable shore wave wash effect — paints foam and wet sand on terrain above the waterline, driven by FFT waves.")]
        public bool enableShoreWaves = true;

        [Tooltip("Maximum height above the waterline that waves can reach on the terrain. Higher = waves wash further up gentle slopes.")]
        [Range(0.1f, 5f)]
        public float shoreWashHeight = 1.5f;

        [Tooltip("Width of the foam band at the leading wave edge in meters.")]
        [Range(0.05f, 2f)]
        public float shoreWashFoamWidth = 0.4f;

        [Tooltip("How much to darken terrain for wet sand effect. 0 = no darkening, 1 = full.")]
        [Range(0f, 1f)]
        public float shoreWetDarkening = 0.5f;

        [Tooltip("Scale of the procedural shore foam noise pattern. Higher = finer foam bubbles.")]
        [Range(0.5f, 10f)]
        public float shoreFoamNoiseScale = 3f;

        [Tooltip("Falloff power for the wash gradient. Higher = foam concentrated near the wave edge, lower = spread out.")]
        [Range(0.5f, 5f)]
        public float shoreWashPower = 2f;

        [Tooltip("Time in seconds for the wash foam to fully fade after the wave recedes. Foam near the peak fades faster.")]
        [Range(0.5f, 10f)]
        public float shoreWashFadeTime = 3f;

        // ── Shore Intersection Map ──────────────────────

        [Header("Shore Intersection Map")]

        [Tooltip("Compute shader for GPU shore intersection detection (OceanShoreIntersection).")]
        public ComputeShader shoreIntersectionShader;

        [Tooltip("Enable the GPU shore intersection map. Provides choppiness-corrected wave-terrain intersection in world-space.")]
        public bool enableShoreIntersectionMap = true;

        [Tooltip("Resolution of the shore intersection map. 512 = good balance, 1024 = high precision.")]
        [Range(64, 2048)]
        public int shoreMapResolution = 512;

        [Tooltip("World-space coverage of the shore map in meters, centered on camera. Should cover the full visible coastline.")]
        [Range(50f, 2000f)]
        public float shoreMapSize = 600f;

        // ── Shading (Phase 2, declared now for planning) ──

        [Header("Shading")]

        [Tooltip("Visual style for the ocean surface.")]
        public OceanShadingStyle shadingStyle = OceanShadingStyle.Stylized;

        [Tooltip("Shallow water color — visible near shoreline and in low-depth areas.")]
        public Color shallowColor = new Color(0.1f, 0.75f, 0.65f, 1f);

        [Tooltip("Deep water color — visible in open ocean and deep areas.")]
        public Color deepColor = new Color(0.02f, 0.12f, 0.25f, 1f);

        [Tooltip("Subsurface scattering color — visible when looking through thin wave crests against the light.")]
        public Color sssColor = new Color(0.1f, 0.6f, 0.4f, 1f);

        [Tooltip("Foam color tint.")]
        public Color foamColor = new Color(0.95f, 0.97f, 1f, 1f);

        [Tooltip("Fresnel power. Higher = reflections only at grazing angles, lower = reflections even when looking straight down.")]
        [Range(1f, 10f)]
        public float fresnelPower = 4f;

        [Tooltip("SSS intensity. Controls how bright the subsurface scattering effect is when backlit.")]
        [Range(0f, 5f)]
        public float sssIntensity = 1.5f;

        [Tooltip("SSS falloff power. Higher = SSS only when looking directly at the sun. Lower = SSS visible from wider angles.")]
        [Range(1f, 8f)]
        public float sssPower = 3f;

        [Tooltip("SSS spread. Controls how gradual the SSS transition is. Low (0.1) = tight visible edge. High (3) = very diffuse, smooth blend.")]
        [Range(0.1f, 10f)]
        public float sssSpread = 1f;

        [Tooltip("Specular highlight sharpness. Higher = tighter sun reflection. 128 = default, 512 = mirror-like.")]
        [Range(16f, 512f)]
        public float specularPower = 128f;

        [Tooltip("Specular highlight brightness.")]
        [Range(0f, 5f)]
        public float specularIntensity = 1f;

        [Tooltip("Ocean surface roughness for GGX specular. Lower = sharper sun highlight. 0.02 = mirror-like, 0.1 = slightly rough.")]
        [Range(0.01f, 1f)]
        public float oceanRoughness = 0.05f;

        [Tooltip("Multiplier on vertical wave displacement. Increase for taller waves, decrease for flatter sea.")]
        [Range(0.01f, 10f)]
        public float heightScale = 1f;

        [Tooltip("Ambient light strength. Minimum brightness in shadow areas.")]
        [Range(0f, 1f)]
        public float ambientStrength = 0.15f;

        [Tooltip("Wrap diffuse factor. 0 = hard Lambert, 0.5 = soft wrap lighting on wave dark side.")]
        [Range(0f, 1f)]
        public float wrapDiffuse = 0.3f;

        // ── Reflection ────────────────────────────

        [Header("Reflection")]

        [Tooltip("Sky color at the horizon, used for Fresnel reflection.")]
        public Color horizonColor = new Color(0.35f, 0.45f, 0.55f, 1f);

        [Tooltip("Sky color at the zenith, used for Fresnel reflection.")]

        public Color zenithColor = new Color(0.15f, 0.3f, 0.6f, 1f);
		[Header("Reflection")]

		[Tooltip("Reflection intensity. 0 = no reflection, 1 = full.")]
        [Range(0f, 2f)]
        public float reflectionIntensity = 0.8f;

        // ── Depth coloring ────────────────────────────

        [Header("Depth Coloring")]

        [Tooltip("How fast the water absorbs light with depth. Higher = faster transition to deep color. Beer-Lambert coefficient.")]
        [Range(0.01f, 2f)]
        public float depthAbsorption = 0.3f;

        [Tooltip("Simulated maximum water depth in meters. Controls how deep the water body appears overall.")]
        [Range(1f, 100f)]
        public float depthMaxDistance = 20f;

        [Tooltip("Deep absorption tint. The color water converges to at maximum depth. Very dark blue/black for ocean.")]
        public Color absorptionColor = new Color(0.01f, 0.04f, 0.08f, 1f);

        // ── Refraction ────────────────────────────

        [Header("Refraction")]

        [Tooltip("How much the underwater scene is distorted by waves. 0 = no distortion, 1 = strong.")]
        [Range(0f, 1f)]
        public float refractionStrength = 0.3f;

        [Tooltip("Depth over which refraction fades from fully transparent to fully opaque water color. Low = quickly opaque, high = see far underwater.")]
        [Range(0.1f, 20f)]
        public float refractionDepthFade = 5f;

        // ── Caustics ──────────────────────────────

        [Header("Caustics")]

        [Tooltip("Caustics texture tiling. Higher = smaller pattern, more repetition.")]
        [Range(0.1f, 10f)]
        public float causticsScale = 1f;

        [Tooltip("Caustics animation speed. 0 = frozen, 1 = gentle drift, 2 = fast.")]
        [Range(0f, 2f)]
        public float causticsSpeed = 0.3f;

        [Tooltip("Caustics brightness. Higher = more prominent light patterns on the sea floor.")]
        [Range(0f, 5f)]
        public float causticsIntensity = 1.5f;

        [Tooltip("Maximum depth at which caustics are visible. Beyond this, caustics fade out.")]
        [Range(0.5f, 30f)]
        public float causticsMaxDepth = 10f;

        [Tooltip("Chromatic dispersion spread. Separates R/G/B caustic channels to simulate light splitting through water. 0 = monochrome.")]
        [Range(0f, 10f)]
        public float causticsChromaSpread = 2f;

        [Tooltip("UV offset direction for the Red channel (scaled by Chroma Spread).")]
        public Vector2 causticsChromaOffsetR = new Vector2(1f, 0f);

        [Tooltip("UV offset direction for the Blue channel (scaled by Chroma Spread).")]
        public Vector2 causticsChromaOffsetB = new Vector2(-0.5f, 0.7f);

        // ── Planar Reflection ─────────────────────

        [Header("Planar Reflection")]

        [Tooltip("Enable planar reflections via a mirrored camera. Replaces the sky gradient reflection with real scene reflections.")]
        public bool usePlanarReflection = false;

        [Tooltip("Resolution of the reflection render texture. Higher = sharper reflections but more GPU cost.")]
        public PlanarReflectionResolution planarReflectionResolution = PlanarReflectionResolution._512;

        [Tooltip("Blend between sky gradient (0) and planar reflection (1).")]
        [Range(0f, 1f)]
        public float planarReflectionBlend = 0.8f;

        [Tooltip("Which layers to render in the reflection. Exclude unnecessary layers for performance.")]
        public LayerMask reflectionLayers = -1;

        [Tooltip("Small offset above the clip plane to avoid edge artifacts at the waterline.")]
        [Range(0f, 0.5f)]
        public float reflectionClipOffset = 0.05f;

        [Tooltip("Render reflections every N frames. 1 = every frame (best quality), 2+ = skip frames for performance.")]
        [Range(1, 4)]
        public int reflectionUpdateInterval = 1;

        // ── Underwater ────────────────────────────

        [Header("Underwater")]

        [Tooltip("Enable underwater fog and god rays when the camera goes below the water surface.")]
        public bool enableUnderwater = true;
		[Header("Underwater")]

		[Tooltip("Underwater fog color. This is the color everything fades into with distance.")]
        public Color underwaterFogColor = new Color(0.04f, 0.18f, 0.22f, 1f);

        [Tooltip("Fog density (Beer-Lambert coefficient). Lower = clearer water. 0.03 = crystal clear, 0.15 = murky.")]
        [Range(0.01f, 1f)]
        public float underwaterFogDensity = 0.06f;

        [Tooltip("Distance in meters before fog starts. Objects within this range keep their full color.")]
        [Range(0f, 50f)]
        public float underwaterFogStartDistance = 2f;

        [Tooltip("Vertical depth absorption rate. Controls how fast sunlight is absorbed with depth. Higher = darker at depth.")]
        [Range(0.01f, 0.5f)]
        public float underwaterDepthAbsorption = 0.08f;

        [Tooltip("Minimum brightness at extreme depth. 0 = total darkness possible, 0.1 = always some ambient.")]
        [Range(0f, 0.2f)]
        public float underwaterDepthDarkeningMin = 0.03f;

        [Header("Underwater Distortion")]

        [Tooltip("Screen distortion strength when underwater. 0 = no distortion.")]
        [Range(0f, 0.03f)]
        public float underwaterDistortionStrength = 0.005f;

        [Tooltip("Speed of the distortion wave animation.")]
        [Range(0.1f, 5f)]
        public float underwaterDistortionSpeed = 1f;

        [Tooltip("Scale of the distortion wave pattern. Higher = tighter waves.")]
        [Range(1f, 50f)]
        public float underwaterDistortionScale = 15f;

        [Tooltip("God ray color tint. Bright teal for stylized, white-blue for realistic.")]
        public Color godRayColor = new Color(0.15f, 0.55f, 0.5f, 1f);

		[Header("God Rays")]

		[Tooltip("God ray brightness. 0 = off, 1 = subtle, 3 = dramatic.")]
        [Range(0f, 5f)]
        public float godRayIntensity = 1.5f;

        [Tooltip("Depth below surface at which god rays reach full intensity. Prevents abrupt pop-in at the waterline.")]
        [Range(0.1f, 10f)]
        public float godRayFadeInDepth = 2f;

        [Tooltip("Beam sharpness. 0 = very diffuse soft glow, 1 = sharp defined beams.")]
        [Range(0f, 1f)]
        public float godRaySharpness = 0.6f;

        [Tooltip("Beam pattern scale. Smaller = larger beams, larger = finer beams.")]
        [Range(0.1f, 1.5f)]
        public float godRayBeamScale = 0.5f;

        [Tooltip("How much beams follow the sun direction. 0 = purely vertical, 1 = fully sun-aligned.")]
        [Range(0f, 1f)]
        public float godRaySunFollow = 0.3f;

        [Tooltip("How quickly beams fade with water depth. Higher = beams only near surface.")]
        [Range(0.01f, 1f)]
        public float godRayDepthFade = 0.15f;

        [Tooltip("Beam extinction along view ray. Higher = beams fade faster with view distance.")]
        [Range(0.01f, 0.3f)]
        public float godRayExtinction = 0.06f;

        [Tooltip("Maximum ray march distance in meters.")]
        [Range(10f, 200f)]
        public float godRayMaxDist = 50f;

        [Tooltip("God ray animation speed. 0 = frozen, 0.5 = gentle, 1 = moderate.")]
        [Range(0f, 2f)]
        public float godRaySpeed = 0.4f;

        [Tooltip("Number of volumetric ray-march steps. More = sharper beams, heavier GPU. 16 is usually enough, 8 is cheap.")]
        [Range(4, 64)]
        public int godRaySteps = 32;

        [Tooltip("Resolution scale for the underwater pass. 1 = full res, 0.5 = quarter pixels (big perf gain). Affects fog, caustics and god rays.")]
        [Range(0.25f, 1f)]
        public float underwaterResolutionScale = 1f;

        [Tooltip("Debug visualization for underwater effect. SceneColor shows the raw input before any fog/ray processing.")]
        public UnderwaterDebugMode underwaterDebugMode = UnderwaterDebugMode.Off;

        [Header("Surface From Below")]

        [Tooltip("Wave distortion on Snell's window boundary. Higher = more wobbly surface edge.")]
        [Range(0f, 0.5f)]
        public float surfaceFromBelowDistortion = 0.2f;

        [Tooltip("Depth at which Snell's window reaches its physical size. Lower = window shrinks faster when diving. Higher = wide view persists deeper.")]
        [Range(0.5f, 20f)]
        public float snellWindowDepth = 3f;

        // ── Underwater Lighting ─────────────────────

        [Header("Underwater Lighting")]

        [Tooltip("Enable spectral light attenuation and Volume Profile blending when underwater.")]
        public bool enableUnderwaterLighting = true;

        [Tooltip("Transition zone in meters below the surface. 0.5 = blend over 50cm.")]
        [Range(0.1f, 2f)]
        public float transitionDepth = 0.5f;

        [Tooltip("RGB absorption coefficients per meter. Real seawater: (0.45, 0.06, 0.02). Red is absorbed first, blue last.")]
        public Vector3 absorptionCoefficients = new Vector3(0.45f, 0.06f, 0.02f);

        [Tooltip("Scales effective depth for absorption calculation. >1 = faster darkening.")]
        [Range(0.1f, 5f)]
        public float attenuationDepthScale = 1f;

        [Tooltip("Overall light intensity decay rate with depth. Keep low (~0.05) if using a Volume Profile with Exposure override.")]
        [Range(0.01f, 1f)]
        public float lightIntensityDecay = 0.05f;

        [Tooltip("Minimum light intensity multiplier — prevents total darkness.")]
        [Range(0f, 0.5f)]
        public float lightIntensityFloor = 0.05f;

        // ── Wake ────────────────────────────────────

        [Header("Wake Trail")]

        [Tooltip("Depression intensity. Higher = deeper wake trail behind the vehicle.")]
        [Range(0.01f, 1f)]
        public float wakeIntensity = 0.3f;

        [Tooltip("Lateral splash distance from center in meters. Creates the bow wave effect.")]
        [Range(0f, 15f)]
        public float wakeSplashWidth = 3f;

        [Tooltip("Radius of the wake stamp in meters.")]
        [Range(2f, 30f)]
        public float wakeStampRadius = 8f;

        [Tooltip("Minimum speed (m/s) before wake is generated.")]
        [Range(0.1f, 5f)]
        public float wakeMinSpeed = 0.5f;

        [Tooltip("Speed at which wake reaches full intensity (m/s).")]
        [Range(5f, 50f)]
        public float wakeFullSpeed = 15f;

        [Tooltip("Blur strength per frame. Higher = smoother trails, softer edges.")]
        [Range(0f, 1f)]
        public float wakeBlurStrength = 0.6f;

        [Tooltip("Number of blur passes per frame. More = smoother but costlier.")]
        [Range(0, 4)]
        public int wakeBlurPasses = 2;

        [Tooltip("Fade rate per frame. 0.99 = slow fade, 0.95 = fast, 0.9 = very fast.")]
        [Range(0.9f, 0.999f)]
        public float wakeFadeRate = 0.97f;

        [Tooltip("How much the wake displaces the ocean surface.")]
        [Range(0.1f, 20f)]
        public float wakeDisplacementScale = 5f;

        [Tooltip("World area covered by the wake heightfield in meters.")]
        [Range(50f, 500f)]
        public float wakeCoverageSize = 150f;

        [Tooltip("Resolution of the wake heightfield.")]
        public OceanResolution wakeResolution = OceanResolution._256;

        [Tooltip("How far below the surface the wake fully disappears (meters).")]
        [Range(0.5f, 20f)]
        public float wakeFadeDepth = 3f;

        // ── Tessellation (Phase 2) ──────────────────

        [Header("Tessellation")]

        [Tooltip("Maximum tessellation factor near the camera.")]
        [Range(1f, 64f)]
        public float tessMaxFactor = 16f;

        [Tooltip("Distance beyond which tessellation is minimum (factor = 1).")]
        [Range(10f, 500f)]
        public float tessMaxDistance = 150f;

        // ── Compute Shaders ─────────────────────────

        [Header("Compute Shaders — FFT")]

        [Tooltip("Generates the initial spectrum H₀(k). Dispatched once when params change.")]
        public ComputeShader initSpectrumShader;

        [Tooltip("Evolves H₀(k) into H(k,t) each frame. Produces Dy/Dx/Dz complex spectra.")]
        public ComputeShader timeDependentSpectrumShader;

        [Tooltip("2D IFFT Stockham butterfly. Transforms frequency → spatial domain.")]
        public ComputeShader fftShader;

        [Tooltip("Post-process: normals from displacement, Jacobian for foam, temporal foam accumulation.")]
        public ComputeShader postProcessShader;

        [Tooltip("Progressive Gaussian blur on foam RT.")]
        public ComputeShader foamBlurShader;

        [Header("Compute Shaders — Wake")]

        [Tooltip("Wake trail compute shader (OceanWakeTrail).")]
        public ComputeShader wakeTrailShader;

        // ── Debug ───────────────────────────────────

        [Header("Debug")]

        [Tooltip("What to show on the FFT debug quad.")]
        public OceanDebugView debugView = OceanDebugView.Off;

        [Tooltip("Which cascade to inspect in the debug view (0-2).")]
        [Range(0, 2)]
        public int debugCascade = 0;

        [Tooltip("Debug visualization shader (OceanDebugSpectrum).")]
        public Shader fftDebugShader;

        [Tooltip("Enable verbose logging in Console. Disable to silence all [OceanManager] info messages. Warnings and errors always show.")]
        public bool verboseLogs = true;

        [Tooltip("Show the FFT output textures as overlays in the Scene view.")]
        public bool debugShowRTs = false;

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        public int ResolutionInt => (int)resolution;

        public float CascadePatchSize(int cascade)
        {
            return patchSize / Mathf.Pow(cascadeScale, cascade);
        }

    }
}
