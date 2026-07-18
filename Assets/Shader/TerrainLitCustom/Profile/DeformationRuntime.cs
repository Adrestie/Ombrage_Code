// DeformationRuntime.cs
// État runtime + logique de la déformation (RT toroïdales world-aligned, stamping véhicule,
// diffusion, fade). Porté fidèlement depuis l'ancien TerrainDeformationManager.
// Détenu par le TerrainProfileController (un ScriptableObject ne peut porter ni refs de scène
// ni état runtime par instance). Config lue depuis le DeformationModule (M), refs de scène
// depuis le contrôleur (C).
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    public class DeformationRuntime
    {
        RenderTexture deformationRT;
        RenderTexture tessellationMaskRT;
        Material stampMaterial;
        Material fadeMaterial;
        Mesh quadMesh;
        Vector3[] lastWheelPositions;
        Vector3[] lastWheelDirections;
        Vector3 lastFlipStampPosition;
        bool wasFlipped;
        bool stampedThisFrame;
        bool initialized;

        DeformationModule M;
        TerrainProfileController C;

        public RenderTexture DeformationRT => deformationRT;
        public RenderTexture TessellationMaskRT => tessellationMaskRT;

        static readonly int ID_ToroidalWrap = Shader.PropertyToID("_ToroidalWrap");
        static readonly int ID_SegmentA = Shader.PropertyToID("_SegmentA");
        static readonly int ID_SegmentB = Shader.PropertyToID("_SegmentB");
        static readonly int ID_Radius = Shader.PropertyToID("_Radius");
        static readonly int ID_Intensity = Shader.PropertyToID("_Intensity");
        static readonly int ID_FadeAmount = Shader.PropertyToID("_FadeAmount");
        static readonly int ID_DiffusionStrength = Shader.PropertyToID("_DiffusionStrength");
        static readonly int ID_TexelSize = Shader.PropertyToID("_TexelSize");

        // ---------------------------------------------------------------------------------
        public void Initialize(DeformationModule m, TerrainProfileController c)
        {
            M = m; C = c;
            CreateDeformationRT();
            CreateTessellationMaskRT();

            if (stampMaterial == null)
            {
                Shader s = m != null ? m.stampShader : null;
#if UNITY_EDITOR
                if (s == null) s = Shader.Find("Hidden/TerrainStamp");
#endif
                if (s) stampMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                else Debug.LogError("DeformationModule: shader 'Hidden/TerrainStamp' manquant — assigne stampShader sur le contrôleur.");
            }
            if (fadeMaterial == null)
            {
                Shader s = m != null ? m.fadeShader : null;
#if UNITY_EDITOR
                if (s == null) s = Shader.Find("Hidden/TerrainDeformFade");
#endif
                if (s) fadeMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                else Debug.LogError("DeformationModule: shader 'Hidden/TerrainDeformFade' manquant — assigne fadeShader sur le contrôleur.");
            }
            if (quadMesh == null) quadMesh = CreateFullscreenQuad();

            int n = (c != null && c.wheels != null) ? c.wheels.Length : 0;
            lastWheelPositions = new Vector3[n];
            lastWheelDirections = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var w = c.wheels[i];
                lastWheelPositions[i] = w != null ? w.position : Vector3.zero;
                lastWheelDirections[i] = (c.vehicleBody != null) ? c.vehicleBody.forward : Vector3.forward;
            }
            lastFlipStampPosition = (c != null && c.vehicleBody != null) ? c.vehicleBody.position : Vector3.zero;
            wasFlipped = false;
            initialized = true;
        }

        public void Dispose()
        {
            ReleaseRT(ref deformationRT);
            ReleaseRT(ref tessellationMaskRT);
            DestroyObj(ref stampMaterial);
            DestroyObj(ref fadeMaterial);
            DestroyObj(ref quadMesh);
            initialized = false;
        }

        public void EnsureRTs(DeformationModule m, TerrainProfileController c)
        {
            M = m; C = c;
            if (deformationRT == null || deformationRT.width != m.resolution) CreateDeformationRT();
            else if (!deformationRT.IsCreated()) { deformationRT.Create(); ClearRT(deformationRT); }
            if (tessellationMaskRT == null || tessellationMaskRT.width != m.maskResolution) CreateTessellationMaskRT();
            else if (!tessellationMaskRT.IsCreated()) { tessellationMaskRT.Create(); ClearRT(tessellationMaskRT); }
        }

        // ---------------------------------------------------------------------------------
        public void Tick(DeformationModule m, TerrainProfileController c, Material mat)
        {
            M = m; C = c;
            if (!initialized || c == null || c.vehicleBody == null || stampMaterial == null) return;
            EnsureRTs(m, c);

            stampedThisFrame = false;
            bool isFlipped = IsVehicleFlipped();
            if (isFlipped) UpdateFlipped();
            else
            {
                if (wasFlipped) StampCircleAtWorld(c.vehicleBody.position, m.flipStampIntensity, m.flipStampRadiusMeters);
                UpdateWheels();
            }
            wasFlipped = isFlipped;

            float texel = 1f / Mathf.Max(1, m.resolution);

            // 1. Diffusion (seulement si stamp cette frame) — les 2 RT
            if (stampedThisFrame && m.diffusionStrength > 0 && m.diffusionIterations > 0 && fadeMaterial != null)
            {
                fadeMaterial.SetFloat(ID_FadeAmount, 0);
                fadeMaterial.SetFloat(ID_DiffusionStrength, m.diffusionStrength);

                fadeMaterial.SetVector(ID_TexelSize, new Vector4(texel, texel, 0, 0));
                for (int it = 0; it < m.diffusionIterations; it++)
                {
                    var tmp = RenderTexture.GetTemporary(m.resolution, m.resolution, 0, RenderTextureFormat.RFloat);
                    tmp.wrapMode = TextureWrapMode.Repeat;
                    Graphics.Blit(deformationRT, tmp, fadeMaterial);
                    Graphics.Blit(tmp, deformationRT);
                    RenderTexture.ReleaseTemporary(tmp);
                }

                if (tessellationMaskRT != null && tessellationMaskRT.IsCreated())
                {
                    float mtexel = 1f / Mathf.Max(1, m.maskResolution);
                    fadeMaterial.SetVector(ID_TexelSize, new Vector4(mtexel, mtexel, 0, 0));
                    for (int it = 0; it < m.diffusionIterations; it++)
                    {
                        var tmp = RenderTexture.GetTemporary(m.maskResolution, m.maskResolution, 0, RenderTextureFormat.RFloat);
                        tmp.wrapMode = TextureWrapMode.Repeat;
                        Graphics.Blit(tessellationMaskRT, tmp, fadeMaterial);
                        Graphics.Blit(tmp, tessellationMaskRT);
                        RenderTexture.ReleaseTemporary(tmp);
                    }
                }
            }

            // 2. Fade (chaque frame) — les 2 RT
            if (m.fadeSpeed > 0 && fadeMaterial != null)
            {
                fadeMaterial.SetFloat(ID_FadeAmount, m.fadeSpeed * Time.deltaTime);
                fadeMaterial.SetFloat(ID_DiffusionStrength, 0);

                fadeMaterial.SetVector(ID_TexelSize, new Vector4(texel, texel, 0, 0));
                var tmp2 = RenderTexture.GetTemporary(m.resolution, m.resolution, 0, RenderTextureFormat.RFloat);
                tmp2.wrapMode = TextureWrapMode.Repeat;
                Graphics.Blit(deformationRT, tmp2, fadeMaterial);
                Graphics.Blit(tmp2, deformationRT);
                RenderTexture.ReleaseTemporary(tmp2);

                if (tessellationMaskRT != null && tessellationMaskRT.IsCreated())
                {
                    float mtexel = 1f / Mathf.Max(1, m.maskResolution);
                    fadeMaterial.SetVector(ID_TexelSize, new Vector4(mtexel, mtexel, 0, 0));
                    var mtmp = RenderTexture.GetTemporary(m.maskResolution, m.maskResolution, 0, RenderTextureFormat.RFloat);
                    mtmp.wrapMode = TextureWrapMode.Repeat;
                    Graphics.Blit(tessellationMaskRT, mtmp, fadeMaterial);
                    Graphics.Blit(mtmp, tessellationMaskRT);
                    RenderTexture.ReleaseTemporary(mtmp);
                }
            }
        }

        // ---------------------------------------------------------------------------------
        bool IsVehicleFlipped()
        {
            return C != null && C.vehicleBody != null && Vector3.Dot(C.vehicleBody.up, Vector3.up) < M.flipThreshold;
        }

        void UpdateWheels()
        {
            var wheels = C.wheels;
            int n = wheels != null ? wheels.Length : 0;
            if (lastWheelPositions == null || lastWheelPositions.Length != n)
            {
                lastWheelPositions = new Vector3[n];
                lastWheelDirections = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    lastWheelPositions[i] = wheels[i] != null ? wheels[i].position : Vector3.zero;
                    lastWheelDirections[i] = C.vehicleBody != null ? C.vehicleBody.forward : Vector3.forward;
                }
            }

            float cosAngleThreshold = Mathf.Cos(M.curveAngleThreshold * Mathf.Deg2Rad);

            for (int i = 0; i < n; i++)
            {
                if (wheels[i] == null) continue;
                Vector3 pos = wheels[i].position;
                float dist = Vector3.Distance(pos, lastWheelPositions[i]);
                if (dist < M.stepDistance * 0.1f) continue;

                float contactIntensity = GetWheelGroundIntensity(wheels[i]);
                if (contactIntensity <= 0) continue;

                bool shouldDraw = false;
                if (dist >= M.maxStepDistance) shouldDraw = true;
                if (dist >= M.stepDistance) shouldDraw = true;
                if (dist >= M.stepDistance * 0.25f)
                {
                    Vector3 currentDir = (pos - lastWheelPositions[i]).normalized;
                    if (Vector3.Dot(currentDir, lastWheelDirections[i]) < cosAngleThreshold) shouldDraw = true;
                }

                if (shouldDraw)
                {
                    float intensity = M.wheelStampIntensity * contactIntensity;
                    DrawTrailSegment(lastWheelPositions[i], pos, M.wheelStampRadiusMeters, intensity);
                    Vector3 dir = (pos - lastWheelPositions[i]).normalized;
                    if (dir.sqrMagnitude > 0.001f) lastWheelDirections[i] = dir;
                    lastWheelPositions[i] = pos;
                }
            }
        }

        float GetWheelGroundIntensity(Transform wheel)
        {
            Vector3 origin = wheel.position + Vector3.up * M.raycastOriginOffset;
            float maxDist = M.raycastOriginOffset + M.wheelContactDistance;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, C.groundLayer))
            {
                float distToSurface = hit.distance - M.raycastOriginOffset;
                if (distToSurface <= 0) return 1f;
                return 1f - Mathf.Clamp01(distToSurface / M.wheelContactDistance);
            }
            return 0f;
        }

        void UpdateFlipped()
        {
            Vector3 center = C.vehicleBody.position;
            if (Vector3.Distance(center, lastFlipStampPosition) >= M.flipStepDistance || !wasFlipped)
            {
                StampCircleAtWorld(center, M.flipStampIntensity, M.flipStampRadiusMeters);
                lastFlipStampPosition = center;
            }
        }

        // ---------------------------------------------------------------------------------
        void StampCircleAtWorld(Vector3 worldPos, float intensity, float radiusMeters)
        {
            Vector2 uv = WorldToBufferUV(worldPos);
            RenderToBothRTs(uv, uv, radiusMeters, intensity);
        }

        void DrawTrailSegment(Vector3 fromWorld, Vector3 toWorld, float radiusMeters, float intensity)
        {
            RenderToBothRTs(WorldToBufferUV(fromWorld), WorldToBufferUV(toWorld), radiusMeters, intensity);
        }

        void RenderToBothRTs(Vector2 uvA, Vector2 uvB, float radiusMeters, float intensity)
        {
            if (deformationRT == null || !deformationRT.IsCreated()) return;

            var prev = RenderTexture.active;
            GL.PushMatrix();
            GL.LoadOrtho();

            // 1. Déformation (toroïdal)
            stampMaterial.SetVector(ID_SegmentA, new Vector4(uvA.x, uvA.y, 0, 0));
            stampMaterial.SetVector(ID_SegmentB, new Vector4(uvB.x, uvB.y, 0, 0));
            stampMaterial.SetFloat(ID_Radius, MetersToBufferUVRadius(radiusMeters));
            stampMaterial.SetFloat(ID_Intensity, intensity);
            stampMaterial.SetFloat(ID_ToroidalWrap, 1.0f);
            RenderTexture.active = deformationRT;
            stampMaterial.SetPass(0);
            Graphics.DrawMeshNow(quadMesh, Matrix4x4.identity);

            // 2. Masque de tessellation (toroïdal, rayon élargi)
            if (tessellationMaskRT != null && tessellationMaskRT.IsCreated())
            {
                float paddedMeters = Mathf.Max(radiusMeters * M.tessellationMaskPadding, 3f * M.bufferWorldSize / Mathf.Max(1, M.maskResolution));
                float maskUVRadius = paddedMeters / M.bufferWorldSize;
                stampMaterial.SetVector(ID_SegmentA, new Vector4(uvA.x, uvA.y, 0, 0));
                stampMaterial.SetVector(ID_SegmentB, new Vector4(uvB.x, uvB.y, 0, 0));
                stampMaterial.SetFloat(ID_Radius, maskUVRadius);
                stampMaterial.SetFloat(ID_Intensity, 1.0f);
                stampMaterial.SetFloat(ID_ToroidalWrap, 1.0f);
                RenderTexture.active = tessellationMaskRT;
                stampMaterial.SetPass(0);
                Graphics.DrawMeshNow(quadMesh, Matrix4x4.identity);
            }

            GL.PopMatrix();
            RenderTexture.active = prev;
            stampedThisFrame = true;
        }

        Vector2 WorldToBufferUV(Vector3 worldPos)
        {
            float bws = Mathf.Max(0.001f, M.bufferWorldSize);
            float rawU = worldPos.x / bws;
            float rawV = worldPos.z / bws;
            return new Vector2(rawU - Mathf.Floor(rawU), rawV - Mathf.Floor(rawV));
        }

        float MetersToBufferUVRadius(float meters) => meters / Mathf.Max(0.001f, M.bufferWorldSize);

        // ---------------------------------------------------------------------------------
        void CreateDeformationRT()
        {
            ReleaseRT(ref deformationRT);
            deformationRT = new RenderTexture(M.resolution, M.resolution, 0, RenderTextureFormat.RFloat)
            {
                name = "TerrainDeformation",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            deformationRT.Create();
            ClearRT(deformationRT);
        }

        void CreateTessellationMaskRT()
        {
            ReleaseRT(ref tessellationMaskRT);
            tessellationMaskRT = new RenderTexture(M.maskResolution, M.maskResolution, 0, RenderTextureFormat.RFloat)
            {
                name = "TessellationMask",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            tessellationMaskRT.Create();
            ClearRT(tessellationMaskRT);
        }

        static void ClearRT(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }

        static Mesh CreateFullscreenQuad()
        {
            var mesh = new Mesh { name = "FullscreenQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                if (Application.isPlaying) Object.Destroy(rt); else Object.DestroyImmediate(rt);
                rt = null;
            }
        }

        static void DestroyObj<T>(ref T o) where T : Object
        {
            if (o != null)
            {
                if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
                o = null;
            }
        }
    }
}
