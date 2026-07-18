using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Viewport 2D intégré : dessine le layout UV du canal courant dans un
    /// rectangle de la fenêtre de l'outil.
    ///
    /// Sous-étape 2.1 — lecture seule : affichage du layout (arêtes des
    /// triangles, remplissage des faces, grille de l'espace [0,1]) avec
    /// navigation pan / zoom. La sélection et les transformations dans le
    /// viewport seront ajoutées aux sous-étapes 2.2 et 2.4.
    ///
    /// Rendu : le tracé GL se fait en coordonnées fenêtre directes (matrice
    /// pixel couvrant toute la fenêtre, SANS GL.Viewport). Le contenu est borné
    /// au rectangle du viewport par rognage géométrique (clip par calcul des
    /// segments et des triangles). Cette approche évite le double remappage de
    /// repère de GL.Viewport, qui décalait le contenu selon la position du
    /// séparateur du diptyque.
    ///
    /// Repère UV : origine en bas à gauche, V vers le haut. L'inversion
    /// verticale est appliquée au tracé.
    /// </summary>
    public class UVViewport2D
    {
        // --- Navigation (état persistant entre deux rendus) ---
        // Position UV affichée au centre du viewport.
        Vector2 _panUV = new Vector2(0.5f, 0.5f);
        // Échelle : pixels écran par unité UV.
        float _pixelsPerUnit = 256f;
        bool _isPanning;

        const float MinPixelsPerUnit = 32f;
        const float MaxPixelsPerUnit = 8192f;
        const float ZoomStep = 1.15f;

        // --- Matériau de tracé ---
        static Material _lineMaterial;

        // Rectangle de tracé courant (coordonnées fenêtre).
        Rect _viewRect;

        public Vector2 PanUV => _panUV;
        public float PixelsPerUnit => _pixelsPerUnit;

        static Material LineMaterial
        {
            get
            {
                if (_lineMaterial == null)
                {
                    var shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader != null)
                    {
                        _lineMaterial = new Material(shader)
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        _lineMaterial.SetInt("_ZWrite", 0);
                        _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                        _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
                        _lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                        _lineMaterial.SetInt("_DstBlend",
                            (int)BlendMode.OneMinusSrcAlpha);
                    }
                }
                return _lineMaterial;
            }
        }

        // ------------------------------------------------------------------
        // Conversions de coordonnées (toujours en repère fenêtre)
        // ------------------------------------------------------------------

        /// <summary>UV -> position écran (coordonnées fenêtre).</summary>
        public Vector2 UVToScreen(Vector2 uv)
        {
            Vector2 center = _viewRect.center;
            float sx = center.x + (uv.x - _panUV.x) * _pixelsPerUnit;
            // V vers le haut : on inverse l'axe vertical de l'écran.
            float sy = center.y - (uv.y - _panUV.y) * _pixelsPerUnit;
            return new Vector2(sx, sy);
        }

        /// <summary>Position écran (coordonnées fenêtre) -> UV.</summary>
        public Vector2 ScreenToUV(Vector2 screen)
        {
            Vector2 center = _viewRect.center;
            float u = _panUV.x + (screen.x - center.x) / _pixelsPerUnit;
            float v = _panUV.y - (screen.y - center.y) / _pixelsPerUnit;
            return new Vector2(u, v);
        }

        // ------------------------------------------------------------------
        // Rendu
        // ------------------------------------------------------------------

        /// <summary>
        /// Dessine le viewport dans <paramref name="rect"/> (coordonnées
        /// fenêtre). <paramref name="uvs"/> sont les UV du canal courant (1 par
        /// vertex), <paramref name="triangles"/> les indices du mesh de travail.
        /// <paramref name="selection"/> peut être null (rien de sélectionné à
        /// surligner).
        /// </summary>
        public void Draw(Rect rect, Vector2[] uvs, int[] triangles, int channel,
            UVSelection selection, UVSelectionMode mode)
        {
            _viewRect = rect;

            // Fond + bordure.
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.17f, 1f));
            DrawRectOutline(rect, new Color(0.30f, 0.30f, 0.32f, 1f));

            Material mat = LineMaterial;
            if (mat == null)
            {
                GUI.Label(rect, "Matériau de tracé introuvable.");
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                GL.PushMatrix();
                // Matrice pixel couvrant TOUTE la fenêtre : pas de GL.Viewport,
                // donc pas de remappage de repère. Le tracé utilise directement
                // les coordonnées fenêtre rendues par UVToScreen.
                GL.LoadPixelMatrix();

                DrawUnitGrid();

                if (uvs != null && triangles != null && uvs.Length > 0)
                {
                    DrawFaceFill(uvs, triangles);
                    if (selection != null)
                        DrawSelectedFaceFill(uvs, triangles, selection);
                    DrawWireframe(uvs, triangles);
                    DrawVertices(uvs, triangles, selection, mode);
                }

                // Rectangle de sélection en cours.
                if (_isBoxSelecting)
                    DrawSelectionRect();

                GL.PopMatrix();
            }

            // Libellés (IMGUI standard, en coordonnées fenêtre).
            DrawOverlayLabels(rect, uvs, triangles, channel);
        }

        static void DrawRectOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        // Grille : bordure de l'espace [0,1] + subdivisions.
        void DrawUnitGrid()
        {
            Material mat = LineMaterial;
            mat.SetPass(0);

            GL.Begin(GL.LINES);
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                bool major = (i == 0 || i == 5 || i == 10);
                Color c = major
                    ? new Color(0.42f, 0.42f, 0.45f, 1f)
                    : new Color(0.26f, 0.26f, 0.28f, 1f);

                ClippedLine(UVToScreen(new Vector2(t, 0f)),
                            UVToScreen(new Vector2(t, 1f)), c);
                ClippedLine(UVToScreen(new Vector2(0f, t)),
                            UVToScreen(new Vector2(1f, t)), c);
            }
            GL.End();

            // Axes U (rouge) et V (vert) sur les bords 0.
            GL.Begin(GL.LINES);
            Vector2 o = UVToScreen(new Vector2(0f, 0f));
            ClippedLine(o, UVToScreen(new Vector2(1f, 0f)),
                new Color(0.75f, 0.35f, 0.35f, 1f));
            ClippedLine(o, UVToScreen(new Vector2(0f, 1f)),
                new Color(0.40f, 0.70f, 0.40f, 1f));
            GL.End();
        }

        // Remplissage léger des faces, pour lire la densité du layout.
        void DrawFaceFill(Vector2[] uvs, int[] triangles)
        {
            Material mat = LineMaterial;
            mat.SetPass(0);

            Color fill = new Color(0.40f, 0.55f, 0.75f, 0.18f);
            GL.Begin(GL.TRIANGLES);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                    continue;
                ClippedTriangle(
                    UVToScreen(uvs[a]), UVToScreen(uvs[b]), UVToScreen(uvs[c]),
                    fill);
            }
            GL.End();
        }

        // Arêtes de tous les triangles.
        void DrawWireframe(Vector2[] uvs, int[] triangles)
        {
            Material mat = LineMaterial;
            mat.SetPass(0);

            Color wire = new Color(0.62f, 0.78f, 1f, 0.9f);
            GL.Begin(GL.LINES);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                    continue;
                Vector2 pa = UVToScreen(uvs[a]);
                Vector2 pb = UVToScreen(uvs[b]);
                Vector2 pc = UVToScreen(uvs[c]);
                ClippedLine(pa, pb, wire);
                ClippedLine(pb, pc, wire);
                ClippedLine(pc, pa, wire);
            }
            GL.End();
        }

        // Remplissage marqué des faces entièrement sélectionnées.
        void DrawSelectedFaceFill(Vector2[] uvs, int[] triangles,
            UVSelection selection)
        {
            Material mat = LineMaterial;
            mat.SetPass(0);

            Color fill = new Color(0.25f, 0.65f, 1f, 0.38f);
            int triCount = triangles.Length / 3;
            GL.Begin(GL.TRIANGLES);
            for (int t = 0; t < triCount; t++)
            {
                if (!selection.IsTriangleSelected(t))
                    continue;
                int i = t * 3;
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                    continue;
                ClippedTriangle(
                    UVToScreen(uvs[a]), UVToScreen(uvs[b]), UVToScreen(uvs[c]),
                    fill);
            }
            GL.End();
        }

        // Points aux vertices. En mode Vertices, ils sont bien visibles ; en
        // mode Faces, ils restent discrets. Les vertices sélectionnés sont
        // toujours surlignés.
        void DrawVertices(Vector2[] uvs, int[] triangles,
            UVSelection selection, UVSelectionMode mode)
        {
            Material mat = LineMaterial;
            mat.SetPass(0);

            bool vertexMode = mode == UVSelectionMode.Vertices;
            float half = vertexMode ? 2.5f : 1.5f;

            Color normal = vertexMode
                ? new Color(0.80f, 0.85f, 0.95f, 0.9f)
                : new Color(0.55f, 0.62f, 0.75f, 0.5f);
            Color selected = new Color(1f, 0.62f, 0.15f, 1f);
            float selHalf = 3.5f;

            // Marque les vertices effectivement référencés par la topologie.
            // (uvs peut contenir des entrées non utilisées après reconstruction.)
            GL.Begin(GL.QUADS);
            for (int v = 0; v < uvs.Length; v++)
            {
                bool isSel = selection != null && selection.ContainsVertex(v);
                if (!vertexMode && !isSel)
                    continue;   // en mode Faces, on n'affiche que la sélection

                Vector2 p = UVToScreen(uvs[v]);
                if (!_viewRect.Contains(p))
                    continue;

                if (isSel)
                    Quad(p, selHalf, selected);
                else
                    Quad(p, half, normal);
            }
            GL.End();
        }

        // Petit carré centré sur p (à appeler entre GL.Begin(GL.QUADS)/GL.End()).
        static void Quad(Vector2 p, float half, Color c)
        {
            GL.Color(c);
            GL.Vertex3(p.x - half, p.y - half, 0f);
            GL.Vertex3(p.x + half, p.y - half, 0f);
            GL.Vertex3(p.x + half, p.y + half, 0f);
            GL.Vertex3(p.x - half, p.y + half, 0f);
        }

        // Rectangle de sélection en cours de tracé.
        void DrawSelectionRect()
        {
            Rect r = GetBoxRect();
            Material mat = LineMaterial;
            mat.SetPass(0);

            GL.Begin(GL.QUADS);
            GL.Color(new Color(0.25f, 0.65f, 1f, 0.12f));
            GL.Vertex3(r.xMin, r.yMin, 0f);
            GL.Vertex3(r.xMax, r.yMin, 0f);
            GL.Vertex3(r.xMax, r.yMax, 0f);
            GL.Vertex3(r.xMin, r.yMax, 0f);
            GL.End();

            Color border = new Color(0.25f, 0.65f, 1f, 0.9f);
            GL.Begin(GL.LINES);
            ClippedLine(new Vector2(r.xMin, r.yMin),
                new Vector2(r.xMax, r.yMin), border);
            ClippedLine(new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMax), border);
            ClippedLine(new Vector2(r.xMax, r.yMax),
                new Vector2(r.xMin, r.yMax), border);
            ClippedLine(new Vector2(r.xMin, r.yMax),
                new Vector2(r.xMin, r.yMin), border);
            GL.End();
        }

        // ------------------------------------------------------------------
        // Clip géométrique contre le rectangle du viewport
        // ------------------------------------------------------------------

        // Trace un segment rogné au rectangle (algorithme de Cohen-Sutherland).
        // À appeler entre GL.Begin(GL.LINES) et GL.End().
        void ClippedLine(Vector2 p0, Vector2 p1, Color color)
        {
            if (ClipSegment(ref p0, ref p1))
            {
                GL.Color(color);
                GL.Vertex3(p0.x, p0.y, 0f);
                GL.Vertex3(p1.x, p1.y, 0f);
            }
        }

        // Trace un triangle rogné au rectangle. Le polygone clippé (3 à 7
        // sommets) est émis en éventail. À appeler entre GL.Begin(GL.TRIANGLES)
        // et GL.End().
        void ClippedTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            // Cas rapide : triangle entièrement dans le rectangle.
            if (_viewRect.Contains(a) && _viewRect.Contains(b) &&
                _viewRect.Contains(c))
            {
                GL.Color(color);
                GL.Vertex3(a.x, a.y, 0f);
                GL.Vertex3(b.x, b.y, 0f);
                GL.Vertex3(c.x, c.y, 0f);
                return;
            }

            // Rejet rapide : boîte englobante hors du rectangle.
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            if (maxX < _viewRect.xMin || minX > _viewRect.xMax ||
                maxY < _viewRect.yMin || minY > _viewRect.yMax)
                return;

            // Découpage du triangle contre les 4 bords (Sutherland-Hodgman).
            _polyA.Clear();
            _polyA.Add(a); _polyA.Add(b); _polyA.Add(c);
            ClipPolygonToRect(_polyA, _polyB);
            if (_polyB.Count < 3)
                return;

            GL.Color(color);
            for (int i = 1; i < _polyB.Count - 1; i++)
            {
                GL.Vertex3(_polyB[0].x, _polyB[0].y, 0f);
                GL.Vertex3(_polyB[i].x, _polyB[i].y, 0f);
                GL.Vertex3(_polyB[i + 1].x, _polyB[i + 1].y, 0f);
            }
        }

        // Tampons réutilisés pour le découpage de polygone (évite les allocations).
        readonly List<Vector2> _polyA = new List<Vector2>(8);
        readonly List<Vector2> _polyB = new List<Vector2>(8);
        readonly List<Vector2> _polyTmp = new List<Vector2>(8);

        // Cohen-Sutherland : codes de région d'un point vis-à-vis du rectangle.
        const int InsideCode = 0, LeftCode = 1, RightCode = 2,
                  BottomCode = 4, TopCode = 8;

        int RegionCode(Vector2 p)
        {
            int code = InsideCode;
            if (p.x < _viewRect.xMin) code |= LeftCode;
            else if (p.x > _viewRect.xMax) code |= RightCode;
            if (p.y < _viewRect.yMin) code |= TopCode;       // y croît vers le bas
            else if (p.y > _viewRect.yMax) code |= BottomCode;
            return code;
        }

        // Rogne le segment au rectangle. Retourne false s'il est entièrement
        // hors champ.
        bool ClipSegment(ref Vector2 p0, ref Vector2 p1)
        {
            int c0 = RegionCode(p0);
            int c1 = RegionCode(p1);

            for (int guard = 0; guard < 16; guard++)
            {
                if ((c0 | c1) == 0)
                    return true;                 // les deux points dedans
                if ((c0 & c1) != 0)
                    return false;                // segment entièrement dehors

                int outCode = c0 != 0 ? c0 : c1;
                Vector2 p;

                if ((outCode & TopCode) != 0)
                {
                    float t = (_viewRect.yMin - p0.y) / (p1.y - p0.y);
                    p = new Vector2(p0.x + (p1.x - p0.x) * t, _viewRect.yMin);
                }
                else if ((outCode & BottomCode) != 0)
                {
                    float t = (_viewRect.yMax - p0.y) / (p1.y - p0.y);
                    p = new Vector2(p0.x + (p1.x - p0.x) * t, _viewRect.yMax);
                }
                else if ((outCode & RightCode) != 0)
                {
                    float t = (_viewRect.xMax - p0.x) / (p1.x - p0.x);
                    p = new Vector2(_viewRect.xMax, p0.y + (p1.y - p0.y) * t);
                }
                else // LeftCode
                {
                    float t = (_viewRect.xMin - p0.x) / (p1.x - p0.x);
                    p = new Vector2(_viewRect.xMin, p0.y + (p1.y - p0.y) * t);
                }

                if (outCode == c0)
                {
                    p0 = p;
                    c0 = RegionCode(p0);
                }
                else
                {
                    p1 = p;
                    c1 = RegionCode(p1);
                }
            }
            return false;
        }

        // Découpe un polygone convexe contre les 4 bords du rectangle.
        // Résultat écrit dans 'output'.
        void ClipPolygonToRect(List<Vector2> input, List<Vector2> output)
        {
            // Bord gauche.
            ClipPolygonToEdge(input, _polyTmp, 0);
            // Bord droit.
            ClipPolygonToEdge(_polyTmp, output, 1);
            // Bord haut.
            ClipPolygonToEdge(output, _polyTmp, 2);
            // Bord bas.
            ClipPolygonToEdge(_polyTmp, output, 3);
        }

        // edge : 0=gauche, 1=droit, 2=haut, 3=bas.
        void ClipPolygonToEdge(List<Vector2> input, List<Vector2> output, int edge)
        {
            output.Clear();
            int n = input.Count;
            if (n == 0)
                return;

            for (int i = 0; i < n; i++)
            {
                Vector2 cur = input[i];
                Vector2 prev = input[(i + n - 1) % n];
                bool curIn = EdgeInside(cur, edge);
                bool prevIn = EdgeInside(prev, edge);

                if (curIn)
                {
                    if (!prevIn)
                        output.Add(EdgeIntersect(prev, cur, edge));
                    output.Add(cur);
                }
                else if (prevIn)
                {
                    output.Add(EdgeIntersect(prev, cur, edge));
                }
            }
        }

        bool EdgeInside(Vector2 p, int edge)
        {
            switch (edge)
            {
                case 0: return p.x >= _viewRect.xMin;
                case 1: return p.x <= _viewRect.xMax;
                case 2: return p.y >= _viewRect.yMin;
                default: return p.y <= _viewRect.yMax;
            }
        }

        Vector2 EdgeIntersect(Vector2 p0, Vector2 p1, int edge)
        {
            float t;
            switch (edge)
            {
                case 0:
                    t = (_viewRect.xMin - p0.x) / (p1.x - p0.x);
                    return new Vector2(_viewRect.xMin, p0.y + (p1.y - p0.y) * t);
                case 1:
                    t = (_viewRect.xMax - p0.x) / (p1.x - p0.x);
                    return new Vector2(_viewRect.xMax, p0.y + (p1.y - p0.y) * t);
                case 2:
                    t = (_viewRect.yMin - p0.y) / (p1.y - p0.y);
                    return new Vector2(p0.x + (p1.x - p0.x) * t, _viewRect.yMin);
                default:
                    t = (_viewRect.yMax - p0.y) / (p1.y - p0.y);
                    return new Vector2(p0.x + (p1.x - p0.x) * t, _viewRect.yMax);
            }
        }

        // ------------------------------------------------------------------
        // Libellés
        // ------------------------------------------------------------------

        void DrawOverlayLabels(Rect rect, Vector2[] uvs, int[] triangles, int channel)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.8f, 0.8f, 0.82f, 1f) }
            };

            string info = $"Canal {UVChannelUtils.ChannelLabel(channel)}";
            if (uvs != null && triangles != null)
                info += $"  •  {triangles.Length / 3} faces  •  {uvs.Length} vertices";
            else
                info += "  •  (aucun mesh)";
            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f),
                info, style);

            // Repères de coordonnées aux coins de l'espace [0,1].
            DrawCornerLabel(new Vector2(0f, 0f), "0,0", rect, style);
            DrawCornerLabel(new Vector2(1f, 1f), "1,1", rect, style);

            var help = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerRight,
                normal = { textColor = new Color(0.55f, 0.55f, 0.58f, 1f) }
            };
            GUI.Label(new Rect(rect.x + 6f, rect.yMax - 18f, rect.width - 12f, 16f),
                "Molette : zoom  •  Clic molette / Alt+clic : déplacer", help);
        }

        void DrawCornerLabel(Vector2 uv, string text, Rect rect, GUIStyle style)
        {
            Vector2 s = UVToScreen(uv);
            // N'affiche le repère que si le coin est visible dans le rectangle.
            if (!rect.Contains(s))
                return;
            GUI.Label(new Rect(s.x + 3f, s.y - 14f, 48f, 14f), text, style);
        }

        // ------------------------------------------------------------------
        // Navigation (pan / zoom)
        // ------------------------------------------------------------------

        /// <summary>
        /// Traite les événements souris de navigation. Retourne vrai si
        /// l'événement a été consommé (l'appelant doit alors se redessiner).
        /// </summary>
        public bool HandleNavigation(Rect rect)
        {
            _viewRect = rect;
            Event e = Event.current;
            if (e == null)
                return false;

            bool used = false;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        // Zoom centré sur le curseur : l'UV sous la souris reste fixe.
                        Vector2 uvBefore = ScreenToUV(e.mousePosition);
                        float factor = e.delta.y > 0f ? 1f / ZoomStep : ZoomStep;
                        _pixelsPerUnit = Mathf.Clamp(_pixelsPerUnit * factor,
                            MinPixelsPerUnit, MaxPixelsPerUnit);
                        Vector2 uvAfter = ScreenToUV(e.mousePosition);
                        _panUV += uvBefore - uvAfter;
                        used = true;
                    }
                    break;

                case EventType.MouseDown:
                    // Clic molette, ou Alt+clic gauche : démarre un pan.
                    if (rect.Contains(e.mousePosition) &&
                        (e.button == 2 || (e.button == 0 && e.alt)))
                    {
                        _isPanning = true;
                        used = true;
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isPanning)
                    {
                        _panUV -= new Vector2(
                            e.delta.x / _pixelsPerUnit,
                            -e.delta.y / _pixelsPerUnit);
                        used = true;
                    }
                    break;

                case EventType.MouseUp:
                    if (_isPanning)
                    {
                        _isPanning = false;
                        used = true;
                    }
                    break;
            }

            if (used)
                e.Use();
            return used;
        }

        /// <summary>Recadre la vue sur l'espace [0,1] dans le rectangle donné.</summary>
        public void FrameUnitSquare(Rect rect)
        {
            _panUV = new Vector2(0.5f, 0.5f);
            float fit = Mathf.Min(rect.width, rect.height) * 0.9f;
            _pixelsPerUnit = Mathf.Clamp(fit, MinPixelsPerUnit, MaxPixelsPerUnit);
        }

        // ------------------------------------------------------------------
        // Interaction de sélection (clic / rectangle)
        // ------------------------------------------------------------------

        bool _isBoxSelecting;
        Vector2 _boxStart;
        Vector2 _boxEnd;

        // Seuil px en deçà duquel un drag est traité comme un clic.
        const float ClickThreshold = 3f;
        // Tolérance px pour cliquer un vertex.
        const float VertexPickRadius = 7f;

        Rect GetBoxRect()
        {
            float xMin = Mathf.Min(_boxStart.x, _boxEnd.x);
            float yMin = Mathf.Min(_boxStart.y, _boxEnd.y);
            float xMax = Mathf.Max(_boxStart.x, _boxEnd.x);
            float yMax = Mathf.Max(_boxStart.y, _boxEnd.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// Traite les événements de sélection dans le viewport. Modifie
        /// directement <paramref name="selection"/>. Retourne vrai si la
        /// sélection a changé. <paramref name="committed"/> indique une fin de
        /// geste (l'appelant doit alors figer un cran d'historique).
        ///
        /// À appeler APRÈS HandleNavigation : si la navigation a consommé
        /// l'événement, la sélection n'est pas traitée.
        /// </summary>
        public bool HandleSelectionInput(Rect rect, Vector2[] uvs, int[] triangles,
            UVSelection selection, UVSelectionMode mode, out bool committed)
        {
            _viewRect = rect;
            committed = false;

            Event e = Event.current;
            if (e == null || uvs == null || triangles == null || selection == null)
                return false;
            // La navigation est prioritaire (pan en cours).
            if (e.alt || e.button == 2)
                return false;

            bool changed = false;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        _isBoxSelecting = true;
                        _boxStart = e.mousePosition;
                        _boxEnd = e.mousePosition;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isBoxSelecting && e.button == 0)
                    {
                        _boxEnd = e.mousePosition;
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isBoxSelecting && e.button == 0)
                    {
                        _isBoxSelecting = false;
                        _boxEnd = e.mousePosition;
                        Rect box = GetBoxRect();
                        bool isClick = box.width < ClickThreshold &&
                                       box.height < ClickThreshold;

                        if (isClick)
                            changed = ClickSelect(e, uvs, triangles, selection, mode);
                        else
                            changed = RectSelect(e, box, uvs, triangles,
                                selection, mode);

                        committed = true;   // fin de geste
                        e.Use();
                    }
                    break;
            }

            return changed;
        }

        static bool IsAdd(Event e) => e.shift;
        static bool IsRemove(Event e) => e.control || e.command;

        // Sélection au clic : vertex le plus proche (mode Vertices) ou face
        // sous le curseur (mode Faces).
        bool ClickSelect(Event e, Vector2[] uvs, int[] triangles,
            UVSelection selection, UVSelectionMode mode)
        {
            bool add = IsAdd(e), remove = IsRemove(e);
            Vector2 mouse = e.mousePosition;

            if (mode == UVSelectionMode.Vertices)
            {
                int best = PickVertex(mouse, uvs);
                if (best < 0)
                {
                    if (!add && !remove && !selection.IsEmpty)
                    {
                        selection.Clear();
                        return true;
                    }
                    return false;
                }
                if (!add && !remove)
                    selection.Clear();
                if (remove)
                    selection.RemoveVertex(best);
                else
                    selection.AddVertex(best);
                return true;
            }

            // Mode Faces : triangle contenant le curseur.
            int tri = PickTriangle(mouse, uvs, triangles);
            if (tri < 0)
            {
                if (!add && !remove && !selection.IsEmpty)
                {
                    selection.Clear();
                    return true;
                }
                return false;
            }
            if (!add && !remove)
                selection.Clear();
            if (remove)
                selection.RemoveTriangle(tri);
            else
                selection.AddTriangle(tri);
            return true;
        }

        // Sélection rectangle.
        bool RectSelect(Event e, Rect box, Vector2[] uvs, int[] triangles,
            UVSelection selection, UVSelectionMode mode)
        {
            bool add = IsAdd(e), remove = IsRemove(e);
            if (!add && !remove)
                selection.Clear();

            bool changed = !add && !remove;   // un clear compte comme changement

            if (mode == UVSelectionMode.Vertices)
            {
                for (int v = 0; v < uvs.Length; v++)
                {
                    Vector2 p = UVToScreen(uvs[v]);
                    if (!box.Contains(p))
                        continue;
                    if (remove) selection.RemoveVertex(v);
                    else selection.AddVertex(v);
                    changed = true;
                }
            }
            else
            {
                // Mode Faces : centroïde du triangle dans le rectangle.
                int triCount = triangles.Length / 3;
                for (int t = 0; t < triCount; t++)
                {
                    int i = t * 3;
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    if (a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                        continue;
                    Vector2 centroid = UVToScreen(
                        (uvs[a] + uvs[b] + uvs[c]) / 3f);
                    if (!box.Contains(centroid))
                        continue;
                    if (remove) selection.RemoveTriangle(t);
                    else selection.AddTriangle(t);
                    changed = true;
                }
            }

            return changed;
        }

        // Vertex le plus proche du point écran, dans le rayon de pick. -1 sinon.
        int PickVertex(Vector2 mouse, Vector2[] uvs)
        {
            int best = -1;
            float bestSqr = VertexPickRadius * VertexPickRadius;
            for (int v = 0; v < uvs.Length; v++)
            {
                Vector2 p = UVToScreen(uvs[v]);
                if (!_viewRect.Contains(p))
                    continue;
                float d = (p - mouse).sqrMagnitude;
                if (d <= bestSqr)
                {
                    bestSqr = d;
                    best = v;
                }
            }
            return best;
        }

        // Triangle contenant le point écran (le dernier trouvé, donc celui
        // dessiné en dernier / au-dessus). -1 sinon.
        int PickTriangle(Vector2 mouse, Vector2[] uvs, int[] triangles)
        {
            int found = -1;
            int triCount = triangles.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i = t * 3;
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                    continue;
                if (PointInTriangle(mouse,
                        UVToScreen(uvs[a]), UVToScreen(uvs[b]), UVToScreen(uvs[c])))
                    found = t;
            }
            return found;
        }

        // Test point dans triangle (signe des produits vectoriels).
        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b);
            float d2 = Cross(p, b, c);
            float d3 = Cross(p, c, a);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b)
        {
            return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }
    }
}
