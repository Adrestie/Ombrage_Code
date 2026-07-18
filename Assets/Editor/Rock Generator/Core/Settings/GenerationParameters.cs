using System;
using UnityEngine;

namespace Ombrage.Tools.Core.Settings
{
    /// <summary>Fractal noise configuration shared by every generation algorithm.</summary>
    [Serializable]
    public sealed class NoiseSettings
    {
        [Tooltip("Underlying noise function.")]
        public NoiseType type = NoiseType.Perlin;

        [Min(0.0001f), Tooltip("Frequency of the first octave.")]
        public float frequency = 1.5f;

        [Range(1, 8), Tooltip("Number of fractal octaves stacked on top of each other.")]
        public int octaves = 4;

        [Range(0f, 1f), Tooltip("Amplitude multiplier applied at each successive octave.")]
        public float persistence = 0.5f;

        [Min(1f), Tooltip("Frequency multiplier applied at each successive octave.")]
        public float lacunarity = 2f;
    }

    /// <summary>Parameters for the PrimitiveNoise generation strategy (Rock mode).</summary>
    [Serializable]
    public sealed class PrimitiveNoiseParameters
    {
        [Tooltip("Base primitive that gets displaced by noise.")]
        public BasePrimitive primitive = BasePrimitive.Icosphere;

        [Range(0, 5), Tooltip("Icosphere subdivision count. Higher means a denser mesh.")]
        public int subdivisions = 3;

        [Tooltip("Box subdivision count per axis.")]
        public Vector3Int boxResolution = new Vector3Int(10, 10, 10);

        [Min(0.0001f), Tooltip("Radius of the base icosphere.")]
        public float radius = 1f;

        [Tooltip("Dimensions of the base box.")]
        public Vector3 boxSize = Vector3.one;

        [Min(0f), Tooltip("Maximum displacement applied along the surface normal.")]
        public float displacementStrength = 0.35f;

        public NoiseSettings noise = new NoiseSettings();
    }

    /// <summary>Parameters for the ConvexHull strategy (Rock mode): a faceted boulder.</summary>
    [Serializable]
    public sealed class ConvexHullParameters
    {
        [Range(4, 512), Tooltip("Number of seed points used to build the hull. More points = less faceted.")]
        public int pointCount = 28;

        [Min(0.0001f), Tooltip("Radius of the point-cloud sphere.")]
        public float radius = 1f;

        [Min(0f), Tooltip("Noise displacement applied to the hull corners.")]
        public float displacementStrength = 0.15f;

        public NoiseSettings noise = new NoiseSettings();
    }

    /// <summary>Parameters for the MarchingCubes strategy (Rock mode): a voxel-extracted blob.</summary>
    [Serializable]
    public sealed class MarchingCubesParameters
    {
        [Tooltip("Voxel grid resolution per axis (number of cells).")]
        public Vector3Int gridResolution = new Vector3Int(24, 24, 24);

        [Tooltip("World-space bounds of the voxel grid.")]
        public Vector3 bounds = Vector3.one * 2f;

        [Range(0f, 1f), Tooltip("Density threshold of the extracted iso-surface.")]
        public float isoLevel = 0.5f;

        [Range(0f, 1.5f), Tooltip("Amount of noise added to the base density field (surface lumpiness).")]
        public float noiseAmplitude = 0.55f;

        public NoiseSettings noise = new NoiseSettings();
    }

    /// <summary>
    /// Parameters for the dedicated Cliff generator. Produces a watertight cliff block
    /// (base at y = 0) meant to be placed against Unity Terrain: layered strata, vertical
    /// fracturing, overhangs and an irregular crest.
    /// </summary>
    [Serializable]
    public sealed class CliffParameters
    {
        [Tooltip("Overall cliff block dimensions: width, height, depth.")]
        public Vector3 size = new Vector3(4f, 6f, 2.5f);

        [Tooltip("Front-face subdivisions: X across the width, Y up the height.")]
        public Vector2Int faceResolution = new Vector2Int(56, 72);

        [Header("Strata")]
        [Min(0.05f), Tooltip("Height of each rock layer.")]
        public float stratumHeight = 0.7f;

        [Range(0f, 1f), Tooltip("How far strata advance or recede (the terraced look).")]
        public float stratumStrength = 0.55f;

        [Range(0f, 1f), Tooltip("Random variation between successive strata.")]
        public float stratumJitter = 0.6f;

        [Range(0f, 1f), Tooltip("How much upper strata jut out over lower ones (overhangs).")]
        public float overhangBias = 0.4f;

        [Range(-0.6f, 0.6f), Tooltip("Overall lean of the cliff face (negative leans back).")]
        public float faceLean = 0.1f;

        [Header("Fracturing & Shape")]
        [Range(0f, 1f), Tooltip("Strength of vertical cracks / columnar jointing.")]
        public float fractureStrength = 0.45f;

        [Min(0.1f), Tooltip("Horizontal scale of the fracturing (higher = narrower columns).")]
        public float fractureScale = 2.5f;

        [Range(0f, 1f), Tooltip("Fine surface roughness.")]
        public float detailStrength = 0.3f;

        [Tooltip("Low-frequency noise that warps the overall face shape.")]
        public NoiseSettings macroNoise = new NoiseSettings { frequency = 0.6f, octaves = 3 };

        [Header("Edges")]
        [Range(0f, 1f), Tooltip("Jaggedness of the top crest.")]
        public float crestRoughness = 0.5f;

        [Range(0f, 1f), Tooltip("How much the base is eroded back (talus feel).")]
        public float erosionStrength = 0.3f;
    }

    /// <summary>
    /// Free-form deformation lattice applied to the generated mesh to reshape its overall
    /// form, independently of the chosen generator.
    /// </summary>
    [Serializable]
    public sealed class FfdParameters
    {
        [Tooltip("Enables the free-form deformation lattice.")]
        public bool enabled = false;

        [Tooltip("Number of lattice control points per axis. Clamped to a minimum of 2.")]
        public Vector3Int resolution = new Vector3Int(2, 2, 2);

        [Tooltip("Per-control-point offsets from the rest lattice, in mesh-local units. "
                 + "Flattened as index = x + y*resX + z*resX*resY. All-zero means no deformation.")]
        public Vector3[] controlPointOffsets = Array.Empty<Vector3>();

        [Range(0f, 0.5f), Tooltip("Extra margin around the mesh bounds when fitting the lattice box.")]
        public float boundsPadding = 0.05f;

        /// <summary>Resolution clamped to the valid range (minimum 2 per axis).</summary>
        public Vector3Int ClampedResolution => new Vector3Int(
            Mathf.Max(2, resolution.x),
            Mathf.Max(2, resolution.y),
            Mathf.Max(2, resolution.z));

        /// <summary>Number of control points implied by <see cref="ClampedResolution"/>.</summary>
        public int ControlPointCount
        {
            get
            {
                Vector3Int r = ClampedResolution;
                return r.x * r.y * r.z;
            }
        }

        /// <summary>
        /// True when the offsets array is consistent with the resolution and actually
        /// describes a non-identity deformation.
        /// </summary>
        public bool HasDeformation
        {
            get
            {
                if (!enabled || controlPointOffsets == null
                    || controlPointOffsets.Length != ControlPointCount)
                    return false;
                for (int i = 0; i < controlPointOffsets.Length; i++)
                    if (controlPointOffsets[i].sqrMagnitude > 1e-10f)
                        return true;
                return false;
            }
        }
    }
}
