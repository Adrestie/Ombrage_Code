// TessellationModule.cs
// Feature Tessellation/Displacement (keyword _TESSELLATION_DISPLACEMENT).
// Global : facteur de tessellation, courbe de falloff (bakée en rampe), élévation par couche.
// Per-layer : quelles couches sont déplacées. Contribue aux patch bounds + active le displacement.
// Porté depuis TerrainLitCustomSetup (bloc Tessellation + BakeTessellationRamp).
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    [TerrainModuleMenu("Displacement/Tessellation Displacement")]
    public class TessellationModule : TerrainFeatureModule
    {
        public ClampedFloatParameter factor = new ClampedFloatParameter(15f, 1f, 64f, true);
        [Tooltip("X = distance normalisée (0=caméra, 1=maxDist), Y = multiplicateur de tessellation (0-1).")]
        public AnimationCurveParameter falloff = new AnimationCurveParameter(AnimationCurve.EaseInOut(0f, 1f, 1f, 0f), true);
        [Tooltip("Élévation par couche déplacée (m). Sans limite haute.")]
        public MinFloatParameter displacementScale = new MinFloatParameter(0.1f, 0f, true);
        [Tooltip("Couches (0-7) déplacées.")]
        public LayerToggleParameter layers = new LayerToggleParameter(new bool[8], true);

        public override string Keyword => "_TESSELLATION_DISPLACEMENT";
        public override bool EnablesVertexDisplacement => true;
        public override float GetMaxVertexDisplacement() => active ? Mathf.Max(0f, displacementScale.value) : 0f;

        [System.NonSerialized] Texture2D m_RampTex;
        [System.NonSerialized] int m_RampHash;

        static readonly int ID_Factor = Shader.PropertyToID("_TessellationFactor");
        static readonly int ID_MaxDisplacement = Shader.PropertyToID("_TessellationMaxDisplacement");
        static readonly int ID_FalloffRamp = Shader.PropertyToID("_TessellationFalloffRamp");
        static readonly int[] ID_EnableLayer = new int[8];
        static readonly int[] ID_Scale = new int[8];

        static TessellationModule()
        {
            for (int i = 0; i < 8; i++)
            {
                ID_EnableLayer[i] = Shader.PropertyToID($"_EnableDisplacementLayer{i}");
                ID_Scale[i] = Shader.PropertyToID($"_DisplacementScale{i}");
            }
        }

        public override void Apply(TerrainApplyContext ctx)
        {
            var m = ctx.material;
            Push(m, ID_Factor, factor);

            if (displacementScale.overrideState)
                m.SetFloat(ID_MaxDisplacement, displacementScale.value);

            if (falloff.overrideState)
            {
                TerrainRampBaker.Bake(falloff.value, ref m_RampTex, ref m_RampHash, "TessFalloffRamp");
                if (m_RampTex != null) m.SetTexture(ID_FalloffRamp, m_RampTex);
            }

            for (int i = 0; i < ctx.layerCount; i++)
            {
                bool on = layers[i];
                if (layers.overrideState) m.SetFloat(ID_EnableLayer[i], on ? 1f : 0f);
                if (displacementScale.overrideState) m.SetFloat(ID_Scale[i], on ? displacementScale.value : 0f);
            }
        }

        public override void OnModuleDisable(TerrainApplyContext ctx)
        {
            TerrainRampBaker.Release(ref m_RampTex);
            m_RampHash = 0;
        }
    }
}
