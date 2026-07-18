// TerrainLitCustomSetup.cs
// Attach to a GameObject with a Terrain to sync custom keywords with material properties.
// Also provides inspector controls for POM / Tessellation / Sand Mode / Wind / per-layer settings.
//
// v6 — WIND DISPLACEMENT:
//   Added direct wind controls (direction angle, speed, strength, turbulence, gusts).
//   Produces procedural scrolling-wave vertex displacement on displacement-enabled layers.
//   No external dependencies — all parameters are on the inspector.
//
// v5 — SAND MODE:
//   Added per-layer sand mode toggle + global sand parameters (glitter, rim push-up,
//   ripples, ocean specular, fresnel). Automatically finds the main directional light
//   and pushes its direction to the shader for glitter/specular computation.
//
// FIX v4 — CRITICAL:
//   All property sets use terrainMat.SetFloat (UnityPerMaterial CBUFFER), NOT
//   Shader.SetGlobalFloat (_Globals CBUFFER). See v4 notes for full explanation.
// ---------------------------------------------------------------------------------

using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Terrain))]
public class TerrainLitCustomSetup : MonoBehaviour
{
    [Header("Parallax Occlusion Mapping")]
    public bool enablePOM = false;
    [Range(0f, 0.2f)] public float pomHeightScale = 0.05f;
    [Range(4, 64)] public int pomMinSteps = 4;
    [Range(8, 128)] public int pomMaxSteps = 32;
    [Tooltip("Distance (meters) at which POM and tessellation fade out")]
    [Range(5f, 200f)] public float pomDistanceFade = 50f;

    [Header("Tessellation Displacement")]
    public bool enableDisplacement = false;
    [Range(1f, 64f)] public float tessellationFactor = 15f;

    [Tooltip("Tessellation falloff curve: X = normalized distance (0=camera, 1=maxDist), Y = tessellation multiplier (0-1).")]
    public AnimationCurve tessellationFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Runtime Deformation (footprints)")]
    [Tooltip("How strongly the deformation map pushes geometry down")]
    [Range(0f, 2f)] public float deformationStrength = 1.0f;

    [Header("Per-Layer POM (by layer index 0-7)")]
    public bool[] layerPOMEnabled = new bool[8];

    [Header("Per-Layer Displacement (by layer index 0-7)")]
    public bool[] layerDisplacementEnabled = new bool[8];
    [Tooltip("Elevation per displaced layer (meters). No upper limit.")]
    [Min(0f)]
    public float displacementScale = 0.1f;

    // ---------------------------------------------------------------------------------
    // SAND MODE
    // ---------------------------------------------------------------------------------
    [Header("Sand Mode")]
    [Tooltip("Enable sand rendering mode (glitter, rim push-up, ripples, ocean spec)")]
    public bool enableSandMode = false;

    [Header("Per-Layer Sand Mode (by layer index 0-7)")]
    [Tooltip("Which layers behave as 'sand' (receive glitter, rim, ripples)")]
    public bool[] layerSandEnabled = new bool[8];

    [Header("Sand: Glitter")]
    [Tooltip("Lerp strength toward mirror normal. 0.3=subtle shimmer, 0.8=strong sparkle, 1.0=full mirror.")]
    [Range(0f, 2f)] public float sandGlitterIntensity = 0.8f;
    [Tooltip("Alignment threshold — higher = fewer but brighter sparkles")]
    [Range(0.9f, 0.999f)] public float sandGlitterThreshold = 0.97f;
    [Tooltip("World-space grain density — higher = smaller grains")]
    [Range(10f, 1000f)] public float sandGlitterScale = 200f;
    [Tooltip("Glitter tint color")]
    public Color sandGlitterColor = new Color(1f, 0.95f, 0.8f, 1f);

    [Tooltip("Distance (meters) at which glitter fully fades out")]
    [Min(1f)] public float sandGlitterMaxDistance = 80f;
    [Tooltip("Glitter falloff curve: X = normalized distance (0=camera, 1=maxDist), Y = intensity multiplier (0-1).")]
    public AnimationCurve sandGlitterFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Sand: Rim Push-Up (Bourrelets)")]
    [Tooltip("Height of sand rims around footprints (meters)")]
    [Range(0f, 0.2f)] public float sandRimPushUp = 0.03f;

    [Header("Sand: Ripples")]
    [Tooltip("World-space ripple frequency — controls ripple spacing")]
    [Range(0.1f, 20f)] public float sandRippleScale = 3.0f;
    [Tooltip("Normal perturbation strength — how pronounced the ripples are")]
    [Range(0f, 0.5f)] public float sandRippleStrength = 0.15f;

    [Header("Sand: Ocean Specular")]
    [Tooltip("Specular exponent — lower = broader highlights")]
    [Range(2f, 64f)] public float sandOceanSpecPower = 16f;
    [Tooltip("Ocean specular brightness (baseColor additive, subtle)")]
    [Range(0f, 1f)] public float sandOceanSpecIntensity = 0.3f;

    [Header("Sand: Fresnel Rim")]
    [Tooltip("Fresnel exponent — higher = thinner rim")]
    [Range(1f, 10f)] public float sandFresnelPower = 4f;
    [Tooltip("Fresnel rim brightness (baseColor additive, subtle)")]
    [Range(0f, 1f)] public float sandFresnelIntensity = 0.3f;

    // ---------------------------------------------------------------------------------
    // WIND DISPLACEMENT
    // ---------------------------------------------------------------------------------
    [Header("Wind Displacement")]
    [Tooltip("Enable wind-driven procedural displacement on displacement-enabled layers")]
    public bool enableWindDisplacement = false;

    [Header("Wind: Noise Maps")]
    [Tooltip("Global noise map — defines the broad wind distortion pattern. Scrolls across the terrain.")]
    public Texture2D windNoiseGlobalMap;

    [Tooltip("Detail noise map — multiplied with global to break repetition and add organic variation.")]
    public Texture2D windNoiseDetailMap;

    [Header("Wind: Tiling")]
    [Tooltip("World-space tiling of the global noise. Lower = larger distortion patterns.")]
    [Range(0.001f, 0.5f)] public float windNoiseGlobalTile = 0.01f;

    [Tooltip("World-space tiling of the detail noise. Typically higher than global for fine breakup.")]
    [Range(0.001f, 1f)] public float windNoiseDetailTile = 0.05f;

    [Header("Wind: Displacement Range")]
    [Tooltip("Displacement (meters) assigned to black (0) in the combined noise.")]
    [Range(-2f, 0f)] public float windMinValue = -0.01f;

    [Tooltip("Displacement (meters) assigned to white (1) in the combined noise.")]
    [Range(0f, 2f)] public float windMaxValue = 0.02f;

    [Header("Wind: Scroll Direction")]
    [Tooltip("Scroll direction and speed of the global noise (XZ world units/sec). Magnitude = speed.")]
    public Vector2 windGlobalOffsetDirection = new Vector2(0.3f, 0.15f);

    [Tooltip("Scroll direction and speed of the detail noise (XZ world units/sec). Use a different angle for natural look.")]
    public Vector2 windDetailOffsetDirection = new Vector2(-0.1f, 0.2f);

    [Header("Wind: Period")]
    [Tooltip("Temporal pulsation speed. Controls how fast wind intensity fades in/out. 0 = always on, higher = faster cycling.")]
    [Range(0f, 2f)] public float windPeriod = 0.08f;

    [Header("Wind: Debug")]
    [Tooltip("Visualize wind noise directly on the terrain surface")]
    public WindDebugMode windDebugMode = WindDebugMode.Off;

    public enum WindDebugMode
    {
        Off = 0,
        GlobalOnly = 1,
        DetailOnly = 2,
        Combined = 3,
        Displacement = 4
    }

    // ---------------------------------------------------------------------------------
    // GRASS TINT (L2) — distant color-match toward the grass color (aerial trompe-l'oeil)
    // ---------------------------------------------------------------------------------
    [Header("Grass Tint (L2)")]
    [Tooltip("Enable the distant grass-tint layer (terrain fades toward grass color with camera distance).")]
    public bool enableGrassTint = false;

    [Header("Per-Layer Grass Tint (by layer index 0-7)")]
    [Tooltip("Which terrain layers read as grass at distance. Should match the grass blade layers.")]
    public bool[] layerGrassTintEnabled = new bool[8];

    [Tooltip("Grass color the terrain fades toward at distance (sRGB). Matches the blade color in Phase 4.")]
    public Color grassTintColor = new Color(0.25f, 0.4f, 0.15f, 1f);

    [Tooltip("Overall tint strength [0-1].")]
    [Range(0f, 1f)] public float grassTintStrength = 1.0f;

    [Tooltip("Smoothness the grass-tinted terrain fades toward.")]
    [Range(0f, 1f)] public float grassTintSmoothness = 0.25f;

    [Tooltip("Camera distance (m) where the grass tint starts to appear.")]
    [Min(0f)] public float grassTintDistanceStart = 30f;

    [Tooltip("Camera distance (m) where the grass tint is fully applied.")]
    [Min(1f)] public float grassTintDistanceFull = 120f;

    [Tooltip("Wind-wave normal tilt strength — the rolling bands (lighting-driven). 0 = static (Phase 1a).")]
    [Range(0f, 2f)] public float grassWaveNormalStrength = 0.5f;

    [Tooltip("Wind-wave albedo luminance strength — subtle direct brightness bands.")]
    [Range(0f, 1f)] public float grassWaveLumStrength = 0.15f;

    [Header("Debug")]
    [Tooltip("Log per-layer displacement values to console each frame")]
    public bool debugLogDisplacement = false;

    Terrain terrain;
    Material terrainMat;
    Texture2D tessRampTexture;
    int tessRampHash;
    Texture2D glitterRampTexture;
    int glitterRampHash;

    // Shader property IDs
    static readonly int ID_POMDistanceFade = Shader.PropertyToID("_POMDistanceFade");
    static readonly int ID_TessellationFactor = Shader.PropertyToID("_TessellationFactor");
    static readonly int ID_TessellationMaxDisplacement = Shader.PropertyToID("_TessellationMaxDisplacement");
    static readonly int ID_DeformationStrength = Shader.PropertyToID("_DeformationStrength");
    static readonly int ID_TessellationFalloffRamp = Shader.PropertyToID("_TessellationFalloffRamp");

    // Per-layer IDs (0-7)
    static readonly int[] ID_EnablePOMLayer = new int[8];
    static readonly int[] ID_HeightScale = new int[8];
    static readonly int[] ID_POMMinSteps = new int[8];
    static readonly int[] ID_POMMaxSteps = new int[8];
    static readonly int[] ID_EnableDisplacementLayer = new int[8];
    static readonly int[] ID_DisplacementScale = new int[8];
    static readonly int[] ID_EnableSandMode = new int[8];

    // Sand global IDs
    static readonly int ID_SandGlitterIntensity = Shader.PropertyToID("_SandGlitterIntensity");
    static readonly int ID_SandGlitterThreshold = Shader.PropertyToID("_SandGlitterThreshold");
    static readonly int ID_SandGlitterScale = Shader.PropertyToID("_SandGlitterScale");
    static readonly int ID_SandGlitterColor = Shader.PropertyToID("_SandGlitterColor");
    static readonly int ID_SandGlitterMaxDistance = Shader.PropertyToID("_SandGlitterMaxDistance");
    static readonly int ID_SandGlitterFalloffRamp = Shader.PropertyToID("_SandGlitterFalloffRamp");
    static readonly int ID_SandRimPushUp = Shader.PropertyToID("_SandRimPushUp");
    static readonly int ID_SandDeformTexelSize = Shader.PropertyToID("_SandDeformTexelSize");
    static readonly int ID_SandSunDirection = Shader.PropertyToID("_SandSunDirection");
    static readonly int ID_SandRippleScale = Shader.PropertyToID("_SandRippleScale");
    static readonly int ID_SandRippleStrength = Shader.PropertyToID("_SandRippleStrength");
    static readonly int ID_SandOceanSpecPower = Shader.PropertyToID("_SandOceanSpecPower");
    static readonly int ID_SandOceanSpecIntensity = Shader.PropertyToID("_SandOceanSpecIntensity");
    static readonly int ID_SandFresnelPower = Shader.PropertyToID("_SandFresnelPower");
    static readonly int ID_SandFresnelIntensity = Shader.PropertyToID("_SandFresnelIntensity");

    // Wind global IDs
    static readonly int ID_WindGlobalMap = Shader.PropertyToID("_WindGlobalMap");
    static readonly int ID_WindDetailMap = Shader.PropertyToID("_WindDetailMap");
    static readonly int ID_WindGlobalTile = Shader.PropertyToID("_WindGlobalTile");
    static readonly int ID_WindDetailTile = Shader.PropertyToID("_WindDetailTile");
    static readonly int ID_WindMinValue = Shader.PropertyToID("_WindMinValue");
    static readonly int ID_WindMaxValue = Shader.PropertyToID("_WindMaxValue");
    static readonly int ID_WindGlobalOffsetDir = Shader.PropertyToID("_WindGlobalOffsetDir");
    static readonly int ID_WindDetailOffsetDir = Shader.PropertyToID("_WindDetailOffsetDir");
    static readonly int ID_WindPeriod = Shader.PropertyToID("_WindPeriod");
    static readonly int ID_WindTime = Shader.PropertyToID("_WindTime");
    static readonly int ID_WindDebugMode = Shader.PropertyToID("_WindDebugMode");

    // Grass tint (L2) IDs
    static readonly int ID_GrassTintColor = Shader.PropertyToID("_GrassTintColor");
    static readonly int ID_GrassTintStrength = Shader.PropertyToID("_GrassTintStrength");
    static readonly int ID_GrassTintSmoothness = Shader.PropertyToID("_GrassTintSmoothness");
    static readonly int ID_GrassTintDistanceStart = Shader.PropertyToID("_GrassTintDistanceStart");
    static readonly int ID_GrassTintDistanceFull = Shader.PropertyToID("_GrassTintDistanceFull");
    static readonly int[] ID_EnableGrassTintLayer = new int[8];
    static readonly int ID_GrassWaveNormalStrength = Shader.PropertyToID("_GrassWaveNormalStrength");
    static readonly int ID_GrassWaveLumStrength = Shader.PropertyToID("_GrassWaveLumStrength");

    static TerrainLitCustomSetup()
    {
        for (int i = 0; i < 8; i++)
        {
            ID_EnablePOMLayer[i] = Shader.PropertyToID($"_EnablePOMLayer{i}");
            ID_HeightScale[i] = Shader.PropertyToID($"_HeightScale{i}");
            ID_POMMinSteps[i] = Shader.PropertyToID($"_POMMinSteps{i}");
            ID_POMMaxSteps[i] = Shader.PropertyToID($"_POMMaxSteps{i}");
            ID_EnableDisplacementLayer[i] = Shader.PropertyToID($"_EnableDisplacementLayer{i}");
            ID_DisplacementScale[i] = Shader.PropertyToID($"_DisplacementScale{i}");
            ID_EnableSandMode[i] = Shader.PropertyToID($"_EnableSandMode{i}");
            ID_EnableGrassTintLayer[i] = Shader.PropertyToID($"_EnableGrassTintLayer{i}");
        }
    }

    void OnEnable()
    {
        terrain = GetComponent<Terrain>();
        ApplyToMaterial();
    }

    void OnValidate()
    {
        if (layerPOMEnabled == null || layerPOMEnabled.Length < 8)
            layerPOMEnabled = new bool[8];
        if (layerDisplacementEnabled == null || layerDisplacementEnabled.Length < 8)
            layerDisplacementEnabled = new bool[8];
        if (layerSandEnabled == null || layerSandEnabled.Length < 8)
            layerSandEnabled = new bool[8];
        if (layerGrassTintEnabled == null || layerGrassTintEnabled.Length < 8)
            layerGrassTintEnabled = new bool[8];
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) ApplyToMaterial();
        };
#else
        ApplyToMaterial();
#endif
    }

    void Update()
    {
        ApplyToMaterial();
    }

    void ApplyToMaterial()
    {
        if (terrain == null) terrain = GetComponent<Terrain>();
        terrainMat = terrain.materialTemplate;
        if (terrainMat == null) return;

        UpdatePatchBounds();

        // --- Keywords ---
        SetKeyword(terrainMat, "_PARALLAX_OCCLUSION_MAPPING", enablePOM);
        SetKeyword(terrainMat, "_TESSELLATION_DISPLACEMENT", enableDisplacement);
        SetKeyword(terrainMat, "_SAND_MODE", enableSandMode);
        SetKeyword(terrainMat, "_WIND_DISPLACEMENT", enableWindDisplacement && enableDisplacement);
        SetKeyword(terrainMat, "_GRASS_TINT", enableGrassTint);

        // --- Existing custom properties ---
        terrainMat.SetFloat(ID_POMDistanceFade, pomDistanceFade);
        terrainMat.SetFloat(ID_TessellationFactor, tessellationFactor);
        terrainMat.SetFloat(ID_DeformationStrength, deformationStrength);

        BakeTessellationRamp();
        if (tessRampTexture != null)
            terrainMat.SetTexture(ID_TessellationFalloffRamp, tessRampTexture);

        int layerCount = terrainMat.IsKeywordEnabled("_TERRAIN_8_LAYERS") ? 8 : 4;

        bool hasMaskMap = terrainMat.IsKeywordEnabled("_MASKMAP");
        if (enablePOM && !hasMaskMap)
        {
            Debug.LogWarning("TerrainLitCustomSetup: POM requires _MASKMAP keyword. " +
                "Assign Mask Maps to your Terrain Layers (POM reads height from the blue channel).");
        }

        for (int i = 0; i < layerCount; i++)
        {
            bool pomEnabled = i < layerPOMEnabled.Length && layerPOMEnabled[i];
            bool dispEnabled = i < layerDisplacementEnabled.Length && layerDisplacementEnabled[i];
            bool sandEnabled = i < layerSandEnabled.Length && layerSandEnabled[i];

            terrainMat.SetFloat(ID_EnablePOMLayer[i], pomEnabled ? 1f : 0f);
            terrainMat.SetFloat(ID_HeightScale[i], pomEnabled ? pomHeightScale : 0f);
            terrainMat.SetFloat(ID_POMMinSteps[i], (float)pomMinSteps);
            terrainMat.SetFloat(ID_POMMaxSteps[i], (float)pomMaxSteps);

            terrainMat.SetFloat(ID_EnableDisplacementLayer[i], dispEnabled ? 1f : 0f);
            terrainMat.SetFloat(ID_DisplacementScale[i], dispEnabled ? displacementScale : 0f);

            terrainMat.SetFloat(ID_EnableSandMode[i], sandEnabled ? 1f : 0f);
        }

        terrainMat.SetFloat(ID_TessellationMaxDisplacement, displacementScale);

        // --- Sand mode global parameters ---
        if (enableSandMode)
        {
            terrainMat.SetFloat(ID_SandGlitterIntensity, sandGlitterIntensity);
            terrainMat.SetFloat(ID_SandGlitterThreshold, sandGlitterThreshold);
            terrainMat.SetFloat(ID_SandGlitterScale, sandGlitterScale);
            terrainMat.SetColor(ID_SandGlitterColor, sandGlitterColor.linear); // ensure linear space
            terrainMat.SetFloat(ID_SandGlitterMaxDistance, sandGlitterMaxDistance);

            BakeGlitterRamp();
            if (glitterRampTexture != null)
                terrainMat.SetTexture(ID_SandGlitterFalloffRamp, glitterRampTexture);
            terrainMat.SetFloat(ID_SandRimPushUp, sandRimPushUp);
            terrainMat.SetFloat(ID_SandRippleScale, sandRippleScale);
            terrainMat.SetFloat(ID_SandRippleStrength, sandRippleStrength);
            terrainMat.SetFloat(ID_SandOceanSpecPower, sandOceanSpecPower);
            terrainMat.SetFloat(ID_SandOceanSpecIntensity, sandOceanSpecIntensity);
            terrainMat.SetFloat(ID_SandFresnelPower, sandFresnelPower);
            terrainMat.SetFloat(ID_SandFresnelIntensity, sandFresnelIntensity);

            // Push sun direction from main directional light
            PushSunDirection();

            // Deform texel size is pushed by TerrainDeformationManager
            // (see _SandDeformTexelSize). Fallback default is set in Properties block.
        }

        // --- Wind displacement parameters ---
        if (enableWindDisplacement && enableDisplacement)
        {
            PushWindParameters();
        }
        else
        {
            terrainMat.SetFloat(ID_WindMaxValue, 0f);
            terrainMat.SetFloat(ID_WindMinValue, 0f);
            terrainMat.SetFloat(ID_WindDebugMode, 0f);
        }

        // --- Grass tint (L2) parameters ---
        if (enableGrassTint)
        {
            terrainMat.SetColor(ID_GrassTintColor, grassTintColor.linear);
            terrainMat.SetFloat(ID_GrassTintStrength, grassTintStrength);
            terrainMat.SetFloat(ID_GrassTintSmoothness, grassTintSmoothness);
            terrainMat.SetFloat(ID_GrassTintDistanceStart, grassTintDistanceStart);
            terrainMat.SetFloat(ID_GrassTintDistanceFull, grassTintDistanceFull);
            terrainMat.SetFloat(ID_GrassWaveNormalStrength, grassWaveNormalStrength);
            terrainMat.SetFloat(ID_GrassWaveLumStrength, grassWaveLumStrength);
            for (int i = 0; i < layerCount; i++)
            {
                bool g = i < layerGrassTintEnabled.Length && layerGrassTintEnabled[i];
                terrainMat.SetFloat(ID_EnableGrassTintLayer[i], g ? 1f : 0f);
            }
        }

        // Debug logging
        if (debugLogDisplacement && enableDisplacement)
        {
            string log = $"[TerrainLitCustom] _MASKMAP={hasMaskMap} layerCount={layerCount} " +
                         $"tessFactor={terrainMat.GetFloat(ID_TessellationFactor):F1} " +
                         $"maxDisp={terrainMat.GetFloat(ID_TessellationMaxDisplacement):F3}" +
                         $" sandMode={enableSandMode}" +
                         $" windDisp={enableWindDisplacement}\n";
            for (int i = 0; i < layerCount; i++)
            {
                float scale = terrainMat.GetFloat(ID_DisplacementScale[i]);
                float enable = terrainMat.GetFloat(ID_EnableDisplacementLayer[i]);
                float sand = terrainMat.GetFloat(ID_EnableSandMode[i]);
                log += $"  Layer{i}: enable={enable:F0} scale={scale:F4} sand={sand:F0}";
                if (terrain.terrainData.terrainLayers != null && i < terrain.terrainData.terrainLayers.Length)
                {
                    var layer = terrain.terrainData.terrainLayers[i];
                    log += $" mask={layer?.maskMapTexture?.name ?? "NULL"}";
                    log += $" [{layer?.name ?? "?"}]";
                }
                log += "\n";
            }
            Debug.Log(log);
        }
    }

    // ---------------------------------------------------------------------------------
    // Push sun direction to the shader
    // Uses RenderSettings.sun (the main directional light in the scene).
    // Falls back to a default diagonal direction if no sun is found.
    // ---------------------------------------------------------------------------------
    void PushSunDirection()
    {
        Vector3 sunDir = new Vector3(0.3f, 0.8f, 0.5f).normalized; // default fallback

        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            // Fallback: find any directional light
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional && light.isActiveAndEnabled)
                {
                    sun = light;
                    break;
                }
            }
        }

        if (sun != null)
        {
            // Direction TO the sun (opposite of light's forward)
            sunDir = -sun.transform.forward;
        }

        terrainMat.SetVector(ID_SandSunDirection, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
    }

    // ---------------------------------------------------------------------------------
    // Push wind displacement parameters to the shader
    // ---------------------------------------------------------------------------------
    void PushWindParameters()
    {
        if (windNoiseGlobalMap != null)
            terrainMat.SetTexture(ID_WindGlobalMap, windNoiseGlobalMap);
        if (windNoiseDetailMap != null)
            terrainMat.SetTexture(ID_WindDetailMap, windNoiseDetailMap);

        terrainMat.SetFloat(ID_WindGlobalTile, windNoiseGlobalTile);
        terrainMat.SetFloat(ID_WindDetailTile, windNoiseDetailTile);
        terrainMat.SetFloat(ID_WindMinValue, windMinValue);
        terrainMat.SetFloat(ID_WindMaxValue, windMaxValue);
        terrainMat.SetVector(ID_WindGlobalOffsetDir, new Vector4(windGlobalOffsetDirection.x, windGlobalOffsetDirection.y, 0, 0));
        terrainMat.SetVector(ID_WindDetailOffsetDir, new Vector4(windDetailOffsetDirection.x, windDetailOffsetDirection.y, 0, 0));
        terrainMat.SetFloat(ID_WindPeriod, windPeriod);
        terrainMat.SetFloat(ID_WindTime, Time.time);
        terrainMat.SetFloat(ID_WindDebugMode, (float)windDebugMode);
    }

    // ---------------------------------------------------------------------------------
    // Tessellation falloff ramp
    // ---------------------------------------------------------------------------------
    void BakeTessellationRamp()
    {
        if (tessellationFalloff == null || tessellationFalloff.length == 0)
            return;

        int hash = ComputeCurveContentHash(tessellationFalloff);
        if (tessRampTexture != null && hash == tessRampHash)
            return;

        const int width = 256;
        if (tessRampTexture == null)
        {
            tessRampTexture = new Texture2D(width, 1, TextureFormat.RFloat, false)
            {
                name = "TessellationFalloffRamp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        Color[] pixels = new Color[width];
        for (int i = 0; i < width; i++)
        {
            float t = (float)i / (width - 1);
            pixels[i] = new Color(Mathf.Clamp01(tessellationFalloff.Evaluate(t)), 0, 0, 0);
        }
        tessRampTexture.SetPixels(pixels);
        tessRampTexture.Apply(false, false);
        tessRampHash = hash;
    }

    // ---------------------------------------------------------------------------------
    // Glitter distance falloff ramp
    // ---------------------------------------------------------------------------------
    void BakeGlitterRamp()
    {
        if (sandGlitterFalloff == null || sandGlitterFalloff.length == 0)
            return;

        int hash = ComputeCurveContentHash(sandGlitterFalloff);
        if (glitterRampTexture != null && hash == glitterRampHash)
            return;

        const int width = 256;
        if (glitterRampTexture == null)
        {
            glitterRampTexture = new Texture2D(width, 1, TextureFormat.RFloat, false)
            {
                name = "GlitterFalloffRamp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        Color[] pixels = new Color[width];
        for (int i = 0; i < width; i++)
        {
            float t = (float)i / (width - 1);
            pixels[i] = new Color(Mathf.Clamp01(sandGlitterFalloff.Evaluate(t)), 0, 0, 0);
        }
        glitterRampTexture.SetPixels(pixels);
        glitterRampTexture.Apply(false, false);
        glitterRampHash = hash;
    }

    void OnDisable()
    {
        if (tessRampTexture != null)
        {
            DestroyImmediate(tessRampTexture);
            tessRampTexture = null;
        }
        if (glitterRampTexture != null)
        {
            DestroyImmediate(glitterRampTexture);
            glitterRampTexture = null;
        }
    }

    void UpdatePatchBounds()
    {
        if (terrain == null || terrain.terrainData == null) return;

        float maxDisp = displacementScale;
        // Account for sand rim push-up in patch bounds
        if (enableSandMode)
            maxDisp += sandRimPushUp;
        // Account for wind displacement amplitude
        if (enableWindDisplacement)
            maxDisp += Mathf.Max(Mathf.Abs(windMinValue), Mathf.Abs(windMaxValue));

        if (enableDisplacement && maxDisp > 0)
        {
            float terrainHeight = Mathf.Max(terrain.terrainData.size.y, 0.01f);
            float boundsY = Mathf.Max(
                maxDisp * 65535f / terrainHeight,
                2f);
            boundsY = Mathf.Min(boundsY, 10000f);
            terrain.patchBoundsMultiplier = new Vector3(1f, boundsY, 1f);
        }
        else
        {
            terrain.patchBoundsMultiplier = Vector3.one;
        }
    }

    static int ComputeCurveContentHash(AnimationCurve curve)
    {
        int hash = 17;
        foreach (var key in curve.keys)
        {
            hash = hash * 31 + key.time.GetHashCode();
            hash = hash * 31 + key.value.GetHashCode();
            hash = hash * 31 + key.inTangent.GetHashCode();
            hash = hash * 31 + key.outTangent.GetHashCode();
        }
        return hash;
    }

    static void SetKeyword(Material mat, string keyword, bool enabled)
    {
        if (enabled)
            mat.EnableKeyword(keyword);
        else
            mat.DisableKeyword(keyword);
    }
}
