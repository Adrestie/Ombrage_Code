using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.InspectorSearch
{
public class InspectorSearchWindow : EditorWindow
{
    private string _searchQuery = "";
    private Vector2 _scrollPos;
    private Object[] _cachedSelection;

    // All properties scanned once per selection change
    private List<PropertyEntry> _allProperties = new List<PropertyEntry>();
    // Filtered view, rebuilt on query change
    private List<PropertyEntry> _filtered = new List<PropertyEntry>();

    private struct PropertyEntry
    {
        public Object Target;
        public SerializedObject SerializedObject;
        public SerializedProperty Property;
        public string PathLabel;
        public string HeaderLabel;
        public string NormalizedName; // pre-computed for fast filtering
    }

    [MenuItem("Window/Ombrage Tools/Scene/Inspector Search %&f")]
    public static void Open()
    {
        var window = GetWindow<InspectorSearchWindow>("Inspector Search");
        window.minSize = new Vector2(350, 200);
    }

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        RebuildPropertyCache();
        ApplyFilter();
        Repaint();
    }

    private void OnGUI()
    {
        // Detect selection changes not caught by the event (e.g. window just opened)
        if (!SelectionMatches())
        {
            RebuildPropertyCache();
            ApplyFilter();
        }

        // Search bar
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        GUI.SetNextControlName("SearchField");
        _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck())
            ApplyFilter();

        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton, GUILayout.Width(18)))
        {
            _searchQuery = "";
            _filtered.Clear();
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        // Selection info
        int selCount = Selection.objects != null ? Selection.objects.Length : 0;
        if (selCount == 0)
        {
            EditorGUILayout.HelpBox("Sélectionnez un ou plusieurs objets (GameObject, ScriptableObject, etc.).", MessageType.Info);
            return;
        }

        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            EditorGUILayout.HelpBox($"{selCount} objet(s) sélectionné(s) — {_allProperties.Count} propriétés indexées. Tapez pour filtrer.", MessageType.Info);
            return;
        }

        // Results
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_filtered.Count == 0)
        {
            EditorGUILayout.HelpBox("Aucun résultat.", MessageType.Warning);
        }
        else
        {
            string prevHeader = null;

            foreach (var entry in _filtered)
            {
                if (entry.Target == null || entry.SerializedObject == null)
                    continue;

                entry.SerializedObject.Update();

                if (entry.HeaderLabel != prevHeader)
                {
                    prevHeader = entry.HeaderLabel;
                    EditorGUILayout.Space(6);
                    DrawHeader(entry.Target, entry.HeaderLabel);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.PropertyField(entry.Property, new GUIContent(entry.PathLabel), true);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                entry.SerializedObject.ApplyModifiedProperties();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader(Object target, string label)
    {
        var rect = EditorGUILayout.GetControlRect(false, 22);
        EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f, 1f));

        var iconRect = new Rect(rect.x + 4, rect.y + 3, 16, 16);
        var icon = EditorGUIUtility.ObjectContent(target, target.GetType()).image;
        if (icon != null)
            GUI.DrawTexture(iconRect, icon);

        var labelRect = new Rect(rect.x + 24, rect.y + 2, rect.width - 60, rect.height);
        var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
        EditorGUI.LabelField(labelRect, label, style);

        var pingRect = new Rect(rect.xMax - 30, rect.y + 2, 28, 18);
        if (GUI.Button(pingRect, "►", EditorStyles.miniButton))
        {
            EditorGUIUtility.PingObject(target);
            Selection.activeObject = target;
        }
    }

    private bool SelectionMatches()
    {
        var current = Selection.objects;
        if (_cachedSelection == null || current == null)
            return _cachedSelection == current;
        if (_cachedSelection.Length != current.Length)
            return false;
        for (int i = 0; i < current.Length; i++)
        {
            if (_cachedSelection[i] != current[i])
                return false;
        }
        return true;
    }

    // Heavy work: scan all properties once, cache them
    private void RebuildPropertyCache()
    {
        _allProperties.Clear();
        _cachedSelection = Selection.objects;

        if (_cachedSelection == null)
            return;

        foreach (var obj in _cachedSelection)
        {
            if (obj == null)
                continue;

            if (obj is GameObject go)
            {
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null)
                        continue;
                    var so = new SerializedObject(component);
                    string header = $"{component.GetType().Name}  ({go.name})";
                    ScanAllProperties(so, component, header);
                }
            }
            else
            {
                var so = new SerializedObject(obj);
                string header = $"{obj.GetType().Name}  ({obj.name})";
                ScanAllProperties(so, obj, header);
            }
        }

        _allProperties.Sort((a, b) =>
        {
            int cmp = string.Compare(a.HeaderLabel, b.HeaderLabel);
            if (cmp != 0) return cmp;
            return string.Compare(a.Property.propertyPath, b.Property.propertyPath);
        });
    }

    private void ScanAllProperties(SerializedObject so, Object target, string header)
    {
        var iterator = so.GetIterator();

        while (iterator.NextVisible(true))
        {
            if (iterator.name == "m_Script")
                continue;
            if (iterator.propertyType == SerializedPropertyType.ArraySize)
                continue;

            string pathLabel = BuildPathLabel(iterator);

            _allProperties.Add(new PropertyEntry
            {
                Target = target,
                SerializedObject = so,
                Property = iterator.Copy(),
                PathLabel = $"{pathLabel}  [{iterator.type}]",
                HeaderLabel = header,
                NormalizedName = NormalizeForSearch(iterator.displayName) + "|" + NormalizeForSearch(iterator.name)
            });
        }
    }

    // Lightweight: just filter the cached list
    private void ApplyFilter()
    {
        _filtered.Clear();

        if (string.IsNullOrWhiteSpace(_searchQuery))
            return;

        var normalizedQuery = NormalizeForSearch(_searchQuery);

        foreach (var entry in _allProperties)
        {
            if (entry.NormalizedName.Contains(normalizedQuery))
                _filtered.Add(entry);
        }
    }

    private static string BuildPathLabel(SerializedProperty property)
    {
        var path = property.propertyPath;
        var parts = path.Split('.');
        var sb = new StringBuilder();

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];

            if (part == "Array" && i + 1 < parts.Length && parts[i + 1].StartsWith("data["))
            {
                var index = parts[i + 1].Replace("data[", "[");
                if (sb.Length > 0 && sb[sb.Length - 1] == '/')
                    sb.Remove(sb.Length - 1, 1);
                sb.Append(index);
                sb.Append(" / ");
                i++;
                continue;
            }

            sb.Append(ObjectNames.NicifyVariableName(part));
            if (i < parts.Length - 1)
                sb.Append(" / ");
        }

        return sb.ToString();
    }

    private static string NormalizeForSearch(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c != ' ' && c != '_' && c != '-')
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
}
