using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;
using Ombrage.Tools.Core.Serialization;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Editor
{
    /// <summary>
    /// Main Rock Generator window. While open, it keeps a transient GameObject in the active
    /// scene (see <see cref="ScenePreview"/>) that updates live as parameters change, so the
    /// result is judged under the project's real environment. Closing the window removes
    /// that GameObject.
    ///
    /// The generation mode is the primary switch: Rock exposes the three rock algorithms;
    /// Cliff exposes the dedicated cliff generator. The free-form deformation lattice can be
    /// edited directly in the Scene view. "Generate" exports the result as FBX + JSON;
    /// "Load Preset" re-imports a JSON preset for further editing.
    /// </summary>
    public sealed class RockGeneratorWindow : EditorWindow
    {
        [SerializeField] RockGenerationSettings _settings;
        [SerializeField] bool _editFfd;
        [SerializeField] Bounds _ffdBox;

        SerializedObject _serializedSelf;
        SerializedProperty _settingsProperty;
        ScenePreview _preview;
        bool _previewEnsured;
        Vector2 _scroll;
        string _meshInfo = "-";
        string _error;
        string _status;

        [MenuItem("Window/Ombrage Tools/Mesh/Rock Generator")]
        static void Open()
        {
            var window = GetWindow<RockGeneratorWindow>();
            window.titleContent = new GUIContent("Rock Generator");
            window.minSize = new Vector2(380f, 520f);
        }

        void OnEnable()
        {
            if (_settings == null)
                _settings = RockGenerationSettings.CreateDefault();

            _serializedSelf = new SerializedObject(this);
            _settingsProperty = _serializedSelf.FindProperty(nameof(_settings));

            _preview = new ScenePreview();
            _previewEnsured = false;

            Undo.undoRedoPerformed += Regenerate;
            SceneView.duringSceneGui += OnSceneGui;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Regenerate;
            SceneView.duringSceneGui -= OnSceneGui;

            // Closing the window (or a domain reload) removes the preview from the scene.
            _preview?.Dispose();
            _preview = null;
        }

        void OnGUI()
        {
            EnsurePreview();

            DrawPreviewBar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            bool changed = DrawSettings();
            EditorGUILayout.EndScrollView();

            DrawActions();

            if (changed)
                Regenerate();
        }

        // ------------------------------------------------------------------
        // Sections
        // ------------------------------------------------------------------

        void DrawPreviewBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                bool exists = _preview != null && _preview.Exists;
                EditorGUILayout.LabelField(
                    "Preview", exists ? "live in active scene" : "(none)");
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (GUILayout.Button("Select in scene",
                            EditorStyles.miniButton, GUILayout.Width(110f)))
                        Selection.activeGameObject = _preview.Root;
                }
            }
        }

        /// <summary>Draws the settings UI. Returns true when a value changed this frame.</summary>
        bool DrawSettings()
        {
            _serializedSelf.Update();
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(Relative("presetName"), new GUIContent("Preset Name"));
            EditorGUILayout.PropertyField(Relative("seed"));

            SerializedProperty modeProp = Relative("mode");
            EditorGUILayout.PropertyField(modeProp);
            var mode = (GenerationMode)modeProp.enumValueIndex;

            EditorGUILayout.Space(6f);
            if (mode == GenerationMode.Rock)
            {
                SerializedProperty algorithmProp = Relative("algorithm");
                EditorGUILayout.PropertyField(algorithmProp);
                var algorithm = (GenerationAlgorithm)algorithmProp.enumValueIndex;

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Algorithm Parameters", EditorStyles.boldLabel);
                switch (algorithm)
                {
                    case GenerationAlgorithm.PrimitiveNoise:
                        EditorGUILayout.PropertyField(Relative("primitiveNoise"), true);
                        break;
                    case GenerationAlgorithm.ConvexHull:
                        EditorGUILayout.PropertyField(Relative("convexHull"), true);
                        break;
                    case GenerationAlgorithm.MarchingCubes:
                        EditorGUILayout.PropertyField(Relative("marchingCubes"), true);
                        break;
                }
            }
            else // Cliff
            {
                EditorGUILayout.LabelField("Cliff Parameters", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Dedicated cliff generator: a watertight block with layered strata and "
                    + "overhangs, meant to be planted against Unity Terrain.", MessageType.None);
                EditorGUILayout.PropertyField(Relative("cliff"), true);
            }

            DrawFfdParameters();

            bool changed = EditorGUI.EndChangeCheck();
            _serializedSelf.ApplyModifiedProperties();
            EnsureFfdArray();

            changed |= DrawFfdLatticeControls();
            return changed;
        }

        void DrawFfdParameters()
        {
            SerializedProperty ffdProp = Relative("ffd");
            SerializedProperty enabledProp = ffdProp.FindPropertyRelative("enabled");

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("FFD Box", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enabled"));

            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                EditorGUILayout.PropertyField(ffdProp.FindPropertyRelative("resolution"));
                EditorGUILayout.PropertyField(ffdProp.FindPropertyRelative("boundsPadding"));
            }
        }

        /// <summary>
        /// Draws the lattice edit toggle and reset button (after the change check, since
        /// these are tool actions rather than mesh parameters). Returns true if a reset
        /// happened and the mesh should be regenerated.
        /// </summary>
        bool DrawFfdLatticeControls()
        {
            bool ffdEnabled = _settings.ffd.enabled;
            bool resetRequested = false;

            using (new EditorGUI.DisabledScope(!ffdEnabled))
            {
                bool newEdit = EditorGUILayout.Toggle("Edit Lattice In Scene", _editFfd);
                if (newEdit != _editFfd)
                {
                    _editFfd = newEdit;
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Reset Lattice"))
                {
                    ResetFfdLattice();
                    resetRequested = true;
                }
            }

            if (ffdEnabled && _editFfd)
                EditorGUILayout.HelpBox(
                    "Drag the yellow control points in the Scene view to reshape the mesh.",
                    MessageType.None);

            return resetRequested;
        }

        void DrawActions()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate", GUILayout.Height(26f)))
                    Regenerate();

                if (GUILayout.Button("Generate (FBX + JSON)", GUILayout.Height(26f)))
                    OnExport();
            }

            if (GUILayout.Button("Load Preset (JSON)", GUILayout.Height(22f)))
                OnLoadPreset();

            EditorGUILayout.LabelField("Mesh", _meshInfo, EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(_error))
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            else if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        // ------------------------------------------------------------------
        // Scene view: FFD lattice editing
        // ------------------------------------------------------------------

        void OnSceneGui(SceneView sceneView)
        {
            if (!_editFfd || _settings == null || !_settings.ffd.enabled)
                return;
            if (_preview == null || !_preview.Exists)
                return;
            if (_ffdBox.size == Vector3.zero)
                return;

            EnsureFfdArray();

            bool changed = FfdSceneEditor.DrawAndEdit(
                _preview.Root.transform, _ffdBox, _settings.ffd, this);
            if (changed)
                Regenerate();
        }

        // ------------------------------------------------------------------
        // Logic
        // ------------------------------------------------------------------

        void EnsurePreview()
        {
            if (_previewEnsured || _preview == null)
                return;
            _previewEnsured = true;
            _preview.EnsureCreated();
            Regenerate();
        }

        void Regenerate()
        {
            if (_preview == null || _settings == null)
                return;

            try
            {
                RockBuildResult result = RockGenerator.Build(_settings);
                _ffdBox = result.FfdBox;

                Mesh mesh = result.Mesh.ToMesh(_settings.presetName);
                _preview.SetMesh(mesh);
                _meshInfo = result.Mesh.VertexCount + " verts / "
                            + result.Mesh.TriangleCount + " tris";
                _error = null;
            }
            catch (Exception e)
            {
                _error = e.Message;
                Debug.LogException(e);
            }

            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>Rebuilds the final mesh and exports it as FBX + JSON.</summary>
        void OnExport()
        {
            if (_settings == null)
                return;

            try
            {
                RockBuildResult result = RockGenerator.Build(_settings);
                ExportResult export = RockExporter.Export(_settings, result.Mesh);
                if (export.Completed)
                {
                    _status = export.Message;
                    _error = null;
                }
            }
            catch (Exception e)
            {
                _error = "Export failed: " + e.Message;
                Debug.LogException(e);
            }

            Repaint();
        }

        /// <summary>Re-imports a JSON preset, replacing the current settings.</summary>
        void OnLoadPreset()
        {
            string path = EditorUtility.OpenFilePanel(
                "Load Preset (JSON)", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                RockGenerationSettings loaded = new JsonSettingsSerializer().Deserialize(json);

                Undo.RecordObject(this, "Load Rock Preset");
                _settings = loaded;
                EnsureFfdArray();

                // The settings object was replaced: rebuild the serialized wrappers.
                _serializedSelf = new SerializedObject(this);
                _settingsProperty = _serializedSelf.FindProperty(nameof(_settings));

                _status = "Loaded preset: " + Path.GetFileName(path);
                _error = null;
                Regenerate();
            }
            catch (Exception e)
            {
                _error = "Load failed: " + e.Message;
                Debug.LogException(e);
            }

            Repaint();
        }

        /// <summary>Keeps the FFD offsets array sized to the current lattice resolution.</summary>
        void EnsureFfdArray()
        {
            if (_settings == null)
                return;

            FfdParameters ffd = _settings.ffd;
            int count = ffd.ControlPointCount;
            if (ffd.controlPointOffsets == null || ffd.controlPointOffsets.Length != count)
                ffd.controlPointOffsets = new Vector3[count];
        }

        void ResetFfdLattice()
        {
            if (_settings == null)
                return;
            Undo.RecordObject(this, "Reset FFD Lattice");
            _settings.ffd.controlPointOffsets = new Vector3[_settings.ffd.ControlPointCount];
        }

        SerializedProperty Relative(string name) => _settingsProperty.FindPropertyRelative(name);
    }
}
