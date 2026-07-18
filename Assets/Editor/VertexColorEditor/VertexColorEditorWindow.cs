using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Canal isolé pour l'affichage de la preview.
    /// </summary>
    public enum ViewChannelMode { RGB, R, G, B, A }

    /// <summary>
    /// Mode de la catégorie Select : définit le masque appliqué à la peinture.
    /// </summary>
    public enum SelectMode { Free, Vertices, Faces }

    /// <summary>
    /// Mode de la catégorie Paint : outil de peinture.
    /// </summary>
    public enum PaintMode { Brush, Gradient, Fill, Smooth }

    /// <summary>
    /// Catégorie dont dépend le comportement du clic gauche dans la SceneView.
    /// </summary>
    public enum ActiveCategory { Select, Paint }

    /// <summary>
    /// Fenêtre éditeur du Vertex Color Editor — Phase 3.
    /// Deux catégories : Select (Free / Vertices / Faces -> masque) et
    /// Paint (Brush / Gradient / Fill / Smooth -> outil).
    /// </summary>
    public class VertexColorEditorWindow : EditorWindow
    {
        // --- État sérialisé (persiste au reload de scripts) ---
        [SerializeField] Mesh _targetMeshAsset;
        [SerializeField] Material _displayMaterial;
        [SerializeField] bool _showMaterial;
        [SerializeField] VertexColorBrush _brush = new VertexColorBrush();
        [SerializeField] VertexColorBrush _smoothBrush = new VertexColorBrush();
        [SerializeField] Gradient _gradient;
        [SerializeField] float _fillStrength = 1f;
        [SerializeField] float _gradientStrength = 1f;
        [SerializeField] bool _channelR = true;
        [SerializeField] bool _channelG = true;
        [SerializeField] bool _channelB = true;
        [SerializeField] bool _channelA = false;
        [SerializeField] ViewChannelMode _viewChannel = ViewChannelMode.RGB;
        [SerializeField] SelectMode _selectMode = SelectMode.Free;
        [SerializeField] PaintMode _paintMode = PaintMode.Brush;
        [SerializeField] ActiveCategory _category = ActiveCategory.Paint;
        [SerializeField] bool _backfaceCull = true;
        [SerializeField] float _backfaceThreshold = 0f;
        [SerializeField] bool _occlusion = true;
        [SerializeField] bool _isActive = true;
        [SerializeField] Vector3 _gradientStart;
        [SerializeField] Vector3 _gradientEnd;
        [SerializeField] bool _hasGradientLine;

        // --- État runtime (reconstruit après reload) ---
        Mesh _workingMesh;
        Color[] _colors;
        Vector3[] _previewVertices;
        int[] _previewTriangles;
        VertexColorPainter _painter;
        VertexColorPreviewInstance _preview;
        VertexColorSceneController _sceneController;
        VertexSelection _selection;
        FaceSelection _faceSelection;
        VertexMeshTopology _topology;
        string _statusMessage;
        MessageType _statusType = MessageType.Info;

        // --- Accès pour le SceneController ---
        public bool IsActive => _isActive;
        public bool HasWorkingMesh => _workingMesh != null && _colors != null;
        public Mesh WorkingMesh => _workingMesh;
        public Color[] Colors => _colors;
        public Vector3[] PreviewVertices => _previewVertices;
        public int[] PreviewTriangles => _previewTriangles;
        public VertexColorBrush Brush => _brush;
        public VertexColorBrush SmoothBrush => _smoothBrush;
        public VertexColorPainter Painter => _painter;
        public VertexColorPreviewInstance Preview => _preview;
        public VertexSelection Selection => _selection;
        public FaceSelection FaceSelection => _faceSelection;
        public VertexMeshTopology Topology => _topology;
        public ViewChannelMode ViewChannel => _viewChannel;
        public SelectMode SelectMode => _selectMode;
        public PaintMode PaintMode => _paintMode;
        public ActiveCategory Category => _category;
        public bool BackfaceCull => _backfaceCull;
        // Seuil exprimé en produit scalaire : une face est sélectionnable si
        // dot(normale, direction caméra) >= ce seuil. 0° -> 0 (ne rejette que le
        // vrai dos) ; 90° -> 1 (n'accepte que les faces vues de face).
        public float BackfaceThresholdDot => Mathf.Sin(_backfaceThreshold * Mathf.Deg2Rad);
        public bool Occlusion => _occlusion;
        public bool HasGradientLine => _hasGradientLine;
        public Vector3 GradientStart => _gradientStart;
        public Vector3 GradientEnd => _gradientEnd;

        public ChannelMask GetChannelMask() =>
            new ChannelMask(_channelR, _channelG, _channelB, _channelA);

        /// <summary>
        /// Construit le masque de vertices effectif pour la peinture, selon le mode
        /// Select courant. Renvoie null = aucun masque (peinture libre) ; c'est aussi
        /// le fallback quand la sélection concernée est vide.
        /// </summary>
        public bool[] BuildPaintMask()
        {
            switch (_selectMode)
            {
                case SelectMode.Vertices:
                    return _selection != null ? _selection.BuildVertexMask() : null;
                case SelectMode.Faces:
                    return _faceSelection != null
                        ? _faceSelection.BuildVertexMask(_previewTriangles,
                            _workingMesh != null ? _workingMesh.vertexCount : 0)
                        : null;
                default:
                    return null; // Free
            }
        }

        /// <summary>
        /// Convertit la sélection de vertices en sélection de faces : une face est
        /// retenue si ses trois vertices sont sélectionnés (faces entièrement
        /// composées de vertices sélectionnés).
        /// </summary>
        void ConvertVerticesToFaces()
        {
            if (_previewTriangles == null || _selection == null || _faceSelection == null)
                return;

            _faceSelection.Clear();
            int triCount = _previewTriangles.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int b = t * 3;
                if (_selection.Contains(_previewTriangles[b]) &&
                    _selection.Contains(_previewTriangles[b + 1]) &&
                    _selection.Contains(_previewTriangles[b + 2]))
                {
                    _faceSelection.Add(t);
                }
            }
        }

        /// <summary>
        /// Convertit la sélection de faces en sélection de vertices : tout vertex
        /// appartenant à une face sélectionnée est retenu.
        /// </summary>
        void ConvertFacesToVertices()
        {
            if (_previewTriangles == null || _selection == null || _faceSelection == null)
                return;

            _selection.Clear();
            foreach (int tri in _faceSelection.Indices)
            {
                int b = tri * 3;
                if (b + 2 >= _previewTriangles.Length)
                    continue;
                _selection.Add(_previewTriangles[b]);
                _selection.Add(_previewTriangles[b + 1]);
                _selection.Add(_previewTriangles[b + 2]);
            }
        }

        [MenuItem("Window/Ombrage Tools/Mesh/Vertex Color Editor")]
        public static void Open()
        {
            var window = GetWindow<VertexColorEditorWindow>("Vertex Color Editor");
            window.minSize = new Vector2(340f, 600f);
            window.Show();
        }

        static Gradient CreateDefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.black, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            return g;
        }

        void OnEnable()
        {
            if (_brush == null)
                _brush = new VertexColorBrush();
            _brush.Validate();

            if (_smoothBrush == null)
                _smoothBrush = new VertexColorBrush();
            _smoothBrush.Validate();

            if (_gradient == null)
                _gradient = CreateDefaultGradient();

            _painter = new VertexColorPainter();
            _preview = new VertexColorPreviewInstance();
            _selection = new VertexSelection();
            _faceSelection = new FaceSelection();
            _topology = new VertexMeshTopology();
            _sceneController = new VertexColorSceneController(this);
            _sceneController.Enable();

            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            if (_targetMeshAsset != null)
                LoadMesh(_targetMeshAsset);
        }

        void OnDisable()
        {
            _sceneController?.Disable();
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Cleanup();
        }

        void OnPlayModeChanged(PlayModeStateChange state)
        {
            // Détruit l'instance temporaire avant l'entrée en Play Mode.
            if (state == PlayModeStateChange.ExitingEditMode)
                Cleanup();
        }

        void Cleanup()
        {
            _preview?.Destroy();

            if (_workingMesh != null)
            {
                DestroyImmediate(_workingMesh);
                _workingMesh = null;
            }

            _colors = null;
            _previewVertices = null;
            _previewTriangles = null;
            _selection?.SetVertexCount(0);
            _faceSelection?.SetTriangleCount(0);
        }

        void OnUndoRedo()
        {
            if (_workingMesh == null)
                return;

            _colors = _workingMesh.colors;
            if (_colors == null || _colors.Length != _workingMesh.vertexCount)
                _colors = VertexColorMeshIO.LoadColors(_workingMesh);

            SceneView.RepaintAll();
            Repaint();
        }

        // ----------------------------------------------------------------------
        // Chargement
        // ----------------------------------------------------------------------

        void LoadMesh(Mesh source)
        {
            Cleanup();

            if (source == null)
            {
                SetStatus("Aucun mesh assigné.", MessageType.Info);
                return;
            }

            if (!source.isReadable)
            {
                SetStatus(
                    "Le mesh source n'est pas lisible : Read/Write est désactivé sur le " +
                    "FBX. Utilise le bouton ci-dessous pour l'activer.",
                    MessageType.Error);
                return;
            }

            _workingMesh = Instantiate(source);
            _workingMesh.name = source.name;

            _colors = VertexColorMeshIO.LoadColors(_workingMesh);
            _workingMesh.SetColors(_colors);

            _previewVertices = _workingMesh.vertices;
            _previewTriangles = _workingMesh.triangles;

            _painter.SetMesh(_workingMesh);
            _preview.Create(_workingMesh);
            // L'instance vient d'être recréée : on lui réapplique l'état d'affichage.
            ApplyMaterialPreview();

            // Topologie (pour le Smooth) + réinitialisation des sélections / dégradé.
            _topology.Build(_previewVertices, _previewTriangles);
            _selection.SetVertexCount(_workingMesh.vertexCount);
            _faceSelection.SetTriangleCount(_previewTriangles.Length / 3);
            _hasGradientLine = false;

            SetStatus(
                $"Mesh chargé : {source.name}  —  {_workingMesh.vertexCount} vertices, " +
                $"{_previewTriangles.Length / 3} triangles.",
                MessageType.Info);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Répercute l'état "Show Material" sur l'instance de preview : MeshRenderer
        /// actif avec le material référencé, ou rendu GL vertex colors du tool.
        /// </summary>
        void ApplyMaterialPreview()
        {
            if (_preview != null && _preview.Exists)
                _preview.SetMaterialPreview(_showMaterial && _displayMaterial != null,
                    _displayMaterial);
        }

        void EnableReadWrite(Mesh source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;

            if (importer == null)
            {
                SetStatus(
                    "Impossible d'accéder au ModelImporter de ce mesh.",
                    MessageType.Error);
                return;
            }

            importer.isReadable = true;
            importer.SaveAndReimport();
            SetStatus("Read/Write activé sur le FBX source. Rechargement…", MessageType.Info);
            LoadMesh(source);
        }

        // ----------------------------------------------------------------------
        // Sauvegarde
        // ----------------------------------------------------------------------

        public void PushColorsToMesh()
        {
            if (_workingMesh != null && _colors != null)
            {
                // SetColors (et non l'affectation .colors) garantit que le canal
                // couleur fait partie intégrante du VertexData du mesh, donc qu'il
                // est bien sérialisé dans le .asset.
                _workingMesh.SetColors(_colors);
            }
        }

        void DoSave()
        {
            if (_workingMesh == null || _targetMeshAsset == null)
            {
                SetStatus("Rien à sauvegarder.", MessageType.Warning);
                return;
            }

            PushColorsToMesh();
            var result = VertexColorMeshIO.Save(_workingMesh, _targetMeshAsset);

            if (result.success)
            {
                string msg = result.message;
                if (!string.IsNullOrEmpty(result.backupPath))
                    msg += $"\nBackup : {result.backupPath}";
                SetStatus(msg, MessageType.Info);

                // Re-cible le tool sur le .asset généré : les sauvegardes
                // suivantes l'écraseront (avec backup versionné).
                _targetMeshAsset = result.savedAsset;
            }
            else
            {
                SetStatus(result.message, MessageType.Error);
            }
        }

        // ----------------------------------------------------------------------
        // Opérations Fill / Gradient (déclenchées hors stroke continu)
        // ----------------------------------------------------------------------

        void DoFill()
        {
            if (!HasWorkingMesh)
                return;

            Undo.RegisterCompleteObjectUndo(_workingMesh, "Fill Vertex Colors");
            bool changed = _painter.ApplyFill(_colors, _brush.color, _brush.alphaValue,
                GetChannelMask(), _fillStrength, BuildPaintMask());

            if (changed)
            {
                PushColorsToMesh();
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// Applique le dégradé en espace écran (appelé par le SceneController au
        /// relâchement du drag). startWorld = origine ancrée sur l'impact du raycast ;
        /// startGui / endGui = extrémités de la ligne tracée en coordonnées écran.
        /// La ligne est mémorisée en monde (les deux extrémités) pour l'affichage.
        /// </summary>
        public void ApplyGradientLineScreen(Camera cam, Vector3 startWorld,
            Vector2 startGui, Vector2 endGui)
        {
            if (!HasWorkingMesh || cam == null)
                return;

            // Extrémités mémorisées en monde : départ = impact du raycast ; fin =
            // point GUI de fin ramené sur le plan parallèle à la caméra passant par
            // le départ (la ligne monde, reprojetée sous l'angle du tracé, redonne
            // exactement le segment écran tracé).
            _gradientStart = startWorld;
            _gradientEnd = GuiToWorldOnPlane(cam, endGui, startWorld);
            _hasGradientLine = true;

            ApplyGradientWithCamera(cam, startGui, endGui);
        }

        // Réapplique le dégradé : la ligne monde mémorisée est reprojetée en écran
        // selon la vue courante, puis le dégradé écran est ré-appliqué.
        void ReapplyGradient()
        {
            if (!HasWorkingMesh || !_hasGradientLine)
                return;

            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
            {
                SetStatus("Réappliquer : aucune SceneView active.", MessageType.Warning);
                return;
            }

            Camera cam = sv.camera;
            ApplyGradientWithCamera(cam,
                WorldToGui(cam, _gradientStart), WorldToGui(cam, _gradientEnd));
        }

        void ApplyGradientWithCamera(Camera cam, Vector2 startGui, Vector2 endGui)
        {
            Undo.RegisterCompleteObjectUndo(_workingMesh, "Gradient Vertex Colors");
            bool changed = _painter.ApplyGradient(_colors, _preview.LocalToWorld,
                cam, startGui, endGui, _gradient, GetChannelMask(),
                _gradientStrength, BuildPaintMask());

            if (changed)
            {
                PushColorsToMesh();
                SceneView.RepaintAll();
            }
            Repaint();
        }

        // Position GUI (origine haut-gauche) d'un point monde.
        static Vector2 WorldToGui(Camera cam, Vector3 worldPoint)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPoint);
            return new Vector2(sp.x, cam.pixelHeight - sp.y);
        }

        // Point monde correspondant à une position GUI, sur le plan parallèle à la
        // caméra passant par planePoint (gère perspective et orthographique).
        static Vector3 GuiToWorldOnPlane(Camera cam, Vector2 gui, Vector3 planePoint)
        {
            float depth = Vector3.Dot(planePoint - cam.transform.position,
                cam.transform.forward);
            float screenY = cam.pixelHeight - gui.y;
            return cam.ScreenToWorldPoint(new Vector3(gui.x, screenY, depth));
        }

        // ----------------------------------------------------------------------
        // UI
        // ----------------------------------------------------------------------

        void OnGUI()
        {
            if (_brush == null)
                _brush = new VertexColorBrush();
            _brush.Validate();

            if (_smoothBrush == null)
                _smoothBrush = new VertexColorBrush();
            _smoothBrush.Validate();

            if (_gradient == null)
                _gradient = CreateDefaultGradient();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Vertex Color Editor — Phase 3", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // --- Mesh source ---
            EditorGUI.BeginChangeCheck();
            var newMesh = (Mesh)EditorGUILayout.ObjectField(
                "Mesh", _targetMeshAsset, typeof(Mesh), false);
            if (EditorGUI.EndChangeCheck())
            {
                _targetMeshAsset = newMesh;
                LoadMesh(_targetMeshAsset);
            }

            // --- Material d'affichage (optionnel) ---
            EditorGUI.BeginChangeCheck();
            _displayMaterial = (Material)EditorGUILayout.ObjectField(
                "Material", _displayMaterial, typeof(Material), false);
            // Le toggle ne peut être actif que si un material est référencé.
            if (_displayMaterial == null)
                _showMaterial = false;
            using (new EditorGUI.DisabledScope(_displayMaterial == null))
            {
                _showMaterial = GUILayout.Toggle(
                    _showMaterial,
                    new GUIContent("Show Material",
                        "Actif : affiche le mesh avec le material référencé. " +
                        "Inactif : affiche la preview vertex colors du tool."),
                    "Button");
            }
            if (EditorGUI.EndChangeCheck())
            {
                ApplyMaterialPreview();
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(_targetMeshAsset == null))
            {
                if (GUILayout.Button("Charger / Recharger"))
                    LoadMesh(_targetMeshAsset);
            }

            if (_targetMeshAsset != null && !_targetMeshAsset.isReadable)
            {
                if (GUILayout.Button("Activer Read/Write sur le FBX source"))
                    EnableReadWrite(_targetMeshAsset);
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!HasWorkingMesh))
            {
                _isActive = EditorGUILayout.ToggleLeft(
                    "Outils actifs dans la SceneView", _isActive);

                DrawCategorySelector();

                EditorGUILayout.Space();
                if (_category == ActiveCategory.Select)
                    DrawSelectPanel();
                else
                    DrawPaintPanel();

                // --- Affichage ---
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Affichage", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _viewChannel = (ViewChannelMode)EditorGUILayout.EnumPopup(
                    "Canal affiché", _viewChannel);
                if (EditorGUI.EndChangeCheck())
                    SceneView.RepaintAll();

                EditorGUILayout.Space();
                if (GUILayout.Button("Sauvegarder  (.asset + backup)", GUILayout.Height(30f)))
                    DoSave();
            }

            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }

        // --- Sélecteur de mode + toolbar de la catégorie courante ---
        void DrawCategorySelector()
        {
            EditorGUILayout.Space();

            // Dropdown : une seule catégorie visible à la fois.
            EditorGUI.BeginChangeCheck();
            var category = (ActiveCategory)EditorGUILayout.EnumPopup("Mode", _category);
            if (EditorGUI.EndChangeCheck())
            {
                _category = category;
                SceneView.RepaintAll();
            }

            // _selectMode et _paintMode sont des champs distincts : le mode de
            // chaque catégorie est mémorisé et restauré au retour via le dropdown.
            if (_category == ActiveCategory.Select)
            {
                // L'encadré d'aide est placé AU-DESSUS des boutons Free/Vertices/Faces.
                DrawSelectHelpBox();

                EditorGUI.BeginChangeCheck();
                var newSelectMode = (SelectMode)GUILayout.Toolbar((int)_selectMode,
                    new[] { "Free", "Vertices", "Faces" });
                if (EditorGUI.EndChangeCheck())
                {
                    // Basculement direct Vertices <-> Faces : conversion de la sélection.
                    if (_selectMode == SelectMode.Vertices &&
                        newSelectMode == SelectMode.Faces)
                        ConvertVerticesToFaces();
                    else if (_selectMode == SelectMode.Faces &&
                             newSelectMode == SelectMode.Vertices)
                        ConvertFacesToVertices();
                    _selectMode = newSelectMode;
                    SceneView.RepaintAll();
                }

                // Backface Cull : disponible tant que la catégorie Select est active.
                EditorGUI.BeginChangeCheck();
                _backfaceCull = EditorGUILayout.ToggleLeft(
                    new GUIContent("Backface Cull",
                        "Si activé, le rectangle de sélection ignore les faces (ou " +
                        "vertices de faces) dos à la caméra, ainsi que celles masquées " +
                        "par la géométrie (occlusion)."),
                    _backfaceCull);
                if (EditorGUI.EndChangeCheck())
                    SceneView.RepaintAll();

                // Seuil d'angle : les faces trop rasantes sont traitées comme de dos.
                using (new EditorGUI.DisabledScope(!_backfaceCull))
                {
                    _backfaceThreshold = EditorGUILayout.Slider(
                        new GUIContent("Seuil d'angle (°)",
                            "Angle de vue minimal pour qu'une face soit sélectionnable. " +
                            "Les faces plus rasantes que ce seuil sont considérées de " +
                            "dos et exclues. 0 = ne rejette que les faces réellement " +
                            "dos à la caméra."),
                        _backfaceThreshold, 0f, 90f);
                }

                // Occlusion : test indépendant du Backface Cull.
                _occlusion = EditorGUILayout.ToggleLeft(
                    new GUIContent("Occlusion",
                        "Si activé, le rectangle de sélection ignore les faces masquées " +
                        "par la géométrie (test de visibilité par raycast)."),
                    _occlusion);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                _paintMode = (PaintMode)GUILayout.Toolbar((int)_paintMode,
                    new[] { "Brush", "Gradient", "Fill", "Smooth" });
                if (EditorGUI.EndChangeCheck())
                    SceneView.RepaintAll();
            }
        }

        // Encadré d'aide de la catégorie Select, affiché au-dessus de la toolbar.
        void DrawSelectHelpBox()
        {
            if (_selectMode == SelectMode.Free)
            {
                EditorGUILayout.HelpBox(
                    "Mode Free : aucune sélection, aucun masque. La peinture s'applique " +
                    "à tout le mesh.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Clic = sélectionner un élément\n" +
                    "Clic dans le vide = désélectionner\n" +
                    "Clic + drag = rectangle de sélection\n" +
                    "Shift = ajouter\n" +
                    "Ctrl/Cmd = retirer\n" +
                    "aucun modificateur = remplacer",
                    MessageType.None);
            }
        }

        // --- Panneau de la catégorie Select (sous la toolbar) ---
        void DrawSelectPanel()
        {
            // L'aide du mode Free est déjà affichée au-dessus de la toolbar.
            if (_selectMode == SelectMode.Free)
                return;

            bool faces = _selectMode == SelectMode.Faces;
            EditorGUILayout.LabelField(
                faces ? "Sélection de faces" : "Sélection de vertices",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tout sélectionner"))
                {
                    if (faces) _faceSelection.SelectAll();
                    else _selection.SelectAll();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Tout désélectionner"))
                {
                    if (faces) _faceSelection.Clear();
                    else _selection.Clear();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Inverser"))
                {
                    if (faces) _faceSelection.Invert();
                    else _selection.Invert();
                    SceneView.RepaintAll();
                }
            }

            if (faces)
                EditorGUILayout.LabelField(
                    $"Sélection : {_faceSelection.Count} / {_faceSelection.TriangleCount} faces");
            else
                EditorGUILayout.LabelField(
                    $"Sélection : {_selection.Count} / {_selection.VertexCount} vertices");
        }

        // --- Panneau de la catégorie Paint ---
        void DrawPaintPanel()
        {
            switch (_paintMode)
            {
                case PaintMode.Brush:    DrawBrushPanel(); break;
                case PaintMode.Gradient: DrawGradientPanel(); break;
                case PaintMode.Fill:     DrawFillPanel(); break;
                case PaintMode.Smooth:   DrawSmoothPanel(); break;
            }
        }

        void DrawBrushPanel()
        {
            EditorGUILayout.LabelField("Canaux peints", EditorStyles.boldLabel);
            DrawChannelToggles();

            EditorGUI.BeginChangeCheck();
            DrawColorOrAlphaField();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            _brush.radius = Mathf.Max(0.001f,
                EditorGUILayout.FloatField("Rayon (monde)", _brush.radius));
            _brush.strength = EditorGUILayout.Slider("Force", _brush.strength, 0f, 1f);
            _brush.falloff = EditorGUILayout.CurveField(
                "Falloff", _brush.falloff, Color.cyan, new Rect(0f, 0f, 1f, 1f));
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            DrawMaskInfo();
        }

        void DrawFillPanel()
        {
            EditorGUILayout.LabelField("Canaux peints", EditorStyles.boldLabel);
            DrawChannelToggles();

            EditorGUI.BeginChangeCheck();
            DrawColorOrAlphaField();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fill", EditorStyles.boldLabel);
            _fillStrength = EditorGUILayout.Slider("Force", _fillStrength, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            if (GUILayout.Button("Remplir", GUILayout.Height(24f)))
                DoFill();

            DrawMaskInfo();
        }

        void DrawGradientPanel()
        {
            EditorGUILayout.LabelField("Canaux peints", EditorStyles.boldLabel);
            DrawChannelToggles();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gradient", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _gradient = EditorGUILayout.GradientField("Dégradé", _gradient);
            _gradientStrength = EditorGUILayout.Slider("Force", _gradientStrength, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            EditorGUILayout.HelpBox(
                "Clic + drag dans la SceneView pour tracer la ligne du dégradé " +
                "(début -> fin). Le dégradé s'applique au relâchement.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(!_hasGradientLine))
            {
                if (GUILayout.Button("Réappliquer le dégradé"))
                    ReapplyGradient();
            }

            DrawMaskInfo();
        }

        void DrawSmoothPanel()
        {
            EditorGUILayout.LabelField("Canaux lissés", EditorStyles.boldLabel);
            DrawChannelToggles();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Smooth", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _smoothBrush.radius = Mathf.Max(0.001f,
                EditorGUILayout.FloatField("Rayon (monde)", _smoothBrush.radius));
            _smoothBrush.strength = EditorGUILayout.Slider(
                "Force", _smoothBrush.strength, 0f, 1f);
            _smoothBrush.falloff = EditorGUILayout.CurveField(
                "Falloff", _smoothBrush.falloff, Color.cyan, new Rect(0f, 0f, 1f, 1f));
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            EditorGUILayout.HelpBox(
                "Clic gauche maintenu sur le mesh : lisse les canaux cochés vers la " +
                "moyenne des voisins.", MessageType.None);

            DrawMaskInfo();
        }

        // Champ Couleur (canaux RGB) ou slider Alpha (canal A), partagé Brush / Fill.
        void DrawColorOrAlphaField()
        {
            if (_channelA)
                _brush.alphaValue = EditorGUILayout.Slider("Alpha", _brush.alphaValue, 0f, 1f);
            else
                _brush.color = EditorGUILayout.ColorField(
                    new GUIContent("Couleur"), _brush.color, true, false, false);
        }

        void DrawMaskInfo()
        {
            switch (_selectMode)
            {
                case SelectMode.Vertices:
                    if (_selection != null && !_selection.IsEmpty)
                        EditorGUILayout.HelpBox(
                            $"Masque actif : {_selection.Count} vertices. L'opération est " +
                            "limitée à la sélection.", MessageType.Info);
                    else
                        EditorGUILayout.HelpBox(
                            "Mode Select = Vertices, mais sélection vide : opération libre " +
                            "(fallback Free).", MessageType.None);
                    break;

                case SelectMode.Faces:
                    if (_faceSelection != null && !_faceSelection.IsEmpty)
                        EditorGUILayout.HelpBox(
                            $"Masque actif : {_faceSelection.Count} faces. L'opération est " +
                            "limitée aux vertices de ces faces.", MessageType.Info);
                    else
                        EditorGUILayout.HelpBox(
                            "Mode Select = Faces, mais sélection vide : opération libre " +
                            "(fallback Free).", MessageType.None);
                    break;
            }
        }

        // --- Toggles de canaux RGBA avec exclusivité A / RGB ---
        void DrawChannelToggles()
        {
            // Cohérence : le canal A est exclusif avec les canaux RGB.
            if (_channelA)
            {
                _channelR = false;
                _channelG = false;
                _channelB = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_channelA))
                {
                    _channelR = GUILayout.Toggle(_channelR, "R", "Button");
                    _channelG = GUILayout.Toggle(_channelG, "G", "Button");
                    _channelB = GUILayout.Toggle(_channelB, "B", "Button");
                }

                EditorGUI.BeginChangeCheck();
                _channelA = GUILayout.Toggle(_channelA, "A", "Button");
                if (EditorGUI.EndChangeCheck())
                {
                    if (_channelA)
                    {
                        _channelR = false;
                        _channelG = false;
                        _channelB = false;
                    }
                    else
                    {
                        _channelR = true;
                        _channelG = true;
                        _channelB = true;
                    }
                }
            }
        }

        void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
            Repaint();
        }
    }
}
