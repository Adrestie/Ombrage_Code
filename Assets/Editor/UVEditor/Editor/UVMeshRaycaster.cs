using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Raycast manuel (Möller–Trumbore) d'un rayon monde sur le mesh de travail.
    /// Aucune dépendance à la physique : fonctionne hors Play Mode et sur un mesh
    /// transitoire sans collider. Repris du Vertex Color Editor.
    /// </summary>
    public class UVMeshRaycaster
    {
        Vector3[] _vertices;
        int[] _triangles;

        public bool IsReady => _vertices != null && _triangles != null;

        /// <summary>Met en cache la géométrie du mesh de travail.</summary>
        public void SetMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                _vertices = null;
                _triangles = null;
                return;
            }

            _vertices = mesh.vertices;
            _triangles = mesh.triangles;
        }

        public struct RayHit
        {
            public bool hit;
            public Vector3 pointWorld;
            public Vector3 normalWorld;
            public int triangleIndex;
        }

        /// <summary>Indices des 3 vertices d'un triangle.</summary>
        public void GetTriangleVertices(int triangleIndex, out int v0, out int v1, out int v2)
        {
            int t = triangleIndex * 3;
            v0 = _triangles[t];
            v1 = _triangles[t + 1];
            v2 = _triangles[t + 2];
        }

        /// <summary>Raycast d'un rayon monde sur le mesh.</summary>
        public RayHit Raycast(Ray worldRay, Matrix4x4 localToWorld)
        {
            var result = new RayHit { hit = false };
            if (!IsReady)
                return result;

            Matrix4x4 worldToLocal = localToWorld.inverse;
            Vector3 ro = worldToLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 rd = worldToLocal.MultiplyVector(worldRay.direction).normalized;

            float closest = float.MaxValue;
            int hitTri = -1;
            Vector3 hitLocal = Vector3.zero;

            for (int t = 0; t < _triangles.Length; t += 3)
            {
                Vector3 v0 = _vertices[_triangles[t]];
                Vector3 v1 = _vertices[_triangles[t + 1]];
                Vector3 v2 = _vertices[_triangles[t + 2]];

                if (RayTriangle(ro, rd, v0, v1, v2, out float dist) && dist < closest)
                {
                    closest = dist;
                    hitTri = t;
                    hitLocal = ro + rd * dist;
                }
            }

            if (hitTri >= 0)
            {
                Vector3 a = _vertices[_triangles[hitTri]];
                Vector3 b = _vertices[_triangles[hitTri + 1]];
                Vector3 c = _vertices[_triangles[hitTri + 2]];
                Vector3 nLocal = Vector3.Cross(b - a, c - a).normalized;

                result.hit = true;
                result.pointWorld = localToWorld.MultiplyPoint3x4(hitLocal);
                result.normalWorld = localToWorld.MultiplyVector(nLocal).normalized;
                result.triangleIndex = hitTri / 3;
            }

            return result;
        }

        /// <summary>Test rayon / triangle (Möller–Trumbore). Réutilisé par la grille d'occlusion.</summary>
        internal static bool RayTriangle(Vector3 ro, Vector3 rd,
            Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            const float EPS = 1e-7f;
            distance = 0f;

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(rd, e2);
            float det = Vector3.Dot(e1, p);

            if (det > -EPS && det < EPS)
                return false;

            float invDet = 1f / det;
            Vector3 tvec = ro - v0;

            float u = Vector3.Dot(tvec, p) * invDet;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(tvec, e1);
            float v = Vector3.Dot(rd, q) * invDet;
            if (v < 0f || u + v > 1f)
                return false;

            float dist = Vector3.Dot(e2, q) * invDet;
            if (dist <= EPS)
                return false;

            distance = dist;
            return true;
        }
    }

    /// <summary>
    /// Accélère le test d'occlusion lors d'une sélection rectangle. Les triangles
    /// sont rangés dans une grille 2D en espace écran selon leur rectangle
    /// englobant projeté ; tester si un point est masqué ne raycaste alors que
    /// les triangles de sa cellule. Construite une fois par sélection rectangle.
    /// Reprise du Vertex Color Editor.
    /// </summary>
    public class UVScreenOcclusionGrid
    {
        Vector3[] _verts;
        int[] _tris;
        Matrix4x4 _worldToLocal;
        bool _orthographic;
        Vector3 _camForwardWorld;
        Vector3 _camPositionWorld;
        float _orthoPushback;
        float _epsilon;

        int _cols, _rows;
        Rect _gridBounds;
        List<int>[] _cells;
        readonly List<int> _spanning = new List<int>();
        bool _ready;

        public bool IsReady => _ready;

        public void Build(Vector3[] verts, int[] tris, Matrix4x4 localToWorld, Camera cam)
        {
            _ready = false;
            _spanning.Clear();

            if (verts == null || verts.Length == 0 || tris == null ||
                tris.Length < 3 || cam == null)
                return;

            _verts = verts;
            _tris = tris;
            _worldToLocal = localToWorld.inverse;
            _orthographic = cam.orthographic;
            _camForwardWorld = cam.transform.forward;
            _camPositionWorld = cam.transform.position;

            // Étendue monde du mesh -> epsilon d'occlusion + recul orthographique
            // (échelle mesh, indépendants de la distance caméra).
            Vector3 min = localToWorld.MultiplyPoint3x4(verts[0]);
            Vector3 max = min;
            for (int i = 1; i < verts.Length; i++)
            {
                Vector3 w = localToWorld.MultiplyPoint3x4(verts[i]);
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }
            float diagonal = Mathf.Max(1e-6f, (max - min).magnitude);
            _epsilon = diagonal * 1e-3f;
            _orthoPushback = diagonal * 2f;

            int triCount = tris.Length / 3;
            var triBounds = new Rect[triCount];
            var triValid = new bool[triCount];

            float bxMin = 0f, byMin = 0f, bxMax = 0f, byMax = 0f;
            bool boundsInit = false;

            for (int t = 0; t < triCount; t++)
            {
                int b = t * 3;
                Vector3 w0 = localToWorld.MultiplyPoint3x4(verts[tris[b]]);
                Vector3 w1 = localToWorld.MultiplyPoint3x4(verts[tris[b + 1]]);
                Vector3 w2 = localToWorld.MultiplyPoint3x4(verts[tris[b + 2]]);

                if (cam.WorldToViewportPoint(w0).z <= 0f ||
                    cam.WorldToViewportPoint(w1).z <= 0f ||
                    cam.WorldToViewportPoint(w2).z <= 0f)
                {
                    _spanning.Add(t);
                    continue;
                }

                Vector2 g0 = HandleUtility.WorldToGUIPoint(w0);
                Vector2 g1 = HandleUtility.WorldToGUIPoint(w1);
                Vector2 g2 = HandleUtility.WorldToGUIPoint(w2);

                float xMin = Mathf.Min(g0.x, Mathf.Min(g1.x, g2.x));
                float xMax = Mathf.Max(g0.x, Mathf.Max(g1.x, g2.x));
                float yMin = Mathf.Min(g0.y, Mathf.Min(g1.y, g2.y));
                float yMax = Mathf.Max(g0.y, Mathf.Max(g1.y, g2.y));
                triBounds[t] = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                triValid[t] = true;

                if (!boundsInit)
                {
                    bxMin = xMin; byMin = yMin; bxMax = xMax; byMax = yMax;
                    boundsInit = true;
                }
                else
                {
                    if (xMin < bxMin) bxMin = xMin;
                    if (yMin < byMin) byMin = yMin;
                    if (xMax > bxMax) bxMax = xMax;
                    if (yMax > byMax) byMax = yMax;
                }
            }

            if (!boundsInit)
            {
                _cols = _rows = 1;
                _gridBounds = new Rect(0f, 0f, 1f, 1f);
                _cells = new[] { new List<int>() };
                _ready = true;
                return;
            }

            _gridBounds = Rect.MinMaxRect(bxMin, byMin, bxMax, byMax);

            int targetCells = Mathf.Max(1, triCount / 8);
            float aspect = _gridBounds.width / Mathf.Max(1f, _gridBounds.height);
            _cols = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(targetCells * aspect)), 1, 128);
            _rows = Mathf.Clamp(Mathf.RoundToInt((float)targetCells / _cols), 1, 128);

            _cells = new List<int>[_cols * _rows];
            for (int i = 0; i < _cells.Length; i++)
                _cells[i] = new List<int>();

            for (int t = 0; t < triCount; t++)
            {
                if (!triValid[t])
                    continue;
                Rect r = triBounds[t];
                int cx0 = CellX(r.xMin), cx1 = CellX(r.xMax);
                int cy0 = CellY(r.yMin), cy1 = CellY(r.yMax);
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                        _cells[cy * _cols + cx].Add(t);
            }

            _ready = true;
        }

        int CellX(float guiX)
        {
            float u = (guiX - _gridBounds.xMin) / Mathf.Max(1e-6f, _gridBounds.width);
            return Mathf.Clamp(Mathf.FloorToInt(u * _cols), 0, _cols - 1);
        }

        int CellY(float guiY)
        {
            float v = (guiY - _gridBounds.yMin) / Mathf.Max(1e-6f, _gridBounds.height);
            return Mathf.Clamp(Mathf.FloorToInt(v * _rows), 0, _rows - 1);
        }

        /// <summary>
        /// Vrai si le point monde est masqué par la géométrie du mesh vu de la
        /// caméra (un triangle est traversé avant d'atteindre le point).
        /// </summary>
        public bool IsOccluded(Vector3 pointWorld)
        {
            if (!_ready)
                return false;

            Vector2 gui = HandleUtility.WorldToGUIPoint(pointWorld);
            List<int> cell = _cells[CellY(gui.y) * _cols + CellX(gui.x)];

            Vector3 fromWorld = _orthographic
                ? pointWorld - _camForwardWorld * _orthoPushback
                : _camPositionWorld;

            return SegmentBlocked(fromWorld, pointWorld, cell)
                || SegmentBlocked(fromWorld, pointWorld, _spanning);
        }

        bool SegmentBlocked(Vector3 fromWorld, Vector3 toWorld, List<int> candidates)
        {
            if (candidates.Count == 0)
                return false;

            Vector3 ro = _worldToLocal.MultiplyPoint3x4(fromWorld);
            Vector3 to = _worldToLocal.MultiplyPoint3x4(toWorld);
            Vector3 d = to - ro;
            float tEnd = d.magnitude;
            if (tEnd < 1e-6f)
                return false;
            Vector3 rd = d / tEnd;

            for (int k = 0; k < candidates.Count; k++)
            {
                int b = candidates[k] * 3;
                if (UVMeshRaycaster.RayTriangle(ro, rd,
                        _verts[_tris[b]], _verts[_tris[b + 1]], _verts[_tris[b + 2]],
                        out float dist)
                    && dist < tEnd - _epsilon)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
