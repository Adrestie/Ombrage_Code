using System;
using UnityEngine;
using Ombrage.Tools.Core.Meshing.Strategies;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Core.Meshing
{
    /// <summary>Result of a build: the generated mesh and the FFD lattice box used.</summary>
    public readonly struct RockBuildResult
    {
        public readonly MeshData Mesh;

        /// <summary>
        /// The free-form deformation lattice box (the base mesh's padded bounds). Stable
        /// across FFD control-point edits, so editor tooling can place lattice handles.
        /// </summary>
        public readonly Bounds FfdBox;

        public RockBuildResult(MeshData mesh, Bounds ffdBox)
        {
            Mesh = mesh;
            FfdBox = ffdBox;
        }
    }

    /// <summary>
    /// Single entry point for producing a mesh from settings. The generation mode is the
    /// primary switch: <see cref="GenerationMode.Rock"/> resolves one of the three rock
    /// algorithms; <see cref="GenerationMode.Cliff"/> runs the dedicated cliff generator
    /// (ignoring <see cref="RockGenerationSettings.algorithm"/>). Free-form deformation is
    /// then applied to either. Used by both the live preview and the export so they never
    /// diverge.
    /// </summary>
    public static class RockGenerator
    {
        public static RockBuildResult Build(RockGenerationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            MeshData mesh = settings.mode == GenerationMode.Cliff
                ? new CliffStrategy().Generate(settings)
                : ResolveStrategy(settings.algorithm).Generate(settings);

            // The box is computed from the base mesh, before deformation, so it stays
            // stable while the user drags FFD control points.
            Bounds ffdBox = FfdDeformer.ComputeBox(mesh, settings.ffd);
            FfdDeformer.Deform(mesh, settings.ffd, ffdBox);

            return new RockBuildResult(mesh, ffdBox);
        }

        /// <summary>Returns the rock strategy implementing the requested algorithm.</summary>
        public static IMeshGenerationStrategy ResolveStrategy(GenerationAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case GenerationAlgorithm.PrimitiveNoise:
                    return new PrimitiveNoiseStrategy();
                case GenerationAlgorithm.ConvexHull:
                    return new ConvexHullStrategy();
                case GenerationAlgorithm.MarchingCubes:
                    return new MarchingCubesStrategy();
                default:
                    throw new NotSupportedException("Unknown algorithm: " + algorithm);
            }
        }
    }
}
