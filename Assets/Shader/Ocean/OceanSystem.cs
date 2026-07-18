using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ocean
{
    // ──────────────────────────────────────────────
    //  Enums (formerly nested in OceanPlane / OceanManager)
    // ──────────────────────────────────────────────

    public enum CoverageSize
    {
        [InspectorName("1 × 1 tile (small, near-field only)")]
        _1x1 = 1,
        [InspectorName("3 × 3 tiles (recommended)")]
        _3x3 = 3,
        [InspectorName("5 × 5 tiles (wide horizon)")]
        _5x5 = 5,
        [InspectorName("7 × 7 tiles (extreme distance)")]
        _7x7 = 7
    }

    public enum OceanDebugView
    {
        [InspectorName("Off")]
        Off,
        [InspectorName("H₀ Spectrum")]
        H0Spectrum,
        [InspectorName("Displacement Y (height)")]
        DisplacementY,
        [InspectorName("Displacement X (choppy)")]
        DisplacementX,
        [InspectorName("Displacement Z (choppy)")]
        DisplacementZ,
        [InspectorName("Normal map")]
        NormalMap,
        [InspectorName("Foam")]
        Foam,
        [InspectorName("Shore Map (intersection)")]
        ShoreMap
    }

    // ──────────────────────────────────────────────
    //  OceanSystem — single MonoBehaviour for the
    //  entire ocean pipeline: mesh, FFT, shading,
    //  wake trail, planar reflections.
    // ──────────────────────────────────────────────

    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class OceanSystem : MonoBehaviour
    {
        private const int MAX_CASCADES = 3;

        // ═══════════════════════════════════════════
        //  Inspector
        // ═══════════════════════════════════════════

        [Header("Configuration")]

        [Tooltip("Ocean settings asset. Create via Assets → Create → Ocean → Ocean Settings.")]
        public OceanSettings settings;

        [Tooltip("Override sun light. If null, uses RenderSettings.sun or first directional light.")]
        public Light sunLight;

        [Tooltip("WindZone driving ocean wind. If null, defaults to 10 m/s East.")]
        public WindZone windZone;

        // ── Mesh ────────────────────────────────────

        [Header("Ocean Mesh")]

        [Tooltip("Size of one tile in meters. Should match OceanSettings.patchSize for correct UV mapping.")]
        public float tileSize = 250f;

        [Tooltip("How many tiles the plane covers. 3×3 = 9 tiles around camera. Larger = visible to horizon but more triangles.")]
        public CoverageSize coverage = CoverageSize._3x3;

        [Tooltip("Extra vertical bounds to prevent culling when waves displace vertices above/below the plane. Set higher than your max wave amplitude.")]
        [Range(1f, 50f)]
        public float boundsHeightPadding = 15f;

        // ── Wake ────────────────────────────────────

        [Header("Wake Trail")]

        [Tooltip("The transform to track for wake generation.")]
        public Transform wakeTarget;

        // ── Debug ───────────────────────────────────

        [Header("Debug")]

        [Tooltip("Quad to display FFT debug output.")]
        public Renderer fftDebugQuad;

        [Tooltip("Quad to visualize the wake RT.")]
        public Renderer wakeDebugQuad;


        // ═══════════════════════════════════════════
        //  Private state — Mesh
        // ═══════════════════════════════════════════

        private Mesh _mesh;
        private Camera _mainCam;
        private int _meshCachedRes;
        private float _meshCachedTileSize;
        private int _meshCachedCoverage;

        // ═══════════════════════════════════════════
        //  Private state — FFT
        // ═══════════════════════════════════════════

        private readonly RenderTexture[] _h0RTs             = new RenderTexture[MAX_CASCADES];
        private readonly RenderTexture[] _displacementYRTs   = new RenderTexture[MAX_CASCADES];
        private readonly RenderTexture[] _displacementXRTs   = new RenderTexture[MAX_CASCADES];
        private readonly RenderTexture[] _displacementZRTs   = new RenderTexture[MAX_CASCADES];
        private readonly RenderTexture[] _normalMapRTs       = new RenderTexture[MAX_CASCADES];
        private readonly RenderTexture[] _foamRT_As          = new RenderTexture[MAX_CASCADES];
        private readonly RenderTexture[] _foamRT_Bs          = new RenderTexture[MAX_CASCADES];
        private readonly bool[]          _foamPingIsAs        = new bool[MAX_CASCADES];

        private RenderTexture _dySpectrumRT;
        private RenderTexture _dxSpectrumRT;
        private RenderTexture _dzSpectrumRT;
        private RenderTexture _fftPingRT;
        private RenderTexture _fftPongRT;
        private RenderTexture _foamBlurTempRT;

        private Material _fftDebugMaterial;

        private int _initKernel        = -1;
        private int _timeDepKernel     = -1;
        private int _butterflyKernel   = -1;
        private int _finalScaleKernel  = -1;
        private int _postProcessKernel = -1;
        private int _foamBlurHKernel   = -1;
        private int _foamBlurVKernel   = -1;

        private int _fftCachedRes;
        private int _cachedSeed;
        private float _cachedWindSpeed;
        private float _cachedWindAngle;
        private float _cachedAmplitude;
        private float _cachedPatchSize;
        private float _cachedSmallWaveCutoff;
        private SpectrumType _cachedSpectrumType;
        private float _cachedFetchLength;
        private float _cachedJonswapGamma;
        private int _cachedCascadeCount;
        private float _cachedCascadeScale;

        private bool _fftInitialized;
        private int _logN;

        private static readonly string[] _dispYNames  = { "_OceanDisplacementY", "_OceanDisplacementY1", "_OceanDisplacementY2" };
        private static readonly string[] _dispXNames  = { "_OceanDisplacementX", "_OceanDisplacementX1", "_OceanDisplacementX2" };
        private static readonly string[] _dispZNames  = { "_OceanDisplacementZ", "_OceanDisplacementZ1", "_OceanDisplacementZ2" };
        private static readonly string[] _normalNames = { "_OceanNormalMap", "_OceanNormalMap1", "_OceanNormalMap2" };
        private static readonly string[] _foamNames   = { "_OceanFoamMap", "_OceanFoamMap1", "_OceanFoamMap2" };
        private static readonly string[] _patchNames  = { "_OceanPatchSize", "_OceanPatchSize1", "_OceanPatchSize2" };

        // ═══════════════════════════════════════════
        //  Private state — Wake
        // ═══════════════════════════════════════════

        private RenderTexture _wakeRT;
        private RenderTexture _wakeTempRT;
        private Material _wakeDebugMaterial;
        private Vector3 _wakeLastPos;
        private bool _wakeHasLastPos;
        private int _wakeCachedRes;

        private int _wakePaintKernel  = -1;
        private int _wakeBlurHKernel  = -1;
        private int _wakeBlurVKernel  = -1;
        private int _wakeFadeKernel   = -1;

        // ═══════════════════════════════════════════
        //  Private state — Shore Intersection Map
        // ═══════════════════════════════════════════

        private RenderTexture _shoreIntersectionRT;
        private Terrain _cachedTerrain;
        private int _shoreIntersectionKernel = -1;
        private int _shoreCachedRes;

        // ═══════════════════════════════════════════
        //  Private state — Shading
        // ═══════════════════════════════════════════

        private Light _cachedSunLight;
        private Material _oceanMat;

        // ═══════════════════════════════════════════
        //  Private state — Planar Reflection
        // ═══════════════════════════════════════════

        private Camera _reflectionCamera;
        private RenderTexture _reflectionRT;
        private int _reflCachedRes;
        private int _reflFrameCounter;

        // ═══════════════════════════════════════════
        //  Shader property IDs
        // ═══════════════════════════════════════════

        // Shading
        private static readonly int ID_ShallowColor          = Shader.PropertyToID("_ShallowColor");
        private static readonly int ID_DeepColor             = Shader.PropertyToID("_DeepColor");
        private static readonly int ID_SSSColor              = Shader.PropertyToID("_SSSColor");
        private static readonly int ID_FoamColor             = Shader.PropertyToID("_FoamColor");
        private static readonly int ID_FresnelPower          = Shader.PropertyToID("_FresnelPower");
        private static readonly int ID_SSSIntensity          = Shader.PropertyToID("_SSSIntensity");
        private static readonly int ID_SSSPower              = Shader.PropertyToID("_SSSPower");
        private static readonly int ID_SSSSpread             = Shader.PropertyToID("_SSSSpread");
        private static readonly int ID_SpecularPower         = Shader.PropertyToID("_SpecularPower");
        private static readonly int ID_SpecularIntensity     = Shader.PropertyToID("_SpecularIntensity");
        private static readonly int ID_HeightScale           = Shader.PropertyToID("_HeightScale");
        private static readonly int ID_AmbientStrength       = Shader.PropertyToID("_AmbientStrength");
        private static readonly int ID_FoamTexScale          = Shader.PropertyToID("_FoamTexScale");
        private static readonly int ID_FoamTexBlend          = Shader.PropertyToID("_FoamTexBlend");
        private static readonly int ID_DepthAbsorption       = Shader.PropertyToID("_DepthAbsorption");
        private static readonly int ID_DepthMaxDistance       = Shader.PropertyToID("_DepthMaxDistance");
        private static readonly int ID_AbsorptionColor       = Shader.PropertyToID("_AbsorptionColor");
        private static readonly int ID_RefractionStrength    = Shader.PropertyToID("_RefractionStrength");
        private static readonly int ID_RefractionDepthFade   = Shader.PropertyToID("_RefractionDepthFade");
        private static readonly int ID_TessMaxFactor         = Shader.PropertyToID("_TessMaxFactor");
        private static readonly int ID_TessMaxDistance        = Shader.PropertyToID("_TessMaxDistance");
        private static readonly int ID_SunDirection          = Shader.PropertyToID("_SunDirection");
        private static readonly int ID_WrapDiffuse           = Shader.PropertyToID("_WrapDiffuse");
        private static readonly int ID_OceanRoughness        = Shader.PropertyToID("_OceanRoughness");
        private static readonly int ID_ReflectionIntensity   = Shader.PropertyToID("_ReflectionIntensity");
        private static readonly int ID_HorizonColor          = Shader.PropertyToID("_HorizonColor");
        private static readonly int ID_ZenithColor           = Shader.PropertyToID("_ZenithColor");
        private static readonly int ID_UsePlanarReflection   = Shader.PropertyToID("_UsePlanarReflection");
        private static readonly int ID_PlanarReflectionBlend = Shader.PropertyToID("_PlanarReflectionBlend");
        private static readonly int ID_ShoreFoamDistance     = Shader.PropertyToID("_ShoreFoamDistance");
        private static readonly int ID_ShoreFoamStrength     = Shader.PropertyToID("_ShoreFoamStrength");
        private static readonly int ID_ShoreFoamFalloff      = Shader.PropertyToID("_ShoreFoamFalloff");

        // Wake
        private static readonly int ID_WakeRT        = Shader.PropertyToID("_WakeRT");
        private static readonly int ID_WakeInput     = Shader.PropertyToID("_WakeInput");
        private static readonly int ID_WakeOutput    = Shader.PropertyToID("_WakeOutput");
        private static readonly int ID_WakeResolution = Shader.PropertyToID("_Resolution");
        private static readonly int ID_BoatUV        = Shader.PropertyToID("_BoatUV");
        private static readonly int ID_BoatDir       = Shader.PropertyToID("_BoatDir");
        private static readonly int ID_BoatSpeed     = Shader.PropertyToID("_BoatSpeed");
        private static readonly int ID_StampRadius   = Shader.PropertyToID("_StampRadius");
        private static readonly int ID_WakeIntensity = Shader.PropertyToID("_WakeIntensity");
        private static readonly int ID_SplashWidth   = Shader.PropertyToID("_SplashWidth");
        private static readonly int ID_BlurStrength  = Shader.PropertyToID("_BlurStrength");
        private static readonly int ID_FadeRate      = Shader.PropertyToID("_FadeRate");

        // Reflection
        private static readonly int ID_PlanarReflectionTex = Shader.PropertyToID("_OceanPlanarReflectionTex");

        // Caustics
        private static readonly int ID_CausticsScale     = Shader.PropertyToID("_CausticsScale");
        private static readonly int ID_CausticsIntensity = Shader.PropertyToID("_CausticsIntensity");
        private static readonly int ID_CausticsMaxDepth  = Shader.PropertyToID("_CausticsMaxDepth");
        private static readonly int ID_CausticsChromaSpread  = Shader.PropertyToID("_CausticsChromaSpread");
        private static readonly int ID_CausticsChromaOffsetR = Shader.PropertyToID("_CausticsChromaOffsetR");
        private static readonly int ID_CausticsChromaOffsetB = Shader.PropertyToID("_CausticsChromaOffsetB");

        // ═══════════════════════════════════════════
        //  Lifecycle
        // ═══════════════════════════════════════════

        private void OnEnable()
        {
            _fftInitialized = false;
            FFTValidateAndInit();
            WakeCreateResources();
            ReflectionCreateResources();
            MeshGenerateIfNeeded();

            var rend = GetComponent<MeshRenderer>();
            if (rend != null)
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            FFTReleaseAll();
            WakeReleaseResources();
            ReflectionReleaseResources();
            ShoreReleaseResources();
            MeshRelease();
        }

        private void OnValidate()
        {
            _meshCachedRes = 0;
            MeshGenerateIfNeeded();
        }

        private void Update()
        {
            if (settings == null) return;

            // 1. FFT pipeline
            UpdateFFT();

            // 2. Shore intersection map (reads FFT, writes world-space intersection RT)
            UpdateShoreIntersection();

            // 3. Wake trail
            UpdateWake();

            // 4. Push shading params to material
            UpdateMaterialParams();
        }

        private void LateUpdate()
        {
            if (settings == null) return;

            MeshGenerateIfNeeded();

            if (settings.followMode == FollowMode.FollowCamera)
                MeshSnapToCamera();
            else
                MeshApplyWaterLevel();
        }

        // ═══════════════════════════════════════════
        //  Mesh
        // ═══════════════════════════════════════════

        private float WaterLevel => settings != null ? settings.waterLevel : 0f;

        private void MeshApplyWaterLevel()
        {
            var pos = transform.position;
            if (!Mathf.Approximately(pos.y, WaterLevel))
                transform.position = new Vector3(pos.x, WaterLevel, pos.z);
        }

        private void MeshSnapToCamera()
        {
            Camera cam = GetCamera();
            if (cam == null) return;

            Vector3 camPos = cam.transform.position;
            float snapX = Mathf.Floor(camPos.x / tileSize) * tileSize;
            float snapZ = Mathf.Floor(camPos.z / tileSize) * tileSize;

            transform.position = new Vector3(snapX, WaterLevel, snapZ);
        }

        private Camera GetCamera()
        {
            if (_mainCam != null && _mainCam.isActiveAndEnabled)
                return _mainCam;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sceneView = UnityEditor.SceneView.lastActiveSceneView;
                if (sceneView != null && sceneView.camera != null)
                    return sceneView.camera;
            }
#endif

            _mainCam = Camera.main;
            return _mainCam;
        }

        private void MeshGenerateIfNeeded()
        {
            if (settings == null) return;

            int cov = (int)coverage;
            if (_mesh != null &&
                _meshCachedRes == (int)settings.meshResolution &&
                Mathf.Approximately(_meshCachedTileSize, tileSize) &&
                _meshCachedCoverage == cov)
                return;

            MeshGenerate();
        }

        private void MeshGenerate()
        {
            int resPerTile = (int)settings.meshResolution;
            int tiles = (int)coverage;
            int totalRes = resPerTile * tiles;

            int vertCount = (totalRes + 1) * (totalRes + 1);
            int triCount = totalRes * totalRes * 6;

            var vertices = new Vector3[vertCount];
            var uvs      = new Vector2[vertCount];
            var normals  = new Vector3[vertCount];
            var indices  = new int[triCount];

            float totalSize = tileSize * tiles;
            float halfSize = totalSize * 0.5f;
            float step = totalSize / totalRes;

            int v = 0;
            for (int z = 0; z <= totalRes; z++)
            {
                for (int x = 0; x <= totalRes; x++)
                {
                    vertices[v] = new Vector3(-halfSize + x * step, 0f, -halfSize + z * step);
                    uvs[v] = new Vector2((float)x / totalRes, (float)z / totalRes);
                    normals[v] = Vector3.up;
                    v++;
                }
            }

            int t = 0;
            for (int z = 0; z < totalRes; z++)
            {
                for (int x = 0; x < totalRes; x++)
                {
                    int topLeft  = z * (totalRes + 1) + x;
                    int topRight = topLeft + 1;
                    int botLeft  = topLeft + (totalRes + 1);
                    int botRight = botLeft + 1;

                    indices[t++] = topLeft;
                    indices[t++] = botLeft;
                    indices[t++] = topRight;

                    indices[t++] = topRight;
                    indices[t++] = botLeft;
                    indices[t++] = botRight;
                }
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "OceanPlane" };
            }
            else
            {
                _mesh.Clear();
            }

            _mesh.indexFormat = vertCount > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

            _mesh.vertices  = vertices;
            _mesh.uv        = uvs;
            _mesh.normals   = normals;
            _mesh.triangles = indices;

            _mesh.RecalculateBounds();
            var bounds = _mesh.bounds;
            bounds.Expand(new Vector3(0f, boundsHeightPadding * 2f, 0f));
            _mesh.bounds = bounds;

            GetComponent<MeshFilter>().sharedMesh = _mesh;

            _meshCachedRes      = (int)settings.meshResolution;
            _meshCachedTileSize = tileSize;
            _meshCachedCoverage = (int)coverage;

            if (settings != null && settings.verboseLogs)
                Debug.Log($"[OceanSystem] Mesh: {tiles}×{tiles} tiles ({totalRes}×{totalRes} quads, {vertCount} verts, {totalSize}m total)", this);
        }

        private void MeshRelease()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        private void OnDrawGizmosSelected()
        {
            int tiles = (int)coverage;
            float totalSize = tileSize * tiles;
            float half = totalSize * 0.5f;

            Gizmos.color = new Color(0, 0.7f, 1f, 0.25f);
            for (int i = 0; i <= tiles; i++)
            {
                float offset = -half + i * tileSize;
                Gizmos.DrawLine(
                    transform.position + new Vector3(offset, 0, -half),
                    transform.position + new Vector3(offset, 0, half));
                Gizmos.DrawLine(
                    transform.position + new Vector3(-half, 0, offset),
                    transform.position + new Vector3(half, 0, offset));
            }
        }

        // ═══════════════════════════════════════════
        //  FFT Pipeline
        // ═══════════════════════════════════════════

        private void UpdateFFT()
        {
            if (!_fftInitialized)
            {
                FFTValidateAndInit();
                return;
            }

            if (FFTSpectrumParamsChanged())
            {
                FFTReleaseAll();
                _fftInitialized = false;
                FFTValidateAndInit();
                if (!_fftInitialized) return;
            }

            float t;
#if UNITY_EDITOR
            t = Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            t = Time.time;
#endif
            t *= settings.timeScale;

            int cascades = settings.cascadeCount;
            for (int c = 0; c < cascades; c++)
            {
                float patchSize = settings.CascadePatchSize(c);
                FFTDispatchTimeDependentSpectrum(t, c, patchSize);
                FFTPerformIFFT(_dySpectrumRT, _displacementYRTs[c]);
                FFTPerformIFFT(_dxSpectrumRT, _displacementXRTs[c]);
                FFTPerformIFFT(_dzSpectrumRT, _displacementZRTs[c]);
                FFTDispatchPostProcess(c, patchSize);
                FFTDispatchFoamBlur(c);
            }

            FFTPushGlobalTextures();
            FFTUpdateDebugQuad();
        }

        private void FFTValidateAndInit()
        {
            if (settings == null || settings.initSpectrumShader == null ||
                settings.timeDependentSpectrumShader == null || settings.fftShader == null ||
                settings.postProcessShader == null)
                return;

            if (settings.foamBlurShader == null && settings.verboseLogs)
                Debug.LogWarning("[OceanSystem] Foam Blur compute not assigned — foam blur disabled.", this);

            int res = settings.ResolutionInt;
            _logN = (int)Mathf.Log(res, 2);
            int cascades = settings.cascadeCount;

            if (settings.verboseLogs)
                Debug.Log($"[OceanSystem] FFT init: {res}×{res}, logN={_logN}, {settings.spectrumType}, " +
                          $"wind={GetEffectiveWindSpeed():F1}m/s, cascades={cascades}", this);

            FFTCreateRTs();
            FFTFindKernels();

            if (_initKernel < 0 || _timeDepKernel < 0 || _butterflyKernel < 0 ||
                _finalScaleKernel < 0 || _postProcessKernel < 0)
            {
                Debug.LogError("[OceanSystem] One or more FFT kernels not found!", this);
                return;
            }

            for (int c = 0; c < cascades; c++)
            {
                float patchSize = settings.CascadePatchSize(c);
                float minWL = (c < cascades - 1) ? settings.CascadePatchSize(c + 1) : 0f;
                FFTDispatchInitSpectrum(c, patchSize, minWL);
            }

            FFTCacheSpectrumParams();
            _fftInitialized = true;

            if (settings.verboseLogs)
            {
                for (int c = 0; c < cascades; c++)
                    Debug.Log($"[OceanSystem] Cascade {c}: patch={settings.CascadePatchSize(c):F1}m", this);
                Debug.Log("[OceanSystem] FFT pipeline ready.", this);
            }
        }

        private void FFTCreateRTs()
        {
            int res = settings.ResolutionInt;
            int cascades = settings.cascadeCount;

            for (int c = 0; c < cascades; c++)
            {
                _h0RTs[c]             = CreateRT(res, RenderTextureFormat.ARGBFloat, $"OceanH0_C{c}");
                _displacementYRTs[c]  = CreateRT(res, RenderTextureFormat.RGFloat,   $"OceanDispY_C{c}");
                _displacementXRTs[c]  = CreateRT(res, RenderTextureFormat.RGFloat,   $"OceanDispX_C{c}");
                _displacementZRTs[c]  = CreateRT(res, RenderTextureFormat.RGFloat,   $"OceanDispZ_C{c}");
                _normalMapRTs[c]      = CreateRT(res, RenderTextureFormat.ARGBFloat, $"OceanNormals_C{c}");
                _foamRT_As[c]         = CreateRT(res, RenderTextureFormat.RFloat,    $"OceanFoamA_C{c}");
                _foamRT_Bs[c]         = CreateRT(res, RenderTextureFormat.RFloat,    $"OceanFoamB_C{c}");
                _foamPingIsAs[c]      = true;
            }

            _dySpectrumRT = CreateRT(res, RenderTextureFormat.RGFloat, "OceanDySpectrum");
            _dxSpectrumRT = CreateRT(res, RenderTextureFormat.RGFloat, "OceanDxSpectrum");
            _dzSpectrumRT = CreateRT(res, RenderTextureFormat.RGFloat, "OceanDzSpectrum");
            _fftPingRT    = CreateRT(res, RenderTextureFormat.RGFloat, "OceanFFTPing");
            _fftPongRT    = CreateRT(res, RenderTextureFormat.RGFloat, "OceanFFTPong");
            if (settings.foamBlurShader != null)
                _foamBlurTempRT = CreateRT(res, RenderTextureFormat.RFloat, "OceanFoamBlurTemp");
        }

        private void FFTReleaseAll()
        {
            for (int c = 0; c < MAX_CASCADES; c++)
            {
                ReleaseRT(ref _h0RTs[c]);
                ReleaseRT(ref _displacementYRTs[c]);
                ReleaseRT(ref _displacementXRTs[c]);
                ReleaseRT(ref _displacementZRTs[c]);
                ReleaseRT(ref _normalMapRTs[c]);
                ReleaseRT(ref _foamRT_As[c]);
                ReleaseRT(ref _foamRT_Bs[c]);
            }

            ReleaseRT(ref _dySpectrumRT);
            ReleaseRT(ref _dxSpectrumRT);
            ReleaseRT(ref _dzSpectrumRT);
            ReleaseRT(ref _fftPingRT);
            ReleaseRT(ref _fftPongRT);
            ReleaseRT(ref _foamBlurTempRT);

            SafeDestroy(ref _fftDebugMaterial);
        }

        private void FFTFindKernels()
        {
            _initKernel        = SafeFindKernel(settings.initSpectrumShader,        "InitSpectrum");
            _timeDepKernel     = SafeFindKernel(settings.timeDependentSpectrumShader, "TimeDependentSpectrum");
            _butterflyKernel   = SafeFindKernel(settings.fftShader,                 "ButterflyPass");
            _finalScaleKernel  = SafeFindKernel(settings.fftShader,                 "FinalPermute");
            _postProcessKernel = SafeFindKernel(settings.postProcessShader,         "PostProcess");
            if (settings.foamBlurShader != null)
            {
                _foamBlurHKernel = SafeFindKernel(settings.foamBlurShader, "BlurHorizontal");
                _foamBlurVKernel = SafeFindKernel(settings.foamBlurShader, "BlurVertical");
            }
        }

        private void FFTDispatchInitSpectrum(int cascade, float patchSize, float minWavelength)
        {
            int res = settings.ResolutionInt;
            Vector2 windDir = GetEffectiveWindDirection();
            float windSpeed = GetEffectiveWindSpeed();

            var cs = settings.initSpectrumShader;
            cs.SetTexture(_initKernel, "_H0Texture", _h0RTs[cascade]);
            cs.SetInt("_Resolution", res);
            cs.SetFloat("_PatchSize", patchSize);
            cs.SetFloats("_WindDirection", windDir.x, windDir.y);
            cs.SetFloat("_WindSpeed", windSpeed);
            cs.SetFloat("_Amplitude", settings.amplitude);
            cs.SetFloat("_SmallWaveCutoff", settings.smallWaveCutoff);
            cs.SetFloat("_Gravity", 9.81f);
            cs.SetInt("_Seed", settings.seed + cascade * 7919);
            cs.SetInt("_UseJONSWAP", settings.spectrumType == SpectrumType.JONSWAP ? 1 : 0);
            cs.SetFloat("_FetchLength", settings.fetchLength);
            cs.SetFloat("_JonswapGamma", settings.jonswapGamma);
            cs.SetFloat("_MinWavelength", minWavelength);

            int groups = Mathf.CeilToInt(res / 8f);
            cs.Dispatch(_initKernel, groups, groups, 1);
        }

        private void FFTDispatchTimeDependentSpectrum(float time, int cascade, float patchSize)
        {
            int res = settings.ResolutionInt;
            var cs = settings.timeDependentSpectrumShader;

            cs.SetTexture(_timeDepKernel, "_H0Texture", _h0RTs[cascade]);
            cs.SetTexture(_timeDepKernel, "_DySpectrum", _dySpectrumRT);
            cs.SetTexture(_timeDepKernel, "_DxSpectrum", _dxSpectrumRT);
            cs.SetTexture(_timeDepKernel, "_DzSpectrum", _dzSpectrumRT);
            cs.SetInt("_Resolution", res);
            cs.SetFloat("_PatchSize", patchSize);
            cs.SetFloat("_Time", time);
            cs.SetFloat("_Gravity", 9.81f);
            cs.SetFloat("_Choppiness", settings.choppiness);

            int groups = Mathf.CeilToInt(res / 8f);
            cs.Dispatch(_timeDepKernel, groups, groups, 1);
        }

        private void FFTPerformIFFT(RenderTexture spectrumInput, RenderTexture spatialOutput)
        {
            int res = settings.ResolutionInt;
            int groups = Mathf.CeilToInt(res / 8f);

            Graphics.Blit(spectrumInput, _fftPingRT);

            bool pingIsInput = true;

            settings.fftShader.SetInt("_Resolution", res);
            settings.fftShader.SetInt("_IsVertical", 0);

            for (int stage = 0; stage < _logN; stage++)
            {
                var inputRT  = pingIsInput ? _fftPingRT : _fftPongRT;
                var outputRT = pingIsInput ? _fftPongRT : _fftPingRT;

                settings.fftShader.SetInt("_Stage", stage);
                settings.fftShader.SetTexture(_butterflyKernel, "_Input", inputRT);
                settings.fftShader.SetTexture(_butterflyKernel, "_Output", outputRT);
                settings.fftShader.Dispatch(_butterflyKernel, groups, groups, 1);

                pingIsInput = !pingIsInput;
            }

            settings.fftShader.SetInt("_IsVertical", 1);

            for (int stage = 0; stage < _logN; stage++)
            {
                var inputRT  = pingIsInput ? _fftPingRT : _fftPongRT;
                var outputRT = pingIsInput ? _fftPongRT : _fftPingRT;

                settings.fftShader.SetInt("_Stage", stage);
                settings.fftShader.SetTexture(_butterflyKernel, "_Input", inputRT);
                settings.fftShader.SetTexture(_butterflyKernel, "_Output", outputRT);
                settings.fftShader.Dispatch(_butterflyKernel, groups, groups, 1);

                pingIsInput = !pingIsInput;
            }

            {
                var inputRT = pingIsInput ? _fftPingRT : _fftPongRT;
                settings.fftShader.SetTexture(_finalScaleKernel, "_Input", inputRT);
                settings.fftShader.SetTexture(_finalScaleKernel, "_Output", spatialOutput);
                settings.fftShader.Dispatch(_finalScaleKernel, groups, groups, 1);
            }
        }

        private void FFTDispatchPostProcess(int cascade, float patchSize)
        {
            int res = settings.ResolutionInt;
            int groups = Mathf.CeilToInt(res / 8f);

            var foamPrev    = _foamPingIsAs[cascade] ? _foamRT_Bs[cascade] : _foamRT_As[cascade];
            var foamCurrent = _foamPingIsAs[cascade] ? _foamRT_As[cascade] : _foamRT_Bs[cascade];

            var cs = settings.postProcessShader;
            cs.SetTexture(_postProcessKernel, "_DisplacementY", _displacementYRTs[cascade]);
            cs.SetTexture(_postProcessKernel, "_DisplacementX", _displacementXRTs[cascade]);
            cs.SetTexture(_postProcessKernel, "_DisplacementZ", _displacementZRTs[cascade]);
            cs.SetTexture(_postProcessKernel, "_FoamPrev", foamPrev);
            cs.SetTexture(_postProcessKernel, "_NormalMap", _normalMapRTs[cascade]);
            cs.SetTexture(_postProcessKernel, "_FoamCurrent", foamCurrent);

            cs.SetInt("_Resolution", res);
            cs.SetFloat("_PatchSize", patchSize);
            cs.SetFloat("_FoamThreshold", settings.foamThreshold);
            cs.SetFloat("_FoamDecay", settings.foamDecay);
            cs.SetFloat("_FoamStrength", settings.foamStrength);
            cs.SetFloat("_HeightScale", settings.heightScale);
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) dt = 1f / 60f;
            cs.SetFloat("_DeltaTime", dt);

            cs.Dispatch(_postProcessKernel, groups, groups, 1);

            _foamPingIsAs[cascade] = !_foamPingIsAs[cascade];
        }

        private void FFTDispatchFoamBlur(int cascade)
        {
            if (settings.foamBlurShader == null || _foamBlurHKernel < 0 || _foamBlurVKernel < 0) return;
            if (settings.foamBlurRadius <= 0.01f) return;

            int res = settings.ResolutionInt;
            int groups = Mathf.CeilToInt(res / 8f);

            var foamJustWritten = _foamPingIsAs[cascade] ? _foamRT_Bs[cascade] : _foamRT_As[cascade];

            var cs = settings.foamBlurShader;
            cs.SetInt("_Resolution", res);
            cs.SetFloat("_BlurRadius", settings.foamBlurRadius);

            cs.SetTexture(_foamBlurHKernel, "_BlurInput", foamJustWritten);
            cs.SetTexture(_foamBlurHKernel, "_BlurOutput", _foamBlurTempRT);
            cs.Dispatch(_foamBlurHKernel, groups, groups, 1);

            cs.SetTexture(_foamBlurVKernel, "_BlurInput", _foamBlurTempRT);
            cs.SetTexture(_foamBlurVKernel, "_BlurOutput", foamJustWritten);
            cs.Dispatch(_foamBlurVKernel, groups, groups, 1);
        }

        private void FFTPushGlobalTextures()
        {
            int cascades = settings.cascadeCount;

            for (int c = 0; c < cascades; c++)
            {
                Shader.SetGlobalTexture(_dispYNames[c], _displacementYRTs[c]);
                Shader.SetGlobalTexture(_dispXNames[c], _displacementXRTs[c]);
                Shader.SetGlobalTexture(_dispZNames[c], _displacementZRTs[c]);
                Shader.SetGlobalTexture(_normalNames[c], _normalMapRTs[c]);

                var foamJustWritten = _foamPingIsAs[c] ? _foamRT_Bs[c] : _foamRT_As[c];
                Shader.SetGlobalTexture(_foamNames[c], foamJustWritten);

                Shader.SetGlobalFloat(_patchNames[c], settings.CascadePatchSize(c));
            }

            Shader.SetGlobalInt("_OceanCascadeCount", cascades);
            Shader.SetGlobalFloat("_OceanWaterLevel", settings.waterLevel);

            Shader.SetGlobalFloat(ID_CausticsScale, settings.causticsScale);
            Shader.SetGlobalFloat(ID_CausticsIntensity, settings.causticsIntensity);
            Shader.SetGlobalFloat(ID_CausticsMaxDepth, settings.causticsMaxDepth);
            Shader.SetGlobalFloat(ID_CausticsChromaSpread, settings.causticsChromaSpread);
            Shader.SetGlobalVector(ID_CausticsChromaOffsetR, settings.causticsChromaOffsetR);
            Shader.SetGlobalVector(ID_CausticsChromaOffsetB, settings.causticsChromaOffsetB);
        }

        private bool FFTSpectrumParamsChanged()
        {
            return _fftCachedRes         != settings.ResolutionInt
                || _cachedSeed           != settings.seed
                || !Mathf.Approximately(_cachedWindSpeed, GetEffectiveWindSpeed())
                || !Mathf.Approximately(_cachedWindAngle, GetEffectiveWindAngle())
                || !Mathf.Approximately(_cachedAmplitude, settings.amplitude)
                || !Mathf.Approximately(_cachedPatchSize, settings.patchSize)
                || !Mathf.Approximately(_cachedSmallWaveCutoff, settings.smallWaveCutoff)
                || _cachedSpectrumType   != settings.spectrumType
                || !Mathf.Approximately(_cachedFetchLength, settings.fetchLength)
                || !Mathf.Approximately(_cachedJonswapGamma, settings.jonswapGamma)
                || _cachedCascadeCount   != settings.cascadeCount
                || !Mathf.Approximately(_cachedCascadeScale, settings.cascadeScale);
        }

        private void FFTCacheSpectrumParams()
        {
            _fftCachedRes          = settings.ResolutionInt;
            _cachedSeed            = settings.seed;
            _cachedWindSpeed       = GetEffectiveWindSpeed();
            _cachedWindAngle       = GetEffectiveWindAngle();
            _cachedAmplitude       = settings.amplitude;
            _cachedPatchSize       = settings.patchSize;
            _cachedSmallWaveCutoff = settings.smallWaveCutoff;
            _cachedSpectrumType    = settings.spectrumType;
            _cachedFetchLength     = settings.fetchLength;
            _cachedJonswapGamma    = settings.jonswapGamma;
            _cachedCascadeCount    = settings.cascadeCount;
            _cachedCascadeScale    = settings.cascadeScale;
        }

        private float GetEffectiveWindAngle()
        {
            if (windZone == null)
                return settings.windAngleOffset;

            Vector3 fwd = windZone.transform.forward;
            float wzAngle = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            return wzAngle + settings.windAngleOffset;
        }

        private float GetEffectiveWindSpeed()
        {
            float baseSpeed, turbulence, pulseMag, pulseFreq;

            if (windZone == null)
            {
                baseSpeed  = settings.windSpeedFactor;
                turbulence = settings.windTurbulenceFactor;
                pulseMag   = settings.windPulseMagnitudeFactor;
                pulseFreq  = settings.windPulseFrequencyFactor;
            }
            else
            {
                baseSpeed  = windZone.windMain * settings.windSpeedFactor;
                turbulence = windZone.windTurbulence * settings.windTurbulenceFactor;
                pulseMag   = windZone.windPulseMagnitude * settings.windPulseMagnitudeFactor;
                pulseFreq  = windZone.windPulseFrequency * settings.windPulseFrequencyFactor;
            }

            float pulse = pulseMag * Mathf.Sin(Time.time * pulseFreq * Mathf.PI * 2f);
            return Mathf.Max(0.1f, baseSpeed + baseSpeed * turbulence + pulse);
        }

        private Vector2 GetEffectiveWindDirection()
        {
            float angle = GetEffectiveWindAngle();
            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
        }

        // ═══════════════════════════════════════════
        //  Wake Trail
        // ═══════════════════════════════════════════

        private void UpdateWake()
        {
            if (wakeTarget == null || settings.wakeTrailShader == null) return;
            if (_wakePaintKernel < 0) return;

            Vector3 pos = wakeTarget.position;
            float dt = Time.deltaTime;
            if (dt <= 0) dt = 0.016f;

            float speed = 0;
            Vector2 dir2D = Vector2.up;
            if (_wakeHasLastPos)
            {
                Vector3 delta = pos - _wakeLastPos;
                Vector2 delta2D = new Vector2(delta.x, delta.z);
                speed = delta2D.magnitude / dt;
                if (speed > 0.01f)
                    dir2D = delta2D.normalized;
            }
            _wakeLastPos = pos;
            _wakeHasLastPos = true;

            int res = (int)settings.wakeResolution;
            int groups = Mathf.CeilToInt(res / 8f);

            WakeRecreateIfResChanged(res);

            // 1. Fade
            WakeDispatchFade(groups, res);

            // 2. Paint if moving and not too deep
            float depthBelow = settings.waterLevel - pos.y;
            float depthFade = 1f - Mathf.Clamp01(depthBelow / settings.wakeFadeDepth);

            if (speed > settings.wakeMinSpeed && depthFade > 0.01f)
            {
                float speedFactor = Mathf.Clamp01((speed - settings.wakeMinSpeed) / (settings.wakeFullSpeed - settings.wakeMinSpeed));
                speedFactor *= depthFade;
                WakeDispatchPaint(pos, dir2D, speedFactor, groups, res);
            }

            // 3. Blur
            for (int i = 0; i < settings.wakeBlurPasses; i++)
                WakeDispatchBlur(groups, res);

            // Push globals
            Shader.SetGlobalTexture("_OceanWakeMap", _wakeRT);
            Shader.SetGlobalFloat("_OceanWakeCoverageSize", settings.wakeCoverageSize);
            Shader.SetGlobalFloat("_OceanWakeDisplacementScale", settings.wakeDisplacementScale);
            Shader.SetGlobalVector("_OceanWakeCenter", new Vector4(pos.x, pos.z, 0, 0));
            Shader.SetGlobalFloat("_OceanWakeTexelSize", 1f / res);

            WakeUpdateDebugQuad();
        }

        private void WakeRecreateIfResChanged(int desiredRes)
        {
            if (_wakeRT != null && _wakeCachedRes == desiredRes) return;
            WakeReleaseResources();
            WakeCreateResources();
        }

        private void WakeCreateResources()
        {
            if (settings.wakeTrailShader == null) return;

            int res = settings != null ? (int)settings.wakeResolution : 256;

            _wakeRT     = CreateRT(res, RenderTextureFormat.RFloat, "OceanWakeRT");
            _wakeTempRT = CreateRT(res, RenderTextureFormat.RFloat, "OceanWakeTemp");

            ClearRT(_wakeRT);
            ClearRT(_wakeTempRT);

            _wakePaintKernel = settings.wakeTrailShader.FindKernel("PaintWake");
            _wakeBlurHKernel = settings.wakeTrailShader.FindKernel("BlurH");
            _wakeBlurVKernel = settings.wakeTrailShader.FindKernel("BlurV");
            _wakeFadeKernel  = settings.wakeTrailShader.FindKernel("Fade");

            _wakeCachedRes = res;
            _wakeHasLastPos = false;

            if (settings != null && settings.verboseLogs)
                Debug.Log($"[OceanSystem] Wake trail ready: {res}×{res}", this);
        }

        private void WakeReleaseResources()
        {
            ReleaseRT(ref _wakeRT);
            ReleaseRT(ref _wakeTempRT);
            SafeDestroy(ref _wakeDebugMaterial);
        }

        private void WakeDispatchPaint(Vector3 worldPos, Vector2 moveDir, float speedFactor, int groups, int res)
        {
            float cov   = settings.wakeCoverageSize;
            float stamp = settings.wakeStampRadius;

            var cs = settings.wakeTrailShader;
            cs.SetTexture(_wakePaintKernel, ID_WakeRT, _wakeRT);
            cs.SetInt(ID_WakeResolution, res);
            cs.SetVector(ID_BoatUV, new Vector4(Frac(worldPos.x / cov), Frac(worldPos.z / cov), 0, 0));
            cs.SetVector(ID_BoatDir, new Vector4(moveDir.x, moveDir.y, 0, 0));
            cs.SetFloat(ID_BoatSpeed, speedFactor);
            cs.SetFloat(ID_StampRadius, stamp / cov);
            cs.SetFloat(ID_WakeIntensity, settings.wakeIntensity);
            cs.SetFloat(ID_SplashWidth, settings.wakeSplashWidth / stamp);

            cs.Dispatch(_wakePaintKernel, groups, groups, 1);
        }

        private void WakeDispatchBlur(int groups, int res)
        {
            if (settings.wakeBlurStrength <= 0.01f) return;

            var cs = settings.wakeTrailShader;
            cs.SetInt(ID_WakeResolution, res);
            cs.SetFloat(ID_BlurStrength, settings.wakeBlurStrength);

            cs.SetTexture(_wakeBlurHKernel, ID_WakeInput, _wakeRT);
            cs.SetTexture(_wakeBlurHKernel, ID_WakeOutput, _wakeTempRT);
            cs.Dispatch(_wakeBlurHKernel, groups, groups, 1);

            cs.SetTexture(_wakeBlurVKernel, ID_WakeInput, _wakeTempRT);
            cs.SetTexture(_wakeBlurVKernel, ID_WakeOutput, _wakeRT);
            cs.Dispatch(_wakeBlurVKernel, groups, groups, 1);
        }

        private void WakeDispatchFade(int groups, int res)
        {
            var cs = settings.wakeTrailShader;
            cs.SetTexture(_wakeFadeKernel, ID_WakeRT, _wakeRT);
            cs.SetInt(ID_WakeResolution, res);
            cs.SetFloat(ID_FadeRate, settings.wakeFadeRate);
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) dt = 1f / 60f;
            cs.SetFloat("_DeltaTime", dt);

            cs.Dispatch(_wakeFadeKernel, groups, groups, 1);
        }

        // ═══════════════════════════════════════════
        //  Shore Intersection Map
        // ═══════════════════════════════════════════

        private void UpdateShoreIntersection()
        {
            Shader.SetGlobalFloat("_WaveShoreAttenuationDist", settings.waveShoreAttenuationDist);
            Shader.SetGlobalFloat("_WaveShoreMinAmplitude", settings.waveShoreMinAmplitude);

            if (!_fftInitialized) return;
            if (!settings.enableShoreIntersectionMap || settings.shoreIntersectionShader == null)
            {
                Shader.SetGlobalFloat("_OceanShoreMapSize", 0f);
                return;
            }

            if (_shoreIntersectionKernel < 0)
            {
                _shoreIntersectionKernel = SafeFindKernel(settings.shoreIntersectionShader, "ShoreIntersection");
                if (_shoreIntersectionKernel < 0) return;
            }

            if (_cachedTerrain == null)
                _cachedTerrain = Terrain.activeTerrain;
            if (_cachedTerrain == null)
            {
                Shader.SetGlobalFloat("_OceanShoreMapSize", 0f);
                return;
            }

            TerrainData td = _cachedTerrain.terrainData;
            if (td == null) return;

            RenderTexture heightmap = td.heightmapTexture;
            if (heightmap == null) return;

            Vector3 tOrigin = _cachedTerrain.transform.position;
            Vector3 tSize = td.size;
            float mapSize = Mathf.Max(tSize.x, tSize.z);
            float centerX = tOrigin.x + tSize.x * 0.5f;
            float centerZ = tOrigin.z + tSize.z * 0.5f;

            int res = settings.shoreMapResolution;

            ShoreCreateRTIfNeeded();
            if (_shoreIntersectionRT == null) return;

            var cs = settings.shoreIntersectionShader;
            int k = _shoreIntersectionKernel;

            cs.SetTexture(k, "_TerrainHeightmap", heightmap);
            cs.SetVector("_TerrainOrigin", new Vector4(tOrigin.x, tOrigin.y, tOrigin.z, 0));
            cs.SetVector("_TerrainSize", new Vector4(tSize.x, tSize.y, tSize.z, 0));
            cs.SetFloat("_TerrainHeightScale", td.heightmapScale.y);

            cs.SetTexture(k, "_DispY", _displacementYRTs[0]);
            cs.SetTexture(k, "_DispX", _displacementXRTs[0]);
            cs.SetTexture(k, "_DispZ", _displacementZRTs[0]);
            cs.SetTexture(k, "_FFTFoam", _foamPingIsAs[0] ? _foamRT_Bs[0] : _foamRT_As[0]);

            cs.SetTexture(k, "_DispY1", settings.cascadeCount >= 2 ? _displacementYRTs[1] : _displacementYRTs[0]);
            cs.SetTexture(k, "_DispX1", settings.cascadeCount >= 2 ? _displacementXRTs[1] : _displacementXRTs[0]);
            cs.SetTexture(k, "_DispZ1", settings.cascadeCount >= 2 ? _displacementZRTs[1] : _displacementZRTs[0]);
            cs.SetTexture(k, "_FFTFoam1", settings.cascadeCount >= 2
                ? (_foamPingIsAs[1] ? _foamRT_Bs[1] : _foamRT_As[1])
                : (_foamPingIsAs[0] ? _foamRT_Bs[0] : _foamRT_As[0]));

            cs.SetTexture(k, "_DispY2", settings.cascadeCount >= 3 ? _displacementYRTs[2] : _displacementYRTs[0]);
            cs.SetTexture(k, "_DispX2", settings.cascadeCount >= 3 ? _displacementXRTs[2] : _displacementXRTs[0]);
            cs.SetTexture(k, "_DispZ2", settings.cascadeCount >= 3 ? _displacementZRTs[2] : _displacementZRTs[0]);
            cs.SetTexture(k, "_FFTFoam2", settings.cascadeCount >= 3
                ? (_foamPingIsAs[2] ? _foamRT_Bs[2] : _foamRT_As[2])
                : (_foamPingIsAs[0] ? _foamRT_Bs[0] : _foamRT_As[0]));

            cs.SetFloat("_PatchSize", settings.CascadePatchSize(0));
            cs.SetFloat("_PatchSize1", settings.cascadeCount >= 2 ? settings.CascadePatchSize(1) : settings.CascadePatchSize(0));
            cs.SetFloat("_PatchSize2", settings.cascadeCount >= 3 ? settings.CascadePatchSize(2) : settings.CascadePatchSize(0));
            cs.SetInt("_CascadeCount", settings.cascadeCount);
            cs.SetFloat("_WaterLevel", settings.waterLevel);
            cs.SetFloat("_HeightScale", settings.heightScale);

            cs.SetVector("_MapCenter", new Vector4(centerX, centerZ, 0, 0));
            cs.SetFloat("_MapSize", mapSize);
            cs.SetInt("_MapResolution", res);
            cs.SetTexture(k, "_ShoreMapOutput", _shoreIntersectionRT);

            int groups = Mathf.CeilToInt(res / 8f);
            cs.Dispatch(k, groups, groups, 1);

            Shader.SetGlobalTexture("_OceanShoreMap", _shoreIntersectionRT);
            Shader.SetGlobalFloat("_OceanShoreMapSize", mapSize);
            Shader.SetGlobalVector("_OceanShoreMapCenter", new Vector4(centerX, centerZ, 0, 0));
        }

        private void ShoreCreateRTIfNeeded()
        {
            int res = settings.shoreMapResolution;
            if (_shoreIntersectionRT != null && _shoreCachedRes == res) return;

            ReleaseRT(ref _shoreIntersectionRT);

            _shoreIntersectionRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name              = "OceanShoreMap",
                enableRandomWrite = true,
                filterMode        = FilterMode.Bilinear,
                wrapMode          = TextureWrapMode.Clamp,
                useMipMap         = false,
                autoGenerateMips  = false
            };
            _shoreIntersectionRT.Create();
            _shoreCachedRes = res;

            if (settings.verboseLogs)
                Debug.Log($"[OceanSystem] Shore intersection map: {res}×{res}", this);
        }

        private void ShoreReleaseResources()
        {
            ReleaseRT(ref _shoreIntersectionRT);
            _shoreIntersectionKernel = -1;
            _shoreCachedRes = 0;
            _cachedTerrain = null;
        }

        // ═══════════════════════════════════════════
        //  Material Params (from OceanSetup)
        // ═══════════════════════════════════════════

        private void UpdateMaterialParams()
        {
            Material mat = settings.oceanMaterial;
            if (mat == null)
            {
                var rend = GetComponent<MeshRenderer>();
                if (rend != null)
                    mat = rend.sharedMaterial;
            }
            if (mat == null) return;
            _oceanMat = mat;

            PushShadingParams();
            PushTessellationParams();
            PushSunDirection();
        }

        private void PushShadingParams()
        {
            _oceanMat.SetColor(ID_ShallowColor,         settings.shallowColor);
            _oceanMat.SetColor(ID_DeepColor,            settings.deepColor);
            _oceanMat.SetColor(ID_SSSColor,             settings.sssColor);
            _oceanMat.SetColor(ID_FoamColor,            settings.foamColor);
            _oceanMat.SetFloat(ID_FresnelPower,         settings.fresnelPower);
            _oceanMat.SetFloat(ID_SSSIntensity,         settings.sssIntensity);
            _oceanMat.SetFloat(ID_SSSPower,             settings.sssPower);
            _oceanMat.SetFloat(ID_SSSSpread,            settings.sssSpread);
            _oceanMat.SetFloat(ID_SpecularPower,        settings.specularPower);
            _oceanMat.SetFloat(ID_SpecularIntensity,    settings.specularIntensity);
            _oceanMat.SetFloat(ID_HeightScale,          settings.heightScale);
            _oceanMat.SetFloat(ID_AmbientStrength,      settings.ambientStrength);
            _oceanMat.SetFloat(ID_FoamTexScale,         settings.foamTexScale);
            _oceanMat.SetFloat(ID_FoamTexBlend,         settings.foamTexBlend);
            _oceanMat.SetFloat(ID_DepthAbsorption,      settings.depthAbsorption);
            _oceanMat.SetFloat(ID_DepthMaxDistance,      settings.depthMaxDistance);
            _oceanMat.SetColor(ID_AbsorptionColor,      settings.absorptionColor);
            _oceanMat.SetFloat(ID_RefractionStrength,   settings.refractionStrength);
            _oceanMat.SetFloat(ID_RefractionDepthFade,  settings.refractionDepthFade);
            _oceanMat.SetFloat(ID_WrapDiffuse,          settings.wrapDiffuse);
            _oceanMat.SetFloat(ID_OceanRoughness,       settings.oceanRoughness);
            _oceanMat.SetFloat(ID_ReflectionIntensity,  settings.reflectionIntensity);
            _oceanMat.SetColor(ID_HorizonColor,         settings.horizonColor);
            _oceanMat.SetColor(ID_ZenithColor,          settings.zenithColor);
            _oceanMat.SetFloat(ID_UsePlanarReflection,  settings.usePlanarReflection ? 1f : 0f);
            _oceanMat.SetFloat(ID_PlanarReflectionBlend, settings.planarReflectionBlend);
            _oceanMat.SetFloat(ID_ShoreFoamDistance,     settings.shoreFoamDistance);
            _oceanMat.SetFloat(ID_ShoreFoamStrength,     settings.shoreFoamStrength);
            _oceanMat.SetFloat(ID_ShoreFoamFalloff,      settings.shoreFoamFalloff);
        }

        private void PushTessellationParams()
        {
            _oceanMat.SetFloat(ID_TessMaxFactor,   settings.tessMaxFactor);
            _oceanMat.SetFloat(ID_TessMaxDistance,  settings.tessMaxDistance);
        }

        private void PushSunDirection()
        {
            Light sun = sunLight;
            if (sun == null) sun = RenderSettings.sun;
            if (sun == null)
            {
                if (_cachedSunLight == null || !_cachedSunLight.isActiveAndEnabled)
                    _cachedSunLight = FindFirstDirectionalLight();
                sun = _cachedSunLight;
            }
            if (sun == null) return;

            _oceanMat.SetVector(ID_SunDirection, sun.transform.forward);
        }

        private static Light FindFirstDirectionalLight()
        {
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional && l.isActiveAndEnabled)
                    return l;
            }
            return null;
        }

        // ═══════════════════════════════════════════
        //  Planar Reflection
        // ═══════════════════════════════════════════

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (settings == null || !settings.usePlanarReflection) return;
            if (_reflectionCamera == null || _reflectionRT == null) return;
            if (cam == _reflectionCamera) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sceneView = UnityEditor.SceneView.lastActiveSceneView;
                if (sceneView == null || cam != sceneView.camera) return;
            }
            else
            {
                if (cam != Camera.main) return;
            }
#else
            if (cam != Camera.main) return;
#endif

            _reflFrameCounter++;
            if (_reflFrameCounter % settings.reflectionUpdateInterval != 0) return;

            int desiredRes = (int)settings.planarReflectionResolution;
            if (_reflCachedRes != desiredRes)
            {
                ReflectionReleaseResources();
                ReflectionCreateResources();
                if (_reflectionCamera == null) return;
            }

            if (cam.transform.position.y < settings.waterLevel)
                return;

            ReflectionRender(cam);
        }

        private void ReflectionCreateResources()
        {
            if (settings == null) return;

            int res = (int)(settings.usePlanarReflection
                ? settings.planarReflectionResolution
                : PlanarReflectionResolution._512);

            if (_reflectionRT != null && _reflCachedRes == res) return;

            ReflectionReleaseResources();

            _reflectionRT = new RenderTexture(res, res, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            {
                name             = "OceanPlanarReflection",
                filterMode       = FilterMode.Bilinear,
                useMipMap        = false,
                autoGenerateMips = false
            };
            _reflectionRT.Create();

            var camGO = new GameObject("_OceanReflectionCam") { hideFlags = HideFlags.HideAndDontSave };
            _reflectionCamera = camGO.AddComponent<Camera>();
            _reflectionCamera.enabled = false;
            _reflectionCamera.targetTexture = _reflectionRT;

            var hdCamData = camGO.AddComponent<HDAdditionalCameraData>();
            hdCamData.customRenderingSettings = true;

            var mask = hdCamData.renderingPathCustomFrameSettingsOverrideMask;
            mask.mask[(uint)FrameSettingsField.Volumetrics]        = true;
            mask.mask[(uint)FrameSettingsField.TransparentPostpass] = true;
            mask.mask[(uint)FrameSettingsField.AfterPostprocess]   = true;
            mask.mask[(uint)FrameSettingsField.MotionVectors]      = true;
            hdCamData.renderingPathCustomFrameSettingsOverrideMask = mask;

            var fs = hdCamData.renderingPathCustomFrameSettings;
            fs.SetEnabled(FrameSettingsField.Volumetrics, false);
            fs.SetEnabled(FrameSettingsField.TransparentPostpass, false);
            fs.SetEnabled(FrameSettingsField.AfterPostprocess, false);
            fs.SetEnabled(FrameSettingsField.MotionVectors, false);
            hdCamData.renderingPathCustomFrameSettings = fs;

            _reflCachedRes = res;

            if (settings.verboseLogs)
                Debug.Log($"[OceanSystem] Reflection RT: {res}×{res}", this);
        }

        private void ReflectionReleaseResources()
        {
            if (_reflectionCamera != null)
            {
                if (Application.isPlaying) Destroy(_reflectionCamera.gameObject);
                else DestroyImmediate(_reflectionCamera.gameObject);
                _reflectionCamera = null;
            }

            if (_reflectionRT != null)
            {
                _reflectionRT.Release();
                if (Application.isPlaying) Destroy(_reflectionRT);
                else DestroyImmediate(_reflectionRT);
                _reflectionRT = null;
            }
        }

        private void ReflectionRender(Camera sourceCam)
        {
            _reflectionCamera.CopyFrom(sourceCam);
            _reflectionCamera.targetTexture = _reflectionRT;
            _reflectionCamera.cullingMask = settings.reflectionLayers;
            _reflectionCamera.enabled = false;

            Vector3 camPos = sourceCam.transform.position;
            Vector3 mirroredPos = new Vector3(camPos.x, 2f * settings.waterLevel - camPos.y, camPos.z);

            Vector3 camFwd = sourceCam.transform.forward;
            Vector3 camUp  = sourceCam.transform.up;

            _reflectionCamera.transform.position = mirroredPos;
            _reflectionCamera.transform.rotation = Quaternion.LookRotation(
                new Vector3(camFwd.x, -camFwd.y, camFwd.z),
                new Vector3(camUp.x,  -camUp.y,  camUp.z));

            Vector4 clipPlane = CameraSpaceClipPlane(
                _reflectionCamera,
                new Vector3(0, settings.waterLevel + settings.reflectionClipOffset, 0),
                Vector3.up);
            _reflectionCamera.projectionMatrix = sourceCam.CalculateObliqueMatrix(clipPlane);

            GL.invertCulling = true;
            _reflectionCamera.Render();
            GL.invertCulling = false;

            Shader.SetGlobalTexture(ID_PlanarReflectionTex, _reflectionRT);
        }

        private static Vector4 CameraSpaceClipPlane(Camera cam, Vector3 planePos, Vector3 planeNormal)
        {
            Matrix4x4 worldToCam = cam.worldToCameraMatrix;
            Vector3 cPos    = worldToCam.MultiplyPoint(planePos);
            Vector3 cNormal = worldToCam.MultiplyVector(planeNormal).normalized;
            return new Vector4(cNormal.x, cNormal.y, cNormal.z, -Vector3.Dot(cPos, cNormal));
        }

        // ═══════════════════════════════════════════
        //  Debug
        // ═══════════════════════════════════════════

        private void FFTUpdateDebugQuad()
        {
            if (fftDebugQuad == null || settings.debugView == OceanDebugView.Off) return;

            if (_fftDebugMaterial == null)
            {
                var shader = settings.fftDebugShader;
                if (shader == null) shader = Shader.Find("Hidden/OceanDebugSpectrum");
                if (shader == null) shader = Shader.Find("HDRP/Unlit");
                if (shader == null) return;

                _fftDebugMaterial = new Material(shader) { name = "OceanDebug (auto)" };
            }

            int dc = Mathf.Clamp(settings.debugCascade, 0, settings.cascadeCount - 1);

            RenderTexture target = settings.debugView switch
            {
                OceanDebugView.H0Spectrum    => _h0RTs[dc],
                OceanDebugView.DisplacementY => _displacementYRTs[dc],
                OceanDebugView.DisplacementX => _displacementXRTs[dc],
                OceanDebugView.DisplacementZ => _displacementZRTs[dc],
                OceanDebugView.NormalMap     => _normalMapRTs[dc],
                OceanDebugView.Foam          => _foamPingIsAs[dc] ? _foamRT_Bs[dc] : _foamRT_As[dc],
                OceanDebugView.ShoreMap      => _shoreIntersectionRT,
                _ => null
            };
            if (target == null) return;

            _fftDebugMaterial.mainTexture = target;
            _fftDebugMaterial.SetFloat("_Amplify", settings.debugView switch
            {
                OceanDebugView.H0Spectrum => 5000f,
                OceanDebugView.NormalMap  => 1f,
                OceanDebugView.Foam       => 5f,
                OceanDebugView.ShoreMap   => 1f,
                _                         => 50f
            });
            _fftDebugMaterial.SetFloat("_DebugMode", settings.debugView == OceanDebugView.ShoreMap ? 1f : 0f);

            fftDebugQuad.sharedMaterial = _fftDebugMaterial;
        }

        private void WakeUpdateDebugQuad()
        {
            if (wakeDebugQuad == null || _wakeRT == null) return;

            if (_wakeDebugMaterial == null)
            {
                var shader = Shader.Find("Hidden/OceanDebugSpectrum");
                if (shader == null) shader = Shader.Find("HDRP/Unlit");
                if (shader != null)
                    _wakeDebugMaterial = new Material(shader) { name = "WakeDebug (auto)" };
            }

            if (_wakeDebugMaterial != null)
            {
                _wakeDebugMaterial.mainTexture = _wakeRT;
                _wakeDebugMaterial.SetFloat("_Amplify", 5f);
                wakeDebugQuad.sharedMaterial = _wakeDebugMaterial;
            }
        }

        [ContextMenu("Verify Foam RT")]
        public void VerifyFoamRT()
        {
            if (settings == null) return;

            for (int c = 0; c < settings.cascadeCount; c++)
            {
                var foamRT = _foamPingIsAs[c] ? _foamRT_Bs[c] : _foamRT_As[c];
                if (foamRT == null)
                {
                    Debug.LogError($"[OceanSystem] Foam RT cascade {c} is null!", this);
                    continue;
                }

                int cascade = c;
                AsyncGPUReadback.Request(foamRT, 0, TextureFormat.RFloat, (req) =>
                {
                    if (req.hasError) { Debug.LogError($"[OceanSystem] Foam readback failed (cascade {cascade})!", this); return; }

                    var data = req.GetData<float>();
                    float min = float.MaxValue, max = float.MinValue, sum = 0;
                    int nonZero = 0;
                    for (int i = 0; i < data.Length; i++)
                    {
                        float v = data[i];
                        if (v < min) min = v;
                        if (v > max) max = v;
                        sum += v;
                        if (v > 0.001f) nonZero++;
                    }
                    float avg = sum / data.Length;
                    float coveragePct = (float)nonZero / data.Length * 100f;

                    Debug.Log($"[OceanSystem] Foam RT cascade {cascade} ({data.Length} px):\n" +
                              $"  Min: {min:F4}  Max: {max:F4}  Avg: {avg:F4}\n" +
                              $"  Non-zero: {nonZero} ({coveragePct:F1}%)", this);
                });
            }

            if (_normalMapRTs[0] == null) return;
            AsyncGPUReadback.Request(_normalMapRTs[0], 0, TextureFormat.RGBAFloat, (req) =>
            {
                if (req.hasError) { Debug.LogError("[OceanSystem] Normal readback failed!", this); return; }

                var data = req.GetData<Color>();
                float jMin = float.MaxValue, jMax = float.MinValue, jSum = 0;
                int count = Mathf.Min(data.Length, 5000);
                for (int i = 0; i < count; i++)
                {
                    float j = data[i].a;
                    if (j < jMin) jMin = j;
                    if (j > jMax) jMax = j;
                    jSum += j;
                }
                float jAvg = jSum / count;

                Debug.Log($"[OceanSystem] Jacobian (first {count} px):\n" +
                          $"  Min: {jMin:F4}  Max: {jMax:F4}  Avg: {jAvg:F4}\n" +
                          $"  Foam threshold: {settings.foamThreshold:F2}", this);
            });
        }

        [ContextMenu("Verify Wake RT")]
        public void VerifyWakeRT()
        {
            if (_wakeRT == null) { Debug.LogError("[OceanSystem] Wake RT null!", this); return; }

            AsyncGPUReadback.Request(_wakeRT, 0, TextureFormat.RFloat, (req) =>
            {
                if (req.hasError) { Debug.LogError("[OceanSystem] Wake readback failed!", this); return; }
                var data = req.GetData<float>();
                float min = float.MaxValue, max = float.MinValue;
                int nonZero = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] < min) min = data[i];
                    if (data[i] > max) max = data[i];
                    if (Mathf.Abs(data[i]) > 0.001f) nonZero++;
                }
                Debug.Log($"[OceanSystem] Wake RT: Min={min:F4} Max={max:F4} NonZero={nonZero}/{data.Length}\n" +
                    $"  Displacement: [{min * settings.wakeDisplacementScale:F2}m, {max * settings.wakeDisplacementScale:F2}m]", this);
            });
        }

        // ═══════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════

        public RenderTexture DisplacementYRT => _displacementYRTs[0];
        public RenderTexture DisplacementXRT => _displacementXRTs[0];
        public RenderTexture DisplacementZRT => _displacementZRTs[0];
        public RenderTexture NormalMapRT     => _normalMapRTs[0];
        public RenderTexture FoamRT          => _foamPingIsAs[0] ? _foamRT_Bs[0] : _foamRT_As[0];

        public RenderTexture GetNormalMapRT(int cascade)
        {
            if (settings == null || cascade < 0 || cascade >= settings.cascadeCount) return null;
            return _normalMapRTs[cascade];
        }

        public void ForceRefreshSpectrum()
        {
            FFTReleaseAll();
            _fftInitialized = false;
            FFTValidateAndInit();
        }

        // ═══════════════════════════════════════════
        //  Shared helpers
        // ═══════════════════════════════════════════

        private RenderTexture CreateRT(int res, RenderTextureFormat format, string rtName)
        {
            var rt = new RenderTexture(res, res, 0, format, RenderTextureReadWrite.Linear)
            {
                name              = rtName,
                enableRandomWrite = true,
                filterMode        = FilterMode.Bilinear,
                wrapMode          = TextureWrapMode.Repeat,
                useMipMap         = false,
                autoGenerateMips  = false
            };
            rt.Create();
            return rt;
        }

        private static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Destroy(rt);
            else DestroyImmediate(rt);
            rt = null;
        }

        private static void ClearRT(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }

        private void SafeDestroy(ref Material mat)
        {
            if (mat == null) return;
            if (Application.isPlaying) Destroy(mat);
            else DestroyImmediate(mat);
            mat = null;
        }

        private int SafeFindKernel(ComputeShader cs, string kernelName)
        {
            if (cs == null)
            {
                Debug.LogError($"[OceanSystem] ComputeShader null when looking for kernel '{kernelName}'", this);
                return -1;
            }

            try
            {
                int idx = cs.FindKernel(kernelName);
                if (settings != null && settings.verboseLogs)
                    Debug.Log($"[OceanSystem] Kernel '{kernelName}' (index {idx}) in '{cs.name}'", this);
                return idx;
            }
            catch (System.Exception)
            {
                Debug.LogError($"[OceanSystem] Kernel '{kernelName}' NOT found in '{cs.name}'.", this);
                return -1;
            }
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
