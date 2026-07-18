using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Pilote l'interaction dans la SceneView selon la catégorie active :
    ///  - Paint  : le clic gauche peint (brush sphérique, masqué par la sélection).
    ///  - Select : le clic gauche sélectionne (clic = point, clic+drag = rectangle),
    ///             sur les vertices ou les faces selon le mode Select courant.
    /// Gère aussi le rendu GL de la preview, des points / faces sélectionnés et
    /// des overlays.
    /// </summary>
    public class VertexColorSceneController
    {
        const string PreviewShaderName = "Hidden/VertexColorEditor/Preview";
        const float BoxThreshold = 3f; // px : en-deçà, un drag est traité comme un clic.
        const float GradientSnapDegrees = 5f; // pas de snap angulaire (Maj + gradient).

        readonly VertexColorEditorWindow _window;

        static Material _previewMaterial;
        static Material _overlayMaterial;

        VertexColorPainter.RayHit _currentHit;
        bool _isPaintStroke;
        bool[] _strokeMask;          // masque figé au début d'un trait de peinture
        bool _isBoxSelecting;
        Vector2 _boxStart;
        Vector2 _boxEnd;
        bool _isGradientDragging;
        Vector3 _gradientStart;      // origine monde (impact du raycast au clic)
        Vector2 _gradientStartGui;   // origine en espace écran (GUI)
        Vector2 _gradientEndGui;     // extrémité en espace écran (GUI)
        Camera _sceneCamera;         // caméra de la SceneView courante

        public VertexColorSceneController(VertexColorEditorWindow window)
        {
            _window = window;
        }

        public void Enable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        public void Disable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        // ----------------------------------------------------------------------
        // Matériaux
        // ----------------------------------------------------------------------

        static Material PreviewMaterial
        {
            get
            {
                if (_previewMaterial == null)
                {
                    var shader = Shader.Find(PreviewShaderName);
                    if (shader != null)
                    {
                        _previewMaterial = new Material(shader)
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }
                }
                return _previewMaterial;
            }
        }

        // Matériau coloré (toujours visible) pour les overlays de sélection.
        static Material OverlayMaterial
        {
            get
            {
                if (_overlayMaterial == null)
                {
                    var shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader != null)
                    {
                        _overlayMaterial = new Material(shader)
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        _overlayMaterial.SetInt("_ZWrite", 0);
                        _overlayMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                        _overlayMaterial.SetInt("_Cull", (int)CullMode.Off);
                        _overlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                        _overlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    }
                }
                return _overlayMaterial;
            }
        }

        // ----------------------------------------------------------------------
        // Boucle SceneView
        // ----------------------------------------------------------------------

        void OnSceneGUI(SceneView sceneView)
        {
            if (!_window.IsActive || !_window.HasWorkingMesh)
                return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            ActiveCategory category = _window.Category;
            _sceneCamera = sceneView.camera;

            // Nettoie les états de stroke incohérents avec la catégorie courante.
            if (category != ActiveCategory.Paint)
            {
                _isPaintStroke = false;
                _isGradientDragging = false;
            }
            if (category != ActiveCategory.Select)
                _isBoxSelecting = false;

            // Raycast pour le survol (peinture, ou clic simple de sélection).
            if ((e.type == EventType.MouseMove || e.type == EventType.MouseDrag ||
                 e.type == EventType.MouseDown))
            {
                UpdateHit(e);
            }

            switch (e.type)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.Repaint:
                    DrawMeshPreview();
                    DrawSelectionOverlay(sceneView);
                    DrawCursorOverlay(category);
                    break;
            }

            if (category == ActiveCategory.Paint)
                HandlePaintInput(e);
            else if (category == ActiveCategory.Select)
                HandleSelectInput(e, sceneView);

            if (e.type == EventType.MouseMove)
                sceneView.Repaint();
        }

        void UpdateHit(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            _currentHit = _window.Painter.Raycast(ray, _window.Preview.LocalToWorld);
        }

        static bool IsAdd(Event e) => e.shift;
        static bool IsRemove(Event e) => e.control || e.command;

        // ----------------------------------------------------------------------
        // Catégorie Paint
        // ----------------------------------------------------------------------

        void HandlePaintInput(Event e)
        {
            switch (_window.PaintMode)
            {
                case PaintMode.Brush:    HandleStrokeInput(e, false); break;
                case PaintMode.Smooth:   HandleStrokeInput(e, true); break;
                case PaintMode.Gradient: HandleGradientInput(e); break;
                case PaintMode.Fill:     break; // déclenché par le bouton du panneau
            }
        }

        // Trait continu : Brush ou Smooth (squelette commun, application différente).
        void HandleStrokeInput(Event e, bool smooth)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && _currentHit.hit)
                    {
                        BeginPaintStroke(smooth);
                        ApplyStroke(smooth);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button == 0 && !e.alt && _isPaintStroke)
                    {
                        ApplyStroke(smooth);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0 && _isPaintStroke)
                    {
                        _isPaintStroke = false;
                        _strokeMask = null;
                        e.Use();
                    }
                    break;
            }
        }

        void BeginPaintStroke(bool smooth)
        {
            _isPaintStroke = true;
            // Le masque est figé au début du trait pour rester stable pendant le drag.
            _strokeMask = _window.BuildPaintMask();
            // Un seul point d'undo par trait.
            Undo.RegisterCompleteObjectUndo(_window.WorkingMesh,
                smooth ? "Smooth Vertex Colors" : "Paint Vertex Colors");
        }

        void ApplyStroke(bool smooth)
        {
            if (!_currentHit.hit)
                return;

            bool changed;
            if (smooth)
            {
                changed = _window.Painter.ApplySmooth(
                    _window.Colors, _window.Preview.LocalToWorld, _currentHit.pointWorld,
                    _window.SmoothBrush, _window.GetChannelMask(), 1f, _strokeMask,
                    _window.Topology);
            }
            else
            {
                changed = _window.Painter.ApplyBrush(
                    _window.Colors, _window.Preview.LocalToWorld, _currentHit.pointWorld,
                    _window.Brush, _window.GetChannelMask(), 1f, _strokeMask);
            }

            if (changed)
            {
                _window.PushColorsToMesh();
                SceneView.RepaintAll();
            }
        }

        // --- Gradient : trace une ligne en espace écran, applique au relâchement ---
        void HandleGradientInput(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && _currentHit.hit)
                    {
                        _isGradientDragging = true;
                        // Origine ancrée sur l'impact du raycast (point monde).
                        _gradientStart = _currentHit.pointWorld;
                        _gradientStartGui = e.mousePosition;
                        _gradientEndGui = e.mousePosition;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button == 0 && !e.alt && _isGradientDragging)
                    {
                        // L'extrémité suit le curseur en espace écran (GUI).
                        // Maj : l'angle de la ligne est snappé par pas de 5°.
                        _gradientEndGui = e.shift
                            ? SnapGuiAngle(_gradientStartGui, e.mousePosition,
                                GradientSnapDegrees)
                            : e.mousePosition;
                        SceneView.RepaintAll();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0 && _isGradientDragging)
                    {
                        _isGradientDragging = false;
                        if ((_gradientEndGui - _gradientStartGui).sqrMagnitude > 1f)
                            _window.ApplyGradientLineScreen(_sceneCamera,
                                _gradientStart, _gradientStartGui, _gradientEndGui);
                        e.Use();
                    }
                    break;
            }
        }

        // Snappe la position 'end' pour que l'angle du segment start->end soit un
        // multiple de stepDegrees, en conservant la longueur du segment.
        static Vector2 SnapGuiAngle(Vector2 start, Vector2 end, float stepDegrees)
        {
            Vector2 d = end - start;
            float length = d.magnitude;
            if (length < 1e-4f)
                return end;

            // Atan2 en repère GUI (y vers le bas) ; le snap reste correct car on
            // ré-applique le même repère pour reconstruire le point.
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float step = Mathf.Max(0.01f, stepDegrees);
            float snapped = Mathf.Round(angle / step) * step * Mathf.Deg2Rad;

            return start + new Vector2(Mathf.Cos(snapped), Mathf.Sin(snapped)) * length;
        }

        // ----------------------------------------------------------------------
        // Catégorie Select
        // ----------------------------------------------------------------------

        void HandleSelectInput(Event e, SceneView sv)
        {
            SelectMode mode = _window.SelectMode;
            if (mode == SelectMode.Free)
                return; // Free : aucun outil de clic.

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt)
                    {
                        _isBoxSelecting = true;
                        _boxStart = e.mousePosition;
                        _boxEnd = e.mousePosition;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button == 0 && !e.alt && _isBoxSelecting)
                    {
                        _boxEnd = e.mousePosition;
                        sv.Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0 && _isBoxSelecting)
                    {
                        _isBoxSelecting = false;
                        _boxEnd = e.mousePosition;
                        Rect rect = GetBoxRect();
                        bool isClick = rect.width < BoxThreshold && rect.height < BoxThreshold;

                        if (mode == SelectMode.Vertices)
                        {
                            if (isClick) ClickSelectVertex(e);
                            else RectSelectVertices(sv, rect, e);
                        }
                        else // Faces
                        {
                            if (isClick) ClickSelectFace(e);
                            else RectSelectFaces(sv, rect, e);
                        }
                        e.Use();
                    }
                    break;
            }
        }

        // --- Sélection de vertices ---

        // Clic : sélectionne le vertex le plus proche du point cliqué (face touchée).
        // Clic dans le vide : désélectionne (en mode remplacer, sans modificateur).
        void ClickSelectVertex(Event e)
        {
            var sel = _window.Selection;
            bool add = IsAdd(e), remove = IsRemove(e);

            if (!_currentHit.hit)
            {
                if (!add && !remove)
                {
                    sel.Clear();
                    AfterSelectionChanged();
                }
                return;
            }

            _window.Painter.GetTriangleVertices(_currentHit.triangleIndex,
                out int v0, out int v1, out int v2);
            Vector3[] verts = _window.PreviewVertices;
            Matrix4x4 l2w = _window.Preview.LocalToWorld;

            int nearest = v0;
            float best = float.MaxValue;
            int[] candidates = { v0, v1, v2 };
            for (int k = 0; k < 3; k++)
            {
                float d = (l2w.MultiplyPoint3x4(verts[candidates[k]])
                           - _currentHit.pointWorld).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = candidates[k];
                }
            }

            if (!add && !remove)
                sel.Clear();
            if (remove)
                sel.Remove(nearest);
            else
                sel.Add(nearest);

            AfterSelectionChanged();
        }

        void RectSelectVertices(SceneView sv, Rect rect, Event e)
        {
            var sel = _window.Selection;
            Vector3[] verts = _window.PreviewVertices;
            int[] tris = _window.PreviewTriangles;
            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            Camera cam = sv.camera;
            bool add = IsAdd(e), remove = IsRemove(e);

            if (!add && !remove)
                sel.Clear();

            // Backface cull : un vertex est conservé s'il appartient à au moins
            // une face tournée vers la caméra. Occlusion : test indépendant.
            bool cull = _window.BackfaceCull;
            bool occ = _window.Occlusion;
            bool[] frontFacing = cull
                ? ComputeVertexFrontFacing(verts, tris, l2w, cam,
                    _window.BackfaceThresholdDot)
                : null;

            // Grille d'occlusion (accélère le test de visibilité).
            ScreenOcclusionGrid occlusion = null;
            if (occ)
            {
                occlusion = new ScreenOcclusionGrid();
                occlusion.Build(verts, tris, l2w, cam);
            }

            for (int i = 0; i < verts.Length; i++)
            {
                if (cull && !frontFacing[i])
                    continue;
                Vector3 wp = l2w.MultiplyPoint3x4(verts[i]);
                if (cam.WorldToViewportPoint(wp).z <= 0f)
                    continue;
                if (!rect.Contains(HandleUtility.WorldToGUIPoint(wp)))
                    continue;
                // Occlusion : le vertex est ignoré s'il est masqué par la géométrie.
                if (occ && occlusion.IsOccluded(wp))
                    continue;
                if (remove)
                    sel.Remove(i);
                else
                    sel.Add(i);
            }

            AfterSelectionChanged();
        }

        // --- Sélection de faces ---

        // Clic : sélectionne la face sous le curseur.
        // Clic dans le vide : désélectionne (en mode remplacer, sans modificateur).
        void ClickSelectFace(Event e)
        {
            var sel = _window.FaceSelection;
            bool add = IsAdd(e), remove = IsRemove(e);

            if (!_currentHit.hit)
            {
                if (!add && !remove)
                {
                    sel.Clear();
                    AfterSelectionChanged();
                }
                return;
            }

            if (!add && !remove)
                sel.Clear();
            if (remove)
                sel.Remove(_currentHit.triangleIndex);
            else
                sel.Add(_currentHit.triangleIndex);

            AfterSelectionChanged();
        }

        void RectSelectFaces(SceneView sv, Rect rect, Event e)
        {
            var sel = _window.FaceSelection;
            Vector3[] verts = _window.PreviewVertices;
            int[] tris = _window.PreviewTriangles;
            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            Camera cam = sv.camera;
            bool add = IsAdd(e), remove = IsRemove(e);
            bool cull = _window.BackfaceCull;
            bool occ = _window.Occlusion;

            if (!add && !remove)
                sel.Clear();

            // Grille d'occlusion (accélère le test de visibilité).
            ScreenOcclusionGrid occlusion = null;
            if (occ)
            {
                occlusion = new ScreenOcclusionGrid();
                occlusion.Build(verts, tris, l2w, cam);
            }

            // Une face est prise si son centroïde tombe dans le rectangle.
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = verts[tris[t]];
                Vector3 b = verts[tris[t + 1]];
                Vector3 c = verts[tris[t + 2]];

                // Backface cull : on ignore les faces dos à la caméra.
                if (cull && !IsFaceFrontFacing(a, b, c, l2w, cam,
                        _window.BackfaceThresholdDot))
                    continue;

                Vector3 wp = l2w.MultiplyPoint3x4((a + b + c) / 3f);
                if (cam.WorldToViewportPoint(wp).z <= 0f)
                    continue;
                if (!rect.Contains(HandleUtility.WorldToGUIPoint(wp)))
                    continue;
                // Occlusion : on ignore les faces masquées par la géométrie.
                if (occ && occlusion.IsOccluded(wp))
                    continue;
                int triIndex = t / 3;
                if (remove)
                    sel.Remove(triIndex);
                else
                    sel.Add(triIndex);
            }

            AfterSelectionChanged();
        }

        // --- Backface culling ---

        // Une face est tournée vers la caméra si l'angle entre sa normale sortante
        // et la direction caméra est suffisamment faible : dot(normale, dirCaméra)
        // >= thresholdDot. thresholdDot = 0 -> ne rejette que le vrai dos ;
        // valeur croissante -> rejette aussi les faces de plus en plus rasantes.
        static bool IsFaceFrontFacing(Vector3 v0, Vector3 v1, Vector3 v2,
            Matrix4x4 l2w, Camera cam, float thresholdDot)
        {
            Vector3 nWorld = l2w.MultiplyVector(Vector3.Cross(v1 - v0, v2 - v0))
                .normalized;
            Vector3 toCamera;
            if (cam.orthographic)
            {
                toCamera = -cam.transform.forward;
            }
            else
            {
                Vector3 centroidWorld = l2w.MultiplyPoint3x4((v0 + v1 + v2) / 3f);
                toCamera = (cam.transform.position - centroidWorld).normalized;
            }
            return Vector3.Dot(nWorld, toCamera) >= thresholdDot;
        }

        // Marque true tout vertex appartenant à au moins une face front-facing.
        static bool[] ComputeVertexFrontFacing(Vector3[] verts, int[] tris,
            Matrix4x4 l2w, Camera cam, float thresholdDot)
        {
            bool[] front = new bool[verts.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                if (IsFaceFrontFacing(verts[i0], verts[i1], verts[i2], l2w, cam,
                        thresholdDot))
                {
                    front[i0] = true;
                    front[i1] = true;
                    front[i2] = true;
                }
            }
            return front;
        }

        void AfterSelectionChanged()
        {
            _window.Repaint();
            SceneView.RepaintAll();
        }

        Rect GetBoxRect()
        {
            float xMin = Mathf.Min(_boxStart.x, _boxEnd.x);
            float yMin = Mathf.Min(_boxStart.y, _boxEnd.y);
            float xMax = Mathf.Max(_boxStart.x, _boxEnd.x);
            float yMax = Mathf.Max(_boxStart.y, _boxEnd.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        // ----------------------------------------------------------------------
        // Rendu de la preview
        // ----------------------------------------------------------------------

        void DrawMeshPreview()
        {
            // Mode "Show Material" : le mesh est rendu par son MeshRenderer via le
            // pipeline ; pas de rendu GL de la preview vertex colors.
            if (_window.Preview.IsMaterialPreviewActive)
                return;

            Color[] colors = _window.Colors;
            Vector3[] verts = _window.PreviewVertices;
            int[] tris = _window.PreviewTriangles;

            if (colors == null || verts == null || tris == null)
                return;
            if (colors.Length != verts.Length)
                return;

            Material mat = PreviewMaterial;
            if (mat == null)
            {
                Debug.LogError(
                    "[VertexColorEditor] Shader de preview introuvable : " + PreviewShaderName);
                return;
            }

            ViewChannelMode mode = _window.ViewChannel;
            mat.SetFloat("_UseChecker", mode == ViewChannelMode.A ? 0f : 1f);

            mat.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(_window.Preview.LocalToWorld);
            GL.Begin(GL.TRIANGLES);
            for (int i = 0; i < tris.Length; i += 3)
            {
                EmitVertex(verts, colors, tris[i], mode);
                EmitVertex(verts, colors, tris[i + 1], mode);
                EmitVertex(verts, colors, tris[i + 2], mode);
            }
            GL.End();
            GL.PopMatrix();
        }

        static void EmitVertex(Vector3[] verts, Color[] colors, int index, ViewChannelMode mode)
        {
            GL.Color(ResolveContentColor(colors[index], mode));
            GL.Vertex(verts[index]);
        }

        static Color ResolveContentColor(Color c, ViewChannelMode mode)
        {
            switch (mode)
            {
                case ViewChannelMode.R: return new Color(c.r, 0f, 0f, c.a);
                case ViewChannelMode.G: return new Color(0f, c.g, 0f, c.a);
                case ViewChannelMode.B: return new Color(0f, 0f, c.b, c.a);
                case ViewChannelMode.A: return new Color(1f, 1f, 1f, c.a);
                default:                return new Color(c.r, c.g, c.b, c.a); // RGB
            }
        }

        // ----------------------------------------------------------------------
        // Rendu des overlays de sélection
        // ----------------------------------------------------------------------

        // La sélection (vertices ou faces) est affichée dès qu'elle est non vide,
        // quelle que soit la catégorie active : le masque reste ainsi toujours visible.
        void DrawSelectionOverlay(SceneView sv)
        {
            SelectMode mode = _window.SelectMode;
            if (mode == SelectMode.Faces)
                DrawSelectedFaces();
            else if (mode == SelectMode.Vertices)
                DrawSelectedVertices(sv);
            // Free : rien (aucun masque).
        }

        void DrawSelectedVertices(SceneView sv)
        {
            var sel = _window.Selection;
            if (sel == null || sel.IsEmpty)
                return;

            Vector3[] verts = _window.PreviewVertices;
            Material mat = OverlayMaterial;
            if (verts == null || mat == null)
                return;

            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            Camera cam = sv.camera;
            Vector3 camRight = cam.transform.right;
            Vector3 camUp = cam.transform.up;

            float size = HandleUtility.GetHandleSize(l2w.MultiplyPoint3x4(Vector3.zero)) * 0.03f;
            Vector3 r = camRight * size;
            Vector3 u = camUp * size;

            mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(new Color(1f, 0.85f, 0.15f, 0.95f));
            foreach (int i in sel.Indices)
            {
                if (i < 0 || i >= verts.Length)
                    continue;
                Vector3 wp = l2w.MultiplyPoint3x4(verts[i]);
                GL.Vertex(wp - r - u);
                GL.Vertex(wp - r + u);
                GL.Vertex(wp + r + u);
                GL.Vertex(wp + r - u);
            }
            GL.End();
            GL.PopMatrix();
        }

        void DrawSelectedFaces()
        {
            var sel = _window.FaceSelection;
            if (sel == null || sel.IsEmpty)
                return;

            Vector3[] verts = _window.PreviewVertices;
            int[] tris = _window.PreviewTriangles;
            Material mat = OverlayMaterial;
            if (verts == null || tris == null || mat == null)
                return;

            Matrix4x4 l2w = _window.Preview.LocalToWorld;

            mat.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(l2w);
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1f, 0.85f, 0.15f, 0.35f));
            foreach (int tri in sel.Indices)
            {
                int t = tri * 3;
                if (t + 2 >= tris.Length)
                    continue;
                GL.Vertex(verts[tris[t]]);
                GL.Vertex(verts[tris[t + 1]]);
                GL.Vertex(verts[tris[t + 2]]);
            }
            GL.End();
            GL.PopMatrix();
        }

        // ----------------------------------------------------------------------
        // Overlays curseur
        // ----------------------------------------------------------------------

        void DrawCursorOverlay(ActiveCategory category)
        {
            if (category == ActiveCategory.Paint)
            {
                switch (_window.PaintMode)
                {
                    case PaintMode.Brush:    DrawBrushDisc(_window.Brush.radius); break;
                    case PaintMode.Smooth:   DrawBrushDisc(_window.SmoothBrush.radius); break;
                    case PaintMode.Gradient: DrawGradientLine(); break;
                    case PaintMode.Fill:     break;
                }
            }
            else if (category == ActiveCategory.Select)
            {
                SelectMode mode = _window.SelectMode;
                if (mode == SelectMode.Free)
                    return;
                if (_isBoxSelecting)
                    DrawBoxRect();
                else if (mode == SelectMode.Faces)
                    DrawHoveredFace();
                else
                    DrawHoveredVertex();
            }
        }

        void DrawBrushDisc(float radius)
        {
            if (!_currentHit.hit)
                return;

            Handles.color = new Color(1f, 0.45f, 0.1f, 1f);
            Handles.DrawWireDisc(_currentHit.pointWorld, _currentHit.normalWorld, radius);
            Handles.color = new Color(1f, 0.45f, 0.1f, 0.25f);
            Handles.DrawSolidDisc(_currentHit.pointWorld, _currentHit.normalWorld, radius * 0.12f);
        }

        // Ligne du dégradé : tracé en cours (espace écran), ou ligne persistée
        // (espace monde) réutilisable.
        void DrawGradientLine()
        {
            if (_isGradientDragging)
            {
                DrawGradientGizmoGui(_gradientStartGui, _gradientEndGui);
            }
            else if (_window.HasGradientLine)
            {
                DrawGradientGizmo(_window.GradientStart, _window.GradientEnd);
            }
        }

        // Tracé en cours : ligne dessinée directement en coordonnées écran (GUI).
        void DrawGradientGizmoGui(Vector2 a, Vector2 b)
        {
            Handles.BeginGUI();
            Vector3 a3 = new Vector3(a.x, a.y, 0f);
            Vector3 b3 = new Vector3(b.x, b.y, 0f);
            Handles.color = new Color(1f, 0.45f, 0.1f, 1f);
            Handles.DrawAAPolyLine(5f, a3, b3);
            Handles.color = Color.black;
            Handles.DrawSolidDisc(a3, Vector3.forward, 5f);
            Handles.color = Color.white;
            Handles.DrawSolidDisc(b3, Vector3.forward, 5f);
            Handles.EndGUI();
        }

        void DrawGradientGizmo(Vector3 a, Vector3 b)
        {
            Handles.color = new Color(1f, 0.45f, 0.1f, 1f);
            Handles.DrawAAPolyLine(5f, a, b);
            // Repères d'extrémités : début (sombre, t=0) et fin (clair, t=1).
            Handles.color = Color.black;
            Handles.SphereHandleCap(0, a, Quaternion.identity,
                HandleUtility.GetHandleSize(a) * 0.06f, EventType.Repaint);
            Handles.color = Color.white;
            Handles.SphereHandleCap(0, b, Quaternion.identity,
                HandleUtility.GetHandleSize(b) * 0.06f, EventType.Repaint);
        }

        void DrawHoveredFace()
        {
            if (!_currentHit.hit)
                return;

            _window.Painter.GetTriangleVertices(_currentHit.triangleIndex,
                out int v0, out int v1, out int v2);
            Vector3[] verts = _window.PreviewVertices;
            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            Vector3 p0 = l2w.MultiplyPoint3x4(verts[v0]);
            Vector3 p1 = l2w.MultiplyPoint3x4(verts[v1]);
            Vector3 p2 = l2w.MultiplyPoint3x4(verts[v2]);

            Handles.color = new Color(1f, 0.45f, 0.1f, 1f);
            Handles.DrawAAPolyLine(4f, p0, p1, p2, p0);
        }

        void DrawHoveredVertex()
        {
            if (!_currentHit.hit)
                return;

            Handles.color = new Color(1f, 0.45f, 0.1f, 1f);
            float size = HandleUtility.GetHandleSize(_currentHit.pointWorld) * 0.04f;
            Handles.DrawSolidDisc(_currentHit.pointWorld, _currentHit.normalWorld, size);
        }

        void DrawBoxRect()
        {
            Handles.BeginGUI();
            Rect r = GetBoxRect();
            EditorGUI.DrawRect(r, new Color(1f, 0.85f, 0.15f, 0.12f));
            Color border = new Color(1f, 0.85f, 0.15f, 0.9f);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, 1f), border);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - 1f, r.width, 1f), border);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, 1f, r.height), border);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.yMin, 1f, r.height), border);
            Handles.EndGUI();
        }
    }
}
