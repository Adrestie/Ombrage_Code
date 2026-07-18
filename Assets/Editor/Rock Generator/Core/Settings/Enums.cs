namespace Ombrage.Tools.Core.Settings
{
    /// <summary>High-level shape family the generator targets.</summary>
    public enum GenerationMode
    {
        Rock = 0,
        Cliff = 1
    }

    /// <summary>Mesh generation algorithm. One <see cref="Meshing.IMeshGenerationStrategy"/> per value.</summary>
    public enum GenerationAlgorithm
    {
        PrimitiveNoise = 0,
        ConvexHull = 1,
        MarchingCubes = 2
    }

    /// <summary>Underlying noise function used to drive surface displacement.</summary>
    public enum NoiseType
    {
        Perlin = 0,
        Voronoi = 1
    }

    /// <summary>Base primitive the PrimitiveNoise strategy displaces.</summary>
    public enum BasePrimitive
    {
        Icosphere = 0,
        Box = 1
    }
}
