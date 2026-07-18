// ParallaxOcclusionModule.cs
// Feature POM (keyword _PARALLAX_OCCLUSION_MAPPING). Global : height scale, min/max steps,
// distance fade. Per-layer : quelles couches reçoivent le POM.
// Porté depuis TerrainLitCustomSetup (bloc POM).
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    [TerrainModuleMenu("Displacement/Parallax Occlusion Mapping")]
    public class ParallaxOcclusionModule : TerrainFeatureModule
    {
        [Tooltip("Échelle de hauteur POM (lue dans le canal bleu du Mask Map).")]
        public ClampedFloatParameter heightScale = new ClampedFloatParameter(0.05f, 0f, 0.2f, true);
        public ClampedIntParameter minSteps = new ClampedIntParameter(4, 4, 64, true);
        public ClampedIntParameter maxSteps = new ClampedIntParameter(32, 8, 128, true);
        [Tooltip("Distance (m) à laquelle le POM s'estompe.")]
        public ClampedFloatParameter distanceFade = new ClampedFloatParameter(50f, 5f, 200f, true);
        [Tooltip("Couches (0-7) qui reçoivent le POM.")]
        public LayerToggleParameter layers = new LayerToggleParameter(new bool[8], true);

        public override string Keyword => "_PARALLAX_OCCLUSION_MAPPING";

        static readonly int ID_DistanceFade = Shader.PropertyToID("_POMDistanceFade");
        static readonly int[] ID_Enable = new int[8];
        static readonly int[] ID_Height = new int[8];
        static readonly int[] ID_Min = new int[8];
        static readonly int[] ID_Max = new int[8];

        static ParallaxOcclusionModule()
        {
            for (int i = 0; i < 8; i++)
            {
                ID_Enable[i] = Shader.PropertyToID($"_EnablePOMLayer{i}");
                ID_Height[i] = Shader.PropertyToID($"_HeightScale{i}");
                ID_Min[i] = Shader.PropertyToID($"_POMMinSteps{i}");
                ID_Max[i] = Shader.PropertyToID($"_POMMaxSteps{i}");
            }
        }

        public override void Apply(TerrainApplyContext ctx)
        {
            var m = ctx.material;
            Push(m, ID_DistanceFade, distanceFade);

            for (int i = 0; i < ctx.layerCount; i++)
            {
                bool on = layers[i];
                if (layers.overrideState) m.SetFloat(ID_Enable[i], on ? 1f : 0f);
                if (heightScale.overrideState) m.SetFloat(ID_Height[i], on ? heightScale.value : 0f);
                if (minSteps.overrideState) m.SetFloat(ID_Min[i], minSteps.value);
                if (maxSteps.overrideState) m.SetFloat(ID_Max[i], maxSteps.value);
            }
        }
    }
}
