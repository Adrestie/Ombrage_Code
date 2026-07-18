// OceanSurfaceRuntime.cs  (Ocean_v2 / P2)
// État RUNTIME de la surface, détenu par OceanSystem via SetRuntime (JAMAIS sérialisé dans le SO).
// Porte :
//   - le GameObject enfant non sérialisé (MeshFilter + MeshRenderer) qui rend la surface ;
//   - le maillage de base : grille UNIFORME FIXE world-locked (la tessellation hardware gère seule la
//     densité ; anneaux concentriques / suivi caméra = pivot clipmap Q3.4 DIFFÉRÉ — voir CLIPMAP_READY) ;
//   - les bounds étendus XYZ, RECALCULÉS à chaud sur changement d'amplitude (évite le culling GPU des
//     crêtes déplacées en vue rasante) ;
//   - le coordinator Motion Vectors T-1 (OceanMotionVectorPass) ;
//   - lastSnapshotFrame est porté par le coordinator (proxy de cadence = Time.frameCount, pas un champ P1).
//
// Cycle de vie symétrique strict (anti-fuite [ExecuteAlways]) : Build au OnModuleEnable, Destroy au
// OnModuleDisable/Teardown ; reconstruction du mesh uniquement sur changement de structure (extent/res).
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    public sealed class OceanSurfaceRuntime
    {
        public GameObject go;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Mesh mesh;
        public Material material;
        public bool ownsMaterial;          // true si la RT a créé le matériau (→ à détruire)

        public readonly OceanMotionVectorPass mv = new OceanMotionVectorPass();

        // Feature écume P4 (Q12.4 : feature interne du module surface) : arrays de moments (J, J²)
        // mippés, alloués en miroir des arrays P1, libérés au OnModuleDisable.
        public readonly OceanFoamFeature foam = new OceanFoamFeature();

        // Détection de reconstruction du mesh (structure) et de recalcul des bounds (amplitude).
        public int gridParamHash = int.MinValue;   // extent + résolution
        public int boundsParamHash = int.MinValue;  // maxWaveHeight + maxHorizontalDisplacement + safety
        // Détection d'un saut du champ de vagues (slider LookDev : état de mer / amplitude / choppiness…)
        // pour invalider les Motion Vectors ce frame-là (le déplacement change discontinûment).
        public int dispParamHash = int.MinValue;

        // CLIPMAP_READY : remplacer GenerateUniformGrid par une topologie en anneaux concentriques +
        // suivi caméra (recentrage/snapping XZ) si le pivot clipmap Q3.4 est déclenché après mesure.
        // En P2 la grille est UNIFORME et FIXE (world-locked) : aucun recentrage par frame.
        public static Mesh GenerateUniformGrid(int resolution, float extent)
        {
            resolution = Mathf.Clamp(resolution, 2, 254);  // <256 segments → <65k verts (UInt16 OK ; sinon UInt32)
            int verts1D = resolution + 1;
            int vCount = verts1D * verts1D;

            var m = new Mesh { name = "OceanSurfaceGrid" };
            m.indexFormat = (vCount > 65000)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            var positions = new Vector3[vCount];
            var uvs = new Vector2[vCount];
            float step = (2f * extent) / resolution;   // grille centrée sur l'origine du GO

            for (int z = 0; z < verts1D; z++)
            {
                for (int x = 0; x < verts1D; x++)
                {
                    int i = z * verts1D + x;
                    float px = -extent + x * step;
                    float pz = -extent + z * step;
                    positions[i] = new Vector3(px, 0f, pz);
                    uvs[i] = new Vector2((float)x / resolution, (float)z / resolution);
                }
            }

            var tris = new int[resolution * resolution * 6];
            int t = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i0 = z * verts1D + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + verts1D;
                    int i3 = i2 + 1;
                    // 2 triangles CW (cohérent avec Cull Back / outputtopology triangle_cw HDRP).
                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            }

            m.vertices = positions;
            m.uv = uvs;
            m.triangles = tris;
            // Normale plane vers le haut : la vraie normale est recomposée analytiquement dans le
            // fragment depuis les pentes des cascades P1 (anti-bug#2). On fournit néanmoins une normale
            // de base correcte pour le hull/domain HDRP (back-face cull, interpolation).
            var normals = new Vector3[vCount];
            for (int i = 0; i < vCount; i++) normals[i] = Vector3.up;
            m.normals = normals;
            return m;
        }

        public void SetBounds(float maxWaveHeight, float maxHorizontalDisp, float extent)
        {
            if (mesh == null) return;
            // Bounds XZ = étendue de la grille + déplacement horizontal max ; Y = hauteur de vague max.
            float halfXZ = extent + Mathf.Max(0f, maxHorizontalDisp);
            float halfY = Mathf.Max(0.01f, maxWaveHeight);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(halfXZ * 2f, halfY * 2f, halfXZ * 2f));
        }
    }
}
