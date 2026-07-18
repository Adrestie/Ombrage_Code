// GrassBladesRuntime.cs
// État runtime + logique des brins d'herbe L0 (BRG + compute scatter), détenu par le
// TerrainProfileController via le dictionnaire de runtime des modules (comme DeformationRuntime).
// Un ScriptableObject (GrassBladesModule) ne peut porter ni ressources GPU ni état runtime par
// instance : la config (tuning + refs assets + sélection de layers) vit sur le module (M), les
// terrains viennent du contexte (ctx.terrains résolus par le contrôleur), et tout le GPU vit ici.
//
// Rendu = un BatchRendererGroup (custom DOTS-instanced HDRP shader, éclairé dans toutes les passes),
// entièrement GPU-driven, deux passes compute :
//   - ScatterGrid (au franchissement de cellule) -> population SOURCE compactée dans le buffer
//     d'instances (fenêtre centrée caméra, anti-shimmer : brin épinglé à sa cellule monde) +
//     multi-terrain (Texture2DArray + buffer de tuiles, FindTile O(1)).
//   - CullAndLod (chaque frame, DispatchIndirect) -> listes d'INDICES par population :
//     NEAR (frustum, mesh détaillé, MV objet) / FAR (frustum, mesh réduit, MV caméra) /
//     SHADOW (sphère distance caméra, mesh réduit, cascades limitées).
//     Counts GPU-side dans les args indirects -> aucun readback, 2 draws caméra + 1 draw ombre.
// VENT : non poussé ici — les brins lisent les globals _GrassWind* partagés (poussés par GrassTint).
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.TerrainFeatures
{
    public class GrassBladesRuntime
    {
        BatchRendererGroup m_BRG;
        // ONE instance buffer (the source population). The per-frame cull writes INDEX lists, the
        // instance data itself never moves after a scatter.
        GraphicsBuffer m_InstanceData;
        BatchID m_BatchID;
        // 3 x IndirectDrawIndexedArgs (15 uints): NEAR @0, FAR @5, SHADOW @10. instanceCounts
        // ([1]/[6]/[11]) are zeroed by the CPU before each cull dispatch and written on the GPU.
        GraphicsBuffer m_ArgsBuffer;
        // Visible-index lists, 3 sections of m_Capacity uints each: near | far | shadow.
        GraphicsBuffer m_VisibleIndexBuffer;
        // Scatter meta: [0] = cull dispatch groupsX, [1] = 1, [2] = 1, [3] = source blade count.
        GraphicsBuffer m_SourceMeta;
        bool m_Setup;
        int m_ScatterKernel = -1;
        int m_CullKernel = -1;
        // Per-species meshes: NEAR (detailed) + FAR/SHADOW (low). Blade species share the generated
        // Bézier strip; a species with a custom mesh uses it (same for near & far). Indexed by species.
        BatchMeshID[] m_NearID;
        BatchMeshID[] m_FarID;
        Mesh[] m_NearMesh;                 // Mesh objects (for the indirect-args index counts)
        Mesh[] m_FarMesh;
        BatchMaterialID m_MaterialID;
        Mesh m_GeneratedMesh;              // shared procedural blade (detailed), owned/destroyed here
        Mesh m_GeneratedMeshLow;           // shared procedural blade (low)
        Texture2DArray m_HeightmapArray;
        Texture2DArray m_ControlArray0;
        Texture2DArray m_ControlArray1;
        GraphicsBuffer m_TilesBuffer;
        GraphicsBuffer m_SpeciesBuffer;    // multi-species table (StructuredBuffer<GrassSpecies>, 80 B/entry)
        int m_SpeciesCount;
        float m_MaxBladeHeight = 0.6f;     // max species height*(1+heightRandom) — frustum-margin slack
        // Hi-Z occlusion (experimental): we capture the target camera's depth into our own tight texture
        // at endCameraRendering, paired with that camera's VP, then cull against it next frame (1-frame-late
        // but camera-matched). Reading HDRP's depth global directly was unreliable with multiple cameras.
        int m_CaptureKernel = -1;
        RenderTexture m_OccHiZ;            // captured conservative Hi-Z (reverse-Z depth)
        Matrix4x4 m_OccHiZVP;              // VP that produced m_OccHiZ
        Vector2 m_OccHiZNearFar;           // near/far of the captured camera
        bool m_OccHiZValid;                // a capture has happened (else the cull skips occlusion)
        bool m_OccSubscribed;             // endCameraRendering hooked

        // Horizon impostor (étape A) baked ONCE into a PERSISTENT RT, bound as the global _GrassImpostorTex
        // for the impostor draw (next steps) to sample. Re-baked only when the impostor params change
        // (content hash) -> survives keep-bake rebuilds; released on full teardown.
        RenderTexture m_ImpostorTex;
        int m_ImpostorHash = -1;
        Material m_ImpostorMat;                    // impostor material (Custom/HDRP/GrassImpostor)
        Mesh m_ImpostorQuad;                       // unit billboard quad (positionOS.x = side, .y = v)
        // Step 3 impostor BRG — a SEPARATE lightweight batch (own draw); never touches the blade cull.
        BatchRendererGroup m_ImpBRG;
        GraphicsBuffer m_ImpInstanceData;
        BatchID m_ImpBatchID;
        BatchMeshID m_ImpMeshID;
        BatchMaterialID m_ImpMatID;
        int m_ImpCount;                            // cards filled this frame (the Direct-draw count)
        int m_ImpMaxCount;                         // fixed buffer capacity (the full camera window)
        Vector3[] m_ImpPositions;                  // cached card ground positions (CPU frustum cull)
        int m_ImpLastCellX = int.MinValue, m_ImpLastCellZ = int.MinValue; // re-fill cell tracker
        uint m_ImpAddrO2W, m_ImpAddrGP, m_ImpAddrGP2, m_ImpAddrCol;
        float m_ImpHalf;                           // impostor display reach (m) = maxBladeDistance * impostorReach
        const float kImpRefillCell = 16f;          // re-fill the window on this coarse cell-cross (card spacing = M.impostorSpacing)
        Terrain[] m_Tiles;                 // active terrains (from ctx.terrains); slice index = array index
        Vector3[] m_BakedPositions;        // terrain-bake cache signature (tile positions at bake time)
        float[][,,] m_AlphaCPU;            // per-tile CPU copy of the splat alphamap (impostor grass placement)
        Vector2 m_GridOrigin;
        Vector2 m_TileSize;
        int m_GridCols, m_GridRows;
        int m_LastOriginCellX = int.MinValue, m_LastOriginCellZ = int.MinValue;
        // Geometry-clipmap LOD (Phase 3): m_NumLevels concentric levels, each a m_ClipRes² lattice at
        // spacing baseSpacing·2^L centered on the camera. Cell count is FIXED per build (no per-frame
        // resize): all threads run, holed/culled ones just don't append. The compacted SOURCE count
        // varies. m_ClipRes is derived from cellPixels & screen height (resolution-independent density).
        int m_ClipRes;                     // cells per side, per level
        int m_NumLevels;
        int m_Capacity;                    // = m_NumLevels * m_ClipRes² (dispatch threads + buffer + section stride)
        int m_InstanceCount;               // = m_Capacity (every level/cell gets a thread)
        int m_BuiltScreenH;                // screen height at last build (rebuild on resolution change)
        uint m_AddrXform, m_AddrCol, m_AddrGP, m_AddrGP2;
        uint[] m_ArgsData;                 // (m_SpeciesCount*3) IndirectDrawIndexedArgs (5 uints each)
        static readonly uint[] s_MetaReset = { 0u, 1u, 1u, 0u };
        // Up to 8 species (matches GRASS_MAX_SECTIONS=24 in the compute).
        const int kMaxSpecies = 8;
        readonly Plane[] m_PlaneCache = new Plane[6];
        readonly Vector4[] m_PlaneVecs = new Vector4[6];

        // Config (module) + scene refs (controller), refreshed each call (like DeformationRuntime).
        GrassBladesModule M;
        TerrainProfileController C;

        const int kSizeOfPackedMatrix = sizeof(float) * 4 * 3; // 48 (float3x4)
        const int kSizeOfFloat4       = sizeof(float) * 4;     // 16
        const int kSizeOfMatrix       = sizeof(float) * 4 * 4; // 64
        // xform(16) + color + params + params2. ALL builtin matrices are a shared identity (no per-
        // instance o2w/w2o/prevM); the compact _GrassXform (pos+yaw) drives the hook-rebuilt transform.
        const int kBytesPerInstance   = kSizeOfFloat4 * 4; // 64
        const int kExtraBytes         = kSizeOfMatrix * 2;     // head: zero + padding
        // Clipmap bounds. Per-level resolution is derived from cellPixels & screen height and clamped
        // here; total threads = numLevels·clipRes² stays far under the 1D dispatch ceiling (65535·64).
        // e.g. 10·384² ≈ 1.47M -> 23040 groups. Even clipRes (centered window needs a clean half).
        const int kMinClipRes  = 64;
        const int kMaxClipRes  = 512;
        const int kMaxLevels   = 10;

        struct PackedMatrix
        {
            public float c0x, c0y, c0z, c1x, c1y, c1z, c2x, c2y, c2z, c3x, c3y, c3z;
            public PackedMatrix(Matrix4x4 m)
            {
                c0x = m.m00; c0y = m.m10; c0z = m.m20;
                c1x = m.m01; c1y = m.m11; c1z = m.m21;
                c2x = m.m02; c2y = m.m12; c2z = m.m22;
                c3x = m.m03; c3y = m.m13; c3z = m.m23;
            }
        }

        // MUST match struct TerrainTile in GrassScatter.compute (32 bytes). Placement is species-driven
        // (per-layer density) now, so the tile no longer carries a grass mask.
        struct TerrainTileData
        {
            public Vector2 worldPosXZ;
            public float   posY;
            public float   sizeY;
            public Vector2 sizeXZ;
            public int     slice;
            public int     pad;
        }
        const int kTileStride = 32;

        // MUST match struct GrassSpecies in GrassScatter.compute (80 bytes = 5 float4).
        struct SpeciesData
        {
            public Vector4 baseColor;    // linear rgb
            public Vector4 shape;        // height, heightRandom, width, bend
            public Vector4 shape2;       // tilt, colorVariation, kind, reserved
            public Vector4 density0123;  // per-layer density (0..1) for splat layers 0-3
            public Vector4 density4567;  // per-layer density (0..1) for splat layers 4-7
        }
        const int kSpeciesStride = 80;

        // ---------------------------------------------------------------------------------
        // Lifecycle (driven by GrassBladesModule via the controller).
        public void Initialize(GrassBladesModule m, TerrainApplyContext ctx)
        {
            M = m; C = ctx != null ? ctx.controller : null;
            // Build lazily in EnsureBuilt (called from Apply) so a half-configured module is safe.
        }

        public void Dispose() => Teardown(false);

        // Build (or rebuild on a dirty inspector change). Called every frame from module.Apply.
        public void EnsureBuilt(GrassBladesModule m, TerrainApplyContext ctx)
        {
            M = m; C = ctx != null ? ctx.controller : null;

            // The whole grass system is OFF until at least one species is defined (no default grass).
            // Cheap early-out each frame; a torn-down state renders nothing.
            bool wantRender = m.material != null && m.species != null && m.species.Length > 0;
            if (!wantRender) { if (m_Setup) Teardown(keepTerrainBake: true); return; }

            if (!m_Setup || m.dirty)
            {
                // Keep the heavy terrain bakes (GetHeights readbacks) across inspector tweaks —
                // BuildTerrainGrid re-bakes only if the terrain set actually changed.
                Teardown(keepTerrainBake: true);
                Build(ctx);
                m.dirty = false;
            }
        }

        void Build(TerrainApplyContext ctx)
        {
            // Requires a material AND at least one species (no default grass).
            if (M == null || M.material == null || M.species == null || M.species.Length == 0) return;

            m_BRG = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            m_BRG.SetEnabledViewTypes(new[] { BatchCullingViewType.Camera, BatchCullingViewType.Light });

            if (!m_OccSubscribed) { RenderPipelineManager.endCameraRendering += OnEndCameraRendering; m_OccSubscribed = true; }

            // Shared procedural blade (ONE hi-def mesh), used by every BLADE species (shape is per-instance).
            m_GeneratedMesh = M.meshOverride != null ? M.meshOverride : BuildBladeMesh(M.bladeSegments);
            // 1a: ONE hi-def blade mesh for NEAR + FAR + SHADOW — the vertex collapses detail with
            // distance (no LOD mesh swap = no pop). m_GeneratedMeshLow aliases it (Teardown's
            // lowShared check then skips a double free). lowMeshSegments now drives the collapse
            // target (_GrassCollapseTargetSeg, pushed by the module), not a separate mesh.
            m_GeneratedMeshLow = m_GeneratedMesh;
            BatchMeshID bladeID    = m_BRG.RegisterMesh(m_GeneratedMesh);
            BatchMeshID bladeIDLow = m_GeneratedMeshLow == m_GeneratedMesh ? bladeID : m_BRG.RegisterMesh(m_GeneratedMeshLow);
            m_MaterialID = m_BRG.RegisterMaterial(M.material);

            BuildSpeciesBuffer(); // sets m_SpeciesCount

            // Per-species meshes: a species with a custom mesh uses it (near==far); otherwise the blade.
            m_NearID = new BatchMeshID[m_SpeciesCount]; m_FarID = new BatchMeshID[m_SpeciesCount];
            m_NearMesh = new Mesh[m_SpeciesCount];      m_FarMesh = new Mesh[m_SpeciesCount];
            for (int s = 0; s < m_SpeciesCount; s++)
            {
                Mesh custom = M.species[s] != null ? M.species[s].mesh : null;
                if (custom != null)
                {
                    var id = m_BRG.RegisterMesh(custom);
                    m_NearID[s] = id;   m_FarID[s] = id;
                    m_NearMesh[s] = custom; m_FarMesh[s] = custom;
                }
                else
                {
                    m_NearID[s] = bladeID;   m_FarID[s] = bladeIDLow;
                    m_NearMesh[s] = m_GeneratedMesh; m_FarMesh[s] = m_GeneratedMeshLow;
                }
            }

            BuildTerrainGrid(ctx);

            m_BRG.SetGlobalBounds(new Bounds(OriginPos(), Vector3.one * 1000f));

            AllocateAndPopulate(ctx);
            EnsureImpostorBake();   // Step 1: persistent horizon-card bake -> global _GrassImpostorTex
            BuildImpostorBRG();     // Step 3a: separate impostor BRG (far-band cards)
            m_Setup = true;
        }

        public void Tick(GrassBladesModule m, TerrainApplyContext ctx)
        {
            M = m; C = ctx != null ? ctx.controller : null;
            if (!m_Setup) return;
            if (!M.useCompute || M.scatterCompute == null || m_InstanceData == null) return;

            // Re-scatter only when the camera crosses a level-0 cell (the clipmap is camera-centered
            // and 360°, so rotation never needs a re-scatter — the per-frame frustum cull handles it).
            // Cheap: 1 compute dispatch, no readback; blades stay world-pinned so scrolling never shimmers.
            // A screen-resolution change rebuilds (clipRes depends on it).
            if (M.cameraCentered)
            {
                if (ScreenHeight() != m_BuiltScreenH) { m.dirty = true; return; } // rebuilt next Apply
                Vector2 c = ClipCenterXZ();
                float sp0 = Mathf.Max(M.spacing, 0.01f);
                int ccx = Mathf.FloorToInt(c.x / sp0), ccz = Mathf.FloorToInt(c.y / sp0);
                if (ccx != m_LastOriginCellX || ccz != m_LastOriginCellZ)
                    DispatchScatter();
            }

            // Impostor cards follow the camera (world-pinned): re-fill their positions on cell-cross.
            if (m_ImpBRG != null && m_ImpMaxCount > 0)
            {
                Vector2 ic = ClipCenterXZ();
                int icx = Mathf.FloorToInt(ic.x / kImpRefillCell), icz = Mathf.FloorToInt(ic.y / kImpRefillCell);
                if (icx != m_ImpLastCellX || icz != m_ImpLastCellZ)
                {
                    FillImpostorPositions();
                    m_ImpLastCellX = icx; m_ImpLastCellZ = icz;
                }
            }

            // Allocate the occlusion Hi-Z here (Update context) so the render callback only dispatches.
            EnsureOccHiZ(GetWindowCamera());
            // ...then refresh the visible lists (frustum + Hi-Z occlusion, both always on).
            DispatchCull();

            // Horizon impostor — étape A: one-shot bake preview (bakes a blade tuft to a PNG to inspect).
            // Not wired into rendering yet (the horizon draw layer is a separate, future step).
            if (M.previewImpostorBake) { BakeImpostorPreview(); M.previewImpostorBake = false; }
        }

        // ---- Horizon impostor bake (étape A) -------------------------------------------------------
        // Build a tuft of real (curved, tapered) blades on the CPU and render it into an offscreen RT to
        // bake the horizon card: RGB = neutral grass shading, A = silhouette coverage. Bakes + dumps a PNG.
        Mesh BuildTuftMesh(int bladeCount, float radius, float height, out float maxTop)
        {
            const int seg = 5;
            int rows = seg + 1;
            var verts = new System.Collections.Generic.List<Vector3>(bladeCount * rows * 2);
            var norms = new System.Collections.Generic.List<Vector3>(bladeCount * rows * 2);
            var uvs   = new System.Collections.Generic.List<Vector2>(bladeCount * rows * 2);
            var tris  = new System.Collections.Generic.List<int>(bladeCount * seg * 6);
            maxTop = 0f;

            var st = UnityEngine.Random.state;
            UnityEngine.Random.InitState(12345);          // deterministic tuft
            for (int bld = 0; bld < bladeCount; bld++)
            {
                float yaw = UnityEngine.Random.value * Mathf.PI * 2f;
                float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
                Vector3 fwd = new Vector3(sy, 0f, cy);     // blade lean direction
                Vector3 right = new Vector3(cy, 0f, -sy);
                Vector2 disk = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 baseP = new Vector3(disk.x, 0f, disk.y);
                float h = height * Mathf.Lerp(0.7f, 1.1f, UnityEngine.Random.value);
                float bend = h * Mathf.Lerp(0.15f, 0.5f, UnityEngine.Random.value); // forward lean
                float w = Mathf.Lerp(0.02f, 0.035f, UnityEngine.Random.value);
                int baseIdx = verts.Count;

                Vector3 prevC = baseP;
                for (int j = 0; j < rows; j++)
                {
                    float t = (float)j / seg;
                    float up = h * t;
                    float lean = bend * t * t;
                    Vector3 c = baseP + Vector3.up * up + fwd * lean;
                    float hw = w * 0.5f * (1f - 0.85f * t);          // taper to the tip
                    Vector3 tangent = (j == 0) ? (Vector3.up) : (c - prevC).normalized;
                    Vector3 nrm = Vector3.Cross(tangent, right).normalized;
                    if (nrm.sqrMagnitude < 1e-4f) nrm = fwd;
                    verts.Add(c - right * hw); norms.Add(nrm); uvs.Add(new Vector2(0f, t));
                    verts.Add(c + right * hw); norms.Add(nrm); uvs.Add(new Vector2(1f, t));
                    prevC = c;
                    maxTop = Mathf.Max(maxTop, c.y);
                }
                for (int j = 0; j < seg; j++)
                {
                    int b = baseIdx + j * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            }
            UnityEngine.Random.state = st;

            var mesh = new Mesh { name = "GrassImpostorTuft", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Render the tuft into an RGBA RT with an ortho side view -> the impostor texture.
        RenderTexture BakeImpostor()
        {
            var tuft = BuildTuftMesh(M.impostorBladeCount, M.impostorTuftRadius, M.impostorTuftHeight, out float top);
            float halfH = Mathf.Max(top, 0.1f) * 0.55f;
            float halfW = Mathf.Max(M.impostorTuftRadius, 0.05f) * 1.25f;
            int h = Mathf.Clamp(M.impostorResolution, 64, 1024);
            int w = Mathf.Clamp(Mathf.RoundToInt(h * (halfW / halfH)), 16, 1024);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "GrassImpostor" };
            rt.Create();
            var mat = new Material(Shader.Find("Hidden/Ombrage/GrassImpostorBake")) { hideFlags = HideFlags.HideAndDontSave };

            // Camera at -Z looking +Z, centred vertically on the tuft. Scale(1,1,-1) = Unity's view Z flip.
            Vector3 camPos = new Vector3(0f, halfH, -10f);
            Matrix4x4 view = Matrix4x4.TRS(camPos, Quaternion.identity, new Vector3(1f, 1f, -1f)).inverse;
            // renderIntoTexture:false -> natural Y (root at v=0, tip at v=1) so we can sample with standard uv.
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-halfW, halfW, -halfH, halfH, 0.01f, 100f), false);

            var cmd = new CommandBuffer { name = "GrassImpostorBake" };
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, 0f));
            cmd.SetViewProjectionMatrices(view, proj);
            cmd.DrawMesh(tuft, Matrix4x4.identity, mat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            if (Application.isPlaying) { UnityEngine.Object.Destroy(tuft); UnityEngine.Object.Destroy(mat); }
            else { UnityEngine.Object.DestroyImmediate(tuft); UnityEngine.Object.DestroyImmediate(mat); }
            return rt;
        }

        // Step 1 — persistent bake: ensure m_ImpostorTex holds the baked horizon card and is bound as the
        // global _GrassImpostorTex. Re-bakes only when the impostor params change (cheap content hash), so it
        // survives keep-bake rebuilds (inspector tweaks of unrelated blade params). The impostor draw (next
        // steps) samples _GrassImpostorTex; nothing renders it yet.
        void EnsureImpostorBake()
        {
            int hash = M.impostorBladeCount
                     + M.impostorResolution * 31
                     + Mathf.RoundToInt(M.impostorTuftRadius * 1000f) * 131
                     + Mathf.RoundToInt(M.impostorTuftHeight * 1000f) * 517;
            if (m_ImpostorTex != null && hash == m_ImpostorHash)
            {
                Shader.SetGlobalTexture("_GrassImpostorTex", m_ImpostorTex);
                return;
            }
            if (m_ImpostorTex != null) { m_ImpostorTex.Release(); m_ImpostorTex = null; }
            m_ImpostorTex = BakeImpostor();
            m_ImpostorHash = hash;
            Shader.SetGlobalTexture("_GrassImpostorTex", m_ImpostorTex);
        }

        // ---- Step 2: dedicated impostor shader test (a ring of camera-facing billboard cards) --------
        // Validates GrassImpostor.shader end-to-end (billboard vertex + baked-card cutout + deferred
        // lighting/shadows) BEFORE the real impostor BRG/scatter (Step 3). Cards are placed in a ring at
        // the blade reach, facing the camera; they sample the SAME persistent bake the production layer will.
        void BuildImpostorQuad()
        {
            if (m_ImpostorQuad != null) return;
            var m = new Mesh { name = "GrassImpostorQuad" };
            m.SetVertices(new Vector3[] {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) });
            m.SetUVs(0, new Vector2[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) });
            m.SetNormals(new Vector3[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            m.SetTriangles(new int[] { 0, 2, 1, 1, 2, 3 }, 0);
            // The vertex hook relocates verts to the card's world position, so the local bounds are wrong for
            // culling -> huge bounds so Graphics.DrawMesh never frustum-culls the (billboarded) card away.
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            m_ImpostorQuad = m;
        }

        void EnsureImpostorMaterial()
        {
            if (m_ImpostorMat != null) return;
            var sh = Shader.Find("Custom/HDRP/GrassImpostor");
            if (sh == null) { Debug.LogWarning("[GrassImpostor] shader 'Custom/HDRP/GrassImpostor' introuvable."); return; }
            m_ImpostorMat = new Material(sh) { name = "GrassImpostorMat", hideFlags = HideFlags.HideAndDontSave };
            m_ImpostorMat.SetFloat("_AlphaCutoff", 0.5f);
            m_ImpostorMat.SetFloat("_Smoothness", 0.2f);
        }

        void BuildImpostorBRG()
        {
            if (m_ImpBRG != null) return;
            BuildImpostorQuad();
            EnsureImpostorMaterial();
            if (m_ImpostorMat == null || m_ImpostorQuad == null) return;
            m_ImpBRG = new BatchRendererGroup(OnPerformCullingImpostor, IntPtr.Zero);
            m_ImpBRG.SetEnabledViewTypes(new[] { BatchCullingViewType.Camera, BatchCullingViewType.Light });
            m_ImpMeshID = m_ImpBRG.RegisterMesh(m_ImpostorQuad);
            m_ImpMatID  = m_ImpBRG.RegisterMaterial(m_ImpostorMat);
            m_ImpBRG.SetGlobalBounds(new Bounds(OriginPos(), Vector3.one * 10000f));
            AllocateImpostorInstances();
        }

        // Per-instance: unity_ObjectToWorld (translate; the vertex reads the card root from it), _GrassParams.x
        // (height), _GrassParams2.x (width), _BaseColor (tint). unity_WorldToObject / MatrixPreviousM reuse the
        // shared identity at the buffer head (translate-only -> normals unaffected, no per-instance motion).
        // 3a fills a STATIC grid; 3b swaps it for the camera-pinned far-band scatter, 3c adds frustum culling.
        // The buffer is sized for the FULL camera window (fixed capacity m_ImpMaxCount). Constant per-card
        // data (size + tint) is written once; FillImpostorPositions re-writes only the o2w (positions) as the
        // camera moves. Per-instance: unity_ObjectToWorld (translate; the vertex reads the card root from it),
        // _GrassParams.x (height), _GrassParams2.x (width), _BaseColor (tint). W2O/prevM = shared head identity.
        void AllocateImpostorInstances()
        {
            m_ImpHalf = Mathf.Clamp(M.maxBladeDistance * M.impostorReach, 60f, 600f); // display reach (L2 tint beyond)
            float step = Mathf.Max(M.impostorSpacing, 1f);
            int side = Mathf.FloorToInt(2f * m_ImpHalf / step) + 1;
            m_ImpMaxCount = Mathf.Min(side * side, 40000); // cap: dense+far CPU fill cost -> clamp the buffer/draw

            const int kImpBytesPerInstance = 48 + 16 + 16 + 16; // o2w + _GrassParams + _GrassParams2 + _BaseColor
            int bufferCount = BufferCountForInstances(kImpBytesPerInstance, m_ImpMaxCount, kExtraBytes);

            m_ImpAddrO2W = (uint)(kSizeOfPackedMatrix * 2);             // 96 head (shared identity for W2O + prevM)
            m_ImpAddrGP  = m_ImpAddrO2W + (uint)(kSizeOfPackedMatrix * m_ImpMaxCount);
            m_ImpAddrGP2 = m_ImpAddrGP  + (uint)(kSizeOfFloat4 * m_ImpMaxCount);
            m_ImpAddrCol = m_ImpAddrGP2 + (uint)(kSizeOfFloat4 * m_ImpMaxCount);

            var metadata = new NativeArray<MetadataValue>(6, Allocator.Temp);
            metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"),   Value = 0x80000000 | m_ImpAddrO2W };
            metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"),   Value = 0u }; // shared identity
            metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousM"), Value = 0u }; // shared identity (no motion)
            metadata[3] = new MetadataValue { NameID = Shader.PropertyToID("_GrassParams"),          Value = 0x80000000 | m_ImpAddrGP };
            metadata[4] = new MetadataValue { NameID = Shader.PropertyToID("_GrassParams2"),         Value = 0x80000000 | m_ImpAddrGP2 };
            metadata[5] = new MetadataValue { NameID = Shader.PropertyToID("_BaseColor"),            Value = 0x80000000 | m_ImpAddrCol };

            m_ImpInstanceData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bufferCount, sizeof(int));
            m_ImpInstanceData.SetData(new PackedMatrix[1] { new PackedMatrix(Matrix4x4.identity) }, 0, 0, 1); // head identity

            m_ImpBatchID = m_ImpBRG.AddBatch(metadata, m_ImpInstanceData.bufferHandle);
            metadata.Dispose();

            // Size + tint are written PER-CARD (species-driven) in FillImpostorPositions. Here we only push the
            // surface globals: a sane size fallback, the soft-normal lift, and the Step 4 crossfade band (camera
            // distances): [blade-melt start, max blade dist] = fade IN (cross-dissolve with the melting blades),
            // [outer 60%, impostor outer] = fade OUT (toward the L2 tint ground). Re-pushed on rebuild.
            float fbScale = Mathf.Max(M.impostorCardScale, 0.1f);
            Shader.SetGlobalFloat("_GrassImpostorCardWidth",  Mathf.Max(M.impostorTuftRadius * 2.5f * fbScale, 0.1f));
            Shader.SetGlobalFloat("_GrassImpostorCardHeight", Mathf.Max(M.impostorTuftHeight * fbScale, 0.1f));
            Shader.SetGlobalFloat("_GrassImpostorNormalUp", 0.4f);
            // Cards reach FULL opacity right as the blade dither STARTS (first ~10% of the melt band, i.e. the
            // last ring): the tall fading blades occlude them until they melt, revealing the already-solid cards
            // underneath. So the cards are present the WHOLE time the blades fade -> no hole (a shrinking blade
            // covers far less than its height fraction, so a late/1:1 cross-dissolve leaves a gap mid-band).
            // Fade-in band: cards (placed at nearSkip) start half a crossfade-band BEFORE the blade-melt start
            // and reach 100% AT it (xfadeEnd = crossfadeStart) -> they fade in hidden under the full blades, then
            // are solid the moment the blades begin to disappear. Fade-out [outerStart, outer] toward the L2 tint.
            float xfadeEnd   = M.maxBladeDistance - M.CrossfadeBandM;             // = blade-melt start: cards 100% here
            float xfadeStart = Mathf.Max(xfadeEnd - M.CrossfadeBandM * 0.5f, 0f); // cards fade in over this band BEFORE it
            float outerStart = M.maxBladeDistance + (m_ImpHalf - M.maxBladeDistance) * 0.6f;
            Shader.SetGlobalVector("_GrassImpBand", new Vector4(xfadeStart, xfadeEnd, outerStart, m_ImpHalf));

            FillImpostorPositions(); // fills positions + per-card size/tint
        }

        // World-pinned cards in a window around the camera, snapped to the lattice (no shimmer). Re-filled when
        // the camera crosses a cell (Tick). Only the o2w (positions) changes; size/tint stay constant.
        void FillImpostorPositions()
        {
            if (m_ImpInstanceData == null || m_ImpMaxCount == 0) return;
            Camera cam = GetWindowCamera();
            Vector3 c = cam != null ? cam.transform.position : OriginPos();
            float step = Mathf.Max(M.impostorSpacing, 1f);
            float baseX = Mathf.Floor(c.x / step) * step;   // snap to the world card lattice -> world-pinned
            float baseZ = Mathf.Floor(c.z / step) * step;
            // Cards BEGIN half a crossfade-band BEFORE the blade-melt start, so they fade in (hidden under the
            // still-full blades) and reach 100% exactly AT the blade-melt start -> solid the moment the blades go.
            float nearSkip = Mathf.Max(M.maxBladeDistance - M.CrossfadeBandM * 1.5f, 10f);
            float scale = Mathf.Max(M.impostorCardScale, 0.1f);     // card size multiplier (× the baked tuft, aspect kept)
            float cardH = Mathf.Max(M.impostorTuftHeight * scale, 0.1f);
            float cardW = Mathf.Max(M.impostorTuftRadius * 2.5f * scale, 0.1f);
            // 3D distance band (the blade melt is 3D): the cards begin EXACTLY where the blades START dithering
            // out (3D crossfadeStart) at any camera height -> present + full the moment the blades begin to go,
            // no gap. camH = camera height above the ground (a horizontal distance maps to a larger 3D one).
            Terrain t0 = (m_Tiles != null && m_Tiles.Length > 0) ? m_Tiles[0] : null;
            float camGroundY = t0 != null ? t0.transform.position.y + t0.SampleHeight(c) : c.y;
            float camH = Mathf.Max(c.y - camGroundY, 0f);

            if (m_ImpPositions == null || m_ImpPositions.Length != m_ImpMaxCount) m_ImpPositions = new Vector3[m_ImpMaxCount];
            var o2w = new PackedMatrix[m_ImpMaxCount];
            var gp  = new Vector4[m_ImpMaxCount];
            var gp2 = new Vector4[m_ImpMaxCount];
            var col = new Vector4[m_ImpMaxCount];
            float jitter = step * 0.85f;
            int n = 0;
            for (float dx = -m_ImpHalf; dx <= m_ImpHalf && n < m_ImpMaxCount; dx += step)
            for (float dz = -m_ImpHalf; dz <= m_ImpHalf && n < m_ImpMaxCount; dz += step)
            {
                float r3d = Mathf.Sqrt(dx * dx + dz * dz + camH * camH); // 3D distance (matches the blade melt)
                if (r3d < nearSkip || r3d > m_ImpHalf) continue;
                // Deterministic per-lattice-cell jitter -> breaks the regular grid look, stays world-pinned (no shimmer).
                int cx = Mathf.RoundToInt((baseX + dx) / step), cz = Mathf.RoundToInt((baseZ + dz) / step);
                uint hh = (uint)(cx * 73856093) ^ (uint)(cz * 19349663);
                hh ^= hh >> 13; hh *= 2654435761u; hh ^= hh >> 16;
                float x = baseX + dx + (((hh & 0xffffu) / 65535f) - 0.5f) * jitter;
                float z = baseZ + dz + ((((hh >> 16) & 0xffffu) / 65535f) - 0.5f) * jitter;
                if (!SampleGrassSpecies(x, z, out int sp, out float groundY)) continue; // grass only
                var wp = new Vector3(x, groundY, z);
                o2w[n] = new PackedMatrix(Matrix4x4.Translate(wp));
                m_ImpPositions[n] = wp;                 // cache for the CPU frustum cull
                gp[n]  = new Vector4(cardH, 0f, 0f, 0f);
                gp2[n] = new Vector4(cardW, 0f, 0f, 0f);
                Color lin = M.species[sp].color.linear;
                col[n] = new Vector4(lin.r, lin.g, lin.b, 1f);
                n++;
            }
            m_ImpCount = n;
            if (n > 0)
            {
                m_ImpInstanceData.SetData(o2w, 0, (int)(m_ImpAddrO2W / kSizeOfPackedMatrix), n);
                m_ImpInstanceData.SetData(gp,  0, (int)(m_ImpAddrGP  / kSizeOfFloat4), n);
                m_ImpInstanceData.SetData(gp2, 0, (int)(m_ImpAddrGP2 / kSizeOfFloat4), n);
                m_ImpInstanceData.SetData(col, 0, (int)(m_ImpAddrCol / kSizeOfFloat4), n);
            }
        }

        // CPU grass placement sample: is there grass at world (x,z), and which species? Uses the cached terrain
        // splat + the per-species per-layer density (the SAME presence logic as the blade scatter, on CPU since
        // the impostor field is sparse). Returns the ground Y + the strongest-present species.
        bool SampleGrassSpecies(float x, float z, out int speciesIdx, out float groundY)
        {
            speciesIdx = -1; groundY = OriginPos().y;
            if (m_Tiles == null || m_AlphaCPU == null) return false;
            for (int s = 0; s < m_Tiles.Length && s < m_AlphaCPU.Length; s++)
            {
                var t = m_Tiles[s]; if (t == null) continue;
                var td = t.terrainData;
                Vector3 p = t.transform.position;
                float u = (x - p.x) / td.size.x, v = (z - p.z) / td.size.z;
                if (u < 0f || u >= 1f || v < 0f || v >= 1f) continue;
                var amap = m_AlphaCPU[s];
                if (amap == null) return false;
                int ah = amap.GetLength(0), aw = amap.GetLength(1), layers = amap.GetLength(2);
                int ax = Mathf.Clamp((int)(u * aw), 0, aw - 1);
                int az = Mathf.Clamp((int)(v * ah), 0, ah - 1);
                float best = 0f; int bestS = -1;
                for (int sp = 0; sp < m_SpeciesCount && sp < M.species.Length; sp++)
                {
                    var spec = M.species[sp];
                    if (spec == null || spec.layerDensity == null) continue;
                    float pres = 0f;
                    for (int L = 0; L < layers && L < 8; L++) // LayerDensity is a fixed 8-layer indexer (cf. blades)
                        pres += Mathf.Clamp01(spec.layerDensity[L]) * amap[az, ax, L];
                    if (pres > best) { best = pres; bestS = sp; }
                }
                if (best > 0.1f)
                {
                    groundY = p.y + t.SampleHeight(new Vector3(x, 0f, z)); // only sample Y where a card lands
                    speciesIdx = bestS; return true;
                }
                return false; // inside this tile but not enough grass here
            }
            return false;
        }

        // Étape A inspection: bake + save the impostor to the desktop as a PNG.
        void BakeImpostorPreview()
        {
            var rt = BakeImpostor();
            var prev = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            string path = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "GrassImpostor.png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            if (Application.isPlaying) { UnityEngine.Object.Destroy(tex); } else { UnityEngine.Object.DestroyImmediate(tex); }
            rt.Release();
            Debug.Log($"[GrassImpostor] bake {rt.width}x{rt.height} ({M.impostorBladeCount} brins) sauvé → {path}");
        }

        void Teardown(bool keepTerrainBake)
        {
            m_Setup = false;
            m_ScatterKernel = -1;
            m_CullKernel = -1;
            if (m_InstanceData != null) { m_InstanceData.Dispose(); m_InstanceData = null; }
            if (m_ArgsBuffer != null) { m_ArgsBuffer.Dispose(); m_ArgsBuffer = null; }
            if (m_VisibleIndexBuffer != null) { m_VisibleIndexBuffer.Dispose(); m_VisibleIndexBuffer = null; }
            if (m_SourceMeta != null) { m_SourceMeta.Dispose(); m_SourceMeta = null; }
            if (m_SpeciesBuffer != null) { m_SpeciesBuffer.Dispose(); m_SpeciesBuffer = null; }
            if (m_BRG != null) { m_BRG.Dispose(); m_BRG = null; }
            if (m_ImpBRG != null) { m_ImpBRG.Dispose(); m_ImpBRG = null; }
            if (m_ImpInstanceData != null) { m_ImpInstanceData.Dispose(); m_ImpInstanceData = null; }
            m_ImpCount = 0; m_ImpMaxCount = 0;
            m_ImpLastCellX = int.MinValue; m_ImpLastCellZ = int.MinValue;
            // Per-species mesh ID/ref arrays just point at the shared blade or the user's assets — drop them.
            m_NearID = null; m_FarID = null; m_NearMesh = null; m_FarMesh = null;
            bool lowShared = m_GeneratedMeshLow == m_GeneratedMesh;
            DestroyGeneratedMesh(ref m_GeneratedMesh);   // only the procedural blade is owned
            if (lowShared) m_GeneratedMeshLow = null;
            else DestroyGeneratedMesh(ref m_GeneratedMeshLow);
            if (m_TilesBuffer != null) { m_TilesBuffer.Dispose(); m_TilesBuffer = null; }
            if (!keepTerrainBake)
            {
                DestroyTex(ref m_HeightmapArray);
                DestroyTex(ref m_ControlArray0);
                DestroyTex(ref m_ControlArray1);
                m_Tiles = null;
                m_BakedPositions = null;
                m_AlphaCPU = null;
                // Occlusion capture is camera state, independent of the grass build — only drop it on a FULL
                // teardown. Keeping it across a keep-bake rebuild (any inspector tweak, incl. toggling
                // Log Gpu Counts) means the capture/validity survive, so the cull & the A/B readback still see it.
                if (m_OccSubscribed) { RenderPipelineManager.endCameraRendering -= OnEndCameraRendering; m_OccSubscribed = false; }
                if (m_OccHiZ != null) { m_OccHiZ.Release(); m_OccHiZ = null; }
                m_OccHiZValid = false; m_CaptureKernel = -1;
                if (m_ImpostorTex != null) { m_ImpostorTex.Release(); m_ImpostorTex = null; }
                m_ImpostorHash = -1;
                if (m_ImpostorMat != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(m_ImpostorMat);
                    else UnityEngine.Object.DestroyImmediate(m_ImpostorMat);
                    m_ImpostorMat = null;
                }
                DestroyGeneratedMesh(ref m_ImpostorQuad);
            }
            m_LastOriginCellX = int.MinValue;
            m_LastOriginCellZ = int.MinValue;
        }

        void DestroyGeneratedMesh(ref Mesh mesh)
        {
            if (mesh != null && (M == null || mesh != M.meshOverride))
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                else UnityEngine.Object.DestroyImmediate(mesh);
            }
            mesh = null;
        }

        static void DestroyTex(ref Texture2DArray tex)
        {
            if (tex == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(tex);
            else UnityEngine.Object.DestroyImmediate(tex);
            tex = null;
        }

        // Reference position for the flat fallback Y and the non-camera-centered window center.
        Vector3 OriginPos() => C != null ? C.transform.position : Vector3.zero;

        // ---------------------------------------------------------------------------------
        // Multi-terrain bake (from ctx.terrains — the controller resolves the terrains sharing the material).
        void BuildTerrainGrid(TerrainApplyContext ctx)
        {
            var valid = new List<Terrain>();
            if (ctx != null && ctx.terrains != null)
                foreach (var t in ctx.terrains)
                    if (t != null && t.terrainData != null) valid.Add(t);

            // Bake cache: the heightmap/control arrays survive inspector tweaks; only re-bake when
            // the terrain set (refs, resolutions, positions) actually changed.
            bool reuseBake = SameBakeSet(valid);

            m_Tiles = valid.ToArray();
            if (m_Tiles.Length == 0) return; // _UseTerrain stays 0 -> flat grid

            var td0 = m_Tiles[0].terrainData;
            m_TileSize = new Vector2(td0.size.x, td0.size.z);
            float originX = float.MaxValue, originZ = float.MaxValue;
            foreach (var t in m_Tiles)
            {
                Vector3 p = t.transform.position;
                if (p.x < originX) originX = p.x;
                if (p.z < originZ) originZ = p.z;
            }
            m_GridOrigin = new Vector2(originX, originZ);

            int cols = 1, rows = 1;
            foreach (var t in m_Tiles)
            {
                Vector3 p = t.transform.position;
                int col = Mathf.RoundToInt((p.x - originX) / m_TileSize.x);
                int row = Mathf.RoundToInt((p.z - originZ) / m_TileSize.y);
                cols = Mathf.Max(cols, col + 1);
                rows = Mathf.Max(rows, row + 1);
            }
            m_GridCols = cols;
            m_GridRows = rows;

            if (!reuseBake)
            {
                DestroyTex(ref m_HeightmapArray);
                DestroyTex(ref m_ControlArray0);
                DestroyTex(ref m_ControlArray1);
                BakeHeightmapArray();
                BakeControlArrays();
                CacheAlphamapsCPU();
                m_BakedPositions = new Vector3[m_Tiles.Length];
                for (int i = 0; i < m_Tiles.Length; i++) m_BakedPositions[i] = m_Tiles[i].transform.position;
            }

            // Always rebuilt: the grass-layer mask may have changed (cheap, no texture IO).
            BuildTilesBuffer();
        }

        bool SameBakeSet(List<Terrain> valid)
        {
            if (m_Tiles == null || m_HeightmapArray == null || m_ControlArray0 == null) return false;
            if (m_BakedPositions == null || m_BakedPositions.Length != m_Tiles.Length) return false;
            if (valid.Count == 0 || m_Tiles.Length != valid.Count) return false;
            for (int i = 0; i < m_Tiles.Length; i++)
            {
                if (m_Tiles[i] != valid[i] || valid[i].terrainData == null) return false;
                if (m_BakedPositions[i] != valid[i].transform.position) return false;
            }
            if (m_HeightmapArray.width != valid[0].terrainData.heightmapResolution) return false;
            if (m_ControlArray0.width != valid[0].terrainData.alphamapResolution) return false;
            return true;
        }

        void BakeHeightmapArray()
        {
            int res = m_Tiles[0].terrainData.heightmapResolution;
            m_HeightmapArray = new Texture2DArray(res, res, m_Tiles.Length, TextureFormat.RFloat, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GrassHeightmapArray"
            };

            var data = new float[res * res];
            for (int s = 0; s < m_Tiles.Length; s++)
            {
                var td = m_Tiles[s].terrainData;
                int r = Mathf.Min(td.heightmapResolution, res);
                float[,] heights = td.GetHeights(0, 0, r, r);
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                        data[y * res + x] = (y < r && x < r) ? heights[y, x] : 0f;
                m_HeightmapArray.SetPixelData(data, 0, s);
            }
            m_HeightmapArray.Apply(false, false);
        }

        void BakeControlArrays()
        {
            int ares = m_Tiles[0].terrainData.alphamapResolution;
            m_ControlArray0 = new Texture2DArray(ares, ares, m_Tiles.Length, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GrassControlArray0"
            };
            m_ControlArray1 = new Texture2DArray(ares, ares, m_Tiles.Length, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GrassControlArray1"
            };

            for (int s = 0; s < m_Tiles.Length; s++)
            {
                var maps = m_Tiles[s].terrainData.alphamapTextures;
                if (maps.Length > 0 && maps[0].width == ares && maps[0].height == ares)
                    Graphics.CopyTexture(maps[0], 0, 0, m_ControlArray0, s, 0);
                if (maps.Length > 1 && maps[1].width == ares && maps[1].height == ares)
                    Graphics.CopyTexture(maps[1], 0, 0, m_ControlArray1, s, 0);
            }
        }

        // CPU copy of each tile's splat alphamap (GetAlphamaps) for the impostor's CPU grass placement.
        void CacheAlphamapsCPU()
        {
            m_AlphaCPU = new float[m_Tiles.Length][,,];
            for (int s = 0; s < m_Tiles.Length; s++)
            {
                var td = m_Tiles[s].terrainData;
                m_AlphaCPU[s] = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
            }
        }

        void BuildTilesBuffer()
        {
            int cellCount = m_GridCols * m_GridRows;
            var tiles = new TerrainTileData[cellCount];
            for (int i = 0; i < cellCount; i++) tiles[i].slice = -1;

            for (int s = 0; s < m_Tiles.Length; s++)
            {
                var t = m_Tiles[s];
                var td = t.terrainData;
                Vector3 p = t.transform.position;
                int col = Mathf.RoundToInt((p.x - m_GridOrigin.x) / m_TileSize.x);
                int row = Mathf.RoundToInt((p.z - m_GridOrigin.y) / m_TileSize.y);
                int cell = col + row * m_GridCols;
                if (cell < 0 || cell >= cellCount) continue;

                tiles[cell] = new TerrainTileData
                {
                    worldPosXZ = new Vector2(p.x, p.z),
                    posY       = p.y,
                    sizeY      = td.size.y,
                    sizeXZ     = new Vector2(td.size.x, td.size.z),
                    slice      = s,
                    pad        = 0,
                };
            }

            if (m_TilesBuffer != null) m_TilesBuffer.Dispose();
            m_TilesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, kTileStride);
            m_TilesBuffer.SetData(tiles);
        }

        // Build the species table (StructuredBuffer<GrassSpecies>). Placement is fully species-driven:
        // each entry carries a per-layer density (density0123/4567). Called only when the list has ≥1
        // species (Build/EnsureBuilt gate on that — no default grass). Also tracks the tallest blade
        // for the frustum-cull margin.
        void BuildSpeciesBuffer()
        {
            if (m_SpeciesBuffer != null) { m_SpeciesBuffer.Dispose(); m_SpeciesBuffer = null; }
            var list = M.species;
            m_SpeciesCount = Mathf.Min(list.Length, kMaxSpecies); // ≥1 (Build gate), capped for binning
            if (list.Length > kMaxSpecies)
                Debug.LogWarning($"GrassBlades: {list.Length} species, capped to {kMaxSpecies} (extra ignored).");
            var data = new SpeciesData[m_SpeciesCount];
            m_MaxBladeHeight = 0.05f;

            for (int i = 0; i < m_SpeciesCount; i++)
            {
                SpeciesEntry s = list[i];
                Color lin = s.color.linear;
                Vector4 d0 = Vector4.zero, d1 = Vector4.zero;
                if (s.layerDensity != null)
                    for (int c = 0; c < 8; c++)
                    {
                        float d = Mathf.Clamp01(s.layerDensity[c]);
                        if (c < 4) d0[c] = d; else d1[c - 4] = d;
                    }
                float kind = (s.mesh != null) ? 1f : 0f; // 1 = custom mesh, 0 = procedural blade
                data[i] = new SpeciesData
                {
                    baseColor = new Vector4(lin.r, lin.g, lin.b, 1f),
                    shape     = new Vector4(s.height, s.heightRandom, s.width, s.bend),
                    shape2    = new Vector4(s.tilt, s.colorVariation, kind, 0f),
                    density0123 = d0,
                    density4567 = d1,
                };
                m_MaxBladeHeight = Mathf.Max(m_MaxBladeHeight, s.height * (1f + s.heightRandom));
            }
            m_SpeciesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_SpeciesCount, kSpeciesStride);
            m_SpeciesBuffer.SetData(data);
        }

        // ---------------------------------------------------------------------------------
        void AllocateAndPopulate(TerrainApplyContext ctx)
        {
            // Clipmap sizing: per-level resolution from cellPixels & screen height (resolution-
            // independent screen density), number of levels from the reach (maxBladeDistance). Capacity
            // is FIXED for the batch lifetime; rebuild (dirty) on a param or screen-resolution change.
            ComputeClipmap(out m_ClipRes, out m_NumLevels, out m_BuiltScreenH);
            m_Capacity      = m_NumLevels * m_ClipRes * m_ClipRes;
            m_InstanceCount = m_Capacity; // every (level, cell) gets a thread; holed/culled ones don't append

            int bufferCount = BufferCountForInstances(kBytesPerInstance, m_Capacity, kExtraBytes);

            // Compression step B: NO per-instance matrices at all. unity_ObjectToWorld / WorldToObject /
            // MatrixPreviousM are a single SHARED identity (constant metadata, offset 0). The per-instance
            // transform is the compact _GrassXform (16 B = abs pos + yaw); the vertex hook rebuilds it and
            // outputs camera-relative world positions + world normals. (96 -> 64 B/instance.)
            m_AddrXform = (uint)(kSizeOfPackedMatrix * 2);                            // 96 (head holds identity)
            m_AddrCol   = m_AddrXform + (uint)(kSizeOfFloat4 * m_Capacity);
            m_AddrGP    = m_AddrCol   + (uint)(kSizeOfFloat4 * m_Capacity);
            m_AddrGP2   = m_AddrGP    + (uint)(kSizeOfFloat4 * m_Capacity);
            const uint kIdentityAddr = 0u; // shared identity float3x4 at the buffer head (offset 0)

            var metadata = new NativeArray<MetadataValue>(7, Allocator.Temp);
            // The 3 builtin matrices are CONSTANT (no 0x80000000) -> all instances read the shared identity.
            metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"),   Value = kIdentityAddr };
            metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"),   Value = kIdentityAddr };
            metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousM"), Value = kIdentityAddr };
            metadata[3] = new MetadataValue { NameID = Shader.PropertyToID("_GrassXform"),           Value = 0x80000000 | m_AddrXform };
            metadata[4] = new MetadataValue { NameID = Shader.PropertyToID("_BaseColor"),            Value = 0x80000000 | m_AddrCol };
            metadata[5] = new MetadataValue { NameID = Shader.PropertyToID("_GrassParams"),          Value = 0x80000000 | m_AddrGP };
            metadata[6] = new MetadataValue { NameID = Shader.PropertyToID("_GrassParams2"),         Value = 0x80000000 | m_AddrGP2 };
            m_InstanceData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bufferCount, sizeof(int));
            // Head: shared identity float3x4 at offset 0 (read by unity_WorldToObject for all instances).
            m_InstanceData.SetData(new PackedMatrix[1] { new PackedMatrix(Matrix4x4.identity) }, 0, 0, 1);
            m_BatchID = m_BRG.AddBatch(metadata, m_InstanceData.bufferHandle);
            metadata.Dispose();

            // Per-frame visible-index lists: one section per (species, band) — section = species*3 + band
            // (0=NEAR,1=FAR,2=SHADOW), each m_Capacity wide. The CullAndLod pass rebuilds them every frame.
            int sections = m_SpeciesCount * 3;
            m_VisibleIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, sections * m_Capacity, sizeof(int));

            // Scatter meta ([0]=cull groupsX, [1]=1, [2]=1, [3]=source count), GPU-written.
            m_SourceMeta = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 4, sizeof(uint));

            // (species*3) IndirectDrawIndexedArgs: per species NEAR (detailed) / FAR (low) / SHADOW (low).
            m_ArgsData = new uint[sections * 5];
            m_ArgsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, sections * 5, sizeof(uint));
            for (int s = 0; s < m_SpeciesCount; s++)
            {
                FillArgsTemplate(m_NearMesh[s], (s * 3 + 0) * 5); // NEAR
                FillArgsTemplate(m_FarMesh[s],  (s * 3 + 1) * 5); // FAR
                FillArgsTemplate(m_FarMesh[s],  (s * 3 + 2) * 5); // SHADOW
            }

            if (M.useCompute && M.scatterCompute != null)
            {
                DispatchScatter();
                DispatchCull();
            }
            else
                FillCPU();
        }

        void FillArgsTemplate(Mesh mesh, int baseIdx)
        {
            m_ArgsData[baseIdx + 0] = mesh.GetIndexCount(0);
            m_ArgsData[baseIdx + 1] = 0; // instanceCount: GPU-written (cull) / CPU (FillCPU)
            m_ArgsData[baseIdx + 2] = mesh.GetIndexStart(0);
            m_ArgsData[baseIdx + 3] = mesh.GetBaseVertex(0);
            m_ArgsData[baseIdx + 4] = 0;
        }

        // Scatter the SOURCE population (all clipmap levels). Runs when the camera crosses a level-0
        // cell, not per frame. Fixed thread count (m_Capacity); holed/empty cells just don't append.
        void DispatchScatter()
        {
            var cs = M.scatterCompute;
            int k = m_ScatterKernel >= 0 ? m_ScatterKernel : (m_ScatterKernel = cs.FindKernel("ScatterGrid"));
            cs.SetBuffer(k, "_InstanceBuffer", m_InstanceData);

            // Clipmap center = camera XZ (controller XZ in non-camera fallback). Record the level-0
            // cell so Tick re-dispatches only when the camera crosses a finest-level cell.
            Vector2 centerXZ = ClipCenterXZ();
            float sp0 = Mathf.Max(M.spacing, 0.01f);
            m_LastOriginCellX = Mathf.FloorToInt(centerXZ.x / sp0);
            m_LastOriginCellZ = Mathf.FloorToInt(centerXZ.y / sp0);

            cs.SetInt("_InstanceCount", m_InstanceCount);
            cs.SetInt("_GridResolution", m_ClipRes);
            cs.SetInt("_NumLevels", m_NumLevels);
            cs.SetFloat("_Spacing", M.spacing);
            cs.SetFloat("_PositionJitter", M.positionJitter);
            cs.SetVector("_Origin", OriginPos()); // only .y (flat fallback) is read
            cs.SetVector("_ClipCamXZ", new Vector4(centerXZ.x, centerXZ.y, 0f, 0f));
            cs.SetFloat("_LodTransition", M.lodTransition);
            // Blade shape + color are now PER-SPECIES (in the _Species table) — no global blade uniforms.

            cs.SetInt("_AddrXform", (int)m_AddrXform);
            cs.SetInt("_AddrCol",   (int)m_AddrCol);
            cs.SetInt("_AddrGP",    (int)m_AddrGP);
            cs.SetInt("_AddrGP2",   (int)m_AddrGP2);

            // Species table — placement is fully species-driven (system only runs with ≥1 species).
            if (m_SpeciesBuffer != null) cs.SetBuffer(k, "_Species", m_SpeciesBuffer);
            cs.SetInt("_SpeciesCount", m_SpeciesCount);
            cs.SetFloat("_Density", M.density); // global density multiplier

            bool hasTerrain = m_Tiles != null && m_Tiles.Length > 0 &&
                              m_HeightmapArray != null && m_TilesBuffer != null;
            cs.SetFloat("_UseTerrain", hasTerrain ? 1f : 0f);
            if (hasTerrain)
            {
                cs.SetTexture(k, "_HeightmapArray", m_HeightmapArray);
                cs.SetTexture(k, "_ControlArray0",  m_ControlArray0);
                cs.SetTexture(k, "_ControlArray1",  m_ControlArray1);
                cs.SetBuffer(k, "_Tiles", m_TilesBuffer);
                cs.SetVector("_GridOrigin", new Vector4(m_GridOrigin.x, m_GridOrigin.y, 0f, 0f));
                cs.SetVector("_TileSize",   new Vector4(m_TileSize.x,   m_TileSize.y,   0f, 0f));
                cs.SetInt("_GridCols", m_GridCols);
                cs.SetInt("_GridRows", m_GridRows);
            }

            cs.SetFloat("_UseClumping", M.useClumping ? 1f : 0f);
            if (M.useClumping)
            {
                cs.SetFloat("_ClumpSize", M.clumpSize);
                cs.SetFloat("_ClumpHeightVariation", M.clumpHeightVariation);
                cs.SetFloat("_ClumpPullStrength", M.clumpPullStrength);
                cs.SetFloat("_ClumpDirectionStrength", M.clumpDirectionStrength);
                cs.SetFloat("_ClumpColorVariation", M.clumpColorVariation);
            }

            // Reset the source meta ({groupsX, 1, 1, count}); the dispatch rebuilds it GPU-side.
            m_SourceMeta.SetData(s_MetaReset);
            cs.SetBuffer(k, "_SourceMeta", m_SourceMeta);

            int groups = (m_InstanceCount + 63) / 64;
            cs.Dispatch(k, groups, 1, 1);

            if (m_BRG != null)
            {
                // Bounds must contain the outermost clipmap level (camera-centered): half-extent =
                // clipRes/2 · baseSpacing · 2^(numLevels-1).
                float radius = (m_ClipRes * 0.5f) * sp0 * (1 << (m_NumLevels - 1)) + 50f;
                Vector3 cc = new Vector3(m_LastOriginCellX * sp0, OriginPos().y, m_LastOriginCellZ * sp0);
                m_BRG.SetGlobalBounds(new Bounds(cc, new Vector3(2f * radius, 4000f, 2f * radius)));
            }
        }

        // Per-frame cull/LOD: rebuild the near/far/shadow index lists + their GPU instanceCounts.
        // Cheap (1 position load per source blade), dispatched indirect from the source count. Runs every
        // frame: frustum cull + Hi-Z occlusion are both always on (occlusion needs the depth re-evaluated
        // each frame anyway). Occlusion engages once a camera-matched capture exists (1-frame-late).
        void DispatchCull()
        {
            if (!M.useCompute || M.scatterCompute == null) return;
            if (m_InstanceData == null || m_ArgsBuffer == null || m_SourceMeta == null) return;

            var cs = M.scatterCompute;
            Camera cam = GetWindowCamera();
            int k = m_CullKernel >= 0 ? m_CullKernel : (m_CullKernel = cs.FindKernel("CullAndLod"));

            Vector3 camPos = OriginPos();
            float useFrustum = 0f;
            if (cam != null)
            {
                camPos = cam.transform.position;
                GeometryUtility.CalculateFrustumPlanes(cam, m_PlaneCache);
                // Game-camera VP + pixel size -> the impostor surface projects fragments into the GAME screen
                // space for its dither, so the dissolve pattern is stable relative to the game camera (not the
                // rendering/Scene view).
                Shader.SetGlobalMatrix("_GrassCullCamVP", GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix);
                Shader.SetGlobalVector("_GrassCullCamScreen", new Vector4(cam.pixelWidth, cam.pixelHeight, 0f, 0f));
                // Margin: config + tallest blade, so a base just offscreen still shows its tip.
                float margin = M.frustumMargin + m_MaxBladeHeight; // tallest species blade as slack
                for (int i = 0; i < 6; i++)
                {
                    Vector3 n = m_PlaneCache[i].normal;
                    m_PlaneVecs[i] = new Vector4(n.x, n.y, n.z, m_PlaneCache[i].distance + margin);
                }
                useFrustum = 1f;
            }

            // Zero every section's instanceCount (the mesh fields stay prefilled).
            for (int sec = 0; sec < m_SpeciesCount * 3; sec++) m_ArgsData[sec * 5 + 1] = 0u;
            m_ArgsBuffer.SetData(m_ArgsData);

            cs.SetInt("_AddrXform", (int)m_AddrXform); // cull reads blade positions from _GrassXform.xyz
            cs.SetInt("_AddrGP2", (int)m_AddrGP2);     // ...and the species index from gp2.z
            cs.SetInt("_NumSpecies", m_SpeciesCount);
            cs.SetBuffer(k, "_InstanceBufferR", m_InstanceData);
            cs.SetBuffer(k, "_VisibleIndices", m_VisibleIndexBuffer);
            cs.SetBuffer(k, "_DrawArgs", m_ArgsBuffer);
            cs.SetBuffer(k, "_SourceMeta", m_SourceMeta);
            cs.SetVectorArray("_FrustumPlanes", m_PlaneVecs);
            cs.SetVector("_CullCamPos", camPos);
            // The vertex shader's melt/LOD distance must use the CULL camera (not the render camera), so a
            // cull-kept blade never melts to height 0 (degenerate Bézier -> NaN normal -> black screen) when
            // cullFromMainCamera makes the render cam differ from the cull cam.
            Shader.SetGlobalVector("_GrassCullCamPos", camPos);
            // Ring dither applies only to the inner n-2 clipmap rings (stable radii); the outer 2 hand off to the
            // distance-based melt. Threshold = the second-outermost ring's outer radius (× 0.75 for a clean cut
            // between it and the third-outermost). 1<<max(numLevels-2,0) so numLevels<=2 -> no ring dither.
            Shader.SetGlobalFloat("_GrassRingDitherMax",
                (m_ClipRes * 0.5f) * Mathf.Max(M.spacing, 0.01f) * (1 << Mathf.Max(m_NumLevels - 2, 0)) * 0.75f);
            cs.SetFloat("_UseFrustum", useFrustum);
            cs.SetFloat("_MaxBladeDist", M.maxBladeDistance);
            cs.SetFloat("_BandSplitDist", M.BandSplitM);
            cs.SetFloat("_ShadowDist", M.ShadowDistanceM);
            cs.SetFloat("_ShadowFadeBand", M.ShadowFadeBandM);
            cs.SetFloat("_ShadowDensity", M.shadowDensity);
            cs.SetFloat("_ShadowEnabled", M.shadowCascades > 0 ? 1f : 0f);
            cs.SetFloat("_ShadowFrustumCull", M.shadowFrustumCull ? 1f : 0f);
            cs.SetInt("_SectionCapacity", m_Capacity); // section stride = fixed buffer capacity

            // Hi-Z occlusion: cull against the Hi-Z captured for this camera at endCameraRendering
            // (1-frame-late but camera-matched). Engages once a valid capture exists.
            bool occ = m_OccHiZValid && m_OccHiZ != null;
            cs.SetFloat("_UseOcclusion", occ ? 1f : 0f);
            cs.SetTexture(k, "_OccHiZ", occ ? (Texture)m_OccHiZ : Texture2D.blackTexture);
            if (occ)
            {
                cs.SetMatrix("_OccVP", m_OccHiZVP);    // the VP that produced the captured depth
                cs.SetVector("_OccNearFar", m_OccHiZNearFar);
                cs.SetInts("_OccHiZSize", m_OccHiZ.width, m_OccHiZ.height); // cull indexes the Hi-Z by pixel
                cs.SetFloat("_OccBias", M.occlusionBias);
                cs.SetFloat("_OcclusionCullsShadows", 1f);  // always drop occluded in-frustum shadow casters
                cs.SetFloat("_OccLift", m_MaxBladeHeight);  // test the blade tip (visible silhouette)
                cs.SetFloat("_OccBorder", 0.01f);           // inset for the edge-clamp of off-screen (margin) blades
            }

            cs.DispatchIndirect(k, m_SourceMeta, 0);
        }

        // Camera the scatter + cull are computed from. Camera.main by default (so you can fly the editor
        // camera and inspect the Main-Camera culling from outside); only follows the Scene view in edit
        // mode when cullFromMainCamera is off (authoring preview). Falls back to Scene view if no main cam.
        Camera GetWindowCamera()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                bool useMain = M != null && M.cullFromMainCamera;
                if (useMain && Camera.main != null) return Camera.main;
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null) return sv.camera;
            }
#endif
            return Camera.main;
        }

        // Hi-Z occlusion capture — at endCameraRendering for the TARGET camera, copy its just-rendered HDRP
        // depth into our own tight Hi-Z (conservative farthest-in-footprint reduction) and store its VP.
        // This guarantees depth + VP + camera all match (reading the depth global directly did not, with
        // several cameras rendering per frame), and isolates all HDRP depth quirks to the CaptureHiZ kernel.
        void OnEndCameraRendering(ScriptableRenderContext _, Camera cam)
        {
            if (!m_Setup || M == null) return;
            if (cam != GetWindowCamera()) return;             // only the camera we cull against
            var cs = M.scatterCompute;
            if (cs == null || m_OccHiZ == null) return;       // Hi-Z is allocated in Update (EnsureOccHiZ)
            if (m_CaptureKernel < 0) m_CaptureKernel = cs.FindKernel("CaptureHiZ");

            int w = m_OccHiZ.width, h = m_OccHiZ.height;
            cs.SetTextureFromGlobal(m_CaptureKernel, "_CamDepth", "_CameraDepthTexture");
            // HDRP depth pyramid mip-0 size = camera render resolution; we Load by pixel (no _RTHandleScale).
            cs.SetVector("_CamScreenSize", new Vector4(cam.pixelWidth, cam.pixelHeight, 0f, 0f));
            cs.SetTexture(m_CaptureKernel, "_OccHiZOut", m_OccHiZ);
            cs.SetInts("_OccHiZSize", w, h);
            cs.Dispatch(m_CaptureKernel, (w + 7) / 8, (h + 7) / 8, 1);

            // Match the cull's reconstruction: renderIntoTexture:false -> uv = ndc*0.5+0.5 (bottom-left),
            // the convention CaptureHiZ used to fetch the depth, so cull & capture agree.
            m_OccHiZVP = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix;
            m_OccHiZNearFar = new Vector2(cam.nearClipPlane, cam.farClipPlane);
            m_OccHiZValid = true;
        }

        // Allocate/resize the Hi-Z in the Update context (safe — not mid-render). Called from Tick.
        void EnsureOccHiZ(Camera cam)
        {
            if (cam == null) return;
            int w = Mathf.Clamp(cam.pixelWidth / 4, 64, 512);
            int h = Mathf.Clamp(cam.pixelHeight / 4, 64, 512);
            if (m_OccHiZ != null && m_OccHiZ.width == w && m_OccHiZ.height == h && m_OccHiZ.IsCreated()) return;
            if (m_OccHiZ != null) m_OccHiZ.Release();
            m_OccHiZ = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat)
            { enableRandomWrite = true, name = "GrassOccHiZ", filterMode = FilterMode.Point };
            m_OccHiZ.Create();
        }

        // Derive the clipmap shape. clipRes (cells per level side) ∝ screenHeight / cellPixels, so the
        // on-screen blade density is resolution-independent (4K gets more cells than 1080p for the same
        // look). numLevels so the OUTERMOST ring (half-extent = clipRes/2·baseSpacing·2^(L-1)) reaches
        // maxBladeDistance. Total threads = numLevels·clipRes², far under the 1D dispatch ceiling.
        void ComputeClipmap(out int clipRes, out int numLevels, out int screenH)
        {
            screenH = ScreenHeight();
            float cp = Mathf.Max(1f, M.cellPixels);
            int raw = Mathf.Clamp(Mathf.RoundToInt(screenH / cp), kMinClipRes, kMaxClipRes);
            clipRes = Mathf.Max(kMinClipRes, raw & ~1); // even: the centered window halves cleanly

            float sp0 = Mathf.Max(M.spacing, 0.01f);
            float level0Reach = clipRes * 0.5f * sp0; // half-extent of the finest level
            numLevels = 1;
            if (M.maxBladeDistance > level0Reach && level0Reach > 1e-3f)
                numLevels = 1 + Mathf.CeilToInt(Mathf.Log(M.maxBladeDistance / level0Reach, 2f));
            numLevels = Mathf.Clamp(numLevels, 1, kMaxLevels);
        }

        // Clipmap center XZ: the camera (camera mode) or the controller (fallback).
        Vector2 ClipCenterXZ()
        {
            Camera cam = M.cameraCentered ? GetWindowCamera() : null;
            Vector3 p = cam != null ? cam.transform.position : OriginPos();
            return new Vector2(p.x, p.z);
        }

        // Active camera's pixel height (drives clipRes). Scene view in edit, Camera.main in play.
        int ScreenHeight()
        {
            Camera cam = GetWindowCamera();
            int h = cam != null ? cam.pixelHeight : 0;
            return h > 0 ? h : 1080;
        }

        // CPU fallback: a single flat clipRes² grid centered on the controller GO (no terrain, no
        // cull, no LOD levels — every blade goes to the NEAR and SHADOW lists with identity indices).
        void FillCPU()
        {
            int n = m_ClipRes * m_ClipRes;
            var rng = new System.Random(12345);
            var xform = new Vector4[n];   // xyz = abs world pos, w = yaw (radians)
            var col   = new Vector4[n];
            var gp    = new Vector4[n];
            var gp2   = new Vector4[n];

            // Representative species for the flat fallback (first species, or the default grass).
            SpeciesEntry rep = (M.species != null && M.species.Length > 0) ? M.species[0] : new SpeciesEntry();
            Color baseLin = rep.color.linear;
            float half = (m_ClipRes - 1) * 0.5f;
            Vector3 origin = OriginPos();
            int idx = 0;
            for (int z = 0; z < m_ClipRes; z++)
            for (int x = 0; x < m_ClipRes; x++)
            {
                float jx = (float)(rng.NextDouble() - 0.5) * M.spacing * M.positionJitter;
                float jz = (float)(rng.NextDouble() - 0.5) * M.spacing * M.positionJitter;
                Vector3 pos = origin + new Vector3((x - half) * M.spacing + jx, 0f, (z - half) * M.spacing + jz);
                float yaw = (float)(rng.NextDouble() * 6.2831853); // radians (the hook does sincos)
                xform[idx] = new Vector4(pos.x, pos.y, pos.z, yaw);

                float cv = (float)(rng.NextDouble() - 0.5) * 2f * rep.colorVariation;
                col[idx] = new Vector4(
                    Mathf.Clamp01(baseLin.r + cv * 0.15f),
                    Mathf.Clamp01(baseLin.g + cv * 0.20f),
                    Mathf.Clamp01(baseLin.b + cv * 0.08f),
                    1f);

                float hMul  = Mathf.Lerp(1f - rep.heightRandom, 1f + rep.heightRandom, (float)rng.NextDouble());
                float bendV = rep.bend * (0.4f + 0.6f * (float)rng.NextDouble());
                float tiltV = rep.tilt * ((float)rng.NextDouble() - 0.5f) * 2f;
                float phase = (float)rng.NextDouble();
                gp[idx] = new Vector4(rep.height * hMul, bendV, tiltV, phase);
                gp2[idx] = new Vector4(rep.width, 0f, 0f, 0f);
                idx++;
            }

            m_InstanceData.SetData(xform, 0, (int)(m_AddrXform / kSizeOfFloat4),    xform.Length);
            m_InstanceData.SetData(col, 0, (int)(m_AddrCol / kSizeOfFloat4),       col.Length);
            m_InstanceData.SetData(gp,  0, (int)(m_AddrGP  / kSizeOfFloat4),       gp.Length);
            m_InstanceData.SetData(gp2, 0, (int)(m_AddrGP2 / kSizeOfFloat4),       gp2.Length);

            var ids = new int[n];
            for (int i = 0; i < n; i++) ids[i] = i;
            m_VisibleIndexBuffer.SetData(ids, 0, 0, n);                   // NEAR section
            m_VisibleIndexBuffer.SetData(ids, 0, 2 * m_Capacity, n); // SHADOW section (stride = capacity)
            m_ArgsData[1] = (uint)n; m_ArgsData[6] = 0; m_ArgsData[11] = (uint)n;
            m_ArgsBuffer.SetData(m_ArgsData);
        }

        static int BufferCountForInstances(int bytesPerInstance, int numInstances, int extraBytes)
        {
            bytesPerInstance = (bytesPerInstance + sizeof(int) - 1) / sizeof(int) * sizeof(int);
            extraBytes       = (extraBytes + sizeof(int) - 1) / sizeof(int) * sizeof(int);
            long totalBytes  = (long)bytesPerInstance * numInstances + extraBytes;
            return (int)(totalBytes / sizeof(int));
        }

        static Mesh BuildBladeMesh(int segments)
        {
            segments = Mathf.Max(1, segments);
            int rows = segments + 1;
            var mesh = new Mesh { name = "GrassBlades_Blade" + segments };

            var verts   = new Vector3[rows * 2];
            var normals = new Vector3[rows * 2];
            var uv      = new Vector2[rows * 2];
            for (int i = 0; i < rows; i++)
            {
                float v = (float)i / segments;
                verts[i * 2 + 0] = new Vector3(-0.5f, v, 0f);
                verts[i * 2 + 1] = new Vector3( 0.5f, v, 0f);
                normals[i * 2 + 0] = Vector3.forward;
                normals[i * 2 + 1] = Vector3.forward;
                uv[i * 2 + 0] = new Vector2(0f, v);
                uv[i * 2 + 1] = new Vector2(1f, v);
            }

            var tris = new int[segments * 6];
            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                tris[t++] = b;     tris[t++] = b + 2; tris[t++] = b + 1;
                tris[t++] = b + 1; tris[t++] = b + 2; tris[t++] = b + 3;
            }

            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.uv        = uv;
            mesh.triangles = tris;
            mesh.RecalculateTangents();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }

        unsafe JobHandle OnPerformCulling(
            BatchRendererGroup rendererGroup,
            BatchCullingContext cullingContext,
            BatchCullingOutput cullingOutput,
            IntPtr userContext)
        {
            // Render only when the module is active. Camera + shadow (Light) views only.
            if (M == null || !M.active) return new JobHandle();
            bool lightView = cullingContext.viewType == BatchCullingViewType.Light;
            if (!lightView && cullingContext.viewType != BatchCullingViewType.Camera) return new JobHandle();
            if (m_ArgsBuffer == null || m_VisibleIndexBuffer == null) return new JobHandle();
            if (lightView && M.shadowCascades <= 0) return new JobHandle(); // grass shadows off

            // One draw per (species, band): camera = NEAR (detailed, per-object MV for wind) + FAR (low,
            // no HasMotion) per species; light = SHADOW per species. Each uses that species' mesh.
            int cmdCount = lightView ? m_SpeciesCount : 2 * m_SpeciesCount;

            int alignment = UnsafeUtility.AlignOf<long>();
            var drawCommands = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();

            drawCommands->indirectDrawCommands = (BatchDrawCommandIndirect*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommandIndirect>() * cmdCount, alignment, Allocator.TempJob);
            drawCommands->drawRanges           = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
            drawCommands->indirectDrawCommandCount = cmdCount;
            drawCommands->drawRangeCount           = 1;

            drawCommands->drawCommands     = null;
            drawCommands->drawCommandCount = 0;
            drawCommands->proceduralDrawCommands     = null;
            drawCommands->proceduralDrawCommandCount = 0;
            drawCommands->proceduralIndirectDrawCommands     = null;
            drawCommands->proceduralIndirectDrawCommandCount = 0;
            drawCommands->visibleInstances     = null;
            drawCommands->visibleInstanceCount = 0;
            drawCommands->drawCommandPickingInstanceIDs = null;
            drawCommands->instanceSortingPositions = null;
            drawCommands->instanceSortingPositionFloatCount = 0;

            if (lightView)
            {
                ushort shadowMask = (ushort)((1 << Mathf.Clamp(M.shadowCascades, 1, 8)) - 1);
                for (int s = 0; s < m_SpeciesCount; s++)
                {
                    int sec = s * 3 + 2; // SHADOW
                    drawCommands->indirectDrawCommands[s] = MakeCmd(
                        m_FarID[s], BatchDrawCommandFlags.None,
                        visibleSection: sec, argsByteOffset: (uint)(sec * 20), splitMask: shadowMask);
                }
            }
            else
            {
                for (int s = 0; s < m_SpeciesCount; s++)
                {
                    int near = s * 3 + 0, far = s * 3 + 1;
                    drawCommands->indirectDrawCommands[2 * s + 0] = MakeCmd(
                        m_NearID[s], BatchDrawCommandFlags.HasMotion,
                        visibleSection: near, argsByteOffset: (uint)(near * 20), splitMask: 0xff);
                    drawCommands->indirectDrawCommands[2 * s + 1] = MakeCmd(
                        m_FarID[s], BatchDrawCommandFlags.None,
                        visibleSection: far, argsByteOffset: (uint)(far * 20), splitMask: 0xff);
                }
            }

            // Unity 6: BatchDrawRange fields are Malloc'd (uninitialized) — set them ALL.
            // drawCommandsType=Indirect routes Begin/Count to the indirectDrawCommands array.
            drawCommands->drawRanges[0].drawCommandsType  = BatchDrawCommandType.Indirect;
            drawCommands->drawRanges[0].drawCommandsBegin = 0;
            drawCommands->drawRanges[0].drawCommandsCount = (uint)cmdCount;
            drawCommands->drawRanges[0].filterSettings = new BatchFilterSettings
            {
                renderingLayerMask = 0xffffffff,
                shadowCastingMode  = ShadowCastingMode.On,
                receiveShadows     = true,
            };

            return new JobHandle();
        }

        BatchDrawCommandIndirect MakeCmd(BatchMeshID mesh, BatchDrawCommandFlags flags,
                                         int visibleSection, uint argsByteOffset, ushort splitMask)
        {
            return new BatchDrawCommandIndirect
            {
                flags               = flags,
                batchID             = m_BatchID,
                materialID          = m_MaterialID,
                meshID              = mesh,
                topology            = MeshTopology.Triangles,
                splitVisibilityMask = splitMask,
                lightmapIndex       = 0xffff, // no lightmap
                sortingPosition     = 0,
                // RawBuffer mode (D3D12 SSBO) binds the WHOLE index buffer: both window fields
                // MUST stay 0 (ConstantBuffer-mode-only parameters — Unity errors otherwise).
                // The per-population section is selected with visibleOffset (element index).
                visibleInstancesBufferHandle          = m_VisibleIndexBuffer.bufferHandle,
                visibleInstancesBufferWindowOffset    = 0,
                visibleInstancesBufferWindowSizeBytes = 0,
                visibleOffset       = (uint)(visibleSection * m_Capacity),
                indirectArgsBufferHandle = m_ArgsBuffer.bufferHandle,
                indirectArgsBufferOffset = argsByteOffset,
            };
        }

        // Step 3a — impostor draw emission. DIRECT path (CPU-known instance list): one draw command, every
        // card visible today. 3c will fill visibleInstances with only the frustum-visible subset. The blade's
        // OnPerformCulling above is INDIRECT (GPU-driven); this BRG is independent and never touches it.
        unsafe JobHandle OnPerformCullingImpostor(
            BatchRendererGroup rendererGroup,
            BatchCullingContext cullingContext,
            BatchCullingOutput cullingOutput,
            IntPtr userContext)
        {
            if (M == null || !M.active) return new JobHandle();
            // Impostors are the FAR field (beyond the blades) -> negligible shadow casters; only the camera view.
            if (cullingContext.viewType != BatchCullingViewType.Camera) return new JobHandle();
            if (m_ImpInstanceData == null || m_ImpCount == 0 || m_ImpPositions == null) return new JobHandle();

            int alignment = UnsafeUtility.AlignOf<long>();
            var dc = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();

            // CPU frustum cull (sparse field -> cheap): emit only the cards inside the view. Cull against the
            // WINDOW camera (= the Main/Game camera when cullFromMainCamera) via m_PlaneCache — the SAME frustum
            // the blades use (computed in DispatchCull each Tick), NOT cullingContext (the RENDERING view). So
            // from the Scene view the cards are restricted to the Game camera, exactly like the blades. Generous
            // radius bounds the card (cached pos is the ground; the card extends up) so edge cards survive.
            const float kCardRadius = 3f;
            dc->visibleInstances = (int*)UnsafeUtility.Malloc(sizeof(int) * m_ImpCount, alignment, Allocator.TempJob);
            int vis = 0;
            for (int i = 0; i < m_ImpCount; i++)
            {
                Vector3 wp = m_ImpPositions[i];
                bool inside = true;
                for (int pl = 0; pl < 6; pl++)
                    if (m_PlaneCache[pl].GetDistanceToPoint(wp) < -kCardRadius) { inside = false; break; }
                if (inside) dc->visibleInstances[vis++] = i;
            }
            dc->visibleInstanceCount = vis;

            dc->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>(), alignment, Allocator.TempJob);
            dc->drawCommandCount = 1;
            dc->drawCommands[0] = new BatchDrawCommand
            {
                visibleOffset       = 0,
                visibleCount        = (uint)vis,
                batchID             = m_ImpBatchID,
                materialID          = m_ImpMatID,
                meshID              = m_ImpMeshID,
                submeshIndex        = 0,
                splitVisibilityMask = 0xff,
                flags               = BatchDrawCommandFlags.None,
                sortingPosition     = 0,
            };

            // Unity 6: Malloc'd BatchDrawRange fields are uninitialized — set them ALL. Direct (not Indirect).
            dc->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
            dc->drawRangeCount = 1;
            dc->drawRanges[0] = new BatchDrawRange
            {
                drawCommandsType  = BatchDrawCommandType.Direct,
                drawCommandsBegin = 0,
                drawCommandsCount = 1,
                filterSettings = new BatchFilterSettings
                {
                    renderingLayerMask = 0xffffffff,
                    shadowCastingMode  = ShadowCastingMode.On,
                    receiveShadows     = true,
                },
            };

            dc->indirectDrawCommands = null; dc->indirectDrawCommandCount = 0;
            dc->proceduralDrawCommands = null; dc->proceduralDrawCommandCount = 0;
            dc->proceduralIndirectDrawCommands = null; dc->proceduralIndirectDrawCommandCount = 0;
            dc->drawCommandPickingInstanceIDs = null;
            dc->instanceSortingPositions = null; dc->instanceSortingPositionFloatCount = 0;

            return new JobHandle();
        }
    }
}
