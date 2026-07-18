// SandModule.cs
// Feature Sand Mode (keyword _SAND_MODE) : glitter, bourrelets (rim push-up), ripples,
// spéculaire océan, fresnel. Per-layer : quelles couches sont du sable.
// Tick : pousse la direction du soleil (depuis RenderSettings.sun) pour glitter/spéculaire.
// NB : _SandDeformTexelSize est poussé par la déformation (Phase C), PAS ici.
// Porté depuis TerrainLitCustomSetup (bloc Sand + PushSunDirection + BakeGlitterRamp).
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    [TerrainModuleMenu("Surface/Sand Mode")]
    public class SandModule : TerrainFeatureModule
    {
        [Tooltip("Couches (0-7) qui se comportent comme du sable.")]
        public LayerToggleParameter layers = new LayerToggleParameter(new bool[8], true);

        [Header("Glitter")]
        public ClampedFloatParameter glitterIntensity = new ClampedFloatParameter(0.8f, 0f, 2f, true);
        public ClampedFloatParameter glitterThreshold = new ClampedFloatParameter(0.97f, 0.9f, 0.999f, true);
        public ClampedFloatParameter glitterScale = new ClampedFloatParameter(200f, 10f, 1000f, true);
        public ColorParameter glitterColor = new ColorParameter(new Color(1f, 0.95f, 0.8f, 1f), true);
        public MinFloatParameter glitterMaxDistance = new MinFloatParameter(80f, 1f, true);
        [Tooltip("X = distance normalisée, Y = intensité (0-1).")]
        public AnimationCurveParameter glitterFalloff = new AnimationCurveParameter(AnimationCurve.EaseInOut(0f, 1f, 1f, 0f), true);

        [Header("Rim / Ripples / Ocean / Fresnel")]
        public ClampedFloatParameter rimPushUp = new ClampedFloatParameter(0.03f, 0f, 0.2f, true);
        public ClampedFloatParameter rippleScale = new ClampedFloatParameter(3f, 0.1f, 20f, true);
        public ClampedFloatParameter rippleStrength = new ClampedFloatParameter(0.15f, 0f, 0.5f, true);
        public ClampedFloatParameter oceanSpecPower = new ClampedFloatParameter(16f, 2f, 64f, true);
        public ClampedFloatParameter oceanSpecIntensity = new ClampedFloatParameter(0.3f, 0f, 1f, true);
        public ClampedFloatParameter fresnelPower = new ClampedFloatParameter(4f, 1f, 10f, true);
        public ClampedFloatParameter fresnelIntensity = new ClampedFloatParameter(0.3f, 0f, 1f, true);

        public override string Keyword => "_SAND_MODE";
        // Le rim push-up est un déplacement de vertex (ne s'applique qu'avec le displacement actif → pas EnablesVertexDisplacement).
        public override float GetMaxVertexDisplacement() => active ? Mathf.Max(0f, rimPushUp.value) : 0f;

        [System.NonSerialized] Texture2D m_RampTex;
        [System.NonSerialized] int m_RampHash;

        static readonly int ID_GlitterIntensity = Shader.PropertyToID("_SandGlitterIntensity");
        static readonly int ID_GlitterThreshold = Shader.PropertyToID("_SandGlitterThreshold");
        static readonly int ID_GlitterScale = Shader.PropertyToID("_SandGlitterScale");
        static readonly int ID_GlitterColor = Shader.PropertyToID("_SandGlitterColor");
        static readonly int ID_GlitterMaxDistance = Shader.PropertyToID("_SandGlitterMaxDistance");
        static readonly int ID_GlitterFalloffRamp = Shader.PropertyToID("_SandGlitterFalloffRamp");
        static readonly int ID_RimPushUp = Shader.PropertyToID("_SandRimPushUp");
        static readonly int ID_RippleScale = Shader.PropertyToID("_SandRippleScale");
        static readonly int ID_RippleStrength = Shader.PropertyToID("_SandRippleStrength");
        static readonly int ID_OceanSpecPower = Shader.PropertyToID("_SandOceanSpecPower");
        static readonly int ID_OceanSpecIntensity = Shader.PropertyToID("_SandOceanSpecIntensity");
        static readonly int ID_FresnelPower = Shader.PropertyToID("_SandFresnelPower");
        static readonly int ID_FresnelIntensity = Shader.PropertyToID("_SandFresnelIntensity");
        static readonly int ID_SunDirection = Shader.PropertyToID("_SandSunDirection");
        static readonly int[] ID_EnableSand = new int[8];

        static SandModule()
        {
            for (int i = 0; i < 8; i++)
                ID_EnableSand[i] = Shader.PropertyToID($"_EnableSandMode{i}");
        }

        public override void Apply(TerrainApplyContext ctx)
        {
            var m = ctx.material;

            for (int i = 0; i < ctx.layerCount; i++)
            {
                if (layers.overrideState) m.SetFloat(ID_EnableSand[i], layers[i] ? 1f : 0f);
            }

            Push(m, ID_GlitterIntensity, glitterIntensity);
            Push(m, ID_GlitterThreshold, glitterThreshold);
            Push(m, ID_GlitterScale, glitterScale);
            Push(m, ID_GlitterColor, glitterColor, true); // linéaire (comme le monolithe)
            Push(m, ID_GlitterMaxDistance, glitterMaxDistance);
            Push(m, ID_RimPushUp, rimPushUp);
            Push(m, ID_RippleScale, rippleScale);
            Push(m, ID_RippleStrength, rippleStrength);
            Push(m, ID_OceanSpecPower, oceanSpecPower);
            Push(m, ID_OceanSpecIntensity, oceanSpecIntensity);
            Push(m, ID_FresnelPower, fresnelPower);
            Push(m, ID_FresnelIntensity, fresnelIntensity);

            if (glitterFalloff.overrideState)
            {
                TerrainRampBaker.Bake(glitterFalloff.value, ref m_RampTex, ref m_RampHash, "GlitterFalloffRamp");
                if (m_RampTex != null) m.SetTexture(ID_GlitterFalloffRamp, m_RampTex);
            }
        }

        public override void Tick(TerrainApplyContext ctx)
        {
            // Direction VERS le soleil (RenderSettings.sun, sinon 1ère directionnelle active).
            Vector3 sunDir = new Vector3(0.3f, 0.8f, 0.5f).normalized;
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var l in lights)
                    if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
            }
            if (sun != null) sunDir = -sun.transform.forward;
            ctx.material.SetVector(ID_SunDirection, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
        }

        public override void OnModuleDisable(TerrainApplyContext ctx)
        {
            TerrainRampBaker.Release(ref m_RampTex);
            m_RampHash = 0;
        }
    }
}
