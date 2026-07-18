using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Pilote l'interaction et le rendu dans la SceneView pour l'UV Editor :
    ///  - rendu GL de la preview "checker UV" (canal courant) ;
    ///  - sélection de faces (clic, clic+drag rectangle), reprise du Vertex
    ///    Color Editor, en portée "Selected Faces" ;
    ///  - gizmo du cadre de projection (box / cylindre / sphère) et poignées de
    ///    manipulation (déplacer / tourner / redimensionner).
    /// </summary>
    public class UVEditorSceneController
    {
        const string CheckerShaderName = "Hidden/UVEditor/CheckerPreview";
        const float BoxThreshold = 3f; // px : en-deçà, un drag est traité comme un clic.

        readonly UVEditorWindow _window;

        static Material _checkerMaterial;
        static Material _overlayMaterial;

        UVMeshRaycaster.RayHit _currentHit;
        bool _isBoxSelecting;
        Vector2 _boxStart;
        Vector2 _boxEnd;

        public UVEditorSceneController(UVEditorWindow window)
        {
            _window = window;
        }

        public void Enable() => SceneView.duringSceneGui += OnSceneGUI;
        public void Disable() => SceneView.duringSceneGui -= OnSceneGUI;

        // ------------------------------------------------------------------
        // Matériaux
        // ------------------------------------------------------------------

        static Material CheckerMaterial
        {
            get
            {
                if (_checkerMaterial == null)
                {
                    var shader = Shader.Find(CheckerShaderName);
                    if (shader != null)
                        _checkerMaterial = new Material(shader)
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                }
                return _checkerMaterial;
            }
        }

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

        // ------------------------------------------------------------------
        // Boucle SceneView
        // ------------------------------------------------------------------

        void OnSceneGUI(SceneView sceneView)
        {
            if (!_window.HasWorkingMesh)
                return;

            Event e = Event.current;
            // Capturé tôt : les poignées / la sélection peuvent consommer
            // l'événement (e.Use()) avant le commit de fin de geste.
            EventType rawType = e.type;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            // La sélection (faces ou vertices) est toujours active en 3D :
            // elle alimente la synchronisation 2D/3D et les transformations.
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag ||
                e.type == EventType.MouseDown)
            {
                UpdateHit(e);
            }

            switch (e.type)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.Repaint:
                    DrawCheckerPreview();
                    DrawSelectionOverlay();
                    DrawProjectionGizmo();
                    if (_isBoxSelecting)
                        DrawBoxRect();
                    else
                        DrawHover();
                    break;
            }

            // Les poignées du cadre consomment l'événement si elles sont saisies.
            DrawProjectionFrameHandles();

            HandleSelectionInput(e, sceneView);

            // Fin d'un geste de gizmo : on fige le résultat (commit unique pour
            // tout le drag). La sélection, elle, committe déjà via son propre
            // chemin discret.
            if (rawType == EventType.MouseUp)
                _window.CommitIfPending();

            if (e.type == EventType.MouseMove)
                sceneView.Repaint();
        }

        void UpdateHit(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            _currentHit = _window.Raycaster.Raycast(ray, _window.Preview.LocalToWorld);
        }

        static bool IsAdd(Event e) => e.shift;
        static bool IsRemove(Event e) => e.control || e.command;

        // ------------------------------------------------------------------
        // Rendu de la preview checker
        // ------------------------------------------------------------------

        void DrawCheckerPreview()
        {
            // Mode "Show Material" : le mesh est rendu par son MeshRenderer.
            if (_window.Preview.IsMaterialPreviewActive)
                return;

            Vector3[] verts = _window.PreviewVertices;
            int[] tris = _window.PreviewTriangles;
            Vector2[] uvs = _window.ChannelUVs;

            if (verts == null || tris == null || uvs == null)
                return;
            if (uvs.Length != verts.Length)
                return;

            Material mat = CheckerMaterial;
            if (mat == null)
            {
                Debug.LogError("[UV Editor] Shader de preview introuvable : "
                               + CheckerShaderName);
                return;
            }

            mat.SetFloat("_Tiling", Mathf.Max(1f, _window.CheckerTiling));
            mat.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(_window.Preview.LocalToWorld);
            GL.Begin(GL.TRIANGLES);
            for (int i = 0; i < tris.Length; i++)
            {
                int v = tris[i];
                Vector2 uv = uvs[v];
                GL.TexCoord3(uv.x, uv.y, 0f);
                GL.Vertex(verts[v]);
            }
            GL.End();
            GL.PopMatrix();
        }

        void DrawSelectionOverlay()
        {
            UVSelection sel = _window.Selection;
            if (sel == null || sel.IsEmpty)
                return;

            Vector3[] verts = _window.PreviewVertices;
            int[] tris = _window.PreviewTriangles;
            Material mat = OverlayMaterial;
            if (verts == null || tris == null || mat == null)
                return;

            Matrix4x4 l2w = _window.Preview.LocalToWorld;

            // Faces entièrement sélectionnées : remplissage.
            mat.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(l2w);
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(0.25f, 0.65f, 1f, 0.35f));
            int triCount = tris.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                if (!sel.IsTriangleSelected(t))
                    continue;
                int i = t * 3;
                GL.Vertex(verts[tris[i]]);
                GL.Vertex(verts[tris[i + 1]]);
                GL.Vertex(verts[tris[i + 2]]);
            }
            GL.End();
            GL.PopMatrix();

            // Vertices sélectionnés : points. Affichés surtout utiles en mode
            // Vertices, mais montrés dans les deux modes pour cohérence.
            if (_window.SelectionMode == UVSelectionMode.Vertices)
                DrawSelectedVertexPoints(sel, verts, l2w);
        }

        // Points aux vertices sélectionnés, dessinés en taille écran constante.
        void DrawSelectedVertexPoints(UVSelection sel, Vector3[] verts,
            Matrix4x4 l2w)
        {
            Handles.color = new Color(1f, 0.62f, 0.15f, 1f);
            foreach (int v in sel.SelectedVertices)
            {
                if (v < 0 || v >= verts.Length)
                    continue;
                Vector3 wp = l2w.MultiplyPoint3x4(verts[v]);
                float size = HandleUtility.GetHandleSize(wp) * 0.04f;
                Handles.DotHandleCap(0, wp, Quaternion.identity, size,
                    EventType.Repaint);
            }
        }

        void DrawHover()
        {
            if (!_currentHit.hit)
                return;

            Vector3[] verts = _window.PreviewVertices;
            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            _window.Raycaster.GetTriangleVertices(_currentHit.triangleIndex,
                out int v0, out int v1, out int v2);

            if (_window.SelectionMode == UVSelectionMode.Vertices)
            {
                // Survol en mode Vertices : surligne le vertex le plus proche
                // du point d'impact parmi les 3 du triangle touché.
                int hovered = NearestTriangleVertex(_currentHit, verts, l2w,
                    v0, v1, v2);
                if (hovered >= 0)
                {
                    Vector3 wp = l2w.MultiplyPoint3x4(verts[hovered]);
                    float size = HandleUtility.GetHandleSize(wp) * 0.05f;
                    Handles.color = new Color(0.25f, 0.65f, 1f, 1f);
                    Handles.DotHandleCap(0, wp, Quaternion.identity, size,
                        EventType.Repaint);
                }
            }
            else
            {
                // Survol en mode Faces : contour du triangle.
                Vector3 p0 = l2w.MultiplyPoint3x4(verts[v0]);
                Vector3 p1 = l2w.MultiplyPoint3x4(verts[v1]);
                Vector3 p2 = l2w.MultiplyPoint3x4(verts[v2]);
                Handles.color = new Color(0.25f, 0.65f, 1f, 1f);
                Handles.DrawAAPolyLine(4f, p0, p1, p2, p0);
            }
        }

        // Vertex du triangle le plus proche du point d'impact monde.
        static int NearestTriangleVertex(UVMeshRaycaster.RayHit hit,
            Vector3[] verts, Matrix4x4 l2w, int v0, int v1, int v2)
        {
            float d0 = (l2w.MultiplyPoint3x4(verts[v0]) - hit.pointWorld).sqrMagnitude;
            float d1 = (l2w.MultiplyPoint3x4(verts[v1]) - hit.pointWorld).sqrMagnitude;
            float d2 = (l2w.MultiplyPoint3x4(verts[v2]) - hit.pointWorld).sqrMagnitude;
            if (d0 <= d1 && d0 <= d2) return v0;
            if (d1 <= d0 && d1 <= d2) return v1;
            return v2;
        }

        void DrawBoxRect()
        {
            Handles.BeginGUI();
            Rect r = GetBoxRect();
            EditorGUI.DrawRect(r, new Color(0.25f, 0.65f, 1f, 0.12f));
            Color border = new Color(0.25f, 0.65f, 1f, 0.9f);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, 1f), border);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - 1f, r.width, 1f), border);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, 1f, r.height), border);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.yMin, 1f, r.height), border);
            Handles.EndGUI();
        }

        // ------------------------------------------------------------------
        // Gizmo du cadre de projection
        // ------------------------------------------------------------------

        void DrawProjectionGizmo()
        {
            UVProjectionSettings p = _window.ProjectionSettings;
            if (p == null)
                return;

            Color gizmoColor = new Color(1f, 0.78f, 0.2f, 1f);

            if (p.type == UVProjectionType.Triplanar)
            {
                // Cadre non éditable : bounding box locale du mesh, en lecture seule.
                Bounds b = _window.WorkingMeshBounds;
                using (new Handles.DrawingScope(new Color(1f, 0.78f, 0.2f, 0.6f),
                           _window.Preview.LocalToWorld))
                {
                    Handles.DrawWireCube(b.center, b.size);
                }
                return;
            }

            Matrix4x4 frame = _window.Preview.LocalToWorld * p.FrameMatrix;
            using (new Handles.DrawingScope(gizmoColor, frame))
            {
                switch (p.type)
                {
                    case UVProjectionType.Planar:
                        Handles.DrawWireCube(Vector3.zero, Vector3.one);
                        // Flèche indiquant la direction de projection (+Z).
                        Handles.DrawAAPolyLine(3f, Vector3.zero, new Vector3(0f, 0f, 0.7f));
                        Handles.ConeHandleCap(0, new Vector3(0f, 0f, 0.7f),
                            Quaternion.LookRotation(Vector3.forward), 0.12f,
                            EventType.Repaint);
                        break;

                    case UVProjectionType.Box:
                        Handles.DrawWireCube(Vector3.zero, Vector3.one);
                        break;

                    case UVProjectionType.Cylindrical:
                        DrawWireCylinder();
                        break;

                    case UVProjectionType.Spherical:
                        Handles.DrawWireDisc(Vector3.zero, Vector3.right, 0.5f);
                        Handles.DrawWireDisc(Vector3.zero, Vector3.up, 0.5f);
                        Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 0.5f);
                        break;
                }
            }
        }

        static void DrawWireCylinder()
        {
            // Cylindre unité aligné sur l'axe Y, rayon 0.5, hauteur 1.
            Vector3 top = new Vector3(0f, 0.5f, 0f);
            Vector3 bottom = new Vector3(0f, -0.5f, 0f);
            Handles.DrawWireDisc(top, Vector3.up, 0.5f);
            Handles.DrawWireDisc(bottom, Vector3.up, 0.5f);
            Handles.DrawAAPolyLine(2f, new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f));
            Handles.DrawAAPolyLine(2f, new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(-0.5f, -0.5f, 0f));
            Handles.DrawAAPolyLine(2f, new Vector3(0f, 0.5f, 0.5f),
                new Vector3(0f, -0.5f, 0.5f));
            Handles.DrawAAPolyLine(2f, new Vector3(0f, 0.5f, -0.5f),
                new Vector3(0f, -0.5f, -0.5f));
        }

        void DrawProjectionFrameHandles()
        {
            UVProjectionSettings p = _window.ProjectionSettings;
            if (p == null || !p.UsesEditableFrame)
                return;

            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            Vector3 worldCenter = l2w.MultiplyPoint3x4(p.frameCenter);
            Quaternion worldRot = l2w.rotation * Quaternion.Euler(p.frameEuler);

            EditorGUI.BeginChangeCheck();

            Vector3 newCenter = worldCenter;
            Quaternion newRot = worldRot;
            Vector3 newSize = p.frameSize;

            switch (p.gizmoMode)
            {
                case FrameGizmoMode.Move:
                    newCenter = Handles.PositionHandle(worldCenter, worldRot);
                    break;

                case FrameGizmoMode.Rotate:
                    newRot = Handles.RotationHandle(worldRot, worldCenter);
                    break;

                case FrameGizmoMode.Scale:
                    float hs = HandleUtility.GetHandleSize(worldCenter);
                    newSize = Handles.ScaleHandle(p.frameSize, worldCenter,
                        worldRot, hs);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                Matrix4x4 w2l = l2w.inverse;
                p.frameCenter = w2l.MultiplyPoint3x4(newCenter);
                p.frameEuler = (Quaternion.Inverse(l2w.rotation) * newRot).eulerAngles;
                p.frameSize = newSize;
                p.Validate();

                // Drag du cadre = geste continu : reprojection live, le commit
                // (un seul cran d'historique) se fait au relâchement de la souris.
                if (_window.HasWorkingMesh)
                    _window.ApplyLiveContinuous();
                _window.Repaint();
            }
        }

        // ------------------------------------------------------------------
        // Sélection de faces
        // ------------------------------------------------------------------

        void HandleSelectionInput(Event e, SceneView sv)
        {
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
                        bool isClick = rect.width < BoxThreshold &&
                                       rect.height < BoxThreshold;

                        if (isClick)
                            ClickSelect(e);
                        else
                            RectSelect(sv, rect, e);

                        e.Use();
                    }
                    break;
            }
        }

        void ClickSelect(Event e)
        {
            UVSelection sel = _window.Selection;
            bool add = IsAdd(e), remove = IsRemove(e);
            bool vertexMode = _window.SelectionMode == UVSelectionMode.Vertices;

            if (!_currentHit.hit)
            {
                if (!add && !remove && !sel.IsEmpty)
                {
                    sel.Clear();
                    AfterSelectionChanged();
                }
                return;
            }

            Vector3[] verts = _window.PreviewVertices;
            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            _window.Raycaster.GetTriangleVertices(_currentHit.triangleIndex,
                out int v0, out int v1, out int v2);

            if (!add && !remove)
                sel.Clear();

            if (vertexMode)
            {
                // Vertex le plus proche du point d'impact, étendu à tous ses
                // doublons de couture (3D -> tous les coïncidents).
                int picked = NearestTriangleVertex(_currentHit, verts, l2w,
                    v0, v1, v2);
                ApplyToCoincident(sel, picked, remove);
            }
            else
            {
                if (remove)
                    sel.RemoveTriangle(_currentHit.triangleIndex);
                else
                    sel.AddTriangle(_currentHit.triangleIndex);
            }

            AfterSelectionChanged();
        }

        void RectSelect(SceneView sv, Rect rect, Event e)
        {
            UVSelection sel = _window.Selection;
            Vector3[] verts = _window.PreviewVertices;
            int[] tris = _window.PreviewTriangles;
            Matrix4x4 l2w = _window.Preview.LocalToWorld;
            Camera cam = sv.camera;
            bool add = IsAdd(e), remove = IsRemove(e);
            bool cull = _window.BackfaceCull;
            bool occ = _window.Occlusion;
            bool vertexMode = _window.SelectionMode == UVSelectionMode.Vertices;

            if (!add && !remove)
                sel.Clear();

            UVScreenOcclusionGrid occlusion = null;
            if (occ)
            {
                occlusion = new UVScreenOcclusionGrid();
                occlusion.Build(verts, tris, l2w, cam);
            }

            if (vertexMode)
            {
                // Sélection rectangle de vertices. Le backface cull est évalué
                // par face : un vertex est éligible s'il appartient à au moins
                // une face tournée vers la caméra.
                bool[] frontFacingVertex = cull
                    ? ComputeFrontFacingVertices(verts, tris, l2w, cam)
                    : null;

                for (int v = 0; v < verts.Length; v++)
                {
                    if (cull && frontFacingVertex != null && !frontFacingVertex[v])
                        continue;

                    Vector3 wp = l2w.MultiplyPoint3x4(verts[v]);
                    if (cam.WorldToViewportPoint(wp).z <= 0f)
                        continue;
                    if (!rect.Contains(HandleUtility.WorldToGUIPoint(wp)))
                        continue;
                    if (occ && occlusion.IsOccluded(wp))
                        continue;

                    // Étend aux doublons : tous les vertices coïncidents.
                    ApplyToCoincident(sel, v, remove);
                }
            }
            else
            {
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = verts[tris[t]];
                    Vector3 b = verts[tris[t + 1]];
                    Vector3 c = verts[tris[t + 2]];

                    if (cull && !IsFaceFrontFacing(a, b, c, l2w, cam,
                            _window.BackfaceThresholdDot))
                        continue;

                    Vector3 wp = l2w.MultiplyPoint3x4((a + b + c) / 3f);
                    if (cam.WorldToViewportPoint(wp).z <= 0f)
                        continue;
                    if (!rect.Contains(HandleUtility.WorldToGUIPoint(wp)))
                        continue;
                    if (occ && occlusion.IsOccluded(wp))
                        continue;

                    int triIndex = t / 3;
                    if (remove)
                        sel.RemoveTriangle(triIndex);
                    else
                        sel.AddTriangle(triIndex);
                }
            }

            AfterSelectionChanged();
        }

        // (Dé)sélectionne un vertex ET tous ses doublons de même position.
        void ApplyToCoincident(UVSelection sel, int vertex, bool remove)
        {
            var group = _window.WeldMap != null
                ? _window.WeldMap.GetCoincident(vertex)
                : null;

            if (group == null)
            {
                if (remove) sel.RemoveVertex(vertex);
                else sel.AddVertex(vertex);
                return;
            }

            for (int i = 0; i < group.Count; i++)
            {
                if (remove) sel.RemoveVertex(group[i]);
                else sel.AddVertex(group[i]);
            }
        }

        // Marque les vertices appartenant à au moins une face tournée vers la
        // caméra (pour le backface cull en sélection rectangle de vertices).
        static bool[] ComputeFrontFacingVertices(Vector3[] verts, int[] tris,
            Matrix4x4 l2w, Camera cam)
        {
            var front = new bool[verts.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                if (IsFaceFrontFacing(verts[i0], verts[i1], verts[i2],
                        l2w, cam, 0f))
                {
                    front[i0] = true;
                    front[i1] = true;
                    front[i2] = true;
                }
            }
            return front;
        }

        // Une face est tournée vers la caméra si dot(normale, dirCaméra) >= seuil.
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

        void AfterSelectionChanged()
        {
            // Changement de sélection = changement discret. Si la projection
            // est en portée « faces sélectionnées », elle est reprojetée en
            // direct ; sinon on fige juste un cran d'historique de sélection.
            _window.OnSelectionChanged();
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
    }
}
