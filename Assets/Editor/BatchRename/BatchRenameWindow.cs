using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ombrage.Tools.BatchRename
{
    public class BatchRenameWindow : EditorWindow
    {
        private enum ScopeMode { Selection, SceneSearch }
        private enum RenameMode { FullReplace, FindReplace }

        [MenuItem("Window/Ombrage Tools/Management/Batch Rename %&r")]
        private static void Open()
        {
            var window = GetWindow<BatchRenameWindow>("Batch Rename");
            window.minSize = new Vector2(420, 300);
        }

        // Scope
        private ScopeMode _scopeMode;
        private string _searchString = "";
        private bool _includeInactive = true;

        // Rename
        private RenameMode _renameMode;
        private string _newName = "";
        private string _findString = "";
        private string _replaceString = "";

        // Suffix
        private bool _addNumericSuffix;
        private int _digitCount = 3;
        private int _startIndex;

        // Preview
        private bool _showPreview;
        private Vector2 _previewScroll;

        // Exclusion toggles (scene search mode)
        private readonly Dictionary<EntityId, bool> _excludeMap = new();

        // Cached results
        private List<GameObject> _matchedObjects = new();
        private List<PreviewEntry> _previewEntries = new();

        private struct PreviewEntry
        {
            public string Path;
            public string OldName;
            public string NewName;
            public bool Excluded;
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            DrawScope();
            EditorGUILayout.Space(6);
            DrawRenameSettings();
            EditorGUILayout.Space(6);
            DrawSuffixSettings();
            EditorGUILayout.Space(6);
            DrawActions();

            if (_showPreview)
            {
                EditorGUILayout.Space(6);
                DrawPreview();
            }
        }

        // ───────────────────────── Scope ─────────────────────────

        private void DrawScope()
        {
            EditorGUILayout.LabelField("Scope", EditorStyles.boldLabel);

            _scopeMode = (ScopeMode)EditorGUILayout.EnumPopup("Mode", _scopeMode);

            if (_scopeMode == ScopeMode.SceneSearch)
            {
                _searchString = EditorGUILayout.TextField("Search in names", _searchString);
                _includeInactive = EditorGUILayout.Toggle("Include inactive objects", _includeInactive);
            }
        }

        // ───────────────────────── Rename ─────────────────────────

        private void DrawRenameSettings()
        {
            EditorGUILayout.LabelField("Rename", EditorStyles.boldLabel);

            _renameMode = (RenameMode)EditorGUILayout.EnumPopup("Mode", _renameMode);

            if (_renameMode == RenameMode.FullReplace)
            {
                _newName = EditorGUILayout.TextField("New name", _newName);
            }
            else
            {
                _findString = EditorGUILayout.TextField("Find", _findString);
                _replaceString = EditorGUILayout.TextField("Replace with", _replaceString);
            }
        }

        // ───────────────────────── Suffix ─────────────────────────

        private void DrawSuffixSettings()
        {
            EditorGUILayout.LabelField("Numeric suffix", EditorStyles.boldLabel);

            _addNumericSuffix = EditorGUILayout.Toggle("Add suffix _xxx", _addNumericSuffix);

            if (_addNumericSuffix)
            {
                _digitCount = EditorGUILayout.IntSlider("Digits", _digitCount, 1, 6);
                _startIndex = EditorGUILayout.IntField("Start index", _startIndex);
            }
        }

        // ───────────────────────── Actions ─────────────────────────

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Preview", GUILayout.Height(28)))
            {
                RefreshMatchedObjects();
                BuildPreview();
                _showPreview = true;
            }

            EditorGUI.BeginDisabledGroup(_previewEntries.Count == 0 || !_showPreview);
            if (GUILayout.Button("Apply", GUILayout.Height(28)))
            {
                ApplyRename();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            if (_showPreview)
            {
                int total = _previewEntries.Count;
                int active = _previewEntries.Count(e => !e.Excluded);
                EditorGUILayout.HelpBox($"{active} object(s) will be renamed (out of {total} matched).", MessageType.Info);
            }
        }

        // ───────────────────────── Preview ─────────────────────────

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandHeight(true));

            for (int i = 0; i < _previewEntries.Count; i++)
            {
                var entry = _previewEntries[i];

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Exclude toggle in scene search mode
                if (_scopeMode == ScopeMode.SceneSearch)
                {
                    bool excluded = entry.Excluded;
                    bool newExcluded = !EditorGUILayout.Toggle(!excluded, GUILayout.Width(18));
                    if (newExcluded != excluded)
                    {
                        entry.Excluded = newExcluded;
                        _previewEntries[i] = entry;

                        var id = _matchedObjects[i].GetEntityId();
                        _excludeMap[id] = newExcluded;
                    }
                }

                EditorGUI.BeginDisabledGroup(entry.Excluded);

                EditorGUILayout.BeginVertical();

                // Path
                var pathStyle = new GUIStyle(EditorStyles.miniLabel) { richText = true };
                EditorGUILayout.LabelField($"<color=#888888>{entry.Path}</color>", pathStyle);

                // Old -> New
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.OldName, GUILayout.MinWidth(60));
                EditorGUILayout.LabelField("\u2192", GUILayout.Width(20));

                var newNameStyle = new GUIStyle(EditorStyles.label);
                newNameStyle.normal.textColor = entry.OldName == entry.NewName
                    ? new Color(0.6f, 0.6f, 0.6f)
                    : new Color(0.4f, 0.8f, 0.4f);
                EditorGUILayout.LabelField(entry.NewName, newNameStyle, GUILayout.MinWidth(60));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();

                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // ───────────────────────── Logic ─────────────────────────

        private void RefreshMatchedObjects()
        {
            _matchedObjects.Clear();

            if (_scopeMode == ScopeMode.Selection)
            {
                // Sort by sibling index within each parent group
                _matchedObjects = Selection.gameObjects
                    .OrderBy(GetHierarchyOrder)
                    .ToList();
            }
            else
            {
                if (string.IsNullOrEmpty(_searchString))
                    return;

                var roots = new List<GameObject>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded)
                        roots.AddRange(scene.GetRootGameObjects());
                }

                foreach (var root in roots)
                    CollectMatching(root, _searchString, _includeInactive, _matchedObjects);

                _matchedObjects = _matchedObjects.OrderBy(GetHierarchyOrder).ToList();
            }
        }

        private static void CollectMatching(GameObject obj, string search, bool includeInactive, List<GameObject> results)
        {
            if (!includeInactive && !obj.activeInHierarchy)
                return;

            if (obj.name.Contains(search, StringComparison.OrdinalIgnoreCase))
                results.Add(obj);

            for (int i = 0; i < obj.transform.childCount; i++)
                CollectMatching(obj.transform.GetChild(i).gameObject, search, includeInactive, results);
        }

        private void BuildPreview()
        {
            _previewEntries.Clear();

            int suffixIndex = _startIndex;

            for (int i = 0; i < _matchedObjects.Count; i++)
            {
                var go = _matchedObjects[i];
                var id = go.GetEntityId();

                bool excluded = _excludeMap.TryGetValue(id, out bool ex) && ex;

                string baseName = ComputeNewBaseName(go.name);

                if (_addNumericSuffix && !excluded)
                {
                    baseName += "_" + suffixIndex.ToString($"D{_digitCount}");
                    suffixIndex++;
                }

                _previewEntries.Add(new PreviewEntry
                {
                    Path = GetFullPath(go.transform),
                    OldName = go.name,
                    NewName = baseName,
                    Excluded = excluded,
                });
            }
        }

        private string ComputeNewBaseName(string currentName)
        {
            if (_renameMode == RenameMode.FullReplace)
                return _newName;

            if (string.IsNullOrEmpty(_findString))
                return currentName;

            return currentName.Replace(_findString, _replaceString, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyRename()
        {
            Undo.SetCurrentGroupName("Batch Rename");
            int undoGroup = Undo.GetCurrentGroup();

            for (int i = 0; i < _matchedObjects.Count; i++)
            {
                var entry = _previewEntries[i];
                if (entry.Excluded)
                    continue;

                var go = _matchedObjects[i];
                if (go == null) continue;

                Undo.RecordObject(go, "Batch Rename");
                go.name = entry.NewName;
            }

            Undo.CollapseUndoOperations(undoGroup);

            _showPreview = false;
            _previewEntries.Clear();
            _excludeMap.Clear();

            Debug.Log("[Batch Rename] Rename applied. Ctrl+Z to undo.");
        }

        // ───────────────────────── Helpers ─────────────────────────

        private static string GetFullPath(Transform t)
        {
            var parts = new List<string>();
            var current = t;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Returns a sortable key representing the object's position in the hierarchy
        /// (depth-first, ordered by sibling index at each level).
        /// </summary>
        private static string GetHierarchyOrder(GameObject go)
        {
            var indices = new List<int>();
            var current = go.transform;
            while (current != null)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }
            indices.Reverse();
            return string.Join(".", indices.Select(i => i.ToString("D6")));
        }
    }
}
