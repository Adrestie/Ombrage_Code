using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>Portée d'une projection : tout le mesh ou les faces sélectionnées.</summary>
    public enum UVSelectionScope
    {
        WholeMesh = 0,
        SelectedFaces = 1
    }

    /// <summary>
    /// Fenêtre éditeur de l'UV Editor.
    ///
    /// Le mesh assigné dans "Mesh source" est chargé automatiquement (copie de
    /// travail). Les projections UV (planaire, box, cylindrique, sphérique,
    /// triplanaire) s'appliquent sur le canal choisi, puis le résultat est
    /// sauvegardé en <c>.asset</c> avec backup versionné. Aperçu SceneView via
    /// un damier UV (ou le material réel).
    ///
    /// Modèle d'édition — aperçu temps réel :
    ///  - tout changement (gizmo, type, tiling/offset/rotation, canal, sélection)
    ///    ré-applique immédiatement la projection (WYSIWYG) ;
    ///  - la reprojection part TOUJOURS du dernier état committé, jamais du
    ///    résultat intermédiaire précédent — sinon la box dédoublerait des
    ///    vertices à l'infini ;
    ///  - le résultat est COMMITÉ (empilé dans l'historique interne) à la fin
    ///    d'un geste continu (relâchement de souris) ou immédiatement pour un
    ///    changement discret. Les valeurs intermédiaires d'un drag ne créent pas
    ///    d'entrée. Le commit en fin de geste permet de CUMULER des projections.
    ///
    /// Annulation : l'outil gère son PROPRE historique (voir
    /// <see cref="UVEditHistory"/>) et ne touche jamais à l'Undo d'Unity.
    /// Ctrl+Z / Ctrl+Y agissent uniquement quand cette fenêtre a le focus.
    ///
    /// Phase 1 — l'éditeur 2D interactif, la détection d'îlots et les
    /// transformations d'îlots seront ajoutés en phase 2.
    /// </summary>
    public class UVEditorWindow : EditorWindow
    {
        // --- État sérialisé (persiste avec la fenêtre, y compris après reload) ---
        [SerializeField] Mesh _targetMeshAsset;
        [SerializeField] Material _displayMaterial;
        [SerializeField] bool _showMaterial;
        [SerializeField] int _uvChannel;                 // 0..2
        [SerializeField] UVProjectionSettings _projection;
        [SerializeField] UVSelectionScope _scope = UVSelectionScope.WholeMesh;
        [SerializeField] UVSelectionMode _selectionMode = UVSelectionMode.Faces;
        [SerializeField] bool _backfaceCull = true;
        [SerializeField, Range(0f, 89f)] float _backfaceThreshold = 5f;
        [SerializeField] bool _occlusion = true;
        [SerializeField, Range(1f, 64f)] float _checkerTiling = 8f;
        [SerializeField] bool _frameInitialized;
        // Mesh de travail : résultat live courant, affiché et sauvegardé.
        [SerializeField] Mesh _workingMesh;
        // Vrai si la projection a déjà été appliquée au moins une fois (sinon le
        // mesh affiche encore ses UV d'origine).
        [SerializeField] bool _projectionStarted;

        // --- État runtime (reconstruit à l'activation de la fenêtre) ---
        Vector3[] _previewVertices;
        int[] _previewTriangles;
        Vector2[] _channelUVs;
        UVPreviewInstance _preview;
        UVEditorSceneController _sceneController;
        // Sélection unifiée (vertices ; les faces en dérivent).
        UVSelection _selection;
        // Carte des doublons de couture (résolution position -> vertices).
        UVWeldMap _weldMap;
        UVMeshRaycaster _raycaster;
        // Viewport 2D intégré (affichage du layout UV + sélection).
        UVViewport2D _viewport;

        // Historique d'annulation interne (indépendant de l'Undo d'Unity).
        UVEditHistory _history;
        // Snapshot de base servant de point de départ à la reprojection live
        // (= dernier état committé). Reconstruit depuis l'historique.
        UVEditHistory.Snapshot _baseSnapshot;
        // Vrai si un geste continu a produit un résultat non encore committé.
        bool _pendingCommit;

        // Largeur du panneau de gauche (réglages) dans le diptyque, ajustable
        // par le séparateur vertical et mémorisée.
        [SerializeField] float _leftPanelWidth = 360f;
        const float MinLeftPanelWidth = 300f;
        const float MinRightPanelWidth = 220f;
        const float SplitterWidth = 6f;
        bool _draggingSplitter;
        // Drapeaux de cadrage du viewport (runtime).
        bool _viewportFramedOnce;
        bool _viewportRecenterRequested;

        string _status = "Assigne un mesh dans « Mesh source » pour commencer.";
        MessageType _statusType = MessageType.Info;
        Vector2 _scroll;

        // ------------------------------------------------------------------
        // Accesseurs pour le contrôleur de SceneView
        // ------------------------------------------------------------------

        public bool HasWorkingMesh => _workingMesh != null;
        public Mesh WorkingMesh => _workingMesh;
        public Vector3[] PreviewVertices => _previewVertices;
        public int[] PreviewTriangles => _previewTriangles;
        public Vector2[] ChannelUVs => _channelUVs;
        public UVPreviewInstance Preview => _preview;
        public UVMeshRaycaster Raycaster => _raycaster;
        public UVSelection Selection => _selection;
        public UVWeldMap WeldMap => _weldMap;
        public UVSelectionMode SelectionMode => _selectionMode;
        public UVSelectionScope Scope => _scope;
        public bool BackfaceCull => _backfaceCull;
        public bool Occlusion => _occlusion;
        public float CheckerTiling => _checkerTiling;
        public UVProjectionSettings ProjectionSettings => _projection;

        /// <summary>Seuil de backface cull converti en produit scalaire.</summary>
        public float BackfaceThresholdDot => Mathf.Sin(_backfaceThreshold * Mathf.Deg2Rad);

        public Bounds WorkingMeshBounds =>
            _workingMesh != null ? _workingMesh.bounds : new Bounds();

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        [MenuItem("Window/Ombrage Tools/Mesh/UV Editor")]
        public static void Open()
        {
            var window = GetWindow<UVEditorWindow>();
            window.titleContent = new GUIContent("UV Editor");
            window.minSize = new Vector2(340f, 480f);
            window.Show();
        }

        void OnEnable()
        {
            if (_projection == null)
                _projection = new UVProjectionSettings();
            _projection.Validate();

            _selection = new UVSelection();
            _weldMap = new UVWeldMap();
            _raycaster = new UVMeshRaycaster();
            _history = new UVEditHistory(50);
            _viewport = new UVViewport2D();

            _sceneController = new UVEditorSceneController(this);
            _sceneController.Enable();

            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // Reconstruit l'état de preview après un reload de scripts.
            if (_workingMesh != null)
            {
                RecreatePreview();
                RefreshWorkingArrays(true);
                ApplyMaterialPreview();
                // L'historique runtime est perdu au reload : on le réamorce sur
                // l'état courant du mesh de travail.
                _history.Clear();
                PushHistory();
            }
        }

        void OnDisable()
        {
            if (_sceneController != null)
                _sceneController.Disable();

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            // La preview est transitoire : détruite ici, recréée à l'activation.
            if (_preview != null)
                _preview.Destroy();
        }

        void OnDestroy()
        {
            // Fermeture effective de la fenêtre : on libère le mesh de travail.
            DestroyWorkingMesh();
        }

        void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if (_preview != null)
                        _preview.Destroy();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (_workingMesh != null)
                    {
                        RecreatePreview();
                        RefreshWorkingArrays(true);
                        ApplyMaterialPreview();
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Chargement / preview
        // ------------------------------------------------------------------

        void RecreatePreview()
        {
            _preview = new UVPreviewInstance();
            _preview.Create(_workingMesh);
        }

        void DestroyWorkingMesh()
        {
            if (_workingMesh != null)
            {
                DestroyImmediate(_workingMesh);
                _workingMesh = null;
            }
        }

        void LoadMesh(Mesh source)
        {
            if (source == null)
            {
                DestroyWorkingMesh();
                if (_preview != null)
                    _preview.Destroy();
                _projectionStarted = false;
                _pendingCommit = false;
                if (_history != null)
                    _history.Clear();
                SetStatus("Aucun mesh assigné.", MessageType.Info);
                SceneView.RepaintAll();
                return;
            }

            if (!source.isReadable)
            {
                SetStatus("Mesh non lisible — clique sur « Activer Read/Write ».",
                    MessageType.Warning);
                return;
            }

            DestroyWorkingMesh();

            _workingMesh = Instantiate(source);
            _workingMesh.name = source.name;
            _workingMesh.hideFlags = HideFlags.HideAndDontSave;

            _targetMeshAsset = source;
            _projectionStarted = false;
            _pendingCommit = false;
            _viewportFramedOnce = false;

            RecreatePreview();
            RefreshWorkingArrays(true);

            if (!_frameInitialized)
            {
                _projection.FitToBounds(_workingMesh.bounds);
                _frameInitialized = true;
            }

            // L'historique repart de zéro : l'état initial (UV d'origine) en
            // devient le premier cran.
            _history.Clear();
            PushHistory();

            ApplyMaterialPreview();

            if (_workingMesh.subMeshCount > 1)
            {
                SetStatus($"Mesh chargé : {source.name} — ATTENTION : ce mesh a " +
                          $"{_workingMesh.subMeshCount} sous-mesh ; la phase 1 ne " +
                          $"gère qu'un seul sous-mesh.", MessageType.Warning);
            }
            else
            {
                SetStatus($"Mesh chargé : {source.name} " +
                          $"({_workingMesh.vertexCount} vertices, " +
                          $"{_previewTriangles.Length / 3} faces). Les UV d'origine " +
                          $"sont affichées ; la projection s'applique dès la " +
                          $"première modification.", MessageType.Info);
            }

            SceneView.RepaintAll();
        }

        void EnableReadWrite()
        {
            if (_targetMeshAsset == null)
            {
                SetStatus("Assigne d'abord un mesh.", MessageType.Warning);
                return;
            }

            string path = AssetDatabase.GetAssetPath(_targetMeshAsset);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                SetStatus("Read/Write s'applique aux modèles importés (FBX, OBJ…). " +
                          "Ce mesh n'est pas un modèle importé.", MessageType.Warning);
                return;
            }

            importer.isReadable = true;
            importer.SaveAndReimport();
            SetStatus("Read/Write activé sur le modèle source.", MessageType.Info);
            LoadMesh(_targetMeshAsset);
        }

        // Rafraîchit les tableaux runtime à partir du mesh de travail.
        // resetSelection : vide la sélection (chargement d'un mesh).
        // Sinon, la sélection est préservée par position des vertices — robuste
        // au changement de nombre de vertices dû à une reconstruction (box).
        void RefreshWorkingArrays(bool resetSelection)
        {
            if (_workingMesh == null)
                return;

            // Positions de sélection à conserver (avant rafraîchissement).
            HashSet<long> selectedPositions = null;
            if (!resetSelection && _previewVertices != null && _selection != null &&
                !_selection.IsEmpty)
            {
                selectedPositions = new HashSet<long>();
                foreach (int v in _selection.SelectedVertices)
                {
                    if (v >= 0 && v < _previewVertices.Length)
                        selectedPositions.Add(PositionKey(_previewVertices[v]));
                }
            }

            _previewVertices = _workingMesh.vertices;
            _previewTriangles = _workingMesh.triangles;
            _channelUVs = UVChannelUtils.GetChannel(_workingMesh, _uvChannel);
            _raycaster.SetMesh(_workingMesh);
            _weldMap.Build(_previewVertices);

            if (resetSelection)
            {
                _selection.SetMesh(_previewVertices.Length, _previewTriangles);
            }
            else
            {
                _selection.SetMesh(_previewVertices.Length, _previewTriangles);
                if (selectedPositions != null)
                {
                    // Réinjecte la sélection : un vertex est sélectionné si sa
                    // position l'était.
                    for (int v = 0; v < _previewVertices.Length; v++)
                    {
                        if (selectedPositions.Contains(PositionKey(_previewVertices[v])))
                            _selection.AddVertex(v);
                    }
                }
            }
        }

        // Clé de position pour comparer des vertices coïncidents.
        static long PositionKey(Vector3 p)
        {
            const float Q = 100000f;
            long x = (long)Mathf.Round(p.x * Q);
            long y = (long)Mathf.Round(p.y * Q);
            long z = (long)Mathf.Round(p.z * Q);
            unchecked
            {
                long h = 1469598103934665603L;
                h = (h ^ x) * 1099511628211L;
                h = (h ^ y) * 1099511628211L;
                h = (h ^ z) * 1099511628211L;
                return h;
            }
        }

        void ApplyMaterialPreview()
        {
            if (_preview != null && _preview.Exists)
                _preview.SetMaterialPreview(_showMaterial, _displayMaterial);
        }

        // ------------------------------------------------------------------
        // Historique interne (snapshots)
        // ------------------------------------------------------------------

        // Construit un instantané complet de l'état courant.
        UVEditHistory.Snapshot CaptureSnapshot()
        {
            var s = new UVEditHistory.Snapshot();
            Mesh m = _workingMesh;

            s.positions = m.vertices;
            s.triangles = m.triangles;

            var normals = m.normals;
            s.hasNormals = normals != null && normals.Length == s.positions.Length;
            s.normals = s.hasNormals ? normals : null;

            var tangents = m.tangents;
            s.hasTangents = tangents != null && tangents.Length == s.positions.Length;
            s.tangents = s.hasTangents ? tangents : null;

            var colors = m.colors;
            s.hasColors = colors != null && colors.Length == s.positions.Length;
            s.colors = s.hasColors ? colors : null;

            s.uv0 = CaptureUV(m, 0, s.positions.Length, out s.hasUV0);
            s.uv1 = CaptureUV(m, 1, s.positions.Length, out s.hasUV1);
            s.uv2 = CaptureUV(m, 2, s.positions.Length, out s.hasUV2);

            s.projection = _projection.Clone();
            s.projectionStarted = _projectionStarted;

            s.vertexCount = _selection.VertexCount;
            var sel = new List<int>(_selection.SelectedVertices);
            s.selectedVertices = sel.ToArray();

            return s;
        }

        static Vector2[] CaptureUV(Mesh m, int channel, int vertexCount, out bool has)
        {
            var list = new List<Vector2>();
            m.GetUVs(channel, list);
            has = list.Count == vertexCount;
            return has ? list.ToArray() : null;
        }

        // Réécrit le mesh de travail + l'état de l'outil depuis un instantané.
        void RestoreSnapshot(UVEditHistory.Snapshot s)
        {
            if (s == null || _workingMesh == null)
                return;

            Mesh m = _workingMesh;
            m.Clear();
            m.indexFormat = s.positions.Length > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            m.SetVertices(s.positions);
            if (s.hasNormals) m.SetNormals(s.normals);
            if (s.hasColors) m.SetColors(s.colors);
            if (s.hasUV0) m.SetUVs(0, s.uv0);
            if (s.hasUV1) m.SetUVs(1, s.uv1);
            if (s.hasUV2) m.SetUVs(2, s.uv2);

            m.subMeshCount = 1;
            m.SetTriangles(s.triangles, 0);

            if (s.hasTangents)
                m.SetTangents(new List<Vector4>(s.tangents));
            else
                m.RecalculateTangents();

            if (!s.hasNormals)
                m.RecalculateNormals();
            m.RecalculateBounds();

            // Réglages de projection.
            _projection.CopyFrom(s.projection);
            _projection.Validate();
            _projectionStarted = s.projectionStarted;

            // Rafraîchit les tableaux (reset : on va réinjecter la sélection
            // exacte du snapshot juste après, pas la préserver par position).
            RefreshWorkingArrays(true);

            // Sélection de vertices du snapshot.
            if (s.selectedVertices != null)
            {
                _selection.SetVertices(s.selectedVertices);
            }

            ApplyMaterialPreview();
        }

        // Empile l'état courant comme nouveau cran d'historique.
        void PushHistory()
        {
            if (_workingMesh == null)
                return;
            _history.Push(CaptureSnapshot());
            RefreshBaseSnapshot();
        }

        // Le snapshot de base (départ de la reprojection live) = cran courant.
        void RefreshBaseSnapshot()
        {
            _baseSnapshot = _history.Current;
        }

        // ------------------------------------------------------------------
        // Projection temps réel + commit
        // ------------------------------------------------------------------

        /// <summary>
        /// Recalcule le mesh de travail : restaure le snapshot de base puis
        /// applique la projection courante. N'empile RIEN dans l'historique.
        /// Retourne vrai si une projection a effectivement été appliquée.
        /// </summary>
        bool LiveProject()
        {
            if (_workingMesh == null || _baseSnapshot == null)
                return false;

            // Réglages de projection courants (RestoreSnapshot va les écraser
            // avec ceux du snapshot de base) et positions des vertices
            // actuellement sélectionnés (la sélection se conserve par position).
            UVProjectionSettings keepProjection = _projection.Clone();
            HashSet<long> selectedPositions = null;
            if (_previewVertices != null && _selection != null && !_selection.IsEmpty)
            {
                selectedPositions = new HashSet<long>();
                foreach (int v in _selection.SelectedVertices)
                {
                    if (v >= 0 && v < _previewVertices.Length)
                        selectedPositions.Add(PositionKey(_previewVertices[v]));
                }
            }

            // Restaure la géométrie de base (réinitialise la sélection).
            RestoreSnapshot(_baseSnapshot);
            _projection.CopyFrom(keepProjection);
            _projection.Validate();

            // Réinjecte la sélection courante sur la géométrie de base.
            if (selectedPositions != null)
            {
                for (int v = 0; v < _previewVertices.Length; v++)
                {
                    if (selectedPositions.Contains(PositionKey(_previewVertices[v])))
                        _selection.AddVertex(v);
                }
            }

            bool[] mask = null;
            if (_scope == UVSelectionScope.SelectedFaces)
            {
                mask = _selection.BuildTriangleMask();
                if (mask == null)
                {
                    // Portée "faces sélectionnées" mais aucune face entièrement
                    // sélectionnée : on affiche l'état de base sans projeter.
                    SetStatus("Portée « Faces sélectionnées » : sélectionne des " +
                              "faces (3 vertices) pour projeter.",
                        MessageType.Info);
                    Repaint();
                    SceneView.RepaintAll();
                    return false;
                }
            }

            UVProjection.Result result =
                UVProjection.Apply(_workingMesh, _uvChannel, mask, _projection);

            RefreshWorkingArrays(false);
            ApplyMaterialPreview();
            Repaint();
            SceneView.RepaintAll();

            if (!result.success)
            {
                SetStatus(result.message, MessageType.Error);
                return false;
            }

            _projectionStarted = true;
            SetStatus(result.message, MessageType.Info);
            return true;
        }

        /// <summary>
        /// Fige le résultat courant : empile un cran dans l'historique interne.
        /// Appelé en fin de geste continu ou après un changement discret.
        /// </summary>
        void Commit()
        {
            if (_workingMesh == null)
            {
                _pendingCommit = false;
                return;
            }
            PushHistory();
            _pendingCommit = false;
        }

        /// <summary>Commit en attente éventuel (fin de geste continu).</summary>
        public void CommitIfPending()
        {
            if (_pendingCommit)
                Commit();
        }

        /// <summary>
        /// Changement continu (drag de gizmo ou de slider) : reprojette en live
        /// et arme le commit, qui se déclenchera au relâchement de la souris.
        /// </summary>
        public void ApplyLiveContinuous()
        {
            if (LiveProject())
                _pendingCommit = true;
        }

        /// <summary>
        /// Changement discret (popup, sélection) : reprojette en live et commit
        /// immédiatement.
        /// </summary>
        public void ApplyLiveDiscrete()
        {
            if (LiveProject())
                Commit();
        }

        /// <summary>
        /// Notifié par les vues quand la sélection a changé (clic / rectangle,
        /// 2D ou 3D). En portée « faces sélectionnées », la projection est
        /// reprojetée en direct. Dans tous les cas, un cran d'historique est
        /// figé pour que la sélection soit annulable.
        /// </summary>
        public void OnSelectionChanged()
        {
            if (!HasWorkingMesh)
                return;

            if (_scope == UVSelectionScope.SelectedFaces && _projectionStarted)
            {
                // La reprojection live, puis le commit, capturent aussi la
                // nouvelle sélection dans le snapshot.
                ApplyLiveDiscrete();
            }
            else
            {
                // Pas de reprojection : on fige juste l'état (sélection incluse).
                Commit();
            }

            Repaint();
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------
        // Annulation interne (Ctrl+Z / Ctrl+Y, focus fenêtre uniquement)
        // ------------------------------------------------------------------

        void PerformUndo()
        {
            // Un geste continu non committé est d'abord figé pour ne pas le perdre.
            CommitIfPending();

            UVEditHistory.Snapshot s = _history.Undo();
            if (s == null)
            {
                SetStatus("Rien à annuler.", MessageType.Info);
                return;
            }
            RestoreSnapshot(s);
            RefreshBaseSnapshot();
            SetStatus("Annulé.", MessageType.Info);
            Repaint();
            SceneView.RepaintAll();
        }

        void PerformRedo()
        {
            UVEditHistory.Snapshot s = _history.Redo();
            if (s == null)
            {
                SetStatus("Rien à rétablir.", MessageType.Info);
                return;
            }
            RestoreSnapshot(s);
            RefreshBaseSnapshot();
            SetStatus("Rétabli.", MessageType.Info);
            Repaint();
            SceneView.RepaintAll();
        }

        // Intercepte Ctrl+Z / Ctrl+Y quand la fenêtre de l'outil a le focus.
        // L'événement est consommé pour ne pas atteindre l'Undo de la scène.
        void HandleUndoShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown)
                return;

            bool ctrl = e.control || e.command;
            if (!ctrl)
                return;

            if (e.keyCode == KeyCode.Z && !e.shift)
            {
                PerformUndo();
                e.Use();
            }
            else if ((e.keyCode == KeyCode.Y) ||
                     (e.keyCode == KeyCode.Z && e.shift))
            {
                PerformRedo();
                e.Use();
            }
        }

        // ------------------------------------------------------------------
        // Sauvegarde
        // ------------------------------------------------------------------

        void DoSave()
        {
            if (!HasWorkingMesh || _targetMeshAsset == null)
            {
                SetStatus("Rien à sauvegarder.", MessageType.Warning);
                return;
            }

            CommitIfPending();

            UVMeshIO.SaveResult result = UVMeshIO.Save(_workingMesh, _targetMeshAsset);

            if (result.success)
            {
                if (result.savedAsset != null)
                {
                    _targetMeshAsset = result.savedAsset;
                }

                string msg = result.message;
                if (!string.IsNullOrEmpty(result.backupPath))
                    msg += "\nBackup de l'ancien .asset : " + result.backupPath;
                SetStatus(msg, MessageType.Info);
            }
            else
            {
                SetStatus(result.message, MessageType.Error);
            }
        }

        void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }

        // ------------------------------------------------------------------
        // Interface
        // ------------------------------------------------------------------

        void OnGUI()
        {
            // Raccourcis d'annulation : traités avant le reste de l'UI, et
            // uniquement ici — ils n'affectent jamais l'Undo de la scène.
            HandleUndoShortcuts();

            // Diptyque : réglages à gauche, viewport UV à droite, séparateur
            // vertical déplaçable entre les deux.
            Rect full = new Rect(0f, 0f, position.width, position.height);

            // Clamp de la largeur du panneau gauche aux bornes courantes.
            float maxLeft = Mathf.Max(MinLeftPanelWidth,
                full.width - MinRightPanelWidth - SplitterWidth);
            _leftPanelWidth = Mathf.Clamp(_leftPanelWidth,
                MinLeftPanelWidth, maxLeft);

            Rect leftRect = new Rect(0f, 0f, _leftPanelWidth, full.height);
            Rect splitterRect = new Rect(leftRect.xMax, 0f,
                SplitterWidth, full.height);
            Rect rightRect = new Rect(splitterRect.xMax, 0f,
                full.width - splitterRect.xMax, full.height);

            DrawLeftPanel(leftRect);
            DrawSplitter(splitterRect);
            DrawRightPanel(rightRect);

            // Relâchement de souris dans la fenêtre = fin d'un geste de slider :
            // on fige le résultat (commit unique pour tout le drag).
            if (Event.current.type == EventType.MouseUp)
                CommitIfPending();
        }

        // Panneau gauche : tous les réglages, dans une zone scrollable.
        void DrawLeftPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawMeshSection();
            DrawChannelSection();
            DrawScopeSection();
            DrawProjectionSection();
            DrawPreviewSection();
            DrawSaveSection();

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(_status, _statusType);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // Séparateur vertical déplaçable entre les deux panneaux.
        void DrawSplitter(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.14f, 1f));
            // Liseré central pour matérialiser la poignée.
            Rect grip = new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height);
            EditorGUI.DrawRect(grip, new Color(0.32f, 0.32f, 0.34f, 1f));

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition) && e.button == 0)
                    {
                        _draggingSplitter = true;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_draggingSplitter && GUIUtility.hotControl == id)
                    {
                        _leftPanelWidth += e.delta.x;
                        Repaint();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        _draggingSplitter = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        // Panneau droit : viewport UV 2D, pleine hauteur. Tout est dessiné en
        // coordonnées fenêtre (pas de GUILayout.BeginArea), pour que le rendu GL
        // et l'IMGUI partagent le même repère — sans quoi le contenu se décale.
        void DrawRightPanel(Rect rect)
        {
            const float headerHeight = 22f;

            // En-tête : titre + bouton Recadrer.
            Rect headerRect = new Rect(rect.x + 4f, rect.y + 3f,
                rect.width - 8f, headerHeight);
            GUI.Label(new Rect(headerRect.x, headerRect.y, 200f, headerRect.height),
                "Viewport UV (2D)", EditorStyles.boldLabel);

            Rect recenterRect = new Rect(headerRect.xMax - 72f, headerRect.y,
                72f, 18f);
            using (new EditorGUI.DisabledScope(!HasWorkingMesh))
            {
                if (GUI.Button(recenterRect, "Recadrer", EditorStyles.miniButton))
                {
                    _viewportRecenterRequested = true;
                    Repaint();
                }
            }

            // Le viewport occupe tout l'espace sous l'en-tête.
            Rect viewRect = new Rect(
                rect.x,
                rect.y + headerHeight + 4f,
                rect.width,
                rect.height - headerHeight - 4f);

            if (Event.current.type == EventType.Repaint && _viewportRecenterRequested)
            {
                _viewport.FrameUnitSquare(viewRect);
                _viewportRecenterRequested = false;
            }

            // Premier affichage d'un mesh : cadrage automatique sur [0,1].
            if (Event.current.type == EventType.Repaint &&
                HasWorkingMesh && !_viewportFramedOnce)
            {
                _viewport.FrameUnitSquare(viewRect);
                _viewportFramedOnce = true;
            }

            if (_viewport.HandleNavigation(viewRect))
                Repaint();

            // Sélection dans le viewport (après la navigation, qui est
            // prioritaire). Le changement éventuel est notifié à la fenêtre.
            if (HasWorkingMesh)
            {
                bool changed = _viewport.HandleSelectionInput(viewRect,
                    _channelUVs, _previewTriangles, _selection, _selectionMode,
                    out bool committed);
                if (changed && committed)
                    OnSelectionChanged();
                else if (changed)
                    Repaint();
            }

            _viewport.Draw(viewRect,
                HasWorkingMesh ? _channelUVs : null,
                HasWorkingMesh ? _previewTriangles : null,
                _uvChannel,
                HasWorkingMesh ? _selection : null,
                _selectionMode);
        }

        void DrawMeshSection()
        {
            EditorGUILayout.LabelField("Mesh", EditorStyles.boldLabel);

            // L'assignation du champ déclenche directement le chargement.
            EditorGUI.BeginChangeCheck();
            var mesh = (Mesh)EditorGUILayout.ObjectField("Mesh source",
                _targetMeshAsset, typeof(Mesh), false);
            if (EditorGUI.EndChangeCheck())
            {
                _targetMeshAsset = mesh;
                LoadMesh(mesh);
            }

            if (_targetMeshAsset != null && !_targetMeshAsset.isReadable)
            {
                EditorGUILayout.HelpBox("Ce mesh n'est pas en Read/Write : il faut " +
                    "l'activer pour pouvoir l'éditer. Le FBX n'est jamais modifié " +
                    "par l'outil.", MessageType.Warning);
                if (GUILayout.Button("Activer Read/Write"))
                    EnableReadWrite();
            }

            EditorGUI.BeginChangeCheck();
            _displayMaterial = (Material)EditorGUILayout.ObjectField("Material",
                _displayMaterial, typeof(Material), false);
            _showMaterial = EditorGUILayout.Toggle(
                new GUIContent("Afficher le material",
                    "Affiche le mesh avec son material réel au lieu du damier UV."),
                _showMaterial);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyMaterialPreview();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(6f);
        }

        void DrawChannelSection()
        {
            EditorGUILayout.LabelField("Canal UV", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            int channel = EditorGUILayout.Popup("Canal UV cible", _uvChannel,
                new[] { "UV0", "UV1", "UV2" });
            if (EditorGUI.EndChangeCheck())
            {
                _uvChannel = channel;
                if (_projectionStarted && HasWorkingMesh)
                    ApplyLiveDiscrete();
                else
                    RefreshWorkingArrays(false);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(6f);
        }

        void DrawScopeSection()
        {
            EditorGUILayout.LabelField("Portée de la projection", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _scope = (UVSelectionScope)EditorGUILayout.EnumPopup("Portée", _scope);
            if (EditorGUI.EndChangeCheck())
            {
                if (_projectionStarted && HasWorkingMesh)
                    ApplyLiveDiscrete();
                SceneView.RepaintAll();
            }

            if (_scope == UVSelectionScope.SelectedFaces)
            {
                EditorGUILayout.HelpBox(
                    "La projection s'applique aux faces entièrement sélectionnées " +
                    "(leurs 3 vertices sélectionnés).", MessageType.None);
            }

            EditorGUILayout.Space(6f);
            DrawSelectionSection();
        }

        // Section Sélection : mode, compteurs, actions, options de picking.
        // Toujours visible — la sélection sert à la projection ET (phase 2.4)
        // aux transformations.
        void DrawSelectionSection()
        {
            EditorGUILayout.LabelField("Sélection", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _selectionMode = (UVSelectionMode)EditorGUILayout.EnumPopup(
                new GUIContent("Mode",
                    "Granularité de sélection, commune à la SceneView et au " +
                    "viewport 2D."),
                _selectionMode);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            using (new EditorGUI.DisabledScope(!HasWorkingMesh))
            {
                EditorGUILayout.HelpBox(
                    "SceneView et viewport 2D : clic = un élément, clic-glissé = " +
                    "rectangle. Maj = ajouter, Ctrl/Cmd = retirer. La sélection " +
                    "est synchronisée entre les deux vues.",
                    MessageType.None);

                int selVerts = _selection != null ? _selection.SelectedVertexCount : 0;
                int selFaces = _selection != null ? _selection.CountSelectedTriangles() : 0;
                EditorGUILayout.LabelField("Sélectionné",
                    $"{selFaces} faces  •  {selVerts} vertices");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Tout"))
                    {
                        _selection.SelectAllVertices();
                        OnSelectionChanged();
                    }
                    if (GUILayout.Button("Rien"))
                    {
                        _selection.Clear();
                        OnSelectionChanged();
                    }
                    if (GUILayout.Button("Inverser"))
                    {
                        _selection.InvertVertices();
                        OnSelectionChanged();
                    }
                }

                _backfaceCull = EditorGUILayout.Toggle(
                    new GUIContent("Ignorer les faces arrière"), _backfaceCull);
                using (new EditorGUI.DisabledScope(!_backfaceCull))
                {
                    _backfaceThreshold = EditorGUILayout.Slider(
                        new GUIContent("Seuil (degrés)"),
                        _backfaceThreshold, 0f, 89f);
                }
                _occlusion = EditorGUILayout.Toggle(
                    new GUIContent("Test d'occlusion",
                        "Ignore les éléments masqués lors d'une sélection " +
                        "rectangle dans la SceneView."),
                    _occlusion);
            }

            EditorGUILayout.Space(6f);
        }

        void DrawProjectionSection()
        {
            EditorGUILayout.LabelField("Projection", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _projection.type = (UVProjectionType)EditorGUILayout.EnumPopup(
                "Type", _projection.type);
            if (EditorGUI.EndChangeCheck())
            {
                if (HasWorkingMesh)
                    ApplyLiveDiscrete();
                SceneView.RepaintAll();
            }

            if (_projection.UsesEditableFrame)
            {
                // Le mode de gizmo ne change pas la projection : pas de live.
                _projection.gizmoMode = (FrameGizmoMode)GUILayout.Toolbar(
                    (int)_projection.gizmoMode,
                    new[] { "Déplacer", "Tourner", "Redimensionner" });

                EditorGUI.BeginChangeCheck();
                _projection.frameCenter = EditorGUILayout.Vector3Field(
                    "Centre du cadre", _projection.frameCenter);
                _projection.frameEuler = EditorGUILayout.Vector3Field(
                    "Rotation du cadre", _projection.frameEuler);
                _projection.frameSize = EditorGUILayout.Vector3Field(
                    "Taille du cadre", _projection.frameSize);
                if (EditorGUI.EndChangeCheck())
                {
                    if (HasWorkingMesh)
                        ApplyLiveContinuous();
                    SceneView.RepaintAll();
                }

                using (new EditorGUI.DisabledScope(!HasWorkingMesh))
                {
                    if (GUILayout.Button("Caler le cadre sur le mesh"))
                    {
                        _projection.FitToBounds(_workingMesh.bounds);
                        ApplyLiveDiscrete();
                        SceneView.RepaintAll();
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Triplanaire : projection box automatique alignée sur la " +
                    "bounding box locale du mesh. Pas de cadre à régler.",
                    MessageType.None);
            }

            EditorGUI.BeginChangeCheck();
            _projection.tiling = EditorGUILayout.Vector2Field("Tiling", _projection.tiling);
            _projection.offset = EditorGUILayout.Vector2Field("Offset", _projection.offset);
            _projection.rotation = EditorGUILayout.Slider(
                "Rotation UV (°)", _projection.rotation, -180f, 180f);
            if (EditorGUI.EndChangeCheck())
            {
                if (HasWorkingMesh)
                    ApplyLiveContinuous();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(
                "Aperçu temps réel : la projection s'applique en continu. Le " +
                "résultat se fige en fin de geste ; les projections successives " +
                "se cumulent. Ctrl+Z / Ctrl+Y annulent et rétablissent (raccourcis " +
                "propres à l'outil, sans effet sur la scène).",
                MessageType.None);

            EditorGUILayout.Space(6f);
        }

        void DrawPreviewSection()
        {
            EditorGUILayout.LabelField("Aperçu", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _checkerTiling = EditorGUILayout.Slider(
                new GUIContent("Densité du damier"), _checkerTiling, 1f, 64f);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            EditorGUILayout.Space(6f);
        }

        void DrawSaveSection()
        {
            EditorGUILayout.LabelField("Sauvegarde", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!HasWorkingMesh || _targetMeshAsset == null))
            {
                if (GUILayout.Button("Sauvegarder (.asset + backup)",
                        GUILayout.Height(26f)))
                    DoSave();
            }

            EditorGUILayout.HelpBox(
                "La sauvegarde écrit un .asset à côté du mesh source. Si un .asset " +
                "de même nom existe, il est d'abord copié dans " +
                UVMeshIO.BackupRootFolder + "/ (versionné) puis écrasé en " +
                "conservant son identité. Le FBX n'est jamais modifié.",
                MessageType.None);
        }
    }
}
