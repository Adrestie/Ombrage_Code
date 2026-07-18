using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Job Burst calculant l'UV projetée de chaque COIN de triangle.
    ///
    /// Le calcul est fait par coin (et non par vertex) pour deux raisons :
    ///  - les projections box / triplanaire choisissent un plan PAR FACE : deux
    ///    faces partageant un vertex peuvent lui attribuer des UV différentes ;
    ///  - les coins appartenant à des faces non masquées conservent l'UV
    ///    existante du vertex.
    /// Le dédoublement des vertices aux coutures est effectué ensuite, en C#
    /// (voir <see cref="UVProjection"/>).
    /// </summary>
    [BurstCompile]
    public struct UVProjectionJob : IJobParallelFor
    {
        // Géométrie (espace local du mesh).
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<int> triangles;     // longueur = 3 * triCount
        [ReadOnly] public NativeArray<float2> existingUV; // par vertex
        // Masque par triangle. Longueur 0 => aucun masque (toutes les faces projetées).
        [ReadOnly] public NativeArray<bool> triangleMask;

        public int projectionType;                       // (int)UVProjectionType
        public float4x4 meshToFrame;                      // local mesh -> cadre normalisé
        public float2 tiling;
        public float2 offset;
        public float rotationSin;
        public float rotationCos;

        [WriteOnly] public NativeArray<float2> cornerUV;  // longueur = 3 * triCount

        public void Execute(int corner)
        {
            int tri = corner / 3;
            int vIndex = triangles[corner];

            bool projected = triangleMask.Length == 0 || triangleMask[tri];
            if (!projected)
            {
                // Face non masquée : on conserve l'UV existante du vertex.
                cornerUV[corner] = existingUV[vIndex];
                return;
            }

            float3 pFrame = math.transform(meshToFrame, vertices[vIndex]);
            float2 uv;

            switch (projectionType)
            {
                case 1: // Box
                case 4: // Triplanar (box sur la bounding box locale)
                    uv = ProjectBox(tri, pFrame);
                    break;
                case 2: // Cylindrical
                    uv = ProjectCylinder(pFrame);
                    break;
                case 3: // Spherical
                    uv = ProjectSphere(pFrame);
                    break;
                default: // Planar
                    uv = new float2(pFrame.x + 0.5f, pFrame.y + 0.5f);
                    break;
            }

            // Rotation dans le plan autour de (0.5, 0.5), puis tiling + offset.
            uv -= new float2(0.5f, 0.5f);
            uv = new float2(
                uv.x * rotationCos - uv.y * rotationSin,
                uv.x * rotationSin + uv.y * rotationCos);
            uv += new float2(0.5f, 0.5f);
            uv = uv * tiling + offset;

            cornerUV[corner] = uv;
        }

        // Projection box : la face est assignée au plan dont l'axe domine sa
        // normale. Le signe de la normale conditionne un flip pour que la face
        // opposée ne soit pas en miroir.
        float2 ProjectBox(int tri, float3 pFrame)
        {
            int b = tri * 3;
            float3 a0 = math.transform(meshToFrame, vertices[triangles[b]]);
            float3 a1 = math.transform(meshToFrame, vertices[triangles[b + 1]]);
            float3 a2 = math.transform(meshToFrame, vertices[triangles[b + 2]]);
            float3 n = math.cross(a1 - a0, a2 - a0);
            float3 an = math.abs(n);

            if (an.x >= an.y && an.x >= an.z)
            {
                // Plan YZ : U depuis Z, V depuis Y.
                float u = n.x >= 0f ? -pFrame.z : pFrame.z;
                return new float2(u + 0.5f, pFrame.y + 0.5f);
            }
            if (an.y >= an.x && an.y >= an.z)
            {
                // Plan XZ : U depuis X, V depuis Z.
                float v = n.y >= 0f ? pFrame.z : -pFrame.z;
                return new float2(pFrame.x + 0.5f, v + 0.5f);
            }

            // Plan XY : U depuis X, V depuis Y.
            float ux = n.z >= 0f ? pFrame.x : -pFrame.x;
            return new float2(ux + 0.5f, pFrame.y + 0.5f);
        }

        // Projection cylindrique : axe = Y du cadre. Angle -> U, hauteur -> V.
        static float2 ProjectCylinder(float3 p)
        {
            float angle = math.atan2(p.z, p.x);                // [-pi, pi]
            float u = angle / (2f * math.PI) + 0.5f;
            return new float2(u, p.y + 0.5f);
        }

        // Projection sphérique : longitude -> U, latitude -> V.
        static float2 ProjectSphere(float3 p)
        {
            float len = math.length(p);
            if (len < 1e-8f)
                return new float2(0.5f, 0.5f);

            float3 d = p / len;
            float u = math.atan2(d.z, d.x) / (2f * math.PI) + 0.5f;
            float v = math.acos(math.clamp(d.y, -1f, 1f)) / math.PI;
            return new float2(u, v);
        }
    }
}
