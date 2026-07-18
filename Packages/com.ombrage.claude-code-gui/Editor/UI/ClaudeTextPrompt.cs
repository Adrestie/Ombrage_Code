using System;
using UnityEditor;
using UnityEngine;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>Petite boîte de dialogue pour saisir une chaîne (ex : renommer une session).</summary>
    public class ClaudeTextPrompt : EditorWindow
    {
        private string         _value;
        private Action<string> _onConfirm;
        private bool           _focused;

        public static void Show(string title, string initial, Action<string> onConfirm)
        {
            var w = CreateInstance<ClaudeTextPrompt>();
            w.titleContent = new GUIContent(title);
            w._value = initial ?? "";
            w._onConfirm = onConfirm;
            w.minSize = w.maxSize = new Vector2(360, 96);
            w.ShowUtility();
            w.Focus();
        }

        private void OnGUI()
        {
            GUILayout.Space(8);
            GUI.SetNextControlName("field");
            _value = EditorGUILayout.TextField(_value);

            if (!_focused)
            {
                EditorGUI.FocusTextInControl("field");
                _focused = true;
            }

            var e = Event.current;
            bool enter = e.type == EventType.KeyDown && e.keyCode is KeyCode.Return or KeyCode.KeypadEnter;

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Annuler", GUILayout.Width(90))) Close();
            if (GUILayout.Button("OK", GUILayout.Width(90)) || enter)
            {
                _onConfirm?.Invoke((_value ?? "").Trim());
                _onConfirm = null;
                Close();
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
        }
    }
}
