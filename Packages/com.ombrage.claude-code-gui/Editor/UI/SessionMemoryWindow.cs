using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Éditeur générique d'un fichier MEMORY.md (session ou global). Le même fichier peut être
    /// écrit par Claude (via Write/Edit) ; on le relit donc à l'ouverture et via « Recharger ».
    /// </summary>
    public class SessionMemoryWindow : EditorWindow
    {
        private string  _path;
        private string  _title;
        private string  _content;
        private Vector2 _scroll;

        public static void Show(string title, string absolutePath)
        {
            var w = GetWindow<SessionMemoryWindow>(true, "Mémoire", true);
            w._title = title;
            w._path = absolutePath;
            w._content = File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : "";
            w.minSize = new Vector2(480, 380);
        }

        public static void Show(Session session)
        {
            if (session == null) return;
            Show($"Session — {session.GetDisplayTitle()}", SessionStore.MemoryPath(session.id));
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"MEMORY.md — {_title}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_path, EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _content = EditorGUILayout.TextArea(_content, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Recharger", GUILayout.Width(100)))
                _content = File.Exists(_path) ? File.ReadAllText(_path) : "";
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Enregistrer", GUILayout.Width(120)))
            {
                File.WriteAllText(_path, _content ?? "");
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
