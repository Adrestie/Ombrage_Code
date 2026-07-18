// GrassVFXFeeder.cs
// Phase 0 of the VFX-Graph grass system (autonomous — lives under Assets/VFX/Grass and does NOT touch
// the BRG grass under Assets/Shader/Grass). This is an independent COPY of the terrain-bake half of
// GrassBladesRuntime, repackaged so a VFX Graph can sample the terrain on the GPU.
//
// It bakes the Unity Terrain(s) once into GPU-friendly resources and pushes them to a VisualEffect as
// exposed properties:
//   - heightmap -> Texture2DArray<RFloat>     (1 slice/tile)        -> ground height (h in [0,1] = [0, sizeY])
//   - splatmaps -> Texture2DArray<RGBA32> x2  (layers 0-3 / 4-7)    -> where grass grows / which species
//   - tiles     -> StructuredBuffer<TerrainTile>                    -> O(1) FindTile for multi-terrain
//   - grid scalars (origin, tile size, cols, rows)
//
// Why bake: Terrain CPU APIs (GetHeights / alphamapTextures) can't be sampled per-particle on the GPU.
// We snapshot them once; the VFX then samples the textures every frame for free. Re-baked only when the
// terrain set/position/resolution changes (SameBakeSet cache), on live sculpt/paint (TerrainCallbacks),
// or on a manual Re-bake (force a refresh in Edit mode WITHOUT entering Play).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Ombrage.VFXGrass
{
    [ExecuteAlways]
    [AddComponentMenu("Ombrage/VFX Grass/Grass VFX Feeder")]
    public class GrassVFXFeeder : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Terrains to bake. Empty = Terrain.activeTerrains.")]
        public Terrain[] terrains;

        [Header("Target")]
        [Tooltip("VisualEffect that receives the baked terrain. Optional in Phase 0 (the .vfx need not declare the properties yet — pushes are guarded by Has*).")]
        public VisualEffect targetVfx;

        // ---- VFX exposed-property contract: names the .vfx blackboard must declare (later phases). ----
        public const string kHeightmap  = "_TerrainHeightmap";  // Texture2DArray
        public const string kControl0   = "_TerrainControl0";   // Texture2DArray
        public const string kControl1   = "_TerrainControl1";   // Texture2DArray
        public const string kTiles      = "_TerrainTiles";      // GraphicsBuffer<TerrainTile>
        public const string kGridOrigin = "_TerrainGridOrigin"; // Vector2 (world XZ of cell (0,0))
        public const string kTileSize   = "_TerrainTileSize";   // Vector2 (tile XZ size)
        public const string kGridCols   = "_TerrainGridCols";   // int
        public const string kGridRows   = "_TerrainGridRows";   // int

        // MUST match the HLSL TerrainTile struct the VFX will sample (32 bytes). Same layout as the BRG system.
        struct TerrainTile
        {
            public Vector2 worldPosXZ;
            public float   posY;
            public float   sizeY;
            public Vector2 sizeXZ;
            public int     slice;  // Texture2DArray slice (-1 = empty cell)
            public int     pad;
        }
        const int kTileStride = 32;

        // Baked GPU resources.
        Texture2DArray m_Heightmap;
        Texture2DArray m_Control0;
        Texture2DArray m_Control1;
        GraphicsBuffer m_Tiles;

        // Regular tile lattice.
        Terrain[] m_Baked;           // terrains at last bake (slice index = array index)
        Vector3[] m_BakedPositions;  // their positions at bake time (cache signature)
        Vector2 m_GridOrigin, m_TileSize;
        int m_GridCols, m_GridRows;
        bool m_Dirty;

        // Public read access for the next phases / debug.
        public Texture2DArray Heightmap => m_Heightmap;
        public Texture2DArray Control0 => m_Control0;
        public Texture2DArray Control1 => m_Control1;
        public GraphicsBuffer Tiles => m_Tiles;
        public Vector2 GridOrigin => m_GridOrigin;
        public Vector2 TileSize => m_TileSize;
        public int GridCols => m_GridCols;
        public int GridRows => m_GridRows;
        public int BakedTileCount => m_Baked != null ? m_Baked.Length : 0;

        void OnEnable()
        {
            Bake();
            Push();
            TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
            TerrainCallbacks.textureChanged   += OnTextureChanged;
        }

        void OnDisable()
        {
            TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
            TerrainCallbacks.textureChanged   -= OnTextureChanged;
            Dispose();
        }

        void OnValidate() => m_Dirty = true; // inspector change (terrains / targetVfx) -> re-bake next Update

        void Update()
        {
            if (!m_Dirty) return;
            m_Dirty = false;
            m_Baked = null;  // invalidate the cache: content edits don't change the SameBakeSet signature
            Bake();
            Push();
        }

        // Manual re-bake — force a refresh in Edit mode WITHOUT entering Play (button + context menu).
        [ContextMenu("Re-bake terrain")]
        public void Rebake()
        {
            m_Baked = null;
            Bake();
            Push();
        }

        // ----------------------------------------------------------------------------
        List<Terrain> ResolveTerrains()
        {
            var list = new List<Terrain>();
            var src = (terrains != null && terrains.Length > 0) ? terrains : Terrain.activeTerrains;
            if (src != null)
                foreach (var t in src)
                    if (t != null && t.terrainData != null) list.Add(t);
            return list;
        }

        void Bake()
        {
            var valid = ResolveTerrains();
            if (valid.Count == 0) { Dispose(); return; }
            if (SameBakeSet(valid)) return; // cache hit — nothing relevant changed

            Dispose();
            m_Baked = valid.ToArray();

            // Regular grid: origin = min corner, identical tile size, cols/rows deduced from positions.
            var td0 = m_Baked[0].terrainData;
            m_TileSize = new Vector2(td0.size.x, td0.size.z);
            float ox = float.MaxValue, oz = float.MaxValue;
            foreach (var t in m_Baked) { var p = t.transform.position; ox = Mathf.Min(ox, p.x); oz = Mathf.Min(oz, p.z); }
            m_GridOrigin = new Vector2(ox, oz);
            int cols = 1, rows = 1;
            foreach (var t in m_Baked)
            {
                var p = t.transform.position;
                cols = Mathf.Max(cols, Mathf.RoundToInt((p.x - ox) / m_TileSize.x) + 1);
                rows = Mathf.Max(rows, Mathf.RoundToInt((p.z - oz) / m_TileSize.y) + 1);
            }
            m_GridCols = cols; m_GridRows = rows;

            BakeHeightmap();
            BakeControl();
            BuildTiles();

            m_BakedPositions = new Vector3[m_Baked.Length];
            for (int i = 0; i < m_Baked.Length; i++) m_BakedPositions[i] = m_Baked[i].transform.position;
        }

        bool SameBakeSet(List<Terrain> valid)
        {
            if (m_Baked == null || m_Heightmap == null || m_Control0 == null || m_BakedPositions == null) return false;
            if (m_Baked.Length != valid.Count) return false;
            for (int i = 0; i < m_Baked.Length; i++)
            {
                if (m_Baked[i] != valid[i] || valid[i].terrainData == null) return false;
                if (m_BakedPositions[i] != valid[i].transform.position) return false;
            }
            if (m_Heightmap.width != valid[0].terrainData.heightmapResolution) return false;
            if (m_Control0.width  != valid[0].terrainData.alphamapResolution) return false;
            return true;
        }

        void BakeHeightmap()
        {
            int res = m_Baked[0].terrainData.heightmapResolution;
            m_Heightmap = new Texture2DArray(res, res, m_Baked.Length, TextureFormat.RFloat, false, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GrassVFX_Heightmap" };

            var data = new float[res * res];
            for (int s = 0; s < m_Baked.Length; s++)
            {
                var td = m_Baked[s].terrainData;
                int r = Mathf.Min(td.heightmapResolution, res);
                var h = td.GetHeights(0, 0, r, r);
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                        data[y * res + x] = (y < r && x < r) ? h[y, x] : 0f;
                m_Heightmap.SetPixelData(data, 0, s);
            }
            m_Heightmap.Apply(false, false);
        }

        void BakeControl()
        {
            int ares = m_Baked[0].terrainData.alphamapResolution;
            m_Control0 = NewControlArray(ares, "GrassVFX_Control0");
            m_Control1 = NewControlArray(ares, "GrassVFX_Control1");
            for (int s = 0; s < m_Baked.Length; s++)
            {
                var maps = m_Baked[s].terrainData.alphamapTextures;
                if (maps.Length > 0 && maps[0].width == ares && maps[0].height == ares)
                    Graphics.CopyTexture(maps[0], 0, 0, m_Control0, s, 0);
                if (maps.Length > 1 && maps[1].width == ares && maps[1].height == ares)
                    Graphics.CopyTexture(maps[1], 0, 0, m_Control1, s, 0);
            }
        }

        Texture2DArray NewControlArray(int res, string name) =>
            new Texture2DArray(res, res, m_Baked.Length, TextureFormat.RGBA32, false, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };

        void BuildTiles()
        {
            int cells = m_GridCols * m_GridRows;
            var tiles = new TerrainTile[cells];
            for (int i = 0; i < cells; i++) tiles[i].slice = -1;
            for (int s = 0; s < m_Baked.Length; s++)
            {
                var t = m_Baked[s]; var td = t.terrainData; var p = t.transform.position;
                int col = Mathf.RoundToInt((p.x - m_GridOrigin.x) / m_TileSize.x);
                int row = Mathf.RoundToInt((p.z - m_GridOrigin.y) / m_TileSize.y);
                int cell = col + row * m_GridCols;
                if (cell < 0 || cell >= cells) continue;
                tiles[cell] = new TerrainTile
                {
                    worldPosXZ = new Vector2(p.x, p.z),
                    posY = p.y, sizeY = td.size.y,
                    sizeXZ = new Vector2(td.size.x, td.size.z),
                    slice = s, pad = 0
                };
            }
            m_Tiles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells, kTileStride);
            m_Tiles.SetData(tiles);
        }

        // Push the baked resources to the VFX exposed properties (Has* guards = harmless no-op if the
        // graph doesn't declare them yet, e.g. during Phase 0).
        public void Push()
        {
            if (targetVfx == null) return;
            if (m_Heightmap != null && targetVfx.HasTexture(kHeightmap)) targetVfx.SetTexture(kHeightmap, m_Heightmap);
            if (m_Control0  != null && targetVfx.HasTexture(kControl0))  targetVfx.SetTexture(kControl0, m_Control0);
            if (m_Control1  != null && targetVfx.HasTexture(kControl1))  targetVfx.SetTexture(kControl1, m_Control1);
            if (m_Tiles     != null && targetVfx.HasGraphicsBuffer(kTiles)) targetVfx.SetGraphicsBuffer(kTiles, m_Tiles);
            if (targetVfx.HasVector2(kGridOrigin)) targetVfx.SetVector2(kGridOrigin, m_GridOrigin);
            if (targetVfx.HasVector2(kTileSize))   targetVfx.SetVector2(kTileSize, m_TileSize);
            if (targetVfx.HasInt(kGridCols)) targetVfx.SetInt(kGridCols, m_GridCols);
            if (targetVfx.HasInt(kGridRows)) targetVfx.SetInt(kGridRows, m_GridRows);
        }

        void Dispose()
        {
            DestroyTex(ref m_Heightmap);
            DestroyTex(ref m_Control0);
            DestroyTex(ref m_Control1);
            if (m_Tiles != null) { m_Tiles.Dispose(); m_Tiles = null; }
            m_Baked = null; m_BakedPositions = null;
        }

        static void DestroyTex(ref Texture2DArray t)
        {
            if (t == null) return;
            if (Application.isPlaying) Destroy(t); else DestroyImmediate(t);
            t = null;
        }

        // --- Live edit re-bake (sculpt / paint) ---
        void OnHeightmapChanged(Terrain t, RectInt region, bool synched) { if (Affects(t)) m_Dirty = true; }
        void OnTextureChanged(Terrain t, string textureName, RectInt region, bool synched) { if (Affects(t)) m_Dirty = true; }

        bool Affects(Terrain t)
        {
            if (m_Baked == null) return true;
            foreach (var b in m_Baked) if (b == t) return true;
            return false;
        }
    }
}
