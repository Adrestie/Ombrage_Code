#if UNITY_EDITOR_WIN
using Microsoft.Win32;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.EditorPrefsEditor
{
public class EditorPrefsEditorWindow : EditorWindow
{
#if UNITY_EDITOR_WIN
    private const string RegistryPath = @"Software\Unity Technologies\Unity Editor 5.x";
#endif

    private string _searchQuery = "";
    private Vector2 _scrollPos;
    private List<PrefEntry> _allEntries = new();
    private List<PrefEntry> _filtered = new();

    private bool _showAddPanel;
    private string _newKey = "";
    private int _newTypeIndex;
    private string _newStringVal = "";
    private int _newIntVal;
    private float _newFloatVal;
    private bool _newBoolVal;

    private string _confirmDeleteKey;
    private readonly HashSet<string> _boolOverrides = new();

    private static readonly string[] TypeLabels = { "String", "Int", "Float", "Bool" };

    private class PrefEntry
    {
        public string Key;
        public object RawValue;
        public string ValueType;
        public string NormalizedKey;
        public bool IsBool;
    }

    [MenuItem("Window/Ombrage Tools/Management/EditorPrefs Editor %&e")]
    public static void Open()
    {
        var window = GetWindow<EditorPrefsEditorWindow>("EditorPrefs");
        window.minSize = new Vector2(500, 300);
    }

    private void OnEnable()
    {
        RefreshEntries();
        ApplyFilter();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (_showAddPanel)
            DrawAddPanel();

        DrawEntries();
        DrawFooter();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        GUI.SetNextControlName("PrefSearch");
        _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck())
            ApplyFilter();

        if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton, GUILayout.Width(18)))
        {
            _searchQuery = "";
            ApplyFilter();
            GUI.FocusControl(null);
        }

        if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24)))
            _showAddPanel = !_showAddPanel;

        if (GUILayout.Button("↻", EditorStyles.toolbarButton, GUILayout.Width(24)))
        {
            RefreshEntries();
            ApplyFilter();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawAddPanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Ajouter une clé", EditorStyles.boldLabel);

        _newKey = EditorGUILayout.TextField("Clé", _newKey);
        _newTypeIndex = EditorGUILayout.Popup("Type", _newTypeIndex, TypeLabels);

        switch (_newTypeIndex)
        {
            case 0: _newStringVal = EditorGUILayout.TextField("Valeur", _newStringVal); break;
            case 1: _newIntVal = EditorGUILayout.IntField("Valeur", _newIntVal); break;
            case 2: _newFloatVal = EditorGUILayout.FloatField("Valeur", _newFloatVal); break;
            case 3: _newBoolVal = EditorGUILayout.Toggle("Valeur", _newBoolVal); break;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_newKey));
        if (GUILayout.Button("Ajouter", GUILayout.Width(80)))
        {
            AddNewKey();
            _showAddPanel = false;
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Annuler", GUILayout.Width(80)))
            _showAddPanel = false;

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }

    private void DrawEntries()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_filtered.Count == 0)
        {
            EditorGUILayout.HelpBox(
                _allEntries.Count == 0
                    ? "Aucune entrée EditorPrefs trouvée."
                    : "Aucun résultat pour ce filtre.",
                MessageType.Info);
        }
        else
        {
            foreach (var entry in _filtered)
                DrawEntry(entry);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawEntry(PrefEntry entry)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        float keyWidth = Mathf.Max(position.width * 0.35f, 120f);
        EditorGUILayout.LabelField(new GUIContent(entry.Key, entry.Key), GUILayout.Width(keyWidth));

        if (entry.IsBool)
        {
            bool val = entry.RawValue is int i ? i != 0 : false;
            EditorGUI.BeginChangeCheck();
            bool newVal = EditorGUILayout.Toggle(val);
            if (EditorGUI.EndChangeCheck())
            {
                int raw = newVal ? 1 : 0;
                SetRegistryDWord(entry.Key, raw);
                entry.RawValue = raw;
            }
        }
        else if (entry.ValueType == "INT")
        {
            int val = entry.RawValue is int i ? i : 0;
            EditorGUI.BeginChangeCheck();
            int newVal = EditorGUILayout.IntField(val);
            if (EditorGUI.EndChangeCheck())
            {
                SetRegistryDWord(entry.Key, newVal);
                entry.RawValue = newVal;
            }
        }
        else
        {
            string val = entry.RawValue?.ToString() ?? "";
            EditorGUI.BeginChangeCheck();
            string newVal = EditorGUILayout.TextField(val);
            if (EditorGUI.EndChangeCheck())
            {
                SetRegistryString(entry.Key, newVal);
                entry.RawValue = newVal;
            }
        }

        string badgeLabel = entry.IsBool ? "BOOL" : entry.ValueType;
        bool canToggleType = entry.ValueType == "INT";
        if (canToggleType)
        {
            if (GUILayout.Button(badgeLabel, EditorStyles.miniButton, GUILayout.Width(40)))
            {
                entry.IsBool = !entry.IsBool;
                if (entry.IsBool)
                    _boolOverrides.Add(entry.Key);
                else
                    _boolOverrides.Remove(entry.Key);
            }
        }
        else
        {
            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(badgeLabel, badgeStyle, GUILayout.Width(40));
        }

        if (_confirmDeleteKey == entry.Key)
        {
            if (GUILayout.Button("✓", EditorStyles.miniButtonLeft, GUILayout.Width(22)))
            {
                DeleteRegistryValue(entry.Key);
                _confirmDeleteKey = null;
                RefreshEntries();
                ApplyFilter();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("✗", EditorStyles.miniButtonRight, GUILayout.Width(22)))
            {
                _confirmDeleteKey = null;
            }
        }
        else
        {
            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                _confirmDeleteKey = entry.Key;
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawFooter()
    {
        EditorGUILayout.Space(2);
        var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        string label = string.IsNullOrWhiteSpace(_searchQuery)
            ? $"{_allEntries.Count} entrée(s)"
            : $"{_filtered.Count} / {_allEntries.Count} entrée(s)";
        EditorGUILayout.LabelField(label, style);
    }

    private void RefreshEntries()
    {
        _allEntries.Clear();

#if UNITY_EDITOR_WIN
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key == null)
            {
                Debug.LogWarning("[EditorPrefs Editor] Clé de registre introuvable: " + RegistryPath);
                return;
            }

            foreach (string name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;

                var kind = key.GetValueKind(name);
                var value = key.GetValue(name);

                string typeLabel = kind switch
                {
                    RegistryValueKind.DWord => "INT",
                    RegistryValueKind.String => "STR",
                    _ => kind.ToString()[..3].ToUpper()
                };

                bool isBool = kind == RegistryValueKind.DWord
                    && value is int intVal && (intVal == 0 || intVal == 1);

                if (_boolOverrides.Contains(name))
                    isBool = true;

                _allEntries.Add(new PrefEntry
                {
                    Key = name,
                    RawValue = value,
                    ValueType = typeLabel,
                    NormalizedKey = name.ToLowerInvariant(),
                    IsBool = isBool
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[EditorPrefs Editor] Erreur de lecture du registre: " + e.Message);
        }
#else
        Debug.LogWarning("[EditorPrefs Editor] L'énumération des clés n'est disponible que sur Windows.");
#endif

        _allEntries.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
    }

#if UNITY_EDITOR_WIN
    private void SetRegistryDWord(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch (Exception e)
        {
            Debug.LogError($"[EditorPrefs Editor] Erreur d'écriture: {e.Message}");
        }
    }

    private void SetRegistryString(string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.SetValue(name, value, RegistryValueKind.String);
        }
        catch (Exception e)
        {
            Debug.LogError($"[EditorPrefs Editor] Erreur d'écriture: {e.Message}");
        }
    }

    private void DeleteRegistryValue(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch (Exception e)
        {
            Debug.LogError($"[EditorPrefs Editor] Erreur de suppression: {e.Message}");
        }
    }
#endif

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _filtered = new List<PrefEntry>(_allEntries);
            return;
        }

        string query = _searchQuery.ToLowerInvariant();
        _filtered = _allEntries.Where(e => e.NormalizedKey.Contains(query)).ToList();
    }

    private void AddNewKey()
    {
        switch (_newTypeIndex)
        {
            case 0: EditorPrefs.SetString(_newKey, _newStringVal); break;
            case 1: EditorPrefs.SetInt(_newKey, _newIntVal); break;
            case 2: EditorPrefs.SetFloat(_newKey, _newFloatVal); break;
            case 3:
                EditorPrefs.SetBool(_newKey, _newBoolVal);
                _boolOverrides.Add(_newKey);
                break;
        }

        _newKey = "";
        _newStringVal = "";
        _newIntVal = 0;
        _newFloatVal = 0;
        _newBoolVal = false;

        RefreshEntries();
        ApplyFilter();
    }
}
}
