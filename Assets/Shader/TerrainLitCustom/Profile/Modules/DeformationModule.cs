// DeformationModule.cs
// Feature Déformation runtime (empreintes véhicule). Le TUNING vit ici (SO, visible dans l'UI Volume) ;
// les refs de scène (véhicule/roues/shaders/groundLayer) sont sur le contrôleur, et l'état runtime
// (RenderTextures) dans un DeformationRuntime détenu par le contrôleur.
// Pousse au matériau : _DeformationMap, _TessellationMask, _BufferWorldSize, _SandDeformTexelSize,
// _DeformationStrength. (Le _SandDeformTexelSize alimente le rim push-up du module Sand.)
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.TerrainFeatures
{
    [TerrainModuleMenu("Runtime/Deformation (footprints)")]
    public class DeformationModule : TerrainFeatureModule
    {
        [Tooltip("Force avec laquelle la carte de déformation pousse la géométrie vers le bas.")]
        public ClampedFloatParameter deformationStrength = new ClampedFloatParameter(1f, 0f, 2f, true);

        [Header("Toroidal Buffer")]
        [Tooltip("Taille world-space d'une tuile de buffer (m).")]
        [Range(10f, 200f)] public float bufferWorldSize = 40f;
        [Tooltip("Résolution de la texture de déformation.")]
        public int resolution = 1024;

        [Header("Stamp — Upright")]
        [Min(0.001f)] public float stepDistance = 0.5f;
        [Min(0.1f)] public float maxStepDistance = 2f;
        [Range(5f, 45f)] public float curveAngleThreshold = 18f;
        [Range(0.01f, 1f)] public float wheelStampIntensity = 0.15f;
        [Range(0.05f, 2f)] public float wheelStampRadiusMeters = 0.15f;

        [Header("Ground Contact")]
        [Range(0.01f, 1f)] public float wheelContactDistance = 0.3f;
        [Range(0f, 2f)] public float raycastOriginOffset = 0.5f;

        [Header("Stamp — Flipped")]
        [Range(0.01f, 1f)] public float flipStampIntensity = 0.4f;
        [Range(0.5f, 5f)] public float flipStampRadiusMeters = 2f;
        [Range(0.1f, 3f)] public float flipStepDistance = 0.5f;
        [Range(-1f, 0.5f)] public float flipThreshold = 0.1f;

        [Header("Fade & Smoothing")]
        [Range(0f, 0.1f)] public float fadeSpeed = 0f;
        [Range(0f, 1f)] public float diffusionStrength = 0.4f;
        [Range(0, 10)] public int diffusionIterations = 3;

        [Header("Tessellation Mask")]
        public int maskResolution = 512;
        [Range(1f, 5f)] public float tessellationMaskPadding = 2.5f;

        [Header("Shaders (assets)")]
        [Tooltip("Hidden/TerrainStamp")]
        public Shader stampShader;
        [Tooltip("Hidden/TerrainDeformFade")]
        public Shader fadeShader;

        [Header("Debug")]
        public bool showDebugGizmos = false;

        public float BufferWorldSize => bufferWorldSize;

        static readonly int ID_DeformationMap = Shader.PropertyToID("_DeformationMap");
        static readonly int ID_TessellationMask = Shader.PropertyToID("_TessellationMask");
        static readonly int ID_BufferWorldSize = Shader.PropertyToID("_BufferWorldSize");
        static readonly int ID_SandDeformTexelSize = Shader.PropertyToID("_SandDeformTexelSize");
        static readonly int ID_DeformationStrength = Shader.PropertyToID("_DeformationStrength");

        public override void OnModuleEnable(TerrainApplyContext ctx)
        {
            var rt = new DeformationRuntime();
            rt.Initialize(this, ctx.controller);
            ctx.SetRuntime(this, rt);
        }

        public override void OnModuleDisable(TerrainApplyContext ctx)
        {
            (ctx.GetRuntime(this) as DeformationRuntime)?.Dispose();
        }

        public override void Apply(TerrainApplyContext ctx)
        {
            var m = ctx.material;
            if (deformationStrength.overrideState) m.SetFloat(ID_DeformationStrength, deformationStrength.value);

            var rt = ctx.GetRuntime(this) as DeformationRuntime;
            if (rt != null)
            {
                rt.EnsureRTs(this, ctx.controller);
                if (rt.DeformationRT != null)
                {
                    m.SetTexture(ID_DeformationMap, rt.DeformationRT);
                    // Also expose it as a GLOBAL so OTHER systems (the GPU grass) can read the same RT —
                    // they have no terrain material to receive the per-material binding above.
                    Shader.SetGlobalTexture(ID_DeformationMap, rt.DeformationRT);
                }
                if (rt.TessellationMaskRT != null) m.SetTexture(ID_TessellationMask, rt.TessellationMaskRT);
            }
            m.SetFloat(ID_BufferWorldSize, bufferWorldSize);
            Shader.SetGlobalFloat(ID_BufferWorldSize, bufferWorldSize); // global twin for the grass
            m.SetFloat(ID_SandDeformTexelSize, 1f / Mathf.Max(1, resolution));
        }

        public override void Tick(TerrainApplyContext ctx)
        {
            (ctx.GetRuntime(this) as DeformationRuntime)?.Tick(this, ctx.controller, ctx.material);
        }

        public override void DrawGizmos(TerrainApplyContext ctx)
        {
#if UNITY_EDITOR
            if (!showDebugGizmos || ctx.controller == null || ctx.controller.vehicleBody == null) return;
            var vehicleBody = ctx.controller.vehicleBody;

            float groundY = (ctx.terrains != null && ctx.terrains.Length > 0 && ctx.terrains[0] != null)
                ? ctx.terrains[0].transform.position.y + 0.2f
                : vehicleBody.position.y;

            float tileX = Mathf.Floor(vehicleBody.position.x / bufferWorldSize) * bufferWorldSize + bufferWorldSize * 0.5f;
            float tileZ = Mathf.Floor(vehicleBody.position.z / bufferWorldSize) * bufferWorldSize + bufferWorldSize * 0.5f;
            Vector3 tileCenter = new Vector3(tileX, groundY, tileZ);

            Gizmos.color = new Color(0.06f, 0.43f, 0.34f, 0.25f);
            Gizmos.DrawWireCube(tileCenter, new Vector3(bufferWorldSize, 0.1f, bufferWorldSize));
            Gizmos.color = new Color(0.06f, 0.43f, 0.34f, 0.08f);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    Gizmos.DrawWireCube(tileCenter + new Vector3(dx * bufferWorldSize, 0, dz * bufferWorldSize),
                        new Vector3(bufferWorldSize, 0.1f, bufferWorldSize));
                }

            bool flipped = Vector3.Dot(vehicleBody.up, Vector3.up) < flipThreshold;
            if (flipped)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(vehicleBody.position, flipStampRadiusMeters);
            }
            else
            {
                Gizmos.color = Color.cyan;
                var wheels = ctx.controller.wheels;
                if (wheels != null)
                    foreach (var w in wheels)
                        if (w != null) Gizmos.DrawWireSphere(w.position, wheelStampRadiusMeters);
            }

            float texelSize = bufferWorldSize / Mathf.Max(1, resolution);
            Handles.Label(tileCenter + Vector3.up * 2f, $"Tile: {bufferWorldSize}m  Texel: {texelSize * 100:F1}cm  Res: {resolution}²");
#endif
        }
    }
}
