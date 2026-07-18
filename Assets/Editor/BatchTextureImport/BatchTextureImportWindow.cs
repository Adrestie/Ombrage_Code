using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.BatchTextureImport
{
    public class BatchTextureImportWindow : EditorWindow
    {
        [MenuItem("Window/Ombrage Tools/Texture/Batch Texture Import %&t")]
        private static void Open()
        {
            var window = GetWindow<BatchTextureImportWindow>("Batch Texture Import");
            window.minSize = new Vector2(460, 520);
        }

        // Folders
        private List<DefaultAsset> _folders = new List<DefaultAsset>();
        private bool _includeSubFolders = true;

        // Override toggles + values
        private bool _overrideTextureType;
        private TextureImporterType _textureType = TextureImporterType.Default;

        private bool _overrideTextureShape;
        private TextureImporterShape _textureShape = TextureImporterShape.Texture2D;

        private bool _overrideSRGB;
        private bool _sRGB = true;

        private bool _overrideAlphaSource;
        private TextureImporterAlphaSource _alphaSource = TextureImporterAlphaSource.FromInput;

        private bool _overrideAlphaIsTransparency;
        private bool _alphaIsTransparency;

        private bool _overrideReadWrite;
        private bool _readWriteEnabled;

        private bool _overrideMipMaps;
        private bool _generateMipMaps = true;

        private bool _overrideMipMapFilter;
        private TextureImporterMipFilter _mipMapFilter = TextureImporterMipFilter.BoxFilter;

        private bool _overrideFilterMode;
        private FilterMode _filterMode = FilterMode.Bilinear;

        private bool _overrideAnisoLevel;
        private int _anisoLevel = 1;

        private bool _overrideWrapMode;
        private TextureWrapMode _wrapMode = TextureWrapMode.Repeat;

        private bool _overrideMaxSize;
        private int _maxSize = 2048;

        private bool _overrideCompression;
        private TextureImporterCompression _compression = TextureImporterCompression.Compressed;

        private bool _overrideFormat;
        private TextureImporterFormat _format = TextureImporterFormat.Automatic;

        private bool _overrideCrunchCompression;
        private bool _crunchCompression;
        private int _crunchQuality = 50;

        // Preview
        private bool _showPreview;
        private Vector2 _scrollPos;
        private Vector2 _previewScroll;
        private List<string> _matchedTextures = new List<string>();

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawFolderSelection();
            EditorGUILayout.Space(8);
            DrawImportSettings();
            EditorGUILayout.Space(8);
            DrawActions();

            if (_showPreview)
            {
                EditorGUILayout.Space(8);
                DrawPreview();
            }

            EditorGUILayout.EndScrollView();
        }

        // ───────────────────────── Folders ─────────────────────────

        private void DrawFolderSelection()
        {
            EditorGUILayout.LabelField("Target Folders", EditorStyles.boldLabel);

            _includeSubFolders = EditorGUILayout.Toggle("Include sub-folders", _includeSubFolders);

            EditorGUILayout.Space(4);

            for (int i = 0; i < _folders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                _folders[i] = (DefaultAsset)EditorGUILayout.ObjectField(
                    _folders[i], typeof(DefaultAsset), false);

                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    _folders.RemoveAt(i);
                    i--;
                    _showPreview = false;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Add Folder", GUILayout.Width(120)))
            {
                _folders.Add(null);
            }

            EditorGUILayout.EndHorizontal();

            HandleDragAndDrop();
        }

        private void HandleDragAndDrop()
        {
            var evt = Event.current;
            var dropArea = GUILayoutUtility.GetLastRect();

            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;

            if (!dropArea.Contains(evt.mousePosition))
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is DefaultAsset folder)
                    {
                        string path = AssetDatabase.GetAssetPath(folder);
                        if (AssetDatabase.IsValidFolder(path) && !_folders.Contains(folder))
                            _folders.Add(folder);
                    }
                }

                _showPreview = false;
                evt.Use();
            }
        }

        // ───────────────────────── Import Settings ─────────────────────────

        private void DrawImportSettings()
        {
            EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Toggle settings you want to override. Only enabled settings will be applied.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            DrawOverrideEnum("Texture Type", ref _overrideTextureType, ref _textureType);
            DrawOverrideEnum("Texture Shape", ref _overrideTextureShape, ref _textureShape);
            DrawOverrideBool("sRGB (Color Texture)", ref _overrideSRGB, ref _sRGB);
            DrawOverrideEnum("Alpha Source", ref _overrideAlphaSource, ref _alphaSource);
            DrawOverrideBool("Alpha Is Transparency", ref _overrideAlphaIsTransparency, ref _alphaIsTransparency);
            DrawOverrideBool("Read/Write Enabled", ref _overrideReadWrite, ref _readWriteEnabled);
            DrawOverrideBool("Generate Mip Maps", ref _overrideMipMaps, ref _generateMipMaps);
            DrawOverrideEnum("Mip Map Filter", ref _overrideMipMapFilter, ref _mipMapFilter);
            DrawOverrideEnum("Filter Mode", ref _overrideFilterMode, ref _filterMode);
            DrawOverrideIntSlider("Aniso Level", ref _overrideAnisoLevel, ref _anisoLevel, 0, 16);
            DrawOverrideEnum("Wrap Mode", ref _overrideWrapMode, ref _wrapMode);
            DrawOverrideMaxSize();
            DrawOverrideEnum("Compression", ref _overrideCompression, ref _compression);
            DrawOverrideEnum("Format", ref _overrideFormat, ref _format);
            DrawOverrideCrunch();
        }

        private void DrawOverrideEnum<T>(string label, ref bool enabled, ref T value) where T : System.Enum
        {
            EditorGUILayout.BeginHorizontal();
            enabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(18));
            EditorGUI.BeginDisabledGroup(!enabled);
            value = (T)(object)EditorGUILayout.EnumPopup(label, value);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverrideBool(string label, ref bool enabled, ref bool value)
        {
            EditorGUILayout.BeginHorizontal();
            enabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(18));
            EditorGUI.BeginDisabledGroup(!enabled);
            value = EditorGUILayout.Toggle(label, value);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverrideIntSlider(string label, ref bool enabled, ref int value, int min, int max)
        {
            EditorGUILayout.BeginHorizontal();
            enabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(18));
            EditorGUI.BeginDisabledGroup(!enabled);
            value = EditorGUILayout.IntSlider(label, value, min, max);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverrideMaxSize()
        {
            EditorGUILayout.BeginHorizontal();
            _overrideMaxSize = EditorGUILayout.Toggle(_overrideMaxSize, GUILayout.Width(18));
            EditorGUI.BeginDisabledGroup(!_overrideMaxSize);
            _maxSize = EditorGUILayout.IntPopup("Max Size", _maxSize,
                new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384" },
                new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 });
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverrideCrunch()
        {
            EditorGUILayout.BeginHorizontal();
            _overrideCrunchCompression = EditorGUILayout.Toggle(_overrideCrunchCompression, GUILayout.Width(18));
            EditorGUI.BeginDisabledGroup(!_overrideCrunchCompression);
            _crunchCompression = EditorGUILayout.Toggle("Use Crunch Compression", _crunchCompression);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_overrideCrunchCompression && _crunchCompression)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(22);
                _crunchQuality = EditorGUILayout.IntSlider("Compressor Quality", _crunchQuality, 0, 100);
                EditorGUILayout.EndHorizontal();
            }
        }

        // ───────────────────────── Actions ─────────────────────────

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Preview", GUILayout.Height(28)))
            {
                RefreshMatchedTextures();
                _showPreview = true;
            }

            bool hasOverrides = HasAnyOverride();
            EditorGUI.BeginDisabledGroup(!hasOverrides || _matchedTextures.Count == 0 || !_showPreview);
            if (GUILayout.Button("Apply", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog(
                    "Batch Texture Import",
                    $"Apply import settings to {_matchedTextures.Count} texture(s)?\nThis cannot be undone easily for large batches.",
                    "Apply", "Cancel"))
                {
                    ApplySettings();
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            if (!hasOverrides && _showPreview)
            {
                EditorGUILayout.HelpBox("Enable at least one setting override to apply.", MessageType.Warning);
            }
        }

        // ───────────────────────── Preview ─────────────────────────

        private void DrawPreview()
        {
            EditorGUILayout.LabelField($"Textures Found ({_matchedTextures.Count})", EditorStyles.boldLabel);

            if (_matchedTextures.Count == 0)
            {
                EditorGUILayout.HelpBox("No textures found in the selected folder(s).", MessageType.Warning);
                return;
            }

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.MaxHeight(300));

            var pathStyle = new GUIStyle(EditorStyles.miniLabel) { richText = true };

            foreach (var texturePath in _matchedTextures)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                var icon = AssetDatabase.GetCachedIcon(texturePath);
                if (icon != null)
                {
                    var iconRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
                    GUI.DrawTexture(iconRect, icon);
                }

                string displayPath = texturePath.Replace("Assets/", "");
                EditorGUILayout.LabelField(displayPath, pathStyle);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // ───────────────────────── Logic ─────────────────────────

        private void RefreshMatchedTextures()
        {
            _matchedTextures.Clear();

            var validFolders = _folders
                .Where(f => f != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(AssetDatabase.IsValidFolder)
                .Distinct()
                .ToList();

            foreach (var folder in validFolders)
                CollectTextures(folder);

            _matchedTextures = _matchedTextures.Distinct().OrderBy(p => p).ToList();
        }

        private void CollectTextures(string folderPath)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!_includeSubFolders)
                {
                    string dir = Path.GetDirectoryName(path).Replace("\\", "/");
                    if (dir != folderPath)
                        continue;
                }

                _matchedTextures.Add(path);
            }
        }

        private void ApplySettings()
        {
            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < _matchedTextures.Count; i++)
                {
                    string path = _matchedTextures[i];

                    EditorUtility.DisplayProgressBar(
                        "Batch Texture Import",
                        $"Processing {Path.GetFileName(path)} ({i + 1}/{_matchedTextures.Count})",
                        (float)i / _matchedTextures.Count);

                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                        continue;

                    if (_overrideTextureType) importer.textureType = _textureType;
                    if (_overrideTextureShape) importer.textureShape = _textureShape;
                    if (_overrideSRGB) importer.sRGBTexture = _sRGB;
                    if (_overrideAlphaSource) importer.alphaSource = _alphaSource;
                    if (_overrideAlphaIsTransparency) importer.alphaIsTransparency = _alphaIsTransparency;
                    if (_overrideReadWrite) importer.isReadable = _readWriteEnabled;
                    if (_overrideMipMaps) importer.mipmapEnabled = _generateMipMaps;
                    if (_overrideMipMapFilter) importer.mipmapFilter = _mipMapFilter;
                    if (_overrideFilterMode) importer.filterMode = _filterMode;
                    if (_overrideAnisoLevel) importer.anisoLevel = _anisoLevel;
                    if (_overrideWrapMode) importer.wrapMode = _wrapMode;
                    if (_overrideMaxSize) importer.maxTextureSize = _maxSize;
                    if (_overrideCompression) importer.textureCompression = _compression;

                    if (_overrideFormat)
                    {
                        var platformSettings = importer.GetDefaultPlatformTextureSettings();
                        platformSettings.overridden = true;
                        platformSettings.format = _format;
                        importer.SetPlatformTextureSettings(platformSettings);
                    }

                    if (_overrideCrunchCompression)
                    {
                        importer.crunchedCompression = _crunchCompression;
                        if (_crunchCompression)
                            importer.compressionQuality = _crunchQuality;
                    }

                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Batch Texture Import] Settings applied to {_matchedTextures.Count} texture(s).");
            _showPreview = false;
        }

        private bool HasAnyOverride()
        {
            return _overrideTextureType || _overrideTextureShape || _overrideSRGB
                || _overrideAlphaSource || _overrideAlphaIsTransparency || _overrideReadWrite
                || _overrideMipMaps || _overrideMipMapFilter || _overrideFilterMode
                || _overrideAnisoLevel || _overrideWrapMode || _overrideMaxSize
                || _overrideCompression || _overrideFormat || _overrideCrunchCompression;
        }
    }
}
