using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>Fenêtre de gestion des snippets (renommer, éditer, supprimer).</summary>
    public class SnippetManagerWindow : EditorWindow
    {
        private List<Snippet> _items;
        private Vector2       _scroll;
        private Action        _onChanged;

        public static void Show(Action onChanged)
        {
            var w = GetWindow<SnippetManagerWindow>(true, "Snippets", true);
            w._items = SnippetStore.LoadAll();
            w._onChanged = onChanged;
            w.minSize = new Vector2(440, 320);
        }

        private void OnGUI()
        {
            _items ??= SnippetStore.LoadAll();

            EditorGUILayout.HelpBox("Modèles de prompts réutilisables.", MessageType.None);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int remove = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                _items[i].title = EditorGUILayout.TextField(_items[i].title);
                if (GUILayout.Button("✕", GUILayout.Width(26))) remove = i;
                EditorGUILayout.EndHorizontal();
                _items[i].text = EditorGUILayout.TextArea(_items[i].text, GUILayout.Height(48));
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
            if (remove >= 0) _items.RemoveAt(remove);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Enregistrer", GUILayout.Width(120), GUILayout.Height(24)))
            {
                SnippetStore.SaveAll(_items);
                _onChanged?.Invoke();
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
