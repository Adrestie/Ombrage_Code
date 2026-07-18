// TerrainLitCustomSurfaceData.hlsl
// Extended surface data to carry blended height for displacement
// and sand-mode outputs (glitter weight, emission).
// ---------------------------------------------------------------

struct TerrainLitSurfaceData
{
    float3 albedo;
    float3 normalData;
    float  smoothness;
    float  metallic;
    float  ao;
    float  height;            // blended height for tessellation displacement
    float  sandWeight;        // accumulated blend weight of sand-enabled layers [0-1]
    float  displacementWeight; // accumulated blend weight of displacement-enabled layers [0-1]
    float  grassTintWeight;   // accumulated blend weight of grass-tint-enabled layers [0-1] (L2)
};

void InitializeTerrainLitSurfaceData(out TerrainLitSurfaceData surfaceData)
{
    surfaceData.albedo     = 0;
    surfaceData.normalData = 0;
    surfaceData.smoothness = 0;
    surfaceData.metallic   = 0;
    surfaceData.ao         = 1;
    surfaceData.height     = 0;
    surfaceData.sandWeight = 0;
    surfaceData.displacementWeight = 0;
    surfaceData.grassTintWeight = 0;
}
