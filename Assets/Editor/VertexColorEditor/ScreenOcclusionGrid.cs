using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Accélère le test d'occlusion lors d'une sélection rectangle. Les triangles
    /// du mesh sont rangés dans une grille 2D en espace écran (GUI) selon leur
    /// rectangle englobant projeté. Tester si un point est masqué ne raycaste
    /// alors que les triangles de sa cellule (seuls susceptibles de le recouvrir
    /// à l'écran), au lieu de parcourir tout le mesh.
    ///
    /// Construite une fois par sélection rectangle (dépend de la caméra).
    /// </summary>
    public class ScreenOcclusionGrid
    {
        Vector3[] _verts;
        int[] _tris;
        Matrix4x4 _worldToLocal;
        bool _orthographic;
        Vector3 _camForwardWorld;
        Vector3 _camPositionWorld;
        float _orthoPushback;   // recul du rayon en orthographique (échelle mesh)
        float _epsilon;         // marge d'occlusion (échelle mesh, pas distance caméra)

        int _cols, _rows;
        Rect _gridBounds;                                  // bornes GUI de la grille
        List<int>[] _cells;                                // triangles par cellule
        readonly List<int> _spanning = new List<int>();    // triangles à cheval near plane
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

            // Étendue monde du mesh -> epsilon d'occlusion + recul orthographique.
            // Les deux sont à l'échelle du mesh, donc INDÉPENDANTS de la distance
            // caméra (un epsilon proportionnel à la distance devenait énorme en
            // caméra orthographique et désactivait l'occlusion).
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

            // Projection GUI de chaque triangle + rectangle englobant écran.
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

                // Triangle à cheval sur le near plane / derrière la caméra :
                // projection écran non fiable -> liste "spanning", testée pour
                // tout point (peu d'occurrences en vue extérieure normale).
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

            // Aucun triangle projetable : tout est dans la liste spanning.
            if (!boundsInit)
            {
                _cols = _rows = 1;
                _gridBounds = new Rect(0f, 0f, 1f, 1f);
                _cells = new[] { new List<int>() };
                _ready = true;
                return;
            }

            _gridBounds = Rect.MinMaxRect(bxMin, byMin, bxMax, byMax);

            // Nombre de cellules proportionnel au nombre de triangles (~8 par
            // cellule en moyenne) : l'occupation reste stable quel que soit le
            // zoom (un mesh minuscule à l'écran n'écroule pas la grille).
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

            // Rayon d'occlusion : depuis la caméra (perspective) ou depuis un
            // point reculé le long de l'axe de vue (orthographique).
            Vector3 fromWorld = _orthographic
                ? pointWorld - _camForwardWorld * _orthoPushback
                : _camPositionWorld;

            return SegmentBlocked(fromWorld, pointWorld, cell)
                || SegmentBlocked(fromWorld, pointWorld, _spanning);
        }

        // Raycast du segment from -> to contre une liste de triangles donnée.
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
                if (VertexColorPainter.RayTriangle(ro, rd,
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
